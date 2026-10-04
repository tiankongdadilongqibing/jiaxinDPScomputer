using System;
using System.Diagnostics;
using System.Text;
using Rog.MasterData;

namespace DpsMeter;

/// <summary>
/// FEASIBILITY PROBE for the 1.1 "read the game's own bookkeeping" design
/// (see _dpsm_work/DESIGN-1.1-机制观测改造.md). Not a feature: it exists to answer, from a REAL battle,
/// the questions that cannot be settled statically:
///
///   A) Is <c>CharacterDataBase.GetAbilityDetailDataList()</c> materialisable? It returns
///      <c>IEnumerable&lt;Il2CppSystem.ValueTuple&lt;AbilityData, eAbilitySlotType, int&gt;&gt;</c>; Il2Cpp
///      enumerables are not consumable by foreach (see CompositionProbe.Talents.TalentListOf), so the only
///      route is TryCast to the concrete List. If the cast fails, the slot of an ability must be recovered by
///      joining <c>CharacterStatistics.m_abilityStatistics</c> instead -- this probe measures both.
///   B) Are <c>CharacterStatistics.m_abilityStatistics</c> / <c>TalentData.ActivateCount</c> actually
///      POPULATED at runtime? The members exist in metadata, which says nothing about values.
///   C) Which counter is the live one? There are three candidates and their semantics are unverified:
///      <c>TalentData._ActivateCount_k__BackingField</c>, <c>TalentData.ActivateCount</c>,
///      <c>TalentData.TotalActivateCount</c>, vs the aggregated <c>TalentData.m_statistics.ActivateCount</c>.
///      The probe prints all of them side by side so the data identifies itself.
///   D) Is a per-hit counter diff affordable? A per-hit design needs ~30 reads x 2 (before/after) x every hit.
///      The cost bench measures a direct backing-field read against a property read (runtime_invoke) and
///      extrapolates to a whole battle, so the hot-path design is decided by measurement, not by feel.
///   E) Do structured trigger conditions decode? <c>Talent.TriggerCondition.m_data</c> is the machine-readable
///      form of "this talent fires when the target IsFrozen / IsIgnoreAttack" that replaces parsing Japanese text.
///
/// Discipline (ARCHITECTURE 4.11 + the 1.0.57 lesson): every single read is wrapped on its own, failures are
/// COUNTED and reported in a summary line, and nothing here can throw out of Run(). A read that fails costs
/// one line, never the whole dump -- an outer try/catch around a whole collection is exactly what silently
/// blanked the status map in 1.0.57.
/// </summary>
internal static class SlotProbe
{
	private const int MaxActorsFull = 3;
	private const int MaxAbilities = 40;
	private const int MaxTalents = 14;
	private const int MaxConditions = 8;

	private static int _errors;
	private static readonly StringBuilder _firstError = new StringBuilder();

	private static void Err(string where, Exception ex)
	{
		_errors++;
		if (_firstError.Length == 0)
			_firstError.Append(where).Append(": ").Append(ex.GetType().Name).Append(' ').Append(ex.Message);
	}

	private static void W(string line)
	{
		RuntimeLog.Write("[SLOT] " + line);
	}

	// ---------------------------------------------------------------- entry

	internal static void Run(BattleSession s)
	{
		try
		{
			if (s == null) return;
			_errors = 0;
			_firstError.Length = 0;
			int seen = 0, full = 0, withStats = 0, withAbs = 0;
			foreach (ActorStats a in s.OrderedActors)
			{
				try
				{
					BattleObject bo = a.Source;
					if (GameRef.IsNull(bo)) continue;
					seen++;
					bool deep = full < MaxActorsFull;
					if (DumpActor(a, bo, deep))
					{
						withStats++;
						if (AbsCountOf(bo) > 0) withAbs++;
					}
					if (deep) full++;
				}
				catch (Exception ex) { Err("DumpActor", ex); }
			}
			CostBench(s);
			W("汇总: 单位=" + seen + " 详细=" + full
				+ " Statistics可用=" + withStats + " abilityStatistics非空=" + withAbs
				+ " 读取异常=" + _errors
				+ (_firstError.Length > 0 ? " 首条异常: " + _firstError : ""));
		}
		catch (Exception ex)
		{
			W("探针整体失败(不影响战斗): " + ex.GetType().Name + " " + ex.Message);
		}
	}

	// ---------------------------------------------------------------- per actor

