using System.Text;
using UnityEngine;

namespace DpsMeter;

/// <summary>
/// Talent reads: modifiers that exist only as talent data (no ability text).
/// 1005 DamageUp / 1006 DamageCut are folded into the arithmetic; the rest are dumped by the
/// [ABIL] diagnostic so a modifier the text does not describe can still be identified.
/// </summary>
public static partial class CompositionProbe
{
	internal static void AppendTalents(StringBuilder sb, AbilityData data, string label)
	{
		try
		{
			if (data == null) return;
			var talents = data.m_talents;
			if (talents == null) return;
			for (int k = 0; k < talents.Length; k++)
			{
				var t = talents[k];
				if (t == null) continue;
				var td = t.TalentData;
				if (td == null) continue;
				int ty = 0;
				try { ty = (int)td.TalentType; } catch { }
				if (sb.Length > 0) sb.Append(", ");
				sb.Append(label).Append(':').Append(ty);
				// Spell out what the type id means for the "add this parameter" talents, so a hit line
				// showing e.g. "攻击力+4%、贯通率+2" can be traced in one step. Only VERIFIED ids are
				// named; anything else stays a bare number (no guessing).
				string tyName = TalentTypeName(ty);
				if (tyName.Length > 0) sb.Append('(').Append(tyName).Append(')');
				for (int pi = 0; pi < 3; pi++)
				{
					int p = 0;
					try { p = td.GetParam(pi); } catch { p = 0; }
					if (pi > 0 && p == 0) break;
					sb.Append('/').Append(p);
				}
			}
		}
		catch { }
	}

	/// <summary>
	/// Names for the talent/parameter type ids seen in `talents:` dumps. Every entry here is backed by an
	/// ability whose TEXT states the effect, so the mapping is evidence, not a guess:
	///   1  = 耐久        -- #6  text "耐久力+30%"                          -> 1/30
	///   6  = 攻击力      -- #1  text "攻撃力+30%"                          -> 6/30
	///   17 = 魔法防御    -- #19 text "攻撃した敵の魔法防御が5秒間5%低下"     -> 17/5/150
	///                       (150 units = 5 s at the measured 30 units/game second -- an independent
	///                        confirmation of that constant)
	///   26 = 攻击速度    -- #10030 text "攻撃速度+25%、貫通率+5%"           -> 26/25
	///   78 = 会心率      -- #5  text "会心率+15% 会心ダメージ率+15%"        -> 78/15
	///   80 = 会心伤害率  -- #5                                              -> 80/15
	///   86 = 贯通率      -- #10030 "貫通率+5%" and #84 "[賢導]トレイラ
	///                       text 敵撃破時、攻撃力+4%、貫通率+2%（最大50回）" -> 86/2  (and 6/4 for the attack)
	///                       both cross-checked against the [ABIL] 参数表 line, which names the same
	///                       targets (攻击力/贯通率) with the same values
	///   90 = 回避率      -- #85 text "編成時、前衛全ての回避率+5%"           -> 90/5
	///   400..424 = 状态异常耐性 -- #84 エイボンの指輪 text "状態異常耐性+15(スタン/石化/毒/火傷/凍結/暗闇/狂気/恐怖/即死)"
	///                       -> 400/404/408/414/416/418/420/422/424, one id per status
	/// Ids whose meaning is NOT certain are deliberately left unnamed (e.g. 8, seen as '#25 現在耐久の2%分の
	/// 値を攻撃力に加算する' -> 8/2 but also as '#84 -> 8/100', which could be a probability instead).
	/// (High ids are the action-style talents already used by the arithmetic: 1004 AddTalent, 1005 DamageUp,
	///  1006 DamageCut, 1011 Comeback, 1012 TokenSummon, 1060 DamageTakeOver, 1063 ExternalParam.)
	/// </summary>
	internal static string TalentTypeName(int ty)
	{
		switch (ty)
		{
			case 1: return "耐久";
			case 6: return "攻击力";
			case 17: return "魔法防御";
			case 26: return "攻击速度";
			case 78: return "会心率";
			case 80: return "会心伤害率";
			case 86: return "贯通率";
			case 90: return "回避率";
			case 400: case 404: case 408: case 414: case 416:
			case 418: case 420: case 422: case 424: return "状态异常耐性";
			case 1004: return "AddTalent";
			case 1005: return "DamageUp";
			case 1006: return "DamageCut";
			case 1011: return "Comeback";
			case 1012: return "TokenSummon";
			case 1060: return "DamageTakeOver";
			case 1063: return "ExternalParam";
			default: return "";
		}
	}

	/// <summary>
	/// Materialise an Il2Cpp IEnumerable&lt;Talent&gt; (C# foreach cannot consume the interop enumerable,
	/// and the interop enumerator exposes no MoveNext). The game builds these lists as List&lt;Talent&gt;,
	/// so a cast is enough; anything else simply yields nothing.
	/// </summary>
	private static System.Collections.Generic.List<Talent> TalentListOf(Il2CppSystem.Collections.Generic.IEnumerable<Talent> src)
	{
		var res = new System.Collections.Generic.List<Talent>();
		try
		{
			if (src == null) return res;
			var lst = src.TryCast<Il2CppSystem.Collections.Generic.List<Talent>>();
			if (lst == null) return res;
			for (int i = 0; i < lst.Count && i < 64; i++)
			{
				var t = lst[i];
				if (t != null) res.Add(t);
			}
		}
		catch { }
		return res;
	}

