namespace DpsMeter;

/// <summary>
/// The one JSON string-escaping rule for this plugin.
///
/// Extracted in 1.4.0 for the same reason the reconciliation tolerances are exported in-band: two
/// copies of a rule drift. The export writes Chinese unit names, ability texts that contain quotes and
/// newlines, and the composition lines -- so escaping is not cosmetic, and a second, subtly different
/// copy in a new file would corrupt the JSON only for the values that contain the character the copy
/// forgot.
/// </summary>
internal static class JsonText
{
	/// <summary>Escape a raw string for inclusion between double quotes. Null becomes "".</summary>
	internal static string Str(string t)
	{
		if (t == null) return "";
		return t.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
	}
}
