using System;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// RF3c: the composition chain's own tolerances. They decide whether two readings count as the same one,
	/// so a silent edit changes the plugin's ANSWER, not just its display -- and until this round 0.005 was a
	/// bare literal in five places.
	/// </summary>
	public static void CompositionTolerance(Runner r)
	{
		r.Group("policy/composition-tolerance");
		r.EqD("tight-rate-is-5-permille", CompositionTolerancePolicy.TightRate, 0.005);
		r.EqD("loose-rate-is-2-percent", CompositionTolerancePolicy.LooseRate, 0.02);
		r.EqD("the-linearity-window-is-5-percent", CompositionTolerancePolicy.LinearWindow, 0.05);
		r.EqD("the-crit-lower-bound-is-minus-5-percent", CompositionTolerancePolicy.CritDeltaMin, -0.05);
		// The upper bound is deliberately NOT a new number: it is the clock's own frame-delta bound.
		r.EqD("the-crit-upper-bound-is-the-clocks-frame-delta",
		       CompositionTolerancePolicy.CritDeltaMax, BattleClockPolicy.MaxFrameDelta);
		r.EqD("and-that-bound-is-025s", CompositionTolerancePolicy.CritDeltaMax, 0.25);
		// Structural: the pass that decides the ANSWER must be tighter than the one that only picks what to
		// DISPLAY, and the linearity window is looser than both (it classifies a ratio, not a reading).
		r.True("the-truth-pass-is-tighter-than-the-display-pass",
		       CompositionTolerancePolicy.TightRate < CompositionTolerancePolicy.LooseRate);
		r.True("the-linearity-window-is-looser-than-the-display-pass",
		       CompositionTolerancePolicy.LooseRate < CompositionTolerancePolicy.LinearWindow);

		r.Group("policy/composition-crit-window");
		r.True("a-fresh-observation-is-usable", CompositionTolerancePolicy.CritDeltaUsable(0.0));
		r.True("a-slightly-early-observation-is-usable", CompositionTolerancePolicy.CritDeltaUsable(-0.04));
		r.True("exactly-at-the-minimum-is-usable", CompositionTolerancePolicy.CritDeltaUsable(-0.05));
		r.True("one-ulp-past-the-minimum-is-not",
		       !CompositionTolerancePolicy.CritDeltaUsable(-0.05 - 1e-9));
		r.True("exactly-at-the-maximum-is-usable", CompositionTolerancePolicy.CritDeltaUsable(0.25));
		r.True("one-ulp-past-the-maximum-is-not",
		       !CompositionTolerancePolicy.CritDeltaUsable(Math.BitIncrement(0.25)));
		r.True("a-way-too-late-observation-is-not", !CompositionTolerancePolicy.CritDeltaUsable(0.5));
		// A NaN dt is ACCEPTED -- that is what the original comparison did, pinned rather than "fixed".
		r.True("a-NaN-delta-is-accepted-as-before", CompositionTolerancePolicy.CritDeltaUsable(double.NaN));
	}
}
