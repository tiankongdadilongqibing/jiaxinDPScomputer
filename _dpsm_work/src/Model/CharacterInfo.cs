using UnityEngine;

namespace DpsMeter;

public static class CharacterInfo
{
	public static string DisplayName(BattleObject bo)
	{
		if (GameRef.IsNull(bo)) return "?";
		try
		{
			string name = bo.Name;
			if (!string.IsNullOrEmpty(name) && name != "#-1") return name;
			CharacterDataBase data = bo.Data;
			if (data != null && !string.IsNullOrEmpty(data.Name)) return data.Name;
		}
		catch { }
		return KindLabel(bo) switch
		{
			"C" => "城塞",
			"T" => "召唤物",
			"Boss" => "首领",
			_ => "单位#" + bo.EntryId,
		};
	}

	/// <summary>Raw kind from eBattleObjectType flags: P=player E=enemy T=token Boss B=bom C=citadel U=unknown.</summary>
	public static string KindLabel(BattleObject bo)
	{
		if (GameRef.IsNull(bo)) return "?";
		try
		{
			eBattleObjectType objectType = bo.ObjectType;
			if (((int)objectType & 0x10) != 0) return "Boss";
			if (((int)objectType & 2) != 0) return "P";
			if (((int)objectType & 4) != 0) return "E";
			if (((int)objectType & 8) != 0) return "T";
			if (((int)objectType & 0x20) != 0) return "B";
			if (((int)objectType & 0x40) != 0) return "C";
			return "U";
		}
		catch { return "?"; }
	}

	public static bool IsAlly(BattleObject bo)
	{
		// R70: the verdict moved into `SkillSidePolicy` (pure, unit-tested, mutable), because the 技能时间表
		// claimed to use this test on all three of its routes and really used it on one -- the enemy's auto
		// skills were published as ours. This stays the reader: one place reads `TeamType`, one place decides.
		if (!GameRef.IsNull(bo))
			return SkillSidePolicy.IsOurs((int)bo.TeamType);
		return false;
	}

	public static bool IsAllyTeam(TeamType t)
	{
		try { return SkillSidePolicy.IsOurs((int)t); }
		catch { return false; }
	}

	/// <summary>Short Chinese tag for a raw KindLabel value.</summary>
	public static string KindText(string kind)
	{
		switch (kind)
		{
			case "P": return "我";
			case "E": return "敌";
			case "T": return "召";
			case "Boss": return "首";
			case "B": return "弹";
			case "C": return "城";
			default: return "?";
		}
	}
}
