using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DpsMeter;

/// <summary>
/// The battle's reconciliation KPI (1.3.0): how much of the damage composition actually reproduces the
/// game's numbers, and -- when it does not -- WHAT is missing, as a ranked list instead of a single ratio.
///
/// Why this exists (measured 2026-10-03 over all 725 exports / 228,646 events):
///   * the offline "reconcile rate" had no single definition, so two mutually incomparable figures
///     (35.8% from the 1.2.0-era notes and 25.4% from a full-corpus scan) were both in circulation;
///   * the unexplained part is NOT noise: `1.210` (= 1.1 × 1.1) alone was 21.7% of it, the top 20 residual
///     values covered 62%, and 12.1% of it was residual &lt; 0.1 -- i.e. WRONG PAIRING, not a missing
///     multiplier. Mixing those two kinds of failure makes the number impossible to improve.
///
/// So each battle now reports, in-band:
///   exact / approx / unexplained (the KPI itself, with the definition written down),
///   theoryExceeds (the extreme one-sided-gap subset, counted separately),
///   byPair (how the compositions were paired -- "未获佐证" is now visible instead of implied),
///   topResidual (the worklist: which multipliers are still missing, by frequency),
///   byTenth (the KPI per 10% of battle time -- the 2026-10-03 scan showed it is NOT flat: one window of
///   battle 401003 sat at 1.6% while its neighbours sat at 46%).
///
/// Everything is DERIVED from the events at export time, never accumulated during the battle: a summary
/// that is computed separately from the rows it summarises is exactly how the offline figures drifted
/// apart in the first place.
/// </summary>
internal static class CalcReconcile
{
	/// <summary>"approx" band: within this relative distance of the game's number. Measured 2026-10-03:
	/// widening exact to ±5% moves the rate from 26.4% to 27.1%, so the band is reported but the
	/// exact/approx split is not a lever -- the distribution is bimodal (either it matches or it is far off).</summary>
	internal const double ApproxTolerance = 0.05;

	/// <summary>Applied/Theory below this means the chain predicts more than 10× what the game applied.
	///
	/// 1.3.0 first shipped this as "mispaired" and the FIRST RUNTIME BATTLE disproved that reading
	/// (2026-10-03, quest 9999): all 314 such hits were the training-ground structures 城塞 (173) and
	/// T.O.W.E.R.typeR (141), 309 of them from one attacker ムスクーマ, and their theory values were
	/// SMALLER than the battle's median -- i.e. a normal prediction meeting an unmodelled ~×0.03
	/// reduction on those targets, not a wrong pairing. Real quests (401003 / 411001) have 0 of them.
	/// So the counter is named for what it measures -- an extreme one-sided gap -- and the reading is
	/// left to the reader: a high count together with many `live-age` pairings and a concentration on
	/// one victim means "unmodelled mechanic"; scattered across all victims and pairings it would mean
	/// pairing trouble. Never report it as "mispaired" on its own again.</summary>
	internal const double TheoryExceedsResidualMax = 0.10;

	/// <summary>Relative tolerance for counting a record as "exact once the crit roll is taken into
	/// account" (see <see cref="Stats.ExactWithCrit"/>). 1.3.10 replaced exact integer equality with
	/// this: of the 720 crit-shaped hits in battle_...143156, all 720 were within 0.1% of
	/// `Theory × 会心伤害率/100` (median 0.002%, max 0.082%) while only 288 were exactly equal, because
	/// the game's damage number is a rounded integer. The nearest competing class (a 165 crit read
	/// against a 190 stat) is 13% away, so 0.2% cannot swallow anything else. Exported with the other
	/// tolerances so the definition travels with the data.</summary>
	internal const double CritInferredTolerance = 0.002;

	internal const int TopResidualCount = 20;

	/// <summary>
	/// Is this record explained ONCE THE CRIT ROLL IS COUNTED -- i.e. is the residual exactly the
	/// declared 会心ダメージ率? Split out in 1.4.0 so <see cref="Compute"/> and the per-hit forensics
	/// channel cannot disagree about what "unexplained" means; before this the rule existed in one place
	/// and the worklist was built from a second, re-derived copy.
	/// </summary>
	internal static bool IsCritExplained(CalcBreakdown b)
	{
		// CritRate > 0 is required as well: at 会心率 0 no roll can land, so a 190/100 ratio there is an
		// unexplained factor, not a crit.
		if (b.CritRate <= 0 || b.CritDamageRate <= 100 || b.Theory <= 0 || b.GameValue <= 0) return false;
		double want = b.Theory * (b.CritDamageRate / 100.0);
		double diff = want - b.GameValue;
		if (diff < 0) diff = -diff;
		return diff / b.GameValue <= CritInferredTolerance;
	}

