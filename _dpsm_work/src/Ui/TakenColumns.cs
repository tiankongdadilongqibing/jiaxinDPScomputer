using System.Collections.Generic;
using System.Text;

namespace DpsMeter;

/// <summary>
/// R79: the geometry of the 受击来源拆分 page (F3). Column widths live here and nowhere else, exactly as in
/// <see cref="ContributionColumns"/>: the header, the victim rows and the totals row are BUILT from the same
/// constants, so the definition and the artifact cannot drift, and the line width is pinned by a test
/// instead of by eye.
///
/// Pure (string / int / long plus <see cref="DisplayFormat"/>): tests/BehaviorTests compiles and executes
/// these builders, which is the only way a layout claim here can be checked rather than eyeballed.
/// </summary>
internal static class TakenColumns
{
	// ---- T1: one row per victim -------------------------------------------------------------------
	public const int T1Position = 8;
	public const int T1Name = 18;
	public const int T1Nominal = 13;
	public const int T1Taken = 13;
	public const int T1Residual = 13;
	public const int T1Hits = 6;
	public const int T1Share = 7;

	/// <summary>The visible width of a victim row: the two leading spaces PLUS every column. Must stay
	/// inside the panel (see OverlayUGUI.Rows.cs LayoutCharts, which widens the panel for this page).</summary>
	public const int T1LineWidth = 2 + T1Position + T1Name + T1Nominal + T1Taken + T1Residual + T1Hits + T1Share;

	// ---- the per-dimension lines under a victim ----------------------------------------------------
	/// <summary>Width of the dimension word (单位 / 种类 / 属性 / 效果 / 状态).</summary>
	public const int BucketLabel = 6;

	/// <summary>Width a bucket NAME is fitted to before its amount; a longer name is cut with `..`.</summary>
	public const int BucketName = 12;

	/// <summary>Width of a bucket's amount inside a dimension line. <see cref="DisplayFormat.Amt"/> keeps the
	/// grouped form while it fits and falls back to an M/G/T suffix after that, so this is an upper bound and
	/// a long fight can never push the line past the panel.</summary>
	public const int BucketAmount = 11;

	/// <summary>How many buckets one dimension line prints before the remainder is folded into 其余N项.</summary>
	public const int ShownBuckets = 3;

	public static readonly ColumnSpec[] T1 =
	{
		C("站位", T1Position, false), C("单位", T1Name, false), C("受击(口径)", T1Nominal, true),
		C("已发布", T1Taken, true), C("超出", T1Residual, true), C("击数", T1Hits, true), C("占比", T1Share, true),
	};

	private static ColumnSpec C(string label, int width, bool right)
	{
		return new ColumnSpec { Label = label, Width = width, Right = right };
	}

	/// <summary>The column header, on the same geometry as <see cref="T1Row"/>.</summary>
	public static string Header()
	{
		return ContributionColumns.HeaderLine(T1);
	}

	/// <summary>
	/// A victim row. The position cell is the plugin's own word for the snapshot taken on the unit's first
	/// damage (前衛 / 後衛 / 站位未知) -- never a guess, because a failed native read leaves 0 and 0 prints
	/// as unknown.
	/// </summary>
	public static string T1Row(string position, string name, long nominal, long taken, long residual, long hits,
	                            double sharePct)
	{
		return "  " + DisplayFormat.PadR(DisplayFormat.Fit(DisplayFormat.Cell(position), T1Position), T1Position)
		     + DisplayFormat.PadR(DisplayFormat.Fit(DisplayFormat.Cell(name), T1Name), T1Name)
		     + DisplayFormat.Amt(nominal, T1Nominal)
		     + DisplayFormat.Amt(taken, T1Taken)
		     + DisplayFormat.Amt(residual, T1Residual)
		     + DisplayFormat.Amt(hits, T1Hits)
		     + DisplayFormat.PadL(DisplayFormat.Pct(sharePct), T1Share);
	}

	/// <summary>The page's totals row, on the victim rows' own geometry: the two dimensions where a sum is
	/// meaningful are filled and the rest stay blank rather than printing a second, ambiguous percentage.</summary>
	public static string T1TotalsLine(long nominal, long taken, long residual, long hits)
	{
		return "  " + DisplayFormat.PadR("合计", T1Position + T1Name)
		     + DisplayFormat.Amt(nominal, T1Nominal)
		     + DisplayFormat.Amt(taken, T1Taken)
		     + DisplayFormat.Amt(residual, T1Residual)
		     + DisplayFormat.Amt(hits, T1Hits)
		     + DisplayFormat.PadL("", T1Share);
	}

	/// <summary>
	/// One dimension line under a victim: `  - 单位  name amount / name amount / 其余N项 amount`.
	///
	/// A plain ASCII hyphen is the bullet ON PURPOSE. The contribution table's measurements are all about
	/// what happens when a glyph's drawn width disagrees with <see cref="DisplayFormat.DispWidth"/> (U+00D7
	/// was drawn full-width by the CJK font while being counted as one column), and box-drawing characters
	/// like U+251C are in exactly that family. The label column is fitted, so a long word cannot push the
	/// first bucket right.
	/// </summary>
	public static string DimensionLine(string label, List<string> parts)
	{
		var sb = new StringBuilder(96);
		sb.Append("  - ");
		sb.Append(DisplayFormat.PadR(DisplayFormat.Fit(DisplayFormat.Cell(label), BucketLabel), BucketLabel));
		for (int i = 0; i < parts.Count; i++)
		{
			if (i > 0) sb.Append(" / ");
			sb.Append(parts[i]);
		}
		return sb.ToString();
	}

	/// <summary>
	/// One bucket of a dimension line: the name fitted to <see cref="BucketName"/> (with the marker that the
	/// label is only approximate), then the amount, bounded by <see cref="BucketAmount"/> so three parts plus
	/// their separators can never leave the panel however big the fight was. A `*` here is load-bearing: the
	/// attacker/effect dimensions are only value-exact for a small share of hits, and a best-effort label must
	/// never read as a measurement.
	/// </summary>
	public static string BucketPart(string name, long amount, bool approximate)
	{
		return DisplayFormat.Fit(DisplayFormat.Cell(name), BucketName) + (approximate ? "*" : "")
		     + " " + Amount(amount);
	}

	/// <summary>The folded remainder of a dimension: one entry whose amount keeps the sum equal to the
	/// victim's nominal total, so a shortened line is still an arithmetic statement.</summary>
	public static string RestPart(int count, long amount)
	{
		return DisplayFormat.Fit("其余" + count + "项", BucketName) + " " + Amount(amount);
	}

	/// <summary>The bare (unpadded) bounded amount of a dimension line. DisplayFormat.Amt right-pads for a
	/// fixed column; here the amount follows a variable-width name inside a prose line, where the padding
	/// would only open a gap, so the padding is trimmed back off.</summary>
	private static string Amount(long amount)
	{
		return DisplayFormat.Amt(amount, BucketAmount).Trim();
	}
}
