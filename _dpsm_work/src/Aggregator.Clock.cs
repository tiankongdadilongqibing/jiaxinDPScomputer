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

		if (Rt.EventCount > 0)
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
			// R71: BEFORE the first advance of the battle, move the clock's ORIGIN from "the plugin saw the
			// battle" to "the game started it" (measured ~0.9 s late; see BattleClockCalibrationPolicy).
			// It has to happen here -- one axis per battle -- and it is one-shot.
			TryAlignClockOrigin(val, Session);
			// The clock lives in BattleSession: one Advance() for the whole plugin (see its docs).
			Session.Advance(dt, paused);
			// Settle any per-hit "did this record inflict an ailment?" re-check whose window has passed.
			// Driven by the battle clock (not the wall clock) so a pause defers it exactly like everything
			// else, and placed AFTER Advance so the deadline comparison sees the current time.
			StatusDeltaProbe.Tick(Session.ActiveSeconds);
			// R64: the auto skill's charge counter, read from the live `Skill` side. Throttled to one scan
			// per 0.5 s and one printed line per 2 s per slot INSIDE the probe, so this is cheap enough to
			// sit on the frame path; it also stamps the activation instant when the command postfix is
			// absent. See Diagnostics/AutoSkillProbe.cs.
			AutoSkillProbe.Observe(val, Session);
			// diagnostic: real clock vs game counter vs engine deltas. One run is enough to check that
			// active tracks wall 1:1 and to spot the engine's fixed-step behaviour (see the Clock field).
			try
			{
				if (Plugin.CfgAbilityDump != null && Plugin.CfgAbilityDump.Value)
				{
					double wall = (DateTime.Now - Session.StartWallClock).TotalSeconds;
					if (wall - Rt.LastTimeLog >= 2.0)
					{
						Rt.LastTimeLog = wall;
						int g = 0;
						try { g = val.GameTime; } catch { }
						RuntimeLog.Write("[TIME] wall=" + wall.ToString("F1") + "s gameTime=" + g
							+ " dGameTime=" + (g - Rt.GameTimeAtStart)
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
							+ ((Rt.EventCount > 0 && Session.ActiveSeconds < 0.05) ? "  !!! 时钟未推进" : "")
							+ " hits=" + Rt.EventCount);
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
		// RF3b: the idle rule is a policy decision (paused frames can never close a session, and the
		// silence is measured on the no-pause clock).
		if (SessionTransitionPolicy.ShouldCloseIdle(val.IsPaused, Rt.EventCount, Session.IdleCombatSeconds,
		                                            SessionTransitionPolicy.IdleSeconds))
		{
			FinalizeLocked(Session, ((int)val.GameResult != 0) ? val.GameResult : (GameResult)0, "idle");
			Session = null;
		}
		else if (Session.ActiveSeconds - Rt.LastSummaryLog >= 5.0)
		{
			Rt.LastSummaryLog = Session.ActiveSeconds;
			StringBuilder sb = new StringBuilder($"[DpsMeter] t={BattleTime.Seconds(Session.ActiveSeconds)} hits={Rt.EventCount}");
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

	/// <summary>
	/// R71/R72: one-shot decision of the battle clock's ORIGIN.
	///
	/// R72 changed WHEN it is attempted, not what it computes. R71 attempted it from the session's SECOND
	/// frame on and refused whenever `Rt.EventCount != 0` -- and the game stamps its opening damage in the
	/// very frame the session is created, so the shift was refused in both of the user's battles
	/// (`[CLOCK] origin=none reason=events hits=5` 0.121 s after one session started, `hits=1` 0.029 s after
	/// the other) although the auto-skill counters at that moment all said 0.90-0.93 s. The cure is twofold:
	///   * the decision is attempted when the session is CREATED (<see cref="StartSession"/>), i.e. before
	///     anything can be stamped -- the common case now needs no correction at all;
	///   * anything that still arrives before the evidence is HELD and replayed
	///     (<see cref="ClockOriginHoldPolicy"/>), so the guard below cannot be reached with a published
	///     event of this battle in the way.
	///
	/// Called once per frame from <see cref="Tick"/> and once from the session-creation path; it decides
	/// exactly once per battle and reports which of the states it entered (applied / off / window / hold /
	/// events / range), because "no shift" and "refused" must not look alike.
	/// </summary>
	private static void TryAlignClockOrigin(GameSystem val, BattleSession s)
	{
		if (s == null || s.ClockOriginDecided) return;
		try
		{
			if (Plugin.CfgClockAlign == null || !Plugin.CfgClockAlign.Value)
			{
				DecideClockOrigin(s, "off", 0.0, 0,
					"General/ClockAlignToBattleStart=false; times start when the plugin saw the battle");
				return;
			}
			double lag;
			int samples;
			bool measured = AutoSkillProbe.TryMeasureClockLag(val, GameUnitsPerSecond(), s.ActiveSeconds,
				out lag, out samples);
			if (samples > 0) s.ClockOriginSamples = samples;

			if (measured && BattleClockCalibrationPolicy.ShouldRebase(lag, s.ActiveSeconds, Rt.EventCount,
				Rt.OriginHoldOverflowed))
			{
				DecideClockOrigin(s, "applied", lag, samples,
					"this battle's times now start at the GAME's battle start");
				return;
			}
			// Not yet decidable, or refused for a reason that will not change: give up at the window's edge,
			// where the calibrating slots are no longer guaranteed to be on their first charge.
			if (s.ActiveSeconds > BattleClockCalibrationPolicy.WindowSeconds)
			{
				DecideClockOrigin(s, "window", 0.0, samples,
					"active is past the calibration window; times start when the plugin saw the battle");
				return;
			}
			if (Rt.OriginHoldOverflowed)
			{
				// Events already exist on the old axis (the hold cap was hit): shifting now would put one
				// battle on two axes, which is worse than a late origin.
				DecideClockOrigin(s, "hold", 0.0, samples,
					"more than " + ClockOriginHoldPolicy.MaxHeld + " events arrived before the origin was decided; times start when the plugin saw the battle");
				return;
			}
			if (Rt.EventCount > 0)
			{
				DecideClockOrigin(s, "events", 0.0, samples,
					"an event was already stamped; one battle, one origin");
				return;
			}
			if (!measured && samples >= BattleClockCalibrationPolicy.MinSamples)
			{
				// Samples exist but the combined value was refused (out of bounds): that is not a condition
				// that improves by waiting, and a wrong shift would corrupt every time in the file.
				DecideClockOrigin(s, "range", 0.0, samples,
					"combined lag outside [" + BattleClockCalibrationPolicy.MinLagSeconds.ToString("F2") + ", "
					+ BattleClockCalibrationPolicy.MaxLagSeconds.ToString("F1") + "]s; times unaffected");
				return;
			}
			// Fewer than two usable slots so far: keep trying until the window closes. The events that
			// arrive in the meantime are held, not published (HoldOriginEvent).
		}
		catch { }
	}

	/// <summary>
	/// R72: end the origin question for this battle -- apply the shift or refuse it -- and make everything
	/// that already exists consistent with the answer. ONE exit, so no branch can forget the held events:
	/// a refusal replays them with no shift, an application replays them on the corrected axis.
	/// </summary>
	private static void DecideClockOrigin(BattleSession s, string reason, double lag, int samples, string note)
	{
		if (s == null || s.ClockOriginDecided) return;
		bool applied = lag >= BattleClockCalibrationPolicy.MinLagSeconds;
		// FIRST, so the replayed events are aggregated instead of being held again.
		s.ClockOriginDecided = true;
		s.ClockOriginReason = reason;
		if (samples > 0) s.ClockOriginSamples = samples;
		if (applied)
		{
			s.ApplyClockOrigin(lag);
			ShiftStampsOutsideRecorder(lag);
		}
		int held = Rt.OriginHeld.Count;
		FlushOriginHeld(applied ? lag : 0.0);
		RuntimeLog.Write("[CLOCK] " + (applied ? "origin=+" + lag.ToString("F2") + "s" : "origin=none")
			+ " reason=" + reason
			+ " samples=" + s.ClockOriginSamples
			+ " active=" + BattleTime.Log(s.ActiveSeconds)
			+ " hits=" + Rt.EventCount
			+ " held=" + held
			+ " (" + note + ")");
	}

	/// <summary>
	/// R72: the stamps written BEFORE the decision by code paths the held recorder does not cover. Each of
	/// them stored the battle clock once, so adding the decided lag IS the correction; a path left out would
	/// be a silently mixed axis (the page's activation rows are the one the user reads).
	/// </summary>
	private static void ShiftStampsOutsideRecorder(double lag)
	{
		if (!(lag > 0.0)) return;
		// the 技能时间表 page: the activation stamps the user compares against 初动
		try { SkillTimelineProbe.ShiftActiveTimes(lag); } catch { }
		// the charge sampler's per-slot bookkeeping: its intervals are differences, but the "last seen at"
		// stamp is compared against the live clock
		try { AutoSkillProbe.ShiftBookkeeping(lag); } catch { }
		// the diagnostic exports' rows (status applier / talent giver / param owner) and the attack snapshot
		try { StatusApplierProbe.ShiftTimes(lag); } catch { }
		try { GiveApplierProbe.ShiftTimes(lag); } catch { }
		try { ParamOwnerProbe.ShiftTimes(lag); } catch { }
		try { _active.ShiftAt(lag); } catch { }
	}

	/// <summary>
	/// R72: hold one arriving damage/heal event while the origin is undecided, or report the cap.
	///
	/// Returns true when the event was held (and must NOT be aggregated now). The cap is a REFUSAL, not a
	/// silent fallback: once it is hit the shift cannot be applied any more without splitting the battle
	/// across two origins, so the flag it sets makes <see cref="TryAlignClockOrigin"/> refuse and the event
	/// is stamped on the old axis like everything before it.
	/// </summary>
	private static bool HoldOriginEvent(bool heal, BattleObject victim, BattleObject actor, BattleObject owner,
		int amount, int nominal)
	{
		try
		{
			BattleSession s = Session;
			if (s == null) return false;
			int held = Rt.OriginHeld.Count;
			if (ClockOriginHoldPolicy.ShouldHold(s.ClockOriginDecided, held))
			{
				Rt.OriginHeld.Add(new OriginHeldEvent
				{
					Heal = heal,
					Victim = victim,
					Actor = actor,
					Owner = owner,
					Amount = amount,
					Nominal = nominal,
					ArrivalSeconds = s.ActiveSeconds
				});
				return true;
			}
			if (ClockOriginHoldPolicy.Overflowed(s.ClockOriginDecided, held) && !Rt.OriginHoldOverflowed)
			{
				Rt.OriginHoldOverflowed = true;
				RuntimeLog.Write("[CLOCK] hold overflow held=" + held + " limit="
					+ ClockOriginHoldPolicy.MaxHeld
					+ " (the origin shift is REFUSED so this battle keeps ONE axis)");
			}
			return false;
		}
		catch { return false; }
	}

	/// <summary>
	/// R72: re-aggregate the held events, each at the instant it ARRIVED plus the decided lag (no shift on
	/// the refusal path). The recorder is entered with the battle clock set to that instant, so every value
	/// it derives -- `events[].t`, the actor's first/last hit, the per-second bucket, the damage curve, the
	/// pending-figure match, the reaction deadline -- is written once and already correct; nothing downstream
	/// has to know that the event was held.
	///
	/// Called from the decision, from a finalisation and from a manual reset, so a held event can never be
	/// dropped silently. The clock is restored afterwards, so the replay is invisible to the frame.
	/// </summary>
	internal static void FlushOriginHeld(double lag)
	{
		try
		{
			BattleSession s = Session;
			if (s == null) { Rt.OriginHeld.Clear(); return; }
			int n = Rt.OriginHeld.Count;
			if (n == 0) return;
			double savedActive = s.ActiveSeconds;
			double savedCombat = s.CombatSeconds;
			for (int i = 0; i < n; i++)
			{
				OriginHeldEvent e = Rt.OriginHeld[i];
				double at = ClockOriginHoldPolicy.ReplayActive(e.ArrivalSeconds, lag);
				s.ActiveSeconds = at;
				// The hold window is a fraction of a second and no pause is modelled inside it: the arrival
				// was, by construction, as unpaused as the decision that follows it.
				s.CombatSeconds = at;
				if (e.Heal) RecordHealNow(e.Victim, e.Actor, e.Amount, e.Nominal);
				else RecordDamageNow(e.Victim, e.Actor, e.Owner, e.Amount, e.Nominal);
			}
			Rt.OriginHeld.Clear();
			s.ActiveSeconds = savedActive;
			s.CombatSeconds = savedCombat;
			// The battle DID receive events during the hold, so the "silence" the idle rule measures starts
			// now: keeping the pre-replay stamp would let it close a session that just got its opening hits.
			s.LastEventCombat = savedCombat;
			Rt.OriginHeldReplayed += n;
			RuntimeLog.Write("[CLOCK] held=" + n + " replayed at +" + lag.ToString("F2")
				+ "s (event(s) arrived before the origin was decided; stamped at their arrival, not at the replay)");
		}
		catch { }
	}

	private static void SampleHp()
	{
		try
		{
			if (Session == null || !Session.InBattle) return;
			// R72: the HP series is indexed by battle second, so while the origin is undecided a sample
			// would be filed under the OLD axis and the curve would start 0.9 s early. A sample series loses
			// nothing by waiting for the decision (one to four frames); every other series is fed by the
			// held recorder, which is replayed on the corrected axis.
			if (!Session.ClockOriginDecided) return;
			int sec = ClockOriginHoldPolicy.SecondIndex(Session.ActiveSeconds);
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
