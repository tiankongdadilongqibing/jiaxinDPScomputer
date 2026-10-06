using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// R75: which mechanism withheld part of a damage-application call (`Policy/AbsorbClassifyPolicy.cs`).
	///
	/// Why this group exists. `被吸收/无效化` has been published as `nominal - damage` since 1.5.5 and was then
	/// read back as if it named a mechanism: 397 records over 46 exports, 395 of them exactly 500,000, all on
	/// ショゴス -- and a shield was written into the notes on the strength of that number alone, while no
	/// ability, buff, status, master-data field or value ever carried it. The classifier that replaces that
	/// inference therefore has two jobs the suite has to hold it to:
	///
	///   1. an OBSERVED carrier is named, and the decision order that does the naming is pinned (a fixed-damage
	///      reading beats a barrier reading, an invincibility flag beats a takeover);
	///   2. an UNOBSERVED one is NOT invented. `CarrierUnknown` and `Unreadable` are answers, and a failed
	///      reading must never be able to come back as `Barrier`.
	///
	/// The `[ABSPROBE]` text is PINNED, not merely "contains": its whole purpose is comparing two battles by
	/// eye, and the blind spot it exists to expose (`masked=`, the fully withheld hits the existing accounting
	/// books as full damage) is a number that has to be visible in every battle's line.
	/// </summary>
	internal static void AbsorbClassifyCases(Runner r)
	{
		r.Group("policy/absorb-classify");

		// ---- the report line ----------------------------------------------------------------------

		var empty = new AbsorbProbeReport();
		r.Str("an-untouched-report-pins-every-bucket", empty.Describe(),
			"calls=0 withheld=0 sum=0 masked=0/0 lifeMismatch=0"
			+ " verdict(barrier/pool/unknown/takeover/fixed/invincible/unreadable)=0/0/0/0/0/0/0"
			+ " seen(barrierDmg/addBarrier/takeover/fixed)=0/0/0/0 active=0 unreadable=0"
			+ " first(nom/res/life/bar)=0/0/?/? rows=0 dropped=0");

		var d = new AbsorbProbeReport();
		d.Note(Barrier(1103327, 603327, 1000000, 396673, 500000, 0, true), AbsorbClassifyPolicy.Classify(
			Barrier(1103327, 603327, 1000000, 396673, 500000, 0, true)));
		d.Note(Plain(735551, 235551), AbsorbClassifyPolicy.Classify(Plain(735551, 235551)));
		d.Note(Plain(500000, 0), AbsorbClassifyPolicy.Classify(Plain(500000, 0)));
		d.Note(Plain(1000, 1000), AbsorbClassifyPolicy.Classify(Plain(1000, 1000)));
		AbsorbObservation mismatched = Plain(4000, 3000);
		mismatched.LifeBefore = 5000;
		mismatched.LifeAfter = 1500;
		mismatched.LifeReadable = true;
		d.Note(mismatched, AbsorbClassifyPolicy.Classify(mismatched));
		d.NoteRow();
		d.NoteRow();
		d.NoteDropped();
		r.Str("a-filled-report-pins-every-bucket", d.Describe(),
			"calls=5 withheld=3 sum=1001000 masked=1/500000 lifeMismatch=1"
			+ " verdict(barrier/pool/unknown/takeover/fixed/invincible/unreadable)=1/0/2/0/0/0/0"
			+ " seen(barrierDmg/addBarrier/takeover/fixed)=0/0/0/0 active=1 unreadable=0"
			+ " first(nom/res/life/bar)=1103327/603327/603327/500000 rows=2 dropped=1");

		// The masked bucket is NOT part of `withheld`: a fully withheld hit is exactly the case the existing
		// accounting cannot show (it books `result <= 0` as full damage), so folding it into the withheld sum
		// would report it as a hit that was partly applied.
		var m = new AbsorbProbeReport();
		m.Note(Plain(500000, 0), AbsorbClassifyPolicy.Classify(Plain(500000, 0)));
		r.True("masked-is-counted-apart-from-withheld", m.Masked == 1 && m.MaskedTotal == 500000L && m.Withheld == 0 && m.WithheldTotal == 0L);

		// Only the FIRST withheld hit is kept, and it is kept with all four numbers: that quad is what a single
		// battle's log has to settle the carrier with.
		r.True("the-first-withheld-hit-is-the-one-kept",
			d.HasFirst && d.FirstNominal == 1103327 && d.FirstResult == 603327
			&& d.FirstLifeDrop == 603327 && d.FirstBarrierMove == 500000);

		// An unreadable first reading prints as `?` in the quad, never as 0 -- "could not read it" and "it read
		// zero" are different facts, and a printed 0 here would look like a barrier that moved by nothing.
		var q = new AbsorbProbeReport();
		q.Note(Plain(700, 200), AbsorbClassifyPolicy.Classify(Plain(700, 200)));
		r.True("an-unreadable-first-reading-prints-as-a-question-mark",
			q.Describe().Contains("first(nom/res/life/bar)=700/200/?/?"));

		// A life movement that is not the return value is a reading in its own right: it decides whether the
		// return really is "the damage that reached 耐久".
		r.True("a-life-drop-that-differs-from-the-return-is-counted",
			d.LifeMismatch == 1 && m.LifeMismatch == 0);

		// One battle, one set of counters.
		d.Clear();
		r.Str("clear-empties-every-bucket", d.Describe(),
			"calls=0 withheld=0 sum=0 masked=0/0 lifeMismatch=0"
			+ " verdict(barrier/pool/unknown/takeover/fixed/invincible/unreadable)=0/0/0/0/0/0/0"
			+ " seen(barrierDmg/addBarrier/takeover/fixed)=0/0/0/0 active=0 unreadable=0"
			+ " first(nom/res/life/bar)=0/0/?/? rows=0 dropped=0");
		r.True("clear-drops-the-first-quad", !d.HasFirst);

		// ---- the decision -------------------------------------------------------------------------

		r.True("a-hit-that-withheld-nothing-is-not-a-carrier",
			AbsorbClassifyPolicy.Classify(Plain(1000, 1000)) == AbsorbVerdict.None);

		// A fully withheld hit arrives as `result <= 0` and the existing accounting books it as FULL damage;
		// this verdict is the only thing that makes it countable at all.
		r.True("a-fully-withheld-hit-is-masked",
			AbsorbClassifyPolicy.Classify(Plain(500000, 0)) == AbsorbVerdict.Masked);
		r.True("a-negative-return-is-masked-too",
			AbsorbClassifyPolicy.Classify(Plain(500000, -7)) == AbsorbVerdict.Masked);

		// The barrier is named from its OWN movement, and the comparison is a magnitude: whether `mLife` counts
		// the pool that is LEFT or the amount taken so far is not established, and the verdict must not depend
		// on a guess about the polarity.
		r.True("a-barrier-that-moved-by-the-withheld-amount-names-the-carrier",
			AbsorbClassifyPolicy.Classify(Barrier(1103327, 603327, 1000000, 396673, 500000, 0, true)) == AbsorbVerdict.Barrier);
		r.True("the-barrier-lifes-polarity-does-not-decide-the-verdict",
			AbsorbClassifyPolicy.Classify(Barrier(1103327, 603327, 1000000, 396673, 0, 500000, true)) == AbsorbVerdict.Barrier);

		// A pool that ran out INSIDE the hit: the barrier moved, just not by enough. Kept apart from `Barrier`
		// because this is the one shape that separates "a pool of N" from "N per hit".
		r.True("a-barrier-that-moved-by-less-is-a-pool",
			AbsorbClassifyPolicy.Classify(Barrier(1103327, 603327, 1000000, 396673, 500000, 300000, true)) == AbsorbVerdict.BarrierShort);

		// Active but unmoved, and active but unreadable: NEITHER may come back as a carrier. This is the rule
		// that keeps a failed read from being published as an absorption.
		r.True("an-active-barrier-whose-life-did-not-move-is-not-a-carrier",
			AbsorbClassifyPolicy.Classify(Barrier(1103327, 603327, 1000000, 396673, 500000, 500000, true)) == AbsorbVerdict.Unreadable);
		r.True("an-active-barrier-that-could-not-be-read-is-not-a-carrier",
			AbsorbClassifyPolicy.Classify(Barrier(1103327, 603327, 1000000, 396673, 0, 0, false)) == AbsorbVerdict.Unreadable);

		// `Barrier.Damage` running is a sighting in its own right: a barrier can take part in a hit without the
		// `IsActived` flag having been read as true beforehand (order of the readings is not guaranteed), so the
		// two routes are ORed -- and that OR needs its own case or the second route is never executed.
		AbsorbObservation sighting = Barrier(1103327, 603327, 1000000, 396673, 500000, 0, true);
		sighting.BarrierActiveBefore = false;
		sighting.BarrierDamageSeen = true;
		r.True("a-barrier-damage-sighting-names-the-carrier-without-the-active-flag",
			AbsorbClassifyPolicy.Classify(sighting) == AbsorbVerdict.Barrier);

		// Nothing observed => say so. A constant value arriving in THIS bucket is the evidence for a flat
		// per-hit cut that no exported table carries -- and it is not evidence for a shield.
		r.True("a-withheld-hit-with-no-carrier-observed-says-so",
			AbsorbClassifyPolicy.Classify(Plain(1103327, 603327)) == AbsorbVerdict.CarrierUnknown);

		// Order of the named carriers, pinned: a fixed-damage reading outranks a barrier reading, and an
		// invincibility flag outranks a takeover.
		AbsorbObservation both = Barrier(1103327, 603327, 1000000, 396673, 500000, 0, true);
		both.FixedDamageSeen = true;
		r.True("a-fixed-damage-reading-outranks-a-barrier",
			AbsorbClassifyPolicy.Classify(both) == AbsorbVerdict.FixedDamage);

		AbsorbObservation inv = Plain(1103327, 603327);
		inv.InvincibleFlag = true;
		inv.TakeOverSeen = true;
		r.True("an-invincibility-flag-outranks-takeover",
			AbsorbClassifyPolicy.Classify(inv) == AbsorbVerdict.Invincible);

		AbsorbObservation to = Plain(1103327, 603327);
		to.TakeOverSeen = true;
		r.True("a-takeover-reading-is-named", AbsorbClassifyPolicy.Classify(to) == AbsorbVerdict.TakeOver);

		// The names are shared by the log line and the case labels, so they are pinned here rather than typed
		// twice.
		r.True("the-verdict-names-are-stable",
			AbsorbClassifyPolicy.Name(AbsorbVerdict.None) == "none"
			&& AbsorbClassifyPolicy.Name(AbsorbVerdict.Barrier) == "barrier"
			&& AbsorbClassifyPolicy.Name(AbsorbVerdict.BarrierShort) == "pool"
			&& AbsorbClassifyPolicy.Name(AbsorbVerdict.CarrierUnknown) == "unknown"
			&& AbsorbClassifyPolicy.Name(AbsorbVerdict.TakeOver) == "takeover"
			&& AbsorbClassifyPolicy.Name(AbsorbVerdict.FixedDamage) == "fixed"
			&& AbsorbClassifyPolicy.Name(AbsorbVerdict.Invincible) == "invincible"
			&& AbsorbClassifyPolicy.Name(AbsorbVerdict.Masked) == "masked"
			&& AbsorbClassifyPolicy.Name(AbsorbVerdict.Unreadable) == "unreadable");
		r.True("a-verdict-outside-the-enum-prints-as-a-question-mark",
			AbsorbClassifyPolicy.Name((AbsorbVerdict)999) == "?");
	}

	/// <summary>A call with nothing read around it (life and barrier unreadable): the shape the classifier
	/// must answer "we did not look" for.</summary>
	private static AbsorbObservation Plain(int nominal, int result)
	{
		AbsorbObservation o = new AbsorbObservation();
		o.Nominal = nominal;
		o.Result = result;
		return o;
	}

	/// <summary>A call whose victim carried a barrier: life movement given explicitly so the caller can make
	/// the movement equal, smaller than or larger than the withheld amount.</summary>
	private static AbsorbObservation Barrier(int nominal, int result, int lifeBefore, int lifeAfter,
		int barBefore, int barAfter, bool barReadable)
	{
		AbsorbObservation o = Plain(nominal, result);
		o.LifeBefore = lifeBefore;
		o.LifeAfter = lifeAfter;
		o.LifeReadable = true;
		o.BarrierActiveBefore = true;
		o.BarrierLifeBefore = barBefore;
		o.BarrierLifeAfter = barAfter;
		o.BarrierReadable = barReadable;
		return o;
	}
}
