using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;

namespace DpsMeter;

/// <summary>
/// Exports the full data of a finished battle to a JSON file for offline analysis.
/// Format: { app, version, config, battleRef, quest, duration, result, started, totals,
///          actors:[{name,team,kind,summon,dealt,taken,healingGiven,healingTaken,self,
///                   hit,maxHit,crit,perSecDamage:[],perSecTaken:[],perSecHeal:[],sources:{},skills:{}}],
///          events:[{t,type,victim,attacker,owner,attr,amount,nominal,source,crit,calc:{}}],
///          reconcile:{exact,approx,unexplained,theoryExceeds,byPair,byTenth*,topResidual[]},
///          unattributedByVictim:{} }
///
/// R56: `battleRef` (contract battle-ref/1) is this session's stable identity -- id/launchId/sequence are
/// fixed when the session is created, resetCount/revision/state move only at the documented events, and
/// the default file name ENDS with the id so two battles cannot share a target. Older exports have no
/// such block and stay valid; the offline selection tool addresses those by content hash.
/// </summary>
public static class ExportService
{
	private static string _baseDir;

	public static string Dir
	{
		get
		{
			if (_baseDir == null)
			{
				try { _baseDir = Path.Combine(Paths.PluginPath, "DpsMeter", "exports"); }
				catch { _baseDir = Path.Combine(AppContext.BaseDirectory, "exports"); }
				try { Directory.CreateDirectory(_baseDir); } catch { }
			}
			return _baseDir;
		}
	}

	private static HashSet<string> _knownIds;

	/// <summary>
	/// R56 (BID-2, plan §1/§5.1): is this battle id already published under the exports directory? The
	/// registry asks this before handing out a reference, so a colliding launch namespace is REGENERATED
	/// instead of reusing a number that already points at a file.
	///
	/// Built once per process from the directory listing (the ids are part of the file name). An
	/// unreadable directory yields "nothing known": the collision check then degrades, which is why the
	/// write path independently refuses a target name that does not end with this session's id.
	/// </summary>
	public static bool IdExists(string id)
	{
		if (string.IsNullOrEmpty(id)) return false;
		try
		{
			if (_knownIds == null)
			{
				var set = new HashSet<string>(StringComparer.Ordinal);
				foreach (string f in Directory.GetFiles(Dir, "battle_*.json"))
				{
					string name = Path.GetFileNameWithoutExtension(f);
					int at = name.IndexOf("__", StringComparison.Ordinal);
					if (at >= 0 && at + 2 < name.Length) set.Add(name.Substring(at + 2));
				}
				_knownIds = set;
			}
			return _knownIds.Contains(id);
		}
		catch { return false; }
	}

	public static string Export(BattleSession s)
	{
		return ExportTo(s, null);
	}

