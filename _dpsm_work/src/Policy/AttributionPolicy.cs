namespace DpsMeter;

/// <summary>How a composition was paired with a hit. The STRING is what the export and the UI carry, so
/// the mapping lives here with the decision instead of at each of the four assignment sites.</summary>
internal enum PairKind
{
	None = 0,
	/// <summary>Stage 0 with the live calc targeting this victim (the closest to an exact pairing).</summary>
	LiveSame = 1,
	/// <summary>Stage 0 by age only: a calc was running, the target could not be confirmed.</summary>
	LiveAge = 2,
	/// <summary>Stage 1: a recorded calc for this victim whose damage equals this hit.</summary>
	ExactValue = 3,
	/// <summary>Stage 2: oldest unused calc for this victim (time order only, NOT corroborated).</summary>
	Fifo = 4,
}

/// <summary>Which recorded calc a damage event may inherit as "the calc that caused it".</summary>
internal enum CalcSourceKind { None = 0, Attacker = 1, Owner = 2 }

/// <summary>
/// RF3: the attribution DECISIONS as pure functions -- the pairing windows, the live-pair admission rule,
/// the damage-match predicate, the pairing labels and the calc-source priority.
///
/// Why. These numbers were literals scattered through Aggregator.Attribution / Aggregator.Stats, and the
/// "same age window as the composition pairing" claim was a comment rather than one definition: the
/// -0.05..0.20 pair existed in two files, so the two could drift apart without anything failing. The
/// candidate SCAN stays in the facade (it holds the native objects, the Used marking and the FIFO/
/// newest-first order); this file owns which window, which predicate, which label.
///</summary>
internal static class AttributionPolicy
{
	/// <summary>Stage 0 window for "the calc executing right now explains this hit". Bounded on the
	/// negative side because the damage hook can resolve a fraction BEFORE the calc activity note.</summary>
	public const double LivePairMinAge = -0.05;
	public const double LivePairMaxAge = 0.20;

	/// <summary>Within this age the live calc is accepted even without a confirmed target: the same frame
	/// cannot have started an unrelated cast.</summary>
	public const double LiveBlindAge = 0.08;

	/// <summary>Stage 1 (backward scan, newest first): stop looking past this age.</summary>
	public const double ExactValueWindow = 0.80;

	/// <summary>Stage 2 (forward scan, oldest first): skip anything older than this.</summary>
	public const double FifoWindow = 0.60;

	/// <summary>How far back a recorded calc may still donate its attacker/owner to a damage event.</summary>
	public const double CalcSourceWindow = 0.45;

	/// <summary>Is the live calc inside the accepted age window? Two strict comparisons, so an age
	/// exactly at either bound is accepted.</summary>
	public static bool LiveAgeEligible(double age, double minAge, double maxAge)
	{
		return age >= minAge && age <= maxAge;
	}

	/// <summary>A confirmed target makes the age fallback unnecessary; otherwise the calc must be young
	/// enough that no other cast could have produced this hit.</summary>
	public static bool LiveTargetAdmitted(double age, bool sameTarget, double blindAge)
	{
		return sameTarget || age <= blindAge;
	}

	/// <summary>
	/// Does a recorded figure match this hit? The pairing key has to accept BattleObject.Damage's ARGUMENT
	/// as well as its return value: the return is the damage left after 被吸收/无效化, so for an absorbed
	/// hit it can never equal the calc's own value (measured 2026-09-27: calc 421,140 -> 198 applied).
	/// A nominal of 0 means "unknown", never "match 0".
	/// </summary>
	public static bool DmgMatches(int candidateDamage, int damage, int nominal)
	{
		return candidateDamage == damage || (nominal > 0 && candidateDamage == nominal);
	}

	/// <summary>
	/// R62 (A): a damage-detail record whose hit type DISAGREES with the composition that describes this
	/// hit did not come from this hit.
	///
	/// WHY this is a decision and not a hunch. The record channel pairs on (attacker, target) inside a
	/// 0.35 s window with no value match (hitMatch = 2), and ONE hit can leave SEVERAL pending figures
	/// behind it: the DamageAction / ActDamageAction pair plus the 被吸收 accounting call. Measured
	/// 2026-10-05 on the two exports that exposed it (B-...D7F-009 factId 127, D7F-010 factId 173): a 10%
	/// 貫通 extra hit consumed the PREVIOUS hit's post-absorption figure, so `source` said Unknown,
	/// `calcHitType` said 魔法 and `calcEffectId` carried the main skill's id — while the composition for
	/// the same event reproduced the damage exactly (theory == applied). hitType is the one field both
	/// channels read from the same game member (DamageCalculater.m_hitType), so when they disagree at least
	/// one of the two is about a different hit; the composition is the one to believe, because it is
	/// either the calc executing right now for exactly this victim (pairTrusted) or corroborated by the
	/// damage value.
	///
	/// What is NOT claimed: this is not a value check. The record's damage legitimately differs from the
	/// applied damage (it is the calc's own figure, pre-mitigation), so a value mismatch is not evidence.
	/// Only the hit type is comparable, and only when both sides could read it.
	/// </summary>
	public static bool RecordContradictsComposition(int recordHitType, int compHitType, bool compValid, bool compTrusted)
	{
		if (!compValid || !compTrusted) return false;
		if (recordHitType < 0 || compHitType < 0) return false;
		return recordHitType != compHitType;
	}

	/// <summary>R62 (A): the exported `hitMatch` reason code for a figure that WAS consumed and then
	/// discarded because the composition contradicted it. Distinct from 0 (nothing was available): here
	/// the export must be able to say "a figure existed and was rejected", because the two have different
	/// fixes.</summary>
	public const int HitMatchRejected = 3;

	/// <summary>Reason code for a stage-0 pairing (the caller knows whether the target matched).</summary>
	public static PairKind LivePairKind(bool sameTarget)
	{
		return sameTarget ? PairKind.LiveSame : PairKind.LiveAge;
	}

	/// <summary>The export/UI string for a pairing reason code. "" for "no pairing".</summary>
	public static string PairLabel(PairKind kind)
	{
		switch (kind)
		{
			case PairKind.LiveSame: return "live-same";
			case PairKind.LiveAge: return "live-age";
			case PairKind.ExactValue: return "value";
			case PairKind.Fifo: return "fifo";
			default: return "";
		}
	}

	/// <summary>Prefer the explicit attacker, then the summon owner; both missing is "unknown".</summary>
	public static CalcSourceKind CalcSourcePriority(bool hasAttacker, bool hasOwner)
	{
		if (hasAttacker) return CalcSourceKind.Attacker;
		if (hasOwner) return CalcSourceKind.Owner;
		return CalcSourceKind.None;
	}

	/// <summary>The [DMG] log's src= tag for a calc-source decision.</summary>
	public static string CalcSourceTag(CalcSourceKind kind)
	{
		switch (kind)
		{
			case CalcSourceKind.Attacker: return "calcA";
			case CalcSourceKind.Owner: return "calcO";
			default: return "calc?";
		}
	}
}
