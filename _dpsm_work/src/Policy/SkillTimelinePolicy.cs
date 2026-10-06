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
/// ## The merge window (<see cref="MergeSeconds"/>)
/// Two channels observe the same activation: the game's auto-skill command postfix (`cmd`, verified across
/// 9 battles / 471 rows) and the game's skill-record sink (`rec`, the only channel that also covers
/// 奥义/特殊技能). MEASURED 2026-10-06 on the 471 command rows: the shortest gap that the charge counter
/// corroborates as a REAL repeat is 0.30 battle seconds (T.O.W.E.R.typeR, whose counter was read back at
/// full charge on both rows), while same-instant double reports land at 0.00-0.20 s. The window is
/// therefore 0.25 battle seconds -- below the shortest corroborated repeat, above the double reports.
/// A merged row is never hidden: <see cref="SkillTimelineGroup.Merged"/> counts it and the table prints
/// `×N`, so "one activation" and "a burst of three inside the window" cannot look alike.
///
/// ## The time axis
/// Everything is computed on the BATTLE clock (`SkillTimelineEvent.Active`), never on the wall clock: the
/// game pauses its battle clock (measured: トレイラ's counter held 186/240 for 6 wall seconds while the
/// battle clock stood still), so a wall-clock cadence would read the pauses as skill downtime.
/// </summary>
internal static class SkillTimelinePolicy
{
	/// <summary>Activations closer than this on the battle clock are ONE activation (see the class docs).</summary>
	internal const double MergeSeconds = 0.25;

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
	/// all ordinal. Two channels reporting the same activation are merged by <see cref="MergeSeconds"/>;
	/// the channel list keeps the first-seen order so "this row came from cmd only" stays visible.
	/// Rows with an empty unit or skill, or a non-finite battle clock, are skipped -- they cannot be
	/// placed on the axis, and inventing t=0 for them would draw a fake activation at the battle start.
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
		// Stable sort by the battle clock: the merge window is a neighbour test, so the input order of
		// equal stamps must not change the result.
		ordered.Sort(delegate (SkillTimelineEvent a, SkillTimelineEvent b)
		{
			int c = a.Active.CompareTo(b.Active);
			return (c != 0) ? c : a.Wall.CompareTo(b.Wall);
		});

		for (int i = 0; i < ordered.Count; i++)
		{
			SkillTimelineEvent e = ordered[i];
			string key = e.Unit + "\u0001" + e.Skill + "\u0001" + e.Type.ToString(System.Globalization.CultureInfo.InvariantCulture);
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
			if (n > 0 && (e.Active - g.Stamps[n - 1]) < MergeSeconds)
			{
				g.Merged++;
				g.Multiplicity[n - 1]++;
				continue;
			}
			g.Stamps.Add(e.Active);
			g.Multiplicity.Add(1);
		}

		groups.Sort(delegate (SkillTimelineGroup a, SkillTimelineGroup b)
		{
			int c = string.CompareOrdinal(a.Unit, b.Unit);
			if (c != 0) return c;
			c = KindRank(a.Type).CompareTo(KindRank(b.Type));
			if (c != 0) return c;
			return string.CompareOrdinal(a.Skill, b.Skill);
		});
		return groups;
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

	/// <summary>Kept activations, ascending on the battle clock. Consecutive entries are at least
	/// <see cref="SkillTimelinePolicy.MergeSeconds"/> apart by construction.</summary>
	internal readonly List<double> Stamps = new List<double>();

	/// <summary>Observation channels that produced this row, first-seen order
	/// (<see cref="SkillTimelineEvent.ChannelCommand"/> / <see cref="SkillTimelineEvent.ChannelRecord"/>).</summary>
	internal readonly List<string> Channels = new List<string>();

	/// <summary>Raw rows this row was built from.</summary>
	internal int Events;

	/// <summary>Rows folded into the previous stamp by the merge window. Never hidden: the table prints
	/// it as `并N条` when this is non-zero.</summary>
	internal int Merged;

	/// <summary>R67: how many raw rows each kept stamp absorbed (1 = no fold), parallel to
	/// <see cref="Stamps"/>. WHY: R66 printed `x(Merged+1)` and the legend read it as "N activations in
	/// ONE cell" -- measured 2026-10-06, メアリー showed `x3` while her two folds landed in two DIFFERENT
	/// cells (49.10+49.10, 79.30+79.37); 3 of the 5 marked rows that battle said something false.</summary>
	internal readonly List<int> Multiplicity = new List<int>();

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
