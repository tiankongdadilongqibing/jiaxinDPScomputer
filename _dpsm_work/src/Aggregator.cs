using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace DpsMeter;

public static partial class Aggregator
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

	/// <summary>Game-clock seconds of silence after which a session is closed even though the game never
	/// said the battle ended. Measured on the BATTLE clock (paused time excluded), so the value is in the
	/// clock's own unit: 8 game seconds ~= 5.3 real seconds while the game runs at 1.5x.</summary>
	private const double IdleSeconds = 8.0;

	// MaxFrameDelta moved to Policy/BattleClockPolicy.cs (RF3): the clamp rule and its bound now live in
	// one place and are executed by the behaviour tests at the boundary.

	/// <summary>Most recently finalised session, with how/when it closed, so late events that clearly
	/// belong to it can be folded back in instead of opening a fragment session.</summary>
	private static BattleSession _lastClosed;
	private static DateTime _lastClosedWall;
	private static string _lastClosedWhy = "";

	// ResumeWindowSeconds moved to Policy/SessionTransitionPolicy.cs (RF3).

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

	// RunJoinSeconds moved to Policy/SessionTransitionPolicy.cs (RF3).

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

	private static string _lastCalcSrc = "";

	internal static string KindTag(ActorStats a)
	{
		if (a.IsSummonMerge) return "召" + CharacterInfo.KindText(a.Kind);
		return CharacterInfo.KindText(a.Kind);
	}

	// ---- 1.5.0 (A2): the damage-detail channel, and its self-report ----
	// These counters exist because the failure this channel had was SILENT: it had no producer at all, so
	// `source`/`crit` were constants and nothing anywhere said so. Every new read reports what it did.
	/// <summary>Pending damage figures produced by the four damage-returning hooks.</summary>
	internal static int HitDetailProduced;
	/// <summary>Records dropped by the BattleSession.MaxPending cap (never silent).</summary>
	internal static int HitDetailTrimmed;
	/// <summary>Field reads / plumbing failures while producing a record.</summary>
	internal static int HitDetailErrors;
	/// <summary>Damage records matched to a pending figure with the SAME damage value.</summary>
	internal static int HitMatchExact;
	/// <summary>Matched by (attacker, target) only, damage differing -- best effort, counted separately.</summary>
	internal static int HitMatchPair;
	/// <summary>Damage records with no pending figure at all (field stays Unknown).</summary>
	internal static int HitMatchNone;

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
