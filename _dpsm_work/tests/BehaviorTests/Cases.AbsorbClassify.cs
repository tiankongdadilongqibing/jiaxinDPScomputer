using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// R76: the OVERSIZED hit as its own judgement (`Policy/AbsorbClassifyPolicy.cs`).
	///
	/// Why the group was rewritten. R75 shipped a classifier built on the guess that `nominal - result` is an
	/// "absorbed" amount, and the first battle the probe ran in falsified it outright:
	///
	///     res == max(0, nominal - lifeBefore)      798 of 798 readable readings, zero violations
	///
	/// The return value is the OVERFLOW beyond the victim's remaining Life, so on an oversized hit the two
	/// published fields (`入耐久` = res, `被吸收` = nominal - res) are exactly swapped -- and R75's `Masked`
	/// bucket filed 790 rows whose life had moved by the whole nominal, i.e. hits that landed in full.
	///
	/// What this group now holds the policy to:
	///   1. the LIFE READING decides, so `lifeDrop == nominal` can never be a withholding again;
	///   2. an oversized hit reports its own split (`landed = nominal - res`, `overflow = res`) and is
	///      corroborated by the life movement before it is called `oversized` -- a fixed pool that did not move
	///      is labelled `oversizedPool` instead, and an unreadable life is its own verdict rather than a guess;
	///   3. a key row (an oversized hit or a carrier sighting) draws on its OWN budget, because in the measured
	///      battle the single 400-row cap pushed all 11 deciding rows into `dropped`.
	///
	/// The `[ABSPROBE]` text is PINNED, not merely "contains": its purpose is comparing two battles by eye.
	/// </summary>
	internal static void AbsorbClassifyCases(Runner r)
	{
		r.Group("policy/absorb-classify");

		// ---- the report line, and the oversized split it headlines --------------------------------

		var empty = new AbsorbProbeReport();
		r.Str("an-untouched-report-pins-every-bucket", empty.Describe(),
			"calls=0 ovz=0/0/0 pool=0 partial=0 ovzUnread=0 noMove=0 missing=0 lifeUnread=0"
			+ " carrier(barrier/pool/takeover/fixed/invincible/unreadable)=0/0/0/0/0/0"
			+ " seen(barrierDmg/addBarrier/takeover/fixed)=0/0/0/0 lifeMismatch=0 active=0 barrUnread=0"
			+ " first(nom/res/landed/overflow)=0/0/?/? rows=0 key=0 dropped=0 keyDropped=0");

		var d = new AbsorbProbeReport();
		d.Note(Live(600000, 100000, 500000, 0), AbsorbClassifyPolicy.Classify(Live(600000, 100000, 500000, 0)));
		d.Note(Live(500000, 400000, 500000, 500000), AbsorbClassifyPolicy.Classify(Live(500000, 400000, 500000, 500000)));
		d.Note(Live(1000, 0, 5000, 4000), AbsorbClassifyPolicy.Classify(Live(1000, 0, 5000, 4000)));
		d.Note(Live(1000, 0, 5000, 5000), AbsorbClassifyPolicy.Classify(Live(1000, 0, 5000, 5000)));
		d.Note(Plain(1000, 0), AbsorbClassifyPolicy.Classify(Plain(1000, 0)));
		d.Note(Live(1000, 0, 5000, 4500), AbsorbClassifyPolicy.Classify(Live(1000, 0, 5000, 4500)));
		d.Note(Live(800000, 300000, 500000, 300000), AbsorbClassifyPolicy.Classify(Live(800000, 300000, 500000, 300000)));
		r.Str("a-filled-report-pins-every-bucket", d.Describe(),
			"calls=7 ovz=1/1100000/800000 pool=1 partial=1 ovzUnread=0 noMove=1 missing=1 lifeUnread=1"
			+ " carrier(barrier/pool/takeover/fixed/invincible/unreadable)=0/0/0/0/0/0"
			+ " seen(barrierDmg/addBarrier/takeover/fixed)=0/0/0/0 lifeMismatch=2 active=0 barrUnread=0"
			+ " first(nom/res/landed/overflow)=600000/100000/500000/100000 rows=0 key=0 dropped=0 keyDropped=0");

		// The headline sums cover EVERY oversized call, not only the ones the life reading corroborated, so the
		// number does not flatter itself: 500,000+100,000+500,000 landed and 100,000+400,000+300,000 overflow.
		r.True("the-oversized-sums-cover-every-oversized-bucket",
			d.OversizedLandedTotal == 1100000L && d.OversizedOverflowTotal == 800000L
			&& d.Oversized == 1 && d.OversizedPool == 1 && d.OversizedPartial == 1);

		r.True("the-first-quad-is-the-first-oversized-hit",
			d.HasFirst && d.FirstNominal == 600000 && d.FirstResult == 100000
			&& d.FirstLanded == 500000 && d.FirstOverflow == 100000);

		// A life movement that is not `nominal - res` on an oversized hit is a reading in its own right.
		r.True("the-life-mismatch-counts-only-oversized-rows", d.LifeMismatch == 2);

		d.Clear();
		r.Str("clear-empties-every-bucket", d.Describe(),
			"calls=0 ovz=0/0/0 pool=0 partial=0 ovzUnread=0 noMove=0 missing=0 lifeUnread=0"
			+ " carrier(barrier/pool/takeover/fixed/invincible/unreadable)=0/0/0/0/0/0"
			+ " seen(barrierDmg/addBarrier/takeover/fixed)=0/0/0/0 lifeMismatch=0 active=0 barrUnread=0"
			+ " first(nom/res/landed/overflow)=0/0/?/? rows=0 key=0 dropped=0 keyDropped=0");
		r.True("clear-drops-the-first-quad", !d.HasFirst);

		// ---- the life law replaces the old `Masked` bucket -----------------------------------------

		// `res == 0` with the life moving by the WHOLE nominal: the hit landed in full. R75 called this
		// `Masked` ("entirely withheld, booked as full damage") on 790 rows -- the opposite of the truth.
		r.True("a-hit-that-landed-in-full-is-not-withheld",
			AbsorbClassifyPolicy.Classify(Live(781, 0, 427761, 426980)) == AbsorbVerdict.None);
		r.True("a-return-that-is-not-an-overflow-withholds-nothing",
			AbsorbClassifyPolicy.Classify(Plain(1000, 1000)) == AbsorbVerdict.None);
		r.True("nothing-to-judge-is-none",
			AbsorbClassifyPolicy.Classify(Plain(0, 0)) == AbsorbVerdict.None);

		// `res > 0` is the definition of an oversized hit, and the split is its own two numbers.
		var ovz = Live(600000, 100000, 500000, 0);
		r.True("an-oversized-hit-is-recognised-by-its-own-return",
			ovz.IsOversized() && ovz.Landed() == 500000 && ovz.Overflow() == 100000);
		r.True("an-oversized-hit-whose-life-moved-by-the-landed-amount-is-corroborated",
			AbsorbClassifyPolicy.Classify(ovz) == AbsorbVerdict.Oversized);

		// The `ショゴス` shape: an oversized hit whose victim is a fixed pool (Life stays 500,000) is NOT
		// lumped in with the corroborated ones -- the life reading cannot vouch for the split there.
		r.True("an-oversized-hit-on-a-fixed-pool-is-labelled-separately",
			AbsorbClassifyPolicy.Classify(Live(577331, 77331, 500000, 500000)) == AbsorbVerdict.OversizedPool);

		r.True("an-oversized-hit-whose-life-moved-differently-is-not-corroborated",
			AbsorbClassifyPolicy.Classify(Live(800000, 300000, 500000, 400000)) == AbsorbVerdict.OversizedPartial);

		// "We could not look" is an answer: never `None` (which would claim the hit landed) and never a split
		// presented as measured.
		r.True("an-oversized-hit-with-an-unreadable-life-is-not-corroborated",
			AbsorbClassifyPolicy.Classify(Plain(600000, 100000)) == AbsorbVerdict.OversizedUnreadable);

		// ---- the other three shapes, each named separately -----------------------------------------

		r.True("a-hit-that-moved-nothing-is-not-withheld",
			AbsorbClassifyPolicy.Classify(Live(1000, 0, 5000, 5000)) == AbsorbVerdict.NoLifeMovement);

		// This -- not `res <= 0` -- is the shape that would actually justify the word 被吸收: part of the hit
		// went missing and the return value does not say so.
		r.True("a-partial-movement-without-a-return-is-named",
			AbsorbClassifyPolicy.Classify(Live(1000, 0, 5000, 4500)) == AbsorbVerdict.WithheldNoReturn);

		r.True("an-unreadable-life-is-not-a-withheld-hit",
			AbsorbClassifyPolicy.Classify(Plain(1000, 0)) == AbsorbVerdict.LifeUnreadable);
		r.True("a-life-that-rose-is-not-a-withholding",
			AbsorbClassifyPolicy.Classify(Live(1000, 0, 5000, 6000)) == AbsorbVerdict.None);

		// ---- carriers are consulted only when the life law does not explain the hit -----------------

		var withCarrier = Live(800000, 300000, 500000, 400000);
		withCarrier.TakeOverSeen = true;
		r.True("a-carrier-is-consulted-when-the-life-law-does-not-explain-the-hit",
			AbsorbClassifyPolicy.Classify(withCarrier) == AbsorbVerdict.TakeOver);

		var both = Live(800000, 300000, 500000, 400000);
		both.TakeOverSeen = true;
		both.FixedDamageSeen = true;
		r.True("a-fixed-damage-reading-outranks-a-takeover",
			AbsorbClassifyPolicy.Classify(both) == AbsorbVerdict.FixedDamage);

		var bar = Live(800000, 300000, 500000, 400000);
		bar.BarrierActiveBefore = true;
		bar.BarrierReadable = true;
		bar.BarrierLifeBefore = 500000;
		bar.BarrierLifeAfter = 0;
		r.True("an-active-barrier-that-moved-by-the-landed-amount-names-the-carrier",
			AbsorbClassifyPolicy.Classify(bar) == AbsorbVerdict.Barrier);

		var pool = Live(800000, 300000, 500000, 400000);
		pool.BarrierActiveBefore = true;
		pool.BarrierReadable = true;
		pool.BarrierLifeBefore = 500000;
		pool.BarrierLifeAfter = 300000;
		r.True("a-barrier-that-moved-by-less-is-a-pool",
			AbsorbClassifyPolicy.Classify(pool) == AbsorbVerdict.BarrierShort);

		// The polarity of the barrier's own life is NOT established (a remaining-absorb pool would fall, an
		// absorbed-so-far counter would rise), so the comparison is a magnitude and the verdict must not
		// depend on which way it moved.
		var rising = Live(800000, 300000, 500000, 400000);
		rising.BarrierActiveBefore = true;
		rising.BarrierReadable = true;
		rising.BarrierLifeBefore = 0;
		rising.BarrierLifeAfter = 500000;
		r.True("the-barrier-lifes-polarity-does-not-decide-the-verdict",
			AbsorbClassifyPolicy.Classify(rising) == AbsorbVerdict.Barrier);

		var barUnknown = Live(800000, 300000, 500000, 400000);
		barUnknown.BarrierActiveBefore = true;
		r.True("an-active-barrier-that-could-not-be-read-is-not-a-carrier",
			AbsorbClassifyPolicy.Classify(barUnknown) == AbsorbVerdict.Unreadable);

		var inv = Live(800000, 300000, 500000, 400000);
		inv.InvincibleFlag = true;
		r.True("an-invincibility-flag-is-named-when-nothing-else-fits",
			AbsorbClassifyPolicy.Classify(inv) == AbsorbVerdict.Invincible);

		r.True("nothing-observed-is-not-corroborated",
			AbsorbClassifyPolicy.Classify(Live(800000, 300000, 500000, 400000)) == AbsorbVerdict.OversizedPartial);

		// ---- the row budget (requirement 2 of R76) -------------------------------------------------

		var b = new AbsorbProbeReport();
		bool o1 = b.TryTakeRow(AbsorbVerdict.None, 2, 1);
		bool o2 = b.TryTakeRow(AbsorbVerdict.NoLifeMovement, 2, 1);
		bool o3 = b.TryTakeRow(AbsorbVerdict.None, 2, 1);            // refused by the ordinary cap
		bool k1 = b.TryTakeRow(AbsorbVerdict.Oversized, 2, 1);       // a key row is still written
		bool k2 = b.TryTakeRow(AbsorbVerdict.OversizedPool, 2, 1);   // refused by the KEY cap
		r.True("a-key-row-is-written-past-the-ordinary-cap", o1 && o2 && !o3 && k1 && !k2);
		r.True("the-two-refusals-are-counted-apart",
			b.Rows == 3 && b.KeyRows == 1 && b.Dropped == 1 && b.KeyDropped == 1);
		r.True("only-the-oversized-and-carrier-verdicts-are-key",
			AbsorbProbeReport.IsKeyVerdict(AbsorbVerdict.Oversized)
			&& AbsorbProbeReport.IsKeyVerdict(AbsorbVerdict.OversizedPool)
			&& AbsorbProbeReport.IsKeyVerdict(AbsorbVerdict.Barrier)
			&& AbsorbProbeReport.IsKeyVerdict(AbsorbVerdict.TakeOver)
			&& !AbsorbProbeReport.IsKeyVerdict(AbsorbVerdict.None)
			&& !AbsorbProbeReport.IsKeyVerdict(AbsorbVerdict.NoLifeMovement)
			&& !AbsorbProbeReport.IsKeyVerdict(AbsorbVerdict.LifeUnreadable));

		// ---- the names ------------------------------------------------------------------------------

		r.True("the-verdict-names-are-stable",
			AbsorbClassifyPolicy.Name(AbsorbVerdict.None) == "none"
			&& AbsorbClassifyPolicy.Name(AbsorbVerdict.Oversized) == "oversized"
			&& AbsorbClassifyPolicy.Name(AbsorbVerdict.OversizedPool) == "oversizedPool"
			&& AbsorbClassifyPolicy.Name(AbsorbVerdict.OversizedPartial) == "oversizedPartial"
			&& AbsorbClassifyPolicy.Name(AbsorbVerdict.OversizedUnreadable) == "oversizedUnreadable"
			&& AbsorbClassifyPolicy.Name(AbsorbVerdict.NoLifeMovement) == "noLifeMovement"
			&& AbsorbClassifyPolicy.Name(AbsorbVerdict.WithheldNoReturn) == "withheldNoReturn"
			&& AbsorbClassifyPolicy.Name(AbsorbVerdict.LifeUnreadable) == "lifeUnreadable"
			&& AbsorbClassifyPolicy.Name(AbsorbVerdict.Barrier) == "barrier"
			&& AbsorbClassifyPolicy.Name(AbsorbVerdict.BarrierShort) == "pool"
			&& AbsorbClassifyPolicy.Name(AbsorbVerdict.TakeOver) == "takeover"
			&& AbsorbClassifyPolicy.Name(AbsorbVerdict.FixedDamage) == "fixed"
			&& AbsorbClassifyPolicy.Name(AbsorbVerdict.Invincible) == "invincible"
			&& AbsorbClassifyPolicy.Name(AbsorbVerdict.Unreadable) == "unreadable");
		r.True("a-verdict-outside-the-enum-prints-as-a-question-mark",
			AbsorbClassifyPolicy.Name((AbsorbVerdict)999) == "?");
	}

	/// <summary>A call with no readings around it (life and barrier unreadable): the shape the policy must
	/// answer "we did not look" for.</summary>
	private static AbsorbObservation Plain(int nominal, int result)
	{
		AbsorbObservation o = new AbsorbObservation();
		o.Nominal = nominal;
		o.Result = result;
		return o;
	}

	/// <summary>A call with a readable life movement: `before - after` is what the classifier compares against
	/// the nominal (full application) and against `nominal - res` (an oversized hit's split).</summary>
	private static AbsorbObservation Live(int nominal, int result, int lifeBefore, int lifeAfter)
	{
		AbsorbObservation o = Plain(nominal, result);
		o.LifeBefore = lifeBefore;
		o.LifeAfter = lifeAfter;
		o.LifeReadable = true;
		return o;
	}
}
