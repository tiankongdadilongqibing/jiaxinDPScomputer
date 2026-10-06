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
/// THE TWO CHANNELS (R69; the third one is gone).
///   1. `cmd` -- the R64 auto-skill command postfix. Verified over 9 battles / 471 rows.
///   2. `skl` -- R67's three active/special command postfixes (`ActExecutePlayer{ActiveSkill,Skill,
///      SpecialSkill}`). Measured in quest 411001: 595 calls, 554 rejected by the game itself, 41 accepted
///      and all 41 `Skill.Type == 2` (奥義).
/// R69 DELETED the R66 `rec` channel (a postfix on `AddPlayerSkillGameRecord`): it was installed and
/// produced 0 rows with 0 skips in a training battle AND in a real quest, so the page stopped mentioning a
/// channel that does not exist in this game. The falsification is recorded in REFACTOR-BATCH-R69.md.
///
/// R69: A CALL IS NOT AN ACTIVATION (this is the correction that matters). The game calls the auto-skill
/// command once per ATTACK and returns `ok=1` whether or not the skill executed: マッドシーカー's
/// 実験失敗！ (counter 2970 frames = 99 game s) was called 25 times in one 119 s battle and executed ONCE.
/// The verdict is the game's own state (`Skill.GetStatus()`): only a call that finds the skill `Using` is
/// an activation (<see cref="SkillActivationPolicy"/>); the rest are ATTEMPTS, counted in
/// <see cref="AttemptEvents"/> and per row, never placed on the time axis and never fed to a median. The
/// `[AUTOSK] act` rows still print EVERY call (with `verdict=`) -- the raw evidence is what let R69 find
/// this, and a probe that hides the calls it decided against cannot be audited.
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
	/// rows in total and never more than 100 in one battle, so this is ~6x the observed worst case. R69 did
	/// not shrink it when it removed the attempts from the stream: only ACTIVATIONS are stored now.</summary>
	internal const int MaxEvents = 600;

	/// <summary>R69: distinct (unit, skill, type) attempt rows kept. 24 units were seen in 9 battles, so
	/// this is ~2.5x the observed worst case; the overflow is counted.</summary>
	internal const int MaxAttemptRows = 64;

	private static readonly object Gate = new object();
	private static readonly List<SkillTimelineEvent> Events = new List<SkillTimelineEvent>();

	/// <summary>R69: per (unit, skill, type) count of calls that did NOT execute the skill.</summary>
	private static readonly Dictionary<string, SkillTimelineAttempt> Attempts =
		new Dictionary<string, SkillTimelineAttempt>(StringComparer.Ordinal);

	/// <summary>Activations observed through the auto-skill command postfix.</summary>
	internal static int CommandEvents;
	/// <summary>R69: calls that did not execute the skill, through the auto-skill command postfix. This is
	/// the counter that used to be published as activations.</summary>
	internal static int AttemptEvents;
	/// <summary>R69: calls whose status was neither `Using` nor `Charge`/`Usable` (an unreadable field, or
	/// a state this build does not know). Counted, never published as an activation: fail closed.</summary>
	internal static int UnclassifiedEvents;
	/// <summary>R69: attempt rows dropped after <see cref="MaxAttemptRows"/>.</summary>
	internal static int DroppedAttemptRows;
	/// <summary>Calls that were NOT our side (the enemy's skills go through the same hooks).</summary>
	internal static int ForeignSideSkips;
	/// <summary>Calls whose `Player` or `Skill` was null (or whose name could not be read).</summary>
	internal static int NullSkips;
	/// <summary>Record calls of a type that is not a skill ACTIVATION (`*Finish`, Attack*, screen
	/// <summary>Calls that arrived with no live session (the hooks are patched process-wide).</summary>
	internal static int NoSessionSkips;
	/// <summary>Events dropped after <see cref="MaxEvents"/>.</summary>
	internal static int DroppedEvents;
	/// <summary>Field reads that threw.</summary>
	internal static int ReadErrors;

	internal static void Reset()
	{
		lock (Gate)
		{
			Events.Clear();
			Attempts.Clear();
			CommandEvents = 0;
			AttemptEvents = 0;
			UnclassifiedEvents = 0;
			DroppedAttemptRows = 0;
			ForeignSideSkips = 0;
			NullSkips = 0;
			NoSessionSkips = 0;
			DroppedEvents = 0;
			ReadErrors = 0;
			ActiveCmdCalls = 0;
			SkillCmdCalls = 0;
			SpecialCmdCalls = 0;
			SkillCmdRejected = 0;
			SkillCmdNoSkill = 0;
			SkillCmdEvents = 0;
			_skillLogRows = 0;
		}
	}

	private static bool On()
	{
		return Plugin.CfgSkillTimeline != null && Plugin.CfgSkillTimeline.Value;
	}

	/// <summary>
	/// Called by <see cref="AutoSkillProbe"/> once it has RESOLVED the skill and the game's own status said
	/// this call really executed it, so the two probes cannot disagree about what counts: the resolution
	/// rule, the `Aggregator.Session != null` gate, the "unresolved index is not an activation" rule and
	/// (R69) the `Using` verdict all stay in one place and are reused here rather than re-implemented.
	///
	/// <paramref name="coolSeconds"/> is the skill's own cooldown, which is what the page folds by: the game
	/// cannot execute the same skill twice inside it, so repeated `Using` calls inside one execution collapse
	/// back into one stamp.
	/// </summary>
	internal static void NoteCommandActivation(Player player, Skill skill, double coolSeconds)
	{
		try
		{
			if (!On()) return;
			string unit = UnitLabel(player);
			string name = SkillLabel(skill);
			if (string.IsNullOrEmpty(unit) || string.IsNullOrEmpty(name)) return;
			double wall, active;
			if (!Clocks(out wall, out active)) return;
			Add(unit, name, TypeOf(skill), SkillTimelineEvent.ChannelCommand, 0, wall, active, coolSeconds);
			lock (Gate) CommandEvents++;
		}
		catch { ReadErrors++; }
	}

	/// <summary>
	/// R69: one call of the auto-skill command that did NOT execute the skill (the game was still charging,
	/// or charged but not executing). It is NOT an event: it gets no stamp, no channel and no median -- it
	/// only raises the attempt count of its (unit, skill, type). The call itself is still written to the
	/// runtime log by <see cref="AutoSkillProbe"/>, with `verdict=`, so the decision stays auditable.
	/// </summary>
	internal static void NoteCommandAttempt(Player player, Skill skill)
	{
		try
		{
			if (!On()) return;
			string unit = UnitLabel(player);
			string name = SkillLabel(skill);
			if (string.IsNullOrEmpty(unit) || string.IsNullOrEmpty(name)) return;
			int type = TypeOf(skill);
			string key = unit + "\u0001" + name + "\u0001" + type.ToString(CultureInfo.InvariantCulture);
			lock (Gate)
			{
				AttemptEvents++;
				SkillTimelineAttempt row;
				if (!Attempts.TryGetValue(key, out row))
				{
					if (Attempts.Count >= MaxAttemptRows) { DroppedAttemptRows++; return; }
					row = new SkillTimelineAttempt { Unit = unit, Skill = name, Type = type };
					Attempts[key] = row;
				}
				row.Count++;
			}
		}
		catch { ReadErrors++; }
	}

	/// <summary>R67: postfix calls on the three active/special command entry points, per entry point --
	/// printed in the SUM line so "the hook never ran" (all 0) and "it ran but was rejected / was the
	/// enemy" are different facts. That distinction is exactly what R66's record sink could not give
	/// beyond "0 everywhere".</summary>
	internal static int ActiveCmdCalls, SkillCmdCalls, SpecialCmdCalls;
	/// <summary>R67: calls the game itself REJECTED (`__result == false`): counted, never stored.</summary>
	internal static int SkillCmdRejected;
	/// <summary>R67: accepted calls whose skill slot could not be read (null `ActiveSkill`/`SpecialSkill`).</summary>
	internal static int SkillCmdNoSkill;
	/// <summary>R67: accepted activations observed through the command entry points (the `skl` channel).</summary>
	internal static int SkillCmdEvents;
	private const int MaxSkillLogRows = 120;
	private static int _skillLogRows;

	/// <summary>
	/// R67: the body of the postfixes on `GameCmdExecuter.ActExecutePlayerActiveSkill`,
	/// `ActExecutePlayerSkill` and `ActExecutePlayerSpecialSkill`. `entry` names which one ran
	/// (`active`/`skill`/`special`). The skill that fired is read back from the unit's own slot AFTER the
	/// game executed it (`Player.ActiveSkill` for the first two, `Player.SpecialSkill` for the third) and its
	/// `Skill.Type` labels the row -- the page never assumes "active = 奥义"; the `[SKILLTL] skl` row prints
	/// what the slot actually held so that mapping is MEASURED, not inferred.
	/// </summary>
	internal static void NoteSkillCommand(Player player, string entry, bool accepted)
	{
		try
		{
			if (!On()) return;
			lock (Gate)
			{
				if (entry == "active") ActiveCmdCalls++;
				else if (entry == "special") SpecialCmdCalls++;
				else SkillCmdCalls++;
			}
			if (Aggregator.Session == null) { lock (Gate) NoSessionSkips++; return; }
			if (player == null) { lock (Gate) NullSkips++; return; }
			if (!CharacterInfo.IsAlly(player)) { lock (Gate) ForeignSideSkips++; return; }
			if (!accepted) { lock (Gate) SkillCmdRejected++; return; }

			Skill sk = null;
			try { sk = (entry == "special") ? player.SpecialSkill : player.ActiveSkill; }
			catch { ReadErrors++; }
			if (sk == null) { lock (Gate) SkillCmdNoSkill++; return; }

			string unit = UnitLabel(player);
			string name = SkillLabel(sk);
			if (string.IsNullOrEmpty(unit) || string.IsNullOrEmpty(name)) { lock (Gate) NullSkips++; return; }
			int type = TypeOf(sk);
			double wall, active;
			if (!Clocks(out wall, out active)) return;

			bool log = false;
			lock (Gate)
			{
				SkillCmdEvents++;
				if (_skillLogRows < MaxSkillLogRows) { _skillLogRows++; log = true; }
			}
			if (log)
			{
				RuntimeLog.Write("[SKILLTL] skl entry=" + entry + " unit=" + unit + " skill=" + name
					+ " kind=" + SkillTimelinePolicy.KindLabel(type)
					+ " skillType=" + type.ToString(CultureInfo.InvariantCulture)
					+ " wall=" + wall.ToString("F2", CultureInfo.InvariantCulture) + "s"
					+ " active=" + active.ToString("F2", CultureInfo.InvariantCulture) + "s");
			}
			Add(unit, name, type, SkillTimelineEvent.ChannelSkillCommand, 0, wall, active, CoolSecondsOf(sk));
		}
		catch { ReadErrors++; }
	}

	private static void Add(string unit, string skill, int type, string channel, int recordType,
		double wall, double active, double coolSeconds)
	{
		lock (Gate)
		{
			if (Events.Count >= MaxEvents) { DroppedEvents++; return; }
			Events.Add(new SkillTimelineEvent
			{
				Unit = unit, Skill = skill, Type = type, Channel = channel,
				Wall = wall, Active = active, CoolSeconds = coolSeconds,
			});
		}
	}

	/// <summary>R69: the skill's own cooldown in GAME seconds. `Skill.CoolTimeFrame / 30` is used rather
	/// than `Skill.CoolTime` because it keeps the fraction (the corpus's `ct=` column is the truncated
	/// integer, and the fold must not round a 3.33 s cooldown down to 3). Unreadable -> 0, which the policy
	/// treats as "fold by the fixed floor".</summary>
	private static double CoolSecondsOf(Skill sk)
	{
		if (sk == null) return 0.0;
		try
		{
			int frames = sk.CoolTimeFrame;
			return (frames > 0) ? frames / 30.0 : 0.0;
		}
		catch { ReadErrors++; return 0.0; }
	}

	/// <summary>The page's rows, built by the pure text layer from a snapshot of the events and of the
	/// attempt tallies. Both copies are taken under the same lock the hooks use, so a row can never be read
	/// half-written.</summary>
	internal static List<TimelineLine> Rows(bool inBattle)
	{
		List<SkillTimelineEvent> copy = Snapshot();
		return SkillTimelineText.Rows(copy, AttemptSnapshot(), inBattle);
	}

	internal static List<SkillTimelineEvent> Snapshot()
	{
		lock (Gate) return new List<SkillTimelineEvent>(Events);
	}

	/// <summary>R69: the attempt tallies, copied under the lock. The row objects are copied too, because the
	/// page reads them after the lock is released and the hooks keep incrementing them.</summary>
	internal static List<SkillTimelineAttempt> AttemptSnapshot()
	{
		lock (Gate)
		{
			var copy = new List<SkillTimelineAttempt>(Attempts.Count);
			foreach (KeyValuePair<string, SkillTimelineAttempt> kv in Attempts)
				copy.Add(new SkillTimelineAttempt
				{
					Unit = kv.Value.Unit, Skill = kv.Value.Skill, Type = kv.Value.Type, Count = kv.Value.Count,
				});
			return copy;
		}
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
		List<SkillTimelineAttempt> attempts = AttemptSnapshot();
		StringBuilder sb = new StringBuilder(640);
		sb.Append("[SKILLTL] SUM cmd=").Append(CommandEvents)
			.Append(" tries=").Append(AttemptEvents)
			.Append(" unclassified=").Append(UnclassifiedEvents)
			.Append(" skl=").Append(SkillCmdEvents)
			.Append(" sklCalls(active/skill/special)=").Append(ActiveCmdCalls).Append('/').Append(SkillCmdCalls)
			.Append('/').Append(SpecialCmdCalls)
			.Append(" sklRejected=").Append(SkillCmdRejected)
			.Append(" sklNoSkill=").Append(SkillCmdNoSkill)
			.Append(" kept=").Append(copy.Count)
			.Append(" attemptRows=").Append(attempts.Count)
			.Append(" foreignSide=").Append(ForeignSideSkips)
			.Append(" nullSkips=").Append(NullSkips)
			.Append(" noSession=").Append(NoSessionSkips)
			.Append(" dropped=").Append(DroppedEvents)
			.Append(" droppedAttemptRows=").Append(DroppedAttemptRows)
			.Append(" readErrors=").Append(ReadErrors);
		if (copy.Count == 0 && attempts.Count == 0) return sb.ToString();
		List<TimelineLine> lines = SkillTimelineText.Rows(copy, attempts, true);
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
