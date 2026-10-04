using System;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// RF4 family 2: the DECISION half of the battle-wide rule registration. Before this extraction the
	/// "which clause becomes a battle-wide rule" chain existed only inside a loop that walks IL2CPP ability
	/// objects, so the only way to test it was to fight. The texts below are the production ones quoted in
	/// the code and OBSERVED in BepInEx/config/dpsmeter_runtime.log (the authentic one is named as such).
	///
	/// The factor parser stays in the probe, so the tests pass a stub and additionally assert WHICH keyword
	/// the classifier asks for -- that choice is what decides whether a rule is parsed as a damage-taken or a
	/// damage-dealt modifier.
	/// </summary>
	public static void GlobalRule(Runner r)
	{
		Func<string, string, double> fx = (text, keyword) => keyword == "被ダメージ" ? 1.15 : 1.30;

		r.Group("globalrule/classification");
		// OBSERVED: "[RULE]   敌受伤 ×1.150  毒/凍結/火傷状態の敵全ての被ダメージがそれぞれ+15%" (18:03:50).
		const string live = "毒/凍結/火傷状態の敵全ての被ダメージがそれぞれ+15%";
		GlobalRuleShape a = GlobalRuleClassifier.Classify(live, fx);
		r.Eq("the-authentic-rule-is-enemy-takes", (int)a.Kind, (int)GlobalRuleKind.EnemyTakes);
		r.EqD("the-authentic-rule-factor-is-1.15", a.Factor, 1.15);
		r.True("the-authentic-rule-is-per-status", a.PerStatus);
		r.Str("the-clause-is-kept-verbatim", a.Text, live);
		r.True("the-authentic-rule-has-status-tokens", a.Tokens != null && a.Tokens.Count > 0);
		r.Eq("the-authentic-rule-is-not-magic-only", a.MagicOnly ? 1 : 0, 0);
		r.Eq("the-authentic-rule-is-not-phys-only", a.PhysOnly ? 1 : 0, 0);
		r.Eq("the-authentic-rule-is-not-vanguard", a.Vanguard ? 1 : 0, 0);
		r.Eq("the-authentic-rule-is-not-rearguard", a.Rearguard ? 1 : 0, 0);
		r.Eq("the-authentic-rule-token-count", a.Tokens.Count, 3);

		const string haima = "毒/火傷状態の敵全ての被ダメージがそれぞれ+15%";
		GlobalRuleShape b = GlobalRuleClassifier.Classify(haima, fx);
		r.Eq("a-second-observation-of-the-same-shape", (int)b.Kind, (int)GlobalRuleKind.EnemyTakes);
		r.Eq("the-second-observation-has-two-tokens", b.Tokens.Count, 2);
		r.True("the-second-observation-is-per-status", b.PerStatus);

		// ミャウラ: "編成時、味方ヴァイスの魔法攻撃の与ダメージ+15%" -- a buff for ALL allied attacks.
		const string myaura = "編成時、味方ヴァイスの魔法攻撃の与ダメージ+15%";
		GlobalRuleShape c = GlobalRuleClassifier.Classify(myaura, fx);
		r.Eq("the-ally-buff-is-ally-attack", (int)c.Kind, (int)GlobalRuleKind.AllyAttack);
		r.EqD("the-ally-buff-factor-comes-from-the-dealt-keyword", c.Factor, 1.30);
		r.Eq("the-ally-buff-is-magic-only", c.MagicOnly ? 1 : 0, 1);
		r.Eq("the-ally-buff-is-not-phys-only", c.PhysOnly ? 1 : 0, 0);
		r.Eq("the-ally-buff-is-not-per-status", c.PerStatus ? 1 : 0, 0);

		const string phys = "編成時、味方ヴァイスの物理攻撃の与ダメージ+15%";
		GlobalRuleShape d = GlobalRuleClassifier.Classify(phys, fx);
		r.Eq("a-physical-ally-buff-is-phys-only", d.PhysOnly ? 1 : 0, 1);
		r.Eq("a-physical-ally-buff-is-not-magic-only", d.MagicOnly ? 1 : 0, 0);

		const string both = "編成時、味方の物理/魔法攻撃の与ダメージ+15%";
		GlobalRuleShape e = GlobalRuleClassifier.Classify(both, fx);
		r.Eq("both-attributes-means-neither-restriction-magic", e.MagicOnly ? 1 : 0, 0);
		r.Eq("both-attributes-means-neither-restriction-phys", e.PhysOnly ? 1 : 0, 0);

		r.Group("globalrule/position-gates");
		GlobalRuleShape vg = GlobalRuleClassifier.Classify("編成時、味方前衛の与ダメージ+15%", fx);
		r.Eq("a-vanguard-clause-sets-vanguard", vg.Vanguard ? 1 : 0, 1);
		r.Eq("a-vanguard-clause-does-not-set-rearguard", vg.Rearguard ? 1 : 0, 0);
		GlobalRuleShape rg = GlobalRuleClassifier.Classify("編成時、味方後衛の与ダメージ+15%", fx);
		r.Eq("a-rearguard-clause-sets-rearguard", rg.Rearguard ? 1 : 0, 1);
		GlobalRuleShape br = GlobalRuleClassifier.Classify("編成時、味方前衛/後衛の与ダメージ+15%", fx);
		r.Eq("naming-both-positions-clears-vanguard", br.Vanguard ? 1 : 0, 0);
		r.Eq("naming-both-positions-clears-rearguard", br.Rearguard ? 1 : 0, 0);

		r.Group("globalrule/rejections");
		r.Eq("an-empty-clause-is-none", (int)GlobalRuleClassifier.Classify("", fx).Kind, (int)GlobalRuleKind.None);
		r.Eq("a-null-clause-is-none", (int)GlobalRuleClassifier.Classify(null, fx).Kind, (int)GlobalRuleKind.None);
		r.Eq("a-plain-damage-taken-clause-is-not-battle-wide",
		      (int)GlobalRuleClassifier.Classify("物理被ダメージ-15%（前衛のみ）", fx).Kind, (int)GlobalRuleKind.None);
		r.Eq("a-clause-without-alle-is-not-battle-wide",
		      (int)GlobalRuleClassifier.Classify("敵の被ダメージ+15%", fx).Kind, (int)GlobalRuleKind.None);
		r.Eq("an-hp-conditional-enemy-clause-stays-local",
		      (int)GlobalRuleClassifier.Classify("現在耐久が20%以下の場合、被ダメージ-40%（前衛のみ）", fx).Kind,
		      (int)GlobalRuleKind.None);
		r.Eq("an-hp-conditional-ally-clause-stays-local",
		      (int)GlobalRuleClassifier.Classify("HPが50%以下の場合、味方の与ダメージ+15%", fx).Kind,
		      (int)GlobalRuleKind.None);
		r.Eq("a-block-clause-stays-local",
		      (int)GlobalRuleClassifier.Classify("ブロック時、敵全ての被ダメージ+15%", fx).Kind,
		      (int)GlobalRuleKind.None);
		r.Eq("an-ally-clause-without-mikata-is-not-battle-wide",
		      (int)GlobalRuleClassifier.Classify("ヴァイスの魔法攻撃の与ダメージ+15%", fx).Kind,
		      (int)GlobalRuleKind.None);
		r.Eq("a-factor-of-1.0-is-rejected",
		      (int)GlobalRuleClassifier.Classify("敵全ての被ダメージがそれぞれ+0%",
		                                         (t, k) => 1.0).Kind, (int)GlobalRuleKind.None);

		r.Group("globalrule/alternate-target-words");
		r.Eq("aite-all-is-accepted", (int)GlobalRuleClassifier.Classify("相手全ての被ダメージがそれぞれ+15%", fx).Kind,
		      (int)GlobalRuleKind.EnemyTakes);
		r.Eq("taishou-all-is-accepted", (int)GlobalRuleClassifier.Classify("対象全ての被ダメージ-15%", fx).Kind,
		      (int)GlobalRuleKind.EnemyTakes);
		r.Eq("subete-in-kana-is-accepted", (int)GlobalRuleClassifier.Classify("敵すべての被ダメージ+15%", fx).Kind,
		      (int)GlobalRuleKind.EnemyTakes);

		r.Group("globalrule/contracts");
		string asked = null;
		GlobalRuleClassifier.Classify("毒状態の敵全ての被ダメージがそれぞれ+15%",
		                              (t, k) => { asked = k; return 1.15; });
		r.Str("an-enemy-clause-asks-for-the-taken-keyword", asked, "被ダメージ");
		asked = null;
		GlobalRuleClassifier.Classify("編成時、味方の与ダメージ+15%", (t, k) => { asked = k; return 1.15; });
		r.Str("an-ally-clause-asks-for-the-dealt-keyword", asked, "与ダメージ");
		asked = null;
		GlobalRuleClassifier.Classify("物理被ダメージ-15%", (t, k) => { asked = k; return 1.15; });
		r.True("a-rejected-clause-never-parses-a-factor", asked == null);
		asked = null;
		GlobalRuleClassifier.Classify("耐久が50%以下の場合、敵全ての被ダメージ+15%", (t, k) => { asked = k; return 1.15; });
		r.True("an-hp-gated-clause-never-parses-a-factor", asked == null);
		// enemy shape wins when a clause could be read both ways
		GlobalRuleShape mix = GlobalRuleClassifier.Classify("敵全ての被ダメージ+15%、味方の与ダメージ+15%", fx);
		r.Eq("enemy-shape-wins-when-both-appear", (int)mix.Kind, (int)GlobalRuleKind.EnemyTakes);

		r.Group("globalrule/registry-facts");
		var none = default(GlobalRuleClassifier.RegisterFacts);
		r.True("no-entry-is-scanned", !GlobalRuleClassifier.SkipRescan(none));
		var same = new GlobalRuleClassifier.RegisterFacts
		{ HasEntry = true, EntryCount = 2, FirstRuleOwnerName = "A", CurrentName = "A" };
		r.True("the-same-owner-name-skips-the-rescan", GlobalRuleClassifier.SkipRescan(same));
		var reused = new GlobalRuleClassifier.RegisterFacts
		{ HasEntry = true, EntryCount = 2, FirstRuleOwnerName = "A", CurrentName = "B" };
		r.True("a-reused-pointer-is-rescanned", !GlobalRuleClassifier.SkipRescan(reused));
		var memo = new GlobalRuleClassifier.RegisterFacts
		{ HasEntry = true, EntryCount = 0, HasNameMemo = true, NameMemoOwner = "A", CurrentName = "A" };
		r.True("an-empty-entry-with-a-matching-memo-skips", GlobalRuleClassifier.SkipRescan(memo));
		var memoOther = new GlobalRuleClassifier.RegisterFacts
		{ HasEntry = true, EntryCount = 0, HasNameMemo = true, NameMemoOwner = "A", CurrentName = "B" };
		r.True("an-empty-entry-with-another-name-is-rescanned", !GlobalRuleClassifier.SkipRescan(memoOther));
		var memoMissing = new GlobalRuleClassifier.RegisterFacts
		{ HasEntry = true, EntryCount = 0, HasNameMemo = false, CurrentName = "A" };
		r.True("an-empty-entry-without-a-memo-is-rescanned", !GlobalRuleClassifier.SkipRescan(memoMissing));
		var nullEntry = new GlobalRuleClassifier.RegisterFacts
		{ HasEntry = true, EntryCount = -1, HasNameMemo = true, NameMemoOwner = "A", CurrentName = "A" };
		r.True("a-null-entry-is-rescanned", !GlobalRuleClassifier.SkipRescan(nullEntry));

		r.Group("globalrule/registry-valve");
		r.Eq("reclaim-threshold-is-400", GlobalRuleClassifier.ReclaimThreshold, 400);
		r.Eq("clear-all-threshold-is-600", GlobalRuleClassifier.ClearAllThreshold, 600);
		r.True("at-the-reclaim-threshold-nothing-happens", !GlobalRuleClassifier.ShouldReclaim(400));
		r.True("one-past-it-reclaims", GlobalRuleClassifier.ShouldReclaim(401));
		r.True("at-the-clear-all-threshold-nothing-extra-happens", !GlobalRuleClassifier.ShouldClearAll(600));
		r.True("one-past-it-clears-all", GlobalRuleClassifier.ShouldClearAll(601));
		r.True("the-second-test-runs-on-the-post-reclaim-count",
		       GlobalRuleClassifier.ShouldReclaim(700) && !GlobalRuleClassifier.ShouldClearAll(50));
	}
}
