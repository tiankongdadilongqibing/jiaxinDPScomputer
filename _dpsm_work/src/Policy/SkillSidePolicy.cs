namespace DpsMeter;

/// <summary>
/// R70: whose side an observation belongs to -- the verdict R66's 技能时间表 documented but only ever
/// applied to ONE of its three routes.
///
/// WHY IT IS A POLICY AND NOT A FIELD READ. R64-R69 filtered with `CharacterInfo.IsAlly` in the `skl`
/// channel only; the auto-skill command hook and the charge sampler had no test at all, so the page
/// published the enemy's auto skills under a title that says 我方. MEASURED 2026-10-06 (quest 9999, 60.7 s,
/// export `battle_9999_20261006_151957__B-…-001.json`): the page listed ムスクーマ and ネフェスティス, and
/// `actors[].team` puts those two on team 2 ONLY. The verdict now lives here so that the three routes, the
/// contribution surfaces and the export cannot drift apart, and so "team 2 is not ours" is a case the
/// behaviour suite can execute instead of a line nobody can mutate.
///
/// THE CONVENTION IS THE GAME'S OWN, AND IT IS MEASURED, NOT ASSUMED. `1` is our side: the export's
/// `actors[].team` and its `events[].atkTeam/vicTeam` use it, `CharacterInfo.IsAlly` uses it, and in that
/// battle the (1,2) pairs are our hits while (2,1) are the enemy's -- so "team == 1" means "ours" on both
/// the damage side and the skill side.
///
/// A team the plugin cannot read (an exception, or a value the game never produced) is NOT ours: failing
/// closed here keeps an unreadable object out of a table that claims to show our units, which is the same
/// rule <see cref="SkillActivationPolicy"/> applies to an unreadable status.
/// </summary>
internal static class SkillSidePolicy
{
	/// <summary>Our side. The game writes this number into `BattleObject.TeamType`, into the export's
	/// `actors[].team` and into `events[].atkTeam/vicTeam`.</summary>
	internal const int OurTeam = 1;

	/// <summary>True only for our side (see the class docs: anything unreadable fails closed).</summary>
	internal static bool IsOurs(int team)
	{
		return team == OurTeam;
	}
}
