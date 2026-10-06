using System;
using System.Collections.Generic;

namespace DpsMeter;

/// <summary>
/// R66: the PURE arithmetic of the 技能时间表 -- raw activations in, one row per (unit, skill) out.
///
/// WHY A POLICY CLASS. Same reason as the other Policy/*.cs files: this is the part that can be WRONG
/// SILENTLY. A table that merges two real activations into one, that drops the last row, or whose median
/// is computed on the wall clock looks perfectly reasonable on screen. Compiling it into BehaviorTests is
/// what makes each of those decisions executable offline and mutable-by-the-negative-control.
///
/// ## The fold (R69; it was a fixed 0.25 s window in R66/R67, and that window was WRONG)
/// WHAT ONE ACTIVATION LOOKS LIKE. The game calls the auto-skill command ONCE PER ATTACK, and while the
/// skill is executing, each further call sees the skill in the `Using` state again. So one activation
/// leaves a CLUSTER of `cmd` calls, and the cluster is spread over the skill's execution window -- measured
/// spans 1.5 s, 2.1 s, 2.9 s, 3.0 s, 4.53 s in the 9-battle corpus. A 0.25 s window therefore split single
/// activations into several stamps and published a cadence that was too fast: on that corpus the shipped
/// rule produced 184 sub-cooldown intervals out of 342.
///
/// THE RULE THAT REPLACED IT IS A PHYSICAL BOUND, NOT A TUNED NUMBER. The game cannot execute a skill
/// twice inside its own cooldown (`Skill.CoolTime`, i.e. `CoolTimeFrame / 30`), so two candidate calls
/// closer together than THAT SKILL'S cooldown are the same execution and are folded into one stamp, the
/// first one. <see cref="MergeSeconds"/> survives only as the floor for events whose cooldown could not be
/// read. MEASURED 2026-10-06 on both corpora with "status == Using" as the candidate filter:
///   9-battle corpus (471 rows):  174 intervals, 0 below their cooldown (the 0.25 s window: 23 violations)
///   the 411001 battle (97 rows):  47 intervals, 0 below their cooldown (the 0.25 s window: 0 violations)
/// and no folded cluster in either corpus spanned its own cooldown (the widest was 4.53 s against a 10 s
/// cooldown). Folding is never hidden: <see cref="SkillTimelineGroup.Merged"/> counts it and the table
/// prints `并N条M格`, so "one activation" and "a burst of three inside the window" cannot look alike.
///
/// ## The time axis
/// Everything is computed on the BATTLE clock (`SkillTimelineEvent.Active`), never on the wall clock: the
/// game pauses its battle clock (measured: トレイラ's counter held 186/240 for 6 wall seconds while the
/// battle clock stood still), so a wall-clock cadence would read the pauses as skill downtime.
/// </summary>
internal static class SkillTimelinePolicy
{
	/// <summary>R69: the FLOOR of the fold -- same-instant double reports are folded even when the skill's
	/// own cooldown could not be read. The fold itself is <see cref="FoldSeconds"/> (the class docs explain
	/// why a fixed window was the wrong rule).</summary>
	internal const double MergeSeconds = 0.25;

	/// <summary>How close two candidate calls of the same skill must be to count as ONE execution: the
	/// skill's own cooldown, with <see cref="MergeSeconds"/> as the floor for a cooldown that could not be
	/// read. Never NaN and never below the floor, so a bad reading cannot turn folding off.</summary>
	internal static double FoldSeconds(SkillTimelineEvent e)
	{
		double cool = (e == null) ? 0.0 : e.CoolSeconds;
		if (double.IsNaN(cool) || double.IsInfinity(cool) || cool < MergeSeconds) return MergeSeconds;
		return cool;
	}

	/// <summary>Rows the table shows before it collapses the rest into a "... 还有 N 项" line.</summary>
	internal const int MaxGroups = 14;

	/// <summary>Activation stamps printed per row before "...+N". R67: 12 -> 9, because each stamp cell
	/// grew from 5 to 6 columns (see SkillTimelineText.StampW) and the row must keep fitting the panel.</summary>
	internal const int MaxStamps = 9;

	/// <summary>Chinese label of a `Skill.Type`. Deliberately spells out the two auto-skill slots,
	/// because "which of the two auto skills fired" is the question this page exists to answer.</summary>
	internal static string KindLabel(int type)
	{
		if (type == 0) return "技能";
		if (type == 1) return "特殊";
		if (type == 2) return "奥义";
		if (type == 3) return "自动1";
		if (type == 4) return "自动2";
		return "类型" + type;
	}

