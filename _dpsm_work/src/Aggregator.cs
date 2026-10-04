using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace DpsMeter;

public static class Aggregator
{
	public static BattleSession Session;

	public static readonly List<BattleSummary> History = new List<BattleSummary>();

	public const int MaxHistory = 20;

	private static double _lastSummaryLog;
	private static double _lastTimeLog;

	/// <summary>GameSystem.GameTime when the current battle session started. Diagnostic only: GameTime
	/// is an update/frame counter that keeps counting across battles, not a clock.</summary>
	private static int _gameTimeAtStart;

	private static int _eventCount;

	/// <summary>Damage that never reached 耐久 (BattleObject.Damage 入参 − 返回值). Diagnostics + UI only:
	/// the headline totals stay on the applied damage, and this figure is what reconciles them with the
	/// game's own CharacterStatistics.TakenDamage (game口径 = taken + absorbed).</summary>
	private static long AbsorbedTotal;
	private static int AbsorbedHits;

	private static int _lastTickFrame = -1;

	/// <summary>
	/// Monotonic real-time clock for the battle timer. The engine's own deltas must NOT be used:
	/// measured in this game, TIME.UNSCALEDDELTATIME IS THE GAME'S FIXED LOGIC STEP (1/45 s), not the
	/// real frame time (1/30 s at timeScale 1.5). Summing it per rendered frame made the clock run at
	/// 0.67x real speed -- a 17.9 s battle was reported as 10.0 s, and the last hit (which really landed
	/// at ~17.7 s) was drawn at 9.99 s. Evidence (one battle, log + export):
	///   wall 2.0s -> 12.0s : 300 frames, 6.70 s accumulated, 300 x 1/45 = 6.67 s, 300 x 1/30 = 10.0 s
	/// A stopwatch is immune to timeScale, captureDeltaTime and stalled frames (the latter is clamped).
	/// </summary>
	private static readonly Stopwatch Clock = Stopwatch.StartNew();

	/// <summary>Clock.Elapsed seconds at the previous Tick (negative until the first one).</summary>
	private static double _lastTickClock = -1.0;

	/// <summary>GameSystem.GameTime at the previous Tick, for the "game" clock source.</summary>
	private static int _lastGameSteps;
	private static bool _hasLastSteps;

	/// <summary>
	/// How many game units make one game second: the single definition used by the "game" clock source
	/// and reported by the [CLOCK] / [CLOCKP] lines.
	/// Priority: Plugin.CfgGameUnitsPerSecond -> the value measured from the loaded skill data
	/// (Skill.CoolTimeFrame / Skill.CoolTime) -> 30 (measured 2026-09-27: 750/25, 1500/50, 1050/35).
	/// </summary>
	internal static double GameUnitsPerSecond()
	{
		double units = 0.0;
		if (Plugin.CfgGameUnitsPerSecond != null) units = Plugin.CfgGameUnitsPerSecond.Value;
		if (units <= 0.0) units = (TimeProbe.UnitsPerGameSecond > 0.0) ? TimeProbe.UnitsPerGameSecond : 30.0;
		return units;
	}

	/// <summary>Game-clock seconds of silence after which a session is closed even though the game never
	/// said the battle ended. Measured on the BATTLE clock (paused time excluded), so the value is in the
	/// clock's own unit: 8 game seconds ~= 5.3 real seconds while the game runs at 1.5x.</summary>
	private const double IdleSeconds = 8.0;

	/// <summary>A stalled frame (blocked main thread, OS suspend, blocked scene load) hands the whole
	/// stall to us as ONE delta. Measured 2.9 s in a single frame, which jumped the battle clock
	/// (9.3 s -> 12.2 s with no event in between) and was enough to trip the idle timeout in the same
	/// step. Clamped in Tick.</summary>
	private const double MaxFrameDelta = 0.25;

	/// <summary>Most recently finalised session, with how/when it closed, so late events that clearly
	/// belong to it can be folded back in instead of opening a fragment session.</summary>
	private static BattleSession _lastClosed;
	private static DateTime _lastClosedWall;
	private static string _lastClosedWhy = "";

	/// <summary>How long after a SOFT (idle) close a late event may still rejoin its session.</summary>
	private const double ResumeWindowSeconds = 5.0;

	/// <summary>
	/// Grouping marker state (1.3.3). Descriptive ONLY -- it does not change the session boundary, the
	/// clock or any number. See <see cref="BattleSession.RunId"/> for why the grouping is needed.
	/// </summary>
	private static long _runId;
	private static int _runSeq;
	private static DateTime _lastEndWall;
	private static bool _hasEnded;
	private static int _lastEndQuest = int.MinValue;
	private static int _lastEndResult;
	private static string _lastEndWhy = "";

	/// <summary>Two sessions belong to the same "run" (one continuous stretch of play) when the previous
	/// one ended without a result, on the same quest, within this many seconds. A finished battle
	/// (result != 0) NEVER joins the next session, so a marker can never silently absorb a real battle.</summary>
	private const double RunJoinSeconds = 2.0;

	private static long _lastGsPointer;

	private static DamageCalculater _activeCalc;
	private static double _activeCalcT = -1.0;

	/// <summary>Recent damage-calculation events, used to attribute "attacker-less" damage to the calc's owner/attacker.</summary>
	private struct CalcActivity
	{
		public double T;
		public BattleObject A;
		public BattleObject O;
		public BattleObject B;
		public int Dmg;
		/// <summary>The calc itself. The composition is built LAZILY at attach time, because the
		/// calc's own result is only a pre-modifier value: the applied damage (after the target's
		/// damage-taken modifiers / crit) is known only when BattleObject.Damage returns.</summary>
		public DamageCalculater Calc;
		public bool Used;
	}

	private static readonly List<CalcActivity> _calcEvents = new List<CalcActivity>();
	private const int CalcEventMax = 64;

	private static double _lastCompT = -1.0;
	private static int _lastPow;

