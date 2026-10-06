using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DpsMeter;

/// <summary>
/// R66: collect OUR units' skill activations (奥义 / 特殊 / 自动) and hand the 技能时间表 page its rows.
///
/// WHY THIS EXISTS. R64/R65 could time ONE thing: the auto skill, through the game's own command entry
/// point `GameCmdExecuter.ActExecutePlayerAutoSkillForPassive` (verified over 9 battles / 471 rows). The
/// user's question is broader -- "when did each of my characters fire its 奥义 and its 自动技能" -- and the
/// game has no `ActExecutePlayerOverSkill`: the over skill goes through `Player.ExecuteActiveSkill`, which
/// the plugin must not patch (the 1.0.48 crash came from a detour body reading a half-built object).
///
/// THE SECOND CHANNEL, AND WHY IT IS A RECORD AND NOT AN EXECUTION.
/// The four `GameCmdExecuter.AddPlayer*GameRecord` methods are the game's own "I am writing down that this
/// skill was used" sink, and the unified one is
/// `AddPlayerSkillGameRecord(Player player, Skill skill, eUserRecordType type, Vector3 pos)`: it carries the
/// `Skill` OBJECT and the game's own record type (`OverSkillStart`, `SpecialSkillStart`,
/// `AutoSkill1ForPassiveSkillStart`, ...). A postfix on it therefore observes 奥义/特殊/自动 through ONE
/// signature, and the Start/Finish member tells an activation apart from its end. It is a POSTFIX and it
/// touches nothing: a probe that cannot change the game's state cannot break the game's skill use.
///
/// WHAT IS STILL OPEN (R66 must not claim otherwise). Whether `AddPlayerSkillGameRecord` is called for
/// every activation, or only while the game is recording a replay, is NOT known -- there is no static way
/// to ask. So the page prints the per-channel row counts, the empty case says so explicitly, and every
/// record row is written to the log as `[SKILLTL] rec …`. The FIRST battle after this build answers it;
/// until then the 奥义 column must be read as "observed" and not as "complete".
///
/// FILTERING. Only our side (`CharacterInfo.IsAlly`: team 1, the same test every other surface uses) and
/// only our own battle session. A foreign unit's skill use is counted, never stored.
///
/// BOUNDED. At most <see cref="MaxEvents"/> events are kept; the overflow is counted, so a page that stops
/// growing mid-battle says why instead of quietly showing a stale tail.
/// </summary>
internal static class SkillTimelineProbe
{
	/// <summary>How many activations are kept per battle. 9 battles of this game produced 471 auto-skill
	/// rows in total and never more than 100 in one battle, so this is ~6x the observed worst case.</summary>
	internal const int MaxEvents = 600;

	/// <summary>Record-channel rows written to the log before the probe stops writing them (it keeps
	/// counting and keeping them for the page).</summary>
	private const int MaxRecordLogRows = 120;

	private static readonly object Gate = new object();
	private static readonly List<SkillTimelineEvent> Events = new List<SkillTimelineEvent>();

	/// <summary>Activations observed through the auto-skill command postfix.</summary>
	internal static int CommandEvents;
	/// <summary>Activations observed through `AddPlayerSkillGameRecord`.</summary>
	internal static int RecordEvents;
	/// <summary>Record calls that were NOT our side (the enemy's skills go through the same sink).</summary>
	internal static int ForeignSideSkips;
	/// <summary>Record calls whose `Player` or `Skill` was null.</summary>
	internal static int NullSkips;
	/// <summary>Record calls of a type that is not a skill ACTIVATION (`*Finish`, Attack*, screen
	/// gestures, status/buff records, ...). Counted, so "the sink fires but never for a skill start" is a
	/// visible fact rather than an empty page.</summary>
	internal static int NonStartSkips;
	/// <summary>Record calls that arrived with no live session (the sink is patched process-wide).</summary>
	internal static int NoSessionSkips;
	/// <summary>Record rows where the record type and `Skill.Type` disagreed -- the two readings of "what
	/// kind of skill was that" are kept side by side instead of being averaged.</summary>
	internal static int KindMismatches;
	/// <summary>Events dropped after <see cref="MaxEvents"/>.</summary>
	internal static int DroppedEvents;
	/// <summary>Field reads that threw.</summary>
	internal static int ReadErrors;
	/// <summary>Record rows written to the log (bounded by <see cref="MaxRecordLogRows"/>).</summary>
	internal static int RecordLogRows;

