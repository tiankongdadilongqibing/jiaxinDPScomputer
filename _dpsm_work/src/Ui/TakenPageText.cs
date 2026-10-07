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
/// </summary>
internal static class TakenPageText
{
	/// <summary>How many victim blocks the page prints. The rest stay in the export, and the page says so
	/// instead of silently stopping at the cap.</summary>
	public const int ShownVictims = 12;

	public static List<TakenLine> Lines(TakenBreakdown b, bool vanguardOnly, bool inBattle)
	{
		var lines = new List<TakenLine>();
		if (b == null || b.Actors.Count == 0)
		{
			Add(lines, inBattle
				? "  本场还没有受击记录(还没有人挨打)"
				: "  不在战斗中;受击来源拆分只在战斗中累积", TakenLineStyle.Dim);
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

		for (int i = 0; i < mine.Count && i < ShownVictims; i++)
		{
			TakenActor a = mine[i];
			double share = allyNominal > 0 ? 100.0 * a.Nominal / allyNominal : 0.0;
			Add(lines, TakenColumns.T1Row(TakenBreakdownPolicy.PositionLabel(a.Position), a.Name, a.Nominal,
			                              a.Taken, a.Residual, a.Hits, share), TakenLineStyle.Row);
			AddDimension(lines, "单位", a.ByAttacker);
			AddDimension(lines, "种类", a.BySource);
			AddDimension(lines, "属性", a.ByHitType);
			AddDimension(lines, "效果", a.ByEffect);
			AddStatus(lines, a.ByStatus);
			AddOther(lines, a);
		}
		if (mine.Count > ShownVictims)
			Add(lines, "  ... 另有 " + DisplayFormat.Num(mine.Count - ShownVictims)
				+ " 人(导出 takenBreakdown 含全部)", TakenLineStyle.Dim);
		if (mine.Count > 0)
			Add(lines, TakenColumns.T1TotalsLine(allyNominal, allyTaken, allyResidual, allyHits), TakenLineStyle.Row);

		Add(lines, "", TakenLineStyle.Dim);
		Add(lines, "  注:单位=事件记的攻击者显示名;种类=DamageSource;属性=eDamageCalcType", TakenLineStyle.Dim);
		Add(lines, "      带 * 的只对该维度的部分命中权威;效果=技能的 m_effectId;状态=命中的异常与游戏记的付与者",
			TakenLineStyle.Dim);
		Add(lines, "  (每秒最多重算一次;数字与导出的 takenBreakdown 段同源)", TakenLineStyle.Dim);
		return lines;
	}

	/// <summary>One dimension line. <see cref="TakenColumns.ShownBuckets"/> is the maximum number of PARTS
	/// on the line (the folded remainder counts as one), which is what keeps a 3-part line inside the panel
	/// no matter how long the bucket names are.</summary>
	private static void AddDimension(List<TakenLine> lines, string label, List<TakenBucket> buckets)
	{
		if (buckets == null || buckets.Count == 0) return;
		bool hasRest = buckets.Count > TakenColumns.ShownBuckets;
		int shown = hasRest ? TakenColumns.ShownBuckets - 1 : buckets.Count;
		var parts = new List<string>();
		for (int i = 0; i < shown; i++)
			parts.Add(TakenColumns.BucketPart(buckets[i].Name, buckets[i].Amount, !string.IsNullOrEmpty(buckets[i].Quality)));
		if (hasRest)
		{
			long rest = 0;
			for (int i = shown; i < buckets.Count; i++) rest += buckets[i].Amount;
			parts.Add(TakenColumns.RestPart(buckets.Count - shown, rest));
		}
		Add(lines, TakenColumns.DimensionLine(label, parts), TakenLineStyle.Dim);
	}

	/// <summary>The ailments this victim suffered, with the applier the GAME credited -- for damage over time
	/// that is not the hit's attacker, so the two must not be conflated.</summary>
	private static void AddStatus(List<TakenLine> lines, List<TakenStatus> list)
	{
		if (list == null || list.Count == 0) return;
		bool hasRest = list.Count > TakenColumns.ShownBuckets;
		int shown = hasRest ? TakenColumns.ShownBuckets - 1 : list.Count;
		var parts = new List<string>();
		for (int i = 0; i < shown; i++)
		{
			string name = string.IsNullOrEmpty(list[i].Applier) ? list[i].Status : list[i].Status + "(" + list[i].Applier + ")";
			parts.Add(TakenColumns.BucketPart(name, list[i].Amount, false));
		}
		if (hasRest)
		{
			long rest = 0;
			for (int i = shown; i < list.Count; i++) rest += list[i].Amount;
			parts.Add(TakenColumns.RestPart(list.Count - shown, rest));
		}
		Add(lines, TakenColumns.DimensionLine("状态", parts), TakenLineStyle.Dim);
	}

	/// <summary>The two amounts that are IN the victim's nominal total but belong to no attacker bucket:
	/// same-team damage (friendly fire / 回復反転) and the hits whose attacker could not be resolved. Both
	/// are part of 受击, so neither may be dropped -- they are why the attacker buckets do not sum to the
	/// total on their own.</summary>
	private static void AddOther(List<TakenLine> lines, TakenActor a)
	{
		if (a.Friendly <= 0 && a.Unknown <= 0) return;
		var parts = new List<string>();
		if (a.Friendly > 0) parts.Add(TakenColumns.BucketPart("同队自伤", a.Friendly, false));
		// The hit count rides OUTSIDE the name: the name is fitted to BucketName columns, so folding a
		// "(N 击)" suffix into it would cut the very words that say what the amount is.
		if (a.Unknown > 0)
			parts.Add(TakenColumns.BucketPart("攻击者不明", a.Unknown, false)
				+ "(" + DisplayFormat.Num(a.UnknownHits) + " 击)");
		Add(lines, TakenColumns.DimensionLine("其他", parts), TakenLineStyle.Warn);
	}

	private static void Add(List<TakenLine> lines, string text, TakenLineStyle style)
	{
		lines.Add(new TakenLine { Text = text, Style = style });
	}
}
