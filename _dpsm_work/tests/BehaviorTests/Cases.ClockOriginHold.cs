using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// R72: the hold that makes R71's origin shift land. The statements worth pinning are the ones that
	/// decide whether a whole battle's time axis moves:
	///   * an event is held ONLY while the origin is undecided and ONLY below the cap (the cap is a
	///     refusal, not a silent mix of axes);
	///   * a held event is replayed at the clock it ARRIVED at plus the lag -- never at "now", which would
	///     add the length of the hold window to every held hit;
	///   * the per-second bucketing rule is ONE rule (truncation, negative refused), because a series that
	///     disagrees by one index looks like a damage spike that never happened;
	///   * a stamp written before the decision moves by exactly the decided constant, and a refusal
	///     (lag 0/NaN) moves nothing.
	/// </summary>
	public static void ClockOriginHoldCases(Runner r)
	{
		r.Group("policy/clock-origin-hold");

		// ---- may an arriving event be held? ----
		r.True("an-undecided-origin-holds-the-event", ClockOriginHoldPolicy.ShouldHold(false, 0));
		r.True("a-decided-origin-holds-nothing", !ClockOriginHoldPolicy.ShouldHold(true, 0));
		r.True("the-cap-stops-the-hold", !ClockOriginHoldPolicy.ShouldHold(false, ClockOriginHoldPolicy.MaxHeld));
		r.True("just-below-the-cap-still-holds",
			ClockOriginHoldPolicy.ShouldHold(false, ClockOriginHoldPolicy.MaxHeld - 1));
		r.True("a-negative-count-is-not-a-hold", !ClockOriginHoldPolicy.ShouldHold(false, -1));
		r.Eq("the-cap-is-pinned", ClockOriginHoldPolicy.MaxHeld, 256);

		// ---- the cap is a refusal, and it is not the same question as "may I hold" ----
		r.True("a-full-hold-is-an-overflow", ClockOriginHoldPolicy.Overflowed(false, ClockOriginHoldPolicy.MaxHeld));
		r.True("an-overflow-past-the-cap-is-still-an-overflow",
			ClockOriginHoldPolicy.Overflowed(false, ClockOriginHoldPolicy.MaxHeld + 5));
		r.True("a-decided-origin-cannot-overflow", !ClockOriginHoldPolicy.Overflowed(true, 900));
		r.True("a-partial-hold-is-not-an-overflow", !ClockOriginHoldPolicy.Overflowed(false, 3));

		// ---- the clock a held event is replayed at ----
		r.EqD("a-held-event-replays-at-arrival-plus-the-lag",
			ClockOriginHoldPolicy.ReplayActive(0.03, 0.90), 0.93);
		// NOT "now": the 1.10 s that elapsed while the event was held must not be added to it (the window is
		// real battle time, and the arrival instant is what the hit belonged to).
		r.True("the-replay-is-not-now",
			ClockOriginHoldPolicy.ReplayActive(0.03, 0.90) < 1.10);
		r.EqD("a-refusal-replays-at-the-arrival", ClockOriginHoldPolicy.ReplayActive(0.03, 0.0), 0.03);
		r.EqD("a-nan-lag-replays-at-the-arrival", ClockOriginHoldPolicy.ReplayActive(0.03, double.NaN), 0.03);
		r.EqD("a-negative-lag-replays-at-the-arrival", ClockOriginHoldPolicy.ReplayActive(0.03, -4.0), 0.03);
		r.EqD("a-nan-arrival-replays-at-the-lag", ClockOriginHoldPolicy.ReplayActive(double.NaN, 0.90), 0.90);
		r.EqD("a-negative-arrival-replays-at-the-lag", ClockOriginHoldPolicy.ReplayActive(-2.0, 0.90), 0.90);

		// ---- the one bucketing rule ----
		r.Eq("second-zero-holds-0.0", ClockOriginHoldPolicy.SecondIndex(0.0), 0);
		r.Eq("a-fraction-truncates-down", ClockOriginHoldPolicy.SecondIndex(0.99), 0);
		r.Eq("exactly-one-second-is-bucket-one", ClockOriginHoldPolicy.SecondIndex(1.0), 1);
		r.Eq("2.999-is-still-bucket-two", ClockOriginHoldPolicy.SecondIndex(2.999), 2);
		r.Eq("119.9-is-bucket-119", ClockOriginHoldPolicy.SecondIndex(119.9), 119);
		r.Eq("a-negative-clock-has-no-bucket", ClockOriginHoldPolicy.SecondIndex(-0.001), -1);
		r.Eq("a-nan-clock-has-no-bucket", ClockOriginHoldPolicy.SecondIndex(double.NaN), -1);
		r.Eq("a-clock-past-any-battle-has-no-bucket", ClockOriginHoldPolicy.SecondIndex(36000.0), -1);
		r.Eq("the-last-representable-second-still-has-one",
			ClockOriginHoldPolicy.SecondIndex(35999.99), 35999);

		// ---- moving a stamp written before the decision ----
		r.EqD("a-stamp-moves-by-the-decided-lag", ClockOriginHoldPolicy.Shift(3.0, 0.90), 3.9);
		r.EqD("a-refusal-moves-nothing", ClockOriginHoldPolicy.Shift(3.0, 0.0), 3.0);
		r.EqD("a-nan-lag-moves-nothing", ClockOriginHoldPolicy.Shift(3.0, double.NaN), 3.0);
		r.EqD("a-negative-lag-moves-nothing", ClockOriginHoldPolicy.Shift(3.0, -0.9), 3.0);
		r.True("a-nan-stamp-stays-nan", double.IsNaN(ClockOriginHoldPolicy.Shift(double.NaN, 0.90)));
		r.EqD("a-zero-stamp-moves-to-the-lag (it is a real reading)", ClockOriginHoldPolicy.Shift(0.0, 0.90), 0.9);

		// ---- the session side: one place moves the clock, and it moves both clocks plus the pending
		//      damage figures (which the calc hooks write outside the held recorder) ----
		r.Group("runtime/clock-origin");

		var s = new BattleSession();
		s.Advance(0.03, false);
		var pending = new HitRecord { T = 0.02, Damage = 10 };
		s.PendingHits.Add(pending);
		r.EqD("the-session-before-the-shift", s.ActiveSeconds, 0.03);
		s.ApplyClockOrigin(0.90);
		r.EqD("the-shift-moves-the-battle-clock", s.ActiveSeconds, 0.93);
		r.EqD("and-the-no-pause-clock", s.CombatSeconds, 0.93);
		r.EqD("and-it-remembers-what-it-added", s.ClockOriginShift, 0.90);
		r.EqD("and-the-pending-damage-figure-moves-with-it", s.PendingHits[0].T, 0.92);

		var refuse = new BattleSession();
		refuse.Advance(0.03, false);
		refuse.ApplyClockOrigin(0.0);
		r.EqD("a-refusal-does-not-move-the-clock", refuse.ActiveSeconds, 0.03);
		r.EqD("and-records-no-shift", refuse.ClockOriginShift, 0.0);
		refuse.ApplyClockOrigin(double.NaN);
		r.EqD("a-nan-lag-does-not-move-the-clock", refuse.ActiveSeconds, 0.03);

		// the last-event stamp the idle rule reads must move with the clock, or a shift would look like
		// silence (idle = CombatSeconds - LastEventCombat)
		var idle = new BattleSession();
		idle.Advance(0.05, false);
		idle.NoteEvent();
		r.EqD("idle-is-zero-right-after-an-event", idle.IdleCombatSeconds, 0.0);
		idle.ApplyClockOrigin(0.90);
		r.EqD("a-shift-does-not-invent-silence", idle.IdleCombatSeconds, 0.0);

		// ---- the per-battle container: the queue and the refusal flag are per battle, and F9 drops the
		//      queue (a held hit must not be replayed into a battle that was just reset) ----
		r.Group("runtime/clock-origin-counter");

		var rt = new BattleRuntimeCounters();
		rt.OnSessionStart();
		rt.OriginHeld.Add(new OriginHeldEvent { ArrivalSeconds = 0.02, Amount = 7 });
		rt.OriginHoldOverflowed = true;
		rt.OriginHeldReplayed = 3;
		rt.EventCount = 9;
		rt.OnSessionStart();
		r.Eq("a-new-battle-starts-with-an-empty-hold", rt.OriginHeld.Count, 0);
		r.Eq("and-no-overflow-flag", rt.OriginHoldOverflowed ? 1 : 0, 0);
		r.Eq("and-no-replayed-count", rt.OriginHeldReplayed, 0);
		r.Eq("and-no-events", rt.EventCount, 0);

		var rt2 = new BattleRuntimeCounters();
		rt2.OnSessionStart();
		rt2.OriginHeld.Add(new OriginHeldEvent { ArrivalSeconds = 0.02, Amount = 7 });
		rt2.EventCount = 4;
		rt2.OnManualReset();
		r.Eq("f9-drops-the-held-events", rt2.OriginHeld.Count, 0);
		r.Eq("f9-drops-the-event-count", rt2.EventCount, 0);
	}
}
