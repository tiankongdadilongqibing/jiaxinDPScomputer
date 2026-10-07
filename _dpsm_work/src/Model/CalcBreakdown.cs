namespace DpsMeter;

/// <summary>
/// Structured form of ONE hit's damage chain (1.3.0).
///
/// The 中文 `comp1..comp4` strings stay for humans, but every number they mention is also carried here,
/// so offline analysis reads fields instead of regex-parsing Chinese. It also separates the two
/// questions the old single "不符" label conflated:
///
///   * <see cref="Pair"/> / <see cref="PairTrusted"/> -- is this composition even ABOUT this hit?
///     (which DamageCalculater was paired with this damage record, and how strong that pairing is)
///   * <see cref="ValueMatches"/> -- does the chain's own arithmetic reproduce the game's number?
///
/// Measured 2026-10-03: 98.6% of rows whose arithmetic DOES reproduce the number were still carrying the
/// "不符" label, because the label was reporting the pairing, not the arithmetic. Offline analysis can now
/// tell the two apart; the label was reworded in the same release (see Aggregator.TryGetCompForVictim).
///
/// Filled by CompositionProbe.BuildChainParts; <see cref="Pair"/> is set by the pairing code (Aggregator).
/// </summary>
public struct CalcBreakdown
{
	/// <summary>A chain was actually produced for this hit.</summary>
	public bool Valid;

	/// <summary>DamageCalculater.m_effectId. 0 = the calc carries no skill id (81% of hits historically).</summary>
	public int EffectId;

	/// <summary>m_hitType: 1 / 5 = 物理, 2 = 魔法, 3 = 貫通 (no defence applied). -1 = unreadable.</summary>
	public int HitType;

	/// <summary>攻击力 as SHOWN in the chain -- the chosen candidate, not a raw read. The raw reads can
	/// disagree by 140% for DOT/summon hits (see the 【同威力主档】 note in the text form).</summary>
	public int AttackPower;

	/// <summary>计算威力 (the game's own number, fixed when the calc was built).</summary>
	public int Power;

	/// <summary>Power / AttackPower -- the skill's damage coefficient, as recovered (not read).</summary>
	public double Ratio;

	/// <summary>The defence figure actually subtracted. 0 = none applied.</summary>
	public int DefenseUsed;

	/// <summary>物防 / 魔防 / "" when no defence was applied.</summary>
	public string DefenseKind;

	/// <summary>攻击方贯通率 % -- readable, so it is applied in the chain rather than left in the residual.</summary>
	public int Penetration;

	/// <summary>DefenseUsed × (1 − 贯通率).</summary>
	public int EffectiveDefense;

	/// <summary>保底伤害 (5% of 计算威力) triggered instead of the normal subtraction.</summary>
	public bool MinRule;

	/// <summary>Damage after the defence term, before the identified multipliers.</summary>
	public int BaseDamage;

	/// <summary>属性相克倍率 (1.00 / 2.00 / 0.50 ...).</summary>
	public double AttrMult;

	/// <summary>与ダメージ補正 identified from ability texts + talent parameters.</summary>
	public double DealtMult;

	/// <summary>被ダメージ補正 identified the same way.</summary>
	public double TakenMult;

	/// <summary>AttrMult × DealtMult × TakenMult.</summary>
	public double KnownMult;

	/// <summary>BaseDamage × KnownMult -- what the chain predicts. This is the printed "理论 ... = R".</summary>
	public long Theory;

	/// <summary>The amount this plugin PUBLISHED for the hit, i.e. the exported amount: the call's return when
	/// that return reports an overflow, and `BattleObject.Damage`'s argument otherwise. R78: the return is the
	/// OVERFLOW beyond the victim's remaining Life (R76: `res == max(0, nominal - lifeBefore)`, 798/798
	/// readings), so on those calls this field is the part that did NOT fit -- not the damage that reached 耐久.
	/// </summary>
	public int Applied;

