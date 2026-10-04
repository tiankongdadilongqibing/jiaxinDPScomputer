using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DpsMeter;

/// <summary>
/// 1.5.5 (贡献归因 A, attempt 2): WHO granted a talent to WHOM, recorded at APPLICATION time.
///
/// WHY NOT THE FIELD READ. 1.5.4 read GiveTalentData.ownerAction -> m_activateCondition.m_ownerObject;
/// measured over 5 battles: that getter THROWS for every entry (giverErrors 38,736-62,486, resolved=0)
/// -- the action reference is not readable from the grant entry, so the chain is a dead end.
///
/// The APPLICATION path does carry both units: the give-type actions'
/// ActExecute(BattleObject owner, BattleObject guest, TalentOption option) (decompiled 2026-10-03:
/// TalentActionAddTalent and TalentActionAddTalentLottery override it; TalentActionAddTalentDuplicate
/// inherits from AddTalent, so the parent patch covers it). This probe records (t, kind, owner, guest)
/// at execution time and keeps "most recent giver per TARGET NAME" for the per-hit given fold to stamp
/// as byUnit. Name is the same key space as the rest of the export; when one target receives grants
/// from more than one distinct giver the map keeps the latest and counts the ambiguity (MultiGiver).
///
/// Everything is counted (per-kind hook calls, rows dropped, lookups hit/missed), because the open
/// question is empirical: do the give actions really run through these two classes in this game?
/// The first battle after 1.5.5 answers it from the numbers.
/// </summary>
internal static class GiveApplierProbe
{
	internal const int MaxRows = 512;
	internal static readonly Dictionary<string, int> HookCalls = new Dictionary<string, int>();
	internal static int Recorded;
	internal static int NullOwner;
	internal static int NullGuest;
	internal static int Errors;
	internal static int RowsDropped;
	internal static int MultiGiver;
	internal static int LookupHits;
	internal static int LookupMisses;

	internal struct Row
	{
		internal double T;
		internal string Kind;
		internal string Owner;
		internal string Guest;
	}

	private static readonly List<Row> _rows = new List<Row>(64);
	private static readonly Dictionary<string, string> _giver = new Dictionary<string, string>();

	internal static void Note(string kind, BattleObject owner, BattleObject guest)
	{
		int n;
		HookCalls.TryGetValue(kind ?? "?", out n);
		HookCalls[kind ?? "?"] = n + 1;
		if (Plugin.CfgGivenGiverHook != null && !Plugin.CfgGivenGiverHook.Value) return;
		try
		{
			string o = Aggregator.NameOf(owner);
			string g = Aggregator.NameOf(guest);
			if (o == null || o == "?") { o = null; NullOwner++; }
			if (g == null || g == "?") { g = null; NullGuest++; }
			double tt = 0.0;
			try { var s = Aggregator.Session; if (s != null) tt = s.ActiveSeconds; } catch { }
			if (_rows.Count < MaxRows)
			{
				var r = new Row();
				r.T = tt;
				r.Kind = kind;
				r.Owner = o;
				r.Guest = g;
				_rows.Add(r);
			}
			else RowsDropped++;
			if (g != null)
			{
				string prev;
				if (_giver.TryGetValue(g, out prev) && prev != null && o != null && prev != o) MultiGiver++;
				_giver[g] = o;
			}
			Recorded++;
		}
		catch { Errors++; }
	}

	/// <summary>The unit that most recently granted something to `targetName`; null when never seen.
	/// The lookup is counted so "the hook never fired" and "the hook fired but the map missed" differ.</summary>
	internal static string LastGiver(string targetName)
	{
		if (targetName == null) { LookupMisses++; return null; }
		string v;
		if (_giver.TryGetValue(targetName, out v) && v != null) { LookupHits++; return v; }
		LookupMisses++;
		return null;
	}

	internal static void Reset()
	{
		_rows.Clear();
		_giver.Clear();
		HookCalls.Clear();
		Recorded = 0; NullOwner = 0; NullGuest = 0; Errors = 0; RowsDropped = 0; MultiGiver = 0;
		LookupHits = 0; LookupMisses = 0;
	}

	internal static string Summary()
	{
		var sb = new StringBuilder(120);
		sb.Append("钩子=");
		foreach (var kv in HookCalls) sb.Append(kv.Key).Append(':').Append(kv.Value).Append(' ');
		sb.Append("记录=").Append(Recorded)
		  .Append(" 目标=").Append(_giver.Count)
		  .Append(" 多来源=").Append(MultiGiver)
		  .Append(" 查询命中=").Append(LookupHits).Append('/').Append(LookupHits + LookupMisses)
		  .Append(" 错误=").Append(Errors);
		return sb.ToString();
	}

	internal static void AppendJson(StringBuilder sb)
	{
		sb.Append(",\"givenApplies\":{\"hookCalls\":{");
		bool first = true;
		foreach (var kv in HookCalls)
		{
			if (!first) sb.Append(',');
			first = false;
			sb.Append('"').Append(kv.Key).Append("\":").Append(kv.Value);
		}
		sb.Append("},\"recorded\":").Append(Recorded)
		  .Append(",\"nullOwner\":").Append(NullOwner)
		  .Append(",\"nullGuest\":").Append(NullGuest)
		  .Append(",\"errors\":").Append(Errors)
		  .Append(",\"rowsDropped\":").Append(RowsDropped)
		  .Append(",\"maxRows\":").Append(MaxRows)
		  .Append(",\"targets\":").Append(_giver.Count)
		  .Append(",\"multiGiver\":").Append(MultiGiver)
		  .Append(",\"lookupHits\":").Append(LookupHits)
		  .Append(",\"lookupMisses\":").Append(LookupMisses)
		  .Append(",\"rows\":[");
		for (int i = 0; i < _rows.Count; i++)
		{
			if (i > 0) sb.Append(',');
			Row r = _rows[i];
			sb.Append("{\"t\":").Append(r.T.ToString("F2", CultureInfo.InvariantCulture))
			  .Append(",\"kind\":\"").Append(JsonText.Str(r.Kind)).Append('"')
			  .Append(",\"owner\":").Append(r.Owner == null ? "null" : "\"" + JsonText.Str(r.Owner) + "\"")
			  .Append(",\"guest\":").Append(r.Guest == null ? "null" : "\"" + JsonText.Str(r.Guest) + "\"")
			  .Append('}');
		}
		sb.Append("]}");
	}
}