using System;

namespace DpsMeter;

/// <summary>
/// FEATURE (R63): convert a master-data skill cooldown from the MASTER's unit into the game's.
///
/// WHY THIS EXISTS
/// `Rog.MasterData.SkillMasterDataBase` -- and therefore `AutoSkillMasterData` -- stores its cooldown as
/// an int in SECONDS (`minCoolTime` / `maxCoolTime` / `minFirstCoolTime` / `maxFirstCoolTime`), while the
/// live `Skill` counts game UPDATES in `m_coolTimeFrame` / `m_waitCountFrame`. Measured 2026-09-27
/// (`Plugin.cs`:344, `Diagnostics/TimeProbe.cs`): `Skill.CoolTimeFrame / Skill.CoolTime = 30.0` for every
/// skill (750/25, 1500/50, 1050/35) -- 30 units per game second -- and `BattleClockPolicy`
/// (`DefaultUnitsPerGameSecond`) is where the plugin already keeps that number.
///
/// Publishing only the master's seconds invites exactly the mistake the dump exists to prevent: comparing
/// `minCoolTime = 14` with a frame counter, or with a battle time in game seconds without conversion.
/// So the dump emits BOTH -- the master's own seconds verbatim, and the frame count beside it.
///
/// Pure (no Unity, no IL2CPP, no file system), which is why BehaviorTests can execute it; see
/// `tests/BehaviorTests/Cases.SkillCooldown.cs`, and the `skill-cooldown-*` negative control that makes
/// the conversion's named case go red.
/// </summary>
public static class SkillCooldownPolicy
{
	/// <summary>Game updates per game second used until the live value has been measured.</summary>
	public const double FallbackUnitsPerGameSecond = BattleClockPolicy.DefaultUnitsPerGameSecond;

	/// <summary>
	/// The frame count the game's own cooldown counter would hold for a master value of `seconds`.
	///
	/// Returns 0 for a non-positive `seconds` AND for an unusable `unitsPerGameSecond`: both mean "there is
	/// no number to publish", which must never be confused with "a cooldown of zero frames" (a real
	/// instant skill). The caller publishes the master's seconds unconditionally, so nothing is lost.
	/// Rounds half away from zero so the number matches the way a reader would convert by hand.
	/// </summary>
	public static int Frames(int seconds, double unitsPerGameSecond)
	{
		if (seconds <= 0 || unitsPerGameSecond <= 0.0 || double.IsNaN(unitsPerGameSecond)) return 0;
		double f = seconds * unitsPerGameSecond;
		if (f > int.MaxValue) return 0;
		return (int)Math.Round(f, MidpointRounding.AwayFromZero);
	}

	/// <summary>The same conversion at the plugin's configured default unit rate.</summary>
	public static int Frames(int seconds)
	{
		return Frames(seconds, FallbackUnitsPerGameSecond);
	}
}