	/// <summary>The difference `argument − published` (R78: 超出剩余耐久/非吸收; the old name said "absorbed").
	/// On an overflow call it equals the victim's remaining Life before the hit (see
	/// `Policy/AbsorbWording.cs`), and it is not an absorption: the shape that would deserve that word (R76's
	/// `WithheldNoReturn`) is empty in every corpus measured. The game's own statistics count the argument,
	/// which is why <see cref="GameValue"/> exists.</summary>
	public int Absorbed;

	/// <summary>Applied / Theory -- everything the chain could NOT identify. Exactly 1.000 is a full match.
	/// This is the "剩余倍率" the text form prints. R78: the TEXT form prints it from the game's value
	/// (`AbsorbWording.ResidualBasis`), while this field keeps its original basis on purpose -- it is part of
	/// the export (`calc.residual`) and a `forensics` bucket key, so moving it is the accounting round's call.
	/// </summary>
	public double Residual;

	/// <summary>攻击方 CriticalRate (0 when the attacker could not be read).</summary>
	public int CritRate;

	/// <summary>攻击方 CriticalDamageRate (150 = ×1.50).</summary>
	public int CritDamageRate;

	/// <summary>
	/// The VICTIM's blocking state at hit time, read straight from BattleObject (`IsBlocking` /
	/// `IsUnitBlocking` / `BlockCount`). -1 = could not be read.
	///
	/// Why measure instead of model: the single largest unexplained multiplier in the 1.3.3 battle was a
	/// constant ×1.210 on one boss (43.7% of that battle's unexplained hits, 1508 of them), split into
	/// exactly two values on that boss -- 1.000 (2039 hits) and 1.210 (1508) -- which is the signature of
	/// a CONDITIONAL rule rather than a constant. `エヴァラス・フラウ` carries two copies of 刻印 id=26
	/// 「ブロックしている敵の被ダメージ+10%（前衛のみ）」 (`type=1006 被伤害- p=[-10]`), and 1.1² = 1.21
	/// exactly. The composition has no path for it: `vicMod` only scans the VICTIM's own abilities, and
	/// this rule lives on an ally. The condition ("the enemy I am blocking") is what has to be evaluated,
	/// so it is captured per hit BEFORE any rule is written. The names are ambiguous
	/// (IsBlocking vs IsUnitBlocking vs IsCitadelBlocking), so all of them are recorded and the data
	/// decides which one splits the two groups.
	/// </summary>
	public int VictimIsBlocking;

	/// <summary>See <see cref="VictimIsBlocking"/>. -1 = unreadable.</summary>
	public int VictimIsUnitBlocking;

	/// <summary>See <see cref="VictimIsBlocking"/>. -1 = unreadable.</summary>
	public int VictimBlockCount;

	/// <summary>How many ADDITIONAL (granted) talents the victim carried at hit time. -1 = unreadable.
	/// See <see cref="VictimExtraTalentText"/> for why this is read per hit rather than once.</summary>
	public int VictimExtraTalents;

	/// <summary>Compact rendering of those granted talents, e.g. "1006/-10/0,1006/-10/0" -- which is what
	/// 「ブロックしている敵の被ダメージ+10%」 granted twice would look like on the blocked enemy.
	/// The [ABIL] dump cannot answer this: it is taken when a unit is FIRST SEEN, and measured 2026-10-03
	/// the boss read empty at t=0 s while the unexplained ×1.21 only starts ~10 s in.</summary>
	public string VictimExtraTalentText;

	/// <summary>How many buffs the victim carried at hit time. -1 = unreadable. Taken together with
	/// <see cref="VictimExtraTalents"/> so that "the victim really carries nothing" can be distinguished
	/// from "we failed to look".</summary>
	public int VictimBuffs;

	/// <summary>
	/// Live (non-deleted) entries in the VICTIM's `m_giveTalentData` -- talents handed to it by other
	/// units. -1 = unreadable.
	///
	/// This is the field that resolves the ×1.210 window. `m_additionalTalents` stayed 0 on the boss for
	/// all 5490 hits (measured 2026-10-03) and `m_buffList` (175-270 entries, churning every hit) did not
	/// separate the two groups either (median 185 inside the window vs 187 outside). The grant has its
	/// own list, and the master definition of 刻印 id=26 says outright that the rule is an
	/// `AddTalent`-delivered `1006 p=[-10]`.
	/// </summary>
	public int GivenTalents;

