using System.Text;
using UnityEngine;

namespace DpsMeter;

/// <summary>
/// The displayed damage chain: the arithmetic, the attacker row, the victim row, the status row
/// and the resolved buff/parameter list.
/// </summary>
public static partial class CompositionProbe
{
	/// <summary>
	/// Four-part damage chain:
	///   line1 = the arithmetic: power, defense subtraction, IDENTIFIED multipliers, theory, applied, residual
	///   line2 = ATTACKER side: attack type, crit stats, attribute relation, buffs, dealt-damage modifiers
	///   line3 = VICTIM side: defense used and taken-damage modifiers
	///
	/// The identified multipliers (attribute x2, parsed 与ダメージ/被ダメージ clauses) are folded into the
	/// formula, so the trailing residual multiplier only has to cover what could NOT be identified
	/// (crit -- not readable -- plus any modifier the game does not expose as text).
	/// </summary>
	/// <param name="absorbed">
	/// 本次计算对应的伤害里没有进入耐久的部分 (BattleObject.Damage 的入参 − 返回值)。
	/// 游戏自己的 CharacterStatistics.TakenDamage 按入参累计,所以我们记录的"实际伤害"会比游戏少这一段;
	/// 不把它单独说出来,它就会被误读成"剩余倍率 0.001"这种不存在的游戏机制。
	/// </param>
	public static void BuildChainParts(DamageCalculater calc, BattleObject blocker, int finalDamage, out string line1, out string line2, out string line3, out string line4, int absorbed = 0)
	{
		BuildChainParts(calc, blocker, finalDamage, out line1, out line2, out line3, out line4, absorbed, out _);
	}