	/// <summary>
	/// Native pointer of an interop wrapper: a plain managed field read, so it makes no IL2CPP call and
	/// is safe even for an object the game handed us mid-construction (unlike ObjectType / Life /
	/// IsNull, all of which invoke native code and can therefore raise an uncatchable AccessViolation).
	/// </summary>
	private static long PtrOf(object o)
	{
		try
		{
			Il2CppObjectBase b = o as Il2CppObjectBase;
			return (b == null) ? 0L : b.Pointer.ToInt64();
		}
		catch { return 0L; }
	}

	internal static void NoteCalcActivity(DamageCalculater calc, BattleObject attacker, BattleObject owner, BattleObject blocker, int dmg)
	{
		try
		{
			_calcEvents.Add(new CalcActivity
			{
				T = (Session != null) ? Session.ActiveSeconds : 0.0,
				A = attacker,
				O = owner,
				B = blocker,
				Dmg = dmg,
				Calc = calc
			});
			while (_calcEvents.Count > CalcEventMax) _calcEvents.RemoveAt(0);
			if (calc != null)
			{
				_lastPow = CompositionProbe.Power(calc);
				_lastCompT = (Session != null) ? Session.ActiveSeconds : 0.0;
			}
		}
		catch { }
	}

	/// <summary>
	/// Composition of the damage calc behind this hit, built with the APPLIED damage so the shown
	/// multiplier is the real one.
	///
	/// Pairing order:
	///   0) the calc that is executing right now (DamageCalculater.Action -> ... -> BattleObject.Damage);
	///      its blocker is the target being damaged, and a single calc legitimately explains every
	///      target of one AoE cast -- this is the closest thing to an exact pairing.
	///   1) a recent calc for this victim with the same damage value.
	///   2) oldest unused calc for this victim (FIFO keeps multi-hit bursts in order).
	/// Anything below (0) is marked in the text so an approximate line never masquerades as exact.
	/// </summary>
	internal static void TryGetCompForVictim(BattleObject victim, int damage, int nominal, out string a, out string b, out string c, out string d, out CalcBreakdown brk)
	{
		a = "";
		b = "";
		c = "";
		d = "";
		brk = default(CalcBreakdown);
		int absorbed = (nominal > damage) ? (nominal - damage) : 0;
		try
		{
			double now = (Session != null) ? Session.ActiveSeconds : 0.0;

			// ---- 0) the currently executing calc ----
			DamageCalculater live = _activeCalc;
			if (live != null && _activeCalcT >= 0.0)
			{
				double age = now - _activeCalcT;
				if (age >= -0.05 && age <= 0.20)
				{
					BattleObject lb = null;
					try { lb = live.m_blocker; } catch { }
					bool sameTarget = !GameRef.IsNull(lb)
						&& GameRef.Same(lb, victim);
					if (sameTarget || age <= 0.08)
					{
						CompositionProbe.BuildChainParts(live, victim, damage, out string la, out string l2, out string l3, out string l4, absorbed, out CalcBreakdown lbrk);
						if (!string.IsNullOrEmpty(la))
						{
							a = la;
							b = sameTarget ? l2 : (l2 + " · 按当前动作配对");
							// "A calc was running" is not the same as "this calc produced THIS hit". When the
							// damage does not match the calc's own value the composition is borrowed, and the
							// row must say so instead of presenting a wrong 计算威力 as exact.
							//
							// 1.3.0: the wording now says PAIRING, because that is what this check reports.
							// Measured 2026-10-03: 98.6% of the rows whose arithmetic DOES reproduce the game's
							// number still carried the old "本次伤害与该次计算值不符" label, which read as "the
							// composition is wrong". The arithmetic verdict is now the structured
							// calc.valueMatches field; this sentence only reports whether the pairing was
							// corroborated by the calc's own damage value.
							bool corroborated = CalcValueMatches(live, victim, damage, nominal);
							if (!corroborated)
								b += " · 配对未获结算对象佐证(计算威力仅供参考)";
							c = l3;
							d = l4;
							lbrk.Pair = sameTarget ? "live-same" : "live-age";
							lbrk.PairCorroborated = corroborated;
							brk = lbrk;
							return;
						}
					}
				}
			}

			int best = -1;
			bool exact = false;
			// ---- 1) same victim AND same damage value ----
			for (int i = _calcEvents.Count - 1; i >= 0; i--)
			{
				CalcActivity cc = _calcEvents[i];
				if (now - cc.T > 0.80) break;
				if (cc.Used) continue;
				if (!GameRef.Same(cc.B, victim)) continue;
				if (cc.Calc == null) continue;
				if (cc.Dmg == damage || (nominal > 0 && cc.Dmg == nominal)) { best = i; exact = true; break; }
			}
			// ---- 2) oldest unused calc for this victim ----
			if (best < 0)
			{
				for (int i = 0; i < _calcEvents.Count; i++)
				{
					CalcActivity cc = _calcEvents[i];
					if (now - cc.T > 0.60) continue;
					if (cc.Used) continue;
					if (!GameRef.Same(cc.B, victim)) continue;
					if (cc.Calc == null) continue;
					best = i;
					break;
				}
			}
			if (best < 0) return;
			CalcActivity hit = _calcEvents[best];
			hit.Used = true;
			_calcEvents[best] = hit;
			CompositionProbe.BuildChainParts(hit.Calc, hit.B, damage, out a, out b, out c, out d, absorbed, out brk);
			if (!exact && !string.IsNullOrEmpty(b)) b += " · 按时间顺序配对";
			// "value" = a recorded calc for this victim whose damage equals this hit (corroborated);
			// "fifo" = oldest unused calc for this victim (pure time order, NOT corroborated).
			brk.Pair = exact ? "value" : "fifo";
			brk.PairCorroborated = exact;
		}
		catch { }
	}

