namespace DpsMeter;

/// <summary>
/// R79: the DamageSource / eDamageCalcType label maps, moved out of
/// <c>Composition/CompositionProbe.Text.cs</c> into the pure policy layer. That file is game-coupled
/// (UnityEngine, BattleObject, DamageCalculater), so nothing in it is reachable from
/// tests/BehaviorTests -- which meant every label the plugin prints was unpinnable, and the new
/// 受击来源拆分 board needs to label a bucket per dimension.
///
/// The three methods below are the ones that file published before this round, with byte-identical
/// strings; it now delegates here, so the map has exactly ONE definition and no battle output changed.
/// (Deliberately no new cases: `HitTypeName(-1)` still returns "攻击属性-1", because the panel prints it
/// that way today and this round does not move any existing text. The taken board labels the damage
/// path's -1 "unreadable" sentinel itself.)
/// </summary>
internal static class DamageSourceLabelPolicy
{
	/// <summary>Chinese label for a DamageSource enum value (0 Unknown .. 16 DebugDamage).</summary>
	public static string Source(int v)
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
	public static string HitType(int v)
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
}