	/// <summary>R52 (证据提取流程): the SAME writer, to an explicit path. The evidence bundle must not
	/// grow a second serializer -- a second writer is a second contract, and the two would eventually
	/// disagree. A null/empty path keeps the historical exports/ location, so every existing caller and
	/// the export schema itself are unchanged.</summary>
	public static string ExportTo(BattleSession s, string file)
	{
		try
		{
			if (s == null) return null;
			// Computed once, from the events, and used for both the JSON and the self-report line, so the
			// number the user reads in the log and the number stored in the file cannot drift apart.
			CalcReconcile.Stats rec = CalcReconcile.Compute(s);
			// R56 (plan §5.1): a caller-supplied path (the evidence bundle) is a COPY of the same
			// session, not a second identity; only the default exports/ path is name-checked below.
			bool explicitTarget = !string.IsNullOrEmpty(file);
			if (!explicitTarget)
				file = Path.Combine(Dir, BattleRefPolicy.FileName(s.QuestId, s.StartWallClock,
					s.Ref == null ? null : s.Ref.Id));
			else
			{
				// An explicit path may live in a directory that does not exist yet (the bundle dir).
				try
				{
					string parent = Path.GetDirectoryName(file);
					if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
				}
				catch { }
			}
			string json = BuildJson(s, rec);
			// 1.5.0: structural self-check BEFORE the file is written. A hand-built writer can be broken by
			// an edit the compiler cannot see (a swallowed statement, a missing bracket, a duplicated key),
			// and the failure would otherwise only surface as "the analysis script threw". See JsonCheck.
			string jcErr;
			int jcDup;
			bool jsonOk = JsonCheck.Validate(json, out jcErr, out jcDup);
			// R56 (BID-2, plan §5.2): validate the identity, write through a temp file, and only then
			// report success. A write that did not happen must NOT look like one: before this the return
			// value was the path even when the write threw, so callers treated a lost export as saved.
			string idErr = (s.Ref == null) ? "" : BattleRefPolicy.ValidateBlock(
				s.Ref.Id, s.Ref.LaunchId, s.Ref.Sequence, s.Ref.Revision, s.Ref.State);
			if (idErr.Length == 0 && s.Ref != null && !explicitTarget && !BattleRefPolicy.FileNameMatchesId(file, s.Ref.Id))
				idErr = "目标文件名与该会话的编号不符";
			if (idErr.Length > 0)
			{
				string emsg = "[DpsMeter][BREF] 身份校验失败:" + idErr + " (仍写出文件,但不标记为已导出)";
				Plugin.LogSource.LogWarning(emsg);
				RuntimeLog.Write(emsg);
			}
			if (!WriteAtomic(file, json))
			{
				string fmsg = $"[DpsMeter] Export FAILED (临时写入或发布失败,旧文件保持不变) -> {file}";
				Plugin.LogSource.LogWarning(fmsg);
				RuntimeLog.Write(fmsg);
				return null;
			}
			string sha = FileSha256(file);
			// The identity now knows where its bytes are. Only a successful write may say so.
			if (s.Ref != null && idErr.Length == 0) BattleRefRegistry.MarkExported(s.Ref, file, sha);
			if (s.Ref != null)
			{
				string bline = "[DpsMeter][BREF] id=" + s.Ref.Id + " rev=" + s.Ref.Revision
					+ " state=" + s.Ref.State + " reset=" + s.Ref.ResetCount
					+ " close=" + (string.IsNullOrEmpty(s.Ref.CloseReason) ? "-" : s.Ref.CloseReason)
					+ " sha256=" + (sha.Length > 0 ? sha : "?") + " file=" + Path.GetFileName(file);
				Plugin.LogSource.LogInfo(bline);
				RuntimeLog.Write(bline);
				RuntimeLog.Flush();
			}
			// R57 (real machine): the battle-end evidence bundle writes the SAME data to an explicit target,
			// and the retention policy (ExtractKeep) deletes old bundles BY DESIGN. Logging both as "Exported
			// full battle data" made the live-log gate report "exported but the file is gone (truncated
			// corpus?)" for a bundle that had simply rotated. The durable export keeps its historical line;
			// a copy says it is a copy, so a reader -- and the gate -- can tell the two apart.
			string msg = explicitTarget
				? $"[DpsMeter] Exported COPY (explicit target, same serializer) -> {file} ({json.Length} bytes)"
				: $"[DpsMeter] Exported full battle data -> {file} ({json.Length} bytes)";
			Plugin.LogSource.LogInfo(msg);
			RuntimeLog.Write(msg);
			// The self-check result, always stated. `dupRootKeys` is reported even when the JSON parses,
			// because a duplicated root key is silently LOST on parse and that is a data-loss bug, not a
			// style issue. (json.Length is a character count, not bytes -- the message says bytes because
			// that is how the field has read since 0.9.x; the check below is what actually matters.)
			string jline = "[DpsMeter][JSON] 结构=" + (jsonOk ? "OK" : ("非法:" + jcErr))
				+ " 根键重复=" + jcDup + " 字符=" + json.Length;
			if (!jsonOk) Plugin.LogSource.LogWarning(jline); else Plugin.LogSource.LogInfo(jline);
			RuntimeLog.Write(jline);
			// The KPI as a one-liner: "is the composition right?" should be answerable without opening a
			// 6 MB JSON, and an unmeasurable battle should say so instead of looking like a zero.
			string line = "[DpsMeter][RECON] " + CalcReconcile.Summary(rec);
			Plugin.LogSource.LogInfo(line);
			RuntimeLog.Write(line);
			// The forensics channel self-report (1.4.0). Printed even when it kept nothing: "no unexplained
			// hits" and "the channel silently failed" are different results and must not look the same.
			string fline = "[DpsMeter][FORENSIC] " + Forensics.Summary()
				+ " liveState=" + UnitStateProbe.Reads + "读/" + UnitStateProbe.Errors + "错/"
				+ UnitStateProbe.NotCharacter + "非角色/" + UnitStateProbe.NullResistance + "无抗性对象"
				+ " 阈值=" + (UnitStateProbe.ThresholdRead ? UnitStateProbe.Threshold.ToString() : "?");
			Plugin.LogSource.LogInfo(fline);
			RuntimeLog.Write(fline);
			// 1.5.0 (A2): the damage-detail channel reported on itself. This line is the reason the
			// channel can no longer be silently dead: "produced=0" is a visible failure, whereas the old
			// state of affairs was a `source` column that was constant in 510,735 of 510,735 events and
			// said nothing about it.
			string hline = "[DpsMeter][HITDET] 产出=" + Aggregator.Rt.HitDetailProduced
				+ " 丢弃=" + Aggregator.Rt.HitDetailTrimmed
				+ " 错=" + Aggregator.Rt.HitDetailErrors
				+ " 匹配精确=" + Aggregator.Rt.HitMatchExact
				+ " 匹配弱=" + Aggregator.Rt.HitMatchPair
				+ " 未匹配=" + Aggregator.Rt.HitMatchNone
				// 1.5.2: the exported event count, printed NEXT TO the channel counters. On the 1.5.1
				// battle these disagreed by ~151 (5220 consumed vs 5055 exported damage events) and the
				// gap could not be attributed from the code -- the counters are static per session and
				// `AddEvent` is the statement immediately after `FactStore.Observe`. Printing the number
				// the export is actually built from makes the two directly comparable in one line, so the
				// next battle says whether events were lost or counters over-counted.
				+ " 导出事件=" + (s != null && s.Events != null ? s.Events.Count : 0);
			Plugin.LogSource.LogInfo(hline);
			RuntimeLog.Write(hline);
			// 1.5.0 (B4): the state-timeline self-report, so the channel states what it read.
			string tline = "[DpsMeter][TIMELINE] " + StateTimeline.Summary();
			Plugin.LogSource.LogInfo(tline);
			RuntimeLog.Write(tline);
			// 1.5.4 (贡献归因 C): the madness-applier channel states what its hook saw.
			string mline = "[DpsMeter][MADAPP] " + StatusApplierProbe.Summary();
			Plugin.LogSource.LogInfo(mline);
			RuntimeLog.Write(mline);
			// 1.5.5 (贡献归因 A): the give-applier channel states what its hooks saw.
			string givline = "[DpsMeter][GIVAPP] " + GiveApplierProbe.Summary();
			Plugin.LogSource.LogInfo(givline);
			RuntimeLog.Write(givline);
			// 1.5.0 (B1): the fact store, with its MEASURED coverage -- "every hit has facts" has to be a
			// number in the log, not a claim in a comment.
			int cf, cl, cd;
			FactStore.Coverage(s, out cf, out cl, out cd);
			string factLine = "[DpsMeter][FACT] " + FactStore.Summary()
				+ " 覆盖=" + cf + "/" + cd + " 含活体=" + cl
				// 1.5.2: same reason as the HITDET line -- the fact store's `击=` is a static counter and
				// `覆盖` is recomputed from the exported events, so the two must be readable side by side.
				+ " 导出事件=" + (s != null && s.Events != null ? s.Events.Count : 0);
			Plugin.LogSource.LogInfo(factLine);
			RuntimeLog.Write(factLine);
			// The granted-talent channel as its own line (1.3.5): "did the victim actually carry a
			// granted 被伤害 modifier, and did it account for the ×1.21 window?" is a different question
			// from the KPI, and it has to be answerable from the log alone. Read failures and unusable
			// values are printed even when zero, so a silent miss cannot look like "nothing was there".
			string gline = "[DpsMeter][GIVE]"
				+ " reads=" + CompositionProbe.GivenReads
				+ " hits=" + CompositionProbe.GivenCount
				+ " applied=" + CompositionProbe.GivenApplied
				+ " foldHits=" + CompositionProbe.GivenFoldHits
				+ " unusable=" + CompositionProbe.GivenUnusable
				+ " overflow=" + CompositionProbe.GivenOverflow
				+ " errors=" + CompositionProbe.GivenErrors
				+ " flagTrue=" + CompositionProbe.GivenActive
				+ " flagFalse=" + CompositionProbe.GivenPassive
				+ " clausesDeferred=" + CompositionProbe.GivenClauseDeferred
				+ " cancelled=" + CompositionProbe.GivenCancelled
				+ " types=" + CompositionProbe.GivenTypeHistogram()
				+ " selftest[" + CompositionProbe.GivenFactorSelfTest() + "]"
				+ " canceltest[" + CompositionProbe.GivenCancelSelfTest() + "]";
			Plugin.LogSource.LogInfo(gline);
			RuntimeLog.Write(gline);
			RuntimeLog.Flush();
			return file;
		}
		catch (Exception ex)
		{
			string msg = $"[DpsMeter] Export failed: {ex.Message}";
			Plugin.LogSource.LogWarning(msg);
			RuntimeLog.Write(msg);
			return null;
		}
	}

