using System;
using System.Collections.Generic;
using DpsMeter;

namespace BehaviorTests;

/// <summary>
/// R52: the evidence-extraction flow and the unresolved-fold census.
///
/// Two things are pinned here and nowhere else:
///   * Policy/ExtractPolicy.cs -- the trigger key (parse + human form), the bundle name and the retention
///     decision. The retention one matters most: getting it backwards would delete the NEWEST bundle,
///     which is the only one anybody wants, and no battle would ever reveal that.
///   * Output/Contribution.cs -- the census that Diagnostics/EvidenceExtractor writes. It must group,
///     must NOT change a single credit, and its carrier verdict must stay an INFERENCE with an honest
///     three-way answer (unique / ambiguous / none) rather than a silent pick.
/// </summary>
internal static partial class Cases
{
	private static ContributionActor CensusActor(int key, string name, int team, params string[] grants)
	{
		var a = new ContributionActor { Key = key, Name = name, Team = team };
		for (int i = 0; i < grants.Length; i++) a.Grants.Add(grants[i]);
		return a;
	}

	private static ContributionFold CensusFold(string kind, string origin, double factor,
		string byUnit = null, string label = "")
	{
		return new ContributionFold
		{
			Kind = kind, Side = "vic", Origin = origin, Factor = factor, Label = label, ByUnit = byUnit,
		};
	}

	private static ContributionHit CensusHit(double damage, int attackerKey, string victim, int victimKey,
		params ContributionFold[] folds)
	{
		var h = new ContributionHit { Damage = damage, AttackerKey = attackerKey, HasCalc = true, Victim = victim, VictimKey = victimKey };
		for (int i = 0; i < folds.Length; i++) h.Folds.Add(folds[i]);
		return h;
	}