	/// <summary>
	/// Did the calc that is executing right now actually produce this hit?
	///
	/// The pairing key has to be BattleObject.Damage's ARGUMENT as well as its return value: the return is
	/// the damage left after 被吸收/无效化, so for an absorbed hit it can never equal the calc's own value
	/// (measured 2026-09-27: calc 421,140 → 198 applied, and 198 matched nothing).
	///
	/// 1.3.0 fix: this used to return on the FIRST record whose calc pointer matched, so a single AoE cast
	/// -- one calc, one record per target, plus the DamageAction / ActDamageAction pair -- was judged
	/// against whichever target resolved last. Measured on the two 1.2.3 battles (3,317 and 3,848 hits):
	/// the check reported "not corroborated" for 2,458 / 2,458 live-paired hits, which is why the old
	/// sentence was printed on 99.5% of all rows and why it read as "the composition is wrong" instead of
	/// "this pairing was not corroborated". The record's blocker is now part of the key and every record
	/// for (this calc, this victim) is considered, so the answer means what it says.
	/// </summary>
	private static bool CalcValueMatches(DamageCalculater calc, BattleObject victim, int damage, int nominal)
	{
		try
		{
			long want = PtrOf(calc);
			if (want == 0L) return false;
			for (int i = _calcEvents.Count - 1; i >= 0; i--)
			{
				CalcActivity cc = _calcEvents[i];
				if (PtrOf(cc.Calc) != want) continue;
				if (!GameRef.Same(cc.B, victim)) continue;
				if (cc.Dmg == damage) return true;
				if (nominal > 0 && cc.Dmg == nominal) return true;
			}
		}
		catch { }
		return false;
	}

	private static BattleObject TryResolveCalcSource(BattleObject victim, int damage)
	{
		try
		{
			double now = (Session != null) ? Session.ActiveSeconds : 0.0;
			for (int i = _calcEvents.Count - 1; i >= 0; i--)
			{
				CalcActivity c = _calcEvents[i];
				if (now - c.T > 0.45) continue;
				if (!GameRef.Same(c.B, victim)) continue;
				// prefer explicit attacker, then summon owner; tiny damage ignored
				if (!GameRef.IsNull(c.A)) { _lastCalcSrc = "calcA"; return c.A; }
				if (!GameRef.IsNull(c.O)) { _lastCalcSrc = "calcO"; return c.O; }
				_lastCalcSrc = "calc?";
				return null;
			}
		}
		catch { }
		_lastCalcSrc = "";
		return null;
	}

