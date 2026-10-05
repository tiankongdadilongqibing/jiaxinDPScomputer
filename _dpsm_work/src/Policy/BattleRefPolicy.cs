using System;
using System.Globalization;
using System.Text;

namespace DpsMeter;

/// <summary>
/// R56 (BID-0, plan §3-§6): the FROZEN contract of a battle reference -- its text format, its lifecycle
/// states, the file name it owns, the legacy reference used for files that predate it, and the exact
/// text the 复制引用 action puts on the clipboard.
///
/// Pure on purpose: no Unity, no file system, no clock of its own. The generator takes its time and its
/// random token as ARGUMENTS (<see cref="BattleRefRegistry"/>), which is what makes "a collision must be
/// detected", "sequence 1000 must not be truncated" and "a view rebuild must not mint a number"
/// executable statements instead of review comments.
/// </summary>
public static class BattleRefPolicy
{
	public const string SchemaVersion = "1";
	public const string IdPrefix = "B-";
	public const string LegacyPrefix = "legacy:";
	public const int MinSequenceDigits = 3;
	public const int TokenHexDigits = 16;

	public const string StateLive = "live";
	public const string StateProvisional = "provisional";
	public const string StateFinal = "final";

	/// <summary>"{utc:yyyyMMdd-HHmmss}-{token}". The time is for reading, the token for collision
	/// resistance; neither alone is a uniqueness guarantee, which is why the registry still validates.</summary>
	public static string FormatLaunchId(DateTime utc, string token)
	{
		string t = (token ?? "").Trim().ToUpperInvariant();
		return utc.ToUniversalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + t;
	}

	/// <summary>"B-{launchId}-{sequence}". At least 3 digits; 1000 becomes 4 digits and is NEVER cut
	/// (plan §1: 超过999按完整数字扩展,不截断).</summary>
	public static string FormatId(string launchId, int sequence)
	{
		return IdPrefix + (launchId ?? "") + "-" + SequenceText(sequence);
	}

	public static string SequenceText(int sequence)
	{
		int s = sequence < 0 ? 0 : sequence;
		string digits = s.ToString(CultureInfo.InvariantCulture);
		while (digits.Length < MinSequenceDigits) digits = "0" + digits;
		return digits;
	}

	/// <summary>The short tag the overlay shows ("#003"). Display only -- never a cross-launch key.</summary>
	public static string ShortTag(int sequence)
	{
		return "#" + SequenceText(sequence);
	}

	/// <summary>Parse a full reference. False for anything malformed, including a missing sequence.</summary>
	public static bool TryParse(string id, out string launchId, out int sequence)
	{
		launchId = "";
		sequence = 0;
		if (string.IsNullOrEmpty(id)) return false;
		string s = id.Trim();
		if (!s.StartsWith(IdPrefix, StringComparison.Ordinal)) return false;
		s = s.Substring(IdPrefix.Length);
		int at = s.LastIndexOf('-');
		if (at <= 0) return false;
		string seqText = s.Substring(at + 1);
		string launch = s.Substring(0, at);
		if (launch.Length == 0 || seqText.Length == 0) return false;
		// No separate digit scan: int.TryParse with NumberStyles.None rejects signs, spaces and full-width
		// digits, and a hand-rolled loop in front of it was dead code no mutation could reach (R56: the
		// first draft had one, and the negative control proved it could not bite).
		int value;
		if (!int.TryParse(seqText, NumberStyles.None, CultureInfo.InvariantCulture, out value)) return false;
		if (value <= 0) return false;
		launchId = launch;
		sequence = value;
		return true;
	}

	/// <summary>legacy:&lt;sha256&gt;. Old exports are never rewritten; they are located by CONTENT hash,
	/// and the tool must say that this is a file reference, not an id the overlay ever showed (plan §8).</summary>
	public static string LegacyRef(string sha256)
	{
		string h = (sha256 ?? "").Trim().ToLowerInvariant();
		return LegacyPrefix + h;
	}

