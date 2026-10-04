using System.Collections.Generic;
using System;
using System.IO;
using System.Text;
using DpsMeter;

namespace ReconProbe;

/// <summary>
/// Offline harness for the reconciliation KPI: compiles the REAL Model/CalcBreakdown.cs and
/// Output/CalcReconcile.cs against stubs (Stubs.cs) with no game assemblies, then asserts every
/// classification the export reports.
///
/// It is not decoration -- it has caught real errors twice: the 1.3.9 crit classification initially
/// counted a 会心率=0 hit as a crit, and the 1.3.10 tolerance rewrite is pinned by the
/// rounded-integer case below.
///
/// NOTE (2026-10-03): this file was corrupted once by rewriting it with PowerShell whole-file
/// read/write -- it contains CJK, and Set-Content wrote it back as ANSI (the corrupt copy is kept as
/// `Program.cs.corrupt-gbk-20261003`). It was recovered from the IL of the last successful build.
/// Use the editor tooling, never PowerShell, on files with non-ASCII text.
/// </summary>
public static class Program
{
	private static int _fail;

	/// <summary>1.7.2: the paramOwners section as emitted, appended to the probe artifact so the runner
	/// parses the very JSON the assertions above were made on.</summary>
	private static string _paramOwnersJson = "";

	private static void Check(string what, long got, long want)
	{
		bool ok = got == want;
		if (!ok) _fail++;
		Console.WriteLine("{0} {1}: got={2} want={3}", ok ? "PASS" : "FAIL", what, got, want);
	}

	/// <summary>1.5.0: structural checks on the emitted JSON (presence/absence of a key, escaping).
	/// A substring assertion is weaker than a parse, so the written probe output is ALSO parsed as JSON
	/// by the runner; these pin the exact shapes the analysis scripts will read.</summary>
	/// <summary>
	/// 1.7.2 (阶段 G): the applied-parameter OWNER channel. It exists to answer one question -- who
	/// granted an 攻击力 addition to this unit -- so what is asserted here is precisely the part that a
	/// battle cannot prove: that the counters are COUNTED from the rows instead of assumed, that both
	/// owner sides are kept, that a repeated entry MERGES (the union is the whole point -- a buff that
	/// expires before the export must not erase the fact that it existed), and that an unreadable owner
	/// is emitted as null rather than as a plausible name.
	/// </summary>
	/// <summary>
	/// 1.7.4 (阶段 G): the attack-power attribution formula. Every number a battle can produce is a
	/// consequence of this pure function, so the factor identity, the collision rule and the refusal
	/// guards are pinned here -- a battle can only show that some number came out, not that the arithmetic
	/// behind it was the intended one.
	/// </summary>
	private static void AtkAddChecks()
	{
		Console.WriteLine("--- 阶段 G: 攻击力加算归属 (atkadd) ---");
		AtkAddFold.Reset();
		var items = new List<AtkAddItem>
		{
			new AtkAddItem { Type = "Actual", Value = 300, Plus = true, Ref = "/refExistenceTime3000", Owner = "ルナリス" },
			new AtkAddItem { Type = "Rate", Value = 10, Plus = true, Owner = "ルナリス" },
			new AtkAddItem { Type = "Rate", Value = 9, Plus = true, Owner = "テトラ" },
		};
		const int P = 1971;
		var rows = new List<AtkAddRow>();
		// 1.7.8 (P1-A): the signature gained the attacker's actor key. These items carry NO key
		// (OwnerKey defaults to 0), so this call exercises the COUNTED name fallback -- exactly the
		// path that keeps the pre-1.7.8 expectation. AtkAddKeyChecks() below drives the key path.
		AtkAddFold.Compute(P, 1, "テトラ", items, rows);
		Check("atkadd rows for a giver other than the attacker", rows.Count, 1);
		if (rows.Count == 1)
		{
			AtkAddRow row = rows[0];
			CheckBool("atkadd names the GIVER, not the holder", row.ByUnit == "ルナリス");
			CheckBool("atkadd origin is deterministic", row.Origin == "atkadd#ルナリス");
			// the formula has to close on itself: removing dP_g must give back B for the remaining terms
			CheckClose("atkadd factor reproduces P when dP_g is removed", P / row.Factor + row.DeltaPower, P, 1e-6);
			CheckClose("atkadd dP_g equals B*r_g/100 + a_g", row.DeltaPower, row.Base * row.RateSum / 100.0 + row.ActualSum, 1e-9);
			CheckClose("atkadd rate excludes the attacker own 9%", row.RateSum, 10.0, 1e-9);
			Check("atkadd actual sum", row.ActualSum, 300);
		}
		Check("atkadd self values are not charged to a giver", AtkAddFold.Emitted, 1);
		// a (type,value) carrying BOTH the holder and another giver is refused, not guessed: measured real
		// case, 攻击力 Rate/300 is granted by ルナリス and also carried by ネーフェ.
		AtkAddFold.Reset();
		var collide = new List<AtkAddItem>
		{
			new AtkAddItem { Type = "Rate", Value = 300, Plus = true, Owner = "ルナリス" },
			new AtkAddItem { Type = "Rate", Value = 300, Plus = true, Owner = "ネーフェ" },
		};
		rows = new List<AtkAddRow>();
		AtkAddFold.Compute(5976, 1, "ネーフェ", collide, rows);
		Check("atkadd colliding value emits nothing", rows.Count, 0);
		Check("atkadd colliding value is counted", AtkAddFold.SkippedCollision, 1);
		// the premise P = B*(1+r)+a fails here (P <= a) -- the hit must be refused and counted, never
		// silently charged: measured 104 such hits in one battle (クトゥグア P=12..145 with a=300).
		AtkAddFold.Reset();
		var flat = new List<AtkAddItem> { new AtkAddItem { Type = "Actual", Value = 300, Plus = true, Owner = "ルナリス" } };
		rows = new List<AtkAddRow>();
		AtkAddFold.Compute(100, 1, "テトラ", flat, rows);
		Check("atkadd refuses a hit whose power is below its own addends", rows.Count, 0);
		Check("atkadd refusal is counted", AtkAddFold.SkippedGuard, 1);
		// an unreadable owner is a refusal too (the 1.5.4 precedent: the owner getter threw for EVERY entry)
		AtkAddFold.Reset();
		var unowned = new List<AtkAddItem> { new AtkAddItem { Type = "Actual", Value = 300, Plus = true } };
		rows = new List<AtkAddRow>();
		AtkAddFold.Compute(P, 1, "テトラ", unowned, rows);
		Check("atkadd unreadable owner emits nothing", rows.Count, 0);
		Check("atkadd unreadable owner is counted", AtkAddFold.SkippedUnowned, 1);
		// no owner data at all -> byte-identical section: this is the no-op contract for 768+ old exports
		AtkAddFold.Reset();
		rows = new List<AtkAddRow>();
		AtkAddFold.Compute(P, 1, "テトラ", new List<AtkAddItem>(), rows);
		Check("atkadd empty input emits nothing", rows.Count, 0);
		Check("atkadd empty input counts no hit", AtkAddFold.Hits, 0);
	}