	internal static void Reset()
	{
		lock (Gate)
		{
			Events.Clear();
			CommandEvents = 0;
			RecordEvents = 0;
			ForeignSideSkips = 0;
			NullSkips = 0;
			NonStartSkips = 0;
			NoSessionSkips = 0;
			KindMismatches = 0;
			DroppedEvents = 0;
			ReadErrors = 0;
			RecordLogRows = 0;
		}
	}

	private static bool On()
	{
		return Plugin.CfgSkillTimeline != null && Plugin.CfgSkillTimeline.Value;
	}

	/// <summary>
	/// Called by <see cref="AutoSkillProbe"/> once it has RESOLVED the skill and decided this really was an
	/// activation, so the two probes cannot disagree about what counts: the resolution rule, the
	/// `Aggregator.Session != null` gate and the "unresolved index is not an activation" rule all stay in
	/// one place and are reused here rather than re-implemented.
	/// </summary>
	internal static void NoteCommandActivation(Player player, Skill skill)
	{
		try
		{
			if (!On()) return;
			string unit = UnitLabel(player);
			string name = SkillLabel(skill);
			if (string.IsNullOrEmpty(unit) || string.IsNullOrEmpty(name)) return;
			double wall, active;
			if (!Clocks(out wall, out active)) return;
			Add(unit, name, TypeOf(skill), SkillTimelineEvent.ChannelCommand, 0, wall, active);
			lock (Gate) CommandEvents++;
		}
		catch { ReadErrors++; }
	}

	/// <summary>
	/// The postfix body for `GameCmdExecuter.AddPlayerSkillGameRecord`. Never throws outwards.
	/// </summary>
	internal static void NoteRecord(Player player, Skill skill, eUserRecordType recordType)
	{
		try
		{
			if (!On()) return;
			if (Aggregator.Session == null) { lock (Gate) NoSessionSkips++; return; }
			if (player == null || skill == null) { lock (Gate) NullSkips++; return; }
			int type = KindOfRecord(recordType);
			if (type < 0) { lock (Gate) NonStartSkips++; return; }
			if (!CharacterInfo.IsAlly(player)) { lock (Gate) ForeignSideSkips++; return; }

			string unit = UnitLabel(player);
			string name = SkillLabel(skill);
			if (string.IsNullOrEmpty(unit) || string.IsNullOrEmpty(name)) { lock (Gate) NullSkips++; return; }

			int skillType = TypeOf(skill);
			bool mismatch = (skillType >= 0 && skillType != type);
			if (mismatch) { lock (Gate) KindMismatches++; }

			double wall, active;
			bool clocked = Clocks(out wall, out active);
			// The decision to LOG is taken under the lock (it reads the shared counter), the write itself
			// is NOT: RuntimeLog does file I/O, and a probe must never hold a lock across I/O on a path the
			// game's own skill use runs through.
			bool log = false;
			lock (Gate)
			{
				RecordEvents++;
				if (RecordLogRows < MaxRecordLogRows) { RecordLogRows++; log = true; }
			}
			if (log)
			{
				StringBuilder sb = new StringBuilder(180);
				sb.Append("[SKILLTL] rec unit=").Append(unit)
					.Append(" skill=").Append(name)
					.Append(" kind=").Append(SkillTimelinePolicy.KindLabel(type))
					.Append(" recType=").Append(((int)recordType).ToString(CultureInfo.InvariantCulture))
					.Append(" skillType=").Append(skillType.ToString(CultureInfo.InvariantCulture))
					.Append(mismatch ? " MISMATCH" : "")
					.Append(" wall=").Append(clocked ? wall.ToString("F2", CultureInfo.InvariantCulture) + "s" : "-")
					.Append(" active=").Append(clocked ? active.ToString("F2", CultureInfo.InvariantCulture) + "s" : "-");
				RuntimeLog.Write(sb.ToString());
			}
			Add(unit, name, type, SkillTimelineEvent.ChannelRecord, (int)recordType, wall, active);
		}
		catch { ReadErrors++; }
	}