	/// <summary>Row order, and the reason it is not "when it first fired": a table whose rows reorder as
	/// the battle goes on cannot be read while playing. 奥义 first, then 特殊, then the plain skill, then
	/// the two auto slots -- and inside one kind, by name.</summary>
	internal static int KindRank(int type)
	{
		if (type == 2) return 0;
		if (type == 1) return 1;
		if (type == 0) return 2;
		if (type == 3) return 3;
		if (type == 4) return 4;
		return 5;
	}

	internal static bool IsAuto(int type)
	{
		return type == 3 || type == 4;
	}

	/// <summary>
	/// Collapse a raw activation stream into one row per (unit, skill, type).
	///
	/// The order is DETERMINISTIC and independent of arrival order: (unit name, kind rank, skill name),
	/// all ordinal. Two calls of the same skill closer than its own cooldown are merged into one stamp (see
	/// <see cref="FoldSeconds"/>); the channel list keeps the first-seen order so "this row came from cmd
	/// only" stays visible. Rows with an empty unit or skill, or a non-finite battle clock, are skipped --
	/// they cannot be placed on the axis, and inventing t=0 for them would draw a fake activation at the
	/// battle start.
	/// </summary>
	internal static List<SkillTimelineGroup> Group(IList<SkillTimelineEvent> events)
	{
		var groups = new List<SkillTimelineGroup>();
		if (events == null || events.Count == 0) return groups;

		var byKey = new Dictionary<string, SkillTimelineGroup>(StringComparer.Ordinal);
		var ordered = new List<SkillTimelineEvent>(events.Count);
		for (int i = 0; i < events.Count; i++)
		{
			SkillTimelineEvent e = events[i];
			if (e == null) continue;
			if (string.IsNullOrEmpty(e.Unit) || string.IsNullOrEmpty(e.Skill)) continue;
			if (double.IsNaN(e.Active) || double.IsInfinity(e.Active)) continue;
			ordered.Add(e);
		}
		// Stable sort by the battle clock: the fold is a neighbour test, so the input order of equal
		// stamps must not change the result.
		ordered.Sort(delegate (SkillTimelineEvent a, SkillTimelineEvent b)
		{
			int c = a.Active.CompareTo(b.Active);
			return (c != 0) ? c : a.Wall.CompareTo(b.Wall);
		});

		for (int i = 0; i < ordered.Count; i++)
		{
			SkillTimelineEvent e = ordered[i];
			string key = Key(e.Unit, e.Skill, e.Type);
			SkillTimelineGroup g;
			if (!byKey.TryGetValue(key, out g))
			{
				g = new SkillTimelineGroup { Unit = e.Unit, Skill = e.Skill, Type = e.Type };
				byKey[key] = g;
				groups.Add(g);
			}
			g.Events++;
			if (!string.IsNullOrEmpty(e.Channel) && !g.Channels.Contains(e.Channel)) g.Channels.Add(e.Channel);
			int n = g.Stamps.Count;
			if (n > 0 && (e.Active - g.Stamps[n - 1]) < g.StampFold[n - 1])
			{
				g.Merged++;
				g.Multiplicity[n - 1]++;
				continue;
			}
			g.Stamps.Add(e.Active);
			g.Multiplicity.Add(1);
			// The threshold travels with the stamp it was read from: a unit can raise a skill's level (and
			// its cooldown) mid-battle, and the fold must then follow the row that STARTED the cluster.
			g.StampFold.Add(FoldSeconds(e));
		}

		groups.Sort(CompareGroups);
		return groups;
	}

	/// <summary>Row order: unit, then kind, then skill name -- the same order for every producer, so a row
	/// added after the fact (R69's `ApplyAttempts`) cannot land somewhere else.</summary>
	internal static int CompareGroups(SkillTimelineGroup a, SkillTimelineGroup b)
	{
		int c = string.CompareOrdinal(a.Unit, b.Unit);
		if (c != 0) return c;
		c = KindRank(a.Type).CompareTo(KindRank(b.Type));
		if (c != 0) return c;
		return string.CompareOrdinal(a.Skill, b.Skill);
	}

