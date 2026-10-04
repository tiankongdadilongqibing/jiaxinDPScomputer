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

	public const int MaxHistory = BattleHistoryRing.Max;

	/// <summary>
	/// RF4, third family: the per-battle counters. Their state, their doc-level ownership and their reset
	/// transitions live in <see cref="BattleRuntimeCounters"/>; every former bare field is now `Rt.X`.
	/// A finalisation does NOT clear them (the export reads them), a soft resume does not either, and
	/// F9 clears only the event count -- all three are cases in tests/BehaviorTests (runtime/counters).
	/// </summary>
	internal static readonly BattleRuntimeCounters Rt = new BattleRuntimeCounters();

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

	// IdleSeconds moved to Policy/SessionTransitionPolicy.cs (RF3b), together with the rule that uses it.

	// MaxFrameDelta moved to Policy/BattleClockPolicy.cs (RF3): the clamp rule and its bound now live in
	// one place and are executed by the behaviour tests at the boundary.

	/// <summary>
	/// RF4 (first state family): all state that must SURVIVE a session boundary -- the remembered soft
	/// close, the grouping marker, and how the previous session ended. One owner, one place, and drivable
	/// offline (tests/BehaviorTests, group "runtime/continuity"). Ownership rules:
	/// _dpsm_work/STATE-LIFETIME-MATRIX.md (not cleared by F9, only a finalisation or a start changes it).
	/// </summary>
	private static readonly SessionContinuity Continuity = new SessionContinuity();

	private static long _lastGsPointer;

	/// <summary>RF4g: the attack snapshot (calc + its clock stamp) and their set/clear rules live in
	/// <see cref="AttackSnapshot{TCalc}"/>; the readers below ask it whether it is Valid.</summary>
	private static readonly AttackSnapshot<DamageCalculater> _active = new AttackSnapshot<DamageCalculater>();

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

	/// <summary>RF4f: the recent-activity ring. Its cap, its ordering and its clearing rules live in
	/// <see cref="CalcActivityLog{T}"/>; the readers below index it exactly as before.</summary>
	private static readonly CalcActivityLog<CalcActivity> _calcEvents = new CalcActivityLog<CalcActivity>();

	// RF4: two fields were DELETED here (_lastCompT, _lastPow). The matrix found them write-only: they were
	// assigned on every calc-activity note and read nowhere in src (PowerProbe has its own pair with the
	// same names, which is what made them look alive). Removing them removes two writes from a per-hit
	// path; the assignment they sat next to was pure (CompositionProbe.Power only decrypts a value).

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

	// 1.5.0 (A2) damage-detail self-report counters: moved to BattleRuntimeCounters (RF4 third family).
	// They are read by ExportService and incremented from the hit-detail channel; see Rt.

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
