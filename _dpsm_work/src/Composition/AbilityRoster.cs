using System;
using System.Collections.Generic;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace DpsMeter;

/// <summary>One talent (素质/词条) inside an ability, with its decoded structured trigger condition and a
/// live counter handle. <see cref="Last"/> is the counter value at the previous observation, so the
/// per-hit diff in <see cref="TalentRuntime"/> never has to re-read the raw field twice.</summary>
public sealed class TalentRef
{
	public int Index;
	public int Type;          // TalentDefine.Type
	public int P0, P1, P2;    // GetParam(0..2)
	public int Timing;        // TalentDefine.TriggerTiming
	public string Cond = "";  // structured condition, e.g. "IsFrozen" / "IsIgnoreAttack(1)"
	public TalentData Live;   // null when the counter was unreadable
	public int Last;          // last observed _ActivateCount
	public long Delta;        // activations observed so far this battle
}

/// <summary>One ability of a unit, together with WHERE it came from (eAbilitySlotType).</summary>
public sealed class RosterAbility
{
	public int Slot = -1;     // eAbilitySlotType, -1 = unknown
	public int SlotId = -1;
	public int SlotSubId;
	public int Id;
	public int Level;
	public string Name = "";
	public int Origin;        // 0 unknown, 1 = route A (game's own slot list), 2 = route C (statistics join)
	/// <summary>True when Name was filled in from the master tables because the ability had none of its
	/// own (currently only 刻印). Kept so the export can say where a name came from.</summary>
	public bool NameFromMaster;
	public readonly List<TalentRef> Talents = new List<TalentRef>();
}

/// <summary>Battle-end row of the per-unit talent table (see TalentRuntime.FinalizeTable).</summary>
public sealed class TalentUsage
{
	public int Slot = -1;
	public int AbilityId;
	public string Ability = "";
	public int Index;
	public int Type;
	public int P0, P1, P2;
	public long Delta;   // activations WE observed during the battle
	public int Prop;     // TalentData.TotalActivateCount at battle end (audit)
	public int Agg;      // TalentData.m_statistics.ActivateCount at battle end (audit)
}

/// <summary>
/// The unit's ability roster with provenance -- "this unit has what, and where did it come from".
///
/// WHY THIS EXISTS
/// Until now the composition module identified an ability by its position in bo.m_ability plus its text.
/// That is exactly what produced the historical "two abilities with the same id share one cache entry"
/// corruption (SESSION-STATE 10): the same character carries several abilities whose ids collide, and the
/// list index is not stable across units. The game itself knows the answer -- every ability comes from a
/// slot (eAbilitySlotType: Job / Awaking / Potential / UniqueWeapon / Engraving / Artifact / Skin / Skill
/// ...) -- so the roster keys abilities by (slot, slotId, slotSubId, level) instead.
///
/// HOW THE SLOT IS RECOVERED (three routes, tried in order, degrade instead of failing)
///   1. bo.Data.GetAbilityDetailDataList() -- the game's own (AbilityData, eAbilitySlotType, index) list.
///      Its return type is IEnumerable&lt;Il2CppSystem.ValueTuple&lt;...&gt;&gt;; Il2CPP enumerables cannot be
///      consumed by foreach, so the ONLY route is TryCast to the concrete List. If that cast fails this
///      route yields nothing (which is a measured outcome, not an exception -- see Diagnostics/SlotProbe.cs).
///   2. match by ability-object POINTER (exact, no id collisions) against route 1's result;
///   3. otherwise join CharacterStatistics.m_abilityStatistics on (AbilityData.Id, AbilityData.Level).
/// Anything still unmatched keeps Slot = -1 and is reported as 未分类 rather than guessed.
///
/// COST / SAFETY
///   * Read-only: no Harmony patch, no game mutation.
///   * Built at most once per unit per battle and cached on ActorStats -- the roster outlives the live
///     BattleObject, which matters because Aggregator nulls ActorStats.Source right after the export.
///   * Every read is individually guarded and failures are COUNTED and reported, because an outer
///     try/catch around a whole collection is what silently blanked the status map in 1.0.57.
/// </summary>
internal static class AbilityRoster
{
	private const int MaxAbilities = 64;
	private const int MaxTalents = 32;
	private const int MaxConditions = 6;
	private const int MaxRouteAEntries = 128;
	/// <summary>eAbilitySlotType.Engraving -- the slot whose rows come out unnamed and are therefore
	/// filled from the master tables (see MasterData/MasterDataNames.cs).</summary>
	private const int EngravingSlot = 10;

