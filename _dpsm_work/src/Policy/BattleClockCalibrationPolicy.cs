using System;
using System.Collections.Generic;

namespace DpsMeter;

/// <summary>
/// R71: align the battle clock's ORIGIN with the game's own battle start.
///
/// THE DEFECT (measured 2026-10-06, R71). The clock's RATE was always right (`ClockSource=game` divides
/// `GameSystem.GameTime` steps by the game's units-per-second, so it ticks exactly like skill cooldowns).
/// The ORIGIN was not: it started when the plugin first saw the battle, which is ~0.9 s AFTER the game
/// initialised it. Three independent measurements agreed on the same number:
///   1. the auto-skill charge counters (below): lag = 0.90-0.93 s, constant over 12 slots x 3 battles;
///   2. the game's own on-screen battle timer (`GameTimeLimitCounter.NowTime`): `now/30 + active` was
///      constant to 0.03-0.07 s over 27-40 samples per battle at 89.07-89.13 (a 90 s stage limit) and
///      119.07-119.13 (a 120 s stage limit) => same rate, lag = 0.90 s;
///   3. the page's earliest activation per skill vs the master's `FirstCoolTime`: every skill fired at
///      firstCool - 0.9 s in our old numbers.
/// The user chose to fix it (方案 B): moving the origin makes every published time comparable with the
/// game's own 初动/冷却/计时器 -- the page, the `active=` columns, the export's `events[].t` and `duration`
/// (which now reads 120.0 s for a 120 s stage). Files written before 1.7.22 keep the old origin, i.e. their
/// absolute times are ~0.9 s early; every relative quantity is unaffected.
///
/// HOW THE LAG IS MEASURED, AND WHY THE COUNTER IS THE RIGHT INSTRUMENT. An auto-skill slot carries the
/// game's own charge counter (`Skill.WaitCountFrame` over `Skill.FirstCoolTime`, 30 units = 1 game second)
/// and the game INITIALISES it to `FirstCoolTime` at battle start, resetting it to `CoolTime` after each
/// activation. So while a slot is still on its FIRST charge,
///       lag = (FirstCoolTime - WaitCountFrame) / unitsPerSecond - activeSeconds
/// is "how long the game had already been charging this slot when our clock read `activeSeconds`" -- i.e.
/// exactly the origin offset. That the counter starts at `FirstCoolTime` (not `CoolTime`) is not an
/// assumption: with `CoolTime` the estimates scatter (0.9 / 2.9 / 4.9 / 9.9 s across slots) and with
/// `FirstCoolTime` they collapse to 0.90 +/- 0.03 s, which is also what the game's own timer says.
///
/// WHY THIS IS A POLICY. It is arithmetic that can be wrong while looking reasonable -- a bad sample (a slot
/// that already fired, an unreadable field, a party whose units spawn late) would silently shift every
/// published time in the battle. The bounds, the median and the "only before the first recorded event" rule
/// live here so they are executable in the behaviour suite, and the facade (Aggregator) only reads and adds.
/// </summary>
internal static class BattleClockCalibrationPolicy
{
	/// <summary>Below this the measurement is noise (our clock is at most a tick behind), so no shift is
	/// applied -- a rebase of 0.01 s is not worth an exception to "one axis per battle".</summary>
	internal const double MinLagSeconds = 0.05;

	/// <summary>Above this the measurement is not a battle-start offset but something else (a scene load, a
	/// probe that ran late, a slot whose first charge predates the battle). Refused and reported: shifting
	/// the axis by seconds on a bad reading would corrupt every time in the file.</summary>
	internal const double MaxLagSeconds = 5.0;

	/// <summary>A sample that says our clock is AHEAD by more than this is refused too: the game cannot have
	/// started the battle after we saw it.</summary>
	internal const double MaxAheadSeconds = 0.5;

	/// <summary>The rebase must happen inside this window (battle seconds). It cannot be later: the axis
	/// must be defined before the first event is stamped, and the calibrating slots must still be on their
	/// first charge (the shortest `FirstCoolTime` observed is 3 s).</summary>
	internal const double WindowSeconds = 2.0;

	/// <summary>Slots a measurement needs before it is used. Two independent slots must agree that the
	/// origin is late; with one slot a single bad read would move the whole axis.</summary>
	internal const int MinSamples = 2;

