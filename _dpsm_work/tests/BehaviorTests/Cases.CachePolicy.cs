using System;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// RF5: the contribution view's cache decisions. The facade-level behaviour was already pinned by the
	/// "cache/" group (which must keep passing unchanged -- that is the evidence this extraction preserved
	/// it); these cases pin the DECISIONS themselves, including the two counter-intuitive properties the
	/// ADR records: it is not a throttle, and the folds clause is unreachable from the facade.
	/// </summary>
	public static void CachePolicy(Runner r)
	{
		const bool Same = true, Other = false, FoldsOn = true, FoldsOff = false;
		const bool CachedFoldsOn = true;

		r.Group("policy/cache");
		r.EqD("refresh-seconds-is-1", ContributionCachePolicy.RefreshSeconds, 1.0);

		// The reuse case: same session, same count, same flag, half a second old.
		r.True("an-unchanged-cache-is-reused",
		       !ContributionCachePolicy.IsStale(Same, 5, 5, CachedFoldsOn, FoldsOn, 100.0, 100.5, 1.0));
		r.True("a-different-session-is-stale",
		       ContributionCachePolicy.IsStale(Other, 5, 5, CachedFoldsOn, FoldsOn, 100.0, 100.5, 1.0));
		r.True("a-changed-event-count-is-stale",
		       ContributionCachePolicy.IsStale(Same, 5, 6, CachedFoldsOn, FoldsOn, 100.0, 100.0, 1.0));
		r.True("a-folds-flag-change-is-stale",
		       ContributionCachePolicy.IsStale(Same, 5, 5, CachedFoldsOn, FoldsOff, 100.0, 100.0, 1.0));
		r.True("an-old-cache-is-stale",
		       ContributionCachePolicy.IsStale(Same, 5, 5, CachedFoldsOn, FoldsOn, 100.0, 101.5, 1.0));
		// strict `>`: exactly at the interval is still a reuse
		r.True("elapsed-exactly-at-the-refresh-is-not-stale",
		       !ContributionCachePolicy.IsStale(Same, 5, 5, CachedFoldsOn, FoldsOn, 100.0, 101.0, 1.0));
		// (+1 ulp must be tested where it SURVIVES the subtraction: 100.0 + BitIncrement(1.0) rounds back
		// to exactly 101.0 at that magnitude, which is a reuse -- measured here after the case failed.)
		r.True("one-ulp-past-the-refresh-is-stale",
		       ContributionCachePolicy.IsStale(Same, 5, 5, CachedFoldsOn, FoldsOn, 0.0,
		                                      Math.BitIncrement(1.0), 1.0));
		r.True("exactly-the-refresh-from-zero-is-not-stale",
		       !ContributionCachePolicy.IsStale(Same, 5, 5, CachedFoldsOn, FoldsOn, 0.0, 1.0, 1.0));

		// NOT a throttle: a moved event count recomputes with a ZERO age.
		r.True("a-count-change-recomputes-with-no-delay",
		       ContributionCachePolicy.IsStale(Same, 5, 6, CachedFoldsOn, FoldsOn, 100.0, 100.0, 1.0));
		r.True("an-unchanged-count-at-zero-age-does-not",
		       !ContributionCachePolicy.IsStale(Same, 5, 5, CachedFoldsOn, FoldsOn, 100.0, 100.0, 1.0));

		// Each clause is independently sufficient (they are OR-ed, in this order).
		r.True("clause-1-alone-fires", ContributionCachePolicy.IsStale(Other, 5, 5, true, true, 0.0, 0.0, 1.0));
		r.True("clause-2-alone-fires", ContributionCachePolicy.IsStale(true, 5, 6, true, true, 0.0, 0.0, 1.0));
		r.True("clause-3-alone-fires", ContributionCachePolicy.IsStale(true, 5, 5, true, false, 0.0, 0.0, 1.0));
		r.True("clause-4-alone-fires", ContributionCachePolicy.IsStale(true, 5, 5, true, true, 0.0, 2.0, 1.0));
		// The documented UNREACHABLE case: clause 3 fires here, but the facade returns an "unavailable" view
		// before it ever reaches the staleness test when useFolds is false (see the ADR). Pinned so the
		// unreachability is a recorded fact with a test behind it rather than a comment.
		r.True("the-folds-clause-fires-if-it-is-ever-reached",
		       ContributionCachePolicy.IsStale(true, 5, 5, true, false, 0.0, 0.0, 1.0));

		r.Group("policy/cache-source");
		r.Eq("a-live-battle-is-the-live-source", (int)ContributionCachePolicy.SelectSource(true, false), (int)ViewSource.Live);
		r.Eq("a-live-battle-wins-over-a-cache", (int)ContributionCachePolicy.SelectSource(true, true), (int)ViewSource.Live);
		r.Eq("no-battle-with-a-cache-is-history", (int)ContributionCachePolicy.SelectSource(false, true), (int)ViewSource.History);
		r.Eq("no-battle-and-no-cache-is-nothing", (int)ContributionCachePolicy.SelectSource(false, false), (int)ViewSource.None);

		r.Group("policy/cache-reason");
		r.Str("live-with-folds-off-explains-the-switch",
		       ContributionCachePolicy.ReasonText(CacheUnavailable.LiveNoFolds),
		       "未识别倍率:ReconcileCalc 已关闭(没有折叠就没有归属)");
		r.Str("live-with-no-events-says-so",
		       ContributionCachePolicy.ReasonText(CacheUnavailable.LiveNoEvents), "尚无伤害事件");
		r.Str("history-with-folds-on-says-no-data",
		       ContributionCachePolicy.ReasonText(CacheUnavailable.HistoryNone), "暂无战斗数据");
		r.Str("history-with-folds-off-is-the-short-form",
		       ContributionCachePolicy.ReasonText(CacheUnavailable.HistoryNoneNoFolds),
		       "未识别倍率:ReconcileCalc 已关闭");
		r.Str("no-reason-is-an-empty-string", ContributionCachePolicy.ReasonText(CacheUnavailable.None), "");
	}
}