	/// <summary>
	/// Same chain, plus every number it mentions as a struct (1.3.0). The text form is for humans and is
	/// unchanged; this overload exists so the export can carry fields instead of Chinese, which is what
	/// makes the reconciliation KPI (Output/CalcReconcile.cs) computable at all.
	/// </summary>
	public static void BuildChainParts(DamageCalculater calc, BattleObject blocker, int finalDamage, out string line1, out string line2, out string line3, out string line4, int absorbed, out CalcBreakdown brk)
	{
		line1 = "";
		line2 = "";
		line3 = "";
		line4 = "";
		brk = default(CalcBreakdown);
		// -1, not the default 0: for this field "0 entries" IS a result ("the victim's grant list was read
		// and was empty") and must never be what an early failure reports. Same reason every other
		// per-hit measurement in this struct is -1 when unreadable.
		brk.GivenTalents = -1;
		try
		{
			BattleObject atk = null;
			try { atk = calc.Attacker; } catch { }
			// the status conditions of THIS hit are judged against the target's state when the calc
			// started, not against the state after this hit applied its own debuff
			UseStatusSnapshot(calc);
			// 攻击力: prefer the ATTACK-START snapshot (same invariant as the HP/status conditions).
			// 计算威力 is fixed when the calc is built, but BattleObject.Power read at damage time is live;
			// for a summon the token can be re-created/rebuffed in between and then read a base value, so
			// the printed attack and the power disagreed by 5x on some hits (measured: identical 计算威力
			// 98571 printed with 攻击力 32857 -> 系数 3.00 on 26 hits, and with 攻击力 6487 -> 系数 15.20
			// on the two that landed while the token was mid-rebuild).
			int atkPower = 0;
			bool atkSnapshotted = false;
			if (!GameRef.IsNull(atk))
			{
				atkPower = SnapshotPowerFor(atk);
				if (atkPower > 0) atkSnapshotted = true;
				else { try { atkPower = atk.Power; } catch { } }
			}
			int liveAtkPower = LivePowerOf(atk);
			int pow = Power(calc);
			// The calc also knows its OWNER (m_owner): for a summon's hit the power and the printed attack
			// can come from different objects, so the owner's value is shown when it disagrees.
			int ownerPower = SnapshotOwnerPower();
			if (ownerPower == 0)
			{
				try
				{
					BattleObject own = calc.m_owner;
					if (!GameRef.IsNull(own) && !GameRef.Same(own, atk)) ownerPower = LivePowerOf(own);
				}
				catch { }
			}
			int effectId = 0;
			try { effectId = calc.m_effectId; } catch { }
			int hitType = -1;
			try { hitType = (int)calc.m_hitType; } catch { }
			_curHitType = hitType;

			// ---- defence term (penetration is readable, so it is applied here; a defence debuff
			//      on the victim is not readable and therefore stays in the residual) ----
			int use = 0;
			string defLabel = "";
			int rawDef = 0;
			if (!GameRef.IsNull(blocker))
			{
				try { rawDef = blocker.Defense; } catch { }
				try
				{
					if (hitType == 1 || hitType == 5) { use = rawDef; defLabel = "物防"; }
					else if (hitType == 2) { use = blocker.MagicDefense; defLabel = "魔防"; }
				}
				catch { }
			}
			int pen = 0;
			try { if (!GameRef.IsNull(atk)) pen = atk.PenetrationRate; } catch { }
			if (pen < 0) pen = 0;
			if (pen > 100) pen = 100;
			int effDef = (int)((long)use * (100 - pen) / 100);
			bool minRule = (hitType != 3) && (use > 0) && (pow > 0) && ((long)use * 100 >= (long)pow * 95);
			int baseDmg = pow;
			if (hitType == 3) baseDmg = pow;                       // 贯通属性:完全无视防御
			else if (minRule) baseDmg = (int)((long)pow * 5 / 100); // 保底伤害 = 计算威力 × 5%
			else if (use > 0) { baseDmg = pow - effDef; if (baseDmg < 0) baseDmg = 0; }

			// ---- identified multipliers ----
			double attrMult = AttrMultiplier(atk, blocker);
			double atkMod = 1.0, vicMod = 1.0;
			string atkModText = "", vicModText = "";
			// 1.5.0 (A1/A5): the per-hit provenance sink. Every fold site below reports WHICH rule it
			// multiplied and by how much, so the two running doubles stop being the only record of the
			// chain. Named `fctx` because `fold` is already a local switch name in two places here.
			var fctx = new FoldContext();
			try { atkModText = AbilityScan(atk, 6, hitType, victimSide: false, blocker, out atkMod, fctx); } catch { }
			try { vicModText = AbilityScan(blocker, 6, hitType, victimSide: true, atk, out vicMod, fctx); } catch { }
			// numeric 与/被ダメージ補正 taken straight from the abilities' TALENTS: this is the only way
			// to see the ones whose ability text is EMPTY (the クラス特性 rows -- ヘビーシールダー's
			// 被ダメージ-30% for instance), which is what used to leave a constant ×0.700 residual.
			try
			{
				string tTxt;
				double t = TalentDamage(atk, false, out tTxt, fctx);
				if (t != 1.0) { atkMod *= t; atkModText = Join(atkModText, tTxt); }
			}
			catch { }
			try
			{
				string tTxt;
				double t = TalentDamage(blocker, true, out tTxt, fctx);
				if (t != 1.0) { vicMod *= t; vicModText = Join(vicModText, tTxt); }
			}
			catch { }
			// battle-wide debuffs ("毒/火傷状態の敵全ての被ダメージ+15%") apply to ANY attacker's hit.
			// MUST run before the granted-talent scan below (1.3.6): it records which 敌方受伤 factors
			// this hit already accounted for, and the scan cancels the granted copies of exactly those.
			try
			{
				RegisterGlobalDebuffs(atk);
				RegisterGlobalDebuffs(blocker);
				string gTxt;
				double g = ApplyGlobalDebuffs(atk, blocker, out gTxt, fctx);
				if (g != 1.0) { atkMod *= g; atkModText = Join(atkModText, gTxt); }
			}
			catch { }
			// Talents GRANTED to the victim by other units. This is the channel 刻印 id=26
			// 「ブロックしている敵の被ダメージ+10%（前衛のみ）」 travels through: the carrier hands
			// `type=1006 被伤害- p=[-10]` to the enemy it blocks, so the modifier is a property of the
			// VICTIM and applies to every attacker -- which is exactly the ×1.21 window measured across
			// all four attackers at once. Folded here (victim side) and NOT on the attacker side; see
			// JudgeClause for the 53/53 over-prediction that proved the attacker-side fold wrong.
			// Entries whose factor the global-rule path just accounted for are cancelled, not folded
			// (1.3.6): GiveTalent(1..3) copies of 母なる変異の飛沫 / 海魔の残滓 are the SAME modifiers
			// their text clauses already described, and counting both put 63.9% of a battle on 1/1.15^k.
			int gvCount = -1, gvApplied = 0;
			string gvTxtStored = null;
			// 1.7.9: the switch is read ONCE here, before the call, and handed to the callee. It used to be
			// read after the call and used only for `vicMod *= gv`, so with General/GivenTalent=false the
			// callee registered a kind="given" fold step and counted it as applied anyway: the export then
			// carried prod(calc.fold) != dealtMult*takenMult plus a vicGiveApplied naming entries the theory
			// had discarded. One decision, both halves, is what keeps them in agreement in BOTH settings.
			bool givenFold = Plugin.CfgGivenTalent == null || Plugin.CfgGivenTalent.Value;
			try
			{
				string gvTxt;
				double gv = GivenTalentDamage(blocker, 1006, out gvCount, out gvApplied, out gvTxt, fctx, true, givenFold);
				gvTxtStored = gvTxt;
				if (gvCount > 0) GivenCount++;
				// `GivenTalent` off = measure only: the entry and its counts are still exported, the
				// theory simply leaves it in the residual. It exists so a regression can be isolated
				// without a rebuild; the fold itself is proven by the master definition of 刻印 id=26.
				if (GivenFoldApplies(givenFold, gv)) { vicMod *= gv; vicModText = Join(vicModText, gvTxt); GivenFoldHits++; }
			}
			catch { GivenErrors++; }
			brk.GivenTalents = gvCount;
			brk.GivenApplied = gvApplied;
			brk.GivenFoldOn = givenFold;
			brk.GivenTalentText = gvTxtStored;
			// ---- 狂気 on the VICTIM: INCOMING multiplier (1.5.3) ----
			// The 1.4.1 fold below is OUTGOING -- the attacker DEALS more. 狂気 ALSO raises the damage its
			// holder TAKES (被ダメージ+50%), and the composition never modelled that side, so every hit whose
			// victim carried 狂気 was short by exactly that factor. Measured on 183213/191159: two clean
			// テトラ cells with a baseline residual of 1.0 go 0.9996 -> 1.4954 and 0.9965 -> 1.5011, and the
			// pooled controlled factor is 1.4886. The factor is the measured 1.5 and NOT ratio/100:
			// MadnessAllyBuffRatio is the ALLY buff (250 on an enemy) and is NOT this factor, so reading it
			// here would over-count. Own switch so a regression is isolatable without a rebuild, and the flag
			// is exported either way (`victimMadnessOn`) so the rule stays falsifiable from data.
			//
			// The status text is read HERE, once, and reused by comp4 below: the fold and the exported
			// `victimStatuses` list must be the same read, or a falsification would have to guess which of two
			// reads drove the multiplier.
			string vicStatusRaw = null;
			bool vicMad = false;
			try
			{
				vicStatusRaw = StatusBrief(blocker);
				string[] vlist = SplitStatusList(vicStatusRaw);
				if (vlist != null)
				{
					for (int i = 0; i < vlist.Length; i++)
						if (vlist[i] == "狂気") { vicMad = true; break; }
				}
				if (vicMad && (Plugin.CfgMadnessVictim == null || Plugin.CfgMadnessVictim.Value))
				{
					const double VicMadFactor = 1.5;
					vicMod *= VicMadFactor;
					string vmadTxt = "狂気(受击方) 被伤害×1.50";
					vicModText = Join(vicModText, vmadTxt);
					// 1.5.4 (贡献归因 C): stamp the most recent 狂気 applier; null when never seen (the fold
					// itself never depends on the lookup).
					fctx.Add("vic", "madness", "vicmadness#150", VicMadFactor, vmadTxt,
						StatusApplierProbe.LastMadnessApplier(Aggregator.NameOf(blocker)));
				}
			}
			catch { }
			brk.VictimMadnessOn = vicMad;
			// ---- 狂気 (Madness) の与ダメージ倍率 (1.4.1) ----
			// The composition used to read 狂気 ONLY as one of the 18 resistance slots, never as a
			// damage modifier. Measured on battle_411001_20261003_150140: 924/924 of メアリー's records
			// satisfy `residual == 1.15^n × (狂気 ? 2.5 : 1.0)` with zero exceptions, and
			// `CharaStatus.MadnessAllyBuffRatio` is 250 while 狂気 is on / 100 otherwise. Attacker side:
			// the residual being explained is damage the unit DEALS.
			// Kept behind its own switch (like the granted-talent fold) so a regression can be isolated
			// without a rebuild; ratio/flag are exported either way so the rule stays falsifiable.
			int madRatio = 0;
			bool madOn = false;
			try
			{
				double mad = UnitStateProbe.MadnessMultiplier(atk, out madRatio, out madOn);
				bool fold = (Plugin.CfgMadness == null || Plugin.CfgMadness.Value);
				if (fold && mad != 1.0)
				{
					atkMod *= mad;
					string madTxt = "狂気 与伤害×" + mad.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
						+ "(狂気比" + madRatio + ")";
					atkModText = Join(atkModText, madTxt);
					// 1.5.0 (A1): origin carries the RAW ratio, so the fold stays falsifiable from the
					// per-hit data instead of only from the `madnessRatio` scalar.
					fctx.Add("atk", "madness", "madness#" + madRatio, mad, madTxt,
						StatusApplierProbe.LastMadnessApplier(Aggregator.NameOf(atk)));
				}
			}
			catch { }
			brk.MadnessRatio = madRatio;
			brk.MadnessOn = madOn;
			// 1.5.0 (A1/A5): hand the provenance to the consumer. Left null when empty so "no chain" and
			// "chain with no factors" stay distinguishable, and so the export can omit the key.
			if (fctx.Steps.Count > 0) brk.Fold = fctx.Steps;
			if (fctx.Cancels.Count > 0) brk.FoldCancels = fctx.Cancels;
			brk.FoldResponsible = fctx.EnemyFactorCount;
			brk.FoldMaxAbsorbed = fctx.MaxAbsorbedByOne;
			brk.FoldDropped = fctx.StepsDropped + fctx.CancelsDropped;
			double known = attrMult * atkMod * vicMod;
			long theory = (long)(baseDmg * known);
			double residual = (theory > 0) ? ((double)finalDamage / theory) : 0.0;

			// ---- line 1: the arithmetic ----
			var sb1 = new StringBuilder(220);
			if (effectId != 0) sb1.Append("技能#").Append(effectId).Append(" · ");
			bool mixedAttack = ownerPower > 0 && atkPower > 0
				&& (ownerPower > atkPower * 1.1 || atkPower > ownerPower * 1.1);
			// did the attacker's attack change between cast and damage application?
			bool atkDrift = atkSnapshotted && liveAtkPower > 0 && atkPower > 0
				&& (liveAtkPower > atkPower * 1.1 || atkPower > liveAtkPower * 1.1);

			// ---- 攻击力 × 系数 = 计算威力 ---------------------------------------------------------
			// 计算威力 is the game's own number and is fixed when the calc is built; DamageCalculater does
			// NOT expose the attack it used, so 攻击力 has to be read from the game and the two can come
			// from different moments -- measured on skill #10024: 44 hits read 攻击力 32435 with
			// 计算威力 32435 (系数 1.00, correct) while 2 hits read 攻击力 6065 with the SAME 计算威力
			// 32435 (nonsense 系数 5.35); the identical damage 7299 appeared under both readings, which
			// proves 32435 is the real basis.
			//
			// So the attack is chosen from CANDIDATES instead of trusting one read:
			//   构造前 (PowerProbe, read in the prefix of DamageCalculater's attack ctor)
			//   攻击开始快照 (DamageCalculater.Action)   |   结算时 (live read)   |   归属者
			// A skill's damage rate is a constant, so once the skill's own hits have established it, the
			// candidate whose ratio matches that rate wins; if none matches, the value implied by the rate
			// is shown. A genuinely multi-stage skill has each of its ratios established, so its hits are
			// displayed as they are -- only a rare outlier ever gets replaced.
			long calcKey = 0;
			try { calcKey = calc.Pointer.ToInt64(); } catch { }
			int ctorAtk = PowerProbe.CtorPowerOf(calc);
			if (ctorAtk == 0) ctorAtk = PowerProbe.RecentPowerOfAttacker(atk);   // pointer-free fallback
			double ratio = (atkPower > 0 && pow > 0) ? (double)pow / atkPower : 0.0;
			double dominant = (ratio > 0.0) ? SkillRateDominant(effectId) : 0.0;
			bool ratioEstablished = effectId != 0 && SkillRateCount(effectId, ratio) >= SkillRateMinSamples;
			int shownAtk = atkPower;
			string shownSrc = "";
			int mainAtk = (dominant > 0.0 && pow > 0) ? (int)System.Math.Round(pow / dominant) : 0;
			int samePowAtk = SamePowerAttack(pow);
			bool samePowOk = (samePowAtk <= 0) || Within(atkPower, samePowAtk, CompositionTolerancePolicy.TightRate);
			if (!samePowOk)
			{
				// The same 计算威力 was read with a different attack on other hits -> that other value is
				// the basis this power was built from (works even for hits without a skill id).
				shownAtk = samePowAtk;
				shownSrc = "同威力主档";
			}
			else if (dominant > 0.0 && pow > 0 && !MatchesRate(atkPower, pow, dominant, CompositionTolerancePolicy.TightRate))
			{
				// 1) a candidate that reproduces the skill's rate EXACTLY (<=0.5%) is the value the power
				//    was built from -> show it as read;
				if (MatchesRate(ctorAtk, pow, dominant, CompositionTolerancePolicy.TightRate)) { shownAtk = ctorAtk; shownSrc = "构造前"; }
				else if (MatchesRate(liveAtkPower, pow, dominant, CompositionTolerancePolicy.TightRate)) { shownAtk = liveAtkPower; shownSrc = "结算时"; }
				else if (MatchesRate(ownerPower, pow, dominant, CompositionTolerancePolicy.TightRate)) { shownAtk = ownerPower; shownSrc = "归属者"; }
				// 2) a candidate that is merely CLOSE (<=2%) is a stale read of the right object ->
				//    display the exact value implied by the rate and mention the read in the note;
				else if (MatchesRate(ctorAtk, pow, dominant, CompositionTolerancePolicy.LooseRate)) { shownAtk = ctorAtk; shownSrc = "构造前"; }
				else if (MatchesRate(atkPower, pow, dominant, CompositionTolerancePolicy.LooseRate) || MatchesRate(liveAtkPower, pow, dominant, CompositionTolerancePolicy.LooseRate)
					|| MatchesRate(ownerPower, pow, dominant, CompositionTolerancePolicy.LooseRate))
				{ shownAtk = mainAtk; shownSrc = "按主档校正"; }
				// 3) nothing explains the power -> derive the attack from the rate itself
				else { shownAtk = mainAtk; shownSrc = "按主档反推"; }
			}
			if (shownAtk > 0) sb1.Append("攻击力 ").Append(shownAtk);
			else sb1.Append("攻击力 未知");
			if (shownSrc.Length > 0) sb1.Append('(').Append(shownSrc).Append(')');
			else if (atkSnapshotted && atkDrift) sb1.Append("(攻击开始快照;结算时 ").Append(liveAtkPower).Append(')');
			if (mixedAttack) sb1.Append("(归属者 ").Append(ownerPower).Append(')');

			string coefText = (shownAtk > 0 && pow > 0) ? ((double)pow / shownAtk).ToString("F2") : "?";
			string coefNote = "";
			int impliedAtk = (dominant > 0.0 && pow > 0) ? (int)System.Math.Round(pow / dominant) : 0;
			bool ratioClean = false;
			if (ratio > 0.0)
			{
				double scaled = ratio * 10.0;
				ratioClean = System.Math.Abs(scaled - System.Math.Round(scaled)) <= CompositionTolerancePolicy.LinearWindow;
			}
			if (shownSrc == "同威力主档")
			{
				double gap = (atkPower > 0) ? System.Math.Abs(samePowAtk - atkPower) / (double)atkPower : 0.0;
				coefNote = $"【同一计算威力 {pow} 的其它命中读到的攻击力是 {samePowAtk}:本次读到的 {atkPower}"
					+ $" 与其相差 {gap * 100:F1}%,按多数口径显示】";
			}
			else if (shownSrc.Length > 0)
			{
				double gap = (atkPower > 0 && shownAtk > 0)
					? System.Math.Abs(shownAtk - atkPower) / (double)atkPower : 0.0;
				coefNote = $"【攻击力采用「{shownSrc}」{shownAtk}:读取到的 {atkPower} × 系数 {ratio:F2} 与"
					+ $"「技能#{effectId}」主档 ×{dominant:F2} 不符(相差 {gap * 100:F1}%)】";
			}
			else if (ratio > 0.0)
			{
				if (atkDrift) coefNote = "【结算时攻击力已变化:系数按攻击开始快照计算】";
				else if (mixedAttack) coefNote = "【攻击力与计算威力口径不一致:见「归属者」】";
				else if (!ratioClean)
					coefNote = (dominant > 0.0)
						? "【系数非整数档:本技能命中数不足 3 次,暂无法定档】"
						: "【系数非整数档:该次计算未带技能号(DamageCalculater.m_effectId=0),暂无法定档】";
			}
			// one [POWER] line per interesting hit: the readings disagree, or the ratio is not a whole tenth
			// (a normal, self-consistent hit stays silent).
			try
			{
				bool disagree = (ctorAtk > 0 && (ctorAtk != atkPower || ctorAtk != liveAtkPower))
					|| (liveAtkPower > 0 && atkPower > 0 && liveAtkPower != atkPower)
					|| (samePowAtk > 0 && !samePowOk);
				if (disagree || !ratioClean)
					PowerProbe.Log(calcKey, effectId, dominant, pow, ctorAtk, atkPower, liveAtkPower, ownerPower,
						impliedAtk, shownSrc.Length > 0 ? shownSrc : "读取值",
						(shownSrc.Length > 0 ? ("读取值" + atkPower + (samePowAtk > 0 ? (" 同威力主档" + samePowAtk) : "")) : ""));
			}
			catch { }
			if (pow > 0 && atkPower > 0) NotePowerAttack(pow, atkPower);
			if (effectId != 0 && ratio > 0.0) NoteSkillRatio(effectId, ratio);
			sb1.Append(" × 系数 ").Append(coefText).Append("(推算)").Append(coefNote);
			sb1.Append(" = 计算威力 ").Append(pow);
			if (pow > 0)
			{
				sb1.Append(" · ");
				if (hitType == 3)
				{
					sb1.Append("贯通属性:无视防御 → 减算后 ").Append(baseDmg);
				}
				else if (use > 0)
				{
					sb1.Append("防御减算 ").Append(pow).Append(" − ").Append(defLabel).Append(' ').Append(use);
					if (pen > 0) sb1.Append("×(1−贯通").Append(pen).Append("%)");
					sb1.Append(" = ").Append(effDef > 0 ? (pow - effDef > 0 ? pow - effDef : 0) : pow);
					if (minRule) sb1.Append(" → 触发保底伤害 ").Append(baseDmg).Append("(5%,不会心)");
				}
				else
				{
					sb1.Append("无适用防御 → 减算后 ").Append(baseDmg);
				}
			}
			if (known != 1.0 || baseDmg > 0)
			{
				sb1.Append(" · 已识别倍率 ×").Append(known.ToString("F3"))
					.Append("(属性×").Append(attrMult.ToString("F2"))
					.Append(" · 与伤害×").Append(atkMod.ToString("F3"))
					.Append(" · 被伤害×").Append(vicMod.ToString("F3")).Append(')');
				sb1.Append(" · 理论 ").Append(baseDmg).Append(" × ").Append(known.ToString("F3")).Append(" = ").Append(theory);
			}
			sb1.Append(" · 实际伤害 ").Append(finalDamage);
			if (absorbed > 0)
			{
				// A hit whose 计算威力 theory is far above the applied damage is normally NOT a wrong power:
				// part of the damage was absorbed / nullified before it reached 耐久. Say so explicitly,
				// otherwise the row reads as a 1/1863 "residual" that no game rule explains.
				sb1.Append(" · 被吸收/无效化 ").Append(absorbed)
					.Append("(游戏口径 ").Append(finalDamage + absorbed)
					.Append(" = 入耐久 ").Append(finalDamage).Append(" + 被吸收 ").Append(absorbed)
					.Append(";游戏自身统计按 ").Append(finalDamage + absorbed).Append(" 计入)");
			}
			else if (theory > 0)
			{
				sb1.Append(" · 剩余倍率 ×").Append(residual.ToString("F3")).Append("(会心/未识别部分)");
			}
			line1 = sb1.ToString();
			// Structured mirror of line1 (+ the crit stats from line2). Filled as soon as the arithmetic
			// exists, so a later failure while building lines 2-4 cannot leave a half-filled breakdown
			// claiming to describe a chain that was never printed. Pair is left for the caller.
			if (line1.Length > 0)
			{
				brk.Valid = true;
				brk.EffectId = effectId;
				brk.HitType = hitType;
				brk.AttackPower = shownAtk;
				brk.Power = pow;
				brk.Ratio = ratio;
				brk.DefenseUsed = use;
				brk.DefenseKind = defLabel;
				brk.Penetration = pen;
				brk.EffectiveDefense = effDef;
				brk.MinRule = minRule;
				brk.BaseDamage = baseDmg;
				brk.AttrMult = attrMult;
				brk.DealtMult = atkMod;
				brk.TakenMult = vicMod;
				brk.KnownMult = known;
				brk.Theory = theory;
				brk.Applied = finalDamage;
				brk.Absorbed = absorbed;
				brk.Residual = residual;
			}

			// ---- line 2: attacker side ----
			var sb2 = new StringBuilder(190);
			try { sb2.Append("攻击属性 ").Append(HitTypeName(hitType)); } catch { }
			try
			{
				if (!GameRef.IsNull(atk))
				{
					int cr = 0, cd = 0;
					try { cr = atk.CriticalRate; } catch { }
					try { cd = atk.CriticalDamageRate; } catch { }
					brk.CritRate = cr;
					brk.CritDamageRate = cd;
					if (cr > 0 || cd > 0)
					{
						sb2.Append(" · 攻击方会心 率").Append(cr).Append("/伤害率").Append(cd);
						if (cd > 100) sb2.Append("(×").Append((cd / 100.0).ToString("F2")).Append(')');
					}
				}
			}
			catch { }
			// The victim's blocking state: MEASURED, not modelled -- see CalcBreakdown.VictimIsBlocking.
			// Three separate readings because the member names are ambiguous (IsBlocking /
			// IsUnitBlocking / BlockCount on BattleObject); the next battle's data will say which one
			// separates "this hit carried the extra ×1.21" from "this hit did not".
			if (!GameRef.IsNull(blocker))
			{
				brk.VictimIsBlocking = ReadBlockFlag(blocker, 0);
				brk.VictimIsUnitBlocking = ReadBlockFlag(blocker, 1);
				brk.VictimBlockCount = -1;
				try { brk.VictimBlockCount = blocker.BlockCount; BlockReads++; }
				catch { BlockErrors++; }
				// And what the victim is actually carrying RIGHT NOW (granted talents + buffs). Same
				// measurement-not-model reasoning; the count and the text both matter because a granted
				// 被伤害+10% twice is exactly ×1.21.
				int xTal, xBuff;
				brk.VictimExtraTalentText = VictimExtraSnapshot(blocker, out xTal, out xBuff);
				brk.VictimExtraTalents = xTal;
				brk.VictimBuffs = xBuff;
			}
			try
			{
				string ct = CritText(blocker);
				if (!string.IsNullOrEmpty(ct)) sb2.Append(" · ").Append(ct);
			}
			catch { }
			string aAttr = AttrName(atk);
			string tAttr = AttrName(blocker);
			if (!string.IsNullOrEmpty(aAttr) && !string.IsNullOrEmpty(tAttr))
			{
				if (sb2.Length > 0) sb2.Append(" · ");
				sb2.Append("属性 ").Append(aAttr).Append("→").Append(tAttr);
				string rel = AttrRelation(atk, blocker);
				if (!string.IsNullOrEmpty(rel)) sb2.Append(' ').Append(rel);
			}
			try
			{
				string fl = CalcFlags(calc);
				if (!string.IsNullOrEmpty(fl)) sb2.Append(" · ").Append(fl);
			}
			catch { }
			try
			{
				int src = (int)calc.m_damageSource;
				string s = SrcName(src);
				if (!string.IsNullOrEmpty(s) && s != "未知" && s != "直接攻击")
					sb2.Append(" · 来源 ").Append(s);
			}
			catch { }
			var atkAddItems = new System.Collections.Generic.List<AtkAddItem>(8);
			string buffs = BuildBuffText(atk, 40, false, atkAddItems);
			// 1.7.4 (阶段 G): the per-hit attribution inputs, computed HERE because this is the only place that
			// has the attacker object, its name in the same key space the owners come from, and AttackPower.
			brk.AtkAdd = new System.Collections.Generic.List<AtkAddRow>(4);
			// 1.7.8 (P1-A): the self verdict is made on the ACTOR KEY first; the name is only the
			// counted fallback, because a teammate can share the attacker's DisplayName.
			try { AtkAddFold.Compute(brk.AttackPower, ActorKeyOf(atk), Aggregator.NameOf(atk), atkAddItems, brk.AtkAdd); } catch { }
			if (!string.IsNullOrEmpty(buffs)) sb2.Append(" · 增益:").Append(buffs);
			if (!string.IsNullOrEmpty(atkModText)) sb2.Append(" · 与伤害补正:").Append(atkModText);
			line2 = sb2.ToString();

			// ---- line 3: victim side ----
			var sb3 = new StringBuilder(120);
			if (!GameRef.IsNull(blocker))
			{
				try
				{
					int d = 0, md = 0;
					try { d = blocker.Defense; } catch { }
					try { md = blocker.MagicDefense; } catch { }
					sb3.Append("受击方 物防").Append(d).Append("/魔防").Append(md);
					if (defLabel.Length > 0) sb3.Append("(本次使用").Append(defLabel).Append(')');
				}
				catch { }
			}
			if (!string.IsNullOrEmpty(vicModText))
			{
				if (sb3.Length > 0) sb3.Append(" · ");
				sb3.Append("被伤害补正:").Append(vicModText);
			}
			else
			{
				if (sb3.Length > 0) sb3.Append(" · ");
				sb3.Append("被伤害补正:未检出(无相关能力文本)");
			}
			// status abnormalities of the attacker stay off the attacker line too (own row below)
			line3 = sb3.ToString();

			// ---- line 4: status abnormalities (own row, deliberately not merged into line 2/3) ----
			try
			{
				var sb4 = new StringBuilder(80);
				string vst = (vicStatusRaw != null) ? vicStatusRaw : StatusBrief(blocker);
				sb4.Append("受击方状态:").Append(string.IsNullOrEmpty(vst) ? "无" : vst);
				string ast2 = StatusBrief(atk);
				if (!string.IsNullOrEmpty(ast2)) sb4.Append("   自身状态:").Append(ast2);
				line4 = sb4.ToString();
				// 1.5.0 (B3): keep the SAME statuses as a list. Splitting the string that was just
				// written makes "the list and the sentence agree" structural rather than a promise, and
				// lets StatusDeltaProbe stop parsing comp4 (which it used to do, prefix and all).
				brk.VictimStatuses = SplitStatusList(vst);
				brk.AttackerStatuses = SplitStatusList(ast2);
			}
			catch { }
		}
		catch { brk = default(CalcBreakdown); }
		finally { UseStatusSnapshot(null); }   // the attack-start snapshot belongs to this hit only
	}

