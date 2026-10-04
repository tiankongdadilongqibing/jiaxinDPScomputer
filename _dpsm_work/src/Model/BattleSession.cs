using System;
using System.Collections.Generic;
using UnityEngine;

namespace DpsMeter;

public sealed class BattleSession
{
	public bool InBattle;

	public GameResult Result;

	public int QuestId;

	/// <summary>
	/// Grouping marker (1.3.3): which CONTINUOUS stretch of play this session belongs to.
	///
	/// Wave/arena content fires the game's battle-end signal once per wave, and those signals carry no
	/// result, so the meter closes and reopens a session each time -- measured 2026-10-03: a ~45 s arena
	/// stage produced 13 sessions/exports, and 116 of 757 exports (15.3%) are 0.0 s shells
	/// (worst quest: 302158 at 79.3%).
	///
	/// The numbers are deliberately NOT merged here: a wrong merge silently inflates every total, which is
	/// worse than a fragment. Instead every session of one stretch carries the same <see cref="RunId"/>,
	/// so offline analysis can add the fragments up itself and can always see which decision was taken.
	/// A run only continues when the previous session ended WITHOUT a result, on the same quest, within
	/// 2 s; a finished battle (Win/Lose) therefore always starts a new run.
	/// </summary>
	public long RunId;

	/// <summary>0-based position of this session inside its run.</summary>
	public int RunSeq;

	/// <summary>Seconds since the previous session of the run ended. -1 = this session starts a run.</summary>
	public double RunGap;

	/// <summary>How the previous session ended (idle / end / teardown / restart). "" when starting a run.</summary>
	public string RunPrevWhy;

	/// <summary>The previous session's result (0 = it had no result, which is why the run continues).</summary>
	public int RunPrevResult;

	public DateTime StartWallClock;

	public DateTime LastEventWall;

	/// <summary>
	/// THE battle clock, in real seconds, counted from this session's first tick.
	///
	/// Every consumer reads it and nothing else may accumulate time:
	///   * the overlay roster header ("时间 N秒")            -- OverlayUGUI.Rows
	///   * the F6 per-hit line ("t=N.Ns")                   -- via BattleEvent.T, stamped in Aggregator
	///   * the chart x-axis, the DPS denominator, the export `duration`
	///   * the [TIME] / finalize log lines
	/// so the numbers on the main page and in the detail view cannot drift apart. Time is only ever
	/// added through <see cref="Advance"/>, which the single per-frame driver (Aggregator.Tick) calls.
	///
	/// The clock is REAL time measured by Aggregator's stopwatch, not an engine delta: in this game
	/// Time.unscaledDeltaTime is the fixed logic step (1/45 s) while a rendered frame is 1/30 s, so
	/// summing it per frame ran the clock at 0.67x and a 17.9 s battle reported 10.0 s.
	/// </summary>
	public double ActiveSeconds;

	/// <summary>Same clock with paused time removed (real seconds in which the game was not paused).
	/// Only the idle rule uses it, so a pause can never look like silence.</summary>
	public double CombatSeconds;

	/// <summary>CombatSeconds at the last damage/heal event (see <see cref="IdleCombatSeconds"/>).</summary>
	public double LastEventCombat;

	/// <summary>True once the first real damage/heal event arrives; the battle clock starts there.</summary>
	public bool TimingStarted;

	/// <summary>Advance the clock by one frame's REAL seconds (already stall-clamped by the caller).
	/// The only place battle time is accumulated.</summary>
	public void Advance(double dt, bool paused)
	{
		if (dt <= 0.0) return;
		ActiveSeconds += dt;
		if (!paused) CombatSeconds += dt;
		if (ActiveSeconds > 0.0) TimingStarted = true;
	}

	/// <summary>Stamp "an event just happened". The only place the event timestamps are written, so
	/// the idle rule can never be fed by a different notion of "now" than the events use.</summary>
	public void NoteEvent()
	{
		LastEventWall = DateTime.Now;
		LastEventCombat = CombatSeconds;
	}

	/// <summary>Real seconds of silence, with paused time excluded. The idle rule (Aggregator.Tick)
	/// and every "idle=" diagnostic read this one property, so they always agree.</summary>
	public double IdleCombatSeconds
	{
		get { return CombatSeconds - LastEventCombat; }
	}

