using System;

namespace BehaviorTests;

internal static class Program
{
	/// <summary>Pinned case total. Deleting a case, or dropping a whole group from Main, fails the run.</summary>
	public const int ExpectedCases = 378;

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