	/// <summary>The record types that mean "this skill STARTED". Everything else (including every
	/// `*Finish`, the attack records, the screen gestures and the status/buff records that share this sink)
	/// is not an activation and is not stored.</summary>
	private static int KindOfRecord(eUserRecordType t)
	{
		if (t == eUserRecordType.OverSkillStart) return 2;
		if (t == eUserRecordType.SpecialSkillStart) return 1;
		if (t == eUserRecordType.AutoSkill1ForPassiveSkillStart) return 3;
		if (t == eUserRecordType.AutoSkill2ForPassiveSkillStart) return 4;
		return -1;
	}

	private static void Add(string unit, string skill, int type, string channel, int recordType,
		double wall, double active)
	{
		lock (Gate)
		{
			if (Events.Count >= MaxEvents) { DroppedEvents++; return; }
			Events.Add(new SkillTimelineEvent
			{
				Unit = unit, Skill = skill, Type = type, Channel = channel, RecordType = recordType,
				Wall = wall, Active = active,
			});
		}
	}

	/// <summary>The page's rows, built by the pure text layer from a snapshot of the events. The copy is
	/// taken under the same lock the hooks use, so a row can never be read half-written.</summary>
	internal static List<TimelineLine> Rows(bool inBattle)
	{
		List<SkillTimelineEvent> copy = Snapshot();
		return SkillTimelineText.Rows(copy, inBattle);
	}

	internal static List<SkillTimelineEvent> Snapshot()
	{
		lock (Gate) return new List<SkillTimelineEvent>(Events);
	}

	/// <summary>
	/// The battle-end evidence: the counters, then THE SAME LINES THE PAGE SHOWS. That is deliberate -- the
	/// page is otherwise only verifiable by looking at it, and "the log says 47 cmd rows while the panel
	/// listed 5" is exactly the kind of split this repo has been bitten by. The lines are prefixed so the
	/// reader can tell evidence from the live view.
	/// </summary>
	internal static string Summary()
	{
		if (!On()) return "";   // feature off = no line at all, so "off" and "nothing fired" cannot look alike
		List<SkillTimelineEvent> copy = Snapshot();
		StringBuilder sb = new StringBuilder(640);
		sb.Append("[SKILLTL] SUM cmd=").Append(CommandEvents)
			.Append(" rec=").Append(RecordEvents)
			.Append(" kept=").Append(copy.Count)
			.Append(" foreignSide=").Append(ForeignSideSkips)
			.Append(" nullSkips=").Append(NullSkips)
			.Append(" nonStart=").Append(NonStartSkips)
			.Append(" noSession=").Append(NoSessionSkips)
			.Append(" kindMismatch=").Append(KindMismatches)
			.Append(" dropped=").Append(DroppedEvents)
			.Append(" readErrors=").Append(ReadErrors);
		if (copy.Count == 0) return sb.ToString();
		List<TimelineLine> lines = SkillTimelineText.Rows(copy, true);
		for (int i = 0; i < lines.Count; i++)
			sb.Append("\n[SKILLTL] panel ").Append(lines[i].Text);
		return sb.ToString();
	}

	// ---- readers (each one counted, so an unreadable field is never a silent 0) ----

	private static int TypeOf(Skill sk)
	{
		if (sk == null) return -1;
		try { return (int)sk.m_type; }
		catch { ReadErrors++; return -1; }
	}

	private static string SkillLabel(Skill sk)
	{
		if (sk == null) return "";
		try
		{
			string n = sk.Name;
			if (string.IsNullOrEmpty(n))
			{
				try { n = sk.Description; } catch { }
			}
			if (string.IsNullOrEmpty(n)) n = "unnamed";
			if (n.Length > 40) n = n.Substring(0, 40);
			return n.Replace(' ', '_');
		}
		catch { ReadErrors++; return ""; }
	}

	private static string UnitLabel(Player p)
	{
		if (p == null) return "";
		try
		{
			string n = p.Name;
			if (string.IsNullOrEmpty(n)) return "entry" + p.EntryId;
			return n.Replace(' ', '_');
		}
		catch { ReadErrors++; return ""; }
	}

	/// <summary>The same clocks the [AUTOSK] rows print, so an activation in the table can be joined to a
	/// charge sample by eye. False = no live session (the caller drops the row).</summary>
	private static bool Clocks(out double wall, out double active)
	{
		wall = 0.0;
		active = 0.0;
		try
		{
			BattleSession s = Aggregator.Session;
			if (s == null) return false;
			wall = (DateTime.Now - s.StartWallClock).TotalSeconds;
			active = s.ActiveSeconds;
			return true;
		}
		catch { ReadErrors++; return false; }
	}
}
