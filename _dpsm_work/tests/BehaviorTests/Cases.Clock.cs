using System;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// The battle clock. Before this file the clock had ZERO executable coverage: the only place time is
	/// accumulated is BattleSession.Advance, and the only place it is formatted is BattleTime -- both
	/// would keep compiling through a refactor that changed their semantics, and no battle export would
	/// show it (the export writes a duration, not the accumulation rule).
	///
	/// The stall clamp case is deliberately a NON-clamp: MaxFrameDelta = 0.25 s lives in Aggregator, so
	/// Advance must add the whole delta. If a refactor moves the clamp down into Advance this goes red.
	/// </summary>
	public static void Clock(Runner r)
	{
		r.Group("clock/advance");
		var s = new BattleSession();
		s.Advance(0.0, false);
		r.EqD("zero-delta-adds-no-time", s.ActiveSeconds, 0.0);
		r.True("zero-delta-does-not-start-timing", !s.TimingStarted);
		s.Advance(-5.0, false);
		r.EqD("negative-delta-is-ignored", s.ActiveSeconds, 0.0);
		s.Advance(0.25, false);
		r.EqD("real-delta-is-added", s.ActiveSeconds, 0.25);
		r.EqD("unpaused-delta-fills-combat-clock", s.CombatSeconds, 0.25);
		r.True("first-time-starts-timing", s.TimingStarted);
		s.Advance(0.1, true);
		r.EqD("paused-delta-still-moves-active", s.ActiveSeconds, 0.35);
		r.EqD("paused-delta-does-not-move-combat", s.CombatSeconds, 0.25);

		var stall = new BattleSession();
		stall.Advance(2.9, false);
		r.EqD("advance-does-not-clamp-a-stall (clamp lives in Aggregator)", stall.ActiveSeconds, 2.9);

		r.Group("clock/idle-rule-input");
		var idle = new BattleSession();
		idle.Advance(10.0, false);
		idle.NoteEvent();
		r.EqD("idle-is-zero-right-after-an-event", idle.IdleCombatSeconds, 0.0);
		idle.Advance(3.0, false);
		r.EqD("idle-counts-unpaused-time", idle.IdleCombatSeconds, 3.0);
		idle.Advance(7.0, true);
		r.EqD("idle-ignores-paused-time", idle.IdleCombatSeconds, 3.0);
		idle.NoteEvent();
		r.EqD("a-new-event-resets-the-idle-age", idle.IdleCombatSeconds, 0.0);

		r.Group("clock/formatting");
		r.Str("seconds-f0", BattleTime.Seconds(17.94), "18" + "\u79d2");
		r.Str("seconds-zero", BattleTime.Seconds(0.0), "0" + "\u79d2");
		r.Str("hit-f1", BattleTime.Hit(17.94), "t=17.9s");
		r.Str("log-f1", BattleTime.Log(17.94), "17.9s");
		r.Str("log-zero", BattleTime.Log(0.0), "0.0s");
	}
}
