using System;

namespace BehaviorTests;

internal static class Program
{
	/// <summary>Pinned case total. Deleting a case, or dropping a whole group from Main, fails the run.</summary>
	public const int ExpectedCases = 697;

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
