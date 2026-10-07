using System.Collections.Generic;

namespace DpsMeter;

/// <summary>R79: the 受击来源拆分 page's line styles, mapped to the panel palette by the renderer the same
/// way <see cref="TimelineLineStyle"/> is -- the pure layer names the role, the UI picks the colour.</summary>
internal enum TakenLineStyle
{
	Header = 0,
	Row = 1,
	Dim = 2,
	Warn = 3,
}

/// <summary>One finished page line: the exact string on screen plus what it is.</summary>
internal struct TakenLine
{
	public string Text;
	public TakenLineStyle Style;
}

/// <summary>
/// R79 受击来源拆分 page: turns one <see cref="TakenBreakdown"/> into the lines the panel shows.
///
/// Pure on purpose (no UnityEngine, no game types): the same rule the 技能时间表 page follows, so what the
/// panel displays can be executed by tests/BehaviorTests instead of being eyeballed. The amounts are the
/// GAME's accounted damage (nominal) and every dimension partitions that same total, so a row here can be
/// reconciled line by line against the exported takenBreakdown section.
///
/// R80 made the page COMPLETE: every victim is printed (R79 stopped at 12) and every bucket gets its own
/// row (R79 printed at most three per dimension and folded the rest into 其余N项, on top of the 64-bucket cap
/// that also truncated the export). The shape now follows the 总贡献 table: a column header, one row per
/// entry, and a closing total per block, scrolling instead of folding.
/// </summary>
internal static class TakenPageText
{
	public static List<TakenLine> Lines(TakenBreakdown b, bool vanguardOnly, bool inBattle)
	{
		var lines = new List<TakenLine>();
		if (b == null || b.Actors.Count == 0)
		{
			// R80: b is null only when there is NEITHER a live session NOR a finished battle to show, so the
			// old "只在战斗中累积" sentence would now be a lie -- the previous battle is shown too.
			Add(lines, inBattle
				? "  本场还没有受击记录(还没有人挨打)"
				: "  没有可看的受击记录(本场与上一场都没有)", TakenLineStyle.Dim);
			return lines;
		}

		long enemyNominal = 0;
		int enemyCount = 0;
		long allyNominal = 0, allyTaken = 0, allyResidual = 0, allyHits = 0;
		long vg = 0, rg = 0, unk = 0;
		var mine = new List<TakenActor>();
		for (int i = 0; i < b.Actors.Count; i++)
		{
			TakenActor a = b.Actors[i];
			if (!a.Ally)
			{
				enemyCount++;
				enemyNominal += a.Nominal;
				continue;
			}
			allyNominal += a.Nominal;
			allyTaken += a.Taken;
			allyResidual += a.Residual;
			allyHits += a.Hits;
			if (a.Position == 1) vg += a.Nominal;
			else if (a.Position == 2) rg += a.Nominal;
			else unk += a.Nominal;
			if (!vanguardOnly || a.Position == 1) mine.Add(a);
		}

		// The totals line states the ONE scope question this page can get wrong, so it answers it on the
		// first line: the game figure covers both sides, the table below only our side.
		Add(lines, "击 " + DisplayFormat.Num(b.Hits)
			+ "   受击(游戏口径) " + DisplayFormat.Fmt(b.Nominal)
			+ "   已发布 " + DisplayFormat.Fmt(b.Taken)
			+ "   超出剩余耐久 " + DisplayFormat.Fmt(b.Residual), TakenLineStyle.Header);
		Add(lines, "  我方 " + DisplayFormat.Fmt(allyNominal) + "   敌方 " + DisplayFormat.Fmt(enemyNominal)
			+ "(" + DisplayFormat.Num(enemyCount) + " 人未列入下表)   同队自伤 " + DisplayFormat.Fmt(b.Friendly)
			+ "   攻击者不明 " + DisplayFormat.Fmt(b.Unknown), TakenLineStyle.Dim);
		Add(lines, "  站位(该单位首次受击时的快照,之后移动不改写)  前衛 " + DisplayFormat.Fmt(vg)
			+ "   後衛 " + DisplayFormat.Fmt(rg) + "   站位未知 " + DisplayFormat.Fmt(unk), TakenLineStyle.Dim);

		Add(lines, "", TakenLineStyle.Dim);
		Add(lines, "【受击角色】(仅我方;按游戏口径降序"
			+ (vanguardOnly ? ";只看前衛" : "") + ")", TakenLineStyle.Header);
		Add(lines, "  口径:受击=游戏自己记账的承伤;已发布=本插件记账;超出剩余耐久=前者-后者,不是被吸收",
			TakenLineStyle.Dim);
		Add(lines, TakenColumns.Header(), TakenLineStyle.Dim);

		for (int i = 0; i < mine.Count; i++)
		{
			TakenActor a = mine[i];
			if (i > 0) Add(lines, "", TakenLineStyle.Dim);
			double share = allyNominal > 0 ? 100.0 * a.Nominal / allyNominal : 0.0;
			Add(lines, TakenColumns.T1Row(TakenBreakdownPolicy.PositionLabel(a.Position), a.Name, a.Nominal,
			                              a.Taken, a.Residual, a.Hits, share), TakenLineStyle.Row);
			AddBuckets(lines, "单位(攻击者)", a.ByAttacker, a.Nominal);
			AddBuckets(lines, "种类(DamageSource)", a.BySource, a.Nominal);
			AddBuckets(lines, "属性(eDamageCalcType)", a.ByHitType, a.Nominal);
			AddBuckets(lines, "效果(m_effectId)", a.ByEffect, a.Nominal);
			AddStatuses(lines, a.ByStatus, a.Nominal);
			AddOther(lines, a, a.Nominal);
		}
		if (mine.Count > 0)
			Add(lines, TakenColumns.T1TotalsLine(allyNominal, allyTaken, allyResidual, allyHits), TakenLineStyle.Row);

		Add(lines, "", TakenLineStyle.Dim);
		Add(lines, "  注:单位=事件记的攻击者显示名;种类=DamageSource;属性=eDamageCalcType", TakenLineStyle.Dim);
		Add(lines, "      带 * 的名字只对该维度的部分命中权威(别的命中只认到攻击者/目标);效果=技能的 m_effectId",
			TakenLineStyle.Dim);
		Add(lines, "      状态=命中的异常与游戏记的付与者;每一节的合计等于该单位的受击(口径)总额", TakenLineStyle.Dim);
		Add(lines, "  (数字与导出的 takenBreakdown 段同源;进行中的战斗每秒最多重算一次,已结束的战斗只算一次)",
			TakenLineStyle.Dim);
		return lines;
	}