	public static bool IsLegacyRef(string s)
	{
		return s != null && s.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>The legacy hash part, or "" when this is not a legacy reference.</summary>
	public static string LegacyHash(string s)
	{
		if (!IsLegacyRef(s)) return "";
		return s.Substring(LegacyPrefix.Length).Trim().ToLowerInvariant();
	}

	/// <summary>
	/// Which state a close leaves behind. final = the game reported the battle ended AND produced a
	/// result; every other close (idle silence, teardown, restart, an end with no result) is
	/// provisional. A provisional export must never be read as "this battle is complete" (plan §4).
	/// </summary>
	public static string CloseState(string why, int result)
	{
		bool end = string.Equals(why, "end", StringComparison.Ordinal);
		bool hasResult = result == 1 || result == 2;   // GameResult: 1 = Win, 2 = Lose
		return (end && hasResult) ? StateFinal : StateProvisional;
	}

	public static bool IsFinal(string state)
	{
		return string.Equals(state, StateFinal, StringComparison.Ordinal);
	}

	/// <summary>Only a final session may enter a cross-battle comparison; live/provisional may be
	/// listed, and a diagnostic mode must say the sample is unfinished (plan §7).</summary>
	public static bool IsComparableState(string state)
	{
		return IsFinal(state);
	}

	/// <summary>A comparison of two references is a CONFLICT when the same id is presented with
	/// different content. Same id + same revision + different hash is the case that must never be
	/// silently resolved (plan §5.3).</summary>
	public static bool SameIdDifferentContent(string idA, int revA, string hashA, string idB, int revB, string hashB)
	{
		if (!string.Equals(idA, idB, StringComparison.Ordinal)) return false;
		if (revA != revB) return false;
		return !string.Equals((hashA ?? "").ToLowerInvariant(), (hashB ?? "").ToLowerInvariant(), StringComparison.Ordinal);
	}

	/// <summary>The session's export file name. The quest stays in its historical position (the second
	/// underscore-separated field) so existing globs and the quest lookup keep working, and the full id
	/// is appended so two battles in the same second cannot overwrite each other (plan §5.1).</summary>
	public static string FileName(int quest, DateTime startedWall, string id)
	{
		return "battle_" + quest.ToString(CultureInfo.InvariantCulture) + "_"
			+ startedWall.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)
			+ (string.IsNullOrEmpty(id) ? "" : "__" + id) + ".json";
	}

	/// <summary>WHY a file name is not acceptable for this reference: it must END with the id, so a
	/// mismatched target is refused instead of being written over.</summary>
	public static bool FileNameMatchesId(string file, string id)
	{
		if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(id)) return false;
		string name = file;
		int slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
		if (slash >= 0) name = name.Substring(slash + 1);
		return name.EndsWith("__" + id + ".json", StringComparison.Ordinal) || name == (id + ".json");
	}

	/// <summary>
	/// The structural check the writer runs BEFORE it claims success (plan §5.2 step 1). Returns "" when
	/// the identity is well-formed and self-consistent, else a reason. It never throws.
	/// </summary>
	public static string ValidateBlock(string id, string launchId, int sequence, int revision, string state)
	{
		string parsedLaunch;
		int parsedSeq;
		if (!TryParse(id, out parsedLaunch, out parsedSeq)) return "id 格式非法";
		if (parsedSeq != sequence) return "id 的序号与 sequence 字段不一致";
		if (!string.Equals(parsedLaunch, launchId ?? "", StringComparison.Ordinal)) return "id 的启动命名空间与 launchId 不一致";
		if (revision < 1) return "revision 必须 >= 1";
		if (state != StateLive && state != StateProvisional && state != StateFinal) return "state 取值非法";
		return "";
	}

	/// <summary>
	/// The 复制引用 payload (plan §6). When nothing has been written yet the file/hash lines say so --
	/// inventing a hash or a path would be the one thing this feature exists to prevent.
	/// </summary>
	public static string CopyText(string id, int revision, int quest, string state, int resetCount,
	                              string fileName, string sha256)
	{
		var sb = new StringBuilder(256);
		sb.Append("DpsMeter battle-ref/").Append(SchemaVersion).Append('\n');
		sb.Append("battleId: ").Append(id ?? "").Append('\n');
		sb.Append("revision: ").Append(revision.ToString(CultureInfo.InvariantCulture)).Append('\n');
		sb.Append("quest: ").Append(quest.ToString(CultureInfo.InvariantCulture)).Append('\n');
		sb.Append("state: ").Append(state ?? "").Append('\n');
		sb.Append("resetCount: ").Append(resetCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
		sb.Append("file: ").Append(string.IsNullOrEmpty(fileName) ? "(尚未导出,尚无可分析文件)" : fileName).Append('\n');
		sb.Append("sha256: ").Append(string.IsNullOrEmpty(sha256) ? "(尚无可分析文件)" : sha256.ToLowerInvariant()).Append('\n');
		return sb.ToString();
	}
}
