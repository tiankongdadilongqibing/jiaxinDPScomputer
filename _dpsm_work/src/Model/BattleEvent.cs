using System.Collections.Generic;

namespace DpsMeter;

public sealed class BattleEvent
{
	public double T;          // battle second (game-clock derived)
	public string Type;       // "dmg" | "heal"
	public string Victim;     // display name
	public string Attacker;   // display name (may be "?")
	public string Owner;      // display name (may be "?")
	public string Attr;       // attribution mode: A / O / C:calcA / C:calcO / ?
	public long Amount;       // damage or actual healing
	public long Nominal;      // healing nominal (0 for damage)
	public int Source;        // DamageSource enum value (0 if unknown)
	public bool Crit;         // best-effort crit flag; equal to (CritObserved == 2)

	/// <summary>
	/// 1.5.0 (A2/A3): the GAME's own crit flag for this hit, observed through
	/// `DamageCalculater.GetFlyTextNumberSizeForAttack`: 0 = not observed, 1 = observed NOT a crit,
	/// 2 = observed crit.
	///
	/// Wire-up history matters here: before 1.5.0 this signal was captured (CompositionProbe.NoteCrit)
	/// but only used for display, and the per-hit `crit` field was a constant `false` in all 570,078
	/// events of the 768-export corpus because the channel that was supposed to feed it had no producer.
	/// The tri-state is deliberate: "the game said no" and "we never saw the flag" must not look alike.
	/// </summary>
	public byte CritObserved;

	/// <summary>
	/// 1.5.0 (A2): how the damage-detail record behind <see cref="Source"/>/<see cref="CritObserved"/> was
	/// matched -- 0 = none, 1 = same attacker+target+damage value (authoritative), 2 = attacker+target only
	/// (best effort). Exported so a best-effort label is never read as a measurement.
	/// </summary>
	public int HitMatch;

	/// <summary>
	/// 1.5.2: the figure the MATCHED damage-detail record carried, or 0 when no record was matched.
	///
	/// WHY IT IS EXPORTED. `hitMatch` is a verdict, and until this field existed the verdict could not be
	/// audited: MEASURED 2026-10-03 (battle_...175142) 5181 of 5220 records were labelled best-effort (2),
	/// i.e. the produced figure matched NEITHER `amount` NOR `nominal` on 96% of hits -- yet for 3270 of
	/// those very hits the independently-derived `calc.theory` equalled `amount` exactly. So the mismatch
	/// is an accounting difference between what the game's calc RETURNS and what it RECORDS, not a pairing
	/// failure -- and 1.5.1's attempt to fix it by also accepting `nominal` was aimed at the wrong quantity
	/// because the quantity being compared was never visible. With this field the next battle measures the
	/// relation directly instead of inferring it.
	/// </summary>
	public long HitValue;

	/// <summary>1.5.0 (A2): the calc's own hit type for this hit (`eDamageCalcType`), -1 unreadable.
	/// Previously reachable only through `calc.hitType`, which exists only when a composition was built.</summary>
	public int CalcHitType;

	/// <summary>1.5.0 (A2): the calc's `m_effectId` for this hit, 0 when the calc carries none.
	/// Read from the damage-detail record, so it also covers hits the composition could not pair.</summary>
	public int CalcEffectId;

	/// <summary>
	/// True when the game itself classified this calc as a heal (eDamageCalcType.Heal).
	/// This is a first-class signal, unlike guessing from the damage-source string.
	/// </summary>
	public bool HealCalc;

	/// <summary>TeamType of the attacker / victim (1 = our side, 2 = enemy, 0 = unknown).
	/// Needed because this content can field the same character NAME on both sides.</summary>
	public int AttackerTeam;
	public int VictimTeam;

