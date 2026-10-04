using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// RF4g: the attack snapshot. The rule worth pinning is that BOTH halves are required -- a calc without
	/// a stamp is exactly the 1.5.0 defect this container was extracted around (a previous battle's calc
	/// labelling this battle's hits, because the reference survived while nothing re-stamped it).
	/// </summary>
	public static void AttackSnapshotCases(Runner r)
	{
		var s = new AttackSnapshot<string>();
		r.Group("runtime/attack-snapshot");
		r.True("a-fresh-snapshot-is-not-valid", !s.Valid);
		r.True("a-fresh-snapshot-has-no-calc", s.Calc == null);
		r.EqD("and-its-stamp-is-the-minus-one-sentinel", s.At, -1.0);

		s.Begin("calc", 12.5);
		r.True("begun-is-valid", s.Valid);
		r.Str("begin-remembers-the-calc", s.Calc, "calc");
		r.EqD("and-its-stamp", s.At, 12.5);
		r.EqD("age-is-now-minus-the-stamp", s.Age(15.0), 2.5);

		// the two halves are independent requirements
		var noCalc = new AttackSnapshot<string>();
		noCalc.Begin(null, 5.0);
		r.True("a-null-calc-with-a-stamp-is-not-valid", !noCalc.Valid);
		var noStamp = new AttackSnapshot<string>();
		noStamp.Begin("calc", -1.0);
		r.True("a-calc-with-the-sentinel-stamp-is-not-valid", !noStamp.Valid);
		var zeroStamp = new AttackSnapshot<string>();
		zeroStamp.Begin("calc", 0.0);
		r.True("a-stamp-of-zero-counts (it is a real battle clock reading)", zeroStamp.Valid);

		s.Clear();
		r.True("clear-drops-the-calc", s.Calc == null);
		r.EqD("clear-resets-the-stamp", s.At, -1.0);
		r.True("and-clear-makes-it-invalid-again", !s.Valid);

		// no accumulation: a second Begin replaces both halves
		s.Begin("a", 1.0);
		s.Begin("b", 2.0);
		r.Str("a-second-begin-replaces-the-calc", s.Calc, "b");
		r.EqD("and-the-stamp", s.At, 2.0);
	}
}
