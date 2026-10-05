using System.Collections.Generic;

// RF1 test surface. Every type here stands in for a game-side or plugin-side dependency of the
// REAL production files listed in BehaviorTests.csproj. The rule is: declare the smallest member
// set that compiles, and make the semantics match production for the paths under test
// (GameRef.Same is reference identity, which is what the IL2CPP pointer comparison reduces to for
// a live object; Time.unscaledTime is a settable float so cache-staleness can be driven).
//
// A stub that grows a convenience member the production file does not use is a smell: it means the
// test is drifting onto a fake game surface.

namespace UnityEngine
{
	/// <summary>Only UnityEngine.Time is touched by the compiled sources (ContributionSession).</summary>
	public static class Time
	{
		/// <summary>Settable: the cache tests advance this to cross the 1 s refresh clause.</summary>
		public static float unscaledTime;
	}
}

public enum TeamType { Unknown = 0, Ally = 1, Enemy = 2 }

public enum GameResult { None = 0, Win = 1, Lose = 2 }

/// <summary>Stand-in for the IL2CPP BattleObject. Two members suffice for BattleSession/HitRecord.</summary>
public class BattleObject
{
	public TeamType TeamType;
	public BattleObject TokenOwner;
	public string DebugName = "";
	public override string ToString() { return DebugName; }
}

namespace DpsMeter
{
	/// <summary>Reference-identity stand-in for the IL2CPP wrapper comparison.</summary>
	public static class GameRef
	{
		public static bool IsNull(object o) { return o == null; }
		public static bool IsAlive(object o) { return !IsNull(o); }
		public static bool Same(object a, object b) { return ReferenceEquals(a, b); }
	}

	/// <summary>Names are NOT under test here: the window/clock cases must not depend on them.</summary>
	public static class CharacterInfo
	{
		public static string DisplayName(BattleObject b) { return b == null ? "" : b.DebugName; }
		public static string KindLabel(BattleObject b) { return "T"; }
		public static bool IsAllyTeam(TeamType t) { return t == TeamType.Ally; }
	}

	public enum DamageSource { Unknown = 0, DirectAttack = 1, Dot = 3, DirectHeal = 8 }

	public enum eDamageCalcType { None = 0, Attack = 1, Heal = 2 }

	/// <summary>R52: the grant-channel fields ContributionSession reads from a talent (Type/P0/Cond).
	/// The production TalentRef also carries Index/P1/P2/Timing/Live/Last/Delta -- they are NOT declared
	/// here because no compiled source touches them, and a convenience member would be drift onto a fake
	/// game surface (see the file header).</summary>
	public sealed class TalentRef
	{
		public int Type;
		public int P0;
		public string Cond = "";
	}

	public sealed class RosterAbility
	{
		public int Id;
		public string Name = "";
		public readonly List<TalentRef> Talents = new List<TalentRef>();
	}

	public sealed class TalentUsage
	{
		public int AbilityId;
		public string Ability = "";
	}

	public sealed class ConfigEntry<T>
	{
		public T Value;
	}

	public static class Plugin
	{
		public static ConfigEntry<bool> CfgParamOwners;
		public static ConfigEntry<bool> CfgJsonPretty;
	}

	/// <summary>ContributionSession reads Aggregator.Session and nothing else.</summary>
	public static class Aggregator
	{
		public static BattleSession Session;
	}
}