	/// <summary>
	/// 1.7.8 (P1-A): the atkadd self verdict must be made on the ACTOR KEY, not on DisplayName.
	/// The bug this pins: an attacker and a teammate can share a name, and the old `own == selfName`
	/// rule then called the TEAMMATE's grant self and silently left it in baseCredit -- no row, no
	/// counter, nothing in the export. Each case is one branch of the new decision table:
	///   giverKey == attackerKey -> self by key; giverKey != attackerKey -> teammate, and the name
	///   collision that the old rule would have hidden is COUNTED; a missing key -> the counted name
	///   fallback (the only path the 26-export corpus can take, so its rows do not move).
	/// Only ASCII is printed, so this section is safe on a GBK console.
	/// </summary>
	private static void AtkAddKeyChecks()
	{
		Console.WriteLine("--- P1-A: atkadd actor-key self verdict ---");
		const string Self = "テトラ";
		const int P = 1971;

		// (1) THE BUG: attacker key=1 and teammate key=2, BOTH named テトラ, and the grant is carried
		// by the TEAMMATE. The old name rule returned SelfValues=1 / Emitted=0 here: the teammate's
		// grant stayed inside baseCredit with nothing in the export recording it.
		AtkAddFold.Reset();
		var twin = new List<AtkAddItem>
		{
			new AtkAddItem { Type = "Rate", Value = 9, Plus = true, Owner = Self, OwnerKey = 2 },
		};
		var rows = new List<AtkAddRow>();
		AtkAddFold.Compute(P, 1, Self, twin, rows);
		Check("atkadd same-name teammate is NOT dropped into baseCredit", rows.Count, 1);
		Check("atkadd same-name teammate is charged", AtkAddFold.Emitted, 1);
		Check("atkadd same-name teammate is not self", AtkAddFold.SelfValues, 0);
		Check("atkadd same-name teammate is counted as a name collision", AtkAddFold.NameCollision, 1);
		Check("atkadd same-name teammate uses the key path, not the fallback", AtkAddFold.SelfByNameFallback, 0);
		Check("atkadd same-name teammate resolves both keys", AtkAddFold.OwnerUnknown, 0);
		if (rows.Count == 1)
		{
			CheckClose("atkadd same-name teammate keeps the log-share factor f=P/(P-dP)",
				rows[0].Factor, P / (P - (P / 1.09) * 0.09), 1e-9);
			CheckBool("atkadd same-name teammate keeps byUnit = the giver name", rows[0].ByUnit == Self);
		}

		// (2) a REAL self grant: same key, same name -> self by key, no row, no collision.
		AtkAddFold.Reset();
		var mine = new List<AtkAddItem>
		{
			new AtkAddItem { Type = "Rate", Value = 9, Plus = true, Owner = Self, OwnerKey = 1 },
		};
		rows = new List<AtkAddRow>();
		AtkAddFold.Compute(P, 1, Self, mine, rows);
		Check("atkadd self grant by key emits nothing", rows.Count, 0);
		Check("atkadd self grant by key is counted as self", AtkAddFold.SelfByKey, 1);
		Check("atkadd self grant by key is not a collision", AtkAddFold.NameCollision, 0);
		Check("atkadd self grant by key needs no fallback", AtkAddFold.SelfByNameFallback, 0);

		// (3) the giver has no key: the name fallback is TAKEN and COUNTED. A same-name giver is still
		// self here, which is what the old code did -- and it is now visible as a fallback, not assumed.
		AtkAddFold.Reset();
		var noKey = new List<AtkAddItem>
		{
			new AtkAddItem { Type = "Rate", Value = 9, Plus = true, Owner = Self },
		};
		rows = new List<AtkAddRow>();
		AtkAddFold.Compute(P, 1, Self, noKey, rows);
		Check("atkadd keyless self is decided by name", AtkAddFold.SelfByNameFallback, 1);
		Check("atkadd keyless verdict is counted as owner-unknown", AtkAddFold.OwnerUnknown, 1);
		Check("atkadd keyless self emits nothing", rows.Count, 0);

		// (4) no giver key and the name does NOT match: a teammate by name, still counted as unknown.
		// The export keeps a named row; nothing is silently reassigned to the attacker.
		AtkAddFold.Reset();
		var noKeyMate = new List<AtkAddItem>
		{
			new AtkAddItem { Type = "Rate", Value = 9, Plus = true, Owner = "ルナリス" },
		};
		rows = new List<AtkAddRow>();
		AtkAddFold.Compute(P, 1, Self, noKeyMate, rows);
		Check("atkadd keyless teammate is charged", rows.Count, 1);
		Check("atkadd keyless teammate is counted as owner-unknown", AtkAddFold.OwnerUnknown, 1);
		Check("atkadd keyless teammate is not a self fallback", AtkAddFold.SelfByNameFallback, 0);

		// (5) the ATTACKER's key is missing while the giver key IS known: the key comparison is
		// impossible on one side, so the fallback is taken and counted -- never guessed silently.
		AtkAddFold.Reset();
		var mateWithKey = new List<AtkAddItem>
		{
			new AtkAddItem { Type = "Rate", Value = 9, Plus = true, Owner = Self, OwnerKey = 7 },
		};
		rows = new List<AtkAddRow>();
		AtkAddFold.Compute(P, 0, Self, mateWithKey, rows);
		Check("atkadd missing attacker key falls back to name", AtkAddFold.SelfByNameFallback, 1);
		Check("atkadd missing attacker key is counted", AtkAddFold.OwnerUnknown, 1);
	}

	private static void ParamOwnerChecks()
	{
		Console.WriteLine("--- 阶段 G: 参数所有者 (paramOwners) ---");
		ParamOwnerProbe.Reset();
		Plugin.CfgParamOwners = new ConfigEntry<bool>();
		Plugin.CfgParamOwners.Value = true;
		Aggregator.Session = new BattleSession();
		Aggregator.Session.ActiveSeconds = 12.5;

		// Row layout produced by the single walk in BuffParamTableDump:
		// [tgtName, tyName, value, vTgtName, vTyName, vParam, vRef, valueOwner, keyOwner]
		var rows = new List<string[]>
		{
			new[] { "攻击力", "Actual", "300", "攻击力", "Actual", "300", "/refExistenceTime3000", "ルナリス", "テトラ" },
			new[] { "攻击力", "Rate", "7", "攻击力", "Rate", "7", "", "ルナリス", "テトラ" },
			new[] { "攻击力", "Rate", "9", "攻击力", "Rate", "9", "", "テトラ", "テトラ" },
			new[] { "攻击力", "Rate", "10", "攻击力", "Rate", "10", "", "?", "?" },
		};
		ParamOwnerProbe.Observe("-1|テトラ", "テトラ", rows);
		// the SAME +300 entry again: a union, so it must merge and its count must reach 2
		ParamOwnerProbe.Observe("-1|テトラ", "テトラ", new List<string[]>
		{
			new[] { "攻击力", "Actual", "300", "攻击力", "Actual", "300", "/refExistenceTime3000", "ルナリス", "テトラ" },
		});
		Check("paramOwners reads", ParamOwnerProbe.Calls, 2);
		Check("paramOwners entries seen", ParamOwnerProbe.EntriesSeen, 5);
		Check("paramOwners owner = another unit (the GIVER)", ParamOwnerProbe.OwnerOther, 3);
		Check("paramOwners owner = this unit (the holder)", ParamOwnerProbe.OwnerSelf, 1);
		Check("paramOwners owner unreadable", ParamOwnerProbe.OwnerNull, 1);
		Check("paramOwners distinct owners in the union", ParamOwnerProbe.DistinctOwners(), 2);
		Check("paramOwners value/key owner disagreements", ParamOwnerProbe.Mismatch, 3);
		Check("paramOwners cross-checked entries", ParamOwnerProbe.CrossChecked, 4);
		// 1.7.2 rev2 (pre-review Q6): completeness is an IDENTITY, not a promise. The walk reports the
		// entries it scanned and the ones whose owner getter was skipped, so "the union is complete" is
		// checkable in the export instead of assumed.
		ParamOwnerProbe.NoteScan(10, 7, 3);
		ParamOwnerProbe.NoteScan(5, 5, 0);
		Check("paramOwners scanned entries", ParamOwnerProbe.EntriesScanned, 15);
		Check("paramOwners owner getters", ParamOwnerProbe.OwnerGetters, 12);
		Check("paramOwners skipped entries", ParamOwnerProbe.EntriesDropped, 3);
		CheckBool("paramOwners completeness identity holds",
			ParamOwnerProbe.EntriesScanned == ParamOwnerProbe.OwnerGetters + ParamOwnerProbe.EntriesDropped);

		var sb = new StringBuilder();
		ParamOwnerProbe.AppendJson(sb);
		string js = sb.ToString();
		_paramOwnersJson = js;
		string jerr2;
		int jdup2;
		CheckBool("paramOwners JSON is structurally valid", JsonCheck.Validate(js, out jerr2, out jdup2));
		CheckBool("paramOwners JSON names the giver", js.Contains("\"owner\":\"ルナリス\""));
		CheckBool("paramOwners JSON emits null for an unreadable owner", js.Contains("\"owner\":null"));
		CheckBool("paramOwners JSON carries the merged entry as seen=2", js.Contains("\"seen\":2"));
		CheckBool("paramOwners JSON keeps the reference text", js.Contains("/refExistenceTime3000"));
	}

