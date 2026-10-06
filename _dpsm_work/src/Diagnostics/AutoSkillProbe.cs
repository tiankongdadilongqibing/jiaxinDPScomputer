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
/// TWO CHANNELS, ON PURPOSE -- BUT ONLY ONE OF THEM IS AN ACTIVATION (corrected in R65, corrected AGAIN
/// in R69)
///   1. `via=cmd` -- a postfix on the game's own command entry point
///      `GameCmdExecuter.ActExecutePlayerAutoSkillForPassive(Player, int index, Vector3)`. **Every
///      published interval comes from here.** The raw `index` is printed as `gameIdx=` and the resolution
///      used is printed inside `via=` (`cmd/named` vs `cmd/rosterPos`), because the game's index semantics
///      are not yet settled.
///   2. `via=usingEdge` -- the rising edge of `GetStatus() == Using` on the same `Skill` objects. R64
///      treated this as a second activation channel and let it into the median. **MEASURED 2026-10-06: it
///      is not usable as one.** `Using` flickers, so for トレイラ it produced 9 "events" with a 1.10 s
///      median gap while the command channel showed real firing gaps of 8.08 and 10.18 s. It is kept as
///      evidence of the flicker, labelled `not an activation`, and excluded from every interval.
///
/// R69: A COMMAND CALL IS NOT AN ACTIVATION EITHER -- the call's own STATUS decides. The command returns
/// `ok=1` whether or not the skill fired (it is the passive's entry point, called once per attack), so
/// R66/R67 published every call as an activation and マッドシーカー's 99 game-second 実験失敗！ read as
/// "fires every ~5 s" (25 calls, 1 real activation, measured in quest 411001). Now a call is an activation
/// only when the skill's status at that moment is `Using` (<see cref="SkillActivationPolicy"/>); the
/// Charge/Usable calls are attempts, counted per row and in `[AUTOSK] SUM tries=`, and they never enter an
/// interval. The flicker above is why the STATUS alone is not enough either: the page folds repeated
/// `Using` calls by the skill's own cooldown (<see cref="SkillTimelinePolicy.FoldSeconds"/>), which is a
/// physical bound rather than a tuned window.
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
	/// <summary>R69: command calls that did NOT execute the skill (`status` Charge/Usable). These used to be
	/// published as activations -- マッドシーカー's 実験失敗！ (99 game-second counter) read as "fires every
	/// ~5 s" because 24 of her 25 calls arrived while the skill was still charging.</summary>
	internal static int AttemptRows;
	/// <summary>R69: command calls whose `status` was neither `Using` nor `Charge`/`Usable` -- counted, and
	/// deliberately NOT treated as activations (fail closed).</summary>
	internal static int UnclassifiedRows;
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

	/// <summary>R66 (renamed in R67 from `IndexAmbiguous`): command postfix calls where the game's `index`
	/// resolves to TWO DIFFERENT `Skill` objects depending on how it is read (1-based auto-skill slot
	/// `Player.AutoSkill1/2` versus the 0-based position in `Player.PassiveSkills`). NOT an ambiguity any
	/// more: MEASURED 2026-10-06 (first battle with `cmdidx` + `inst=`), `gameIdx=1` resolves only through
	/// the named slot and its roster-position reading is the nameless AutoSkill2 placeholder, while
	/// `gameIdx=0` resolves only through the roster position -- and both land on the SAME object (one `inst`
	/// tag per real auto skill). The resolver's order (named for 1/2, roster position otherwise) is right;
	/// this counter now only records how often the wrong reading WOULD have differed. MEASURED on 9 battles (471 rows): `gameIdx=0` occurs 24 times
	/// per (unit,skill) and resolves ONLY through the roster position, `gameIdx=1` resolves through the
	/// named slot -- and for マッドシーカー every activation arrived with `gameIdx=1`, which is why the two
	/// readings must be reported side by side instead of silently choosing one. The `[AUTOSK] cmdidx` line
	/// prints both candidates once per (unit, index) so the semantics stop being a guess.</summary>
	internal static int IndexReadingsDiffer;

	/// <summary>R67: sampler slot numbers of the unit's ACTIVE and SPECIAL skill (`Player.ActiveSkill` /
	/// `Player.SpecialSkill`). Outside 1/2 (the auto slots) and 100+ (the passive fallback) on purpose.</summary>
	internal const int ActiveSlotIndex = 10;
	internal const int SpecialSlotIndex = 11;

	private static readonly Dictionary<string, SlotState> Slots = new Dictionary<string, SlotState>();

	/// <summary>R66: per-battle tags for the `Skill` OBJECT identity (1, 2, 3 ...). The identity key used
	/// to be text only (EntryId|name|skillId|name), which MERGES two different objects that carry the same
	/// skill -- and the merged rows then look self-contradictory (measured 2026-10-06: マッドシーカー's
	/// counter read 2970/2970 at one activation and 2721/2970 at the next, which cannot be one object
	/// draining at 30 units/game-second). Tagging the pointer turns that into a visible fact.</summary>
	private static readonly Dictionary<long, int> InstanceTags = new Dictionary<long, int>();
	private static int _instSeq;

	/// <summary>(unit, gameIdx) pairs whose `cmdidx` diagnosis has already been printed.</summary>
	private static readonly HashSet<string> IndexLogged = new HashSet<string>();

	/// <summary>Units whose `[AUTOSK] roster` line has already been printed (once per battle).</summary>
	private static readonly HashSet<string> RosterLogged = new HashSet<string>();

	private static double _lastScanWall = double.MinValue;

	/// <summary>R74: the calibration sampler's own evidence, printed as one `[CLOCK] calib` line when the
	/// origin is decided (see `Aggregator.DecideClockOrigin`). Filled by every attempt inside the window,
	/// cleared per battle by <see cref="Reset"/>. Pure container: `Policy/ClockLagDiagnostics.cs`.</summary>
	internal static readonly ClockLagDiagnostics Calib = new ClockLagDiagnostics();

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
		/// <summary>R66: the object tag of the `Skill` this slot is keyed on, and its raw pointer (printed
		/// in the SUM line only), so "one skill" can be told from "two objects that share a name".</summary>
		internal int Instance;
		internal long Pointer;
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
		/// <summary>R69: calls of this slot that did NOT execute the skill (Charge/Usable). Published next to
		/// `n=` so a row can never be read as "it fired N times" when most calls were 試行.</summary>
		internal int Attempts;
		internal readonly List<double> WallIntervals = new List<double>();
		internal readonly List<double> ActiveIntervals = new List<double>();
	}

	internal static void Reset()
	{
		Slots.Clear();
		RosterLogged.Clear();
		IndexLogged.Clear();
		InstanceTags.Clear();
		_instSeq = 0;
		_lastScanWall = double.MinValue;
		Calib.Clear();
		SampleRows = 0;
		CommandActivations = 0;
		AttemptRows = 0;
		UnclassifiedRows = 0;
		UsingEdges = 0;
		CommandUnresolved = 0;
		PlaceholderSlots = 0;
		IndexReadingsDiffer = 0;
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
	/// R72: move the per-slot stamps that were taken before the battle clock's ORIGIN was decided onto the
	/// corrected axis. <see cref="SlotState.LastActive"/>/<see cref="SlotState.PrevActActive"/> are compared
	/// against the live clock, so leaving them on the old axis would make the first interval after the
	/// decision read 0.9 s too long (and the charge rate derived from it too slow) -- a probe artefact that
	/// would look like a game behaviour.
	///
	/// The WALL stamps deliberately do not move: real seconds are not a battle-clock quantity.
	/// </summary>
	internal static void ShiftBookkeeping(double delta)
	{
		if (!(delta > 0.0)) return;
		try
		{
			foreach (KeyValuePair<string, SlotState> kv in Slots)
			{
				SlotState st = kv.Value;
				if (st == null) continue;
				st.LastActive = ClockOriginHoldPolicy.Shift(st.LastActive, delta);
				if (st.HasActivation)
					st.PrevActActive = ClockOriginHoldPolicy.Shift(st.PrevActActive, delta);
			}
		}
		catch { ReadErrors++; }
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
			// R70: THE COMMAND LAYER IS PATCHED PROCESS-WIDE, SO IT SEES THE ENEMY TOO. This hook is on
			// `GameCmdExecuter.ActExecutePlayerAutoSkillForPassive`, which the enemy's units call exactly
			// like ours. MEASURED 2026-10-06 (quest 9999, 60.7 s): the 技能时间表 listed ムスクーマ and
			// ネフェスティス, and the export's `actors[].team` puts those two on team 2 ONLY -- i.e. the
			// page had been publishing the enemy's auto skills as ours since R66 (the `skl` channel had the
			// filter, this one never did; its SUM `foreignSide=9` is that channel's proof the filter works).
			// `CharacterInfo.IsAlly` is the same team test every other surface uses (team == 1).
			if (!CharacterInfo.IsAlly(player))
			{
				SkillTimelineProbe.NoteForeign(player, "cmd", index);
				return;
			}
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
			NoteIndexDiagnosis(player, index, resolvedBy);
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
		var party = new List<Player>(16);
		CollectParty(val, party, true);
		for (int i = 0; i < party.Count; i++) ObservePlayer(party[i], s, wall);
	}

	/// <summary>
	/// R70/R71: the party players the standby list exposes, in list order, de-duplicated by pointer and
	/// filtered to OUR SIDE -- ONE walk, used by the charge sampler and by R71's clock calibration, because
	/// two copies of "who is in this battle" would drift apart exactly the way the resolver and its
	/// diagnosis did in R65.
	///
	/// <paramref name="account"/> is true for the sampler (which owns `players=`/`viaOwner=` in its SUM
	/// line) and false for the one-shot calibration, so a calibration read cannot inflate the sampler's
	/// statistics.
	/// </summary>
	private static void CollectParty(GameSystem val, List<Player> into, bool account)
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
						if (!GameRef.IsNull(p) && account) PlayersViaOwner++;
					}
				}
			}
			catch { ReadErrors++; continue; }
			if (GameRef.IsNull(p)) continue;
			long ptr = PointerOf(p);
			if (ptr != 0L && !seen.Add(ptr)) continue;
			// R70: THE STANDBY LIST IS NOT PARTY-ONLY EITHER. MEASURED 2026-10-06 (quest 9999): the sampler
			// walked 9 units and two of them (ムスクーマ, ネフェスティス) exist ONLY on the enemy team in
			// that battle's export, so their `chg`/`roster` rows were the enemy's charge evidence sitting in
			// a page titled 我方. Same team test as everywhere else; counted AND written out once per unit,
			// because "the sampler skipped a unit" must not look like "this battle had fewer units".
			if (!CharacterInfo.IsAlly(p))
			{
				SkillTimelineProbe.NoteForeign(p, "chg", -1);
				continue;
			}
			if (account) PlayersSeen++;
			into.Add(p);
		}
	}

	/// <summary>
	/// R71: measure how much our battle clock LAGS the game's battle start, from the auto-skill slots that
	/// are still on their FIRST charge (the first charge in game UNITS vs `Skill.WaitCountFrame`; see
	/// <see cref="BattleClockCalibrationPolicy"/> for why that difference IS the origin offset, and R74's
	/// <see cref="BattleClockCalibrationPolicy.FirstCoolFrames"/> for which reading supplies the unit value).
	///
	/// Only the two NAMED auto slots are read. The 奥義/特殊 slots are not: their initial counter value was
	/// never measured, and a wrong-but-plausible reading there would shift every time in the battle.
	///
	/// Read-only, bounded (<see cref="BattleClockCalibrationPolicy.MaxSamples"/> slots), never throws: it
	/// runs on the frame path at the very start of a battle. Returns false when fewer than
	/// <see cref="BattleClockCalibrationPolicy.MinSamples"/> slots can answer -- the caller then keeps
	/// trying until the calibration window closes.
	/// </summary>
	internal static bool TryMeasureClockLag(GameSystem val, double unitsPerSecond, double activeSeconds,
		out double lag, out int samples)
	{
		lag = 0.0;
		samples = 0;
		try
		{
			Calib.Attempts++;
			if (val == null) return false;
			var party = new List<Player>(16);
			CollectParty(val, party, false);
			if (party.Count > Calib.Party) Calib.Party = party.Count;
			var lags = new List<double>(party.Count * 2);
			for (int i = 0; i < party.Count; i++)
			{
				AddClockLagSample(lags, NamedSlotSkill(party[i], 1), unitsPerSecond, activeSeconds);
				AddClockLagSample(lags, NamedSlotSkill(party[i], 2), unitsPerSecond, activeSeconds);
				if (lags.Count >= BattleClockCalibrationPolicy.MaxSamples) break;
			}
			samples = lags.Count;
			Calib.Usable += lags.Count;
			if (lags.Count < BattleClockCalibrationPolicy.MinSamples) return false;
			lag = BattleClockCalibrationPolicy.Combine(lags);
			return lag > 0.0;
		}
		catch { ReadErrors++; return false; }
	}

	private static void AddClockLagSample(List<double> into, Skill sk, double unitsPerSecond, double activeSeconds)
	{
		if (sk == null || into.Count >= BattleClockCalibrationPolicy.MaxSamples) return;
		// R74: BOTH readings, unconditionally. `FirstCoolTime` is the property R71/R72 wrongly handed to the
		// policy as a unit count, and `m_firstCoolTimeFrame` is the frame-denominated field the interop
		// metadata puts next to `m_coolTimeFrame` ON THE MASTER-DATA OBJECT (R74: `Skill.m_firstCoolTimeFrame`
		// is CS1061; `Skill.m_data.m_firstCoolTimeFrame` compiles). Printing them together is what settles the
		// unit question from the battle's own log instead of from the master table.
		int seconds = ReadFirstCoolSeconds(sk);
		int frame = ReadFirstCoolFrame(sk);
		int wait = 0;
		try { wait = sk.WaitCountFrame; } catch { ReadErrors++; return; }
		bool usedFallback;
		int first = BattleClockCalibrationPolicy.FirstCoolFrames(seconds, frame, unitsPerSecond, out usedFallback);
		Calib.SlotsRead++;
		Calib.NoteReading(seconds, frame, wait, usedFallback);
		double lag;
		BattleClockCalibrationPolicy.LagReason why =
			BattleClockCalibrationPolicy.Reject(first, wait, unitsPerSecond, activeSeconds, out lag);
		Calib.Note(why);
		if (why == BattleClockCalibrationPolicy.LagReason.Usable) into.Add(lag);
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
		// R67: the ACTIVE and SPECIAL skill slots, read-only, as slots 10/11. They are not auto skills and do
		// not count toward `live` (the passive fallback below is about auto skills only). WHY: the record
		// sink R66 relied on for 奥义 was never called, and before trusting a new command channel the probe
		// has to show WHAT these slots hold (`type=` names OverSkill/SpecialSkill/Skill) and how their
		// counters move -- the same evidence that let R65/R66 read the auto skill instead of guessing it.
		try
		{
			Skill act = null, spe = null;
			try { act = p.ActiveSkill; } catch { ReadErrors++; }
			try { spe = p.SpecialSkill; } catch { ReadErrors++; }
			SampleSlot(p, ActiveSlotIndex, act, s, wall);
			SampleSlot(p, SpecialSlotIndex, spe, s, wall);
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
				.Append("#").Append(InstanceTag(NamedSlotSkill(p, 1)))
				.Append(" auto2=").Append(SkillLabel(NamedSlotSkill(p, 2)))
				.Append("#").Append(InstanceTag(NamedSlotSkill(p, 2)));
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
					.Append("#").Append(InstanceTag(sk))
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
			st = new SlotState { Unit = unit, Index = index, SkillId = ReadSkillId(sk), Instance = InstanceTag(sk), Pointer = PointerOf(sk) };
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
			.Append(" inst=").Append(InstanceTag(sk))
			.Append(" skill=").Append(st.SkillName)
			.Append(" type=").Append(TypeNumber(type)).Append('(').Append(TypeName(type)).Append(')')
			.Append(" status=").Append(status)
			.Append(" wait=").Append(wait).Append('/').Append(cool)
			.Append(" ct=").Append(ReadCoolSeconds(sk))
			// R74: the two first-charge readings side by side. `first=` is the SECONDS property (what
			// R71/R72 used as if it were a unit count), `firstFrame=` is the frame-denominated field the
			// calibration now prefers. Printed on every sample so one battle answers both open questions:
			// "seconds or units" and "initial value or live remaining".
			.Append(" first=").Append(ReadFirstCoolSeconds(sk))
			.Append(" firstFrame=").Append(ReadFirstCoolFrame(sk))
			.Append(" dur=").Append(ReadDuration(sk))
			.Append(" stock=").Append(ReadStock(sk))
			.Append(" passiveAuto=").Append(ReadIsPassiveAuto(sk) ? 1 : 0)
			.Append(" autoActivate=").Append(ReadAutoActivate(sk))
			.Append(" act=").Append(ReadActivationType(sk))
			.Append(" actP=").Append(ReadActivationParam(sk))
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
	/// One command call: logged ALWAYS, counted as an activation only when the game's own status says the
	/// skill executed (R69). `via` names the channel AND how the skill was resolved (`cmd/named`,
	/// `cmd/rosterPos`), so the two readings of the game's index stay distinguishable.
	///
	/// WHY THE ROW IS WRITTEN EVEN FOR AN ATTEMPT. That row IS the evidence: マッドシーカー's 25 calls with
	/// `status=Charge` and 1 with `status=Using` are what proved the page had been publishing 試行 as 発動.
	/// A probe that stops logging the calls it decided against cannot be audited, so the row carries
	/// `verdict=act|try|unknown` and only `act` rows touch the interval bookkeeping, `n=` and the page.
	/// </summary>
	private static void NoteActivation(Player player, int index, Skill sk, string via, int result)
	{
		string unit = UnitLabel(player);
		string key = KeyOf(player, sk);
		SlotState st;
		if (!Slots.TryGetValue(key, out st))
		{
			if (Slots.Count >= MaxSlots) { SlotsDropped++; return; }
			st = new SlotState { Unit = unit, Index = index, SkillId = ReadSkillId(sk), Instance = InstanceTag(sk), Pointer = PointerOf(sk) };
			Slots[key] = st;
		}
		string status = StatusName(sk);
		bool activation = SkillActivationPolicy.IsActivation(status);
		bool attempt = !activation && SkillActivationPolicy.IsAttempt(status);
		string verdict = activation ? "act" : (attempt ? "try" : "unknown");

		double wall, active;
		bool clocked = Clocks(out wall, out active);

		// The interval is measured on BOTH clocks from the same stored previous moment, so the two
		// published numbers describe the same pair of activations -- and ONLY activations: an attempt has no
		// moment to measure from, which is exactly the defect R69 fixed.
		double dWall = 0.0, dActive = 0.0;
		if (activation)
		{
			CommandActivations++;
			st.Activations++;
			// R66: the SAME activation feeds the 技能时间表 page. Called from here (and not from the hook)
			// because this is the one place that has already decided "this is an activation of this Skill",
			// so the page and the probe can never disagree about what counts. R69 adds the cooldown the page
			// folds by.
			SkillTimelineProbe.NoteCommandActivation(player, sk, CoolSeconds(sk));
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
		}
		else
		{
			st.Attempts++;
			if (attempt) { AttemptRows++; SkillTimelineProbe.NoteCommandAttempt(player, sk); }
			else UnclassifiedRows++;
		}

		StringBuilder sb = new StringBuilder(280);
		sb.Append("[AUTOSK] act via=").Append(via)
			.Append(" wall=").Append(clocked ? wall.ToString("F2") + "s" : "-")
			.Append(" active=").Append(clocked ? active.ToString("F2") + "s" : "-")
			.Append(" unit=").Append(unit)
			.Append(" gameIdx=").Append(index)
			.Append(" skillId=").Append(st.SkillId)
			.Append(" inst=").Append(InstanceTag(sk))
			.Append(" skill=").Append(SkillLabel(sk))
			.Append(" type=").Append(TypeNumber(ReadType(sk))).Append('(').Append(TypeName(ReadType(sk))).Append(')')
			.Append(" verdict=").Append(verdict)
			.Append(" status=").Append(status)
			.Append(" wait=").Append(ReadWait(sk)).Append('/').Append(ReadCoolFrames(sk))
			.Append(" ct=").Append(ReadCoolSeconds(sk))
			.Append(" dur=").Append(ReadDuration(sk))
			.Append(" stock=").Append(ReadStock(sk))
			.Append(" act=").Append(ReadActivationType(sk))
			.Append(" actP=").Append(ReadActivationParam(sk))
			.Append(" level=").Append(ReadLevel(sk))
			.Append(" ok=").Append(result)
			.Append(" n=").Append(st.Activations)
			.Append(" tries=").Append(st.Attempts)
			.Append(" dWall=").Append(dWall > 0.0 ? dWall.ToString("F2") + "s" : "-")
			.Append(" dActive=").Append(dActive > 0.0 ? dActive.ToString("F2") + "s" : "-");
		RuntimeLog.Write(sb.ToString());
		if (st.WallIntervals.Count > MaxActivationRows) IntervalsDropped++;
	}

	/// <summary>R69: the skill's own cooldown in GAME seconds, read for the page's fold
	/// (`CoolTimeFrame / 30`, so a 100-frame cooldown stays 3.33 s instead of the truncated `ct=3`).</summary>
	private static double CoolSeconds(Skill sk)
	{
		int frames = ReadCoolFrames(sk);
		return (frames > 0) ? frames / 30.0 : 0.0;
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
		Skill roster = RosterPosSkill(p, gameIdx);
		if (roster != null) { resolvedBy = "rosterPos"; return roster; }
		return null;
	}

	/// <summary>The second reading of the game's index: the `gameIdx`-th entry of `Player.PassiveSkills`
	/// that actually carries an `AutoSkill`. Shared by the resolver and the R66 diagnosis on purpose -- two
	/// copies of this walk are exactly how the resolver and its evidence would drift apart.</summary>
	private static Skill RosterPosSkill(Player p, int gameIdx)
	{
		if (p == null || gameIdx < 0) return null;
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
				if (seen == gameIdx) return sk;
				seen++;
			}
		}
		catch { ReadErrors++; }
		return null;
	}

	/// <summary>
	/// R66: print what the game's raw `index` selects under BOTH readings, once per (unit, index), and count
	/// the cases where they disagree.
	///
	/// MEASURED on 9 battles / 471 activation rows: the index is only ever 0 or 1; `0` resolves through the
	/// roster position and `1` through `Player.AutoSkill1`; the per-slot activation counter `n=` is strictly
	/// increasing ACROSS the two paths, so both paths address the same skill identity. What is still not
	/// settled is whether they address the same OBJECT (the identity key is text, so two objects sharing a
	/// name would merge) -- and whether `1` means "slot 1" or "roster position 1" (whose entry is the
	/// nameless `AutoSkill2` placeholder). This line, plus `inst=`, is what answers both next battle.
	/// </summary>
	private static void NoteIndexDiagnosis(Player p, int gameIdx, string resolvedBy)
	{
		try
		{
			string unit = UnitLabel(p);
			if (!IndexLogged.Add(unit + "|" + gameIdx)) return;
			Skill named = (gameIdx == 1 || gameIdx == 2) ? NamedSlotSkill(p, gameIdx) : null;
			Skill roster = RosterPosSkill(p, gameIdx);
			bool both = (named != null && roster != null);
			bool agree = both && PointerOf(named) == PointerOf(roster);
			if (both && !agree) IndexReadingsDiffer++;
			RuntimeLog.Write("[AUTOSK] cmdidx unit=" + unit + " gameIdx=" + gameIdx
				+ " named=" + SkillLabel(named) + "#" + InstanceTag(named)
				+ " rosterPos=" + SkillLabel(roster) + "#" + InstanceTag(roster)
				+ " agree=" + (agree ? 1 : 0)
				+ " chose=" + resolvedBy
				+ " (0/1: which reading the game's index means is settled by these two columns)");
		}
		catch { ReadErrors++; }
	}

	/// <summary>R66: the per-battle tag of a `Skill` OBJECT (see <see cref="InstanceTags"/>). 0 when the
	/// pointer cannot be read, which the log prints as `#0` rather than hiding it.</summary>
	private static int InstanceTag(Skill sk)
	{
		long p = PointerOf(sk);
		if (p == 0L) return 0;
		int tag;
		if (InstanceTags.TryGetValue(p, out tag)) return tag;
		tag = ++_instSeq;
		InstanceTags[p] = tag;
		return tag;
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

	/// <summary>R74: `Skill.FirstCoolTime`, the SECONDS sibling of `Skill.CoolTime` (see
	/// `BattleClockCalibrationPolicy.FirstCoolFrames` for the unit argument). Read for the diagnostic and as
	/// the fallback source only; `int.MinValue` = unreadable, never a silent 0.</summary>
	private static int ReadFirstCoolSeconds(Skill sk)
	{
		if (sk == null) return int.MinValue;
		try { return sk.FirstCoolTime; } catch { ReadErrors++; return int.MinValue; }
	}

	/// <summary>R74: `m_firstCoolTimeFrame`, the frame-denominated first charge (the sibling of
	/// `m_coolTimeFrame`, produced by the game's own `CalcFirstCoolTimeFrame`). `int.MinValue` = unreadable,
	/// never a silent 0.</summary>
	private static int ReadFirstCoolFrame(Skill sk)
	{
		if (sk == null) return int.MinValue;
		try { return sk.m_data.m_firstCoolTimeFrame; } catch { ReadErrors++; return int.MinValue; }
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

	/// <summary>R66: `Skill.ActivationType` -- the game's own answer to "WHAT makes this skill fire".
	/// Printed as its raw number on purpose: the enum's member names are not reachable from the interop
	/// surface this plugin compiles against (checked 2026-10-06), and the QUESTION it has to settle is
	/// comparative -- マッドシーカー fires every ~5 game s while its counter reads 2870/2970, so if this
	/// number differs from the units whose counter really does gate the skill, that is the answer.</summary>
	private static int ReadActivationType(Skill sk)
	{
		if (sk == null) return int.MinValue;
		try { return (int)sk.ActivationType; } catch { ReadErrors++; return int.MinValue; }
	}

	/// <summary>R66: `Skill.ActivationTypeParam`, the parameter of <see cref="ReadActivationType"/>.</summary>
	private static int ReadActivationParam(Skill sk)
	{
		if (sk == null) return int.MinValue;
		try { return sk.ActivationTypeParam; } catch { ReadErrors++; return int.MinValue; }
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

	private static long PointerOf(Il2CppObjectBase o)
	{
		try { return (long)o.Pointer; }
		catch { return 0L; }
	}

	/// <summary>R65 (extended in R66 with the object tag): keyed by the SKILL's identity, not by the slot
	/// number -- `Player.AutoSkill1` can hand back a different `Skill` over time (measured 2026-10-06:
	/// T.O.W.E.R.typeR's slot read CoolTimeFrame 420 in two `cmd` rows and 300 in every `chg` sample), and
	/// keying by index silently merged the two into one self-contradictory row. R66 appends the OBJECT tag,
	/// because the text identity alone still merges two distinct objects that carry the same skill (the
	/// 2970/2970-then-2721/2970 pair the same battle produced).</summary>
	private static string KeyOf(Player p, Skill sk)
	{
		try { return p.EntryId + "|" + p.Name + "|" + ReadSkillId(sk) + "|" + (sk == null ? "-" : sk.Name) + "|" + InstanceTag(sk); }
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
			.Append(" tries=").Append(AttemptRows)
			.Append(" unclassified=").Append(UnclassifiedRows)
			.Append(" foreignSide=").Append(SkillTimelineProbe.ForeignCommand)
			.Append(" foreignUnits=").Append(SkillTimelineProbe.ForeignSample)
			.Append(" cmdUnresolved=").Append(CommandUnresolved)
			.Append(" indexReadingsDiffer=").Append(IndexReadingsDiffer)
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
				.Append(" inst=").Append(st.Instance)
				.Append(" ptr=0x").Append(st.Pointer.ToString("X"))
				.Append(" skillId=").Append(st.SkillId)
				.Append(" skill=").Append(string.IsNullOrEmpty(st.SkillName) ? "-" : st.SkillName)
				.Append(" type=").Append(TypeNumber(st.LastType)).Append('(').Append(TypeName(st.LastType)).Append(')')
				.Append(" last=").Append(string.IsNullOrEmpty(st.LastStatus) ? "-" : st.LastStatus)
				.Append(st.Quiet ? " QUIET(placeholder)" : "")
				// `rows`, not `act`: this counts every row written for the slot. The un-ambiguous
				// activation total is actCmd in the header.
				.Append(" rows=").Append(st.Activations)
				.Append(" tries=").Append(st.Attempts)
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
