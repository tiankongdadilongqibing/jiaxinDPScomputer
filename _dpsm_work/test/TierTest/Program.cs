using System;
using DpsMeter;

// Offline check of TieredModifier against the real in-game ability texts (copied from
// BepInEx\config\dpsmeter_runtime.log). No IL2CPP involved: TieredModifier is pure string maths.
internal static class Program
{
	private static int _fail;

	private static void Expect(string label, string abilityText, int hp, string keywordClause, double want, string note)
	{
		TieredModifier.Spec spec = TieredModifier.Get(abilityText);
		if (spec == null) { Console.WriteLine("FAIL " + label + " : no spec parsed"); _fail++; return; }
		double f;
		string n;
		bool ok = TieredModifier.TryEvaluate(spec, hp, keywordClause, out f, out n);
		bool good = want <= 0.0 ? !ok : (ok && Math.Abs(f - want) < 0.0005);
		Console.WriteLine((good ? "ok   " : "FAIL ") + label + " hp=" + hp + " → " + (ok ? ("×" + f.ToString("F4") + "  [" + n + "]") : "inactive") + (good ? "" : ("   want " + (want <= 0.0 ? "inactive" : ("×" + want.ToString("F4"))))));
		if (!good) _fail++;
	}

	private static void ExpectNoSpec(string label, string abilityText)
	{
		TieredModifier.Spec spec = TieredModifier.Get(abilityText);
		bool good = spec == null;
		Console.WriteLine((good ? "ok   " : "FAIL ") + label + " : " + (spec == null ? "no tier list (correct)" : "unexpected tier list!"));
		if (!good) _fail++;
	}

	private static void Main()
	{
		// ---- [痺夏]シゼル＝メ 素質 (victim side, 0.1/0.3/0.5倍 written as a REDUCTION) ----
		const string sizeme = "耐久が減少するほど物理/魔法被ダメージが減少  (現在耐久が9/6/3割以下の場合、それぞれ0.1/0.3/0.5倍)";
		const string sizemeClause = "耐久が減少するほど物理/魔法被ダメージが減少";
		Expect("痺夏/物理 100%", sizeme, 100, sizemeClause, 0.0, null);
		Expect("痺夏/物理  90%", sizeme, 90, sizemeClause, 0.9, null);
		Expect("痺夏/物理  61%", sizeme, 61, sizemeClause, 0.9, null);
		Expect("痺夏/物理  60%", sizeme, 60, sizemeClause, 0.7, null);
		Expect("痺夏/物理  55%", sizeme, 55, sizemeClause, 0.7, null);
		Expect("痺夏/物理  30%", sizeme, 30, sizemeClause, 0.5, null);
		Expect("痺夏/物理   1%", sizeme, 1, sizemeClause, 0.5, null);

		// ---- レヴナント 素質 (victim side, -10/-25/-40%) ----
		const string revenant = "耐久が減少するほど魔法被ダメージが軽減  （現在耐久が9/6/3割以下の場合、それぞれ-10/-25/-40%）";
		const string revenantClause = "耐久が減少するほど魔法被ダメージが軽減";
		Expect("レヴ 100%", revenant, 100, revenantClause, 0.0, null);
		Expect("レヴ  85%", revenant, 85, revenantClause, 0.90, null);
		Expect("レヴ  55%", revenant, 55, revenantClause, 0.75, null);
		Expect("レヴ  25%", revenant, 25, revenantClause, 0.60, null);

		// ---- シゼル＝メ 素質 (attacker side, ×1.1/1.3/1.5) ----
		const string shizel = "耐久が減少するほど与ダメージが上昇  （耐久が9/6/3割以下の場合、それぞれ1.1/1.3/1.5倍上昇）";
		const string shizelClause = "耐久が減少するほど与ダメージが上昇";
		Expect("シゼル 100%", shizel, 100, shizelClause, 0.0, null);
		Expect("シゼル  85%", shizel, 85, shizelClause, 1.1, null);
		Expect("シゼル  55%", shizel, 55, shizelClause, 1.3, null);
		Expect("シゼル  25%", shizel, 25, shizelClause, 1.5, null);

		// ---- ミューゼ 素質: same shape but about 魔法防御力, must never reach the damage maths
		//      (the caller only evaluates a clause that itself contains 被ダメージ/与ダメージ) ----
		const string myuze = "常に魔法防御力+20%、更に耐久が減少するほど魔法防御力が上昇  (耐久が8/6/4/2割以下の場合、それぞれ40/60/80/100%上昇)";
		Expect("ミューゼ parse 20%", myuze, 20, "更に耐久が減少するほど魔法防御力が上昇", 2.0, null);

		// ---- 以上 lists take the largest satisfied threshold ----
		const string above = "現在耐久が8/6/4割以上の場合、それぞれ10/20/30%上昇（与ダメージ）";
		Expect("以上  85%", above, 85, "与ダメージ", 1.1, null);
		Expect("以上  65%", above, 65, "与ダメージ", 1.2, null);
		Expect("以上  45%", above, 45, "与ダメージ", 1.3, null);
		Expect("以上  35%", above, 35, "与ダメージ", 0.0, null);

		// ---- negatives: nothing tiered may be invented for ordinary texts ----
		ExpectNoSpec("毒/火傷 それぞれ", "毒/火傷状態の敵全ての被ダメージがそれぞれ+15%");
		ExpectNoSpec("耐久20%以下", "現在耐久が20%以下の場合、被ダメージ-40%（前衛のみ）");
		ExpectNoSpec("ポポロットAS2", "物理/魔法被ダメージ-70%、被弾時、3秒間回避率-5%");
		ExpectNoSpec("刻印 物理-15%", "物理被ダメージ-15%（前衛のみ）");
		ExpectNoSpec("夢のクリスタライザー", "耐久が0になると一度だけ最大耐久の50%分を回復し、即復活する（前衛のみ）  現在耐久が50%以下の場合、被ダメージ-10%（前衛のみ）");

		// ---- occurrence counting used as the safety gate in AbilityScan ----
		int c1 = TieredModifier.CountOccurrences(sizeme, "被ダメージ");
		int c2 = TieredModifier.CountOccurrences("物理被ダメージ-15%（前衛のみ）", "被ダメージ");
		Console.WriteLine((c1 == 1 && c2 == 1 ? "ok   " : "FAIL ") + "CountOccurrences 被ダメージ = " + c1 + " / " + c2);
		if (c1 != 1 || c2 != 1) _fail++;

		Console.WriteLine(_fail == 0 ? "== ALL PASS ==" : ("== " + _fail + " FAILED =="));
		Environment.Exit(_fail == 0 ? 0 : 1);
	}
}
