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

/// <summary>The contribution table's row values plus the four sums its footer prints.</summary>
internal sealed class ContributionTableValues
{
	public readonly List<ContributionActorValues> Rows = new List<ContributionActorValues>();
	public double SumBase;
	public double SumSelf;
	public double SumAssist;
	public double SumReceived;
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
}
