using System;
using System.Collections.Generic;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace DpsMeter;

/// <summary>
/// PROBE (R64): read the AUTO SKILL from the live `Skill` side, and log BOTH the moment it fires and the
/// charge it held at that moment.
///
/// WHY THIS PROBE EXISTS (the question R63 could not answer)
/// R63 dumped `Rog.MasterData.AutoSkillMasterData` and published, for メルティエル/トレイラ's auto skill,
/// `minCoolTime`/`maxCoolTime` = 300/240 SECONDS and the same numbers as 9000/7200 FRAMES. It could not
/// say whether those numbers drive anything observable, because the cadence previously reverse-inferred
/// from a damage channel (about 13.5 s) contradicts them by a factor of ~18, and the `auto_skill` table's
/// id space is provably NOT the damage-calc effect-id space (the table also holds an unrelated id 10024
/// 「恐怖の特異点」). So that earlier answer was withdrawn pending a direct reading. This is that reading.
///
/// WHERE THE AUTO SKILL ACTUALLY LIVES (metadata, not guesswork)
///   * `Rog.MasterData.AutoSkillMasterData : SkillMasterDataBase` (verified with ilspycmd), so an auto
///     skill becomes an ordinary `SkillData` and then an ordinary `Skill` -- it is NOT in
///     `StandbyController.m_standbyDataList` (measured 2026-10-06 on the last battle: that list's three
///     rendered entries were 地下からの完全顕現 / 電脳掌都 / 狂気の眼球, and of those four names only
///     暗沌への導き appears anywhere in `auto_skill.json`).
///   * the game's own discriminator is `Skill.m_type : Skill.Type`, whose members are
///     `Skill, SpecialSkill, OverSkill, AutoSkill1ForPassiveSkill, AutoSkill2ForPassiveSkill` -- i.e.
///     exactly two auto-skill slots, which is also what `Player.AutoSkill1` / `Player.AutoSkill2`,
///     `PassiveSkill.AutoSkill` and `InvokingCondition.IsAutoSkill1Start/2Start` describe.
///   * the charge is the game's own counter, `Skill.WaitCountFrame` (`m_waitCountFrame`), against
///     `Skill.CoolTimeFrame` (`m_coolTimeFrame`), with `Skill.GetStatus()` returning the game's own state
///     machine `Status { NotHave, Charge, Usable, Using }`. `Skill.Type` is read too, so a slot holding
///     an ACTIVE skill is labelled as such instead of being reported as an auto skill.
///
/// TWO CHANNELS, ON PURPOSE (they cross-check each other)
///   1. `via=cmd` -- a postfix on the game's own command entry point
///      `GameCmdExecuter.ActExecutePlayerAutoSkillForPassive(Player, int index, Vector3)`. This is the
///      exact activation instant and it carries the game's own return value.
///   2. `via=poll` -- the charge sampler detects the rising edge of `GetStatus() == Using` (or of
///      `IsActivated`) on the same `Skill` objects. If the patch ever stops resolving, the moments are
///      still recorded, and if the two channels disagree the counts say so instead of one being assumed
///      true. Same "two independent routes" rule as the 1.5.4 madness-applier channel.
///
/// The sampler is read-only, bounded (one line per slot per <see cref="SampleSeconds"/>, at most
/// <see cref="MaxSlots"/> slots, at most <see cref="MaxActivationRows"/> activation rows), and counts
/// every failure, so "this unit has no auto skill" and "the probe could not read it" never look alike.
///
/// CLOCK DISCIPLINE. The command channel runs OUTSIDE the frame loop, so it reads the clocks itself; the
/// sampler is handed the battle session by `Aggregator.Tick`. Both print the same pair (real seconds
/// since session start, and the battle clock), so an activation row can be joined to a `[TIME]` or
/// `[CLOCKP]` line by eye, and the interval between two activations is published on BOTH clocks.
/// </summary>
internal static class AutoSkillProbe
{
	/// <summary>One charge sample per slot per this many REAL seconds, so two consecutive printed lines
	/// reproduce the printed rate exactly (the rate is computed from the last PRINTED pair).</summary>
	internal const double SampleSeconds = 2.0;

	private const double ScanSeconds = 0.5;
	private const int MaxSlots = 24;
	private const int MaxActivationRows = 400;
	private const int MaxFallbackPassives = 8;

