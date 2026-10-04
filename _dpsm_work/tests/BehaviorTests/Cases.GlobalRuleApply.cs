using System;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// RF4, second family, apply side: the arithmetic of a battle-wide rule on one hit. The gate ladder
	/// itself stays in the probe (every rung performs a lazy native read whose order IS the behaviour -- see
	/// the policy's comment and STATE-LIFETIME-MATRIX section 7), so what is pinned here is what happens
	/// after the gates: how many copies, and the resulting factor.
	/// </summary>
	public static void GlobalRuleApply(Runner r)
	{
		r.Group("policy/globalrule-apply");
		r.Eq("hit-type-phys-is-1", GlobalRuleApplyPolicy.HitTypePhys, 1);
		r.Eq("hit-type-magic-is-2", GlobalRuleApplyPolicy.HitTypeMagic, 2);
		r.Eq("hit-type-both-is-5", GlobalRuleApplyPolicy.HitTypeBoth, 5);
		r.True("a-physical-hit-is-physical", GlobalRuleApplyPolicy.IsPhysHit(1));
		r.True("a-magic-hit-is-not-physical", !GlobalRuleApplyPolicy.IsPhysHit(2));
		r.True("a-magic-hit-is-magic", GlobalRuleApplyPolicy.IsMagicHit(2));
		r.True("a-physical-hit-is-not-magic", !GlobalRuleApplyPolicy.IsMagicHit(1));
		r.True("a-both-hit-satisfies-both", GlobalRuleApplyPolicy.IsMagicHit(5) && GlobalRuleApplyPolicy.IsPhysHit(5));
		r.True("an-unknown-hit-type-satisfies-neither",
		       !GlobalRuleApplyPolicy.IsMagicHit(-1) && !GlobalRuleApplyPolicy.IsPhysHit(-1));
		r.True("a-third-type-satisfies-neither",
		       !GlobalRuleApplyPolicy.IsMagicHit(3) && !GlobalRuleApplyPolicy.IsPhysHit(3));

		r.Group("policy/globalrule-copies");
		r.Eq("a-clause-without-statuses-fires-once", GlobalRuleApplyPolicy.StatusCopies(0, 0), 1);
		r.Eq("a-clause-without-statuses-ignores-the-match-count", GlobalRuleApplyPolicy.StatusCopies(0, 7), 1);
		r.Eq("no-matching-status-means-no-copies", GlobalRuleApplyPolicy.StatusCopies(3, 0), 0);
		r.Eq("one-matching-status-is-one-copy", GlobalRuleApplyPolicy.StatusCopies(3, 1), 1);
		r.Eq("three-matching-statuses-are-three-copies", GlobalRuleApplyPolicy.StatusCopies(3, 3), 3);

		r.Group("policy/globalrule-factor");
		r.True("a-single-copy-is-the-factor-itself", GlobalRuleApplyPolicy.EffectiveFactor(1.15, 1, true) == 1.15);
		r.True("one-copy-is-the-factor-even-with-per-status",
		       GlobalRuleApplyPolicy.EffectiveFactor(1.15, 1, true) == 1.15);
		// per-status off: the factor applies ONCE however many statuses matched
		r.True("without-per-status-three-statuses-still-apply-once",
		       GlobalRuleApplyPolicy.EffectiveFactor(1.15, 3, false) == 1.15);
		// per-status on: the factor is applied once per matching status, as a REPEATED PRODUCT
		r.True("with-per-status-three-statuses-apply-three-times",
		       GlobalRuleApplyPolicy.EffectiveFactor(1.15, 3, true) == 1.15 * 1.15 * 1.15);
		// ... and Math.Pow would differ by one ulp, so the loop is not an implementation detail
		r.True("the-repeated-product-is-not-a-pow",
		       GlobalRuleApplyPolicy.EffectiveFactor(1.15, 3, true) != Math.Pow(1.15, 3));
		r.True("the-measured-1.15-cubed-value-is-1.520875",
		       Math.Abs(GlobalRuleApplyPolicy.EffectiveFactor(1.15, 3, true) - 1.520875) < 1e-12);
		r.True("a-two-copy-product-matches-the-loop",
		       GlobalRuleApplyPolicy.EffectiveFactor(1.1, 2, true) == 1.1 * 1.1);
	}
}
