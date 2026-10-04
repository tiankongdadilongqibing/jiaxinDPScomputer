using System.Collections.Generic;

namespace DpsMeter;

/// <summary>
/// 1.5.0 (架构审视 A1): ONE factor that was actually multiplied into the chain.
///
/// WHY IT EXISTS. Before 1.5.0 the whole fold collapsed into two doubles
/// (<see cref="CalcBreakdown.DealtMult"/> / <see cref="CalcBreakdown.TakenMult"/>) and the identity of
/// each contributing rule survived only as Chinese text. Measured on battle_411001_20261003_150140:
/// 84 distinct attacker-side factor labels, 2-5 of them folded into ONE hit -- so "which rule folded
/// which copy" was undecidable by construction. When a residual of exactly 1.15^n shows up, the question
/// "was this 1.15 under-folded or over-folded?" could only be answered by regexing the prose, which is
/// the same shape of risk that produced the 1.3.5-1.3.8 double-count accidents (exact 32.5% -> 1.5%,
/// then 41.9% -> 68.2%).
///
/// <see cref="Origin"/> is a STABLE ID, not a sentence: it is built from the game's own ids (ability
/// index / ability id / talent index, granted entry index) so two factors with the same VALUE from
/// different rules are distinguishable after the fact. <see cref="Label"/> keeps the human text.
/// </summary>
public sealed class FoldStep
{
	/// <summary>
	/// The side the modifier logically belongs to: "atk" for a 与ダメージ modifier, "vic" for a
	/// 被ダメージ one. NOTE it is the LOGICAL side, not the running product it lands in: the global-rule
	/// channel is folded into <see cref="CalcBreakdown.DealtMult"/> by CompositionProbe.Chain regardless
	/// of whether a rule is 敌方受伤 or 我方攻击 (a pre-1.5.0 wart kept for comparability). The
	/// per-factor side here is the informative one.
	/// </summary>
	public string Side;

	/// <summary>
	/// Which channel produced it:
	/// "text"    = a parsed clause of an ability text (AbilityScan)
	/// "talent"  = a talent parameter on an ability or an active buff (TalentDamage)
	/// "global"  = a battle-wide rule table entry (ApplyGlobalDebuffs)
	/// "given"   = a modifier GRANTED to the victim by another unit (GivenTalentDamage)
	/// "madness" = 狂気, read as CharaStatus.MadnessAllyBuffRatio/100
	/// </summary>
	public string Kind;

	/// <summary>
	/// Stable identity of the rule. Deliberately built from game ids, never from display text:
	///   text    -> "text#&lt;abilityIndex&gt;/&lt;abilityId&gt;/c&lt;clauseIndex&gt;"
	///   talent  -> "talent#&lt;abilityIndex&gt;/&lt;abilityId&gt;/t&lt;talentIndex&gt;"
	///              or "talent#buff&lt;buffIndex&gt;/t&lt;talentIndex&gt;"
	///   global  -> "global#&lt;ownerPointer&gt;/&lt;ruleIndex&gt;"
	///   given   -> "given#&lt;entryIndex&gt;/&lt;type&gt;/&lt;param&gt;"
	///   madness -> "madness#&lt;ratio&gt;"
	/// Two copies of the SAME rule are therefore still distinguishable (different ability/buff/entry
	/// index), which is what makes "is this copy already accounted for?" a decidable question.
	/// </summary>
	public string Origin;

	/// <summary>The number multiplied in. 1.0 means the rule was identified but contributed nothing.</summary>
	public double Factor;

	/// <summary>The human-readable label, the same text the composition line shows for this factor.</summary>
	public string Label;

	/// <summary>
	/// 1.5.4 (贡献归因): the unit this factor traces back to, when the game exposes one:
	///   given   -> the GIVER (GiveTalentData.ownerAction.owner -- decompiled interop 2026-10-03)
	///   madness -> whoever most recently APPLIED 狂気 to the unit carrying it (StatusApplierProbe)
	/// Null for text/talent (those are the attacker's own rules; the attribution rule is measured:
	/// ability-id join == attacker on 5875/5875 folds of battle_...205449) and for global (the owner
	/// pointer plus the name join already identify it). Display name -- the same key space as the
	/// event's attacker/victim fields.
	/// </summary>
	public string ByUnit;

