using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DpsMeter;

/// <summary>
/// 1.7.2 (阶段 G, step 1): WHO granted each entry of a unit's APPLIED-PARAMETER dictionary.
///
/// THE GAP THIS FILLS. The contribution model (log-share/1) only credits the multiplicative rule
/// channels (calc.fold). A flat/percent 攻击力 addition is not a fold: it is already inside the
/// attacker's 攻击力, so it lands in that attacker's 基础 and its PROVIDER gets nothing. The user
/// reported exactly this for ルナリス, and the master dump confirms it is structural, not a one-off:
/// ability 12060 エンチャンター carries a single talent, id 8 = eBuffType.PowerActualPlus, with
/// TalentDefine.RangeType = 29 (FriendTeamAllExcludeToken) and param [[300],[0],[200,3000]] --
/// i.e. 攻击力+300, reference ExistenceTime 3000, to the whole team except tokens. Her artifacts
/// (水神クタアト +7%/+10%, クトゥルフの邪神像 +10%) use RangeType 3 = FriendTeamAll, and 29 is the ONLY
/// team-wide ATK grant in the entire ability table, so this source is uniquely hers.
///
/// WHY THE EXISTING READ CANNOT ANSWER IT. `CompositionProbe.BuffParamTableDump` walks the same
/// dictionary but keeps only the LAST throttled sample per unit and printed no owner. Measured in
/// battle_411001_20261004_023627: 攻击力/Actual/300 appears on exactly the 8 non-token team units and
/// 攻击力/Rate/10 even on the tokens, which already matches RangeType 29 versus 3 -- but the section
/// names no giver, and two units are missing the Rate/7 row 水神クタアト grants, i.e. it is a late
/// per-unit sample and not history.
///
/// WHAT THIS RECORDS. A UNION per unit: (target, type, value, owner) -> first/last seen + count, so an
/// entry that expires before the export cannot erase the fact that it existed. The owner exists on BOTH
/// sides of a dictionary entry -- `BuffParam.m_owner` (its constructor takes a BattleObject owner) and
/// `ParamData.Owner` -- and interop materialises both as properties, so this is a READ, not a hook.
/// Which side the game fills with the GIVER has never been read before, so both are stored and their
/// DISAGREEMENT is counted; a self-buff cannot tell the two apart, but a team-wide +300 can. The
/// deliverable is therefore falsifiable in one battle: either value.Owner repeats ルナリス on units
/// that are not ルナリス (giver), or it repeats the dictionary's own unit (holder).
///
/// Read inside the caller's existing single walk (a second walk of a live dictionary would describe a
/// different instant), on the same 500 ms-per-unit throttle. Read-only, bounded twice -- 64 entries per
/// read and a per-battle read budget -- with every drop counted, per the project rule that a bound must
/// be visible instead of a silent frame-time cliff.
/// </summary>
internal static class ParamOwnerProbe
{
	/// <summary>Entries read per walk. Deliberately larger than the historical 14, which is what made
	/// the owner of an 攻击力 entry unobservable: past the cap the entry was never read at all.</summary>
	internal const int MaxEntriesPerRead = 64;

	/// <summary>Distinct (unit, target, type, value, owner, ref) rows kept. Well above what this game's
	/// dictionary holds per unit; the overflow is a counter, not a silent truncation.</summary>
	internal const int MaxUnionRows = 1024;

	/// <summary>Per-battle ceiling on entry reads. The walk is throttled (~2/s/unit), so this is a
	/// frame-time guard for a pathological dictionary, not an expected limit.</summary>
	internal const int MaxEntryReadsPerBattle = 20000;

	/// <summary>BuffParamData.BuffTarget.Power (攻击力). The one target whose owner is always read,
	/// because "who granted this attack power" is the reason this channel exists.</summary>
	internal const int AtkTarget = 1;

	internal static int Calls;
	internal static int Errors;
	internal static int Units;
	internal static int EntriesSeen;
	internal static int EntriesScanned;
	internal static int EntryReads;
	internal static int BudgetCapped;
	internal static int EntriesDropped;
	internal static int OwnerNull;
	internal static int OwnerSelf;
	internal static int OwnerOther;
	internal static int KeyOwnerNull;
	internal static int OwnerErrors;
	internal static int CrossChecked;
	internal static int Mismatch;
	internal static int UnionOverflow;
	private static string _firstError = "";

	private struct Row
	{
		internal string Unit;
		internal string Owner;
		internal string KeyOwner;
		internal int Value;
		internal string TgtName;
		internal string TyName;
		internal string VTgt;
		internal string VTy;
		internal string Ref;
		internal double FirstT;
		internal double LastT;
		internal int Seen;
	}

	private static readonly List<Row> _union = new List<Row>(64);
	private static readonly Dictionary<string, int> _index = new Dictionary<string, int>();

