namespace DpsMeter;

/// <summary>
/// THE formatting of battle times.
///
/// Every place that shows a battle time to the user, or writes one to the log, goes through here, so
/// the overlay main page ("时间 17秒"), the F6 per-hit list ("t=17.7s"), the chart caption and the
/// [TIME]/finalize log lines always show the same number in the same shape -- and a format change
/// happens in exactly one file.
///
/// All values are seconds of <see cref="BattleSession.ActiveSeconds"/>, the single battle clock.
/// (The exported JSON deliberately keeps a raw two-decimal number, because the offline analysis
/// scripts parse it.)
/// </summary>
public static class BattleTime
{
	/// <summary>Overlay rows and captions: "17秒".</summary>
	public static string Seconds(double s)
	{
		return s.ToString("F0") + "秒";
	}

	/// <summary>F6 per-hit line: "t=17.7s".</summary>
	public static string Hit(double t)
	{
		return "t=" + t.ToString("F1") + "s";
	}

	/// <summary>Log and diagnostic values: "17.9s".</summary>
	public static string Log(double s)
	{
		return s.ToString("F1") + "s";
	}
}