	/// <summary>Returns true when bo.Statistics was reachable.</summary>
	private static bool DumpActor(ActorStats a, BattleObject bo, bool deep)
	{
		CharacterDataBase data = null;
		CharacterStatistics stats = null;
		try { data = bo.Data; } catch (Exception ex) { Err("bo.Data", ex); }
		try { stats = bo.Statistics; } catch (Exception ex) { Err("bo.Statistics", ex); }

		W("单位 " + (a.Name ?? "") + " kind=" + a.Kind + " team=" + (int)a.Team
			+ " Data=" + (data == null ? "空" : "有") + " Statistics=" + (stats == null ? "空" : "有"));

		// Non-deep actors stop here on purpose. Route A invokes a GAME method that builds a list, and this runs
		// during teardown once the battle is over, so the number of such calls is kept to MaxActorsFull by
		// design rather than made once per unit for the sake of completeness.
		if (!deep) return stats != null;

		// ---- A) GetAbilityDetailDataList: the only source that names the SLOT of an ability ----
		try
		{
			if (data == null) W("  A) 不可用(Data 为空)");
			else
			{
				var en = data.GetAbilityDetailDataList();
				if (en == null) W("  A) 返回 null");
				else
				{
					var lst = en.TryCast<Il2CppSystem.Collections.Generic.List<Il2CppSystem.ValueTuple<AbilityData, eAbilitySlotType, int>>>();
					if (lst == null)
					{
						W("  A) TryCast<List<ValueTuple<AbilityData,eAbilitySlotType,int>>> == null"
							+ " -> ValueTuple 路线不可物化,必须改用 B+C 连接");
					}
					else
					{
						W("  A) OK 物化成功 " + lst.Count + " 条");
						if (deep)
						{
							for (int i = 0; i < lst.Count && i < MaxAbilities; i++)
							{
								try
								{
									var t = lst[i];
									if (t == null) { W("    A#" + i + " <null>"); continue; }
									AbilityData d = t.Item1;
									eAbilitySlotType sl = t.Item2;
									int idx = t.Item3;
									W("    A#" + i + " slot=" + SlotName((int)sl) + "(" + (int)sl + ")"
										+ " idx=" + idx + " id=" + IdOf(d) + " lv=" + LevelOf(d) + " name=" + NameOf(d));
								}
								catch (Exception ex) { Err("A[" + i + "]", ex); }
							}
						}
					}
				}
			}
		}
		catch (Exception ex) { Err("routeA", ex); W("  A) 异常 " + ex.GetType().Name + " " + ex.Message); }

		// ---- B) bo.m_ability : the proven route (CompositionProbe.Talents already iterates it) ----
		Il2CppSystem.Collections.Generic.List<Ability> abil = null;
		try
		{
			abil = bo.m_ability;
			W("  B) m_ability = " + (abil == null ? "空" : abil.Count + " 条"));
		}
		catch (Exception ex) { Err("m_ability", ex); }

		// ---- C) CharacterStatistics.m_abilityStatistics : slot + per-talent activation counts ----
		Il2CppSystem.Collections.Generic.List<AbilityStatistics> abs = null;
		try
		{
			if (stats == null) W("  C) 不可用(Statistics 为空)");
			else
			{
				abs = stats.m_abilityStatistics;
				W("  C) m_abilityStatistics = " + (abs == null ? "空" : abs.Count + " 条"));
			}
		}
		catch (Exception ex) { Err("m_abilityStatistics", ex); }

		if (deep) DumpSlotStats(abs);
		JoinTest(abs, abil);
		if (deep) DumpAbilityTalents(abil);
		if (deep && stats != null) DumpTimersAndGates(stats);
		return stats != null;
	}
	private static int AbsCountOf(BattleObject bo)
	{
		try
		{
			var st = bo.Statistics;
			if (st == null) return 0;
			var abs = st.m_abilityStatistics;
			return (abs == null) ? 0 : abs.Count;
		}
		catch { return 0; }
	}