	/// <summary>R56 (plan §5.2 step 2): temp file in the SAME directory, then publish by replace/move, so
	/// an interrupted export can never truncate the previous complete file. A destination that blocks the
	/// atomic primitive falls back to an overwrite COPY -- the data is what matters, and the caller is
	/// told truthfully whether the bytes landed.</summary>
	private static bool WriteAtomic(string file, string json)
	{
		string tmp = file + ".tmp";
		try { File.WriteAllText(tmp, json, new UTF8Encoding(false)); }
		catch
		{
			try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
			return false;
		}
		try
		{
			if (File.Exists(file)) File.Replace(tmp, file, null);
			else File.Move(tmp, file);
			return true;
		}
		catch
		{
			try
			{
				File.Copy(tmp, file, true);
				try { File.Delete(tmp); } catch { }
				return true;
			}
			catch
			{
				try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
				return false;
			}
		}
	}

	/// <summary>SHA256 of the bytes actually on disk. "" when it cannot be read -- the copy text then says
	/// "no analysable file" rather than printing a hash nobody can verify.</summary>
	private static string FileSha256(string file)
	{
		try
		{
			using (var sha = System.Security.Cryptography.SHA256.Create())
			using (var fs = File.OpenRead(file))
			{
				byte[] h = sha.ComputeHash(fs);
				var sb = new StringBuilder(h.Length * 2);
				for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
				return sb.ToString();
			}
		}
		catch { return ""; }
	}

