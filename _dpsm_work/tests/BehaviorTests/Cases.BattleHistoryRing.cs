using System.Collections.Generic;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// RF4h: the process-level history ring. It is newest-first (the overlay indexes History[0]) and capped,
	/// and the cap used to be a second literal next to Aggregator.MaxHistory.
	/// NOTE: BattleSummary.QuestId is a STRING (BattleSession.QuestId is the int) -- the fixtures say so.
	/// </summary>
	public static void BattleHistoryRingCases(Runner r)
	{
		r.Group("runtime/history-ring");
		r.Eq("the-cap-is-20", BattleHistoryRing.Max, 20);
		var h = new List<BattleSummary>();
		r.True("an-empty-history-has-no-newest", BattleHistoryRing.Newest(h) == null);
		r.True("and-a-null-history-does-not-throw", BattleHistoryRing.Newest(null) == null);

		BattleHistoryRing.Push(h, new BattleSummary { QuestId = "1" }, BattleHistoryRing.Max);
		BattleHistoryRing.Push(h, new BattleSummary { QuestId = "2" }, BattleHistoryRing.Max);
		BattleHistoryRing.Push(h, new BattleSummary { QuestId = "3" }, BattleHistoryRing.Max);
		r.Eq("three-pushes-count-three", h.Count, 3);
		r.Str("the-newest-is-at-the-front", h[0].QuestId, "3");
		r.Str("the-oldest-is-at-the-back", h[2].QuestId, "1");
		r.Str("newest-returns-the-front-entry", BattleHistoryRing.Newest(h).QuestId, "3");

		var full = new List<BattleSummary>();
		for (int i = 0; i < 21; i++) BattleHistoryRing.Push(full, new BattleSummary { QuestId = (100 + i).ToString() }, BattleHistoryRing.Max);
		r.Eq("a-full-ring-holds-exactly-the-cap", full.Count, 20);
		r.Str("the-newest-survives", full[0].QuestId, "120");
		r.Str("the-second-oldest-survives", full[19].QuestId, "101");
		bool firstGone = true;
		for (int i = 0; i < full.Count; i++) if (full[i].QuestId == "100") firstGone = false;
		r.True("the-oldest-was-dropped", firstGone);
		BattleHistoryRing.Push(null, new BattleSummary(), BattleHistoryRing.Max);
		r.Eq("pushing-into-a-null-list-is-a-no-op", full.Count, 20);
	}
}