	/// <summary>C) the statistics entries themselves: slot + which talents are counted, and how they compare.</summary>
	private static void DumpSlotStats(Il2CppSystem.Collections.Generic.List<AbilityStatistics> abs)
	{
		if (abs == null) return;
		for (int i = 0; i < abs.Count && i < MaxAbilities; i++)
		{
			try
			{
				var st = abs[i];
				if (st == null) { W("    C#" + i + " <null>"); continue; }
				int slot = -1, slotId = 0, sub = 0;
				try { slot = (int)st.Slot; } catch (Exception ex) { Err("st.Slot", ex); }
				try { slotId = st.SlotId; } catch (Exception ex) { Err("st.SlotId", ex); }
				try { sub = st.SlotSubId; } catch (Exception ex) { Err("st.SlotSubId", ex); }
				var sb = new StringBuilder();
				var tl = st.TalentStatistics;
				int n = (tl == null) ? 0 : tl.Count;
				for (int k = 0; k < n && k < MaxTalents; k++)
				{
					try
					{
						var ts = tl[k];
						if (ts == null) continue;
						int ti = -1, ac = -1;
						try { ti = ts.TalentIndex; } catch (Exception ex) { Err("ts.TalentIndex", ex); }
						try { ac = ts.ActivateCount; } catch (Exception ex) { Err("ts.ActivateCount", ex); }
						if (k > 0) sb.Append(',');
						sb.Append("t").Append(ti).Append('=').Append(ac);
					}
					catch (Exception ex) { Err("ts[" + k + "]", ex); }
				}
				W("    C#" + i + " slot=" + SlotName(slot) + "(" + slot + ")"
					+ " slotId=" + slotId + " sub=" + sub + " talents=" + n + " {" + sb + "}");
			}
			catch (Exception ex) { Err("C[" + i + "]", ex); }
		}
	}

	/// <summary>
	/// Can the slot statistics be joined back to the abilities? That join is the fallback if route A cannot be
	/// materialised, so its hit rate decides whether the design survives losing route A.
	/// </summary>
	private static void JoinTest(
		Il2CppSystem.Collections.Generic.List<AbilityStatistics> abs,
		Il2CppSystem.Collections.Generic.List<Ability> abil)
	{
		if (abs == null || abil == null || abs.Count == 0 || abil.Count == 0) return;
		int byId = 0, byIdLv = 0, bySub = 0;
		try
		{
			for (int i = 0; i < abs.Count; i++)
			{
				int slotId = 0, sub = 0;
				try { var st = abs[i]; if (st == null) continue; slotId = st.SlotId; sub = st.SlotSubId; }
				catch (Exception ex) { Err("join.st", ex); continue; }
				bool hit = false, hitLv = false, hitSub = false;
				for (int k = 0; k < abil.Count; k++)
				{
					try
					{
						var ab = abil[k];
						if (ab == null) continue;
						var d = ab.m_data;
						if (d == null) continue;
						int aid = 0, alv = 0;
						try { aid = d.Id; } catch { }
						try { alv = d.Level; } catch { }
						if (aid == slotId) { hit = true; if (alv == sub) hitLv = true; }
					}
					catch { }
				}
				if (sub == 0 || hitLv) hitSub = true;
				if (hit) byId++;
				if (hitLv) byIdLv++;
				if (hitSub) bySub++;
			}
			W("  连接检验: SlotId==AbilityData.Id 命中 " + byId + "/" + abs.Count
				+ " ; 再加 Level==SlotSubId 命中 " + byIdLv + "/" + abs.Count
				+ " ; Sub 为 0 或命中 " + bySub + "/" + abs.Count);
		}
		catch (Exception ex) { Err("JoinTest", ex); }
	}

	/// <summary>
	/// B+C detail: per ability, its identity, and per talent every counter candidate plus the decoded
	/// structured trigger condition. This is the line that must show whether the 1.1 design has data.
	/// </summary>
	private static void DumpAbilityTalents(Il2CppSystem.Collections.Generic.List<Ability> abil)
	{
		if (abil == null) return;
		for (int i = 0; i < abil.Count && i < MaxAbilities; i++)
		{
			try
			{
				var ab = abil[i];
				if (ab == null) { W("    B#" + i + " <null>"); continue; }
				AbilityData d = null;
				try { d = ab.m_data; } catch (Exception ex) { Err("ab.m_data", ex); }
				W("    B#" + i + " id=" + IdOf(d) + " lv=" + LevelOf(d) + " name=" + NameOf(d)
					+ " talents=" + TalentCountOf(d));
				if (d == null) continue;
				var tl = d.m_talents;
				if (tl == null) continue;
				for (int k = 0; k < tl.Length && k < MaxTalents; k++)
				{
					try
					{
						var t = tl[k];
						if (t == null) continue;
						TalentData td = null;
						try { td = t.m_talentData; } catch (Exception ex) { Err("t.m_talentData", ex); }
						if (td == null) { W("      t" + k + " <TalentData null>"); continue; }
						W("      t" + k + " " + CondText(t, td) + " " + CounterText(td));
					}
					catch (Exception ex) { Err("talent[" + k + "]", ex); }
				}
			}
			catch (Exception ex) { Err("B[" + i + "]", ex); }
		}
	}