	/// <summary>Resolved buff parameters of the attacker, e.g. "攻击力+500%、会心率+100、耐久+800%".
	/// Values come from BuffParamData.ParamData (the value the game actually applies), not from the
	/// raw parameter key. Rate-type values are printed with a % suffix.
	/// The list is no longer cut at 6 entries: the caller wraps long lines into several rows, so
	/// showing everything is better than an ellipsis that looked like a width problem.</summary>
	public static string BuildBuffText(BattleObject atk)
	{
		return BuildBuffText(atk, 40, false);
	}

	/// <summary>Verbose variant used by the diagnostic log (adds the declared value for cross-checking).</summary>
	public static string BuildBuffTextVerbose(BattleObject atk)
	{
		return BuildBuffText(atk, 8, true);
	}

	private static string BuildBuffText(BattleObject atk, int max, bool declared,
		System.Collections.Generic.List<AtkAddItem> atkItems = null)
	{
		try
		{
			if (GameRef.IsNull(atk)) return "";
			var sb = new StringBuilder(140);
			int n = 0;
			// preferred source: parameter dictionary (entry values are the applied ones)
			try
			{
				var pd = atk.BuffParamData;
				var dict = (pd != null) ? pd.mNowBuffParamDataDictionary : null;
				if (dict != null)
				{
					foreach (var kv in dict)
					{
						if (n >= max) { sb.Append('…'); break; }
						string tgtKey = null;
						try { tgtKey = kv.Key.BuffTarget.ToString(); } catch { }
						// only entries that can change THIS hit's damage; 耐久/回复率/攻速/移速/抗性 etc.
						// are noise here and used to bury the relevant lines behind an ellipsis
						if (!IsDamageRelevantTarget(tgtKey)) continue;
						// 1.7.4 (阶段 G): the STRUCTURED twin of one 攻击力 entry, captured from the same walk that
						// produces the text -- so presence belongs to THIS hit and the owners are read at the same
						// instant as AttackPower. The owner family is read here because no other per-hit channel
						// carries it (the battle-scoped union records WHERE something was seen, not WHEN it applied).
						if (atkItems != null && tgtKey == "Power")
						{
							AtkAddItem ai = ReadAtkItem(kv.Key, kv.Value);
							if (ai != null) atkItems.Add(ai);
						}
						string piece = FormatEntry(kv.Key, kv.Value, declared);
						if (string.IsNullOrEmpty(piece)) continue;
						if (n > 0) sb.Append('、');
						sb.Append(piece);
						n++;
					}
				}
			}
			catch { }
			// fallback: raw buff list
			if (n == 0)
			{
				try
				{
					var bl = atk.BuffList;
					if (bl != null)
					{
						foreach (var b in bl)
						{
							if (n >= max) { sb.Append('…'); break; }
							string nm = "";
							bool plus = true;
							try
							{
								var bd = b.BuffData;
								if (bd != null) { nm = StatusName(bd.CharacterStatusType.ToString()); plus = bd.IsPlus; }
							}
							catch { }
							if (string.IsNullOrEmpty(nm)) nm = "状态";
							if (n > 0) sb.Append('、');
							sb.Append(nm).Append(plus ? "+" : "-").Append(b.BuffValue);
							n++;
						}
					}
				}
				catch { }
			}
			return n == 0 ? "" : sb.ToString();
		}
		catch { return ""; }
	}

