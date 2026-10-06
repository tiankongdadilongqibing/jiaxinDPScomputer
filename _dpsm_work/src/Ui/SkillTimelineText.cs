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
	/// <summary>Activation stamps printed on the FIRST line of a row (the same number the policy publishes,
	/// so the two cannot drift).</summary>
	internal const int MaxStamps = SkillTimelinePolicy.MaxStamps;

	/// <summary>R70: how many CONTINUATION lines a row may take. The user asked for a row whose activation
	/// stamps do not fit to grow downwards instead of ending in `+N`; three extra lines carry 42 more stamps,
	/// so a row shows up to 51 activations. Bounded on purpose -- an unbounded row would let one unit own the
	/// page, and `+N` stays meaningful once even the continuations run out.</summary>
	internal const int MaxStampLines = 3;

	/// <summary>R70: stamps one continuation line carries. They start under the first line's stamp column
	/// (so the columns stay aligned) and need no tail, which is why a continuation fits more than a first
	/// line does.</summary>
	internal static int ContinuationStamps
	{
		get { return (LineWidth - Indent) / StampW; }
	}

	/// <summary>Leading columns before the first stamp cell: 2 spaces + the three identity columns.</summary>
	internal static int Indent
	{
		get { return 2 + NameW + KindW + SkillW; }
	}

	/// <summary>Width reserved for the `并N条M格` burst mark, the activation count, the R69 attempt count
	/// and the median gap. The tail is NOT truncated: it is the last column, so an over-long tail costs
	/// trailing width only, and hiding the median behind ".." would hide exactly the number the page exists
	/// to publish. R69: 25 -> 33, because the row now also carries `试N` (calls that did not execute) and
	/// that number is the whole point of this round -- the widest realistic tail is
	/// `并12条9格 n=24 试111 med=13.33s` = 31 columns.</summary>
	internal const int TailW = 33;

	/// <summary>The maximum line width in display columns, pinned by a behaviour test so the panel width
	/// chosen in the renderer cannot silently disagree with the table it has to fit.</summary>
	internal static int LineWidth
	{
		get { return 2 + NameW + KindW + SkillW + StampW * SkillTimelinePolicy.MaxStamps + TailW; }
	}

	/// <summary>The whole page as coloured lines. <paramref name="events"/> is the raw ACTIVATION stream
	/// (both channels) and <paramref name="attempts"/> is the R69 tally of calls that did NOT execute a
	/// skill; the grouping, the fold and the medians come from the policy, so this method only decides how
	/// it reads.
	///
	/// WHY THE ATTEMPTS ARE A SECOND INPUT AND NOT EVENTS. An attempt has no moment: it belongs to no cell
	/// of the time axis, must never enter the fold and must never enter a median. Giving it a timestamp and
	/// then filtering it out again is how R66/R67 published マッドシーカー's 試行 as 発動.</summary>
	internal static List<TimelineLine> Rows(IList<SkillTimelineEvent> events, IList<SkillTimelineAttempt> attempts,
		bool inBattle)
	{
		return Rows(events, attempts, 0, inBattle);
	}

	/// <summary>R70: the same page, plus the number of NON-ALLY observations the probe dropped
	/// (`SkillTimelineProbe.NoteForeign`). The count is printed because the page's title says 我方: without it
	/// a reader cannot tell "the enemy had no skills" from "the filter worked".</summary>
	internal static List<TimelineLine> Rows(IList<SkillTimelineEvent> events, IList<SkillTimelineAttempt> attempts,
		int foreignDropped, bool inBattle)
	{
		var lines = new List<TimelineLine>();
		int cmd = 0, skl = 0, tries = 0;
		if (events != null)
		{
			for (int i = 0; i < events.Count; i++)
			{
				SkillTimelineEvent e = events[i];
				if (e == null) continue;
				if (e.Channel == SkillTimelineEvent.ChannelSkillCommand) skl++;
				else cmd++;
			}
		}
		if (attempts != null)
		{
			for (int i = 0; i < attempts.Count; i++)
				if (attempts[i] != null && attempts[i].Count > 0) tries += attempts[i].Count;
		}

		lines.Add(new TimelineLine("技能时间表  我方奥义/特殊/自动技能发动时刻   F4 返回", TimelineLineStyle.Header));
		// R67: the burst mark states BOTH numbers (rows folded, cells they landed in), because R66's `xN`
		// read as "N in one cell" and was false whenever the folds were spread over several cells.
		// R69: the fold is the skill's own cooldown, not a fixed window, and `试N` is new -- so the legend
		// grew. It is TWO lines on purpose: as one line it reached 189 display columns against a pinned
		// width of 129, i.e. the bottom of the legend was the one thing on the page that did not fit
		// (measured 2026-10-06 by rendering the real battle's rows through this very file). The new case
		// `no-line-of-the-page-exceeds-the-pinned-width` is what keeps that from coming back.
		lines.Add(new TimelineLine(
			"  单位:战斗时钟秒(游戏秒)  并N条M格 = N 条发动并进了 M 格(间隔<该技能自己的冷却)",
			TimelineLineStyle.Dim));
		lines.Add(new TimelineLine(
			"  试N = 本场调用 N 次但没有发动(不计入时刻与中位)  med = 合并后相邻格子间隔的中位数  "
			+ "发动时刻一屏放不下时接着下一行(续行只印时刻)", TimelineLineStyle.Dim));

		if (cmd + skl + tries == 0)
		{
			lines.Add(new TimelineLine(
				inBattle ? "  (本场尚未观测到我方技能发动,也没有试触发)" : "  (未在战斗中,也没有上一场的记录)",
				TimelineLineStyle.Warn));
			lines.Add(new TimelineLine("  " + ChannelLine(cmd, skl, foreignDropped), TimelineLineStyle.Dim));
			return lines;
		}
		if (!inBattle)
			lines.Add(new TimelineLine(
				"  (未在战斗中:下面是我方最近一场的技能时间表)", TimelineLineStyle.Warn));

		List<SkillTimelineGroup> groups = SkillTimelinePolicy.Group(events);
		// R69: the attempts join the rows AFTER the fold, and this is also what materialises a row for a
		// skill that was only ever called while charging (0 activations, N attempts).
		SkillTimelinePolicy.ApplyAttempts(groups, attempts);
		int merged = 0, rows = 0;
		for (int i = 0; i < groups.Count; i++) { merged += groups[i].Merged; rows += groups[i].Stamps.Count; }
		lines.Add(new TimelineLine("  原始 " + (cmd + skl).ToString(CultureInfo.InvariantCulture) + " 条发动 + 试触发 "
			+ tries.ToString(CultureInfo.InvariantCulture) + " 条(未计入) -> "
			+ groups.Count.ToString(CultureInfo.InvariantCulture) + " 行 / "
			+ rows.ToString(CultureInfo.InvariantCulture) + " 次发动(合并 "
			+ merged.ToString(CultureInfo.InvariantCulture) + " 条)", TimelineLineStyle.Dim));
		lines.Add(new TimelineLine("  " + ChannelLine(cmd, skl, foreignDropped), TimelineLineStyle.Dim));
		// R69: `skl` is the only channel that observes 奥义/特殊 (the R66 record sink was deleted), so this
		// warning now fires on it alone -- and it names the counter to look at, because "the hook never ran"
		// and "nothing was accepted" are different facts.
		if (skl == 0)
		{
			lines.Add(new TimelineLine("  奥义/特殊 通道(skl)本场 0 条。", TimelineLineStyle.Warn));
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
		// R70: one row can now occupy several lines (see GroupLines), so the page is a LINE stream whose
		// first line of each row carries the identity and the tail.
		for (int i = 0; i < shown; i++) lines.AddRange(GroupLines(groups[i]));
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
		return GroupLines(g)[0];
	}

	/// <summary>
	/// R70: a row as one or more physical lines. The FIRST line is the row as R66/R67 printed it (identity,
	/// <see cref="MaxStamps"/> stamps, the tail); every further line carries
	/// <see cref="ContinuationStamps"/> more stamps, aligned under the first line's stamp column, and nothing
	/// else. The user's request was exactly this: a row whose activation moments do not fit should grow
	/// downwards instead of ending in `+N` after nine of them.
	///
	/// `+N` still exists, at the END of the last continuation line, and it still means "N further activations
	/// were not printed" -- the cap is <see cref="MaxStampLines"/> extra lines, so a row shows every stamp up
	/// to 9 + 3*14 = 51, and 50 stamps plus a `+N` cell beyond that (the mark always costs the cell it sits
	/// in). Truncation is therefore never silent, which is the invariant every version of this page has kept.
	/// </summary>
	internal static List<TimelineLine> GroupLines(SkillTimelineGroup g)
	{
		var lines = new List<TimelineLine>(1 + MaxStampLines);
		var sb = new StringBuilder(LineWidth + 8);
		sb.Append("  ");
		sb.Append(DisplayFormat.PadR(DisplayFormat.Fit(DisplayFormat.Cell(g.Unit), NameW), NameW));
		sb.Append(DisplayFormat.PadR(DisplayFormat.Fit(SkillTimelinePolicy.KindLabel(g.Type), KindW), KindW));
		sb.Append(DisplayFormat.PadR(DisplayFormat.Fit(DisplayFormat.Cell(g.Skill), SkillW), SkillW));

		int n = g.Stamps.Count;
		// The FIRST line carries up to MaxStamps stamps and never a `+N`: the overflow mark moved to the last
		// continuation line in R70, so the field stays exactly StampW*MaxStamps wide and the columns to its
		// right cannot move.
		int firstShown = (n < MaxStamps) ? n : MaxStamps;
		var stamps = new StringBuilder(StampW * MaxStamps);
		for (int i = 0; i < firstShown; i++)
			stamps.Append(DisplayFormat.PadL(g.Stamps[i].ToString("F1", CultureInfo.InvariantCulture), StampW));
		sb.Append(DisplayFormat.PadR(stamps.ToString(), StampW * MaxStamps));

		var tail = new StringBuilder(36);
		// R67: N rows folded, M cells they landed in. Two numbers, because one number cannot tell "a
		// triple in one cell" from "two doubles in two cells" (R66 printed x3 for both).
		if (g.Merged > 0)
			tail.Append('并').Append(g.Merged.ToString(CultureInfo.InvariantCulture)).Append('条')
				.Append(g.BurstCells.ToString(CultureInfo.InvariantCulture)).Append("格 ");
		tail.Append("n=").Append(n.ToString(CultureInfo.InvariantCulture));
		// R69: the calls that did NOT execute this skill. `n=1 试24` is the honest reading of a skill whose
		// description says 99 s and whose command is called every ~5 s; without this number the row looks
		// like "it fired once and then stopped".
		if (g.Attempts > 0)
			tail.Append(" 试").Append(g.Attempts.ToString(CultureInfo.InvariantCulture));
		double med = SkillTimelinePolicy.MedianInterval(g);
		if (med > 0.0)
			tail.Append(" med=").Append(med.ToString("F2", CultureInfo.InvariantCulture)).Append('s');
		// NOT truncated: the tail is the last column, so an over-long tail costs trailing width only, and
		// hiding the median behind a ".." would hide exactly the number the page exists to publish. The
		// leading space keeps `+4` (the overflow cell) from running into `并2条2格`.
		sb.Append(' ').Append(tail.ToString());
		TimelineLineStyle style = SkillTimelinePolicy.IsAuto(g.Type) ? TimelineLineStyle.Row
			: TimelineLineStyle.Header;
		lines.Add(new TimelineLine(sb.ToString(), style));

		// ---- R70: the continuation lines -------------------------------------------------------------
		int idx = firstShown;
		for (int line = 1; idx < n && line <= MaxStampLines; line++)
		{
			int remaining = n - idx;
			int cap = ContinuationStamps;
			bool lastAllowed = (line == MaxStampLines);
			int take = remaining;
			bool overflow = false;
			if (remaining > cap)
			{
				if (lastAllowed) { take = cap - 1; overflow = true; }
				else take = cap;
			}
			var csb = new StringBuilder(Indent + StampW * cap);
			csb.Append(' ', Indent);
			for (int k = 0; k < take; k++)
				csb.Append(DisplayFormat.PadL(g.Stamps[idx + k].ToString("F1", CultureInfo.InvariantCulture), StampW));
			if (overflow)
				csb.Append(DisplayFormat.PadL("+" + (remaining - take).ToString(CultureInfo.InvariantCulture), StampW));
			lines.Add(new TimelineLine(csb.ToString(), style));
			idx += take;
		}
		return lines;
	}

	/// <summary>Which channels produced the rows. Printed also when the page is empty, because "the command
	/// hook produced nothing" and "our units fired nothing" are different statements. R69: the `rec` column
	/// is gone with the channel it described (patched, never called). R70: the non-ally drop count is
	/// appended, because the page's title promises 我方 and a promise needs its evidence -- and it is what
	/// tells "the enemy had no skills" apart from "the filter worked".</summary>
	internal static string ChannelLine(int cmd, int skl)
	{
		return ChannelLine(cmd, skl, 0);
	}

	internal static string ChannelLine(int cmd, int skl, int foreignDropped)
	{
		string s = "观测通道 cmd(自动技能命令) " + cmd.ToString(CultureInfo.InvariantCulture)
			+ " 条 / skl(奥义特殊命令) " + skl.ToString(CultureInfo.InvariantCulture) + " 条";
		if (foreignDropped > 0)
			s += " / 剔除非我方 " + foreignDropped.ToString(CultureInfo.InvariantCulture) + " 条";
		return s;
	}
}
