using System;
using System.Collections.Generic;
using System.Text;

namespace DpsMeter;

/// <summary>
/// Per-hit observation of WHICH talents (素质/词条) fired, taken from the game's own activation counters.
///
/// This is the direct replacement for inferring "did the 貫通 add-on trigger this character's 素質?" from
/// arithmetic. The game counts activations itself
/// (CharacterStatistics.m_abilityStatistics -> TalentStatistics.ActivateCount, and per-instance
/// TalentData.ActivateCount), so a diff of that counter is evidence, not a reconstruction.
///
/// SEMANTICS OF THE ATTRIBUTION (stated because it is a choice, not a fact)
///   The counter is read in the POSTFIX of BattleObject.Damage -- i.e. once per damage event, after the hit
///   landed -- and compared with the value stored from that actor's PREVIOUS damage event. A talent that
///   fired in between is therefore attributed to the hit it produced, which is the useful direction:
///   for ネーフェ＝ジアー the 素質 add-on fires after hit 1 and lands hit 2, so the delta appears on hit 2 --
///   the extra hit that exists only because the talent fired.
///   Consequence: a talent that fires but produces no damage at all is attributed to that actor's NEXT
///   damage event. The battle-end table (FinalizeTable) is unaffected by this, because it reports the
///   accumulated totals.
///
/// WHY THE COUNTER IS READ FROM THE BACKING FIELD
///   <c>_ActivateCount_k__BackingField</c> is a plain offset + dereference, while the public property is a
///   full <c>il2cpp_runtime_invoke</c>. This runs once per talent per damage event, so the cheap path is
///   the only one that can be considered a hot-path read. All three counter candidates are recorded at
///   battle end (Delta / Prop / Agg) precisely so the live data can confirm which one is the battle-scoped
///   count instead of us assuming it.
/// </summary>
internal static class TalentRuntime
{
	internal static long Reads;
	internal static int Errors;
	internal static int Capped;
	private static string _firstError = "";

	internal static void ResetSession()
	{
		AbilityRoster.ResetSession();
		Reads = 0;
		Errors = 0;
		Capped = 0;
		_firstError = "";
	}

	private static void Note(string where, Exception ex)
	{
		Errors++;
		if (_firstError.Length == 0)
			_firstError = where + ": " + ex.GetType().Name + " " + ex.Message;
	}

	internal static string Diag()
	{
		return "计数读取=" + Reads + " 异常=" + Errors
			+ (Capped > 0 ? " 超预算=" + Capped + " 次(单次命中素质数超过 512)" : "")
			+ (_firstError.Length > 0 ? "(首条 " + _firstError + ")" : "");
	}

	/// <summary>
	/// Make sure every unit on OUR side has a roster, including one that never dealt damage -- a pure
	/// healer would otherwise show no 刻印/神器/素质 at all in the detail view. Bounded to our own side and
	/// to units whose live object still exists, and each build is individually guarded.
	/// </summary>
	private static void EnsureRosters(BattleSession s)
	{
		try
		{
			foreach (ActorStats a in s.OrderedActors)
			{
				try
				{
					if (a == null || a.Roster != null) continue;
					if (!CharacterInfo.IsAllyTeam(a.Team)) continue;
					BattleObject bo = a.Source;
					if (GameRef.IsNull(bo)) continue;
					AbilityRoster.Of(a, bo);
				}
				catch (Exception ex) { Note("EnsureRosters.row", ex); }
			}
		}
		catch (Exception ex) { Note("EnsureRosters", ex); }
	}

