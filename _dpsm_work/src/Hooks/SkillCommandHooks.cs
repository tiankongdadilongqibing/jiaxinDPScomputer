namespace DpsMeter;

/// <summary>
/// The skill COMMAND entry points of `GameCmdExecuter`, patched MANUALLY from
/// `Plugin.TryPatchSkillCommands` -- deliberately NOT [HarmonyPatch]-annotated: they must not join the
/// PatchAll set (a target that fails to resolve would kill every other hook), the same isolation rule as
/// the crit/power/madness/give/auto-skill probes.
///
/// WHY THESE THREE. R64 proved the auto-skill channel on
/// `ActExecutePlayerAutoSkillForPassive` (471 rows over 9 battles). 奥义/特殊 had no channel at all, and
/// the one R66 tried -- a postfix on `AddPlayerSkillGameRecord`, the game's unified record sink -- was
/// installed and NEVER CALLED: `rec=0` with every skip counter 0 in a full battle (2026-10-06, quest 9999)
/// and again in a real quest (2026-10-06, quest 411001, 119 s, 4 hooks applied per `LogOutput.log`). R69
/// therefore DELETED that hook and its channel instead of carrying a patch that never fires; the evidence
/// of the falsification is in REFACTOR-BATCH-R69.md, and re-adding it is a matter of restoring the postfix
/// if some mode is ever found where the game does call it.
///
/// These three sit on the same layer as the proven auto-skill command hook, and R67 measured them: 595
/// calls in one battle, 554 of them REJECTED by the game (`__result == false`), 41 accepted -- and the 41
/// are the 奥義 (`Skill.Type == 2`, read back from `Player.ActiveSkill` after the call). Unlike the
/// auto-skill command, this layer's verdict is meaningful, which is why `ok` gates the row here.
///
/// POSTFIX, and only `__0` (Player) plus the game's own verdict are declared. The position argument(s) are
/// deliberately NOT materialised: materialising an argument is the part of a detour that has crashed this
/// plugin before (`Hooks/BattleObjectHooks.cs`), and nothing here needs a position. Reading a reference and
/// a bool costs nothing to marshal. The bodies cannot throw outwards, and with
/// `Debug/SkillTimeline=false` they return immediately.
/// </summary>
public static class SkillCommandHooks
{
	/// <summary>`GameCmdExecuter.ActExecutePlayerActiveSkill(Player, IEnumerable&lt;Vector3&gt;)`.</summary>
	public static void PostfixActiveSkill(Player __0, ref bool __result)
	{
		try { SkillTimelineProbe.NoteSkillCommand(__0, "active", __result); }
		catch { }
	}

	/// <summary>`GameCmdExecuter.ActExecutePlayerSkill(Player, IEnumerable&lt;Vector3&gt;)`.</summary>
	public static void PostfixSkill(Player __0, ref bool __result)
	{
		try { SkillTimelineProbe.NoteSkillCommand(__0, "skill", __result); }
		catch { }
	}

	/// <summary>`GameCmdExecuter.ActExecutePlayerSpecialSkill(Player, Vector3)`.</summary>
	public static void PostfixSpecialSkill(Player __0, ref bool __result)
	{
		try { SkillTimelineProbe.NoteSkillCommand(__0, "special", __result); }
		catch { }
	}
}
