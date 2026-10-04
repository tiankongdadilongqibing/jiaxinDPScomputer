using System;
using System.Collections.Generic;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Rog.MasterData;
using UnityEngine;

namespace DpsMeter;

/// <summary>
/// FEATURE (1.2.2): give the 刻印 slot entries their OFFICIAL name.
///
/// THE PROBLEM
/// Every 刻印 row in the roster comes out with an EMPTY name (measured 2026-10-03: 39/39 across all
/// allies in battle 401003). The ability's own m_data.Name is empty for these; the official name lives
/// in another table. Read from memory via the same route as MasterData/MasterDataDump.cs.
///
/// WHY THE ID ALONE IS NOT ENOUGH (this is the whole reason this file is careful)
/// The 刻印 slot mixes TWO id spaces that OVERLAP:
///   * ids 1..14  = the 14 base engravings (EngravingMasterTable / EngravingAbilityMasterTable)
///   * ids 1..28  = the 14 刻印变异 rows (EngravingMutateMasterTable), whose ids 1..14 collide
/// Labelling by id alone therefore produces a confident wrong answer. Proven by the exported talent
/// params in battle 401003, e.g. ability id 14: the official base engraving 14【虚突】is 貫通率+4%
/// 会心率+3% (talentIds 78,86) while the row actually carries talentId 26 = 攻撃速度+30% for 150
/// units = 5 s -- which is exactly mutate 14「オーバースキル発動時、5秒間攻撃速度が30%増加」.
/// (This also explains the old note "刻印 #14 攻速%+(30/150)": that observation was mutate 14, not
/// base engraving 14, and so it does NOT contradict the official table.)
///
/// THE RULE (verified, not guessed)
/// Measured on the shipped 401003 export (version 1.2.1, 39 刻印 rows, all unnamed):
/// **15 distinct ids / 18 distinct (id, talent-signature) keys, and the rule resolves 18/18 offline.**
/// The first live run (version 1.2.2, battle 401003 04:37) then labelled **37/37 rows with
/// `nameFrom:"master"`, 0 ambiguous, 0 no-candidate, 0 errors.**
/// The same battle proves the collision is real and that the signature is doing the work: id=1/level=1
/// appears BOTH as 【破壊】の刻印 (talent 6) and as the mutate 魔法与ダメージ+10% (talent 1005), and both
/// are labelled correctly because their signatures differ.
///   1. keep only master rows whose id equals the ability id;
///   2. keep only those whose talent-id SIGNATURE equals the live one;
///   3. if every survivor carries the same display name, use it. This is what resolves the base
///      engravings: their 5 level rows share one signature and one name, so the NAME is unambiguous
///      even though the level is not.
///   4. otherwise (several different names survive) do NOT label -- count it instead. A guessed
///      official name would be indistinguishable from a resolved one, which is the failure mode
///      ARCHITECTURE.md section 4 rule 15 exists to prevent.
///
/// PARAMETERS are deliberately NOT part of the key: the master side stores them as param[[10]] while the
/// live side reports the already-scaled p=[30,0,0], so they are not comparable. (They were used only as
/// human-readable corroboration when establishing which id space a row belongs to -- the implemented
/// rule never consults them.)
///
/// Read-only; no Harmony patches. Falls back to "no name" (never to a guess) if the tables are absent.
/// </summary>
public static class MasterDataNames
{
	private static bool _built;

	private static int _resolved;

	private static int _ambiguous;

	private static int _noCandidate;

	private static int _errors;

	/// <summary>Tables that had more than one loaded instance while building the maps.</summary>
	private static int _duplicateInstances;

	/// <summary>Instances that could not be read while picking the best one.</summary>
	private static int _instErrors;

	private static readonly List<string> Notes = new List<string>();

	private const int MaxNotes = 8;

	private static Dictionary<int, List<Cand>> ById = new Dictionary<int, List<Cand>>();

	private sealed class Cand
	{
		public string Name;

		public string Sig;
	}

	/// <summary>Number of 刻印 rows that got an official name from the master tables.</summary>
	public static int Resolved => _resolved;

	/// <summary>Rows that had matches but more than one distinct name (left unlabelled on purpose).</summary>
	public static int Ambiguous => _ambiguous;

	/// <summary>Rows whose (id, signature) matched no master row at all.</summary>
	public static int NoCandidate => _noCandidate;

	public static string Diag()
	{
		return "刻印命名 已解析=" + _resolved + " 歧义=" + _ambiguous + " 无候选=" + _noCandidate
			+ " 异常=" + _errors + " 多实例表=" + _duplicateInstances + " 实例异常=" + _instErrors
			+ (_built ? "" : " (表未就绪)")
			+ (Notes.Count > 0 ? " 备注[" + string.Join(" | ", Notes.ToArray()) + "]" : "");
	}

