using System;

namespace DpsMeter;

/// <summary>The run marker a new session carries, produced by
/// <see cref="SessionContinuity.BeginSession"/> so the "which run" decision and the state it advances
/// cannot be taken from two different places.</summary>
internal struct RunMarker
{
	public long RunId;
	public int RunSeq;
	/// <summary>Seconds since the previous session of the run ended. -1 = this session starts a run.</summary>
	public double RunGap;
	/// <summary>How the previous session ended ("" when starting a run).</summary>
	public string PrevWhy;
	/// <summary>The previous session's result (0 = it had none, which is why the run continues).</summary>
	public int PrevResult;
}

/// <summary>
/// RF4, first state family: everything that has to SURVIVE a session boundary.
///
/// Why it is a separate object. These ten fields were static members of the Aggregator facade, written
/// from three files and read from three, and their ownership differs from every other piece of state:
///
///   * they are NOT per battle -- they exist to link one battle to the next (grouping marker) and to let
///     a late event rejoin a soft-closed session;
///   * they are NOT cleared by ResetCurrent (F9) or by StartSession: only two paths change them
///     (a session is finalised, or a session starts);
///   * `RememberEnd` runs at the TOP of a finalisation and `RememberClosed` at the very END of it (after
///     the export), because a late event must not be able to rejoin a session whose export is still being
///     written. Two methods, at two points, is how that order is kept instead of remembered.
///
/// It is game-free (BattleSession only), so the behaviour suite drives the whole Start -> End -> Start and
/// idle -> resume sequences offline: group "runtime/continuity" in tests/BehaviorTests.
/// See _dpsm_work/STATE-LIFETIME-MATRIX.md for the full per-field ownership table.
/// </summary>
internal sealed class SessionContinuity
{
	/// <summary>Most recently finalised session, so a late event that clearly belongs to it is folded back
	/// in instead of opening a fragment session.</summary>
	public BattleSession LastClosed;
	public DateTime LastClosedWall;
	public string LastClosedWhy = "";

	/// <summary>Grouping marker (1.3.3). Descriptive ONLY: it never changes a boundary, the clock or any
	/// number. See <see cref="BattleSession.RunId"/> for why the grouping exists at all.</summary>
	public long RunId;
	public int RunSeq;

	/// <summary>How the PREVIOUS session ended, so the next one can decide whether it continues the run.</summary>
	public DateTime LastEndWall;
	public bool HasEnded;
	public int LastEndQuest = int.MinValue;
	public int LastEndResult;
	public string LastEndWhy = "";

	/// <summary>
	/// A session is starting: decide whether it continues the previous run and advance the marker.
	///
	/// <paramref name="runGap"/> is passed in rather than read here, because "now" is a clock read and the
	/// facade owns every clock read (the same rule the RF3 policies follow). The caller computes it as
	/// <c>HasEnded ? (now - LastEndWall).TotalSeconds : -1.0</c>.
	/// </summary>
	public RunMarker BeginSession(double runGap, int questId, double joinSeconds)
	{
		bool continues = SessionTransitionPolicy.RunContinues(HasEnded, LastEndResult, LastEndQuest,
		                                                      questId, runGap, joinSeconds);
		SessionTransitionPolicy.NextRun(continues, ref RunId, ref RunSeq);
		return new RunMarker
		{
			RunId = RunId,
			RunSeq = RunSeq,
			RunGap = continues ? runGap : -1.0,
			PrevWhy = continues ? LastEndWhy : "",
			PrevResult = continues ? LastEndResult : 0,
		};
	}

	/// <summary>How a session ended, recorded at the TOP of the finalisation so the NEXT session can decide
	/// whether it continues this run.</summary>
	public void RememberEnd(int questId, int result, string why, DateTime now)
	{
		LastEndWall = now;
		HasEnded = true;
		LastEndQuest = questId;
		LastEndResult = result;
		LastEndWhy = why ?? "";
	}

	/// <summary>Remember a finalised session as resumable. Called at the very END of the finalisation,
	/// AFTER the export, so nothing can rejoin a session whose export is still being written.</summary>
	public void RememberClosed(BattleSession session, string why, DateTime now)
	{
		LastClosed = session;
		LastClosedWall = now;
		LastClosedWhy = why;
	}

	/// <summary>
	/// The soft-resume gate, with the container's one side effect: an EXPIRED window forgets the remembered
	/// session, while a non-idle close or an unknown actor leaves it (a later event may still rejoin).
	/// The verdict itself is <see cref="SessionTransitionPolicy.ClosedSessionGate"/>.
	/// </summary>
	public ResumeGate Gate(bool hasSession, double gapSeconds, double windowSeconds)
	{
		ResumeGate gate = SessionTransitionPolicy.ClosedSessionGate(hasSession, gapSeconds, windowSeconds,
		                                                            LastClosedWhy);
		if (gate == ResumeGate.WindowExpired) LastClosed = null;
		return gate;
	}

	/// <summary>The caller has taken the remembered session (its actors were recognised): forget it.</summary>
	public void ForgetClosed()
	{
		LastClosed = null;
	}
}
