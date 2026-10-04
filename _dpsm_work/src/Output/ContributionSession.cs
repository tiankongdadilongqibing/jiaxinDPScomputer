using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DpsMeter;

/// <summary>
/// The contribution view the overlay shows: the result plus why it may not be available.
/// A missing result must be shown as "不可用" and never as zeros -- a measured 0 and "not computed"
/// are different statements.
/// </summary>
public sealed class ContributionView
{
	public ContributionResult Result;
	/// <summary>True when this is the CURRENT battle (false = the last finished one).</summary>
	public bool Live;
	public bool Usable = true;
	public string Unavailable;
	/// <summary>1.7.5: WHICH battle this view describes. The overlay header used the live session, so
	/// while showing the previous battle it printed "任务 0  0秒" next to a real table.</summary>
	public int QuestId;
	public double Seconds;
}

/// <summary>
/// 1.6.0 (阶段 E): project a battle into the contribution core's inputs. 1.7.0 adds the overlay view
/// (one compute path shared by the export and the dashboard, cached and throttled).
///
/// This is the ONLY place that knows both the battle model and the contribution core, which keeps
/// <see cref="Contribution"/> free of model/Plugin/IL2CPP dependencies so recon_probe can assert it.
///
/// The predicates here MUST mirror what the export itself does, or the dashboard would describe data
/// the file does not contain:
///   * only Type == "dmg"                                     (ExportService emits dmg events the same way)
///   * attacker identity only via AttackerKey                 (a name join would mix mirror units)
///   * folds only when e.Calc.Valid AND the same switch that gated calc.fold
///   * ability holders from the SAME two sources the export writes (AppendRoster + AppendTalentTable)
/// </summary>
public static class ContributionSession
{
	public static void BuildInputs(BattleSession s, bool useFolds,
		out List<ContributionHit> hits, out List<ContributionActor> roster)
	{
		hits = new List<ContributionHit>(s.Events == null ? 0 : s.Events.Count);
		if (s.Events != null)
		{
			for (int i = 0; i < s.Events.Count; i++)
			{
				BattleEvent e = s.Events[i];
				if (e == null || e.Type != "dmg") continue;
				var h = new ContributionHit
				{
					Damage = e.Amount,
					AttackerKey = e.AttackerKey,
					// CalcBreakdown is a struct: a hit that never got a chain has Valid == false.
					HasCalc = e.Calc.Valid,
				};
				if (h.HasCalc && useFolds && e.Calc.Fold != null)
				{
					if (e.Calc.FoldDropped > 0) h.FoldDropped = e.Calc.FoldDropped;
					for (int j = 0; j < e.Calc.Fold.Count; j++)
					{
						FoldStep f = e.Calc.Fold[j];
						if (f == null) continue;
						h.Folds.Add(new ContributionFold
						{
							Kind = f.Kind ?? "",
							Side = f.Side ?? "",
							Origin = f.Origin ?? "",
							Label = f.Label ?? "",
							ByUnit = f.ByUnit,
							Factor = f.Factor,
						});
					}
				}
				// 1.7.4 (阶段 G): the attack-power additions granted by TEAMMATES, already reduced to a fold
				// factor by the composition pass and written into the file as calc.atkAdd. Read from there, not
				// recomputed, so the section and the file cannot disagree about which giver gets what.
				if (h.HasCalc && useFolds && e.Calc.AtkAdd != null)
				{
					for (int j = 0; j < e.Calc.AtkAdd.Count; j++)
					{
						AtkAddRow ar = e.Calc.AtkAdd[j];
						if (ar == null) continue;
						h.Folds.Add(new ContributionFold
						{
							Kind = ar.Kind,
							Side = ar.Side,
							Origin = ar.Origin ?? "",
							Label = ar.Label ?? "",
							ByUnit = ar.ByUnit,
							Factor = ar.Factor,
						});
					}
				}
				hits.Add(h);
			}
		}

		roster = new List<ContributionActor>(s.OrderedActors == null ? 0 : s.OrderedActors.Count);
		if (s.OrderedActors != null)
		{
			for (int i = 0; i < s.OrderedActors.Count; i++)
			{
				ActorStats a = s.OrderedActors[i];
				if (a == null) continue;
				var ca = new ContributionActor
				{
					Key = a.Key,
					Name = a.Name ?? "",
					Team = (int)a.Team,
					Kind = a.Kind ?? "",
					Summon = a.IsSummonMerge,
				};
				if (a.Roster != null)
					for (int j = 0; j < a.Roster.Count; j++)
					{
						RosterAbility r = a.Roster[j];
						if (r == null) continue;
						if (r.Id != 0) ca.AbilityIds.Add(r.Id);
						if (!string.IsNullOrEmpty(r.Name)) ca.AbilityNames.Add(r.Name);
					}
				if (a.TalentTable != null)
					for (int j = 0; j < a.TalentTable.Count; j++)
					{
						TalentUsage t = a.TalentTable[j];
						if (t == null) continue;
						if (t.AbilityId != 0) ca.AbilityIds.Add(t.AbilityId);
						if (!string.IsNullOrEmpty(t.Ability)) ca.AbilityNames.Add(t.Ability);
					}
				roster.Add(ca);
			}
		}
	}