	/// <summary>
	/// Build the view-only session shown for a FINISHED battle (overlay roster summary, F6 detail,
	/// chart). One factory for all of them: previously each view assembled its own session, which made
	/// it possible for the main page and the detail view to read different data for the same battle.
	/// </summary>
	public static BattleSession FromSummary(BattleSummary b)
	{
		if (b == null) return null;
		int questId;
		int.TryParse(b.QuestId, out questId);
		var v = new BattleSession
		{
			InBattle = false,
			QuestId = questId,
			ActiveSeconds = b.DurationSeconds,   // the same clock the header showed while it was live
			Result = (GameResult)0
		};
		v.OrderedActors.AddRange(b.Actors);
		try { v.Events.AddRange(b.Events); } catch { }
		// 1.7.7 (P2-A #6): a finished-battle view must carry the unattributed pool.
		//
		// Without this every consumer of the previous battle printed "未归属 0" while the export of
		// that very battle wrote a real number (newest 1.7.6 export battle_411001_20261004_115417.json:
		// totals.unattributedDamage=273702, totals.unattributedHits=71). A measured 0 and "nothing to
		// attribute" are different statements, so this project must not default the field to 0.
		//
		// The source is BattleSummary.Session: the exact BattleSession object ExportService read when it
		// wrote totals.unattributedDamage, so the summary view and the file cannot disagree. Aggregator
		// keeps that session alive for every history entry and withdraws the entry itself when the
		// session is resumed (Aggregator.TryResumeClosedSession -> History.RemoveAll), and ResetActors
		// only ever runs on the LIVE session, so a summary that reaches this view always has the value.
		if (b.Session != null)
		{
			v.UnattributedDamage = b.Session.UnattributedDamage;
			v.UnattributedHits = b.Session.UnattributedHits;
		}
		return v;
	}

	/// <summary>Damage that reached a victim but had no usable attacker/owner BattleObject.</summary>
	public long UnattributedDamage;

	public int UnattributedHits;

	/// <summary>victim display name -> unattributed damage received (probe diagnostics).</summary>
	public readonly Dictionary<string, long> UnattributedByVictim = new Dictionary<string, long>();

	public readonly Dictionary<BattleObject, ActorStats> Actors = new Dictionary<BattleObject, ActorStats>();

	public readonly List<ActorStats> OrderedActors = new List<ActorStats>();

	/// <summary>Party-side per-second damage taken (index = battle second). For the taken-heal chart.</summary>
	private readonly List<long> _teamTakenSec = new List<long>();

	/// <summary>Party-side per-second healing received (index = battle second).</summary>
	private readonly List<long> _teamHealSec = new List<long>();

	public void AddTeamTaken(int sec, long amount)
	{
		if (sec < 0) return;
		while (_teamTakenSec.Count <= sec) _teamTakenSec.Add(0L);
		_teamTakenSec[sec] += amount;
	}

	public void AddTeamHeal(int sec, long amount)
	{
		if (sec < 0) return;
		while (_teamHealSec.Count <= sec) _teamHealSec.Add(0L);
		_teamHealSec[sec] += amount;
	}

	public long GetTeamTaken(int sec)
	{
		return (sec >= 0 && sec < _teamTakenSec.Count) ? _teamTakenSec[sec] : 0L;
	}

	public long GetTeamHeal(int sec)
	{
		return (sec >= 0 && sec < _teamHealSec.Count) ? _teamHealSec[sec] : 0L;
	}

	public int TeamMaxSecond()
	{
		int m = _teamTakenSec.Count;
		if (_teamHealSec.Count > m) m = _teamHealSec.Count;
		return m;
	}

	private void ClearTeamSeries()
	{
		_teamTakenSec.Clear();
		_teamHealSec.Clear();
	}

	public readonly List<HitRecord> PendingHits = new List<HitRecord>();

	public const int MaxPending = 2048;

	/// <summary>Full battle event log for offline analysis (exported to JSON on battle end).</summary>
	public readonly List<BattleEvent> Events = new List<BattleEvent>();

	public const int MaxEvents = 100000;

	/// <summary>
	/// Status-source audit (filled by StatusDeltaProbe.Audit just before export): how many times a status
	/// appeared on a victim, how many of those have an infliction record attached to a damage record, and
	/// the ones that do not (i.e. the observation could not see where the status came from).
	/// </summary>
	public int StatusTransitions;
	public int StatusExplained;
	public readonly System.Collections.Generic.List<string> StatusUnexplained = new System.Collections.Generic.List<string>();

	/// <summary>Cap on <see cref="StatusUnexplained"/>. EXPORTED (1.3.7) because without it the numbers
	/// do not reconcile: a battle with 22 appearances and 0 explained showed only 20 entries, and a reader
	/// could not tell "2 were dropped by a display cap" from "the export is broken".</summary>
	public int StatusUnexplainedCap = 20;

	/// <summary>Transitions that were unexplained but did not fit the cap. Reported, never silent.</summary>
	public int StatusUnexplainedOmitted;

	/// <summary>Infliction records where the game named the applier (BuffBase.OwnerIdentifier).</summary>
	public int StatusApplierKnown;

	/// <summary>Those where the named applier differs from the record's attacker -- i.e. the record's
	/// attacker is only the nearest damage record, NOT the source.</summary>
	public readonly System.Collections.Generic.List<string> StatusMismatch = new System.Collections.Generic.List<string>();

