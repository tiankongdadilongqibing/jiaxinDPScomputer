namespace DpsMeter;

/// <summary>
/// RF4g (plan section 9, third family): the ATTACK SNAPSHOT -- the damage calculation whose attacker the
/// composition chain is allowed to use as the live source for a hit.
///
/// Two fields that must move together, and a sentinel that says so:
///   * Calc -- the calculation the last hook reported (null when there is none);
///   * At   -- the battle-clock stamp taken at the same moment, -1 when there is none.
/// <see cref="Valid"/> requires BOTH. That is not decoration: the 1.5.0 defect this class was extracted
/// around was a calc from the PREVIOUS battle labelling this battle's hits, because the reference survived
/// while nothing re-stamped it. A reader that checks only "Calc != null" re-opens that defect.
///
/// The container owns set/clear/stamp. It does NOT own eligibility: the age window (LivePairMinAge/MaxAge)
/// and the attacker-same check are AttributionPolicy's and the caller's -- so this class needs no clock of
/// its own, which is what makes it testable without a game.
/// </summary>
internal sealed class AttackSnapshot<TCalc> where TCalc : class
{
	public TCalc Calc;

	/// <summary>Battle-clock seconds when <see cref="Begin"/> ran; -1 means "never begun".</summary>
	public double At = -1.0;

	/// <summary>Usable as a live source: a calculation AND a real stamp.</summary>
	public bool Valid { get { return Calc != null && At >= 0.0; } }

	/// <summary>Remember a calculation and the clock it belongs to, in one step.</summary>
	public void Begin(TCalc calc, double at)
	{
		Calc = calc;
		At = at;
	}

	/// <summary>Forget both halves. Called when a session starts and when one is resumed -- the two points
	/// the matrix records -- so a new battle can never inherit the previous one's reference.</summary>
	public void Clear()
	{
		Calc = null;
		At = -1.0;
	}

	/// <summary>How long ago the snapshot was taken. Only meaningful when <see cref="Valid"/>.</summary>
	public double Age(double now)
	{
		return now - At;
	}
}
