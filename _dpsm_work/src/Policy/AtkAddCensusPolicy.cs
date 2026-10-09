using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DpsMeter;

/// <summary>
/// R87 (方案A): fold the battle's SELF-classified attack-power addends into "who could have declared it".
///
/// THE ONE RULE THAT MATTERS. A declaration only counts when it belongs to the HOLDER'S OWN TEAM. This
/// content fields the same character on both sides (measured: ソフィー/ポポロット/レヴァナント/
/// T.O.W.E.R.typeR/[賢導]トレイラ/邪悪なる死のカラス all appear on both teams in one battle), and the
/// per-hit channel cannot tell the two copies apart. Scoping the match to the holder's team is the SAME
/// rule <c>Output/Contribution.cs BuildIndex</c> already applies to <c>f.ByUnit</c>, and it is what makes
/// the match decidable: with it, every one of the 9 (value, reference) triples declared in the reference
/// battle has exactly ONE declarer on our side, while a name-only match would have found two.
///
/// WHAT IS REFUSED, AND COUNTED (never silently attributed):
///   * two or more units of the holder's team declare it -&gt; <see cref="AtkAddCensus.Ambiguous"/>, with
///     the candidate names kept so the refusal is inspectable;
///   * nobody of that team declares it -&gt; <see cref="AtkAddCensus.Undeclared"/>;
///   * the holder has no actor row -&gt; <see cref="AtkAddCensus.NoHolder"/>, because with no team there is
///     no scope and any pick would be a guess.
/// The precedent is <c>Contribution.Resolve</c> / <c>Contribution.NoteUnresolved</c>: an unresolvable fold
/// is a counted bucket, never a coin flip.
///
/// Pure: no Unity, no IL2CPP, no Plugin, no Aggregator. Compiled into the behaviour suite, which is what
/// lets the two identities in <see cref="AtkAddCensus"/> be executed rather than asserted in prose.
/// </summary>
internal static class AtkAddCensusPolicy
{
	/// <summary>
	/// <paramref name="entries"/> is the battle-scoped self tally, <paramref name="selfValues"/> the fold's
	/// own counter, <paramref name="roster"/> every unit of the battle with the addends its loadout declares.
	/// </summary>
	public static AtkAddCensus Build(IList<AtkAddSelfEntry> entries, int selfValues,
	                                 IList<AtkAddRosterUnit> roster)
	{
		var c = new AtkAddCensus();
		c.Values = selfValues;

		// declKey -> the DISTINCT units declaring it. Deduplicated by unit key on purpose: one unit may
		// carry the same declaration twice (measured: ネフェスティス holds 刻印 id25 twice), and that is one
		// declarer, not an ambiguity.
		var byKey = new Dictionary<string, List<AtkAddRosterUnit>>();
		// unit key -> the unit, for the HOLDER's team. Every unit is indexed, not only the declarers: a
		// holder usually declares nothing itself, and without its team there is no scope to match in.
		var unitOf = new Dictionary<int, AtkAddRosterUnit>();
		if (roster != null)
		{
			for (int i = 0; i < roster.Count; i++)
			{
				AtkAddRosterUnit u = roster[i];
				if (u == null) continue;
				if (!unitOf.ContainsKey(u.Key)) unitOf[u.Key] = u;
				for (int j = 0; j < u.DeclKeys.Count; j++)
				{
					string dk = u.DeclKeys[j];
					if (dk == null || dk.Length == 0) continue;
					List<AtkAddRosterUnit> l;
					if (!byKey.TryGetValue(dk, out l)) { l = new List<AtkAddRosterUnit>(2); byKey[dk] = l; }
					bool dup = false;
					for (int m = 0; m < l.Count; m++) if (l[m].Key == u.Key) { dup = true; break; }
					if (!dup) l.Add(u);
				}
			}
		}

		var giverByKey = new Dictionary<int, AtkAddCensusGiver>();
		if (entries != null)
		{
			for (int i = 0; i < entries.Count; i++)
			{
				AtkAddSelfEntry e = entries[i];
				if (e == null) continue;
				c.Entries += e.Count;

				AtkAddRosterUnit holder;
				if (!unitOf.TryGetValue(e.AttackerKey, out holder))
				{
					// No roster row for the holder: no team, therefore no scope, therefore no verdict.
					c.NoHolder += e.Count;
					continue;
				}
				List<AtkAddRosterUnit> cand = null;
				if (e.Key != null && e.Key.Length > 0) byKey.TryGetValue(e.Key, out cand);
				if (cand == null || cand.Count == 0)
				{
					c.Undeclared += e.Count;
					AddItem(c.UndeclaredItems, e, null, 0);
					continue;
				}
				// the holder's OWN team only (see the class comment)
				int n = 0;
				AtkAddRosterUnit uniq = null;
				for (int j = 0; j < cand.Count; j++)
					if (cand[j].Team == holder.Team) { n++; uniq = cand[j]; }
				if (n == 0)
				{
					c.Undeclared += e.Count;
					AddItem(c.UndeclaredItems, e, null, 0);
				}
				else if (n > 1)
				{
					c.Ambiguous += e.Count;
					AddItem(c.AmbiguousItems, e, cand, holder.Team);
				}
				else
				{
					c.Matched += e.Count;
					AtkAddCensusGiver g;
					if (!giverByKey.TryGetValue(uniq.Key, out g))
					{
						g = new AtkAddCensusGiver { Key = uniq.Key, Name = uniq.Name ?? "", Team = uniq.Team };
						giverByKey[uniq.Key] = g;
						c.Givers.Add(g);
					}
					g.Entries += e.Count;
					if (!g.Items.Contains(e.Label)) g.Items.Add(e.Label);
				}
			}
		}

		// Entries is the reference-aware count, Values the fold's own; a fold key can only be SPLIT by the
		// extra reference dimension, never merged, so this difference is never negative. Left unclamped on
		// purpose -- check_export_schema.py asserts it, so a real bug shows up as a red guard.
		c.DupKeyEntries = c.Entries - c.Values;

		c.Givers.Sort(delegate (AtkAddCensusGiver x, AtkAddCensusGiver y)
		{
			int r = y.Entries.CompareTo(x.Entries);
			if (r != 0) return r;
			return x.Key.CompareTo(y.Key);
		});
		for (int i = 0; i < c.Givers.Count; i++) c.Givers[i].Items.Sort(StringComparer.Ordinal);
		SortItems(c.AmbiguousItems);
		SortItems(c.UndeclaredItems);
		return c;
	}

