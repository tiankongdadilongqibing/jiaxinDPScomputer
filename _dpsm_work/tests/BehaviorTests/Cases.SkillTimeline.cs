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
	///   * the FOLD (R69) -- one activation leaves a CLUSTER of command calls spread over the skill's
	///     execution window (measured spans up to 4.53 s). Fold too eagerly and a real repeat disappears;
	///     fold too little (R66's fixed 0.25 s) and one activation is published as several -- which is how a
	///     99-second skill came to read `med=4.97s`;
	///   * the TIME AXIS -- everything is on the battle clock, never the wall clock, because the game
	///     pauses its battle clock (measured: a counter stood still for 6 wall seconds);
	///   * the ORDER -- rows must not reorder as the battle goes on, or the table cannot be read while
	///     playing;
	///   * the MARKS -- `并N条M格` (folds) and `+N` (stamps beyond the printed ones) are the only reasons
	///     the table is not a silent truncation, and `试N` (R69) is the only reason a row cannot be read as
	///     "it fired once and stopped".
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

		// ---- the fold FLOOR: same-instant double reports, when no cooldown was read ----
		List<SkillTimelineGroup> merged = SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "S", 3, 20.00, "cmd"), Ev("A", "S", 3, 20.10, "skl"),
		});
		r.Eq("a-double-report-is-one-stamp", merged[0].Stamps.Count, 1);
		r.Eq("a-double-report-is-counted-not-hidden", merged[0].Merged, 1);
		r.Eq("a-double-report-keeps-both-raw-events", merged[0].Events, 2);
		r.Eq("exactly-the-floor-is-not-merged", SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "S", 3, 20.00, "cmd"), Ev("A", "S", 3, 20.25, "cmd"),
		})[0].Stamps.Count, 2);
		// A third report inside the same floor folds into the SAME cell -- it does not open a new one.
		List<SkillTimelineGroup> triple = SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "S", 3, 20.00, "cmd"), Ev("A", "S", 3, 20.10, "skl"), Ev("A", "S", 3, 20.40, "cmd"),
		});
		r.Str("a-burst-folds-into-the-previous-cell", Stamps(triple[0]), "20.0,20.4");
		r.Eq("a-burst-counts-the-cells-it-lost", triple[0].Merged, 1);

		// ---- R69: the fold is the SKILL'S OWN COOLDOWN, not a fixed window ----
		// MEASURED (quest 411001 and the 9-battle corpus): one activation leaves a cluster of `Using` calls
		// spread over the execution window -- 暗沌への導き (8 s cooldown) produced calls at 5.27/6.77 and
		// 18.70/20.80. Those are two activations, not four.
		r.EqD("the-fold-of-a-row-with-no-cooldown-is-the-floor",
			SkillTimelinePolicy.FoldSeconds(Ev("A", "S", 3, 1.0, "cmd")), 0.25);
		r.EqD("the-fold-is-the-skills-own-cooldown",
			SkillTimelinePolicy.FoldSeconds(Ev("A", "S", 3, 1.0, "cmd", 8.0)), 8.0);
		r.EqD("a-nan-cooldown-falls-back-to-the-floor",
			SkillTimelinePolicy.FoldSeconds(Ev("A", "S", 3, 1.0, "cmd", double.NaN)), 0.25);
		r.EqD("a-negative-cooldown-falls-back-to-the-floor",
			SkillTimelinePolicy.FoldSeconds(Ev("A", "S", 3, 1.0, "cmd", -5.0)), 0.25);
		List<SkillTimelineGroup> cluster = SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "S", 3, 5.27, "cmd", 8.0), Ev("A", "S", 3, 6.77, "cmd", 8.0),
			Ev("A", "S", 3, 18.70, "cmd", 8.0), Ev("A", "S", 3, 20.80, "cmd", 8.0),
		});
		r.Str("calls-inside-one-cooldown-are-one-activation", Stamps(cluster[0]), "5.3,18.7");
		r.Eq("the-cluster-counts-its-two-folds", cluster[0].Merged, 2);
		r.EqD("the-cluster-median-is-the-real-cadence", SkillTimelinePolicy.MedianInterval(cluster[0]), 13.43);
		r.Eq("a-cluster-within-the-cooldown-never-widens-the-row", cluster[0].Multiplicity.Count, 2);
		// The threshold travels with the stamp that STARTED the cluster: a later row cannot re-open it.
		r.Eq("the-fold-follows-the-stamp-that-started-the-cluster",
			SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
			{
				Ev("A", "S", 3, 20.0, "cmd", 8.0), Ev("A", "S", 3, 21.0, "cmd", 0.0),
			})[0].Stamps.Count, 1);
		// ... and a real repeat is never eaten: 21.5 s is inside 8 s of nothing.
		r.Eq("a-real-repeat-outside-the-cooldown-survives", SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "S", 3, 20.0, "cmd", 8.0), Ev("A", "S", 3, 28.5, "cmd", 8.0),
		})[0].Stamps.Count, 2);
		// Without a cooldown reading the old floor applies, so a 1.5 s gap still opens a new stamp --
		// exactly the R66 behaviour, kept as the documented degradation and not as the rule.
		r.Eq("an-unreadable-cooldown-does-not-invent-a-fold", SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "S", 3, 20.0, "cmd"), Ev("A", "S", 3, 21.5, "cmd"),
		})[0].Stamps.Count, 2);

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
			Ev("A", "S", 3, 1.0, "cmd"), Ev("A", "S", 3, 1.05, "skl"), Ev("A", "S", 3, 9.0, "cmd"),
			Ev("A", "S", 3, 9.05, "skl"), Ev("A", "S", 3, 9.10, "cmd"), Ev("B", "T", 2, 2.0, "skl"),
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
			Ev("B", "S", 3, 1.0, "cmd"), Ev("A", "S", 3, 5.0, "cmd"), Ev("A", "奥", 2, 20.0, "skl"),
		});
		r.Str("groups-are-ordered-by-unit-then-kind", Names(order), "A/奥|A/S|B/S");
		r.Str("rows-of-one-unit-stay-adjacent", Names(SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "Z", 3, 1.0, "cmd"), Ev("B", "Y", 3, 1.0, "cmd"), Ev("A", "A", 3, 1.0, "cmd"),
		})), "A/A|A/Z|B/Y");

		// ---- the channels of a row ----
		List<SkillTimelineGroup> chans = SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "S", 3, 1.0, "skl"), Ev("A", "S", 3, 9.0, "cmd"), Ev("A", "S", 3, 18.0, "skl"),
		});
		r.Eq("a-row-lists-each-channel-once", chans[0].Channels.Count, 2);
		r.Str("a-row-keeps-the-first-seen-channel-first", chans[0].Channels[0], "skl");

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
		r.EqD("the-fold-floor-is-the-measured-0.25s", SkillTimelinePolicy.MergeSeconds, 0.25);
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
			Ev("A", "S", 3, 10.00, "cmd"), Ev("A", "S", 3, 10.05, "skl"), Ev("A", "S", 3, 10.10, "cmd"),
		});
		r.Eq("a-triple-in-one-cell-counts-two-rows", stacked[0].Merged, 2);
		r.Eq("a-triple-in-one-cell-counts-one-cell", stacked[0].BurstCells, 1);
		r.Str("a-triple-in-one-cell-has-multiplicity-three", Mult(stacked[0]), "3");
		r.Eq("multiplicity-is-parallel-to-the-stamps", spread[0].Multiplicity.Count, spread[0].Stamps.Count);
		r.Eq("the-fold-thresholds-are-parallel-to-the-stamps",
			cluster[0].StampFold.Count, cluster[0].Stamps.Count);

		SkillActivationCases(r);
		SkillSideCases(r);
		SkillAttemptCases(r);
		ClockCalibrationCases(r);
		SkillTimelineTextCases(r);
	}

	// ------------------------------------------------------------------ R71: the battle clock's origin
	private static void ClockCalibrationCases(Runner r)
	{
		r.Group("policy/battle-clock-calibration");

		// THE MEASURED CASE (2026-10-06, three battles): a slot whose first charge is 4.0 s holds 92 of its
		// 120 frames when our clock reads 0.03 s -> the game had been charging it 0.93 s, i.e. our origin is
		// 0.90 s late.
		double lag;
		r.True("a-first-charge-slot-measures-the-lag",
			BattleClockCalibrationPolicy.TryLag(120, 92, 30.0, 0.03, out lag));
		r.EqD("the-lag-of-the-measured-slot", lag, 0.9033333333333333);
		r.True("a-full-first-charge-means-no-lag-yet",
			BattleClockCalibrationPolicy.TryLag(120, 120, 30.0, 0.0, out lag) && lag < 0.001);
		// Refusals: each one is a way a plausible reading would be wrong.
		r.True("a-slot-without-a-first-charge-is-refused",
			!BattleClockCalibrationPolicy.TryLag(0, 0, 30.0, 0.03, out lag));
		r.True("a-negative-first-charge-is-refused",
			!BattleClockCalibrationPolicy.TryLag(-300, 100, 30.0, 0.03, out lag));
		r.True("a-counter-above-its-first-charge-is-refused",
			!BattleClockCalibrationPolicy.TryLag(120, 121, 30.0, 0.03, out lag));
		r.True("a-negative-counter-is-refused",
			!BattleClockCalibrationPolicy.TryLag(120, -1, 30.0, 0.03, out lag));
		r.True("an-unreadable-units-per-second-is-refused",
			!BattleClockCalibrationPolicy.TryLag(120, 92, 0.0, 0.03, out lag));
		r.True("a-sample-outside-the-window-is-refused",
			!BattleClockCalibrationPolicy.TryLag(900, 800, 30.0, 5.0, out lag));
		r.True("a-nan-clock-is-refused",
			!BattleClockCalibrationPolicy.TryLag(120, 92, 30.0, double.NaN, out lag));
		r.True("an-absurd-lag-is-refused",
			!BattleClockCalibrationPolicy.TryLag(9000, 0, 30.0, 0.0, out lag));

		// The combination: the MEDIAN of the usable samples (one bad slot must not drag the axis), and no
		// shift at all below the minimum or with too few samples.
		r.EqD("the-median-of-three-slots", BattleClockCalibrationPolicy.Combine(new List<double> { 0.90, 0.93, 0.90 }),
			0.90);
		r.EqD("a-single-outlier-is-ignored", BattleClockCalibrationPolicy.Combine(new List<double> { 0.90, 0.93, 4.5 }),
			0.93);
		r.EqD("samples-beyond-the-max-lag-are-dropped-before-the-median",
			BattleClockCalibrationPolicy.Combine(new List<double> { 0.90, 0.90, 9.0, 20.0 }), 0.90);
		r.EqD("samples-that-say-we-are-ahead-are-dropped-too",
			BattleClockCalibrationPolicy.Combine(new List<double> { 0.90, 0.90, -3.0, -3.0 }), 0.90);
		r.EqD("a-nan-sample-never-reaches-the-median",
			BattleClockCalibrationPolicy.Combine(new List<double> { 0.90, 0.90, 0.90, double.NaN }), 0.90);
		r.EqD("one-sample-is-not-enough", BattleClockCalibrationPolicy.Combine(new List<double> { 0.90 }), 0.0);
		r.EqD("no-samples-mean-no-shift", BattleClockCalibrationPolicy.Combine(new List<double>()), 0.0);
		r.EqD("a-null-list-means-no-shift", BattleClockCalibrationPolicy.Combine(null), 0.0);
		r.EqD("a-sub-noise-lag-is-not-a-shift", BattleClockCalibrationPolicy.Combine(new List<double> { 0.01, 0.02 }),
			0.0);
		r.EqD("a-negative-lag-is-not-a-shift", BattleClockCalibrationPolicy.Combine(new List<double> { -0.30, -0.30 }),
			0.0);

		// The decision: one battle, one origin -- and only inside the window, before the first event.
		r.True("a-measured-lag-is-applied", BattleClockCalibrationPolicy.ShouldRebase(0.90, 0.03, 0, false));
		r.True("the-shift-is-refused-after-an-event",
			!BattleClockCalibrationPolicy.ShouldRebase(0.90, 0.03, 1, false));
		r.True("the-shift-is-refused-outside-the-window",
			!BattleClockCalibrationPolicy.ShouldRebase(0.90, 3.0, 0, false));
		r.True("the-shift-is-refused-below-the-minimum",
			!BattleClockCalibrationPolicy.ShouldRebase(0.01, 0.03, 0, false));
		r.True("the-shift-is-refused-above-the-maximum",
			!BattleClockCalibrationPolicy.ShouldRebase(6.0, 0.03, 0, false));
		r.True("a-zero-lag-is-not-a-shift", !BattleClockCalibrationPolicy.ShouldRebase(0.0, 0.03, 0, false));
		r.True("a-nan-lag-is-refused", !BattleClockCalibrationPolicy.ShouldRebase(double.NaN, 0.03, 0, false));
		// R72: the hold cap is the SECOND way an event can already sit on the old axis (the first is
		// `eventsRecorded`), and it refuses the shift for the same reason: one battle, one origin.
		r.True("a-hold-overflow-refuses-the-shift",
			!BattleClockCalibrationPolicy.ShouldRebase(0.90, 0.03, 0, true));
		r.Eq("the-bounds-are-pinned",
			BattleClockCalibrationPolicy.MinSamples * 1000 + (int)(BattleClockCalibrationPolicy.MinLagSeconds * 100)
			+ (int)BattleClockCalibrationPolicy.MaxLagSeconds, 2000 + 5 + 5);

		// A fresh session carries no shift until the calibration decides (the fields the log/page read).
		var fresh = new BattleSession();
		r.EqD("a-fresh-session-has-no-origin-shift", fresh.ClockOriginShift, 0.0);
		r.Eq("a-fresh-session-has-not-decided-yet", fresh.ClockOriginDecided ? 1 : 0, 0);
	}

	// ------------------------------------------------------------------ R70: whose skills are these
	private static void SkillSideCases(Runner r)
	{
		r.Group("policy/skill-side");

		// MEASURED 2026-10-06 (quest 9999): `actors[].team` = 1 for our units and 2 for the enemy's, and the
		// page had been mixing them because only the `skl` route applied this test.
		r.Eq("our-team-is-one", SkillSidePolicy.OurTeam, 1);
		r.True("team-one-is-ours", SkillSidePolicy.IsOurs(1));
		r.True("team-two-is-not-ours", !SkillSidePolicy.IsOurs(2));
		r.True("team-zero-is-not-ours", !SkillSidePolicy.IsOurs(0));
		r.True("a-negative-team-is-not-ours", !SkillSidePolicy.IsOurs(-1));
		r.True("an-unreadable-team-is-not-ours", !SkillSidePolicy.IsOurs(int.MinValue));
	}

	// ------------------------------------------------------------------ R69: activation vs attempt
	private static void SkillActivationCases(Runner r)
	{
		r.Group("policy/skill-activation");

		// The verdict is the game's own state machine, read at the moment of the call. MEASURED (quest
		// 411001): of マッドシーカー's 25 calls, 1 was `Using` and 24 were `Charge`.
		r.True("using-is-an-activation", SkillActivationPolicy.IsActivation("Using"));
		r.True("charge-is-not-an-activation", !SkillActivationPolicy.IsActivation("Charge"));
		r.True("usable-is-not-an-activation", !SkillActivationPolicy.IsActivation("Usable"));
		r.True("nothave-is-not-an-activation", !SkillActivationPolicy.IsActivation("NotHave"));
		r.True("an-unreadable-status-is-not-an-activation", !SkillActivationPolicy.IsActivation("?"));
		r.True("a-null-status-is-not-an-activation", !SkillActivationPolicy.IsActivation(null));
		r.True("an-empty-status-is-not-an-activation", !SkillActivationPolicy.IsActivation(""));
		r.True("charge-is-an-attempt", SkillActivationPolicy.IsAttempt("Charge"));
		r.True("usable-is-an-attempt", SkillActivationPolicy.IsAttempt("Usable"));
		r.True("using-is-not-an-attempt", !SkillActivationPolicy.IsAttempt("Using"));
		// FAIL CLOSED: an unknown state is neither, so the probe counts it and publishes nothing.
		r.True("an-unknown-status-is-neither", !SkillActivationPolicy.IsActivation("Status5")
			&& !SkillActivationPolicy.IsAttempt("Status5"));
		r.True("an-unreadable-status-is-neither", !SkillActivationPolicy.IsAttempt("?"));
		r.True("the-status-names-are-the-games-own", SkillActivationPolicy.StatusUsing == "Using"
			&& SkillActivationPolicy.StatusCharge == "Charge" && SkillActivationPolicy.StatusUsable == "Usable");
	}

	// ------------------------------------------------------------------ R69: the attempt tallies
	private static void SkillAttemptCases(Runner r)
	{
		r.Group("policy/skill-attempts");

		// An attempt belongs to the row it was tried with, and it is ADDED to the count, not replaced.
		List<SkillTimelineGroup> g = SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("マッドシーカー", "実験失敗！", 3, 99.1, "cmd", 99.0),
		});
		SkillTimelinePolicy.ApplyAttempts(g, new List<SkillTimelineAttempt>
		{
			Att("マッドシーカー", "実験失敗！", 3, 20), Att("マッドシーカー", "実験失敗！", 3, 4),
		});
		r.Eq("attempts-do-not-open-a-new-row", g.Count, 1);
		r.Eq("attempts-do-not-add-a-stamp", g[0].Stamps.Count, 1);
		r.Eq("attempts-are-summed-onto-the-row", g[0].Attempts, 24);
		r.EqD("attempts-never-enter-the-median", SkillTimelinePolicy.MedianInterval(g[0]), 0.0);

		// A skill that was ONLY ever tried gets a row of its own: "never fired" must be visible, because
		// the alternative is that the unit looks like it has no such skill.
		List<SkillTimelineGroup> only = SkillTimelinePolicy.Group(new List<SkillTimelineEvent>());
		SkillTimelinePolicy.ApplyAttempts(only, new List<SkillTimelineAttempt>
		{
			Att("ネア・ウルム", "ヴェールの拒絶", 3, 1),
		});
		r.Eq("an-attempt-only-skill-still-gets-a-row", only.Count, 1);
		r.Eq("an-attempt-only-row-has-no-stamp", only[0].Stamps.Count, 0);
		r.Eq("an-attempt-only-row-carries-its-count", only[0].Attempts, 1);
		r.Eq("an-attempt-only-row-carries-its-kind", only[0].Type, 3);

		// Rows added after the fact land in the SAME order as the rows that were already there.
		List<SkillTimelineGroup> sorted = SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("B", "S", 3, 1.0, "cmd"),
		});
		SkillTimelinePolicy.ApplyAttempts(sorted, new List<SkillTimelineAttempt>
		{
			Att("A", "Z", 2, 2), Att("B", "A", 3, 1), Att("B", "S", 3, 3),
		});
		r.Str("attempt-rows-are-inserted-in-the-tables-order", Names(sorted), "A/Z|B/A|B/S");
		r.Eq("an-attempt-row-merges-into-the-existing-kind", sorted[2].Attempts, 3);

		// Nothing may be invented out of a malformed tally.
		List<SkillTimelineGroup> dirty = SkillTimelinePolicy.Group(new List<SkillTimelineEvent>());
		SkillTimelinePolicy.ApplyAttempts(dirty, new List<SkillTimelineAttempt>
		{
			null, Att("", "S", 3, 4), Att("A", "", 3, 4), Att("A", "S", 3, 0), Att("A", "S", 3, -2),
		});
		r.Eq("a-malformed-attempt-tally-adds-no-row", dirty.Count, 0);
		List<SkillTimelineGroup> none = SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("A", "S", 3, 1.0, "cmd"),
		});
		SkillTimelinePolicy.ApplyAttempts(none, null);
		r.Eq("a-null-attempt-list-changes-nothing", none[0].Attempts, 0);
		r.Eq("a-null-attempt-list-adds-no-row", none.Count, 1);
	}

	// ------------------------------------------------------------------ the page text
	private static void SkillTimelineTextCases(Runner r)
	{
		r.Group("ui/skill-timeline-text");

		// ---- the empty page: "nothing fired" and "the hook saw nothing" are different lines ----
		List<TimelineLine> empty = SkillTimelineText.Rows(new List<SkillTimelineEvent>(),
			new List<SkillTimelineAttempt>(), true);
		r.True("an-empty-page-in-battle-says-so", Has(empty, "  (本场尚未观测到我方技能发动,也没有试触发)"));
		r.True("an-empty-page-still-prints-the-channel-counts", Has(empty, "观测通道 cmd(自动技能命令) 0 条"));
		List<TimelineLine> idle = SkillTimelineText.Rows(new List<SkillTimelineEvent>(),
			new List<SkillTimelineAttempt>(), false);
		r.True("an-empty-page-out-of-battle-says-so-too", Has(idle, "  (未在战斗中,也没有上一场的记录)"));
		// R69: the `rec` column is gone with the channel it described (patched, never called in 2 battles).
		r.Str("the-channel-line-names-the-two-live-channels",
			SkillTimelineText.ChannelLine(97, 5),
			"观测通道 cmd(自动技能命令) 97 条 / skl(奥义特殊命令) 5 条");
		r.True("the-channel-line-no-longer-mentions-the-deleted-record-sink",
			!SkillTimelineText.ChannelLine(97, 5).Contains("rec"));

		// ---- the channel diagnosis: 奥义/特殊 are seen by skl, never by cmd ----
		List<SkillTimelineEvent> cmdOnly = new List<SkillTimelineEvent>
		{
			Ev("[賢導]トレイラ", "暗沌への導き", 3, 4.0, "cmd"), Ev("[賢導]トレイラ", "暗沌への導き", 3, 13.0, "cmd"),
		};
		r.True("no-over-channel-rows-warns",
			Has(SkillTimelineText.Rows(cmdOnly, null, true), "奥义/特殊 通道(skl)本场 0 条。"));
		List<SkillTimelineEvent> viaSkl = new List<SkillTimelineEvent>(cmdOnly);
		viaSkl.Add(Ev("[賢導]トレイラ", "真なる奥義", 2, 31.0, SkillTimelineEvent.ChannelSkillCommand));
		r.True("a-skill-command-channel-with-rows-does-not-warn",
			!Has(SkillTimelineText.Rows(viaSkl, null, true), "奥义/特殊 通道(skl)本场 0 条。"));
		r.True("skl-rows-are-counted-in-their-own-channel",
			Has(SkillTimelineText.Rows(viaSkl, null, true), "skl(奥义特殊命令) 1 条"));
		r.True("skl-rows-are-counted-in-the-raw-total",
			Has(SkillTimelineText.Rows(viaSkl, null, true), "原始 3 条发动"));
		r.True("a-public-page-does-not-claim-to-have-no-record",
			!Has(SkillTimelineText.Rows(cmdOnly, null, false), "也没有上一场的记录"));

		// ---- R69: the attempt count, on the summary line and on the row ----
		List<SkillTimelineAttempt> tries = new List<SkillTimelineAttempt>
		{
			Att("マッドシーカー", "実験失敗！", 3, 24),
		};
		List<TimelineLine> mad = SkillTimelineText.Rows(new List<SkillTimelineEvent>
		{
			Ev("マッドシーカー", "実験失敗！", 3, 99.1, "cmd", 99.0),
		}, tries, true);
		r.True("the-summary-line-counts-the-attempts", Has(mad, "试触发 24 条(未计入)"));
		r.True("an-attempt-count-is-printed-on-its-own-row", Has(mad, "n=1 试24"));
		r.True("a-99-second-skill-shows-one-activation-and-no-median", !Has(mad, "med="));
		// A tally with no event at all still produces the row (and only that row).
		List<TimelineLine> tryOnly = SkillTimelineText.Rows(new List<SkillTimelineEvent>(), tries, true);
		r.True("an-attempt-only-skill-is-printed", Has(tryOnly, "実験失敗！"));
		r.True("an-attempt-only-row-says-n-zero", Has(tryOnly, "n=0 试24"));
		r.True("an-attempt-only-page-does-not-claim-nothing-was-observed",
			!Has(tryOnly, "本场尚未观测到我方技能发动"));
		r.True("a-zero-attempt-count-is-not-printed",
			!Has(SkillTimelineText.Rows(cmdOnly, new List<SkillTimelineAttempt>
			{ Att("[賢導]トレイラ", "暗沌への導き", 3, 0) }, true), "试0"));

		// ---- one row: geometry, content and the two marks ----
		List<SkillTimelineGroup> g = SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
		{
			Ev("[賢導]トレイラ", "暗沌への導き", 3, 4.8, "cmd"), Ev("[賢導]トレイラ", "暗沌への導き", 3, 18.2, "cmd"),
			Ev("[賢導]トレイラ", "暗沌への導き", 3, 18.25, "skl"), Ev("[賢導]トレイラ", "暗沌への導き", 3, 31.7, "cmd"),
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
		// R69: the tail grew by 8 columns to carry `试N`, which is what moved the panel width.
		r.Eq("the-line-width-is-pinned", SkillTimelineText.LineWidth, 129);
		r.Eq("the-tail-width-is-pinned", SkillTimelineText.TailW, 33);
		r.True("the-longest-tail-still-fits-the-tail-column",
			DisplayFormat.DispWidth("并12条9格 n=24 试111 med=13.33s") <= SkillTimelineText.TailW);
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
			Has(SkillTimelineText.Rows(viaSkl, null, true), "角色")
			&& Has(SkillTimelineText.Rows(viaSkl, null, true), "种类")
			&& Has(SkillTimelineText.Rows(viaSkl, null, true), "技能")
			&& Has(SkillTimelineText.Rows(viaSkl, null, true), "发动时刻"));
		r.True("the-page-starts-with-its-title",
			SkillTimelineText.Rows(viaSkl, null, true)[0].Text.StartsWith("技能时间表"));
		r.True("the-title-line-is-the-header-style",
			SkillTimelineText.Rows(viaSkl, null, true)[0].Style == TimelineLineStyle.Header);
		r.True("the-title-says-the-key-that-leaves-the-page",
			SkillTimelineText.Rows(viaSkl, null, true)[0].Text.Contains("F4 返回"));
		// R69: the legend must state the rule that replaced the fixed window.
		r.True("the-legend-states-the-cooldown-rule",
			Has(SkillTimelineText.Rows(viaSkl, null, true), "间隔<该技能自己的冷却"));
		r.True("the-legend-explains-the-attempt-count",
			Has(SkillTimelineText.Rows(viaSkl, null, true), "试N = 本场调用 N 次但没有发动"));

		// an 奥义 row is the amber "look here" style, an auto row is the ordinary one
		r.True("an-over-skill-row-is-highlighted", SkillTimelineText.GroupLine(
			SkillTimelinePolicy.Group(new List<SkillTimelineEvent> { Ev("A", "奥", 2, 5.0, "skl") })[0]
			).Style == TimelineLineStyle.Header);
		r.True("an-auto-row-is-not-highlighted", SkillTimelineText.GroupLine(g[0]).Style == TimelineLineStyle.Row);
		r.True("a-row-with-a-single-activation-prints-no-median",
			!SkillTimelineText.GroupLine(SkillTimelinePolicy.Group(new List<SkillTimelineEvent>
			{ Ev("A", "S", 3, 5.0, "cmd") })[0]).Text.Contains("med="));

		// R69: the pinned width has to hold for EVERY line of the page, not only for the row the R67 case
		// checked -- the R69 legend reached 189 columns against a width of 129 (measured by rendering the
		// real battle's rows), which is exactly the kind of "everything fits except one line" that a
		// row-only case cannot see.
		List<TimelineLine> whole = SkillTimelineText.Rows(viaSkl, tries, true);
		int overWide = 0;
		for (int i = 0; i < whole.Count; i++)
			if (DisplayFormat.DispWidth(whole[i].Text) > SkillTimelineText.LineWidth) overWide++;
		r.Eq("no-line-of-the-page-exceeds-the-pinned-width", overWide, 0);
		r.True("the-page-never-wraps-its-own-legend",
			DisplayFormat.DispWidth(SkillTimelineText.Rows(new List<SkillTimelineEvent>(),
				new List<SkillTimelineAttempt>(), true)[1].Text) <= SkillTimelineText.LineWidth);

		// ---- truncation is always stated ----
		var many = new List<SkillTimelineEvent>();
		for (int i = 0; i < 15; i++) many.Add(Ev("A", "S", 3, 5.0 + i * 2.0, "cmd"));
		// R70: 15 stamps no longer need a `+N` at all -- the row grows a continuation line and prints all of
		// them. The `+N` mark moved to the LAST continuation line, i.e. it now means "more than 51".
		List<TimelineLine> fifteen = SkillTimelineText.GroupLines(SkillTimelinePolicy.Group(many)[0]);
		r.Eq("fifteen-stamps-take-two-lines", fifteen.Count, 2);
		r.True("fifteen-stamps-are-all-printed", fifteen[1].Text.Contains("33.0") && !fifteen[1].Text.Contains("+"));
		r.Eq("exactly-nine-stamps-print-without-a-plus",
			SkillTimelineText.GroupLine(SkillTimelinePolicy.Group(many.GetRange(0, 9))[0]).Text.Contains("+") ? 1 : 0, 0);
		// ... and the first line still carries the first NINE of them (10 stamps: the 9th is 53.0, the 10th 59.0).
		r.True("the-first-line-prints-the-first-nine-stamps",
			SkillTimelineText.GroupLine(GroupOf(10)).Text.Contains("53.0")
			&& !SkillTimelineText.GroupLine(GroupOf(10)).Text.Contains("59.0"));

		// ---- R70: a row grows DOWNWARDS instead of ending in `+N` (the user's request) ----
		r.Eq("a-continuation-line-carries-fourteen-stamps", SkillTimelineText.ContinuationStamps, 14);
		r.Eq("a-row-may-take-three-continuation-lines", SkillTimelineText.MaxStampLines, 3);
		r.Eq("the-row-cap-is-fifty-one-stamps",
			SkillTimelineText.MaxStamps + SkillTimelineText.MaxStampLines * SkillTimelineText.ContinuationStamps, 51);
		// 9 + 14 = 23 -> exactly two lines; 24 -> three; 51 -> four lines and still no `+`.
		r.Eq("twenty-three-stamps-take-two-lines", WrapLines(23), 2);
		r.Eq("twenty-four-stamps-take-three-lines", WrapLines(24), 3);
		r.Eq("fifty-one-stamps-take-four-lines", WrapLines(51), 4);
		r.True("fifty-one-stamps-still-print-everything",
			!SkillTimelineText.GroupLines(GroupOf(51))[3].Text.Contains("+"));
		// 52 -> the last continuation line spends a cell on `+2` (the mark costs the cell it sits in, so the
		// row shows 50 of the 52 stamps).
		List<TimelineLine> over = SkillTimelineText.GroupLines(GroupOf(52));
		r.Eq("fifty-two-stamps-still-take-four-lines", over.Count, 4);
		r.True("the-fifty-second-stamp-is-reported-as-plus-two", over[3].Text.Contains("+2"));
		// The cap is real: 80 stamps print 50 and say `+30`.
		List<TimelineLine> huge = SkillTimelineText.GroupLines(GroupOf(80));
		r.Eq("a-huge-row-stops-at-four-lines", huge.Count, 4);
		r.True("a-huge-row-states-what-it-dropped", huge[3].Text.Contains("+30"));
		// Every continuation line starts at the first line's stamp column, so the columns stay aligned.
		string pad = new string(' ', SkillTimelineText.Indent);
		foreach (TimelineLine cl in huge.GetRange(1, 3))
			r.True("a-continuation-line-starts-under-the-stamp-column", cl.Text.StartsWith(pad));
		r.True("a-continuation-line-carries-no-name-or-tail",
			!huge[1].Text.Contains("A") && !huge[1].Text.Contains("n="));
		r.True("every-continuation-line-fits-the-pinned-width",
			DisplayFormat.DispWidth(huge[3].Text) <= SkillTimelineText.LineWidth);
		// A row with no stamps (attempt-only) must NOT grow a continuation line.
		List<SkillTimelineGroup> onlyTries = SkillTimelinePolicy.Group(new List<SkillTimelineEvent>());
		SkillTimelinePolicy.ApplyAttempts(onlyTries, new List<SkillTimelineAttempt> { Att("A", "S", 3, 4) });
		r.Eq("an-attempt-only-row-takes-one-line", SkillTimelineText.GroupLines(onlyTries[0]).Count, 1);
		r.Str("the-first-line-of-a-row-is-the-row", SkillTimelineText.GroupLines(GroupOf(30))[0].Text,
			SkillTimelineText.GroupLine(GroupOf(30)).Text);

		// ---- R70: the page says how many non-ally observations were dropped (the title promises 我方) ----
		r.Str("the-channel-line-hides-a-zero-drop-count", SkillTimelineText.ChannelLine(63, 22, 0),
			SkillTimelineText.ChannelLine(63, 22));
		r.True("the-channel-line-reports-the-non-ally-drops",
			SkillTimelineText.ChannelLine(63, 22, 17).Contains("剔除非我方 17 条"));
		r.True("the-page-prints-the-non-ally-drops",
			Has(SkillTimelineText.Rows(viaSkl, tries, 17, true), "剔除非我方 17 条"));
		r.True("the-page-shows-no-drop-suffix-when-nothing-was-dropped",
			!Has(SkillTimelineText.Rows(viaSkl, tries, 0, true), "剔除非我方"));
		r.True("the-legend-mentions-the-continuation-lines",
			Has(SkillTimelineText.Rows(viaSkl, null, true), "发动时刻一屏放不下时接着下一行"));

		// ---- R71: the axis is stated when the origin was moved onto the game's battle start ----
		r.True("the-page-states-the-shifted-origin",
			Has(SkillTimelineText.Rows(viaSkl, null, 0, true, 0.93), "时刻起点 = 游戏自己的战斗开始(已补回本插件晚看到的 +0.93s"));
		r.True("an-unshifted-page-claims-nothing-about-the-origin",
			!Has(SkillTimelineText.Rows(viaSkl, null, 0, true, 0.0), "时刻起点"));
		r.True("the-origin-line-fits-the-pinned-width",
			DisplayFormat.DispWidth(Find(SkillTimelineText.Rows(viaSkl, null, 0, true, 0.93), "时刻起点"))
			<= SkillTimelineText.LineWidth);

		var lots = new List<SkillTimelineEvent>();
		for (int i = 0; i < 16; i++) lots.Add(Ev("U" + i.ToString("D2", CultureInfo.InvariantCulture), "S", 3, 5.0 + i, "cmd"));
		List<TimelineLine> lotsLines = SkillTimelineText.Rows(lots, null, true);
		int rows = 0;
		for (int i = 0; i < lotsLines.Count; i++) if (lotsLines[i].Text.Contains("自动1")) rows++;
		r.Eq("only-14-rows-are-printed", rows, 14);
		r.True("the-truncated-tail-is-reported", Has(lotsLines, "... 还有 2 行(共 16 行"));
	}

	// ------------------------------------------------------------------ helpers

	private static SkillTimelineEvent Ev(string unit, string skill, int type, double active, string channel)
	{
		return Ev(unit, skill, type, active, channel, 0.0);
	}

	/// <summary>R69: <paramref name="coolSeconds"/> is the skill's own cooldown, the fold threshold. 0
	/// keeps the R66 behaviour (the fixed floor), which is what the pre-R69 cases exercise.</summary>
	private static SkillTimelineEvent Ev(string unit, string skill, int type, double active, string channel,
		double coolSeconds)
	{
		return new SkillTimelineEvent
		{
			Unit = unit, Skill = skill, Type = type, Channel = channel,
			Active = active, Wall = active * 1.48, CoolSeconds = coolSeconds,
		};
	}

	private static SkillTimelineAttempt Att(string unit, string skill, int type, int count)
	{
		return new SkillTimelineAttempt { Unit = unit, Skill = skill, Type = type, Count = count };
	}

	/// <summary>R70: one row with `n` stamps 6 s apart, so no fold can absorb them.</summary>
	private static SkillTimelineGroup GroupOf(int n)
	{
		var evs = new List<SkillTimelineEvent>();
		for (int i = 0; i < n; i++) evs.Add(Ev("A", "S", 3, 5.0 + i * 6.0, "cmd"));
		return SkillTimelinePolicy.Group(evs)[0];
	}

	private static int WrapLines(int n)
	{
		return SkillTimelineText.GroupLines(GroupOf(n)).Count;
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

	/// <summary>The first line containing <paramref name="needle"/> ("" when none).</summary>
	private static string Find(List<TimelineLine> lines, string needle)
	{
		for (int i = 0; i < lines.Count; i++) if (lines[i].Text.Contains(needle)) return lines[i].Text;
		return "";
	}
}
