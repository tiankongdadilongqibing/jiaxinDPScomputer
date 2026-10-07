using System;
using System.Collections.Generic;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// R79 built the 受击来源拆分 page, R80 made it COMPLETE (every victim, every bucket). The geometry and
	/// text of `Ui/TakenColumns.cs` / `Ui/TakenPageText.cs` are checked here.
	///
	/// Two things are checked that a screenshot cannot settle. First the WIDTH: every cell is padded on the
	/// assumption that a CJK glyph is two columns and an ASCII one is one, so a row whose measured width differs
	/// from its own header drifts column by column -- exactly the defect R69/R78 chased in the contribution
	/// table. Second the WORDS: the residual (超出剩余耐久) must never be printed as an absorption, and a
	/// best-effort bucket must carry its `*`, because both would otherwise read as measurements.
	///
	/// R80's own subject is that NOTHING is summarised away: no victim cap, no three-bucket fold, and every
	/// dimension's sub-table closes on a total that must equal the victim's whole nominal (the model's
	/// invariant, re-checked here on the strings that actually reach the panel).
	///
	/// R83's subject is the two defects a screenshot found in R80's finished table: a full `100.00%` share cell
	/// was rendered with no separator in front of it (`2` + `100.00%` read as `2100.00%`, because the text is
	/// seven columns wide and `PadL` never shrinks), and the amount column was labelled 金额 -- a currency --
	/// for a damage amount. Both are pinned below as text, not as intent.
	///
	/// R84's subject is the page's SHAPE: it now prints ONE character (the reader's report was that a party's
	/// worth of sub-tables is a lot of scrolling), chosen by clicking a name in a 角色 list printed above the
	/// table. The list is checked to hold EVERY character -- with one character on screen it is the only way to
	/// reach the others -- and the whole-team total row is checked to stay a whole-team total, because with a
	/// single character on screen a bare `合计` would read as that character's own total.
	/// </summary>
	internal static void TakenPageCases(Runner r)
	{
		r.Group("ui/taken-page");

		// ---- the geometry is one number, and the built rows obey it ------------------------------------
		r.Eq("the-victim-row-width-is-pinned", TakenColumns.T1LineWidth, 81);
		r.Eq("the-header-is-exactly-one-row-wide",
			DisplayFormat.DispWidth(TakenColumns.Header()), TakenColumns.T1LineWidth);
		r.Eq("a-victim-row-is-exactly-one-row-wide",
			DisplayFormat.DispWidth(TakenColumns.T1Row("前衛", "レヴァナント", 6000000, 5000000, 1000000, 42, 48.6)),
			TakenColumns.T1LineWidth);
		r.Eq("the-totals-row-is-exactly-one-row-wide",
			DisplayFormat.DispWidth(TakenColumns.T1TotalsLine("全队合计", 6000000, 5000000, 1000000, 42)),
			TakenColumns.T1LineWidth);
		// A long name is fitted, so it can never push the numeric columns right (the R69 defect).
		r.Eq("an-overlong-name-does-not-widen-the-row",
			DisplayFormat.DispWidth(TakenColumns.T1Row("站位未知", "非常に長い名前のボスキャラクター", 1, 1, 0, 1, 100.0)),
			TakenColumns.T1LineWidth);

		// ---- the bucket sub-table: one row per bucket, on its own pinned geometry ----------------------
		r.Eq("the-bucket-row-width-is-pinned", TakenColumns.BLineWidth, 77);
		r.True("the-bucket-table-fits-the-wide-panel", TakenColumns.BLineWidth <= 94);
		r.Eq("the-bucket-header-is-exactly-one-row-wide",
			DisplayFormat.DispWidth(TakenColumns.BHeader()), TakenColumns.BLineWidth);
		r.Eq("a-bucket-row-is-exactly-one-row-wide",
			DisplayFormat.DispWidth(TakenColumns.BRow("ボス", 1234567890123L, 42, 48.6, false)),
			TakenColumns.BLineWidth);
		r.Eq("the-sub-table-total-is-exactly-one-row-wide",
			DisplayFormat.DispWidth(TakenColumns.BTotal("合计", 6000000, 42)), TakenColumns.BLineWidth);
		// The `*` is cut INTO the name cell, so a marked row can never be one column wider than an exact one.
		string wideName = new string('あ', 40);   // 80 columns: longer than the 46-column cell
		string wideRow = TakenColumns.BRow(wideName, 1, 1, 100.0, true);
		r.True("an-overlong-bucket-name-is-cut", !wideRow.Contains(wideName));
		r.True("an-overlong-bucket-name-is-still-marked", TkCells(wideRow)[0].EndsWith("*"));
		r.Eq("a-marked-overlong-name-does-not-widen-the-row",
			DisplayFormat.DispWidth(wideRow), TakenColumns.BLineWidth);

		// ---- R83: a full share cell may not touch the number before it ---------------------------------
		// `DisplayFormat.Pct` prints `100.00%` in exactly seven display columns and `PadL` adds NOTHING to a
		// cell that is already full, so a seven-wide share column rendered the hit count and the percentage as
		// one run of digits: `2` + `100.00%` read as `2100.00%`. R83 widened both share columns to the
		// contribution table's own eight. The two cases above pin the geometry; these pin the REASON for it,
		// so a later "let us save a column" edit has to redden a test that says what it breaks.
		r.Eq("the-share-text-is-seven-columns-wide", DisplayFormat.DispWidth(DisplayFormat.Pct(100.0)), 7);
		r.Str("a-full-share-cell-would-get-no-padding", DisplayFormat.PadL(DisplayFormat.Pct(100.0), 7), "100.00%");
		string fullVictim = TakenColumns.T1Row("前衛", "レヴァナント", 6000000, 6000000, 0, 42, 100.0);
		r.Str("a-full-victim-share-cell-carries-its-own-padding",
			fullVictim.Substring(fullVictim.Length - TakenColumns.T1Share), " 100.00%");
		string fullBucket = TakenColumns.BRow("ショゴス", 6000000, 42, 100.0, false);
		r.Str("a-full-bucket-share-cell-carries-its-own-padding",
			fullBucket.Substring(fullBucket.Length - TakenColumns.BShare), " 100.00%");
		// The separator is the column's width, not a special case for 100%: the share cell always starts with
		// at least one space of its own, so 99.99% cannot glue either.
		string nearFull = TakenColumns.BRow("ショゴス", 5999999, 42, 99.99, false);
		r.True("a-near-full-bucket-share-is-separated-too",
			nearFull[nearFull.Length - TakenColumns.BShare] == ' ');

		// ---- R83: the amount column is a DAMAGE amount, and says so -------------------------------------
		r.True("the-bucket-amount-column-is-labelled-damage", TakenColumns.BHeader().Contains("伤害"));
		r.True("the-bucket-amount-column-is-not-labelled-money", !TakenColumns.BHeader().Contains("金额"));

		// ---- the approximation marker: an estimate may not look like a measurement ---------------------
		r.Str("an-approximate-bucket-is-marked", TkCells(TakenColumns.BRow("ショゴス", 500000, 3, 50.0, true))[0],
			"ショゴス*");
		r.Str("an-exact-bucket-carries-no-marker", TkCells(TakenColumns.BRow("ポポロット", 90676, 3, 50.0, false))[0],
			"ポポロット");
		r.Str("a-short-bucket-name-is-left-alone",
			TkCells(TakenColumns.BRow("非常に長いボスの名前です", 1, 1, 100.0, true))[0],
			"非常に長いボスの名前です*");
		r.Str("the-sub-header-states-the-bucket-count", TakenColumns.BSubHeader("单位(攻击者)", 253),
			"  - 单位(攻击者)  253 项");

		// ---- the page ----------------------------------------------------------------------------------
		List<TakenLine> lines = TakenPageText.Lines(TkSample(), false, true, 0);
		r.Str("the-first-line-states-both-sides-of-the-total",
			lines[0].Text, "击 4   受击(游戏口径) 450   已发布 450   超出剩余耐久 0");
		r.Str("the-second-line-separates-our-side-from-the-enemy-side",
			lines[1].Text, "  我方 350   敌方 100(1 人未列入下表)   同队自伤 0   攻击者不明 0");
		r.Str("the-third-line-names-the-position-snapshot",
			lines[2].Text, "  站位(该单位首次受击时的快照,之后移动不改写)  前衛 300   後衛 50   站位未知 0");
		r.True("our-victim-gets-a-table-row", TkAny(lines, "  前衛"));
		r.True("the-enemy-victim-gets-no-table-row", !TkAny(lines, "  後衛  敵"));

		// The word 被吸收 may appear ONLY inside its own denial -- the R78 rule, kept on the new page.
		r.Eq("the-page-never-calls-the-residual-an-absorption", TkCount(lines, "被吸收"), 1);
		r.True("and-that-one-mention-is-the-denial", TkAny(lines, "不是被吸收"));

		// ---- one character at a time, and the two filters ------------------------------------------------
		// R84: the table prints the SELECTED character only -- the first entry (the biggest victim) until the
		// reader picks another. Everyone else is one click away in the 角色 list above the table, which lists
		// them all (its own cases are below).
		r.Eq("the-table-shows-one-character-at-a-time", TkRows(lines), 1);
		r.Str("and-that-one-is-the-first-entry-by-default", TkCellOf(lines, "  前衛", 1), "レヴァナント");
		r.Eq("the-vanguard-filter-keeps-only-the-front-row",
			TkRows(TakenPageText.Lines(TkSample(), true, true, 0)), 1);
		// the totals line keeps stating the WHOLE battle, so a narrowed table cannot be misread as the picture
		r.Str("the-vanguard-filter-does-not-move-the-headline-total",
			TakenPageText.Lines(TkSample(), true, true, 0)[0].Text,
			"击 4   受击(游戏口径) 450   已发布 450   超出剩余耐久 0");

		// ---- R80 kept complete, R84 adds the index: every victim is reachable, none is dropped ----------
		List<TakenLine> many = TakenPageText.Lines(TkManyVictims(15), false, true, 0);
		r.Eq("the-table-still-shows-exactly-one-of-them", TkRows(many), 1);
		r.Eq("nothing-is-dropped-from-the-page", TkCount(many, "另有"), 0);
		r.Eq("no-longer-points-at-the-export-for-missing-rows", TkCount(many, "人(导出"), 0);

		// ---- R80: no three-bucket fold either -- a dimension prints one row per bucket ------------------
		List<TakenLine> wide = TakenPageText.Lines(TkFiveSources(), false, true, 0);
		r.True("a-five-bucket-dimension-states-five", TkAny(wide, "  - 种类(DamageSource)  5 项"));
		r.Eq("nothing-is-folded-into-a-remainder-row", TkCount(wide, "其余"), 0);
		r.Eq("the-five-buckets-are-five-rows", TkBucketRows(wide, "  - 种类(DamageSource)"), 5);

		// ---- the sub-table totals are the model's invariant, on the strings the panel shows ------------
		r.Str("the-attacker-subtable-totals-the-victims-nominal",
			TkSubTotal(lines, "  - 单位(攻击者)", 0), "300");
		r.Str("the-source-subtable-totals-it-too", TkSubTotal(lines, "  - 种类(DamageSource)", 0), "300");
		r.Str("the-hit-type-subtable-totals-it-too", TkSubTotal(lines, "  - 属性(eDamageCalcType)", 0), "300");
		r.Str("the-effect-subtable-totals-it-too", TkSubTotal(lines, "  - 效果(m_effectId)", 0), "300");
		// R84: the OTHER character is reached by selecting it -- which is exactly how the panel reaches it,
		// since it is no longer printed underneath the first one.
		List<TakenLine> second = TakenPageText.Lines(TkSample(), false, true, 3);
		r.Str("the-other-characters-subtable-totals-its-own-nominal",
			TkSubTotal(second, "  - 单位(攻击者)", 0), "50");
		// Every sub-table prints its own total, so none can silently lose a row. One character's block = four
		// sub-tables here (状態 and 其他 have nothing to print). The page's own whole-team row is NOT one of
		// these: R84 labelled it 全队合计, so `  合计` can only ever be a sub-table's closing row.
		r.Eq("every-sub-table-closes-on-a-total", TkCount(lines, "  合计"), 4);

		// ---- the per-victim block ----------------------------------------------------------------------
		r.True("the-block-lists-the-source-dimension", TkAny(lines, "  - 种类(DamageSource)"));
		r.True("the-block-lists-the-attacker-dimension", TkAny(lines, "  - 单位(攻击者)"));
		r.True("the-block-lists-the-hit-type-dimension", TkAny(lines, "  - 属性(eDamageCalcType)"));
		r.True("the-block-lists-the-effect-dimension", TkAny(lines, "  - 效果(m_effectId)"));
		r.True("the-block-prints-a-column-header", TkAny(lines, "名字"));
		r.True("the-legend-says-the-dimensions-are-not-all-authoritative", TkAny(lines, "带 * 的名字只对该维度的部分命中权威"));
		r.True("the-legend-names-the-export-section-it-mirrors", TkAny(lines, "数字与导出的 takenBreakdown 段同源"));
		r.True("the-unread-effect-id-is-not-invented", TkAny(lines, "未识别"));

		// A friendly/unknown amount must be visible as such: they are IN the total but belong to no attacker.
		var mixed = new List<TakenHit>();
		TakenHit fr = TkPageHit(1, "甲", 100, 100);
		fr.Friendly = true;
		mixed.Add(fr);
		TakenHit un = TkPageHit(1, "甲", 50, 50);
		un.AttackerKey = 0; un.Attacker = ""; un.Attr = "?";
		mixed.Add(un);
		List<TakenLine> odd = TakenPageText.Lines(TakenBreakdownPolicy.Build(mixed, 1), false, true, 0);
		r.True("the-two-odd-amounts-get-their-own-table", TkAny(odd, "  - 其他(不属于任何攻击者桶)  2 项"));
		r.True("friendly-damage-is-named", TkAny(odd, "同队自伤"));
		r.True("an-unresolvable-attacker-is-named", TkAny(odd, "攻击者不明"));
		r.Str("and-the-odd-table-totals-them", TkSubTotal(odd, "  - 其他(不属于任何攻击者桶)", 0), "150");

		// ---- R81: each sub-table's LABEL row carries its section, which is what the renderer colours ----
		// Before R81 every line of this page landed on the same dim grey (or one of the three role colours),
		// so on a victim with hundreds of attackers there was no landmark between 単位 and 属性 at all.
		r.Eq("the-attacker-label-row-carries-its-section", (long)TkBlockOf(lines, "  - 单位(攻击者)"),
			(long)TakenBlock.Attacker);
		r.Eq("the-source-label-row-carries-its-section", (long)TkBlockOf(lines, "  - 种类(DamageSource)"),
			(long)TakenBlock.HitType);
		r.Eq("the-hit-type-label-row-carries-its-section", (long)TkBlockOf(lines, "  - 属性(eDamageCalcType)"),
			(long)TakenBlock.Attr);
		r.Eq("the-effect-label-row-carries-its-section", (long)TkBlockOf(lines, "  - 效果(m_effectId)"),
			(long)TakenBlock.Effect);
		r.Eq("the-odd-label-row-carries-its-section", (long)TkBlockOf(odd, "  - 其他(不属于任何攻击者桶)"),
			(long)TakenBlock.Other);
		// 状態 needs a fixture of its own: none of the tables above carries an ailment
		var ailed = new List<TakenHit>();
		TakenHit ail = TkPageHit(1, "甲", 100, 100);
		ail.Status = "毒"; ail.StatusApplier = "ボス";
		ailed.Add(ail);
		List<TakenLine> withStatus = TakenPageText.Lines(TakenBreakdownPolicy.Build(ailed, 1), false, true, 0);
		r.Eq("the-status-label-row-carries-its-section", (long)TkBlockOf(withStatus, "  - 状态(异常/付与者)"),
			(long)TakenBlock.Status);
		// ONLY the label rows are tagged: 4 = one victim x four dimensions that each have a bucket. Tagging a
		// bucket row would recolour a whole section, which is not what was asked for. (R84: one victim's block
		// is on the page at a time, so this counts that one block's sections.)
		r.Eq("only-the-section-label-rows-are-tagged", (long)TkTaggedRows(lines), 4L);
		r.Eq("the-headline-is-not-tagged-as-a-section", (long)lines[0].Block, (long)TakenBlock.None);
		// Two tags that share a value share one colour, so the six sections plus "no section" must be seven
		// distinct values -- this is the case that goes red if a value is copy-pasted in the vocabulary.
		r.Eq("the-six-sections-plus-none-are-seven-distinct-tags", (long)TkDistinctTags(), 7L);

		// ---- R84: the 角色 list -- the index that replaced everyone's rows -------------------------------
		// With one character on screen, the list is the ONLY way to reach the others, so it is checked to
		// offer every one of them, in the page's own order, with each entry submitting its own position.
		List<TakenLine> list = TakenPageText.Lines(TkManyVictims(15), false, true, 0);
		r.Eq("the-character-list-offers-one-entry-per-character", TkEntries(list).Count, 15);
		r.Str("the-character-list-submits-each-entry-as-the-character-action", TkEntryActions(list), "TakenActor");
		r.Str("the-character-list-passes-each-entries-position", TkEntryArgs(list),
			"0,1,2,3,4,5,6,7,8,9,10,11,12,13,14");
		r.Str("the-character-list-names-every-character", TkEntryNames(list),
			"味方0,味方1,味方2,味方3,味方4,味方5,味方6,味方7,味方8,味方9,味方10,味方11,味方12,味方13,味方14");
		// The marker sits INSIDE the click target, so the entry that is lit up is the one a click switches to.
		r.Str("the-character-list-marks-the-character-on-screen", TkMarked(list), "▶ 味方0");
		List<TakenLine> third = TakenPageText.Lines(TkManyVictims(15), false, true, 102);
		r.Str("and-the-marker-follows-the-selection", TkMarked(third), "▶ 味方2");
		r.True("the-list-states-which-character-is-on-screen", TkAny(third, "角色 3/15(点名字切换)"));
		// 15 names at five to a row = three name rows. The caption has a row of its own (see AddActorList),
		// so the count below counts NAMES, and the one after it pins the wrap width itself.
		r.Eq("the-character-list-wraps-after-five-names", TkNameRows(list), 3);
		r.Eq("no-name-row-carries-more-than-five-entries", TkMaxEntriesPerRow(list), 5);
		// An unknown key -- 0 before anything was picked, or a character that has left the list -- is the
		// first entry. This is the case that keeps "the page remembered who I was looking at" from silently
		// becoming "the page showed someone else".
		List<TakenLine> fallback = TakenPageText.Lines(TkSample(), false, true, 999);
		r.Eq("an-unknown-selection-lands-on-the-first-character", TkRows(fallback), 1);
		r.Str("an-unknown-selection-shows-the-first-character", TkCellOf(fallback, "  前衛", 1), "レヴァナント");
		r.True("an-unknown-selection-says-so-in-the-list", TkAny(fallback, "角色 1/2(点名字切换)"));
		// The character on screen is NOT what the totals row is about: that row is the whole team, and with a
		// single character's block above it a bare 合计 would read as that character's own total.
		List<TakenLine> totals = TakenPageText.Lines(TkSample(), false, true, 3);
		r.Str("the-totals-row-is-labelled-as-the-teams-total", TkCellOf(totals, "  全队合计", 0), "全队合计");
		r.Str("and-that-row-keeps-the-whole-team-amount", TkCellOf(totals, "  全队合计", 1), "350");
		// The list is the page's own victim list: our side only, and under the 前衛 filter.
		r.Eq("the-victim-list-is-our-side-only", TakenPageText.Victims(TkSample(), false).Count, 2);
		r.Eq("the-victim-list-follows-the-front-filter", TakenPageText.Victims(TkSample(), true).Count, 1);
		r.Eq("an-empty-victim-list-selects-nothing", TakenPageText.SelectedIndex(new List<TakenActor>(), 7), -1);

		// ---- the empty states --------------------------------------------------------------------------
		r.Str("an-empty-battle-says-what-is-missing",
			TakenPageText.Lines(null, false, true, 0)[0].Text, "  本场还没有受击记录(还没有人挨打)");
		r.Str("outside-a-battle-the-page-no-longer-claims-it-only-accumulates-live",
			TakenPageText.Lines(null, false, false, 0)[0].Text, "  没有可看的受击记录(本场与上一场都没有)");
		r.Str("an-empty-section-outside-a-battle-says-the-same",
			TakenPageText.Lines(new TakenBreakdown(), false, false, 0)[0].Text,
			"  没有可看的受击记录(本场与上一场都没有)");
		// R84: nothing to choose from means no list at all -- not an empty caption, not a stray marker.
		List<TakenLine> empty = TakenPageText.Lines(new TakenBreakdown(), false, false, 0);
		r.Eq("an-empty-page-offers-no-character-entries", TkEntries(empty).Count, 0);
		r.Eq("and-no-line-of-an-empty-page-carries-segments", TkNameRows(empty), 0);
	}

	/// <summary>Two of our units (one 前衛 hit twice, one 後衛) plus one enemy victim: enough to exercise every
	/// summary line and both filters. レヴァナント carries 300 nominal over two sources but one attacker.</summary>
	private static TakenBreakdown TkSample()
	{
		var hits = new List<TakenHit>();
		TakenHit a = TkPageHit(1, "レヴァナント", 200, 200);
		a.Position = 1; a.Source = 1;
		hits.Add(a);
		TakenHit b = TkPageHit(1, "レヴァナント", 100, 100);
		b.Position = 1; b.Source = 4;
		hits.Add(b);
		TakenHit c = TkPageHit(3, "城塞", 50, 50);
		c.Position = 2; c.Source = 1;
		hits.Add(c);
		TakenHit e = TkPageHit(2, "敵", 100, 100);
		e.VictimTeam = 2;
		hits.Add(e);
		return TakenBreakdownPolicy.Build(hits, 1);
	}

	private static TakenBreakdown TkManyVictims(int count)
	{
		var hits = new List<TakenHit>();
		for (int i = 0; i < count; i++)
		{
			TakenHit h = TkPageHit(100 + i, "味方" + i, 10, 10);
			h.Position = 1;
			hits.Add(h);
		}
		return TakenBreakdownPolicy.Build(hits, 1);
	}

	/// <summary>One victim with five DISTINCT DamageSource values -- R79 printed three and folded the rest.</summary>
	private static TakenBreakdown TkFiveSources()
	{
		var hits = new List<TakenHit>();
		for (int i = 0; i < 5; i++)
		{
			TakenHit h = TkPageHit(1, "甲", 10, 10);
			h.Position = 1;
			h.Source = i + 1;
			h.AttackerKey = 10 + i;
			h.Attacker = "敵" + i;
			h.HitMatch = 1;
			hits.Add(h);
		}
		return TakenBreakdownPolicy.Build(hits, 1);
	}

	private static TakenHit TkPageHit(int victimKey, string victim, long nominal, long amount)
	{
		var h = new TakenHit();
		h.VictimKey = victimKey;
		h.Victim = victim;
		h.VictimTeam = 1;
		h.AttackerKey = 10;
		h.Attacker = "ボス";
		h.Attr = "A";
		h.Source = 1;
		h.HitType = 2;
		h.EffectId = 0;
		h.HitMatch = 1;
		h.Nominal = nominal;
		h.Amount = amount;
		return h;
	}

	/// <summary>The whitespace-separated cells of a built row. Every column is space padded, and no name in
	/// these cases contains a space, so this is the row's own column split.</summary>
	private static string[] TkCells(string row)
	{
		return row.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
	}

	/// <summary>The amount cell of the sub-table that <paramref name="subHeader"/> opens (the nth one), i.e.
	/// what the bucket rows above it added up to.</summary>
	private static string TkSubTotal(List<TakenLine> lines, string subHeader, int nth)
	{
		int seen = 0;
		for (int i = 0; i < lines.Count; i++)
		{
			if (lines[i].Text == null || !lines[i].Text.Contains(subHeader)) continue;
			if (seen++ != nth) continue;
			for (int j = i + 1; j < lines.Count; j++)
			{
				string t = lines[j].Text;
				if (t == null) continue;
				if (t.StartsWith("  合计")) return TkCells(t)[1];
				if (t.StartsWith("  - ")) break;   // the next sub-table started: no total in between
			}
			return null;
		}
		return null;
	}

	/// <summary>How many bucket rows the sub-table opened by <paramref name="subHeader"/> printed: the lines
	/// between its column header and its closing total.</summary>
	private static int TkBucketRows(List<TakenLine> lines, string subHeader)
	{
		for (int i = 0; i < lines.Count; i++)
		{
			if (lines[i].Text == null || !lines[i].Text.Contains(subHeader)) continue;
			int n = 0;
			for (int j = i + 2; j < lines.Count; j++)   // +2: skip the label and the column header
			{
				string t = lines[j].Text;
				if (t == null) continue;
				if (t.StartsWith("  合计")) return n;
				if (t.StartsWith("  - ")) return n;
				n++;
			}
		}
		return 0;
	}

	private static bool TkAny(List<TakenLine> lines, string part)
	{
		for (int i = 0; i < lines.Count; i++)
			if (lines[i].Text != null && lines[i].Text.Contains(part)) return true;
		return false;
	}

	private static int TkCount(List<TakenLine> lines, string part)
	{
		int n = 0;
		for (int i = 0; i < lines.Count; i++)
			if (lines[i].Text != null && lines[i].Text.Contains(part)) n++;
		return n;
	}

	/// <summary>Victim rows are the only lines that start with a position cell.</summary>
	private static int TkRows(List<TakenLine> lines)
	{
		int n = 0;
		for (int i = 0; i < lines.Count; i++)
		{
			string t = lines[i].Text;
			if (t == null) continue;
			if (t.StartsWith("  前衛") || t.StartsWith("  後衛") || t.StartsWith("  站位未知")) n++;
		}
		return n;
	}

	/// <summary>R81: the section tag of the line that opens the sub-table named by <paramref name="subHeader"/>.
	/// A missing label returns None, so the case that asked for it fails rather than silently passing.</summary>
	private static TakenBlock TkBlockOf(List<TakenLine> lines, string subHeader)
	{
		for (int i = 0; i < lines.Count; i++)
		{
			string t = lines[i].Text;
			if (t != null && t.StartsWith(subHeader)) return lines[i].Block;
		}
		return TakenBlock.None;
	}

	/// <summary>R81: how many lines carry a section tag -- only a sub-table's label row should.</summary>
	private static int TkTaggedRows(List<TakenLine> lines)
	{
		int n = 0;
		for (int i = 0; i < lines.Count; i++)
			if (lines[i].Block != TakenBlock.None) n++;
		return n;
	}

	/// <summary>R81: distinct values in the section vocabulary, "no section" included. Two sections that share
	/// a value would be drawn in one colour, which defeats the point of the colours.</summary>
	private static int TkDistinctTags()
	{
		TakenBlock[] all =
		{
			TakenBlock.None, TakenBlock.Attacker, TakenBlock.HitType, TakenBlock.Attr, TakenBlock.Effect,
			TakenBlock.Status, TakenBlock.Other,
		};
		var seen = new List<TakenBlock>();
		for (int i = 0; i < all.Length; i++)
			if (!seen.Contains(all[i])) seen.Add(all[i]);
		return seen.Count;
	}

	/// <summary>R84: the clickable entries the 角色 list put on the page, in page order. Every other line of
	/// this page has no segments, so an empty result is how "there is no list" is checked.</summary>
	private static List<HotkeySeg> TkEntries(List<TakenLine> lines)
	{
		var found = new List<HotkeySeg>();
		for (int i = 0; i < lines.Count; i++)
		{
			List<HotkeySeg> segs = lines[i].Segments;
			if (segs == null) continue;
			for (int s = 0; s < segs.Count; s++)
				if (segs[s].Action != HotkeyAction.None) found.Add(segs[s]);
		}
		return found;
	}

	/// <summary>R84: the DISTINCT actions the list's entries submit. The list is supposed to submit exactly
	/// one (the character action), so a keyboard action leaking into the list shows up here.</summary>
	private static string TkEntryActions(List<TakenLine> lines)
	{
		List<HotkeySeg> entries = TkEntries(lines);
		var seen = new List<string>();
		for (int i = 0; i < entries.Count; i++)
		{
			string action = entries[i].Action.ToString();
			if (!seen.Contains(action)) seen.Add(action);
		}
		return string.Join(",", seen.ToArray());
	}

	/// <summary>R84: the argument of every entry, in order -- the position each name submits.</summary>
	private static string TkEntryArgs(List<TakenLine> lines)
	{
		List<HotkeySeg> entries = TkEntries(lines);
		var parts = new List<string>();
		for (int i = 0; i < entries.Count; i++) parts.Add(entries[i].Arg.ToString());
		return string.Join(",", parts.ToArray());
	}

	/// <summary>R84: the name of every entry, in order, with the marker and the padding removed.</summary>
	private static string TkEntryNames(List<TakenLine> lines)
	{
		List<HotkeySeg> entries = TkEntries(lines);
		var parts = new List<string>();
		for (int i = 0; i < entries.Count; i++) parts.Add(TkEntryName(entries[i]));
		return string.Join(",", parts.ToArray());
	}

	/// <summary>R84: the entries carrying the "on screen" marker, as the words a reader sees. Two markers (or
	/// none) make the expectation fail rather than pass as one.</summary>
	private static string TkMarked(List<TakenLine> lines)
	{
		List<HotkeySeg> entries = TkEntries(lines);
		var parts = new List<string>();
		for (int i = 0; i < entries.Count; i++)
		{
			string text = (entries[i].Text ?? "").Trim();
			if (text.StartsWith("▶")) parts.Add(text);
		}
		return string.Join(",", parts.ToArray());
	}

	/// <summary>An entry's name: its text without the marker and without the padding that separates it from
	/// the next entry.</summary>
	private static string TkEntryName(HotkeySeg seg)
	{
		string text = (seg.Text ?? "").Trim();
		if (text.StartsWith("▶")) text = text.Substring(1).Trim();
		return text;
	}

	/// <summary>R84: how many lines of the page carry entries -- i.e. how many rows the 角色 list took. The
	/// caption is a row of its own, so this counts NAME rows only.</summary>
	private static int TkNameRows(List<TakenLine> lines)
	{
		int n = 0;
		for (int i = 0; i < lines.Count; i++)
			if (lines[i].Segments != null) n++;
		return n;
	}

	/// <summary>R84: the most entries any one name row carries -- the wrap width as it was actually built.</summary>
	private static int TkMaxEntriesPerRow(List<TakenLine> lines)
	{
		int most = 0;
		for (int i = 0; i < lines.Count; i++)
		{
			List<HotkeySeg> segs = lines[i].Segments;
			if (segs == null) continue;
			int n = 0;
			for (int s = 0; s < segs.Count; s++)
				if (segs[s].Action != HotkeyAction.None) n++;
			if (n > most) most = n;
		}
		return most;
	}

	/// <summary>R84: one cell of the first row that opens with <paramref name="prefix"/>. A missing row or a
	/// missing cell returns "", so the caller's expectation fails instead of the run crashing.</summary>
	private static string TkCellOf(List<TakenLine> lines, string prefix, int index)
	{
		string row = "";
		for (int i = 0; i < lines.Count; i++)
		{
			string t = lines[i].Text;
			if (t != null && t.StartsWith(prefix)) { row = t; break; }
		}
		string[] cells = TkCells(row);
		return index < cells.Length ? cells[index] : "";
	}
}