	/// <summary>
	/// Diagnostic: the applied damage-parameter dictionary, printed with BOTH sides of each entry --
	/// the key (BuffParam: target/type/sign/value) and the value (ParamData: target/type/applied value/
	/// reference base). The "增益:" text on a hit line is built from the value side, so this is the dump
	/// that explains a surprising entry: e.g. "会心率+99999" is the game's own sentinel value
	/// (ParamData.Target=CriticalRate, Param=99999) and can be traced here and in the ability's
	/// `talents:` line (a talent 78/99999, where parameter 78 = 会心率 -- see ability #5 会心率+15%:78/15).
	/// </summary>
	/// <summary>
	/// Render the live applied-param dictionary as text, and (1.5.0) OPTIONALLY the same entries as
	/// numbers in <paramref name="rows"/> -- deliberately ONE walk, because the dictionary is live and
	/// churning, so two walks could describe two different instants. Each row is
	/// `[i, buffTargetName, buffTypeName, buffValue, valueTargetName, valueTypeName, valueParam, refText]`.
	/// </summary>
	internal static string BuffParamTableDump(BattleObject bo, System.Collections.Generic.List<string[]> rows = null,
		System.Collections.Generic.List<string[]> owners = null)
	{
		var sb = new StringBuilder(200);
		try
		{
			if (GameRef.IsNull(bo)) return "空";
			var pd = bo.BuffParamData;
			var dict = (pd != null) ? pd.mNowBuffParamDataDictionary : null;
			if (dict == null) return "空";
			// 1.7.2 (阶段 G): ONE walk, two destinations. The TEXT/rows keep the historical 14-entry shape
			// byte for byte, while `owners` is filled up to ParamOwnerProbe.MaxEntriesPerRead. The old
			// `if (i >= 14) break` is exactly why the OWNER of an 攻击力 entry was unobservable -- the
			// entries past the cap were never read at all. Two walks of a live dictionary would describe
			// two different instants, so the owner is read HERE, and the drop is counted instead of silent.
			int i = 0;
			int ownerGetters = 0;
			int ownerSkipped = 0;
			bool ellipsis = false;
			foreach (var kv in dict)
			{
				bool visible = i < 14;
				if (!visible && !ellipsis) { sb.Append("…"); ellipsis = true; }
				if (visible && sb.Length > 0) sb.Append(" | ");
				string kt = "?";
				try { kt = kv.Key.BuffTarget.ToString(); } catch { }
				string kty = "?";
				try { kty = kv.Key.BuffType.ToString(); } catch { }
				int kvv = 0;
				try { kvv = kv.Key.BuffValue; } catch { }
				string ktName = TargetName(kt);
				if (visible)
					sb.Append('#').Append(i).Append(' ').Append(ktName).Append('/').Append(kty)
						.Append('/').Append(kvv);
				// the structured row is filled as we go, so `rows` and `sb` cannot disagree
				string[] row = null;
				if (rows != null && visible)
				{
					row = new string[8];
					row[0] = i.ToString();
					row[1] = ktName;
					row[2] = kty;
					row[3] = kvv.ToString();
				}
				// 1.7.2: the OWNER side. BuffParam.m_owner (key) and ParamData.Owner (value) are BOTH
				// recorded, because which of the two the game fills with the GIVER is an empirical
				// question -- the plugin has never read either before, so nothing here is assumed.
				// 1.7.2 (阶段 G): the three KEY integers above are read for EVERY entry, so completeness is
				// provable (entriesTotal == entryReads + entriesDropped); the two owner GETTERS are the
				// expensive part, so the 攻击力 target always gets them and every other target gets them
				// only while the per-read budget lasts. Never silent: skipped entries are counted.
				string[] orow = null;
				if (owners != null)
				{
					bool atkTarget = false;
					try { atkTarget = (int)kv.Key.BuffTarget == ParamOwnerProbe.AtkTarget; } catch { }
					if (atkTarget || ownerGetters < ParamOwnerProbe.MaxEntriesPerRead)
					{
						ownerGetters++;
						orow = new string[9];
						orow[0] = ktName;
						orow[1] = kty;
						orow[2] = kvv.ToString();
						try
						{
							var vv = kv.Value;
							if (vv != null) orow[7] = Aggregator.NameOf(vv.Owner);
						}
						catch { ParamOwnerProbe.NoteOwnerError(); }
						try { orow[8] = Aggregator.NameOf(kv.Key.m_owner); }
						catch { ParamOwnerProbe.NoteOwnerError(); }
					}
					else ownerSkipped++;
				}
				try
				{
					var v = kv.Value;
					if (v != null)
					{
						string vt = "";
						string vty = "";
						int vp = 0;
						string vref = "";
						try { vt = v.Target.ToString(); } catch { }
						try { vty = v.Type.ToString(); } catch { }
						try { vp = v.Param; } catch { }
						try
						{
							string rt = v.ReferenceType.ToString();
							if (!string.IsNullOrEmpty(rt) && rt != "None") vref = "/ref" + rt + v.ReferenceParam;
						}
						catch { }
						string vtName = TargetName(vt);
						if (visible)
							sb.Append(" → ").Append(vtName).Append('/').Append(vty)
								.Append("/").Append(vp).Append(vref);
						if (row != null)
						{
							row[4] = vtName;
							row[5] = vty;
							row[6] = vp.ToString();
							row[7] = vref;
						}
						if (orow != null)
						{
							orow[3] = vtName;
							orow[4] = vty;
							orow[5] = vp.ToString();
							orow[6] = vref;
						}
					}
				}
				catch { }
				if (row != null) rows.Add(row);
				if (orow != null) owners.Add(orow);
				i++;
			}
			if (owners != null) ParamOwnerProbe.NoteScan(i, ownerGetters, ownerSkipped);
			return sb.Length == 0 ? "空" : sb.ToString();
		}
		catch { return ""; }
	}