	/// <summary>One-shot compute (no cache). The export path uses this.</summary>
	public static ContributionResult Compute(BattleSession s, bool useFolds)
	{
		List<ContributionHit> hits;
		List<ContributionActor> roster;
		BuildInputs(s, useFolds, out hits, out roster);
		// P0-B (1.7.8): the ledger's reconciliationGap is measured against the SAME number
		// ExportService writes into totals.dealt (the sum over OrderedActors), so a reader can
		// subtract the two without wondering whether they are different definitions.
		double totalsDealt = 0.0;
		if (s.OrderedActors != null)
			for (int i = 0; i < s.OrderedActors.Count; i++)
				if (s.OrderedActors[i] != null) totalsDealt += s.OrderedActors[i].DamageDealt;
		return Contribution.Compute(hits, roster, 1, totalsDealt);
	}

	public static ContributionStats AppendJson(StringBuilder sb, BattleSession s, bool useFolds, bool training)
	{
		ContributionResult res = Compute(s, useFolds);
		Contribution.AppendJson(sb, res, BuildInfo.Version, s.QuestId);
		if (training) sb.Append("");   // shape kept identical; the training flag is reported by the caller
		return res.Stats;
	}

	// ---------------------------------------------------------------------------------------------
	// Overlay view. The dashboard must NOT recompute the whole battle every frame (the plan's F
	// requirement): the result is cached and refreshed at most once a second, and only when the event
	// count actually moved. The whole computation is O(hits x folds) (~30k operations here), so this
	// is about keeping the frame budget honest rather than about the cost being large today.
	// ---------------------------------------------------------------------------------------------

	private const double RefreshSeconds = 1.0;

	private static ContributionResult _cache;
	private static object _cacheSession;
	private static int _cacheEvents = -1;
	private static double _cacheAt = -1e9;
	private static bool _cacheUsedFolds;
	private static int _cacheQuest;
	private static double _cacheSeconds;

	public static void Invalidate()
	{
		_cache = null;
		_cacheSession = null;
		_cacheEvents = -1;
		_cacheAt = -1e9;
	}

	public static ContributionView Get(bool useFolds)
	{
		BattleSession s = Aggregator.Session;
		bool live = s != null && s.InBattle;
		if (live)
		{
			if (!useFolds)
			{
				return new ContributionView
				{
					Live = true, Usable = false,
					Unavailable = "未识别倍率:ReconcileCalc 已关闭(没有折叠就没有归属)",
				};
			}
			if (s.Events == null || s.Events.Count == 0)
				return new ContributionView { Live = true, Usable = false, Unavailable = "尚无伤害事件" };

			double now = Time.unscaledTime;
			bool stale = !ReferenceEquals(_cacheSession, s)
				|| _cacheEvents != s.Events.Count
				|| _cacheUsedFolds != useFolds
				|| now - _cacheAt > RefreshSeconds;
			if (stale)
			{
				_cache = Compute(s, useFolds);
				_cacheSession = s;
				_cacheEvents = s.Events.Count;
				_cacheAt = now;
				_cacheUsedFolds = useFolds;
				_cacheQuest = s.QuestId;
				_cacheSeconds = s.ActiveSeconds;
			}
			return new ContributionView { Result = _cache, Live = true, Usable = _cache != null, QuestId = s.QuestId, Seconds = s.ActiveSeconds };
		}

		// Not in a battle: show the last computed result, clearly labelled as the previous battle.
		if (_cache != null)
			return new ContributionView { Result = _cache, Live = false, Usable = true, QuestId = _cacheQuest, Seconds = _cacheSeconds };
		return new ContributionView
		{
			Live = false, Usable = false,
			Unavailable = useFolds ? "暂无战斗数据" : "未识别倍率:ReconcileCalc 已关闭",
		};
	}
}