	/// <summary>Diagnostic: dump every active buff with its runtime class and, for talent-carrying buffs
	/// (BuffTalent), the talents inside. This is where a temporary damage cut handed out by the mode
	/// (weekly arena class buff) has to live -- it is nowhere in the unit's own ability data.</summary>
	internal static string BuffTalentDump(BattleObject bo)
	{
		var sb = new StringBuilder(120);
		try
		{
			var bl = bo.m_buffList;
			if (bl == null) return "空";
			for (int i = 0; i < bl.Count; i++)
			{
				var b = bl[i];
				if (b == null) continue;
				if (sb.Length > 0) sb.Append(" | ");
				string cls = "?";
				try { cls = b.GetType().Name; } catch { }
				int bv = 0;
				try { bv = b.BuffValue; } catch { }
				string stt = "";
				try { stt = b.StatusType.ToString(); } catch { }
				int rem = -1;
				try { rem = b.RemainingTime; } catch { }
				sb.Append('#').Append(i).Append(' ').Append(cls).Append(" st=").Append(stt).Append(" v=").Append(bv);
				if (rem >= 0) sb.Append(" rem=").Append(rem);
				try
				{
					var bt = b.TryCast<BuffTalent>();
					foreach (var t in TalentListOf(bt != null ? bt.m_talentList : null))
					{
						var td = (t != null) ? t.TalentData : null;
						if (td == null) continue;
						int ty = 0;
						try { ty = (int)td.TalentType; } catch { }
						sb.Append(" →talent:").Append(ty);
						for (int pi = 0; pi < 3; pi++)
						{
							int p = 0;
							try { p = td.GetParam(pi); } catch { }
							if (pi > 0 && p == 0) break;
							sb.Append('/').Append(p);
						}
					}
				}
				catch { }
			}
		}
		catch { }
		return sb.Length == 0 ? "空" : sb.ToString();
	}

	/// <summary>
	/// Per-hit snapshot of what the VICTIM is actually carrying: how many additional (granted) talents,
	/// a compact rendering of those talents, and how many buffs.
	///
	/// Why a per-hit read is required: `ExtraTalents` already reads `m_additionalTalents` for the [ABIL]
	/// dump, but that dump is written the FIRST time a unit is seen -- measured 2026-10-03, the boss read
	/// `m_additionalTalents=0 m_buffList=0` at t=0 s while the unexplained ×1.21 only starts about 10 s
	/// into the same battle. A start-of-battle snapshot therefore cannot see a talent that is GRANTED
	/// later (「ブロックしている敵の被ダメージ+10%」 is applied by `type=1004 AddTalent` to the enemy the
	/// carrier is blocking), which is exactly the hypothesis under test.
	///
	/// extraCount / buffCount are -1 when unreadable, so "empty" and "could not read" never look alike.
	/// </summary>
	internal static string VictimExtraSnapshot(BattleObject bo, out int extraCount, out int buffCount)
	{
		extraCount = -1;
		buffCount = -1;
		var sb = new StringBuilder(64);
		try
		{
			if (GameRef.IsNull(bo)) return "";
			var tl = bo.m_additionalTalents;
			if (tl != null)
			{
				extraCount = tl.Count;
				for (int i = 0; i < tl.Count && sb.Length < 72; i++)
				{
					var t = tl[i];
					if (t == null) continue;
					var td = t.TalentData;
					if (td == null) continue;
					int ty = 0;
					try { ty = (int)td.TalentType; } catch { }
					if (sb.Length > 0) sb.Append(',');
					sb.Append(ty);
					for (int pi = 0; pi < 2; pi++)
					{
						int p = 0;
						try { p = td.GetParam(pi); } catch { }
						sb.Append('/').Append(p);
					}
				}
			}
			var bl = bo.m_buffList;
			if (bl != null) buffCount = bl.Count;
		}
		catch { }
		return sb.ToString();
	}

	internal static string ExtraTalents(BattleObject bo)	{
		var sb = new StringBuilder(80);
		try
		{
			var tl = bo.m_additionalTalents;
			if (tl != null)
			{
				for (int i = 0; i < tl.Count; i++)
				{
					var t = tl[i];
					if (t == null) continue;
					var td = t.TalentData;
					if (td == null) continue;
					int ty = 0;
					try { ty = (int)td.TalentType; } catch { }
					if (sb.Length > 0) sb.Append(", ");
					sb.Append("add:").Append(ty);
					for (int pi = 0; pi < 3; pi++)
					{
						int p = 0;
						try { p = td.GetParam(pi); } catch { }
						if (pi > 0 && p == 0) break;
						sb.Append('/').Append(p);
					}
				}
			}
		}
		catch { }
		return sb.ToString();
	}

