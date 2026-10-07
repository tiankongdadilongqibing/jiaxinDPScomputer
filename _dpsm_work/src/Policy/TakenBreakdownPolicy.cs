using System;
using System.Collections.Generic;

namespace DpsMeter;

/// <summary>
/// R79 受击来源拆分: folds the battle's damage events into per-victim source buckets.
///
/// Pure: no Unity, no IL2CPP, no Plugin, no Aggregator -- being compiled into tests/BehaviorTests is what
/// keeps it that way. The callers (<see cref="TakenSession"/>) do the game-side reads: the event walk and
/// the victim's position snapshot.
///
/// Discipline (from the approved plan, 受击来源拆分-可行性评估与实现方案.md §3):
///  * amounts are NOMINAL (the game's own figure), and the published amount rides along as Taken, so
///    `Nominal = Taken + Residual` holds and the residual is never called 被吸收;
///  * units are aggregated by actor KEY, never by display name (the same name can stand on both sides);
///  * friendly fire is its own bucket and never an enemy source;
///  * hits with no resolvable attacker are counted as Unknown and are not guessed at;
///  * every bucket carries how much of it was labelled from an authoritative record (Quality), because the
///    damage-detail match is value-exact for only a small share of hits.
/// </summary>
internal static class TakenBreakdownPolicy
{
	/// <summary>Most buckets kept per dimension per victim; the tail is folded into one row so one strange
	/// battle cannot blow up the export or the page.</summary>
	public const int MaxBuckets = 64;

	/// <summary>Folded-tail bucket key. int.MinValue cannot collide with a real dimension value.</summary>
	public const int FoldedKey = int.MinValue;

	/// <summary>Was this hit's attacker unresolvable? The aggregator's unattributed branch is exactly the
	/// case where it published no actor at all (Attr "?" and no actor row), so this is what it counted into
	/// Session.UnattributedDamage too.</summary>
	public static bool AttackerUnknown(TakenHit h)
	{
		if (h.AttackerKey == 0) return true;
		if (string.IsNullOrEmpty(h.Attacker)) return true;
		if (h.Attacker == "?") return true;
		return h.Attr == "?";
	}

	/// <summary>Label of the hit-type dimension. -1 is the damage path's "no readable record" sentinel and
	/// gets its own word here rather than being pushed through the shared enum map.</summary>
	public static string HitTypeLabel(int v)
	{
		return v < 0 ? "未识别" : DamageSourceLabelPolicy.HitType(v);
	}

	/// <summary>Effect label. There is no skill-name map in the model (ActorStats.SkillNames has no writer),
	/// so the effect id IS the name; 0 means the calc carried none.</summary>
	public static string EffectLabel(int effectId)
	{
		return effectId == 0 ? "未识别" : "效果" + effectId;
	}

	public static string PositionLabel(int position)
	{
		if (position == 1) return "前衛";
		if (position == 2) return "後衛";
		return "站位未知";
	}

	/// <summary>Build the whole section. `allyTeam` is the plugin's own side (Policy/SkillSidePolicy).</summary>
	public static TakenBreakdown Build(List<TakenHit> hits, int allyTeam)
	{
		var byKey = new Dictionary<int, TakenActor>();
		var order = new List<TakenActor>();
		if (hits != null)
		{
			for (int i = 0; i < hits.Count; i++)
			{
				TakenHit h = hits[i];
				TakenActor a;
				if (!byKey.TryGetValue(h.VictimKey, out a))
				{
					a = new TakenActor
					{
						Key = h.VictimKey,
						Name = h.Victim ?? "",
						Team = h.VictimTeam,
						Ally = h.VictimTeam == allyTeam
					};
					byKey[h.VictimKey] = a;
					order.Add(a);
				}
				Add(a, h);
			}
		}

		var res = new TakenBreakdown();
		for (int i = 0; i < order.Count; i++)
		{
			TakenActor a = order[i];
			Finish(a);
			res.Actors.Add(a);
			res.Hits += a.Hits;
			res.Nominal += a.Nominal;
			res.Taken += a.Taken;
			res.Residual += a.Residual;
			res.Unknown += a.Unknown;
			res.UnknownHits += a.UnknownHits;
			res.Friendly += a.Friendly;
			res.FriendlyHits += a.FriendlyHits;
		}
		// Biggest first, key asc as the tie-break: the page and the export must not depend on event order.
		res.Actors.Sort(delegate (TakenActor x, TakenActor y)
		{
			int c = y.Nominal.CompareTo(x.Nominal);
			if (c != 0) return c;
			return x.Key.CompareTo(y.Key);
		});
		return res;
	}

