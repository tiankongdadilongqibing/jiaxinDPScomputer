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
/// The three table widths are the sums of their columns and are pinned by tests: 94 / 77 / 53.
/// Pure: no Unity type, so the behaviour suite compiles and executes the builders.
/// </summary>
internal static class ContributionColumns
{
	// The VISIBLE WIDTH OF A LINE, i.e. the columns PLUS the two leading spaces (83 + 2, 75 + 2, 51 + 2).
	// The layout guard pins the same three numbers as the exported tables' widths, which is how the
	// definition and the artifact stay comparable.
	public const int T1LineWidth = 94;
	public const int T2LineWidth = 77;
	public const int T3LineWidth = 53;

	// ---- T1: the contribution table -----------------------------------------------------------------
	public const int T1Name = 16;
	public const int T1Total = 11;
	public const int T1Share = 8;
	public const int T1Self = 11;
	public const int T1Other = 11;
	public const int T1Stolen = 11;
	// 1.7.12 (user request): the same-team/self-damage part of the character's own hits. It was a column
	// in the 1.5.x table, disappeared when the credit columns were regrouped, and its absence is what made
	// a 2.0M self-damage read as "this character dealt 2.0M".
	public const int T1Friendly = 9;
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
		C("他人因你", T1Other, true), C("被队友分走", T1Stolen, true), C("自伤", T1Friendly, true),
		C("直接占比", T1Direct, true), C("命中", T1Hits, true),
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
	public static string T1TotalsLine(double attributed, double selfAndBase, double assist, double received,
	                                  double friendly)
	{
		return "  " + DisplayFormat.PadR("合计", T1Name)
		     + DisplayFormat.Amt(attributed, T1Total)
		     + DisplayFormat.PadL("", T1Share)
		     + DisplayFormat.Amt(selfAndBase, T1Self)
		     + DisplayFormat.Amt(assist, T1Other)
		     + DisplayFormat.Amt(received, T1Stolen)
		     + DisplayFormat.Amt(friendly, T1Friendly)
		     + DisplayFormat.PadL("", T1Direct)
		     + DisplayFormat.PadL("", T1Hits);
	}

	/// <summary>
	/// A T1 data row. The summon marker goes INSIDE the fit (1.7.7): appending it afterwards made an
	/// 8-column name + "*" 17 columns wide and pushed the whole row right.
	/// </summary>
	public static string T1Row(string name, bool summon, double total, double sharePct, double baseAndSelf,
	                           double assist, double received, double friendly, double directPct, double hits)
	{
		return "  " + DisplayFormat.PadR(DisplayFormat.Fit(DisplayFormat.Cell(name) + (summon ? "*" : ""), T1Name), T1Name)
		     + DisplayFormat.Amt(total, T1Total)
		     + DisplayFormat.PadL(DisplayFormat.Pct(sharePct), T1Share)
		     + DisplayFormat.Amt(baseAndSelf, T1Self)
		     + DisplayFormat.Amt(assist, T1Other)
		     + DisplayFormat.Amt(received, T1Stolen)
		     + DisplayFormat.Amt(friendly, T1Friendly)
		     + DisplayFormat.PadL(DisplayFormat.Pct(directPct), T1Direct)
		     + DisplayFormat.Amt(hits, T1Hits);
	}

	/// <summary>
	/// R55 (user request): the PENDING table's columns. They are T1's columns COPIED at call time -- same
	/// widths, so the two tables line up column for column -- with the two labels whose meaning differs
	/// changed: 角色 becomes 候选角色 (nothing here is attributed) and 命中 becomes 折叠 (these shares are
	/// counted per fold, not per hit). Copying instead of re-declaring is what stops the pair drifting.
	/// </summary>
	public static ColumnSpec[] T1PendingSpec()
	{
		var cols = new ColumnSpec[T1.Length];
		for (int i = 0; i < T1.Length; i++) cols[i] = T1[i];
		cols[0].Label = "候选角色";
		cols[cols.Length - 1].Label = "折叠";
		return cols;
	}

