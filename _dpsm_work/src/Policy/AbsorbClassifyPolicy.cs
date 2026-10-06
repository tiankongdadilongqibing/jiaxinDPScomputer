using System.Globalization;
using System.Text;

namespace DpsMeter;

/// <summary>
/// R76: what a damage-application call actually did, judged from the two numbers AND the victim's own life
/// movement -- with OVERSIZED hits (the hit that exceeded the victim's remaining Life) as their own family.
///
/// WHY THE JUDGEMENT HAD TO CHANGE. R75 shipped a classifier whose buckets were built from the guess that
/// `nominal - result` is an "absorbed" amount. The first battle the probe ran in killed that guess:
///
///   `res == max(0, nominal - lifeBefore)`   --  798 of 798 readable readings, zero violations.
///
/// The game's return value is therefore the OVERFLOW (the part of the hit that exceeded the victim's
/// remaining Life), not the damage that landed. `Hooks/BattleObjectHooks.cs` books `(__result > 0) ? __result
/// : __0` as 入耐久 and `nominal - that` as 被吸收, so on an oversized hit the two published fields are
/// exactly SWAPPED: the overflow is published as the damage taken, and the damage that actually landed
/// (= the victim's remaining Life) is published as "absorbed".
///
/// The measurements behind this file (two battles, quest 9999 + 411001):
///   * 21 oversized hits, every one of them satisfying `nominal - res == lifeBefore` exactly;
///   * the `ショゴス` objects: 396 of 397 readings show `Life == 500,000` and `lifeDrop == 0`, and 397/397
///     carry an invincibility-family flag -- so their "被吸收 500000" is that object's LIFE, repeated once per
///     hit whose nominal exceeded it, and not an absorb ability (their own talent list is `1002 ModeChange` +
///     `6 攻击力/150/-1` and nothing else);
///   * `res == 0` with `lifeDrop == nominal`: the whole hit landed. The R75 bucket called that `Masked`
///     ("entirely withheld"), which is why 790 rows were filed under a name that meant the opposite.
///
/// THE RULE THAT REPLACES IT: the LIFE READING decides. `lifeDrop == nominal` means nothing was withheld,
/// full stop. `res &gt; 0` means the hit was oversized, and then landed = `nominal - res`, overflow = `res`.
/// A reading that fails never becomes a carrier and never becomes "nothing withheld": it gets its own
/// verdict, because "we could not look" is an answer.
/// </summary>
internal enum AbsorbVerdict
{
	/// <summary>Nothing withheld: the return reports no overflow and the life reading agrees that the whole
	/// nominal landed (`lifeDrop == nominal`), or there was nothing to judge.</summary>
	None,

	/// <summary>Oversized, and CORROBORATED: `res &gt; 0` and the victim's life moved by exactly
	/// `nominal - res`. landed = `nominal - res`, overflow = `res`.</summary>
	Oversized,

	/// <summary>Oversized (`res &gt; 0`) but the victim's life did NOT move at all: a fixed-pool or
	/// invincible object (the `ショゴス` case -- `Life` stays 500,000). Kept apart from
	/// <see cref="Oversized"/> because the life reading cannot corroborate the split here, so this is a
	/// labelled number, not a measured one.</summary>
	OversizedPool,

	/// <summary>Oversized (`res &gt; 0`) and the life moved, but by something other than `nominal - res`.
	/// Either the object was recycled mid-call or a second mechanism is in play: reported rather than
	/// smoothed over.</summary>
	OversizedPartial,

	/// <summary>Oversized (`res &gt; 0`) with an unreadable life: the split cannot be corroborated at all.</summary>
	OversizedUnreadable,

	/// <summary>`res &lt;= 0` (the return reports no overflow) but the victim's life did not move either, on
	/// a positive nominal: nothing observed landed. This is the `ショゴス` population's normal shape.</summary>
	NoLifeMovement,