	/// <summary>
	/// The percentage -> multiplier convention for a granted talent, in ONE place so that the self-test
	/// verifies the code the battle actually runs.
	///
	/// `1006 被伤害-` carries the modifier as a NEGATIVE 被伤害 number: p=-10 means "this unit takes 10%
	/// more", so the factor is `1 - (-10)/100 = 1.10` -- the same convention TalentDamage uses, and the
	/// one that historically produced the constant ×0.700 for a ヘビーシールダー victim (p=+30 -> 0.70).
	/// `1005 与伤害+` is the plain positive form.
	///
	/// usable=false covers the values that are not damage multipliers (p=0 = no entry; a factor of 0 or
	/// below, e.g. 被伤害+100 = "immune"; a factor above 5, which no rule in the master tables produces).
	/// They are counted, never folded: silently folding one is how a model starts predicting 0 damage,
	/// which is exactly the residual=0.000 class measured in this very battle (11 hits).
	/// </summary>
	internal static double GivenFactor(int wantType, int v, out bool usable)
	{
		usable = false;
		if (v == 0) return 1.0;
		double f = (wantType == 1006) ? (1.0 - v / 100.0) : (1.0 + v / 100.0);
		if (f <= 0.0 || f > 5.0) return 1.0;
		usable = true;
		return f;
	}

	/// <summary>
	/// Exercised once per export and printed on the [GIVE] line: the factor convention checked against
	/// the master-data values that actually occur (刻印 id=26 p=-10, 母なる変異の飛沫 p=-15, ヘビーシールダー
	/// p=+30), plus the two refusals. A sign or /100 error would otherwise only show up as a KPI that
	/// "did not improve", which is not diagnosable.
	/// </summary>
	internal static string GivenFactorSelfTest()
	{
		var sb = new StringBuilder(96);
		// (label, wantType, param, expected)
		var cases = new object[,]
		{
			{ "1006/-10",  1006, -10,  1.100 }, { "1006/-15",  1006, -15, 1.150 },
			{ "1006/+30",  1006,  30,  0.700 }, { "1006/-10x2", 1006, -10, 1.210 },
			{ "1005/+15",  1005,  15,  1.150 },
		};
		int pass = 0, fail = 0;
		for (int i = 0; i < cases.GetLength(0); i++)
		{
			string label = (string)cases[i, 0];
			int ty = (int)cases[i, 1];
			int p = (int)cases[i, 2];
			double want = (double)cases[i, 3];
			bool ok;
			double got = GivenFactor(ty, p, out ok);
			if (label == "1006/-10x2") { got = got * got; }
			double diff = got - want;
			if (diff < 0) diff = -diff;
			if (ok || label == "1006/+30") { if (diff <= 0.0005) pass++; else { fail++; sb.Append("FAIL ").Append(label).Append('=').Append(got.ToString("F3")).Append(' '); } }
			else { fail++; sb.Append("FAIL ").Append(label).Append(" not usable "); }
		}
		// refusals: 被伤害+100 would zero the damage, 被伤害-600 would multiply by 7
		bool ok1, ok2;
		double r1 = GivenFactor(1006, 100, out ok1);
		double r2 = GivenFactor(1006, -600, out ok2);
		if (!ok1 && r1 == 1.0) pass++; else { fail++; sb.Append("FAIL refuse p=100 "); }
		if (!ok2 && r2 == 1.0) pass++; else { fail++; sb.Append("FAIL refuse p=-600 "); }
		return "pass=" + pass + "/" + (pass + fail) + (fail > 0 ? " " + sb.ToString().Trim() : "");
	}

