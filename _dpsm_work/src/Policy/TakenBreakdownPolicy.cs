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
	// R80: there is NO bucket cap any more. R79 kept 64 rows per dimension per victim and folded the tail
	// into one row ("其他来源(N 项/M 击)"), which made the page unreadable for a battle with a few hundred
	// sources -- and the export lost those rows too, because the page and the exported section are built by
	// this same method. The dimension lists are now complete; Output/TakenSession writes schemaVersion 1.1
	// for that shape, and the only remaining limit is that a victim's dimension lists can never be longer
	// than the number of its own hits.

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
		// R80: one bucket index per victim, shared by the four dimensions and the status list. Bump used to
		// scan its list linearly, which was affordable while every dimension stopped at 64 rows; with the cap
		// gone that scan would be O(hits x buckets) and a long battle with a few hundred sources would pay
		// for it on every recompute (the page recomputes at most once a second, over ALL events).
		var index = new Dictionary<int, BucketIndex>();
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
				BucketIndex bi;
				if (!index.TryGetValue(h.VictimKey, out bi))
				{
					bi = new BucketIndex();
					index[h.VictimKey] = bi;
				}
				Add(a, h, bi);
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

	private static void Add(TakenActor a, TakenHit h, BucketIndex index)
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
			Bump(a.ByAttacker, index.Attacker, h.AttackerKey, h.Attacker, h.Nominal, authoritative);
		}

		bool record = h.HitMatch == 1;
		Bump(a.BySource, index.Source, h.Source, DamageSourceLabelPolicy.Source(h.Source), h.Nominal, record);
		Bump(a.ByHitType, index.HitType, h.HitType, HitTypeLabel(h.HitType), h.Nominal, record);
		Bump(a.ByEffect, index.Effect, h.EffectId, EffectLabel(h.EffectId), h.Nominal, record);

		if (!string.IsNullOrEmpty(h.Status))
		{
			string sk = StatusKey(h.Status, h.StatusApplier);
			int at;
			TakenStatus s;
			if (!index.Status.TryGetValue(sk, out at))
			{
				s = new TakenStatus { Status = h.Status, Applier = h.StatusApplier ?? "" };
				at = a.ByStatus.Count;
				a.ByStatus.Add(s);
				index.Status[sk] = at;
			}
			else
			{
				s = a.ByStatus[at];
			}
			s.Amount += h.Nominal;
			s.Hits++;
		}
	}

	/// <summary>The status list's bucket identity: the status NAME plus the applier the GAME recorded (a DoT's
	/// applier is not necessarily the attacker of the hit that ticked it). NUL joins them, so a status whose
	/// name ends in a digit cannot collide with a different applier.</summary>
	private static string StatusKey(string status, string applier)
	{
		return status + "\0" + (applier ?? "");
	}

	/// <summary>Add one hit to a dimension bucket, finding the bucket through the index instead of scanning
	/// the list (see the note in Build).</summary>
	private static void Bump(List<TakenBucket> list, Dictionary<int, int> index, int key, string name, long amount,
	                         bool authoritative)
	{
		int at;
		TakenBucket b;
		if (!index.TryGetValue(key, out at))
		{
			b = new TakenBucket { Key = key, Name = name ?? "" };
			at = list.Count;
			list.Add(b);
			index[key] = at;
		}
		else
		{
			b = list[at];
		}
		b.Amount += amount;
		b.Hits++;
		if (authoritative) b.AuthoritativeHits++;
	}

	private static void Finish(TakenActor a)
	{
		a.Residual = a.Nominal - a.Taken;
		Sort(a.BySource);
		Sort(a.ByHitType);
		Sort(a.ByAttacker);
		Sort(a.ByEffect);
		Quality(a.BySource);
		Quality(a.ByHitType);
		Quality(a.ByAttacker);
		Quality(a.ByEffect);
		SortStatus(a);
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

	/// <summary>R80: the status list is sorted, never folded -- every status/applier pair the battle produced
	/// gets its own row on the page and its own object in the exported section.</summary>
	private static void SortStatus(TakenActor a)
	{
		a.ByStatus.Sort(delegate (TakenStatus x, TakenStatus y)
		{
			int c = y.Amount.CompareTo(x.Amount);
			if (c != 0) return c;
			c = y.Hits.CompareTo(x.Hits);
			if (c != 0) return c;
			return string.CompareOrdinal(x.Status, y.Status);
		});
	}

	/// <summary>R80: the per-victim bucket lookup, one dictionary per dimension plus the status list. The
	/// lists themselves stay the ordered output (Build sorts them once at the end); nothing is ever removed,
	/// so a recorded position stays valid until the sort, after which the index is dropped with the victim.
	/// Private and per-Build on purpose: it is bookkeeping, never something the model or the export sees.</summary>
	private sealed class BucketIndex
	{
		public readonly Dictionary<int, int> Source = new Dictionary<int, int>();
		public readonly Dictionary<int, int> HitType = new Dictionary<int, int>();
		public readonly Dictionary<int, int> Attacker = new Dictionary<int, int>();
		public readonly Dictionary<int, int> Effect = new Dictionary<int, int>();
		public readonly Dictionary<string, int> Status = new Dictionary<string, int>();
	}
}
