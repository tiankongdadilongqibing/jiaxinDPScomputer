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

/// <summary>R55 (user request): one row of the PENDING granted-channel table -- a CANDIDATE carrier, not
/// a provider. Nothing in this struct is credited anywhere; it exists so the 「阻挡增伤」 pool can be read
/// with the same eye as the character table it is printed under.</summary>
internal struct ContributionPendingValues
{
	public string Name;
	public double Amount;
	public double Share;
	public double Folds;
}

/// <summary>The pending granted-channel table plus its totals and the captions the header needs.</summary>
internal sealed class ContributionPendingTable
{
	public readonly List<ContributionPendingValues> Rows = new List<ContributionPendingValues>();
	/// <summary>The distinct rule labels of the pooled groups, in first-seen order.</summary>
	public readonly List<string> Labels = new List<string>();
	/// <summary>Each ambiguous group's candidate list, verbatim -- the row caption can only carry a COUNT.</summary>
	public readonly List<string> Ambiguous = new List<string>();
	public double Total;
	public double Share;
	public double Folds;
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

	/// <summary>R55: the caption of a row whose candidates are NOT unique. It carries the COUNT, never a
	/// name picked out of the set -- picking one is the wrong answer R54 removed. Public so a test can pin
	/// the exact wording the panel shows.</summary>
	public const string AmbiguousNamePrefix = "候选";

	/// <summary>
	/// R55 (user request): turn the census' granted-channel groups into the rows of the PENDING table.
	///
	/// Input rule: ONLY a group WITH a carrier verdict is a pending row. The verdict is recorded for the
	/// granted channel alone (Contribution.NoteUnresolved), so this filter is what keeps a byUnit_unknown
	/// or a madness fold out of a table whose whole promise is "this is the blocked-damage family".
	///
	/// Aggregation: by (verdict, name), so the several copies of one rule that share a unique holder
	/// collapse into ONE row for that holder -- which is what makes the table read as "who would get it".
	/// An ambiguous group is one row labelled with its candidate COUNT, and its names travel in
	/// <see cref="ContributionPendingTable.Ambiguous"/> for the note line.
	///
	/// Pure: it reads the result and returns a table; it cannot move a single credit, and a test pins that.
	/// </summary>
	public static ContributionPendingTable BuildPending(ContributionResult res, double total)
	{
		var t = new ContributionPendingTable();
		if (res == null || res.Unresolved == null) return t;
		var slot = new Dictionary<string, int>();
		for (int i = 0; i < res.Unresolved.Count; i++)
		{
			ContributionUnresolvedRow u = res.Unresolved[i];
			if (u == null || string.IsNullOrEmpty(u.CarrierVerdict)) continue;
			string id, name;
			if (u.CarrierVerdict == "unique")
			{
				name = string.IsNullOrEmpty(u.CarrierNames) ? "(候选名不可读)" : u.CarrierNames;
				id = "u|" + name;
			}
			else if (u.CarrierVerdict == "ambiguous")
			{
				name = AmbiguousNamePrefix + u.CarrierCount + "人";
				id = "a|" + name;
				if (!string.IsNullOrEmpty(u.CarrierNames) && !t.Ambiguous.Contains(u.CarrierNames))
					t.Ambiguous.Add(u.CarrierNames);
			}
			else
			{
				name = "(无可读候选)";
				id = "n|" + name;
			}
			if (!string.IsNullOrEmpty(u.Label) && !t.Labels.Contains(u.Label)) t.Labels.Add(u.Label);
			int at;
			if (!slot.TryGetValue(id, out at))
			{
				at = t.Rows.Count;
				slot[id] = at;
				t.Rows.Add(new ContributionPendingValues { Name = name });
			}
			ContributionPendingValues row = t.Rows[at];
			row.Amount += u.Amount;
			row.Folds += u.Folds;
			t.Rows[at] = row;
		}
		// Deterministic order (amount desc, then the name): two runs over one battle cannot reorder the page.
		t.Rows.Sort((x, y) => x.Amount != y.Amount
			? (x.Amount < y.Amount ? 1 : -1)
			: string.CompareOrdinal(x.Name, y.Name));
		for (int i = 0; i < t.Rows.Count; i++)
		{
			ContributionPendingValues row = t.Rows[i];
			row.Share = total > 0.0 ? 100.0 * row.Amount / total : 0.0;
			t.Rows[i] = row;
			t.Total += row.Amount;
			t.Folds += row.Folds;
		}
		t.Share = total > 0.0 ? 100.0 * t.Total / total : 0.0;
		return t;
	}
}
