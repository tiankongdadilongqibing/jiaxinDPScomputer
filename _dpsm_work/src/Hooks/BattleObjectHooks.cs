using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DpsMeter;

// Patches on BattleObject: the two damage/heal entry points the meter actually counts, and the unit
// creation hook that registers battle-wide damage rules.

/// <summary>
/// BattleObject.Damage(int damage, BattleObject attacker, BattleObject owner, bool isIgnoreAbility): int
/// Final damage-application entry used by direct hits. Some code paths pass attacker==null
/// (contact / field / dot style damage); Aggregator resolves those from the recent calc activity and
/// counts whatever is left as 未归属.
///
/// The ARGUMENT and the RETURN are two different quantities and BOTH are needed:
///   __0     = the damage the game accounts for (CharacterStatistics.TakenDamage accumulates this)
///   result  = the damage that actually reached 耐久 (absorbed / nullified damage is subtracted)
/// Measured 2026-09-27: per-victim totals from the return matched the game's own counter exactly for
/// T.O.W.E.R.typeR (12,704,393), レヴナント (3,075,278), 城塞 (417,072) and ポポロット (13,169) --
/// only [痺夏]シゼル＝メ was short, by exactly one hit (420,942 vs a recorded 198). So the return is
/// right for normal hits and the difference is 被吸收/无效化, not a missing hook.
/// </summary>
[HarmonyPatch(typeof(BattleObject), "Damage")]
public static class DamageHook
{
	/// <summary>
	/// Snapshot the victim's status ailments BEFORE the hit, so the Postfix can diff them and answer
	/// "did this damage record inflict an 異常状態?". See Diagnostics/StatusDeltaProbe.cs.
	/// </summary>
	public static void Prefix(BattleObject __instance)
	{
		try
		{
			StatusDeltaProbe.NoteBefore(__instance);
		}
		catch { }
	}

	public static void Postfix(BattleObject __instance, int __0, BattleObject __1, BattleObject __2, int __result)
	{
		try
		{
			int damage = (__result > 0) ? __result : __0;
			Aggregator.RecordDamage(__instance, __1, __2, damage, __0);
		}
		catch { }
	}
}

// BattleObject.ActDamage(int damage, BattleObject attacker, bool isIgnoreAbility) : void
//
// The SECOND damage entry point on BattleObject and the only one with no return value. It was probed in
// 1.0.48/1.0.49 and the probe was REMOVED in 1.0.50: the game calls ActDamage in states where
// Il2CppInterop cannot materialise its object arguments, so the detour dies inside
// Il2CppInterop.Runtime.Runtime.Il2CppObjectPool.Get -> il2cpp_object_get_class with an uncatchable
// AccessViolationException -- the game then crashes on startup. Making the postfix body inert does NOT
// help: the crash happens while the generated (il2cpp -> managed) thunk converts the IntPtr parameters,
// i.e. BEFORE the body runs.
//
// Evidence (BepInEx\ErrorLog.log, v1.0.49):
//   Fatal error. System.AccessViolationException
//   Repeat 2 times:
//      at Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(IntPtr)
//      at Il2CppInterop.Runtime.Runtime.Il2CppObjectPool.Get[...](IntPtr)
//      at DynamicClass.(il2cpp -> managed) ActDamage(IntPtr, Int32, IntPtr, Byte, Il2CppMethodInfo*)
//
// Do NOT re-add this patch. If a residual damage gap ever shows up in [CROSS]
// (nominal_taken != game_taken per unit), the experiment to try is an IntPtr-ONLY postfix
// (no BattleObject parameters, so no pooling); the decisive test that needs no patch at all is the
// [CROSS] reconciliation itself.

/// <summary>
/// BattleObject.Heal(int value, BattleObject healer, BattleObject owner)
/// The heuristic flags on if no change in Life is seen, so the prefix captures the pre-heal 耐久 and
/// the postfix reports the REAL healed amount (overheal is not counted).
/// </summary>
[HarmonyPatch(typeof(BattleObject), "Heal")]
public static class HealHook
{
	private static readonly Dictionary<BattleObject, int> BeforeLife = new Dictionary<BattleObject, int>();

	public static void Prefix(BattleObject __instance)
	{
		try
		{
			BeforeLife[__instance] = __instance.Life;
		}
		catch
		{
		}
	}

	public static void Postfix(BattleObject __instance, int __0, BattleObject __1, BattleObject __2)
	{
		try
		{
			int value;
			int num = (BeforeLife.TryGetValue(__instance, out value) ? value : (-1));
			BeforeLife.Remove(__instance);
			int actual = __0;
			if (num >= 0)
			{
				try
				{
					int num2 = __instance.Life - num;
					actual = ((num2 > 0) ? num2 : 0);
				}
				catch
				{
				}
			}
			BattleObject healer = ((!GameRef.IsNull(__2)) ? __2 : __1);
			Aggregator.RecordHeal(__instance, healer, actual, __0);
		}
		catch { }
	}
}

// void BattleObject.SetupAbility()
//
// Called for every unit as it is created (players, enemies, tokens, citadel). Collecting the
// battle-wide damage rules here means a "編成時、味方ヴァイスの魔法攻撃の与ダメージ+15%" style buff is known from
// the START of the battle -- registering lazily (when the owner first attacks or is hit) left every
// allied hit before that without the multiplier.
//
// NOTE: units are created during scene load, i.e. BEFORE the next battle session starts, which is why
// the rule table is never cleared when a battle is finalised (see CompositionProbe.GlobalRules.cs).
[HarmonyPatch(typeof(BattleObject), "SetupAbility")]
public static class SetupAbilityHook
{
	public static void Postfix(BattleObject __instance)
	{
		try { CompositionProbe.RegisterGlobalDebuffs(__instance); } catch { }
	}
}
