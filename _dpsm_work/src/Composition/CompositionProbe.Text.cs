using System.Text;
using UnityEngine;

namespace DpsMeter;

/// <summary>
/// Enum and label translations (DamageSource, hit type, buff parameters, status and target names).
/// </summary>
public static partial class CompositionProbe
{
	/// <summary>Chinese label for a DamageSource enum value.</summary>
	public static string SrcName(int v)
	{
		switch (v)
		{
			case 0: return "未知";
			case 1: return "直接攻击";
			case 2: return "投射物";
			case 3: return "毒";
			case 4: return "火伤";
			case 5: return "持续伤害";
			case 6: return "反射";
			case 7: return "自身反射";
			case 8: return "直接回复";
			case 9: return "被动回复";
			case 10: return "吸血";
			case 11: return "吸收";
			case 12: return "复活奴仆";
			case 13: return "不死结束";
			case 14: return "DOT";
			case 15: return "回复反噬";
			case 16: return "调试伤害";
			default: return "来源" + v;
		}
	}

	/// <summary>Is this source a healing-family source (never counted as damage output)?</summary>
	public static bool IsHealSource(int v)
	{
		return v == 8 || v == 9 || v == 12 || v == 13 || v == 15 || v == 16;
	}

	/// <summary>攻撃属性 (eDamageCalcType): physical / magic / penetration / heal.</summary>
	public static string HitTypeName(int v)
	{
		switch (v)
		{
			case 0: return "无";
			case 1: return "物理";
			case 2: return "魔法";
			case 3: return "贯通";
			case 4: return "治疗";
			case 5: return "物理或魔法";
			default: return "攻击属性" + v;
		}
	}

	/// <summary>Attribute display name of a battle object (empty when unavailable).</summary>
	private static string AttrName(BattleObject bo)
	{
		try
		{
			if (GameRef.IsNull(bo)) return "";
			// AttributeModel is Il2CppSystem.Object, NOT a UnityEngine.Object: casting it to
			// UnityEngine.Object throws, and the throw used to be swallowed below -- which silently
			// disabled the whole attribute-advantage check (every hit showed 属性×1.00 and the x2
			// bonus ended up in the residual multiplier).
			var at = bo.Attribute;
			if (at == null) return "";
			string nm = at.Name;
			if (string.IsNullOrEmpty(nm)) nm = "属" + at.Id;
			return nm;
		}
		catch { return ""; }
	}

	/// <summary>
	/// Attribute relation via the game's own CheckAttackAdvantage.
	/// Wiki: only "our character attacks an enemy" gets the x2 bonus, and the old
	/// disadvantage penalty (x0.5) has been REMOVED -- so 被克 must not claim a damage cut.
	/// </summary>
	private static string AttrRelation(BattleObject atk, BattleObject blocker)
	{
		try
		{
			if (GameRef.IsNull(atk) || GameRef.IsNull(blocker)) return "";
			var a1 = atk.Attribute;
			var a2 = blocker.Attribute;
			if (a1 == null || a2 == null) return "";
			var res = a1.CheckAttackAdvantage(a2);
			string s = res.ToString();
			switch (s)
			{
				case "Advantage": case "Strong": case "Good": return "克制(伤害×2)";
				case "Disadvantage": case "Weak": case "Bad": return "被克(无减伤)";
				case "Equal": case "Normal": case "None": return "同属/无属性";
				default: return s;
			}
		}
		catch { return ""; }
	}

	/// <summary>Calc-level flags that materially change the final multiplier.</summary>
	private static string CalcFlags(DamageCalculater calc)
	{
		var sb = new StringBuilder(48);
		try { if (calc.m_isIgnoreDamageChange) Append(sb, "无视减伤/增伤"); } catch { }
		try { if (calc.m_isIgnoreCritical) Append(sb, "无视会心"); } catch { }
		try { if (calc.m_isIgnoreEvasion) Append(sb, "必中"); } catch { }
		try { if (calc.m_isIgnoreBarrier) Append(sb, "无视护盾"); } catch { }
		try { if (calc.m_isIgnoreTakeOver) Append(sb, "无视伤害转移"); } catch { }
		try { if (calc.m_isIgnoreEnchant) Append(sb, "无视强化"); } catch { }
		try
		{
			int refl = GameRef.Dec(calc.m_reflectionDamageRate);
			if (refl > 0) Append(sb, "反射" + refl + "%");
		}
		catch { }
		return sb.ToString();
	}