	/// <summary>`res &lt;= 0` and the life moved by MORE than nothing but LESS than the nominal: part of the
	/// hit went missing and the return value does not say so. This -- not `res &lt;= 0` -- is the shape that
	/// would actually justify the word 被吸收.</summary>
	WithheldNoReturn,

	/// <summary>`res &lt;= 0` with an unreadable life: undecidable. Never reported as <see cref="None"/>
	/// (which would claim the hit landed) and never as an oversized hit.</summary>
	LifeUnreadable,

	/// <summary>A barrier was active (or its own Damage ran) and its life moved by at least the difference on
	/// a hit the life law does NOT explain. The only verdict that names a carrier from a reading of that
	/// carrier.</summary>
	Barrier,

	/// <summary>A barrier moved, but by less than the difference: the pool ran out inside this hit.</summary>
	BarrierShort,

	/// <summary>`BattleObject.DamageTakeOver` ran for this hit.</summary>
	TakeOver,

	/// <summary>`BattleObject.TryGetFixedDamage` answered for this hit.</summary>
	FixedDamage,

	/// <summary>An invincibility-family flag was set on the victim (the OR of the five flags the probe
	/// reads).</summary>
	Invincible,

	/// <summary>A reading the verdict depends on failed. Never reported as a carrier, never as `None`.</summary>
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

	/// <summary>`BattleObject.Damage`'s return: MEASURED to be the OVERFLOW beyond the victim's remaining
	/// Life (`res == max(0, nominal - lifeBefore)`, 798/798 readable readings), i.e. the part of the hit that
	/// did NOT stay with the victim.</summary>
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

	/// <summary>`true` when the return value reports an overflow, i.e. the hit exceeded the victim's
	/// remaining Life. This is R76's definition of an oversized hit, and it is the ONE definition: every
	/// verdict in the oversized family requires it.</summary>
	internal bool IsOversized()
	{
		return Result > 0 && Nominal > Result;
	}

	/// <summary>The part of the hit that stayed with the victim: `nominal - res`. Only meaningful for an
	/// oversized call. NOTE for anyone reading the export next to this: the published `入耐久` is `res`
	/// (the overflow) and the published 被吸收/无效化 is this number, so on these calls the two are swapped.
	/// </summary>
	internal int Landed()
	{
		return Nominal - Result;
	}

	/// <summary>The overflow itself (`res`): the part of the hit that exceeded the victim's remaining Life.
	/// </summary>
	internal int Overflow()
	{
		return Result;
	}

	/// <summary>The victim's own life movement, or `int.MinValue` when it could not be read.</summary>
	internal int LifeDrop()
	{
		return LifeReadable ? (LifeBefore - LifeAfter) : int.MinValue;
	}
}