	/// <summary>Charge lines written.</summary>
	internal static int SampleRows;
	/// <summary>Activation rows written from the command postfix.</summary>
	internal static int CommandActivations;
	/// <summary>Activation rows written from the charge sampler's rising edge.</summary>
	internal static int PollActivations;
	/// <summary>Field reads that threw (an unreadable slot, not a missing one).</summary>
	internal static int ReadErrors;
	/// <summary>Slot READS that found no auto skill at all (the normal case for most units, and most units
	/// are read twice per scan, so this is a read count and not a distinct-slot count).</summary>
	internal static int EmptySlots;
	/// <summary>Distinct party players seen in the standby list.</summary>
	internal static int PlayersSeen;
	/// <summary>Players reached only through `Skill.m_owner` because the standby row's player was null.</summary>
	internal static int PlayersViaOwner;
	/// <summary>Slots reached only by scanning `Player.PassiveSkills` (both named slots empty).</summary>
	internal static int PassiveFallbacks;
	/// <summary>Intervals that exceeded <see cref="MaxActivationRows"/> and were therefore not kept for
	/// the median. The activation ROWS themselves are all written to the runtime log regardless.</summary>
	internal static int IntervalsDropped;
	/// <summary>Command postfix calls that arrived with a NULL `Player`. Counted because "the command ran
	/// but carried no player" is a fact about the game, not a probe failure.</summary>
	internal static int NullPlayers;
	/// <summary>Slots dropped after <see cref="MaxSlots"/>.</summary>
	internal static int SlotsDropped;

	private static readonly Dictionary<string, SlotState> Slots = new Dictionary<string, SlotState>();

	private static double _lastScanWall = double.MinValue;

	/// <summary>Per (unit, slot) state. Keyed by `EntryId|name|index`.</summary>
	private sealed class SlotState
	{
		internal string Unit;
		internal string SkillName;
		internal int Index;
		internal int LastType = int.MinValue;
		internal string LastStatus = "";
		internal int LastUsing;
		internal bool Scanned;
		internal bool Logged;
		internal double LastWall;
		internal double LastActive;
		internal int LastWait;
		internal bool HasActivation;
		internal double PrevActWall;
		internal double PrevActActive;
		internal int Activations;
		internal readonly List<double> WallIntervals = new List<double>();
		internal readonly List<double> ActiveIntervals = new List<double>();
	}

	internal static void Reset()
	{
		Slots.Clear();
		_lastScanWall = double.MinValue;
		SampleRows = 0;
		CommandActivations = 0;
		PollActivations = 0;
		ReadErrors = 0;
		EmptySlots = 0;
		PlayersSeen = 0;
		PlayersViaOwner = 0;
		PassiveFallbacks = 0;
		IntervalsDropped = 0;
		NullPlayers = 0;
		SlotsDropped = 0;
	}

	private static bool On()
	{
		return Plugin.CfgAutoSkillProbe != null && Plugin.CfgAutoSkillProbe.Value;
	}

	/// <summary>
	/// The command postfix. Called from <see cref="AutoSkillHooks"/> with only the arguments a
	/// `Player`+`int` signature can supply: the `Vector3` parameter is deliberately NOT declared, because
	/// materialising an argument is the part of a detour that has crashed this plugin before
	/// (`Hooks/BattleObjectHooks.cs`) and nothing here needs the position.
	///
	/// Never throws outwards: a probe must not be able to break the game's activation path.
	/// </summary>
	internal static void NoteCommand(Player player, int index, bool result)
	{
		try
		{
			if (!On()) return;
			// The command layer is patched process-wide, so the FIRST thing this does is prove there is a
			// battle to attach the row to. The 1.0.48/1.0.49 crash was a detour body reading game objects
			// in an impossible state, and a session that was never started is exactly that state -- so
			// `Aggregator.Session != null` (set by StartSession, i.e. the game got as far as a battle
			// setup) gates every native read below. It is deliberately NOT `InBattle`: an auto skill can
			// fire during the post-battle sequence, and dropping those rows would lose real activations.
			if (Aggregator.Session == null) return;
			if (player == null) { NullPlayers++; return; }
			NoteActivation(player, index, SlotSkill(player, index), "cmd", result ? 1 : 0);
		}
		catch { ReadErrors++; }
	}

