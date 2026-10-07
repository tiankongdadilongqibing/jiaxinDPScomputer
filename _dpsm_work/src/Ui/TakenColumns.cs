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

	/// <summary>R83: 8, not 7. <see cref="DisplayFormat.Pct"/> prints `100.00%` in exactly seven display
	/// columns and <see cref="DisplayFormat.PadL"/> never shrinks a full cell, so a seven-wide share cell put
	/// the percentage's leading digit straight against the hit count and `2` + `100.00%` read as `2100.00%`.
	/// Eight is what the contribution table has used for this column since R12 (ContributionColumns.T1Share).</summary>
	public const int T1Share = 8;

	/// <summary>The visible width of a victim row: the two leading spaces PLUS every column. Must stay
	/// inside the panel (see OverlayUGUI.Rows.cs LayoutCharts, which widens the panel for this page).</summary>
	public const int T1LineWidth = 2 + T1Position + T1Name + T1Nominal + T1Taken + T1Residual + T1Hits + T1Share;

	public static readonly ColumnSpec[] T1 =
	{
		C("站位", T1Position, false), C("单位", T1Name, false), C("受击(口径)", T1Nominal, true),
		C("已发布", T1Taken, true), C("超出", T1Residual, true), C("击数", T1Hits, true), C("占比", T1Share, true),
	};

	private static ColumnSpec C(string label, int width, bool right)
	{
		return new ColumnSpec { Label = label, Width = width, Right = right };
	}

	// ---- B: the per-dimension bucket sub-table (R80) ------------------------------------------------
	/// <summary>
	/// R80 replaced R79's one-line-per-dimension form (`  - 单位  name amount / name amount / 其余N项 ...`)
	/// with one ROW per bucket. R79 could only ever print three buckets and folded the rest into 其余N项 --
	/// the same 64-bucket cap that also truncated the export -- so a victim with 300 distinct attackers was
	/// unreadable exactly where the analysis matters. The widths below are the new table's; they are wider
	/// than R79's compact line field because a bucket name is now a whole cell instead of a 12-column prefix.
	/// </summary>
	public const int BName = 46;

	/// <summary>Bucket amount. <see cref="DisplayFormat.Amt"/> keeps the grouped form while it fits and falls
	/// back to an M/G/T suffix after that, so this is an upper bound and a long fight cannot overflow it.</summary>
	public const int BAmount = 14;

	public const int BHits = 7;

	/// <summary>R83: 8 for the same reason as <see cref="T1Share"/> -- `100.00%` is seven columns wide and a
	/// full cell gets no padding at all, so at seven the share column touched the hit count (`2` + `100.00%`
	/// reads as `2100.00%`). Every legitimately computed share is at most `100.00%`, so eight columns always
	/// leaves at least one space between the two numbers.</summary>
	public const int BShare = 8;

	/// <summary>The visible width of a bucket row: two leading spaces PLUS every column. Pinned by a test and
	/// comfortably inside the panel LayoutCharts gives this page (880 px).</summary>
	public const int BLineWidth = 2 + BName + BAmount + BHits + BShare;

	// R83: the amount column is labelled 伤害, not 金额. Nothing on this page is money -- the cell holds a
	// damage amount inside the victim's nominal total -- and the contribution table calls the same kind of
	// cell 当量 in its rule/link tables. The user reported the old label as wrong, and it was: 金额 invited
	// reading the number as a currency.
	public static readonly ColumnSpec[] B =
	{
		C("名字", BName, false), C("伤害", BAmount, true), C("击数", BHits, true), C("占比", BShare, true),
	};

	/// <summary>The column header of a bucket sub-table, on <see cref="BRow"/>'s own geometry.</summary>
	public static string BHeader()
	{
		return ContributionColumns.HeaderLine(B);
	}

	/// <summary>The label line that opens a dimension's sub-table: the dimension's name and how many distinct
	/// buckets it holds. Deliberately not a second total -- the sub-table ends with <see cref="BTotal"/>.</summary>
	public static string BSubHeader(string label, int count)
	{
		return "  - " + label + "  " + DisplayFormat.Num(count) + " 项";
	}

	/// <summary>
	/// One bucket row. `approximate` is R79's `*` marker and it is load-bearing: the attacker/effect
	/// dimensions are only value-exact for a small share of hits, and a best-effort label must never read as
	/// a measurement. The marker is cut into the name's own width, so a marked row is never wider than an
	/// unmarked one.
	/// </summary>
	public static string BRow(string name, long amount, long hits, double sharePct, bool approximate)
	{
		return "  " + DisplayFormat.PadR(BucketName(name, approximate), BName)
		     + DisplayFormat.Amt(amount, BAmount)
		     + DisplayFormat.Amt(hits, BHits)
		     + DisplayFormat.PadL(DisplayFormat.Pct(sharePct), BShare);
	}

	/// <summary>
	/// The closing total of a dimension's sub-table. By the model's invariants every dimension partitions the
	/// victim's whole nominal total, so this row is also the check that nothing was dropped on the way to the
	/// page. The share cell stays blank rather than printing a second, always-100% percentage.
	/// </summary>
	public static string BTotal(string label, long amount, long hits)
	{
		return "  " + DisplayFormat.PadR(DisplayFormat.Fit(DisplayFormat.Cell(label), BName), BName)
		     + DisplayFormat.Amt(amount, BAmount)
		     + DisplayFormat.Amt(hits, BHits)
		     + DisplayFormat.PadL("", BShare);
	}

	/// <summary>The name cell of a bucket row: the display cell, cut to leave room for the `*` marker.</summary>
	private static string BucketName(string name, bool approximate)
	{
		string cell = DisplayFormat.Fit(DisplayFormat.Cell(name), approximate ? BName - 1 : BName);
		return approximate ? cell + "*" : cell;
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
	/// meaningful are filled and the rest stay blank rather than printing a second, ambiguous percentage.
	/// R84: the label is a parameter because the page now shows ONE character at a time while this row sums
	/// the WHOLE allied side -- a bare 合计 printed under a single character's blocks reads as that
	/// character's own total, which it never was (the same trap the share column had in R83).</summary>
	public static string T1TotalsLine(string label, long nominal, long taken, long residual, long hits)
	{
		return "  " + DisplayFormat.PadR(DisplayFormat.Fit(DisplayFormat.Cell(label), T1Position + T1Name),
		                                 T1Position + T1Name)
		     + DisplayFormat.Amt(nominal, T1Nominal)
		     + DisplayFormat.Amt(taken, T1Taken)
		     + DisplayFormat.Amt(residual, T1Residual)
		     + DisplayFormat.Amt(hits, T1Hits)
		     + DisplayFormat.PadL("", T1Share);
	}
}
