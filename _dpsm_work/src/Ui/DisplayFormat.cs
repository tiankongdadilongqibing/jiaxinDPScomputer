using System;

namespace DpsMeter;

/// <summary>
/// RF5 (plan section 10): the overlay's TEXT LAYOUT, separated from the renderer.
///
/// Why. These ten members used to live inside Ui/OverlayUGUI.Rows.cs -- a 1375-line file that
/// also owns the uGUI pool, the row model and the contribution view. They contain no Unity type at
/// all (string/int/double/Math only), which is exactly why the plan asks for them to be a layer: the
/// behaviour suite can now EXECUTE the production formatter instead of trusting a Python replica,
/// and a second renderer has one place to consume the same column widths and number formats.
///
/// The measurements recorded in the comments below (the U+00D7 census, the :N0 reconciliation, the
/// 13-character money column) travelled with the code: they are the REASONS for the rules.
///
/// The Python layout guard (check_contribution_layout.py) keeps its independent replica and compares
/// it against THIS file -- it now reads src/Ui/DisplayFormat.cs instead of the rows file.
/// </summary>
internal static class DisplayFormat
{

	internal static int DispWidth(char c)
	{
		bool wide = (c >= 0x1100 && c <= 0x115F)
			|| (c >= 0x2E80 && c <= 0xA4CF)
			|| (c >= 0xAC00 && c <= 0xD7A3)
			|| (c >= 0xF900 && c <= 0xFAFF)
			|| (c >= 0xFE30 && c <= 0xFE6F)
			|| (c >= 0xFF00 && c <= 0xFF60)
			|| (c >= 0xFFE0 && c <= 0xFFE6);
		return wide ? 2 : 1;
	}














	internal static int DispWidth(string s)
	{
		int w = 0;
		for (int i = 0; i < s.Length; i++) w += DispWidth(s[i]);
		return w;
	}





















	/// <summary>1.7.7: text that goes into a width-padded column must not contain a glyph whose width
	/// depends on the font. A census of the name strings in every export that carries a contribution
	/// section (13 of them; actor names, rule names, owner names and link names) found exactly ONE
	/// such character: U+00D7 MULTIPLICATION SIGN, which a CJK
	/// font may draw full-width (2 columns) while DispWidth counts 1 -- that alone would shift the row.
	/// Rows whose name contains it (two rule names do) are normalised to the ASCII letter x. The links
	/// table's arrow is NOT normalised on purpose: it appears in the header AND every data row, so a
	/// mis-measured arrow moves both by the same amount and the columns stay aligned with each other.</summary>
	internal static string Cell(string s)
	{
		return string.IsNullOrEmpty(s) ? "" : s.Replace('\u00D7', 'x');
	}


































	internal static string PadR(string s, int width)
	{
		int w = DispWidth(s);
		return w >= width ? s : s + new string(' ', width - w);
	}








































	internal static string PadL(string s, int width)
	{
		int w = DispWidth(s);
		return w >= width ? s : new string(' ', width - w) + s;
	}














































	/// <summary>1.7.6: thousands separators, now that the contribution page is drawn in a monospaced
	/// font. On the proportional UI font the varying digit count was exactly what made each column
	/// drift; on a 1:2 grid a separator is one extra column and the alignment still holds.</summary>
	internal static string Num(long v)
	{
		return v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
	}

	/// <summary>RF5h: a whole number WITHOUT thousands separators, for the "秒伤 12345" cells. This is
	/// deliberately not <see cref="Num"/>: those cells are drawn in a proportional font where a separator
	/// shifts the text, and the pre-RF5 code interpolated {x:F0} there. Moving it here does not change one
	/// character of what the user sees -- that is the point -- and it removes the last inline format
	/// specifier from the overlay rows. (The chart view still has its own :N0/:F0 sites; see the batch
	/// record RF5H for why unifying those needs a decision, not a refactor.)</summary>
	internal static string Whole(double v)
	{
		return v.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
	}






















































	/// <summary>1.7.7: money columns use the SAME specifier as the roster's contribution block (:N0),
	/// so one double can no longer print differently on two surfaces. The F5 page used to cast to long
	/// (truncation) while the roster rounded -- the user-visible symptom was "基础+自身规则+辅助 加不
	/// 起来" on the page whose header promises that identity. Rounding is not additive either, so the
	/// footer now states the residual explicitly instead of implying there is none.</summary>
	internal static string Fmt(double v)
	{
		if (double.IsNaN(v) || double.IsInfinity(v)) v = 0.0;
		return v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
	}

































































	internal static string Pct(double v)
	{
		if (double.IsNaN(v) || double.IsInfinity(v)) v = 0.0;
		return v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "%";
	}







































































	/// <summary>1.7.7 rev2: PadL never shrinks, so a money value wider than its column pushed the whole
	/// row right. The corpus already reaches 2.1e8 and one long fight from 1e9 (a 13-character grouped
	/// number in an 11-column slot). Amt keeps the full grouped form whenever it fits (so nothing about
	/// today's numbers changes) and otherwise falls back to an M/G/T suffix that always fits. The exact
	/// value is in the export; this is a display fallback, never a silent truncation of digits.</summary>
	internal static string Amt(double v, int width)
	{
		if (double.IsNaN(v) || double.IsInfinity(v)) v = 0.0;
		string full = Fmt(v);
		if (DispWidth(full) <= width) return PadL(full, width);
		string[] suffix = { "M", "G", "T" };
		double scale = 1e6;
		for (int i = 0; i < suffix.Length; i++, scale *= 1000.0)
		{
			string c = (v / scale).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + suffix[i];
			if (DispWidth(c) <= width) return PadL(c, width);
		}
		return PadL(">" + new string('9', Math.Max(0, width - 1)), width);
	}


	/// <summary>1.7.7: Fit cuts by DISPLAY WIDTH, not by .NET character count.
	///
	/// The defect it fixes: PadR/PadL pad to exactly N display columns (CJK = 2), but Fit used to cut the
	/// string at N characters and append an ellipsis. The two units disagree as soon as a name is wider
	/// than its column: "エヴァラス・フラウ" is 9 characters but 18 columns, so PadR(...,16) returned it
	/// unchanged and every numeric column of that row shifted right by 2 (measured on 1.7.6: 25 of 140
	/// character rows and 78 of 340 rule rows overflowed their column; max overflow 3).
	///
	/// The cut mark is two ASCII dots: a period is exactly one column in every font, whereas U+2026 is
	/// one column in some fonts and two in others -- and a mark that is half a column off would
	/// reintroduce exactly the off-by-one this release removes.</summary>
	internal const string CutMark = "..";


	internal static string Fit(string s, int max)
	{
		if (string.IsNullOrEmpty(s)) return "";
		if (DispWidth(s) <= max) return s;
		if (max <= CutMark.Length) return CutMark.Substring(0, Math.Max(0, Math.Min(CutMark.Length, max)));
		int budget = max - CutMark.Length;   // the mark is ASCII, so one column per character
		int w = 0, i = 0;
		for (; i < s.Length; i++)
		{
			int cw = DispWidth(s[i]);
			if (w + cw > budget) break;
			w += cw;
		}
		if (i > 0 && char.IsHighSurrogate(s[i - 1])) i--;   // never cut a surrogate pair in half
		return s.Substring(0, i) + CutMark;
	}

}