	public static void ExtractionCases(Runner r)
	{
		// ---------------------------------------------------------------------------------------------
		r.Group("extract/key");
		r.Eq("f4-parses-to-vk-0x73", ExtractPolicy.ParseVirtualKey("F4"), 0x73);
		r.Eq("lowercase-f4-parses-too", ExtractPolicy.ParseVirtualKey("f4"), 0x73);
		r.Eq("padded-f12-parses-to-vk-0x7B", ExtractPolicy.ParseVirtualKey(" F12 "), 0x7B);
		r.Eq("f1-is-the-first-function-key", ExtractPolicy.ParseVirtualKey("F1"), 0x70);
		r.Eq("f0-is-not-a-key (off)", ExtractPolicy.ParseVirtualKey("F0"), 0);
		r.Eq("f13-is-not-a-key (off)", ExtractPolicy.ParseVirtualKey("F13"), 0);
		r.Eq("none-disables-the-key", ExtractPolicy.ParseVirtualKey("NONE"), 0);
		r.Eq("off-disables-the-key", ExtractPolicy.ParseVirtualKey("off"), 0);
		r.Eq("empty-disables-the-key", ExtractPolicy.ParseVirtualKey(""), 0);
		r.Eq("null-disables-the-key", ExtractPolicy.ParseVirtualKey(null), 0);
		r.Eq("a-letter-parses-to-its-ascii-code", ExtractPolicy.ParseVirtualKey("Q"), 0x51);
		r.Eq("a-digit-parses-to-its-vk-code", ExtractPolicy.ParseVirtualKey("7"), 0x37);
		r.Eq("a-modifier-combination-is-not-supported (off)", ExtractPolicy.ParseVirtualKey("CTRL+F4"), 0);
		r.Eq("a-single-letter-is-the-letter-key (F is F, not F1)", ExtractPolicy.ParseVirtualKey("F"), 0x46);
		r.Str("the-human-form-names-the-key-and-its-code", ExtractPolicy.DescribeKey(0x73), "F4 (0x73)");
		r.Str("a-disabled-key-says-so", ExtractPolicy.DescribeKey(0), "disabled");
		r.Str("a-letter-key-is-described-as-the-letter", ExtractPolicy.DescribeKey(0x51), "Q (0x51)");

		// ---------------------------------------------------------------------------------------------
		r.Group("extract/bundle-name");
		r.Str("the-bundle-name-is-chronological-and-carry-quest-and-reason",
		      ExtractPolicy.BundleName(new DateTime(2026, 10, 5, 8, 25, 25), 411001, "battle-end"),
		      "extract_20261005_082525_q411001_battle-end");
		r.Str("the-reason-is-lowercased", ExtractPolicy.Sanitise("Battle-End"), "battle-end");
		r.Str("separators-become-dashes", ExtractPolicy.Sanitise("a b/c"), "a-b-c");
		r.Str("a-trailing-separator-is-trimmed", ExtractPolicy.Sanitise("hotkey-"), "hotkey");
		r.Eq("the-reason-part-is-capped", ExtractPolicy.Sanitise("abcdefghijklmnopqrstuvwxyz0123456789").Length, 24);
		r.True("a-name-with-no-reason-still-valid", ExtractPolicy.BundleName(new DateTime(2026, 1, 2, 3, 4, 5), 9, "!!!").EndsWith("_q9"));

		// ---------------------------------------------------------------------------------------------
		r.Group("extract/retention");
		var five = new List<string> { "extract_20260101_000005", "extract_20260101_000001",
		                              "extract_20260101_000003", "extract_20260101_000002",
		                              "extract_20260101_000004" };
		var stale3 = ExtractPolicy.StaleBundles(five, 3);
		r.Eq("keeping-three-of-five-drops-two", stale3.Count, 2);
		r.Str("the-OLDEST-is-dropped-first (never the newest)", stale3[0], "extract_20260101_000001");
		r.Str("and-the-second-oldest-next", stale3[1], "extract_20260101_000002");
		r.Eq("keeping-more-than-are-present-drops-nothing", ExtractPolicy.StaleBundles(five, 9).Count, 0);
		r.Eq("keeping-zero-is-clamped-to-one (never delete everything)", ExtractPolicy.StaleBundles(five, 0).Count, 4);
		r.Eq("keeping-a-negative-number-is-clamped-to-one-too", ExtractPolicy.StaleBundles(five, -5).Count, 4);
		r.Eq("keeping-a-huge-number-is-clamped-to-fifty", ExtractPolicy.StaleBundles(five, 9999).Count, 0);
		r.Eq("an-empty-list-drops-nothing", ExtractPolicy.StaleBundles(new List<string>(), 3).Count, 0);
		r.Eq("a-null-list-drops-nothing", ExtractPolicy.StaleBundles(null, 3).Count, 0);

		// ---------------------------------------------------------------------------------------------
		r.Group("extract/census");
		var team = new List<ContributionActor> { CensusActor(2, "A", 1), CensusActor(1, "BOSS", 2) };
		var hits = new List<ContributionHit>
		{
			CensusHit(1100.0, 2, "BOSS", 1, CensusFold("given", "given#4/1006/-10", 1.1, null, "被伤害+10%(赋予)")),
		};
		ContributionResult res = Contribution.Compute(hits, team, 1);
		r.Eq("an-unresolved-given-fold-produces-one-census-row", res.Unresolved.Count, 1);
		ContributionUnresolvedRow row = res.Unresolved[0];
		r.Str("the-reason-is-unknown_kind", row.Reason, "unknown_kind");
		r.Str("the-kind-is-carried", row.Kind, "given");
		r.Str("the-origin-is-carried", row.Origin, "given#4/1006/-10");
		r.Str("the-factor-is-carried-exactly", row.Factor.ToString("R"), 1.1.ToString("R"));
		r.Eq("one-fold-is-counted", row.Folds, 1);
		r.EqD("the-amount-is-the-log-share (1100 - 1100/1.1)", row.Amount, 100.0);
		r.Str("the-victim-name-is-recorded", row.VictimTop, "BOSS");
		r.Eq("and-so-is-the-victim-slot-count", row.VictimInstances, 1);
		r.Str("with-no-carrier-in-the-roster-the-verdict-is-none", row.CarrierVerdict, "none");
		r.Eq("and-the-candidate-count-is-zero", row.CarrierCount, 0);
		// THE credit must not move: the census is a report, not a decision.
		r.EqD("the-census-does-not-change-the-unattributed-amount", res.Stats.Unattributed, 100.0);
		r.EqD("nor-the-credited-amount", res.Stats.Attributed, 1000.0);
		r.Str("the-unattributed-reason-is-still-unknown_kind", res.Unattributed[0].Reason, "unknown_kind");

		// a unique holding actor -> the loadout route is determinate (but still only reported)
		team = new List<ContributionActor>
		{
			CensusActor(2, "A", 1), CensusActor(9, "GIVER", 1, "1006/-10"), CensusActor(1, "BOSS", 2),
		};
		res = Contribution.Compute(hits, team, 1);
		row = res.Unresolved[0];
		r.Str("a-unique-holding-actor-gives-a-unique-verdict", row.CarrierVerdict, "unique");
		r.Str("and-it-is-named", row.CarrierNames, "GIVER");
		r.EqD("a-unique-carrier-still-does-NOT-move-the-credit", res.Stats.Unattributed, 100.0);

		// two holding actors -> a real ambiguity, never a pick
		team = new List<ContributionActor>
		{
			CensusActor(2, "A", 1), CensusActor(9, "GIVER", 1, "1006/-10"),
			CensusActor(3, "GIVER2", 1, "1006/-10"), CensusActor(1, "BOSS", 2),
		};
		res = Contribution.Compute(hits, team, 1);
		row = res.Unresolved[0];
		r.Str("two-holding-actors-give-an-ambiguous-verdict", row.CarrierVerdict, "ambiguous");
		r.Eq("the-candidate-count-says-two", row.CarrierCount, 2);
		r.Str("both-candidates-are-named", row.CarrierNames, "GIVER, GIVER2");
		r.EqD("an-ambiguous-carrier-cannot-be-credited", res.Stats.Unattributed, 100.0);

		// the index is TEAM scoped: a grant held by the other team is not a candidate for our fold
		team = new List<ContributionActor>
		{
			CensusActor(2, "A", 1), CensusActor(7, "ENEMY_GIVER", 2, "1006/-10"), CensusActor(1, "BOSS", 2),
		};
		res = Contribution.Compute(hits, team, 1);
		r.Str("a-grant-held-only-by-the-other-team-is-not-a-candidate", res.Unresolved[0].CarrierVerdict, "none");

		// a different (type,param) is a different rule: the index must not match loosely
		team = new List<ContributionActor>
		{
			CensusActor(2, "A", 1), CensusActor(9, "GIVER", 1, "1006/-15"), CensusActor(1, "BOSS", 2),
		};
		res = Contribution.Compute(hits, team, 1);
		r.Str("a-grant-of-another-param-is-not-a-carrier", res.Unresolved[0].CarrierVerdict, "none");

		// the other byUnit channel: madness has no runtime applier -> unknown_kind, but NOT a granted carrier
		hits = new List<ContributionHit>
		{
			CensusHit(1100.0, 2, "BOSS", 1, CensusFold("madness", "vicmadness#150", 1.5, null, "狂気(受击方)")),
		};
		res = Contribution.Compute(hits, team, 1);
		r.Eq("a-madness-fold-with-no-applier-is-unresolved-too", res.Unresolved.Count, 1);
		r.Str("and-its-kind-is-madness", res.Unresolved[0].Kind, "madness");
		r.Str("but-it-is-not-probed-as-a-granted-carrier", res.Unresolved[0].CarrierVerdict, "");

		// an unresolved NAME lookup is a different reason: it must not be censused as an unknown kind
		hits = new List<ContributionHit>
		{
			CensusHit(1100.0, 2, "BOSS", 1, CensusFold("given", "given#4/1006/-10", 1.1, "NOBODY")),
		};
		res = Contribution.Compute(hits, team, 1);
		r.Eq("a-byUnit-that-matches-nobody-is-still-censused", res.Unresolved.Count, 1);
		r.Str("with-its-own-reason-code", res.Unresolved[0].Reason, "byUnit_unknown");
		r.Str("and-it-is-not-probed-for-a-carrier (the name route is the one that failed)",
		      res.Unresolved[0].CarrierVerdict, "");

		// a resolved fold (byUnit names a unique team member) must produce NO census row at all
		hits = new List<ContributionHit>
		{
			CensusHit(1100.0, 2, "BOSS", 1, CensusFold("given", "given#4/1006/-10", 1.1, "A")),
		};
		res = Contribution.Compute(hits, team, 1);
		r.Eq("a-resolved-fold-produces-no-census-row", res.Unresolved.Count, 0);
		r.EqD("and-its-share-is-credited-to-the-named-unit", res.Stats.Attributed, 1100.0);

		// grouping: the same rule on two hits with two victim slots is ONE row
		hits = new List<ContributionHit>
		{
			CensusHit(1100.0, 2, "BOSS", 1, CensusFold("given", "given#4/1006/-10", 1.1)),
			CensusHit(1100.0, 2, "BOSS", 2, CensusFold("given", "given#4/1006/-10", 1.1)),
		};
		res = Contribution.Compute(hits, team, 1);
		r.Eq("the-same-rule-on-two-hits-is-one-group", res.Unresolved.Count, 1);
		r.Eq("with-two-folds", res.Unresolved[0].Folds, 2);
		r.Eq("and-two-victim-slots", res.Unresolved[0].VictimInstances, 2);
		r.EqD("and-the-amount-added-up", res.Unresolved[0].Amount, 200.0);

		// two DIFFERENT origins of the same rule are two groups (the fold's identity includes the origin)
		hits = new List<ContributionHit>
		{
			CensusHit(1100.0, 2, "BOSS", 1, CensusFold("given", "given#4/1006/-10", 1.1)),
			CensusHit(1100.0, 2, "BOSS", 1, CensusFold("given", "given#5/1006/-10", 1.1)),
		};
		res = Contribution.Compute(hits, team, 1);
		r.Eq("two-copies-at-different-list-indexes-are-two-groups", res.Unresolved.Count, 2);

		// the victim top name is deterministic on a tie (count desc, then ordinal name)
		hits = new List<ContributionHit>
		{
			CensusHit(1100.0, 2, "B", 1, CensusFold("given", "given#4/1006/-10", 1.1)),
			CensusHit(1100.0, 2, "A", 2, CensusFold("given", "given#4/1006/-10", 1.1)),
		};
		res = Contribution.Compute(hits, team, 1);
		r.Str("a-victim-name-tie-is-broken-by-ordinal-order", res.Unresolved[0].VictimTop, "A");
		r.Eq("and-the-top-count-is-reported", res.Unresolved[0].VictimTopFolds, 1);

		hits = new List<ContributionHit>
		{
			CensusHit(1100.0, 2, "B", 1, CensusFold("given", "given#4/1006/-10", 1.1)),
			CensusHit(1100.0, 2, "B", 1, CensusFold("given", "given#4/1006/-10", 1.1)),
			CensusHit(1100.0, 2, "A", 2, CensusFold("given", "given#4/1006/-10", 1.1)),
		};
		res = Contribution.Compute(hits, team, 1);
		r.Str("the-MOST-FREQUENT-victim-wins-the-tie", res.Unresolved[0].VictimTop, "B");
		r.Eq("with-its-count", res.Unresolved[0].VictimTopFolds, 2);

		// the filters that already exist must apply to the census too: a noop/sub-unity/zero fold is not
		// an unresolved fold, it is not a fold at all.
		hits = new List<ContributionHit> { CensusHit(1100.0, 2, "BOSS", 1, CensusFold("given", "given#4/1006/-10", 1.0)) };
		res = Contribution.Compute(hits, team, 1);
		r.Eq("a-noop-factor-is-not-censused", res.Unresolved.Count, 0);
		r.Eq("and-it-is-counted-as-a-noop", res.Stats.NoopFactor, 1);
		hits = new List<ContributionHit> { CensusHit(1100.0, 2, "BOSS", 1, CensusFold("given", "given#4/1006/-10", 0.9)) };
		res = Contribution.Compute(hits, team, 1);
		r.Eq("a-sub-unity-factor-is-not-censused", res.Unresolved.Count, 0);
		r.Eq("and-it-is-counted-as-sub-unity", res.Stats.SubUnity, 1);
		hits = new List<ContributionHit> { CensusHit(1100.0, 2, "BOSS", 1, CensusFold("given", "given#4/1006/-10", 0.0)) };
		res = Contribution.Compute(hits, team, 1);
		r.Eq("a-zero-factor-is-not-censused", res.Unresolved.Count, 0);
		r.Eq("and-it-is-counted-as-a-zero", res.Stats.ZeroFactor, 1);

		// a hit whose attacker is not in the roster is never analysed, so its folds are not censused
		hits = new List<ContributionHit>
		{
			CensusHit(1100.0, 999, "BOSS", 1, CensusFold("given", "given#4/1006/-10", 1.1)),
		};
		res = Contribution.Compute(hits, team, 1);
		r.Eq("an-unknown-attacker-hit-is-not-censused", res.Unresolved.Count, 0);
		r.Eq("it-is-counted-as-an-unknown-attacker-hit", res.Stats.UnknownAttackerHits, 1);

		// two folds on ONE hit with the same identity merge into one group with two folds
		hits = new List<ContributionHit>
		{
			CensusHit(1210.0, 2, "BOSS", 1,
				CensusFold("given", "given#4/1006/-10", 1.1),
				CensusFold("given", "given#4/1006/-10", 1.1)),
		};
		res = Contribution.Compute(hits, team, 1);
		r.Eq("two-copies-in-one-hit-are-one-group", res.Unresolved.Count, 1);
		r.Eq("with-two-folds", res.Unresolved[0].Folds, 2);
		r.Eq("but-ONE-victim-slot", res.Unresolved[0].VictimInstances, 1);
		r.Eq("and-one-hit-name", res.Unresolved[0].VictimTopFolds, 2);

		// the rule NAME follows the shared renderer (the [..] inside the label wins)
		hits = new List<ContributionHit>
		{
			CensusHit(1100.0, 2, "BOSS", 1, CensusFold("given", "given#4/1006/-10", 1.1, null, "[刻印] 被伤害+10%")),
		};
		res = Contribution.Compute(hits, team, 1);
		r.Str("the-rule-name-uses-the-bracketed-text", res.Unresolved[0].RuleName, "刻印");
	}
}