	/// <summary>Route-1 outcome for the battle. MEASURED values:
	/// 0 = not attempted, 1 = the whole-list TryCast worked, 2 = the non-generic enumerator worked,
	/// -1 = the game list could not be read at all, -2 = it threw.</summary>
	internal static int RouteAState;
	/// <summary>Items the enumerator yielded that would not read as a ValueTuple (should stay 0).</summary>
	internal static int RouteACastFails;
	/// <summary>Tuples whose slot read was not a valid eAbilitySlotType (broken marshalling / bad layout).</summary>
	internal static int RouteASlotInvalid;
	/// <summary>Tuples where the assumed struct layout did not validate (Item1 not at +0, index implausible).</summary>
	internal static int MismatchedLayout;
	/// <summary>Tuples where the two 4-byte words had to be swapped (declared layout did not hold).</summary>
	internal static int LayoutSwapped;
	/// <summary>Tuples where BOTH words were plausible slot values, i.e. the declared layout had to be
	/// chosen over the alternative.
	///
	/// MEASURED 2026-10-03 (1.4.0): this fires for **100% of route-A tuples** -- across 48 exports it
	/// equals `ptrHits + idHits` exactly, the total number of ability rows, in every battle. It is
	/// therefore NOT a defect signal: an in-slot INDEX in 0..11 is indistinguishable from an
	/// eAbilitySlotType, so a small index always looks like a valid slot. The counter effectively
	/// measures "how many abilities were read", and the declared layout (enum at +8, index at +12) is
	/// independently confirmed offline (3,744 rows, 166 distinct identities, 752/752 identity-groups
	/// mapping to exactly one slot; the slot histogram is dominated by 刻印, which the alternative
	/// reading would make an implausibly common index). `layoutMismatch` / `layoutSwapped` /
	/// `slotInvalid` are all 0, so no other layout ever won.
	/// Kept as a counter (it still detects a layout regression) but `LayoutSample` carries the raw
	/// w8/w12/chosen triples so the claim is checkable from the export rather than from this comment.</summary>
	internal static int AmbiguousLayout;

	/// <summary>
	/// 1.4.0: raw evidence for the layout choice -- up to 8 `w8/w12` pairs plus the chosen slot, and a
	/// histogram of the chosen slots. This exists because `layoutAmbiguous` alone cannot distinguish
	/// "our layout is wrong" from "a small index is indistinguishable from a slot", and the difference
	/// decides whether the slot labels in every export can be trusted.
	/// </summary>
	internal static string LayoutSampleText() { return _layoutSample.ToString(); }
	private static readonly StringBuilder _layoutSample = new StringBuilder(160);
	private static readonly Dictionary<int, int> _layoutSlots = new Dictionary<int, int>();

	/// <summary>
	/// 1.5.0 (B2): the raw `w8/w12` pairs as INTEGERS, so the export can carry the layout evidence as
	/// numbers. The string form only keeps the first 8 pairs and mixes them into one comma-joined line;
	/// a script that wants to check "which word looked like a slot" had to split it.
	/// </summary>
	private static readonly System.Collections.Generic.List<int> _layoutPairs
		= new System.Collections.Generic.List<int>(16);

	private const int MaxLayoutPairs = 8;

	/// <summary>Structured twin of <see cref="LayoutSampleText"/> + <see cref="LayoutSlotHistogram"/>.
	/// The histogram is emitted IN FULL (not just the top 8 the string form prints) plus its total, so a
	/// truncated string can never be mistaken for a complete distribution.</summary>
	internal static void AppendLayoutJson(System.Text.StringBuilder sb)
	{
		try
		{
			sb.Append("{\"pairs\":[");
			for (int i = 0; i + 1 < _layoutPairs.Count; i += 2)
			{
				if (i > 0) sb.Append(',');
				sb.Append("{\"w8\":").Append(_layoutPairs[i])
				  .Append(",\"w12\":").Append(_layoutPairs[i + 1]).Append('}');
			}
			sb.Append("],\"slots\":[");
			var keys = new List<KeyValuePair<int, int>>(_layoutSlots);
			keys.Sort(delegate (KeyValuePair<int, int> a, KeyValuePair<int, int> b) { return b.Value.CompareTo(a.Value); });
			for (int i = 0; i < keys.Count; i++)
			{
				if (i > 0) sb.Append(',');
				sb.Append("{\"slot\":").Append(keys[i].Key)
				  .Append(",\"name\":\"").Append(JsonText.Str(SlotLabel(keys[i].Key)))
				  .Append("\",\"n\":").Append(keys[i].Value).Append('}');
			}
			sb.Append("]}");
		}
		catch { sb.Append("{}"); }
	}