	/// <summary>
	/// Per-battle histogram of the derived coefficient per skill id (effectId -> ratio*100 -> count).
	///
	/// A skill's damage rate is a constant, so the majority ratio over its hits identifies it. That turns an
	/// otherwise ambiguous number into a checkable one: when a hit's own ratio disagrees with the skill's
	/// majority, either the hit is a different charge/level of the skill, or -- the case this was added for --
	/// its 计算威力 and displayed 攻击力 came from different moments/objects. Measured examples:
	///   * 计算威力 97305 = 32435 x 3.00, but the attack read at that moment was 32857 (a kill-stack had just
	///     raised it): ratio 2.96 instead of the skill's 3.00 -> implied attack 32435, gap 1.3%;
	///   * 计算威力 98571 = 32857 x 3.00 printed with attack 6487 while a summon was mid-rebuild:
	///     ratio 15.20 -> implied attack 32857, gap 407%.
	/// </summary>
	private static readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<int, int>> _skillRates =
		new System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<int, int>>();
	private const int SkillRateMaxSkills = 128;
	private const int SkillRateMinSamples = 3;

	/// <summary>Majority coefficient of a skill from this battle's other hits (0 = not enough data yet).</summary>
	private static double SkillRateDominant(int effectId)
	{
		if (effectId == 0) return 0.0;
		System.Collections.Generic.Dictionary<int, int> hist;
		if (!_skillRates.TryGetValue(effectId, out hist) || hist == null) return 0.0;
		int best = 0, bestN = 0;
		foreach (var kv in hist)
			if (kv.Value > bestN) { bestN = kv.Value; best = kv.Key; }
		if (bestN < SkillRateMinSamples) return 0.0;
		return best / 100.0;
	}

