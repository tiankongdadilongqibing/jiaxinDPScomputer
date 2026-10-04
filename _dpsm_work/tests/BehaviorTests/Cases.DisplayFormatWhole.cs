using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// RF5h: DisplayFormat.Whole -- the "秒伤 12345" cells. It is deliberately NOT Num(): these cells are drawn
	/// in a proportional font where a thousands separator shifts the text, and the pre-RF5 code interpolated
	/// {x:F0} there. The cases pin the no-separator behaviour so a future "unification" cannot slip one in.
	/// </summary>
	public static void DisplayFormatWholeCases(Runner r)
	{
		r.Group("format/whole");
		r.Str("zero-is-zero", DisplayFormat.Whole(0.0), "0");
		r.Str("a-whole-number-has-no-decimal-point", DisplayFormat.Whole(12345.0), "12345");
		r.Str("and-no-thousands-separators", DisplayFormat.Whole(1234567.0), "1234567");
		r.Str("fractions-round-half-away-from-zero", DisplayFormat.Whole(999.5), "1000");
		r.Str("and-round-down-below-the-half", DisplayFormat.Whole(999.4), "999");
		r.Str("a-negative-keeps-its-sign", DisplayFormat.Whole(-1234.0), "-1234");
	}
}