/// <summary>
/// The decision half. The order is the whole argument, so it is spelled out:
///
///   1. NOTHING TO JUDGE (`nominal &lt;= 0`, or the return reports no overflow) -&gt; the life law decides,
///      and `lifeDrop == nominal` means <see cref="AbsorbVerdict.None"/>. This single line is what removes
///      R75's false `Masked` bucket (790 rows whose life moved by exactly the nominal).
///   2. OVERSIZED (`res &gt; 0`): the split is `landed = nominal - res`, `overflow = res`. It is called
///      <see cref="AbsorbVerdict.Oversized"/> only when the victim's life moved by exactly that; a pool that
///      did not move gets <see cref="AbsorbVerdict.OversizedPool"/>, a different movement
///      <see cref="AbsorbVerdict.OversizedPartial"/>, an unreadable life
///      <see cref="AbsorbVerdict.OversizedUnreadable"/>.
///   3. A difference the life law does NOT explain may have a carrier behind it, so only there are the
///      carrier readings consulted (fixed damage, takeover, invincibility, barrier -- in that order, each
///      only when its own hook fired).
///   4. `res &lt;= 0` with a life that moved by part of the nominal is
///      <see cref="AbsorbVerdict.WithheldNoReturn"/> -- the only shape that would justify the word 被吸收;
///      an unreadable life is <see cref="AbsorbVerdict.LifeUnreadable"/>, and a life that did not move is
///      <see cref="AbsorbVerdict.NoLifeMovement"/>.
/// </summary>
internal static class AbsorbClassifyPolicy
{
	internal static AbsorbVerdict Classify(AbsorbObservation o)
	{
		if (o.Nominal <= 0)
		{
			return AbsorbVerdict.None;
		}

		if (o.Result > 0)
		{
			int landed = o.Landed();
			if (landed <= 0)
			{
				// The return is not an overflow at all (it equals or exceeds the nominal): nothing withheld.
				return AbsorbVerdict.None;
			}

			if (!o.LifeReadable) return AbsorbVerdict.OversizedUnreadable;

			int lifeDrop = o.LifeDrop();
			if (lifeDrop == landed) return AbsorbVerdict.Oversized;
			if (lifeDrop == 0) return AbsorbVerdict.OversizedPool;

			AbsorbVerdict carrier = ClassifyCarrier(o, landed);
			return (carrier != AbsorbVerdict.None) ? carrier : AbsorbVerdict.OversizedPartial;
		}

		if (!o.LifeReadable) return AbsorbVerdict.LifeUnreadable;

		int drop = o.LifeDrop();
		if (drop == o.Nominal) return AbsorbVerdict.None;
		if (drop == 0) return AbsorbVerdict.NoLifeMovement;
		if (drop > 0 && drop < o.Nominal) return AbsorbVerdict.WithheldNoReturn;

		// The life rose, or moved in a way this policy has no reading for: not a withholding.
		return AbsorbVerdict.None;
	}

	/// <summary>
	/// The carrier readings, consulted ONLY for a difference the life law does not explain. Order is pinned
	/// because it is the difference between two diagnostics: a fixed-damage override and a damage takeover
	/// both explain a shortfall, and inventing one when nothing fired is what R75's `Masked` bucket did.
	/// </summary>
	private static AbsorbVerdict ClassifyCarrier(AbsorbObservation o, int withheld)
	{
		if (o.FixedDamageSeen) return AbsorbVerdict.FixedDamage;
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

		if (o.InvincibleFlag) return AbsorbVerdict.Invincible;

		// Nothing was observed. R75 answered this with a `CarrierUnknown` bucket, which under the measured law
		// is unreachable for an oversized hit: the shortfall IS the overflow, so the honest fallback is
		// `OversizedPartial` -- the life reading disagrees with the split -- and not a mechanism nobody saw.
		return AbsorbVerdict.None;
	}

	/// <summary>The verdict as it is printed, so a log line and a case label cannot drift apart.</summary>
	internal static string Name(AbsorbVerdict v)
	{
		switch (v)
		{
			case AbsorbVerdict.None: return "none";
			case AbsorbVerdict.Oversized: return "oversized";
			case AbsorbVerdict.OversizedPool: return "oversizedPool";
			case AbsorbVerdict.OversizedPartial: return "oversizedPartial";
			case AbsorbVerdict.OversizedUnreadable: return "oversizedUnreadable";
			case AbsorbVerdict.NoLifeMovement: return "noLifeMovement";
			case AbsorbVerdict.WithheldNoReturn: return "withheldNoReturn";
			case AbsorbVerdict.LifeUnreadable: return "lifeUnreadable";
			case AbsorbVerdict.Barrier: return "barrier";
			case AbsorbVerdict.BarrierShort: return "pool";
			case AbsorbVerdict.TakeOver: return "takeover";
			case AbsorbVerdict.FixedDamage: return "fixed";
			case AbsorbVerdict.Invincible: return "invincible";
			case AbsorbVerdict.Unreadable: return "unreadable";
		}
		return "?";
	}
}