	/// <summary>
	/// The charge sampler. Called every frame from `Aggregator.Tick`; it reads at most every
	/// <see cref="ScanSeconds"/> and PRINTS at most every <see cref="SampleSeconds"/> per slot.
	/// </summary>
	internal static void Observe(GameSystem val, BattleSession s)
	{
		try
		{
			if (!On() || val == null || s == null) return;
			double wall = (DateTime.Now - s.StartWallClock).TotalSeconds;
			if (_lastScanWall > double.MinValue && wall - _lastScanWall < ScanSeconds) return;
			_lastScanWall = wall;
			Scan(val, s, wall);
		}
		catch { ReadErrors++; }
	}

	private static void Scan(GameSystem val, BattleSession s, double wall)
	{
		StandbyManager mgr = null;
		StandbyController ctl = null;
		Il2CppSystem.Collections.Generic.List<StandbyDataBase> list = null;
		try
		{
			mgr = val.StandbyManager;
			ctl = (mgr != null) ? mgr.m_standbyController : null;
			list = (ctl != null) ? ctl.m_standbyDataList : null;
		}
		catch { ReadErrors++; }
		if (list == null) return;

		HashSet<long> seen = new HashSet<long>();
		for (int i = 0; i < list.Count; i++)
		{
			Player p = null;
			try
			{
				PlayerSkillStandbyData ps = list[i].TryCast<PlayerSkillStandbyData>();
				if (ps == null) continue;
				try { p = ps.m_player; } catch { }
				if (GameRef.IsNull(p))
				{
					// Second route to the same object: the skill knows its owner. Counted separately,
					// because "the standby row carried no player" is itself a fact about the game.
					Skill ownerSkill = null;
					try { ownerSkill = ps.m_skill; } catch { }
					if (ownerSkill != null)
					{
						try { p = ownerSkill.m_owner.TryCast<Player>(); } catch { }
						if (!GameRef.IsNull(p)) PlayersViaOwner++;
					}
				}
			}
			catch { ReadErrors++; continue; }
			if (GameRef.IsNull(p)) continue;
			long ptr = PointerOf(p);
			if (ptr != 0L && !seen.Add(ptr)) continue;
			PlayersSeen++;
			ObservePlayer(p, s, wall);
		}
	}

	private static void ObservePlayer(Player p, BattleSession s, double wall)
	{
		int live = 0;
		try
		{
			live += SampleSlot(p, 1, SlotSkill(p, 1), s, wall);
			live += SampleSlot(p, 2, SlotSkill(p, 2), s, wall);
		}
		catch { ReadErrors++; }
		if (live > 0) return;
		// Both named slots are empty. An auto skill the game holds only inside `PassiveSkills` would
		// otherwise be invisible, so fall back to that array -- bounded, and counted so the fallback can
		// never be mistaken for the normal route.
		if (!HasPassiveAutoSkill(p)) return;
		try
		{
			Il2CppReferenceArray<PassiveSkill> arr = p.PassiveSkills;
			if (arr == null) return;
			int n = arr.Length;
			if (n > MaxFallbackPassives) n = MaxFallbackPassives;
			for (int i = 0; i < n; i++)
			{
				PassiveSkill ps = null;
				try { ps = arr[i]; } catch { ReadErrors++; continue; }
				if (ps == null) continue;
				Skill sk = null;
				try { sk = ps.AutoSkill; } catch { ReadErrors++; continue; }
				if (sk == null) continue;
				int index = 0;
				try { index = ps.Index; } catch { }
				PassiveFallbacks++;
				SampleSlot(p, 100 + index, sk, s, wall);
			}
		}
		catch { ReadErrors++; }
	}

	private static bool HasPassiveAutoSkill(Player p)
	{
		try { return p.HasAutoSkillForPassiveSkill; }
		catch { ReadErrors++; return false; }
	}

