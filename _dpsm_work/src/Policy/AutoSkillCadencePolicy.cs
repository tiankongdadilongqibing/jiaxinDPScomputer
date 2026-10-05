using System;

namespace DpsMeter;

/// <summary>
/// PROBE (R64): the arithmetic of an auto skill's CHARGE and of the interval between its activations.
///
/// WHY THIS EXISTS
/// R63 published the auto-skill master table (`AutoSkillMasterData`: `minCoolTime`/`maxCoolTime` in
/// SECONDS and the same numbers multiplied by 30 as FRAMES) but could not say whether those numbers
/// drive anything the player sees: the earlier reverse-inferred cadence (about 13.5 s, taken from a
/// damage channel) does not match a 240-300 s master cooldown, and the `auto_skill` table's id space is
/// not the damage-calc effect-id space, so the two could not be reconciled from the export alone.
/// `Diagnostics/AutoSkillProbe.cs` answers it by reading the LIVE `Skill` instance every battle; this
/// file holds the arithmetic that turns those readings into the published numbers, so the numbers can
/// be executed by `tests/BehaviorTests` instead of being argued about (the same split as
/// <see cref="SkillCooldownPolicy"/>).
///
/// THE THREE QUANTITIES, AND WHY EACH IS A FUNCTION HERE
///   * <see cref="UnitsPerSecond"/> -- how fast the charge counter drains. The probe prints this so a
///     reader can check the "30 units per game second" rule against the auto skill itself instead of
///     against an active skill's `CoolTimeFrame / CoolTime`.
///   * <see cref="SecondsFor"/> -- the whole charge expressed in seconds. This is the number that must
///     be compared with the master's `minCoolTime`/`maxCoolTime`; printing the raw frame count is how
///     a 7200 gets compared with a 240.
///   * <see cref="IntervalSeconds"/> + <see cref="Median"/> -- the measured cadence, from consecutive
///     activation moments on ONE clock.
///
/// An unusable input returns 0 rather than a plausible number: 0 means "not measurable", and every
/// caller prints the raw readings beside it so nothing is lost.
/// </summary>
public static class AutoSkillCadencePolicy
{
	/// <summary>
	/// Charge units consumed per second, from two samples of the game's own counter.
	///
	/// 0 when the sample cannot support a rate: a non-positive or NaN time span (two samples in the
	/// same frame), an unread counter, or a non-finite quotient. A NEGATIVE result is returned as-is --
	/// the counter ran BACKWARDS, which is a real observation (the skill reset its wait) and must not be
	/// silently clamped into a small positive rate.
	/// </summary>
	public static double UnitsPerSecond(int deltaUnits, double deltaSeconds)
	{
		if (double.IsNaN(deltaSeconds)) return 0.0;
		if (deltaSeconds <= 0.0) return 0.0;
		double v = deltaUnits / deltaSeconds;
		if (double.IsNaN(v) || double.IsInfinity(v)) return 0.0;
		return v;
	}

	/// <summary>
	/// How many seconds a charge of `units` takes at the measured rate, i.e. the master's own unit.
	///
	/// 0 for a non-positive charge or rate and for a non-finite quotient -- "not derivable". A charge of
	/// 0 units is not a 0-second cooldown, it is a skill with nothing to charge.
	/// </summary>
	public static double SecondsFor(int units, double unitsPerSecond)
	{
		if (units <= 0 || unitsPerSecond <= 0.0 || double.IsNaN(unitsPerSecond)) return 0.0;
		double v = units / unitsPerSecond;
		if (double.IsNaN(v) || double.IsInfinity(v)) return 0.0;
		return v;
	}

	/// <summary>
	/// Seconds between two consecutive activations read from ONE clock. A non-positive difference --
	/// out-of-order samples, or the same moment twice -- is reported as 0, which is what makes a
	/// duplicate record distinguishable from a real same-frame cadence: 0 is not an interval.
	/// </summary>
	public static double IntervalSeconds(double previous, double now)
	{
		if (double.IsNaN(previous) || double.IsNaN(now)) return 0.0;
		double d = now - previous;
		return (d > 0.0) ? d : 0.0;
	}

	/// <summary>
	/// Median of the supplied intervals, or 0 when there is none. Non-positive and NaN entries are
	/// dropped first: a 0 would drag the median onto the "no interval" value that
	/// <see cref="IntervalSeconds"/> uses for a failed comparison, which is a different statement.
	/// The input array is not modified (the caller keeps its own history).
	/// </summary>
	public static double Median(double[] values)
	{
		if (values == null || values.Length == 0) return 0.0;
		double[] copy = new double[values.Length];
		int n = 0;
		for (int i = 0; i < values.Length; i++)
		{
			double v = values[i];
			if (double.IsNaN(v) || v <= 0.0) continue;
			copy[n++] = v;
		}
		if (n == 0) return 0.0;
		Array.Sort(copy, 0, n);
		int mid = n / 2;
		if ((n & 1) == 1) return copy[mid];
		return (copy[mid - 1] + copy[mid]) / 2.0;
	}
}