	/// <summary>How many of <see cref="GivenTalents"/> contributed a factored 被伤害 modifier.</summary>
	public int GivenApplied;

	/// <summary>1.7.9: the General/GivenTalent switch as read for THIS hit, exported per hit next to
	/// <see cref="GivenTalents"/>/<see cref="GivenApplied"/> so the two settings are distinguishable IN
	/// DATA rather than by inference. With it false the granted entries are still counted in
	/// <see cref="GivenTalents"/> but fold nothing, apply nothing, and must not appear in the exported
	/// fold list.</summary>
	public bool GivenFoldOn;

	/// <summary>Compact rendering of the granted 被伤害 modifiers, e.g. "被伤害+10%(赋予)、被伤害+10%(赋予)".</summary>
	public string GivenTalentText;

	/// <summary>
	/// 攻击方 `CharaStatus.MadnessAllyBuffRatio` at hit time (1.4.1). 100 is the neutral value; 250 was
	/// measured while 狂気 was active, and the multiplier folded into <see cref="DealtMult"/> is
	/// ratio/100. 0 = could not be read.
	///
	/// Exported separately from <see cref="DealtMult"/> on purpose: the fold is proven by 924/924
	/// records of one battle, and a future battle can only falsify it if the RAW reading is in the data
	/// rather than already multiplied away.
	/// </summary>
	public int MadnessRatio;

	/// <summary>Was 狂気 on the ATTACKER at hit time? (1.4.1) The fold requires this flag AND a ratio
	/// above the neutral 100, so a unit carrying the status with a neutral ratio cannot be over-counted.</summary>
	public bool MadnessOn;

	/// <summary>Was 狂気 on the VICTIM at hit time? (1.5.3) The incoming fold is keyed on THIS and only this;
	/// the factor is the measured 1.5, so the flag alone decides. Exported as `victimMadnessOn`.</summary>
	public bool VictimMadnessOn;

	/// <summary>
	/// 1.5.0 (架构审视 A1): EVERY factor actually multiplied into this hit, in fold order, each with a
	/// STABLE origin id (see <see cref="FoldStep"/>) -- not just the running products above.
	///
	/// WHY. Measured on battle_411001_20261003_150140: 84 distinct attacker-side factor labels, of which
	/// 2-5 were folded into a single hit, and all of them collapsed into <see cref="DealtMult"/> /
	/// <see cref="TakenMult"/>. So when a residual of exactly 1.15^n appeared, "was this 1.15
	/// under-folded or over-folded?" -- and "which of the two or three same-valued rules folded which
	/// copy?" -- were undecidable from the data. That is the question this list exists to answer.
	///
	/// The two doubles are KEPT (they are the arithmetic and the KPI depends on them); this list is the
	/// provenance. Null when no chain was produced or when the sink is disabled.
	/// </summary>
	public System.Collections.Generic.List<FoldStep> Fold;

	/// <summary>1.7.4 (阶段 G): the GIVERS of this hit extra attack power, each with the factor that
	/// removing them would apply (P / (P - dP_g)). Computed in the SAME composition pass that built the
	/// 增益 text, so the list and AttackPower describe one instant. Serialised as calc.atkAdd so the
	/// offline cores READ the factors instead of re-deriving the formula, and consumed as folds by
	/// ContributionSession. Empty when nothing was granted from outside.</summary>
	public System.Collections.Generic.List<AtkAddRow> AtkAdd;

	/// <summary>
	/// 1.5.0 (架构审视 A5): the granted modifiers this hit did NOT fold because the cross-unit rule path
	/// already accounts for them, each naming the responsible rule that absorbed it.
	///
	/// WHY. The cancellation is the prime suspect for the 1.15^n residual, and before 1.5.0 it left no
	/// per-hit trace at all (only the aggregate `GivenCancelled` counter). Null when nothing was cancelled.
	/// </summary>
	public System.Collections.Generic.List<CancelStep> FoldCancels;