	/// <summary>
	/// R69: attach the per-row attempt counts to the rows the table is about to print, and materialise the
	/// rows that have attempts but NO activation. The second half matters: a unit whose skill was only ever
	/// called while charging used to be invisible (it produced no event), and "my unit never fired" and "my
	/// unit fired nothing because its cooldown never completed" are different statements.
	///
	/// Pure, and deliberately separate from <see cref="Group"/>: the fold must never see an attempt (an
	/// attempt has no stamp to fold).
	/// </summary>
	internal static void ApplyAttempts(List<SkillTimelineGroup> groups, IList<SkillTimelineAttempt> attempts)
	{
		if (groups == null || attempts == null || attempts.Count == 0) return;
		var byKey = new Dictionary<string, SkillTimelineGroup>(StringComparer.Ordinal);
		for (int i = 0; i < groups.Count; i++)
			byKey[Key(groups[i].Unit, groups[i].Skill, groups[i].Type)] = groups[i];
		bool added = false;
		for (int i = 0; i < attempts.Count; i++)
		{
			SkillTimelineAttempt a = attempts[i];
			if (a == null || a.Count <= 0) continue;
			if (string.IsNullOrEmpty(a.Unit) || string.IsNullOrEmpty(a.Skill)) continue;
			SkillTimelineGroup g;
			if (!byKey.TryGetValue(Key(a.Unit, a.Skill, a.Type), out g))
			{
				g = new SkillTimelineGroup { Unit = a.Unit, Skill = a.Skill, Type = a.Type };
				byKey[Key(a.Unit, a.Skill, a.Type)] = g;
				groups.Add(g);
				added = true;
			}
			g.Attempts += a.Count;
		}
		if (added) groups.Sort(CompareGroups);
	}

	private static string Key(string unit, string skill, int type)
	{
		return unit + "\u0001" + skill + "\u0001" + type.ToString(System.Globalization.CultureInfo.InvariantCulture);
	}

	/// <summary>Median gap between consecutive KEPT stamps, on the battle clock. 0 when there is no gap
	/// (a single activation, or a burst that merged into one). Delegates to the cadence policy's median so
	/// "which samples count" cannot drift between the [AUTOSK] summary and this table.</summary>
	internal static double MedianInterval(SkillTimelineGroup g)
	{
		if (g == null || g.Stamps.Count < 2) return 0.0;
		var gaps = new List<double>(g.Stamps.Count - 1);
		for (int i = 1; i < g.Stamps.Count; i++)
		{
			double d = g.Stamps[i] - g.Stamps[i - 1];
			if (d > 0.0) gaps.Add(d);
		}
		return AutoSkillCadencePolicy.Median(gaps.ToArray());
	}
}

/// <summary>One row of the 技能时间表: one of our units and ONE of its skills.</summary>
internal sealed class SkillTimelineGroup
{
	internal string Unit = "";
	internal string Skill = "";
	internal int Type = -1;

	/// <summary>Kept activations, ascending on the battle clock. Consecutive entries are at least their
	/// own skill's cooldown apart by construction (R69; see <see cref="SkillTimelinePolicy.FoldSeconds"/>),
	/// which is what makes the gaps between them a physical statement and not an artefact of a window.</summary>
	internal readonly List<double> Stamps = new List<double>();

	/// <summary>Observation channels that produced this row, first-seen order
	/// (<see cref="SkillTimelineEvent.ChannelCommand"/> / <see cref="SkillTimelineEvent.ChannelRecord"/>).</summary>
	internal readonly List<string> Channels = new List<string>();

	/// <summary>Raw rows this row was built from.</summary>
	internal int Events;

	/// <summary>Rows folded into the previous stamp by the fold. Never hidden: the table prints
	/// it as `并N条M格` when this is non-zero.</summary>
	internal int Merged;

	/// <summary>R67: how many raw rows each kept stamp absorbed (1 = no fold), parallel to
	/// <see cref="Stamps"/>. WHY: R66 printed `x(Merged+1)` and the legend read it as "N activations in
	/// ONE cell" -- measured 2026-10-06, メアリー showed `x3` while her two folds landed in two DIFFERENT
	/// cells (49.10+49.10, 79.30+79.37); 3 of the 5 marked rows that battle said something false.</summary>
	internal readonly List<int> Multiplicity = new List<int>();

	/// <summary>R69: the fold threshold in force at each kept stamp (game seconds), parallel to
	/// <see cref="Stamps"/>. It comes from the event that STARTED the cluster, so a mid-battle cooldown
	/// change cannot retroactively re-fold an already published stamp.</summary>
	internal readonly List<double> StampFold = new List<double>();

	/// <summary>R69: calls of this skill that did NOT execute (the game was still charging, or charged but
	/// not executing). They are never placed on the time axis and never enter the median -- they are the
	/// answer to "why does my unit look like it fires every 5 s when its cooldown is 99 s". Kept per row so
	/// the table can print `试N` next to the activations it did produce.</summary>
	internal int Attempts;

	/// <summary>Kept stamps that absorbed at least one fold.</summary>
	internal int BurstCells
	{
		get
		{
			int c = 0;
			for (int i = 0; i < Multiplicity.Count; i++) if (Multiplicity[i] > 1) c++;
			return c;
		}
	}
}
