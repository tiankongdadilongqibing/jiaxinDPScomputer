using System.Collections.Generic;

namespace DpsMeter;

// Minimal stand-ins for the two game-side types CalcReconcile reads. They deliberately expose ONLY what
// Output/CalcReconcile.cs touches, so this probe cannot accidentally depend on the plugin's game surface.
public sealed class BattleEvent
{
	public double T;
	public string Type;      // "dmg" | "heal"
	public long Amount;      // damage that reached 耐久
	public long Nominal;     // BattleObject.Damage's argument (== Amount unless part was absorbed)
	public CalcBreakdown Calc;
	/// <summary>1.5.0 (A3): the game's own crit flag, 0 = not observed / 1 = not a crit / 2 = a crit.
	/// Added here because CalcReconcile.Compute now compares its crit INFERENCE against this observation;
	/// the stub is what makes that new dependency explicit instead of accidental.</summary>
	public byte CritObserved;
}

public sealed class BattleSession
{
	public double ActiveSeconds;
	public readonly List<BattleEvent> Events = new List<BattleEvent>();
}

// 1.7.2 (阶段 G) stand-ins for Diagnostics/ParamOwnerProbe.cs. That file is compiled into THIS probe so
// the owner channel's counters and its emitted JSON are EXECUTED, not eyeballed -- a battle cannot prove
// that a duplicate entry merges into the union or that an unreadable owner is emitted as null. Only the
// members the file actually touches are declared, so the probe cannot drift onto the plugin's game side.
public sealed class ConfigEntry<T>
{
	public T Value;
}

public static class Plugin
{
	public static ConfigEntry<bool> CfgParamOwners;
}

public static class Aggregator
{
	public static BattleSession Session;
}
