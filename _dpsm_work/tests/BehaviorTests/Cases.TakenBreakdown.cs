using System.Collections.Generic;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// R79: the 受击来源拆分 projection (`Policy/TakenBreakdownPolicy.cs`) -- the layer that turns the damage
	/// events into "what was this unit's incoming damage made of".
	///
	/// The point of the whole section is that its dimensions PARTITION one total, so the cases below are mostly
	/// arithmetic: five dimensions, each summing to the same nominal, and the two amounts that belong to no
	/// attacker bucket (same-team damage, unresolvable attacker) accounted for rather than dropped. A breakdown
	/// whose buckets do not add up is worse than no breakdown: it reads as a measurement.
	/// </summary>
	internal static void TakenBreakdownCases(Runner r)
	{
		r.Group("policy/taken-breakdown");

		// ---- the dimensions partition the same total --------------------------------------------------
		var hits = new List<TakenHit>();
		TakenHit a = TkHit(7, "甲", 100, 100);
		a.Source = 1; a.HitType = 1; a.EffectId = 0;
		hits.Add(a);
		TakenHit b = TkHit(7, "甲", 100, 100);
		b.Source = 1; b.HitType = 2; b.EffectId = 77;
		hits.Add(b);
		TakenHit c = TkHit(7, "甲", 100, 40);
		c.Source = 4; c.HitType = -1; c.EffectId = 77;   // -1 = the damage path's unreadable sentinel
		hits.Add(c);

		TakenBreakdown t = TakenBreakdownPolicy.Build(hits, 1);
		r.Eq("one-victim-is-one-actor-row", t.Actors.Count, 1);
		r.Eq("the-victim-total-is-the-nominal-sum", t.Nominal, 300);
		r.Eq("the-published-total-is-the-amount-sum", t.Taken, 240);
		r.Eq("the-residual-is-nominal-minus-published", t.Residual, 60);

		TakenActor va = t.Actors[0];
		r.Eq("by-source-sums-to-nominal", TkSum(va.BySource), va.Nominal);
		r.Eq("by-hittype-sums-to-nominal", TkSum(va.ByHitType), va.Nominal);
		r.Eq("by-effect-sums-to-nominal", TkSum(va.ByEffect), va.Nominal);
		r.Eq("by-attacker-sums-to-nominal", TkSum(va.ByAttacker), va.Nominal);
		r.Eq("nominal-equals-published-plus-residual", va.Taken + va.Residual, va.Nominal);
		r.Eq("the-two-odd-amounts-close-the-attacker-dimension",
			TkSum(va.ByAttacker) + va.Friendly + va.Unknown, va.Nominal);

		// the labels come from the shared map, not a second copy of it
		r.Str("a-source-bucket-uses-the-shared-label",
			TkFind(va.BySource, 1).Name, DamageSourceLabelPolicy.Source(1));
		r.Str("an-unreadable-hit-type-has-its-own-word",
			TkFind(va.ByHitType, -1).Name, "未识别");
		r.Str("an-effect-bucket-is-named-by-its-id",
			TkFind(va.ByEffect, 77).Name, "效果77");
		r.Str("an-absent-effect-is-not-called-effect-0",
			TkFind(va.ByEffect, 0).Name, "未识别");
		r.Eq("a-hit-without-an-ailment-adds-no-status-row", va.ByStatus.Count, 0);

		// ---- an unresolvable attacker is a stated gap, never a bucket ---------------------------------
		TakenHit u = TkHit(7, "甲", 500, 500);
		u.AttackerKey = 0; u.Attacker = ""; u.Attr = "?";
		r.True("no-attacker-key-means-unknown", TakenBreakdownPolicy.AttackerUnknown(u));
		TakenHit u2 = TkHit(7, "甲", 1, 1);
		u2.Attacker = "?";
		r.True("a-question-mark-name-means-unknown", TakenBreakdownPolicy.AttackerUnknown(u2));
		TakenHit u3 = TkHit(7, "甲", 1, 1);
		u3.Attr = "?";
		r.True("a-question-mark-attr-means-unknown", TakenBreakdownPolicy.AttackerUnknown(u3));
		r.True("a-resolved-attacker-is-not-unknown", !TakenBreakdownPolicy.AttackerUnknown(TkHit(7, "甲", 1, 1)));

		var uh = new List<TakenHit> { u };
		TakenBreakdown tu = TakenBreakdownPolicy.Build(uh, 1);
		r.Eq("an-unknown-attacker-is-counted-as-unknown", tu.Unknown, 500);
		r.Eq("its-hit-is-counted-too", tu.UnknownHits, 1);
		r.Eq("it-gets-no-attacker-bucket", tu.Actors[0].ByAttacker.Count, 0);
		r.Eq("but-it-is-still-part-of-the-victims-nominal", tu.Actors[0].Nominal, 500);

		// ---- same-team damage reaches the source dimension but never an enemy source -------------------
		TakenHit f = TkHit(7, "甲", 300, 300);
		f.Friendly = true; f.Source = 7;
		TakenBreakdown tf = TakenBreakdownPolicy.Build(new List<TakenHit> { f }, 1);
		TakenActor fa = tf.Actors[0];
		r.Eq("friendly-damage-is-its-own-total", tf.Friendly, 300);
		r.Eq("friendly-hits-are-counted", tf.FriendlyHits, 1);
		r.Eq("friendly-damage-has-no-attacker-bucket", fa.ByAttacker.Count, 0);
		r.Eq("friendly-damage-still-has-a-source-bucket", TkSum(fa.BySource), 300);
		r.Eq("friendly-damage-is-not-unknown", fa.Unknown, 0);

		// ---- the approximate marker is silence only when every hit was authoritative --------------------
		TakenHit be = TkHit(7, "甲", 100, 100);
		be.Attr = "C:calc";   // resolved from recent calc activity: best effort, not the objects themselves
		TakenBreakdown tb = TakenBreakdownPolicy.Build(new List<TakenHit> { be }, 1);
		r.Str("a-best-effort-attacker-bucket-says-so",
			TkFind(tb.Actors[0].ByAttacker, 10).Quality, "近似 0/1");
		TakenHit ok = TkHit(7, "甲", 100, 100);
		TakenBreakdown tok = TakenBreakdownPolicy.Build(new List<TakenHit> { ok }, 1);
		r.Str("an-authoritative-attacker-bucket-stays-silent",
			TkFind(tok.Actors[0].ByAttacker, 10).Quality, "");
		// the calc-based dimensions ride on HitMatch: only a value-exact record is authoritative
		TakenHit nr = TkHit(7, "甲", 100, 100);
		nr.HitMatch = 2;
		TakenBreakdown tnr = TakenBreakdownPolicy.Build(new List<TakenHit> { nr }, 1);
		r.Str("a-label-without-a-value-exact-record-is-marked",
			TkFind(tnr.Actors[0].BySource, 1).Quality, "近似 0/1");
		TakenHit vr = TkHit(7, "甲", 100, 100);
		vr.HitMatch = 1;
		TakenBreakdown tvr = TakenBreakdownPolicy.Build(new List<TakenHit> { vr }, 1);
		r.Str("a-value-exact-record-is-not-marked",
			TkFind(tvr.Actors[0].BySource, 1).Quality, "");

		// ---- R80: a dimension has NO bucket cap, and the new dictionary index still merges equal keys ----
		// R79 stopped at 64 buckets and folded the tail into 其他来源(N 项/M 击), which truncated the export
		// as well as the page. The page's whole purpose is the per-source breakdown, so the cap is gone.
		var many = new List<TakenHit>();
		for (int i = 0; i < 200; i++)
		{
			TakenHit h = TkHit(7, "甲", 1, 1);
			h.Source = i;
			many.Add(h);
		}
		TakenBreakdown tm = TakenBreakdownPolicy.Build(many, 1);
		TakenActor ma = tm.Actors[0];
		r.Eq("every-distinct-source-gets-its-own-bucket", ma.BySource.Count, 200);
		r.Eq("an-uncapped-dimension-still-sums-to-the-total", TkSum(ma.BySource), ma.Nominal);
		r.True("no-folded-tail-row-exists-any-more", TkFind(ma.BySource, int.MinValue) == null);

		// The lookup is a dictionary now (R79 scanned the list per hit: O(hits x buckets), which 200+
		// buckets made the dominant cost). A repeated key must still land in exactly ONE bucket, and the
		// index must stay per victim -- one victim's buckets must never absorb another's.
		var repeat = new List<TakenHit>();
		for (int i = 0; i < 50; i++)
		{
			TakenHit h1 = TkHit(7, "甲", 2, 2);
			h1.Source = 3;
			repeat.Add(h1);
			TakenHit h2 = TkHit(9, "乙", 5, 5);
			h2.Source = 4;
			repeat.Add(h2);
		}
		TakenBreakdown tr = TakenBreakdownPolicy.Build(repeat, 1);
		TakenActor rb = tr.Actors[0];   // 乙: 50 x 5
		TakenActor ra = tr.Actors[1];   // 甲: 50 x 2
		r.Eq("a-repeated-key-still-merges-into-one-bucket", ra.BySource.Count, 1);
		r.Eq("the-merged-bucket-carries-every-hit", TkFind(ra.BySource, 3).Hits, 50);
		r.Eq("the-merged-bucket-carries-their-amount", TkFind(ra.BySource, 3).Amount, 100);
		r.Eq("another-victim-keeps-its-own-buckets", rb.BySource.Count, 1);
		r.Eq("and-its-own-amount", TkFind(rb.BySource, 4).Amount, 250);
		r.Eq("the-index-does-not-leak-across-victims", ra.BySource[0].Key, 3);

		// ---- ordering is by size, never by event order -------------------------------------------------
		var mixed = new List<TakenHit>();
		mixed.Add(TkHit(3, "小", 10, 10));
		mixed.Add(TkHit(9, "大", 900, 900));
		mixed.Add(TkHit(5, "中", 100, 100));
		TakenBreakdown ts = TakenBreakdownPolicy.Build(mixed, 1);
		r.Str("victims-are-biggest-first", ts.Actors[0].Name, "大");
		r.Str("then-the-middle-one", ts.Actors[1].Name, "中");
		r.Str("then-the-smallest", ts.Actors[2].Name, "小");
		// A tie is broken by key, so two runs over the same events cannot print two orders.
		var tie = new List<TakenHit>();
		tie.Add(TkHit(8, "后键", 50, 50));
		tie.Add(TkHit(2, "前键", 50, 50));
		TakenBreakdown tt = TakenBreakdownPolicy.Build(tie, 1);
		r.Str("a-tie-is-broken-by-key-not-by-arrival", tt.Actors[0].Name, "前键");

		// ---- the position snapshot: a missing read never erases a known one ----------------------------
		r.Str("position-1-is-the-front", TakenBreakdownPolicy.PositionLabel(1), "前衛");
		r.Str("position-2-is-the-back", TakenBreakdownPolicy.PositionLabel(2), "後衛");
		r.Str("position-0-is-stated-as-unknown", TakenBreakdownPolicy.PositionLabel(0), "站位未知");
		r.Str("an-out-of-range-position-is-not-invented", TakenBreakdownPolicy.PositionLabel(99), "站位未知");
		var walked = new List<TakenHit>();
		TakenHit p1 = TkHit(7, "甲", 10, 10);
		p1.Position = 1;
		walked.Add(p1);
		TakenHit p0 = TkHit(7, "甲", 10, 10);
		p0.Position = 0;
		walked.Add(p0);
		TakenBreakdown tp = TakenBreakdownPolicy.Build(walked, 1);
		r.Eq("a-later-unreadable-position-keeps-the-first-read", tp.Actors[0].Position, 1);

		// ---- the team comes from the event, never from the name ----------------------------------------
		var sides = new List<TakenHit>();
		TakenHit e1 = TkHit(7, "同名", 10, 10);
		e1.VictimTeam = 2;
		sides.Add(e1);
		TakenBreakdown tside = TakenBreakdownPolicy.Build(sides, 1);
		r.True("a-team-2-victim-is-not-ours", !tside.Actors[0].Ally);
		r.Eq("and-its-team-is-kept-verbatim", tside.Actors[0].Team, 2);

		// ---- an ailment carries the applier the GAME credited ------------------------------------------
		var dots = new List<TakenHit>();
		TakenHit d1 = TkHit(7, "甲", 100, 100);
		d1.Status = "毒"; d1.StatusApplier = "乙";
		dots.Add(d1);
		TakenHit d2 = TkHit(7, "甲", 50, 50);
		d2.Status = "毒"; d2.StatusApplier = "乙";
		dots.Add(d2);
		TakenHit d3 = TkHit(7, "甲", 25, 25);
		d3.Status = "毒"; d3.StatusApplier = "丙";
		dots.Add(d3);
		TakenBreakdown td = TakenBreakdownPolicy.Build(dots, 1);
		r.Eq("the-same-ailment-from-two-appliers-are-two-rows", td.Actors[0].ByStatus.Count, 2);
		r.Str("the-applier-is-part-of-the-key", td.Actors[0].ByStatus[0].Applier, "乙");
		r.Eq("the-heavy-applier-is-first", td.Actors[0].ByStatus[0].Amount, 150);
		r.Eq("the-other-one-is-second", td.Actors[0].ByStatus[1].Amount, 25);

		r.Eq("an-empty-input-makes-an-empty-section", TakenBreakdownPolicy.Build(null, 1).Actors.Count, 0);
	}

	private static TakenHit TkHit(int victimKey, string victim, long nominal, long amount)
	{
		var h = new TakenHit();
		h.VictimKey = victimKey;
		h.Victim = victim;
		h.VictimTeam = 1;
		h.AttackerKey = 10;
		h.Attacker = "攻";
		h.Attr = "A";
		h.Source = 1;
		h.HitType = 1;
		h.EffectId = 0;
		h.HitMatch = 1;
		h.Nominal = nominal;
		h.Amount = amount;
		return h;
	}

	private static long TkSum(List<TakenBucket> list)
	{
		long n = 0;
		for (int i = 0; i < list.Count; i++) n += list[i].Amount;
		return n;
	}

	private static TakenBucket TkFind(List<TakenBucket> list, int key)
	{
		for (int i = 0; i < list.Count; i++)
			if (list[i].Key == key) return list[i];
		return null;
	}
}
