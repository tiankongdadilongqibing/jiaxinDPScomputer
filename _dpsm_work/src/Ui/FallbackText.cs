namespace DpsMeter;

/// <summary>
/// RF5e (plan section 10): the IMGUI FALLBACK renderer's contribution lines.
///
/// Why this exists. The plan says the two renderers must "consume the same view, not each compute their own
/// contribution". The INVESTIGATION (round 13) found the plan's sentence is half-true and half-already-done:
///
///   * the NUMBERS were never computed twice -- OverlayCore.DrawContributionDashboard reads the very same
///     cached view through OverlayUGUI.ResolveContributionView, and says so in its own comment;
///   * but the FORMATTING was a second implementation: the fallback interpolated {x:N0}/{x:F2} directly
///     (9 sites, zero uses of DisplayFormat), while the uGUI panel and the export go through
///     DisplayFormat/InvariantCulture. A change to DisplayFormat therefore did not reach the fallback.
///
/// These two builders are the row-heavy part of that fix, and they are pure, so the behaviour suite executes
/// them: the fallback's contribution text is now pinned like the panel's.
///
/// Culture note (a deliberate, documented change): the old interpolation used the CURRENT culture, these use
/// DisplayFormat's InvariantCulture. In the shipped locales (ja/en) both print "," as the group separator,
/// so nothing visible changes; in a locale with another separator the fallback now AGREES with the panel and
/// the export instead of disagreeing with them. The fallback was the odd one out, not the reference.
/// </summary>
internal static class FallbackText
{
	/// <summary>One actor line of the fallback's contribution dashboard. 1.7.0 (phase F).</summary>
	public static string ContributionActorLine(string name, bool summon, double total, double sharePct,
	                                           double baseCredit, double self, double assist, double received)
	{
		return "  " + (name ?? "") + (summon ? "[使魔]" : "")
		     + "  总贡献 " + DisplayFormat.Fmt(total) + "(" + DisplayFormat.Pct(sharePct) + ")"
		     + "  自身 " + DisplayFormat.Fmt(baseCredit + self)
		     + "(基础 " + DisplayFormat.Fmt(baseCredit) + " + 自身规则 " + DisplayFormat.Fmt(self) + ")"
		     + "  他人因你 " + DisplayFormat.Fmt(assist)
		     + "  被队友分走 " + DisplayFormat.Fmt(received);
	}

	/// <summary>The fallback's totals line. The un-attributed share is printed as a percentage of the same
	/// total the panel divides by, so the two renderers cannot disagree about what "未归因" means.</summary>
	public static string ContributionTotalsLine(double attributed, double unattributed, double unattrPct,
	                                            double hits)
	{
		return "  合计 " + DisplayFormat.Fmt(attributed)
		     + "   未归因 " + DisplayFormat.Fmt(unattributed) + "(" + DisplayFormat.Pct(unattrPct) + ")"
		     + "   命中 " + DisplayFormat.Fmt(hits);
	}

	/// <summary>
	/// R54 (user request): the Chinese label of ONE unattributed reason. ONE table for both renderers, so the
	/// panel and the fallback can never name the same reason differently.
	///
	/// WHY THE GRANTED FAMILY IS SEPARATE. 「阻挡增伤」 (the granted 被伤害 modifiers) is the channel whose
	/// PROVIDER is unresolved -- it is a named rule with a missing owner, not an unknown kind of rule. Printing
	/// it inside one anonymous 未归因 total is what made the user misread a 4.2% residual as "something
	/// unexplained" (2026-10-05). MEASURED the same day: the granted channel produced 0-5,862 unresolved folds
	/// depending on whether the giver probe happened to answer, so the family has to be visible on its own.
	/// </summary>
	public static string UnattributedReasonLabel(string reason)
	{
		switch (reason)
		{
			case "given_carrier_ambiguous": return "阻挡增伤(多个候选,未确认)";
			case "given_carrier_one":       return "阻挡增伤(唯一候选,未确认)";
			case "given_carrier_none":      return "阻挡增伤(无可读候选)";
			case "byUnit_unknown":          return "提供者名称不可识别";
			case "byUnit_ambiguous":        return "提供者同名歧义";
			case "byUnit_outside":          return "提供者在队伍之外";
			case "ambiguous_multi_holder":  return "技能持有者歧义";
			case "global_ambiguous":        return "全局规则持有者歧义";
			case "unknown_kind":            return "种类未识别";
			default: return reason ?? "";
		}
	}

	/// <summary>R54: one line per unresolved reason, so the residual reads as families instead of a total.</summary>
	public static string UnattributedBreakdownLine(string reason, double amount, int folds)
	{
		return "    " + UnattributedReasonLabel(reason) + " " + DisplayFormat.Fmt(amount)
		     + "  " + DisplayFormat.Num(folds) + " 折";
	}
}
