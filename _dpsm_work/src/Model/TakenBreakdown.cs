using System.Collections.Generic;

namespace DpsMeter;

/// <summary>
/// R79 受击来源拆分: what ONE unit's incoming damage was made of. Projected on demand from
/// <see cref="BattleSession.Events"/> -- no new Harmony hook, no extra live read except the unit's
/// position (vanguard / rearguard), which only exists while the game object is alive.
///
/// BASIS: the game's own figure. <see cref="TakenActor.Nominal"/> sums <see cref="BattleEvent.Nominal"/>
/// (the ARGUMENT the game accounted for: CharacterStatistics.TakenDamage) and
/// <see cref="TakenActor.Taken"/> sums <see cref="BattleEvent.Amount"/> (the amount this plugin
/// published). R78: on an overflow call the publishable amount is the call's RETURN, measured to be the
/// part of the hit that did NOT fit into the victim's remaining Life, so
/// <c>Nominal = Taken + Residual</c> and Residual is 超出剩余耐久 -- it is NOT an absorption.
///
/// Every dimension partitions the same nominal total, so a bucket sum is checkable in one place:
///   Nominal == Σ BySource == Σ ByHitType == Σ ByEffect
///   Nominal == Σ ByAttacker + Friendly + Unknown
/// </summary>
public sealed class TakenBreakdown
{
	/// <summary>How the section was built (exported verbatim).</summary>
	public const string Method = "by-event/1";

	/// <summary>Which figure the amounts are: the game's own accounted damage, not the published one.</summary>
	public const string Basis = "nominal";

	/// <summary>Victims, biggest first (nominal desc, then key asc: deterministic for the export diff).</summary>
	public List<TakenActor> Actors = new List<TakenActor>();

	public int Hits;

	/// <summary>Σ per-actor Nominal. The game's 口径 for everything in this section.</summary>
	public long Nominal;

	/// <summary>Σ per-actor Taken. What the plugin published (game 口径 = this + Residual).</summary>
	public long Taken;

	/// <summary>Σ per-actor Residual: 超出剩余耐久, never 吸收.</summary>
	public long Residual;

	/// <summary>Hits whose attacker could not be resolved at all; reconciles with Session.UnattributedDamage.</summary>
	public long Unknown;

	public int UnknownHits;

	/// <summary>Damage the victim took from its OWN team (friendly fire / 回復反転); never an enemy source.</summary>
	public long Friendly;

	public int FriendlyHits;
}

/// <summary>One victim's incoming damage, split by five independent dimensions.</summary>
public sealed class TakenActor
{
	public int Key;
	public string Name = "";
	public int Team;

	/// <summary>Team 1 (the plugin's "our side"). Read from the event, never from the name.</summary>
	public bool Ally;

	/// <summary>0 = not read out (the live object was gone), 1 = vanguard (前衛), 2 = rearguard (後衛).
	/// Snapshotted on the victim's FIRST damage event: the game can move a unit mid-battle, and a
	/// later read would relabel every earlier hit.</summary>
	public int Position;

	public int Hits;

	/// <summary>Σ BattleEvent.Nominal (game 口径).</summary>
	public long Nominal;

	/// <summary>Σ BattleEvent.Amount (what the plugin published).</summary>
	public long Taken;

	/// <summary>Nominal - Taken = 超出剩余耐久 (R78 wording; NOT 被吸收).</summary>
	public long Residual;

	public long Friendly;
	public int FriendlyHits;

	/// <summary>Hits with no resolvable attacker (also counted in Taken/Nominal).</summary>
	public long Unknown;

	public int UnknownHits;

	public List<TakenBucket> BySource = new List<TakenBucket>();
	public List<TakenBucket> ByHitType = new List<TakenBucket>();
	public List<TakenBucket> ByAttacker = new List<TakenBucket>();
	public List<TakenBucket> ByEffect = new List<TakenBucket>();
	public List<TakenStatus> ByStatus = new List<TakenStatus>();
}

/// <summary>One bucket of one dimension. Amounts are nominal, hits are hit counts.</summary>
public sealed class TakenBucket
{
	/// <summary>Dimension value: DamageSource / eDamageCalcType / actor key / effect id. int.MinValue = folded.</summary>
	public int Key;

	/// <summary>Label. For the attacker dimension it is the display name the event carried.</summary>
	public string Name = "";

	public long Amount;
	public int Hits;

	/// <summary>How many of <see cref="Hits"/> were labelled from an authoritative record. Bookkeeping for
	/// <see cref="Quality"/>; not exported on its own.</summary>
	public int AuthoritativeHits;

	/// <summary>"" when every hit in the bucket was authoritative, else "近似 a/h". A best-effort label
	/// must never read as a measurement (same rule as BattleEvent.HitMatch).</summary>
	public string Quality = "";
}

/// <summary>An ailment a hit inflicted, and who the GAME credits for it (not necessarily the hit's
/// attacker: for damage over time the nearest damage record is only the carrier).</summary>
public sealed class TakenStatus
{
	public string Status = "";
	public string Applier = "";
	public long Amount;
	public int Hits;
}

/// <summary>Input row of the projection: one damage event, flattened so the policy stays pure.</summary>
public struct TakenHit
{
	public double T;
	public int VictimKey;
	public string Victim;
	public int VictimTeam;

	/// <summary>0 = the plugin could not resolve any attacker for this hit.</summary>
	public int AttackerKey;

	public string Attacker;
	public string Attr;
	public bool Friendly;

	/// <summary>DamageSource enum value (0 = unknown).</summary>
	public int Source;

	/// <summary>eDamageCalcType value; -1 = unreadable (the damage path's sentinel).</summary>
	public int HitType;

	/// <summary>The calc's m_effectId; 0 = the calc carried none / no record matched.</summary>
	public int EffectId;

	/// <summary>0 none / 1 value-exact / 2 attacker+target only / 3 paired but rejected.</summary>
	public int HitMatch;

	public long Amount;
	public long Nominal;
	public string Status;
	public string StatusApplier;

	/// <summary>Victim's position snapshot (0 unknown, 1 vanguard, 2 rearguard).</summary>
	public int Position;
}
