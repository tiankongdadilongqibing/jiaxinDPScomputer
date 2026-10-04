using HarmonyLib;

namespace DpsMeter;

// Every patch on the game's DamageCalculater lives in this file: the composition pipeline depends on
// all of them firing in the right order, so they belong together.
//
// Which path carries the damage (measured over one battle):
//   ApplyEnchantDamage   1096 hits, essentially the whole battle's damage  <- DOMINANT PATH
//   DamageAction          139 hits
//   ApplyBarrierDamage    barrier / enchant damage
//   ActDamageAction       the "Act" (action-side) variant
//   Action                one action/cast starts -> Aggregator.NoteActiveCalc takes the
//                         ATTACK-START snapshot (HP% + statuses) here; see CompositionProbe.Status.cs
//   GetFlyTextNumberSizeForAttack
//                         receives the attribute rate and the crit flag, i.e. the two factors that
//                         are not readable from stats (see CompositionProbe.Crit.cs)
//   ReflectDamage         counter damage (tallied only)
//
// Feedback loop warning: none of these hooks may read damage AFTER the hit has been applied when the
// judgement belongs to the attack start (statuses, 耐久 tiers). Use the snapshot API.

// void Action() -- one damage calculation begins.
[HarmonyPatch(typeof(DamageCalculater), "Action")]
public static class CalcActionHook
{
	public static void Prefix(DamageCalculater __instance)
	{
		try
		{
			Aggregator.NoteActiveCalc(__instance);
		}
		catch
		{
		}
	}
}

// int DamageAction(BattleObject blocker, int power)
[HarmonyPatch(typeof(DamageCalculater), "DamageAction")]
public static class CalcDamageActionHook
{
	public static void Postfix(DamageCalculater __instance, BattleObject __0, int __1, int __result)
	{
		try
		{
			if (__result > 0) Probe.HitSum("Calc.DamageAction", __result);
			if (GameRef.IsNull(__instance.Attacker)) Probe.Hit("Calc.DamageAction(noAttacker)");
			if (__result > 0)
			{
				Aggregator.NoteCalcActivity(__instance, __instance.Attacker, __instance.m_owner, __0, __result);
				// 1.5.0 (A2): the damage-detail record. Its consumer matches by
				// (attacker, blocker, damage value) with a 0.35 s bound, so the record this call produces
				// is the one that labels THIS hit's source / crit / hit type / effect id.
				Aggregator.NoteHitDetail(__instance, __0, __result);
				CompositionProbe.Log(__instance, __0, __result);
			}
		}
		catch { }
	}
}

// int ActDamageAction(BattleObject blocker, int power)
[HarmonyPatch(typeof(DamageCalculater), "ActDamageAction")]
public static class CalcActDamageActionHook
{
	public static void Postfix(DamageCalculater __instance, BattleObject __0, int __result)
	{
		try
		{
			if (__result > 0) Probe.HitSum("Calc.ActDamageAction", __result);
			if (GameRef.IsNull(__instance.Attacker)) Probe.Hit("Calc.ActDamageAction(noAttacker)");
			if (__result > 0)
			{
				Aggregator.NoteCalcActivity(__instance, __instance.Attacker, __instance.m_owner, __0, __result);
				Aggregator.NoteHitDetail(__instance, __0, __result);
			}
		}
		catch { }
	}
}

// int ApplyBarrierDamage(int power)
[HarmonyPatch(typeof(DamageCalculater), "ApplyBarrierDamage")]
public static class CalcBarrierHook
{
	public static void Postfix(DamageCalculater __instance, int __result)
	{
		try
		{
			if (__result > 0) Probe.HitSum("Calc.ApplyBarrierDamage", __result);
			Probe.NoteCalc(__instance, __result, "Calc.ApplyBarrierDamage");
		}
		catch { }
	}
}

// int ApplyEnchantDamage(int power)
//
// Without this hook the majority of damage reaches the meter with no composition at all: the pipeline
// used to hang off DamageAction alone.
[HarmonyPatch(typeof(DamageCalculater), "ApplyEnchantDamage")]
public static class CalcEnchantHook
{
	public static void Postfix(DamageCalculater __instance, int __result)
	{
		try
		{
			if (__result > 0) Probe.HitSum("Calc.ApplyEnchantDamage", __result);
			Probe.NoteCalc(__instance, __result, "Calc.ApplyEnchantDamage");
		}
		catch { }
	}
}

// FlyText.eSizeType GetFlyTextNumberSizeForAttack(float attributeRate, bool isCritical, bool isDefenseGreaterThanAttack)
//
// NOT annotated with [HarmonyPatch]: it is patched manually from Plugin.TryPatchCritProbe, because
// PatchAll aborts the WHOLE patch set when a single target fails to resolve -- a diagnostic probe must
// never be able to disable the meter.
public static class CalcFlyTextSizeHook
{
	public static void Postfix(DamageCalculater __instance, float __0, bool __1, bool __2)
	{
		try
		{
			Probe.Hit("Calc.GetFlyTextNumberSizeForAttack");
			if (__1) Probe.Hit("Calc.GetFlyTextNumberSizeForAttack(crit)");
			if (__0 > 1.5f) Probe.Hit("Calc.GetFlyTextNumberSizeForAttack(attrAdvantage)");
			CompositionProbe.NoteCrit(__instance, __0, __1, __2);
		}
		catch { }
	}
}

// void ReflectDamage(BattleObject blocker, int point)
[HarmonyPatch(typeof(DamageCalculater), "ReflectDamage")]
public static class CalcReflectHook
{
	public static void Postfix(BattleObject __0, int __1)
	{
		try { Probe.HitSum("Calc.ReflectDamage", __1); } catch { }
	}
}

// DamageCalculater constructors / factory
//
// NONE of these are annotated with [HarmonyPatch]: all are patched manually from Plugin.TryPatchPowerProbe
// (same reason as the crit probe -- a constructor whose signature fails to resolve must not be able to abort
// the whole patch set). They exist to answer "which attack value built 计算威力?": ctor1's PREFIX runs before
// the body computes it, so the attacker's Power read there should be the exact value. Measured 2026-09-27:
// ctor1 is never called during combat (heartbeat ctor1=0), so the others are probed too and the [POWERHOOK]
// heartbeat reports which overload the game actually uses. See Diagnostics/PowerProbe.cs.
public static class CalcCtorProbeHook
{
	public static void Prefix(DamageCalculater __instance, BattleObject __0)
	{
		try { PowerProbe.NoteCtor(__instance, __0); } catch { }
	}
}

public static class CalcCtor2ProbeHook
{
	// (DamageSource, int damage, AttributeModel, AttributeModel, BattleObject blocker, eDamageCalcType,
	//  bool draw, int hitRate, BattleObject attacker, BattleObject owner)
	public static void Prefix(DamageCalculater __instance, BattleObject __8)
	{
		try
		{
			PowerProbe.NoteOtherCtor(2);
			PowerProbe.NoteCtor(__instance, __8);
		}
		catch { }
	}
}

public static class CalcCtor3ProbeHook
{
	// (int effectId, Nullable<int> effectSize, Nullable<int> effectSizeCorrection, int waitTime,
	//  bool talentAcceptFlag, TalentExecutor attackerTalentExecutor)
	public static void Prefix(DamageCalculater __instance)
	{
		try
		{
			PowerProbe.NoteOtherCtor(3);
			PowerProbe.NoteCtor(__instance, null);
		}
		catch { }
	}
}

public static class CalcAddBlockerProbeHook
{
	public static void Prefix(DamageCalculater __instance)
	{
		try { PowerProbe.NoteOtherCtor(4); } catch { }
	}
}
