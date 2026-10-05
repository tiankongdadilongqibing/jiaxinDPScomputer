using System;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// RF5d: the three data-row builders. They are constructed from the column definition, so the width of
	/// every row is an INVARIANT of the definition rather than a literal the renderer repeats -- and the
	/// Python layout guard checks the builders structurally (constants in column order, no bare widths).
	/// </summary>
	public static void ColumnRows(Runner r)
	{
		r.Group("columns/rows");
		// every row must be exactly its table's LINE width, whatever the values
		r.Eq("a-typical-t1-row-is-94-wide",
		      DisplayFormat.DispWidth(ContributionColumns.T1Row("角色名", false, 1234.0, 12.34, 1000.0, 200.0, 34.0, 0.0, 5.0, 7)),
		      ContributionColumns.T1LineWidth);
		r.Eq("a-typical-t2-row-is-77-wide",
		      DisplayFormat.DispWidth(ContributionColumns.T2Row("规则", "kind", "自", "持有者", 3, 2, 900.0)),
		      ContributionColumns.T2LineWidth);
		r.Eq("a-typical-t3-row-is-53-wide",
		      DisplayFormat.DispWidth(ContributionColumns.T3Row("甲", "乙", 4, 5000.0)),
		      ContributionColumns.T3LineWidth);
		r.Eq("a-row-with-huge-amounts-is-still-94-wide",
		      DisplayFormat.DispWidth(ContributionColumns.T1Row("名", false, 1e12, 100.0, 1e12, 1e12, 1e12, 1e12, 100.0, 1e9)),
		      ContributionColumns.T1LineWidth);
		r.Eq("a-row-with-unknown-numbers-is-still-94-wide",
		      DisplayFormat.DispWidth(ContributionColumns.T1Row(null, false, double.NaN, double.NaN,
		                                                            double.PositiveInfinity, 0.0, -0.0, 0.0, 0.0, 0.0)),
		      ContributionColumns.T1LineWidth);
		r.Eq("a-huge-t2-amount-is-still-77-wide",
		      DisplayFormat.DispWidth(ContributionColumns.T2Row("r", "k", "s", "o", 1e9, 1e9, 1e12)),
		      ContributionColumns.T2LineWidth);
		r.Eq("a-huge-t3-amount-is-still-53-wide",
		      DisplayFormat.DispWidth(ContributionColumns.T3Row("a", "b", 1e9, 1e12)),
		      ContributionColumns.T3LineWidth);

		// the name column: fitted (with the mark) and the summon marker INSIDE the fit
		string longName = "ああああああああああああ";
		string row = ContributionColumns.T1Row(longName, true, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0);
		// 7 kana = 14 columns + ".." = 16: the cell is 16 .NET characters, the rest is padding.
		r.Str("a-long-name-is-cut-to-the-column-with-the-mark", row.Substring(2, 16).TrimEnd(), "あああああああ..");
		r.Eq("the-cut-row-is-still-94-wide", DisplayFormat.DispWidth(row), ContributionColumns.T1LineWidth);
		string marked = ContributionColumns.T1Row("短", true, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0);
		r.Str("the-summon-marker-lands-inside-the-name-cell", marked.Substring(2, 16), "短*" + new string(' ', 14));
		string unmarked = ContributionColumns.T1Row("短", false, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0);
		r.Str("without-the-marker-the-cell-is-just-the-name", unmarked.Substring(2, 16), "短" + new string(' ', 15));

		// the other fitted columns
		string t2 = ContributionColumns.T2Row("あああああああああああああ", "k", "s", "o", 0, 0, 0);
		// 10 kana = 20 columns of the 22, plus the 2-column mark
		r.Str("a-long-rule-name-is-cut-to-22", t2.Substring(2, 12), "ああああああああああ..");
		// 8 kana = 16 columns, so the 14-column cell really has to cut (7 kana would fit exactly)
		string t3 = ContributionColumns.T3Row("ああああああああ", "b", 0, 0);
		// 6 kana = 12 columns of the 14, plus the 2-column mark
		r.Str("a-long-provider-name-is-cut-to-14", t3.Substring(2, 8), "ああああああ..");

		// verbatim: the smallest row, hand-assembled from the definition
		r.Str("t3-row-verbatim", ContributionColumns.T3Row("a", "b", 1, 2),
		      "  " + "a" + new string(' ', 13) + "→" + new string(' ', 3) + "b" + new string(' ', 13)
		      + new string(' ', 6) + "1" + new string(' ', 11) + "2");
		r.Str("t3-row-verbatim-with-a-multiplication-sign",
		      ContributionColumns.T3Row("a\u00D7b", "b", 0, 0).Substring(2, 14), "axb" + new string(' ', 11));
	}
}