	public void AddEvent(BattleEvent ev)
	{
		if (Events.Count < MaxEvents) Events.Add(ev);
	}

	/// <summary>1.5.0 (A4): next stable per-actor key. Reset with the session; never reused within one, so a
	/// key uniquely identifies one actor row of one export.</summary>
	private int _nextActorKey = 1;

	public ActorStats GetActor(BattleObject bo, bool create)
	{
		if (GameRef.IsNull(bo)) return null;
		if (Actors.TryGetValue(bo, out var value)) return value;
		if (!create) return null;

		bool isToken = false;
		try { isToken = !GameRef.IsNull(bo.TokenOwner); }
		catch { }

		if (isToken)
		{
			string text = CharacterInfo.DisplayName(bo);
			foreach (ActorStats orderedActor in OrderedActors)
			{
				if (orderedActor.IsSummonMerge && orderedActor.Team == bo.TeamType && orderedActor.Name == text)
				{
					Actors[bo] = orderedActor;
					return orderedActor;
				}
			}
			value = new ActorStats
			{
				Key = _nextActorKey++,
				Source = bo,
				Name = text,
				Team = bo.TeamType,
				Kind = CharacterInfo.KindLabel(bo),
				IsSummonMerge = true,
				FirstHitTime = ActiveSeconds,
				LastHitTime = ActiveSeconds
			};
			Actors[bo] = value;
			OrderedActors.Add(value);
			return value;
		}

		value = new ActorStats
		{
			Key = _nextActorKey++,
			Source = bo,
			Name = CharacterInfo.DisplayName(bo),
			Team = bo.TeamType,
			Kind = CharacterInfo.KindLabel(bo),
			FirstHitTime = ActiveSeconds,
			LastHitTime = ActiveSeconds
		};
		Actors[bo] = value;
		OrderedActors.Add(value);
		return value;
	}

	/// <summary>Max age of a pending damage figure before it may no longer label a hit. 0.35 s is the
	/// same order as the composition's own pairing windows (0.20 s live, 0.45 s relaxed). Without a bound
	/// a stale record from an AoE cast could be consumed by an unrelated later hit and silently mislabel
	/// its damage source and its crit flag.</summary>
	public const double HitMatchSeconds = 0.35;

	/// <summary>
	/// 1.5.0 (A2): match a produced damage figure to the damage record it became.
	///
	/// `how` reports WHICH match was used, because the two are not equally trustworthy and the export has
	/// to be able to tell them apart:
	///   0 = no record (this hook never saw the calculation, or the window expired)
	///   1 = exact (same attacker, same target, and the produced figure equals the damage OR the nominal)
	///   2 = pair  (same attacker and target, neither value matches) -- best effort
	///
	/// 1.5.1: the produced figure is the number the CALC computed, while `RecordDamage` sees the damage
	/// that reached 耐久 (`damage`) and the game's own pre-absorption accounting (`nominal`). MEASURED
	/// 2026-10-03 (battle_...173710): comparing only against `damage` matched 115 of 5369 records exactly
	/// and 5156 by pair, i.e. 96% of the labels were best-effort for a reason that is an accounting
	/// difference, not a pairing failure. Both are accepted now; a hit where they differ still matches.
	/// </summary>
	public HitRecord ConsumePending(BattleObject attacker, BattleObject blocker, int damage, int nominal, double now, out int how)
	{
		how = 0;
		int pairIdx = -1;
		for (int i = 0; i < PendingHits.Count; i++)
		{
			HitRecord hitRecord = PendingHits[i];
			if (!GameRef.Same(hitRecord.Attacker, attacker) || !GameRef.Same(hitRecord.Blocker, blocker)) continue;
			if (now - hitRecord.T > HitMatchSeconds) continue;
			if (hitRecord.Damage == damage || hitRecord.Damage == nominal)
			{
				PendingHits.RemoveAt(i);
				how = 1;
				return hitRecord;
			}
			if (pairIdx < 0) pairIdx = i;
		}
		if (pairIdx >= 0)
		{
			HitRecord hitRecord = PendingHits[pairIdx];
			PendingHits.RemoveAt(pairIdx);
			how = 2;
			return hitRecord;
		}
		return null;
	}

	public void ResetActors()
	{
		Actors.Clear();
		OrderedActors.Clear();
		// 1.5.0 (A4): keys restart with the actor table. Safe because ResetActors also clears Events, so
		// no surviving event can refer to a superseded key.
		_nextActorKey = 1;
		PendingHits.Clear();
		UnattributedDamage = 0;
		UnattributedHits = 0;
		UnattributedByVictim.Clear();
		ClearTeamSeries();
		TimingStarted = false;
		Events.Clear();
	}
}