	/// <summary>Slots a measurement may look at (the standby list is short, this only bounds a pathological
	/// one).</summary>
	internal const int MaxSamples = 16;

	/// <summary>
	/// R74: WHY a slot's reading was not usable. R73 needed two rounds to answer "why is `samples=0`"
	/// because a refusal could not name itself: the deciding frame ALWAYS rejects on the window clause
	/// (`TryAlignClockOrigin` only decides once `ActiveSeconds > WindowSeconds`), so the `samples=` it
	/// printed was structurally 0, and the real cause -- every slot refused by `waitFrames > firstCoolFrames`
	/// because the value handed in was in SECONDS while the counter is in UNITS -- stayed invisible in both
	/// the R71 and the R72 log.
	/// </summary>
	internal enum LagReason
	{
		Usable = 0,
		/// <summary>No readable first charge (the frame-denominated field did not resolve and the seconds
		/// fallback produced nothing): a slot that starts ready, e.g. an over-skill with no first wait.</summary>
		NoFirstCool,
		/// <summary>The live counter is not inside `[0, firstCool]`: either it already fired and was reset to
		/// `CoolTime`, or the two readings are in different units (exactly the R71/R72 defect).</summary>
		WaitOutOfRange,
		/// <summary>Units per game second unknown (`Skill.CoolTimeFrame / Skill.CoolTime` unreadable).</summary>
		BadUnits,
		/// <summary>Our clock is outside the calibration window: the calibrating slots are no longer
		/// guaranteed to be on their first charge, so no reading taken now may move the axis.</summary>
		OutOfWindow,
		/// <summary>Inside the window, but outside the accepted lag bounds (see <see cref="MinLagSeconds"/>,
		/// <see cref="MaxAheadSeconds"/> and <see cref="MaxLagSeconds"/>).</summary>
		LagOutOfRange
	}

	/// <summary>
	/// R74: the FIRST CHARGE in game UNITS, from the two readings the runtime `Skill` offers.
	///
	/// R73 (read from `BepInEx/interop/Assembly-CSharp.dll`) and R74 (settled by the compiler): the runtime
	/// `Skill` has NO `FirstCoolTimeFrame` property and no `m_firstCoolTimeFrame` FIELD either --
	/// `get_FirstCoolTimeFrame` occurs 0 times, and the only member named `m_firstCoolTimeFrame` sits in the
	/// MASTER-DATA member block next to `m_coolTimeFrame`, `SetCoolTimeFrame` and the game's own
	/// `CalcFirstCoolTimeFrame`/`GetFirstCoolTimeFrame`, i.e. it is reached as
	/// `Skill.m_data.m_firstCoolTimeFrame` (`Skill.m_firstCoolTimeFrame` is a compile error, CS1061).
	/// `FirstCoolTime` is the sibling property of `CoolTime`, and the plugin's own
	/// conversion proves *that* one is in SECONDS (`Skill.CoolTimeFrame / Skill.CoolTime` = 240/8 = 30
	/// units per game second) -- which is why `(FirstCoolTime - WaitCountFrame)` mixed units and refused
	/// every slot in every frame of every battle.
	///
	/// So the frame-denominated field WINS whenever it reads above 0; the seconds property is only the
	/// fallback for a build where that field cannot be read, and the caller records which route was used so
	/// a fallback value can never be mistaken for a primary one.
	///
	/// WHAT IS DELIBERATELY NOT DECIDED HERE: whether `m_firstCoolTimeFrame` holds the INITIAL/target first
	/// charge (static) or the CURRENT remaining first charge (a live countdown). This method picks a SOURCE
	/// only. The semantics are settled by measurement: a live-countdown reading makes `first - wait` about
	/// 0, which <see cref="Reject"/> refuses by its bounds on its own, and both readings are printed side by
	/// side in the `chg` line and the `[CLOCK] calib` line so ONE battle settles it.
	/// </summary>
	internal static int FirstCoolFrames(int firstCoolSeconds, int firstCoolTimeFrame, double unitsPerSecond,
		out bool usedFallback)
	{
		usedFallback = false;
		if (firstCoolTimeFrame > 0) return firstCoolTimeFrame;
		if (firstCoolSeconds <= 0 || unitsPerSecond <= 1.0) return 0;
		double frames = firstCoolSeconds * unitsPerSecond;
		if (double.IsNaN(frames) || frames > int.MaxValue) return 0;
		usedFallback = true;
		// Round to the nearest unit: the counter is an integer and the seconds property was itself derived
		// by a division, so truncating would bias every slot low by up to one unit.
		return (int)(frames + 0.5);
	}