	/// <summary>
	/// Exercised once per export and printed on the [GIVE] line, next to the factor self-test. This checks
	/// the CANCELLATION arithmetic on the exact primitive the battle path uses -- the failure it guards
	/// against is silent (a KPI that merely "did not improve"), and it already cost one build.
	///
	/// Case ① is the measured one: the boss carries all three statuses, so the global-rule path accounted
	/// for 母なる変異の飛沫 ×3 and 海魔の残滓 ×2 (i.e. it is responsible for 1.15), and the grant list holds
	/// those same copies plus the two 刻印 id=26 copies (1.10 ×2). The result must be 1.10² = 1.21, not
	/// 1.15^k × 1.21 -- however many stale 1.15 copies the grant list still holds.
	/// </summary>
	internal static string GivenCancelSelfTest()
	{
		int pass = 0, fail = 0;
		var sb = new StringBuilder(96);
		// (factors the rule path is responsible for, granted factors, expected product, expected skips)
		var cases = new object[,]
		{
			{ new double[] { 1.15 }, new double[] { 1.15, 1.15, 1.15, 1.15, 1.15, 1.10, 1.10 }, 1.21, 5 },
			{ new double[] { 1.15 }, new double[] { 1.15, 1.15 },                              1.0,  2 },
			{ new double[] { },      new double[] { 1.10, 1.10 },                              1.21, 0 },
			{ new double[] { 1.15 }, new double[] { 1.10, 1.10, 1.15, 1.15 },                  1.21, 2 },
			{ new double[] { },      new double[] { },                                         1.0,  0 },
		};
		// 1.5.0: the same five cases, now checked against what the cancellation CHANGED as well as what it
		// produced. Two properties are new and both are about the 1.15^n question:
		//   * every cancelled copy names the rule that absorbed it (`Cancels[i].ResponsibleOrigin`);
		//   * how many copies ONE responsible rule absorbed (`MaxAbsorbedByOne`) is visible. Case 0 is the
		//     measured shape -- one responsible 1.15 in the rule table against five stale 1.15 copies in
		//     the grant list -- and 5 is exactly the number that used to be invisible.
		//         case:                0     1    2     3      4
		var wantAbsorbed = new int[] { 5, 2, 0, 2, 0 };
		for (int ci = 0; ci < cases.GetLength(0); ci++)
		{
			double[] responsible = (double[])cases[ci, 0];
			double[] granted = (double[])cases[ci, 1];
			double want = (double)cases[ci, 2];
			int wantSkip = (int)cases[ci, 3];
			var ctx = new FoldContext();
			for (int i = 0; i < responsible.Length; i++)
				ctx.AddEnemy(responsible[i], "test#" + i, "测试规则" + i);
			double m = 1.0;
			int skipped = 0;
			int unnamed = 0;
			for (int i = 0; i < granted.Length; i++)
			{
				string gone = "g#" + i;
				if (GivenCancelsGlobal)
				{
					string r;
					if (ctx.TryConsumeModeled(granted[i], out r))
					{
						skipped++;
						ctx.NoteCancellation(r, gone, granted[i], "测试授予");
						if (string.IsNullOrEmpty(r)) unnamed++;
						continue;
					}
				}
				m *= granted[i];
			}
			double d = m - want;
			if (d < 0) d = -d;
			if (d <= 1e-6 && skipped == wantSkip && ctx.MaxAbsorbedByOne == wantAbsorbed[ci]
				&& ctx.Cancels.Count == wantSkip && unnamed == 0) pass++;
			else
			{
				fail++;
				sb.Append("FAIL#").Append(ci)
				  .Append('=').Append(m.ToString("F4")).Append("/s").Append(skipped)
				  .Append("/a").Append(ctx.MaxAbsorbedByOne).Append('/').Append(wantAbsorbed[ci])
				  .Append("/n").Append(unnamed).Append(' ');
			}
		}
		return "pass=" + pass + "/" + (pass + fail) + (fail > 0 ? " " + sb.ToString().Trim() : "");
	}

	/// <summary>
	/// Talents GRANTED to this unit by another unit, read from `m_giveTalentData`
	/// (`List&lt;BattleObject.GiveTalentData&gt;`, entries `{give, original, talent, ownerAction, isDeleted}`).
	///
	/// This is the channel the cross-unit rules travel through, and it is the only readable record of
	/// *whether* such a rule is active right now:
	///
	///   刻印 id=26 「ブロックしている敵の被ダメージ+10%（前衛のみ）」
	///     i=0 type=1004 AddTalent   timing=Passive  p=[1,0,0]
	///     i=1 type=1006 被伤害-      timing=Block    p=[-10,0,0] cond=GiveTalent(1)
	///
	/// The carrier does not damage itself: it hands the `1006 p=-10` to the enemy it blocks. That is why
	/// the rule never showed up in the victim's own ability list (comp3 said 未检出) and never in
	/// `m_buffList` as a BuffTalent -- and why the attacker-side text scan, which folded it into the
	/// CARRIER's own hits, was wrong (measured 53/53 hits at residual 0.826 = 1/1.21).
	///
	/// MEASURED SHAPE OF THE EFFECT (battle_411001_...125554, 5490 hits): the factor is per-INSTANT, not
	/// per-hit -- at t=7.37 ten hits were all ×1.00 while at t=12.4 ten hits were all ×1.21 -- and the
	/// windows are IDENTICAL for all attackers (ON 10.7-19.3, 22.9-31.4, 39.2-42.2, 45.8-48.8,
	/// 52.8-58.4, 67.4-79.1, 83.0-86.0, 89.5-96.8, 101.3-104.2, 107.8-113.4 s). A party-wide, timed,
	/// victim-side state is exactly what a granted-talent entry with a lifetime looks like, and 1.1² is
	/// exactly what two copies of id=26 on one front-row unit produce.
	///
	/// `give` is exported as a histogram rather than guessed at: the decompiled interop exposes only the
	/// field, not the logic, so "is it the active flag or the direction flag?" is settled by data. Either
	/// reading makes the VICTIM's list the correct one to read, so no guess is needed to use it.
	///
	/// count = number of live (non-deleted) entries, applied = how many contributed a factor, both -1
	/// when unreadable, so "none" and "could not read" never look alike.
	///
	/// applyToChain=false (1.7.9) is the General/GivenTalent-OFF path: the entries are still READ and
	/// counted, and their text is still returned, but nothing is multiplied, counted as applied, or
	/// registered in `ctx`. The caller decides ONCE and hands the same decision down, so the exported
	/// fold list and the exported chain product cannot disagree in either setting.
	/// </summary>
	/// <summary>1.7.9: the ONE expression that decides whether a granted 被伤害 entry participates in the
	/// damage chain. It is deliberately pure and separate from the Il2Cpp read so it can be exercised
	/// offline (recon_probe), and so the two halves of the rule -- multiplying <c>vicMod</c> in the caller
	/// and registering the fold step in the callee -- are visibly driven by the same value. Before 1.7.9
	/// only the caller evaluated the switch, while the callee registered the step unconditionally: with
	/// General/GivenTalent=false the export carried a fold whose factor its takenMult did not contain.
	/// enabled = General/GivenTalent as read for this hit; gv = the factor the read produced.
	/// </summary>
	internal static bool GivenFoldApplies(bool enabled, double gv) => enabled && gv != 1.0;

