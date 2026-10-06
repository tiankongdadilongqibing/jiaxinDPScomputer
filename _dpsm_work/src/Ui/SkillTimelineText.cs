using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DpsMeter;

/// <summary>How the overlay should colour one line of the 技能时间表. The mapping to a Unity colour
/// lives in the renderer, so this file stays game-free and the text it produces is executable in
/// BehaviorTests.</summary>
internal enum TimelineLineStyle
{
	Header = 0,
	Dim = 1,
	Row = 2,
	Warn = 3,
}

internal struct TimelineLine
{
	internal string Text;
	internal TimelineLineStyle Style;

	internal TimelineLine(string text, TimelineLineStyle style)
	{
		Text = text;
		Style = style;
	}
}

/// <summary>
/// R66: the TEXT of the 技能时间表 page -- "when did each of our units fire its 奥义 / 特殊 / 自动 skill".
///
/// WHY THIS IS ITS OWN FILE. The page exists because a cadence question ("how often does it fire?") cannot
/// be answered from a single number: the CHARGE (Skill.CoolTimeFrame / 30) and the OBSERVED interval
/// differ, because an auto skill that has finished charging waits for the unit's next normal attack
/// (measured 2026-10-06: トレイラ, CoolTimeFrame 240 = 8.0 game s, median observed gap 9.00 game s). So the
/// page has to show the individual activation stamps, not just a median -- and the stamps, the ×N burst
/// marker, the channel diagnosis and the truncation are all things a reader must be able to check:
/// they are produced here, compiled into BehaviorTests, and printed into the battle-end log ([SKILLTL])
/// so the same text is verifiable offline.
///
/// GEOMETRY. One line per (unit, skill). The widths below are the 1:2 grid the mono font gives (a CJK
/// glyph is 2 columns), the same assumption the 总贡献 table makes; the panel is widened for this page in
/// OverlayUGUI.Rows.LayoutCharts. A row is NEVER truncated silently: the stamps stop at
/// <see cref="SkillTimelinePolicy.MaxStamps"/> with an explicit "+N" and the extra ROWS are reported in
/// the footer.
/// </summary>
internal static class SkillTimelineText
{
	internal const int NameW = 16;
	internal const int KindW = 6;
	internal const int SkillW = 18;
	/// <summary>Width of ONE activation stamp. R67: 5 -> 6. MEASURED 2026-10-06 on the user's screenshot:
	/// with 5 columns a 3-digit stamp ("107.3") filled its cell completely and the row read
	/// `96.2107.3118.3`. 6 columns keep at least one space in front of every stamp below 10000 s.</summary>
	internal const int StampW = 6;
	/// <summary>Width reserved for the `并N条M格` burst mark, the activation count and the median gap. The
	/// tail is NOT truncated: it is the last column, so an over-long tail costs trailing width only, and
	/// hiding the median behind ".." would hide exactly the number the page exists to publish.</summary>
	internal const int TailW = 25;

	/// <summary>The maximum line width in display columns, pinned by a behaviour test so the panel width
	/// chosen in the renderer cannot silently disagree with the table it has to fit.</summary>
	internal static int LineWidth
	{
		get { return 2 + NameW + KindW + SkillW + StampW * SkillTimelinePolicy.MaxStamps + TailW; }
	}