	/// <summary>
	/// Per-battle histogram of the attack value read for each EXACT 计算威力 (pow -> attack -> count).
	///
	/// This is the hook-independent way to identify the attack that built the power, and it also covers the
	/// large group of hits whose DamageCalculater carries no skill id (m_effectId = 0, shown as 技能#-),
	/// where a per-skill rate consensus is impossible. Rationale: the same skill always produces the same
	/// power for a given attack, so a wrong read (a different object's attack, or a stale value) shows up as
	/// a MINORITY attack for the same power. Measured on skill #10024: 计算威力 97305 was read with attack
	/// 32435 on 26 hits and with 6065 on 2 hits -- and the identical damage value 7299 appeared under both,
	/// proving 32435 is the basis. 计算威力 19461 likewise: 6487 (x3.00) on many hits, 32857 once.
	/// </summary>
	private static readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<int, int>> _powAttacks =
		new System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<int, int>>();
	private const int PowAttackMaxKeys = 512;
	private const int PowAttackMinSamples = 3;

	/// <summary>Attack value that most often accompanied this exact 计算威力 (0 = not enough data).</summary>
	private static int SamePowerAttack(int pow)
	{
		try
		{
			if (pow <= 0) return 0;
			System.Collections.Generic.Dictionary<int, int> hist;
			if (!_powAttacks.TryGetValue(pow, out hist) || hist == null) return 0;
			int best = 0, bestN = 0;
			foreach (var kv in hist)
				if (kv.Value > bestN) { bestN = kv.Value; best = kv.Key; }
			return (bestN >= PowAttackMinSamples) ? best : 0;
		}
		catch { return 0; }
	}