/// <summary>
/// The counting half: one bucket per value, one `[ABSPROBE] sum` line per battle, plus the ROW BUDGET that
/// makes requirement 2 of R76 work.
///
/// WHY THE BUDGET IS A POLICY AND NOT A PROBE DETAIL. In the measured battle the 400-row cap pushed all 11
/// oversized `ショゴス` hits into `dropped=5157`, i.e. the deciding rows were the ones thrown away -- the
/// evidence existed only as an aggregate. The fix is a two-tier budget: an oversized (or carrier) row is
/// written even after the ordinary cap is full, bounded by its own cap so a pathological battle still cannot
/// flood the log, and both refusals are counted separately. It lives here, in the pure class, so a mutation
/// can redden the case that pins it.
///
/// R77: A COUNT AND ITS AMOUNTS MUST COME FROM THE SAME POPULATION, AND NO CALL MAY GO UNCOUNTED. The first
/// battle the R76 line ran in (quest 411001, 2026-10-07 01:48) printed `ovz=0/6500000/2358285`: the count
/// came from the corroborated bucket alone while the amounts summed all four oversized buckets, so a battle
/// with ZERO corroborated hits showed a zero standing next to six and a half million -- the label
/// contradicted its own number. The same line printed 5,489 calls against 5,470 counted buckets, and the 19
/// unaccounted ones (`None`, with four different paths behind them) were readable only by SUBTRACTION. So
/// `ovz` now prints the corroborated triple, `ovzAll` the all-bucket triple, every family keeps its own
/// count, and `None` is counted like the rest with its measured subset `full` broken out.
/// </summary>
internal sealed class AbsorbProbeReport
{
	/// <summary>Every damage-application call the probe observed.</summary>
	internal int Calls;

	/// <summary>Every call the policy answered <see cref="AbsorbVerdict.None"/> for. R77 added this because
	/// the first battle the R76 line ran in printed 5,489 calls against 5,470 counted buckets: the remaining
	/// 19 were `None`, and with no counter of their own the only way to find them was SUBTRACTION. `None` has
	/// four paths, so <see cref="FullLanded"/> keeps the measured one apart from the other three.</summary>
	internal int None;

	/// <summary>The `None` calls the life reading itself measured as an application of the WHOLE nominal:
	/// positive nominal, no overflow reported, readable life, `lifeDrop == nominal`. That is the classifier's
	/// `drop == o.Nominal` rule -- the one that deleted R75's 790 false withheld rows -- counted instead of
	/// only classified. The other three `None` paths (nothing to judge, a return that is not an overflow, a
	/// life that rose) must never land here, or the count drifts back to "somewhere inside `None`".</summary>
	internal int FullLanded;

	/// <summary>Oversized calls whose life movement CORROBORATED the split. Its count and the two sums below
	/// are the SAME population, which is why the line prints them as one triple: R76 printed this count beside
	/// amounts summed over all four oversized buckets, so a battle with no corroborated hit read as
	/// `ovz=0/6500000/2358285` -- a zero standing next to six and a half million.</summary>
	internal int Oversized;

	/// <summary>Sum of `nominal - res` and of `res` over the CORROBORATED oversized calls only -- the pair
	/// printed with <see cref="Oversized"/>.</summary>
	internal long OversizedLanded;
	internal long OversizedOverflow;

	internal int OversizedPool;
	internal int OversizedPartial;
	internal int OversizedUnreadable;

	/// <summary>Every oversized call, counted once whatever the life reading sorted it into. Kept as its own
	/// counter rather than re-added from the four buckets, so a bucket that stops being incremented shows up as
	/// `ovzAll != ovz + ovzPool + ovzPartial + ovzUnread` on the line instead of hiding inside an aggregate.</summary>
	internal int OversizedAll;

	/// <summary>Sum of `nominal - res` and of `res` over every oversized call (all four buckets), so the
	/// headline does not depend on how well the life reading corroborated each one.</summary>
	internal long OversizedLandedTotal;
	internal long OversizedOverflowTotal;

	/// <summary>`res &lt;= 0` with a readable life that did not move / moved by part of the nominal.</summary>
	internal int NoLifeMovement;
	internal int WithheldNoReturn;