	/// <summary>
	/// C) The four counter candidates side by side. Their semantics are unverified; printing them together
	/// lets the live data say which one is the battle-scoped activation count.
	/// </summary>
	private static string CounterText(TalentData td)
	{
		var sb = new StringBuilder(64);
		try
		{
			int field = -1, prop = -1, total = -1, agg = -1;
			try { field = td._ActivateCount_k__BackingField; } catch (Exception ex) { Err("td.field", ex); }
			try { prop = td.ActivateCount; } catch (Exception ex) { Err("td.ActivateCount", ex); }
			try { total = td.TotalActivateCount; } catch (Exception ex) { Err("td.TotalActivateCount", ex); }
			var st = td.m_statistics;
			if (st != null)
			{
				try { agg = st.ActivateCount; } catch (Exception ex) { Err("st.ActivateCount", ex); }
			}
			sb.Append("cnt[field=").Append(field).Append(" prop=").Append(prop)
			  .Append(" total=").Append(total).Append(" aggStat=").Append(agg).Append(']');
		}
		catch (Exception ex) { Err("CounterText", ex); }
		return sb.ToString();
	}

	/// <summary>E) Talent type/timing/condition in structured form -- no Japanese text parsing.
	/// The condition list lives on <c>Talent.m_triggerCondition</c>, NOT on TalentData (verified by the
	/// compiler: TalentData has no such member). The tuning values (type/timing) are on TalentData.</summary>
	private static string CondText(Talent t, TalentData td)
	{
		var sb = new StringBuilder(96);
		try
		{
			int tt = 0, timing = -1;
			try { tt = (int)td.TalentType; } catch (Exception ex) { Err("td.TalentType", ex); }
			try { timing = (int)td.GetTriggerTiming(); } catch (Exception ex) { Err("GetTriggerTiming", ex); }
			sb.Append("type=").Append(tt).Append('(').Append(CompositionProbe.TalentTypeName(tt)).Append(')');
			sb.Append(" timing=").Append(timing).Append('(').Append(AbilityRoster.TimingLabel(timing)).Append(')');
			InvokingCondition ic = null;
			try { ic = t.m_triggerCondition; } catch (Exception ex) { Err("t.m_triggerCondition", ex); }
			if (ic == null) { sb.Append(" cond=无"); return sb.ToString(); }
			var list = ic.m_data;
			if (list == null) { sb.Append(" cond=空表"); return sb.ToString(); }
			sb.Append(" cond[").Append(list.Count).Append("]=");
			for (int i = 0; i < list.Count && i < MaxConditions; i++)
			{
				try
				{
					var at = list[i];
					if (at == null) continue;
					int ty = 0;
					try { ty = (int)at.type; } catch (Exception ex) { Err("at.type", ex); }
					if (i > 0) sb.Append(',');
					sb.Append(AbilityRoster.CondLabel(ty));
					try
					{
						var nums = at.num;
						if (nums != null && nums.Count > 0)
						{
							sb.Append('[');
							for (int k = 0; k < nums.Count && k < 4; k++)
							{
								if (k > 0) sb.Append('|');
								int nv = 0;
								try { nv = GameRef.Dec(nums[k]); } catch (Exception ex) { Err("nums[k]", ex); }
								sb.Append(nv);
							}
							sb.Append(']');
						}
					}
					catch (Exception ex) { Err("at.num", ex); }
					try
					{
						var opt = at.option;
						if (opt != null && opt.Count > 0)
						{
							sb.Append('{');
							for (int k = 0; k < opt.Count && k < 3; k++)
							{
								if (k > 0) sb.Append('|');
								try { sb.Append(opt[k]); } catch { }
							}
							sb.Append('}');
						}
					}
					catch (Exception ex) { Err("at.option", ex); }
				}
				catch (Exception ex) { Err("cond[" + i + "]", ex); }
			}
		}
		catch (Exception ex) { Err("CondText", ex); }
		return sb.ToString();
	}

	/// <summary>D) Are the periodic-talent timers and the probability gates readable at all?</summary>
	private static void DumpTimersAndGates(CharacterStatistics st)
	{
		try
		{
			var m = st.m_talentElapsedTimeManager;
			if (m == null) W("  计时器: 空");
			else
			{
				var c = m.mCounters;
				if (c == null) W("  计时器: mCounters 空");
				else
				{
					int n = Math.Min(c.Length, 24);
					var sb = new StringBuilder();
					for (int i = 0; i < n; i++)
					{
						if (i > 0) sb.Append(',');
						try { sb.Append(c[i]); } catch { sb.Append('?'); }
					}
					W("  计时器: mCounters[" + c.Length + "] = " + sb);
				}
			}
			var p = st.m_talentTriggerProbabilityManager;
			W("  概率门: " + (p == null ? "空" : "存在"));
		}
		catch (Exception ex) { Err("DumpTimers", ex); }
	}