	private static void Append(StringBuilder sb, string s)
	{
		if (sb.Length > 0) sb.Append('/');
		sb.Append(s);
	}

	/// <summary>Attribute damage multiplier (2.0 when the game reports an advantage, else 1.0).</summary>
	private static double AttrMultiplier(BattleObject atk, BattleObject blocker)
	{
		try
		{
			if (GameRef.IsNull(atk)) return 1.0;
			if (GameRef.IsNull(blocker)) return 1.0;
			var a1 = atk.Attribute;
			var a2 = blocker.Attribute;
			if (a1 == null || a2 == null) return 1.0;
			string s = a1.CheckAttackAdvantage(a2).ToString();
			if (s == "Advantage" || s == "Strong" || s == "Good") return 2.0;
			if (s == "Normal" || s == "Equal" || s == "None" || s == "Disadvantage" || s == "Weak" || s == "Bad") return 1.0;
			// unexpected value: say so once instead of silently returning 1.0 forever
			if (_attrOddLogged < 4)
			{
				_attrOddLogged++;
				RuntimeLog.Write("[ABIL] ! 属性判定返回了未预期的值: " + s + " (按 ×1.00 处理)");
			}
			return 1.0;
		}
		catch { return 1.0; }
	}

	/// <summary>Join two display fragments with the list separator used by the composition lines.</summary>
	private static string Join(string a, string b)
	{
		if (string.IsNullOrEmpty(a)) return b;
		if (string.IsNullOrEmpty(b)) return a;
		return a + "、" + b;
	}

	private static readonly string[] NoStatuses = new string[0];

	/// <summary>
	/// 1.5.0 (B3): the status LIST behind a "、" joined status string (the form
	/// <see cref="StatusBrief"/> produces and comp4 embeds). Returns an EMPTY ARRAY, never null, so a
	/// consumer can distinguish "the unit had no status" from "there was no chain at all" by whether the
	/// owning CalcBreakdown is valid.
	///
	/// This is deliberately a split of the SAME string that goes into comp4 rather than a second walk of
	/// the buff list: two walks could disagree, one split cannot.
	/// </summary>
	internal static string[] SplitStatusList(string joined)
	{
		if (string.IsNullOrEmpty(joined)) return NoStatuses;
		return joined.Split('、');
	}

	/// <summary>
	/// Diagnostic: append every talent of one ability as "type/param0[/param1]". Used by the [ABIL] dump
	/// so that a modifier the text does not describe (the arena's weekly class buff, for instance) can be
	/// located and identified.
	/// </summary>