	/// <summary>
	/// One slot's contribution, or false when the slot cannot answer: an unreadable/zero first charge (a
	/// skill without a first charge), a counter that is not inside `[0, firstCool]` (it already fired and was
	/// reset to `CoolTime`, or -- the R71/R72 defect -- the two readings were in different units), an
	/// unknown units-per-second, or a clock that is not inside the calibration window.
	///
	/// R74: the REASON is available from <see cref="Reject"/>; this wrapper keeps the boolean shape so the
	/// existing callers and pinned cases read exactly as before.
	/// </summary>
	internal static bool TryLag(int firstCoolFrames, int waitFrames, double unitsPerSecond,
		double activeSeconds, out double lag)
	{
		return Reject(firstCoolFrames, waitFrames, unitsPerSecond, activeSeconds, out lag) == LagReason.Usable;
	}

	/// <summary>R74: <see cref="TryLag"/> with the reason instead of a bare false. The clauses and their
	/// order are identical (the first one that fires is the answer), so the two can never disagree.</summary>
	internal static LagReason Reject(int firstCoolFrames, int waitFrames, double unitsPerSecond,
		double activeSeconds, out double lag)
	{
		lag = 0.0;
		if (firstCoolFrames <= 0) return LagReason.NoFirstCool;
		if (waitFrames < 0 || waitFrames > firstCoolFrames) return LagReason.WaitOutOfRange;
		if (unitsPerSecond <= 1.0) return LagReason.BadUnits;
		if (double.IsNaN(activeSeconds) || activeSeconds < 0.0 || activeSeconds > WindowSeconds) return LagReason.OutOfWindow;
		lag = (firstCoolFrames - waitFrames) / unitsPerSecond - activeSeconds;
		if (double.IsNaN(lag) || lag < -MaxAheadSeconds || lag > MaxLagSeconds) return LagReason.LagOutOfRange;
		return LagReason.Usable;
	}

	/// <summary>
	/// The measurement: the MEDIAN of the usable samples (never the mean -- one slot whose `FirstCoolTime`
	/// was read after it fired would drag a mean far off, while a median ignores it), 0.0 when fewer than
	/// <see cref="MinSamples"/> samples survive. The samples arrive in slot order and are not re-ordered by
	/// the caller, so the median is taken here.
	/// </summary>
	internal static double Combine(IList<double> lags)
	{
		if (lags == null) return 0.0;
		var usable = new List<double>(lags.Count);
		for (int i = 0; i < lags.Count; i++)
		{
			double v = lags[i];
			if (double.IsNaN(v) || double.IsInfinity(v)) continue;
			if (v < -MaxAheadSeconds || v > MaxLagSeconds) continue;
			usable.Add(v);
		}
		if (usable.Count < MinSamples) return 0.0;
		double med = AutoSkillCadencePolicy.Median(usable.ToArray());
		return (med >= MinLagSeconds) ? med : 0.0;
	}

	/// <summary>
	/// Whether the shift may be applied NOW. Every clause is a way a plausible-looking rebase would be
	/// wrong: shifting after the first event was stamped (two origins in one battle), shifting outside the
	/// window (the calibrating slots are no longer on their first charge), or shifting by a value the bounds
	/// above do not accept.
	///
	/// R72: <paramref name="holdOverflowed"/> is the second way an event can already be on the old axis. The
	/// hold that keeps the battle's opening events out of the way (<see cref="ClockOriginHoldPolicy"/>) is
	/// bounded; once its cap is reached the events past it ARE published, so the shift must be refused for
	/// the same reason as <paramref name="eventsRecorded"/>.
	/// </summary>
	internal static bool ShouldRebase(double lag, double activeSeconds, int eventsRecorded, bool holdOverflowed)
	{
		if (holdOverflowed) return false;
		if (eventsRecorded != 0) return false;
		if (double.IsNaN(activeSeconds) || activeSeconds < 0.0 || activeSeconds > WindowSeconds) return false;
		if (double.IsNaN(lag)) return false;
		return lag >= MinLagSeconds && lag <= MaxLagSeconds;
	}
}