	// ---------------------------------------------------------------- cost bench

	/// <summary>
	/// D) The decision the per-hit design hinges on: a direct backing-field read goes through
	/// <c>il2cpp_field_get_offset</c> + a pointer dereference, while the property goes through
	/// <c>il2cpp_runtime_invoke</c> (orders of magnitude dearer). Measured, then extrapolated to a 6000-hit
	/// battle with 30 talents of interest and two snapshots per hit.
	/// </summary>
	private static void CostBench(BattleSession s)
	{
		try
		{
			var tds = new System.Collections.Generic.List<TalentData>();
			foreach (ActorStats a in s.OrderedActors)
			{
				try
				{
					BattleObject bo = a.Source;
					if (GameRef.IsNull(bo)) continue;
					var list = bo.m_ability;
					if (list == null) continue;
					for (int i = 0; i < list.Count && tds.Count < 64; i++)
					{
						var ab = list[i];
						if (ab == null) continue;
						var d = ab.m_data;
						if (d == null) continue;
						var tl = d.m_talents;
						if (tl == null) continue;
						for (int k = 0; k < tl.Length && tds.Count < 64; k++)
						{
							var t = tl[k];
							if (t == null) continue;
							var td = t.m_talentData;
							if (td != null) tds.Add(td);
						}
					}
					if (tds.Count >= 64) break;
				}
				catch (Exception ex) { Err("bench.collect", ex); }
			}
			if (tds.Count == 0) { W("成本测定: 没有可用的 TalentData,无法测定"); return; }

			const int N = 20000;
			long sumField = 0, sumProp = 0;
			var sw = Stopwatch.StartNew();
			for (int i = 0; i < N; i++) { TalentData td = tds[i % tds.Count]; sumField += td._ActivateCount_k__BackingField; }
			double msField = sw.Elapsed.TotalMilliseconds;
			sw.Restart();
			for (int i = 0; i < N; i++) { TalentData td = tds[i % tds.Count]; sumProp += td.ActivateCount; }
			double msProp = sw.Elapsed.TotalMilliseconds;

			double nsField = msField * 1e6 / N;
			double nsProp = msProp * 1e6 / N;
			W(string.Format("成本测定(TalentData 数={0}): 字段直读 {1:F1}ms/{2} = {3:F0}ns/次 ; 属性(runtime_invoke) {4:F1}ms = {5:F0}ns/次 ; 比值 {6:F1}x (sum {7}/{8})",
				tds.Count, msField, N, nsField, msProp, nsProp, nsPorProp(nsField, nsProp), sumField, sumProp));
			W(string.Format("外推(6000 击 x 30 素质 x 前后2次): 字段直读 {0:F0}ms/场 ; 属性 {1:F0}ms/场",
				nsField * 30.0 * 2.0 * 6000.0 / 1e6, nsProp * 30.0 * 2.0 * 6000.0 / 1e6));
		}
		catch (Exception ex) { Err("CostBench", ex); }
	}

	private static double nsPorProp(double nsField, double nsProp)
	{
		return (nsField <= 0.0) ? 0.0 : nsProp / nsField;
	}

	// ---------------------------------------------------------------- small helpers

	private static int IdOf(AbilityData d)
	{
		try { return (d == null) ? 0 : d.Id; } catch (Exception ex) { Err("d.Id", ex); return 0; }
	}

	private static int LevelOf(AbilityData d)
	{
		try { return (d == null) ? 0 : d.Level; } catch (Exception ex) { Err("d.Level", ex); return 0; }
	}

	private static string NameOf(AbilityData d)
	{
		try { return (d == null) ? "" : (d.Name ?? ""); } catch (Exception ex) { Err("d.Name", ex); return ""; }
	}

	private static int TalentCountOf(AbilityData d)
	{
		try
		{
			if (d == null) return 0;
			var tl = d.m_talents;
			return (tl == null) ? 0 : tl.Length;
		}
		catch (Exception ex) { Err("d.m_talents", ex); return 0; }
	}

	/// <summary>Delegates to <see cref="AbilityRoster"/> so there is exactly one slot-id table in the
	/// codebase: the probe and the General/AbilityRoster feature must never disagree about what slot 10
	/// (刻印) or slot 9 (神器) means.</summary>
	internal static string SlotName(int v)
	{
		return AbilityRoster.SlotLabel(v);
	}
}