	/// <summary>Full damage chain, used by the diagnostic log.</summary>
	public static string BuildChain(DamageCalculater calc, BattleObject blocker, int finalDamage)
	{
		try
		{
			var sb = new StringBuilder(260);
			BattleObject atk = null;
			try { atk = calc.Attacker; } catch { }
			int atkPower = 0;
			if (!GameRef.IsNull(atk))
			{
				try { atkPower = atk.Power; } catch { }
			}
			int pow = Power(calc);
			sb.Append("攻击力 ").Append(atkPower);
			string aAttr = AttrName(atk);
			if (!string.IsNullOrEmpty(aAttr)) sb.Append("(属性 ").Append(aAttr).Append(')');
			sb.Append(" → 计算威力 ").Append(pow);
			if (atkPower > 0 && pow > 0)
				sb.Append("(技能系数 ×").Append(((double)pow / atkPower).ToString("F2")).Append(')');
			if (!GameRef.IsNull(blocker))
			{
				int def = 0, mdef = 0;
				try { def = blocker.Defense; } catch { }
				try { mdef = blocker.MagicDefense; } catch { }
				string tAttr = AttrName(blocker);
				sb.Append(" → 目标 ").Append(Name(blocker));
				if (!string.IsNullOrEmpty(tAttr)) sb.Append("(属性 ").Append(tAttr).Append(')');
				sb.Append(" 防御 ").Append(def).Append("(魔防 ").Append(mdef).Append(')');
				string rel = AttrRelation(atk, blocker);
				if (!string.IsNullOrEmpty(rel)) sb.Append(" 属性关系:").Append(rel);
			}
			try { sb.Append(" 攻击属性:").Append(HitTypeName((int)calc.m_hitType)); } catch { }
			string fl = CalcFlags(calc);
			if (!string.IsNullOrEmpty(fl)) sb.Append(" [").Append(fl).Append(']');
			if (pow > 0)
				sb.Append(" → 后段倍率 ×").Append(((double)finalDamage / pow).ToString("F3"));
			sb.Append(" → 最终 ").Append(finalDamage);
			return sb.ToString();
		}
		catch { return ""; }
	}

	/// <summary>Chinese label for BuffTarget members (complete enum coverage).</summary>
	public static string TargetName(string en)
	{
		if (string.IsNullOrEmpty(en)) return "状态";
		switch (en)
		{
			case "Life": return "耐久";
			case "Power": return "攻击力";
			case "Defense": return "物理防御";
			case "MagicDefense": return "魔法防御";
			case "MoveSpeed": return "移动速度";
			case "AttackSpeed": return "攻击速度";
			case "AttackInterval": return "攻击间隔";
			case "AttackRange": return "攻击范围";
			case "AttackCount": return "攻击次数";
			case "BlockNum": return "格挡数";
			case "TargetNum": return "目标数";
			case "SubTargetNum": return "副目标数";
			case "NeedBlockNum": return "需格挡数";
			case "RegenePoint": return "回复点";
			case "HealingLimit": return "回复上限";
			case "Hate": return "仇恨";
			case "RangeHate": return "远程仇恨";
			case "CitadelAreaHate": return "城壁区域仇恨";
			case "GuardianAreaHate": return "守护区域仇恨";
			case "AdditiveReflectRate": return "反射率";
			case "HitRate": return "命中率";
			case "CriticalRate": return "会心率";
			case "CriticalRateLimit": return "会心率上限";
			case "CriticalDamageRate": return "会心伤害率";
			case "CriticalDownRate": return "会心率降低";
			case "CriticalDamageDownRate": return "会心伤害降低";
			case "PenetrationRate": return "贯通率";
			case "HealingRate": return "回复率";
			case "EvasionRate": return "回避率";
			case "Missile": return "投射物";
			case "RangeShape": return "范围形状";
			case "TargetType": return "目标类型";
			case "HitType": return "攻击属性";
			case "AttackSortConfig": return "攻击排序";
			case "AttackFilterConfig": return "攻击筛选";
			case "SubAttack": return "副攻击";
			case "PlayKnockBackType": return "击退类型";
			case "PlayKnockBackSpeed": return "击退速度";
			case "AdditionalAttribute": return "附加属性";
			case "AutoSkillChargingSpeed": return "自动技能充能";
			case "SkillFirstWaitRate": return "技能首发等待";
			case "ActiveBomSkillUnlimited": return "技能无限制";
			case "OverSkillChargingSpeed": return "必杀充能";
			case "StunResistance": return "眩晕抗性";
			case "StunTerminalResistance": return "眩晕阈值抗性";
			case "PetrifactionResistance": return "石化抗性";
			case "PetrifactionTerminalResistance": return "石化阈值抗性";
			case "PoisonResistance": return "毒抗性";
			case "PoisonDamageResistance": return "毒伤害抗性";
			case "KnockBackResistance": return "击退抗性";
			case "BurnResistance": return "火伤抗性";
			case "FrozenResistance": return "冻结抗性";
			case "DarknessResistance": return "黑暗抗性";
			case "MadnessResistance": return "疯狂抗性";
			case "FearResistance": return "恐惧抗性";
			case "DeathResistance": return "即死抗性";
			case "TimeStopResistance": return "时停抗性";
			case "BaseStatusResistance": return "基础状态抗性";
			case "MoveSpeedResistance": return "减速抗性";
			case "AttackSpeedResistance": return "攻速降低抗性";
			case "AttackIntervalResistance": return "攻击间隔抗性";
			case "KnockBackSpeed": return "击退速度";
			case "PoisonDamage": return "毒伤害";
			case "BurnDamage": return "火伤伤害";
			case "Max": return "上限";
			case "None": return "无";
			default: return en;
		}
	}

