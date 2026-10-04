namespace DpsMeter;

/// <summary>
/// RF4, second family, apply side: the ARITHMETIC of a battle-wide rule on one hit.
///
/// What is here and what is deliberately NOT. The per-hit ladder that decides whether a rule applies
/// (owner-as-attacker, owner object alive, victim team, positions, attribute, status) stays in
/// CompositionProbe.ApplyGlobalDebuffs, because every rung of it performs a LAZY NATIVE READ: the owner
/// team test only happens for enemy-takes rules, the owner position only when the rule names a position,
/// and `GameRef.IsNull` is the documented uncatchable-AccessViolation risk. Reading those eagerly to feed
/// a pure function would add interop calls AND crash surface on the hottest path for no test gain -- the
/// gate is a sequence of native reads whose ORDER is the behaviour (see STATE-LIFETIME-MATRIX section 7).
///
/// What CAN be pure is what the ladder does AFTER the gates: how many times the rule applies, and the
/// factor that results. That is the part with the 1.15^n history, so it is the part worth pinning.
/// </summary>
internal static class GlobalRuleApplyPolicy
{
	/// <summary>The eDamageCalcType values the rule gates test (the source hardcodes these numbers).</summary>
	public const int HitTypePhys = 1;
	public const int HitTypeMagic = 2;
	/// <summary>Both attributes at once, which satisfies BOTH gates.</summary>
	public const int HitTypeBoth = 5;

	public static bool IsMagicHit(int hitType)
	{
		return hitType == HitTypeMagic || hitType == HitTypeBoth;
	}

	public static bool IsPhysHit(int hitType)
	{
		return hitType == HitTypePhys || hitType == HitTypeBoth;
	}

	/// <summary>
	/// How many copies of the factor this rule contributes to one hit.
	///
	/// 1 when the clause has no status condition at all (`tokenCount == 0`): the rule fires once whatever
	/// the victim carries. Otherwise it is the number of the rule's status tokens the victim actually has,
	/// and 0 means "none of them" -- the rule does not fire, and the CALLER must skip it entirely,
	/// including its responsibility recording.
	/// </summary>
	public static int StatusCopies(int tokenCount, int matchedTokens)
	{
		if (tokenCount == 0) return 1;
		return matchedTokens;
	}

	/// <summary>
	/// The factor actually folded: the rule's factor, multiplied by itself <paramref name="copies"/> times
	/// when the clause said それぞれ (per-status) and more than one status matched.
	///
	/// The repetition is a LOOP, not Math.Pow, and that is deliberate: for 1.15^3 the two differ by one ulp
	/// (0x1.855810624dd2d vs 0x1.855810624dd2e, measured), so a Pow would silently move every per-status
	/// residual by a bit and change the exported explanation. The case pins the repeated product exactly.
	/// </summary>
	public static double EffectiveFactor(double factor, int copies, bool perStatus)
	{
		if (copies > 1 && perStatus)
		{
			double acc = 1.0;
			for (int h = 0; h < copies; h++) acc *= factor;
			return acc;
		}
		return factor;
	}
}
