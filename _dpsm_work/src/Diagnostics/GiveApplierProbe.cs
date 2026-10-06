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

	// ---- R54: the EXACT (target, modifier) map -----------------------------------------------------------------
	// WHY IT EXISTS. The legacy map above is keyed by TARGET NAME ONLY, so its answer means "who last gave this
	// target ANYTHING". MEASURED 2026-10-05 on battle_411001_20261005_135533: the granted rule
	// 被伤害+10%(赋予) (6,705,889.97) was credited to イグナ, whose only granted modifier is 80/5 (バルザイの
	// 偃月刀 id=10120), while the roster's holders of 1006/-10 are エヴァラス・フラウ and チェイシィ. The legacy
	// answer is therefore counted and REJECTED for attribution; only an exact (target,type/param) match credits.
	internal static int GrantKeyReads;
	internal static int GrantKeyErrors;
	internal static int ExactHits;
	internal static int ExactMisses;
	/// <summary>Lookups where NO exact key matched but the legacy target-only map WOULD have answered: the
	/// measured size of the wrong answers the old route produced.</summary>
	internal static int TargetOnlyRejected;

	internal struct Row
	{
		internal double T;
		internal string Kind;
		internal string Owner;
		internal string Guest;
	}

	private static readonly List<Row> _rows = new List<Row>(64);
	private static readonly Dictionary<string, string> _giver = new Dictionary<string, string>();
	/// <summary>Key = targetName + "|" + type + "/" + param. The exact identity, which is what a granted
	/// fold asks about.</summary>
	private static readonly Dictionary<string, string> _giverMod = new Dictionary<string, string>();

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
			string mod = null;
			if (g != null)
			{
				try { mod = NewestGrantKey(guest); }
				catch { GrantKeyErrors++; }
			}
			if (g != null)
			{
				string prev;
				if (_giver.TryGetValue(g, out prev) && prev != null && o != null && prev != o) MultiGiver++;
				_giver[g] = o;
				if (mod != null) _giverMod[g + "|" + mod] = o;
			}
			Recorded++;
		}
		catch { Errors++; }
	}

	/// <summary>The unit that most recently granted something to `targetName`; null when never seen.
	/// The lookup is counted so "the hook never fired" and "the hook fired but the map missed" differ.
	/// R54: this is EVIDENCE ONLY -- it cannot say WHICH modifier was granted, so it must not be used to
	/// attribute a fold. Use <see cref="LastGiverExact"/> for that.</summary>
	internal static string LastGiver(string targetName)
	{
		if (targetName == null) { LookupMisses++; return null; }
		string v;
		if (_giver.TryGetValue(targetName, out v) && v != null) { LookupHits++; return v; }
		LookupMisses++;
		return null;
	}

	/// <summary>
	/// The unit that granted THIS modifier (type/param) to `targetName`; null when that exact pair was never
	/// recorded. Only an exact match may credit a granted fold (R54); the legacy target-only answer is counted
	/// as <see cref="TargetOnlyRejected"/> so the size of the old wrong answers stays measurable.
	/// </summary>
	internal static string LastGiverExact(string targetName, int type, int param)
	{
		if (targetName == null)
		{
			ExactMisses++;
			return null;
		}
		string key = targetName + "|" + type + "/" + param;
		string v;
		if (_giverMod.TryGetValue(key, out v) && v != null) { ExactHits++; return v; }
		ExactMisses++;
		string legacy;
		if (_giver.TryGetValue(targetName, out legacy) && legacy != null) TargetOnlyRejected++;
		return null;
	}

	/// <summary>The (type/param) of the grant just applied to `guest`, read from the TARGET's own give list.
	/// The hook is a POSTFIX, so the entry exists by the time this runs -- the same assumption the per-hit
	/// given fold already relies on. Every read is individually guarded and the failures are counted.</summary>
	private static string NewestGrantKey(BattleObject guest)
	{
		if (GameRef.IsNull(guest)) return null;
		var list = guest.m_giveTalentData;
		if (list == null) return null;
		string best = null;
		int n = list.Count;
		if (n > 64) n = 64;
		for (int i = 0; i < n; i++)
		{
			try
			{
				var gd = list[i];
				if (gd == null) continue;
				if (gd.isDeleted) continue;
				var t = gd.talent;
				if (t == null) { try { t = gd.original; } catch { } }
				if (t == null) continue;
				var td = t.TalentData;
				if (td == null) continue;
				int ty = (int)td.TalentType;
				int v = td.GetParam(0);
				best = ty + "/" + v;
				GrantKeyReads++;
			}
			catch { GrantKeyErrors++; }
		}
		return best;
	}

	internal static void Reset()
	{
		_rows.Clear();
		_giver.Clear();
		HookCalls.Clear();
		Recorded = 0; NullOwner = 0; NullGuest = 0; Errors = 0; RowsDropped = 0; MultiGiver = 0;
		LookupHits = 0; LookupMisses = 0;
		_giverMod.Clear();
		GrantKeyReads = 0; GrantKeyErrors = 0; ExactHits = 0; ExactMisses = 0; TargetOnlyRejected = 0;
	}

	/// <summary>R72: same correction as <see cref="StatusApplierProbe.ShiftTimes"/> -- this probe is fed by a
	/// talent-grant hook, not by the held recorder, so its stamps must be moved by hand.</summary>
	internal static void ShiftTimes(double delta)
	{
		if (!(delta > 0.0)) return;
		for (int i = 0; i < _rows.Count; i++)
		{
			Row r = _rows[i];
			r.T = ClockOriginHoldPolicy.Shift(r.T, delta);
			_rows[i] = r;
		}
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
		  // R54: the exact route and the size of the answer it rejected.
		  .Append(",\"exactHits\":").Append(ExactHits)
		  .Append(",\"exactMisses\":").Append(ExactMisses)
		  .Append(",\"targetOnlyRejected\":").Append(TargetOnlyRejected)
		  .Append(",\"grantKeyReads\":").Append(GrantKeyReads)
		  .Append(",\"grantKeyErrors\":").Append(GrantKeyErrors)
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