	/// <summary>The chain reproduces the game's number, exactly or with the crit roll folded in.</summary>
	internal static bool IsExplained(CalcBreakdown b)
	{
		return b.ValueMatches || IsCritExplained(b);
	}

	internal sealed class Stats
	{
		public int DmgEvents;        // every damage record in the export (the wider denominator)
		public int WithCalc;         // those that carry a composition (the KPI denominator)
		public int Exact;
		public int Approx;
		public int Unexplained;
		/// <summary>
		/// Records that are exact ONCE THE GAME'S OWN CRIT ROLL IS TAKEN INTO ACCOUNT: i.e.
		/// `Theory == GameValue` OR `Theory × CriticalDamageRate/100 == GameValue`.
		///
		/// 1.3.9. Why this is a classification and not a formula change: the crit MULTIPLIER is fully
		/// determined (`critDamageRate/100`, already verified twice -- residual == that value on exactly
		/// the hits whose 会心率 says the roll should land, 39.6% observed against 40% declared), but the
		/// plugin cannot read the game's crit FLAG. So the count is derived from the record, is emitted
		/// under its own name, and `Exact` keeps its strict meaning (`Theory == GameValue`).
		///
		/// Measured 2026-10-03 on battle_...141816: 680 of the 1,757 unexplained records sat on exactly
		/// 1.900 (= 190/100) and another 45 on 1.650 -- with this counted, the explained share goes from
		/// 68.2% to ~82%.
		/// </summary>
		public int ExactWithCrit;
		/// <summary>Records counted by <see cref="ExactWithCrit"/> that were NOT already exact.</summary>
		public int CritInferred;
		/// <summary>Subset of Unexplained whose theory exceeds the game's number by more than 10×
		/// (see <see cref="TheoryExceedsResidualMax"/> for why this is NOT called "mispaired").</summary>
		public int TheoryExceeds;
		/// <summary>How much of a hit exceeded the target's remaining 耐久, summed over the battle -- R78's
		/// rename of what this field used to call "absorbed / nullified before reaching 耐久" (R76 measured the
		/// call's return to be the OVERFLOW: `res == max(0, nominal - lifeBefore)`, 798/798 readable readings;
		/// see `Policy/AbsorbWording.cs`).
		///
		/// Taken from the EVENTS (nominal − amount), not from Aggregator's Rt.AbsorbedTotal, for the same
		/// reason the rest of this block is derived from the events: one source of truth.
		///
		/// It matters because the GAME's own damage statistic counts the value the game accounted for (measured:
		/// it accumulates BattleObject.Damage's argument -- see the 超出剩余耐久 note in the chain text),
		/// so our `dealt` reads lower than the in-game report by exactly this amount.
		/// Measured over the 32 battle-exports of 2026-10-03: 13.15% in aggregate, 55.6% in one battle.
		/// Exported as its own field so both figures can be reconciled WITHOUT changing the displayed one.
		/// R78 changed this doc and the labels only; the number and the export key are untouched.</summary>
		public long AbsorbedAmount;
		public int Absorbed;         // records where the hit exceeded the target's remaining 耐久 (R78 rename)
		public int DistinctResiduals;
		public int PairLiveSame, PairLiveAge, PairValue, PairFifo, PairNone;
		/// <summary>Compositions whose pairing route was never set. Should stay 0 -- it exists so that a
		/// new code path that forgets to set Pair is COUNTED instead of silently joining the
		/// "no composition at all" bucket.</summary>
		public int PairUnknown;
		public int PairCorroborated;