	/// <summary>
	/// Read one slot and, when its print interval has elapsed, emit one charge line. Returns 1 when the
	/// slot held a readable skill, so the caller can tell "no auto skill" from "not looked at".
	/// </summary>
	private static int SampleSlot(Player p, int index, Skill sk, BattleSession s, double wall)
	{
		if (sk == null) { EmptySlots++; return 0; }
		string unit = UnitLabel(p);
		string key = KeyOf(p, index);
		SlotState st;
		if (!Slots.TryGetValue(key, out st))
		{
			if (Slots.Count >= MaxSlots) { SlotsDropped++; return 1; }
			st = new SlotState { Unit = unit, Index = index };
			Slots[key] = st;
		}
		st.Unit = unit;

		int type = ReadType(sk);
		string status = StatusName(sk);
		int wait = ReadWait(sk);
		int cool = ReadCoolFrames(sk);
		int usingNow = ((status == "Using") || ReadActivated(sk)) ? 1 : 0;

		// ---- channel 2: the rising edge of "in use" on the game's own state machine ----
		if (st.Scanned && st.LastUsing == 0 && usingNow == 1) NoteActivation(p, index, sk, "poll", -1);
		st.Scanned = true;
		st.LastUsing = usingNow;
		st.LastType = type;
		st.LastStatus = status;

		// ---- channel 1: the charge trajectory ----
		if (st.Logged && (wall - st.LastWall) < SampleSeconds) return 1;
		double dWall = st.Logged ? AutoSkillCadencePolicy.IntervalSeconds(st.LastWall, wall) : 0.0;
		double dActive = st.Logged ? AutoSkillCadencePolicy.IntervalSeconds(st.LastActive, s.ActiveSeconds) : 0.0;
		int dWait = st.Logged ? wait - st.LastWait : 0;
		double upsGame = AutoSkillCadencePolicy.UnitsPerSecond(-dWait, dActive);
		double upsWall = AutoSkillCadencePolicy.UnitsPerSecond(-dWait, dWall);
		bool hadPrevious = st.Logged;
		st.Logged = true;
		st.LastWall = wall;
		st.LastActive = s.ActiveSeconds;
		st.LastWait = wait;
		st.SkillName = SkillLabel(sk);

		StringBuilder sb = new StringBuilder(260);
		sb.Append("[AUTOSK] chg wall=").Append(wall.ToString("F2")).Append('s')
			.Append(" active=").Append(s.ActiveSeconds.ToString("F2")).Append('s')
			.Append(" unit=").Append(unit)
			.Append(" idx=").Append(index)
			.Append(" skill=").Append(st.SkillName)
			.Append(" type=").Append(TypeNumber(type)).Append('(').Append(TypeName(type)).Append(')')
			.Append(" status=").Append(status)
			.Append(" wait=").Append(wait).Append('/').Append(cool)
			.Append(" dur=").Append(ReadDuration(sk))
			.Append(" stock=").Append(ReadStock(sk))
			.Append(" passiveAuto=").Append(ReadIsPassiveAuto(sk) ? 1 : 0)
			.Append(" autoActivate=").Append(ReadAutoActivate(sk))
			.Append(" level=").Append(ReadLevel(sk));
		if (hadPrevious && dWall > 0.0)
		{
			sb.Append(" dWait=").Append(dWait)
				.Append(" dWall=").Append(dWall.ToString("F2"))
				.Append(" dActive=").Append(dActive.ToString("F2"))
				.Append(" upsGame=").Append(upsGame.ToString("F1"))
				.Append(" upsWall=").Append(upsWall.ToString("F1"))
				.Append(" chargeSec=").Append(AutoSkillCadencePolicy.SecondsFor(cool, upsGame).ToString("F1")).Append('s');
		}
		RuntimeLog.Write(sb.ToString());
		SampleRows++;
		return 1;
	}