	/// <summary>
	/// Attribute every talent that fired since this actor's previous damage event to <paramref name="ev"/>.
	/// Called from Aggregator.RecordDamage right after the event object exists.
	/// </summary>
	internal static void NoteAttack(BattleObject attacker, ActorStats a, BattleEvent ev)
	{
		if (a == null || ev == null) return;
		try
		{
			if (Plugin.CfgTalentTriggers != null && !Plugin.CfgTalentTriggers.Value) return;
			List<RosterAbility> roster = AbilityRoster.Of(a, attacker);
			if (roster == null || roster.Count == 0) return;

			List<TriggerHit> hits = null;
			// Hard per-hit budget. Typical units carry ~15 abilities x a few talents (~75 reads), but the
			// roster is capped at 64x32 = 2048, and this runs on every damage event. The budget makes the
			// worst case bounded and VISIBLE (Capped) instead of a silent frame-time cliff.
			int budget = 512;
			bool capped = false;
			for (int i = 0; i < roster.Count; i++)
			{
				RosterAbility re = roster[i];
				for (int k = 0; k < re.Talents.Count; k++)
				{
					if (budget <= 0) { capped = true; break; }
					budget--;
					TalentRef tr = re.Talents[k];
					TalentData td = tr.Live;
					if (td == null) continue;
					int now;
					try { now = td._ActivateCount_k__BackingField; }
					catch (Exception ex) { Note("read", ex); continue; }
					Reads++;
					int d = now - tr.Last;
					tr.Last = now;
					if (d <= 0) continue;
					tr.Delta += d;
					if (hits == null) hits = new List<TriggerHit>(4);
					hits.Add(new TriggerHit
					{
						Type = tr.Type,
						TalentIndex = tr.Index,
						Delta = d,
						Slot = re.Slot,
						AbilityId = re.Id,
						Ability = re.Name
					});
				}
				if (capped) break;
			}
			if (capped) Capped++;
			if (hits != null)
			{
				ev.TriggerList = hits;
				int hidden;
				string txt = TriggerText(hits, out hidden);
				// MEASURED (battle 9999): passive parameter talents fire on nearly every hit (eBuffType 511
				// KnockBack alone fired 408 times), so the row label names only the NOTABLE ones -- actions
				// (id >= 1000) and status applications (eBuffType 500..519). When none of those fired the label
				// stays empty rather than turning every row into noise; TriggerList/triggers[] keep everything.
				if (txt.Length > 0) ev.Triggers = txt;
			}
		}
		catch (Exception ex) { Note("NoteAttack", ex); }
	}

	/// <summary>True for a talent worth naming on the hit row: an action, or a status application.</summary>
	private static bool IsNotable(int t)
	{
		return t >= 1000 || (t >= 500 && t <= 519);
	}

	/// <summary>"追加攻击、冻结" -- notable activations only; <paramref name="hidden"/> receives the count of
	/// parameter-only activations that were left out (they are still in TriggerList and the battle table).</summary>
	private static string TriggerText(List<TriggerHit> hits, out int hidden)
	{
		var sb = new StringBuilder(48);
		hidden = 0;
		try
		{
			for (int i = 0; i < hits.Count; i++)
			{
				TriggerHit h = hits[i];
				if (!IsNotable(h.Type)) { hidden++; continue; }
				if (sb.Length > 0) sb.Append('、');
				sb.Append(AbilityRoster.TypeLabel(h.Type));
				if (h.Delta > 1) sb.Append('×').Append(h.Delta);
			}
		}
		catch { }
		return sb.ToString();
	}

	/// <summary>
	/// Battle-end per-unit talent table. Runs BEFORE the export and before ActorStats.Source is cleared,
	/// because the audit columns (TotalActivateCount / m_statistics.ActivateCount) need the live object.
	/// Only talents that did something are kept, so the export does not grow by the whole roster.
	/// </summary>
	internal static void FinalizeTable(BattleSession s)
	{
		try
		{
			if (s == null) return;
			EnsureRosters(s);
			foreach (ActorStats a in s.OrderedActors)
			{
				try
				{
					List<RosterAbility> roster = a.Roster;
					if (roster == null || roster.Count == 0) continue;
					var tbl = new List<TalentUsage>();
					for (int i = 0; i < roster.Count; i++)
					{
						RosterAbility re = roster[i];
						for (int k = 0; k < re.Talents.Count; k++)
						{
							TalentRef tr = re.Talents[k];
							var u = new TalentUsage
							{
								Slot = re.Slot,
								AbilityId = re.Id,
								Ability = re.Name,
								Index = tr.Index,
								Type = tr.Type,
								P0 = tr.P0,
								P1 = tr.P1,
								P2 = tr.P2,
								Delta = tr.Delta
							};
							try { if (tr.Live != null) u.Prop = tr.Live.TotalActivateCount; } catch { }
							try
							{
								if (tr.Live != null)
								{
									TalentStatistics st = tr.Live.m_statistics;
									if (st != null) u.Agg = st.ActivateCount;
								}
							}
							catch { }
							if (u.Delta != 0 || u.Prop != 0 || u.Agg != 0) tbl.Add(u);
						}
					}
					if (tbl.Count > 0)
					{
						// Most-fired first: the table is read to answer "what does this unit actually do".
						tbl.Sort(delegate (TalentUsage x, TalentUsage y)
						{
							long a = Math.Max(x.Delta, Math.Max(x.Prop, x.Agg));
							long b = Math.Max(y.Delta, Math.Max(y.Prop, y.Agg));
							return b.CompareTo(a);
						});
						a.TalentTable = tbl;
					}
				}
				catch (Exception ex) { Note("table.row", ex); }
			}
		}
		catch (Exception ex) { Note("FinalizeTable", ex); }
	}
}