		/// <summary>
		/// 1.5.0 (A3): the crit question, DECOMPOSED. `ExactWithCrit` infers the crit roll from the
		/// arithmetic because the plugin never used to read the game's own flag; 1.5.0 wires that flag up
		/// (see BattleEvent.CritObserved) and these four counters say how well the inference agrees with
		/// the game. They are counters, NOT a change of KPI: `exact`/`exactWithCrit` keep the 1.3.10
		/// definition so a battle stays comparable with the corpus. Once the agreement is measured, moving
		/// the KPI onto the observed flag becomes a decision with evidence behind it.
		/// </summary>
		public int CritObserved;
		/// <summary>Of those, the ones the game reported AS a crit.</summary>
		public int CritObservedYes;
		/// <summary>Observed a crit AND the crit multiplier explains the residual -- inference confirmed.</summary>
		public int CritAgree;
		/// <summary>Observed NOT a crit, yet the residual is exactly the crit multiplier: the inference is
		/// wrong on this hit (counted, never hidden).</summary>
		public int CritInferredButDenied;
		/// <summary>Observed a crit, yet the crit multiplier does NOT explain the residual: the inference
		/// missed it.</summary>
		public int CritObservedButUnexplained;

		public readonly int[] TenthExact = new int[10];
		public readonly int[] TenthTotal = new int[10];
		public readonly List<KeyValuePair<int, int>> Top = new List<KeyValuePair<int, int>>();

		public double Rate { get { return WithCalc > 0 ? (100.0 * Exact / WithCalc) : 0.0; } }
	}

	internal static Stats Compute(BattleSession s)
	{
		var st = new Stats();
		if (s == null || s.Events == null) return st;
		double dur = s.ActiveSeconds;
		var hist = new Dictionary<int, int>();
		try
		{
			for (int i = 0; i < s.Events.Count; i++)
			{
				BattleEvent e = s.Events[i];
				if (e == null || e.Type != "dmg") continue;
				st.DmgEvents++;
				// Absorbed is derivable from the event's own two numbers, so it is available even for a
				// record that carries no composition at all.
				if (e.Nominal > e.Amount) st.AbsorbedAmount += e.Nominal - e.Amount;
				CalcBreakdown b = e.Calc;
				int tenth = (dur > 0.0) ? (int)(10.0 * e.T / dur) : 0;
				if (tenth < 0) tenth = 0;
				if (tenth > 9) tenth = 9;
				if (!b.Valid)
				{
					st.PairNone++;
					st.TenthTotal[tenth]++;
					continue;
				}
				st.WithCalc++;
				st.TenthTotal[tenth]++;
				if (b.Absorbed > 0) st.Absorbed++;
				if (b.PairCorroborated) st.PairCorroborated++;
				switch (b.Pair)
				{
					case "live-same": st.PairLiveSame++; break;
					case "live-age": st.PairLiveAge++; break;
					case "value": st.PairValue++; break;
					case "fifo": st.PairFifo++; break;
					default: st.PairUnknown++; break;
				}
				bool exact = b.ValueMatches;
				// 1.3.9/1.4.0: the crit roll is the single largest remaining class and its multiplier is
				// known exactly (critDamageRate/100). Counted under its own name so `Exact` stays strict;
				// the rule itself lives in IsCritExplained so the forensics channel uses the same one.
				bool critExact = !exact && IsCritExplained(b);
				// 1.5.0 (A3): compare the inference against the game's own flag. Placed before the
				// `continue`s below so it sees every classified hit.
				if (e.CritObserved != 0)
				{
					st.CritObserved++;
					if (e.CritObserved == 2) st.CritObservedYes++;
					if (e.CritObserved == 2 && critExact) st.CritAgree++;
					if (e.CritObserved == 1 && critExact) st.CritInferredButDenied++;
					if (e.CritObserved == 2 && !IsExplained(b)) st.CritObservedButUnexplained++;
				}
				if (exact)
				{
					st.Exact++;
					st.ExactWithCrit++;
					st.TenthExact[tenth]++;
					continue;
				}
				if (critExact)
				{
					st.CritInferred++;
					st.ExactWithCrit++;
					continue;
				}
				long game = b.GameValue;
				bool approx = game > 0 && b.Theory > 0
					&& Math.Abs((double)b.Theory - game) / game <= ApproxTolerance;
				if (approx) { st.Approx++; continue; }
				st.Unexplained++;
				// A normal prediction meeting a very large one-sided gap. Reading it as "wrong pairing"
				// was disproved by the first runtime battle -- see TheoryExceedsResidualMax.
				if (b.Absorbed == 0 && b.Theory > 0 && b.Residual > 0.0 && b.Residual < TheoryExceedsResidualMax)
					st.TheoryExceeds++;
				int key = (int)Math.Round(b.Residual * 1000.0);
				int c;
				hist.TryGetValue(key, out c);
				hist[key] = c + 1;
			}
		}
		catch { }
		st.DistinctResiduals = hist.Count;
		var all = new List<KeyValuePair<int, int>>(hist);
		all.Sort(delegate (KeyValuePair<int, int> x, KeyValuePair<int, int> y)
		{
			int d = y.Value.CompareTo(x.Value);
			return d != 0 ? d : x.Key.CompareTo(y.Key);
		});
		int take = all.Count < TopResidualCount ? all.Count : TopResidualCount;
		for (int i = 0; i < take; i++) st.Top.Add(all[i]);
		return st;
	}

