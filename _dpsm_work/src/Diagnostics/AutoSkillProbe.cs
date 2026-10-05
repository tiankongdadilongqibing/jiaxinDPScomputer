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
/// TWO CHANNELS, ON PURPOSE -- BUT ONLY ONE OF THEM IS AN ACTIVATION (corrected in R65)
///   1. `via=cmd` -- a postfix on the game's own command entry point
///      `GameCmdExecuter.ActExecutePlayerAutoSkillForPassive(Player, int index, Vector3)`. This is the
///      activation, and it carries the game's own return value. **Every published interval comes from
///      here.** The raw `index` is printed as `gameIdx=` and the resolution used is printed inside `via=`
///      (`cmd/named` vs `cmd/rosterPos`), because the game's index semantics are not yet settled.
///   2. `via=usingEdge` -- the rising edge of `GetStatus() == Using` on the same `Skill` objects. R64
///      treated this as a second activation channel and let it into the median. **MEASURED 2026-10-06: it
///      is not an activation.** `Using` flickers, so for トレイラ it produced 9 "events" with a 1.10 s
///      median gap while the command channel showed real firing gaps of 8.08 and 10.18 s. It is kept as
///      evidence of the flicker, labelled `not an activation`, and excluded from every interval.
///
/// The sampler is read-only, bounded (one line per slot per <see cref="SampleSeconds"/>, at most
/// <see cref="MaxSlots"/> slots, at most <see cref="MaxActivationRows"/> intervals), and counts every
/// failure, so "this unit has no auto skill" and "the probe could not read it" never look alike.
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
	private const int MaxSlots = 64;
	private const int MaxActivationRows = 400;
	private const int MaxFallbackPassives = 8;

	/// <summary>Charge lines written.</summary>
	internal static int SampleRows;
	/// <summary>Activation rows written from the command postfix. THIS is the channel the published
	/// interval comes from.</summary>
	internal static int CommandActivations;
	/// <summary>Rows written from the sampler's `GetStatus() == Using` rising edge. R65: NOT an activation
	/// and deliberately EXCLUDED from every interval. Measured 2026-10-06: `Using` flickers, so this edge
	/// fired 9 times in 44 s for トレイラ (median gap 1.10 s) while her real firing gaps were 8.08 and
	/// 10.18 s. It is kept only as evidence of that flicker.</summary>
	internal static int UsingEdges;
	/// <summary>Field reads that threw (an unreadable slot, not a missing one).</summary>
	internal static int ReadErrors;
	/// <summary>Slot READS that found no auto skill at all (the normal case for most units, and most units
	/// are read twice per scan, so this is a read count and not a distinct-slot count).</summary>
	internal static int EmptySlots;
	/// <summary>Distinct party players seen in the standby list.</summary>
	internal static int PlayersSeen;
	/// <summary>Players reached only through `Skill.m_owner` because the standby row's player was null.</summary>
	internal static int PlayersViaOwner;
	/// <summary>Slots reached by scanning `Player.PassiveSkills`.</summary>
	internal static int PassiveFallbacks;
	/// <summary>Intervals that exceeded <see cref="MaxActivationRows"/> and were therefore not kept for
	/// the median. The activation ROWS themselves are all written to the runtime log regardless.</summary>
	internal static int IntervalsDropped;
	/// <summary>Command postfix calls that arrived with a NULL `Player`. Counted because "the command ran
	/// but carried no player" is a fact about the game, not a probe failure.</summary>
	internal static int NullPlayers;
	/// <summary>Slots dropped after <see cref="MaxSlots"/>.</summary>
	internal static int SlotsDropped;
	/// <summary>R65: command postfix calls whose slot index the probe could NOT resolve to a `Skill`.
	/// These used to create a nameless slot (measured 2026-10-06: 32 rows) and, worse, those junk slots
	/// consumed the slot budget so that 9 REAL slots were evicted. Now they are counted here and write a
	/// single row each, so "the game passed an index I do not understand" is visible instead of silent.
	/// The game's raw index is printed verbatim in `gameIdx=`.</summary>
	internal static int CommandUnresolved;
	/// <summary>R65: slots that hold a placeholder `Skill` (no name, `CoolTimeFrame == 0`, `WaitCountFrame
	/// == 0`) -- measured 2026-10-06: `Player.AutoSkill2` is such a placeholder on most units. They are
	/// printed ONCE each (so the fact is recorded) and then left silent, which is what frees the slot
	/// budget for real skills.</summary>
	internal static int PlaceholderSlots;

	private static readonly Dictionary<string, SlotState> Slots = new Dictionary<string, SlotState>();

	/// <summary>Units whose `[AUTOSK] roster` line has already been printed (once per battle).</summary>
	private static readonly HashSet<string> RosterLogged = new HashSet<string>();

	private static double _lastScanWall = double.MinValue;

	/// <summary>Per (unit, slot) state. R65: keyed by `EntryId|name|skillId` -- NOT by the slot index --
	/// because `Player.AutoSkill1` can hand back a DIFFERENT `Skill` over time (measured 2026-10-06:
	/// T.O.W.E.R.typeR's slot read `CoolTimeFrame` 420 in two `cmd` rows and 300 in every `chg` sample),
	/// and keying by index merged the two into one contradiction.</summary>
	private sealed class SlotState
	{
		internal string Unit;
		internal string SkillName;
		internal int SkillId;
		internal int Index;
		internal int LastType = int.MinValue;
		internal string LastStatus = "";
		internal int LastUsing;
		internal bool Scanned;
		internal bool Logged;
		internal bool Quiet;
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
		RosterLogged.Clear();
		_lastScanWall = double.MinValue;
		SampleRows = 0;
		CommandActivations = 0;
		UsingEdges = 0;
		CommandUnresolved = 0;
		PlaceholderSlots = 0;
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
	/// R65: the game's `index` is reported VERBATIM as `gameIdx=` and is NOT assumed to be the probe's own
	/// slot number. Measured 2026-10-06 it is 0 or 1: `1` resolved to a `Skill.Type == 3`
	/// (AutoSkill1ForPassiveSkill) through `Player.AutoSkill1`, and `0` resolved to nothing at all. So the
	/// probe resolves the skill first and, when it cannot, records the raw index and ONE row instead of
	/// inventing a nameless slot. What that `0` actually selects is still open; the roster printed by
	/// `Scan` (`Player.PassiveSkills` with each `passiveIdx`) is what will settle it.
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
			string resolvedBy;
			Skill sk = ResolveCommandSkill(player, index, out resolvedBy);
			if (sk == null)
			{
				CommandUnresolved++;
				if (CommandUnresolved <= 8) RuntimeLog.Write(
					"[AUTOSK] cmdunres unit=" + UnitLabel(player) + " gameIdx=" + index
					+ " ok=" + (result ? 1 : 0) + " (no Skill resolved for this index; not counted as an activation)");
				return;
			}
			NoteActivation(player, index, sk, "cmd/" + resolvedBy, result ? 1 : 0);
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
		// R65: the passive roster is printed ONCE per unit per battle. It is the data that settles what the
		// game's `index` means -- without it the raw index in the `cmd` rows cannot be matched to anything.
		LogRoster(p);

		int live = 0;
		try
		{
			live += SampleSlot(p, 1, NamedSlotSkill(p, 1), s, wall);
			live += SampleSlot(p, 2, NamedSlotSkill(p, 2), s, wall);
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

	/// <summary>R65: one `[AUTOSK] roster` line per unit per battle -- every `Player.PassiveSkills` entry
	/// with its `Index`, its `AutoSkill`'s name/`Skill.Type`/`CoolTimeFrame`/`WaitCountFrame`, and the
	/// two named slots beside them. Bounded and printed once, so "which passive carries which auto skill,
	/// and what number does the game's `index` refer to" is answered from data instead of assumed.</summary>
	private static void LogRoster(Player p)
	{
		try
		{
			string unit = UnitLabel(p);
			// Keyed by EntryId AND name: two units can share a display name, and hiding the second one's
			// roster would hide exactly the case this line exists to expose.
			string rosterKey;
			try { rosterKey = p.EntryId + "|" + unit; } catch { rosterKey = unit; }
			if (!RosterLogged.Add(rosterKey)) return;
			StringBuilder sb = new StringBuilder(300);
			sb.Append("[AUTOSK] roster unit=").Append(unit)
				.Append(" auto1=").Append(SkillLabel(NamedSlotSkill(p, 1)))
				.Append(" auto2=").Append(SkillLabel(NamedSlotSkill(p, 2)));
			Il2CppReferenceArray<PassiveSkill> arr = null;
			try { arr = p.PassiveSkills; } catch { ReadErrors++; }
			if (arr == null) { sb.Append(" passiveSkills=null"); RuntimeLog.Write(sb.ToString()); return; }
			sb.Append(" passiveSkills=").Append(arr.Length);
			int n = arr.Length;
			if (n > MaxFallbackPassives) n = MaxFallbackPassives;
			for (int i = 0; i < n; i++)
			{
				PassiveSkill ps = null;
				try { ps = arr[i]; } catch { ReadErrors++; continue; }
				if (ps == null) continue;
				int pidx = int.MinValue;
				Skill sk = null;
				try { pidx = ps.Index; } catch { ReadErrors++; }
				try { sk = ps.AutoSkill; } catch { ReadErrors++; }
				sb.Append(" [pos=").Append(i).Append(" passiveIdx=").Append(pidx)
					.Append(" skill=").Append(SkillLabel(sk))
					.Append(" type=").Append(TypeNumber(ReadType(sk)))
					.Append(" wait=").Append(ReadWait(sk)).Append('/').Append(ReadCoolFrames(sk))
					.Append(']');
			}
			RuntimeLog.Write(sb.ToString());
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
		string key = KeyOf(p, sk);
		SlotState st;
		if (!Slots.TryGetValue(key, out st))
		{
			if (Slots.Count >= MaxSlots) { SlotsDropped++; return 1; }
			st = new SlotState { Unit = unit, Index = index, SkillId = ReadSkillId(sk) };
			Slots[key] = st;
		}
		st.Unit = unit;

		int type = ReadType(sk);
		string status = StatusName(sk);
		int wait = ReadWait(sk);
		int cool = ReadCoolFrames(sk);
		int usingNow = ((status == "Using") || ReadActivated(sk)) ? 1 : 0;

		// ---- the `Using` rising edge: evidence of the game's own state machine, NOT an activation ----
		// R65: this used to be recorded (and counted) as an activation. Measured 2026-10-06 it is not one:
		// `Using` flickers, so for トレイラ it produced 9 "events" with a 1.10 s median gap while her real
		// firing gaps were 8.08 and 10.18 s. It is now labelled `via=usingEdge` and EXCLUDED from every
		// interval; the published cadence comes from the command channel only.
		if (st.Scanned && st.LastUsing == 0 && usingNow == 1)
		{
			UsingEdges++;
			string line = "[AUTOSK] usingEdge wall=" + wall.ToString("F2") + "s"
				+ " active=" + s.ActiveSeconds.ToString("F2") + "s"
				+ " unit=" + unit + " idx=" + index + " skill=" + SkillLabel(sk)
				+ " type=" + TypeNumber(type) + " wait=" + wait + "/" + cool
				+ " (not an activation; excluded from the median)";
			RuntimeLog.Write(line);
		}
		st.Scanned = true;
		st.LastUsing = usingNow;
		st.LastType = type;
		st.LastStatus = status;

		// ---- R65: a placeholder slot is printed ONCE and then left silent ----
		// Measured: `Player.AutoSkill2` is a nameless `Skill` with CoolTimeFrame=0 and WaitCountFrame=0 on
		// most units. Printing it every 2 s was pure noise AND it consumed the slot budget (24 slots, 9
		// of them placeholders, so 9 real slots were evicted). Printing it once keeps the fact.
		bool placeholder = (cool == 0 && wait == 0);
		if (placeholder)
		{
			if (!st.Logged)
			{
				PlaceholderSlots++;
				st.Logged = true;
				st.Quiet = true;
				RuntimeLog.Write("[AUTOSK] placeholder unit=" + unit + " idx=" + index + " skillId=" + st.SkillId
					+ " type=" + TypeNumber(type) + " name=" + SkillLabel(sk)
					+ " (CoolTimeFrame=0 and WaitCountFrame=0; printed once, then silent)");
			}
			return 1;
		}
		st.Quiet = false;

		// ---- the charge trajectory ----
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

		StringBuilder sb = new StringBuilder(280);
		sb.Append("[AUTOSK] chg wall=").Append(wall.ToString("F2")).Append('s')
			.Append(" active=").Append(s.ActiveSeconds.ToString("F2")).Append('s')
			.Append(" unit=").Append(unit)
			.Append(" idx=").Append(index)
			.Append(" skillId=").Append(st.SkillId)
			.Append(" skill=").Append(st.SkillName)
			.Append(" type=").Append(TypeNumber(type)).Append('(').Append(TypeName(type)).Append(')')
			.Append(" status=").Append(status)
			.Append(" wait=").Append(wait).Append('/').Append(cool)
			.Append(" ct=").Append(ReadCoolSeconds(sk))
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
				.Append(" upsWall=").Append(upsWall.ToString("F1"));
			// R65: only when the counter really drained. A reset sample makes `upsGame` non-positive, and
			// printing `chargeSec=0.0s` there reads like "a zero-second cooldown" instead of "not derivable".
			if (upsGame > 0.0)
				sb.Append(" chargeSec=").Append(AutoSkillCadencePolicy.SecondsFor(cool, upsGame).ToString("F1")).Append('s');
		}
		RuntimeLog.Write(sb.ToString());
		SampleRows++;
		return 1;
	}

	/// <summary>
	/// One activation row. `via` names the channel AND how the skill was resolved
	/// (`cmd/named`, `cmd/rosterPos`), so the two readings of the game's index stay distinguishable.
	/// R65: only `cmd*` rows feed the interval; the `usingEdge` rows are written by `SampleSlot` and are
	/// deliberately not routed here at all.
	/// </summary>
	private static void NoteActivation(Player player, int index, Skill sk, string via, int result)
	{
		string unit = UnitLabel(player);
		string key = KeyOf(player, sk);
		SlotState st;
		if (!Slots.TryGetValue(key, out st))
		{
			if (Slots.Count >= MaxSlots) { SlotsDropped++; return; }
			st = new SlotState { Unit = unit, Index = index, SkillId = ReadSkillId(sk) };
			Slots[key] = st;
		}
		CommandActivations++;
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

		StringBuilder sb = new StringBuilder(250);
		sb.Append("[AUTOSK] act via=").Append(via)
			.Append(" wall=").Append(clocked ? wall.ToString("F2") + "s" : "-")
			.Append(" active=").Append(clocked ? active.ToString("F2") + "s" : "-")
			.Append(" unit=").Append(unit)
			.Append(" gameIdx=").Append(index)
			.Append(" skillId=").Append(st.SkillId)
			.Append(" skill=").Append(SkillLabel(sk))
			.Append(" type=").Append(TypeNumber(ReadType(sk))).Append('(').Append(TypeName(ReadType(sk))).Append(')')
			.Append(" status=").Append(StatusName(sk))
			.Append(" wait=").Append(ReadWait(sk)).Append('/').Append(ReadCoolFrames(sk))
			.Append(" ct=").Append(ReadCoolSeconds(sk))
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

	/// <summary>The two NAMED auto-skill slots the game exposes on `Player` (1 = `AutoSkill1`,
	/// 2 = `AutoSkill2`). The sampler walks exactly these two.</summary>
	private static Skill NamedSlotSkill(Player p, int index)
	{
		try
		{
			if (index == 1) return p.AutoSkill1;
			if (index == 2) return p.AutoSkill2;
			return null;
		}
		catch { ReadErrors++; return null; }
	}

	/// <summary>
	/// R65: resolve the GAME's raw `index` (from `ActExecutePlayerAutoSkillForPassive`) to a `Skill`.
	///
	/// Two readings are tried, and `ResolvedBy` records which one answered, because the data does not yet
	/// say which is correct:
	///   1. `named`     -- the game's index as the probe's own slot number (1 -> `AutoSkill1`,
	///                      2 -> `AutoSkill2`). This is what the 2026-10-06 battle supports: the only
	///                      index that resolved at all was 1, and it resolved to a `Skill.Type == 3`
	///                      (AutoSkill1ForPassiveSkill) skill.
	///   2. `rosterPos` -- the game's index as a 0-based position in `Player.PassiveSkills`, taking the
	///                      entries that actually carry an `AutoSkill`.
	/// Returning null is a legitimate answer: the caller counts it and writes ONE row saying so, instead
	/// of inventing a nameless slot the way 1.7.16 did.
	/// </summary>
	private static Skill ResolveCommandSkill(Player p, int gameIdx, out string resolvedBy)
	{
		resolvedBy = "none";
		if (gameIdx == 1 || gameIdx == 2)
		{
			Skill named = NamedSlotSkill(p, gameIdx);
			if (named != null) { resolvedBy = "named"; return named; }
		}
		try
		{
			Il2CppReferenceArray<PassiveSkill> arr = p.PassiveSkills;
			if (arr == null) return null;
			int n = arr.Length;
			if (n > MaxFallbackPassives) n = MaxFallbackPassives;
			int seen = 0;
			for (int i = 0; i < n; i++)
			{
				PassiveSkill ps = null;
				try { ps = arr[i]; } catch { ReadErrors++; continue; }
				if (ps == null) continue;
				Skill sk = null;
				try { sk = ps.AutoSkill; } catch { ReadErrors++; continue; }
				if (sk == null) continue;
				if (seen == gameIdx) { resolvedBy = "rosterPos"; return sk; }
				seen++;
			}
		}
		catch { ReadErrors++; }
		return null;
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

	private static int ReadCoolSeconds(Skill sk)
	{
		if (sk == null) return int.MinValue;
		try { return sk.CoolTime; } catch { ReadErrors++; return int.MinValue; }
	}

	private static int ReadSkillId(Skill sk)
	{
		if (sk == null) return int.MinValue;
		try { return sk.m_data.Id; } catch { ReadErrors++; return int.MinValue; }
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

	/// <summary>R65: keyed by the SKILL's identity, not by the slot number -- `Player.AutoSkill1` can hand
	/// back a different `Skill` over time (measured 2026-10-06: T.O.W.E.R.typeR's slot read CoolTimeFrame
	/// 420 in two `cmd` rows and 300 in every `chg` sample), and keying by index silently merged the two
	/// into one self-contradictory row.</summary>
	private static string KeyOf(Player p, Skill sk)
	{
		try { return p.EntryId + "|" + p.Name + "|" + ReadSkillId(sk) + "|" + (sk == null ? "-" : sk.Name); }
		catch { return "?|" + ReadSkillId(sk); }
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
	///
	/// R65: `medianWall`/`medianActive` are computed from the COMMAND channel only. If `actCmd=0` they read
	/// `-`, which means "no activation was observed", NOT "no interval" -- the `usingEdge` rows are
	/// evidence of the game's own state machine and are never an interval.
	/// </summary>
	internal static string Summary()
	{
		if (Slots.Count == 0) return "";
		StringBuilder sb = new StringBuilder(480);
		sb.Append("[AUTOSK] SUM samples=").Append(SampleRows)
			.Append(" actCmd=").Append(CommandActivations)
			.Append(" cmdUnresolved=").Append(CommandUnresolved)
			.Append(" useEdges=").Append(UsingEdges)
			.Append(" players=").Append(PlayersSeen)
			.Append(" viaOwner=").Append(PlayersViaOwner)
			.Append(" passive=").Append(PassiveFallbacks)
			.Append(" placeholders=").Append(PlaceholderSlots)
			.Append(" emptySlotReads=").Append(EmptySlots)
			.Append(" nullPlayers=").Append(NullPlayers)
			.Append(" readErrors=").Append(ReadErrors)
			.Append(" droppedSlots=").Append(SlotsDropped)
			.Append(" droppedIntervals=").Append(IntervalsDropped);
		foreach (KeyValuePair<string, SlotState> kv in Slots)
		{
			SlotState st = kv.Value;
			sb.Append("\n  ").Append(st.Unit).Append(" idx=").Append(st.Index)
				.Append(" skillId=").Append(st.SkillId)
				.Append(" skill=").Append(string.IsNullOrEmpty(st.SkillName) ? "-" : st.SkillName)
				.Append(" type=").Append(TypeNumber(st.LastType)).Append('(').Append(TypeName(st.LastType)).Append(')')
				.Append(" last=").Append(string.IsNullOrEmpty(st.LastStatus) ? "-" : st.LastStatus)
				.Append(st.Quiet ? " QUIET(placeholder)" : "")
				// `rows`, not `act`: this counts every row written for the slot. The un-ambiguous
				// activation total is actCmd in the header.
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
