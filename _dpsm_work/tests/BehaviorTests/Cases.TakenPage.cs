using System.Collections.Generic;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// R79: the 受击来源拆分 page's geometry and text (`Ui/TakenColumns.cs`, `Ui/TakenPageText.cs`).
	///
	/// Two things are checked here that a screenshot cannot settle. First the WIDTH: every cell is padded on the
	/// assumption that a CJK glyph is two columns and an ASCII one is one, so a row whose measured width differs
	/// from its own header drifts column by column -- exactly the defect R69/R78 chased in the contribution
	/// table. Second the WORDS: the residual (超出剩余耐久) must never be printed as an absorption, and a
	/// best-effort bucket must carry its `*`, because both would otherwise read as measurements.
	/// </summary>
	internal static void TakenPageCases(Runner r)
	{
		r.Group("ui/taken-page");

		// ---- the geometry is one number, and the built rows obey it ------------------------------------
		r.Eq("the-victim-row-width-is-pinned", TakenColumns.T1LineWidth, 80);
		r.Eq("the-header-is-exactly-one-row-wide",
			DisplayFormat.DispWidth(TakenColumns.Header()), TakenColumns.T1LineWidth);
		r.Eq("a-victim-row-is-exactly-one-row-wide",
			DisplayFormat.DispWidth(TakenColumns.T1Row("前衛", "レヴァナント", 6000000, 5000000, 1000000, 42, 48.6)),
			TakenColumns.T1LineWidth);
		r.Eq("the-totals-row-is-exactly-one-row-wide",
			DisplayFormat.DispWidth(TakenColumns.T1TotalsLine(6000000, 5000000, 1000000, 42)),
			TakenColumns.T1LineWidth);
		// A long name is fitted, so it can never push the numeric columns right (the R69 defect).
		r.Eq("an-overlong-name-does-not-widen-the-row",
			DisplayFormat.DispWidth(TakenColumns.T1Row("站位未知", "非常に長い名前のボスキャラクター", 1, 1, 0, 1, 100.0)),
			TakenColumns.T1LineWidth);

		// The per-dimension lines are variable-length, so their bound is the panel: the contribution page's
		// widest table is 94 columns and R79 puts this page on the same panel width.
		var worst = new List<string>();
		worst.Add(TakenColumns.BucketPart("非常に長いボス名の単位", 1234567890123L, true));
		worst.Add(TakenColumns.BucketPart("もう一つの長い名前", 987654321098L, true));
		worst.Add(TakenColumns.RestPart(97, 555555555555L));
		r.True("a-worst-case-dimension-line-fits-the-wide-panel",
			DisplayFormat.DispWidth(TakenColumns.DimensionLine("单位", worst)) <= 94);

		// ---- the fold marker: an approximation may not look like a measurement -------------------------
		r.Str("an-approximate-bucket-is-marked", TakenColumns.BucketPart("ショゴス", 500000, true), "ショゴス* 500,000");
		r.Str("an-exact-bucket-carries-no-marker", TakenColumns.BucketPart("ポポロット", 90676, false), "ポポロット 90,676");
		r.Str("an-overlong-bucket-name-is-cut-and-still-marked",
			TakenColumns.BucketPart("非常に長いボスの名前です", 1, true), "非常に長い..* 1");

		// ---- the page ----------------------------------------------------------------------------------
		List<TakenLine> lines = TakenPageText.Lines(TkSample(), false, true);
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

		// ---- the two filters ---------------------------------------------------------------------------
		r.Eq("the-unfiltered-page-shows-both-our-rows", TkRows(TakenPageText.Lines(TkSample(), false, true)), 2);
		r.Eq("the-vanguard-filter-keeps-only-the-front-row", TkRows(TakenPageText.Lines(TkSample(), true, true)), 1);
		// the totals line keeps stating the WHOLE battle, so a narrowed table cannot be misread as the picture
		r.Str("the-vanguard-filter-does-not-move-the-headline-total",
			TakenPageText.Lines(TkSample(), true, true)[0].Text,
			"击 4   受击(游戏口径) 450   已发布 450   超出剩余耐久 0");

		// ---- the cap is stated, not silent -------------------------------------------------------------
		List<TakenLine> many = TakenPageText.Lines(TkManyVictims(TakenPageText.ShownVictims + 3), false, true);
		r.Eq("the-cap-is-twelve", TakenPageText.ShownVictims, 12);
		r.True("the-dropped-victims-are-named-with-a-count", TkAny(many, "另有 3 人"));
		r.True("and-the-page-points-at-the-export-that-has-them", TkAny(many, "takenBreakdown"));
		r.Eq("the-shown-victim-rows-stop-at-the-cap", TkRows(many), TakenPageText.ShownVictims);
		r.Eq("no-cap-line-when-nothing-was-dropped", TkCount(lines, "另有"), 0);

		// ---- the empty states --------------------------------------------------------------------------
		r.Str("an-empty-battle-says-what-is-missing",
			TakenPageText.Lines(null, false, true)[0].Text, "  本场还没有受击记录(还没有人挨打)");
		r.Str("outside-a-battle-the-page-says-so",
			TakenPageText.Lines(null, false, false)[0].Text, "  不在战斗中;受击来源拆分只在战斗中累积");
		r.Str("an-empty-section-outside-a-battle-still-says-so",
			TakenPageText.Lines(new TakenBreakdown(), false, false)[0].Text,
			"  不在战斗中;受击来源拆分只在战斗中累积");

		// ---- the per-victim block ----------------------------------------------------------------------
		r.True("the-block-lists-the-source-dimension", TkAny(lines, "  - 种类  "));
		r.True("the-block-lists-the-attacker-dimension", TkAny(lines, "  - 单位  "));
		r.True("the-block-lists-the-hit-type-dimension", TkAny(lines, "  - 属性  "));
		r.True("the-block-lists-the-effect-dimension", TkAny(lines, "  - 效果  "));
		r.True("the-legend-says-the-dimensions-are-not-all-authoritative", TkAny(lines, "带 * 的只对该维度的部分命中权威"));
		r.True("the-legend-names-the-export-section-it-mirrors", TkAny(lines, "(每秒最多重算一次;数字与导出的 takenBreakdown 段同源)"));

		// A friendly/unknown amount must be visible as such: they are IN the total but belong to no attacker.
		var mixed = new List<TakenHit>();
		TakenHit fr = TkPageHit(1, "甲", 100, 100);
		fr.Friendly = true;
		mixed.Add(fr);
		TakenHit un = TkPageHit(1, "甲", 50, 50);
		un.AttackerKey = 0; un.Attacker = ""; un.Attr = "?";
		mixed.Add(un);
		List<TakenLine> odd = TakenPageText.Lines(TakenBreakdownPolicy.Build(mixed, 1), false, true);
		r.True("the-two-odd-amounts-get-their-own-line", TkAny(odd, "  - 其他  "));
		r.True("friendly-damage-is-named", TkAny(odd, "同队自伤"));
		r.True("an-unresolvable-attacker-is-named", TkAny(odd, "攻击者不明 50(1 击)"));
	}

	/// <summary>Two of our units (one 前衛 hit twice, one 後衛) plus one enemy victim: enough to exercise every
	/// summary line and both filters.</summary>
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
}