	private static string BuildJson(BattleSession s, CalcReconcile.Stats rec)
	{
		var sb = new StringBuilder(4096);
		sb.Append("{\"app\":\"").Append(BuildInfo.Name).Append("\",\"version\":\"").Append(BuildInfo.Version).Append('"');
		// 1.7.12: the settings that change the MEANING of numbers already in this file, so a reader can
		// tell from the file alone which convention it was written under. Today exactly one: does totals.dealt
		// (and per-second) include same-team damage (回復反転 / self-damage)? The contribution section's
		// friendly/hostileDamage split is event-derived and does NOT depend on this. Grows additively.
		sb.Append(",\"config\":{\"filterFriendlyFire\":")
		  .Append(Plugin.CfgFilterFriendlyFire != null && Plugin.CfgFilterFriendlyFire.Value ? "true" : "false")
		  .Append('}');
		// R56 (BID-0, plan §4): the identity block. It is written from the SAME BattleRef the overlay shows
		// and battle_select.py resolves, so "the number on screen" and "the file I compare" cannot diverge.
		if (s.Ref != null)
			sb.Append(",\"battleRef\":{\"schemaVersion\":\"").Append(BattleRefPolicy.SchemaVersion)
			  .Append("\",\"id\":\"").Append(Escape(s.Ref.Id))
			  .Append("\",\"launchId\":\"").Append(Escape(s.Ref.LaunchId))
			  .Append("\",\"sequence\":").Append(s.Ref.Sequence)
			  .Append(",\"resetCount\":").Append(s.Ref.ResetCount)
			  .Append(",\"revision\":").Append(s.Ref.Revision)
			  .Append(",\"state\":\"").Append(Escape(s.Ref.State))
			  .Append("\",\"closeReason\":\"").Append(Escape(s.Ref.CloseReason)).Append("\"}");

		long dealt = 0, taken = 0, heal = 0, healGiven = 0;
		foreach (var a in s.OrderedActors)
		{
			dealt += a.DamageDealt;
			taken += a.DamageTaken;
			heal += a.HealingTaken;
			healGiven += a.HealingGiven;
		}

		sb.Append(",\"quest\":").Append(s.QuestId);
		sb.Append(",\"duration\":").Append(s.ActiveSeconds.ToString("F2"));
		sb.Append(",\"result\":\"").Append(Escape(s.Result.ToString())).Append('"');
		sb.Append(",\"started\":\"").Append(s.StartWallClock.ToString("yyyy-MM-ddTHH:mm:ss")).Append('"');
		// Grouping marker (1.3.3): which continuous stretch of play this export belongs to. Wave/arena
		// content produces many short sessions for one stage; they share `run.id`, so offline analysis can
		// add them up. seq 0 starts a run (then gap/prevWhy/prevResult are omitted -- their absence means
		// "nothing preceded this in the run", not "not measured").
		sb.Append(",\"run\":{\"id\":").Append(s.RunId).Append(",\"seq\":").Append(s.RunSeq);
		if (s.RunSeq > 0)
			sb.Append(",\"gap\":").Append(s.RunGap.ToString("F2"))
			  .Append(",\"prevWhy\":\"").Append(Escape(s.RunPrevWhy)).Append('"')
			  .Append(",\"prevResult\":").Append(s.RunPrevResult);
		sb.Append('}');
		sb.Append(",\"totals\":{\"dealt\":").Append(dealt)
		  .Append(",\"taken\":").Append(taken)
		  .Append(",\"healing\":").Append(heal)
		  .Append(",\"healingGiven\":").Append(healGiven)
		  .Append(",\"unattributedDamage\":").Append(s.UnattributedDamage)
		  .Append(",\"unattributedHits\":").Append(s.UnattributedHits)
		  // Both 口径 side by side, so "why is the meter lower than the in-game report?" is answerable
		  // from one file. `dealt` (unchanged) counts what reached 耐久; the game's own statistic counts
		  // the PRE-absorption figure, i.e. dealt + absorbed. Measured 2026-10-03 (32 battles): the gap is
		  // 13.15% in aggregate and up to 55.6% in a single battle, so it is not a rounding detail.
		  .Append(",\"absorbed\":").Append(rec != null ? rec.AbsorbedAmount : 0L)
		  .Append(",\"dealtWithAbsorbed\":").Append(dealt + (rec != null ? rec.AbsorbedAmount : 0L))
		  .Append('}');

		// actors
		sb.Append(",\"actors\":[");
		bool first = true;
		foreach (var a in s.OrderedActors)
		{
			if (!first) sb.Append(',');
			first = false;
			int end = Math.Max(0, a.MaxSecond() - 1);
			sb.Append('{');
			// 1.5.0 (A4): the stable key events join on. `name` is NOT unique (same character on both
			// sides, same-kind summons within one team), which is why this exists.
			sb.Append("\"key\":").Append(a.Key);
			sb.Append(",\"name\":\"").Append(Escape(a.Name ?? "")).Append('"');
			sb.Append(",\"team\":").Append((int)a.Team);
			sb.Append(",\"kind\":\"").Append(Escape(a.Kind)).Append('"');
			sb.Append(",\"summon\":").Append(a.IsSummonMerge ? "true" : "false");
			sb.Append(",\"dealt\":").Append(a.DamageDealt);
			sb.Append(",\"friendly\":").Append(a.DamageFriendly);
			sb.Append(",\"friendlyHits\":").Append(a.FriendlyHits);
			sb.Append(",\"taken\":").Append(a.DamageTaken);
			// How much of the damage aimed at this unit was absorbed / nullified before 耐久. Present
			// only when it happened: its absence means "nothing was absorbed", not "not measured".
			if (a.DamageAbsorbed > 0) sb.Append(",\"absorbed\":").Append(a.DamageAbsorbed);
			sb.Append(",\"healingGiven\":").Append(a.HealingGiven);
			sb.Append(",\"healingTaken\":").Append(a.HealingTaken);
			sb.Append(",\"self\":").Append(a.HealingSelf);
			sb.Append(",\"hit\":").Append(a.HitCount);
			sb.Append(",\"maxHit\":").Append(a.MaxHitDamage);
			sb.Append(",\"crit\":").Append(a.CritCount);
			sb.Append(",\"attr\":\"").Append(Escape(a.AttrMode)).Append('"');

			sb.Append(",\"perSecDamage\":["); AppendLongArray(sb, a, end, 0);
			sb.Append("],\"perSecTaken\":["); AppendLongArray(sb, a, end, 1);
			sb.Append("],\"perSecHeal\":["); AppendLongArray(sb, a, end, 2);
			sb.Append("],\"hpPct\":["); AppendFloatArray(sb, a, end);
			sb.Append("],\"sources\":{");
			bool sf = true;
			foreach (var kv in a.SourceDamage)
			{
				if (!sf) sb.Append(',');
				sf = false;
				sb.Append('"').Append(kv.Key).Append("\":").Append(kv.Value);
			}
			sb.Append("}");
			sb.Append(',');
			sb.Append("\"skills\":{");
			bool sk = true;
			foreach (var kv in a.SkillDamage)
			{
				if (!sk) sb.Append(',');
				sk = false;
				sb.Append('"').Append(kv.Key).Append("\":").Append(kv.Value);
			}
			sb.Append("}");
			AppendRoster(sb, a);
			AppendTalentTable(sb, a);
			sb.Append('}');
		}
		sb.Append(']');

		// events
		sb.Append(",\"events\":[");
		first = true;
		if (s.Events != null)
		{
			foreach (var e in s.Events)
			{
				if (!first) sb.Append(',');
				first = false;
				sb.Append('{');
				sb.Append("\"t\":").Append(e.T.ToString("F2"));
				sb.Append(",\"type\":\"").Append(e.Type).Append('"');
				sb.Append(",\"victim\":\"").Append(Escape(e.Victim)).Append('"');
				sb.Append(",\"attacker\":\"").Append(Escape(e.Attacker)).Append('"');
				sb.Append(",\"owner\":\"").Append(Escape(e.Owner)).Append('"');
				sb.Append(",\"attr\":\"").Append(Escape(e.Attr)).Append('"');
				sb.Append(",\"amount\":").Append(e.Amount);
				sb.Append(",\"nominal\":").Append(e.Nominal);
				sb.Append(",\"source\":").Append(e.Source);
				sb.Append(",\"crit\":").Append(e.Crit ? "true" : "false");
				// 1.5.0 (A2/A3): the game's OWN crit flag as a tri-state -- 1 = observed NOT a crit,
				// 2 = observed crit, absent = not observed. `crit` above keeps its old bool shape for
				// existing scripts; the two can never disagree because Crit == (CritObserved == 2).
				if (e.CritObserved != 0) sb.Append(",\"critObserved\":").Append(e.CritObserved);
				// 1.5.0 (A2): how the damage-detail record was matched and what it carried. `hitMatch`
				// matters: 1 = authoritative (attacker+target+damage), 2 = best effort (attacker+target).
				if (e.HitMatch != 0) sb.Append(",\"hitMatch\":").Append(e.HitMatch);
				// 1.5.2: the figure the matched record carried. Without it `hitMatch` was a verdict with
				// no evidence -- see BattleEvent.HitValue.
				if (e.HitValue != 0) sb.Append(",\"hitValue\":").Append(e.HitValue);
				if (e.CalcHitType >= 0) sb.Append(",\"calcHitType\":").Append(e.CalcHitType);
				if (e.CalcEffectId != 0) sb.Append(",\"calcEffectId\":").Append(e.CalcEffectId);
				if (e.HealCalc) sb.Append(",\"healCalc\":true");
				if (e.Source == 15) sb.Append(",\"reversal\":true");
				if (e.AttackerTeam != 0) sb.Append(",\"atkTeam\":").Append(e.AttackerTeam);
				if (e.VictimTeam != 0) sb.Append(",\"vicTeam\":").Append(e.VictimTeam);
				// 1.5.0 (A4): stable actor keys, so an event joins to `actors[]` exactly instead of by
				// (name, team). 0 = the actor row could not be resolved.
				if (e.AttackerKey != 0) sb.Append(",\"atkKey\":").Append(e.AttackerKey);
				if (e.VictimKey != 0) sb.Append(",\"vicKey\":").Append(e.VictimKey);
				if (e.Friendly) sb.Append(",\"friendly\":true");
				// 1.5.0 (B1): the deduplicated fact record this hit belongs to. Absent = no fact recorded
				// for this event (not "the fact was empty": `facts.items` carries `hits`, so the reverse
				// lookup is exact).
				if (e.FactId > 0) sb.Append(",\"factId\":").Append(e.FactId);
				if (!string.IsNullOrEmpty(e.Comp)) sb.Append(",\"comp\":\"").Append(Escape(e.Comp)).Append('"');
				if (!string.IsNullOrEmpty(e.Comp2)) sb.Append(",\"comp2\":\"").Append(Escape(e.Comp2)).Append('"');
				if (!string.IsNullOrEmpty(e.Comp3)) sb.Append(",\"comp3\":\"").Append(Escape(e.Comp3)).Append('"');
				if (!string.IsNullOrEmpty(e.Comp4)) sb.Append(",\"comp4\":\"").Append(Escape(e.Comp4)).Append('"');
				// Structured mirror of the composition (1.3.0). The comp strings stay for humans; analysis
				// reads these fields instead of regex-parsing Chinese. Switchable because it is ~20% of the
				// file size, but ON by default: it is the data the whole reconciliation KPI is about.
				if (e.Calc.Valid && Plugin.CfgReconcileCalc != null && Plugin.CfgReconcileCalc.Value)
					CalcReconcile.AppendEventCalc(sb, e.Calc);
				// Ailments this record inflicted on the victim (diffed before/after the hit, see
				// Diagnostics/StatusDeltaProbe.cs). Also emitted as a short "inflicted" list so offline
				// analysis does not have to parse the Chinese sentence.
				if (!string.IsNullOrEmpty(e.StatusDelta))
				{
					sb.Append(",\"statusDelta\":\"").Append(Escape(e.StatusDelta)).Append('"');
					string tag = StatusDeltaProbe.Tag(e);
					if (!string.IsNullOrEmpty(tag)) sb.Append(",\"inflicted\":\"").Append(Escape(tag)).Append('"');
					if (!string.IsNullOrEmpty(e.StatusApplier))
						sb.Append(",\"applier\":\"").Append(Escape(e.StatusApplier)).Append('"');
				}
				// Which 素质/词条 fired between this attacker's previous hit and this one, read from the
				// game's own activation counters (Diagnostics/TalentRuntime.cs). Emitted both as a short
				// label and as structure, so analysis never has to parse the label.
				if (e.TriggerList != null && e.TriggerList.Count > 0)
				{
					sb.Append(",\"triggers\":[");
					for (int i = 0; i < e.TriggerList.Count; i++)
					{
						TriggerHit h = e.TriggerList[i];
						if (i > 0) sb.Append(',');
						sb.Append("{\"type\":").Append(h.Type)
						  .Append(",\"name\":\"").Append(Escape(AbilityRoster.TypeLabel(h.Type))).Append('"')
						  .Append(",\"n\":").Append(h.Delta)
						  .Append(",\"slot\":").Append(h.Slot)
						  .Append(",\"talentIdx\":").Append(h.TalentIndex)
						  .Append(",\"abilityId\":").Append(h.AbilityId);
						if (!string.IsNullOrEmpty(h.Ability))
							sb.Append(",\"ability\":\"").Append(Escape(h.Ability)).Append('"');
						sb.Append('}');
					}
					sb.Append(']');
				}
				sb.Append('}');
			}
		}
		sb.Append(']');

		// Reconciliation KPI (1.3.0). DERIVED from the events above, so the summary can never disagree
		// with the rows it summarises. Carries the definition of every band in-band (approxTolerance /
		// theoryExceedsResidualMax): the offline "reconcile rate" drifted into two incomparable figures
		// precisely because the definition lived only in whoever wrote the last analysis script.
		CalcReconcile.AppendJson(sb, rec);
		// 1.5.0 (A2): what the damage-detail channel actually did. This block exists because the channel's
		// failure mode was SILENCE -- it had no producer at all, so `source` and `crit` were constants in
		// every one of the 768 exported battles and nothing in the data said so. Counters, always emitted.
		sb.Append(",\"hitDetail\":{\"produced\":").Append(Aggregator.Rt.HitDetailProduced)
		  .Append(",\"trimmed\":").Append(Aggregator.Rt.HitDetailTrimmed)
		  .Append(",\"errors\":").Append(Aggregator.Rt.HitDetailErrors)
		  .Append(",\"matchExact\":").Append(Aggregator.Rt.HitMatchExact)
		  .Append(",\"matchPair\":").Append(Aggregator.Rt.HitMatchPair)
		  .Append(",\"matchNone\":").Append(Aggregator.Rt.HitMatchNone)
		  // R62 (A): figures that were paired and then REJECTED because the composition contradicted
		  // their hit type. Its own counter: "nothing was available" (matchNone) and "the available
		  // figure belonged to another hit" need different fixes, and only the second means the row
		  // would otherwise have carried another hit's source / hit type / effect id.
		  .Append(",\"matchRejected\":").Append(Aggregator.Rt.HitMatchRejected)
		  .Append('}');
		// 1.5.0 (B4): the full-resolution status/resistance change timeline, with its own legend and
		// counters. Always emitted, so "nothing changed" cannot be confused with "the channel never ran".
		StateTimeline.AppendJson(sb);
		// 1.5.4 (贡献归因 C): madness applications with both unit names (direction settles by data).
		StatusApplierProbe.AppendJson(sb);
		// 1.5.5 (贡献归因 A): give applications + the target->giver map counters.
		GiveApplierProbe.AppendJson(sb);
		// 1.5.0 (B1): the deduplicated fact table every hit points into, plus its measured coverage.
		FactStore.AppendJson(sb);
		int covFact, covLive, covDmg;
		FactStore.Coverage(s, out covFact, out covLive, out covDmg);
		sb.Append(",\"factCoverage\":{\"dmg\":").Append(covDmg)
		  .Append(",\"withFact\":").Append(covFact)
		  .Append(",\"withLiveState\":").Append(covLive)
		  .Append('}');

		// Per-hit forensics (1.4.0): a bounded specimen set of the UNEXPLAINED hits, each with the live
		// status state of both sides. Kept next to `reconcile` because it is the worklist behind that
		// KPI's `topResidual` -- the residual names WHICH multiplier is missing, these records show what
		// was actually on the two units at that instant.
		Forensics.AppendJson(sb);

		// unattributed by victim
		sb.Append(",\"unattributedByVictim\":{");
		bool uf = true;
		if (s.UnattributedByVictim != null)
		{
			foreach (var kv in s.UnattributedByVictim)
			{
				if (!uf) sb.Append(',');
				uf = false;
				sb.Append('"').Append(Escape(kv.Key)).Append("\":").Append(kv.Value);
			}
		}
		// status-source audit: every "status appeared on this victim" transition and whether a damage
		// record's infliction explains it. unexplained[] is the answer to "does every stretch of the
		// status have a precise source?" -- an entry there means the OBSERVATION missed it, not the game.
		sb.Append("},\"statusAudit\":{\"appearances\":").Append(s.StatusTransitions)
			.Append(",\"explained\":").Append(s.StatusExplained)
			// 1.3.7: state the cap, so appearances = explained + unexplained.length + omitted always holds
			.Append(",\"unexplainedCap\":").Append(s.StatusUnexplainedCap)
			.Append(",\"unexplainedOmitted\":").Append(s.StatusUnexplainedOmitted)
			// 1.4.0: WHY explained can be 0. The audit diffs `StatusEnt.Value` (a SUM of BuffValue), while
			// comp4 prints the buff NAME -- so a status whose BuffValue reads 0 lands but cannot be seen as
			// an infliction. Corpus-wide the diff names exactly one status ever (暗闇, 353 records) against
			// 137,947 毒 appearances in comp4, so this is not a corner case. `statusValueSample` is the
			// per-name min..max of BuffValue; `毒=0` next to `暗闇=50` is the answer.
			.Append(",\"sigErrors\":").Append(CompositionProbe.SigErrors)
			.Append(",\"sigErrorMsg\":\"").Append(Escape(CompositionProbe.SigErrorMsg)).Append('"')
			.Append(",\"sigNoList\":").Append(CompositionProbe.SigNoList)
			.Append(",\"statusValueSample\":\"").Append(Escape(CompositionProbe.StatusValueSample())).Append('"')
			.Append(",\"unexplained\":[");
		bool auf = true;
		foreach (string u in s.StatusUnexplained)
		{
			if (!auf) sb.Append(',');
			auf = false;
			sb.Append('"').Append(Escape(u)).Append('"');
		}
		sb.Append("]}");
		// Provenance self-report. Which route labelled the abilities is a MEASURED outcome, so the export
		// states it instead of implying the slots are exact:
		//   routeA 0 = not attempted, 1 = whole-list TryCast worked, 2 = non-generic enumerator worked,
		//          -1 = the game's list could not be read, -2 = it threw.
		sb.Append(",\"rosterAudit\":{\"routeA\":").Append(AbilityRoster.RouteAState)
		  .Append(",\"unitsOk\":").Append(AbilityRoster.RouteAUnitsOk)
		  .Append(",\"unitsFail\":").Append(AbilityRoster.RouteAUnitsFail)
		  .Append(",\"castFails\":").Append(AbilityRoster.RouteACastFails)
		  .Append(",\"slotInvalid\":").Append(AbilityRoster.RouteASlotInvalid)
		  .Append(",\"layoutMismatch\":").Append(AbilityRoster.MismatchedLayout)
		  .Append(",\"layoutSwapped\":").Append(AbilityRoster.LayoutSwapped)
		  .Append(",\"layoutAmbiguous\":").Append(AbilityRoster.AmbiguousLayout)
		  // 1.4.0: the raw evidence behind the layout choice. `layoutAmbiguous` alone cannot tell "our
		  // struct layout is wrong" from "a small index looks like a slot" -- and it fires for 100% of
		  // route-A tuples (== ptrHits+idHits), so the distinction is the whole question.
		  .Append(",\"layoutSample\":\"").Append(Escape(AbilityRoster.LayoutSampleText())).Append('"')
		  .Append(",\"layoutSlots\":\"").Append(Escape(AbilityRoster.LayoutSlotHistogram())).Append('"')
		  .Append(",\"slotConflicts\":").Append(AbilityRoster.SlotConflicts)
		  .Append(",\"statsEntries\":").Append(AbilityRoster.StatsEntries)
		  .Append(",\"statsSample\":\"").Append(Escape(AbilityRoster.StatsSample)).Append('"')
		  .Append(",\"ptrHits\":").Append(AbilityRoster.RouteAHits)
		  .Append(",\"idHits\":").Append(AbilityRoster.RouteAIdHits)
		  // 1.4.0: `cJoins` was REMOVED with the dead route it counted (0 in 50/50 exports; its join
		  // key mixed the statistics' slot-local index with AbilityData.Id). `statsEntries`/`statsSample`
		  // stay -- "is that list populated?" is still a useful measurement.
		  .Append(",\"unlabelled\":").Append(AbilityRoster.Unlabelled)
		  .Append(",\"rosterErrors\":").Append(AbilityRoster.Errors)
		  .Append(",\"triggerReads\":").Append(TalentRuntime.Reads)
		  .Append(",\"triggerErrors\":").Append(TalentRuntime.Errors)
		  // Reads of the VICTIM's blocking state per hit (1.3.4). Reported so that a silent failure of
		  // this measurement is visible instead of looking like "nothing was ever blocking".
		  .Append(",\"blockReads\":").Append(CompositionProbe.BlockReads)
		  .Append(",\"blockErrors\":").Append(CompositionProbe.BlockErrors)
		  // Granted-talent channel (1.3.5): the reads, the failures, how many entries were seen and how
		  // many carried a usable modifier, plus the `give` histogram that settles what that flag means
		  // (the decompiled interop exposes the field but not the logic that sets it). Every count is
		  // reported so a wrong guess shows up as a number rather than as a silently missing factor.
		  .Append(",\"giveReads\":").Append(CompositionProbe.GivenReads)
		  .Append(",\"giveErrors\":").Append(CompositionProbe.GivenErrors)
		  .Append(",\"giveHits\":").Append(CompositionProbe.GivenCount)
		  .Append(",\"giveApplied\":").Append(CompositionProbe.GivenApplied)
		  // 1.7.9: the hit-level half of the same fact. Before this the hit-level increment was added into
		  // giveApplied itself, which made that field entries+hits (measured on all 29 exports, 28/28 non-zero exact).
		  .Append(",\"giveFoldHits\":").Append(CompositionProbe.GivenFoldHits)
		  .Append(",\"giveUnusable\":").Append(CompositionProbe.GivenUnusable)
		  .Append(",\"giveOverflow\":").Append(CompositionProbe.GivenOverflow)
		  .Append(",\"giveFlagTrue\":").Append(CompositionProbe.GivenActive)
		  .Append(",\"giveFlagFalse\":").Append(CompositionProbe.GivenPassive)
		  .Append(",\"giveClausesDeferred\":").Append(CompositionProbe.GivenClauseDeferred)
		  // 1.3.6: granted entries dropped because the global-rule path already counted them. This is
		  // the number that tells whether the double count is really gone, so it is exported explicitly
		  // rather than inferred from a KPI that could also move for other reasons.
		  .Append(",\"giveCancelled\":").Append(CompositionProbe.GivenCancelled)
		  // 1.5.4 (贡献归因 A): giver-resolution self-report -- resolved+null+errors covers every
		  // surviving wantType entry, so a broken ownerAction read shows up as a number.
		  .Append(",\"giverResolved\":").Append(CompositionProbe.GiverResolved)
		  .Append(",\"giverNull\":").Append(CompositionProbe.GiverNull)
		  .Append(",\"giverErrors\":").Append(CompositionProbe.GiverErrors)

		  .Append(",\"giveTypes\":\"").Append(Escape(CompositionProbe.GivenTypeHistogram())).Append('"')
		  // 1.5.0 (B2): the structured twin of the line above is emitted as its own root key
		  // (`giveTypeList`) right after this block, so it can carry an array without interrupting the
		  // chain of `.Append` calls here.
		  // 1.3.7 status resistances: a compact copy of the [RESIST] lines, plus the read counters, so
		  // "the boss resists poison by N" is answerable from the export without the runtime log.
		  .Append(",\"resistUnits\":").Append(CompositionProbe.ResistUnits)
		  .Append(",\"resistReads\":").Append(CompositionProbe.ResistReads)
		  .Append(",\"resistErrors\":").Append(CompositionProbe.ResistErrors)
		  .Append(",\"resistNull\":").Append(CompositionProbe.ResistNull)
		  .Append(",\"resistSamples\":").Append(CompositionProbe.ResistSamples)
		  // 1.3.9 NAMING: these two are the MASTER/template slots, NOT the effective in-battle resistance.
		  // Measured 2026-10-03 (battle_...141816): 952 throttled re-reads over one battle with zero
		  // movement while 5,120 毒耐性-30 grants were landing on the same unit -- so the field is
		  // immutable and the moving half is in `subParamSample` below. The names say which is which.
		  .Append(",\"resistMasterSample\":\"").Append(Escape(CompositionProbe.ResistSample())).Append('"')
		  .Append(",\"resistMasterLastSample\":\"").Append(Escape(CompositionProbe.ResistLastSample())).Append('"')
		  // 1.3.9: the live stat sub-params (`BattleObject.m_statusSubParams`), i.e. the part that moves.
		  .Append(",\"subParamReads\":").Append(CompositionProbe.SubParamReads)
		  .Append(",\"subParamErrors\":").Append(CompositionProbe.SubParamErrors)
		  .Append(",\"subParamSample\":\"").Append(Escape(CompositionProbe.SubParamSample())).Append('"')
		  // 1.3.10: the applied-param dictionary, read LATE and per unit. Last remaining candidate for
		  // where a live (mid-battle) resistance modifier actually lives.
		  .Append(",\"paramReads\":").Append(CompositionProbe.ParamReads)
		  .Append(",\"paramErrors\":").Append(CompositionProbe.ParamErrors)
		  .Append(",\"paramSample\":\"").Append(Escape(CompositionProbe.ParamSample())).Append('"')
		  // 1.4.0: the LIVE status state (`Character.Status` -> `CharaStatus` -> `.Resistance`), which is
		  // the value the game actually consults. Every earlier field in this block reads a TEMPLATE that
		  // measured zero movement across a whole battle while thousands of resistance debuffs landed, so
		  // the names here say "live" explicitly rather than repeating the ambiguity that cost 1.3.7-1.3.10.
		  .Append(",\"ailmentThreshold\":").Append(UnitStateProbe.ThresholdRead ? UnitStateProbe.Threshold.ToString() : "null")
		  .Append(",\"liveReads\":").Append(UnitStateProbe.Reads)
		  .Append(",\"liveErrors\":").Append(UnitStateProbe.Errors)
		  .Append(",\"liveNotCharacter\":").Append(UnitStateProbe.NotCharacter)
		  .Append(",\"liveNullStatus\":").Append(UnitStateProbe.NullStatus)
		  .Append(",\"liveNullResistance\":").Append(UnitStateProbe.NullResistance)
		  .Append(",\"liveSamples\":").Append(UnitStateProbe.Samples)
		  .Append(",\"liveResistFirst\":\"").Append(Escape(UnitStateProbe.FirstSample())).Append('"')
		  .Append(",\"liveResistLast\":\"").Append(Escape(UnitStateProbe.LastSample())).Append('"')
		  // 1.4.1: FIRST/LAST alone could not see the boss's in-battle resistance movement -- the
		  // debuffs land and then EXPIRE, so both endpoints read the template 100 while the middle of
		  // the battle reads 毒=25 / 火傷=55. Export the per-slot min..max so "did it move?" is answered
		  // by a range instead of by two samples. Empty = nothing moved (a result, not a failure).
		  .Append(",\"liveRangeSamples\":").Append(UnitStateProbe.RangeSamples)
		  .Append(",\"liveResistRange\":\"").Append(Escape(UnitStateProbe.RangeSample())).Append('"')
		  .Append('}');
		// 1.5.0 (B2): the structured twin of `rosterAudit.giveTypes`. Emitted as its own root key so the
		// histogram can be an ARRAY of {"type","param","n"} instead of a "1006/-15:2" string that every
		// script re-splits. The old key stays: 768 historical exports only have the string form.
		sb.Append(",\"giveTypeList\":");
		CompositionProbe.AppendGivenTypesJson(sb);
		// 1.5.0 (B2): the structured twin of the master/template resistance samples. The numeric form is
		// what makes "this field is a template and never moves" a COMPARISON rather than a re-parse:
		// `resistMaster.first` vs `.last` is exactly the claim the 1.3.9/1.4.0 saga rested on.
		sb.Append(",\"resistMaster\":");
		CompositionProbe.AppendResistJson(sb);
		// 1.5.0 (B2): structured twins of `statusValueSample` and the layout evidence. Both stay as
		// strings above for the 768 exports that only have the text form.
		sb.Append(",\"statusValues\":");
		CompositionProbe.AppendStatusValuesJson(sb);
		sb.Append(",\"layout\":");
		AbilityRoster.AppendLayoutJson(sb);
		// 1.5.0 (B2): structured twin of `rosterAudit.liveResistRange` -- the per-slot min..max that the
		// 1.4.1 resistance conclusion rests on, as numbers with a slot legend.
		sb.Append(",\"liveRange\":");
		UnitStateProbe.AppendRangeJson(sb);
		// 1.5.0 (B2): structured twin of `rosterAudit.subParamSample` -- the entries keyed by the SAME
		// eBuffType codes as `giveTypeList`, so the two can be joined as numbers.
		sb.Append(",\"subParams\":");
		CompositionProbe.AppendSubParamJson(sb);
		// 1.5.0 (B2): the last two structured twins -- `paramSample` (the applied-param dictionary, the
		// last candidate location for a mid-battle resistance modifier) and `statsSample` (the statistics
		// list, whose slot-local index vs AbilityData.Id mismatch is the reason route C was deleted).
		sb.Append(",\"params\":");
		CompositionProbe.AppendParamJson(sb);
		// 1.7.2 (阶段 G): WHO owns each applied parameter -- the missing input for attributing 攻击力
		// additions granted by a teammate. Purely additive; the section above is byte-identical.
		sb.Append(",\"paramOwners\":");
		ParamOwnerProbe.AppendJson(sb);

		// 1.7.4 (阶段 G): the counters of the per-hit attack-power attribution. They are the audit of the
		// REFUSALS -- every value the model declined to charge to a giver is a number here, so "the section
		// gives nobody credit" and "the guards refused everything" can never look alike.
		sb.Append(",\"atkAdd\":{\"hits\":").Append(AtkAddFold.Hits)
		  .Append(",\"emitted\":").Append(AtkAddFold.Emitted)
		  .Append(",\"selfValues\":").Append(AtkAddFold.SelfValues)
		  .Append(",\"skippedGuard\":").Append(AtkAddFold.SkippedGuard)
		  .Append(",\"skippedCollision\":").Append(AtkAddFold.SkippedCollision)
		  .Append(",\"skippedUnowned\":").Append(AtkAddFold.SkippedUnowned)
		  .Append(",\"skippedOwnerNull\":").Append(AtkAddFold.SkippedOwnerNull)
		  .Append(",\"skippedNegative\":").Append(AtkAddFold.SkippedNegative)
		  .Append(",\"skippedType\":").Append(AtkAddFold.SkippedType)
		  // 1.7.8 (P1-A): WHY a value was treated as self. The old test was name equality, so a teammate
		  // sharing the attacker name had its addend silently dropped into baseCredit. The actor KEY now
		  // decides and the name is only a counted fallback; the identity below pins that the two paths
		  // partition the self values: selfValues == selfByKey + selfByNameFallback.
		  .Append(",\"selfByKey\":").Append(AtkAddFold.SelfByKey)
		  .Append(",\"selfByNameFallback\":").Append(AtkAddFold.SelfByNameFallback)
		  .Append(",\"nameCollision\":").Append(AtkAddFold.NameCollision)
		  .Append(",\"ownerUnknown\":").Append(AtkAddFold.OwnerUnknown)
		  .Append('}');
		sb.Append(",\"statsRows\":");
		AbilityRoster.AppendStatsJson(sb);
		// 1.6.0 (阶段 E): the contribution section. Derived here, from the event list, exactly like
		// CalcReconcile -- never accumulated during the battle. Gated on the SAME predicate that gated
		// calc.fold (ReconcileCalc), because the attribution reads those folds: if the file has no folds,
		// the section must not pretend otherwise (the offline crosscheck compares them field by field).
		if (Plugin.CfgContribution != null && Plugin.CfgContribution.Value)
		{
			sb.Append(",\"contribution\":");
			try
			{
				bool useFolds = Plugin.CfgReconcileCalc != null && Plugin.CfgReconcileCalc.Value;
				ContributionSession.AppendJson(sb, s, useFolds, s.QuestId == 9999);
			}
			catch (System.Exception ex)
			{
				// A contribution bug must NEVER cost the battle its export: the whole point of the
				// section is the data, and the data is already in the file. Degrade to a section that
				// says it failed -- deliberately WITHOUT the mandatory fields, so the schema guard and
				// the offline tools fail loudly instead of reading zeros as measurements.
				sb.Append("{\"schemaVersion\":\"1.0\",\"method\":\"log-share/1\",\"damageBasis\":\"dealt\",\"error\":\"")
				  .Append(Escape(ex.GetType().Name + ": " + ex.Message))
				  .Append("\"}");
			}
		}
		sb.Append('}');
		return sb.ToString();
	}