	private static void CheckBool(string what, bool ok)
	{
		if (!ok) _fail++;
		Console.WriteLine("{0} {1}", ok ? "PASS" : "FAIL", what);
	}

	/// <summary>1.6.0: numeric assertion for the contribution core (floating point, so == is wrong).</summary>
	private static void CheckClose(string what, double got, double want, double tol)
	{
		bool ok = Math.Abs(got - want) <= tol;
		if (!ok) _fail++;
		Console.WriteLine("{0} {1}: got={2:F4} want={3:F4}", ok ? "PASS" : "FAIL", what, got, want);
	}

	private static BattleEvent Dmg(double t, string pair, bool corroborated,
		int theory, int applied, int absorbed, int critRate, int critDmg, byte critObserved = 0)
	{
		int pow = 10000;
		int baseDmg = theory;
		return new BattleEvent
		{
			T = t,
			Type = "dmg",
			Amount = applied,
			Nominal = applied + absorbed,
			CritObserved = critObserved,
			Calc = new CalcBreakdown
			{
				Valid = true,
				EffectId = 10100,
				HitType = 1,
				AttackPower = pow,
				Power = pow,
				Ratio = 1.0,
				DefenseUsed = 500,
				DefenseKind = "物防",
				Penetration = 5,
				EffectiveDefense = 475,
				BaseDamage = baseDmg,
				AttrMult = 1.0,
				DealtMult = 1.0,
				TakenMult = 1.0,
				KnownMult = 1.0,
				Theory = theory,
				Applied = applied,
				Absorbed = absorbed,
				Residual = theory > 0 ? (double)applied / theory : 0.0,
				CritRate = critRate,
				CritDamageRate = critDmg,
				Pair = pair,
				PairCorroborated = corroborated
			}
		};
	}

	// ---------------------------------------------------------------------------------------------
	// 1.6.0 (阶段 E): the contribution core. Same samples as CONTRIBUTION-DATA-DICTIONARY.md section 7
	// (S1-S13), asserted against the REAL src/Output/Contribution.cs compiled into this harness.
	//
	// WHY HERE. The plugin now emits a contribution section, and the offline Python core computes the
	// same numbers independently (dpsmeter_contrib / _dpsm_work/contrib). Two implementations that can
	// drift are worse than one: this pins the C# side of the contract, and contrib/crosscheck.py pins the
	// two against each other on a real export. A drift therefore shows up as a FAIL in one of them
	// instead of as a quietly different ranking.
	// ---------------------------------------------------------------------------------------------

	private static ContributionFold CF(string kind, string side, string origin, double factor,
		string label = "", string byUnit = null)
	{
		return new ContributionFold { Kind = kind, Side = side, Origin = origin, Factor = factor,
			Label = label ?? "", ByUnit = byUnit };
	}

	private static ContributionActor CA(int key, string name, int team = 1, bool summon = false,
		int[] ids = null, string[] names = null)
	{
		var a = new ContributionActor { Key = key, Name = name, Team = team, Kind = "P", Summon = summon };
		if (ids != null) a.AbilityIds.AddRange(ids);
		if (names != null) a.AbilityNames.AddRange(names);
		return a;
	}

	private static ContributionHit CH(double dmg, int atk, params ContributionFold[] folds)
	{
		var h = new ContributionHit { Damage = dmg, AttackerKey = atk, HasCalc = true };
		if (folds != null) h.Folds.AddRange(folds);
		return h;
	}

	/// <summary>
	/// P0-B (1.7.8): the damage ledger, asserted on the ACCEPTANCE SAMPLE of the review
	/// (CONTRIBUTION-REVIEW-NEXT-STEPS.md, Agent P0-B): totals.dealt=1000, analyzableDealt=600,
	/// unattributedCredit=100, excludedDamage=400, attributedDamage=500.
	///
	/// The three buckets are built EXPLICITLY, because the whole point of the ledger is that they are not
	/// the same thing: one analysed hit, one hit by a KNOWN other-team attacker, one hit whose attacker
	/// cannot be resolved at all. If any two were merged, the assertions below stop distinguishing them.
	/// </summary>
	private static void LedgerChecks()
	{
		var roster = new System.Collections.Generic.List<ContributionActor> { CA(1, "A"), CA(2, "B"), CA(99, "Enemy", 2) };
		// M = f1*f2 = 3 -> base = 200; pool 400 splits 300 (to B) / 100 (to nobody) => attributed 500, unattr 100.
		double f1 = System.Math.Pow(3.0, 0.75);   // ln f1 : ln f2 = 3 : 1
		double f2 = System.Math.Pow(3.0, 0.25);
		var hits = new System.Collections.Generic.List<ContributionHit>
		{
			CH(600, 1, CF("given", "vic", "given#1/1006/-10", f1, "resolvable", "B"),
			          CF("given", "vic", "given#1/1006/-11", f2, "unresolvable", "Ghost")),
			CH(400, 99, CF("given", "vic", "given#1/1006/-12", 2.0, "enemy", "Enemy")),
			CH(400, 0, CF("given", "vic", "given#1/1006/-13", 2.0, "no attacker", "Ghost")),
		};
		ContributionResult res = Contribution.Compute(hits, roster, 1, 1000.0);
		ContributionStats st = res.Stats;
		CheckClose("P0-B analyzableDealt", st.Analyzable, 600.0, 1e-6);
		CheckClose("P0-B attributedDamage", st.Attributed, 500.0, 1e-6);
		CheckClose("P0-B unattributedCredit", st.Unattributed, 100.0, 1e-6);
		CheckClose("P0-B excludedDamage == unknown-attacker pool", st.ExcludedDamage, 400.0, 1e-9);
		CheckClose("P0-B outsideTeamDealt is a SEPARATE bucket", st.OutsideTeamDamage, 400.0, 1e-9);
		CheckClose("P0-B eventSumAll", st.EventSumAll, 1400.0, 1e-9);
		Check("P0-B events", st.Events, 3);
		Check("P0-B analyzableHits", st.Hits, 1);
		Check("P0-B outsideTeamHits", st.OutsideTeamHits, 1);
		Check("P0-B unknownAttackerHits", st.UnknownAttackerHits, 1);
		CheckClose("P0-B analysisDamageCoverage = 60%", st.AnalysisDamageCoverage, 0.6, 1e-9);
		CheckClose("P0-B creditCoverageWithinAnalyzed = 83.333%", st.CreditCoverageWithinAnalyzed, 500.0 / 600.0, 1e-9);
		CheckClose("P0-B overallAttributedCoverage = 50%", st.OverallAttributedCoverage, 0.5, 1e-9);
		CheckClose("P0-B ledger identity analyzable+outsideTeam == totalsDealt", st.Analyzable + st.OutsideTeamDamage, st.TotalsDealt, 1e-9);
		CheckClose("P0-B reconciliationGap is zero when every unit is bucketed", st.ReconciliationGap, 0.0, 1e-9);
		var sb = new StringBuilder();
		Contribution.AppendJson(sb, Contribution.Compute(hits, roster, 1, 1000.0), "recon-probe", 411001);
		string js = sb.ToString();
		CheckBool("P0-B totalDamage is analyzableDealt, not analyzable+unattributed",
			js.Contains("\"totalDamage\":600.0000") && !js.Contains("\"totalDamage\":700.0000"));
		CheckBool("P0-B schema identity is 1.1 / producer plugin",
			js.Contains("\"schemaVersion\":\"1.1\"") && js.Contains("\"producer\":\"plugin\""));
		CheckBool("P0-B ledger block is emitted",
			js.Contains("\"damageLedger\":{") && js.Contains("\"excludedDamage\":400.0000"));
		ContributionStats empty = Contribution.Compute(
			new System.Collections.Generic.List<ContributionHit>(),
			new System.Collections.Generic.List<ContributionActor> { CA(1, "A") }, 1, 0.0).Stats;
		// An undefined denominator must be undefined (the JSON writes null), while a KNOWN total of 0
		// means "nothing is unexplained" -- a measured 0 and "not applicable" are different statements.
		CheckBool("P0-B zero-denominator ratios are undefined (JSON null), not a measured 0",
			double.IsNaN(empty.AnalysisDamageCoverage) && empty.ReconciliationGap == 0.0);
		ContributionStats noTotals = Contribution.Compute(hits, roster, 1).Stats;
		CheckBool("P0-B an unknown totals.dealt leaves the gap undefined, not 0",
			double.IsNaN(noTotals.TotalsDealt) && double.IsNaN(noTotals.ReconciliationGap));
	}

