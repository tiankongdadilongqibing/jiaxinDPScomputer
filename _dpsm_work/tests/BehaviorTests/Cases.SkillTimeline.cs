using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// R66: the 技能时间表 -- the raw activation stream in, one row per (unit, skill) out.
	///
	/// Why this group exists. The page answers "when did each of my characters fire its 奥义 / 自动技能",
	/// and the readings can only be taken in a live battle (`Diagnostics/SkillTimelineProbe.cs` needs the
	/// IL2CPP surface and is not in this project's compile list). What CAN be executed offline is every
	/// decision that turns those readings into the table, and those are exactly the decisions that can be
	/// wrong while looking right:
	///
	///   * the MERGE WINDOW -- two channels report the same activation (the game's auto-skill command
	///     postfix and its skill-record sink). Merge too eagerly and a real 0.3 s repeat disappears; do not
	///     merge at all and every activation is listed twice;
	///   * the TIME AXIS -- everything is on the battle clock, never the wall clock, because the game
	///     pauses its battle clock (measured: a counter stood still for 6 wall seconds);
	///   * the ORDER -- rows must not reorder as the battle goes on, or the table cannot be read while
	///     playing;
	///   * the MARKS -- `xN` (a burst folded into one cell) and `+N` (stamps beyond the printed ones) are
	///     the only reasons the table is not a silent truncation.
	/// </summary>
	public static void SkillTimeline(Runner r)
	{
		r.Group("policy/skill-timeline");

		// ---- the shape of an empty page ----
		r.Eq("no-events-no-groups", SkillTimelinePolicy.Group(new List<SkillTimelineEvent>()).Count, 0);
		r.Eq("a-null-list-is-empty", SkillTimelinePolicy.Group(null).Count, 0);

		// ---- one activation ----
		List<SkillTimelineGroup> one = SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("[賢導]トレイラ", "暗沌への導き", 3, 4.80, SkillTimelineEvent.ChannelCommand),
		});
		r.Eq("one-activation-is-one-group", one.Count, 1);
		r.Eq("one-activation-is-one-stamp", one[0].Stamps.Count, 1);
		r.Eq("one-activation-carries-its-raw-event", one[0].Events, 1);
		r.Eq("one-activation-merged-nothing", one[0].Merged, 0);
		r.EqD("one-activation-has-no-median", SkillTimelinePolicy.MedianInterval(one[0]), 0.0);
		r.Str("the-first-stamp-is-the-battle-clock-value", F1(one[0].Stamps[0]), "4.8");

		// ---- the stamps of one row are ascending, whatever order they arrive in ----
		r.Str("stamps-are-kept-ascending", Stamps(SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "S", 3, 10.0, "cmd"), Ev("A", "S", 3, 4.0, "cmd"), Ev("A", "S", 3, 7.0, "cmd"),
		})[0]), "4.0,7.0,10.0");

		// ---- the merge window (the measured 0.25 battle seconds) ----
		List<SkillTimelineGroup> merged = SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "S", 3, 20.00, "cmd"), Ev("A", "S", 3, 20.10, "rec"),
		});
		r.Eq("a-double-report-is-one-stamp", merged[0].Stamps.Count, 1);
		r.Eq("a-double-report-is-counted-not-hidden", merged[0].Merged, 1);
		r.Eq("a-double-report-keeps-both-raw-events", merged[0].Events, 2);
		// The shortest repeat the charge counter corroborates in the 9-battle corpus is 0.30 s, so the
		// window is EXCLUSIVE at its own value and 0.30 s must survive as two activations.
		r.Eq("exactly-the-window-is-not-merged", SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "S", 3, 20.00, "cmd"), Ev("A", "S", 3, 20.25, "cmd"),
		})[0].Stamps.Count, 2);
		r.Eq("the-shortest-corroborated-repeat-survives", SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("T.O.W.E.R.typeR", "電触補壁", 3, 20.00, "cmd"), Ev("T.O.W.E.R.typeR", "電触補壁", 3, 20.30, "cmd"),
		})[0].Stamps.Count, 2);
		// A third report inside the same window folds into the SAME cell -- it does not open a new one.
		List<SkillTimelineGroup> triple = SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "S", 3, 20.00, "cmd"), Ev("A", "S", 3, 20.10, "rec"), Ev("A", "S", 3, 20.40, "cmd"),
		});
		r.Str("a-burst-folds-into-the-previous-cell", Stamps(triple[0]), "20.0,20.4");
		r.Eq("a-burst-counts-the-cells-it-lost", triple[0].Merged, 1);

		// ---- what must NEVER be merged ----
		r.Eq("two-units-at-the-same-instant-stay-apart", SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "S", 3, 20.0, "cmd"), Ev("B", "S", 3, 20.0, "cmd"),
		}).Count, 2);
		r.Eq("two-skills-at-the-same-instant-stay-apart", SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "S1", 3, 20.0, "cmd"), Ev("A", "S2", 3, 20.0, "cmd"),
		}).Count, 2);
		r.Eq("the-two-auto-slots-stay-apart", SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "S", 3, 20.0, "cmd"), Ev("A", "S", 4, 20.0, "cmd"),
		}).Count, 2);

		// ---- rows that cannot be placed on the axis are skipped, not stamped 0 ----
		r.Eq("a-row-without-a-unit-is-dropped", SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("", "S", 3, 5.0, "cmd"),
		}).Count, 0);
		r.Eq("a-row-without-a-skill-is-dropped", SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "", 3, 5.0, "cmd"),
		}).Count, 0);
		r.Eq("a-nan-clock-is-dropped", SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "S", 3, double.NaN, "cmd"),
		}).Count, 0);
		r.Eq("an-infinite-clock-is-dropped", SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "S", 3, double.PositiveInfinity, "cmd"),
		}).Count, 0);
		r.Eq("a-null-row-is-dropped", SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			null, Ev("A", "S", 3, 5.0, "cmd"),
		}).Count, 1);

		// ---- the invariant that makes "nothing is hidden" true ----
		List<SkillTimelineGroup> mixed = SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "S", 3, 1.0, "cmd"), Ev("A", "S", 3, 1.05, "rec"), Ev("A", "S", 3, 9.0, "cmd"),
			Ev("A", "S", 3, 9.05, "rec"), Ev("A", "S", 3, 9.10, "cmd"), Ev("B", "T", 2, 2.0, "rec"),
			null, Ev("C", "", 3, 3.0, "cmd"),
		});
		int folded = 0;
		for (int i = 0; i < mixed.Count; i++) folded += mixed[i].Stamps.Count + mixed[i].Merged;
		r.Eq("every-raw-row-lands-in-a-cell-or-in-the-merged-count", folded, 6);
		r.Eq("the-invariant-covers-every-group", mixed.Count, 2);

		// ---- the median gap, on the battle clock ----
		List<SkillTimelineGroup> grid = SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "S", 3, 0.0, "cmd"), Ev("A", "S", 3, 9.0, "cmd"),
			Ev("A", "S", 3, 19.0, "cmd"), Ev("A", "S", 3, 30.0, "cmd"),
		});
		r.EqD("the-median-gap-of-9-10-11-is-10", SkillTimelinePolicy.MedianInterval(grid[0]), 10.0);
		r.EqD("two-stamps-take-their-only-gap", SkillTimelinePolicy.MedianInterval(SkillTimelinePolicy.Group(
			new List<SkillTimelineEvent> { Ev("A", "S", 3, 0.0, "cmd"), Ev("A", "S", 3, 7.73, "cmd") })[0]), 7.73);
		r.EqD("a-group-with-no-median-is-zero-not-nan",
			SkillTimelinePolicy.MedianInterval(new SkillTimelineGroup()), 0.0);

		// ---- order: stable, and by unit then kind ----
		List<SkillTimelineGroup> order = SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("B", "S", 3, 1.0, "cmd"), Ev("A", "S", 3, 5.0, "cmd"), Ev("A", "奥", 2, 20.0, "rec"),
		});
		r.Str("groups-are-ordered-by-unit-then-kind", Names(order), "A/奥|A/S|B/S");
		r.Str("rows-of-one-unit-stay-adjacent", Names(SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "Z", 3, 1.0, "cmd"), Ev("B", "Y", 3, 1.0, "cmd"), Ev("A", "A", 3, 1.0, "cmd"),
		})), "A/A|A/Z|B/Y");

		// ---- the channels of a row ----
		List<SkillTimelineGroup> chans = SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "S", 3, 1.0, "rec"), Ev("A", "S", 3, 9.0, "cmd"), Ev("A", "S", 3, 18.0, "rec"),
		});
		r.Eq("a-row-lists-each-channel-once", chans[0].Channels.Count, 2);
		r.Str("a-row-keeps-the-first-seen-channel-first", chans[0].Channels[0], "rec");

		// ---- labels and ranks ----
		r.Str("kind-0-is-the-plain-skill", SkillTimelinePolicy.KindLabel(0), "技能");
		r.Str("kind-1-is-the-special-skill", SkillTimelinePolicy.KindLabel(1), "特殊");
		r.Str("kind-2-is-the-over-skill", SkillTimelinePolicy.KindLabel(2), "奥义");
		r.Str("kind-3-is-the-first-auto-slot", SkillTimelinePolicy.KindLabel(3), "自动1");
		r.Str("kind-4-is-the-second-auto-slot", SkillTimelinePolicy.KindLabel(4), "自动2");
		r.Str("an-unreadable-type-says-so", SkillTimelinePolicy.KindLabel(-1), "类型-1");
		r.Eq("the-over-skill-ranks-first", SkillTimelinePolicy.KindRank(2), 0);
		r.Eq("the-special-skill-ranks-second", SkillTimelinePolicy.KindRank(1), 1);
		r.Eq("the-two-auto-slots-rank-last-among-the-known-kinds", SkillTimelinePolicy.KindRank(4), 4);
		r.Eq("an-unknown-type-ranks-after-everything-known", SkillTimelinePolicy.KindRank(-1), 5);
		r.True("only-the-two-auto-slots-are-auto", SkillTimelinePolicy.IsAuto(3) && SkillTimelinePolicy.IsAuto(4)
			&& !SkillTimelinePolicy.IsAuto(0) && !SkillTimelinePolicy.IsAuto(1) && !SkillTimelinePolicy.IsAuto(2));

		// ---- the pinned constants (the text layer and the panel width are built on them) ----
		r.EqD("the-merge-window-is-the-measured-0.25s", SkillTimelinePolicy.MergeSeconds, 0.25);
		r.Eq("the-table-shows-14-rows", SkillTimelinePolicy.MaxGroups, 14);
		r.Eq("a-row-prints-9-stamps", SkillTimelinePolicy.MaxStamps, 9);

		// ---- R67: WHERE the folds landed (R66's single number could not say) ----
		// The measured case: メアリー's two folds landed in two different cells (49.10+49.10, 79.30+79.37).
		List<SkillTimelineGroup> spread = SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("メアリー", "影爪の強制裁断", 3, 49.10, "cmd"), Ev("メアリー", "影爪の強制裁断", 3, 49.10, "cmd"),
			Ev("メアリー", "影爪の強制裁断", 3, 59.20, "cmd"),
			Ev("メアリー", "影爪の強制裁断", 3, 79.30, "cmd"), Ev("メアリー", "影爪の強制裁断", 3, 79.37, "cmd"),
		});
		r.Eq("two-folds-in-two-cells-count-two-rows", spread[0].Merged, 2);
		r.Eq("two-folds-in-two-cells-count-two-cells", spread[0].BurstCells, 2);
		r.Str("each-cell-keeps-its-own-multiplicity", Mult(spread[0]), "2,1,2");
		List<SkillTimelineGroup> stacked = SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "S", 3, 10.00, "cmd"), Ev("A", "S", 3, 10.05, "rec"), Ev("A", "S", 3, 10.10, "cmd"),
		});
		r.Eq("a-triple-in-one-cell-counts-two-rows", stacked[0].Merged, 2);
		r.Eq("a-triple-in-one-cell-counts-one-cell", stacked[0].BurstCells, 1);
		r.Str("a-triple-in-one-cell-has-multiplicity-three", Mult(stacked[0]), "3");
		r.Eq("multiplicity-is-parallel-to-the-stamps", spread[0].Multiplicity.Count, spread[0].Stamps.Count);

		SkillTimelineTextCases(r);
	}

	// ------------------------------------------------------------------ the page text
	private static void SkillTimelineTextCases(Runner r)
	{
		r.Group("ui/skill-timeline-text");

		// ---- the empty page: "nothing fired" and "the hook saw nothing" are different lines ----
		List<TimelineLine> empty = SkillTimelineText.Rows(new List<SkillTimelineEvent>(), true);
		r.True("an-empty-page-in-battle-says-so", Has(empty, "  (本场尚未观测到我方技能发动)"));
		r.True("an-empty-page-still-prints-the-channel-counts", Has(empty, "观测通道 cmd(自动技能命令) 0 条"));
		List<TimelineLine> idle = SkillTimelineText.Rows(new List<SkillTimelineEvent>(), false);
		r.True("an-empty-page-out-of-battle-says-so-too", Has(idle, "  (未在战斗中,也没有上一场的记录)"));
		r.Str("the-channel-line-counts-all-three-channels",
			SkillTimelineText.ChannelLine(97, 5, 0),
			"观测通道 cmd(自动技能命令) 97 条 / skl(奥义特殊命令) 5 条 / rec(技能记录) 0 条");

		// ---- the channel diagnosis: 奥义/特殊 are seen by skl (R67) or rec (R66), never by cmd ----
		List<SkillTimelineEvent> cmdOnly = new List<SkillTimelineEvent>
		{
			Ev("[賢導]トレイラ", "暗沌への導き", 3, 4.0, "cmd"), Ev("[賢導]トレイラ", "暗沌への導き", 3, 13.0, "cmd"),
		};
		r.True("no-over-channel-rows-warns", Has(SkillTimelineText.Rows(cmdOnly, true), "两条通道(skl/rec)本场都是 0 条"));
		List<SkillTimelineEvent> both = new List<SkillTimelineEvent>(cmdOnly);
		both.Add(Ev("[賢導]トレイラ", "真なる奥義", 2, 31.0, "rec"));
		r.True("a-record-channel-with-rows-does-not-warn",
			!Has(SkillTimelineText.Rows(both, true), "两条通道(skl/rec)本场都是 0 条"));
		List<SkillTimelineEvent> viaSkl = new List<SkillTimelineEvent>(cmdOnly);
		viaSkl.Add(Ev("[賢導]トレイラ", "真なる奥義", 2, 31.0, SkillTimelineEvent.ChannelSkillCommand));
		r.True("a-skill-command-channel-with-rows-does-not-warn",
			!Has(SkillTimelineText.Rows(viaSkl, true), "两条通道(skl/rec)本场都是 0 条"));
		r.True("skl-rows-are-counted-in-their-own-channel",
			Has(SkillTimelineText.Rows(viaSkl, true), "skl(奥义特殊命令) 1 条"));
		r.True("skl-rows-are-counted-in-the-raw-total",
			Has(SkillTimelineText.Rows(viaSkl, true), "原始 3 条"));
		r.True("a-public-page-does-not-claim-to-have-no-record",
			!Has(SkillTimelineText.Rows(cmdOnly, false), "也没有上一场的记录"));

		// ---- one row: geometry, content and the two marks ----
		List<SkillTimelineGroup> g = SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("[賢導]トレイラ", "暗沌への導き", 3, 4.8, "cmd"), Ev("[賢導]トレイラ", "暗沌への導き", 3, 18.2, "cmd"),
			Ev("[賢導]トレイラ", "暗沌への導き", 3, 18.25, "rec"), Ev("[賢導]トレイラ", "暗沌への導き", 3, 31.7, "cmd"),
		});
		TimelineLine row = SkillTimelineText.GroupLine(g[0]);
		r.Str("a-row-is-two-spaces-then-the-name", row.Text.Substring(0, 2), "  ");
		r.True("a-row-carries-the-unit", row.Text.Contains("[賢導]トレイラ"));
		r.True("a-row-carries-the-kind-label", row.Text.Contains("自动1"));
		r.True("a-row-carries-the-skill-name", row.Text.Contains("暗沌への導き"));
		r.True("a-row-counts-its-activations", row.Text.Contains("n=3"));
		r.True("a-row-prints-the-median-gap", row.Text.Contains("med=13.45s"));
		r.True("a-row-marks-a-folded-burst", row.Text.Contains("并1条1格"));
		r.True("every-row-fits-the-pinned-line-width",
			DisplayFormat.DispWidth(row.Text) <= SkillTimelineText.LineWidth);
		r.Eq("the-line-width-is-pinned", SkillTimelineText.LineWidth, 121);
		// R67: the measured defects of the R66 page, as cases.
		TimelineLine spreadRow = SkillTimelineText.GroupLine(SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("メアリー", "影爪", 3, 49.10, "cmd"), Ev("メアリー", "影爪", 3, 49.10, "cmd"),
			Ev("メアリー", "影爪", 3, 79.30, "cmd"), Ev("メアリー", "影爪", 3, 79.37, "cmd"),
		})[0]);
		r.True("two-folds-in-two-cells-print-both-numbers", spreadRow.Text.Contains("并2条2格"));
		r.True("the-r66-one-cell-claim-is-gone", !spreadRow.Text.Contains("x3"));
		TimelineLine late = SkillTimelineText.GroupLine(SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "S", 3, 96.2, "cmd"), Ev("A", "S", 3, 107.3, "cmd"), Ev("A", "S", 3, 118.3, "cmd"),
		})[0]);
		r.True("three-digit-stamps-stay-separated", late.Text.Contains(" 96.2 107.3 118.3"));
		r.True("three-digit-stamps-never-run-together", !late.Text.Contains("96.2107.3"));
		r.True("the-table-header-names-its-four-columns",
			Has(SkillTimelineText.Rows(both, true), "角色") && Has(SkillTimelineText.Rows(both, true), "种类")
			&& Has(SkillTimelineText.Rows(both, true), "技能") && Has(SkillTimelineText.Rows(both, true), "发动时刻"));
		r.True("the-page-starts-with-its-title",
			SkillTimelineText.Rows(both, true)[0].Text.StartsWith("技能时间表"));
		r.True("the-title-line-is-the-header-style",
			SkillTimelineText.Rows(both, true)[0].Style == TimelineLineStyle.Header);
		r.True("the-title-says-the-key-that-leaves-the-page",
			SkillTimelineText.Rows(both, true)[0].Text.Contains("F4 返回"));

		// an 奥义 row is the amber "look here" style, an auto row is the ordinary one
		r.True("an-over-skill-row-is-highlighted", SkillTimelineText.GroupLine(
			SkillTimelinePolicy.Group(new List<SkillTimelineEvent> { Ev("A", "奥", 2, 5.0, "rec") })[0]
			).Style == TimelineLineStyle.Header);
		r.True("an-auto-row-is-not-highlighted", SkillTimelineText.GroupLine(g[0]).Style == TimelineLineStyle.Row);
		r.True("a-row-with-a-single-activation-prints-no-median",
			!SkillTimelineText.GroupLine(SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
			{ Ev("A", "S", 3, 5.0, "cmd") })[0]).Text.Contains("med="));

		// ---- truncation is always stated ----
		var many = new List<SkillTimelineEvent>();
		for (int i = 0; i < 15; i++) many.Add(Ev("A", "S", 3, 5.0 + i * 2.0, "cmd"));
		// 15 stamps, 9 cells: 8 stamps + the "+7" cell
		r.True("stamps-beyond-the-printed-ones-are-counted",
			SkillTimelineText.GroupLine(SkillTimelinePolicy.Group(many)[0]).Text.Contains("+7"));
		r.Eq("exactly-nine-stamps-print-without-a-plus",
			SkillTimelineText.GroupLine(SkillTimelinePolicy.Group(many.GetRange(0, 9))[0]).Text.Contains("+") ? 1 : 0, 0);

		var lots = new List<SkillTimelineEvent>();
		for (int i = 0; i < 16; i++) lots.Add(Ev("U" + i.ToString("D2", CultureInfo.InvariantCulture), "S", 3, 5.0 + i, "cmd"));
		List<TimelineLine> lotsLines = SkillTimelineText.Rows(lots, true);
		int rows = 0;
		for (int i = 0; i < lotsLines.Count; i++) if (lotsLines[i].Text.Contains("自动1")) rows++;
		r.Eq("only-14-rows-are-printed", rows, 14);
		r.True("the-truncated-tail-is-reported", Has(lotsLines, "... 还有 2 行(共 16 行"));
	}

	// ------------------------------------------------------------------ helpers

	private static SkillTimelineEvent Ev(string unit, string skill, int type, double active, string channel)
	{
		return new SkillTimelineEvent
		{
			Unit = unit, Skill = skill, Type = type, Channel = channel,
			Active = active, Wall = active * 1.48,
		};
	}

	private static string Stamps(SkillTimelineGroup g)
	{
		var sb = new StringBuilder();
		for (int i = 0; i < g.Stamps.Count; i++)
		{
			if (sb.Length > 0) sb.Append(',');
			sb.Append(F1(g.Stamps[i]));
		}
		return sb.ToString();
	}

	private static string Names(List<SkillTimelineGroup> groups)
	{
		var sb = new StringBuilder();
		for (int i = 0; i < groups.Count; i++)
		{
			if (sb.Length > 0) sb.Append('|');
			sb.Append(groups[i].Unit).Append('/').Append(groups[i].Skill);
		}
		return sb.ToString();
	}

	private static string Mult(SkillTimelineGroup g)
	{
		var sb = new StringBuilder();
		for (int i = 0; i < g.Multiplicity.Count; i++)
		{
			if (sb.Length > 0) sb.Append(',');
			sb.Append(g.Multiplicity[i].ToString(CultureInfo.InvariantCulture));
		}
		return sb.ToString();
	}

	private static string F1(double v)
	{
		return v.ToString("F1", CultureInfo.InvariantCulture);
	}

	private static bool Has(List<TimelineLine> lines, string needle)
	{
		for (int i = 0; i < lines.Count; i++) if (lines[i].Text.Contains(needle)) return true;
		return false;
	}
}
