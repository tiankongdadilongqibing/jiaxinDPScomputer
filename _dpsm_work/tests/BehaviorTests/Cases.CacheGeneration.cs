using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// Round 41: the user's cache decisions, as rules.
	///   * the throttle is one second, strict `>`, and it is the ONLY time clause (an unchanged panel is not
	///     recomputed just because time passed);
	///   * a F9 reset must be visible even when the event count is unchanged, so the rule compares a data
	///     GENERATION as well - counting events cannot see a reset that leaves the count where it was.
	/// </summary>
	public static void CacheGenerationCases(Runner r)
	{
		r.Group("cache/throttle");
		r.True("an-unchanged-panel-inside-one-second-is-reused",
		       !ContributionCachePolicy.IsStale(true, 5, 5, true, true, 10.0, 10.5, 1.0));
		r.True("exactly-at-one-second-is-still-reused (strict >)",
		       !ContributionCachePolicy.IsStale(true, 5, 5, true, true, 10.0, 11.0, 1.0));
		r.True("one-second-and-a-bit-recomputes",
		       ContributionCachePolicy.IsStale(true, 5, 5, true, true, 10.0, 11.001, 1.0));
		r.True("a-new-event-recomputes-immediately",
		       ContributionCachePolicy.IsStale(true, 5, 6, true, true, 10.0, 10.01, 1.0));
		r.True("a-battle-switch-recomputes-immediately",
		       ContributionCachePolicy.IsStale(false, 5, 5, true, true, 10.0, 10.01, 1.0));
		r.True("toggling-the-folds-switch-recomputes-immediately",
		       ContributionCachePolicy.IsStale(true, 5, 5, true, false, 10.0, 10.01, 1.0));

		r.Group("cache/generation");
		r.True("a-fresh-cache-in-the-same-generation-is-reused",
		       !ContributionCachePolicy.IsStale(true, 5, 5, true, true, 10.0, 10.2, 1.0, 7, 7));
		// THE point of the round: identical event count, different generation -> the pre-reset numbers must go
		r.True("a-new-generation-is-stale-even-with-the-same-event-count",
		       ContributionCachePolicy.IsStale(true, 5, 5, true, true, 10.0, 10.2, 1.0, 7, 8));
		r.True("and-it-is-stale-even-within-the-throttle-window",
		       ContributionCachePolicy.IsStale(true, 5, 5, true, true, 10.0, 10.0001, 1.0, 7, 8));
		r.Eq("the-generation-is-not-mistaken-for-an-event-count",
		     ContributionCachePolicy.IsStale(true, 7, 7, true, true, 10.0, 10.1, 1.0, 5, 7) ? 1 : 0, 1);

		// the facade half: a reset bumps the generation, so the panel cannot reuse the pre-reset result
		int before = ContributionSession.Generation;
		ContributionSession.Invalidate();
		r.Eq("invalidate-bumps-the-generation-by-one", ContributionSession.Generation - before, 1);
		ContributionSession.Invalidate();
		r.Eq("and-every-invalidate-bumps-it-again", ContributionSession.Generation - before, 2);
	}
}