	/// <summary>Compact JSON, built by hand like the rest of the export.</summary>
	public void AppendJson(System.Text.StringBuilder sb)
	{
		sb.Append('{')
		  .Append("\"side\":\"").Append(Side ?? "").Append('"')
		  .Append(",\"kind\":\"").Append(Kind ?? "").Append('"')
		  .Append(",\"origin\":\"").Append(DpsMeter.JsonText.Str(Origin)).Append('"')
		  // 1.6.1: Round-trip, not "F4". A 4-decimal factor (1.15^3 = 1.520875 -> "1.5209") made the
		  // export LOSSY: the contribution section is computed from the in-memory factor, so a reader
		  // recomputing it from the file got a different number (measured 2026-10-04 on 1.6.0: 58 fields
		  // off by ~1e-5 relative, and restoring the exact factor made the two implementations agree
		  // bit-for-bit). Reproducibility from the file is the contract this project is built on, so the
		  // file now carries the exact multiplier. Readers that displayed 4 decimals still can:
		  // the Comp text is unchanged and unchanged in meaning.
		  .Append(",\"factor\":").Append(Factor.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
		  .Append(",\"label\":\"").Append(DpsMeter.JsonText.Str(Label)).Append('"');
		// 1.5.4: conditional on purpose -- a byUnit on every text fold would be dead weight.
		if (!string.IsNullOrEmpty(ByUnit))
			sb.Append(",\"byUnit\":\"").Append(DpsMeter.JsonText.Str(ByUnit)).Append('"');
		sb.Append('}');
	}
}

/// <summary>
/// 1.5.0 (架构审视 A5): ONE granted modifier that was NOT folded because the cross-unit rule path
/// already accounts for it (the 1.3.6 cancellation in <c>GivenTalentDamage</c>).
///
/// WHY IT EXISTS. Before 1.5.0 a cancellation left NO per-hit trace -- only the aggregate counter
/// <c>GivenCancelled</c>. So the central open question ("is the 1.15^n residual caused by the
/// cancellation absorbing copies that belong to a different rule?") was unanswerable from one battle:
/// you could see that N grants were cancelled, but not WHICH responsible rule absorbed HOW MANY.
///
/// With this list one battle answers it, per hit. See <see cref="FoldedFactor.Absorbed"/>.
/// </summary>
public struct CancelStep
{
	/// <summary>The granted entry that was dropped, e.g. "given#3/1006/-15".</summary>
	public string Origin;

	/// <summary>The granted entry's factor value (1.15 for p=-15).</summary>
	public double Value;

	/// <summary>The responsible rule that absorbed it, e.g. "global#123456/2".</summary>
	public string ResponsibleOrigin;

	/// <summary>Human label of the granted entry.</summary>
	public string Label;

	/// <summary>1.5.4 (贡献归因 A): the GIVER of this cancelled grant (same source as FoldStep.ByUnit).
	/// The copy contributed nothing to this hit, but whose grant got absorbed is part of the
	/// absorption audit trail.</summary>
	public string ByUnit;
}

/// <summary>
/// 1.5.0 (架构审视 A5): one entry of a hit's 敌方受伤 RESPONSIBILITY set, carrying WHO it came from and
/// HOW MANY granted copies it absorbed.
///
/// Before 1.5.0 this set was <c>Dictionary&lt;double,int&gt;</c> (<c>_enemyCandidates</c>) and the gap
/// this fills is narrow but decisive: the lookup tested only whether a key existed, and the stored count
/// was never read (verified at all 7 use sites). So two rules of equal value were indistinguishable in
/// the data, and "one responsible rule absorbed five granted copies of the same value" -- the exact
/// pattern that would explain an over-folded 1.15^n -- was invisible.
///
/// WHAT IS DELIBERATELY NOT CHANGED. The cancellation stays VALUE-ONLY and UNBOUNDED: a responsible
/// value cancels every granted copy of that value. That is not an oversight, it is a measurement:
/// <c>CompositionProbe.GlobalRules</c> records that "consume one copy per fired factor" was tried in
/// 1.3.8 and left the stale per-status copies behind, over-counting by 1.15^2..1.15^3 on ~1,700 hits
/// (residuals 0.756 / 0.658 / 0.657 plus their crit variants). A multiset reading of THIS set therefore
/// cannot work as long as the set is recorded per RULE while the grant list holds per-STATUS copies.
/// 1.5.0 records the evidence instead of guessing which reading is right.
/// </summary>
public struct FoldedFactor
{
	/// <summary>Quantised factor value (see FoldContext.Q).</summary>
	public double Value;

	/// <summary>Stable identity of the responsible rule.</summary>
	public string Origin;

	/// <summary>Human-readable rule text (for the export).</summary>
	public string Text;

	/// <summary>How many granted copies this entry has absorbed on this hit. &gt;1 means the
	/// responsibility set and the grant list disagree about multiplicity -- the pattern under suspicion,
	/// and the reason this field exists.</summary>
	public int Absorbed;
}

/// <summary>
/// 1.5.0 (架构审视 A1+A5): the per-hit provenance sink.
///
/// It is passed EXPLICITLY down the fold (optional parameter, default null), replacing two pieces of
/// hidden per-hit state: the two running doubles' provenance (which never existed) and the static
/// <c>_enemyCandidates</c> set. The static set was silently overwritten by ANY call to
/// ApplyGlobalDebuffs -- including the diagnostics path, which recomputes the chain -- so the
/// cancellation decision depended on call ORDER rather than on data.
///
/// A null context is legal and means "measure only": every fold site keeps its arithmetic and simply
/// drops the provenance. That keeps the diagnostics path free of the cost and free of side effects.
///
/// This type must stay free of IL2CPP and Plugin references: <c>recon_probe</c> compiles it offline.
/// </summary>
public sealed class FoldContext
{
	/// <summary>Hard cap on recorded steps per hit, so one pathological hit cannot inflate the export.
	/// Overflow is counted, never silent.</summary>
	public const int MaxSteps = 24;