	/// <summary>
	/// 1.5.0 (B2): the structured twin of <see cref="StatsSample"/> -- the same `(slot:slotId/slotSubId)`
	/// triples as numbers, plus how many entries the sampled list actually had.
	///
	/// WHY IT MATTERS: this list is the ONLY evidence in the export that would prove a route-C join
	/// (statistics slot-local index vs `AbilityData.Id`) mixes two id spaces -- and it was readable only as
	/// a six-entry string. `StatsRowTotal` is carried so the 6-entry sample cannot be mistaken for the
	/// whole list.
	/// </summary>
	internal static readonly List<int[]> StatsRows = new List<int[]>(6);

	/// <summary>Length of the statistics list the current <see cref="StatsSample"/> was taken from.</summary>
	internal static int StatsRowTotal;

	/// <summary>Structured twin of <see cref="StatsSample"/>.</summary>
	internal static void AppendStatsJson(System.Text.StringBuilder sb)
	{
		try
		{
			sb.Append("{\"entries\":").Append(StatsEntries)
			  .Append(",\"sampledFrom\":").Append(StatsRowTotal)
			  .Append(",\"rows\":[");
			for (int i = 0; i < StatsRows.Count; i++)
			{
				if (i > 0) sb.Append(',');
				int[] r = StatsRows[i];
				sb.Append("{\"slot\":").Append(r[0])
				  .Append(",\"slotName\":\"").Append(JsonText.Str(SlotLabel(r[0])))
				  .Append("\",\"slotId\":").Append(r[1])
				  .Append(",\"slotSubId\":").Append(r[2]).Append('}');
			}
			sb.Append("]}");
		}
		catch { sb.Append("{}"); }
	}

	private static void NoteLayout(int w8, int w12)
	{
		try
		{
			int c;
			_layoutSlots.TryGetValue(w8, out c);
			_layoutSlots[w8] = c + 1;
			if (_layoutSample.Length < 120)
			{
				if (_layoutSample.Length > 0) _layoutSample.Append(',');
				_layoutSample.Append(w8).Append('/').Append(w12);
			}
			else if (_layoutSample.Length < 124)
			{
				_layoutSample.Append("…");
			}
			// 1.5.0 (B2): the same pairs as integers, capped at the same 8 the string form shows, so the
			// structured and textual forms describe the same sample rather than two different ones.
			if (_layoutPairs.Count < MaxLayoutPairs * 2)
			{
				_layoutPairs.Add(w8);
				_layoutPairs.Add(w12);
			}
		}
		catch { }
	}

	/// <summary>The chosen-slot histogram, sorted by count -- "which slot did the declared layout
	/// produce", which is what makes an implausible reading visible at a glance.</summary>
	internal static string LayoutSlotHistogram()
	{
		try
		{
			var sb = new StringBuilder(120);
			var keys = new List<KeyValuePair<int, int>>(_layoutSlots);
			keys.Sort(delegate (KeyValuePair<int, int> a, KeyValuePair<int, int> b) { return b.Value.CompareTo(a.Value); });
			for (int i = 0; i < keys.Count && i < 8; i++)
			{
				if (sb.Length > 0) sb.Append(',');
				sb.Append(SlotLabel(keys[i].Key)).Append(':').Append(keys[i].Value);
			}
			return sb.ToString();
		}
		catch { return ""; }
	}
	/// <summary>
	/// Independent check on the layout choice: the SAME ability must get the SAME slot on every unit.
	/// Mirror matches field the same character on both sides, so they are a free repeat measurement -- a
	/// wrong offset choice would scatter the labels and show up here.
	///
	/// The key includes the ability NAME, not just (id, level): AbilityData.Id collides across different
	/// content (MEASURED 2026-10-03: artifact #21 黄金の蜂蜜酒 and engraving #21 are both id 21), so keying on
	/// id alone reported those collisions as if they were layout failures.
	/// </summary>
	internal static int SlotConflicts;
	private static readonly Dictionary<string, int> _slotByIdentity = new Dictionary<string, int>();
	/// <summary>Units whose route A produced at least one usable slot, and units where it produced none.</summary>
	internal static int RouteAUnitsOk;
	internal static int RouteAUnitsFail;
	/// <summary>Entries found in CharacterStatistics.m_abilityStatistics (answers "is it populated?").</summary>
	internal static int StatsEntries;
	/// <summary>First few (slot, slotId, subId) of those entries -- the evidence for joining route C if needed.</summary>
	internal static string StatsSample = "";
	internal static int RouteAHits;
	internal static int RouteAIdHits;
	internal static int Unlabelled;
	internal static int Errors;
	private static string _firstError = "";