	/// <summary>`res &lt;= 0` with an unreadable life: counted, never guessed.</summary>
	internal int LifeUnreadable;

	/// <summary>Calls whose victim life moved by something other than `nominal - res`.</summary>
	internal int LifeMismatch;

	internal int BarrierActive;
	internal int BarrierUnreadable;
	internal int BarrierDamageSeen;
	internal int AddBarrierSeen;
	internal int TakeOverSeen;
	internal int FixedDamageSeen;

	internal int SolvedBarrier;
	internal int SolvedBarrierShort;
	internal int SolvedTakeOver;
	internal int SolvedFixed;
	internal int SolvedInvincible;
	internal int SolvedUnreadable;

	/// <summary>The FIRST call whose return reported an overflow: the quad that settles the split from one
	/// battle's log (`nom/res/landed/overflow`).</summary>
	internal bool HasFirst;
	internal int FirstNominal;
	internal int FirstResult;

	/// <summary>Seeded at DECLARATION as well as in <see cref="Clear"/>, because a report that was never
	/// cleared and one that was must print the same line -- the suite pins both, and R75 shipped exactly this
	/// defect once (a fresh instance printed `0/0` where a cleared one printed `?/?`).</summary>
	internal int FirstLanded = int.MinValue;
	internal int FirstOverflow = int.MinValue;

	/// <summary>Rows written (all kinds), key rows written, ordinary refusals, key refusals.</summary>
	internal int Rows;
	internal int KeyRows;
	internal int Dropped;
	internal int KeyDropped;

	internal void Clear()
	{
		Calls = 0;
		None = 0;
		FullLanded = 0;
		Oversized = 0;
		OversizedLanded = 0L;
		OversizedOverflow = 0L;
		OversizedPool = 0;
		OversizedPartial = 0;
		OversizedUnreadable = 0;
		OversizedAll = 0;
		OversizedLandedTotal = 0L;
		OversizedOverflowTotal = 0L;
		NoLifeMovement = 0;
		WithheldNoReturn = 0;
		LifeUnreadable = 0;
		LifeMismatch = 0;
		BarrierActive = 0;
		BarrierUnreadable = 0;
		BarrierDamageSeen = 0;
		AddBarrierSeen = 0;
		TakeOverSeen = 0;
		FixedDamageSeen = 0;
		SolvedBarrier = 0;
		SolvedBarrierShort = 0;
		SolvedTakeOver = 0;
		SolvedFixed = 0;
		SolvedInvincible = 0;
		SolvedUnreadable = 0;
		HasFirst = false;
		FirstNominal = 0;
		FirstResult = 0;
		FirstLanded = int.MinValue;
		FirstOverflow = int.MinValue;
		Rows = 0;
		KeyRows = 0;
		Dropped = 0;
		KeyDropped = 0;
	}

	/// <summary>Record one classified call.</summary>
	internal void Note(AbsorbObservation o, AbsorbVerdict v)
	{
		Calls++;

		if (o.IsOversized())
		{
			OversizedAll++;
			OversizedLandedTotal += o.Landed();
			OversizedOverflowTotal += o.Overflow();
			if (!HasFirst)
			{
				HasFirst = true;
				FirstNominal = o.Nominal;
				FirstResult = o.Result;
				FirstLanded = o.Landed();
				FirstOverflow = o.Overflow();
			}
		}

		switch (v)
		{
			case AbsorbVerdict.None:
				None++;
				if (IsMeasuredFullApplication(o)) FullLanded++;
				break;
			case AbsorbVerdict.Oversized:
				Oversized++;
				OversizedLanded += o.Landed();
				OversizedOverflow += o.Overflow();
				break;
			case AbsorbVerdict.OversizedPool: OversizedPool++; break;
			case AbsorbVerdict.OversizedPartial: OversizedPartial++; break;
			case AbsorbVerdict.OversizedUnreadable: OversizedUnreadable++; break;
			case AbsorbVerdict.NoLifeMovement: NoLifeMovement++; break;
			case AbsorbVerdict.WithheldNoReturn: WithheldNoReturn++; break;
			case AbsorbVerdict.LifeUnreadable: LifeUnreadable++; break;
			case AbsorbVerdict.Barrier: SolvedBarrier++; break;
			case AbsorbVerdict.BarrierShort: SolvedBarrierShort++; break;
			case AbsorbVerdict.TakeOver: SolvedTakeOver++; break;
			case AbsorbVerdict.FixedDamage: SolvedFixed++; break;
			case AbsorbVerdict.Invincible: SolvedInvincible++; break;
			case AbsorbVerdict.Unreadable: SolvedUnreadable++; break;
		}

		if (o.LifeReadable && o.IsOversized() && o.LifeDrop() != o.Landed())
		{
			LifeMismatch++;
		}
		if (o.BarrierActiveBefore) BarrierActive++;
		if (o.BarrierActiveBefore && !o.BarrierReadable) BarrierUnreadable++;
	}