	/// <summary>
	/// Clears the PER-BATTLE counters. The id -> candidate map is process-global and is deliberately kept,
	/// so this is cheap. Without it these counters accumulate across battles while being printed on the
	/// per-battle [ROSTER] line, which would be read as that battle's numbers.
	/// </summary>
	internal static void ResetSession()
	{
		_resolved = 0;
		_ambiguous = 0;
		_noCandidate = 0;
		_errors = 0;
		_duplicateInstances = 0;
		_instErrors = 0;
		Notes.Clear();
	}

	private static void Note(string s)
	{
		if (Notes.Count < MaxNotes) Notes.Add(s);
	}

	/// <summary>
	/// True when the official name was resolved. `label` is then safe to show; when false the caller
	/// must leave the name empty rather than substitute anything.
	/// </summary>
	public static bool TryLabel(int abilityId, int level, List<TalentRef> talents, out string label)
	{
		label = null;
		try
		{
			if (!_built && !Build()) return false;
			List<Cand> list;
			if (!ById.TryGetValue(abilityId, out list) || list == null || list.Count == 0)
			{
				_noCandidate++;
				return false;
			}

			string sig = Signature(talents);
			string name = null;
			int distinct = 0;
			for (int i = 0; i < list.Count; i++)
			{
				Cand c = list[i];
				if (c.Sig != sig) continue;
				if (name == null)
				{
					name = c.Name;
					distinct = 1;
				}
				else if (name != c.Name)
				{
					distinct = 2;
					break;
				}
			}

			if (distinct == 1 && !string.IsNullOrEmpty(name))
			{
				label = name;
				_resolved++;
				return true;
			}
			if (distinct == 0) _noCandidate++;
			else _ambiguous++;
			return false;
		}
		catch
		{
			_errors++;
			return false;
		}
	}

	/// <summary>Sorted, comma-joined talent type ids -- the discriminator between the two id spaces.</summary>
	private static string Signature(List<TalentRef> talents)
	{
		if (talents == null || talents.Count == 0) return "";
		List<int> ids = new List<int>(talents.Count);
		for (int i = 0; i < talents.Count; i++)
		{
			try { ids.Add(talents[i].Type); } catch { }
		}
		ids.Sort();
		StringBuilder sb = new StringBuilder();
		for (int i = 0; i < ids.Count; i++)
		{
			if (i > 0) sb.Append(',');
			sb.Append(ids[i]);
		}
		return sb.ToString();
	}

	/// <summary>
	/// Builds id -> candidates from the live master tables, into a LOCAL map that is committed only on
	/// success. Committing early would leave a half-built map behind after a failure, and because a
	/// failure does not set _built the next attempt would append duplicates on top of it.
	/// Not cached on failure: a battle that runs before the tables exist must be able to succeed later.
	/// </summary>
	private static bool Build()
	{
		try
		{
			EngravingMasterTable eng = FindBest<EngravingMasterTable, int, EngravingMasterData>(
				(EngravingMasterTable t) => t.m_cache, "刻印");
			EngravingAbilityMasterTable engAb = FindBest<EngravingAbilityMasterTable, int, EngravingAbilityMasterData>(
				(EngravingAbilityMasterTable t) => t.m_cache, "刻印效果");
			EngravingMutateMasterTable mut = FindBest<EngravingMutateMasterTable, int, EngravingMutateMasterData>(
				(EngravingMutateMasterTable t) => t.m_cache, "刻印变异");
			if (eng == null || engAb == null || mut == null) return false;

			Dictionary<int, List<Cand>> map = new Dictionary<int, List<Cand>>();

			Dictionary<int, string> engName = new Dictionary<int, string>();
			foreach (EngravingMasterData e in Rows<EngravingMasterTable, int, EngravingMasterData>(eng, (EngravingMasterTable t) => t.m_cache))
			{
				if (e == null) continue;
				try { engName[e.id] = e.name ?? ""; } catch { }
			}

			foreach (EngravingAbilityMasterData ea in Rows<EngravingAbilityMasterTable, int, EngravingAbilityMasterData>(engAb, (EngravingAbilityMasterTable t) => t.m_cache))
			{
				if (ea == null) continue;
				string nm = null;
				try { engName.TryGetValue(ea.id, out nm); } catch { nm = null; }
				if (string.IsNullOrEmpty(nm)) continue;
				Il2CppSystem.Collections.Generic.List<EngravingAbilityMasterData.AbilityData> entries;
				try { entries = ea.abilityDataList; } catch { continue; }
				if (entries == null) continue;
				int n;
				try { n = entries.Count; } catch { continue; }
				for (int i = 0; i < n; i++)
				{
					try
					{
						EngravingAbilityMasterData.AbilityData a = entries[i];
						if (a == null) continue;
						Add(map, ea.id, nm, TalentSig(a.talentList));
					}
					catch { _errors++; }
				}
			}

			foreach (EngravingMutateMasterData m in Rows<EngravingMutateMasterTable, int, EngravingMutateMasterData>(mut, (EngravingMutateMasterTable t) => t.m_cache))
			{
				if (m == null) continue;
				try
				{
					Add(map, m.id, m.text ?? "", TalentSig(m.talentList));
				}
				catch { _errors++; }
			}

			ById = map;      // commit only now
			_built = true;
			return true;
		}
		catch
		{
			_errors++;
			return false;
		}
	}

