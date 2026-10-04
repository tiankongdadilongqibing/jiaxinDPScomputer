using System;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	private static HitRecord Pending(BattleObject atk, BattleObject blk, int dmg, double t)
	{
		return new HitRecord { Attacker = atk, Blocker = blk, Damage = dmg, T = t };
	}

	/// <summary>
	/// The 0.35 s hit-pairing window (BattleSession.ConsumePending). This decides which damage-detail
	/// record labels a hit, i.e. the exported `source` / `crit` / `hitValue` of every event, so an
	/// off-by-one in the window or in the candidate order silently relabels hits without changing any
	/// total. The boundary cases use Math.BitIncrement so "equal to the window" is EXACT rather than
	/// arithmetic luck (1.35 - 1.0 is not 0.35 in binary floating point).
	/// </summary>
	public static void HitWindow(Runner r)
	{
		r.Group("window/constants");
		r.EqD("hit-match-window-is-0.35s", BattleSession.HitMatchSeconds, 0.35);
		r.Eq("pending-cap-is-2048", BattleSession.MaxPending, 2048);
		r.Eq("event-cap-is-100000", BattleSession.MaxEvents, 100000);

		var a = new BattleObject { DebugName = "A" };
		var b = new BattleObject { DebugName = "B" };
		var other = new BattleObject { DebugName = "X" };
		int how;

		r.Group("window/matching");
		var s = new BattleSession();
		r.True("empty-queue-returns-null", s.ConsumePending(a, b, 100, 100, 1.0, out how) == null);
		r.Eq("empty-queue-how-is-zero", how, 0);

		var exact = Pending(a, b, 100, 1.0);
		s.PendingHits.Add(exact);
		var got = s.ConsumePending(a, b, 100, 0, 1.0, out how);
		r.Same("exact-on-damage-returns-the-record", got, exact);
		r.Eq("exact-on-damage-how-is-1", how, 1);
		r.Eq("exact-on-damage-consumes-it", s.PendingHits.Count, 0);

		var viaNominal = Pending(a, b, 100, 1.0);
		s.PendingHits.Add(viaNominal);
		r.Same("exact-on-nominal-also-matches", s.ConsumePending(a, b, 0, 100, 1.0, out how), viaNominal);
		r.Eq("exact-on-nominal-how-is-1", how, 1);

		var pairOnly = Pending(a, b, 500, 1.0);
		s.PendingHits.Add(pairOnly);
		r.Same("neither-value-matches-pairs-by-target", s.ConsumePending(a, b, 100, 0, 1.0, out how), pairOnly);
		r.Eq("pair-match-how-is-2", how, 2);

		r.Group("window/expiry-boundary");
		var w = new BattleSession();
		var fresh = Pending(a, b, 100, 0.0);
		w.PendingHits.Add(fresh);
		r.Same("window-minus-epsilon-still-matches", w.ConsumePending(a, b, 100, 0, Math.BitDecrement(0.35), out how), fresh);
		var atEdge = Pending(a, b, 100, 0.0);
		w.PendingHits.Add(atEdge);
		r.Same("exactly-at-the-window-still-matches (>)", w.ConsumePending(a, b, 100, 0, 0.35, out how), atEdge);
		var justOver = Pending(a, b, 100, 0.0);
		w.PendingHits.Add(justOver);
		r.True("one-ulp-past-the-window-expires", w.ConsumePending(a, b, 100, 0, Math.BitIncrement(0.35), out how) == null);
		r.Eq("expired-record-is-not-consumed", w.PendingHits.Count, 1);
		r.Same("the-expired-record-is-still-the-same-one", w.PendingHits[0], justOver);
		r.Eq("expired-how-is-zero", how, 0);

		r.Group("window/candidate-order");
		var o = new BattleSession();
		var earlierPair = Pending(a, b, 999, 1.0);
		var laterExact = Pending(a, b, 100, 1.0);
		o.PendingHits.Add(earlierPair);
		o.PendingHits.Add(laterExact);
		r.Same("an-exact-match-beats-an-earlier-pair", o.ConsumePending(a, b, 100, 0, 1.0, out how), laterExact);
		r.Eq("beaten-pair-candidate-remains", o.PendingHits.Count, 1);
		r.Same("beaten-pair-candidate-is-the-pair-one", o.PendingHits[0], earlierPair);

		var p = new BattleSession();
		var firstPair = Pending(a, b, 999, 1.0);
		var secondPair = Pending(a, b, 888, 1.0);
		p.PendingHits.Add(firstPair);
		p.PendingHits.Add(secondPair);
		r.Same("no-exact-match-takes-the-first-pair", p.ConsumePending(a, b, 100, 0, 1.0, out how), firstPair);
		r.Eq("pair-take-how-is-2", how, 2);
		r.Same("the-second-pair-candidate-remains", p.PendingHits[0], secondPair);

		r.Group("window/identity-and-consumption");
		var i = new BattleSession();
		var wrongBlocker = Pending(a, other, 100, 1.0);
		var wrongAttacker = Pending(other, b, 100, 1.0);
		i.PendingHits.Add(wrongBlocker);
		i.PendingHits.Add(wrongAttacker);
		r.True("a-different-blocker-is-not-consumed", i.ConsumePending(a, b, 100, 0, 1.0, out how) == null);
		r.Eq("nothing-was-consumed", i.PendingHits.Count, 2);

		var c = new BattleSession();
		var once = Pending(a, b, 100, 1.0);
		c.PendingHits.Add(once);
		r.Same("first-consumption-succeeds", c.ConsumePending(a, b, 100, 0, 1.0, out how), once);
		r.True("a-consumed-record-cannot-be-consumed-again", c.ConsumePending(a, b, 100, 0, 1.0, out how) == null);
		r.Eq("second-consumption-how-is-zero", how, 0);
	}
}