	/// <summary>
	/// `true` when the classifier's `drop == o.Nominal` rule is what decided this call: a positive nominal, no
	/// overflow reported, a readable life, and a movement equal to the whole nominal. Written out in full
	/// rather than as "`v == None` and the life moved" so the other three `None` paths -- nothing to judge, a
	/// return that is not an overflow, a life that rose -- cannot be counted as a whole application. This is
	/// the predicate R77 added to answer "how many hits landed in full" without subtracting buckets.
	/// </summary>
	private static bool IsMeasuredFullApplication(AbsorbObservation o)
	{
		return o.Nominal > 0 && o.Result <= 0 && o.LifeReadable && o.LifeDrop() == o.Nominal;
	}

	/// <summary>Counters fed by the carrier hooks themselves (they fire outside the classifier).</summary>
	internal void NoteBarrierDamage() { BarrierDamageSeen++; }
	internal void NoteAddBarrier() { AddBarrierSeen++; }
	internal void NoteTakeOver() { TakeOverSeen++; }
	internal void NoteFixedDamage() { FixedDamageSeen++; }

	/// <summary>
	/// Rows that must survive the ordinary cap. An oversized hit is the entire object of R76, and a carrier
	/// sighting is the only thing that can name a mechanism, so both are "key"; everything else (a hit that
	/// landed in full, an unmoved life, an undecidable reading) is ordinary and may be capped.
	/// </summary>
	internal static bool IsKeyVerdict(AbsorbVerdict v)
	{
		switch (v)
		{
			case AbsorbVerdict.Oversized:
			case AbsorbVerdict.OversizedPool:
			case AbsorbVerdict.OversizedPartial:
			case AbsorbVerdict.OversizedUnreadable:
			case AbsorbVerdict.Barrier:
			case AbsorbVerdict.BarrierShort:
			case AbsorbVerdict.TakeOver:
			case AbsorbVerdict.FixedDamage:
				return true;
		}
		return false;
	}

	/// <summary>
	/// May this verdict still write a row? Books the write or the refusal, and keeps the two refusals apart:
	/// `dropped` (ordinary rows the ordinary cap refused) and `keyDropped` (key rows the key cap refused).
	/// A key row is checked against its own cap only, so it can never be crowded out by ordinary rows.
	/// </summary>
	internal bool TryTakeRow(AbsorbVerdict v, int maxRows, int maxKeyRows)
	{
		if (IsKeyVerdict(v))
		{
			if (KeyRows >= maxKeyRows) { KeyDropped++; return false; }
			KeyRows++;
			Rows++;
			return true;
		}
		if (Rows >= maxRows) { Dropped++; return false; }
		Rows++;
		return true;
	}

