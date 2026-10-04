using System;
using System.Collections.Generic;
using System.Text;

namespace DpsMeter;

/// <summary>
/// PROBE (diagnostics): tallies how often each candidate damage path fires and how often it lacks an
/// attacker reference. Used to find where "enemy damage without attribution" comes from.
///
/// Session-reset by Aggregator.StartSession, dumped by FinalizeLocked when Debug/TraceCandidates is on.
/// The hooks that feed it live in Hooks/DamageCalculaterHooks.cs.
/// </summary>
public static class Probe
{
	private static readonly Dictionary<string, long> Counts = new Dictionary<string, long>();
	private static readonly Dictionary<string, long> Sums = new Dictionary<string, long>();

	public static void Reset()
	{
		Counts.Clear();
		Sums.Clear();
	}

	public static void Hit(string key)
	{
		Counts.TryGetValue(key, out var c);
		Counts[key] = c + 1;
	}

	public static void HitSum(string key, long amount)
	{
		Counts.TryGetValue(key, out var c);
		Counts[key] = c + 1;
		Sums.TryGetValue(key, out var s);
		Sums[key] = s + amount;
	}

	public static void DumpAll()
	{
		StringBuilder sb = new StringBuilder();
		sb.Append("[DpsMeter][PROBE] === candidate path tallies ===");
		foreach (var kv in Counts)
		{
			sb.Append($"\n  {kv.Key,-34} x{kv.Value,7}");
			if (Sums.TryGetValue(kv.Key, out var s)) sb.Append($"  sum={s,10}");
		}
		string text = sb.ToString();
		Plugin.LogSource.LogInfo(text);
		RuntimeLog.Write(text);
	}

	/// <summary>
	/// Feed a damage-calculation path into the composition + attribution pipeline.
	/// Without this, damage applied through paths other than DamageAction (barrier/enchant)
	/// reaches the meter with no composition at all.
	/// </summary>
	internal static void NoteCalc(DamageCalculater calc, int damage, string tag)
	{
		try
		{
			if (damage <= 0) return;
			BattleObject blocker = null;
			try { blocker = calc.m_blocker; } catch { }
			Aggregator.NoteCalcActivity(calc, calc.Attacker, calc.m_owner, blocker, damage);
			// 1.5.0 (A2): these two entry points (`ApplyBarrierDamage` / `ApplyEnchantDamage`) take only
			// the power, so the blocker comes from the calc -- and they carry most of a battle's damage
			// (measured 1096 of one battle's hits through ApplyEnchantDamage alone). Wired here rather
			// than in each hook so the two cannot drift apart.
			Aggregator.NoteHitDetail(calc, blocker, damage);
			if (Plugin.CfgDamageComposition != null && Plugin.CfgDamageComposition.Value)
			{
				Probe.Hit(tag + "(comp)");
				CompositionProbe.Log(calc, blocker, damage);
			}
		}
		catch { }
	}
}