	/// <summary>How many 敌方受伤 rules were RESPONSIBLE for this hit (the set the cancellation consults).
	/// Exported per hit so "one rule absorbed five copies" is visible as 1 vs 5. 0 means either no chain
	/// or no responsible rule -- <see cref="Valid"/> tells the two apart.</summary>
	public int FoldResponsible;

	/// <summary>The largest number of granted copies absorbed by ONE responsible rule on this hit.
	/// 1 = the responsibility set and the grant list agree on multiplicity; &gt;1 is the pattern that used
	/// to be invisible and is the reason this field exists.</summary>
	public int FoldMaxAbsorbed;

	/// <summary>Provenance entries dropped by the per-hit caps (steps + cancellations). Reported so a
	/// truncated list can never look like a complete one.</summary>
	public int FoldDropped;

	/// <summary>
	/// 1.5.0 (架构审视 B3): the VICTIM's status names at hit time as a LIST, not only as the
	/// "受击方状态:…" sentence inside the comp4 string.
	///
	/// WHY. `StatusDeltaProbe.Audit` -- a top-level export block -- had no input other than that Chinese
	/// sentence: its own comment said so ("this parses that instead of duplicating the data into a new
	/// event field"), and it depended on the literal prefix and a three-space separator. Changing the
	/// wording, or the separator, would have silently changed `statusAudit.appearances` with the build
	/// still green. The plugin now keeps its own data instead of re-reading its own prose.
	///
	/// Producers: CompositionProbe.Chain splits the very same string it puts into comp4, so the two cannot
	/// disagree. Null when no chain was produced.
	/// </summary>
	public string[] VictimStatuses;

	/// <summary>See <see cref="VictimStatuses"/>; the ATTACKER's status names at hit time.</summary>
	public string[] AttackerStatuses;

	/// <summary>Which pairing produced this composition:
	/// "live-same" = the calc executing right now, for exactly this victim;
	/// "live-age"  = a calc executing right now, for ANOTHER target (time-order guess);
	/// "value"     = a recorded calc for this victim whose damage equals this hit;
	/// "fifo"      = oldest unused calc for this victim (pure time order);
	/// ""          = no composition was produced at all.</summary>
	public string Pair;

	/// <summary>Did the GAME's own record for this (calc, victim) carry exactly the damage we are
	/// explaining? This is the check behind the "配对未获结算对象佐证" sentence. Measured 2026-10-03 on the
	/// 1.2.3 exports it was false for 2,458 of 2,458 live-paired hits -- the check used to stop at the
	/// first record of the calc pointer, so an AoE cast (one calc, one record per target) was always
	/// judged against the wrong target. Fixed in 1.3.0; kept as its own field because "the pairing was
	/// not corroborated" and "the arithmetic does not add up" are different failures.</summary>
	public bool PairCorroborated;

	/// <summary>Can the composition be trusted to be ABOUT this hit? "live-same" is the closest thing to an
	/// exact pairing; "value" is corroborated by the damage value. "live-age" and "fifo" are time-order
	/// guesses and are reported as NOT trusted rather than silently presented as exact.</summary>
	public bool PairTrusted
	{
		get { return Pair == "live-same" || Pair == "value"; }
	}

	/// <summary>The game's own figure for this hit (pre-absorption).
	/// Measured 2026-09-27: calc 421,140 → 198 applied, so comparing against the return value alone would
	/// mark every absorbed hit as a mismatch.</summary>
	public long GameValue
	{
		get { return (long)Applied + Absorbed; }
	}

	/// <summary>Does the chain's arithmetic reproduce the game's number?</summary>
	public bool ValueMatches
	{
		get
		{
			if (!Valid || Theory <= 0) return false;
			return Theory == GameValue;
		}
	}
}
