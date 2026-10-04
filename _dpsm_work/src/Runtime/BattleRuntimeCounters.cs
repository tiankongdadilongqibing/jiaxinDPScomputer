namespace DpsMeter;

/// <summary>
/// RF4, third family: the counters ONE battle accumulates, and the transitions that clear them.
///
/// Why a container. These twelve values were static fields of the Aggregator facade, written and read
/// from eight files, and their reset rules were spread over three methods (StartSession, ResetCurrent,
/// TryResumeClosedSession). The matrix had to write those rules down in prose; here they are methods, so
/// the suite can execute them:
///
///   * <see cref="OnSessionStart"/> -- a new battle starts every one of them at zero;
///   * <see cref="OnManualReset"/> -- F9 drops ONLY the event count (measured in Aggregator.ResetCurrent);
///   * a soft RESUME clears none of them (a resumed session continues the same battle);
///   * a FINALISATION clears none of them either -- the export reads them (see below), so there is no
///     OnFinalize method on purpose.
///
/// Ownership: this is per-battle state, NOT per-process (unlike the clock cursors, which survive a
/// session so a new battle does not inherit a stale frame delta) and NOT cross-session (unlike the
/// registry in Runtime/GlobalRuleRegistry).
/// </summary>
internal sealed class BattleRuntimeCounters
{
	/// <summary>Damage / heal events admitted for this battle. Read by the idle-close rule and by the
	/// [TIME] heartbeat, incremented once per recorded event.</summary>
	public int EventCount;

	/// <summary>Damage that never reached 耐久 (BattleObject.Damage 入参 − 返回值). Diagnostics + UI only:
	/// the headline totals stay on the applied damage, and this figure is what reconciles them with the
	/// game's own CharacterStatistics.TakenDamage (game口径 = taken + absorbed).</summary>
	public long AbsorbedTotal;
	public int AbsorbedHits;

	// ---- 1.5.0 (A2): the damage-detail channel, and its self-report ----
	// These counters exist because the failure this channel had was SILENT: it had no producer at all, so
	// `source`/`crit` were constants and nothing anywhere said so. Every new read reports what it did.
	/// <summary>Pending damage figures produced by the four damage-returning hooks.</summary>
	public int HitDetailProduced;
	/// <summary>Records dropped by the BattleSession.MaxPending cap (never silent).</summary>
	public int HitDetailTrimmed;
	/// <summary>Field reads / plumbing failures while producing a record.</summary>
	public int HitDetailErrors;
	/// <summary>Damage records matched to a pending figure with the SAME damage value.</summary>
	public int HitMatchExact;
	/// <summary>Matched by (attacker, target) only, damage differing -- best effort, counted separately.</summary>
	public int HitMatchPair;
	/// <summary>Damage records with no pending figure at all (field stays Unknown).</summary>
	public int HitMatchNone;

	/// <summary>Battle-clock stamps of the last [SUMMARY] / [TIME] log lines. Per-battle throttles: a new
	/// battle must be allowed to log immediately.</summary>
	public double LastSummaryLog;
	public double LastTimeLog;

	/// <summary>GameSystem.GameTime when the current battle session started. Diagnostic only: GameTime
	/// is an update/frame counter that keeps counting across battles, not a clock. NOT zeroed by
	/// <see cref="OnSessionStart"/>: its value comes from a native read taken at the same point, and the
	/// facade owns every native read.</summary>
	public int GameTimeAtStart;

	/// <summary>A new battle: everything above starts at zero.</summary>
	public void OnSessionStart()
	{
		EventCount = 0;
		AbsorbedTotal = 0L;
		AbsorbedHits = 0;
		HitDetailProduced = 0;
		HitDetailTrimmed = 0;
		HitDetailErrors = 0;
		HitMatchExact = 0;
		HitMatchPair = 0;
		HitMatchNone = 0;
		LastSummaryLog = 0.0;
		LastTimeLog = 0.0;
	}

	/// <summary>
	/// F9 / manual reset. ONLY the event count is dropped: the players are re-listed (ResetActors) and the
	/// event count restarts, while the absorbed totals and the damage-detail self-report still describe the
	/// whole battle and are deliberately kept -- the export reports them at the end.
	/// </summary>
	public void OnManualReset()
	{
		EventCount = 0;
	}
}
