using System;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	// ------------------------------------------------------------------------------------------------
	// RF3 (pure policy extraction). The legacy oracles below are TRANSCRIBED in full from the pre-RF3
	// source (git 8f3aafd: src/Aggregator.Clock.cs::FrameDelta, src/Aggregator.Session.cs::StartSession /
	// TryResumeClosedSession, src/Aggregator.Attribution.cs, src/Aggregator.Stats.cs). They are NOT
	// production code and prove nothing on their own -- the production code under test is the policy. What
	// they do is turn "the extraction changed nothing" into a comparison over a GRID instead of over the
	// few inputs a person happened to think of, which is where a bad parameterisation would hide.
	// ------------------------------------------------------------------------------------------------

	private static string LegacySource(string cfg, bool legacy)
	{
		string source = (cfg != null) ? cfg.Trim().ToLowerInvariant() : "";
		if (source != "real" && source != "engine" && source != "game")
			source = legacy ? "engine" : "game";
		return source;
	}

	private static bool LegacyRunContinues(bool hasEnded, int prevResult, int prevQuest, int quest,
	                                      double gap, double join)
	{
		return hasEnded && prevResult == 0 && prevQuest == quest && gap >= 0.0 && gap <= join;
	}

	private static bool LegacyLiveAge(double age) { return age >= -0.05 && age <= 0.20; }

	private static bool LegacyLiveTarget(double age, bool sameTarget) { return sameTarget || age <= 0.08; }

	private static bool LegacyDmgMatches(int cand, int damage, int nominal)
	{
		return cand == damage || (nominal > 0 && cand == nominal);
	}

	private static double LegacyClamp(double dt, double max)
	{
		if (dt < 0.0) dt = 0.0;
		if (dt > max) dt = max;
		return dt;
	}

	public static void Policy(Runner r)
	{
		r.Group("policy/clock-source");
		r.Str("unset-config-defaults-to-game", BattleClockPolicy.ResolveSource(null, false), "game");
		r.Str("empty-config-defaults-to-game", BattleClockPolicy.ResolveSource("", false), "game");
		r.Str("legacy-bool-turns-the-fallback-to-engine", BattleClockPolicy.ResolveSource(null, true), "engine");
		r.Str("a-misspelled-value-falls-back", BattleClockPolicy.ResolveSource("xyz", false), "game");
		r.Str("a-misspelled-value-with-legacy-falls-back-to-engine", BattleClockPolicy.ResolveSource("xyz", true), "engine");
		r.Str("case-and-surrounding-space-are-normalised", BattleClockPolicy.ResolveSource("  REAL  ", false), "real");
		r.Str("engine-is-named-as-is", BattleClockPolicy.ResolveSource("Engine", false), "engine");
		r.Str("game-wins-over-the-legacy-bool", BattleClockPolicy.ResolveSource("game", true), "game");
		int srcMismatch = 0;
		string[] cfgs = new string[] { null, "", "real", "REAL", " real ", "engine", "game", "Game", "xyz", "0" };
		foreach (string c in cfgs)
			foreach (bool legacy in new bool[] { false, true })
				if (BattleClockPolicy.ResolveSource(c, legacy) != LegacySource(c, legacy)) srcMismatch++;
		r.Eq("oracle-sweep-source-resolution-mismatches", srcMismatch, 0);

		r.Group("policy/clock-units");
		r.EqD("default-units-per-game-second", BattleClockPolicy.DefaultUnitsPerGameSecond, 30.0);
		r.EqD("a-configured-value-wins", BattleClockPolicy.ResolveUnitsPerGameSecond(45.0, 30.0, 30.0), 45.0);
		r.EqD("a-zero-config-falls-to-the-probe", BattleClockPolicy.ResolveUnitsPerGameSecond(0.0, 60.0, 30.0), 60.0);
		r.EqD("a-negative-config-falls-to-the-probe", BattleClockPolicy.ResolveUnitsPerGameSecond(-1.0, 60.0, 30.0), 60.0);
		r.EqD("a-zero-probe-falls-to-the-fallback", BattleClockPolicy.ResolveUnitsPerGameSecond(0.0, 0.0, 30.0), 30.0);
		r.EqD("a-negative-probe-falls-to-the-fallback", BattleClockPolicy.ResolveUnitsPerGameSecond(0.0, -2.0, 30.0), 30.0);
		r.EqD("nothing-configured-falls-back", BattleClockPolicy.ResolveUnitsPerGameSecond(0.0, 0.0,
		                                                                             BattleClockPolicy.DefaultUnitsPerGameSecond), 30.0);
		int unitMismatch = 0;
		double[] vals = new double[] { -1.0, 0.0, 0.001, 30.0, 45.0 };
		foreach (double c in vals)
			foreach (double p in vals)
				if (Math.Abs(BattleClockPolicy.ResolveUnitsPerGameSecond(c, p, 30.0)
				             - ((c > 0.0) ? c : ((p > 0.0) ? p : 30.0))) > 1e-12) unitMismatch++;
		r.Eq("oracle-sweep-units-mismatches", unitMismatch, 0);

		r.Group("policy/clock-clamp");
		r.EqD("max-frame-delta-is-0.25s", BattleClockPolicy.MaxFrameDelta, 0.25);
		r.EqD("a-negative-delta-is-zero", BattleClockPolicy.ClampFrameDelta(-5.0, 0.25), 0.0);
		r.EqD("a-zero-delta-stays-zero", BattleClockPolicy.ClampFrameDelta(0.0, 0.25), 0.0);
		r.EqD("a-normal-delta-passes-through", BattleClockPolicy.ClampFrameDelta(0.033, 0.25), 0.033);
		r.EqD("exactly-at-the-bound-is-not-clamped (>)", BattleClockPolicy.ClampFrameDelta(0.25, 0.25), 0.25);
		r.EqD("one-ulp-past-the-bound-is-clamped", BattleClockPolicy.ClampFrameDelta(Math.BitIncrement(0.25), 0.25), 0.25);
		r.EqD("a-stall-is-capped-at-the-bound", BattleClockPolicy.ClampFrameDelta(2.9, BattleClockPolicy.MaxFrameDelta), 0.25);
		int clampMismatch = 0;
		double[] deltas = new double[] { -5.0, -1e-12, 0.0, 1e-12, 0.1, 0.25, 0.2500001, 2.9 };
		foreach (double d in deltas)
			if (Math.Abs(BattleClockPolicy.ClampFrameDelta(d, 0.25) - LegacyClamp(d, 0.25)) > 1e-12) clampMismatch++;
		r.Eq("oracle-sweep-clamp-mismatches", clampMismatch, 0);

		r.Group("policy/clock-delta");
		double last = -1.0;
		r.EqD("the-first-real-tick-charges-nothing", BattleClockPolicy.RealDelta(500.0, ref last), 0.0);
		r.EqD("the-first-tick-starts-the-clock-at-now", last, 500.0);
		r.EqD("the-next-tick-is-the-difference", BattleClockPolicy.RealDelta(501.5, ref last), 1.5);
		r.EqD("the-last-tick-was-updated", last, 501.5);
		r.EqD("a-backwards-counter-yields-a-negative-delta (clamped by the caller)",
		      BattleClockPolicy.RealDelta(500.0, ref last), -1.5);
		double fresh = -1.0;
		r.EqD("a-fresh-clock-starts-at-zero-not-at-uptime", BattleClockPolicy.RealDelta(9999.0, ref fresh), 0.0);

		int lastSteps = 0;
		bool hasLast = false;
		r.EqD("the-first-game-tick-has-no-previous-value", BattleClockPolicy.GameDelta(100, ref lastSteps, ref hasLast, 30.0), 0.0);
		r.True("the-first-game-tick-remembers-the-counter", hasLast && lastSteps == 100);
		r.EqD("a-45-step-frame-at-30-units-is-1.5s", BattleClockPolicy.GameDelta(145, ref lastSteps, ref hasLast, 30.0), 1.5);
		r.EqD("a-non-advancing-counter-adds-nothing", BattleClockPolicy.GameDelta(145, ref lastSteps, ref hasLast, 30.0), 0.0);
		r.EqD("a-backwards-counter-adds-nothing", BattleClockPolicy.GameDelta(10, ref lastSteps, ref hasLast, 30.0), 0.0);
		r.EqD("the-backwards-counter-is-now-the-reference", BattleClockPolicy.GameDelta(40, ref lastSteps, ref hasLast, 30.0), 1.0);

		r.Group("policy/run-grouping");
		r.EqD("run-join-window-is-2s", SessionTransitionPolicy.RunJoinSeconds, 2.0);
		bool ended = true;
		r.True("no-previous-end-never-joins",
		       !SessionTransitionPolicy.RunContinues(false, 0, 7, 7, 0.5, 2.0));
		r.True("a-finished-battle-never-joins",
		       !SessionTransitionPolicy.RunContinues(true, 1, 7, 7, 0.5, 2.0));
		r.True("a-different-quest-never-joins",
		       !SessionTransitionPolicy.RunContinues(true, 0, 7, 8, 0.5, 2.0));
		r.True("a-negative-gap-never-joins",
		       !SessionTransitionPolicy.RunContinues(true, 0, 7, 7, -0.1, 2.0));
		// The boundary cases use a LITERAL threshold: a case that passes the constant cannot detect a
		// changed constant (the driver caught exactly that), and the threshold itself is pinned separately.
		r.True("inside-the-window-joins",
		       SessionTransitionPolicy.RunContinues(true, 0, 7, 7, 1.999, 2.0));
		r.True("exactly-at-the-window-joins (<=)",
		       SessionTransitionPolicy.RunContinues(true, 0, 7, 7, 2.0, 2.0));
		r.True("one-ulp-past-the-window-does-not-join",
		       !SessionTransitionPolicy.RunContinues(true, 0, 7, 7, Math.BitIncrement(2.0), 2.0));
		int runMismatch = 0;
		foreach (bool he in new bool[] { false, true })
			foreach (int pr in new int[] { 0, 1 })
				foreach (int pq in new int[] { 6, 7 })
					foreach (double g in new double[] { -1.0, 0.0, 1.999, 2.0, 2.001, 9.0 })
						if (SessionTransitionPolicy.RunContinues(he, pr, pq, 7, g, 2.0)
						    != LegacyRunContinues(he, pr, pq, 7, g, 2.0)) runMismatch++;
		r.Eq("oracle-sweep-run-join-mismatches", runMismatch, 0);
		r.True("a-guard-reads-the-local-flags (sanity)", ended);

		long runId = 0;
		int runSeq = 0;
		SessionTransitionPolicy.NextRun(true, ref runId, ref runSeq);
		r.Eq("continuing-keeps-the-run-id", runId, 0);
		r.Eq("continuing-advances-the-sequence", runSeq, 1);
		SessionTransitionPolicy.NextRun(true, ref runId, ref runSeq);
		r.Eq("a-second-continuation-is-sequence-2", runSeq, 2);
		SessionTransitionPolicy.NextRun(false, ref runId, ref runSeq);
		r.Eq("not-continuing-starts-a-new-run", runId, 1);
		r.Eq("a-new-run-resets-the-sequence", runSeq, 0);

		r.Group("policy/resume-gate");
		r.EqD("resume-window-is-5s", SessionTransitionPolicy.ResumeWindowSeconds, 5.0);
		r.Eq("nothing-remembered", (int)SessionTransitionPolicy.ClosedSessionGate(false, 0.0, 5.0, ""),
		      (int)ResumeGate.NoClosedSession);
		r.Eq("inside-the-window-and-idle-is-eligible",
		      (int)SessionTransitionPolicy.ClosedSessionGate(true, 1.0, 5.0, "idle"), (int)ResumeGate.Eligible);
		r.Eq("exactly-at-the-window-is-eligible (>)",
		      (int)SessionTransitionPolicy.ClosedSessionGate(true, 5.0, 5.0, "idle"), (int)ResumeGate.Eligible);
		r.Eq("one-ulp-past-the-window-expires",
		      (int)SessionTransitionPolicy.ClosedSessionGate(true, Math.BitIncrement(5.0), 5.0, "idle"),
		      (int)ResumeGate.WindowExpired);
		r.Eq("a-real-battle-end-is-not-resumable",
		      (int)SessionTransitionPolicy.ClosedSessionGate(true, 1.0, 5.0, "end"), (int)ResumeGate.NotAnIdleClose);
		r.Eq("teardown-is-not-resumable",
		      (int)SessionTransitionPolicy.ClosedSessionGate(true, 1.0, 5.0, "teardown"), (int)ResumeGate.NotAnIdleClose);
		r.Eq("the-window-is-checked-before-the-reason",
		      (int)SessionTransitionPolicy.ClosedSessionGate(true, 6.0, 5.0, "end"), (int)ResumeGate.WindowExpired);
		r.Eq("an-empty-reason-is-not-an-idle-close",
		      (int)SessionTransitionPolicy.ClosedSessionGate(true, 1.0, 5.0, ""), (int)ResumeGate.NotAnIdleClose);

		r.Group("policy/live-pair");
		r.EqD("live-pair-min-age", AttributionPolicy.LivePairMinAge, -0.05);
		r.EqD("live-pair-max-age", AttributionPolicy.LivePairMaxAge, 0.20);
		r.EqD("live-blind-age", AttributionPolicy.LiveBlindAge, 0.08);
		r.EqD("exact-value-window", AttributionPolicy.ExactValueWindow, 0.80);
		r.EqD("fifo-window", AttributionPolicy.FifoWindow, 0.60);
		r.EqD("calc-source-window", AttributionPolicy.CalcSourceWindow, 0.45);
		r.True("exactly-at-the-min-age-is-eligible",
		       AttributionPolicy.LiveAgeEligible(-0.05, -0.05, 0.20));
		r.True("just-before-the-min-age-is-not",
		       !AttributionPolicy.LiveAgeEligible(Math.BitDecrement(-0.05), -0.05, 0.20));
		r.True("exactly-at-the-max-age-is-eligible",
		       AttributionPolicy.LiveAgeEligible(0.20, -0.05, 0.20));
		r.True("one-ulp-past-the-max-age-is-not",
		       !AttributionPolicy.LiveAgeEligible(Math.BitIncrement(0.20), -0.05, 0.20));
		r.True("a-confirmed-target-is-admitted-at-any-accepted-age",
		       AttributionPolicy.LiveTargetAdmitted(0.20, true, 0.08));
		r.True("an-unconfirmed-target-is-admitted-up-to-the-blind-age",
		       AttributionPolicy.LiveTargetAdmitted(0.08, false, 0.08));
		r.True("one-ulp-past-the-blind-age-is-not-admitted",
		       !AttributionPolicy.LiveTargetAdmitted(Math.BitIncrement(0.08), false, 0.08));
		int ageMismatch = 0;
		int targetMismatch = 0;
		foreach (double a in new double[] { -0.2, -0.05, -0.049, 0.0, 0.08, 0.081, 0.20, 0.201, 1.0 })
		{
			if (AttributionPolicy.LiveAgeEligible(a, -0.05, 0.20) != LegacyLiveAge(a)) ageMismatch++;
			foreach (bool same in new bool[] { false, true })
				if (AttributionPolicy.LiveTargetAdmitted(a, same, 0.08) != LegacyLiveTarget(a, same)) targetMismatch++;
		}
		r.Eq("oracle-sweep-live-age-mismatches", ageMismatch, 0);
		r.Eq("oracle-sweep-live-target-mismatches", targetMismatch, 0);

		r.Group("policy/dmg-match");
		r.True("the-applied-damage-matches", AttributionPolicy.DmgMatches(198, 198, 421140));
		r.True("the-nominal-of-an-absorbed-hit-matches", AttributionPolicy.DmgMatches(421140, 198, 421140));
		r.True("a-zero-nominal-never-matches", !AttributionPolicy.DmgMatches(0, 198, 0));
		r.True("a-different-value-does-not-match", !AttributionPolicy.DmgMatches(199, 198, 421140));
		r.True("a-negative-nominal-is-not-a-match", !AttributionPolicy.DmgMatches(-5, 198, -5));
		int dmgMismatch = 0;
		foreach (int c in new int[] { -5, 0, 198, 199, 421140 })
			foreach (int d in new int[] { 0, 198, 421140 })
				foreach (int nm in new int[] { -5, 0, 198, 421140 })
					if (AttributionPolicy.DmgMatches(c, d, nm) != LegacyDmgMatches(c, d, nm)) dmgMismatch++;
		r.Eq("oracle-sweep-dmg-match-mismatches", dmgMismatch, 0);

		r.Group("policy/pair-label");
		r.Str("live-same", AttributionPolicy.PairLabel(PairKind.LiveSame), "live-same");
		r.Str("live-age", AttributionPolicy.PairLabel(PairKind.LiveAge), "live-age");
		r.Str("value", AttributionPolicy.PairLabel(PairKind.ExactValue), "value");
		r.Str("fifo", AttributionPolicy.PairLabel(PairKind.Fifo), "fifo");
		r.Str("none-is-empty", AttributionPolicy.PairLabel(PairKind.None), "");
		r.Eq("live-same-kind", (int)AttributionPolicy.LivePairKind(true), (int)PairKind.LiveSame);
		r.Eq("live-age-kind", (int)AttributionPolicy.LivePairKind(false), (int)PairKind.LiveAge);

		r.Group("policy/calc-source");
		r.Eq("attacker-wins", (int)AttributionPolicy.CalcSourcePriority(true, true), (int)CalcSourceKind.Attacker);
		r.Eq("owner-is-the-fallback", (int)AttributionPolicy.CalcSourcePriority(false, true), (int)CalcSourceKind.Owner);
		r.Eq("neither-is-unknown", (int)AttributionPolicy.CalcSourcePriority(false, false), (int)CalcSourceKind.None);
		r.Str("tag-attacker", AttributionPolicy.CalcSourceTag(CalcSourceKind.Attacker), "calcA");
		r.Str("tag-owner", AttributionPolicy.CalcSourceTag(CalcSourceKind.Owner), "calcO");
		r.Str("tag-unknown", AttributionPolicy.CalcSourceTag(CalcSourceKind.None), "calc?");
	}
}
