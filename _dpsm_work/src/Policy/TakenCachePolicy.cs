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

	public static bool IsStale(bool sameSession, int cachedEvents, int currentEvents, double cachedAt, double now,
	                           int cachedGeneration, int currentGeneration)
	{
		return ContributionCachePolicy.IsStale(sameSession, cachedEvents, currentEvents, false, false,
		                                       cachedAt, now, RefreshSeconds, cachedGeneration, currentGeneration);
	}
}
