using HarmonyLib;

namespace DpsMeter;

// R75: the CARRIER hooks -- the sightings the classifier is allowed to name a mechanism from.
//
// ALL OF THEM ARE DARK BY DEFAULT (`Debug/AbsorbProbeHooks = false`), and for the same reason the crit and
// power probes carry no attribute either: `_harmony.PatchAll()` patches EVERY class in this assembly that has
// a `[HarmonyPatch]` attribute, so an attribute here would install these carrier hooks no matter what the
// config says. Instead they are patched one by one from `Plugin.TryPatchAbsorbCarrierHooks`, each in its own
// try/catch, so a target that fails to resolve (or a detour the runtime refuses) cannot disable the meter.
//
// WHY THE ISOLATION IS NOT PARANOIA (measured, not feared). 1.0.48/1.0.49 shipped a patch on
// `BattleObject.ActDamage` and the game died at startup with
// `System.AccessViolationException ... at Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(IntPtr)` raised
// while the generated (il2cpp -> managed) thunk CONVERTED the arguments -- i.e. before any postfix body ran,
// and making the body inert did not help. `Hooks/BattleObjectHooks.cs` carries the full note and forbids
// re-adding that patch. The rule kept here: every postfix below declares ONLY its `__instance` and, where it
// needs a number, the INT argument. That lowers what the thunk has to materialise; it does not remove the
// risk, which is exactly why the opt-in switch -- not the signature -- is what makes this safe to ship.
//
// WHAT EACH ONE ANSWERS
//   Barrier.Activate               -> a barrier appeared (and with what value)
//   Barrier.Deactivate             -> a barrier was consumed/expired (pool vs per-hit)
//   Barrier.CalcLife (static)      -> the number a barrier is SET UP with: 500,000 here would name the carrier
//   Barrier.Damage                 -> the barrier absorbing inside a hit: the deciding sighting for H1
//   TalentActionAddBarrier         -> WHO grants barriers (a talent, i.e. a source that can be named)
//   BattleObject.DamageTakeOver    -> damage redirected to another object (the boss is ~190 objects)
//   BattleObject.TryGetFixedDamage -> a fixed-damage override (expected absent; kept so it is closed by a
//                                     reading rather than by its absence from the master tables)
//
// The member names in the comments are the ones `tests/IlDump` prints for
// `BepInEx/interop/Assembly-CSharp.dll` (`Barrier`, `TalentActionAddBarrier`, `BattleObject`). No line
// numbers are quoted because an interop member has none.

// void Barrier.Activate(BattleObject, ReferenceType, int)
// interop: NativeMethodInfoPtr_Activate_Public_Void_BattleObject_ReferenceType_Int32_0
public static class AbsorbBarrierActivateHook
{
	public static void Postfix(Barrier __instance, int __2)
	{
		try
		{
			// A barrier appearing IS the "granted" event, whichever route produced it (this or the talent),
			// so both feed the same bucket: `seen(.../addBarrier/...)` answers "did a barrier ever exist in
			// this battle" even when the per-hit sighting is missed.
			AbsorbProbe.SawAddBarrier();
			AbsorbProbe.LogCarrier("barrier", "activate val=" + __2 + " life=" + AbsorbProbe.BarrierLife(__instance));
		}
		catch { }
	}
}

// void Barrier.Deactivate()
// interop: NativeMethodInfoPtr_Deactivate_Public_Void_0
public static class AbsorbBarrierDeactivateHook
{
	public static void Postfix(Barrier __instance)
	{
		try
		{
			AbsorbProbe.LogCarrier("barrier", "deactivate life=" + AbsorbProbe.BarrierLife(__instance));
		}
		catch { }
	}
}

// int Barrier.CalcLife(BattleObject, ReferenceType, int) -- private static
// interop: NativeMethodInfoPtr_CalcLife_Private_Static_Int32_BattleObject_ReferenceType_Int32_0
public static class AbsorbBarrierCalcLifeHook
{
	public static void Postfix(int __result)
	{
		try
		{
			AbsorbProbe.LogCarrier("barrier", "calclife=" + __result);
		}
		catch { }
	}
}

// void Barrier.Damage(int, BattleObject)
// interop: NativeMethodInfoPtr_Damage_Public_Void_Int32_BattleObject_0
//
// This is the sighting that decides H1 (barrier) over H2 (flat cut): if a 500,000 withholding has no
// `bdmg=1` beside it, no barrier took part in that hit.
public static class AbsorbBarrierDamageHook
{
	public static void Postfix(Barrier __instance, int __0)
	{
		try
		{
			AbsorbProbe.SawBarrierDamage();
			AbsorbProbe.LogCarrier("bdmg", "in=" + __0 + " life=" + AbsorbProbe.BarrierLife(__instance));
		}
		catch { }
	}
}

// void TalentActionAddBarrier.ActExecute(BattleObject, BattleObject, TalentOption)
// interop: NativeMethodInfoPtr_ActExecute_Protected_Virtual_Void_BattleObject_BattleObject_TalentOption_0
//
// Only `__instance` is declared, so the granter's NAME is not printed: naming it would mean declaring two
// `BattleObject` parameters, which is the shape that crashed the process in 1.0.48. The identity stays
// recoverable from the `[ABIL]`/talent rows around the same timestamp -- `TalentActionAddBarrier` is the
// `044 AddBarrier` member of the talent action `Type` enum printed there.
public static class AbsorbAddBarrierTalentHook
{
	public static void Postfix(TalentActionAddBarrier __instance)
	{
		try
		{
			AbsorbProbe.SawAddBarrier();
			AbsorbProbe.LogCarrier("grant", "addBarrier");
		}
		catch { }
	}
}

// int BattleObject.DamageTakeOver(int, BattleObject, BattleObject, eDamageCalcType, eSizeType)
// interop: NativeMethodInfoPtr_DamageTakeOver_Public_Int32_Int32_BattleObject_BattleObject_eDamageCalcType_eSizeType_0
//
// The boss exists as ~190 separate `ショゴス` objects in one battle, so "damage handed to another object" is a
// live alternative to absorption -- and from the two numbers alone it looks identical.
public static class AbsorbTakeOverHook
{
	public static void Postfix(BattleObject __instance, int __0)
	{
		try
		{
			AbsorbProbe.SawTakeOver();
			AbsorbProbe.LogCarrier("takeover", "in=" + __0);
		}
		catch { }
	}
}

// void BattleObject.AddDamageTakeOverChara(BattleObject, int, eDamageCalcType)
// interop: NativeMethodInfoPtr_AddDamageTakeOverChara_Public_Void_BattleObject_Int32_eDamageCalcType_0
public static class AbsorbTakeOverLinkHook
{
	public static void Postfix(BattleObject __instance, int __1)
	{
		try
		{
			AbsorbProbe.LogCarrier("takeover", "link in=" + __1);
		}
		catch { }
	}
}

// bool BattleObject.TryGetFixedDamage(out int)
// interop: NativeMethodInfoPtr_TryGetFixedDamage_Public_Boolean_byref_Int32_0
public static class AbsorbFixedDamageHook
{
	public static void Postfix(bool __result)
	{
		try
		{
			if (!__result) return;
			AbsorbProbe.SawFixedDamage();
			AbsorbProbe.LogCarrier("fixed", "hit");
		}
		catch { }
	}
}
