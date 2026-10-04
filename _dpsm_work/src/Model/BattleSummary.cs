using System.Collections.Generic;

namespace DpsMeter;

public sealed class BattleSummary
{
	public string QuestId = "";

	public string Result = "";

	public double DurationSeconds;

	public int ActorCount;

	public long TotalDealt;

	public long TotalTaken;

	public long TotalHealing;

	public readonly List<ActorStats> Actors = new List<ActorStats>();

	/// <summary>The session this summary was built from. Kept so a session that gets resumed after a
	/// soft (idle) close can withdraw its own stale summary (see Aggregator.TryResumeClosedSession).</summary>
	public BattleSession Session;

	/// <summary>Per-hit event log kept for the in-game detail view of the finished battle.</summary>
	public readonly List<BattleEvent> Events = new List<BattleEvent>();
}
