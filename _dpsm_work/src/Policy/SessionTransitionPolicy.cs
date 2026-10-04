namespace DpsMeter;

/// <summary>Why a soft-closed session may NOT be resumed. Order matters: see
/// <see cref="SessionTransitionPolicy.ClosedSessionGate"/>.</summary>
internal enum ResumeGate
{
	/// <summary>The window and the close reason allow a resume; the caller still has to check the actors.</summary>
	Eligible = 0,
	/// <summary>Nothing is remembered.</summary>
	NoClosedSession = 1,
	/// <summary>Too late: the remembered session must be dropped (the ONLY verdict with a side effect).</summary>
	WindowExpired = 2,
	/// <summary>It was a real battle end ("end"/"teardown"/"restart"), not an idle timeout.</summary>
	NotAnIdleClose = 3,
}

/// <summary>
/// RF3: session-boundary DECISIONS as pure functions -- which sessions form one "run" (the grouping
/// marker), how the run id/sequence advances, and whether a soft-closed session may be resumed.
///
/// Why they are split out. These rules decide whether a wave/arena stage becomes 13 exports or one, and
/// whether the tail of an interrupted battle is folded back or becomes a 0.0 s fragment. Both were
/// arithmetic over DateTime reads buried in StartSession/TryResumeClosedSession, so nothing could test
/// the boundaries: now the facade reads the clock and holds the state, and this file decides.
///
/// The thresholds are constants here and are also passed in explicitly, so a test can drive the boundary
/// without editing the production value.
///</summary>
internal static class SessionTransitionPolicy
{
	/// <summary>Two sessions belong to the same run when the previous one ended WITHOUT a result, on the
	/// same quest, within this many seconds. A finished battle (result != 0) NEVER joins the next session,
	/// so a grouping marker can never silently absorb a real battle.</summary>
	public const double RunJoinSeconds = 2.0;

	/// <summary>How long after a SOFT (idle) close a late event may still rejoin its session.</summary>
	public const double ResumeWindowSeconds = 5.0;

	/// <summary>Does this session continue the previous stretch of play? The gap must be non-negative
	/// (a negative gap means "no previous session") and at most <paramref name="joinSeconds"/>.</summary>
	public static bool RunContinues(bool hasEnded, int prevResult, int prevQuest, int quest,
	                               double gapSeconds, double joinSeconds)
	{
		return hasEnded
			&& prevResult == 0                     // the previous session was NOT a finished battle
			&& prevQuest == quest                  // same quest
			&& gapSeconds >= 0.0 && gapSeconds <= joinSeconds;
	}

	/// <summary>Run id/sequence transition: continuing keeps the id and advances the sequence, otherwise a
	/// new id starts at sequence 0. Separate from the predicate so the "which marker" decision is testable
	/// without a session object.</summary>
	public static void NextRun(bool continues, ref long runId, ref int runSeq)
	{
		if (continues) runSeq++;
		else { runId++; runSeq = 0; }
	}

	/// <summary>
	/// Admission for resuming a soft-closed session, in the order the facade must evaluate it:
	/// (1) nothing remembered, (2) the window, (3) the close reason. The actor check is deliberately NOT
	/// here -- it reads native objects (GameRef.IsNull / Actors.ContainsKey) and must stay in the facade,
	/// evaluated only after these gates pass.
	///
	/// Only <see cref="ResumeGate.WindowExpired"/> means "forget the remembered session"; a non-idle close
	/// or an unknown actor leaves it remembered, which is what lets a later event still rejoin.
	/// </summary>
	public static ResumeGate ClosedSessionGate(bool hasClosedSession, double gapSeconds, double windowSeconds,
	                                          string closedWhy)
	{
		if (!hasClosedSession) return ResumeGate.NoClosedSession;
		if (gapSeconds > windowSeconds) return ResumeGate.WindowExpired;
		if (closedWhy != "idle") return ResumeGate.NotAnIdleClose;
		return ResumeGate.Eligible;
	}
}
