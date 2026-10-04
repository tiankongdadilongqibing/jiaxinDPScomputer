using System;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// RF5e: the IMGUI fallback's contribution lines, executed as production code. The investigation that
	/// prompted this (round 13) found the plan's "the two renderers must not each compute their own
	/// contribution" was half-true: the NUMBERS were already shared through ResolveContributionView, but the
	/// FORMATTING was a second implementation (9 direct :N0/:F2 sites, no DisplayFormat use).
	/// </summary>
	public static void FallbackTextCases(Runner r)
	{
		r.Group("fallback/contribution");
		r.Str("the-actor-line-is-verbatim", FallbackText.ContributionActorLine("角色名", false, 1234.0, 12.34, 1000.0, 200.0, 30.0, 4.0),
		      "  角色名  总贡献 1,234(12.34%)  自身 1,200(基础 1,000 + 自身规则 200)  他人因你 30  被队友分走 4");
		r.Str("the-summon-marker-is-a-suffix", FallbackText.ContributionActorLine("名", true, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0),
		      "  名[使魔]  总贡献 0(0.00%)  自身 0(基础 0 + 自身规则 0)  他人因你 0  被队友分走 0");
		r.Str("a-null-name-is-just-empty", FallbackText.ContributionActorLine(null, false, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0),
		      "    总贡献 0(0.00%)  自身 0(基础 0 + 自身规则 0)  他人因你 0  被队友分走 0");
		// The fallback does NOT truncate: it is a wrapped GUILayout label, not a fixed-column table.
		string longName = "あああああああああああああああ";
		r.True("a-long-name-is-not-truncated", FallbackText.ContributionActorLine(longName, false, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0).Contains(longName));
		r.Str("the-totals-line-is-verbatim", FallbackText.ContributionTotalsLine(1234.0, 56.0, 4.34, 99.0),
		      "  合计 1,234   未归因 56(4.34%)   命中 99");
		// The point of the round: the fallback's numbers are the SAME strings the panel and the export use.
		r.True("the-actor-line-uses-the-shared-total-format",
		       FallbackText.ContributionActorLine("n", false, 1234567.0, 0.0, 0.0, 0.0, 0.0, 0.0)
		           .Contains(DisplayFormat.Fmt(1234567.0)));
		r.True("the-totals-line-uses-the-shared-count-format",
		       FallbackText.ContributionTotalsLine(0.0, 0.0, 0.0, 1234567.0).Contains(DisplayFormat.Num(1234567L)));
		r.True("the-totals-line-uses-the-shared-percent-format",
		       FallbackText.ContributionTotalsLine(0.0, 0.0, 12.345, 0.0).Contains(DisplayFormat.Pct(12.345)));
		r.True("an-unknown-total-prints-as-zero-not-NaN",
		       FallbackText.ContributionActorLine("n", false, double.NaN, double.NaN, 0.0, 0.0, 0.0, 0.0).Contains("0(0.00%)"));
		r.Eq("the-panel-and-the-fallback-agree-on-a-large-number",
		      ContributionColumns.T1Row("n", false, 1234567.0, 100.0, 0.0, 0.0, 0.0, 0.0, 0.0).Contains(DisplayFormat.Fmt(1234567.0)) ? 1 : 0, 1);
	}
}