	private static void NotePowerAttack(int pow, int atk)
	{
		try
		{
			if (pow <= 0 || atk <= 0) return;
			System.Collections.Generic.Dictionary<int, int> hist;
			if (!_powAttacks.TryGetValue(pow, out hist) || hist == null)
			{
				if (_powAttacks.Count >= PowAttackMaxKeys) return;
				hist = new System.Collections.Generic.Dictionary<int, int>();
				_powAttacks[pow] = hist;
			}
			int c;
			hist.TryGetValue(atk, out c);
			hist[atk] = c + 1;
		}
		catch { }
	}

	/// <summary>Reads one of BattleObject's blocking flags. `which` selects the member because the game has
	/// three similarly named ones and only the data can tell which means "the enemy I am hitting is being
	/// blocked". Returns -1 when unreadable, so "false" and "could not read" never look alike.</summary>
	private static int ReadBlockFlag(BattleObject bo, int which)
	{
		try
		{
			bool v = (which == 0) ? bo.IsBlocking : bo.IsUnitBlocking;
			BlockReads++;
			return v ? 1 : 0;
		}
		catch { BlockErrors++; return -1; }
	}

	/// <summary>Successful / failed reads of the victim's blocking state (reported in rosterAudit).</summary>
	internal static int BlockReads;
	internal static int BlockErrors;

	/// <summary>True when both values are within the given relative tolerance.</summary>
	private static bool Within(int a, int b, double tolerance)
	{
		try
		{
			if (a <= 0 || b <= 0) return false;
			return System.Math.Abs(a - b) / (double)b <= tolerance;
		}
		catch { return false; }
	}

	/// <summary>Does 计算威力 ÷ candidate reproduce the rate the skill's own hits agree on?</summary>
	private static bool MatchesRate(int candidate, int pow, double rate)
	{
		return MatchesRate(candidate, pow, rate, CompositionTolerancePolicy.LooseRate);
	}

	private static bool MatchesRate(int candidate, int pow, double rate, double tolerance)
	{
		try
		{
			if (candidate <= 0 || pow <= 0 || rate <= 0.0) return false;
			double r = (double)pow / candidate;
			return System.Math.Abs(r - rate) / rate <= tolerance;
		}
		catch { return false; }
	}

	/// <summary>How many hits of this skill produced exactly this ratio (rounded to 0.01).</summary>
	private static int SkillRateCount(int effectId, double ratio)
	{
		try
		{
			if (effectId == 0 || ratio <= 0.0) return 0;
			System.Collections.Generic.Dictionary<int, int> hist;
			if (!_skillRates.TryGetValue(effectId, out hist) || hist == null) return 0;
			int c;
			return hist.TryGetValue((int)System.Math.Round(ratio * 100.0), out c) ? c : 0;
		}
		catch { return 0; }
	}

	private static void NoteSkillRatio(int effectId, double ratio)
	{
		try
		{
			if (effectId == 0 || ratio <= 0.0 || ratio > 100.0) return;
			int key = (int)System.Math.Round(ratio * 100.0);
			System.Collections.Generic.Dictionary<int, int> hist;
			if (!_skillRates.TryGetValue(effectId, out hist) || hist == null)
			{
				if (_skillRates.Count >= SkillRateMaxSkills) return;
				hist = new System.Collections.Generic.Dictionary<int, int>();
				_skillRates[effectId] = hist;
			}
			int c;
			hist.TryGetValue(key, out c);
			hist[key] = c + 1;
		}
		catch { }
	}

	internal static void ClearSkillRates()
	{
		try
		{
			_skillRates.Clear();
			_powAttacks.Clear();
		}
		catch { }
	}

	/// <summary>
	/// 1.7.8 (P1-A): the STABLE actor key of a BattleObject, or 0 when it has no actor row yet.
	///
	/// WHY HERE AND NOT IN Aggregator. The mapping already exists -- BattleSession.Actors is an
	/// object-identity dictionary and GetActor(bo, create:false) is its read-only side; it is the same
	/// table every damage row is keyed by, and BattleSession.GetActor already merges a re-created
	/// summon token into its actor row. What did NOT exist was a read-only "give me the key, do not
	/// create a row" entry point. This private wrapper is the smallest honest addition: the atkadd
	/// capture is its only caller, and Aggregator.cs is outside this task's edit boundary. If a second
	/// caller ever appears, promote this method verbatim to Aggregator.KeyOf.
	///
	/// create:false is essential. A giver that has not dealt damage yet (a support that only GRANTED
	/// attack power) must not be handed an actor row just to be attributed -- that would change the
	/// roster and the export. It returns 0 instead, which AtkAddFold treats as "take the COUNTED name
	/// fallback". No exception may escape: an unreadable key degrades to the same counted fallback.
	/// </summary>
	private static int ActorKeyOf(BattleObject bo)
	{
		if (GameRef.IsNull(bo)) return 0;
		try
		{
			BattleSession s = Aggregator.Session;
			if (s == null) return 0;
			ActorStats a = s.GetActor(bo, false);
			return (a == null) ? 0 : a.Key;
		}
		catch { return 0; }
	}

