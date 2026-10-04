using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace DpsMeter;

public static partial class Aggregator
{

	public static bool InBattle
	{
		get
		{
			if (Session != null) return Session.InBattle;
			return false;
		}
	}

	public static int PendingCount
	{
		get
		{
			if (Session == null) return 0;
			return Session.PendingHits.Count;
		}
	}

	public static void StartSession()
	{
		BattleSession session = Session;
		if (session != null && session.InBattle) FinalizeLocked(session, (GameResult)0, "restart");
		// Per-battle counters (1.3.5). `BlockReads`/`BlockErrors` are deliberately left cumulative for
		// continuity with 1.3.4, but the granted-talent counters have to describe ONE battle: the whole
		// point of `giveHits` is to compare it against that battle's ×1.21 window.
		CompositionProbe.ResetGivenCounters();
		// 1.4.0: the per-hit forensics specimen set has the same per-battle lifetime.
		Forensics.Reset();
		GameSystem val = GameSystemAccess.TryGet();
		int questId = (val != null ? val.QuestID : -1);
		// ---- grouping marker (1.3.3): does this session continue the previous stretch of play? ----
		// Not a merge: the numbers stay exactly as they were. In wave/arena content the game fires its
		// battle-end signal once per wave with no result, which cut a measured 45 s stage into 13 exports;
		// marking them lets offline analysis add the fragments up itself, without this code ever risking
		// the silent merge that a relaxed resume rule would cause.
		// RF3 + RF4: the grouping RULE is the policy, the marker STATE is the container; the wall-clock read
		// stays here (every clock read belongs to the facade).
		double runGap = Continuity.HasEnded ? (DateTime.Now - Continuity.LastEndWall).TotalSeconds : -1.0;
		RunMarker marker = Continuity.BeginSession(runGap, questId, SessionTransitionPolicy.RunJoinSeconds);
		int runSeq = marker.RunSeq;
		long runIdNow = marker.RunId;
		BattleSession battleSession = (Session = new BattleSession
		{
			InBattle = true,
			Result = (GameResult)0,
			QuestId = questId,
			StartWallClock = DateTime.Now,
			LastEventWall = DateTime.Now,
			RunId = runIdNow,
			RunSeq = runSeq,
			RunGap = marker.RunGap,
			RunPrevWhy = marker.PrevWhy,
			RunPrevResult = marker.PrevResult
		});
		// RF4 third family: one call replaces the twelve inline resets (their rules are the container's).
		Rt.OnSessionStart();
		_calcEvents.Clear();
		// 1.5.0 (A2): the active calc and the pending damage figures are PER BATTLE. `_activeCalc` was
		// only cleared when a session was resumed, never when one started, so a calc from the previous
		// battle could label this battle's opening hits -- and the pending-hit channel would have carried
		// stale figures across the boundary too.
		_activeCalc = null;
		_activeCalcT = -1.0;
		// diagnostic only: GameTime keeps counting across battles, so remember where this one started
		// (the battle clock itself is accumulated from dt in Tick -- GameTime is a frame counter).
		// Native read -> it stays here and is written INTO the container.
		try { Rt.GameTimeAtStart = (val != null) ? val.GameTime : 0; } catch { Rt.GameTimeAtStart = 0; }
		Probe.Reset();
		CompositionProbe.Reset();
		StatusDeltaProbe.Reset();
		StateTimeline.Reset();
		StatusApplierProbe.Reset();
		GiveApplierProbe.Reset();
		ParamOwnerProbe.Reset();
		AtkAddFold.Reset();
		FactStore.Reset();
		// Ability rosters and talent activation counters are per-battle state cached on ActorStats.
		TalentRuntime.ResetSession();
		OverlayUGUI.LogSessionStart(battleSession.QuestId);
		string text = $"[DpsMeter] Battle session started (quest={battleSession.QuestId})"
			+ $" gameTimeAtStart={Rt.GameTimeAtStart} run=#{runIdNow}.{runSeq}"
			+ (runSeq > 0
				? $" gap={marker.RunGap:F2}s prev={marker.PrevWhy}/{marker.PrevResult}"
				: " (run start)");
		Plugin.LogSource.LogInfo(text);
		RuntimeLog.Write(text);
	}

