using System;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
	{
	/// <summary>
	/// The contribution-view cache (ContributionSession.Get). The refactor plan (section 3.3) warns that
	/// this is NOT a strict one-second throttle: the staleness test is a four-way OR, so a changed event
	/// count recomputes immediately, and the plan forbids "simplifying" the OR to an AND without a new
	/// decision. These cases pin the CURRENT rule, including the two behaviours that look like bugs and
	/// are therefore the ones a refactor would silently "fix":
	///
	///   1. a reset that refills to the SAME event count during the same second returns the OLD cache;
	///   2. the `_cacheUsedFolds != useFolds` clause is UNREACHABLE while live, because a false switch
	///      returns early with an "unavailable" view -- so `useFolds` is always true at the staleness test.
	///
	/// Both are recorded, not endorsed: RF5 changes them deliberately, behind these cases.
	/// It also drives UnityEngine.Time directly, which is the only clock the cache has.
	/// </summary>
	public static void Cache(Runner r)
	{
		r.Group("cache/unavailable");
		Aggregator.Session = null;
		ContributionSession.Invalidate();
		UnityEngine.Time.unscaledTime = 0f;
		var none = ContributionSession.Get(true);
		r.True("not-live-and-no-cache-is-unusable", !none.Usable);
		r.True("not-live-and-no-cache-is-not-live", !none.Live);
		r.Str("not-live-and-no-cache-says-no-data", none.Unavailable, "暂无战斗数据");
		r.Str("not-live-and-no-cache-folds-off-says-switch-off", ContributionSession.Get(false).Unavailable,
		      "未识别倍率:ReconcileCalc 已关闭");

		var live = new BattleSession { InBattle = true, QuestId = 411001 };
		Aggregator.Session = live;
		r.Str("live-with-no-events-says-no-events", ContributionSession.Get(true).Unavailable, "尚无伤害事件");
		var liveOff = ContributionSession.Get(false);
		r.Str("live-folds-off-says-no-folds", liveOff.Unavailable, "未识别倍率:ReconcileCalc 已关闭(没有折叠就没有归属)");
		r.True("live-folds-off-carries-no-result", liveOff.Result == null);

		r.Group("cache/live-recompute");
		live.Advance(20.0, false);
		live.Events.Add(new BattleEvent { Type = "dmg", Amount = 100, AttackerKey = 1 });
		var one = ContributionSession.Get(true);
		r.True("a-live-battle-computes", one.Usable && one.Result != null);
		r.True("the-live-view-is-marked-live", one.Live);
		r.Eq("the-live-view-carries-the-quest", one.QuestId, 411001);
		r.EqD("the-live-view-carries-the-clock", one.Seconds, 20.0);
		r.Same("a-second-call-with-the-same-event-count-is-cached", ContributionSession.Get(true).Result, one.Result);
		live.Events.Add(new BattleEvent { Type = "dmg", Amount = 50, AttackerKey = 1 });
		var two = ContributionSession.Get(true);
		r.Diff("a-new-event-recomputes-immediately (NOT a one-second throttle)", two.Result, one.Result);
		r.Same("the-recomputed-result-is-then-cached", ContributionSession.Get(true).Result, two.Result);

		r.Group("cache/reset-hazard");
		live.ResetActors();
		live.Events.Add(new BattleEvent { Type = "dmg", Amount = 7, AttackerKey = 1 });
		live.Events.Add(new BattleEvent { Type = "dmg", Amount = 8, AttackerKey = 1 });
		r.Same("same-session-refilled-to-the-same-count-returns-the-OLD-cache",
		       ContributionSession.Get(true).Result, two.Result);
		r.EqD("the-stale-cache-still-reports-the-live-clock", ContributionSession.Get(true).Seconds, 20.0);

		r.Group("cache/time-clause");
		UnityEngine.Time.unscaledTime = 1.01f;
		r.Diff("one-second-past-the-refresh-recomputes", ContributionSession.Get(true).Result, two.Result);

		r.Group("cache/history-selection");
		var held = ContributionSession.Get(true).Result;
		r.True("the-held-result-is-not-null", held != null);
		Aggregator.Session = null;
		var hist = ContributionSession.Get(true);
		r.True("the-finished-battle-view-is-usable", hist.Usable);
		r.True("the-finished-battle-view-is-not-live", !hist.Live);
		r.Same("the-finished-battle-view-returns-the-held-result", hist.Result, held);
		r.Eq("the-finished-battle-view-keeps-the-cached-quest", hist.QuestId, 411001);
		r.EqD("the-finished-battle-view-keeps-the-cached-clock", hist.Seconds, 20.0);
		r.True("the-finished-battle-view-ignores-the-folds-switch", ContributionSession.Get(false).Usable);

		r.Group("cache/invalidate");
		ContributionSession.Invalidate();
		var after = ContributionSession.Get(true);
		r.True("invalidate-drops-the-cache", !after.Usable);
		r.Str("after-invalidate-the-view-says-no-data", after.Unavailable, "暂无战斗数据");
		Aggregator.Session = null;
		ContributionSession.Invalidate();
	}
}