	/// <summary>eAbilitySlotType values this build knows. Anything else is treated as "not read successfully"
	/// rather than being printed as if it were meaningful.</summary>
	private static bool ValidSlot(int s)
	{
		return (s >= 0 && s <= 11) || s == 1000;
	}

	private static int RankRouteA(int s)
	{
		switch (s)
		{
			case 1: return 4;    // whole-list TryCast worked
			case 2: return 3;    // non-generic enumerator worked
			case -2: return 2;   // threw
			case -1: return 1;   // read nothing
			default: return 0;   // not attempted
		}
	}

	/// <summary>
	/// Record the best route-A outcome across the whole battle, not the last one.
	/// MEASURED (battle 9999, 2026-10-03): the per-call value used to overwrite a GOOD result with a later
	/// unit's failure, so the self-report said "A不可读" while 151 abilities had in fact been matched.
	/// </summary>
	private static void SetRouteA(int s)
	{
		if (RankRouteA(s) > RankRouteA(RouteAState)) RouteAState = s;
	}

	internal static void ResetSession()
	{
		// Per-battle counters of the 刻印 naming must reset with the session: they are printed on the
		// per-battle [ROSTER] line, so leaving them cumulative would make the numbers unreadable across
		// battles. The id -> candidate map inside is process-global and deliberately survives.
		try { MasterDataNames.ResetSession(); } catch { }
		RouteAState = 0;
		RouteACastFails = 0;
		RouteASlotInvalid = 0;
		MismatchedLayout = 0;
		LayoutSwapped = 0;
		AmbiguousLayout = 0;
		SlotConflicts = 0;
		_slotByIdentity.Clear();
		RouteAUnitsOk = 0;
		RouteAUnitsFail = 0;
		StatsEntries = 0;
		StatsSample = "";
		StatsRows.Clear();
		StatsRowTotal = 0;
		RouteAHits = 0;
		RouteAIdHits = 0;
		Unlabelled = 0;
		Errors = 0;
		_firstError = "";
		_layoutSample.Length = 0;
		_layoutSlots.Clear();
		_layoutPairs.Clear();
	}

	internal static void Note(string where, Exception ex)
	{
		Errors++;
		if (_firstError.Length == 0)
			_firstError = where + ": " + ex.GetType().Name + " " + ex.Message;
	}

	internal static string Diag()
	{
		string route = RouteAState == 1 ? "A可用(整表cast)"
			: RouteAState == 2 ? "A可用(枚举器)"
			: RouteAState == 0 ? "A未尝试"
			: RouteAState == -1 ? "A不可读"
			: "A异常";
		return route + " 单位成功/失败=" + RouteAUnitsOk + "/" + RouteAUnitsFail
			+ " cast失败项=" + RouteACastFails + " 槽位非法=" + RouteASlotInvalid
			+ " 布局不符=" + MismatchedLayout + " 布局交换=" + LayoutSwapped + " 布局歧义=" + AmbiguousLayout
			+ " 同id槽位冲突=" + SlotConflicts
			+ " 统计表条数=" + StatsEntries + (StatsSample.Length > 0 ? " 样本" + StatsSample : "")
			+ " 指针命中=" + RouteAHits + " id命中=" + RouteAIdHits
			// "C连接" removed in 1.4.0 together with the route: it was 0 in 50/50 exports because its
			// join key mixed two id spaces. A counter for deleted code is worse than no counter.
			+ " 未分类=" + Unlabelled
			+ " 读取异常=" + Errors + (_firstError.Length > 0 ? "(首条 " + _firstError + ")" : "")
			+ " | " + MasterDataNames.Diag();
	}

	internal static long Ptr(object o)
	{
		try
		{
			Il2CppObjectBase b = o as Il2CppObjectBase;
			return (b == null) ? 0L : b.Pointer.ToInt64();
		}
		catch { return 0L; }
	}

	private static long Key(int id, int lv)
	{
		return ((long)id << 20) ^ (uint)lv;
	}

