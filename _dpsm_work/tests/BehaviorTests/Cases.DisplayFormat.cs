using System;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// RF5b: the overlay's text layout, EXECUTED as production code. The plan asks the layout guard to keep
	/// fixed expected outputs for long names / CJK / extremes / zero / unknown and to run the real formatter;
	/// before this round the width/pad/fit rules were only a Python replica plus an artifact cross-check, so
	/// these cases are the first execution of the C# implementation itself.
	/// </summary>
	public static void DisplayFormatCases(Runner r)
	{
		r.Group("display/width");
		r.Eq("ascii-is-one-column-per-character", DisplayFormat.DispWidth("abc"), 3);
		r.Eq("cjk-is-two-columns-per-character", DisplayFormat.DispWidth("角色"), 4);
		r.Eq("mixed-widths-add-up", DisplayFormat.DispWidth("abc角色"), 7);
		r.Eq("an-empty-string-is-zero-wide", DisplayFormat.DispWidth(""), 0);
		r.Eq("a-fullwidth-digit-is-two", DisplayFormat.DispWidth("１２３"), 6);
		// U+FF61..FF9F (half-width katakana) is deliberately NOT in the wide ranges: the font draws it narrow.
		r.Eq("half-width-katakana-is-one", DisplayFormat.DispWidth("ｱ"), 1);
		r.Eq("fullwidth-latin-is-two", DisplayFormat.DispWidth("Ａ"), 2);
		// An astral CJK glyph is TWO UTF-16 units of width 1 each, i.e. a 2-column glyph -- measured behaviour,
		// and the reason Fit has to avoid cutting a surrogate pair in half.
		r.Eq("an-astral-cjk-glyph-is-two-columns", DisplayFormat.DispWidth("\U00029E3D"), 2);

		r.Group("display/pad");
		r.Str("padr-pads-on-the-right", DisplayFormat.PadR("ab", 5), "ab   ");
		r.Str("padl-pads-on-the-left", DisplayFormat.PadL("ab", 5), "   ab");
		r.Str("padr-counts-cjk-as-two", DisplayFormat.PadR("角色", 6), "角色  ");
		r.Str("padl-counts-cjk-as-two", DisplayFormat.PadL("角色", 6), "  角色");
		r.Str("padr-never-shrinks", DisplayFormat.PadR("abcdef", 3), "abcdef");
		r.Str("padl-never-shrinks", DisplayFormat.PadL("abcdef", 3), "abcdef");
		r.Str("padr-of-empty-is-all-spaces", DisplayFormat.PadR("", 3), "   ");

		r.Group("display/cell");
		r.Str("a-null-cell-is-empty", DisplayFormat.Cell(null), "");
		r.Str("an-empty-cell-is-empty", DisplayFormat.Cell(""), "");
		// U+00D7 MULTIPLICATION SIGN: the ONE glyph a census of every exported name found whose width depends
		// on the font, so it is normalised to the ASCII letter x.
		r.Str("the-multiplication-sign-becomes-an-ascii-x", DisplayFormat.Cell("a\u00D7b"), "axb");
		r.Str("other-text-passes-through", DisplayFormat.Cell("角色 12"), "角色 12");

		r.Group("display/numbers");
		r.Str("num-groups-thousands", DisplayFormat.Num(1234567L), "1,234,567");
		r.Str("num-of-zero", DisplayFormat.Num(0L), "0");
		r.Str("num-of-a-negative", DisplayFormat.Num(-1234L), "-1,234");
		r.Str("fmt-rounds-a-double", DisplayFormat.Fmt(1234.6), "1,235");
		r.Str("fmt-of-nan-is-zero", DisplayFormat.Fmt(double.NaN), "0");
		r.Str("fmt-of-infinity-is-zero", DisplayFormat.Fmt(double.PositiveInfinity), "0");
		r.Str("pct-is-two-decimals", DisplayFormat.Pct(12.345), "12.35%");
		r.Str("pct-of-nan-is-zero", DisplayFormat.Pct(double.NaN), "0.00%");

		r.Group("display/amount");
		// 2.1e8 is the corpus maximum: 11 columns, so it still prints in full.
		r.Str("an-11-column-amount-fits-exactly", DisplayFormat.Amt(210000000.0, 11), "210,000,000");
		r.Str("a-13-column-amount-falls-back-to-M", DisplayFormat.Amt(1e9, 11), "    1000.0M");
		r.Str("the-M-fallback-also-takes-the-NaN-guard", DisplayFormat.Amt(double.NaN, 6), "     0");
		r.Eq("every-amount-column-is-exactly-its-width", DisplayFormat.DispWidth(DisplayFormat.Amt(1e9, 11)), 11);
		r.Eq("the-corpus-maximum-column-is-exactly-its-width", DisplayFormat.DispWidth(DisplayFormat.Amt(2.1e8, 11)), 11);

		r.Group("display/fit");
		r.Str("a-short-string-is-untouched", DisplayFormat.Fit("abc", 5), "abc");
		r.Str("an-empty-string-stays-empty", DisplayFormat.Fit("", 5), "");
		r.Str("cjk-is-cut-on-a-column-boundary", DisplayFormat.Fit("あああああ", 6), "ああ..");
		r.Str("a-long-ascii-name-is-cut-with-the-mark", DisplayFormat.Fit("abcdefghij", 6), "abcd..");
		r.Str("a-max-of-two-is-just-the-mark", DisplayFormat.Fit("abcdef", 2), "..");
		r.Str("a-max-of-one-takes-one-character-of-the-mark", DisplayFormat.Fit("abcdef", 1), ".");
		r.Str("a-max-of-zero-is-empty", DisplayFormat.Fit("abcdef", 0), "");
		// The surrogate rule: cutting this string at a 3-column budget must NOT leave half a pair.
		string astral = "\U00029E3D\U00029E3D\U00029E3D";
		r.Str("an-astral-string-is-not-cut-mid-pair", DisplayFormat.Fit(astral, 3), "..");
		r.True("the-fit-output-has-no-lone-surrogate", !HasLoneSurrogate(DisplayFormat.Fit(astral, 3)));
		bool anyLone = false;
		for (int max = 0; max <= 12; max++)
			if (HasLoneSurrogate(DisplayFormat.Fit(astral + "ab" + astral, max))) anyLone = true;
		r.True("no-budget-produces-a-lone-surrogate", !anyLone);
		r.Eq("every-fit-result-is-at-most-the-budget", FitsAllBudgets(), 1);
	}

	private static bool HasLoneSurrogate(string s)
	{
		if (s == null) return false;
		for (int i = 0; i < s.Length; i++)
		{
			if (char.IsHighSurrogate(s[i]) && (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1]))) return true;
			if (char.IsLowSurrogate(s[i]) && (i == 0 || !char.IsHighSurrogate(s[i - 1]))) return true;
		}
		return false;
	}

	/// <summary>Sweep the budgets over a mixed corpus and report 1 when every result fits its budget.</summary>
	private static int FitsAllBudgets()
	{
		string[] corpus = { "", "abc", "角色名がとても長い場合の表示", "\U00029E3D\U00029E3D", "a\u00D7b", "１２３４５" };
		for (int i = 0; i < corpus.Length; i++)
			for (int max = 0; max <= 20; max++)
			{
				string got = DisplayFormat.Fit(corpus[i], max);
				if (DisplayFormat.DispWidth(got) > Math.Max(max, DisplayFormat.CutMark.Length)) return 0;
			}
		return 1;
	}
}