	/// <summary>
	/// One activation row. `via` names the channel, so a `cmd` row and a `poll` row for the same instant
	/// stay individually identifiable instead of being silently deduplicated.
	/// </summary>
	private static void NoteActivation(Player player, int index, Skill sk, string via, int result)
	{
		string unit = UnitLabel(player);
		string key = KeyOf(player, index);
		SlotState st;
		if (!Slots.TryGetValue(key, out st))
		{
			if (Slots.Count >= MaxSlots) { SlotsDropped++; return; }
			st = new SlotState { Unit = unit, Index = index };
			Slots[key] = st;
		}
		if (via == "cmd") CommandActivations++; else PollActivations++;
		st.Activations++;

		double wall, active;
		bool clocked = Clocks(out wall, out active);

		// The interval is measured on BOTH clocks from the same stored previous moment, so the two
		// published numbers describe the same pair of activations.
		double dWall = 0.0, dActive = 0.0;
		if (clocked && st.HasActivation)
		{
			dWall = AutoSkillCadencePolicy.IntervalSeconds(st.PrevActWall, wall);
			dActive = AutoSkillCadencePolicy.IntervalSeconds(st.PrevActActive, active);
			if (dWall > 0.0) st.WallIntervals.Add(dWall);
			if (dActive > 0.0) st.ActiveIntervals.Add(dActive);
		}
		if (clocked)
		{
			st.HasActivation = true;
			st.PrevActWall = wall;
			st.PrevActActive = active;
		}

		StringBuilder sb = new StringBuilder(220);
		sb.Append("[AUTOSK] act via=").Append(via)
			.Append(" wall=").Append(clocked ? wall.ToString("F2") + "s" : "-")
			.Append(" active=").Append(clocked ? active.ToString("F2") + "s" : "-")
			.Append(" unit=").Append(unit)
			.Append(" idx=").Append(index)
			.Append(" skill=").Append(SkillLabel(sk))
			.Append(" type=").Append(TypeNumber(ReadType(sk))).Append('(').Append(TypeName(ReadType(sk))).Append(')')
			.Append(" status=").Append(StatusName(sk))
			.Append(" wait=").Append(ReadWait(sk)).Append('/').Append(ReadCoolFrames(sk))
			.Append(" dur=").Append(ReadDuration(sk))
			.Append(" stock=").Append(ReadStock(sk))
			.Append(" level=").Append(ReadLevel(sk))
			.Append(" ok=").Append(result)
			.Append(" n=").Append(st.Activations)
			.Append(" dWall=").Append(dWall > 0.0 ? dWall.ToString("F2") + "s" : "-")
			.Append(" dActive=").Append(dActive > 0.0 ? dActive.ToString("F2") + "s" : "-");
		RuntimeLog.Write(sb.ToString());
		if (st.WallIntervals.Count > MaxActivationRows) IntervalsDropped++;
	}

	// ---- readers: each one counted, so an unreadable field is never a silent 0 ----

	private static Skill SlotSkill(Player p, int index)
	{
		try
		{
			if (index == 1) return p.AutoSkill1;
			if (index == 2) return p.AutoSkill2;
			return null;
		}
		catch { ReadErrors++; return null; }
	}

	private static int ReadType(Skill sk)
	{
		if (sk == null) return -1;
		try { return (int)sk.m_type; } catch { ReadErrors++; return -1; }
	}

	private static string TypeNumber(int t)
	{
		return (t < 0) ? "?" : t.ToString();
	}

	private static string TypeName(int t)
	{
		if (t == 0) return "Skill";
		if (t == 1) return "SpecialSkill";
		if (t == 2) return "OverSkill";
		if (t == 3) return "AutoSkill1ForPassiveSkill";
		if (t == 4) return "AutoSkill2ForPassiveSkill";
		return "unknown";
	}

	private static string StatusName(Skill sk)
	{
		if (sk == null) return "-";
		try
		{
			int st = (int)sk.GetStatus();
			if (st == 0) return "NotHave";
			if (st == 1) return "Charge";
			if (st == 2) return "Usable";
			if (st == 3) return "Using";
			return "Status" + st;
		}
		catch { ReadErrors++; return "?"; }
	}

	private static string SkillLabel(Skill sk)
	{
		if (sk == null) return "-";
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
		catch { ReadErrors++; return "?"; }
	}

	private static string UnitLabel(Player p)
	{
		if (p == null) return "?";
		try
		{
			string n = p.Name;
			if (string.IsNullOrEmpty(n)) return "entry" + p.EntryId;
			return n.Replace(' ', '_');
		}
		catch { ReadErrors++; return "?"; }
	}

	private static int ReadWait(Skill sk)
	{
		if (sk == null) return int.MinValue;
		try { return sk.WaitCountFrame; } catch { ReadErrors++; return int.MinValue; }
	}

	private static int ReadCoolFrames(Skill sk)
	{
		if (sk == null) return int.MinValue;
		try { return sk.CoolTimeFrame; } catch { ReadErrors++; return int.MinValue; }
	}