	private static void Add(Dictionary<int, List<Cand>> map, int id, string name, string sig)
	{
		if (string.IsNullOrEmpty(name)) return;
		List<Cand> list;
		if (!map.TryGetValue(id, out list))
		{
			list = new List<Cand>(8);
			map[id] = list;
		}
		Cand c = new Cand();
		c.Name = name;
		c.Sig = sig;
		list.Add(c);
	}

	private static string TalentSig(Il2CppSystem.Collections.Generic.List<AbilityTalent> list)
	{
		if (list == null) return "";
		List<int> ids = new List<int>();
		int n;
		try { n = list.Count; } catch { return ""; }
		for (int i = 0; i < n; i++)
		{
			try
			{
				AbilityTalent t = list[i];
				if (t == null) continue;
				ids.Add(GameRef.Dec(t.talentId));
			}
			catch { _errors++; }
		}
		ids.Sort();
		StringBuilder sb = new StringBuilder();
		for (int i = 0; i < ids.Count; i++)
		{
			if (i > 0) sb.Append(',');
			sb.Append(ids[i]);
		}
		return sb.ToString();
	}

	/// <summary>
	/// The same non-generic, AOT-safe lookup MasterDataDump uses (see ARCHITECTURE.md section 4 rule 18):
	/// the generic Resources.FindObjectsOfTypeAll&lt;T&gt;() would need an AOT instantiation that does not
	/// exist for a game type.
	///
	/// It scans EVERY loaded instance and keeps the one with the most rows, exactly like
	/// MasterDataDump.Table does. Taking found[0] would be the same blind pick that made 1.2.0 publish a
	/// 28-row 能力 table when the real one had 210 -- and here the only symptom would be silently fewer
	/// labelled rows, which is precisely the failure mode rule 15 forbids.
	/// </summary>
	private static TTable FindBest<TTable, TKey, TRow>(
		Func<TTable, Il2CppSystem.Collections.Generic.Dictionary<TKey, TRow>> getRows, string label)
		where TTable : MasterTableBase
		where TRow : Il2CppObjectBase
	{
		try
		{
			Il2CppReferenceArray<UnityEngine.Object> found = Resources.FindObjectsOfTypeAll(Il2CppType.Of<TTable>());
			if (found == null || found.Length == 0) return null;
			TTable best = null;
			int bestCount = -1;
			for (int i = 0; i < found.Length; i++)
			{
				try
				{
					TTable cand = found[i].TryCast<TTable>();
					if (cand == null) continue;
					Il2CppSystem.Collections.Generic.Dictionary<TKey, TRow> d = getRows(cand);
					int c = d == null ? -1 : d.Count;
					if (c > bestCount)
					{
						bestCount = c;
						best = cand;
					}
				}
				catch { _instErrors++; }
			}
			if (found.Length > 1)
			{
				_duplicateInstances++;
				Note(label + " 实例=" + found.Length + " 取行数最多(=" + bestCount + ")");
			}
			return best;
		}
		catch { _errors++; return null; }
	}

	private static List<TRow> Rows<TTable, TKey, TRow>(TTable table,
		Func<TTable, Il2CppSystem.Collections.Generic.Dictionary<TKey, TRow>> getRows)
		where TTable : MasterTableBase
		where TRow : Il2CppObjectBase
	{
		List<TRow> outList = new List<TRow>();
		try
		{
			Il2CppSystem.Collections.Generic.Dictionary<TKey, TRow> d = getRows(table);
			if (d == null) return outList;
			int n = d.Count;
			Il2CppReferenceArray<TRow> arr = new Il2CppReferenceArray<TRow>(n);
			d.Values.CopyTo(arr, 0);
			for (int i = 0; i < arr.Length; i++)
			{
				if (arr[i] != null) outList.Add(arr[i]);
			}
		}
		catch { _errors++; }
		return outList;
	}
}
