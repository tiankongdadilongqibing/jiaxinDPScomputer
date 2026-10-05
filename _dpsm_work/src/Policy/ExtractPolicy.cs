using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DpsMeter;

/// <summary>
/// R52 (证据提取流程): the PURE decisions of the extraction flow -- trigger key, bundle naming and retention.
///
/// WHY A POLICY CLASS. The same reason Policy/ContributionCachePolicy.cs exists: the decisions that can be
/// wrong silently (which key triggers, how many bundles survive) are exactly the ones that must be
/// exercisable offline by BehaviorTests and by the mutation driver. The I/O half lives in
/// Diagnostics/EvidenceExtractor.cs and is deliberately thin.
///
/// THE KEY IS CONFIGURABLE ON PURPOSE. The overlay already owns F5/F6/F7/F8/F9/F10/F11/F12, and the GAME
/// owns an unknown set; a hard-coded new key would be a permanent conflict risk the user could not move
/// without a rebuild. `NONE`/empty disables the trigger while the battle-end route keeps working.
/// </summary>
internal static class ExtractPolicy
{
	public const string DefaultKey = "F4";
	public const int DefaultKeep = 5;
	public const int MinKeep = 1;
	public const int MaxKeep = 50;

	/// <summary>Config string -> GetAsyncKeyState virtual-key code. 0 means "no key trigger".
	/// The accepted forms are the ones a user can be expected to type and that we can map without a
	/// lookup table that could drift: F1..F12, A..Z, 0..9, plus explicit off words.</summary>
	public static int ParseVirtualKey(string s)
	{
		if (s == null) return 0;
		string t = s.Trim().ToUpperInvariant();
		if (t.Length == 0 || t == "NONE" || t == "OFF" || t == "DISABLED" || t == "0") return 0;
		if (t[0] == 'F' && (t.Length == 2 || t.Length == 3))
		{
			int n;
			if (int.TryParse(t.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)
				&& n >= 1 && n <= 12)
				return 0x70 + (n - 1);          // VK_F1 = 0x70 .. VK_F12 = 0x7B
			return 0;
		}
		if (t.Length == 1)
		{
			char c = t[0];
			if (c >= 'A' && c <= 'Z') return c;              // VK_A..VK_Z are the ASCII codes
			if (c >= '0' && c <= '9') return c - '0' + 0x30; // VK_0..VK_9
		}
		return 0;
	}

	/// <summary>Human-readable form for the startup/log line, so "which key is armed" is never a guess.</summary>
	public static string DescribeKey(int vk)
	{
		if (vk <= 0) return "disabled";
		if (vk >= 0x70 && vk <= 0x7B) return "F" + (vk - 0x70 + 1).ToString(CultureInfo.InvariantCulture)
			+ " (0x" + vk.ToString("X2", CultureInfo.InvariantCulture) + ")";
		if (vk >= 0x30 && vk <= 0x39) return ((char)('0' + vk - 0x30)).ToString()
			+ " (0x" + vk.ToString("X2", CultureInfo.InvariantCulture) + ")";
		if (vk >= 'A' && vk <= 'Z') return ((char)vk).ToString()
			+ " (0x" + vk.ToString("X2", CultureInfo.InvariantCulture) + ")";
		return "0x" + vk.ToString("X2", CultureInfo.InvariantCulture);
	}

	/// <summary>Bundle directory name. Chronological by construction (yyyyMMdd_HHmmss first), so retention
	/// can be an ordinary string sort instead of reading file times.</summary>
	public static string BundleName(DateTime stamp, int quest, string reason)
	{
		var sb = new StringBuilder(48);
		sb.Append("extract_").Append(stamp.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
		sb.Append("_q").Append(quest.ToString(CultureInfo.InvariantCulture));
		string r = Sanitise(reason);
		if (r.Length > 0) sb.Append('_').Append(r);
		return sb.ToString();
	}

	/// <summary>Lowercase [a-z0-9-] only, capped -- the reason becomes part of a directory name.</summary>
	public static string Sanitise(string reason)
	{
		if (string.IsNullOrEmpty(reason)) return "";
		var sb = new StringBuilder(reason.Length);
		for (int i = 0; i < reason.Length && sb.Length < 24; i++)
		{
			char c = reason[i];
			if (c >= 'A' && c <= 'Z') c = (char)(c + 32);
			if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) { sb.Append(c); continue; }
			// EVERY other character folds to ONE dash with runs collapsed, rather than being dropped:
			// dropping would turn "a/b" and "ab" into the same directory name.
			if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
		}
		return sb.ToString().Trim('-');
	}

	/// <summary>Which existing bundle directories must be deleted so that at most <paramref name="keep"/>
	/// of them remain. Names are compared ordinally: they start with "extract_yyyyMMdd_HHmmss", so the
	/// OLDEST sort first and the decision needs no file times (which can lie after a copy).</summary>
	public static List<string> StaleBundles(IList<string> names, int keep)
	{
		var sorted = new List<string>();
		if (names != null)
			for (int i = 0; i < names.Count; i++)
				if (!string.IsNullOrEmpty(names[i])) sorted.Add(names[i]);
		sorted.Sort(StringComparer.Ordinal);
		int k = keep < MinKeep ? MinKeep : (keep > MaxKeep ? MaxKeep : keep);
		int drop = sorted.Count - k;
		var outl = new List<string>();
		for (int i = 0; i < drop; i++) outl.Add(sorted[i]);
		return outl;
	}

	/// <summary>Where an on-demand bundle can take its data from.</summary>
	public enum BundleSource
	{
		/// <summary>Nothing to extract: report it, never write an empty bundle.</summary>
		None = 0,
		/// <summary>A live session (mid-battle, or the battle-end call inside the finalisation).</summary>
		Live = 1,
		/// <summary>The snapshot of the last FINALISED battle (see EvidenceExtractor.Remember).</summary>
		LastFinalised = 2,
	}

	/// <summary>
	/// Which source an extraction run may use. WHY THIS IS A DECISION AND NOT AN if:
	/// MEASURED 2026-10-05 13:26 -- the user pressed the extraction key ~12 s after a battle and the log
	/// said "跳过:没有战斗会话". Aggregator nulls Session at teardown / idle close, and the resume window
	/// (5 s) had already expired, but the export file and the contribution result computed at finalisation
	/// were both still there. A key that cannot read the battle the user just fought is useless exactly
	/// when it is wanted, so the fallback order is fixed here and exercised offline.
	/// </summary>
	public static BundleSource SelectSource(bool liveAvailable, bool rememberedAvailable)
	{
		if (liveAvailable) return BundleSource.Live;
		if (rememberedAvailable) return BundleSource.LastFinalised;
		return BundleSource.None;
	}
}
