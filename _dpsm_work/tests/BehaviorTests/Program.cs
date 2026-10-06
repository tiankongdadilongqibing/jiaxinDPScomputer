using System;

namespace BehaviorTests;

internal static class Program
{
	/// <summary>Pinned case total. Deleting a case, or dropping a whole group from Main, fails the run.
	/// R66: 1002 -> 1076 (+74: 47 in policy/skill-timeline, 27 in ui/skill-timeline-text).
	/// R67: 1076 -> 1090 (+14: burst multiplicity/cells, wider stamp cells, the skl channel).
	/// R69: 1090 -> 1140 (+50: +16 policy/skill-timeline for the cooldown fold, +14 policy/skill-activation
	/// for the Using verdict, +18 policy/skill-attempts for the attempt tallies and their rows, +2 for the
	/// whole-page width pin, and the rewritten page cases -- the deleted `rec` channel cases are inside that
	/// delta).
	/// R71: 1171 -> 1205 (+34: +31 policy/battle-clock-calibration for the origin shift -- the lag arithmetic
	/// from the charge counters, its bounds, the median, the one-origin-per-battle rule -- plus 3 for the
	/// page's origin line).
	/// R72: 1205 -> 1254 (+49: +48 for the hold that makes the shift LAND -- policy/clock-origin-hold 33,
	/// runtime/clock-origin 11, runtime/clock-origin-counter 4 -- plus 1 case for the hold-overflow refusal
	/// in policy/battle-clock-calibration, and the counters-shape pin moved from 13 to 16 members, which is
	/// a rename rather than a new case).
	/// </summary>
	public const int ExpectedCases = 1275;

	private static int Main(string[] args)
	{
		bool quiet = false;
		foreach (string a in args) if (a == "--quiet") quiet = true;

		var r = new Runner();
		r.Quiet = quiet;
		Console.WriteLine("BehaviorTests -- game-free execution of the real production sources");

		Cases.Clock(r);
		Cases.HitWindow(r);
		Cases.SessionState(r);
		Cases.Series(r);
		Cases.Cache(r);
		Cases.Tiered(r);
		// RF3: the extracted pure policies, plus grid sweeps against oracles transcribed from the
		// pre-extraction source (git 8f3aafd).
		Cases.Policy(r);
		// RF4: the cross-session state family (Start/End/resume/F9, consecutive battles).
		Cases.Runtime(r);
		// RF4 family 2: the battle-wide rule classification (the decision half of the registration path).
		Cases.GlobalRule(r);
		// RF4c: the battle-wide rule registry container (ownership + the register-before-finalise timing).
		Cases.Registry(r);
		// RF5: the cache decisions (staleness, source selection, reason texts).
		Cases.CachePolicy(r);
		// RF4 apply side: copies and the per-status factor.
		Cases.GlobalRuleApply(r);
		// RF4 third family: the per-battle counters and their reset transitions.
		Cases.Counters(r);
		// RF5b: the display formatter (widths, padding, numbers, fitting), executed as production code.
		Cases.DisplayFormatCases(r);
		// RF5c: the column definition of the three overlay tables.
		Cases.Columns(r);
		// RF5d: the data-row builders, constructed from the column definition.
		Cases.ColumnRows(r);
		// RF5e: the IMGUI fallback's contribution lines (same number formats as the panel).
		Cases.FallbackTextCases(r);
		// RF6a: the master-data official-name rule (the measured 刻印 collision cases).
		Cases.MasterDataLabel(r);
		// RF3c: the composition chain's tolerances and the crit observation window.
		Cases.CompositionTolerance(r);
		// RF4f: the per-battle activity ring (order, cap, clearing).
		Cases.CalcActivityLog(r);
		// RF5f: the contribution table's row values (shown rows vs summed rows).
		Cases.ContributionRowModelCases(r);
		Cases.ContributionRowModelTables(r);
		// RF4g: the attack snapshot (both halves required).
		Cases.AttackSnapshotCases(r);
		// RF4h: the process-level history ring.
		Cases.BattleHistoryRingCases(r);
		// RF5h: the no-separator whole-number formatter.
		Cases.DisplayFormatWholeCases(r);
		// Round 41: the cache decisions (1s throttle, F9 generation).
		Cases.CacheGenerationCases(r);
		// R52: the evidence-extraction flow (trigger key, bundle name, retention) and the unresolved-fold
		// census the bundle carries.
		Cases.ExtractionCases(r);
		// R56: the battle reference -- format, lifecycle, launch namespace, collision handling.
		Cases.BattleRefCases(r);
		// R63's `Cases.SkillCooldown` and the policy it executed were DELETED in R65: the premise ("the
		// auto-skill master stores seconds") was falsified by a live battle, so the group that pinned the
		// x30 conversion is gone with it. 1016 -> 1002 cases (16 removed, 2 added in the cadence group).
		// R64/R65: the auto-skill cadence arithmetic (charge rate, charge in seconds, interval, median).
		Cases.AutoSkillCadence(r);
		// R66: the 技能时间表 -- one row per (unit, skill) out of a raw activation stream (merge window,
		// battle-clock axis, order, burst/overflow marks) plus the page text built from it.
		Cases.SkillTimeline(r);
		// R72: the hold that makes R71's origin shift land (an event that arrives before the evidence is
		// replayed on the corrected axis instead of blocking the shift).
		Cases.ClockOriginHoldCases(r);
		// R74: the calibration sampler's own evidence (why "no samples" happened, one bucket per value) --
		// the line that replaces the structurally-useless `samples=0` R71/R72 shipped, plus the unit choice
		// for the first charge (`m_firstCoolTimeFrame` when it answers, the seconds property x units as the
		// recorded fallback).
		Cases.ClockLagDiagnosticsCases(r);

		int fail = r.Failed;
		if (r.Cases != ExpectedCases)
		{
			Console.WriteLine("FAIL cases/pinned-total: got=" + r.Cases + " want=" + ExpectedCases);
			fail++;
		}
		Console.WriteLine("behavior tests: cases=" + r.Cases + " failed=" + fail + " pinned=" + ExpectedCases);
		Console.WriteLine(fail == 0 ? "== ALL PASS ==" : ("== " + fail + " FAILED =="));
		return fail == 0 ? 0 : 1;
	}
}