	/// <summary>Per-unit ability roster with provenance: slot, id, level, name and every talent's decoded
	/// trigger condition. This is what makes offline analysis self-contained -- previously nothing in the
	/// export said what a unit actually had equipped.</summary>
	private static void AppendRoster(StringBuilder sb, ActorStats a)
	{
		sb.Append(",\"abilities\":[");
		bool first = true;
		if (a.Roster != null)
		{
			foreach (RosterAbility r in a.Roster)
			{
				if (!first) sb.Append(',');
				first = false;
				sb.Append("{\"slot\":").Append(r.Slot)
				  .Append(",\"slotName\":\"").Append(Escape(AbilityRoster.SlotLabel(r.Slot))).Append('"')
				  .Append(",\"origin\":").Append(r.Origin)
				  .Append(",\"id\":").Append(r.Id)
				  .Append(",\"level\":").Append(r.Level)
				  .Append(",\"name\":\"").Append(Escape(r.Name)).Append('"');
				// Provenance of the name: 1 means it came from the master tables because the ability had
				// none of its own (currently only 刻印), so a resolved official name is never mistaken for
				// one the game supplied. See MasterData/MasterDataNames.cs.
				if (r.NameFromMaster) sb.Append(",\"nameFrom\":\"master\"");
				sb.Append(",\"talents\":[");
				bool tf = true;
				foreach (TalentRef t in r.Talents)
				{
					if (!tf) sb.Append(',');
					tf = false;
					sb.Append("{\"i\":").Append(t.Index)
					  .Append(",\"type\":").Append(t.Type)
					  .Append(",\"typeName\":\"").Append(Escape(AbilityRoster.TypeLabel(t.Type))).Append('"')
					  .Append(",\"timing\":\"").Append(Escape(AbilityRoster.TimingLabel(t.Timing))).Append('"')
					  .Append(",\"p\":[").Append(t.P0).Append(',').Append(t.P1).Append(',').Append(t.P2).Append(']');
					if (!string.IsNullOrEmpty(t.Cond))
						sb.Append(",\"cond\":\"").Append(Escape(t.Cond)).Append('"');
					sb.Append('}');
				}
				sb.Append("]}");
			}
		}
		sb.Append(']');
	}

