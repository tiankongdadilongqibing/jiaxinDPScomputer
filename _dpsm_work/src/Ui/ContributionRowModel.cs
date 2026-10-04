using System.Collections.Generic;

namespace DpsMeter;

/// <summary>One actor's values in the contribution table, with both shares already computed. The text is
/// ContributionColumns.T1Row's job; this struct carries only the numbers and the two labels.</summary>
internal struct ContributionActorValues
{
	public string Name;
	public bool Summon;
	public double Total;
	/// <summary>percent of the SAME total the panel divides by (stats.analyzable).</summary>
	public double Share;
	public double BaseAndSelf;
	public double Assist;
	public double Received;
	/// <summary>percent of directly dealt damage.</summary>
	public double DirectShare;
	public double Hits;
}

/// <summary>One rule row's values (table 2).</summary>
internal struct ContributionRuleValues
{
	public string Name;
	public string Kind;
	public string Side;
	public string Owner;
	public double Hits;
	public double Folds;
	public double Damage;
}

/// <summary>One relation row's values (table 3), with both endpoint names already resolved.</summary>
internal struct ContributionLinkValues
{
	public string From;
	public string To;
	public double Hits;
	public double Amount;
}

/// <summary>The contribution table's row values plus the four sums its footer prints.</summary>
internal sealed class ContributionTableValues
{
	public readonly List<ContributionActorValues> Rows = new List<ContributionActorValues>();
	public double SumBase;
	public double SumSelf;
	public double SumAssist;
	public double SumReceived;
	public readonly List<ContributionRuleValues> Rules = new List<ContributionRuleValues>();
	public int RuleTotal;
	public readonly List<ContributionLinkValues> Links = new List<ContributionLinkValues>();
	public int LinkTotal;
}

/// <summary>
/// RF5f (plan section 10, the RowViewModel half): the contribution table's VALUES, separated from the
/// renderer. The renderer used to walk the result twice -- once to emit the visible rows, once to sum the
/// footer -- which is exactly the shape in which "the shown rows" and "the summed rows" drift apart.
///
/// Two rules that this file makes explicit, both pinned by tests:
///   * an actor is SHOWN when it contributed anything (total or direct above zero);
///   * the four SUMS cover every actor, including the ones not shown, because the footer reconciles with
///     the export's totals rather than with the visible rows.
/// The sums are accumulated in the same order as before (actor order) so the floating-point result is
/// bit-identical; there is deliberately no reordering.
/// </summary>
internal static class ContributionRowModel
{
	public static ContributionTableValues Build(ContributionResult res, double total)
	{
		var v = new ContributionTableValues();
		if (res == null || res.Actors == null) return v;
		for (int i = 0; i < res.Actors.Count; i++)
		{
			ContributionActorRow a = res.Actors[i];
			if (a == null) continue;
			// the sums first: they cover ALL actors (see the class comment), in actor order
			v.SumBase += a.Base;
			v.SumSelf += a.Self;
			v.SumAssist += a.Assist;
			v.SumReceived += a.Received;
			if (a.Total <= 0.0 && a.Direct <= 0.0) continue;
			v.Rows.Add(new ContributionActorValues
			{
				Name = a.Name,
				Summon = a.Summon,
				Total = a.Total,
				Share = total > 0.0 ? 100.0 * a.Total / total : 0.0,
				BaseAndSelf = a.Base + a.Self,
				Assist = a.Assist,
				Received = a.Received,
				DirectShare = total > 0.0 ? 100.0 * a.Direct / total : 0.0,
				Hits = a.Hits,
			});
		}
		return v;
	}

	/// <summary>How many rows of the rule and link tables are shown before the "... 共 N 条" line. Named
	/// because it decides what the user sees, and it was a bare 12 in two loops.</summary>
	public const int ShownLimit = 12;

	/// <summary>
	/// Table 2 (rule equivalents): rules with a positive equivalent, capped at ShownLimit. <see
	/// cref="ContributionTableValues.RuleTotal"/> is the FULL rule count, because the overflow line reports
	/// the total rather than the remainder.
	/// </summary>
	public static void BuildRules(ContributionResult res, ContributionTableValues v)
	{
		if (res == null || res.Rules == null) return;
		v.RuleTotal = res.Rules.Count;
		for (int i = 0; i < res.Rules.Count && v.Rules.Count < ShownLimit; i++)
		{
			ContributionRuleRow rr = res.Rules[i];
			if (rr == null || rr.Damage <= 0.0) continue;
			v.Rules.Add(new ContributionRuleValues
			{
				Name = rr.Name, Kind = rr.Kind, Side = rr.Side, Owner = rr.OwnerName,
				Hits = rr.Hits, Folds = rr.Folds, Damage = rr.Damage,
			});
		}
	}

	/// <summary>Table 3 (relations): every link up to ShownLimit, with both endpoints NAMED. There is no
	/// value filter here -- unlike the rules table -- because a link only exists when something moved.</summary>
	public static void BuildLinks(ContributionResult res, ContributionTableValues v)
	{
		if (res == null || res.Links == null) return;
		v.LinkTotal = res.Links.Count;
		for (int i = 0; i < res.Links.Count && v.Links.Count < ShownLimit; i++)
		{
			ContributionLinkRow l = res.Links[i];
			if (l == null) continue;
			v.Links.Add(new ContributionLinkValues
			{
				From = LinkName(res, l.From), To = LinkName(res, l.To), Hits = l.Hits, Amount = l.Amount,
			});
		}
	}

	/// <summary>The display name of a link endpoint: the actor's own name when the key is known, else
		/// "#key" (a link can name a unit the result does not list).</summary>
	public static string LinkName(ContributionResult res, int key)
	{
		if (res != null && res.Actors != null)
			for (int i = 0; i < res.Actors.Count; i++)
				if (res.Actors[i] != null && res.Actors[i].Key == key) return res.Actors[i].Name;
		return "#" + key;
	}
}