	/// <summary>The whole page as coloured lines. <paramref name="events"/> is the RAW activation stream
	/// (both channels); the grouping, the merge window and the medians come from the policy, so this
	/// method only decides how it reads.</summary>
	internal static List<TimelineLine> Rows(IList<SkillTimelineEvent> events, bool inBattle)
	{
		var lines = new List<TimelineLine>();
		int cmd = 0, rec = 0, skl = 0;
		if (events != null)
		{
			for (int i = 0; i < events.Count; i++)
			{
				SkillTimelineEvent e = events[i];
				if (e == null) continue;
				if (e.Channel == SkillTimelineEvent.ChannelRecord) rec++;
				else if (e.Channel == SkillTimelineEvent.ChannelSkillCommand) skl++;
				else cmd++;
			}
		}

		lines.Add(new TimelineLine("技能时间表  我方奥义/特殊/自动技能发动时刻   F4 返回", TimelineLineStyle.Header));
		// R67: the burst mark now states BOTH numbers (rows folded, cells they landed in), because R66's
		// `xN` read as "N in one cell" and was false whenever the folds were spread over several cells.
		lines.Add(new TimelineLine(
			"  单位:战斗时钟秒(游戏秒)  并N条M格 = N 条发动(间隔<"
			+ SkillTimelinePolicy.MergeSeconds.ToString("F2", CultureInfo.InvariantCulture)
			+ "s)并进了 M 个格子  med = 合并后相邻格子间隔的中位数", TimelineLineStyle.Dim));

		if (cmd + rec + skl == 0)
		{
			lines.Add(new TimelineLine(
				inBattle ? "  (本场尚未观测到我方技能发动)" : "  (未在战斗中,也没有上一场的记录)",
				TimelineLineStyle.Warn));
			lines.Add(new TimelineLine("  " + ChannelLine(cmd, skl, rec), TimelineLineStyle.Dim));
			return lines;
		}
		if (!inBattle)
			lines.Add(new TimelineLine(
				"  (未在战斗中:下面是我方最近一场的技能时间表)", TimelineLineStyle.Warn));

		List<SkillTimelineGroup> groups = SkillTimelinePolicy.Group(events);
		int merged = 0, rows = 0;
		for (int i = 0; i < groups.Count; i++) { merged += groups[i].Merged; rows += groups[i].Stamps.Count; }
		lines.Add(new TimelineLine("  原始 " + (cmd + rec + skl).ToString(CultureInfo.InvariantCulture) + " 条 -> "
			+ groups.Count.ToString(CultureInfo.InvariantCulture) + " 行 / "
			+ rows.ToString(CultureInfo.InvariantCulture) + " 次发动(合并 "
			+ merged.ToString(CultureInfo.InvariantCulture) + " 条)", TimelineLineStyle.Dim));
		lines.Add(new TimelineLine("  " + ChannelLine(cmd, skl, rec), TimelineLineStyle.Dim));
		// R67: 奥义/特殊 have two channels now (skl = command hooks, rec = record sink). The warning fires
		// only when BOTH are empty -- one of them observing is enough to stop calling the column blind.
		if (skl + rec == 0)
		{
			lines.Add(new TimelineLine("  奥义/特殊 两条通道(skl/rec)本场都是 0 条。", TimelineLineStyle.Warn));
			lines.Add(new TimelineLine("  若本场确有奥义,是钩子未命中(见 [SKILLTL] SUM 的调用计数),不是没发动。",
				TimelineLineStyle.Warn));
		}

		lines.Add(new TimelineLine(
			"  " + DisplayFormat.PadR("角色", NameW) + DisplayFormat.PadR("种类", KindW)
			+ DisplayFormat.PadR("技能", SkillW)
			+ DisplayFormat.PadR("发动时刻(战斗时钟秒)", StampW * SkillTimelinePolicy.MaxStamps)
			+ DisplayFormat.PadR("次数/中位间隔", TailW), TimelineLineStyle.Dim));

		if (groups.Count == 0)
		{
			lines.Add(new TimelineLine("  (有观测记录,但没有一行能落到时间轴上 -- 见 [SKILLTL] 日志)",
				TimelineLineStyle.Warn));
			return lines;
		}

		int shown = groups.Count;
		if (shown > SkillTimelinePolicy.MaxGroups) shown = SkillTimelinePolicy.MaxGroups;
		for (int i = 0; i < shown; i++) lines.Add(GroupLine(groups[i]));
		if (groups.Count > shown)
			lines.Add(new TimelineLine("  ... 还有 " + (groups.Count - shown).ToString(CultureInfo.InvariantCulture)
				+ " 行(共 " + groups.Count.ToString(CultureInfo.InvariantCulture) + " 行;F6 明细有逐次伤害)",
				TimelineLineStyle.Dim));
		return lines;
	}

