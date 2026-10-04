namespace DpsMeter;

/// <summary>
/// 1.5.0 (架构审视 A2): one damage figure the game's DamageCalculater produced, waiting to be matched to
/// the damage record it turns into.
///
/// Before 1.5.0 this class had NO producer at all: `Aggregator.RecordHitDetail` was the only definition
/// and nothing called it, so `Session.PendingHits` stayed empty, `ConsumePending` always returned null,
/// and three exported fields were constants for the whole corpus -- measured over all 768 exports /
/// 570,078 events: `crit` was false in 570,078 of 570,078, damage `source` was 0 (Unknown) in 510,735 of
/// 510,735, and every `actors[].crit` / `actors[].sources` was empty. (Healing events DO carry
/// `source=8` DirectHeal, but that is written directly by `RecordHeal`, not through this channel -- so
/// the field was always writable; the damage path simply never fed it.)
/// </summary>
public sealed class HitRecord
{
	public BattleObject Attacker;

	public BattleObject Blocker;

	public int Damage;

	public DamageSource Source;

	public eDamageCalcType HitType;

	public int EffectId;

	/// <summary>
	/// 1.5.0 (A3): the game's OWN crit flag as observed through
	/// `DamageCalculater.GetFlyTextNumberSizeForAttack`, matched to this target by the same
	/// target+time window the composition line uses.
	/// 0 = not observed, 1 = observed NOT a crit, 2 = observed crit.
	///
	/// A tri-state rather than a bool because "we did not see the flag" and "the game said no" must not
	/// look alike -- they are the two halves of the crit question and only one of them is evidence.
	/// </summary>
	public byte CritObserved;

	/// <summary>Session time (seconds) when this record was produced. Used to bound the pairing window,
	/// so a stale record can never be consumed by a much later hit.</summary>
	public double T;
}
