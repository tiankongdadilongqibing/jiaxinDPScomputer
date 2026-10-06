namespace DpsMeter;

/// <summary>
/// R69: how many times ONE of our units called the auto-skill command for ONE of its skills WITHOUT the
/// skill executing. This is the number that explains a wrong cadence, so it is a first-class row and not a
/// counter.
///
/// WHY IT EXISTS. `GameCmdExecuter.ActExecutePlayerAutoSkillForPassive` is the PASSIVE's entry point: it is
/// called once per attack and returns `ok=1` whether or not the skill fires. MEASURED 2026-10-06 (quest
/// 411001): マッドシーカー's 実験失敗！ (counter 2970 frames = 99 game s) was called 25 times in 119 s and
/// executed ONCE -- 1 call while `Using`, 24 while still `Charge`. Publishing all 25 as activations is how
/// the 技能时间表 came to show `med=4.97s` for a 99-second skill. The attempts are therefore counted here,
/// shown next to the real activations (`试N`), and never placed on the time axis.
///
/// NO TIMESTAMP ON PURPOSE. An attempt is not an event in the battle's skill timeline -- giving it one
/// would put it back into the stream the fold and the median read. Only the count is kept, per
/// (unit, skill, type); the individual calls stay in the runtime log as `[AUTOSK] act … status=Charge`
/// rows, which is where a future round would go to study WHEN the passive asks.
/// </summary>
internal sealed class SkillTimelineAttempt
{
	/// <summary>Display name of our unit (`Player.Name`, spaces replaced).</summary>
	internal string Unit = "";

	/// <summary>Skill name (`Skill.Name`), spaces replaced.</summary>
	internal string Skill = "";

	/// <summary>`Skill.Type`, the same value the activation rows carry, so an attempt can be attributed to
	/// the auto slot it belongs to instead of to "one of this unit's skills".</summary>
	internal int Type = -1;

	/// <summary>Command calls that did not execute the skill.</summary>
	internal int Count;
}
