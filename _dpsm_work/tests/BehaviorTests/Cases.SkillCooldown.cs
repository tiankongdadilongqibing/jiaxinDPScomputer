using System;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// R63: the master-data skill cooldown unit conversion, exercised as production code.
	///
	/// Why this group exists at all. The auto-skill master row (`AutoSkillMasterData`) stores its cooldown
	/// in SECONDS while the live `Skill` counts game UPDATES, and 30 units per game second is a MEASURED
	/// constant (750/25, 1500/50, 1050/35 -- `Plugin.cs`:344). Dumping one without the other is how a
	/// reader ends up comparing 14 with a frame counter, so the dump publishes both and the conversion is
	/// pinned here rather than argued about. `MasterDataDump.cs` itself is not in this project's compile
	/// list (it needs the IL2CPP surface); the rule it calls is.
	/// </summary>
	public static void SkillCooldown(Runner r)
	{
		r.Group("policy/skill-cooldown");

		// The three ratios the live game actually reported, run backwards: the loaded skills were
		// CT=25/CTF=750, CT=50/CTF=1500 and CT=30/CTF=900, so a master row of the same second count must
		// come back as the same frame count the game would hold.
		r.Eq("the-master-seconds-become-frames", SkillCooldownPolicy.Frames(14, 30.0), 420);
		r.Eq("the-measured-25s-skill-round-trips", SkillCooldownPolicy.Frames(25, 30.0), 750);
		r.Eq("the-measured-50s-skill-round-trips", SkillCooldownPolicy.Frames(50, 30.0), 1500);
		r.Eq("the-measured-30s-skill-round-trips", SkillCooldownPolicy.Frames(30, 30.0), 900);

		// The rate is a PARAMETER: a process that measured something other than 30 must not silently get
		// the default. (This is the case a "forgot the unit" mutation must redden.)
		r.Eq("a-different-live-rate-is-used-not-the-default", SkillCooldownPolicy.Frames(14, 45.0), 630);
		r.Eq("a-different-live-rate-changes-the-frame-count",
		     SkillCooldownPolicy.Frames(14, 45.0) - SkillCooldownPolicy.Frames(14, 30.0), 210);

		// Half away from zero, so the published number matches a hand conversion.
		r.Eq("half-a-unit-rounds-away-from-zero", SkillCooldownPolicy.Frames(1, 15.5), 16);

		// The parameterless overload is the configured default, not a second literal.
		r.EqD("the-fallback-rate-is-the-clock-policy-default",
		      SkillCooldownPolicy.FallbackUnitsPerGameSecond, BattleClockPolicy.DefaultUnitsPerGameSecond);
		r.EqD("the-fallback-rate-is-thirty", SkillCooldownPolicy.FallbackUnitsPerGameSecond, 30.0);
		r.Eq("the-default-overload-uses-that-rate", SkillCooldownPolicy.Frames(14), 420);

		// "no number to publish" is 0 for every unusable input, and it is distinct from a real 0-second
		// skill -- which the master would express as a non-positive minCoolTime anyway, so the caller
		// keeps the seconds verbatim either way.
		r.Eq("zero-seconds-publish-no-frame-count", SkillCooldownPolicy.Frames(0, 30.0), 0);
		r.Eq("negative-seconds-publish-no-frame-count", SkillCooldownPolicy.Frames(-5, 30.0), 0);
		r.Eq("a-zero-rate-publishes-no-frame-count", SkillCooldownPolicy.Frames(14, 0.0), 0);
		r.Eq("a-negative-rate-publishes-no-frame-count", SkillCooldownPolicy.Frames(14, -1.0), 0);
		r.Eq("a-nan-rate-publishes-no-frame-count", SkillCooldownPolicy.Frames(14, double.NaN), 0);
		r.Eq("an-overflowing-product-publishes-no-frame-count", SkillCooldownPolicy.Frames(int.MaxValue, 30.0), 0);
	}
}