using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace DpsMeter;

public static partial class Aggregator
{

	/// <summary>
	/// How many game units make one game second: the single definition used by the "game" clock source
	/// and reported by the [CLOCK] / [CLOCKP] lines.
	/// Priority: Plugin.CfgGameUnitsPerSecond -> the value measured from the loaded skill data
	/// (Skill.CoolTimeFrame / Skill.CoolTime) -> 30 (measured 2026-09-27: 750/25, 1500/50, 1050/35).
	/// </summary>
	internal static double GameUnitsPerSecond()
	{
		// RF3: the fallback chain (config -> measured skill data -> 30.0) is a policy decision.
		double configured = (Plugin.CfgGameUnitsPerSecond != null) ? Plugin.CfgGameUnitsPerSecond.Value : 0.0;
		return BattleClockPolicy.ResolveUnitsPerGameSecond(configured, TimeProbe.UnitsPerGameSecond,
		                                                   BattleClockPolicy.DefaultUnitsPerGameSecond);
	}

	/// <summary>
	/// Seconds to add to the battle clock for this frame, in the unit selected by
	/// Plugin.CfgClockSource (see <see cref="Tick"/> for the semantics).
	///
	/// MUST only be called from the Tick invocation that will actually apply the result (i.e. after the
	/// once-per-frame guard), because the "game" and "real" branches advance their last-seen state here.
	/// That ordering is not cosmetic: the game increments GameTime inside its own update, so the driver
	/// that runs FIRST in a frame reads the counter before the increment and the driver that carries the
	/// increment is the SECOND one. When this was computed before the guard, the applying call always saw
	/// dSteps = 0 while the carrying call was dropped by the guard, and the clock froze at 0.0 s for a
	/// whole battle (log 2026-09-27: source=game units=30.0 active=0.0s wall=20.6s, while [TIME] showed
	/// gameTime advancing 166 -> 710). Keeping the state in the applying call makes the total correct no
	/// matter which driver runs first.
	/// </summary>
	private static double FrameDelta(GameSystem val)
	{
		// RF3: "which source" is a policy decision and was implemented TWICE (here and ClockSourceName).
		// NOTE for the next default change: BepInEx keeps the value already present in
		// BepInEx\config\dev.dpsmeter.cfg, so changing the default in code does NOT migrate an installed
		// config -- the [CLOCK]/[TIME] lines print `source=` so the effective value is always visible.
		string source = BattleClockPolicy.ResolveSource(
			(Plugin.CfgClockSource != null) ? Plugin.CfgClockSource.Value : null,
			Plugin.CfgTimerUsesGameTime != null && Plugin.CfgTimerUsesGameTime.Value);
		if (source == "engine")
		{
			// The engine's scaled delta: 0 while the game is paused.
			return Time.deltaTime;
		}
		if (source == "game")
		{
			// The game's own clock: update steps / units-per-second (measured 30.0 from the skill data).
			int steps = 0;
			try { steps = val.GameTime; } catch { }
			return BattleClockPolicy.GameDelta(steps, ref _lastGameSteps, ref _hasLastSteps, GameUnitsPerSecond());
		}
		// Real seconds from the monotonic stopwatch (see the Clock field).
		return BattleClockPolicy.RealDelta(Clock.Elapsed.TotalSeconds, ref _lastTickClock);
	}

	/// <summary>Effective clock source name, for the diagnostics.</summary>
	internal static string ClockSourceName()
	{
		// RF3: the same policy the applied delta uses, so the reported source cannot drift from it.
		return BattleClockPolicy.ResolveSource(
			(Plugin.CfgClockSource != null) ? Plugin.CfgClockSource.Value : null,
			Plugin.CfgTimerUsesGameTime != null && Plugin.CfgTimerUsesGameTime.Value);
	}

