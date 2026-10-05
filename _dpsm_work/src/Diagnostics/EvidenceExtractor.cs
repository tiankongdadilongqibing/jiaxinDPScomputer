using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;

namespace DpsMeter;

/// <summary>
/// R52 (证据提取流程): ONE self-contained evidence bundle, from ONE trigger.
///
/// WHY THIS EXISTS. Every new question about the export ("what is unknown_kind made of?", "why is the
/// giver always null?", "is this residual the same rule in another battle?") used to cost a one-off
/// script over a 24 MB file, and the answer lived only in that script's stdout. The bundle makes the
/// extraction a PRODUCT of the plugin: trigger it (battle end or the configured key) and you get a
/// directory that contains the battle itself, the unresolved-fold census, a copy of the game's own master
/// tables, and a manifest that hashes all of it -- so a claim about it can be checked later without the
/// live game and without guessing which build produced it.
///
/// WHAT IT DELIBERATELY IS NOT. It is not a second computation of anything: the battle comes from
/// ExportService (the same serializer, see ExportTo) and the census from Contribution.Compute (the same
/// core that writes the export's own contribution section). A second path would be a second contract.
///
/// THE CHECKSUM IS FNV-1a 64, AND THE MANIFEST SAYS SO. No cryptography is used on purpose: this project
/// has never linked System.Security.Cryptography into the IL2CPP plugin, and a stripped/absent crypto
/// implementation would fail at runtime inside a battle-end hook. A checksum that is honestly named is
/// worth more than a digest that might not exist. `check` in the manifest carries the algorithm name.
///
/// FAILURE POLICY. Nothing here may throw outward: this runs at battle end (where a throw would abort the
/// finalisation) and on a hotkey inside the render loop. Every failure is counted and written to the
/// runtime log, and a half-written bundle is left on disk ON PURPOSE -- with the manifest missing, which
/// is exactly the signal the offline verifier needs to call it incomplete.
/// </summary>
internal static class EvidenceExtractor
{
	internal const string ManifestSchema = "extract-manifest/1";
	internal const string CensusSchema = "contrib-census/1";
	/// <summary>Stated in the manifest so nobody reads it as a cryptographic digest.</summary>
	internal const string HashName = "fnv1a64";
	internal const int CopyBuffer = 1 << 16;
	private const ulong FnvOffset = 14695981039346656037UL;
	private const ulong FnvPrime = 1099511628211UL;

	private static string _dir;
	private static string _lastDir;

	// ---- self-report (the same discipline as every other probe: a counter, never a swallowed catch) ----
	internal static int Runs;
	internal static int Failures;
	internal static int LastCensusGroups;
	internal static int LastCensusFolds;
	internal static double LastCensusAmount;
	internal static int LastCarrierUnique;
	internal static int LastCarrierAmbiguous;
	internal static int LastCarrierNone;
	internal static int LastMasterdataFiles;
	internal static int LastMasterdataFailures;
	internal static int RetentionDeleted;
	internal static string LastError;

	/// <summary>&lt;plugin&gt;/extract -- the bundle root. Null only when even the fallback path failed.</summary>
	internal static string Dir
	{
		get
		{
			if (_dir == null)
			{
				try { _dir = Path.Combine(Paths.PluginPath, "DpsMeter", "extract"); }
				catch { try { _dir = Path.Combine(AppContext.BaseDirectory, "extract"); } catch { _dir = null; } }
				try { if (_dir != null) Directory.CreateDirectory(_dir); } catch { }
			}
			return _dir;
		}
	}

	internal static string LastDir { get { return _lastDir; } }

