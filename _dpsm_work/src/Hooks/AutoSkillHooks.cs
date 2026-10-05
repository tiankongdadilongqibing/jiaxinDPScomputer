namespace DpsMeter;

/// <summary>
/// `GameCmdExecuter.ActExecutePlayerAutoSkillForPassive(Player player, int index, Vector3 pos)`.
/// Patched MANUALLY from `Plugin.TryPatchAutoSkillActivation` -- deliberately NOT
/// [HarmonyPatch]-annotated: it must not join the PatchAll set (a target that fails to resolve would
/// kill every other hook), the same isolation rule as the crit/power/madness/give probes.
///
/// POSTFIX, not prefix: the command layer returns whether the activation was accepted, so running after
/// it is what makes `ok=` in the row the game's own verdict rather than a prediction.
///
/// Only `__0` (the Player), `__1` (the slot index) and `__result` are declared. The `Vector3` parameter
/// is deliberately NOT materialised: materialising an argument is the part of a detour that has crashed
/// this plugin before (see `Hooks/BattleObjectHooks.cs`), a bool and an int cost nothing to marshal, and
/// nothing in the probe needs the position. The body cannot throw outwards.
/// </summary>
public static class AutoSkillHooks
{
	public static void PostfixAutoSkillForPassive(Player __0, int __1, ref bool __result)
	{
		try { AutoSkillProbe.NoteCommand(__0, __1, __result); }
		catch { }
	}
}