	/// <summary>One line for the runtime log, so the KPI is visible without opening the JSON.</summary>
	internal static string Summary(Stats st)
	{
		if (st == null) return "";
		var sb = new StringBuilder(220);
		sb.Append("伤害事件=").Append(st.DmgEvents)
		  .Append(" 有构成=").Append(st.WithCalc)
		  .Append(" 精确=").Append(st.Exact).Append('(').Append(st.Rate.ToString("F1", CultureInfo.InvariantCulture)).Append("%)")
		  .Append(" 计入会心=").Append(st.ExactWithCrit)
		  .Append("(其中凭会心新增=").Append(st.CritInferred).Append(')')
		  // 1.5.0 (A3): how the inference compares with the game's own flag. Printed even at 0 so a dead
		  // channel cannot look like perfect agreement.
		  .Append(" 会心实测=").Append(st.CritObserved).Append("/是=").Append(st.CritObservedYes)
		  .Append(" 一致=").Append(st.CritAgree)
		  .Append(" 实测否决=").Append(st.CritInferredButDenied)
		  .Append(" 实测未解释=").Append(st.CritObservedButUnexplained)
		  .Append(" 近似=").Append(st.Approx)
		  .Append(" 未解释=").Append(st.Unexplained)
		  .Append(" | 理论超实际10倍=").Append(st.TheoryExceeds)
		  .Append(" 被吸收=").Append(st.Absorbed).Append("件/").Append(st.AbsorbedAmount)
		  .Append(" | 配对 live-same=").Append(st.PairLiveSame)
		  .Append(" live-age=").Append(st.PairLiveAge)
		  .Append(" value=").Append(st.PairValue)
		  .Append(" fifo=").Append(st.PairFifo)
		  .Append(" 无构成=").Append(st.PairNone)
		  .Append(" 配对未标注=").Append(st.PairUnknown)
		  .Append(" 佐证=").Append(st.PairCorroborated)
		  .Append(" | 未识别倍率 种类=").Append(st.DistinctResiduals);
		if (st.Top.Count > 0)
		{
			sb.Append(" 首位=").Append((st.Top[0].Key / 1000.0).ToString("F3", CultureInfo.InvariantCulture))
			  .Append('×').Append(st.Top[0].Value);
		}
		// Anything that could not be measured is stated rather than left to look like a zero.
		sb.Append(" | 口径:精确=理论==游戏值(含被吸收),近似=|差|≤5%,"
			+ "理论超实际10倍=理论>10×实际(可能未建模的大幅减伤,不必然是配对错位)");
		return sb.ToString();
	}

