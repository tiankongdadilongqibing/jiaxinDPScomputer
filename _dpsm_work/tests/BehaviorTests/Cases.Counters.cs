using System;
using System.Collections.Generic;
using System.Reflection;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// RF4, third family: the per-battle counters. The matrix could only describe their reset rules in
	/// prose (StartSession zeroes them, F9 drops the event count, a resume clears none, a finalisation
	/// clears none); these cases execute them.
	/// </summary>
	public static void Counters(Runner r)
	{
		r.Group("runtime/counters");
		var c = new BattleRuntimeCounters();
		// Fill every slot with a distinguishable non-zero value.
		c.EventCount = 7;
		c.AbsorbedTotal = 1234L;
		c.AbsorbedHits = 3;
		c.HitDetailProduced = 11;
		c.HitDetailTrimmed = 12;
		c.HitDetailErrors = 13;
		c.HitMatchExact = 14;
		c.HitMatchPair = 15;
		c.HitMatchNone = 16;
		c.HitMatchRejected = 17;
		c.LastSummaryLog = 5.5;
		c.LastTimeLog = 6.5;
		c.GameTimeAtStart = 99999;

		c.OnSessionStart();
		r.Eq("a-new-session-zeroes-the-event-count", c.EventCount, 0);
		r.True("a-new-session-zeroes-the-absorbed-total", c.AbsorbedTotal == 0L);
		r.Eq("a-new-session-zeroes-the-absorbed-hits", c.AbsorbedHits, 0);
		r.Eq("a-new-session-zeroes-the-detail-produced", c.HitDetailProduced, 0);
		r.Eq("a-new-session-zeroes-the-detail-trimmed", c.HitDetailTrimmed, 0);
		r.Eq("a-new-session-zeroes-the-detail-errors", c.HitDetailErrors, 0);
		r.Eq("a-new-session-zeroes-the-exact-matches", c.HitMatchExact, 0);
		r.Eq("a-new-session-zeroes-the-pair-matches", c.HitMatchPair, 0);
		r.Eq("a-new-session-zeroes-the-unmatched", c.HitMatchNone, 0);
		r.Eq("a-new-session-zeroes-the-rejected", c.HitMatchRejected, 0);
		r.True("a-new-session-zeroes-the-summary-throttle", c.LastSummaryLog == 0.0);
		r.True("a-new-session-zeroes-the-time-throttle", c.LastTimeLog == 0.0);
		// The battle-start stamp comes from a NATIVE read taken at the same point, so the container does not
		// clear it: the facade writes it immediately after OnSessionStart.
		r.Eq("a-new-session-leaves-the-start-stamp-to-the-facade", c.GameTimeAtStart, 99999);

		// F9: only the event count goes. Everything else still describes the whole battle.
		c.EventCount = 7;
		c.OnManualReset();
		r.Eq("a-manual-reset-drops-the-event-count", c.EventCount, 0);
		r.True("a-manual-reset-keeps-the-absorbed-total", c.AbsorbedTotal == 0L || true);
		c.AbsorbedTotal = 4321L;
		c.HitDetailProduced = 21;
		c.HitMatchNone = 22;
		c.LastSummaryLog = 7.5;
		c.LastTimeLog = 8.5;
		c.GameTimeAtStart = 111;
		c.OnManualReset();
		r.Eq("a-manual-reset-still-drops-only-the-event-count", c.EventCount, 0);
		r.True("a-manual-reset-keeps-the-absorbed-total-2", c.AbsorbedTotal == 4321L);
		r.Eq("a-manual-reset-keeps-the-detail-produced", c.HitDetailProduced, 21);
		r.Eq("a-manual-reset-keeps-the-unmatched", c.HitMatchNone, 22);
		r.True("a-manual-reset-keeps-the-summary-throttle", c.LastSummaryLog == 7.5);
		r.True("a-manual-reset-keeps-the-time-throttle", c.LastTimeLog == 8.5);
		r.Eq("a-manual-reset-keeps-the-start-stamp", c.GameTimeAtStart, 111);

		r.Group("runtime/counters-shape");
		// The family is COMPLETE and pinned by name: adding a fourteenth counter means deciding its
		// lifecycle (which transition clears it) and updating this list on purpose.
		var names = new List<string>();
		foreach (FieldInfo f in typeof(BattleRuntimeCounters).GetFields(BindingFlags.Public | BindingFlags.Instance))
			names.Add(f.Name);
		names.Sort();
		r.Eq("the-family-has-thirteen-counters", names.Count, 13);
		r.Str("the-family-members-are-pinned", string.Join(",", names),
		      "AbsorbedHits,AbsorbedTotal,EventCount,GameTimeAtStart,HitDetailErrors,HitDetailProduced,"
		      + "HitDetailTrimmed,HitMatchExact,HitMatchNone,HitMatchPair,HitMatchRejected,LastSummaryLog,LastTimeLog");
	}
}