	internal static double GivenTalentDamage(BattleObject bo, int wantType, out int count, out int applied, out string text, FoldContext ctx = null, bool measure = true, bool applyToChain = true)
	{
		count = -1;
		applied = 0;
		text = null;
		double mult = 1.0;
		var sb = new StringBuilder(64);
		try
		{
			if (GameRef.IsNull(bo)) return 1.0;
			var list = bo.m_giveTalentData;
			GivenReads += measure ? 1 : 0;
			if (list == null) return 1.0;
			count = 0;
			// A hit's worth of entries is small (the rule is per-carrier and a party has a handful of
			// carriers); the cap is only a guard against an unbounded list, and it is REPORTED.
			int n = list.Count;
			if (n > 64) { if (measure) GivenOverflow += (n - 64); n = 64; }
			for (int i = 0; i < n; i++)
			{
				try
				{
					var gd = list[i];
					if (gd == null) continue;
					bool del = false;
					try { del = gd.isDeleted; } catch { if (measure) GivenErrors++; }
					if (del) continue;
					bool gv = false;
					try { gv = gd.give; } catch { if (measure) GivenErrors++; }
					count++;
					if (measure) { if (gv) GivenActive++; else GivenPassive++; }
					var t = gd.talent;
					if (t == null) { try { t = gd.original; } catch { } }
					if (t == null) continue;
					var td = t.TalentData;
					if (td == null) continue;
					int ty = 0;
					try { ty = (int)td.TalentType; } catch { if (measure) GivenErrors++; }
					int v = 0;
					try { v = td.GetParam(0); } catch { if (measure) GivenErrors++; }
					// Keyed by type AND param (1.3.7): for a resistance modifier the type alone does not
					// say how much (`409 毒耐性-` is meaningless without the -30), and the param is what
					// lets the export be checked against the giver's master text.
					string tkey = ty + "/" + v;
					if (measure)
					{
						if (_givenTypes.TryGetValue(tkey, out int tc)) _givenTypes[tkey] = tc + 1;
						else if (_givenTypes.Count < 16) _givenTypes[tkey] = 1;
						else _givenTypesOther++;
					}
					if (ty != wantType) continue;
					bool usable;
					double f = GivenFactor(wantType, v, out usable);
					// A granted 被伤害 100 would mean "immune" and a 与伤害 -100 would mean "heals the
					// enemy"; neither is a damage multiplier we can honour, and honouring them silently is
					// how a model ends up predicting 0 damage. Count them instead of folding them.
					if (!usable) { if (measure && v != 0) GivenUnusable++; continue; }
					// 1.3.6/1.3.8: a granted modifier that the composition's own cross-unit rule path is
					// responsible for must NOT be added again. Measured 2026-10-03 on battle_...132600:
					// without this, 39.1% of all hits landed on residual 1/1.15^5 and another 24.8% on
					// 1/1.15^4 -- the `GiveTalent(1..3)` copies of 母なる変異の飛沫 and 海魔の残滓 counted
					// twice, once from their text and once from the grant. The 刻印 id=26 grant has no
					// text counterpart (its clause is parked as DeferredGrant), so it survives this step --
					// which is exactly the ×1.10² the model was missing.
					// 1.5.0 (A5): the responsibility set now lives in the per-hit FoldContext and carries
					// the responsible rule's ORIGIN and its absorption count. Pre-1.5.0 it was a static
					// Dictionary&lt;double,int&gt; whose stored counts were never read (verified at all 7 use
					// sites), and which any other caller of ApplyGlobalDebuffs silently overwrote --
					// including the diagnostics path, which recomputes the chain. The DECISION itself is
					// unchanged: value-only and unbounded, which is a measurement, not an oversight.
					string grantedOrigin = "given#" + i + "/" + ty + "/" + v;
					string grantedLabel = wantType == 1006
						? (v < 0 ? "被伤害+" + (-v) + "%(赋予)" : "被伤害-" + v + "%(赋予)")
						: (v < 0 ? "与伤害-" + (-v) + "%(赋予)" : "与伤害+" + v + "%(赋予)");
					// 1.5.5 (贡献归因 A, 第 2 方案): 字段读取这条路已判死 —— 1.5.4 实测 gd.ownerAction 对每个条目都抛
					// 异常(5 场、38,736-62,486 次、resolved=0),所以授予者改从**施加侧**拿:GiveApplierProbe 在授予类
					// 动作的 ActExecute 里记录 (owner, guest),这里按**目标名**查表(bo = 挂着这条授予的单位)。
					// 命中/未命中分开计数:「钩子没触发」和「触发了但表里没有」不能长得一样。
					string byUnit = null;
					try
					{
						byUnit = GiveApplierProbe.LastGiver(Aggregator.NameOf(bo));
						if (measure) { if (byUnit != null) GiverResolved++; else GiverNull++; }
					}
					catch { if (measure) GiverErrors++; }
					if (GivenCancelsGlobal && ctx != null)
					{
						string responsible;
						// 1.5.0: the DECISION is unchanged -- value-only and unbounded, which is a
						// measurement, not an oversight (see FoldedFactor's doc: the multiset variant was
						// tried in 1.3.8 and over-counted by 1.15^2..1.15^3 on ~1,700 hits). What is new
						// is that the absorbing rule's origin and the absorbed count are recorded, so
						// "did this rule absorb copies belonging to a different rule?" becomes answerable
						// from one battle instead of by guesswork across many.
						if (ctx.TryConsumeModeled(f, out responsible))
						{
							if (measure) GivenCancelled++;
							ctx.NoteCancellation(responsible, grantedOrigin, f, grantedLabel, byUnit);
							continue;
						}
					}
					// 1.7.9: ONE decision for both halves of the rule. Before this, the caller's General/GivenTalent
					// switch suppressed only `vicMod *= gv` here, while this function still registered a
					// kind="given" fold step and incremented both applied counters. With the switch OFF an export
					// therefore carried a fold whose factor the exported takenMult does not contain
					// (prod(calc.fold) != dealtMult*takenMult), and vicGiveApplied claimed entries the theory had
					// discarded. The switch exists so a regression can be isolated without a rebuild; it must not
					// be able to manufacture that contradiction.
					if (applyToChain)
					{
						mult *= f;
						applied++;
						if (measure) GivenApplied++;
						if (ctx != null)
							ctx.Add("vic", "given", grantedOrigin, f, grantedLabel, byUnit);
					}
					if (sb.Length > 0) sb.Append('、');
					if (wantType == 1006)
						sb.Append(v < 0 ? "被伤害+" : "被伤害-").Append(v < 0 ? -v : v).Append("%(赋予)");
					else
						sb.Append(v < 0 ? "与伤害-" : "与伤害+").Append(v < 0 ? -v : v).Append("%(赋予)");
				}
				catch { if (measure) GivenErrors++; }
			}
			if (sb.Length > 0) text = sb.ToString();
		}
		catch { if (measure) GivenErrors++; }
		return mult;
	}