	/// <summary>Appends the root-level `reconcile` object.</summary>
	internal static void AppendJson(StringBuilder sb, Stats st)
	{
		if (st == null) return;
		sb.Append(",\"reconcile\":{\"dmgEvents\":").Append(st.DmgEvents)
		  .Append(",\"withCalc\":").Append(st.WithCalc)
		  .Append(",\"exact\":").Append(st.Exact)
		  // 1.3.9: exact OR exact-once-the-crit-roll-is-accounted-for, plus how much of the latter is new.
		  .Append(",\"exactWithCrit\":").Append(st.ExactWithCrit)
		  .Append(",\"critInferred\":").Append(st.CritInferred)
		  // 1.5.0 (A3): the crit question decomposed. `critObserved` counts hits where the GAME's own flag
		  // was read (0 = the channel saw nothing, which is why the counters are always emitted).
		  .Append(",\"critObserved\":").Append(st.CritObserved)
		  .Append(",\"critObservedYes\":").Append(st.CritObservedYes)
		  .Append(",\"critAgree\":").Append(st.CritAgree)
		  .Append(",\"critInferredButDenied\":").Append(st.CritInferredButDenied)
		  .Append(",\"critObservedButUnexplained\":").Append(st.CritObservedButUnexplained)
		  .Append(",\"approx\":").Append(st.Approx)
		  .Append(",\"unexplained\":").Append(st.Unexplained)
		  .Append(",\"theoryExceeds\":").Append(st.TheoryExceeds)
		  .Append(",\"absorbed\":").Append(st.Absorbed)
		  .Append(",\"absorbedAmount\":").Append(st.AbsorbedAmount)
		  .Append(",\"distinctResiduals\":").Append(st.DistinctResiduals)
		  .Append(",\"approxTolerance\":").Append(ApproxTolerance.ToString("F2", CultureInfo.InvariantCulture))
		  .Append(",\"theoryExceedsResidualMax\":").Append(TheoryExceedsResidualMax.ToString("F2", CultureInfo.InvariantCulture))
		  .Append(",\"critInferredTolerance\":").Append(CritInferredTolerance.ToString("F4", CultureInfo.InvariantCulture))
		  .Append(",\"byPair\":{\"liveSame\":").Append(st.PairLiveSame)
		  .Append(",\"liveAge\":").Append(st.PairLiveAge)
		  .Append(",\"value\":").Append(st.PairValue)
		  .Append(",\"fifo\":").Append(st.PairFifo)
		  .Append(",\"none\":").Append(st.PairNone)
		  .Append(",\"unknown\":").Append(st.PairUnknown)
		  .Append(",\"corroborated\":").Append(st.PairCorroborated).Append('}');
		sb.Append(",\"byTenthExact\":[");
		for (int i = 0; i < 10; i++) { if (i > 0) sb.Append(','); sb.Append(st.TenthExact[i]); }
		sb.Append("],\"byTenthTotal\":[");
		for (int i = 0; i < 10; i++) { if (i > 0) sb.Append(','); sb.Append(st.TenthTotal[i]); }
		sb.Append(']');
		// The worklist: which multipliers are still missing, most frequent first.
		sb.Append(",\"topResidual\":[");
		for (int i = 0; i < st.Top.Count; i++)
		{
			if (i > 0) sb.Append(',');
			sb.Append("{\"r\":").Append((st.Top[i].Key / 1000.0).ToString("F3", CultureInfo.InvariantCulture))
			  .Append(",\"n\":").Append(st.Top[i].Value).Append('}');
		}
		sb.Append("]}");
	}