	/// <summary>A pending row: the name/amount/share/folds are real, and the credit columns that do not
	/// apply print a dash. A 0 there would read as a measurement of zero, which is a different statement
	/// from "this pool is not charged to anyone yet".</summary>
	public static string T1PendingRow(string name, double amount, double sharePct, double folds)
	{
		// the share text is materialised first on purpose: writing the same PadL(Pct(sharePct), T1Share)
		// expression here as in T1Row would make the negative-control mutation that targets that ONE line
		// match twice, i.e. turn a working gate into a driver failure.
		string share = DisplayFormat.Pct(sharePct);
		return "  " + DisplayFormat.PadR(DisplayFormat.Fit(DisplayFormat.Cell(name), T1Name), T1Name)
		     + DisplayFormat.Amt(amount, T1Total)
		     + DisplayFormat.PadL(share, T1Share)
		     + PendingDash(T1Self)
		     + PendingDash(T1Other)
		     + PendingDash(T1Stolen)
		     + PendingDash(T1Friendly)
		     + PendingDash(T1Direct)
		     + DisplayFormat.Amt(folds, T1Hits);
	}

	/// <summary>The pending table's footer, on the character table's own geometry so the two totals sit in
	/// the same column. It says 待确认 rather than 合计 because this pool is exactly the part that is NOT in
	/// the 合计 above it.</summary>
	public static string T1PendingTotalsLine(double amount, double sharePct, double folds)
	{
		string share = DisplayFormat.Pct(sharePct);
		return "  " + DisplayFormat.PadR("待确认合计", T1Name)
		     + DisplayFormat.Amt(amount, T1Total)
		     + DisplayFormat.PadL(share, T1Share)
		     + PendingDash(T1Self)
		     + PendingDash(T1Other)
		     + PendingDash(T1Stolen)
		     + PendingDash(T1Friendly)
		     + PendingDash(T1Direct)
		     + DisplayFormat.Amt(folds, T1Hits);
	}

	private static string PendingDash(int width)
	{
		return DisplayFormat.PadL("-", width);
	}

	/// <summary>A T2 data row: four fitted/padded text cells, then three amounts.</summary>
	public static string T2Row(string rule, string kind, string side, string owner, double hits, double folds,
	                           double amount)
	{
		return "  " + DisplayFormat.PadR(DisplayFormat.Fit(DisplayFormat.Cell(rule), T2Rule), T2Rule)
		     + DisplayFormat.PadR(DisplayFormat.Fit(DisplayFormat.Cell(kind), T2Kind), T2Kind)
		     + DisplayFormat.PadR(DisplayFormat.Fit(DisplayFormat.Cell(side), T2Side), T2Side)
		     + DisplayFormat.PadR(DisplayFormat.Fit(DisplayFormat.Cell(owner), T2Owner), T2Owner)
		     + DisplayFormat.Amt(hits, T2Hits)
		     + DisplayFormat.Amt(folds, T2Folds)
		     + DisplayFormat.Amt(amount, T2Amount);
	}

	/// <summary>A T3 data row. The arrow is a plain padded cell: it appears in the header AND in every row,
	/// so a mis-measured arrow moves both by the same amount and the columns stay aligned with each other.
	/// </summary>
	public static string T3Row(string from, string to, double hits, double amount)
	{
		return "  " + DisplayFormat.PadR(DisplayFormat.Fit(DisplayFormat.Cell(from), T3From), T3From)
		     + DisplayFormat.PadR("→", T3Arrow)
		     + DisplayFormat.PadR(DisplayFormat.Fit(DisplayFormat.Cell(to), T3To), T3To)
		     + DisplayFormat.Amt(hits, T3Hits)
		     + DisplayFormat.Amt(amount, T3Amount);
	}
}
