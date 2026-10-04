using System;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	private static void Tier(Runner r, string label, string text, int hp, string clause, double want)
	{
		TieredModifier.Spec spec = TieredModifier.Get(text);
		if (spec == null) { r.True(label + " (spec parsed)", false); return; }
		double f;
		string n;
		bool ok = TieredModifier.TryEvaluate(spec, hp, clause, out f, out n);
		if (want <= 0.0) r.True(label + " (inactive)", !ok);
		else r.True(label + " (x" + want.ToString("F4") + ")", ok && Math.Abs(f - want) < 0.0005);
	}

	private static void TierNoSpec(Runner r, string label, string text)
	{
		r.True(label + " (no tier list)", TieredModifier.Get(text) == null);
	}

	/// <summary>
	/// Migrated from the ad-hoc test/TierTest/Program.cs, which the acceptance pipeline never ran --
	/// so the 1.1.0 tiered-modifier rules had no executed coverage in any batch. The texts below are the
	/// real in-game ability strings (copied from BepInEx/config/dpsmeter_runtime.log) and the thresholds
	/// are the whole point: 9/6/3割 boundaries decide a damage multiplier on live content.
	/// Labels are ASCII so a FAILURE line is still readable in a GBK console.
	/// </summary>
	public static void Tiered(Runner r)
	{
		r.Group("tiered/victim-reduction");
		const string sizeme = "耐久が減少するほど物理/魔法被ダメージが減少  (現在耐久が9/6/3割以下の場合、それぞれ0.1/0.3/0.5倍)";
		const string sizemeClause = "耐久が減少するほど物理/魔法被ダメージが減少";
		Tier(r, "sizeme-100", sizeme, 100, sizemeClause, 0.0);
		Tier(r, "sizeme-90", sizeme, 90, sizemeClause, 0.9);
		Tier(r, "sizeme-61", sizeme, 61, sizemeClause, 0.9);
		Tier(r, "sizeme-60", sizeme, 60, sizemeClause, 0.7);
		Tier(r, "sizeme-55", sizeme, 55, sizemeClause, 0.7);
		Tier(r, "sizeme-30", sizeme, 30, sizemeClause, 0.5);
		Tier(r, "sizeme-1", sizeme, 1, sizemeClause, 0.5);
		const string revenant = "耐久が減少するほど魔法被ダメージが軽減  （現在耐久が9/6/3割以下の場合、それぞれ-10/-25/-40%）";
		const string revenantClause = "耐久が減少するほど魔法被ダメージが軽減";
		Tier(r, "revenant-100", revenant, 100, revenantClause, 0.0);
		Tier(r, "revenant-85", revenant, 85, revenantClause, 0.90);
		Tier(r, "revenant-55", revenant, 55, revenantClause, 0.75);
		Tier(r, "revenant-25", revenant, 25, revenantClause, 0.60);

		r.Group("tiered/attacker-increase");
		const string shizel = "耐久が減少するほど与ダメージが上昇  （耐久が9/6/3割以下の場合、それぞれ1.1/1.3/1.5倍上昇）";
		const string shizelClause = "耐久が減少するほど与ダメージが上昇";
		Tier(r, "shizel-100", shizel, 100, shizelClause, 0.0);
		Tier(r, "shizel-85", shizel, 85, shizelClause, 1.1);
		Tier(r, "shizel-55", shizel, 55, shizelClause, 1.3);
		Tier(r, "shizel-25", shizel, 25, shizelClause, 1.5);
		const string myuze = "常に魔法防御力+20%、更に耐久が減少するほど魔法防御力が上昇  (耐久が8/6/4/2割以下の場合、それぞれ40/60/80/100%上昇)";
		Tier(r, "myuze-20", myuze, 20, "更に耐久が減少するほど魔法防御力が上昇", 2.0);
		const string above = "現在耐久が8/6/4割以上の場合、それぞれ10/20/30%上昇（与ダメージ）";
		Tier(r, "above-85", above, 85, "与ダメージ", 1.1);
		Tier(r, "above-65", above, 65, "与ダメージ", 1.2);
		Tier(r, "above-45", above, 45, "与ダメージ", 1.3);
		Tier(r, "above-35", above, 35, "与ダメージ", 0.0);

		r.Group("tiered/negatives");
		TierNoSpec(r, "dot-status", "毒/火傷状態の敵全ての被ダメージがそれぞれ+15%");
		TierNoSpec(r, "hp-under-20", "現在耐久が20%以下の場合、被ダメージ-40%（前衛のみ）");
		TierNoSpec(r, "poporot-as2", "物理/魔法被ダメージ-70%、被弾時、3秒間回避率-5%");
		TierNoSpec(r, "engrave-phys-15", "物理被ダメージ-15%（前衛のみ）");
		TierNoSpec(r, "dream-crystalizer", "耐久が0になると一度だけ最大耐久の50%分を回復し、即復活する（前衛のみ）  現在耐久が50%以下の場合、被ダメージ-10%（前衛のみ）");

		r.Group("tiered/occurrence-gate");
		const string sizeme2 = "耐久が減少するほど物理/魔法被ダメージが減少  (現在耐久が9/6/3割以下の場合、それぞれ0.1/0.3/0.5倍)";
		r.Eq("one-occurrence-in-the-real-text", TieredModifier.CountOccurrences(sizeme2, "被ダメージ"), 1);
		r.Eq("one-occurrence-in-a-plain-text", TieredModifier.CountOccurrences("物理被ダメージ-15%（前衛のみ）", "被ダメージ"), 1);
	}
}