	/// <summary>
	/// Read the SLOT out of one (AbilityData, eAbilitySlotType, int) tuple -- and read it as a raw int32,
	/// NOT through the generated Item2 property.
	///
	/// WHY (MEASURED, battle 9999 2026-10-03): every one of 165 abilities came back with
	/// slot = -1247486736 -- a wild pointer value -- while Item1 was perfectly correct. The generated getter is
	///     PointerToValueGeneric&lt;eAbilitySlotType&gt;(addr, isFieldPointer: true, valueTypeWouldBeBoxed: false)
	/// and that helper chooses between "dereference the field as an object pointer" and "box the field" via
	///     il2cpp_class_is_valuetype(Il2CppClassPointerStore&lt;T&gt;.NativeClassPtr)
	/// For this interop enum that class lookup does not resolve, so the 4-byte field was dereferenced AS A
	/// POINTER and its low bits were returned. Item1 is unaffected because a reference field takes the other
	/// branch.
	///
	/// The offset is VERIFIED, not assumed: the layout of ValueTuple&lt;ref, enum, int&gt; is sequential, so Item1
	/// must sit at +0 and the index at +12. If Item1 does not match what the working getter returns, or the
	/// index is not a plausible small number, the read is REFUSED (and counted) instead of producing a
	/// number that would look like a real slot.
	/// </summary>
	private static int ReadSlotOf(Il2CppSystem.Object boxed, Il2CppSystem.ValueTuple<AbilityData, eAbilitySlotType, int> t, out int idx)
	{
		idx = -1;
		try
		{
			if (boxed == null || t == null) return -1;
			unsafe
			{
				IntPtr data = IL2CPP.il2cpp_object_unbox(boxed.Pointer);
				if (data == IntPtr.Zero) return -1;
				byte* b = (byte*)data;
				// Item1 goes through the WORKING path, so pointer equality proves we are looking at the
				// right struct with the right base offset.
				if (*(IntPtr*)b != new IntPtr(Ptr(t.Item1))) { MismatchedLayout++; return -1; }
				int w8 = *(int*)(b + 8);
				int w12 = *(int*)(b + 12);
				// Disambiguate instead of trusting one hard-coded offset: the slot is whichever of the two
				// 4-byte words is a valid eAbilitySlotType and whose partner is a plausible index.
				bool ok8 = ValidSlot(w8) && w12 >= -1 && w12 <= 1000000;
				bool ok12 = ValidSlot(w12) && w8 >= -1 && w8 <= 1000000;
				if (ok8)
				{
					if (ok12) { AmbiguousLayout++; NoteLayout(w8, w12); }   // both plausible: declared layout (enum second) wins
					idx = w12;
					return w8;
				}
				if (ok12)
				{
					LayoutSwapped++;
					idx = w8;
					return w12;
				}
				MismatchedLayout++;
				return -1;
			}
		}
		catch (Exception ex) { Note("ReadSlotOf", ex); return -1; }
	}

	/// <summary>Record one (ability, slot, index) triple read from the game's own list.</summary>
	private static void TakeTuple(
		Il2CppSystem.Object boxed,
		Il2CppSystem.ValueTuple<AbilityData, eAbilitySlotType, int> t,
		System.Collections.Generic.Dictionary<long, int> byPtr,
		System.Collections.Generic.Dictionary<long, int> byIdLv)
	{
		try
		{
			if (t == null) return;
			int idx;
			int slot = ReadSlotOf(boxed, t, out idx);
			// An invalid slot is dropped so the entry is simply absent from byPtr/byIdLv; the ability then
			// falls through to route C instead of being labelled with garbage.
			if (!ValidSlot(slot)) { RouteASlotInvalid++; return; }
			AbilityData d = t.Item1;
			long p = Ptr(d);
			if (p != 0L) byPtr[p] = slot;
			int id = 0, lv = 0;
			try { if (d != null) id = d.Id; } catch { }
			try { if (d != null) lv = d.Level; } catch { }
			byIdLv[Key(id, lv)] = slot;
		}
		catch (Exception ex) { Note("TakeTuple", ex); }
	}

