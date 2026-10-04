namespace DpsMeter;

/// <summary>Why the overlay has no contribution table to show. The TEXT lives with the decision so the
/// four strings have one definition each (the live and history paths word the switch-off case
/// differently on purpose: only the live one can add "没有折叠就没有归属").</summary>
internal enum CacheUnavailable
{
	None = 0,
	/// <summary>Live, but ReconcileCalc is off -- so there is no fold data to attribute with.</summary>
	LiveNoFolds = 1,
	/// <summary>Live, but no damage event has been recorded yet.</summary>
	LiveNoEvents = 2,
	/// <summary>Not in a battle, nothing cached, folds available.</summary>
	HistoryNone = 3,
	/// <summary>Not in a battle, nothing cached, folds unavailable.</summary>
	HistoryNoneNoFolds = 4,
}

/// <summary>Which battle the view describes.</summary>
internal enum ViewSource
{
	/// <summary>Nothing to show.</summary>
	None = 0,
	/// <summary>The battle in progress.</summary>
	Live = 1,
	/// <summary>The last computed result, labelled as the previous battle.</summary>
	History = 2,
}

/// <summary>
/// RF5: the contribution view's CACHE DECISIONS, as pure functions.
///
/// Why. The dashboard recomputes the whole battle (O(hits x folds), ~30k operations) at most once a
/// second, and the rule that decides "recompute or reuse" is a four-way OR buried in the middle of
/// ContributionSession.Get. Two of its properties are counter-intuitive and were pinned by tests before
/// this file existed:
///
///   1. it is NOT a one-second throttle -- a changed event count recomputes IMMEDIATELY, so a busy hit
///      stream can recompute many times per second;
///   2. the `cachedUsedFolds != useFolds` clause cannot fire from the facade at all, because Get()
///      returns an "unavailable" view before reaching the test whenever useFolds is false, and the
///      cached flag is only ever written as true.
///
/// Both are recorded here as the CURRENT rule. Changing either is a behaviour decision, not a refactor:
/// see _dpsm_work/CACHE-SEMANTICS-ADR.md for the options and what each would cost.
/// </summary>
internal static class ContributionCachePolicy
{
	/// <summary>How long a computed result may be reused without the event count moving.</summary>
	public const double RefreshSeconds = 1.0;

	/// <summary>
	/// Should the result be recomputed? The four clauses are the production ones, in the production order,
	/// and the last comparison is STRICT `>`: an age exactly at the refresh interval is still reused (the
	/// boundary has a test, including the +1 ulp side).
	///
	/// <paramref name="sameSession"/> is passed in rather than compared here, because session identity is a
	/// reference comparison against whatever the facade currently holds.
	/// </summary>
	public static bool IsStale(bool sameSession, int cachedEvents, int currentEvents, bool cachedUsedFolds,
	                           bool useFolds, double cachedAt, double now, double refreshSeconds)
	{
		return !sameSession
			|| cachedEvents != currentEvents
			|| cachedUsedFolds != useFolds
			|| now - cachedAt > refreshSeconds;
	}

	/// <summary>Which battle the view should describe: the live one if there is one, else the last computed
	/// result (clearly labelled), else nothing.</summary>
	public static ViewSource SelectSource(bool live, bool hasCache)
	{
		if (live) return ViewSource.Live;
		return hasCache ? ViewSource.History : ViewSource.None;
	}

	/// <summary>The user-visible reason for an unusable view. One definition per string.</summary>
	public static string ReasonText(CacheUnavailable reason)
	{
		switch (reason)
		{
			case CacheUnavailable.LiveNoFolds: return "未识别倍率:ReconcileCalc 已关闭(没有折叠就没有归属)";
			case CacheUnavailable.LiveNoEvents: return "尚无伤害事件";
			case CacheUnavailable.HistoryNoneNoFolds: return "未识别倍率:ReconcileCalc 已关闭";
			case CacheUnavailable.HistoryNone: return "暂无战斗数据";
			default: return "";
		}
	}
}
