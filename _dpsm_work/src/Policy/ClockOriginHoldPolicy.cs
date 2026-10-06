using System;

namespace DpsMeter;

/// <summary>
/// R72: the rule that makes R71's origin shift ACTUALLY LAND — hold the battle's first damage/heal events
/// until the clock's origin is decided, and move the few stamps written outside that path by hand.
///
/// THE DEFECT (measured 2026-10-06, the user's report "发动时间还是比初动早,完全没有改变"). R71 computed the
/// right lag (+0.90 s) and refused to apply it, because its guard required `Rt.EventCount == 0` and the
/// alignment was only attempted from the session's SECOND frame on: the game stamps its opening damage in
/// the very same frame the session is created. The log of two consecutive battles says exactly that:
///     [CLOCK] origin=none reason=events hits=5   (quest 9999,  refused 0.121 s after the session start)
///     [CLOCK] origin=none reason=events hits=1   (quest 411001, refused 0.029 s after the session start)
/// while the auto-skill counters at those very instants were already valid and all said 0.90-0.93 s
/// (quest 411001, active=0.03 s: 夢幻の連鎖破裂 wait=92/180 with FirstCoolTime=120 -> 0.903 s;
///  不浄な体液の噴出 212/300 with FirstCoolTime=240 -> 0.903 s; 爆ぜる燭光 122/210 with 150 -> 0.903 s;
///  影爪の強制裁断 92/390 with 120 -> 0.903 s; 実験失敗！ 2942/2970 -> 0.903 s).
/// So the evidence was there and the ORDER was wrong: an event had already been published on the old axis.
///
/// THE RULE. While the origin is undecided the damage/heal events are not aggregated at all: they are held
/// (bounded, counted) and REPLAYED once the decision lands, with the battle clock temporarily set to the
/// instant they arrived plus the decided lag. That is exact for every derived value -- `events[].t`, the
/// actors' first/last hit times, the per-second buckets, the damage-history samples, the pending-hit
/// matching and the reaction windows -- because the recorder is re-entered with the corrected clock instead
/// of its output being edited afterwards. The held window is the time it takes the probe to see the party,
/// measured as one to four frames (0.03-0.13 s), so the overlay's "nothing happened yet" state is not
/// observable; what IS observable is the cap: if the battle somehow produces more than
/// <see cref="MaxHeld"/> events before the decision, holding stops, the overflow is reported and the shift
/// is REFUSED (two origins in one battle is worse than a late one).
///
/// WHY A POLICY. Every number here decides whether a whole battle's axis moves: whether an event may be
/// held at all, what clock a held event must be replayed at, which second-bucket a stamp belongs to, and
/// how far the stamps written outside the recorder move. All of it is arithmetic, so it is executable in
/// the behaviour suite instead of being read.
/// </summary>
internal static class ClockOriginHoldPolicy
{
	/// <summary>How many events one battle may hold. The observed opening bursts are 1 and 5 events in
	/// 0.03-0.13 s (~40 hits/s), so the window this bounds is ~6 s of the densest observed opening: far
	/// above any real case, and small enough that the buffer cannot grow with the battle.</summary>
	internal const int MaxHeld = 256;

	/// <summary>
	/// Whether an arriving damage/heal event must be held instead of aggregated. False once the origin is
	/// decided (the normal case: the decision is attempted when the session is created, so nothing is ever
	/// held) and false once the cap is reached (see <see cref="Overflowed"/>: the caller must then refuse
	/// the shift, because events would otherwise exist on both axes).
	/// </summary>
	internal static bool ShouldHold(bool originDecided, int heldCount)
	{
		if (originDecided) return false;
		if (heldCount < 0) return false;
		return heldCount < MaxHeld;
	}

	/// <summary>The cap was reached while the origin was still undecided: the battle can no longer be held
	/// as one unit, so it must not be shifted at all.</summary>
	internal static bool Overflowed(bool originDecided, int heldCount)
	{
		if (originDecided) return false;
		return heldCount >= MaxHeld;
	}

	/// <summary>
	/// The battle clock a held event must be replayed at: the clock it ARRIVED at, moved onto the corrected
	/// axis. Arrival and not "now": the held window is real time the battle spent, so replaying at `now`
	/// would silently move every held hit later by the length of the window.
	///
	/// A NaN or negative arrival (an unreadable clock) falls back to 0 + lag, and a NaN/negative lag means
	/// "no shift", so a bad value can never move a stamp into the future.
	/// </summary>
	internal static double ReplayActive(double arrivalSeconds, double lag)
	{
		if (double.IsNaN(arrivalSeconds) || arrivalSeconds < 0.0) arrivalSeconds = 0.0;
		if (double.IsNaN(lag) || lag < 0.0) lag = 0.0;
		return arrivalSeconds + lag;
	}

	/// <summary>
	/// THE per-second bucketing rule (`index = floor(seconds)`, implemented as the truncation every caller
	/// used inline). One definition, because the damage, taken, heal, team and HP series must agree: a
	/// second-by-second chart whose series disagree by one index looks like a damage spike that never
	/// happened. -1 = not a bucket (an unreadable/negative clock, or a value beyond any battle).
	/// </summary>
	internal static int SecondIndex(double seconds)
	{
		if (double.IsNaN(seconds) || seconds < 0.0) return -1;
		if (seconds >= 36000.0) return -1;
		return (int)seconds;
	}

	/// <summary>
	/// Move a stamp that was written BEFORE the origin was decided onto the corrected axis. Used only for
	/// the surfaces that are not fed by the held recorder (the page's activation rows, the probes'
	/// bookkeeping, the pending damage figures, the attack snapshot): a stamp taken on the old axis plus the
	/// decided lag IS its corrected value, because the lag is a constant offset.
	///
	/// A non-positive/NaN lag is "no shift" (the refusal path), and a NaN stamp stays NaN rather than
	/// becoming a plausible number.
	/// </summary>
	internal static double Shift(double stamp, double lag)
	{
		if (double.IsNaN(stamp)) return stamp;
		if (double.IsNaN(lag) || lag <= 0.0) return stamp;
		return stamp + lag;
	}
}