	/// <summary>Build (once) and return the roster of <paramref name="bo"/>, cached on
	/// <paramref name="a"/>. Returns an empty list -- never null -- when nothing could be read.</summary>
	internal static List<RosterAbility> Of(ActorStats a, BattleObject bo)
	{
		if (a == null) return null;
		if (a.Roster != null) return a.Roster;
		var res = new List<RosterAbility>();
		// Assign BEFORE building: a partially built roster must never be rebuilt on every hit.
		a.Roster = res;
		try
		{
			if (GameRef.IsNull(bo)) return res;
			Il2CppSystem.Collections.Generic.List<Ability> list = null;
			try { list = bo.m_ability; } catch (Exception ex) { Note("m_ability", ex); }
			if (list == null) return res;

			// ---- route A: ability -> slot, straight from the game ----
			var byPtr = new Dictionary<long, int>();
			var byIdLv = new Dictionary<long, int>();
			try
			{
				// This is the only GAME METHOD the roster calls, so it is behind its own switch: if it ever
				// misbehaves it can be disabled without a rebuild and routes C still labels the abilities.
				if (Plugin.CfgRosterRouteA == null || Plugin.CfgRosterRouteA.Value)
				{
					CharacterDataBase data = null;
					try { data = bo.Data; } catch (Exception ex) { Note("bo.Data", ex); }
					if (data != null)
					{
						var en = data.GetAbilityDetailDataList();
						if (en == null)
						{
							RouteAState = -1;
						}
						else
						{
							// (1) TryCast the whole list. MEASURED 2026-10-03 (battle 9999): this FAILS.
							//     Il2CppInterop models ValueTuple as a CLASS wrapper while the native collection
							//     is List<struct ValueTuple<...>>, so the generic type check never matches.
							var lst = en.TryCast<Il2CppSystem.Collections.Generic.List<Il2CppSystem.ValueTuple<AbilityData, eAbilitySlotType, int>>>();
							if (lst != null)
							{
								SetRouteA(1);
								for (int i = 0; i < lst.Count && i < MaxRouteAEntries; i++)
									TakeTuple(lst[i], lst[i], byPtr, byIdLv);
							}
							else
							{
								// (2) The way in is the NON-generic System.Collections.IEnumerable: its interop
								//     type DOES expose GetEnumerator/MoveNext -- the "interop enumerator exposes
								//     no MoveNext" limitation applies only to the generic IEnumerator<T> -- and
								//     each item arrives as a boxed object that can be read as a ValueTuple.
								var ngen = en.TryCast<Il2CppSystem.Collections.IEnumerable>();
								var it = (ngen == null) ? null : ngen.GetEnumerator();
								if (it == null)
								{
									SetRouteA(-1);
								}
								else
								{
									int guard = 0;
									int got = 0;
									while (guard++ < MaxRouteAEntries && it.MoveNext())
									{
										Il2CppSystem.Object cur = null;
										try { cur = it.Current; } catch (Exception ex) { Note("routeA.Current", ex); break; }
										if (cur == null) continue;
										var t = cur.TryCast<Il2CppSystem.ValueTuple<AbilityData, eAbilitySlotType, int>>();
										if (t == null) { RouteACastFails++; continue; }
										int before = byPtr.Count + byIdLv.Count;
										TakeTuple(cur, t, byPtr, byIdLv);
										if (byPtr.Count + byIdLv.Count > before) got++;
									}
									SetRouteA(got > 0 ? 2 : -1);
								}
							}
						}
						if (byPtr.Count == 0 && byIdLv.Count == 0) SetRouteA(-1);
						if (byPtr.Count > 0 || byIdLv.Count > 0) RouteAUnitsOk++; else RouteAUnitsFail++;
					}
				}
			}
			catch (Exception ex) { Note("routeA", ex); SetRouteA(-2); }

			// ---- the statistics entries are READ but no longer JOINED (1.4.0) ----
			// Route C used to map `byStatId[Key(s.SlotId, s.SlotSubId)] = s.Slot` and look it up with
			// `Key(re.Id, re.Level)`. The export's own `statsSample` proves those are different id spaces:
			// it reads "(神器:0/0)(神器:1/0)(被动:0/0)(被动:1/0)" -- two 神器 rows with SlotId 0 and 1, both
			// SlotSubId 0, i.e. a SLOT-LOCAL index -- while ability ids look like 40009. So the join could
			// never match, and it did not: `cJoins` is 0 in 50 of 50 exports, including battle_9999 (v1.1.0)
			// where it was the last resort and rescued none of 142 unlabelled abilities.
			// It is deleted rather than re-keyed: the row type `AbilityStatistics` is absent from the API
			// dump, so the field semantics a correct key needs cannot be verified -- any re-key would be
			// another guess. The entries are still read and reported (StatsEntries / StatsSample), because
			// "is that list populated at all?" remains a useful measurement.
			try
			{
				CharacterStatistics st = null;
				try { st = bo.Statistics; } catch (Exception ex) { Note("bo.Statistics", ex); }
				var abs = (st == null) ? null : st.m_abilityStatistics;
				if (abs != null)
				{
					// Keep the sample from the unit with the LONGEST list: every unit overwrites the statics,
					// so the last one (often a 1-ability token) used to be all we ever saw.
					if (abs.Count >= StatsEntries)
					{
						StatsEntries = abs.Count;
						var smp = new StringBuilder(48);
						StatsRows.Clear();
						StatsRowTotal = abs.Count;
						for (int i = 0; i < abs.Count && i < 6; i++)
						{
							try
							{
								var s = abs[i];
								if (s == null) continue;
								smp.Append('(').Append(SlotLabel((int)s.Slot)).Append(':')
								   .Append(s.SlotId).Append('/').Append(s.SlotSubId).Append(')');
								// 1.5.0 (B2): the same triple as numbers, collected in the SAME walk so
								// the string and the numbers cannot describe two different reads.
								StatsRows.Add(new int[] { (int)s.Slot, s.SlotId, s.SlotSubId });
							}
							catch { }
						}
						StatsSample = smp.ToString();
					}
				}
			}
			catch (Exception ex) { Note("statistics", ex); }

			// ---- assemble ----
			for (int i = 0; i < list.Count && res.Count < MaxAbilities; i++)
			{
				try
				{
					Ability ab = list[i];
					if (ab == null) continue;
					AbilityData d = null;
					try { d = ab.m_data; } catch (Exception ex) { Note("ab.m_data", ex); }
					if (d == null) continue;
					var re = new RosterAbility();
					try { re.Id = d.Id; } catch (Exception ex) { Note("d.Id", ex); }
					try { re.Level = d.Level; } catch (Exception ex) { Note("d.Level", ex); }
					try { re.Name = d.Name ?? ""; } catch { }
					long p = Ptr(d);
					int slot;
					if (p != 0L && byPtr.TryGetValue(p, out slot)) { re.Slot = slot; re.Origin = 1; RouteAHits++; }
					else if (byIdLv.TryGetValue(Key(re.Id, re.Level), out slot)) { re.Slot = slot; re.Origin = 1; RouteAIdHits++; }
					// No route C: deleted in 1.4.0 (its key was proven wrong -- see the note above).
					else Unlabelled++;
					// Cross-unit consistency: an ability (id + level + name) must always resolve to one slot.
					if (re.Origin != 0 && re.Slot >= 0)
					{
						string ident = re.Id + "|" + re.Level + "|" + re.Name;
						int prev;
						if (_slotByIdentity.TryGetValue(ident, out prev))
						{
							if (prev != re.Slot) SlotConflicts++;
						}
						else _slotByIdentity[ident] = re.Slot;
					}

					var tl = d.m_talents;
					if (tl != null)
						for (int k = 0; k < tl.Length && re.Talents.Count < MaxTalents; k++)
						{
							try
							{
								Talent t = tl[k];
								if (t == null) continue;
								TalentData td = null;
								try { td = t.m_talentData; } catch (Exception ex) { Note("t.m_talentData", ex); }
								if (td == null) continue;
								var tr = new TalentRef { Index = k, Live = td };
								try { tr.Type = (int)td.TalentType; } catch (Exception ex) { Note("TalentType", ex); }
								try { tr.P0 = td.GetParam(0); } catch { }
								try { tr.P1 = td.GetParam(1); } catch { }
								try { tr.P2 = td.GetParam(2); } catch { }
								try { tr.Timing = (int)td.GetTriggerTiming(); } catch { }
								InvokingCondition ic = null;
								try { ic = t.m_triggerCondition; } catch (Exception ex) { Note("m_triggerCondition", ex); }
								try { tr.Cond = CondText(ic); } catch (Exception ex) { Note("cond", ex); }
								try { tr.Last = td._ActivateCount_k__BackingField; } catch (Exception ex) { Note("field", ex); }
								re.Talents.Add(tr);
							}
							catch (Exception ex) { Note("talent[" + k + "]", ex); }
						}
					// 刻印 rows carry no name of their own. Fill it from the master tables, but only when
					// the (id, talent-signature) key resolves to ONE distinct official name -- the two 刻印
					// id spaces overlap, so an id-only lookup would be a confident wrong answer.
					// See MasterData/MasterDataNames.cs for the measured evidence.
					if (string.IsNullOrEmpty(re.Name) && re.Slot == EngravingSlot)
					{
						try
						{
							string official;
							if (MasterDataNames.TryLabel(re.Id, re.Level, re.Talents, out official))
							{
								re.Name = official;
								re.NameFromMaster = true;
							}
						}
						catch (Exception ex) { Note("masterName", ex); }
					}
					res.Add(re);
				}
				catch (Exception ex) { Note("ability[" + i + "]", ex); }
			}
		}
		catch (Exception ex) { Note("Of", ex); }
		return res;
	}