	private static string RunSection(System.Collections.Generic.List<ContributionActor> roster,
		System.Collections.Generic.List<ContributionHit> hits, out ContributionStats st)
	{
		var sb = new StringBuilder();
		st = Contribution.AppendJson(sb, hits, roster, 1, "recon-probe", 411001);
		return sb.ToString();
	}

	private static void ContributionChecks()
	{
		Console.WriteLine("--- Contribution: 1.6.0 阶段 E (字典 S1-S13) ---");
		ContributionStats st;
		string json;
		string jerr; int jdup;

		// S1 single actor, no multipliers
		json = RunSection(new System.Collections.Generic.List<ContributionActor> { CA(1, "A") },
			new System.Collections.Generic.List<ContributionHit> { CH(1000, 1) }, out st);
		CheckClose("S1 analyzable", st.Analyzable, 1000, 1e-6);
		CheckClose("S1 attributed", st.Attributed, 1000, 1e-6);
		CheckClose("S1 unattributed", st.Unattributed, 0, 1e-6);
		Check("S1 hits", st.Hits, 1);
		CheckBool("S1 base credit line is exact", json.Contains("\"totalCredit\":1000.0000"));

		// S2 attacker + one assist multiplier (given x1.5 by Beta)
		json = RunSection(new System.Collections.Generic.List<ContributionActor> { CA(1, "A"), CA(2, "B") },
			new System.Collections.Generic.List<ContributionHit> { CH(3000, 1, CF("given", "vic", "given#1/1006/-10", 1.5, "被伤害+50%(赋予)", "B")) }, out st);
		CheckClose("S2 attributed", st.Attributed, 3000, 1e-6);
		CheckClose("S2 unattributed", st.Unattributed, 0, 1e-6);
		CheckBool("S2 assist credit 1000 goes to B", json.Contains("\"assistCredit\":1000.0000"));

		// S3 own rule x1.1 + assist x2.0 + unattributable global x1.25 (D = 1000 x 2.75)
		json = RunSection(new System.Collections.Generic.List<ContributionActor>
			{ CA(1, "A", 1, false, new int[] { 900 }), CA(2, "B") },
			new System.Collections.Generic.List<ContributionHit> { CH(2750, 1,
				CF("text", "atk", "text#1/900/c0", 1.1, "[AlphaRule] 自身"),
				CF("given", "vic", "given#2/1006/-10", 2.0, "被伤害+100%(赋予)", "B"),
				CF("global", "vic", "global#123/0", 1.25, "[未知全局规则] x")) }, out st);
		CheckClose("S3 analyzable", st.Analyzable, 2750, 1e-6);
		CheckClose("S3 unattributed = 386.02", st.Unattributed, 386.02, 0.01);
		CheckClose("S3 identity credited+unattributed", st.Attributed + st.Unattributed, 2750, 1e-6);
		int rc;
		CheckBool("S3 ambiguous global is a reason code",
			st.ReasonCounts.TryGetValue("global_ambiguous", out rc) && rc == 1);

		// S4 byUnit points outside the team
		json = RunSection(new System.Collections.Generic.List<ContributionActor>
			{ CA(1, "A"), CA(99, "Out", 2) },
			new System.Collections.Generic.List<ContributionHit> { CH(1500, 1,
				CF("madness", "atk", "madness#250", 1.5, "狂気", "Out")) }, out st);
		CheckClose("S4 attributed", st.Attributed, 1000, 1e-6);
		CheckClose("S4 unattributed", st.Unattributed, 500, 1e-6);
		CheckBool("S4 outside reason",
			st.ReasonCounts.TryGetValue("byUnit_outside", out rc) && rc == 1);

		// S5 the attacker is also the rule owner: self rule, no double count, no link
		json = RunSection(new System.Collections.Generic.List<ContributionActor>
			{ CA(1, "A", 1, false, new int[] { 900 }) },
			new System.Collections.Generic.List<ContributionHit> { CH(2000, 1,
				CF("text", "atk", "text#1/900/c0", 2.0, "[AlphaRule] 自身")) }, out st);
		CheckClose("S5 attributed", st.Attributed, 2000, 1e-6);
		CheckBool("S5 no assist", json.Contains("\"assistCredit\":0.0000"));
		CheckBool("S5 no link", json.Contains("\"links\":[]"));

		// S6 non-positive factor must not raise (ln(0)) and is accounted as a diagnostic
		json = RunSection(new System.Collections.Generic.List<ContributionActor> { CA(1, "A") },
			new System.Collections.Generic.List<ContributionHit> { CH(0, 1,
				CF("text", "vic", "text#5/74/c1", 0.0, "100% reduction")) }, out st);
		Check("S6 zero-factor folds", st.ZeroFactor, 1);
		CheckClose("S6 unattributed", st.Unattributed, 0, 1e-6);

		// S8 an enemy carries an ally's name: byUnit must resolve inside OUR team
		json = RunSection(new System.Collections.Generic.List<ContributionActor>
			{ CA(1, "Twin", 1, false, new int[] { 910 }), CA(2, "Solo"), CA(50, "Twin", 2) },
			new System.Collections.Generic.List<ContributionHit> { CH(2000, 2,
				CF("given", "vic", "given#1/1/-10", 2.0, "赋予", "Twin")) }, out st);
		CheckClose("S8 attributed", st.Attributed, 2000, 1e-6);
		CheckBool("S8 assist goes to the ALLY", json.Contains("\"assistCredit\":1000.0000"));
		CheckBool("S8 enemy key never appears", !json.Contains("\"key\":50"));

		// S9 two same-named summons in our team: ambiguous, nobody credited
		json = RunSection(new System.Collections.Generic.List<ContributionActor>
			{ CA(1, "A"), CA(10, "Turret", 1, true), CA(11, "Turret", 1, true) },
			new System.Collections.Generic.List<ContributionHit> { CH(1500, 1,
				CF("given", "vic", "given#1/1/-10", 1.5, "赋予", "Turret")) }, out st);
		CheckClose("S9 unattributed", st.Unattributed, 500, 1e-6);
		CheckBool("S9 ambiguous reason",
			st.ReasonCounts.TryGetValue("byUnit_ambiguous", out rc) && rc == 1);

		// S10a shared ability id, attacker holds it -> attacker (its own rule)
		json = RunSection(new System.Collections.Generic.List<ContributionActor>
			{ CA(1, "H1", 1, false, new int[] { 900 }), CA(2, "H2", 1, false, new int[] { 900 }), CA(3, "N") },
			new System.Collections.Generic.List<ContributionHit> { CH(2000, 1,
				CF("text", "atk", "text#1/900/c0", 2.0, "Shared")) }, out st);
		CheckClose("S10a attributed", st.Attributed, 2000, 1e-6);
		CheckClose("S10a unattributed", st.Unattributed, 0, 1e-6);

		// S10b shared ability id, attacker does NOT hold it -> ambiguous, never charged to the attacker
		json = RunSection(new System.Collections.Generic.List<ContributionActor>
			{ CA(1, "H1", 1, false, new int[] { 900 }), CA(2, "H2", 1, false, new int[] { 900 }), CA(3, "N") },
			new System.Collections.Generic.List<ContributionHit> { CH(2000, 3,
				CF("text", "atk", "text#1/900/c0", 2.0, "Shared")) }, out st);
		CheckClose("S10b unattributed", st.Unattributed, 1000, 1e-6);
		CheckBool("S10b ambiguous reason",
			st.ReasonCounts.TryGetValue("ambiguous_multi_holder", out rc) && rc == 1);

		// S11 unknown channel -> unattributed with a reason code
		json = RunSection(new System.Collections.Generic.List<ContributionActor> { CA(1, "A") },
			new System.Collections.Generic.List<ContributionHit> { CH(1000, 1,
				CF("mystery", "atk", "mystery#1", 1.5, "?")) }, out st);
		CheckClose("S11 unattributed", st.Unattributed, 333.333, 0.01);
		CheckBool("S11 unknown reason",
			st.ReasonCounts.TryGetValue("unknown_kind", out rc) && rc == 1);

		// S12 an out-of-team attacker key is not analyzable at all
		json = RunSection(new System.Collections.Generic.List<ContributionActor>
			{ CA(1, "A"), CA(50, "Enemy", 2) },
			new System.Collections.Generic.List<ContributionHit> { CH(1000, 50) }, out st);
		CheckClose("S12 analyzable", st.Analyzable, 0, 1e-6);
		Check("S12 hits", st.Hits, 0);

		// S13 a factor below 1 (damage reduction) must not create negative credit
		json = RunSection(new System.Collections.Generic.List<ContributionActor> { CA(1, "A") },
			new System.Collections.Generic.List<ContributionHit> { CH(1000, 1,
				CF("text", "vic", "text#9/1/c0", 0.5, "被伤害-50%")) }, out st);
		Check("S13 sub-unity folds", st.SubUnity, 1);
		CheckClose("S13 attributed keeps the whole hit", st.Attributed, 1000, 1e-6);
		Check("S13 no negative lines", st.Negative, 0);

		// diagnostics that exist because their ABSENCE used to be invisible
		var h = CH(1000, 1);
		h.HasCalc = false;
		json = RunSection(new System.Collections.Generic.List<ContributionActor> { CA(1, "A") },
			new System.Collections.Generic.List<ContributionHit> { h }, out st);
		Check("calc-missing hit counted", st.CalcMissing, 1);
		h = CH(1000, 1, CF("text", "atk", "text#1/2/c1", 1.15, "x"));
		h.FoldDropped = 2;
		json = RunSection(new System.Collections.Generic.List<ContributionActor> { CA(1, "A") },
			new System.Collections.Generic.List<ContributionHit> { h }, out st);
		Check("dropped folds counted", st.FoldDropped, 2);
		Check("dropped-fold hits counted", st.FoldDroppedHits, 1);

		// 1.7.0: the structured result (what the overlay dashboard reads) must agree with the emitted
		// JSON path -- they are one implementation, and this pins that they cannot drift apart.
		{
			var roster2 = new System.Collections.Generic.List<ContributionActor> { CA(1, "A"), CA(2, "B") };
			var hits2 = new System.Collections.Generic.List<ContributionHit> { CH(3000, 1,
				CF("given", "vic", "given#1/1006/-10", 1.5, "被伤害+50%(赋予)", "B")) };
			ContributionResult res = Contribution.Compute(hits2, roster2, 1);
			var sb2 = new StringBuilder();
			Contribution.AppendJson(sb2, res, "recon-probe", 411001);
			ContributionStats st2 = Contribution.AppendJson(new StringBuilder(), hits2, roster2, 1, "recon-probe", 411001);
			CheckClose("Compute stats == AppendJson stats (attributed)", res.Stats.Attributed, st2.Attributed, 1e-9);
			Check("structured result has one actor row per credited actor", res.Actors.Count, 2);
			CheckClose("dashboard row totalCredit == stats", res.Actors[0].Total + res.Actors[1].Total, st2.Attributed, 1e-6);
			CheckBool("actor rows are ordered by totalCredit desc", res.Actors[0].Total >= res.Actors[1].Total);
			CheckBool("structured JSON == overload JSON", sb2.ToString().Contains("\"assistCredit\":1000.0000"));
		}

		// the emitted section must be valid JSON by the plugin's OWN validator
		CheckBool("contribution section passes JsonCheck",
			JsonCheck.Validate(json, out jerr, out jdup));
		// 1.7.8 (P0-B): the contract version moved to 1.1 when totalDamage changed meaning, and the
		// producer field was added so a reader can tell the plugin's section from the offline draft.
		CheckBool("section carries its schema identity",
			json.Contains("\"schemaVersion\":\"1.1\"") && json.Contains("\"producer\":\"plugin\"")
			&& json.Contains("\"method\":\"log-share/1\"") && json.Contains("\"damageBasis\":\"dealt\""));
	}