	/// <summary>Fold one entry into its refusal bucket, keeping the candidate names of an ambiguous one so
	/// the refusal can be inspected rather than merely counted.</summary>
	private static void AddItem(List<AtkAddCensusItem> list, AtkAddSelfEntry e, List<AtkAddRosterUnit> cand, int team)
	{
		string key = e.Label ?? "";
		AtkAddCensusItem it = null;
		for (int i = 0; i < list.Count; i++) if (list[i].Item == key) { it = list[i]; break; }
		if (it == null) { it = new AtkAddCensusItem { Item = key }; list.Add(it); }
		it.Entries += e.Count;
		if (cand != null && team != 0 && it.Candidates.Length == 0)
		{
			var sb = new StringBuilder(48);
			for (int i = 0; i < cand.Count; i++)
			{
				if (cand[i].Team != team) continue;
				if (sb.Length > 0) sb.Append(", ");
				sb.Append(cand[i].Name ?? "");
			}
			it.Candidates = sb.ToString();
		}
	}

	private static void SortItems(List<AtkAddCensusItem> list)
	{
		list.Sort(delegate (AtkAddCensusItem x, AtkAddCensusItem y)
		{
			int r = y.Entries.CompareTo(x.Entries);
			if (r != 0) return r;
			return string.CompareOrdinal(x.Item, y.Item);
		});
	}

	/// <summary>The census totals as the export prints them. Kept here (and not in the writer) so the two
	/// identities are executed by the behaviour suite, and so the guard's expectation and the plugin's
	/// output can never be two different arithmetic.</summary>
	public static string Describe(AtkAddCensus c)
	{
		if (c == null) return "entries=0";
		return "entries=" + I(c.Entries) + " values=" + I(c.Values) + " dup=" + I(c.DupKeyEntries)
		     + " matched=" + I(c.Matched) + " ambiguous=" + I(c.Ambiguous)
		     + " undeclared=" + I(c.Undeclared) + " noHolder=" + I(c.NoHolder);
	}

	/// <summary>
	/// The section's JSON, written HERE rather than in the impure writer so the bytes are executable by the
	/// behaviour suite. That is not a style choice: R80 shipped a section whose writer produced unquoted
	/// strings, i.e. a file no parser could read, and no compiled test could see it because the writer sat
	/// in a file the suite cannot compile. The glue that gathers declarations stays impure (it reads
	/// ActorStats.Roster); everything below is pure and pinned.
	/// </summary>
	public static void AppendJson(StringBuilder sb, AtkAddCensus c)
	{
		if (c == null) { sb.Append("{}"); return; }
		sb.Append('{');
		sb.Append("\"entries\":").Append(I(c.Entries));
		sb.Append(",\"values\":").Append(I(c.Values));
		sb.Append(",\"dupKeyEntries\":").Append(I(c.DupKeyEntries));
		sb.Append(",\"matched\":").Append(I(c.Matched));
		sb.Append(",\"ambiguous\":").Append(I(c.Ambiguous));
		sb.Append(",\"undeclared\":").Append(I(c.Undeclared));
		sb.Append(",\"noHolder\":").Append(I(c.NoHolder));
		sb.Append(",\"givers\":[");
		for (int i = 0; i < c.Givers.Count; i++)
		{
			if (i > 0) sb.Append(',');
			AtkAddCensusGiver g = c.Givers[i];
			sb.Append("{\"key\":").Append(I(g.Key));
			sb.Append(",\"name\":\"").Append(JsonText.Str(g.Name)).Append('"');
			sb.Append(",\"team\":").Append(I(g.Team));
			sb.Append(",\"entries\":").Append(I(g.Entries));
			sb.Append(",\"items\":[");
			for (int j = 0; j < g.Items.Count; j++)
			{
				if (j > 0) sb.Append(',');
				sb.Append('"').Append(JsonText.Str(g.Items[j])).Append('"');
			}
			sb.Append("]}");
		}
		sb.Append("],\"ambiguousItems\":");
		AppendItems(sb, c.AmbiguousItems);
		sb.Append(",\"undeclaredItems\":");
		AppendItems(sb, c.UndeclaredItems);
		sb.Append('}');
	}

	private static void AppendItems(StringBuilder sb, List<AtkAddCensusItem> list)
	{
		sb.Append('[');
		for (int i = 0; i < list.Count; i++)
		{
			if (i > 0) sb.Append(',');
			AtkAddCensusItem it = list[i];
			sb.Append("{\"item\":\"").Append(JsonText.Str(it.Item)).Append('"');
			sb.Append(",\"entries\":").Append(I(it.Entries));
			sb.Append(",\"candidates\":\"").Append(JsonText.Str(it.Candidates)).Append("\"}");
		}
		sb.Append(']');
	}

	private static string I(int v)
	{
		return v.ToString(CultureInfo.InvariantCulture);
	}
}
