using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace DpsMeter;

public static partial class Aggregator
{

	private static void FinalizeLocked(BattleSession s, GameResult result, string why)
	{
		s.InBattle = false;
		// R56 (BID-1, plan §3/§4): stamp the close BEFORE anything reads the identity, so the export,
		// the evidence bundle, the summary and the overlay all describe the same state/reason pair. A
		// close without a result stays provisional -- it must not masquerade as a completed battle.
		BattleRefRegistry.MarkClosed(s.Ref, why, (int)result);
		// Remember how this one ended so the NEXT session can decide whether it is a continuation of the
		// same stretch of play (grouping marker only -- see BattleSession.RunId). RF4: the state is the
		// container's; this stays the FIRST thing a finalisation does.
		Continuity.RememberEnd(s.QuestId, (int)result, why, DateTime.Now);
		// NOTE: the battle-wide rule table is deliberately NOT cleared here. Units of the NEXT battle are
		// created (BattleObject.SetupAbility -> RegisterGlobalDebuffs) BEFORE the previous battle is
		// finalized, so clearing here threw away the fresh registrations and an ally buff like
		// "編成時、味方ヴァイスの魔法攻撃の与ダメージ+15%" only worked after its owner had acted once.
		// Stale entries point at destroyed objects (skipped by the Unity null check) and reused pointers
		// are re-scanned because the registered owner name is compared.
		BattleSummary battleSummary = new BattleSummary
		{
			QuestId = s.QuestId.ToString(),
			Result = result.ToString(),
			DurationSeconds = s.ActiveSeconds,
			Session = s,
			Ref = s.Ref
		};
		foreach (ActorStats orderedActor in s.OrderedActors)
		{
			battleSummary.Actors.Add(orderedActor);
			battleSummary.TotalDealt += orderedActor.DamageDealt;
			battleSummary.TotalTaken += orderedActor.DamageTaken;
			battleSummary.TotalHealing += orderedActor.HealingGiven;
		}
		battleSummary.ActorCount = battleSummary.Actors.Count;
		try { battleSummary.Events.AddRange(s.Events); } catch { }
		// RF4h: the ring rule (insert newest-first, drop the oldest, cap at MaxHistory) is a tested helper;
		// the cap used to be a second literal 20 next to Aggregator.MaxHistory.
		BattleHistoryRing.Push(History, battleSummary, BattleHistoryRing.Max);

		StringBuilder sb = new StringBuilder();
		sb.Append($"[DpsMeter] Battle {why}: result={result} dur={BattleTime.Log(s.ActiveSeconds)} idle={BattleTime.Log(s.IdleCombatSeconds)} quest={s.QuestId} actors={s.OrderedActors.Count} unattributed={s.UnattributedDamage}(x{s.UnattributedHits})\n");
		foreach (ActorStats a in s.OrderedActors)
		{
			if ((int)a.Team == 1 || a.DamageDealt != 0L || a.DamageTaken != 0L || a.HealingGiven != 0L)
			{
				sb.Append($"  {KindTag(a),-5} {a.Name,-24} dealt={a.DamageDealt,10} dps={a.Dps(s.ActiveSeconds),8:F1} hits={a.HitCount,5} maxhit={a.MaxHitDamage,8} healed={a.HealingGiven,8}(nom{a.HealingGivenNominal,9}) self={a.HealingSelf,8} taken={a.DamageTaken,8} [{a.AttrMode}]\n");
			}
		}
		if (s.UnattributedDamage != 0L)
		{
			sb.Append($"  >>> 未归属伤害(打到目标但无攻击者来源) {s.UnattributedDamage} / {s.UnattributedHits} hits\n");
			var top = new List<KeyValuePair<string, long>>(s.UnattributedByVictim);
			top.Sort((x, y) => y.Value.CompareTo(x.Value));
			int shown = 0;
			foreach (var kv in top)
			{
				if (shown++ >= 15) break;
				sb.Append($"      gap victim: {kv.Key,-28} {kv.Value,10}\n");
			}
		}
		sb.Append($"  TOTALS dealt={battleSummary.TotalDealt} taken={battleSummary.TotalTaken} healing={battleSummary.TotalHealing}");
		if (Rt.AbsorbedHits > 0)
		{
			// The reconciliation line: our taken total is the damage that reached 耐久, the game's own
			// counter adds everything that was absorbed on the way, so taken + absorbed must equal it.
			sb.Append($"\n  >>> 被吸收/无效化 {Rt.AbsorbedTotal} / {Rt.AbsorbedHits} hits  (taken {battleSummary.TotalTaken} + 吸收 {Rt.AbsorbedTotal} = 游戏口径 {battleSummary.TotalTaken + Rt.AbsorbedTotal})");
		}
		string text = sb.ToString();
		Plugin.LogSource.LogInfo(text);
		RuntimeLog.Write(text);

		// Clock self-check. For the "real" source the battle clock and the wall clock measure the same
		// thing, so active/wall must be ~1.00; anything far from it means the clock is fed the wrong time
		// base (0.67 was measured while it consumed Time.unscaledDeltaTime = the 1/45 s logic step). For
		// the "game" source a ratio of ~stepRate/units (1.5 at timeScale 1.5, 30 units per game second)
		// is CORRECT, so only the source, the units and the ratio are reported -- the [CLOCKP] line shows
		// stepRate and the skill data that produced those units.
		try
		{
			double wallDur = (DateTime.Now - s.StartWallClock).TotalSeconds;
			if (wallDur > 1.0)
			{
				double rate = s.ActiveSeconds / wallDur;
				string src = ClockSourceName();
				string flag = "";
				if (src == "real" && (rate < 0.85 || rate > 1.15))
					flag = "  !!! 时钟与真实时间不符,请检查 Tick 的时间来源";
				string clock = $"[DpsMeter][CLOCK] source={src} units={GameUnitsPerSecond():F1} active={BattleTime.Log(s.ActiveSeconds)} wall={BattleTime.Log(wallDur)} ratio={rate:F2}{flag}";
				Plugin.LogSource.LogInfo(clock);
				RuntimeLog.Write(clock);
			}
		}
		catch { }

		// Cross check against game's own CharacterStatistics (official result panel data).
		foreach (ActorStats orderedActor3 in s.OrderedActors)
		{
			try
			{
				CharacterStatistics val = (!GameRef.IsNull(orderedActor3.Source)) ? orderedActor3.Source.Statistics : null;
				if (val != null)
				{
					string t2 = $"[DpsMeter][CROSS] {KindTag(orderedActor3),-5} {orderedActor3.Name,-24} mine_dealt={orderedActor3.DamageDealt,9} game_given={GameRef.Dec(val.GivenDamage),9} mine_taken={orderedActor3.DamageTaken,8} absorbed={orderedActor3.DamageAbsorbed,8} nominal_taken={orderedActor3.DamageTakenNominal,8} game_taken={GameRef.Dec(val.TakenDamage),8} mine_heal={orderedActor3.HealingGiven,8}(nom{orderedActor3.HealingGivenNominal,9}/self{orderedActor3.HealingSelf,8}) game_heal={GameRef.Dec(val.GivenHealing),8} mine_hits={orderedActor3.HitCount,5} game_attacks={GameRef.Dec(val.AttackCount),5}";
					Plugin.LogSource.LogInfo(t2);
					RuntimeLog.Write(t2);
				}
			}
			catch { }
		}
		if (Plugin.CfgTraceCandidates.Value) Probe.DumpAll();
		// Settle every per-hit ailment re-check still in flight BEFORE exporting, otherwise a record whose
		// status landed late would be exported without it.
		StatusDeltaProbe.FlushAll(s.ActiveSeconds);
		// ...then audit the result: every "status appeared" transition must have an infliction record.
		StatusDeltaProbe.Audit(s);
		// Battle-end talent table. MUST run before the export and before ActorStats.Source is cleared below,
		// because the audit columns (TotalActivateCount / m_statistics.ActivateCount) need the live object.
		TalentRuntime.FinalizeTable(s);
		// Self-report for the 1.1 provenance/talent features. Route A materialising or not is a MEASURED
		// outcome, so it is stated every battle instead of being assumed: if the ValueTuple list cannot be
		// materialised the slots come from the statistics join, and any unit left 未分类 is visible here.
		try
		{
			string rr = "[DpsMeter][ROSTER] 出处 " + AbilityRoster.Diag() + " | 素质 " + TalentRuntime.Diag();
			Plugin.LogSource.LogInfo(rr);
			RuntimeLog.Write(rr);
			if (Plugin.CfgAbilityRoster != null && Plugin.CfgAbilityRoster.Value)
			{
				foreach (ActorStats ra in s.OrderedActors)
				{
					if (ra == null || ra.Roster == null || ra.Roster.Count == 0) continue;
					string det = "[DpsMeter][ROSTER] " + (CharacterInfo.IsAllyTeam(ra.Team) ? "我方" : "敌方") + " " + ra.Name
						+ " 能力=" + ra.Roster.Count + " [" + AbilityRoster.Brief(ra.Roster) + "]"
						+ " 素质发动=" + (ra.TalentTable != null ? ra.TalentTable.Count : 0) + " 条";
					Plugin.LogSource.LogInfo(det);
					RuntimeLog.Write(det);
				}
			}
		}
		catch (Exception ex)
		{
			RuntimeLog.Write("[DpsMeter][ROSTER] 自检行输出失败(不影响导出): " + ex.Message);
		}
		// R64: the auto-skill cadence this battle actually produced -- per slot, the number of activations
		// and the MEDIAN interval between them on both clocks, plus every counter, so "it never fired" and
		// "the probe could not read it" are different lines. The per-activation [AUTOSK] act rows are
		// already in the log; this is the headline that can be quoted without re-parsing them.
		try
		{
			string ask = AutoSkillProbe.Summary();
			if (!string.IsNullOrEmpty(ask))
			{
				Plugin.LogSource.LogInfo(ask);
				RuntimeLog.Write(ask);
			}
		}
		catch (Exception ex)
		{
			RuntimeLog.Write("[AUTOSK] 自检行输出失败(不影响导出): " + ex.Message);
		}
		// R66: the 技能时间表's evidence. The counter line names every channel (so "the record hook never
		// fired" cannot be mistaken for "our units fired nothing"), and then the page's OWN lines are
		// written out, so what the panel showed this battle stays checkable offline.
		try
		{
			string stl = SkillTimelineProbe.Summary();
			if (!string.IsNullOrEmpty(stl))
			{
				Plugin.LogSource.LogInfo(stl);
				RuntimeLog.Write(stl);
			}
		}
		catch (Exception ex)
		{
			RuntimeLog.Write("[SKILLTL] 自检行输出失败(不影响导出): " + ex.Message);
		}
		// Feasibility probe for the 1.1 design (Diagnostics/SlotProbe.cs): must run BEFORE the export clears
		// the actors' BattleObject references (below), and before _calcEvents.Clear(). Gated by Debug/SlotProbe.
		if (Plugin.CfgSlotProbe != null && Plugin.CfgSlotProbe.Value) SlotProbe.Run(s);
		// Master data (the game's own tables) is process-global and never changes, so it is dumped once.
		// Done here because the tables are guaranteed loaded by the time a battle has ended; RunOnce
		// retries on a later battle if none were found yet.
		if (Plugin.CfgMasterDataDump != null && Plugin.CfgMasterDataDump.Value) MasterDataDump.RunOnce();
		string exportPath = ExportService.Export(s); // full-data JSON for offline analysis
		// R52c: keep the last finalised battle so the extraction KEY still works after it is over. MEASURED
		// 2026-10-05 13:26: a key press 12 s after a battle logged "跳过:没有战斗会话" because Aggregator nulls
		// Session at teardown and the 5 s resume window had expired. Runs here, while the session model is
		// still alive; EvidenceExtractor.Remember never throws.
		EvidenceExtractor.Remember(s, exportPath);
		// R52 (证据提取流程): the self-contained bundle. OFF by default, so a normal battle writes exactly
		// what it wrote before (the bundle is a ~25 MB copy). It runs AFTER the export and BEFORE the
		// actors' live references are cleared, so the census sees the same finished session the file does.
		// EvidenceExtractor.Run never throws -- a failure is counted, not propagated into a finalisation.
		try
		{
			if (Plugin.CfgExtractOnBattleEnd != null && Plugin.CfgExtractOnBattleEnd.Value)
				EvidenceExtractor.Run(s, "battle-end");
			else
				RuntimeLog.Write("[DpsMeter][EXTRACT] 关闭(General/ExtractOnBattleEnd=false) 自检 " + EvidenceExtractor.Diag());
		}
		catch (Exception ex)
		{
			RuntimeLog.Write("[DpsMeter][EXTRACT] 调度失败(不影响收尾): " + ex.Message);
		}
		_calcEvents.Clear();
		foreach (ActorStats orderedActor4 in s.OrderedActors) orderedActor4.Source = null;
		RuntimeLog.Flush();
		// Remember the close so a late event of the SAME battle can rejoin it (TryResumeClosedSession)
		// instead of opening a fragment session. Only an "idle" close is resumable. RF4: this stays the LAST
		// thing a finalisation does -- after the export -- so nothing can rejoin a half-written session.
	}
}
