using System;
using System.Text;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// RF5c: the column definition of the three overlay tables. The definition is pinned by literals here;
	/// the header/totals builders are then checked by SLICING their output at the pinned widths, which is
	/// what proves the builders actually use the definition (rather than agreeing with a copy of it).
	/// The Python layout guard cross-checks the same definition and the renderer's inline row widths.
	/// </summary>
	public static void Columns(Runner r)
	{
		r.Group("columns/spec");
		r.Str("t1-labels", Labels(ContributionColumns.T1), "角色,总贡献,占比,自身,他人因你,被队友分走,直接占比,命中");
		r.Str("t1-widths", Widths(ContributionColumns.T1), "16,11,8,11,11,11,9,6");
		r.Str("t2-labels", Labels(ContributionColumns.T2), "规则,通道,侧,持有者,命中,折叠,当量");
		r.Str("t2-widths", Widths(ContributionColumns.T2), "22,8,5,14,7,7,12");
		r.Str("t3-labels", Labels(ContributionColumns.T3), "提供者,→,受益者,命中,当量");
		r.Str("t3-widths", Widths(ContributionColumns.T3), "14,4,14,7,12");
		r.Eq("t1-columns-add-up-to-83", ContributionColumns.WidthOf(ContributionColumns.T1), 83);
		r.Eq("t2-columns-add-up-to-75", ContributionColumns.WidthOf(ContributionColumns.T2), 75);
		r.Eq("t3-columns-add-up-to-51", ContributionColumns.WidthOf(ContributionColumns.T3), 51);
		// the LINE width is the columns plus the two leading spaces, and it is the number the layout guard
		// pins for the exported tables
		r.Eq("t1-line-width-is-85", ContributionColumns.T1LineWidth, ContributionColumns.WidthOf(ContributionColumns.T1) + 2);
		r.Eq("t2-line-width-is-77", ContributionColumns.T2LineWidth, ContributionColumns.WidthOf(ContributionColumns.T2) + 2);
		r.Eq("t3-line-width-is-53", ContributionColumns.T3LineWidth, ContributionColumns.WidthOf(ContributionColumns.T3) + 2);
		// the name column is left-aligned, every numeric column is right-aligned
		r.Str("t1-alignment", Alignments(ContributionColumns.T1), "L,R,R,R,R,R,R,R");
		r.Str("t2-alignment", Alignments(ContributionColumns.T2), "L,L,L,L,R,R,R");
		r.Str("t3-alignment", Alignments(ContributionColumns.T3), "L,L,L,R,R");

		r.Group("columns/header");
		r.Eq("the-t1-header-is-85-wide", DisplayFormat.DispWidth(ContributionColumns.HeaderLine(ContributionColumns.T1)), ContributionColumns.T1LineWidth);
		r.Eq("the-t2-header-is-77-wide", DisplayFormat.DispWidth(ContributionColumns.HeaderLine(ContributionColumns.T2)), ContributionColumns.T2LineWidth);
		r.Eq("the-t3-header-is-53-wide", DisplayFormat.DispWidth(ContributionColumns.HeaderLine(ContributionColumns.T3)), ContributionColumns.T3LineWidth);
		r.Str("each-t1-slice-is-its-padded-label", SliceCheck(ContributionColumns.T1), "ok");
		r.Str("each-t2-slice-is-its-padded-label", SliceCheck(ContributionColumns.T2), "ok");
		r.Str("each-t3-slice-is-its-padded-label", SliceCheck(ContributionColumns.T3), "ok");
		// Hand-assembled from the definition: 2 + PadR(提供者,14) + PadR(→,4) + PadR(受益者,14)
		// + PadL(命中,7) + PadL(当量,12). Written as fragments so each column is auditable.
		r.Str("the-t3-header-verbatim", ContributionColumns.HeaderLine(ContributionColumns.T3),
		      "  " + "提供者" + new string(' ', 8) + "→" + new string(' ', 3)
		      + "受益者" + new string(' ', 8) + new string(' ', 3) + "命中" + new string(' ', 8) + "当量");

		r.Group("columns/totals");
		// An empty share column is 8 columns and an empty 命中 column is 6: the totals row keeps the same
		// geometry as the header (85 columns) so the columns stay under their labels.
		r.Eq("the-totals-row-is-85-wide",
		      DisplayFormat.DispWidth(ContributionColumns.T1TotalsLine(1000.0, 600.0, 300.0, 100.0)),
		      ContributionColumns.T1LineWidth);
		r.Str("the-totals-row-verbatim", ContributionColumns.T1TotalsLine(1000.0, 600.0, 300.0, 100.0),
		      "  合计            " + new string(' ', 6) + "1,000" + new string(' ', 8)
		      + new string(' ', 8) + "600" + new string(' ', 8) + "300" + new string(' ', 8) + "100"
		      + new string(' ', 9) + new string(' ', 6));
	}

	private static string Labels(ColumnSpec[] cols)
	{
		var sb = new StringBuilder();
		for (int i = 0; i < cols.Length; i++) { if (i > 0) sb.Append(','); sb.Append(cols[i].Label); }
		return sb.ToString();
	}

	private static string Widths(ColumnSpec[] cols)
	{
		var sb = new StringBuilder();
		for (int i = 0; i < cols.Length; i++) { if (i > 0) sb.Append(','); sb.Append(cols[i].Width); }
		return sb.ToString();
	}

	private static string Alignments(ColumnSpec[] cols)
	{
		var sb = new StringBuilder();
		for (int i = 0; i < cols.Length; i++) { if (i > 0) sb.Append(','); sb.Append(cols[i].Right ? 'R' : 'L'); }
		return sb.ToString();
	}

	/// <summary>Slice the built header at the definition's column boundaries: every slice must be exactly
	/// that column's label padded on that column's side.</summary>
	private static string SliceCheck(ColumnSpec[] cols)
	{
		string line = ContributionColumns.HeaderLine(cols);
		if (line.Length < 2 || line.Substring(0, 2) != "  ") return "no leading two spaces";
		int at = 2;
		for (int i = 0; i < cols.Length; i++)
		{
			string want = cols[i].Right ? DisplayFormat.PadL(cols[i].Label, cols[i].Width)
			                             : DisplayFormat.PadR(cols[i].Label, cols[i].Width);
			if (at + want.Length > line.Length) return "column " + i + " runs past the line";
			string got = line.Substring(at, want.Length);
			if (got != want) return "column " + i + " slice=" + got + " want=" + want;
			at += want.Length;
		}
		return at == line.Length ? "ok" : "trailing " + (line.Length - at) + " columns";
	}
}
