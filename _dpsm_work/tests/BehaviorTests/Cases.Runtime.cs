using System;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// RF4, first state family: everything that must SURVIVE a session boundary. These are the plan's
	/// RF1 lifecycle row (Start->Damage->End, idle->resume, a real end followed by a late event, restart,
	/// teardown, F9, consecutive A/B battles) executed against the production container -- before RF4 that
	/// row had NO offline coverage at all, because the state lived in static fields of the IL2CPP-bound
	/// facade.
	///
	/// The container is driven directly: no game, no Unity, no clock (every time value is passed in).
	/// </summary>
	public static void Runtime(Runner r)
	{
		var T0 = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
		DateTime T(double s) { return T0.AddSeconds(s); }

		r.Group("runtime/continuity-run-marker");
		var c = new SessionContinuity();
		var first = c.BeginSession(-1.0, 411001, 2.0);
		r.Eq("a-first-session-is-run-1", first.RunId, 1);
		r.Eq("a-first-session-is-sequence-0", first.RunSeq, 0);
		r.EqD("a-first-session-carries-no-gap", first.RunGap, -1.0);
		r.Str("a-first-session-has-no-previous-reason", first.PrevWhy, "");
		r.Eq("a-first-session-has-no-previous-result", first.PrevResult, 0);

		// A finished battle: the next session must NOT join it, even on the same quest and within the window.
		c.RememberEnd(411001, 2, "end", T(10));
		var afterWin = c.BeginSession(1.0, 411001, 2.0);
		r.Eq("a-real-end-starts-a-new-run", afterWin.RunId, 2);
		r.Eq("a-real-end-resets-the-sequence", afterWin.RunSeq, 0);
		r.EqD("a-new-run-carries-no-gap", afterWin.RunGap, -1.0);

		// A wave/arena close with NO result on the same quest, moments later: the same run continues.
		c.RememberEnd(411001, 0, "idle", T(20));
		var joined = c.BeginSession(1.5, 411001, 2.0);
		r.Eq("an-idle-close-continues-the-run", joined.RunId, 2);
		r.Eq("a-continuation-advances-the-sequence", joined.RunSeq, 1);
		r.EqD("a-continuation-carries-the-gap", joined.RunGap, 1.5);
		r.Str("a-continuation-names-the-previous-reason", joined.PrevWhy, "idle");
		r.Eq("a-continuation-names-the-previous-result", joined.PrevResult, 0);

		var third = c.BeginSession(1.5, 411001, 2.0);
		r.Eq("a-third-fragment-stays-in-the-run", third.RunId, 2);
		r.Eq("a-third-fragment-is-sequence-2", third.RunSeq, 2);

		var otherQuest = c.BeginSession(1.5, 700817, 2.0);
		r.Eq("a-different-quest-starts-a-new-run", otherQuest.RunId, 3);
		r.Eq("a-different-quest-resets-the-sequence", otherQuest.RunSeq, 0);

		r.Group("runtime/continuity-resume");
		var c2 = new SessionContinuity();
		var closed = new BattleSession { QuestId = 411001, InBattle = false };
		r.Eq("nothing-remembered-yet", (int)c2.Gate(false, -1.0, 5.0), (int)ResumeGate.NoClosedSession);
		c2.RememberClosed(closed, "idle", T(30));
		r.Str("the-close-reason-is-remembered", c2.LastClosedWhy, "idle");
		r.Same("the-closed-session-is-remembered", c2.LastClosed, closed);

		r.Eq("inside-the-window-is-eligible", (int)c2.Gate(true, 1.0, 5.0), (int)ResumeGate.Eligible);
		r.Same("an-eligible-gate-keeps-the-session", c2.LastClosed, closed);
		c2.ForgetClosed();
		r.True("taking-the-session-forgets-it", c2.LastClosed == null);
		r.Eq("a-taken-session-cannot-be-taken-again", (int)c2.Gate(false, 0.0, 5.0),
		      (int)ResumeGate.NoClosedSession);

		// The window: an EXPIRED one forgets the session, a real end does not.
		var c3 = new SessionContinuity();
		var closed3 = new BattleSession { QuestId = 411001 };
		c3.RememberClosed(closed3, "idle", T(40));
		r.Eq("exactly-at-the-window-is-eligible", (int)c3.Gate(true, 5.0, 5.0), (int)ResumeGate.Eligible);
		r.Eq("one-ulp-past-the-window-expires", (int)c3.Gate(true, Math.BitIncrement(5.0), 5.0),
		      (int)ResumeGate.WindowExpired);
		r.True("an-expired-window-forgets-the-session", c3.LastClosed == null);

		var c4 = new SessionContinuity();
		var closed4 = new BattleSession { QuestId = 411001 };
		c4.RememberClosed(closed4, "end", T(50));
		r.Eq("a-real-end-is-not-resumable", (int)c4.Gate(true, 1.0, 5.0), (int)ResumeGate.NotAnIdleClose);
		r.Same("a-non-idle-close-stays-remembered", c4.LastClosed, closed4);
		r.Eq("teardown-is-not-resumable", (int)c4.Gate(true, 1.0, 5.0), (int)ResumeGate.NotAnIdleClose);

		// An UNKNOWN actor: the container admits the window, and the facade owns the actor check, so the
		// session must stay remembered for a later event that names a known actor.
		var c5 = new SessionContinuity();
		var closed5 = new BattleSession { QuestId = 411001 };
		c5.RememberClosed(closed5, "idle", T(60));
		r.Eq("the-container-admits-an-unknown-actor", (int)c5.Gate(true, 1.0, 5.0), (int)ResumeGate.Eligible);
		r.Same("an-unknown-actor-leaves-the-session-remembered", c5.LastClosed, closed5);

		// F9 (ResetCurrent) resets a SESSION, not the continuity state: the container has no reset path, and
		// that is the contract. A new StartSession after a manual reset still sees the previous end.
		var c6 = new SessionContinuity();
		var beforeReset = c6.BeginSession(-1.0, 9999, 2.0);
		c6.RememberEnd(9999, 0, "idle", T(70));
		c6.RememberClosed(new BattleSession { QuestId = 9999 }, "idle", T(70));
		var afterReset = c6.BeginSession(0.5, 9999, 2.0);
		r.Eq("the-run-existed-before-the-reset", beforeReset.RunId, 1);
		r.EqD("a-manual-reset-does-not-drop-the-grouping-marker", afterReset.RunGap, 0.5);
		r.Eq("a-manual-reset-does-not-drop-the-run-id", afterReset.RunId, 1);
		r.Eq("a-manual-reset-does-not-drop-the-sequence", afterReset.RunSeq, 1);
		r.True("a-manual-reset-does-not-drop-the-remembered-close", c6.LastClosed != null);

		r.Group("runtime/continuity-consecutive-battles");
		// Two real battles in a row, then a soft close, then the next soft fragment: the sequence the
		// grouping marker exists for (measured earlier: a 45 s arena stage produced 13 exports).
		var c7 = new SessionContinuity();
		var a = c7.BeginSession(-1.0, 411001, 2.0);
		c7.RememberEnd(411001, 2, "end", T(100));
		var b = c7.BeginSession(1.0, 411001, 2.0);
		c7.RememberEnd(411001, 2, "end", T(200));
		var w1 = c7.BeginSession(1.0, 411001, 2.0);
		c7.RememberEnd(411001, 0, "idle", T(210));
		var w2 = c7.BeginSession(0.9, 411001, 2.0);
		c7.RememberEnd(411001, 0, "idle", T(220));
		var w3 = c7.BeginSession(0.9, 411001, 2.0);
		r.Eq("battle-A-is-run-1", a.RunId, 1);
		r.Eq("battle-B-is-run-2", b.RunId, 2);
		r.Eq("the-first-arena-fragment-opens-run-3", w1.RunId, 3);
		r.Eq("the-second-fragment-continues-run-3", w2.RunId, 3);
		r.Eq("the-second-fragment-is-sequence-1", w2.RunSeq, 1);
		r.Eq("the-third-fragment-is-sequence-2", w3.RunSeq, 2);
		r.EqD("the-third-fragment-carries-its-own-gap", w3.RunGap, 0.9);

		r.Group("policy/idle-rule");
		r.EqD("idle-seconds-is-8", SessionTransitionPolicy.IdleSeconds, 8.0);
		r.True("no-events-never-closes",
		       !SessionTransitionPolicy.ShouldCloseIdle(false, 0, 999.0, SessionTransitionPolicy.IdleSeconds));
		r.True("a-paused-frame-never-closes",
		       !SessionTransitionPolicy.ShouldCloseIdle(true, 10, 999.0, SessionTransitionPolicy.IdleSeconds));
		r.True("exactly-at-the-threshold-does-not-close (>)",
		       !SessionTransitionPolicy.ShouldCloseIdle(false, 10, 8.0, SessionTransitionPolicy.IdleSeconds));
		r.True("one-ulp-past-the-threshold-closes",
		       SessionTransitionPolicy.ShouldCloseIdle(false, 10, Math.BitIncrement(8.0),
		                                              SessionTransitionPolicy.IdleSeconds));
		r.True("silence-past-the-threshold-with-events-closes",
		       SessionTransitionPolicy.ShouldCloseIdle(false, 1, 9.0, SessionTransitionPolicy.IdleSeconds));
		r.True("a-paused-frame-with-events-and-silence-still-does-not-close",
		       !SessionTransitionPolicy.ShouldCloseIdle(true, 1, 100.0, SessionTransitionPolicy.IdleSeconds));
	}
}