	// ---- self-report: every read either succeeds or is counted, never swallowed ----
	/// <summary>Reads of `m_giveTalentData` attempted.</summary>
	internal static int GivenReads;
	/// <summary>HITS whose victim carried at least one live granted-talent entry (1.7.9 doc fix: this is
	/// incremented once per hit by the caller, NOT once per entry -- the per-entry count is the callee's
	/// `count` out parameter, exported per hit as calc.vicGive). It used to say "entries", which invited
	/// exactly the unit confusion that produced the giveApplied defect below.</summary>
	internal static int GivenCount;
	/// <summary>Entries whose field read failed.</summary>
	internal static int GivenErrors;
	/// <summary>ENTRIES that contributed a factored modifier (incremented per entry, and only when
	/// applyToChain is true). 1.7.9: the caller used to increment this a second time per HIT, so the
	/// exported giveApplied (rosterAudit.giveApplied) was entries+hits and matched no quantity in the file. MEASURED on
	/// all 29 corpus exports (2026-10-04): giveApplied == (kind="given" fold steps) + (hits carrying one)
	/// held exactly, 28/28 non-zero files (the 29th, 700817, is 0/0/0). Per-hit applications now have their own counter.</summary>
	internal static int GivenApplied;
	/// <summary>HITS on which a granted modifier actually folded (1.7.9). Kept separate from
	/// <see cref="GivenApplied"/> so the two units can never be added together again, and exported as
	/// `giveFoldHits` so the entry-level and hit-level facts are both readable from one file.</summary>
	internal static int GivenFoldHits;
	/// <summary>Entries skipped because the value cannot be a damage multiplier (|factor| out of range).</summary>
	internal static int GivenUnusable;
	/// <summary>Entries beyond the per-hit read cap.</summary>
	internal static int GivenOverflow;
	/// <summary>Entries with `give` true / false -- exported so the field's meaning is settled by data.</summary>
	internal static int GivenActive;
	internal static int GivenPassive;
	/// <summary>Attacker-side clauses parked on the granted-talent channel (see JudgeClause).</summary>
	internal static int GivenClauseDeferred;

	/// <summary>1.3.6: granted entries dropped because this hit's global-rule path already counted them.</summary>
	internal static int GivenCancelled;

	/// <summary>1.5.5 (贡献归因 A): giver resolution from the application map on surviving granted
	/// entries of the wanted type: resolved to a display name / map had no entry / the lookup threw.
	/// resolved+null+errors must equal givenApplied+givenCancelled -- every surviving entry resolves
	/// the giver exactly once, so an empty map shows up as a number (giverNull).</summary>
	internal static int GiverResolved;
	internal static int GiverNull;
	internal static int GiverErrors;

