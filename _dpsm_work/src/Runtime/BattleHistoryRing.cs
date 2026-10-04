using System.Collections.Generic;

namespace DpsMeter;

/// <summary>
/// RF4h (plan section 9, fourth family -- process-level state): the battle history RING.
///
/// The rule was two inline lines in the finalisation: insert the newest summary at the FRONT, then drop from
/// the BACK while the list is over the cap. Two things were wrong with that as code:
///   * the cap was the literal 20 while <see cref="Aggregator.MaxHistory"/> said 20 next to it -- two places
///     to change, one of them easy to miss;
///   * the rule could not be tested anywhere, although it is the only thing that keeps a long session from
///     accumulating summaries forever.
///
/// This is NOT the same container as Runtime/CalcActivityLog: that one appends at the END (oldest first) and
/// trims the FRONT, because its readers walk backwards for the most recent match. History is newest-first,
/// which is what the overlay and the "previous battle" view index with History[0].
/// </summary>
internal static class BattleHistoryRing
{
	/// <summary>How many finished battles are kept. Only one place declares it: Aggregator.MaxHistory
	/// refers to this, because the finalisation used to carry a second literal 20 next to it.</summary>
	public const int Max = 20;

	/// <summary>Push a summary in front and drop the OLDEST while the cap is exceeded.</summary>
	public static void Push(List<BattleSummary> history, BattleSummary summary, int max)
	{
		if (history == null) return;
		history.Insert(0, summary);
		while (history.Count > max) history.RemoveAt(history.Count - 1);
	}

	/// <summary>The most recent FINISHED battle, or null when there is none. The overlay and the
	/// "previous battle" view both mean this when they write History[0].</summary>
	public static BattleSummary Newest(List<BattleSummary> history)
	{
		if (history == null || history.Count == 0) return null;
		return history[0];
	}
}
