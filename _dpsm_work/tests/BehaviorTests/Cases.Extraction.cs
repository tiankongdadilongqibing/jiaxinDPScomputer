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
		// R54: the granted channel is no longer called unknown_kind -- it has its own code.
		r.Str("the-reason-is-the-granted-channels-own-code", row.Reason, "given_carrier_none");
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
		r.Str("and-the-exported-reason-is-the-same-code", res.Unattributed[0].Reason, "given_carrier_none");

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

		// ---------------------------------------------------------------------------------------------
		// R54 (user request): the granted 「阻挡增伤」 family must be readable as its own thing instead of
		// being mixed into one anonymous unattributed total. The three codes still mean UNATTRIBUTED -- what
		// they add is how close the roster-side evidence is to naming the provider.
		r.Group("extract/given-reasons");
		var baseTeam = new List<ContributionActor> { CensusActor(2, "A", 1), CensusActor(1, "BOSS", 2) };
		var givenHit = new List<ContributionHit>
		{
			CensusHit(1100.0, 2, "BOSS", 1, CensusFold("given", "given#4/1006/-10", 1.1)),
		};
		var noHolder = Contribution.Compute(givenHit, baseTeam, 1);
		r.Str("no-roster-holder-says-given_carrier_none", noHolder.Unattributed[0].Reason, "given_carrier_none");
		var oneHolder = Contribution.Compute(givenHit, new List<ContributionActor>
		{
			CensusActor(2, "A", 1), CensusActor(9, "GIVER", 1, "1006/-10"), CensusActor(1, "BOSS", 2),
		}, 1);
		r.Str("one-roster-holder-says-given_carrier_one", oneHolder.Unattributed[0].Reason, "given_carrier_one");
		var twoHolders = Contribution.Compute(givenHit, new List<ContributionActor>
		{
			CensusActor(2, "A", 1), CensusActor(9, "GIVER", 1, "1006/-10"),
			CensusActor(3, "GIVER2", 1, "1006/-10"), CensusActor(1, "BOSS", 2),
		}, 1);
		r.Str("two-roster-holders-say-given_carrier_ambiguous", twoHolders.Unattributed[0].Reason,
		      "given_carrier_ambiguous");
		r.EqD("and-the-split-still-moves-NOTHING (none)", noHolder.Stats.Unattributed, 100.0);
		r.EqD("and-the-split-still-moves-NOTHING (one)", oneHolder.Stats.Unattributed, 100.0);
		r.EqD("and-the-split-still-moves-NOTHING (ambiguous)", twoHolders.Stats.Unattributed, 100.0);
		r.EqD("the-attacker-keeps-only-the-base", twoHolders.Stats.Attributed, 1000.0);
		var madness = Contribution.Compute(new List<ContributionHit>
		{
			CensusHit(1100.0, 2, "BOSS", 1, CensusFold("madness", "vicmadness#150", 1.5)),
		}, baseTeam, 1);
		r.Str("a-NON-granted-channel-still-says-unknown_kind", madness.Unattributed[0].Reason, "unknown_kind");

		r.Group("extract/reason-labels");
		r.Str("the-granted-family-is-named-as-blocking-damage-increase",
		      FallbackText.UnattributedReasonLabel("given_carrier_ambiguous"), "阻挡增伤(多个候选,未确认)");
		r.Str("the-one-candidate-case-says-so", FallbackText.UnattributedReasonLabel("given_carrier_one"),
		      "阻挡增伤(唯一候选,未确认)");
		r.Str("the-generic-residual-keeps-its-own-label", FallbackText.UnattributedReasonLabel("unknown_kind"),
		      "种类未识别");
		r.Str("an-unknown-code-is-printed-as-is", FallbackText.UnattributedReasonLabel("some_future_code"),
		      "some_future_code");
		r.Str("the-breakdown-line-names-the-family-amount-and-folds",
		      FallbackText.UnattributedBreakdownLine("given_carrier_one", 1234567.0, 42),
		      "    阻挡增伤(唯一候选,未确认) 1,234,567  42 折");

		// ---------------------------------------------------------------------------------------------
		// R55 (user request): the pending 「阻挡增伤」 table -- the granted pool drawn with the CHARACTER
		// table's own geometry. What is pinned here is the two things that could go wrong silently: the
		// table must contain ONLY the granted family, and building it must not move one unit of credit.
		r.Group("extract/pending");
		var pendTeam = new List<ContributionActor> { CensusActor(2, "A", 1), CensusActor(1, "BOSS", 2) };
		var giverTeam = new List<ContributionActor>
		{
			CensusActor(2, "A", 1), CensusActor(9, "GIVER", 1, "1006/-10"), CensusActor(1, "BOSS", 2),
		};
		var oneGiven = new List<ContributionHit>
		{
			CensusHit(1100.0, 2, "BOSS", 1, CensusFold("given", "given#4/1006/-10", 1.1, null, "被伤害+10%(赋予)")),
		};
		ContributionResult pres = Contribution.Compute(oneGiven, giverTeam, 1);
		ContributionPendingTable pt = ContributionRowModel.BuildPending(pres, pres.Stats.Analyzable);
		r.Eq("one-unique-candidate-is-one-row", pt.Rows.Count, 1);
		r.Str("the-row-names-the-candidate-holder", pt.Rows[0].Name, "GIVER");
		r.EqD("with-the-pending-amount", pt.Rows[0].Amount, 100.0);
		r.EqD("and-the-pending-fold-count", pt.Rows[0].Folds, 1.0);
		r.EqD("and-its-share-of-the-analyzable-total", pt.Rows[0].Share, 100.0 * 100.0 / 1100.0);
		r.EqD("the-footer-totals-the-pool", pt.Total, 100.0);
		r.EqD("the-footer-share-is-the-same-share", pt.Share, 100.0 * 100.0 / 1100.0);
		r.Eq("the-rule-label-travels-with-the-table", pt.Labels.Count, 1);
		r.Str("and-it-is-the-rules-own-label", pt.Labels[0], "被伤害+10%(赋予)");
		r.EqD("building-the-table-does-NOT-move-the-credit", pres.Stats.Unattributed, 100.0);
		r.EqD("nor-the-attributed-side", pres.Stats.Attributed, 1000.0);
		r.Eq("a-null-result-produces-no-pending-rows",
		     ContributionRowModel.BuildPending(null, 100.0).Rows.Count, 0);

		// several copies of ONE rule that share ONE holder are ONE row for that holder (that is what makes
		// the table answer "who would get it" instead of listing the same name four times)
		var twoCopies = new List<ContributionHit>
		{
			CensusHit(1100.0, 2, "BOSS", 1, CensusFold("given", "given#4/1006/-10", 1.1, null, "被伤害+10%(赋予)")),
			CensusHit(1100.0, 2, "BOSS", 1, CensusFold("given", "given#7/1006/-10", 1.1, null, "被伤害+10%(赋予)")),
		};
		pres = Contribution.Compute(twoCopies, giverTeam, 1);
		pt = ContributionRowModel.BuildPending(pres, pres.Stats.Analyzable);
		r.Eq("two-copies-sharing-one-holder-are-ONE-row", pt.Rows.Count, 1);
		r.EqD("with-the-two-shares-added-up", pt.Rows[0].Amount, 200.0);
		r.EqD("and-two-folds", pt.Rows[0].Folds, 2.0);

		// an ambiguous roster route keeps its candidates apart: one row carrying the COUNT, never a picked name
		var twoGivers = new List<ContributionActor>
		{
			CensusActor(2, "A", 1), CensusActor(9, "GIVER", 1, "1006/-10"),
			CensusActor(3, "GIVER2", 1, "1006/-10"), CensusActor(1, "BOSS", 2),
		};
		pres = Contribution.Compute(oneGiven, twoGivers, 1);
		pt = ContributionRowModel.BuildPending(pres, pres.Stats.Analyzable);
		r.Eq("an-ambiguous-group-is-still-one-row", pt.Rows.Count, 1);
		r.Str("and-it-carries-the-count-not-a-picked-name", pt.Rows[0].Name, "候选2人");
		r.Eq("the-candidate-names-are-kept-for-the-note-line", pt.Ambiguous.Count, 1);
		r.Str("with-both-candidates-in-it", pt.Ambiguous[0], "GIVER, GIVER2");
		r.EqD("and-no-credit-moves-here-either", pres.Stats.Unattributed, 100.0);

		// no readable candidate is a row too -- it is a statement about the roster read, not an absence
		pres = Contribution.Compute(oneGiven, pendTeam, 1);
		pt = ContributionRowModel.BuildPending(pres, pres.Stats.Analyzable);
		r.Eq("no-readable-candidate-is-still-a-row", pt.Rows.Count, 1);
		r.Str("labelled-as-such", pt.Rows[0].Name, "(无可读候选)");
		r.Eq("with-no-ambiguous-note", pt.Ambiguous.Count, 0);

		// ONLY the granted-channnel groups have a carrier verdict, so only they may reach this table
		var mixed = new List<ContributionHit>
		{
			CensusHit(1100.0, 2, "BOSS", 1, CensusFold("given", "given#4/1006/-10", 1.1)),
			CensusHit(1100.0, 2, "BOSS", 1, CensusFold("madness", "vicmadness#150", 1.5)),
			CensusHit(1100.0, 2, "BOSS", 1, CensusFold("given", "given#4/1006/-10", 1.1, "NOBODY")),
		};
		pres = Contribution.Compute(mixed, giverTeam, 1);
		r.Eq("three-unrelated-unresolved-groups-are-censused", pres.Unresolved.Count, 3);
		pt = ContributionRowModel.BuildPending(pres, pres.Stats.Analyzable);
		r.Eq("but-only-the-granted-carriers-pool-reaches-the-pending-table", pt.Rows.Count, 1);
		r.Str("and-it-is-the-granted-group", pt.Rows[0].Name, "GIVER");

		// order: the heavier pool first, and a tie broken by the name (ordinal, so it cannot shuffle per run)
		var orderTeam = new List<ContributionActor>
		{
			CensusActor(2, "A", 1), CensusActor(9, "ZZZ", 1, "1006/-10"),
			CensusActor(3, "AAA", 1, "2006/-10"), CensusActor(1, "BOSS", 2),
		};
		var tie = new List<ContributionHit>
		{
			CensusHit(1100.0, 2, "BOSS", 1, CensusFold("given", "given#4/1006/-10", 1.1, null, "R1")),
			CensusHit(1100.0, 2, "BOSS", 1, CensusFold("given", "given#5/2006/-10", 1.1, null, "R2")),
		};
		pres = Contribution.Compute(tie, orderTeam, 1);
		pt = ContributionRowModel.BuildPending(pres, pres.Stats.Analyzable);
		r.Eq("two-holders-are-two-rows", pt.Rows.Count, 2);
		r.EqD("their-shares-tie", pt.Rows[0].Amount, pt.Rows[1].Amount);
		r.Str("so-the-name-breaks-it (ordinal)", pt.Rows[0].Name, "AAA");
		r.Str("and-the-other-follows", pt.Rows[1].Name, "ZZZ");
		r.Eq("two-distinct-labels-are-both-listed", pt.Labels.Count, 2);
		// the heavier pool is ZZZ's (x2.0 on the 1006/-10 grant), so the amount -- not the alphabet -- decides
		var heavy = new List<ContributionHit>
		{
			CensusHit(1100.0, 2, "BOSS", 1, CensusFold("given", "given#4/1006/-10", 2.0, null, "R1")),
			CensusHit(1100.0, 2, "BOSS", 1, CensusFold("given", "given#5/2006/-10", 1.1, null, "R2")),
		};
		pres = Contribution.Compute(heavy, orderTeam, 1);
		pt = ContributionRowModel.BuildPending(pres, pres.Stats.Analyzable);
		r.Str("the-heavier-pool-is-printed-first-even-against-the-alphabet", pt.Rows[0].Name, "ZZZ");
		r.EqD("with-the-whole-share", pt.Rows[0].Amount, 550.0);

		// the geometry IS the character table's -- same column count, same widths, same nominal line width
		ColumnSpec[] pspec = ContributionColumns.T1PendingSpec();
		r.Eq("the-pending-table-has-the-same-column-count", pspec.Length, ContributionColumns.T1.Length);
		r.Str("the-first-column-is-relabelled-candidates", pspec[0].Label, "候选角色");
		r.Str("the-last-column-is-relabelled-folds", pspec[pspec.Length - 1].Label, "折叠");
		for (int pi = 0; pi < pspec.Length; pi++)
			r.Eq("pending-column-" + pi + "-keeps-the-T1-width", pspec[pi].Width, ContributionColumns.T1[pi].Width);
		r.Eq("the-pending-header-is-as-wide-as-the-character-header",
		     DisplayFormat.DispWidth(ContributionColumns.HeaderLine(pspec)), ContributionColumns.T1LineWidth);
		r.Eq("and-so-is-a-pending-data-row",
		     DisplayFormat.DispWidth(ContributionColumns.T1PendingRow("エヴァラス・フラウ", 7373676.891, 3.885, 5368)),
		     ContributionColumns.T1LineWidth);
		r.Eq("and-the-pending-footer",
		     DisplayFormat.DispWidth(ContributionColumns.T1PendingTotalsLine(7373676.891, 3.885, 5368)),
		     ContributionColumns.T1LineWidth);
		r.Eq("a-1e9-class-pool-must-not-push-the-row-either",
		     DisplayFormat.DispWidth(ContributionColumns.T1PendingRow("X", 9.9e9, 100.0, 1234567)),
		     ContributionColumns.T1LineWidth);
		string prow = ContributionColumns.T1PendingRow("X", 1.0, 1.0, 1);
		int dashes = 0;
		for (int pi = 0; pi < prow.Length; pi++) if (prow[pi] == '-') dashes++;
		r.Eq("the-five-inapplicable-credit-columns-print-a-dash (not a zero)", dashes, 5);
		string pfoot = ContributionColumns.T1PendingTotalsLine(1.0, 1.0, 1);
		int fdashes = 0;
		for (int pi = 0; pi < pfoot.Length; pi++) if (pfoot[pi] == '-') fdashes++;
		r.Eq("the-footer-dashes-the-same-five-columns", fdashes, 5);

		// ---------------------------------------------------------------------------------------------
		// 1.7.12: the same-team split of one attacker own hits. Two hits by the SAME attacker: one on the
		// enemy, one on his own side (the event Friendly flag). Direct is their sum and the two parts must
		// PARTITION it -- that partition is what the new self-damage column and the JSON fields publish.
		// The last assertion is the important one: the split is REPORTING, it does not move a single credit.
		r.Group("extract/friendly-split");
		var splitTeam = new List<ContributionActor>
		{
			CensusActor(3, "SELF", 1), CensusActor(4, "ALLY", 1), CensusActor(1, "BOSS", 2),
		};
		var splitHits = new List<ContributionHit>
		{
			CensusHit(100.0, 3, "BOSS", 1),
			CensusHit(40.0, 3, "ALLY", 4),
		};
		splitHits[1].Friendly = true;
		ContributionResult sres = Contribution.Compute(splitHits, splitTeam, 1);
		ContributionActorRow srow = null;
		for (int si = 0; si < sres.Actors.Count; si++) if (sres.Actors[si].Key == 3) srow = sres.Actors[si];
		r.True("the-attacker-row-exists", srow != null);
		r.EqD("direct-is-both-hits", srow.Direct, 140.0);
		r.EqD("the-friendly-hit-is-the-friendly-part", srow.Friendly, 40.0);
		r.Eq("and-it-is-one-hit", srow.FriendlyHits, 1);
		r.EqD("the-enemy-hit-is-the-hostile-part", srow.Hostile, 100.0);
		r.EqD("the-two-parts-partition-the-direct-damage", srow.Friendly + srow.Hostile, srow.Direct);
		r.EqD("and-the-friendly-hit-still-credits-its-own-attacker (reporting only)", srow.Base, 140.0);

		// back to the family the caption cases below belong to: the group label is part of a case NAME, so
		// leaving them in extract/friendly-split would rename four cases (and a negative control keys on one).
		r.Group("extract/pending");

		// the captions: the family's own name, an explicit "not charged" promise, and the candidate list
		r.Str("the-pending-header-names-the-family-and-the-rule",
		      FallbackText.PendingHeaderLine(new List<string> { "被伤害+10%(赋予)" }),
		      "【阻挡增伤·待确认】被伤害+10%(赋予)   提供者未确认,下列份额未计入任何角色");
		r.Str("a-third-label-becomes-a-count-not-a-third-name",
		      FallbackText.PendingHeaderLine(new List<string> { "a", "b", "c" }),
		      "【阻挡增伤·待确认】a / b 等3种   提供者未确认,下列份额未计入任何角色");
		r.Str("no-label-still-produces-a-caption",
		      FallbackText.PendingHeaderLine(null),
		      "【阻挡增伤·待确认】   提供者未确认,下列份额未计入任何角色");
		r.Str("the-note-says-the-candidate-is-an-inference",
		      FallbackText.PendingNoteLine(new ContributionPendingTable()),
		      "  (* 候选来自名册持有者的推断,不是实测;确认归属前不计入任何角色)");
		pres = Contribution.Compute(oneGiven, twoGivers, 1);
		pt = ContributionRowModel.BuildPending(pres, pres.Stats.Analyzable);
		r.True("the-note-lists-the-ambiguous-candidates",
		       FallbackText.PendingNoteLine(pt).EndsWith("多个候选: GIVER, GIVER2"));

		// ---------------------------------------------------------------------------------------------
		// R52c: which source an extraction run may use. The point of the group is the PRIORITY, not the
		// enumeration: after a battle the live session is gone (Aggregator nulls it) while the snapshot of
		// the finalised battle remains, and the key must still produce the battle the user just fought.
		r.Group("extract/source");
		r.Eq("a-live-session-is-used-when-there-is-one",
		     (int)ExtractPolicy.SelectSource(true, true), (int)ExtractPolicy.BundleSource.Live);
		r.Eq("live-beats-the-snapshot (the live session is the more recent truth)",
		     (int)ExtractPolicy.SelectSource(true, false), (int)ExtractPolicy.BundleSource.Live);
		r.Eq("with-no-live-session-the-last-finalised-battle-is-used",
		     (int)ExtractPolicy.SelectSource(false, true), (int)ExtractPolicy.BundleSource.LastFinalised);
		r.Eq("with-neither-an-empty-bundle-is-NOT-written",
		     (int)ExtractPolicy.SelectSource(false, false), (int)ExtractPolicy.BundleSource.None);
	}
}