	/// <summary>The structured trigger condition of a talent, decoded from InvokingCondition.m_data.
	/// This is the machine-readable replacement for parsing Japanese ability text: eTalentCondType names
	/// the exact predicate, so "does this talent fire on 凍結 / on 貫通" is a value comparison.</summary>
	private static string CondText(InvokingCondition ic)
	{
		var sb = new StringBuilder(48);
		try
		{
			if (ic == null) return "";
			var list = ic.m_data;
			if (list == null) return "";
			for (int i = 0; i < list.Count && i < MaxConditions; i++)
			{
				try
				{
					var at = list[i];
					if (at == null) continue;
					int ty = 0;
					try { ty = (int)at.type; } catch { }
					if (sb.Length > 0) sb.Append('+');
					sb.Append(CondLabel(ty));
					var nums = at.num;
					if (nums != null && nums.Count > 0)
					{
						sb.Append('(');
						for (int k = 0; k < nums.Count && k < 3; k++)
						{
							if (k > 0) sb.Append('|');
							int v = 0;
							try { v = GameRef.Dec(nums[k]); } catch { }
							sb.Append(v);
						}
						sb.Append(')');
					}
				}
				catch (Exception ex) { Note("cond[" + i + "]", ex); }
			}
		}
		catch { }
		return sb.ToString();
	}

