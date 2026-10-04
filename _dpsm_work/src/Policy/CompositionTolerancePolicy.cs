namespace DpsMeter;

/// <summary>
/// RF3c (plan sections 7-8, rows R1/R2): the composition chain's own tolerances, NAMED.
///
/// Why. The plan lists two leftovers after RF3: "时间窗提为命名常量" (R1) and "composition 链自身窗口" (R2).
/// The attribution windows and the clock constants were named in RF3; what remained were the five numbers
/// the CHAIN uses while it decides whether two readings are the same one. They decide whether a hit is
/// explained or counted as a residual, so a silent edit of one of them is a silent change of the plugin's
/// answer -- and until now 0.005 appeared five times as a bare literal.
///
/// The names say what the number buys, not what it is:
///   * TightRate -- the chain accepts a candidate power as the same reading within 5 permille;
///   * LooseRate -- the second pass widens that to 2%, used only to pick a DISPLAY source;
///   * LinearWindow -- a scaled ratio this close to an integer counts as linear (no residual to explain);
///   * CritDeltaMin/Max -- how far a crit observation may sit from the calc's clock stamp.
///
/// CritDeltaMax is deliberately NOT a new number: it is <see cref="BattleClockPolicy.MaxFrameDelta"/>, the
/// same 0.25 s the clock uses to reject a bogus frame delta. Both mean "this reading is too far from the
/// hit to belong to it", and they are now written once.
/// </summary>
internal static class CompositionTolerancePolicy
{
	/// <summary>A candidate power within this relative distance of the dominant reading is the same
	/// reading. 5 permille.</summary>
	public const double TightRate = 0.005;

	/// <summary>The display-only second pass: "which of the candidate readings do we SHOW as the attack",
	/// allowed to be looser because it never changes a number. 2%.</summary>
	public const double LooseRate = 0.02;

	/// <summary>A scaled ratio within this distance of an integer is treated as linear (an exact multiple,
	/// so there is no residual left to explain).</summary>
	public const double LinearWindow = 0.05;

	/// <summary>A crit observation may be this far BEHIND the calc's stamp (the hook can fire a hair
	/// before the stamp is taken).</summary>
	public const double CritDeltaMin = -0.05;

	/// <summary>... and this far AHEAD of it -- the clock's own frame-delta bound, not a second number.</summary>
	public static double CritDeltaMax { get { return BattleClockPolicy.MaxFrameDelta; } }

	/// <summary>
	/// Is a crit observation this far from the calc's stamp usable? Both ends are INCLUSIVE (measured: a
	/// dt of exactly -0.05 or exactly 0.25 is accepted, the first value past either end is not). A NaN dt
	/// is accepted, which is what the original comparison did -- kept, and pinned, rather than "fixed"
	/// here.
	/// </summary>
	public static bool CritDeltaUsable(double dt)
	{
		return !(dt < CritDeltaMin || dt > CritDeltaMax);
	}
}
