using System.Text;

namespace DpsMeter;

/// <summary>One column of an overlay table: what it is called, how wide it is in DISPLAY columns, and
/// which side it is padded on.</summary>
internal struct ColumnSpec
{
	public string Label;
	public int Width;
	/// <summary>false = padded on the RIGHT (left-aligned: the name column); true = padded on the LEFT
	/// (right-aligned: every numeric column).</summary>
	public bool Right;
}

/// <summary>
/// RF5c (plan section 10, ColumnSpec): the definition of the overlay's three tables in ONE place.
///
/// Before this file the column widths were literals copied into the header line AND into every data row
/// in Ui/OverlayUGUI.Rows.cs -- and check_contribution_layout.py independently re-derived the visible
/// layout from the exported text. Three copies of the same numbers. Now:
///
///   * the header lines and the totals row are BUILT from <see cref="T1"/>/<see cref="T2"/>/<see cref="T3"/>
///     here, so their widths cannot drift from the labels;
///   * the data rows still carry their widths inline (they interleave Amt's suffix fallback with the
///     columns), and the layout guard now verifies those literals against THIS file, so a mismatch is a
///     red build rather than a misaligned table.
///
/// The three table widths are the sums of their columns and are pinned by tests: 85 / 77 / 53.
/// Pure: no Unity type, so the behaviour suite compiles and executes the builders.
/// </summary>
internal static class ContributionColumns
{
	// The VISIBLE WIDTH OF A LINE, i.e. the columns PLUS the two leading spaces (83 + 2, 75 + 2, 51 + 2).
	// The layout guard pins the same three numbers as the exported tables' widths, which is how the
	// definition and the artifact stay comparable.
	public const int T1LineWidth = 85;
	public const int T2LineWidth = 77;
	public const int T3LineWidth = 53;

	// ---- T1: the contribution table -----------------------------------------------------------------
	public const int T1Name = 16;
	public const int T1Total = 11;
	public const int T1Share = 8;
	public const int T1Self = 11;
	public const int T1Other = 11;
	public const int T1Stolen = 11;
	public const int T1Direct = 9;
	public const int T1Hits = 6;

	// ---- T2: the rule table ------------------------------------------------------------------------
	public const int T2Rule = 22;
	public const int T2Kind = 8;
	public const int T2Side = 5;
	public const int T2Owner = 14;
	public const int T2Hits = 7;
	public const int T2Folds = 7;
	public const int T2Amount = 12;

	// ---- T3: the link table ------------------------------------------------------------------------
	public const int T3From = 14;
	public const int T3Arrow = 4;
	public const int T3To = 14;
	public const int T3Hits = 7;
	public const int T3Amount = 12;

	public static readonly ColumnSpec[] T1 =
	{
		C("角色", T1Name, false), C("总贡献", T1Total, true), C("占比", T1Share, true), C("自身", T1Self, true),
		C("他人因你", T1Other, true), C("被队友分走", T1Stolen, true), C("直接占比", T1Direct, true), C("命中", T1Hits, true),
	};

	public static readonly ColumnSpec[] T2 =
	{
		C("规则", T2Rule, false), C("通道", T2Kind, false), C("侧", T2Side, false), C("持有者", T2Owner, false),
		C("命中", T2Hits, true), C("折叠", T2Folds, true), C("当量", T2Amount, true),
	};

	public static readonly ColumnSpec[] T3 =
	{
		C("提供者", T3From, false), C("→", T3Arrow, false), C("受益者", T3To, false),
		C("命中", T3Hits, true), C("当量", T3Amount, true),
	};

	private static ColumnSpec C(string label, int width, bool right)
	{
		return new ColumnSpec { Label = label, Width = width, Right = right };
	}

	/// <summary>The header line: two leading spaces, then every label padded to ITS column width, on the
	/// side that column's numbers use. The data rows below must add up to the same geometry.</summary>
	public static string HeaderLine(ColumnSpec[] cols)
	{
		var sb = new StringBuilder(96);
		sb.Append("  ");
		for (int i = 0; i < cols.Length; i++)
			sb.Append(cols[i].Right ? DisplayFormat.PadL(cols[i].Label, cols[i].Width)
			                         : DisplayFormat.PadR(cols[i].Label, cols[i].Width));
		return sb.ToString();
	}

	/// <summary>The sum of the COLUMN widths, without the two leading spaces (see T1LineWidth for the line).</summary>
	public static int WidthOf(ColumnSpec[] cols)
	{
		int w = 0;
		for (int i = 0; i < cols.Length; i++) w += cols[i].Width;
		return w;
	}

	/// <summary>
	/// The T1 totals row. The two share columns stay empty on purpose: they are shares of a total that is
	/// printed in the 总贡献 column, and filling them with a second percentage is what made the pre-1.7.7
	/// variant ambiguous. The geometry comes from the same constants the header uses.
	/// </summary>
	public static string T1TotalsLine(double attributed, double selfAndBase, double assist, double received)
	{
		return "  " + DisplayFormat.PadR("合计", T1Name)
		     + DisplayFormat.Amt(attributed, T1Total)
		     + DisplayFormat.PadL("", T1Share)
		     + DisplayFormat.Amt(selfAndBase, T1Self)
		     + DisplayFormat.Amt(assist, T1Other)
		     + DisplayFormat.Amt(received, T1Stolen)
		     + DisplayFormat.PadL("", T1Direct)
		     + DisplayFormat.PadL("", T1Hits);
	}
}