	private static string _lastCalcSrc = "";

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
		double runGap = _hasEnded ? (DateTime.Now - _lastEndWall).TotalSeconds : -1.0;
		bool runContinues = _hasEnded
			&& _lastEndResult == 0                     // previous session was NOT a finished battle
			&& _lastEndQuest == questId                // same quest
			&& runGap >= 0.0 && runGap <= RunJoinSeconds;
		if (runContinues) _runSeq++;
		else { _runId++; _runSeq = 0; }
		int runSeq = _runSeq;
		long runIdNow = _runId;
		double runGapNow = runContinues ? runGap : -1.0;
		string runPrevWhy = runContinues ? _lastEndWhy : "";
		int runPrevResult = runContinues ? _lastEndResult : 0;
		BattleSession battleSession = (Session = new BattleSession
		{
			InBattle = true,
			Result = (GameResult)0,
			QuestId = questId,
			StartWallClock = DateTime.Now,
			LastEventWall = DateTime.Now,
			RunId = runIdNow,
			RunSeq = runSeq,
			RunGap = runGapNow,
			RunPrevWhy = runPrevWhy,
			RunPrevResult = runPrevResult
		});
		_eventCount = 0;
		_lastSummaryLog = 0.0;
		_lastTimeLog = 0.0;
		_calcEvents.Clear();
		// 1.5.0 (A2): the active calc and the pending damage figures are PER BATTLE. `_activeCalc` was
		// only cleared when a session was resumed, never when one started, so a calc from the previous
		// battle could label this battle's opening hits -- and the pending-hit channel would have carried
		// stale figures across the boundary too.
		_activeCalc = null;
		_activeCalcT = -1.0;
		HitDetailProduced = 0;
		HitDetailTrimmed = 0;
		HitDetailErrors = 0;
		HitMatchExact = 0;
		HitMatchPair = 0;
		HitMatchNone = 0;
		AbsorbedTotal = 0L;
		AbsorbedHits = 0;
		// diagnostic only: GameTime keeps counting across battles, so remember where this one started
		// (the battle clock itself is accumulated from dt in Tick -- GameTime is a frame counter)
		try { _gameTimeAtStart = (val != null) ? val.GameTime : 0; } catch { _gameTimeAtStart = 0; }
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
			+ $" gameTimeAtStart={_gameTimeAtStart} run=#{runIdNow}.{runSeq}"
			+ (runSeq > 0
				? $" gap={runGapNow:F2}s prev={runPrevWhy}/{runPrevResult}"
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
			BattleSession s = _lastClosed;
			if (s == null) return false;
			double gap = (DateTime.Now - _lastClosedWall).TotalSeconds;
			if (gap > ResumeWindowSeconds)
			{
				_lastClosed = null;
				return false;
			}
			if (_lastClosedWhy != "idle") return false;      // a real battle end is a real end
			bool known = (!GameRef.IsNull(a) && s.Actors.ContainsKey(a))
				|| (!GameRef.IsNull(b) && s.Actors.ContainsKey(b));
			if (!known) return false;                        // unknown units -> this is the next battle

			_lastClosed = null;
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

	private static void FinalizeLocked(BattleSession s, GameResult result, string why)
	{
		s.InBattle = false;
		// Remember how this one ended so the NEXT session can decide whether it is a continuation of the
		// same stretch of play (grouping marker only -- see BattleSession.RunId).
		_lastEndWall = DateTime.Now;
		_hasEnded = true;
		_lastEndQuest = s.QuestId;
		_lastEndResult = (int)result;
		_lastEndWhy = why ?? "";
		// NOTE: the battle-wide rule table is deliberately NOT cleared here. Units of the NEXT battle are
		// created (BattleObject.SetupAbility -> RegisterGlobalDebuffs) BEFORE the previous battle is
		// finalized, so clearing here threw away the fresh registrations and an ally buff like
		// "編成時、味方ヴァイスの魔法攻撃の与ダメージ+15%" only worked after its owner had acted once.
		// Stale entries point at destroyed objects (skipped by the Unity null check) and reused pointers
		// are re-scanned because the registered owner name is compared.
		BattleSummary battleSummary = new BattleSummary
		{
			QuestId = s.QuestId.ToString(),
			Result = result.ToString(),
			DurationSeconds = s.ActiveSeconds,
			Session = s
		};
		foreach (ActorStats orderedActor in s.OrderedActors)
		{
			battleSummary.Actors.Add(orderedActor);
			battleSummary.TotalDealt += orderedActor.DamageDealt;
			battleSummary.TotalTaken += orderedActor.DamageTaken;
			battleSummary.TotalHealing += orderedActor.HealingGiven;
		}
		battleSummary.ActorCount = battleSummary.Actors.Count;
		try { battleSummary.Events.AddRange(s.Events); } catch { }
		History.Insert(0, battleSummary);
		while (History.Count > 20) History.RemoveAt(History.Count - 1);

		StringBuilder sb = new StringBuilder();
		sb.Append($"[DpsMeter] Battle {why}: result={result} dur={BattleTime.Log(s.ActiveSeconds)} idle={BattleTime.Log(s.IdleCombatSeconds)} quest={s.QuestId} actors={s.OrderedActors.Count} unattributed={s.UnattributedDamage}(x{s.UnattributedHits})\n");
		foreach (ActorStats a in s.OrderedActors)
		{
			if ((int)a.Team == 1 || a.DamageDealt != 0L || a.DamageTaken != 0L || a.HealingGiven != 0L)
			{
				sb.Append($"  {KindTag(a),-5} {a.Name,-24} dealt={a.DamageDealt,10} dps={a.Dps(s.ActiveSeconds),8:F1} hits={a.HitCount,5} maxhit={a.MaxHitDamage,8} healed={a.HealingGiven,8}(nom{a.HealingGivenNominal,9}) self={a.HealingSelf,8} taken={a.DamageTaken,8} [{a.AttrMode}]\n");
			}
		}
		if (s.UnattributedDamage != 0L)
		{
			sb.Append($"  >>> 未归属伤害(打到目标但无攻击者来源) {s.UnattributedDamage} / {s.UnattributedHits} hits\n");
			var top = new List<KeyValuePair<string, long>>(s.UnattributedByVictim);
			top.Sort((x, y) => y.Value.CompareTo(x.Value));
			int shown = 0;
			foreach (var kv in top)
			{
				if (shown++ >= 15) break;
				sb.Append($"      gap victim: {kv.Key,-28} {kv.Value,10}\n");
			}
		}
		sb.Append($"  TOTALS dealt={battleSummary.TotalDealt} taken={battleSummary.TotalTaken} healing={battleSummary.TotalHealing}");
		if (AbsorbedHits > 0)
		{
			// The reconciliation line: our taken total is the damage that reached 耐久, the game's own
			// counter adds everything that was absorbed on the way, so taken + absorbed must equal it.
			sb.Append($"\n  >>> 被吸收/无效化 {AbsorbedTotal} / {AbsorbedHits} hits  (taken {battleSummary.TotalTaken} + 吸收 {AbsorbedTotal} = 游戏口径 {battleSummary.TotalTaken + AbsorbedTotal})");
		}
		string text = sb.ToString();
		Plugin.LogSource.LogInfo(text);
		RuntimeLog.Write(text);

		// Clock self-check. For the "real" source the battle clock and the wall clock measure the same
		// thing, so active/wall must be ~1.00; anything far from it means the clock is fed the wrong time
		// base (0.67 was measured while it consumed Time.unscaledDeltaTime = the 1/45 s logic step). For
		// the "game" source a ratio of ~stepRate/units (1.5 at timeScale 1.5, 30 units per game second)
		// is CORRECT, so only the source, the units and the ratio are reported -- the [CLOCKP] line shows
		// stepRate and the skill data that produced those units.
		try
		{
			double wallDur = (DateTime.Now - s.StartWallClock).TotalSeconds;
			if (wallDur > 1.0)
			{
				double rate = s.ActiveSeconds / wallDur;
				string src = ClockSourceName();
				string flag = "";
				if (src == "real" && (rate < 0.85 || rate > 1.15))
					flag = "  !!! 时钟与真实时间不符,请检查 Tick 的时间来源";
				string clock = $"[DpsMeter][CLOCK] source={src} units={GameUnitsPerSecond():F1} active={BattleTime.Log(s.ActiveSeconds)} wall={BattleTime.Log(wallDur)} ratio={rate:F2}{flag}";
				Plugin.LogSource.LogInfo(clock);
				RuntimeLog.Write(clock);
			}
		}
		catch { }

		// Cross check against game's own CharacterStatistics (official result panel data).
		foreach (ActorStats orderedActor3 in s.OrderedActors)
		{
			try
			{
				CharacterStatistics val = (!GameRef.IsNull(orderedActor3.Source)) ? orderedActor3.Source.Statistics : null;
				if (val != null)
				{
					string t2 = $"[DpsMeter][CROSS] {KindTag(orderedActor3),-5} {orderedActor3.Name,-24} mine_dealt={orderedActor3.DamageDealt,9} game_given={GameRef.Dec(val.GivenDamage),9} mine_taken={orderedActor3.DamageTaken,8} absorbed={orderedActor3.DamageAbsorbed,8} nominal_taken={orderedActor3.DamageTakenNominal,8} game_taken={GameRef.Dec(val.TakenDamage),8} mine_heal={orderedActor3.HealingGiven,8}(nom{orderedActor3.HealingGivenNominal,9}/self{orderedActor3.HealingSelf,8}) game_heal={GameRef.Dec(val.GivenHealing),8} mine_hits={orderedActor3.HitCount,5} game_attacks={GameRef.Dec(val.AttackCount),5}";
					Plugin.LogSource.LogInfo(t2);
					RuntimeLog.Write(t2);
				}
			}
			catch { }
		}
		if (Plugin.CfgTraceCandidates.Value) Probe.DumpAll();
		// Settle every per-hit ailment re-check still in flight BEFORE exporting, otherwise a record whose
		// status landed late would be exported without it.
		StatusDeltaProbe.FlushAll(s.ActiveSeconds);
		// ...then audit the result: every "status appeared" transition must have an infliction record.
		StatusDeltaProbe.Audit(s);
		// Battle-end talent table. MUST run before the export and before ActorStats.Source is cleared below,
		// because the audit columns (TotalActivateCount / m_statistics.ActivateCount) need the live object.
		TalentRuntime.FinalizeTable(s);
		// Self-report for the 1.1 provenance/talent features. Route A materialising or not is a MEASURED
		// outcome, so it is stated every battle instead of being assumed: if the ValueTuple list cannot be
		// materialised the slots come from the statistics join, and any unit left 未分类 is visible here.
		try
		{
			string rr = "[DpsMeter][ROSTER] 出处 " + AbilityRoster.Diag() + " | 素质 " + TalentRuntime.Diag();
			Plugin.LogSource.LogInfo(rr);
			RuntimeLog.Write(rr);
			if (Plugin.CfgAbilityRoster != null && Plugin.CfgAbilityRoster.Value)
			{
				foreach (ActorStats ra in s.OrderedActors)
				{
					if (ra == null || ra.Roster == null || ra.Roster.Count == 0) continue;
					string det = "[DpsMeter][ROSTER] " + (CharacterInfo.IsAllyTeam(ra.Team) ? "我方" : "敌方") + " " + ra.Name
						+ " 能力=" + ra.Roster.Count + " [" + AbilityRoster.Brief(ra.Roster) + "]"
						+ " 素质发动=" + (ra.TalentTable != null ? ra.TalentTable.Count : 0) + " 条";
					Plugin.LogSource.LogInfo(det);
					RuntimeLog.Write(det);
				}
			}
		}
		catch (Exception ex)
		{
			RuntimeLog.Write("[DpsMeter][ROSTER] 自检行输出失败(不影响导出): " + ex.Message);
		}
		// Feasibility probe for the 1.1 design (Diagnostics/SlotProbe.cs): must run BEFORE the export clears
		// the actors' BattleObject references (below), and before _calcEvents.Clear(). Gated by Debug/SlotProbe.
		if (Plugin.CfgSlotProbe != null && Plugin.CfgSlotProbe.Value) SlotProbe.Run(s);
		// Master data (the game's own tables) is process-global and never changes, so it is dumped once.
		// Done here because the tables are guaranteed loaded by the time a battle has ended; RunOnce
		// retries on a later battle if none were found yet.
		if (Plugin.CfgMasterDataDump != null && Plugin.CfgMasterDataDump.Value) MasterDataDump.RunOnce();
		ExportService.Export(s); // full-data JSON for offline analysis
		_calcEvents.Clear();
		foreach (ActorStats orderedActor4 in s.OrderedActors) orderedActor4.Source = null;
		RuntimeLog.Flush();
		// Remember the close so a late event of the SAME battle can rejoin it (TryResumeClosedSession)
		// instead of opening a fragment session. Only an "idle" close is resumable.
		_lastClosed = s;
		_lastClosedWall = DateTime.Now;
		_lastClosedWhy = why;
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
		string source = (Plugin.CfgClockSource != null) ? Plugin.CfgClockSource.Value.Trim().ToLowerInvariant() : "";
		if (source != "real" && source != "engine" && source != "game")
		{
			// Unset / misspelled: honour the legacy bool if it was turned on, else the default.
			// NOTE for the next default change: BepInEx keeps the value already present in
			// BepInEx\config\dev.dpsmeter.cfg, so changing the default in code does NOT migrate an
			// installed config -- the [CLOCK]/[TIME] lines print `source=` so the effective value is
			// always visible in the log.
			source = (Plugin.CfgTimerUsesGameTime != null && Plugin.CfgTimerUsesGameTime.Value) ? "engine" : "game";
		}
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
			int dSteps = _hasLastSteps ? (steps - _lastGameSteps) : 0;
			_lastGameSteps = steps;
			_hasLastSteps = true;
			return (dSteps > 0) ? dSteps / GameUnitsPerSecond() : 0.0;
		}
		// Real seconds from the monotonic stopwatch (see the Clock field).
		double now = Clock.Elapsed.TotalSeconds;
		double delta = (_lastTickClock < 0.0) ? 0.0 : now - _lastTickClock;
		_lastTickClock = now;
		return delta;
	}

