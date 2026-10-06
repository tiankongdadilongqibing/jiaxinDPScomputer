namespace DpsMeter;

/// <summary>
/// R66: ONE observed skill activation of ONE of our units, in the two clocks a battle already carries.
///
/// WHY A MODEL TYPE AND NOT A PROBE-INTERNAL STRUCT. The overlay's 技能时间表 page and the battle-end
/// `[SKILLTL]` log lines must show the SAME activations, and the arithmetic that turns a raw activation
/// stream into that table (merge window, grouping, median interval, ordering) has to be executable
/// offline. Keeping the row itself game-free is what lets <see cref="SkillTimelinePolicy"/> and
/// <see cref="SkillTimelineText"/> be compiled into BehaviorTests, so the table can be tested instead of
/// eyeballed in a battle.
///
/// The two clocks are the ones every other probe prints:
///   * <see cref="Wall"/>   -- real seconds since the session's first tick (pauses included);
///   * <see cref="Active"/> -- the battle clock (`BattleSession.ActiveSeconds`), i.e. the axis the
///     export's `events[].t` uses. **This is the axis the timetable is built on**, because the game's
///     own cadence is regular on it and the wall clock carries the game's pauses (measured 2026-10-06:
///     one トレイラ interval was 8 s on the battle clock and 20 s on the wall clock).
/// </summary>
internal sealed class SkillTimelineEvent
{
	/// <summary>Display name of our unit (`Player.Name`, spaces replaced).</summary>
	internal string Unit = "";

	/// <summary>Skill name (`Skill.Name`), spaces replaced.</summary>
	internal string Skill = "";

	/// <summary>`Skill.Type`: 0 Skill, 1 SpecialSkill, 2 OverSkill, 3 AutoSkill1ForPassiveSkill,
	/// 4 AutoSkill2ForPassiveSkill; -1 = the field could not be read.</summary>
	internal int Type = -1;

	/// <summary>Which observation channel produced this row: <see cref="ChannelCommand"/> (the game's own
	/// auto-skill command entry point) or <see cref="ChannelSkillCommand"/> (the active/special command
	/// entry points, added in R67). Two channels may report the SAME skill, which is what the fold in the
	/// policy exists for. R69 deleted the third channel (`rec`, the game's skill-record sink): it was
	/// patched and never called, in two battles, one of them a real quest.</summary>
	internal string Channel = ChannelCommand;

	/// <summary>Real seconds since the session's first tick.</summary>
	internal double Wall;

	/// <summary>Battle-clock seconds (`Session.ActiveSeconds`).</summary>
	internal double Active;

	/// <summary>R69: this skill's own cooldown in GAME seconds (`Skill.CoolTime`, which equals
	/// `Skill.CoolTimeFrame / 30` on every one of the 568 measured command rows -- verified 2026-10-06, 0
	/// mismatches). It is the physics of the fold: the game CANNOT execute the same skill twice inside its
	/// own cooldown, so two `Using` calls closer than this belong to ONE execution.
	///
	/// 0 = the field could not be read (or the skill really has no cooldown). That is NOT "fold nothing":
	/// <see cref="SkillTimelinePolicy"/> then falls back to its fixed floor, so an unreadable cooldown can
	/// only ever fold the same-instant double reports it folded before R69.</summary>
	internal double CoolSeconds;

	internal const string ChannelCommand = "cmd";

	/// <summary>R67: the game's command entry points for the ACTIVE/SPECIAL skills
	/// (`GameCmdExecuter.ActExecutePlayer{ActiveSkill,Skill,SpecialSkill}`) -- the same layer as the proven
	/// auto-skill command hook. Added because the record sink was installed and NEVER CALLED in a full
	/// battle (`rec=0` with every skip counter 0), so 奥义/特殊 had no working channel.</summary>
	internal const string ChannelSkillCommand = "skl";
}
