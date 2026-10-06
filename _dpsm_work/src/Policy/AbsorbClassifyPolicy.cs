using System.Globalization;
using System.Text;

namespace DpsMeter;

/// <summary>
/// R75: WHICH mechanism withheld part of a damage-application call -- decided from READINGS, never from
/// naming the difference.
///
/// WHY IT EXISTS. The plugin has published a number called 被吸收/无效化 since 1.5.5, and every round that
/// looked at it could only repeat "500,000 less reached 耐久". That label is the plugin's own word for
/// `nominal - damage`, i.e. for an ARITHMETIC FACT: 46 exports showed 397 such records, 395 of them exactly
/// 500,000 and all of them on ショゴス, while the ally-side ones are irregular (2,821 / 19,010 / 56,087).
/// Naming a mechanism from a difference is exactly how a shield gets invented, and no ability, buff, status
/// or master-data field was ever found carrying the 500,000. So this classifier takes the readings (the
/// victim's life, its barrier's life, the invincibility family flags, and which carrier hooks fired) and is
/// allowed -- required -- to answer "CarrierUnknown".
///
/// THE ONE THING IT MUST NOT DO is turn a failed read into a carrier. `int.MinValue` means "could not read
/// it" and is classified <see cref="AbsorbVerdict.Unreadable"/>, because "the barrier absorbed it" and "we
/// could not look" must stay different answers. Pure by construction (no Unity, no IL2CPP, no Plugin), so
/// the decision table and the `[ABSPROBE]` text are executed by the behaviour suite instead of being argued
/// about after a battle.
/// </summary>
internal enum AbsorbVerdict
{
	/// <summary>Nothing was withheld: `nominal == result` (or neither is positive).</summary>
	None,

	/// <summary>A barrier was active (or its own Damage ran) AND its life moved by at least what was
	/// withheld. This is the only verdict that names a carrier from a reading of that carrier.</summary>
	Barrier,

	/// <summary>A barrier moved but by LESS than what was withheld: the pool ran out inside this hit, or two
	/// mechanisms are in play at once. Kept apart from <see cref="Barrier"/> on purpose -- a partial pool is
	/// the one shape that distinguishes "a pool of N" from "N per hit".</summary>
	BarrierShort,

	/// <summary>Something withheld damage and not one candidate carrier was observed. A CONSTANT value in
	/// this bucket (e.g. 500,000 on every hit) is the evidence for a flat per-hit cut that no exported table
	/// carries -- it is NOT evidence for a shield.</summary>
	CarrierUnknown,

	/// <summary>`BattleObject.DamageTakeOver` ran for this hit.</summary>
	TakeOver,

	/// <summary>`BattleObject.TryGetFixedDamage` answered for this hit.</summary>
	FixedDamage,

	/// <summary>An invincibility-family flag was set on the victim.</summary>
	Invincible,

	/// <summary>`result &lt;= 0` while `nominal &gt; 0`: the whole hit was withheld -- and
	/// `Hooks/BattleObjectHooks.cs`' fallback `(__result &gt; 0) ? __result : __0` then books it as FULL
	/// damage with `absorbed = 0`, so the existing `[ABSORB]` line cannot show it at all. This verdict is
	/// what makes that blind spot countable.</summary>
	Masked,

	/// <summary>A reading the verdict depends on failed. Never reported as a carrier.</summary>
	Unreadable,
}

/// <summary>
/// One damage-application call as the probe read it. `Nominal` / `Result` are the two numbers the existing
/// hook already has (`__0` and `__result`); everything else is a reading taken around that call.
///
/// `BarrierLifeBefore/After` carry `int.MinValue` when the barrier could not be read, and
/// `BarrierReadable` distinguishes that from a real 0. The polarity of the barrier's life is NOT established
/// yet (a remaining-absorb pool would fall, an absorbed-so-far counter would rise), so the classifier
/// compares MAGNITUDES there and never the sign.
/// </summary>
internal struct AbsorbObservation
{
	/// <summary>`BattleObject.Damage`'s argument: the damage the game accounts for.</summary>
	internal int Nominal;

	/// <summary>`BattleObject.Damage`'s return: what actually reached 耐久.</summary>
	internal int Result;

