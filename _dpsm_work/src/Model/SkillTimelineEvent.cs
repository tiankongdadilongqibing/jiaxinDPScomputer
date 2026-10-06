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
	/// auto-skill command entry point) or <see cref="ChannelRecord"/> (the game's skill-record sink, which
	/// is the only channel that also covers 奥义/特殊技能). Two channels may report the SAME activation,
	/// which is what the merge window in the policy exists for.</summary>
	internal string Channel = ChannelCommand;

	/// <summary>The game's `eUserRecordType` value for record-channel rows, 0 for command rows. Kept so
	/// the raw evidence can be audited without re-deriving which record type meant "started". The `= 0` is
	/// explicit because this model is also compiled into the behaviour tests, where the probe that fills it
	/// is not present -- and a warning-free build is a rule of this repository.</summary>
	internal int RecordType = 0;

	/// <summary>Real seconds since the session's first tick.</summary>
	internal double Wall;

	/// <summary>Battle-clock seconds (`Session.ActiveSeconds`).</summary>
	internal double Active;

	internal const string ChannelCommand = "cmd";
	internal const string ChannelRecord = "rec";
}