	/// <summary>Write one bundle. Returns its directory (null when nothing could be written).
	/// Never throws: see the failure policy in the class comment.</summary>
	internal static string Run(BattleSession s, string reason)
	{
		string dir = null;
		try
		{
			Runs++;
			if (s == null)
			{
				LastError = "no session";
				RuntimeLog.Write("[DpsMeter][EXTRACT] 跳过:没有战斗会话");
				return null;
			}
			string baseDir = Dir;
			if (baseDir == null)
			{
				Failures++;
				LastError = "no directory";
				RuntimeLog.Write("[DpsMeter][EXTRACT] 失败:无法建立提取目录");
				return null;
			}
			DateTime stamp = DateTime.Now;
			dir = Path.Combine(baseDir, ExtractPolicy.BundleName(stamp, s.QuestId, reason));
			Directory.CreateDirectory(dir);

			var names = new List<string>();
			// 1) the battle, through the serializer the normal export uses
			string battle = Path.Combine(dir, "battle.json");
			ExportService.ExportTo(s, battle);
			if (File.Exists(battle)) names.Add("battle.json");
			// 2) the census, through the core that writes the export's contribution section
			bool useFolds = Plugin.CfgReconcileCalc != null && Plugin.CfgReconcileCalc.Value;
			ContributionResult res = ContributionSession.Compute(s, useFolds);
			Summarise(res);
			string census = BuildCensus(s, res, reason, stamp, useFolds);
			names.Add("contrib_census.json");
			WriteChecked(Path.Combine(dir, "contrib_census.json"), census, "contrib_census");
			// 3) the game's own tables, copied (they are dumped once per process, at battle end)
			int mdFiles = 0;
			long mdBytes = 0;
			CopyMasterData(dir, out mdFiles, out mdBytes);
			// 4) the manifest LAST, so it lists and hashes everything above it
			string manifest = BuildManifest(s, reason, stamp, dir, names, mdFiles, mdBytes);
			names.Add("manifest.json");
			WriteChecked(Path.Combine(dir, "manifest.json"), manifest, "manifest");
			// 5) retention
			DeleteStale(baseDir);

			_lastDir = dir;
			string line = "[DpsMeter][EXTRACT] " + reason + " -> " + dir
				+ " 文件=" + names.Count.ToString(CultureInfo.InvariantCulture)
				+ " 主数据=" + mdFiles.ToString(CultureInfo.InvariantCulture)
				+ " 未归因组=" + LastCensusGroups.ToString(CultureInfo.InvariantCulture)
				+ " 折叠=" + LastCensusFolds.ToString(CultureInfo.InvariantCulture)
				+ " 金额=" + LastCensusAmount.ToString("F4", CultureInfo.InvariantCulture)
				+ " 候选(唯一/歧义/无)=" + LastCarrierUnique + "/" + LastCarrierAmbiguous + "/" + LastCarrierNone;
			Plugin.LogSource.LogInfo(line);
			RuntimeLog.Write(line);
			return dir;
		}
		catch (Exception ex)
		{
			Failures++;
			LastError = ex.Message;
			try { RuntimeLog.Write("[DpsMeter][EXTRACT] 失败(" + reason + "): " + ex.Message); } catch { }
			return dir;
		}
	}

	/// <summary>One-line self-report for the finalisation log, printed even when extraction is off.</summary>
	internal static string Diag()
	{
		return "运行=" + Runs.ToString(CultureInfo.InvariantCulture)
			+ " 失败=" + Failures.ToString(CultureInfo.InvariantCulture)
			+ " 最近=" + (LastDir == null ? "-" : Path.GetFileName(LastDir))
			+ " 未归因组=" + LastCensusGroups.ToString(CultureInfo.InvariantCulture)
			+ " 折叠=" + LastCensusFolds.ToString(CultureInfo.InvariantCulture)
			+ " 候选(唯一/歧义/无)=" + LastCarrierUnique + "/" + LastCarrierAmbiguous + "/" + LastCarrierNone
			+ " 保留删除=" + RetentionDeleted.ToString(CultureInfo.InvariantCulture)
			+ (LastError == null ? "" : (" 最近错误=" + LastError));
	}

	// ---------------------------------------------------------------------------------------------

	private static void Summarise(ContributionResult res)
	{
		LastCensusGroups = 0;
		LastCensusFolds = 0;
		LastCensusAmount = 0.0;
		LastCarrierUnique = LastCarrierAmbiguous = LastCarrierNone = 0;
		if (res == null) return;
		LastCensusGroups = res.Unresolved.Count;
		for (int i = 0; i < res.Unresolved.Count; i++)
		{
			ContributionUnresolvedRow r = res.Unresolved[i];
			LastCensusFolds += r.Folds;
			LastCensusAmount += r.Amount;
			if (r.CarrierVerdict == "unique") LastCarrierUnique++;
			else if (r.CarrierVerdict == "ambiguous") LastCarrierAmbiguous++;
			else if (r.CarrierVerdict == "none") LastCarrierNone++;
		}
	}

	private static string Num(double v) { return v.ToString("F4", CultureInfo.InvariantCulture); }
	private static string NumOrNull(double v)
	{
		if (double.IsNaN(v) || double.IsInfinity(v)) return "null";
		return v.ToString("F4", CultureInfo.InvariantCulture);
	}
	private static string Num6(double v)
	{
		if (double.IsNaN(v) || double.IsInfinity(v)) return "null";
		return v.ToString("F6", CultureInfo.InvariantCulture);
	}