	/// <summary>
	/// Per-frame driver (see Hooks/): advances the battle clock, samples 耐久, detects the end of a
	/// battle and emits the periodic [TIME]/[CLOCKP] diagnostics.
	///
	/// Clock source (Plugin.CfgClockSource), because the game's own clocks are unit based:
	///   game   (default) -- THE GAME'S OWN CLOCK: update steps (GameSystem.GameTime deltas) divided by
	///                       the game's units-per-second (30.0, read from the loaded skill data:
	///                       Skill.CoolTimeFrame / Skill.CoolTime), so the meter ticks exactly like
	///                       skill cooldowns do. Measured: GameTime, GameTimeLimitCounter.NowTime and a
	///                       skill's wait counter all advance 45.0 units per real second at timeScale
	///                       1.5, and 30 units = 1 game second -> the game clock runs 1.5x real time;
	///   real             -- real seconds from the Clock stopwatch below;
	///   engine           -- `Time.deltaTime`, i.e. the engine's scaled delta (0 while paused).
	/// See Diagnostics/TimeProbe.cs for the probe that produced those numbers.
	/// </summary>
	public static void Tick()
	{
		// Idempotent within one frame: Tick has two drivers (GameSystem.EarlyUpdateMain and
		// InputManager.Update, see Hooks/) so that either one alone keeps the meter alive, and both fire
		// in the same frame. This guard MUST stay first, and the clock delta MUST be computed after it --
		// see FrameDelta().
		int frame = Time.frameCount;
		if (frame == _lastTickFrame) return;
		_lastTickFrame = frame;

		GameSystem val = GameSystemAccess.TryGet();
		if (val == null)
		{
			if (Session != null && Session.InBattle) { FinalizeLocked(Session, Session.Result, "teardown"); Session = null; }
			_lastGsPointer = 0L;
			return;
		}

		// How much did the clock advance for this frame. Computed only in the call that actually applies
		// it (i.e. after the guard), because only this call may advance the "last seen" state.
		double dt = FrameDelta(val);
		// A stalled frame (blocked main thread, OS suspend, blocked scene load) delivers the entire stall
		// as ONE delta -- measured 2.9 s in a single frame. Clamp it: the battle clock must not jump, and
		// an unclamped stall is by itself enough to trip the idle timeout in the very same step (that is
		// how one battle got split in two: logged dur 9.3 s -> 12.2 s with no event in between).
		// RF3: the negative guard and the clamp are the policy's decision, at the policy's bound.
		dt = BattleClockPolicy.ClampFrameDelta(dt, BattleClockPolicy.MaxFrameDelta);

		long num;
		try { num = (long)((Il2CppObjectBase)val).Pointer; }
		catch { num = 0L; }

		if (Session == null || !Session.InBattle)
		{
			if (num != 0L && num != _lastGsPointer)
			{
				_lastGsPointer = num;
				StartSession();
			}
			return;
		}

		GameResult gameResult = val.GameResult;
		if ((int)gameResult != 0 && (int)Session.Result == 0) { EndSession(gameResult); return; }

		if (_eventCount > 0)
		{
			try
			{
				if (val.IsForceBattleEnd || val.IsTeamDestroyed((TeamType)1) || val.IsTeamDestroyed((TeamType)2))
				{
					EndSession(val.GameResult);
					return;
				}
			}
			catch { }
		}

		// Battle clock. ActiveSeconds is REAL elapsed seconds since this session's first tick, so it
		// matches the length of the battle as the player experienced it (the end-of-battle sequence
		// pause included: a battle that took 17.9 s of real time reports 17.9 s). CombatSeconds is the
		// same clock with paused time removed; it exists for the idle rule and the diagnostics, because
		// "no event for N seconds" must not be satisfied by a pause.
		//
		// GameSystem.GameTime must NOT be used as a clock: it keeps counting across battles and its rate
		// follows the game's step rate (45/s at timeScale 1.5), not seconds. It is kept for [TIME] only.
		try
		{
			bool paused = false;
			try { paused = val.IsPaused || val.IsTimePaused; } catch { }
			// The clock lives in BattleSession: one Advance() for the whole plugin (see its docs).
			Session.Advance(dt, paused);
			// Settle any per-hit "did this record inflict an ailment?" re-check whose window has passed.
			// Driven by the battle clock (not the wall clock) so a pause defers it exactly like everything
			// else, and placed AFTER Advance so the deadline comparison sees the current time.
			StatusDeltaProbe.Tick(Session.ActiveSeconds);
			// diagnostic: real clock vs game counter vs engine deltas. One run is enough to check that
			// active tracks wall 1:1 and to spot the engine's fixed-step behaviour (see the Clock field).
			try
			{
				if (Plugin.CfgAbilityDump != null && Plugin.CfgAbilityDump.Value)
				{
					double wall = (DateTime.Now - Session.StartWallClock).TotalSeconds;
					if (wall - _lastTimeLog >= 2.0)
					{
						_lastTimeLog = wall;
						int g = 0;
						try { g = val.GameTime; } catch { }
						RuntimeLog.Write("[TIME] wall=" + wall.ToString("F1") + "s gameTime=" + g
							+ " dGameTime=" + (g - _gameTimeAtStart)
							+ " frame=" + Time.frameCount
							+ " scale=" + Time.timeScale.ToString("F2")
							+ " paused=" + (paused ? 1 : 0)
							+ " active=" + BattleTime.Log(Session.ActiveSeconds)
							+ " combat=" + BattleTime.Log(Session.CombatSeconds)
							+ " idle=" + BattleTime.Log(Session.IdleCombatSeconds)
							// active/wall: ~1.00 for ClockSource=real, ~stepRate/units (1.5 at timeScale
							// 1.5) for ClockSource=game. It read 0.67 while the clock was fed
							// Time.unscaledDeltaTime, which is the game's 1/45 s logic step.
							+ " rate=" + ((wall > 1.0) ? (Session.ActiveSeconds / wall).ToString("F2") : "-")
							// canary: with damage events on the board the clock CANNOT still be at zero.
							// This fires if a future change makes the applied delta disappear (the bug
							// fixed in 1.0.36: the delta was computed before the once-per-frame guard, so
							// the call carrying the game's increment was the one dropped by the guard).
							+ ((_eventCount > 0 && Session.ActiveSeconds < 0.05) ? "  !!! 时钟未推进" : "")
							+ " hits=" + _eventCount);
						// Which clock is the game's own? Prints every candidate raw value + its rate.
						string probe = TimeProbe.Line(val, wall, Session);
						if (!string.IsNullOrEmpty(probe)) RuntimeLog.Write(probe);
						// heartbeat of the damage-calc constructor probe (is it running, what does it see?)
						string hb = PowerProbe.Heartbeat();
						if (!string.IsNullOrEmpty(hb)) RuntimeLog.Write(hb);
					}
				}
			}
			catch { }
		}
		catch { }
		SampleHp();

		// Idle close, measured on CombatSeconds (real seconds with paused time removed) so that neither a
		// pause nor a stall can be mistaken for silence. The wall-clock version of this test closed a
		// session on the first frame after a pause -- 0.02 s before that same battle's remaining hits
		// arrived, which then opened a fragment session starting at t=0 (what F6 used to show).
		if (!val.IsPaused && _eventCount > 0 && Session.IdleCombatSeconds > IdleSeconds)
		{
			FinalizeLocked(Session, ((int)val.GameResult != 0) ? val.GameResult : (GameResult)0, "idle");
			Session = null;
		}
		else if (Session.ActiveSeconds - _lastSummaryLog >= 5.0)
		{
			_lastSummaryLog = Session.ActiveSeconds;
			StringBuilder sb = new StringBuilder($"[DpsMeter] t={BattleTime.Seconds(Session.ActiveSeconds)} hits={_eventCount}");
			foreach (ActorStats orderedActor in Session.OrderedActors)
			{
				if ((int)orderedActor.Team == 1 && orderedActor.DamageDealt > 0L)
					sb.Append($" | {orderedActor.Name}:{orderedActor.DamageDealt}");
			}
			if (Session.UnattributedDamage > 0L)
				sb.Append($" | [未归属敌方伤害]:{Session.UnattributedDamage}");
			string text = sb.ToString();
			Plugin.LogSource.LogInfo(text);
			RuntimeLog.Write(text);
		}
	}

	private static void SampleHp()
	{
		try
		{
			if (Session == null || !Session.InBattle) return;
			int sec = (int)Session.ActiveSeconds;
			if (sec < 0) return;
			foreach (var a in Session.OrderedActors)
			{
				if (!CharacterInfo.IsAllyTeam(a.Team)) continue;
				BattleObject src = a.Source;
				if (GameRef.IsNull(src)) continue;
				try
				{
					int life = src.Life;
					int max = src.MaxLife;
					float pct = max > 0 ? Mathf.Clamp01((float)life / max) * 100f : 100f;
					a.AddHpPct(sec, pct);
				}
				catch { }
			}
		}
		catch { }
	}

	private static void BeginTimingIfNeeded()
	{
		if (Session == null || Session.TimingStarted) return;
		Session.TimingStarted = true;
		Session.NoteEvent();
	}
}