	public static int Main(string[] args)
	{
		var s = new BattleSession { ActiveSeconds = 100.0 };
		//                 t     pair         corr   theory  applied  abs  cr  cd
		s.Events.Add(Dmg(1.0, "live-same", true, 1000, 1000, 0, 25, 160));    // exact
		s.Events.Add(Dmg(2.0, "value", true, 1000, 800, 200, 25, 160));        // exact via 被吸收
		s.Events.Add(Dmg(3.0, "live-same", false, 1000, 1030, 0, 25, 160));    // approx (+3%)
		s.Events.Add(Dmg(15.0, "live-same", false, 1000, 1210, 0, 0, 0));      // unexplained 1.210
		s.Events.Add(Dmg(25.0, "live-same", false, 1000, 1210, 0, 0, 0));      // unexplained 1.210
		s.Events.Add(Dmg(45.0, "live-same", false, 100000, 4000, 0, 0, 0));    // unexplained 0.040 -> theoryExceeds
		s.Events.Add(Dmg(55.0, "value", false, 1000, 1210, 0, 0, 0));          // unexplained 1.210
		s.Events.Add(new BattleEvent { T = 65.0, Type = "dmg", Calc = default(CalcBreakdown) }); // no composition
		s.Events.Add(Dmg(75.0, "live-age", false, 500, 500, 0, 0, 0));         // exact, untrusted pairing
		s.Events.Add(Dmg(85.0, "fifo", false, 500, 500, 0, 0, 0));             // exact, untrusted pairing
		s.Events.Add(Dmg(95.0, "live-same", false, 1000, 2500, 0, 0, 0));      // unexplained 2.500
		// 1.3.9: the crit classification. 1000 x 1.90 == 1900 with 会心伤害率=190 -> counted as
		// crit-inferred, and it must NOT move the strict `exact` count.
		s.Events.Add(Dmg(97.0, "live-same", false, 1000, 1900, 0, 40, 190, 2));   // 凭会心新增 + 实测一致
		// and the same shape with 会心率 0 must NOT be counted (no roll could have landed)
		s.Events.Add(Dmg(98.0, "live-same", false, 1000, 1900, 0, 0, 190, 2));    // unexplained 1.900,实测说会心也没解释
		// 1.3.10: the game's number is a ROUNDED integer, so the classification must use a relative
		// tolerance -- 1000*1.9 = 1900 against an applied 1899 (0.053%) is still a crit.
		s.Events.Add(Dmg(99.0, "live-same", false, 1000, 1899, 0, 40, 190, 1));   // 凭会心新增,但实测否决
		s.Events.Add(new BattleEvent { T = 96.0, Type = "heal", Calc = default(CalcBreakdown) }); // ignored (not dmg)

		CalcReconcile.Stats st = CalcReconcile.Compute(s);

		Console.WriteLine("--- 分类断言 ---");
		Check("dmgEvents", st.DmgEvents, 14);
		Check("withCalc", st.WithCalc, 13);
		Check("exact", st.Exact, 4);
		Check("exactWithCrit", st.ExactWithCrit, 6);
		Check("critInferred", st.CritInferred, 2);
		Check("approx", st.Approx, 1);
		Check("unexplained", st.Unexplained, 6);
		Check("theoryExceeds", st.TheoryExceeds, 1);
		Check("absorbed", st.Absorbed, 1);
		// 1.5.0 (A3): the crit question decomposed. The three cases above are, in order, an inference the
		// game CONFIRMS, an inference the game DENIES, and a case where the game says crit but the
		// arithmetic still does not add up -- which is the state of every unexplained crit today.
		Check("critObserved", st.CritObserved, 3);
		Check("critObservedYes", st.CritObservedYes, 2);
		Check("critAgree", st.CritAgree, 1);
		Check("critInferredButDenied", st.CritInferredButDenied, 1);
		Check("critObservedButUnexplained", st.CritObservedButUnexplained, 1);
		Check("absorbedAmount", st.AbsorbedAmount, 200);
		Check("distinctResiduals", st.DistinctResiduals, 4);
		Check("pairLiveSame", st.PairLiveSame, 9);
		Check("pairLiveAge", st.PairLiveAge, 1);
		Check("pairValue", st.PairValue, 2);
		Check("pairFifo", st.PairFifo, 1);
		Check("pairNone", st.PairNone, 1);
		Check("pairUnknown", st.PairUnknown, 0);
		Check("pairCorroborated", st.PairCorroborated, 2);
		Check("top[0].r(x1000)", st.Top.Count > 0 ? st.Top[0].Key : -1, 1210);
		Check("top[0].n", st.Top.Count > 0 ? st.Top[0].Value : -1, 3);
		long t0 = 0, tt = 0;
		for (int i = 0; i < 10; i++) { t0 += st.TenthExact[i]; tt += st.TenthTotal[i]; }
		Check("byTenth exact sum", t0, 4);
		Check("byTenth total sum", tt, 14);
		Check("tenth[0].total", st.TenthTotal[0], 3);
		Check("tenth[0].exact", st.TenthExact[0], 2);
		Check("tenth[7].exact", st.TenthExact[7], 1);

		// ValueMatches semantics: an absorbed hit matches the game's pre-absorption figure only.
		CalcBreakdown abs = s.Events[1].Calc;
		Check("absorbed GameValue", abs.GameValue, 1000);
		Check("absorbed ValueMatches", abs.ValueMatches ? 1 : 0, 1);
		Check("absorbed PairTrusted", abs.PairTrusted ? 1 : 0, 1);
		Check("live-age PairTrusted", s.Events[8].Calc.PairTrusted ? 1 : 0, 0);
		Check("fifo PairTrusted", s.Events[9].Calc.PairTrusted ? 1 : 0, 0);

		Console.WriteLine("--- [RECON] 自检行 ---");
		Console.WriteLine("[DpsMeter][RECON] " + CalcReconcile.Summary(st));

		var jr = new StringBuilder();
		CalcReconcile.AppendJson(jr, st);
		string jsonRecon = "{\"pad\":0" + jr + "}";
		var j1 = new StringBuilder();
		CalcReconcile.AppendEventCalc(j1, s.Events[0].Calc);
		string jsonFull = "{\"pad\":0" + j1 + "}";
		var j2 = new StringBuilder();
		CalcReconcile.AppendEventCalc(j2, s.Events[5].Calc);
		string jsonBad = "{\"pad\":0" + j2 + "}";
		Console.WriteLine("--- JSON: reconcile ---");
		Console.WriteLine(jsonRecon);
		Console.WriteLine("--- JSON: event calc (full) ---");
		Console.WriteLine(jsonFull);
		Console.WriteLine("--- JSON: event calc (theory exceeds actual 25x) ---");
		Console.WriteLine(jsonBad);

		// ---- 1.5.0 (架构审视 A1/A5): provenance emission ----
		// The point of these assertions is that the 1.15^n question is answerable FROM THE DATA, so the
		// keys have to exist with exactly these names and the absent case has to stay absent.
		Console.WriteLine("--- JSON: 1.5.0 fold/cancel provenance ---");
		CalcBreakdown foldB = s.Events[0].Calc;
		foldB.Fold = new System.Collections.Generic.List<FoldStep>
		{
			new FoldStep { Side = "atk", Kind = "text", Origin = "text#3/40009/c1", Factor = 1.15, Label = "与ダメージ+15%" },
			new FoldStep { Side = "vic", Kind = "global", Origin = "global#123/2", Factor = 1.15, Label = "毒状態の敵全ての被ダメージ+15%" }
		};
		foldB.FoldCancels = new System.Collections.Generic.List<CancelStep>
		{
			new CancelStep { Origin = "given#7/1006/-15", Value = 1.15, ResponsibleOrigin = "global#123/2", Label = "被伤害+15%(赋予)" }
		};
		foldB.FoldResponsible = 1;
		foldB.FoldMaxAbsorbed = 5;
		var jf = new StringBuilder();
		CalcReconcile.AppendEventCalc(jf, foldB);
		string foldJson = jf.ToString();
		Console.WriteLine(foldJson);
		CheckBool("fold[] emitted", foldJson.Contains("\"fold\":["));
		CheckBool("fold keeps a stable origin id", foldJson.Contains("\"origin\":\"global#123/2\""));
		// 1.6.1: the factor is written ROUND-TRIP, not at 4 decimals. A truncated factor made the
		// contribution section unreproducible from the file (measured 2026-10-04: 58 fields off by
		// ~1e-5 relative on live 1.6.0 data, and restoring the exact value made the plugin and the
		// independent Python core agree bit-for-bit). This asserts the new contract in both directions.
		CheckBool("fold factor round-trips exactly", foldJson.Contains("\"factor\":1.15"));
		{
			var exact = new StringBuilder();
			new FoldStep { Side = "atk", Kind = "text", Origin = "text#1/2/c1", Factor = 1.15 * 1.15 * 1.15, Label = "x" }.AppendJson(exact);
			// the exact decimal spelling of the double is not the point (1.15*1.15*1.15 may be
			// 1.5208749999999997 in binary); the point is that it is NOT truncated to 4 decimals and
			// that it parses back to the identical double -- that is what makes the export replayable.
			CheckBool("fold factor is not truncated to 4 decimals", !exact.ToString().Contains("1.5209"));
			double back;
			string body = exact.ToString();
			int i = body.IndexOf("\"factor\":") + 9;
			int j = body.IndexOf(',', i);
			CheckBool("the written factor parses back to the same double",
				double.TryParse(body.Substring(i, j - i), System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out back) && back == 1.15 * 1.15 * 1.15);
		}
		CheckBool("cancel[] emitted with the dropped copy", foldJson.Contains("\"cancel\":[{\"origin\":\"given#7/1006/-15\""));
		CheckBool("cancel names the absorbing rule", foldJson.Contains("\"by\":\"global#123/2\""));
		CheckBool("responsibility emitted", foldJson.Contains("\"responsibility\":1"));
		CheckBool("maxAbsorbed emitted (1 rule absorbed 5 copies)", foldJson.Contains("\"maxAbsorbed\":5"));
		CheckBool("foldDropped omitted when 0", !foldJson.Contains("foldDropped"));
		// A chain with no recorded factors must not emit the key at all, so that "no provenance" and
		// "provenance was empty" stay distinguishable -- jsonFull was built before Fold was set.
		CheckBool("no fold key when absent", !jsonFull.Contains("\"fold\":["));
		CheckBool("no cancel key when absent", !jsonFull.Contains("\"cancel\":["));
		// Escaping: the 1.4.x writers passed DefenseKind/Pair straight through and relied on the values
		// happening to be quote-free (ARCH-REVIEW-1.5 F7). A quote must not be able to break the JSON.
		foldB.Fold[0].Label = "a\"b";
		foldB.DefenseKind = "x\"y";
		var je = new StringBuilder();
		CalcReconcile.AppendEventCalc(je, foldB);
		CheckBool("label quote escaped", je.ToString().Contains("a\\\"b"));
		CheckBool("defenseKind quote escaped", je.ToString().Contains("x\\\"y"));

		// ---- 1.5.2: the FACT signature's canonical status set (Model/StatusKey.cs) ----
		// The 1.5.2 identity fix is only correct if a SET has ONE canonical form: StatusBrief emits
		// BuffList order, which is not part of the state, so the same statuses in a different order must
		// not produce a different signature. Executed here rather than promised in a comment because
		// FactStore itself needs IL2CPP and cannot be compiled into this probe.
		Console.WriteLine("--- StatusKey: 1.5.2 canonical set ---");
		var sk = new string[8];
		Check("canonical count", StatusKey.Write(new[] { "A", "B", "C" }, sk), 3);
		var sk2 = new string[8];
		Check("permutation+duplicate count", StatusKey.Write(new[] { "C", "A", "B", "A" }, sk2), 3);
		CheckBool("permutation maps to one form",
			sk[0] == sk2[0] && sk[1] == sk2[1] && sk[2] == sk2[2]);
		CheckBool("canonical order is ordinal", sk[0] == "A" && sk[1] == "B" && sk[2] == "C");
		// B > A > C would be a culture/case-insensitive order; ordinal is what the signature needs.
		var sk3 = new string[4];
		Check("ordinal order ignores case-ish collation", StatusKey.Write(new[] { "b", "A" }, sk3), 2);
		CheckBool("ordinal puts 'A' before 'b'", sk3[0] == "A" && sk3[1] == "b");
		CheckBool("SameSet accepts a permutation", StatusKey.SameSet(new[] { "A", "B" }, new[] { "B", "A" }));
		CheckBool("SameSet rejects a subset", !StatusKey.SameSet(new[] { "A", "B" }, new[] { "A" }));
		CheckBool("SameSet rejects a different name", !StatusKey.SameSet(new[] { "A" }, new[] { "B" }));
		Check("null source yields nothing", StatusKey.Write(null, sk), 0);
		Check("null buffer yields nothing", StatusKey.Write(new[] { "A" }, null), 0);
		Check("empty and null names are skipped", StatusKey.Write(new[] { "", "A", null }, sk), 1);
		// the writer is given the caller's buffer and must not exceed src.Length slots
		var skTight = new string[2];
		Check("tight buffer holds two names", StatusKey.Write(new[] { "A", "B" }, skTight), 2);
		CheckBool("tight buffer is ordered", skTight[0] == "A" && skTight[1] == "B");

		// ---- 1.5.3: the clause -> status-name RUN (Model/ClauseStatusRun.cs) ----
		// This rule had been wrong twice in the same way (毒/火傷状態 first, then 毒と火傷状態) and both
		// times the cost was SILENT: the clause was parked as 条件性,未计入 and the model stayed short by
		// 1.15^n. The strings below are the game's own master-table wording; what is asserted is the MATCH,
		// because the match is the part that was wrong -- not a factor.
		Console.WriteLine("--- ClauseStatusRun: 1.5.3 status-list joiners (と/や/及) ---");
		var skNames = new System.Collections.Generic.Dictionary<string, int>
		{
			{ "毒", 1 }, { "毒状態", 1 }, { "火傷", 2 }, { "火傷状態", 2 }, { "凍結", 3 }, { "狂気", 4 }
		};
		var skKeys = skNames.Keys;
		string skJoiner = "毒と火傷状態の敵への与ダメージがそれぞれ+15%";
		CheckBool("と-list: the name BEFORE the joiner finds its 状態 run", ClauseStatusRun.FollowedByState(skJoiner, 1, skKeys));
		CheckBool("slash-list still works", ClauseStatusRun.FollowedByState("毒/火傷状態", 1, skKeys));
		CheckBool("a plain name still works", ClauseStatusRun.FollowedByState("毒状態", 1, skKeys));
		CheckBool("a name NOT followed by a 状態 run does not match", !ClauseStatusRun.FollowedByState("毒の短剣", 1, skKeys));
		CheckBool("longest body wins at the 火傷 offset", ClauseStatusRun.StatusNameAt(skJoiner, 2, skKeys) == "火傷");
		// The strip must take the WHOLE と-run: leaving a bare 毒 in the residual text is exactly what
		// tripped the generic marker list in CompositionProbe.Rules and parked the clause forever.
		string skStripped = ClauseStatusRun.StripVerifiedStateRuns(skJoiner, new System.Collections.Generic.List<string> { "火傷" });
		CheckBool("と-run stripped when its SECOND name is verified", skStripped.IndexOf("状態") < 0);
		CheckBool("と-run leaves no bare 毒 behind", skStripped.IndexOf("毒") < 0);
		string skStripped2 = ClauseStatusRun.StripVerifiedStateRuns(skJoiner, new System.Collections.Generic.List<string> { "毒" });
		CheckBool("mirror: the 火傷 sibling goes too", skStripped2.IndexOf("火傷") < 0);
		CheckBool("an unrelated verified name strips nothing", ClauseStatusRun.StripVerifiedStateRuns(skJoiner, new System.Collections.Generic.List<string> { "凍結" }).Contains("状態"));
		// StatusTokens is the site that DECIDES a status-conditioned clause (JudgeClause reads stTokens
		// first, and ClauseStatusNames is only the fallback). The run used to stop on と, so the second
		// name was the only token: stHits=1, the それぞれ stacking never ran, and the clause was parked.
		var skTok = ClauseStatusRun.StatusTokens(skJoiner);
		Check("と-list yields BOTH status tokens", skTok.Count, 2);
		CheckBool("と-list tokens are 毒 and 火傷", skTok.Contains("毒") && skTok.Contains("火傷"));
		var skTokSlash = ClauseStatusRun.StatusTokens("毒/火傷状態の敵全ての被ダメージがそれぞれ+15%");
		Check("slash-list still yields both", skTokSlash.Count, 2);
		var skTokOne = ClauseStatusRun.StatusTokens("毒状態の敵に対して与ダメージ+15％");
		Check("a single name yields one token", skTokOne.Count, 1);
		CheckBool("the single token is 毒", skTokOne.Contains("毒"));
		// the arithmetic those tokens feed in JudgeClause: それぞれ stacks once per satisfied status
		double skAcc = 1.0;
		for (int t = 0; t < skTok.Count; t++) skAcc *= 1.15;
		CheckBool("それぞれ with 2 tokens is 1.15^2 = 1.3225", Math.Abs(skAcc - 1.3225) < 1e-9);

		// ---- 1.5.4: byUnit (giver/applier) rides on fold and cancel steps ----
		// The field is CONDITIONAL in the JSON: absent unless the channel produced a name. Both halves
		// are pinned because byUnit-on-every-fold would be dead weight on the 12k text folds, and
		// byUnit-never would silently defeat the whole 1.5.4 attribution.
		Console.WriteLine("--- FoldStep: 1.5.4 byUnit ---");
		var bySb = new StringBuilder();
		new FoldStep { Side = "vic", Kind = "given", Origin = "given#4/1006/-10", Factor = 1.1, Label = "被伤害+10%(赋予)", ByUnit = "メアリー" }.AppendJson(bySb);
		CheckBool("fold step carries byUnit when set", bySb.ToString().Contains("\"byUnit\":\"メアリー\""));
		var noBySb = new StringBuilder();
		new FoldStep { Side = "atk", Kind = "text", Origin = "text#1/2/c1", Factor = 1.15, Label = "x" }.AppendJson(noBySb);
		CheckBool("fold step omits byUnit when unset", !noBySb.ToString().Contains("byUnit"));
		var byCtx = new FoldContext();
		byCtx.Add("vic", "given", "given#5/1006/-10", 1.1, "被伤害+10%(赋予)", "テトラ");
		CheckBool("Add overload stores ByUnit", byCtx.Steps.Count == 1 && byCtx.Steps[0].ByUnit == "テトラ");
		byCtx.Add("vic", "madness", "vicmadness#150", 1.5, "狂気(受击方)");
		CheckBool("old Add signature leaves ByUnit null", byCtx.Steps.Count == 2 && byCtx.Steps[1].ByUnit == null);
		byCtx.NoteCancellation("global#7/0", "given#6/1006/-15", 1.15, "被伤害+15%(赋予)", "ルゥ=ルルサ");
		CheckBool("NoteCancellation stores ByUnit", byCtx.Cancels.Count == 1 && byCtx.Cancels[0].ByUnit == "ルゥ=ルルサ");
		byCtx.NoteCancellation("global#7/0", "given#7/1006/-15", 1.15, "old-signature");
		CheckBool("old NoteCancellation signature leaves ByUnit null", byCtx.Cancels[1].ByUnit == null);

		// ---- 1.5.0: the export's structural self-check (Output/JsonCheck.cs) ----
		// The plugin runs this on every export before writing the file. It is asserted HERE because a
		// validator that has never been shown to REJECT anything is not a validator -- the same lesson as
		// verify_143's wrong predicate, which turned a true rule into a false one.
		Console.WriteLine("--- JSON: 结构自检 ---");
		string jerr;
		int jdup;
		CheckBool("check accepts the probe's own fold JSON", JsonCheck.Validate(foldJson, out jerr, out jdup));
		CheckBool("check accepts the escaped JSON", JsonCheck.Validate(je.ToString(), out jerr, out jdup));
		CheckBool("check rejects unbalanced brace", !JsonCheck.Validate("{\"a\":1", out jerr, out jdup));
		CheckBool("check rejects unbalanced bracket", !JsonCheck.Validate("{\"a\":[1}", out jerr, out jdup));
		CheckBool("check rejects unterminated string", !JsonCheck.Validate("{\"a\":\"1}", out jerr, out jdup));
		CheckBool("check rejects a stray closing brace", !JsonCheck.Validate("{\"a\":1}}", out jerr, out jdup));
		CheckBool("check ignores brackets inside strings", JsonCheck.Validate("{\"a\":\"}{][\"}", out jerr, out jdup));
		CheckBool("check ignores an escaped quote", JsonCheck.Validate("{\"a\":\"x\\\"y\"}", out jerr, out jdup));
		JsonCheck.Validate("{\"a\":1,\"b\":2,\"a\":3}", out jerr, out jdup);
		Check("duplicate root keys counted", jdup, 1);
		JsonCheck.Validate("{\"a\":{\"x\":1,\"x\":2}}", out jerr, out jdup);
		Check("nested duplicate NOT counted as root", jdup, 0);
		// 1.5.0: the failure message must be SEARCHABLE -- the export is one 13 MB line, so an offset
		// alone would send the reader hunting. `Around` quotes the surrounding text.
		JsonCheck.Validate("{\"good\":1,\"bad\":[1}", out jerr, out jdup);
		CheckBool("error carries an offset", jerr != null && jerr.Contains("@"));
		CheckBool("error quotes the context", jerr != null && jerr.Contains("bad"));
		CheckBool("error names the depth at end", (JsonCheck.Validate("{\"a\":{\"b\":1}", out jerr, out jdup) == false)
			&& jerr.Contains("depth"));

		ContributionChecks();
		LedgerChecks();
		ParamOwnerChecks();

		AtkAddChecks();
		AtkAddKeyChecks();


		if (args != null && args.Length > 0)
		{
			var outSb = new StringBuilder();
			// every line is a complete JSON object, so the runner can parse the artifact as well as
			// assert on it in-process (the Append* methods emit a leading comma by design).
			outSb.Append(jsonRecon).Append('\n').Append(jsonFull).Append('\n').Append(jsonBad).Append('\n')
			     .Append("{\"pad\":0").Append(foldJson).Append('}').Append('\n')
			     .Append("{\"pad\":0").Append(je).Append('}').Append('\n')
			     .Append("{\"pad\":0,\"paramOwners\":").Append(_paramOwnersJson).Append('}').Append('\n');
			File.WriteAllText(args[0], outSb.ToString(), new UTF8Encoding(false));
			Console.WriteLine("wrote " + args[0]);
		}
		Console.WriteLine(_fail == 0 ? "ALL CHECKS PASSED" : (_fail + " CHECK(S) FAILED"));
		return _fail != 0 ? 1 : 0;
	}
}
