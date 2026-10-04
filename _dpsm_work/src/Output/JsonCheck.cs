using System.Collections.Generic;

namespace DpsMeter;

/// <summary>
/// 1.5.0 (架构审视 D/F5 follow-up): a STRUCTURAL self-check of the export, run before the file is written.
///
/// WHY IT EXISTS. The export has no schema test and cannot easily have one (the writer depends on IL2CPP
/// types), so the only thing standing between a bad edit and a 13 MB file that no script can read was
/// "someone will notice". Two concrete near-misses in this project:
///   * 1.4.1 shipped a `Config.Bind` line whose edit swallowed half the statement -- it COMPILED with 0
///     errors and 0 warnings and the channel was silently dead (see ARCHITECTURE rule 33);
///   * 1.5.0 itself produced two edits that joined two statements onto one line (rule 32's hazard).
/// Neither is visible to the compiler. This check is: it walks the finished string once, verifying that
/// brackets balance OUTSIDE string literals and that no key is emitted twice in the root object -- the
/// two malformations a hand-built writer actually produces. It runs on ~13 MB, i.e. milliseconds.
///
/// It deliberately does NOT validate values or names: it answers "is this parseable JSON", not "is this
/// the right JSON". The offline `recon_probe` pins the exact key shapes separately.
/// </summary>
internal static class JsonCheck
{
	/// <summary>
	/// True when <paramref name="json"/> is structurally sound. <paramref name="error"/> describes the
	/// first problem found WITH ITS OFFSET AND THE SURROUNDING TEXT -- the export is one 13 MB line in
	/// practice, so "brace depth 3 at end" alone would send the reader hunting; a quoted fragment can be
	/// searched for directly. <paramref name="dupKeys"/> counts duplicate keys seen at the ROOT level
	/// (which JSON tolerates but which silently loses data -- the 1.5.0 timeline emitted `rows` twice
	/// before this check existed).
	/// </summary>
	internal static bool Validate(string json, out string error, out int dupKeys)
	{
		error = null;
		dupKeys = 0;
		if (string.IsNullOrEmpty(json)) { error = "empty"; return false; }
		int brace = 0, bracket = 0;
		bool inStr = false, esc = false;
		// root-level keys: a string at brace depth 1 that is followed by ':'
		bool atKeyStart = false;
		int strStart = 0;
		var rootKeys = new HashSet<string>();
		int n = json.Length;
		for (int i = 0; i < n; i++)
		{
			char c = json[i];
			if (inStr)
			{
				if (esc) { esc = false; continue; }
				if (c == '\\') { esc = true; continue; }
				if (c == '"')
				{
					inStr = false;
					// key iff this string started right where a root member begins
					if (atKeyStart && brace == 1 && i + 1 < n && json[i + 1] == ':')
					{
						string k = json.Substring(strStart, i - strStart);
						if (!rootKeys.Add(k)) dupKeys++;
					}
					atKeyStart = false;
				}
				continue;
			}
			switch (c)
			{
				case '"':
					inStr = true;
					strStart = i + 1;
					// a root-level string is a key when the previous non-space char is '{' or ','
					if (brace == 1)
					{
						int j = i - 1;
						while (j >= 0 && (json[j] == ' ' || json[j] == '\n' || json[j] == '\r' || json[j] == '\t')) j--;
						atKeyStart = (j >= 0 && (json[j] == '{' || json[j] == ','));
					}
					break;
				case '{': brace++; break;
				case '}': brace--; if (brace < 0) { error = "unbalanced } " + Around(json, i); return false; } break;
				case '[': bracket++; break;
				case ']': bracket--; if (bracket < 0) { error = "unbalanced ] " + Around(json, i); return false; } break;
			}
		}
		if (inStr) { error = "unterminated string " + Around(json, n - 1); return false; }
		if (brace != 0) { error = "brace depth " + brace + " at end" + Around(json, n - 1); return false; }
		if (bracket != 0) { error = "bracket depth " + bracket + " at end" + Around(json, n - 1); return false; }
		return true;
	}

	/// <summary>
	/// A searchable fragment around a character offset. The point is not to be pretty: it is that the
	/// reported text can be pasted into a search on a 13 MB single-line file.
	/// </summary>
	private static string Around(string s, int at)
	{
		try
		{
			const int Span = 70;
			int from = at - Span; if (from < 0) from = 0;
			int to = at + Span; if (to > s.Length) to = s.Length;
			return "@" + at + " «" + s.Substring(from, to - from) + "»";
		}
		catch { return "@" + at; }
	}
}
