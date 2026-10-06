namespace DpsMeter;

/// <summary>
/// R69: the verdict that decides whether ONE call of a skill command entry point was a real ACTIVATION or
/// only an ATTEMPT. This is the rule R66/R67 were missing, and getting it wrong is how the 技能时间表 came
/// to show マッドシーカー's auto skill firing every ~5 s while the game's own description says 99 s.
///
/// THE EVIDENCE (2026-10-06, one 119.07 s battle, `[AUTOSK] act` rows joined to the `[AUTOSK] chg` samples):
///   * `GameCmdExecuter.ActExecutePlayerAutoSkillForPassive` is called by the PASSIVE every time the unit
///     attacks -- 25 times for マッドシーカー in that battle -- and it returns `ok=1` every time. The
///     command return value therefore says NOTHING about whether the skill fired (unlike
///     `ActExecutePlayerSkill`, whose `false` is the game's own rejection: 554 of 595 calls in the same
///     battle).
///   * The counter resolves it. Her slot 1 counter drains 2970 -> 0 once (frames; 2970 / 30 = 99 game s)
///     and resets at active=101.17 s. Of her 25 calls exactly ONE happened while the skill was in the
///     game's own `Using` state with the counter full -- the call at active=99.1 s. The other 24 were
///     `Charge` with a half-drained counter: the passive trying, not the skill firing.
///   * `Skill.GetStatus()` is the game's own state machine (`NotHave / Charge / Usable / Using`), so the
///     verdict is read, not invented. MEASURED: `Using` and "counter full" are the same set of calls
///     (305/305 in the 471-row corpus), and the fold in <see cref="SkillTimelinePolicy"/> is what makes
///     repeated `Using` calls inside ONE execution collapse back into one activation.
///
/// WHY UNKNOWN IS NOT AN ACTIVATION. Fail CLOSED: a status the probe could not read (`?`) or a value that
/// is none of the three known ones must never be published as "this unit fired its 奥义" -- the page would
/// be inventing an event. It is counted separately (`unclassified` in the [SKILLTL]/[AUTOSK] summaries),
/// so "the probe saw a state it does not know" stays visible instead of turning into a fake activation.
/// </summary>
internal static class SkillActivationPolicy
{
	/// <summary>`Skill.Status.Using` -- the game is EXECUTING this skill right now.</summary>
	internal const string StatusUsing = "Using";

	/// <summary>`Skill.Status.Charge` -- the counter is still draining; a call in this state is the passive
	/// asking again, and the game does not fire the skill.</summary>
	internal const string StatusCharge = "Charge";

	/// <summary>`Skill.Status.Usable` -- charged, but the skill has not been executed (it waits for the
	/// unit's next action). A call in this state is still not an execution.</summary>
	internal const string StatusUsable = "Usable";

	/// <summary>True only for the game's own "executing" state.</summary>
	internal static bool IsActivation(string status)
	{
		return string.Equals(status, StatusUsing, System.StringComparison.Ordinal);
	}

	/// <summary>True for the two states that mean "the command was called but the skill did not run".
	/// Anything else (including an unreadable status) is neither: see the class docs.</summary>
	internal static bool IsAttempt(string status)
	{
		return string.Equals(status, StatusCharge, System.StringComparison.Ordinal)
			|| string.Equals(status, StatusUsable, System.StringComparison.Ordinal);
	}
}