	/// <summary>Slot-grouped one-liner, e.g. "职业2/觉醒1/刻印3/神器1/技能2/未分类1".</summary>
	internal static string Brief(List<RosterAbility> roster)
	{
		if (roster == null || roster.Count == 0) return "";
		var cnt = new Dictionary<int, int>();
		foreach (var r in roster)
		{
			cnt.TryGetValue(r.Slot, out var c);
			cnt[r.Slot] = c + 1;
		}
		var keys = new List<int>(cnt.Keys);
		keys.Sort();
		var sb = new StringBuilder(64);
		foreach (int k in keys)
		{
			if (sb.Length > 0) sb.Append('/');
			sb.Append(SlotLabel(k)).Append(cnt[k]);
		}
		return sb.ToString();
	}

	// ------------------------------------------------------------- labels

	/// <summary>
	/// TalentDefine.Type and eBuffType share this one field, so the id decides which table applies.
	/// MEASURED (battle 9999, 2026-10-03): a real battle mixes both -- 1005 DamageUp / 1015 AdditionalAttack
	/// come from TalentDefine.Type, while 511 KnockBack / 515 Frozen / 519 Death come from eBuffType.
	/// Anything in neither table keeps its raw id so a wrong guess can never look like a decoded meaning.
	/// </summary>
	internal static string TypeLabel(int t)
	{
		if (t >= 1000)
		{
			string a = TalentNames.ActionType(t);
			return (a.Length > 0) ? a : ("T" + t);
		}
		if (t > 0)
		{
			string b = TalentNames.BuffType(t);
			if (b.Length > 0) return b;
			string c = CompositionProbe.TalentTypeName(t);   // the small verified subset [ABIL] already uses
			if (c.Length > 0) return c;
		}
		return (t == 0) ? "None" : ("t" + t);
	}

	internal static string TimingLabel(int v)
	{
		string s = TalentNames.Timing(v);
		return (s.Length > 0) ? s : ("T" + v);
	}

	internal static string CondLabel(int v)
	{
		string s = TalentNames.Cond(v);
		return (s.Length > 0) ? s : ("c" + v);
	}

	internal static string SlotLabel(int v)
	{
		switch (v)
		{
			case 0: return "未定义";
			case 1: return "基础被动";
			case 2: return "职业特性";
			case 3: return "副技能";
			case 4: return "觉醒";
			case 5: return "被动";
			case 6: return "潜在";
			case 7: return "专用武器";
			case 8: return "特殊行动";
			case 9: return "神器";
			case 10: return "刻印";
			case 11: return "皮肤";
			case 1000: return "技能";
			default: return "未分类";
		}
	}

}