	/// <summary>Hard cap on recorded cancellations per hit (same reasoning).</summary>
	public const int MaxCancels = 8;

	/// <summary>The factors folded into this hit, in fold order.</summary>
	public readonly List<FoldStep> Steps = new List<FoldStep>(8);

	/// <summary>Granted copies dropped by the cancellation rule, with their origin and their absorber.</summary>
	public readonly List<CancelStep> Cancels = new List<CancelStep>(4);

	/// <summary>Steps that did not fit <see cref="MaxSteps"/>.</summary>
	public int StepsDropped;

	/// <summary>Cancellations that did not fit <see cref="MaxCancels"/>.</summary>
	public int CancelsDropped;

	private readonly List<FoldedFactor> _enemy = new List<FoldedFactor>(4);

	/// <summary>How many 敌方受伤 rules were responsible for this hit (exported as a diagnostic).</summary>
	public int EnemyFactorCount { get { return _enemy.Count; } }

	/// <summary>Granted copies cancelled against the responsibility set.</summary>
	public int EnemyConsumed;

	/// <summary>Largest number of granted copies absorbed by ONE responsible rule on this hit. 1 means
	/// the responsibility set and the grant list agree; &gt;1 is the pattern under suspicion.</summary>
	public int MaxAbsorbedByOne;

	/// <summary>Quantised key so 1.15f and 1.1499999 cannot become two different candidates.
	/// Same rounding as the pre-1.5.0 code so the responsibility set is unchanged.</summary>
	public static double Q(double f) { return System.Math.Round(f, 4); }

	/// <summary>Record one folded factor. <paramref name="origin"/> must be a stable id.
	/// 1.5.4: <paramref name="byUnit"/> optionally names the unit the factor traces to (giver of a
	/// granted modifier, applier of 狂気); null when the channel has no unit or the read failed.</summary>
	public void Add(string side, string kind, string origin, double factor, string label, string byUnit = null)
	{
		if (Steps.Count >= MaxSteps) { StepsDropped++; return; }
		Steps.Add(new FoldStep
		{
			Side = side,
			Kind = kind,
			Origin = origin,
			Factor = factor,
			Label = label,
			ByUnit = byUnit
		});
	}

	/// <summary>Record one 敌方受伤 rule as RESPONSIBLE for this hit. Recorded before the status test on
	/// purpose (responsibility, not "what fired") -- unchanged from the pre-1.5.0 semantics.</summary>
	public void AddEnemy(double value, string origin, string text)
	{
		var e = new FoldedFactor();
		e.Value = Q(value);
		e.Origin = origin;
		e.Text = text;
		e.Absorbed = 0;
		_enemy.Add(e);
	}

	/// <summary>
	/// Is a modifier of this value already accounted for by the cross-unit rule path? ALWAYS the
	/// pre-1.5.0 decision rule (value-only, unbounded -- see <see cref="FoldedFactor"/> for why that is
	/// deliberate), but now it also reports the absorbING rule's origin and counts the absorption.
	/// </summary>
	public bool TryConsumeModeled(double f, out string origin)
	{
		origin = null;
		double q = Q(f);
		for (int i = 0; i < _enemy.Count; i++)
		{
			double d = _enemy[i].Value - q;
			if (d < 0) d = -d;
			if (d > 1e-3) continue;
			var e = _enemy[i];
			e.Absorbed++;
			if (e.Absorbed > MaxAbsorbedByOne) MaxAbsorbedByOne = e.Absorbed;
			_enemy[i] = e;
			EnemyConsumed++;
			origin = e.Origin;
			return true;
		}
		return false;
	}

	/// <summary>Record a cancelled granted modifier, naming the rule that absorbed it. Separated from the
	/// decision so the decision and the evidence can never disagree.</summary>
	public void NoteCancellation(string responsibleOrigin, string grantedOrigin, double value, string label, string byUnit = null)
	{
		if (Cancels.Count >= MaxCancels) { CancelsDropped++; return; }
		var c = new CancelStep();
		c.Origin = grantedOrigin;
		c.Value = value;
		c.ResponsibleOrigin = responsibleOrigin;
		c.Label = label;
		c.ByUnit = byUnit;
		Cancels.Add(c);
	}

	/// <summary>Compact self-report for the runtime log.</summary>
	public string Summary()
	{
		var sb = new System.Text.StringBuilder(72);
		sb.Append("steps=").Append(Steps.Count);
		if (StepsDropped > 0) sb.Append("(+").Append(StepsDropped).Append("溢出)");
		sb.Append(" 责任=").Append(_enemy.Count)
		  .Append(" 取消=").Append(Cancels.Count)
		  .Append(" 单条最大吸收=").Append(MaxAbsorbedByOne);
		return sb.ToString();
	}
}
