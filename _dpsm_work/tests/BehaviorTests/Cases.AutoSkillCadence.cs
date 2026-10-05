using System;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// R64: the auto-skill cadence arithmetic, exercised as production code.
	///
	/// Why this group exists. The round's question is "how often does the auto skill actually fire, and
	/// does that match the master row R63 published?". The measurement itself can only be taken in a live
	/// battle (`Diagnostics/AutoSkillProbe.cs` needs the IL2CPP surface and is not in this project's
	/// compile list), but the three conversions that turn the readings into the published numbers are
	/// pure, and they are where a wrong answer would hide:
	///
	///   * the charge rate, which is the ONLY way to check the 30-units-per-game-second rule against the
	///     auto skill itself rather than against an active skill's `CoolTimeFrame / CoolTime`;
	///   * the charge expressed in seconds, which is the number that has to be compared with the
	///     master's `minCoolTime`/`maxCoolTime` -- printing 7200 next to a 240 is exactly the mistake
	///     R63's unit rule exists to prevent;
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

		// ---- the whole charge, back in the master's own unit ----
		// THE headline conversion: 暗沌への導き's dumped frame counts must come back as the master's own
		// seconds, and the two clocks must give the two different real-world answers.
		r.EqD("the-9000-frame-charge-is-the-masters-300s", AutoSkillCadencePolicy.SecondsFor(9000, 30.0), 300.0);
		r.EqD("the-7200-frame-charge-is-the-masters-240s", AutoSkillCadencePolicy.SecondsFor(7200, 30.0), 240.0);
		r.EqD("the-same-charge-is-160-real-seconds-at-the-wall-rate", AutoSkillCadencePolicy.SecondsFor(7200, 45.0), 160.0);
		r.EqD("the-same-charge-is-200-real-seconds-at-the-wall-rate", AutoSkillCadencePolicy.SecondsFor(9000, 45.0), 200.0);
		r.EqD("a-zero-charge-is-not-a-zero-second-cooldown", AutoSkillCadencePolicy.SecondsFor(0, 30.0), 0.0);
		r.EqD("a-negative-charge-has-no-duration", AutoSkillCadencePolicy.SecondsFor(-1, 30.0), 0.0);
		r.EqD("a-zero-rate-has-no-duration", AutoSkillCadencePolicy.SecondsFor(9000, 0.0), 0.0);
		r.EqD("a-negative-rate-has-no-duration", AutoSkillCadencePolicy.SecondsFor(9000, -30.0), 0.0);
		r.EqD("a-nan-rate-has-no-duration", AutoSkillCadencePolicy.SecondsFor(9000, double.NaN), 0.0);

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
