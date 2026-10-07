namespace DpsMeter;

/// <summary>
/// R79: the staleness rule of the 受击来源拆分 projection.
///
/// Same five-way rule as the contribution board -- deliberately ONE definition
/// (<see cref="ContributionCachePolicy.IsStale"/>) instead of a second copy, because two implementations of
/// "when is a session projection stale" would drift. Pinned to this board's inputs: it has no fold switch,
/// so those two terms are constants here.
/// </summary>
internal static class TakenCachePolicy
{
	/// <summary>Live refresh throttle, shared with the contribution board.</summary>
	public const double RefreshSeconds = ContributionCachePolicy.RefreshSeconds;

	/// <summary>
	/// R80: `live` says whether the session is still being fought. The one-second throttle only makes sense
	/// for a battle in progress -- its event list grows, so the projection is worth recomputing now and then
	/// even when no new hit arrived. A FINISHED battle's event list can never change again, so re-deriving it
	/// once a second would be pure waste: for those, only a content change (a different session, more events
	/// than the cache saw, or a bumped generation) makes it stale. The throttle is expressed as "no throttle"
	/// by handing the shared rule an interval no elapsed time can exceed, NOT by pretending the cache is
	/// fresh, so the other four terms keep their meaning.
	/// </summary>
	public static bool IsStale(bool sameSession, int cachedEvents, int currentEvents, bool live, double cachedAt,
	                           double now, int cachedGeneration, int currentGeneration)
	{
		return ContributionCachePolicy.IsStale(sameSession, cachedEvents, currentEvents, false, false,
		                                       cachedAt, now, live ? RefreshSeconds : double.MaxValue,
		                                       cachedGeneration, currentGeneration);
	}
}