	/// <summary>
	/// 1.3.6: whether to cancel granted modifiers against the factors the global-rule path already applied.
	/// On by default -- without it the same rule is counted twice (see GivenTalentDamage). Turned off only
	/// to reproduce the 1.3.5 behaviour for a measurement.
	/// </summary>
	internal static bool GivenCancelsGlobal = true;

	/// <summary>TalentType/param -> how many granted entries carried it. This is what makes the first
	/// battle decisive even when nothing is folded: it says WHAT was in the list, not just that it was
	/// non-empty (e.g. "1006/-10:2" = the two granted 被伤害 modifiers 刻印 id=26 hands out,
	/// "409/-30" = a granted 毒耐性-30).</summary>
	private static readonly System.Collections.Generic.Dictionary<string, int> _givenTypes
		= new System.Collections.Generic.Dictionary<string, int>();

	/// <summary>Granted entries whose (type/param) pair fell outside the histogram cap.</summary>
	internal static int _givenTypesOther;

	/// <summary>Compact rendering of the granted talent types, e.g. "1006/-15:2,409/-30:1".</summary>
	internal static string GivenTypeHistogram()
	{
		var sb = new StringBuilder(64);
		var keys = new System.Collections.Generic.List<string>(_givenTypes.Keys);
		keys.Sort();
		for (int i = 0; i < keys.Count; i++)
		{
			if (sb.Length > 0) sb.Append(',');
			sb.Append(keys[i]).Append(':').Append(_givenTypes[keys[i]]);
		}
		if (_givenTypesOther > 0) sb.Append(",…+").Append(_givenTypesOther);
		return sb.Length == 0 ? "空" : sb.ToString();
	}

	/// <summary>
	/// 1.5.0 (架构审视 B2): the STRUCTURED twin of <see cref="GivenTypeHistogram"/> -- an array of
	/// `{"type":1006,"param":-15,"n":2}` instead of the "1006/-15:2" string.
	///
	/// WHY. The histogram is what identified the granted resistance modifiers (409 毒耐性- / 411 毒伤害耐性-)
	/// and it was ONLY available as a delimited string, so every analysis script that needed it re-derived
	/// the split. The old key stays for existing scripts; this one is added alongside rather than replacing
	/// it, because 768 historical exports have only the string form.
	///
	/// `capped` reports entries that fell outside the 16-slot display cap, so a truncated histogram can
	/// never be read as a complete one.
	/// </summary>
	internal static void AppendGivenTypesJson(StringBuilder sb)
	{
		sb.Append('[');
		var keys = new System.Collections.Generic.List<string>(_givenTypes.Keys);
		keys.Sort();
		for (int i = 0; i < keys.Count; i++)
		{
			string k = keys[i];
			if (i > 0) sb.Append(',');
			int slash = k.IndexOf('/');
			string ty = slash > 0 ? k.Substring(0, slash) : k;
			string pa = slash > 0 ? k.Substring(slash + 1) : "";
			sb.Append("{\"type\":").Append(ty)
			  .Append(",\"param\":").Append(string.IsNullOrEmpty(pa) ? "null" : pa)
			  .Append(",\"n\":").Append(_givenTypes[k]).Append('}');
		}
		sb.Append(']');
	}

	/// <summary>Zero the per-battle granted-talent counters. Called from Aggregator.StartSession.
	/// Also clears the 1.3.7 resistance first-sight gate and sample (same per-battle lifetime).</summary>
	internal static void ResetGivenCounters()
	{
		GivenReads = 0;
		GivenCount = 0;
		GivenErrors = 0;
		GivenApplied = 0;
		GivenFoldHits = 0;
		GivenUnusable = 0;
		GivenOverflow = 0;
		GivenActive = 0;
		GivenPassive = 0;
		GivenClauseDeferred = 0;
		GivenCancelled = 0;
		GiverResolved = 0;
		GiverNull = 0;
		GiverErrors = 0;
		_givenTypes.Clear();
		_givenTypesOther = 0;
		ResistUnits = 0;
		ResistReads = 0;
		ResistErrors = 0;
		ResistNull = 0;
		ResistSamples = 0;
		SubParamReads = 0;
		SubParamErrors = 0;
		ParamReads = 0;
		ParamErrors = 0;
		_resistDumped.Clear();
		_resistSample.Length = 0;
		_resistLast.Clear();
		_resistLastTick.Clear();
		// 1.5.0 (B2): the structured twins are per-battle too, or the export would carry last battle's
		// numbers next to this battle's strings.
		_resistFirstRaw.Clear();
		_resistLastRaw.Clear();
		_subParamRaw.Clear();
		_paramRaw.Clear();
		_subParamLast.Clear();
		_paramLast.Clear();
		UnitStateProbe.Reset();
	}