	private static void Add(TakenActor a, TakenHit h)
	{
		a.Hits++;
		a.Nominal += h.Nominal;
		a.Taken += h.Amount;
		if (h.Position != 0) a.Position = h.Position;

		if (h.Friendly)
		{
			// Own-team damage (回復反転 / self-damage skills). Still part of Nominal and of the
			// source/hit-type dimensions, but never attributed as an enemy source.
			a.Friendly += h.Nominal;
			a.FriendlyHits++;
		}
		else if (AttackerUnknown(h))
		{
			a.Unknown += h.Nominal;
			a.UnknownHits++;
		}
		else
		{
			// Authoritative when the attacker came from the objects themselves (Attr A / A+O / O); the
			// "C:calc*" fallback resolved it from recent calc activity and is best effort.
			bool authoritative = !(h.Attr != null && h.Attr.StartsWith("C:", StringComparison.Ordinal));
			Bump(a.ByAttacker, h.AttackerKey, h.Attacker, h.Nominal, authoritative);
		}

		bool record = h.HitMatch == 1;
		Bump(a.BySource, h.Source, DamageSourceLabelPolicy.Source(h.Source), h.Nominal, record);
		Bump(a.ByHitType, h.HitType, HitTypeLabel(h.HitType), h.Nominal, record);
		Bump(a.ByEffect, h.EffectId, EffectLabel(h.EffectId), h.Nominal, record);

		if (!string.IsNullOrEmpty(h.Status))
		{
			TakenStatus s = null;
			for (int i = 0; i < a.ByStatus.Count; i++)
			{
				TakenStatus c = a.ByStatus[i];
				if (c.Status == h.Status && c.Applier == (h.StatusApplier ?? "")) { s = c; break; }
			}
			if (s == null)
			{
				s = new TakenStatus { Status = h.Status, Applier = h.StatusApplier ?? "" };
				a.ByStatus.Add(s);
			}
			s.Amount += h.Nominal;
			s.Hits++;
		}
	}

	private static void Bump(List<TakenBucket> list, int key, string name, long amount, bool authoritative)
	{
		TakenBucket b = null;
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i].Key == key) { b = list[i]; break; }
		}
		if (b == null)
		{
			b = new TakenBucket { Key = key, Name = name ?? "" };
			list.Add(b);
		}
		b.Amount += amount;
		b.Hits++;
		if (authoritative) b.AuthoritativeHits++;
	}

	private static void Finish(TakenActor a)
	{
		a.Residual = a.Nominal - a.Taken;
		Fold(a.BySource, "其他来源");
		Fold(a.ByHitType, "其他属性");
		Fold(a.ByAttacker, "其他来源单位");
		Fold(a.ByEffect, "其他效果");
		Sort(a.BySource);
		Sort(a.ByHitType);
		Sort(a.ByAttacker);
		Sort(a.ByEffect);
		Quality(a.BySource);
		Quality(a.ByHitType);
		Quality(a.ByAttacker);
		Quality(a.ByEffect);
		FoldStatus(a);
	}

	private static void Sort(List<TakenBucket> list)
	{
		list.Sort(delegate (TakenBucket x, TakenBucket y)
		{
			int c = y.Amount.CompareTo(x.Amount);
			if (c != 0) return c;
			c = y.Hits.CompareTo(x.Hits);
			if (c != 0) return c;
			return x.Key.CompareTo(y.Key);
		});
	}

	private static void Quality(List<TakenBucket> list)
	{
		for (int i = 0; i < list.Count; i++)
		{
			TakenBucket b = list[i];
			b.Quality = b.AuthoritativeHits >= b.Hits ? "" : "近似 " + b.AuthoritativeHits + "/" + b.Hits;
		}
	}

	/// <summary>Keep the heaviest MaxBuckets-1 rows and fold the tail into one row, so the sum over the
	/// dimension is still the victim's whole nominal total.</summary>
	private static void Fold(List<TakenBucket> list, string tailName)
	{
		if (list.Count <= MaxBuckets) return;
		Sort(list);
		int keep = MaxBuckets - 1;
		var tail = new TakenBucket { Key = FoldedKey, Name = tailName };
		for (int i = keep; i < list.Count; i++)
		{
			tail.Amount += list[i].Amount;
			tail.Hits += list[i].Hits;
			tail.AuthoritativeHits += list[i].AuthoritativeHits;
		}
		int folded = list.Count - keep;
		list.RemoveRange(keep, folded);
		tail.Name = tailName + "(" + folded + " 项/" + tail.Hits + " 击)";
		list.Add(tail);
	}

	private static void FoldStatus(TakenActor a)
	{
		a.ByStatus.Sort(delegate (TakenStatus x, TakenStatus y)
		{
			int c = y.Amount.CompareTo(x.Amount);
			if (c != 0) return c;
			c = y.Hits.CompareTo(x.Hits);
			if (c != 0) return c;
			return string.CompareOrdinal(x.Status, y.Status);
		});
		if (a.ByStatus.Count <= MaxBuckets) return;
		var tail = new TakenStatus { Status = "其他状态", Applier = "" };
		for (int i = MaxBuckets - 1; i < a.ByStatus.Count; i++)
		{
			tail.Amount += a.ByStatus[i].Amount;
			tail.Hits += a.ByStatus[i].Hits;
		}
		a.ByStatus.RemoveRange(MaxBuckets - 1, a.ByStatus.Count - (MaxBuckets - 1));
		a.ByStatus.Add(tail);
	}
}