	public static void EnsureSessionStarted()
	{
		if (Session == null || !Session.InBattle) StartSession();
	}

	/// <summary>
	/// Session for an incoming damage/heal event.
	///
	/// A battle used to be split into TWO sessions whenever the meter closed a session while the game
	/// was still applying damage (idle timeout, or a battle-end signal arriving while hits were still
	/// resolving): the remaining hits then started a fresh session whose clock begins at 0, and that
	/// fragment is what the F6 detail view and the export showed -- a "battle" whose every event sits at
	/// t=0.0s (seen in battle_9999_20260927_015822.json: 52 events, dur 0.46 s, all t &lt;= 0.46).
	///
	/// So: if the previous session was closed by the IDLE timeout moments ago and this event's unit is
	/// already a known actor of it, the hit belongs to that battle -- resume it and let the clock and the
	/// totals continue. Otherwise start a new session exactly as before.
	/// </summary>
	private static void EnsureSessionStartedFor(BattleObject a, BattleObject b)
	{
		if (Session != null && Session.InBattle) return;
		if (TryResumeClosedSession(a, b)) return;
		StartSession();
	}

	private static bool TryResumeClosedSession(BattleObject a, BattleObject b)
	{
		try
		{
			BattleSession s = Continuity.LastClosed;
			double gap = (s != null) ? (DateTime.Now - Continuity.LastClosedWall).TotalSeconds : -1.0;
			// RF3 + RF4: the verdict is the policy's, the state (and its one side effect: only an expired
			// window forgets the session) is the container's.
			ResumeGate gate = Continuity.Gate(s != null, gap, SessionTransitionPolicy.ResumeWindowSeconds);
			if (gate != ResumeGate.Eligible) return false;
			// The native actor check stays in the facade and runs only after the gates (it reads objects).
			bool known = (!GameRef.IsNull(a) && s.Actors.ContainsKey(a))
				|| (!GameRef.IsNull(b) && s.Actors.ContainsKey(b));
			if (!known) return false;                        // unknown units -> this is the next battle

			Continuity.ForgetClosed();
			// The soft close pushed a summary into the history and wrote an export; both are replaced
			// when the resumed session is finalised for real. The export file name is derived from
			// StartWallClock, so it is overwritten instead of duplicated.
			History.RemoveAll(h => ReferenceEquals(h.Session, s));
			s.InBattle = true;
			s.NoteEvent();
			// FinalizeLocked drops the BattleObject references to release the native objects; restore
			// them so 耐久 sampling and the [CROSS] check keep working for the resumed tail.
			foreach (var kv in s.Actors)
			{
				ActorStats st = kv.Value;
				if (!st.IsSummonMerge && GameRef.IsNull(st.Source)) st.Source = kv.Key;
			}
			Session = s;
			_activeCalc = null;
			_activeCalcT = -1.0;
			_calcEvents.Clear();
			string text = $"[DpsMeter] Battle session resumed: {gap:F2}s after an idle close, clock continues at {BattleTime.Log(s.ActiveSeconds)} ({s.OrderedActors.Count} actors)";
			Plugin.LogSource.LogInfo(text);
			RuntimeLog.Write(text);
			return true;
		}
		catch { return false; }
	}

	public static void EndSession(GameResult result)
	{
		if (Session != null && Session.InBattle)
		{
			Session.Result = result;
			FinalizeLocked(Session, result, "end");
			Session = null;
		}
	}

	public static void ResetCurrent()
	{
		if (Session != null && Session.InBattle)
		{
			Session.ResetActors();
			Rt.OnManualReset();
			string text = "[DpsMeter] Manual reset";
			Plugin.LogSource.LogInfo(text);
			RuntimeLog.Write(text);
		}
	}
}