	internal int LifeBefore;
	internal int LifeAfter;
	internal bool LifeReadable;

	internal int BarrierLifeBefore;
	internal int BarrierLifeAfter;
	internal bool BarrierReadable;
	internal bool BarrierActiveBefore;

	/// <summary>`Barrier.Damage` ran between the prefix and the postfix of this call.</summary>
	internal bool BarrierDamageSeen;

	internal bool TakeOverSeen;
	internal bool FixedDamageSeen;
	internal bool InvincibleFlag;

	/// <summary>How much of the call never reached 耐久. Meaningful only for a call that was applied at all;
	/// <see cref="AbsorbVerdict.Masked"/> must never be folded into this number.</summary>
	internal int Withheld()
	{
		return Nominal - Result;
	}

	/// <summary>The victim's own life movement, or `int.MinValue` when it could not be read.</summary>
	internal int LifeDrop()
	{
		return LifeReadable ? (LifeBefore - LifeAfter) : int.MinValue;
	}
}

/// <summary>
/// The decision half. Order is the whole argument, so it is spelled out:
///
///   1. <see cref="AbsorbVerdict.Masked"/> FIRST. A call that was entirely withheld arrives as
///      `result == 0`, which the existing accounting books as full damage -- so it is the one case that must
///      not be reachable as `None`.
///   2. The named carriers, each only when its own hook was OBSERVED during this call.
///   3. The barrier, and only when its own life actually moved: active-but-not-moving is
///      <see cref="AbsorbVerdict.Unreadable"/>, not a carrier.
///   4. Otherwise <see cref="AbsorbVerdict.CarrierUnknown"/>: an honest "something did it and we did not see
///      what", which is what the next battle's data has to resolve.
/// </summary>
internal static class AbsorbClassifyPolicy
{
	internal static AbsorbVerdict Classify(AbsorbObservation o)
	{
		if (o.Nominal > 0 && o.Result <= 0)
		{
			return AbsorbVerdict.Masked;
		}

		int withheld = o.Withheld();
		if (withheld <= 0)
		{
			return AbsorbVerdict.None;
		}

		if (o.FixedDamageSeen) return AbsorbVerdict.FixedDamage;
		if (o.InvincibleFlag) return AbsorbVerdict.Invincible;
		if (o.TakeOverSeen) return AbsorbVerdict.TakeOver;

		if (o.BarrierActiveBefore || o.BarrierDamageSeen)
		{
			if (!o.BarrierReadable)
			{
				return AbsorbVerdict.Unreadable;
			}
			long moved = (long)o.BarrierLifeBefore - o.BarrierLifeAfter;
			if (moved < 0L) moved = -moved;
			if (moved >= withheld) return AbsorbVerdict.Barrier;
			if (moved > 0L) return AbsorbVerdict.BarrierShort;
			return AbsorbVerdict.Unreadable;
		}

		return AbsorbVerdict.CarrierUnknown;
	}

	/// <summary>The verdict as it is printed, so a log line and a case label cannot drift apart.</summary>
	internal static string Name(AbsorbVerdict v)
	{
		switch (v)
		{
			case AbsorbVerdict.None: return "none";
			case AbsorbVerdict.Barrier: return "barrier";
			case AbsorbVerdict.BarrierShort: return "pool";
			case AbsorbVerdict.CarrierUnknown: return "unknown";
			case AbsorbVerdict.TakeOver: return "takeover";
			case AbsorbVerdict.FixedDamage: return "fixed";
			case AbsorbVerdict.Invincible: return "invincible";
			case AbsorbVerdict.Masked: return "masked";
			case AbsorbVerdict.Unreadable: return "unreadable";
		}
		return "?";
	}
}

/// <summary>
/// The counting half: one bucket per value, one `[ABSPROBE] sum` line per battle, so two battles can be
/// compared by eye and "the probe saw nothing" is a different line from "the probe never ran".
///
/// The `first(nom/res/life/bar)` quad is the single most informative thing a battle can produce here: it is
/// the FIRST withheld call's four numbers, taken while the mechanism is certainly still in its first state,
/// and it settles from one battle whether the withheld amount equals the victim's own life movement and
/// whether the barrier's life moved at all. Unreadable entries print as `?`, never as 0.
/// </summary>
internal sealed class AbsorbProbeReport
{
	/// <summary>Every damage-application call the probe observed (withheld or not).</summary>
	internal int Calls;