	internal static void Reset()
	{
		_union.Clear();
		_index.Clear();
		Calls = 0; Errors = 0; Units = 0; EntriesSeen = 0; EntriesScanned = 0; EntryReads = 0; BudgetCapped = 0;
		EntriesDropped = 0; OwnerNull = 0; OwnerSelf = 0; OwnerOther = 0; KeyOwnerNull = 0;
		OwnerErrors = 0; CrossChecked = 0; Mismatch = 0; UnionOverflow = 0;
		_firstError = "";
	}

	/// <summary>Called ONCE per walk by the same code that produced the owner rows.
	///
	/// 1.7.2 rev2 (pre-review Q6): the FIRST cut stopped iterating at 64 entries, so 8,908 of 24,447
	/// entries in one battle were never even LOOKED at -- the union could not be called complete. Now every
	/// entry contributes its cheap key integers, and the two expensive owner GETTERS are the only thing
	/// budgeted (`getters`), with ATK-target entries always served. Completeness is therefore an IDENTITY
	/// anyone can check in the export: entriesTotal == entryReads + entriesDropped.</summary>
	internal static void NoteScan(int total, int getters, int skipped)
	{
		EntriesScanned += total;
		OwnerGetters += getters;
		if (skipped > 0) EntriesDropped += skipped;
	}

	/// <summary>Owner getters actually called (the budgeted half of the scan).</summary>
	internal static int OwnerGetters;

	/// <summary>An owner property threw. Counted separately from other errors because the precedent
	/// (GiveApplierProbe 1.5.4: the owner getter threw for EVERY entry) is exactly this failure.</summary>
	internal static void NoteOwnerError()
	{
		OwnerErrors++;
	}

	private static void Note(string where, Exception ex)
	{
		Errors++;
		if (_firstError.Length == 0) _firstError = where + ": " + ex.GetType().Name + " " + ex.Message;
	}

	internal static string Diag()
	{
		return "读取=" + Calls + " 单位=" + Units + " 条目=" + EntriesSeen
			+ " 所有者:他方=" + OwnerOther + " 己方=" + OwnerSelf + " 空=" + OwnerNull
			+ " 键侧空=" + KeyOwnerNull + " 交叉=" + CrossChecked + " 不一致=" + Mismatch
			+ " 所有者异常=" + OwnerErrors
			+ " 扫描=" + EntriesScanned + " 取值器=" + OwnerGetters + " 跳过=" + EntriesDropped + " 预算截断=" + BudgetCapped
			+ " 并集=" + _union.Count + " 并集溢出=" + UnionOverflow
			+ " 异常=" + Errors
			+ (_firstError.Length > 0 ? "(首条 " + _firstError + ")" : "");
	}

	/// <summary>
	/// Fold one unit's owner rows (produced by the single walk in BuffParamTableDump) into the union.
	/// Row layout from that walk:
	/// [tgtName, tyName, value, vTgtName, vTyName, vParam, vRef, valueOwner, keyOwner].
	/// `selfName` is the DISPLAY name of the unit that owns the dictionary, i.e. the same name space
	/// the owner strings come from, so "the owner is the holder" is a string comparison and not a guess.
	/// </summary>
	internal static void Observe(string unitKey, string selfName, List<string[]> ownerRows)
	{
		try
		{
			if (Plugin.CfgParamOwners != null && !Plugin.CfgParamOwners.Value) return;
			if (ownerRows == null) return;
			Calls++;
			Units++;
			double now = 0.0;
			try { var s = Aggregator.Session; if (s != null) now = s.ActiveSeconds; } catch { }
			for (int i = 0; i < ownerRows.Count; i++)
			{
				try
				{
					if (EntryReads >= MaxEntryReadsPerBattle) { BudgetCapped++; break; }
					EntryReads++;
					string[] r = ownerRows[i];
					if (r == null) continue;
					int value = ParseInt(r, 2);
					string owner = Clean(r, 7);
					string keyOwner = Clean(r, 8);
					if (owner == null) OwnerNull++;
					else if (selfName != null && selfName.Length > 0 && owner == selfName) OwnerSelf++;
					else OwnerOther++;
					if (keyOwner == null) KeyOwnerNull++;
					if (owner != null && keyOwner != null)
					{
						CrossChecked++;
						if (owner != keyOwner) Mismatch++;
					}
					EntriesSeen++;
					Add(unitKey, owner, keyOwner, value, Str(r, 0), Str(r, 1), Str(r, 3), Str(r, 4), Str(r, 6), now);
				}
				catch (Exception ex) { Note("row", ex); }
			}
		}
		catch (Exception ex) { Note("Observe", ex); }
	}

	private static string Str(string[] r, int i) { return (r != null && i < r.Length && r[i] != null) ? r[i] : ""; }

	/// <summary>An owner string, or null. "?" is `Aggregator.NameOf`'s sentinel for "unreadable".</summary>
	private static string Clean(string[] r, int i)
	{
		string s = Str(r, i);
		if (s.Length == 0 || s == "?") return null;
		return s;
	}

