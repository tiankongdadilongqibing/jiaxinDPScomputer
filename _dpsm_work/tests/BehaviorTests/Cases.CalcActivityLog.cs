using System.Collections.Generic;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// RF4f: the per-battle activity ring. The rules it owns are order, the cap and clearing -- the USABILITY
	/// rules (age windows, team/position gates) stay in AttributionPolicy, which is why nothing here needs a
	/// clock or a game object.
	/// </summary>
	public static void CalcActivityLog(Runner r)
	{
		r.Group("runtime/calc-activity-log");
		r.Eq("the-cap-is-64", CalcActivityLog<int>.Max, 64);
		var log = new CalcActivityLog<int>();
		r.Eq("a-new-ring-is-empty", log.Count, 0);
		log.Add(1); log.Add(2); log.Add(3);
		r.Eq("three-adds-count-three", log.Count, 3);
		r.Eq("index-zero-is-the-oldest", log[0], 1);
		r.Eq("the-last-index-is-the-newest", log[2], 3);
		log[1] = 20;
		r.Eq("an-entry-can-be-rewritten-in-place", log[1], 20);
		r.Eq("rewriting-does-not-change-the-count", log.Count, 3);

		// the cap: the OLDEST entry is the one that goes
		var full = new CalcActivityLog<int>();
		for (int i = 0; i < CalcActivityLog<int>.Max; i++) full.Add(i);
		r.Eq("a-full-ring-holds-exactly-the-cap", full.Count, CalcActivityLog<int>.Max);
		full.Add(999);
		r.Eq("one-more-add-keeps-the-count-at-the-cap", full.Count, CalcActivityLog<int>.Max);
		r.Eq("and-drops-the-oldest", full[0], 1);
		r.Eq("while-the-newest-is-kept", full[CalcActivityLog<int>.Max - 1], 999);
		for (int i = 0; i < 100; i++) full.Add(1000 + i);
		r.Eq("a-hundred-more-adds-still-hold-the-cap", full.Count, CalcActivityLog<int>.Max);
		r.Eq("and-keep-exactly-the-last-hundred", full[CalcActivityLog<int>.Max - 1], 1099);
		r.Eq("with-the-oldest-of-those-at-the-front", full[0], 1036);

		// clearing: the three transitions (session start / resume / finalise) all call this
		full.Clear();
		r.Eq("clear-empties-the-ring", full.Count, 0);
		full.Add(7);
		r.Eq("and-it-can-be-refilled", full.Count, 1);
		r.Eq("with-the-new-entry-at-the-front", full[0], 7);

		// generic in the entry type: the facade stores a struct holding IL2CPP references, the tests an int
		var strings = new CalcActivityLog<string>();
		strings.Add("a"); strings.Add("b");
		r.Str("the-ring-is-generic", strings[0] + strings[1], "ab");
	}
}
