using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// RF5g: the RULE and LINK tables join the row model. The two tables had different rules that only
	/// existed as loop shapes: rules are filtered by a positive equivalent and capped, links are not filtered
	/// (a link exists only because something moved) but are capped the same way, and both report the FULL
	/// count on the "... 共 N" line.
	/// </summary>
	public static void ContributionRowModelTables(Runner r)
	{
		var res = new ContributionResult();
		res.Actors.Add(new ContributionActorRow { Key = 7, Name = "甲" });
		res.Actors.Add(new ContributionActorRow { Key = 9, Name = "乙" });
		// The zero-equivalent rule comes FIRST on purpose. When it was appended after the 13 positive
		// rows the cap (12) was already full by the time the loop reached it, so removing the filter
		// changed nothing and the case below stayed GREEN under its own mutation (measured 2026-10-05:
		// negative_control rowmodel-rules-keep-zero-damage did not bite). The filter is only falsifiable
		// if an unfiltered zero row would CONSUME a cap slot.
		res.Rules.Add(new ContributionRuleRow { Name = "zero", Damage = 0.0 });
		for (int i = 0; i < 13; i++)
			res.Rules.Add(new ContributionRuleRow { Name = "r" + i, Kind = "k", Side = "自", OwnerName = "甲",
			                                        Hits = i, Folds = 1, Damage = 10.0 + i });
		for (int i = 0; i < 13; i++)
			res.Links.Add(new ContributionLinkRow { From = 7, To = 9, Hits = i, Amount = 5.0 });
		res.Links.Add(new ContributionLinkRow { From = 7, To = 404, Hits = 1, Amount = 0.0 });
		var v = new ContributionTableValues();
		ContributionRowModel.BuildRules(res, v);
		ContributionRowModel.BuildLinks(res, v);

		r.Group("rowmodel/rules");
		r.Eq("the-shown-limit-is-12", ContributionRowModel.ShownLimit, 12);
		r.Eq("at-most-twelve-rules-are-shown", v.Rules.Count, 12);
		r.Eq("the-total-is-every-rule-including-the-unshown", v.RuleTotal, 14);
		r.Str("the-first-shown-rule-is-the-first-rule", v.Rules[0].Name, "r0");
		r.EqD("the-damage-comes-from-the-result", v.Rules[0].Damage, 10.0);
		r.EqD("hits-come-from-the-result", v.Rules[0].Hits, 0.0);
		r.Str("the-owner-name-travels", v.Rules[0].Owner, "甲");
		// the zero-damage rule is skipped BEFORE the cap is consumed, so it never displaces a real row
		bool anyZero = false;
		for (int i = 0; i < v.Rules.Count; i++) if (v.Rules[i].Name == "zero") anyZero = true;
		r.True("a-zero-equivalent-rule-is-not-shown", !anyZero);

		r.Group("rowmodel/links");
		r.Eq("at-most-twelve-links-are-shown", v.Links.Count, 12);
		r.Eq("the-link-total-is-every-link", v.LinkTotal, 14);
		r.Str("the-provider-is-named-from-the-actors", v.Links[0].From, "甲");
		r.Str("the-beneficiary-is-named-too", v.Links[0].To, "乙");
		r.EqD("the-amount-comes-from-the-result", v.Links[0].Amount, 5.0);
		r.Str("an-unknown-endpoint-becomes-a-hash-key", ContributionRowModel.LinkName(res, 404), "#404");
		r.Str("and-a-known-one-stays-a-name", ContributionRowModel.LinkName(res, 9), "乙");
		r.Str("a-null-result-does-not-throw", ContributionRowModel.LinkName(null, 3), "#3");

		r.Group("rowmodel/empty-tables");
		var empty = new ContributionTableValues();
		ContributionRowModel.BuildRules(new ContributionResult(), empty);
		ContributionRowModel.BuildLinks(new ContributionResult(), empty);
		r.Eq("an-empty-result-has-no-rule-rows", empty.Rules.Count, 0);
		r.Eq("and-no-link-rows", empty.Links.Count, 0);
		r.Eq("and-a-zero-total", empty.RuleTotal + empty.LinkTotal, 0);
	}
}