	/// <summary>One ASCII line, every bucket named, unreadable printed as `?`. The shape is pinned by the
	/// behaviour suite because the point of the line is comparing two battles by eye.</summary>
	internal string Describe()
	{
		var sb = new StringBuilder(360);
		sb.Append("calls=").Append(Calls.ToString(CultureInfo.InvariantCulture));
		sb.Append(" ovz=").Append(Oversized.ToString(CultureInfo.InvariantCulture))
		  .Append('/').Append(OversizedLanded.ToString(CultureInfo.InvariantCulture))
		  .Append('/').Append(OversizedOverflow.ToString(CultureInfo.InvariantCulture));
		sb.Append(" ovzAll=").Append(OversizedAll.ToString(CultureInfo.InvariantCulture))
		  .Append('/').Append(OversizedLandedTotal.ToString(CultureInfo.InvariantCulture))
		  .Append('/').Append(OversizedOverflowTotal.ToString(CultureInfo.InvariantCulture));
		sb.Append(" ovzPool=").Append(OversizedPool.ToString(CultureInfo.InvariantCulture));
		sb.Append(" ovzPartial=").Append(OversizedPartial.ToString(CultureInfo.InvariantCulture));
		sb.Append(" ovzUnread=").Append(OversizedUnreadable.ToString(CultureInfo.InvariantCulture));
		sb.Append(" noMove=").Append(NoLifeMovement.ToString(CultureInfo.InvariantCulture));
		sb.Append(" missing=").Append(WithheldNoReturn.ToString(CultureInfo.InvariantCulture));
		sb.Append(" lifeUnread=").Append(LifeUnreadable.ToString(CultureInfo.InvariantCulture));
		sb.Append(" full=").Append(FullLanded.ToString(CultureInfo.InvariantCulture));
		sb.Append(" none=").Append(None.ToString(CultureInfo.InvariantCulture));
		sb.Append(" carrier(barrier/pool/takeover/fixed/invincible/unreadable)=")
		  .Append(SolvedBarrier.ToString(CultureInfo.InvariantCulture)).Append('/')
		  .Append(SolvedBarrierShort.ToString(CultureInfo.InvariantCulture)).Append('/')
		  .Append(SolvedTakeOver.ToString(CultureInfo.InvariantCulture)).Append('/')
		  .Append(SolvedFixed.ToString(CultureInfo.InvariantCulture)).Append('/')
		  .Append(SolvedInvincible.ToString(CultureInfo.InvariantCulture)).Append('/')
		  .Append(SolvedUnreadable.ToString(CultureInfo.InvariantCulture));
		sb.Append(" seen(barrierDmg/addBarrier/takeover/fixed)=")
		  .Append(BarrierDamageSeen.ToString(CultureInfo.InvariantCulture)).Append('/')
		  .Append(AddBarrierSeen.ToString(CultureInfo.InvariantCulture)).Append('/')
		  .Append(TakeOverSeen.ToString(CultureInfo.InvariantCulture)).Append('/')
		  .Append(FixedDamageSeen.ToString(CultureInfo.InvariantCulture));
		sb.Append(" lifeMismatch=").Append(LifeMismatch.ToString(CultureInfo.InvariantCulture));
		sb.Append(" active=").Append(BarrierActive.ToString(CultureInfo.InvariantCulture));
		sb.Append(" barrUnread=").Append(BarrierUnreadable.ToString(CultureInfo.InvariantCulture));
		sb.Append(" first(nom/res/landed/overflow)=").Append(Num(FirstNominal)).Append('/')
		  .Append(Num(FirstResult)).Append('/').Append(Num(FirstLanded)).Append('/')
		  .Append(Num(FirstOverflow));
		sb.Append(" rows=").Append(Rows.ToString(CultureInfo.InvariantCulture));
		sb.Append(" key=").Append(KeyRows.ToString(CultureInfo.InvariantCulture));
		sb.Append(" dropped=").Append(Dropped.ToString(CultureInfo.InvariantCulture));
		sb.Append(" keyDropped=").Append(KeyDropped.ToString(CultureInfo.InvariantCulture));
		return sb.ToString();
	}

	private static string Num(int v)
	{
		return (v == int.MinValue) ? "?" : v.ToString(CultureInfo.InvariantCulture);
	}
}