	/// <summary>One unit+skill row: name, kind, skill, up to MaxStamps stamps, then the burst marker, the
	/// activation count and the median gap.</summary>
	internal static TimelineLine GroupLine(SkillTimelineGroup g)
	{
		var sb = new StringBuilder(LineWidth + 8);
		sb.Append("  ");
		sb.Append(DisplayFormat.PadR(DisplayFormat.Fit(DisplayFormat.Cell(g.Unit), NameW), NameW));
		sb.Append(DisplayFormat.PadR(DisplayFormat.Fit(SkillTimelinePolicy.KindLabel(g.Type), KindW), KindW));
		sb.Append(DisplayFormat.PadR(DisplayFormat.Fit(DisplayFormat.Cell(g.Skill), SkillW), SkillW));

		int n = g.Stamps.Count;
		// Overflow is not allowed to widen the row: when there are more stamps than fit, the LAST cell is
		// spent on the `+N` mark instead of on a stamp, so the field stays exactly StampW*MaxStamps wide
		// and the columns to its right cannot move. `+N` therefore means "N further activations were not
		// printed", never "one more".
		bool overflow = n > SkillTimelinePolicy.MaxStamps;
		int shown = overflow ? SkillTimelinePolicy.MaxStamps - 1 : n;
		var stamps = new StringBuilder(StampW * SkillTimelinePolicy.MaxStamps);
		for (int i = 0; i < shown; i++)
			stamps.Append(DisplayFormat.PadL(g.Stamps[i].ToString("F1", CultureInfo.InvariantCulture), StampW));
		if (overflow)
			stamps.Append(DisplayFormat.PadL("+" + (n - shown).ToString(CultureInfo.InvariantCulture), StampW));
		sb.Append(DisplayFormat.PadR(stamps.ToString(), StampW * SkillTimelinePolicy.MaxStamps));

		var tail = new StringBuilder(28);
		// R67: N rows folded, M cells they landed in. Two numbers, because one number cannot tell "a
		// triple in one cell" from "two doubles in two cells" (R66 printed x3 for both).
		if (g.Merged > 0)
			tail.Append('并').Append(g.Merged.ToString(CultureInfo.InvariantCulture)).Append('条')
				.Append(g.BurstCells.ToString(CultureInfo.InvariantCulture)).Append("格 ");
		tail.Append("n=").Append(n.ToString(CultureInfo.InvariantCulture));
		double med = SkillTimelinePolicy.MedianInterval(g);
		if (med > 0.0)
			tail.Append(" med=").Append(med.ToString("F2", CultureInfo.InvariantCulture)).Append('s');
		// NOT truncated: the tail is the last column, so an over-long tail costs trailing width only, and
		// hiding the median behind a ".." would hide exactly the number the page exists to publish.
		sb.Append(tail.ToString());
		return new TimelineLine(sb.ToString(), SkillTimelinePolicy.IsAuto(g.Type) ? TimelineLineStyle.Row
			: TimelineLineStyle.Header);
	}

	/// <summary>Which channels produced the rows. Printed also when the page is empty, because "the record
	/// hook produced nothing" and "our units fired nothing" are different statements.</summary>
	internal static string ChannelLine(int cmd, int skl, int rec)
	{
		return "观测通道 cmd(自动技能命令) " + cmd.ToString(CultureInfo.InvariantCulture)
			+ " 条 / skl(奥义特殊命令) " + skl.ToString(CultureInfo.InvariantCulture)
			+ " 条 / rec(技能记录) " + rec.ToString(CultureInfo.InvariantCulture) + " 条";
	}
}
