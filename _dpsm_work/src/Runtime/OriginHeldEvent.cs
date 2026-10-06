namespace DpsMeter;

/// <summary>
/// R72: one damage/heal event that arrived before the battle clock's ORIGIN was decided.
///
/// The recorder's ARGUMENTS, not its output: the event is re-aggregated later (see
/// <see cref="ClockOriginHoldPolicy"/>) with the battle clock set back to <see cref="ArrivalSeconds"/> plus
/// the decided lag, so everything the event produces -- its export stamp, the actor totals, the per-second
/// bucket, the damage curve, the pending-figure match -- is written once, on the corrected axis. The
/// alternative (aggregating immediately and editing the results) would have to reach every derived surface,
/// and a missed one is a silently mixed axis.
///
/// <see cref="Victim"/>/<see cref="Actor"/> mirror the recorder signatures: damage = victim + attacker,
/// heal = target + healer. <see cref="Owner"/> is the damage path's owner fallback (null for a heal).
/// The native object references are held only for the length of the hold window (one to four frames) of the
/// same battle, so they cannot outlive their battle the way a cross-session snapshot would.
/// </summary>
internal sealed class OriginHeldEvent
{
	/// <summary>True = heal (RecordHeal), false = damage (RecordDamage).</summary>
	public bool Heal;

	/// <summary>Damage: the victim. Heal: the target.</summary>
	public BattleObject Victim;

	/// <summary>Damage: the attacker. Heal: the healer.</summary>
	public BattleObject Actor;

	/// <summary>Damage only: the owner fallback when the attacker is unknown.</summary>
	public BattleObject Owner;

	/// <summary>Damage: what reached 耐久. Heal: the actual heal.</summary>
	public int Amount;

	/// <summary>Damage: the game's own figure (nominal &gt; amount means part was absorbed). Heal: nominal.</summary>
	public int Nominal;

	/// <summary>The battle clock when the event ARRIVED (old axis). The replay uses
	/// <see cref="ClockOriginHoldPolicy.ReplayActive"/>, never "now".</summary>
	public double ArrivalSeconds;
}
