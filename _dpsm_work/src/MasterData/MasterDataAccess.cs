using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Rog.MasterData;
using UnityEngine;

namespace DpsMeter;

/// <summary>What the instance scan found. The COUNTERS stay with the caller: the two routes count different
/// things (the label route tracks instance errors and duplicate instances, the dump tracks missing tables and
/// per-table statistics), so the scan reports and the caller decides what it means.</summary>
internal struct BestInstance
{
	/// <summary>How many loaded instances the scan saw (0 when none, or when the scan itself threw).</summary>
	public int Found;
	/// <summary>Row count of the chosen instance, -1 when none was usable.</summary>
	public int BestCount;
	/// <summary>Per-instance failures, counted rather than thrown: one bad instance must not hide a table.</summary>
	public int CastErrors;
	/// <summary>The outer scan threw (the caller counts that differently from a per-instance failure).</summary>
	public bool Failed;
}

/// <summary>
/// RF6b (plan section 11): the table lookup the two master-data routes had DUPLICATED. This type owns exactly
/// the two algorithms they shared and nothing else:
///
///   * FindBest -- locate the loaded table instance to use. It goes through the NON-GENERIC
///     Resources.FindObjectsOfTypeAll(Il2CppType.Of&lt;TTable&gt;()). The generic overload compiles and would fail
///     at runtime, because IL2CPP is AOT and a game table type has no instantiation of it in the image (the
///     comment in MasterDataDump records how that was established). It then scans EVERY instance and keeps the
///     one with the most rows: the ability table was once loaded nine times and rarity twice, and taking the
///     first published a 28-row table as the whole one in 1.2.0. Every instance is wrapped individually, so one
///     failure cannot erase a table.
///   * Rows -- materialise a table's values into a list, the same way in both routes.
///
/// What it deliberately does NOT own: the counters, the notes, the decryption accessors, the per-field layout
/// and the retry policy, all of which differ per route. Moving those here would have changed behaviour, which
/// is the one thing this extraction must not do.
/// </summary>
internal static class MasterDataAccess
{
	public static TTable FindBest<TTable, TKey, TRow>(
		Func<TTable, Il2CppSystem.Collections.Generic.Dictionary<TKey, TRow>> getRows, out BestInstance info)
		where TTable : MasterTableBase
		where TRow : Il2CppObjectBase
	{
		info = new BestInstance { BestCount = -1 };
		try
		{
			Il2CppReferenceArray<UnityEngine.Object> found = Resources.FindObjectsOfTypeAll(Il2CppType.Of<TTable>());
			if (found == null || found.Length == 0) return null;
			info.Found = found.Length;
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
				catch { info.CastErrors++; }
			}
			info.BestCount = bestCount;
			return best;
		}
		catch { info.Failed = true; return null; }
	}

	public static List<TRow> Rows<TTable, TKey, TRow>(TTable table,
		Func<TTable, Il2CppSystem.Collections.Generic.Dictionary<TKey, TRow>> getRows, out bool failed)
		where TTable : MasterTableBase
		where TRow : Il2CppObjectBase
	{
		failed = false;
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
		catch { failed = true; }
		return outList;
	}
}