	/// <summary>
	/// Numeric 与/被ダメージ補正 read from an ability list's TALENTS instead of its text.
	///
	/// 与/被ダメージ補正 are implemented as talents (`TalentDefine.Type.DamageUp = 1005`,
	/// `DamageCut = 1006`) whose first parameter is the percentage, and the クラス特性 abilities
	/// ("ヘビーシールダー" etc.) carry such a talent with an EMPTY description -- so a text scan can
	/// never see them. That is exactly the constant ×0.700 seen on every ヘビーシールダー victim.
	///
	/// To avoid counting an ability twice, an ability whose text mentions ダメージ anywhere is left to
	/// the text scan (it owns the conditions); only text-less abilities are taken from the talents.
	/// </summary>
	private static double TalentDamage(BattleObject bo, bool victimSide, out string text, FoldContext ctx = null)
	{
		double mult = 1.0;
		text = null;
		try
		{
			if (GameRef.IsNull(bo)) return 1.0;
			var list = bo.m_ability;
			if (list == null) return 1.0;
			int want = victimSide ? 1006 : 1005;
			int entry = 0;
			try { entry = bo.EntryId; } catch { }
			var sb = new StringBuilder(48);
			for (int i = 0; i < list.Count; i++)
			{
				try
				{
					var ab = list[i];
					if (ab == null) continue;
					var data = ab.Data;
					if (data == null) continue;
					int id = 0;
					string raw = "";
					try { raw = GetAbilityText(bo, ab, i, entry, out id, out raw); } catch { }
					if (!string.IsNullOrEmpty(raw) && raw.IndexOf("ダメージ", System.StringComparison.Ordinal) >= 0)
						continue;   // the text scan handles this ability (conditions included)
					var talents = data.m_talents;
					if (talents == null) continue;
					for (int k = 0; k < talents.Length; k++)
					{
						var t = talents[k];
						if (t == null) continue;
						var td = t.TalentData;
						if (td == null) continue;
						int ty = 0;
						try { ty = (int)td.TalentType; } catch { }
						if (ty != want) continue;
						int v = 0;
						try { v = td.GetParam(0); } catch { }
						if (v == 0) continue;
						if (want == 1006)
						{
							mult *= (1.0 - v / 100.0);
							if (ctx != null)
								ctx.Add(victimSide ? "vic" : "atk", "talent",
									"talent#" + i + "/" + id + "/t" + k + "/" + ty, 1.0 - v / 100.0,
									"被ダメージ-" + v + "%");
							if (sb.Length > 0) sb.Append('、');
							sb.Append("被ダメージ-").Append(v).Append('%');
						}
						else
						{
							mult *= (1.0 + v / 100.0);
							if (ctx != null)
								ctx.Add(victimSide ? "vic" : "atk", "talent",
									"talent#" + i + "/" + id + "/t" + k + "/" + ty, 1.0 + v / 100.0,
									"与ダメージ+" + v + "%");
							if (sb.Length > 0) sb.Append('、');
							sb.Append("与ダメージ+").Append(v).Append('%');
						}
						if (Plugin.CfgAbilityDump != null && Plugin.CfgAbilityDump.Value)
							RuntimeLog.Write("[ABIL]   talent " + (victimSide ? "被ダメージ" : "与ダメージ")
								+ " " + v + "%  ability#" + id);
					}
				}
				catch { }
			}
			// active buffs may carry talents too (BuffTalent): a temporary damage cut handed out by the
			// mode (weekly arena class buff) lives here rather than in the unit's ability data, and
			// because it is an ACTIVE buff the value is correct for this instant by construction.
			try
			{
				var bl = bo.m_buffList;
				if (bl != null)
					for (int i = 0; i < bl.Count; i++)
					{
						try
						{
							var b = bl[i];
							if (b == null) continue;
							var bt = b.TryCast<BuffTalent>();
							int tk = 0;
							foreach (var t in TalentListOf(bt != null ? bt.m_talentList : null))
							{
								// index taken BEFORE any `continue`, so the origin id is stable per entry
								int tki = tk++;
								var td = (t != null) ? t.TalentData : null;
								if (td == null) continue;
								int ty = 0;
								try { ty = (int)td.TalentType; } catch { }
								if (ty != want) continue;
								int v = 0;
								try { v = td.GetParam(0); } catch { }
								if (v == 0) continue;
								if (want == 1006)
								{
									mult *= (1.0 - v / 100.0);
									if (ctx != null)
										ctx.Add(victimSide ? "vic" : "atk", "talent",
											"talent#buff" + i + "/t" + tki + "/" + ty, 1.0 - v / 100.0,
											"被ダメージ-" + v + "%(增益)");
									if (sb.Length > 0) sb.Append('、');
									sb.Append("被ダメージ-").Append(v).Append("%(增益)");
								}
								else
								{
									mult *= (1.0 + v / 100.0);
									if (ctx != null)
										ctx.Add(victimSide ? "vic" : "atk", "talent",
											"talent#buff" + i + "/t" + tki + "/" + ty, 1.0 + v / 100.0,
											"与ダメージ+" + v + "%(增益)");
									if (sb.Length > 0) sb.Append('、');
									sb.Append("与ダメージ+").Append(v).Append("%(增益)");
								}
								if (Plugin.CfgAbilityDump != null && Plugin.CfgAbilityDump.Value)
									RuntimeLog.Write("[ABIL]   talent(buff) " + (victimSide ? "被ダメージ" : "与ダメージ")
										+ " " + v + "%");
							}
						}
						catch { }
					}
			}
			catch { }
			if (mult < 0.0) mult = 0.0;
			if (sb.Length > 0) text = sb.ToString() + "(天赋数值)";
		}
		catch { }
		return mult;
	}
}