	/// <summary>
	/// 1.5.0 (A4): stable per-session actor keys (see <see cref="ActorStats.Key"/>), 0 = unknown.
	///
	/// Why they exist: the display names flanking them are NOT unique -- this content can field the same
	/// character name on both sides (which is why AttackerTeam/VictimTeam exist) and can field same-kind
	/// summons or mirror tokens inside ONE team. Every join from an event to the roster / ability table /
	/// live resistance reading had to key on (name, team) and silently mixed those up. Joining on the key
	/// is exact, and `actors[].key` carries the other end.
	/// </summary>
	public int AttackerKey;
	public int VictimKey;

	/// <summary>
	/// Damage dealt to the attacker's own team. Not output: this is friendly fire such as
	/// 回復反転 (heal reversal) or a self-damage skill.
	/// </summary>
	public bool Friendly;

	/// <summary>Compact damage-composition summary (power x ratio, buffs at hit time). May be empty.</summary>
	public string Comp;

	/// <summary>Second line of the composition (attacker side). May be empty.</summary>
	public string Comp2;

	/// <summary>Third line of the composition (victim side: defense used + taken-damage modifiers).</summary>
	public string Comp3;

	/// <summary>Fourth line: status abnormalities (受击方/自身), kept off the victim line on purpose.</summary>
	public string Comp4;

	/// <summary>
	/// Structured form of the same chain (1.3.0): every number comp1..comp4 mention, plus the two
	/// booleans that used to be conflated into one sentence -- whether the composition was PAIRED with
	/// this hit (<see cref="CalcBreakdown.PairTrusted"/>) and whether its ARITHMETIC reproduces the
	/// game's number (<see cref="CalcBreakdown.ValueMatches"/>). Valid == false when no chain exists.
	/// </summary>
	public CalcBreakdown Calc;

	/// <summary>
	/// 1.5.0 (架构审视 B1): 1-based id into the export's root `facts.items` array -- the deduplicated FACT
	/// RECORD for this hit (composition lines + the live state of the victim at that class's first sight).
	/// 0 = no fact was recorded for this event.
	///
	/// WHY. Live game state is readable only while the objects are alive, so anything not captured during
	/// the battle is unrecoverable; before 1.5.0 that capture happened for at most 160 hits per battle
	/// (3%), which is what turned every new question into "fight another battle". The same hits collapse
	/// to ~500 distinct fact classes, so this reference gives 100% coverage for the same budget.
	/// </summary>
	public int FactId;

	/// <summary>
	/// Ailments this record INFLICTED on its victim, derived by diffing the victim's own status list
	/// before the hit / right after it / 0.35 s later (Diagnostics/StatusDeltaProbe.cs).
	/// Empty when nothing changed -- most damage records inflict nothing.
	/// </summary>
	public string StatusDelta;

	/// <summary>
	/// Who the GAME credits for the ailment(s) in <see cref="StatusDelta"/>, read from
	/// BuffBase.OwnerIdentifier, e.g. "暗闇=死のカラス". Empty when unknown or nothing was inflicted.
	/// When it differs from <see cref="Attacker"/> the record's attacker is only the nearest damage
	/// record, not the source; StatusDelta says so in words as well.
	/// </summary>
	public string StatusApplier;

	/// <summary>
	/// Short "which talents fired" label for this record, e.g. "追加攻击、贯通攻击", derived from the game's
	/// own activation counters (Diagnostics/TalentRuntime.cs). Empty for the vast majority of records.
	/// </summary>
	public string Triggers;

	/// <summary>Structured form of <see cref="Triggers"/> for offline analysis (null when nothing fired).</summary>
	public List<TriggerHit> TriggerList;
}

/// <summary>
/// One talent activation observed between this actor's previous damage event and this one. Delta is read
/// from the game's counter, so it is a measurement of the game's own bookkeeping, not an inference.
/// </summary>
public sealed class TriggerHit
{
	public int Type;         // TalentDefine.Type
	public int TalentIndex;
	public int Delta;
	public int Slot = -1;    // eAbilitySlotType of the owning ability
	public int AbilityId;
	public string Ability = "";
}

public static class BattleEventList
{
	public static void AddNew(List<BattleEvent> list, ref BattleEvent ev, int cap = 100000)
	{
		if (list.Count < cap) list.Add(ev);
	}
}