	/// <summary>Effective clock source name, for the diagnostics.</summary>
	internal static string ClockSourceName()
	{
		string source = (Plugin.CfgClockSource != null) ? Plugin.CfgClockSource.Value.Trim().ToLowerInvariant() : "";
		if (source == "real" || source == "engine" || source == "game") return source;
		return (Plugin.CfgTimerUsesGameTime != null && Plugin.CfgTimerUsesGameTime.Value) ? "engine" : "game";
	}

	internal static string KindTag(ActorStats a)
	{
		if (a.IsSummonMerge) return "召" + CharacterInfo.KindText(a.Kind);
		return CharacterInfo.KindText(a.Kind);
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
		if (dt < 0.0) dt = 0.0;
		// A stalled frame (blocked main thread, OS suspend, blocked scene load) delivers the entire stall
		// as ONE delta -- measured 2.9 s in a single frame. Clamp it: the battle clock must not jump, and
		// an unclamped stall is by itself enough to trip the idle timeout in the very same step (that is
		// how one battle got split in two: logged dur 9.3 s -> 12.2 s with no event in between).
		if (dt > MaxFrameDelta) dt = MaxFrameDelta;

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

	/// <summary>Same-team damage (heal reversal / self-damage) is not output.
	/// Also used by the battle-wide debuff rules, which only affect a unit's ENEMIES.</summary>
	internal static bool IsSameTeam(BattleObject attacker, BattleObject victim)
	{
		try
		{
			if (GameRef.IsNull(attacker)) return false;
			if (GameRef.IsNull(victim)) return false;
			return attacker.TeamType == victim.TeamType;
		}
		catch { return false; }
	}

	private static int TeamOf(BattleObject b)
	{
		try
		{
			if (!GameRef.IsNull(b)) return (int)b.TeamType;
		}
		catch { }
		return 0;
	}

	/// <summary>Add a damage event to an actor's totals.
	///
	/// Same-team damage (回復反転 / self-damage) is ALWAYS tallied separately into DamageFriendly.
	/// By default it also stays inside DamageDealt, because the game's own damage report counts it:
	/// verified on a real battle, game_given == friendly + normal for the affected unit
	/// (T.O.W.E.R.typeR: 5,252,510 + 1,921 == 5,254,431). Set FilterFriendlyFire=true to drop it
	/// from the totals instead; either way the UI always labels it.</summary>
	private static void Accumulate(ActorStats st, int damage, string attrMode, bool friendly)
	{
		if (st == null) return;
		if (friendly)
		{
			st.DamageFriendly += damage;
			st.FriendlyHits++;
			bool filter = Plugin.CfgFilterFriendlyFire != null && Plugin.CfgFilterFriendlyFire.Value;
			if (filter) return;
		}
		st.DamageDealt += damage;
		st.HitCount++;
		st.AttrMode = attrMode;
		st.LastHitTime = Session.ActiveSeconds;
		if (st.HitCount == 1) st.FirstHitTime = Session.ActiveSeconds;
		// self-injury must not become the "biggest hit" of an attacker
		if (!friendly && damage > st.MaxHitDamage) st.MaxHitDamage = damage;
		st.AddSample(Session.ActiveSeconds, st.DamageDealt);
		st.AddSecondDamage((int)Session.ActiveSeconds, damage);
	}

	/// <param name="nominal">
	/// BattleObject.Damage's ARGUMENT: the damage the game accounted for (CharacterStatistics.TakenDamage).
	/// <paramref name="damage"/> is its RETURN: what actually reached 耐久. nominal &gt; damage means part of
	/// the hit was 被吸收/无效化. Passing 0 (the default) means "same as damage".
	/// </param>
	public static void RecordDamage(BattleObject victim, BattleObject attacker, BattleObject owner, int damage, int nominal = 0)
	{
		if (damage <= 0 && nominal <= 0) return;
		if (nominal < damage) nominal = damage;
		int absorbed = nominal - damage;
		EnsureSessionStartedFor(attacker, victim);
		BeginTimingIfNeeded();
		_eventCount++;
		Session.NoteEvent();

		ActorStats victimStats = Session.GetActor(victim, create: true);
		if (victimStats != null)
		{
			victimStats.DamageTaken += damage;
			victimStats.DamageTakenNominal += nominal;
			victimStats.DamageAbsorbed += absorbed;
			victimStats.AddSecondTaken((int)Session.ActiveSeconds, damage);
			if (CharacterInfo.IsAllyTeam(victimStats.Team))
				Session.AddTeamTaken((int)Session.ActiveSeconds, damage);
		}
		if (absorbed > 0)
		{
			AbsorbedTotal += absorbed;
			AbsorbedHits++;
			// Rare by nature, and the single most confusing row in the detail list, so it is always logged.
			string l2 = $"[DpsMeter][ABSORB] {Desc(victim)} 被吸收/无效化 {absorbed}(游戏口径 {nominal} = 入耐久 {damage} + 吸收 {absorbed})";
			Plugin.LogSource.LogInfo(l2);
			RuntimeLog.Write(l2);
		}

		BattleObject source = attacker;
		string attrMode = "?";
		if (GameRef.IsNull(source)) source = owner;
		if (!GameRef.IsNull(attacker))
			attrMode = (!GameRef.IsNull(owner) && !GameRef.Same(owner, attacker)) ? "A+O" : "A";
		else if (!GameRef.IsNull(owner)) attrMode = "O";

		// Damage dealt to the attacker's OWN team is not output: 回復反転 (heal reversal, wiki:
		// damage = 20% of the heal value) and self-damage skills both land here. Team is read from
		// the objects themselves, so identical names on both sides cannot fool this check --
		// unlike the old name-based heuristics, which is why reversal damage used to be counted
		// as the healer's DPS.
		bool friendly = IsSameTeam(source, victim);

		ActorStats actorStats = null;
		if (!GameRef.IsNull(source))
		{
			actorStats = Session.GetActor(source, create: true);
			if (actorStats != null) Accumulate(actorStats, damage, attrMode, friendly);
		}
		else
		{
			// Try to resolve the attacker from the recent damage-calculation activity
			// (many enemy/area attacks carry no attacker/owner on BattleObject.Damage itself).
			BattleObject resolved = TryResolveCalcSource(victim, damage);
			if (!GameRef.IsNull(resolved))
			{
				// keep the event's attacker name in sync with the resolved source (used to stay "?")
				source = resolved;
				attrMode = "C:" + _lastCalcSrc;
				bool friendlyResolved = IsSameTeam(resolved, victim);
				actorStats = Session.GetActor(resolved, create: true);
				if (actorStats != null) Accumulate(actorStats, damage, attrMode, friendlyResolved);
				friendly = friendlyResolved;
			}
			else
			{
				Session.UnattributedDamage += damage;
				Session.UnattributedHits++;
				if (!GameRef.IsNull(victim))
				{
					try
					{
						string vn = CharacterInfo.DisplayName(victim);
						Session.UnattributedByVictim.TryGetValue(vn, out var acc);
						Session.UnattributedByVictim[vn] = acc + damage;
					}
					catch { }
				}
			}
		}

		// 1.5.0 (A2): the damage-detail match. `hitHow` says which kind of match was used (0 none,
		// 1 exact by damage value, 2 by attacker+target only) and is exported per hit, so a best-effort
		// label can never be mistaken for an authoritative one.
		int hitHow;
		HitRecord hitRecord = Session.ConsumePending(source, victim, damage, nominal, Session.ActiveSeconds, out hitHow);
		if (hitRecord != null) { if (hitHow == 1) HitMatchExact++; else HitMatchPair++; }
		else HitMatchNone++;
		if (hitRecord != null && actorStats != null)
		{
			// The crit tally only moves on an OBSERVED flag: a record with CritObserved == 0 says nothing
			// about crit, and counting it as "not a crit" is how a metric silently becomes a fiction.
			if (hitRecord.CritObserved == 2)
			{
				actorStats.CritCount++;
				actorStats.CritDamage += damage;
			}
			else
			{
				actorStats.NonCritDamage += damage;
			}
			if (hitRecord.EffectId != 0)
			{
				actorStats.SkillDamage.TryGetValue(hitRecord.EffectId, out var v1);
				actorStats.SkillDamage[hitRecord.EffectId] = v1 + damage;
				actorStats.SkillHits.TryGetValue(hitRecord.EffectId, out var v2);
				actorStats.SkillHits[hitRecord.EffectId] = v2 + 1;
			}
			actorStats.SourceDamage.TryGetValue((int)hitRecord.Source, out var v3);
			actorStats.SourceDamage[(int)hitRecord.Source] = v3 + damage;
		}
		else if (actorStats != null && _activeCalc != null)
		{
			try
			{
				// 1.5.0 (A2): the SAME age window the composition's own pairing uses. This fallback had
				// none, so a calc left over from the previous battle could label this battle's opening
				// hits -- and `_activeCalc` was not even cleared when a session started (fixed in
				// StartSession below). The composition path was protected; this one was not.
				double age = Session.ActiveSeconds - _activeCalcT;
				if (age >= -0.05 && age <= 0.20 && GameRef.Same(_activeCalc.Attacker, source))
				{
					int effectId = _activeCalc.m_effectId;
					if (effectId != 0)
					{
						actorStats.SkillDamage.TryGetValue(effectId, out var v4);
						actorStats.SkillDamage[effectId] = v4 + damage;
						actorStats.SkillHits.TryGetValue(effectId, out var v5);
						actorStats.SkillHits[effectId] = v5 + 1;
					}
				}
			}
			catch { }
		}

		if (Plugin.CfgVerbose.Value)
		{
			string line = $"[DpsMeter] DMG {damage} {Desc(source)} -> {Desc(victim)} mode={attrMode} src={hitRecord?.Source} crit={hitRecord?.CritObserved} match={hitHow} eff={hitRecord?.EffectId}";
			Plugin.LogSource.LogInfo(line);
			RuntimeLog.Write(line);
		}
		// full event log for offline analysis
		try
		{
			TryGetCompForVictim(victim, damage, nominal, out string compA, out string compB, out string compC, out string compD, out CalcBreakdown compCalc);
			var ev = new BattleEvent
			{
				T = Session.ActiveSeconds,
				Type = "dmg",
				Victim = NameOf(victim),
				Attacker = NameOf(source),
				Owner = NameOf(owner),
				Attr = attrMode,
				Amount = damage,
				Nominal = nominal,
				Source = hitRecord != null ? (int)hitRecord.Source : 0,
				Crit = hitRecord != null && hitRecord.CritObserved == 2,
				// 1.5.0 (A2/A3): a tri-state, so "the game said no" and "we never saw the flag" are
				// distinguishable in the data. `Crit` above keeps its old bool shape for existing scripts
				// and is exactly (CritObserved == 2).
				CritObserved = hitRecord != null ? hitRecord.CritObserved : (byte)0,
				HitMatch = hitHow,
				// 1.5.2: the value the matched record actually carried, so `hitMatch` becomes auditable
				// instead of only assertable -- see BattleEvent.HitValue.
				HitValue = hitRecord != null ? hitRecord.Damage : 0L,
				CalcHitType = hitRecord != null ? (int)hitRecord.HitType : -1,
				CalcEffectId = hitRecord != null ? hitRecord.EffectId : 0,
				HealCalc = hitRecord != null && hitRecord.HitType == eDamageCalcType.Heal,
				AttackerTeam = TeamOf(source),
				VictimTeam = TeamOf(victim),
				AttackerKey = actorStats != null ? actorStats.Key : 0,
				VictimKey = victimStats != null ? victimStats.Key : 0,
				Friendly = friendly,
				Comp = compA,
				Comp2 = compB,
				Comp3 = compC,
				Comp4 = compD,
				Calc = compCalc
			};
			// Did THIS record inflict an ailment on the victim? Diff the status list captured by the
			// Damage prefix against the state now, and schedule a late re-check (StatusDeltaProbe).
			// Bind before AddEvent: the probe keeps the reference and mutates it when the late result
			// lands, and the event is the same object the export and the overlay read.
			StatusDeltaProbe.Bind(ev, victim, Session.ActiveSeconds);
			// Which 素质/词条 fired between this attacker's previous hit and this one? Read the game's own
			// activation counters (Diagnostics/TalentRuntime.cs). The extra hit produced by a follow-up
			// talent is exactly the evidence that the talent fired, so it is attributed to this record.
			TalentRuntime.NoteAttack(source, actorStats, ev);
			// 1.4.0: keep a bounded specimen of the hits the chain could NOT explain, with the LIVE state
			// of both sides. Must run BEFORE AddEvent but while `source`/`victim` are still alive -- the
			// composition reads only what it models, and what it does not model is the whole question.
			Forensics.Observe(ev, source, victim);
			// 1.5.0 (B4): the full-resolution state timeline for the VICTIM. Runs for EVERY damage event
			// (not only the unexplained ones) and emits a row only when something actually changed, so the
			// cost is 18 slot reads here and near-zero output. This is what makes a resistance curve
			// measured rather than sampled -- see Diagnostics/StateTimelineProbe.cs.
			StateTimeline.Observe(victim, victimStats != null ? victimStats.Key : 0, Session.ActiveSeconds);
			// 1.5.0 (B1): the deduplicated fact record. Runs for EVERY damage hit, so the export can
			// answer a question about any past battle instead of only about the 3% forensics sampled.
			ev.FactId = FactStore.Observe(ev, source, victim,
				actorStats != null ? actorStats.Key : 0,
				victimStats != null ? victimStats.Key : 0);
			Session.AddEvent(ev);
		}
		catch { }
	}

	public static void RecordHeal(BattleObject target, BattleObject healer, int actual, int nominal)
	{
		if (actual <= 0 && nominal <= 0) return;
		EnsureSessionStartedFor(healer, target);
		BeginTimingIfNeeded();
		_eventCount++;
		Session.NoteEvent();
		ActorStats actor = Session.GetActor(target, create: true);
		if (actor != null)
		{
			actor.HealingTaken += actual;
			actor.HealingTakenNominal += nominal;
			actor.AddSecondHeal((int)Session.ActiveSeconds, actual);
			if (CharacterInfo.IsAllyTeam(actor.Team))
				Session.AddTeamHeal((int)Session.ActiveSeconds, actual);
		}
		// 1.5.0 (A4): resolved OUTSIDE the block below so the event can carry stable keys. `healerStats` is
		// null when the healer object is missing, which is exactly what key 0 means.
		ActorStats healerStats = null;
		if (!GameRef.IsNull(healer))
		{
			ActorStats actor2 = Session.GetActor(healer, create: true);
			healerStats = actor2;
			if (actor2 != null)
			{
				actor2.HealingGiven += actual;
				actor2.HealingGivenNominal += nominal;
			}
		}
		else if (actor != null)
		{
			actor.HealingSelf += nominal;
		}
		if (Plugin.CfgVerbose.Value)
		{
			string line = $"[DpsMeter] HEAL {actual}/{nominal} {Desc(healer)} -> {Desc(target)}";
			Plugin.LogSource.LogInfo(line);
			RuntimeLog.Write(line);
		}
		try
		{
			Session.AddEvent(new BattleEvent
			{
				T = Session.ActiveSeconds,
				Type = "heal",
				Victim = NameOf(target),
				Attacker = NameOf(healer),
				Owner = NameOf(healer),
				Attr = "H",
				Amount = actual,
				Nominal = nominal,
				Source = (int)DamageSource.DirectHeal,
				Crit = false,
				AttackerTeam = TeamOf(healer),
				VictimTeam = TeamOf(target),
				// 1.5.0 (A4): the same stable keys as the damage path. `actor` is the TARGET of the heal
				// and `actor2` the healer, so the names line up with Attacker/Victim above.
				AttackerKey = healerStats != null ? healerStats.Key : 0,
				VictimKey = actor != null ? actor.Key : 0
			});
		}
		catch { }
	}

	// ---- 1.5.0 (A2): the damage-detail channel, and its self-report ----
	// These counters exist because the failure this channel had was SILENT: it had no producer at all, so
	// `source`/`crit` were constants and nothing anywhere said so. Every new read reports what it did.
	/// <summary>Pending damage figures produced by the four damage-returning hooks.</summary>
	internal static int HitDetailProduced;
	/// <summary>Records dropped by the 2048-entry cap (never silent).</summary>
	internal static int HitDetailTrimmed;
	/// <summary>Field reads / plumbing failures while producing a record.</summary>
	internal static int HitDetailErrors;
	/// <summary>Damage records matched to a pending figure with the SAME damage value.</summary>
	internal static int HitMatchExact;
	/// <summary>Matched by (attacker, target) only, damage differing -- best effort, counted separately.</summary>
	internal static int HitMatchPair;
	/// <summary>Damage records with no pending figure at all (field stays Unknown).</summary>
	internal static int HitMatchNone;

	/// <summary>
	/// 1.5.0 (A2): produce one pending damage figure from the calc that computed it. Called by the four
	/// hooks that return a damage number, so that `DamageSource`, the crit flag, the hit type and the
	/// effect id stop being constants in the export.
	///
	/// The blocker is taken from the argument when the hook has one and from `calc.m_blocker` otherwise
	/// (`ApplyBarrierDamage` / `ApplyEnchantDamage` take only the power, and they carry most of a battle's
	/// damage -- measured 1096 of one battle's hits through ApplyEnchantDamage alone).
	/// </summary>
	internal static void NoteHitDetail(DamageCalculater calc, BattleObject blocker, int damage)
	{
		if (calc == null) return;
		try
		{
			if (blocker == null) { try { blocker = calc.m_blocker; } catch { HitDetailErrors++; } }
			var src = DamageSource.Unknown;
			try { src = calc.m_damageSource; } catch { HitDetailErrors++; }
			int ht = -1;
			try { ht = (int)calc.m_hitType; } catch { HitDetailErrors++; }
			int eff = 0;
			try { eff = calc.m_effectId; } catch { HitDetailErrors++; }
			BattleObject atk = null;
			try { atk = calc.Attacker; } catch { HitDetailErrors++; }
			RecordHitDetail(atk, blocker, damage, src, (eDamageCalcType)ht, eff, CompositionProbe.ObservedCrit(blocker));
		}
		catch { HitDetailErrors++; }
	}

	public static void RecordHitDetail(BattleObject attacker, BattleObject blocker, int damage, DamageSource source, eDamageCalcType hitType, int effectId, byte critObserved)
	{
		if (Session != null && Session.InBattle)
		{
			try
			{
				Session.PendingHits.Add(new HitRecord
				{
					Attacker = attacker,
					Blocker = blocker,
					Damage = damage,
					Source = source,
					HitType = hitType,
					EffectId = effectId,
					CritObserved = critObserved,
					T = Session.ActiveSeconds
				});
				HitDetailProduced++;
				if (Session.PendingHits.Count > 2048)
				{
					int n = Session.PendingHits.Count - 2048;
					Session.PendingHits.RemoveRange(0, n);
					HitDetailTrimmed += n;
				}
			}
			catch { HitDetailErrors++; }
		}
	}

	public static void NoteActiveCalc(DamageCalculater calc)
	{
		_activeCalc = calc;
		_activeCalcT = (Session != null) ? Session.ActiveSeconds : 0.0;
		// attack start: remember the target's statuses, because the game judges status conditions here
		// (a debuff applied by this very hit must not raise its own hit)
		try { CompositionProbe.SnapshotStatuses(calc); } catch { }
	}

	public static void ResetCurrent()
	{
		if (Session != null && Session.InBattle)
		{
			Session.ResetActors();
			_eventCount = 0;
			string text = "[DpsMeter] Manual reset";
			Plugin.LogSource.LogInfo(text);
			RuntimeLog.Write(text);
		}
	}

	internal static string Desc(BattleObject bo)
	{
		if (GameRef.IsNull(bo)) return "?";
		try { return $"{CharacterInfo.KindLabel(bo)}:{CharacterInfo.DisplayName(bo)}"; }
		catch { return "?"; }
	}

	internal static string NameOf(BattleObject bo)
	{
		if (GameRef.IsNull(bo)) return "?";
		try { return CharacterInfo.DisplayName(bo); }
		catch { return "?"; }
	}
}