	/// <summary>1.7.4 (阶段 G): the structured twin of ONE 攻击力 entry in the 增益 text. It carries
	/// the APPLIED value (ParamData.Param, not the declared key value), the sign the text prints, the
	/// reference, and BOTH owner sides read at this very instant. The owner is what
	/// AtkAddFold turns into a giver; the value side is the one MEASURED to be the giver (12,539 of
	/// 15,539 entries name a unit other than the dictionary holder, and the two sides disagreed 0 times).
	/// Reads are individually guarded and counted -- the precedent is the 1.5.4 owner getter that threw
	/// for every single entry, so a total failure has to be a NUMBER, not a crash or a silent null.</summary>
	private static AtkAddItem ReadAtkItem(BuffParam key, BuffParamData.ParamData val)
	{
		try
		{
			var it = new AtkAddItem();
			it.Plus = true;
			try { it.Plus = key.IsPlus; } catch { }
			if (val != null)
			{
				try { it.Type = val.Type.ToString(); } catch { }
				try { it.Value = val.Param; } catch { }
				try
				{
					string rt = val.ReferenceType.ToString();
					int rp = val.ReferenceParam;
					if (!string.IsNullOrEmpty(rt) && rt != "None") it.Ref = "/ref" + rt + rp;
				}
				catch { }
			}
			if (string.IsNullOrEmpty(it.Type)) { try { it.Type = key.BuffType.ToString(); } catch { } }
			if (it.Value == 0) { try { it.Value = key.BuffValue; } catch { } }
			try
			{
				var o = val == null ? null : val.Owner;
				if (!GameRef.IsNull(o)) { it.Owner = Aggregator.NameOf(o); it.OwnerKey = ActorKeyOf(o); }
			}
			catch { ParamOwnerProbe.NoteOwnerError(); }
			try
			{
				var ko = key.m_owner;
				if (!GameRef.IsNull(ko)) { it.KeyOwner = Aggregator.NameOf(ko); it.KeyOwnerKey = ActorKeyOf(ko); }
			}
			catch { ParamOwnerProbe.NoteOwnerError(); }
			if (it.Owner == "?") it.Owner = null;
			if (it.KeyOwner == "?") it.KeyOwner = null;
			return it;
		}
		catch { return null; }
	}

	/// <summary>
	/// One buff entry. BuffTarget decides the wording; BuffType decides the unit (Rate = %).
	/// Some targets carry an ID rather than a amount (Missile / RangeShape / HitType / ...),
	/// so those are printed as "#id" instead of pretending to be a magnitude.
	/// </summary>
	private static string FormatEntry(BuffParam key, BuffParamData.ParamData val, bool declared)
	{
		try
		{
			string tgtEn = null;
			string typEn = null;
			bool plus = true;
			int applied = 0;
			string refNote = "";
			int declaredValue = 0;

			try { tgtEn = key.BuffTarget.ToString(); } catch { }
			try { typEn = key.BuffType.ToString(); } catch { }
			try { plus = key.IsPlus; } catch { }
			try { declaredValue = key.BuffValue; } catch { }

			// ParamData carries the value the game actually applies, plus its reference base
			try
			{
				if (val != null)
				{
					try { tgtEn = val.Target.ToString(); } catch { }
					try { typEn = val.Type.ToString(); } catch { }
					try { applied = val.Param; } catch { }
					try
					{
						string rt = val.ReferenceType.ToString();
						int rp = val.ReferenceParam;
						if (!string.IsNullOrEmpty(rt) && rt != "None") refNote = "(参照" + RefName(rt) + rp + "%)";
					}
					catch { }
				}
			}
			catch { }

			string tgt = TargetName(tgtEn);
			if (string.IsNullOrEmpty(tgt) || tgt == "无" || tgt == "上限") return "";
			if (string.IsNullOrEmpty(typEn) || typEn == "None" || typEn == "Max") typEn = "Actual";

			if (IsIdTarget(tgtEn))
				return tgt + "#" + (applied != 0 ? applied : declaredValue);

			int value = (applied != 0) ? applied : declaredValue;
			string unit = (typEn == "Rate") ? "%" : "";
			var sb = new StringBuilder(28);
			sb.Append(tgt).Append(plus ? "+" : "-").Append(value).Append(unit);
			// The game itself stores sentinel values in this table: measured "会心率+99999" comes from an
			// ability whose text says 会心率+50% and whose talent line reads 78/99999 (parameter 78 = 会心率,
			// confirmed by ability #5 「会心率+15%」→ 78/15). The number is what the game applies, so it is
			// printed verbatim -- the marker only prevents it from being read as a value we computed.
			if (System.Math.Abs(value) >= 10000) sb.Append("(原始值)");
			if (!string.IsNullOrEmpty(refNote)) sb.Append(refNote);
			if (declared && declaredValue != 0 && declaredValue != value) sb.Append("(声明").Append(declaredValue).Append(')');
			return sb.ToString();
		}
		catch { return ""; }
	}

	/// <summary>
	/// Buff parameters that can change the damage of a single hit: attack power, the crit family,
	/// penetration and the attack attribute. Everything else (耐久/回复率/攻撃速度/移動速度/格挡数/
	/// 仇恨/各种抗性/投射物ID …) is irrelevant to "why did this hit land for this much".
	/// </summary>
	private static bool IsDamageRelevantTarget(string en)
	{
		switch (en)
		{
			case "Power":
			case "CriticalRate":
			case "CriticalRateLimit":
			case "CriticalDamageRate":
			case "CriticalDownRate":
			case "CriticalDamageDownRate":
			case "PenetrationRate":
			case "AdditionalAttribute":
			case "HitType":
				return true;
			default:
				return false;
		}
	}

	/// <summary>Targets whose value is an identifier, not a magnitude.</summary>
	private static bool IsIdTarget(string en)	{
		switch (en)
		{
			case "Missile":
			case "RangeShape":
			case "TargetType":
			case "HitType":
			case "AttackSortConfig":
			case "AttackFilterConfig":
			case "SubAttack":
			case "PlayKnockBackType":
			case "AdditionalAttribute":
				return true;
			default:
				return false;
		}
	}

	/// <summary>Compact Chinese composition summary for the in-game detail view.</summary>
	public static string BuildSummary(DamageCalculater calc)
	{
		try
		{
			var sb = new StringBuilder(160);
			int pow = 0;
			try { pow = GameRef.Dec(calc.m_power); } catch { }
			sb.Append("威力 ").Append(pow);
			try { if (calc.m_effectId != 0) sb.Append(" · 效果").Append(calc.m_effectId); } catch { }
			try { sb.Append(" · 攻击属性 ").Append(HitTypeName((int)calc.m_hitType)); } catch { }
			try { sb.Append(" · ").Append(SrcName((int)calc.m_damageSource)); } catch { }

			BattleObject atk = null;
			try { atk = calc.Attacker; } catch { }
			if (!GameRef.IsNull(atk))
			{
				string buffs = BuildBuffTextVerbose(atk);
				sb.Append(" · 增益:").Append(string.IsNullOrEmpty(buffs) ? "无" : buffs);
			}
			try
			{
				BattleObject blk = null;
				try { blk = calc.m_blocker; } catch { }
				string ct = CritText(blk);
				if (!string.IsNullOrEmpty(ct)) sb.Append(" · ").Append(ct);
				int ht = -1;
				try { ht = (int)calc.m_hitType; } catch { }
				string tgtAb = AbilityBrief(blk, 4, ht, victimSide: true);
				if (!string.IsNullOrEmpty(tgtAb)) sb.Append(" · 受击方被伤害补正:").Append(tgtAb);
				string atkAb = AbilityBrief(atk, 4, ht, victimSide: false);
				if (!string.IsNullOrEmpty(atkAb)) sb.Append(" · 攻击方与伤害补正:").Append(atkAb);
			}
			catch { }
			return sb.ToString();
		}
		catch { return ""; }
	}
}