	/// <summary>CharacterStatus.Type members (state abnormalities), per the wiki status table.</summary>
	public static string StatusName(string en)
	{
		switch (en)
		{
			case "Poison": return "毒";
			case "DeadlyPoison": return "猛毒";
			case "DeathPoison": return "死毒";
			case "Burn": return "火伤";
			case "BigBurn": return "大火伤";
			case "Stun": return "眩晕";
			case "Petrifaction": return "石化";
			case "Frozen": return "冻结";
			case "Fear": return "恐惧";
			case "Darkness": return "黑暗";
			case "Madness": return "疯狂";
			case "Death": return "即死";
			case "TimeStop": return "时停";
			case "Bind": return "束缚";
			case "HealBan": return "禁疗";
			case "SkillSealed": return "技能封印";
			case "Incite": return "煽动";
			case "Flight": return "浮空";
			case "Covert": return "隐蔽";
			case "KnockBack": return "击退";
			case "HitStop": return "硬直";
			case "EnchantBan": return "禁强化";
			default: return TargetName(en);
		}
	}

	public static string TypeName(string en)
	{
		switch (en)
		{
			case "Rate": return "比例";
			case "Actual": return "数值";
			case "Fixed": return "固定";
			case "None": return "无";
			case "Max": return "上限";
			default: return en;
		}
	}

	/// <summary>ReferenceType members, for "ステータスのn%を加算" style buffs.</summary>
	private static string RefName(string en)
	{
		switch (en)
		{
			case "Life": return "耐久";
			case "Power": return "攻击力";
			case "Defense": return "物理防御";
			case "MagicDefense": return "魔法防御";
			case "AttackSpeed": return "攻击速度";
			case "BlockNum": return "格挡数";
			case "CriticalRate": return "会心率";
			case "CriticalDamageRate": return "会心伤害率";
			case "CriticalDownRate": return "会心率降低";
			case "CriticalDamageDownRate": return "会心伤害降低";
			case "PenetrationRate": return "贯通率";
			case "HealingRate": return "回复率";
			case "EvasionRate": return "回避率";
			case "CurrentLife": return "当前耐久";
			case "CurrentPower": return "当前攻击力";
			case "CurrentDefense": return "当前物防";
			case "CurrentMagicDefense": return "当前魔防";
			case "CurrentMoveSpeed": return "当前移动速度";
			case "CurrentAttackSpeed": return "当前攻击速度";
			case "CurrentBlockNum": return "当前格挡数";
			case "CurrentCriticalRate": return "当前会心率";
			case "CurrentCriticalDamageRate": return "当前会心伤害率";
			case "CurrentCriticalDownRate": return "当前会心降低率";
			case "CurrentCriticalDamageDownRate": return "当前会伤降低率";
			case "CurrentPenetrationRate": return "当前贯通率";
			case "CurrentHealingRate": return "当前回复率";
			case "CurrentEvasionRate": return "当前回避率";
			case "MaxLife": return "最大耐久";
			case "MaxPower": return "最大攻击力";
			case "MaxDefense": return "最大物防";
			case "MaxMagicDefense": return "最大魔防";
			case "MaxCriticalRate": return "会心率上限";
			default: return en;
		}
	}
}