	/// <summary>
	/// One dimension's sub-table: a label line, the column header, one row per bucket and the closing total.
	/// Nothing is folded away any more -- a victim with 300 distinct attackers gets 300 rows, which is the
	/// whole point of R80: this page exists to be analysed, not summarised.
	/// </summary>
	private static void AddBuckets(List<TakenLine> lines, string label, List<TakenBucket> buckets, long nominal)
	{
		if (buckets == null || buckets.Count == 0) return;
		Add(lines, TakenColumns.BSubHeader(label, buckets.Count), TakenLineStyle.Dim);
		Add(lines, TakenColumns.BHeader(), TakenLineStyle.Dim);
		long sum = 0;
		long hits = 0;
		for (int i = 0; i < buckets.Count; i++)
		{
			TakenBucket bk = buckets[i];
			sum += bk.Amount;
			hits += bk.Hits;
			Add(lines, TakenColumns.BRow(bk.Name, bk.Amount, bk.Hits, Share(bk.Amount, nominal),
			                             !string.IsNullOrEmpty(bk.Quality)), TakenLineStyle.Row);
		}
		Add(lines, TakenColumns.BTotal("合计", sum, hits), TakenLineStyle.Dim);
	}

	/// <summary>The ailments this victim suffered, with the applier the GAME credited -- for damage over time
	/// that is not the hit's attacker, so the two must not be conflated. Same table shape as a dimension.</summary>
	private static void AddStatuses(List<TakenLine> lines, List<TakenStatus> list, long nominal)
	{
		if (list == null || list.Count == 0) return;
		Add(lines, TakenColumns.BSubHeader("状态(异常/付与者)", list.Count), TakenLineStyle.Dim);
		Add(lines, TakenColumns.BHeader(), TakenLineStyle.Dim);
		long sum = 0;
		long hits = 0;
		for (int i = 0; i < list.Count; i++)
		{
			TakenStatus st = list[i];
			sum += st.Amount;
			hits += st.Hits;
			string name = string.IsNullOrEmpty(st.Applier) ? st.Status : st.Status + "(" + st.Applier + ")";
			Add(lines, TakenColumns.BRow(name, st.Amount, st.Hits, Share(st.Amount, nominal), false),
			    TakenLineStyle.Row);
		}
		Add(lines, TakenColumns.BTotal("合计", sum, hits), TakenLineStyle.Dim);
	}

	/// <summary>The two amounts that are IN the victim's nominal total but belong to no attacker bucket:
	/// same-team damage (friendly fire / 回復反転) and the hits whose attacker could not be resolved. Both
	/// are part of 受击, so neither may be dropped -- they are why the attacker buckets do not sum to the
	/// total on their own, and printing them keeps every block's arithmetic closed.</summary>
	private static void AddOther(List<TakenLine> lines, TakenActor a, long nominal)
	{
		if (a.Friendly <= 0 && a.Unknown <= 0) return;
		Add(lines, TakenColumns.BSubHeader("其他(不属于任何攻击者桶)", (a.Friendly > 0 ? 1 : 0) + (a.Unknown > 0 ? 1 : 0)),
		    TakenLineStyle.Dim);
		Add(lines, TakenColumns.BHeader(), TakenLineStyle.Dim);
		long sum = 0;
		long hits = 0;
		if (a.Friendly > 0)
		{
			sum += a.Friendly;
			hits += a.FriendlyHits;
			Add(lines, TakenColumns.BRow("同队自伤", a.Friendly, a.FriendlyHits, Share(a.Friendly, nominal), false),
			    TakenLineStyle.Warn);
		}
		if (a.Unknown > 0)
		{
			sum += a.Unknown;
			hits += a.UnknownHits;
			Add(lines, TakenColumns.BRow("攻击者不明", a.Unknown, a.UnknownHits, Share(a.Unknown, nominal), false),
			    TakenLineStyle.Warn);
		}
		Add(lines, TakenColumns.BTotal("合计", sum, hits), TakenLineStyle.Dim);
	}

	private static double Share(long amount, long nominal)
	{
		return nominal > 0 ? 100.0 * amount / nominal : 0.0;
	}

	private static void Add(List<TakenLine> lines, string text, TakenLineStyle style)
	{
		lines.Add(new TakenLine { Text = text, Style = style });
	}
}