	private static int ReadDuration(Skill sk)
	{
		if (sk == null) return int.MinValue;
		try { return sk.DurationCountFrame; } catch { ReadErrors++; return int.MinValue; }
	}

	private static bool ReadActivated(Skill sk)
	{
		if (sk == null) return false;
		try { return sk.IsActivated; } catch { ReadErrors++; return false; }
	}

	private static bool ReadIsPassiveAuto(Skill sk)
	{
		if (sk == null) return false;
		try { return sk.IsAutoSkillForPassiveSkill; } catch { ReadErrors++; return false; }
	}

	private static int ReadAutoActivate(Skill sk)
	{
		if (sk == null) return int.MinValue;
		try { return sk.AutoActivate; } catch { ReadErrors++; return int.MinValue; }
	}

	private static int ReadLevel(Skill sk)
	{
		if (sk == null) return int.MinValue;
		try { return sk.Level; } catch { ReadErrors++; return int.MinValue; }
	}

	private static string ReadStock(Skill sk)
	{
		if (sk == null) return "-";
		try { return sk.Stock + "/" + sk.StockMax; }
		catch { ReadErrors++; return "?"; }
	}

	// ---- identity / clocks ----

	private static long PointerOf(Player p)
	{
		try { return (long)((Il2CppObjectBase)p).Pointer; }
		catch { return 0L; }
	}

	private static string KeyOf(Player p, int index)
	{
		try { return p.EntryId + "|" + p.Name + "|" + index; }
		catch { return "?|" + index; }
	}

	/// <summary>The two clocks an activation is stamped with, or false when there is no live session
	/// (the auto skill can fire during the post-battle sequence, after the session closed -- and a row
	/// with no clock is better than a row stamped 0.00s, which would look like the battle start).</summary>
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

	/// <summary>
	/// The battle-end summary: the measured cadence per slot (median of the activation intervals, on both
	/// clocks) plus every counter, so one line answers "did it fire, how often, and was anything
	/// unreadable". Returns "" when the probe never saw a slot.
	/// </summary>
	internal static string Summary()
	{
		if (Slots.Count == 0) return "";
		StringBuilder sb = new StringBuilder(420);
		sb.Append("[AUTOSK] SUM samples=").Append(SampleRows)
			.Append(" actCmd=").Append(CommandActivations)
			.Append(" actPoll=").Append(PollActivations)
			.Append(" players=").Append(PlayersSeen)
			.Append(" viaOwner=").Append(PlayersViaOwner)
			.Append(" passiveFallback=").Append(PassiveFallbacks)
			.Append(" emptySlotReads=").Append(EmptySlots)
			.Append(" nullPlayers=").Append(NullPlayers)
			.Append(" readErrors=").Append(ReadErrors)
			.Append(" droppedSlots=").Append(SlotsDropped)
			.Append(" droppedIntervals=").Append(IntervalsDropped);
		foreach (KeyValuePair<string, SlotState> kv in Slots)
		{
			SlotState st = kv.Value;
			sb.Append("\n  ").Append(st.Unit).Append(" idx=").Append(st.Index)
				.Append(" skill=").Append(string.IsNullOrEmpty(st.SkillName) ? "-" : st.SkillName)
				.Append(" type=").Append(TypeNumber(st.LastType)).Append('(').Append(TypeName(st.LastType)).Append(')')
				.Append(" last=").Append(string.IsNullOrEmpty(st.LastStatus) ? "-" : st.LastStatus)
				// `rows`, not `act`: BOTH channels increment this (they are two independent routes to the
				// same event), so a normal battle with both channels live counts each activation twice.
				// The un-ambiguous per-activation totals are actCmd / actPoll in the header.
				.Append(" rows=").Append(st.Activations)
				.Append(" nInterval=").Append(st.WallIntervals.Count)
				.Append(" medianWall=").Append(Fmt(AutoSkillCadencePolicy.Median(st.WallIntervals.ToArray())))
				.Append(" medianActive=").Append(Fmt(AutoSkillCadencePolicy.Median(st.ActiveIntervals.ToArray())));
		}
		return sb.ToString();
	}

	private static string Fmt(double v)
	{
		return (v > 0.0) ? v.ToString("F2") + "s" : "-";
	}
}