	/// <summary>Calls that withheld something and were applied (`result > 0`).</summary>
	internal int Withheld;
	internal long WithheldTotal;

	/// <summary>Calls with `result &lt;= 0` while `nominal &gt; 0` -- the ones the existing accounting shows
	/// as full damage. Their sum is the size of that blind spot.</summary>
	internal int Masked;
	internal long MaskedTotal;

	/// <summary>Calls whose victim life moved by something other than the return value: the reading that
	/// decides whether the return really is "damage that reached 耐久".</summary>
	internal int LifeMismatch;

	/// <summary>Calls where the victim's barrier was active before the hit.</summary>
	internal int BarrierActive;

	/// <summary>Calls where the barrier existed but its life could not be read.</summary>
	internal int BarrierUnreadable;

	internal int BarrierDamageSeen;
	internal int AddBarrierSeen;
	internal int TakeOverSeen;
	internal int FixedDamageSeen;

	internal int SolvedBarrier;
	internal int SolvedBarrierShort;
	internal int SolvedUnknown;
	internal int SolvedTakeOver;
	internal int SolvedFixed;
	internal int SolvedInvincible;
	internal int SolvedUnreadable;

	internal bool HasFirst;
	internal int FirstNominal;
	internal int FirstResult;

	/// <summary>Seeded to `int.MinValue` at DECLARATION as well as in <see cref="Clear"/>, because a report
	/// that was never cleared and one that was must print the same line: the suite pins both, and the first
	/// version of this class printed `0/0` for a fresh instance and `?/?` for a cleared one -- the same state,
	/// two answers.</summary>
	internal int FirstLifeDrop = int.MinValue;
	internal int FirstBarrierMove = int.MinValue;

	/// <summary>Lines actually written, and the ones the row cap refused.</summary>
	internal int Rows;
	internal int Dropped;

	internal void Clear()
	{
		Calls = 0;
		Withheld = 0;
		WithheldTotal = 0L;
		Masked = 0;
		MaskedTotal = 0L;
		LifeMismatch = 0;
		BarrierActive = 0;
		BarrierUnreadable = 0;
		BarrierDamageSeen = 0;
		AddBarrierSeen = 0;
		TakeOverSeen = 0;
		FixedDamageSeen = 0;
		SolvedBarrier = 0;
		SolvedBarrierShort = 0;
		SolvedUnknown = 0;
		SolvedTakeOver = 0;
		SolvedFixed = 0;
		SolvedInvincible = 0;
		SolvedUnreadable = 0;
		HasFirst = false;
		FirstNominal = 0;
		FirstResult = 0;
		FirstLifeDrop = int.MinValue;
		FirstBarrierMove = int.MinValue;
		Rows = 0;
		Dropped = 0;
	}

	/// <summary>Record one classified call. `isInteresting` is decided by the caller (the probe), because
	/// what deserves a log line is a logging policy, not a verdict.</summary>
	internal void Note(AbsorbObservation o, AbsorbVerdict v)
	{
		Calls++;

		int withheld = o.Withheld();
		if (v == AbsorbVerdict.Masked)
		{
			Masked++;
			MaskedTotal += o.Nominal;
		}
		else if (withheld > 0)
		{
			Withheld++;
			WithheldTotal += withheld;
		}

		if (o.LifeReadable && o.Result > 0 && o.LifeDrop() != o.Result)
		{
			LifeMismatch++;
		}
		if (o.BarrierActiveBefore) BarrierActive++;
		if (o.BarrierActiveBefore && !o.BarrierReadable) BarrierUnreadable++;

		switch (v)
		{
			case AbsorbVerdict.Barrier: SolvedBarrier++; break;
			case AbsorbVerdict.BarrierShort: SolvedBarrierShort++; break;
			case AbsorbVerdict.CarrierUnknown: SolvedUnknown++; break;
			case AbsorbVerdict.TakeOver: SolvedTakeOver++; break;
			case AbsorbVerdict.FixedDamage: SolvedFixed++; break;
			case AbsorbVerdict.Invincible: SolvedInvincible++; break;
			case AbsorbVerdict.Unreadable: SolvedUnreadable++; break;
		}

		if ((v != AbsorbVerdict.None && v != AbsorbVerdict.Masked) && !HasFirst)
		{
			HasFirst = true;
			FirstNominal = o.Nominal;
			FirstResult = o.Result;
			FirstLifeDrop = o.LifeDrop();
			FirstBarrierMove = o.BarrierReadable
				? (int)((long)o.BarrierLifeBefore - o.BarrierLifeAfter)
				: int.MinValue;
		}
	}

