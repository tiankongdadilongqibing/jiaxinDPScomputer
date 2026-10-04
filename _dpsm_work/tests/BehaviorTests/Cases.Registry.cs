using System;
using System.Collections.Generic;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>A stand-in for the probe's private GlobalRule: the registry only needs the rule to be
	/// opaque, plus a predicate the caller supplies, so the tests do not need the native owner object.</summary>
	private sealed class FakeRule
	{
		public string OwnerName = "";
		public bool Alive = true;
	}

	/// <summary>
	/// RF4c: the battle-wide rule registry. These cases are the ones the matrix said were missing: the
	/// second family's state ownership, and the "the next battle's units register BEFORE the previous battle
	/// is finalised" timing property -- both executable offline, with the native aliveness read replaced by a
	/// predicate.
	/// </summary>
	public static void Registry(Runner r)
	{
		Func<FakeRule, bool> alive = rule => rule.Alive;

		r.Group("runtime/global-registry");
		var g = new GlobalRuleRegistry<FakeRule>();
		r.Eq("a-new-registry-is-empty", g.Count, 0);
		var aRules = new List<FakeRule> { new FakeRule { OwnerName = "A" } };
		var bRules = new List<FakeRule> { new FakeRule { OwnerName = "B" } };
		g.Set(1L, "A", aRules);
		g.Set(2L, "B", bRules);
		r.Eq("two-registrations-count-two", g.Count, 2);
		List<FakeRule> got;
		r.True("try-get-finds-the-entry", g.TryGet(1L, out got));
		r.Same("try-get-returns-the-same-list", got, aRules);
		r.True("try-get-misses-a-unknown-key", !g.TryGet(99L, out got) && got == null);
		r.True("the-owner-name-is-remembered", g.HasOwnerName(1L));
		r.Str("the-owner-name-is-readable", g.OwnerNameOf(1L), "A");
		r.True("a-missing-owner-name-is-null", g.OwnerNameOf(99L) == null);

		r.Group("runtime/global-registry-reclaim");
		var dead = new FakeRule { OwnerName = "C", Alive = false };
		g.Set(3L, "C", new List<FakeRule> { dead });
		r.Eq("three-registrations-count-three", g.Count, 3);
		r.Eq("reclaim-drops-exactly-the-dead-owner", g.Reclaim(alive), 1);
		r.Eq("the-two-live-owners-remain", g.Count, 2);
		r.True("the-dead-owner-is-gone", !g.TryGet(3L, out got));
		r.True("the-dead-owner-name-is-forgotten", !g.HasOwnerName(3L));
		r.True("a-live-owner-keeps-its-rules", g.TryGet(2L, out got) && got.Count == 1);
		r.Eq("a-second-reclaim-drops-nothing-more", g.Reclaim(alive), 0);

		// An EMPTY entry is the "scanned, found nothing" memo, so it is always dropped -- even though its
		// owner is alive. That is what makes a still-alive unit get re-scanned exactly once.
		var h = new GlobalRuleRegistry<FakeRule>();
		h.Set(7L, "LIVE", new List<FakeRule>());
		r.Eq("an-empty-memo-is-dropped-even-for-a-live-owner", h.Reclaim(alive), 1);
		r.Eq("and-the-registry-is-empty-again", h.Count, 0);

		// A null list cannot happen today, but the probe guards it, so the container must too.
		var n = new GlobalRuleRegistry<FakeRule>();
		n.Set(8L, "NULL", null);
		r.Eq("a-null-entry-is-reclaimed", n.Reclaim(alive), 1);

		r.Group("runtime/global-registry-timing");
		// THE timing property: a unit of the NEXT battle registers while the previous battle is still being
		// finalised. Nothing in a finalisation touches this state, so a reclaim that runs afterwards must
		// still find the (alive) owner and keep its rules -- the failure recorded in the source comments
		// made a battle-wide buff disappear until its owner happened to act again.
		var t = new GlobalRuleRegistry<FakeRule>();
		var nextBattle = new List<FakeRule> { new FakeRule { OwnerName = "MYAURA" } };
		t.Set(42L, "MYAURA", nextBattle);
		// ... the previous battle is finalised here (no call reaches the registry: that IS the property) ...
		r.Eq("a-finalisation-does-not-drop-the-next-battles-registration", t.Reclaim(alive), 0);
		r.True("and-its-rules-are-still-usable", t.TryGet(42L, out got) && got.Count == 1);
		r.Str("and-its-owner-name-still-blocks-a-rescan", t.OwnerNameOf(42L), "MYAURA");
		// only when the owner object is really gone does it go
		nextBattle[0].Alive = false;
		r.Eq("the-entry-goes-once-its-owner-is-gone", t.Reclaim(alive), 1);
		r.Eq("and-the-registry-is-empty", t.Count, 0);

		r.Group("runtime/global-registry-order");
		// The apply path multiplies double factors while iterating this registry, and floating-point
		// multiplication is not associative, so the ENUMERATION ORDER is part of the behaviour (the plan
		// forbids reordering it). Dictionary enumerates in insertion order while nothing is removed; this
		// case documents that the apply path depends on it, so a runtime change that reorders it is a
		// behaviour change and must be seen, not discovered later.
		var o = new GlobalRuleRegistry<FakeRule>();
		o.Set(30L, "c", new List<FakeRule> { new FakeRule() });
		o.Set(10L, "a", new List<FakeRule> { new FakeRule() });
		o.Set(20L, "b", new List<FakeRule> { new FakeRule() });
		var seen = new List<long>();
		foreach (var kv in o) seen.Add(kv.Key);
		r.Str("enumeration-is-insertion-ordered", string.Join(",", seen), "30,10,20");
		foreach (var kv in o) if (kv.Key == 10L) kv.Value[0].Alive = false;
		o.Reclaim(alive);
		seen.Clear();
		foreach (var kv in o) seen.Add(kv.Key);
		r.Str("the-survivors-keep-their-relative-order", string.Join(",", seen), "30,20");

		r.Group("runtime/global-registry-clear");
		var c = new GlobalRuleRegistry<FakeRule>();
		c.Set(1L, "A", new List<FakeRule> { new FakeRule() });
		c.Set(2L, "B", new List<FakeRule> { new FakeRule() });
		c.ClearAll();
		r.Eq("clear-all-empties-the-rules", c.Count, 0);
		r.True("clear-all-forgets-the-names-too", !c.HasOwnerName(1L) && !c.HasOwnerName(2L));
	}
}