	/// <summary>The unresolved-fold census. Field names mirror ContributionUnresolvedRow 1:1 on purpose,
	/// so a reader of the bundle and a reader of the C# class cannot disagree about what a field means.</summary>
	private static string BuildCensus(BattleSession s, ContributionResult res, string reason, DateTime stamp, bool useFolds)
	{
		var sb = new StringBuilder(4096);
		sb.Append('{');
		sb.Append("\"schema\":\"").Append(CensusSchema).Append('"');
		sb.Append(",\"reason\":\"").Append(JsonText.Str(reason)).Append('"');
		sb.Append(",\"createdAt\":\"").Append(stamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append('"');
		sb.Append(",\"pluginVersion\":\"").Append(JsonText.Str(BuildInfo.Version)).Append('"');
		sb.Append(",\"quest\":").Append(s.QuestId.ToString(CultureInfo.InvariantCulture));
		sb.Append(",\"seconds\":").Append(s.ActiveSeconds.ToString("F2", CultureInfo.InvariantCulture));
		sb.Append(",\"foldsEnabled\":").Append(useFolds ? "true" : "false");
		ContributionStats st = res == null ? null : res.Stats;
		if (st == null)
		{
			sb.Append(",\"usable\":false,\"foldAccounting\":{\"total\":0},\"coverage\":{},\"reasons\":[],\"unresolved\":[]}");
			return sb.ToString();
		}
		sb.Append(",\"usable\":true");
		sb.Append(",\"foldAccounting\":{\"total\":").Append(st.Folds.ToString(CultureInfo.InvariantCulture))
		  .Append(",\"zeroFactor\":").Append(st.ZeroFactor.ToString(CultureInfo.InvariantCulture))
		  .Append(",\"noopFactor\":").Append(st.NoopFactor.ToString(CultureInfo.InvariantCulture))
		  .Append(",\"subUnity\":").Append(st.SubUnity.ToString(CultureInfo.InvariantCulture))
		  .Append(",\"negative\":").Append(st.Negative.ToString(CultureInfo.InvariantCulture))
		  .Append(",\"analysisHits\":").Append(st.Hits.ToString(CultureInfo.InvariantCulture))
		  .Append(",\"events\":").Append(st.Events.ToString(CultureInfo.InvariantCulture)).Append('}');
		sb.Append(",\"coverage\":{\"analyzable\":").Append(Num(st.Analyzable))
		  .Append(",\"attributed\":").Append(Num(st.Attributed))
		  .Append(",\"unattributed\":").Append(Num(st.Unattributed))
		  .Append(",\"creditedShare\":").Append(Num6(st.CreditedShare))
		  .Append(",\"reconciliationGap\":").Append(NumOrNull(st.ReconciliationGap))
		  .Append(",\"unknownAttackerHits\":").Append(st.UnknownAttackerHits.ToString(CultureInfo.InvariantCulture))
		  .Append(",\"outsideTeamHits\":").Append(st.OutsideTeamHits.ToString(CultureInfo.InvariantCulture))
		  .Append('}');
		// reason counts, ordered deterministically (folds desc, then name) so two runs cannot differ
		var order = new List<KeyValuePair<string, int>>(st.ReasonCounts);
		order.Sort((x, y) => x.Value != y.Value ? y.Value - x.Value : string.CompareOrdinal(x.Key, y.Key));
		sb.Append(",\"reasons\":[");
		for (int i = 0; i < order.Count; i++)
		{
			if (i > 0) sb.Append(',');
			sb.Append("{\"reason\":\"").Append(JsonText.Str(order[i].Key)).Append("\",\"folds\":")
			  .Append(order[i].Value.ToString(CultureInfo.InvariantCulture)).Append('}');
		}
		sb.Append(']');
		sb.Append(",\"unresolved\":[");
		for (int i = 0; i < res.Unresolved.Count; i++)
		{
			ContributionUnresolvedRow r = res.Unresolved[i];
			if (i > 0) sb.Append(',');
			sb.Append("{\"reason\":\"").Append(JsonText.Str(r.Reason))
			  .Append("\",\"kind\":\"").Append(JsonText.Str(r.Kind))
			  .Append("\",\"side\":\"").Append(JsonText.Str(r.Side))
			  .Append("\",\"origin\":\"").Append(JsonText.Str(r.Origin))
			  .Append("\",\"label\":\"").Append(JsonText.Str(r.Label))
			  .Append("\",\"ruleName\":\"").Append(JsonText.Str(r.RuleName))
			  .Append("\",\"factor\":").Append(r.Factor.ToString("R", CultureInfo.InvariantCulture))
			  .Append(",\"folds\":").Append(r.Folds.ToString(CultureInfo.InvariantCulture))
			  .Append(",\"amount\":").Append(Num(r.Amount))
			  .Append(",\"victimInstances\":").Append(r.VictimInstances.ToString(CultureInfo.InvariantCulture))
			  .Append(",\"victimTop\":\"").Append(JsonText.Str(r.VictimTop))
			  .Append("\",\"victimTopFolds\":").Append(r.VictimTopFolds.ToString(CultureInfo.InvariantCulture))
			  .Append(",\"carrierVerdict\":\"").Append(JsonText.Str(r.CarrierVerdict))
			  .Append("\",\"carrierCount\":").Append(r.CarrierCount.ToString(CultureInfo.InvariantCulture))
			  .Append(",\"carrierNames\":\"").Append(JsonText.Str(r.CarrierNames)).Append("\"}");
		}
		sb.Append("]}");
		return sb.ToString();
	}

	private static string BuildManifest(BattleSession s, string reason, DateTime stamp, string dir,
		List<string> names, int mdFiles, long mdBytes)
	{
		var sb = new StringBuilder(2048);
		sb.Append('{');
		sb.Append("\"schema\":\"").Append(ManifestSchema).Append('"');
		sb.Append(",\"check\":\"").Append(HashName).Append('"');
		sb.Append(",\"reason\":\"").Append(JsonText.Str(reason)).Append('"');
		sb.Append(",\"createdAt\":\"").Append(stamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append('"');
		sb.Append(",\"pluginVersion\":\"").Append(JsonText.Str(BuildInfo.Version)).Append('"');
		sb.Append(",\"quest\":").Append(s.QuestId.ToString(CultureInfo.InvariantCulture));
		sb.Append(",\"seconds\":").Append(s.ActiveSeconds.ToString("F2", CultureInfo.InvariantCulture));
		sb.Append(",\"bundle\":\"").Append(JsonText.Str(Path.GetFileName(dir))).Append('"');
		// the deployed assembly: without this a bundle cannot be tied to the build that produced it
		string asm = null;
		try { asm = typeof(EvidenceExtractor).Assembly.Location; } catch { asm = null; }
		if (string.IsNullOrEmpty(asm))
		{
			try { asm = Path.Combine(Paths.PluginPath, "DpsMeter", "DpsMeter.dll"); } catch { asm = null; }
		}
		sb.Append(",\"assembly\":{\"path\":").Append(asm == null ? "null" : ("\"" + JsonText.Str(asm) + "\""));
		if (asm != null && File.Exists(asm))
			sb.Append(",\"bytes\":").Append(new FileInfo(asm).Length.ToString(CultureInfo.InvariantCulture))
			  .Append(",\"check\":\"").Append(Hash64Hex(asm)).Append('"');
		sb.Append('}');
		sb.Append(",\"files\":[");
		for (int i = 0; i < names.Count; i++)
		{
			string p = Path.Combine(dir, names[i]);
			if (i > 0) sb.Append(',');
			sb.Append("{\"name\":\"").Append(JsonText.Str(names[i])).Append('"');
			if (File.Exists(p))
				sb.Append(",\"bytes\":").Append(new FileInfo(p).Length.ToString(CultureInfo.InvariantCulture))
				  .Append(",\"check\":\"").Append(Hash64Hex(p)).Append('"');
			else sb.Append(",\"bytes\":0,\"missing\":true");
			sb.Append('}');
		}
		sb.Append(']');
		sb.Append(",\"masterdata\":{\"files\":").Append(mdFiles.ToString(CultureInfo.InvariantCulture))
		  .Append(",\"bytes\":").Append(mdBytes.ToString(CultureInfo.InvariantCulture))
		  .Append(",\"failures\":").Append(LastMasterdataFailures.ToString(CultureInfo.InvariantCulture));
		string mdSrc = null;
		try { mdSrc = Path.Combine(Paths.PluginPath, "DpsMeter", "masterdata"); } catch { mdSrc = null; }
		sb.Append(",\"source\":").Append(mdSrc == null ? "null" : ("\"" + JsonText.Str(mdSrc) + "\"")).Append('}');
		sb.Append(",\"census\":{\"groups\":").Append(LastCensusGroups.ToString(CultureInfo.InvariantCulture))
		  .Append(",\"folds\":").Append(LastCensusFolds.ToString(CultureInfo.InvariantCulture))
		  .Append(",\"amount\":").Append(Num(LastCensusAmount))
		  .Append(",\"carrierUnique\":").Append(LastCarrierUnique.ToString(CultureInfo.InvariantCulture))
		  .Append(",\"carrierAmbiguous\":").Append(LastCarrierAmbiguous.ToString(CultureInfo.InvariantCulture))
		  .Append(",\"carrierNone\":").Append(LastCarrierNone.ToString(CultureInfo.InvariantCulture)).Append('}');
		sb.Append(",\"notes\":[");
		sb.Append("\"battle.json is written by the same serializer as the normal export\"");
		sb.Append(",\"contrib_census.json is computed by the same core that writes the export contribution section\"");
		sb.Append(",\"masterdata is a COPY of the dump made at battle end; absent means it was never produced\"");
		sb.Append(",\"carrierVerdict is an inference from the roster loadout, not a measured runtime read\"");
		sb.Append("]}");
		return sb.ToString();
	}

	private static void WriteChecked(string path, string json, string label)
	{
		string err;
		int dup;
		bool ok = JsonCheck.Validate(json, out err, out dup);
		File.WriteAllText(path, json, new UTF8Encoding(false));
		string line = "[DpsMeter][EXTRACT] " + label + " 结构=" + (ok ? "OK" : ("非法:" + err))
			+ " 根键重复=" + dup.ToString(CultureInfo.InvariantCulture)
			+ " 字符=" + json.Length.ToString(CultureInfo.InvariantCulture);
		if (!ok) Plugin.LogSource.LogWarning(line); else Plugin.LogSource.LogInfo(line);
		RuntimeLog.Write(line);
	}

	private static void CopyMasterData(string dir, out int files, out long bytes)
	{
		files = 0;
		bytes = 0;
		LastMasterdataFiles = 0;
		LastMasterdataFailures = 0;
		try
		{
			string src = Path.Combine(Paths.PluginPath, "DpsMeter", "masterdata");
			if (!Directory.Exists(src)) return;
			string dst = Path.Combine(dir, "masterdata");
			Directory.CreateDirectory(dst);
			string[] list = Directory.GetFiles(src, "*.json");
			for (int i = 0; i < list.Length; i++)
			{
				try
				{
					string target = Path.Combine(dst, Path.GetFileName(list[i]));
					File.Copy(list[i], target, true);
					files++;
					try { bytes += new FileInfo(target).Length; } catch { }
				}
				catch { LastMasterdataFailures++; }
			}
			LastMasterdataFiles = files;
		}
		catch { LastMasterdataFailures++; }
	}

	/// <summary>Keep at most General/ExtractKeep bundles. The decision is a pure string sort
	/// (Policy/ExtractPolicy.StaleBundles), so it is testable and cannot depend on file times.</summary>
	private static void DeleteStale(string baseDir)
	{
		RetentionDeleted = 0;
		try
		{
			string[] dirs = Directory.GetDirectories(baseDir);
			var names = new List<string>(dirs.Length);
			for (int i = 0; i < dirs.Length; i++)
			{
				string n = Path.GetFileName(dirs[i]);
				if (n != null && n.StartsWith("extract_", StringComparison.Ordinal)) names.Add(n);
			}
			int keep = Plugin.CfgExtractKeep == null ? ExtractPolicy.DefaultKeep : Plugin.CfgExtractKeep.Value;
			List<string> stale = ExtractPolicy.StaleBundles(names, keep);
			for (int i = 0; i < stale.Count; i++)
			{
				try
				{
					Directory.Delete(Path.Combine(baseDir, stale[i]), true);
					RetentionDeleted++;
				}
				catch { }
			}
		}
		catch { }
	}

	/// <summary>FNV-1a 64 over the file's bytes, as 16 lowercase hex digits. See the class comment for why
	/// this is not a cryptographic hash.</summary>
	internal static string Hash64Hex(string path)
	{
		ulong h = FnvOffset;
		using (FileStream fs = File.OpenRead(path))
		{
			byte[] buf = new byte[CopyBuffer];
			int n;
			while ((n = fs.Read(buf, 0, buf.Length)) > 0)
			{
				for (int i = 0; i < n; i++)
				{
					h ^= buf[i];
					h *= FnvPrime;
				}
			}
		}
		return h.ToString("x16", CultureInfo.InvariantCulture);
	}
}