	private static int ParseInt(string[] r, int i)
	{
		int v;
		if (int.TryParse(Str(r, i), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
		return 0;
	}

	private static void Add(string unitKey, string owner, string keyOwner, int value,
		string tgtName, string tyName, string vTgt, string vTy, string refText, double t)
	{
		// The names are the JOIN KEY with the sibling `params` section (which publishes the same CJK
		// target strings), so no numeric reverse-mapping layer is introduced here.
		string k = unitKey + "\u0001" + (owner ?? "") + "\u0001" + tgtName + "\u0001" + tyName
			+ "\u0001" + value + "\u0001" + refText + "\u0001" + (keyOwner ?? "") + "\u0001" + vTgt + "\u0001" + vTy;
		int idx;
		if (_index.TryGetValue(k, out idx))
		{
			Row r = _union[idx];
			r.LastT = t;
			r.Seen++;
			_union[idx] = r;
			return;
		}
		if (_union.Count >= MaxUnionRows) { UnionOverflow++; return; }
		var row = new Row();
		row.Unit = unitKey;
		row.Owner = owner;
		row.KeyOwner = keyOwner;
		row.Value = value;
		row.TgtName = tgtName;
		row.TyName = tyName;
		row.VTgt = vTgt;
		row.VTy = vTy;
		row.Ref = refText;
		row.FirstT = t;
		row.LastT = t;
		row.Seen = 1;
		_union.Add(row);
		_index[k] = _union.Count - 1;
	}

	/// <summary>Owners that ever appeared on a value-side entry, for the summary line.</summary>
	internal static int DistinctOwners()
	{
		var set = new HashSet<string>();
		for (int i = 0; i < _union.Count; i++) if (_union[i].Owner != null) set.Add(_union[i].Owner);
		return set.Count;
	}

	internal static void AppendJson(StringBuilder sb)
	{
		sb.Append("{\"reads\":").Append(Calls)
		  .Append(",\"errors\":").Append(Errors)
		  .Append(",\"firstError\": \"").Append(JsonText.Str(_firstError)).Append('"')
		  .Append(",\"units\":").Append(Units)
		  .Append(",\"entriesSeen\":").Append(EntriesSeen)
		  .Append(",\"entriesTotal\":").Append(EntriesScanned)
		  .Append(",\"entryReads\":").Append(EntryReads)
		  .Append(",\"ownerGetters\":").Append(OwnerGetters)
		  .Append(",\"entriesDropped\":").Append(EntriesDropped)
		  .Append(",\"budgetCapped\":").Append(BudgetCapped)
		  .Append(",\"ownerNull\":").Append(OwnerNull)
		  .Append(",\"ownerSelf\":").Append(OwnerSelf)
		  .Append(",\"ownerOther\":").Append(OwnerOther)
		  .Append(",\"keyOwnerNull\":").Append(KeyOwnerNull)
		  .Append(",\"ownerErrors\":").Append(OwnerErrors)
		  .Append(",\"crossChecked\":").Append(CrossChecked)
		  .Append(",\"mismatch\":").Append(Mismatch)
		  .Append(",\"distinctOwners\":").Append(DistinctOwners())
		  .Append(",\"maxEntriesPerRead\":").Append(MaxEntriesPerRead)
		  .Append(",\"maxUnionRows\":").Append(MaxUnionRows)
		  .Append(",\"maxEntryReadsPerBattle\":").Append(MaxEntryReadsPerBattle)
		  .Append(",\"unionOverflow\":").Append(UnionOverflow)
		  .Append(",\"rows\":[");
		for (int i = 0; i < _union.Count; i++)
		{
			if (i > 0) sb.Append(',');
			Row r = _union[i];
			sb.Append("{\"unit\":\"").Append(JsonText.Str(r.Unit)).Append('"')
			  .Append(",\"owner\":").Append(r.Owner == null ? "null" : "\"" + JsonText.Str(r.Owner) + "\"")
			  .Append(",\"keyOwner\":").Append(r.KeyOwner == null ? "null" : "\"" + JsonText.Str(r.KeyOwner) + "\"")
			  .Append(",\"tgt\":\"").Append(JsonText.Str(r.TgtName)).Append('"')
			  .Append(",\"ty\":\"").Append(JsonText.Str(r.TyName)).Append('"')
			  .Append(",\"val\":").Append(r.Value)
			  .Append(",\"vTgt\":\"").Append(JsonText.Str(r.VTgt)).Append('"')
			  .Append(",\"vTy\":\"").Append(JsonText.Str(r.VTy)).Append('"')
			  .Append(",\"ref\":\"").Append(JsonText.Str(r.Ref)).Append('"')
			  .Append(",\"firstT\":").Append(r.FirstT.ToString("F2", CultureInfo.InvariantCulture))
			  .Append(",\"lastT\":").Append(r.LastT.ToString("F2", CultureInfo.InvariantCulture))
			  .Append(",\"seen\":").Append(r.Seen)
			  .Append('}');
		}
		sb.Append("]}");
	}
}