	/// <summary>Battle-end talent table: only talents that did something. Carries all three counter
	/// candidates (delta = what WE observed, prop = TotalActivateCount, agg = m_statistics.ActivateCount)
	/// so the next battle can confirm which one is the battle-scoped count rather than assuming it.</summary>
	private static void AppendTalentTable(StringBuilder sb, ActorStats a)
	{
		sb.Append(",\"talents\":[");
		bool first = true;
		if (a.TalentTable != null)
		{
			foreach (TalentUsage u in a.TalentTable)
			{
				if (!first) sb.Append(',');
				first = false;
				sb.Append("{\"slot\":").Append(u.Slot)
				  .Append(",\"slotName\":\"").Append(Escape(AbilityRoster.SlotLabel(u.Slot))).Append('"')
				  .Append(",\"abilityId\":").Append(u.AbilityId)
				  .Append(",\"ability\":\"").Append(Escape(u.Ability)).Append('"')
				  .Append(",\"i\":").Append(u.Index)
				  .Append(",\"type\":").Append(u.Type)
				  .Append(",\"typeName\":\"").Append(Escape(AbilityRoster.TypeLabel(u.Type))).Append('"')
				  .Append(",\"p\":[").Append(u.P0).Append(',').Append(u.P1).Append(',').Append(u.P2).Append(']')
				  .Append(",\"delta\":").Append(u.Delta)
				  .Append(",\"prop\":").Append(u.Prop)
				  .Append(",\"agg\":").Append(u.Agg)
				  .Append('}');
			}
		}
		sb.Append(']');
	}

	private static void AppendLongArray(StringBuilder sb, ActorStats a, int end, int which)
	{
		for (int s = 0; s <= end; s++)
		{
			if (s > 0) sb.Append(',');
			long v = which == 0 ? a.GetSecondDamage(s) : which == 1 ? a.GetSecondTaken(s) : a.GetSecondHeal(s);
			sb.Append(v);
		}
	}

	private static void AppendFloatArray(StringBuilder sb, ActorStats a, int end)
	{
		for (int s = 0; s <= end; s++)
		{
			if (s > 0) sb.Append(',');
			float v = a.GetHpPct(s);
			sb.Append(((int)(v * 10f)).ToString()); // 0.1% precision
		}
	}

	/// <summary>Delegates to <see cref="JsonText.Str"/> -- one escaping rule for the whole plugin.</summary>
	private static string Escape(string t)
	{
		return JsonText.Str(t);
	}
}
