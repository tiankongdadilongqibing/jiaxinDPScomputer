namespace DpsMeter;

/// <summary>
/// TalentActionAddMadness.ActExecute(BattleObject owner, BattleObject guest, TalentOption option).
/// Patched MANUALLY from Plugin.TryPatchMadnessApplier -- deliberately NOT [HarmonyPatch]-annotated:
/// it must not join the PatchAll set (a target that fails to resolve would kill every other hook),
/// same isolation rule as the crit/power probes.
///
/// POSTFIX, not prefix: the game applies the status first; the row records the ATTEMPT with both
/// unit names. Whether it stuck is the timeline channel's job -- the two channels cross-check each
/// other on the first 1.5.4 battle instead of either one being assumed true.
///
/// Only __0/__1 are materialised; the TalentOption parameter is deliberately not declared. The
/// 1.0.48/1.0.49 ActDamage crash was parameter materialisation in an impossible state -- fewer
/// objects touched is less surface, and the body cannot throw outwards.
/// </summary>
public static class MadnessApplyHook
{
	public static void Postfix(BattleObject __0, BattleObject __1)
	{
		try { StatusApplierProbe.Note(__0, __1); }
		catch { }
	}
}
/// <summary>
/// Give-type actions' ActExecute: TalentActionAddTalent (its child AddTalentDuplicate inherits the
/// same method) and TalentActionAddTalentLottery. Manually patched from Plugin.TryPatchGiveApplier,
/// one isolated patch per class so a class that does not resolve cannot take the others down.
/// POSTFIX: by the time it runs the grant entry exists on the target.
/// </summary>
public static class GiveTalentApplyHooks
{
	public static void PostfixAddTalent(BattleObject __0, BattleObject __1)
	{
		try { GiveApplierProbe.Note("addTalent", __0, __1); }
		catch { }
	}

	public static void PostfixAddTalentLottery(BattleObject __0, BattleObject __1)
	{
		try { GiveApplierProbe.Note("lottery", __0, __1); }
		catch { }
	}
}