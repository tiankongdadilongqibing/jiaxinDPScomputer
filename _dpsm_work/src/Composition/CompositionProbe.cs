using System.Text;
using UnityEngine;

namespace DpsMeter;

/// <summary>
/// Damage composition: what a single hit was made of, and why it landed for the number it did.
///
/// The module is one class split across files by responsibility (it is too large for a single file,
/// and every rule change used to touch the same 2800-line file):
///
///   CompositionProbe.cs             this file: shared state + entry points (Reset, Power)
///   CompositionProbe.Crit.cs        crit flag / attribute rate captured from the fly-text call
///   CompositionProbe.Abilities.cs   ability text cache + the clause scan that folds modifiers in
///   CompositionProbe.Rules.cs       which clause applies and what number it contributes  <-- extend rules here
///   CompositionProbe.Status.cs      status names, live lookup, ATTACK-START snapshot
///   CompositionProbe.GlobalRules.cs battle-wide rules (enemy-takes / ally-attacks)
///   CompositionProbe.Talents.cs     modifiers that exist only as talent data
///   CompositionProbe.Chain.cs       the displayed damage chain (arithmetic + 3 detail rows)
///   CompositionProbe.Text.cs        enum/label translation tables
///   CompositionProbe.Diagnostics.cs the [ABIL] and [COMP] diagnostic dumps
///
/// Grounded in the official damage formula documented on the game wiki (バトル page):
///   ダメージ = (攻撃力 × ダメージ率 − 防御力 × 防御補正 × 貫通補正)
///              × 与・被ダメージ補正 × 会心ダメージ率 × 属性効果
///   最低保証ダメージ = 攻撃力 × ダメージ率 × 5% × 与・被ダメージ補正 × 属性効果
/// where 攻撃力 × ダメージ率 ("計算威力" here) is the first term, and everything after it
/// (defense subtraction, dealt/taken damage modifiers, crit, attribute) is a single composite
/// multiplier we call 後段倍率. The dealt/taken damage modifiers are NOT readable as stats
/// (there is no BuffTarget for them; they live inside BattleObject.Damage / talent code),
/// so the composite cannot be split further -- the display says so instead of guessing.
///
/// Term corrections applied per wiki:
///   * HP is called 耐久, not 生命
///   * 会心率 / 会心ダメージ率 (not 暴击率 / 暴击伤害); crit rate caps at 50%
///   * 貫通率 (not 穿透率); penetration is a flat proportion of defense, not a proc
///   * 回復率 (not 治疗量); heals never crit
///   * attacking a disadvantageous attribute has NO damage penalty (the old x0.5 was removed)
///
/// INVARIANTS (breaking one of these has already caused a shipped bug):
///   * Conditions (statuses, 耐久 tiers) are judged from the ATTACK-START snapshot, never from the
///     live state at damage-apply time -- the hit's own debuff must not raise the hit.
///   * A clause's verdict is decided in exactly ONE place (JudgeClause) and reused for the display,
///     the arithmetic and the [ABIL] dump, so the log can never disagree with the number.
///   * Nothing seasonal/temporary is hard-coded; it is read from the game (see Talents/GlobalRules).
/// </summary>
public static partial class CompositionProbe
{
	private const int MaxLines = 800;
	private static int _lines;
	private static int _attrOddLogged;

	public static void Reset()
	{
		_lines = 0;
		_critHasFlag = false;
		_critBlocker = null;
		// ability texts are per battle: a unit's abilities/levels differ between battles
		_abilityText.Clear();
		_abilityDumped.Clear();
		TieredModifier.ClearCache();
		// attack-start snapshots are per battle; the battle-wide RULES are NOT cleared here: units are
		// created during scene load, i.e. BEFORE the session starts, so clearing at session start threw
		// away every rule collected at spawn (that is why an ally buff did not apply to the first hits).
		_statusSnaps.Clear();
		_curSnapValid = false;
		_ruleLogCount = 0;
		// per-skill coefficient consensus (used to explain a hit whose attack and power disagree)
		ClearSkillRates();
		// candidate attack readings behind the composition (see Diagnostics/PowerProbe.cs)
		PowerProbe.Reset();
	}

	/// <summary>Decrypted calc power (0 when unavailable).</summary>
	public static int Power(DamageCalculater calc)
	{
		try { return GameRef.Dec(calc.m_power); }
		catch { return 0; }
	}

	private static BattleObject TryAttacker(DamageCalculater calc)
	{
		try { return calc.Attacker; } catch { return null; }
	}

	private static string Name(BattleObject b)
	{
		if (GameRef.IsNull(b)) return "?";
		try { return CharacterInfo.DisplayName(b); }
		catch { return "?"; }
	}
}