	/// <summary>Appends the per-event `calc` object: the same numbers the 中文 composition line mentions,
	/// plus the two booleans that used to be conflated into one sentence.</summary>
	internal static void AppendEventCalc(StringBuilder sb, CalcBreakdown b)
	{
		sb.Append(",\"calc\":{\"effectId\":").Append(b.EffectId)
		  .Append(",\"hitType\":").Append(b.HitType)
		  .Append(",\"attackPower\":").Append(b.AttackPower)
		  .Append(",\"power\":").Append(b.Power)
		  .Append(",\"ratio\":").Append(b.Ratio.ToString("F2", CultureInfo.InvariantCulture))
		  .Append(",\"base\":").Append(b.BaseDamage)
		  .Append(",\"attrMult\":").Append(b.AttrMult.ToString("F3", CultureInfo.InvariantCulture))
		  .Append(",\"dealtMult\":").Append(b.DealtMult.ToString("F3", CultureInfo.InvariantCulture))
		  .Append(",\"takenMult\":").Append(b.TakenMult.ToString("F3", CultureInfo.InvariantCulture))
		  .Append(",\"knownMult\":").Append(b.KnownMult.ToString("F3", CultureInfo.InvariantCulture))
		  .Append(",\"theory\":").Append(b.Theory)
		  .Append(",\"applied\":").Append(b.Applied)
		  .Append(",\"residual\":").Append(b.Residual.ToString("F3", CultureInfo.InvariantCulture));
		// Zero/absent values are omitted, same convention as the comp strings: their absence means
		// "nothing applied", not "not measured".
		if (b.DefenseUsed != 0)
			sb.Append(",\"defense\":").Append(b.DefenseUsed)
			  .Append(",\"defenseKind\":\"").Append(JsonText.Str(b.DefenseKind)).Append('"');
		if (b.Penetration != 0) sb.Append(",\"penetration\":").Append(b.Penetration);
		if (b.EffectiveDefense != 0) sb.Append(",\"effectiveDefense\":").Append(b.EffectiveDefense);
		if (b.MinRule) sb.Append(",\"minRule\":true");
		if (b.Absorbed != 0) sb.Append(",\"absorbed\":").Append(b.Absorbed);
		if (b.CritRate != 0) sb.Append(",\"critRate\":").Append(b.CritRate);
		if (b.CritDamageRate != 0) sb.Append(",\"critDamageRate\":").Append(b.CritDamageRate);
		// The victim's blocking state at hit time (see CalcBreakdown.VictimIsBlocking). Always emitted --
		// including the -1 "could not read" case -- because the whole point is to find out which reading
		// separates the hits that carry the extra ×1.21 from those that do not.
		if (b.VictimIsBlocking >= 0) sb.Append(",\"vicBlocking\":").Append(b.VictimIsBlocking);
		if (b.VictimIsUnitBlocking >= 0) sb.Append(",\"vicUnitBlocking\":").Append(b.VictimIsUnitBlocking);
		if (b.VictimBlockCount >= 0) sb.Append(",\"vicBlockCount\":").Append(b.VictimBlockCount);
		// What the victim was actually carrying at hit time. The text is what makes the ×1.210 question
		// answerable: "1006/-10/0,1006/-10/0" on the victim would be the two granted 被ダメージ+10%.
		if (b.VictimExtraTalents >= 0) sb.Append(",\"vicExtra\":").Append(b.VictimExtraTalents);
		if (!string.IsNullOrEmpty(b.VictimExtraTalentText))
			sb.Append(",\"vicExtraText\":\"").Append(b.VictimExtraTalentText).Append('"');
		if (b.VictimBuffs >= 0) sb.Append(",\"vicBuffs\":").Append(b.VictimBuffs);
		// Talents GRANTED to the victim by other units (1.3.5) -- the channel 刻印 id=26 travels through.
		// Emitted even at 0, unlike the fields above: "the victim's grant list was read and was empty" is
		// the single most important negative result for this hypothesis, and it must be distinguishable
		// from "the field was never read".
		if (b.GivenTalents >= 0) sb.Append(",\"vicGive\":").Append(b.GivenTalents);
		if (b.GivenApplied != 0) sb.Append(",\"vicGiveApplied\":").Append(b.GivenApplied);
		if (!string.IsNullOrEmpty(b.GivenTalentText))
			sb.Append(",\"vicGiveText\":\"").Append(b.GivenTalentText).Append('"');
		// 1.7.9: the switch that decided whether those granted entries were folded on THIS hit. Written even
		// when true, unlike the fields above: `false` is the value that changes what vicGive/vicGiveApplied
		// mean, and a reader must be able to tell "this theory was in force" from "this field did not exist
		// yet" without knowing which version wrote the file.
		sb.Append(",\"givenFoldOn\":").Append(b.GivenFoldOn ? "true" : "false");
		// 狂気 (1.4.1): the raw reading travels with the hit so the fold can be falsified from data.
		// `madnessRatio` is emitted even when neutral (100), unlike the other fields -- 100 is the
		// measured baseline and its presence is what says "we looked".
		if (b.MadnessRatio != 0) sb.Append(",\"madnessRatio\":").Append(b.MadnessRatio);
		if (b.MadnessOn) sb.Append(",\"madnessOn\":true");
		// 1.5.3: the VICTIM's 狂気 (incoming side) -- the flag the victim fold was driven by.
		if (b.VictimMadnessOn) sb.Append(",\"victimMadnessOn\":true");
		// 1.5.3: the victim's status NAMES as a machine-readable list in `calc`, from the SAME read that
		// writes comp4's 受击方状态. Before this the only per-hit copy was the comp4 sentence, so every
		// offline script had to parse prose -- and mis-split trailing tokens doing it. Cheap, and it is
		// what lets a status-conditioned rule be checked against the statuses THAT HIT actually saw.
		if (b.VictimStatuses != null && b.VictimStatuses.Length > 0)
		{
			sb.Append(",\"victimStatuses\":[");
			for (int i = 0; i < b.VictimStatuses.Length; i++)
			{
				if (i > 0) sb.Append(',');
				sb.Append('"').Append(b.VictimStatuses[i]).Append('"');
			}
			sb.Append(']');
		}
		// 1.5.0 (A1/A5): the PROVENANCE. `fold` is every factor actually multiplied in, in fold order,
		// each with a stable origin id built from game ids (so two same-valued rules are distinguishable);
		// `cancel` names every granted copy the cross-unit rule path absorbed AND the rule that absorbed
		// it; `responsibility`/`maxAbsorbed` make "one responsible rule absorbed five granted copies"
		// visible. Before 1.5.0 none of this existed per hit -- the provenance was Chinese prose and the
		// cancellation left only an aggregate counter, which is why the 1.15^n residual could only be
		// guessed at across many battles.
		if (b.Fold != null)
		{
			sb.Append(",\"fold\":[");
			for (int i = 0; i < b.Fold.Count; i++)
			{
				if (i > 0) sb.Append(',');
				b.Fold[i].AppendJson(sb);
			}
			sb.Append(']');
		}
		if (b.FoldCancels != null)
		{
			sb.Append(",\"cancel\":[");
			for (int i = 0; i < b.FoldCancels.Count; i++)
			{
				if (i > 0) sb.Append(',');
				CancelStep c = b.FoldCancels[i];
				sb.Append("{\"origin\":\"").Append(JsonText.Str(c.Origin))
				  .Append("\",\"value\":").Append(c.Value.ToString("F4", CultureInfo.InvariantCulture))
				  .Append(",\"by\":\"").Append(JsonText.Str(c.ResponsibleOrigin))
				  .Append("\",\"label\":\"").Append(JsonText.Str(c.Label)).Append('"');
				// 1.5.4 (贡献归因 A): the giver of the absorbed grant, when it could be read.
				if (!string.IsNullOrEmpty(c.ByUnit))
					sb.Append(",\"byUnit\":\"").Append(JsonText.Str(c.ByUnit)).Append('"');
				sb.Append('}');
			}
			sb.Append(']');
		}
		if (b.FoldResponsible > 0) sb.Append(",\"responsibility\":").Append(b.FoldResponsible);
		if (b.FoldMaxAbsorbed > 0) sb.Append(",\"maxAbsorbed\":").Append(b.FoldMaxAbsorbed);
		if (b.FoldDropped > 0) sb.Append(",\"foldDropped\":").Append(b.FoldDropped);
		// 1.7.4 (阶段 G): the GIVERS of this hit extra attack power. Emitted with the FULL fold vocabulary
		// (side/kind/origin/factor/label + byUnit) plus the arithmetic that produced the factor, because the
		// file has to be self-describing: the plugin contribution core and BOTH offline cores consume these
		// rows verbatim rather than re-deriving the formula from prose.
		if (b.AtkAdd != null && b.AtkAdd.Count > 0)
		{
			sb.Append(",\"atkAdd\":[");
			for (int i = 0; i < b.AtkAdd.Count; i++)
			{
				if (i > 0) sb.Append(',');
				AtkAddRow ar = b.AtkAdd[i];
				sb.Append("{\"side\":\"").Append(JsonText.Str(ar.Side))
				  .Append("\",\"kind\":\"").Append(JsonText.Str(ar.Kind))
				  .Append("\",\"origin\":\"").Append(JsonText.Str(ar.Origin))
				  .Append("\",\"label\":\"").Append(JsonText.Str(ar.Label))
				  .Append("\",\"byUnit\":\"").Append(JsonText.Str(ar.ByUnit))
				  .Append("\",\"factor\":").Append(ar.Factor.ToString("R", CultureInfo.InvariantCulture))
				  .Append(",\"rate\":").Append(ar.RateSum.ToString("R", CultureInfo.InvariantCulture))
				  .Append(",\"actual\":").Append(ar.ActualSum)
				  .Append(",\"base\":").Append(ar.Base.ToString("R", CultureInfo.InvariantCulture))
				  .Append(",\"dPower\":").Append(ar.DeltaPower.ToString("R", CultureInfo.InvariantCulture))
				  .Append(",\"items\":\"").Append(JsonText.Str(ar.Items))
				  .Append("\"}");
			}
			sb.Append(']');
		}
		sb.Append(",\"pair\":\"").Append(JsonText.Str(b.Pair)).Append('"')
		  .Append(",\"pairTrusted\":").Append(b.PairTrusted ? "true" : "false")
		  .Append(",\"pairCorroborated\":").Append(b.PairCorroborated ? "true" : "false")
		  .Append(",\"valueMatches\":").Append(b.ValueMatches ? "true" : "false")
		  .Append('}');
	}
}
