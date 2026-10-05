using System;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// R64 (relabelled in R65): the auto-skill cadence arithmetic, exercised as production code.
	///
	/// Why this group exists. The round's question is "how often does the auto skill actually fire, and
	/// does that match the master row R63 published?". The measurement itself can only be taken in a live
	/// battle (`Diagnostics/AutoSkillProbe.cs` needs the IL2CPP surface and is not in this project's
	/// compile list), but the three conversions that turn the readings into the published numbers are
	/// pure, and they are where a wrong answer would hide:
	///
	///   * the charge rate, which is the ONLY way to check the 30-units-per-game-second rule against the
	///     auto skill itself rather than against an active skill's `CoolTimeFrame / CoolTime`;
	///   * the charge expressed in seconds -- R65's correction lives here: the master's numbers ARE the
	///     charge unit (measured verbatim-equal to the live `CoolTimeFrame` on 9 of 9 auto skills), so the
	///     seconds are a DIVISION by that rate, never a multiplication;
	///   * the interval between two activations, on one clock, tabulated as a median.
	/// </summary>
	public static void AutoSkillCadence(Runner r)
	{
		r.Group("policy/autoskill-cadence");

		// ---- the charge rate ----
		// The two real seconds of the sampler against the two clocks the game moves on. 90 units over
		// 2.0 s of the meter's clock is the measured 45/wall-second (timeScale 1.5); 60 units over 2.0 s
		// of game seconds is the 30/game-second the master's frame counts are built on.
		r.EqD("the-measured-wall-rate-is-reproduced", AutoSkillCadencePolicy.UnitsPerSecond(90, 2.0), 45.0);
		r.EqD("the-measured-game-rate-is-reproduced", AutoSkillCadencePolicy.UnitsPerSecond(60, 2.0), 30.0);
		r.EqD("a-frozen-counter-is-a-zero-rate", AutoSkillCadencePolicy.UnitsPerSecond(0, 2.0), 0.0);
		// A counter that went UP is a reset, and the sign is kept: clamping it into a small positive rate
		// would turn "the skill just refilled" into "it is almost ready".
		r.EqD("a-counter-that-ran-backwards-keeps-its-sign", AutoSkillCadencePolicy.UnitsPerSecond(-60, 2.0), -30.0);
		r.EqD("two-samples-in-one-instant-are-not-a-rate", AutoSkillCadencePolicy.UnitsPerSecond(90, 0.0), 0.0);
		r.EqD("a-negative-time-span-is-not-a-rate", AutoSkillCadencePolicy.UnitsPerSecond(90, -2.0), 0.0);
		r.EqD("a-nan-time-span-is-not-a-rate", AutoSkillCadencePolicy.UnitsPerSecond(90, double.NaN), 0.0);

		// ---- the whole charge, back in SECONDS ----
		// THE headline conversion, and the one R65 corrected: the master's numbers ARE the charge unit, so
		// the seconds come from a DIVISION by the measured rate. These six are the values a live battle
		// actually reported on 2026-10-06 (`Skill.CoolTimeFrame` per unit, and the master's own
		// `maxCoolTime`/`minCoolTime` was equal to each of them VERBATIM -- 9 of 9, none of them x30).
		r.EqD("the-measured-240-unit-charge-is-8-game-seconds", AutoSkillCadencePolicy.SecondsFor(240, 30.0), 8.0);
		r.EqD("the-measured-150-unit-charge-is-5-game-seconds", AutoSkillCadencePolicy.SecondsFor(150, 30.0), 5.0);
		r.EqD("the-measured-210-unit-charge-is-7-game-seconds", AutoSkillCadencePolicy.SecondsFor(210, 30.0), 7.0);
		r.EqD("the-measured-420-unit-charge-is-14-game-seconds", AutoSkillCadencePolicy.SecondsFor(420, 30.0), 14.0);
		r.EqD("the-measured-2970-unit-charge-is-99-game-seconds", AutoSkillCadencePolicy.SecondsFor(2970, 30.0), 99.0);
		// The same charge measured against the OTHER clock. The game clock runs 1.5x real time (measured:
		// 30 units per game second vs ~44.8 per real second), so the real duration is SHORTER -- which is
		// exactly the direction a "the master is in seconds, so it takes 240 seconds" reading gets wrong.
		r.EqD("the-same-charge-is-shorter-in-real-seconds", AutoSkillCadencePolicy.SecondsFor(240, 45.0), 16.0 / 3.0);
		r.EqD("a-zero-charge-is-not-a-zero-second-cooldown", AutoSkillCadencePolicy.SecondsFor(0, 30.0), 0.0);
		r.EqD("a-negative-charge-has-no-duration", AutoSkillCadencePolicy.SecondsFor(-1, 30.0), 0.0);
		r.EqD("a-zero-rate-has-no-duration", AutoSkillCadencePolicy.SecondsFor(240, 0.0), 0.0);
		r.EqD("a-negative-rate-has-no-duration", AutoSkillCadencePolicy.SecondsFor(240, -30.0), 0.0);
		r.EqD("a-nan-rate-has-no-duration", AutoSkillCadencePolicy.SecondsFor(240, double.NaN), 0.0);

		// ---- the interval between two activations ----
		r.EqD("consecutive-activations-give-their-difference", AutoSkillCadencePolicy.IntervalSeconds(10.0, 26.0), 16.0);
		r.EqD("the-same-moment-twice-is-not-an-interval", AutoSkillCadencePolicy.IntervalSeconds(10.0, 10.0), 0.0);
		r.EqD("an-out-of-order-pair-is-not-an-interval", AutoSkillCadencePolicy.IntervalSeconds(10.0, 4.0), 0.0);
		r.EqD("a-nan-previous-is-not-an-interval", AutoSkillCadencePolicy.IntervalSeconds(double.NaN, 26.0), 0.0);

		// ---- the median ----
		r.EqD("no-intervals-have-no-median", AutoSkillCadencePolicy.Median(new double[0]), 0.0);
		r.EqD("a-null-array-has-no-median", AutoSkillCadencePolicy.Median(null), 0.0);
		r.EqD("one-interval-is-its-own-median", AutoSkillCadencePolicy.Median(new double[] { 16.0 }), 16.0);
		r.EqD("an-odd-count-takes-the-middle", AutoSkillCadencePolicy.Median(new double[] { 18.0, 14.0, 16.0 }), 16.0);
		r.EqD("an-even-count-averages-the-two-middles", AutoSkillCadencePolicy.Median(new double[] { 20.0, 10.0 }), 15.0);
		// A 0 is what IntervalSeconds returns for a FAILED comparison, so a 0 in the history must not be
		// allowed to pull the median onto that same value: it is a duplicate record, not a cadence.
		r.EqD("a-duplicate-record-does-not-drag-the-median", AutoSkillCadencePolicy.Median(new double[] { 0.0, 10.0, 20.0 }), 15.0);
		r.EqD("negatives-are-dropped-like-duplicates", AutoSkillCadencePolicy.Median(new double[] { -1.0, 8.0 }), 8.0);
		r.EqD("only-unusable-entries-have-no-median", AutoSkillCadencePolicy.Median(new double[] { 0.0, -3.0, double.NaN }), 0.0);

		// The history belongs to the caller: a median that sorted in place would silently reorder the
		// probe's own interval list, which the next activation then reads.
		double[] keep = new double[] { 18.0, 14.0, 16.0 };
		AutoSkillCadencePolicy.Median(keep);
		r.True("the-median-does-not-reorder-the-callers-array",
		       keep[0] == 18.0 && keep[1] == 14.0 && keep[2] == 16.0);
	}
}
