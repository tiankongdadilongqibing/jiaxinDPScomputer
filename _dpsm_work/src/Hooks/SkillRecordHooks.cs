namespace DpsMeter;

/// <summary>
/// `GameCmdExecuter.AddPlayerSkillGameRecord(Player player, Skill skill, eUserRecordType type, Vector3 pos)`.
/// Patched MANUALLY from `Plugin.TryPatchSkillRecord` -- deliberately NOT [HarmonyPatch]-annotated: it must
/// not join the PatchAll set (a target that fails to resolve would kill every other hook), the same
/// isolation rule as the crit/power/madness/give/auto-skill probes.
///
/// WHY THIS ONE METHOD. It is the game's unified skill-record sink and the only observation point that
/// carries BOTH the `Skill` object and the game's own record type, so 奥义 / 特殊 / 自动 are all reachable
/// through one signature -- there is no `ActExecutePlayerOverSkill` to patch (checked with metascan on
/// 2026-10-06: `GameCmdExecuter` has ActExecutePlayer{ActiveSkill,Skill,SpecialSkill,AutoSkillForPassive}
/// and private AddPlayer{Skill,OverSkill,SpecialSkill,AutoSkillForPassiveSkill}GameRecord; the over skill
/// runs through `Player.ExecuteActiveSkill`, which must not be patched).
///
/// POSTFIX, not prefix: the record is only a record once the game has decided it, and running afterwards
/// also means the body cannot influence whether the skill is written down.
///
/// Only `__0` (Player), `__1` (Skill) and `__2` (eUserRecordType) are declared. The `Vector3` parameter is
/// deliberately NOT materialised: materialising an argument is the part of a detour that has crashed this
/// plugin before (`Hooks/BattleObjectHooks.cs`), and nothing here needs the position. Reading a reference
/// and an enum costs nothing to marshal. The body cannot throw outwards.
///
/// This hook is a pure OBSERVER: with `Debug/SkillTimeline=false` the postfix returns immediately, and even
/// when it is on it only appends to a bounded list.
/// </summary>
public static class SkillRecordHooks
{
	// ---- R67: the active/special COMMAND entry points ------------------------------------------------
	// R66's record sink was installed and NEVER CALLED in a full battle (live log 2026-10-06: rec=0 with
	// foreignSide/nonStart/nullSkips all 0 -- the body never ran even once). These three sit on the same
	// layer as `ActExecutePlayerAutoSkillForPassive`, the one channel this plugin has proven (97 / 97
	// activations matched in that battle). Same shape as AutoSkillHooks: Player + the game's own verdict,
	// and the position argument(s) deliberately NOT declared (materialising an argument is the part of a
	// detour that has crashed this plugin; nothing here needs a position).

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

	public static void PostfixSkillRecord(Player __0, Skill __1, eUserRecordType __2)
	{
		try { SkillTimelineProbe.NoteRecord(__0, __1, __2); }
		catch { }
	}
}