	/// <summary>Counters fed by the carrier hooks themselves (they fire outside the classifier).</summary>
	internal void NoteBarrierDamage() { BarrierDamageSeen++; }
	internal void NoteAddBarrier() { AddBarrierSeen++; }
	internal void NoteTakeOver() { TakeOverSeen++; }
	internal void NoteFixedDamage() { FixedDamageSeen++; }

	internal void NoteRow() { Rows++; }
	internal void NoteDropped() { Dropped++; }

	/// <summary>One ASCII line, every bucket named, unreadable printed as `?`. The shape is pinned by the
	/// behaviour suite because the whole point is comparing two battles by eye.</summary>
	internal string Describe()
	{
		var sb = new StringBuilder(320);
		sb.Append("calls=").Append(Calls.ToString(CultureInfo.InvariantCulture));
		sb.Append(" withheld=").Append(Withheld.ToString(CultureInfo.InvariantCulture));
		sb.Append(" sum=").Append(WithheldTotal.ToString(CultureInfo.InvariantCulture));
		sb.Append(" masked=").Append(Masked.ToString(CultureInfo.InvariantCulture))
		  .Append('/').Append(MaskedTotal.ToString(CultureInfo.InvariantCulture));
		sb.Append(" lifeMismatch=").Append(LifeMismatch.ToString(CultureInfo.InvariantCulture));
		sb.Append(" verdict(barrier/pool/unknown/takeover/fixed/invincible/unreadable)=")
		  .Append(SolvedBarrier.ToString(CultureInfo.InvariantCulture)).Append('/')
		  .Append(SolvedBarrierShort.ToString(CultureInfo.InvariantCulture)).Append('/')
		  .Append(SolvedUnknown.ToString(CultureInfo.InvariantCulture)).Append('/')
		  .Append(SolvedTakeOver.ToString(CultureInfo.InvariantCulture)).Append('/')
		  .Append(SolvedFixed.ToString(CultureInfo.InvariantCulture)).Append('/')
		  .Append(SolvedInvincible.ToString(CultureInfo.InvariantCulture)).Append('/')
		  .Append(SolvedUnreadable.ToString(CultureInfo.InvariantCulture));
		sb.Append(" seen(barrierDmg/addBarrier/takeover/fixed)=")
		  .Append(BarrierDamageSeen.ToString(CultureInfo.InvariantCulture)).Append('/')
		  .Append(AddBarrierSeen.ToString(CultureInfo.InvariantCulture)).Append('/')
		  .Append(TakeOverSeen.ToString(CultureInfo.InvariantCulture)).Append('/')
		  .Append(FixedDamageSeen.ToString(CultureInfo.InvariantCulture));
		sb.Append(" active=").Append(BarrierActive.ToString(CultureInfo.InvariantCulture));
		sb.Append(" unreadable=").Append(BarrierUnreadable.ToString(CultureInfo.InvariantCulture));
		sb.Append(" first(nom/res/life/bar)=").Append(Num(FirstNominal)).Append('/')
		  .Append(Num(FirstResult)).Append('/').Append(Num(FirstLifeDrop)).Append('/')
		  .Append(Num(FirstBarrierMove));
		sb.Append(" rows=").Append(Rows.ToString(CultureInfo.InvariantCulture));
		sb.Append(" dropped=").Append(Dropped.ToString(CultureInfo.InvariantCulture));
		return sb.ToString();
	}

	private static string Num(int v)
	{
		return (v == int.MinValue) ? "?" : v.ToString(CultureInfo.InvariantCulture);
	}
}
