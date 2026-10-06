using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DpsMeter;

/// <summary>
/// PROBE (R75): what actually withheld part of a damage-application call.
///
/// WHY IT EXISTS. `被吸收/无效化` has been published since 1.5.5 as `nominal - damage` and nothing ever
/// carried it: 397 records over 46 exports, 395 of them exactly 500,000 and all of them on ショゴス, while
/// `masterdata/*.json` has no field, row or value of 500,000 at all and the boss's live talent list holds
/// only `1002 ModeChange` + `6 攻击力/150/-1`. R74's diagnosis round proved that a difference cannot be
/// turned into a mechanism by staring at it, so this probe reads the things a mechanism WOULD move.
///
/// WHERE IT HOOKS, AND WHY NO NEW PATCH IS NEEDED FOR V1. The two numbers are already in hand:
/// `Hooks/BattleObjectHooks.cs`'s `Damage` prefix/postfix carry `__0` (nominal) and `__result`. Reading the
/// victim's `Life`, its `Character.Barrier` (`IsActived` / `mLife`) and the five invincibility-family flags
/// around that same call costs no extra Harmony detour, which matters because the one crash this project
/// ever caused (1.0.48/1.0.49, see BattleObjectHooks' `ActDamage` note) happened while a NEW patched method
/// converted its arguments. The barrier carrier hooks in `Hooks/AbsorbCarrierHooks.cs` exist, are isolated,
/// and are OFF by default (`Debug/AbsorbProbeHooks`).
///
/// WHAT IS DELIBERATELY NOT DONE HERE: the accounting is not touched. The existing fallback
/// `(__result > 0) ? __result : __0` still books a fully-withheld hit as full damage, and the probe only
/// COUNTS that case (`masked=`), because changing it would move every published taken total and that
/// decision needs the evidence this round collects. Display and export are unchanged too.
///
/// COST: one dictionary write + one `TryCast&lt;Character&gt;` + a handful of property reads per damage event
/// (5,500 events in the measured battle). Read-only, every failure counted, nothing thrown outward.
/// </summary>
internal static class AbsorbProbe
{
	internal static readonly AbsorbProbeReport Report = new AbsorbProbeReport();

	private struct Before
	{
		internal int Life;
		internal bool LifeReadable;
		internal int BarLife;
		internal bool BarReadable;
		internal bool BarActive;
		internal int BarSeq;
		internal int AddSeq;
		internal int TakeSeq;
		internal int FixedSeq;
	}

	private static readonly Dictionary<BattleObject, Before> Prev = new Dictionary<BattleObject, Before>();

	// Carrier sightings, monotonic for the whole battle. The prefix snapshots them and the postfix diffs
	// them, so "Barrier.Damage ran inside THIS call" is an observation about the call and not about the
	// frame: it is the only way to attribute a hook that fires from inside the patched method.
	private static int _barrierDamageSeq;
	private static int _addBarrierSeq;
	private static int _takeOverSeq;
	private static int _fixedDamageSeq;

	internal static int CastErrors;
	internal static int NotCharacter;
	internal static int NoBefore;

	/// <summary>`Debug/AbsorbProbe`. Default true: the probe only reads, and a switch the user has to find
	/// before the evidence exists is how a round ends without evidence.</summary>
	internal static bool Enabled
	{
		get { return Plugin.CfgAbsorbProbe != null && Plugin.CfgAbsorbProbe.Value; }
	}

	private static int MaxRows
	{
		get { return (Plugin.CfgAbsorbProbeMaxRows != null) ? Plugin.CfgAbsorbProbeMaxRows.Value : 400; }
	}

	/// <summary>R76: the SEPARATE budget for key rows (an oversized hit or a carrier sighting). In the
	/// measured battle the single 400-row cap pushed all 11 deciding `ショゴス` rows into `dropped=5157`, so
	/// the two budgets are counted apart -- `dropped` for ordinary rows, `keyDropped` for key rows.</summary>
	private static int MaxKeyRows
	{
		get { return (Plugin.CfgAbsorbProbeKeyRows != null) ? Plugin.CfgAbsorbProbeKeyRows.Value : 200; }
	}

	internal static void Reset()
	{
		Report.Clear();
		Prev.Clear();
		_barrierDamageSeq = 0;
		_addBarrierSeq = 0;
		_takeOverSeq = 0;
		_fixedDamageSeq = 0;
		CastErrors = 0;
		NotCharacter = 0;
		NoBefore = 0;
	}

	// ---- carrier sightings, called from Hooks/AbsorbCarrierHooks.cs --------------------------------

	internal static void SawBarrierDamage() { _barrierDamageSeq++; Report.NoteBarrierDamage(); }
	internal static void SawAddBarrier() { _addBarrierSeq++; Report.NoteAddBarrier(); }
	internal static void SawTakeOver() { _takeOverSeq++; Report.NoteTakeOver(); }
	internal static void SawFixedDamage() { _fixedDamageSeq++; Report.NoteFixedDamage(); }

	/// <summary>`[ABSPROBE] barrier` / `[ABSPROBE] carrier` rows from the isolated hooks. Bounded by the
	/// same two-tier budget as the per-hit rows, so a barrier that fires every frame cannot flood the log
	/// and cannot crowd out an oversized row either.</summary>
	internal static void LogCarrier(string kind, string detail)
	{
		if (!Enabled) return;
		if (!Report.TryTakeRow(CarrierVerdict(kind), MaxRows, MaxKeyRows)) return;
		RuntimeLog.Write("[ABSPROBE] " + kind + " " + detail);
	}

	/// <summary>Which key family a carrier row belongs to, so it draws on the key budget that matches it.</summary>
	private static AbsorbVerdict CarrierVerdict(string kind)
	{
		switch (kind)
		{
			case "takeover": return AbsorbVerdict.TakeOver;
			case "fixed": return AbsorbVerdict.FixedDamage;
			default: return AbsorbVerdict.Barrier;
		}
	}

	/// <summary>A `Barrier`'s own life as text for the carrier rows (they fire outside a damage call, so
	/// there is no before/after pair to print). `?` when unreadable, never 0.
	///
	/// Routed through `Convert` on purpose: the interop surface does not tell us what `mLife` actually is
	/// (a count, a formatted string, a struct), and this probe's job is to PRINT it, not to assume. If it
	/// cannot be read at all the row says so instead of printing a 0.</summary>
	internal static string BarrierLife(Barrier bar)
	{
		if (bar == null) return "?";
		try { return Convert.ToString(bar.mLife, CultureInfo.InvariantCulture); }
		catch { return "?"; }
	}

	// ---- the per-call readings --------------------------------------------------------------------

	/// <summary>Prefix side of `BattleObject.Damage`: the victim's state BEFORE the hit.</summary>
	internal static void NoteBefore(BattleObject victim)
	{
		if (!Enabled || victim == null) return;
		try
		{
			Before b = default(Before);
			b.Life = ReadLife(victim, out b.LifeReadable);
			b.BarActive = ReadBarrier(victim, out b.BarLife, out b.BarReadable);
			b.BarSeq = _barrierDamageSeq;
			b.AddSeq = _addBarrierSeq;
			b.TakeSeq = _takeOverSeq;
			b.FixedSeq = _fixedDamageSeq;
			Prev[victim] = b;
		}
		catch { }
	}

	/// <summary>
	/// Postfix side: classify THIS call and record it. Must be called AFTER the existing
	/// `Aggregator.RecordDamage` so that nothing the probe does can affect the published numbers, and it must
	/// never throw into the hook.
	/// </summary>
	internal static void NoteHit(BattleObject victim, int nominal, int result)
	{
		if (!Enabled || victim == null) return;
		try
		{
			Before b;
			bool have = Prev.TryGetValue(victim, out b);
			if (have) Prev.Remove(victim);
			else NoBefore++;

			AbsorbObservation o = default(AbsorbObservation);
			o.Nominal = nominal;
			o.Result = result;
			o.LifeBefore = have ? b.Life : int.MinValue;
			o.LifeReadable = have && b.LifeReadable;
			o.LifeAfter = o.LifeReadable ? ReadLife(victim, out o.LifeReadable) : int.MinValue;
			o.BarrierLifeBefore = have ? b.BarLife : int.MinValue;
			o.BarrierReadable = have && b.BarReadable;
			bool afterActive = ReadBarrier(victim, out o.BarrierLifeAfter, out o.BarrierReadable);
			o.BarrierActiveBefore = have && b.BarActive;
			o.BarrierDamageSeen = have && b.BarSeq != _barrierDamageSeq;
			o.TakeOverSeen = have && b.TakeSeq != _takeOverSeq;
			o.FixedDamageSeen = have && b.FixedSeq != _fixedDamageSeq;
			o.InvincibleFlag = ReadInvincible(victim);

			AbsorbVerdict v = AbsorbClassifyPolicy.Classify(o);
			Report.Note(o, v);

			// R76: the life law decides what deserves a line. `None` -- the return reports no overflow and the
			// life moved by the whole nominal -- is the only verdict that stays silent by default, so the log
			// cannot fill up with hits nothing happened to.
			bool lifeMismatch = o.LifeReadable && o.IsOversized() && o.LifeDrop() != o.Landed();
			if (v == AbsorbVerdict.None && !o.BarrierActiveBefore && !afterActive && !lifeMismatch) return;

			// Two-tier budget: an oversized hit or a carrier sighting is written even when the ordinary cap is
			// full. In the measured battle the single cap pushed all 11 deciding rows into `dropped=5157`.
			if (!Report.TryTakeRow(v, MaxRows, MaxKeyRows)) return;

			RuntimeLog.Write(Render(victim, o, v, lifeMismatch));
		}
		catch { }
	}

	private static string Render(BattleObject victim, AbsorbObservation o, AbsorbVerdict v, bool lifeMismatch)
	{
		var sb = new StringBuilder(200);
		sb.Append("[ABSPROBE] hit t=").Append(ClockSeconds());
		sb.Append(" vic=").Append(Aggregator.Desc(victim));
		sb.Append(" nom=").Append(o.Nominal.ToString(CultureInfo.InvariantCulture));
		sb.Append(" res=").Append(o.Result.ToString(CultureInfo.InvariantCulture));
		// R76: the honest split, and the raw difference beside it. `diff` is NOT an absorbed amount -- that
		// reading is what R75 got wrong -- and landed/overflow are printed only when the return reports an
		// overflow, so a row without one never looks like a measured split.
		sb.Append(" diff=").Append(Num(o.Nominal - o.Result));
		sb.Append(" landed=").Append(o.IsOversized() ? o.Landed().ToString(CultureInfo.InvariantCulture) : "?");
		sb.Append(" overflow=").Append(o.IsOversized() ? o.Overflow().ToString(CultureInfo.InvariantCulture) : "?");
		sb.Append(" key=").Append(AbsorbProbeReport.IsKeyVerdict(v) ? 1 : 0);
		sb.Append(" life=").Append(Num(o.LifeBefore)).Append('/').Append(Num(o.LifeAfter));
		sb.Append(" lifeDrop=").Append(Num(o.LifeDrop()));
		if (lifeMismatch) sb.Append(" LIFE-MISMATCH");
		sb.Append(" bar=").Append(o.BarrierActiveBefore ? "on" : "off").Append('/')
		  .Append(Num(o.BarrierLifeBefore)).Append('/').Append(Num(o.BarrierLifeAfter));
		sb.Append(" move=").Append(o.BarrierReadable ? ((long)o.BarrierLifeBefore - o.BarrierLifeAfter).ToString(CultureInfo.InvariantCulture) : "?");
		sb.Append(" inv=").Append(o.InvincibleFlag ? 1 : 0);
		sb.Append(" take=").Append(o.TakeOverSeen ? 1 : 0);
		sb.Append(" fixed=").Append(o.FixedDamageSeen ? 1 : 0);
		sb.Append(" bdmg=").Append(o.BarrierDamageSeen ? 1 : 0);
		sb.Append(" verdict=").Append(AbsorbClassifyPolicy.Name(v));
		return sb.ToString();
	}

	/// <summary>The battle-end headline: one line that names every bucket, or null when the probe is off.</summary>
	internal static string Summary()
	{
		if (!Enabled) return null;
		return "[ABSPROBE] sum " + Report.Describe()
			+ " castErr=" + CastErrors.ToString(CultureInfo.InvariantCulture)
			+ " notChar=" + NotCharacter.ToString(CultureInfo.InvariantCulture)
			+ " noBefore=" + NoBefore.ToString(CultureInfo.InvariantCulture);
	}

	private static string Num(int v)
	{
		return (v == int.MinValue) ? "?" : v.ToString(CultureInfo.InvariantCulture);
	}

	private static string ClockSeconds()
	{
		try
		{
			BattleSession s = Aggregator.Session;
			if (s != null) return s.ActiveSeconds.ToString("F2", CultureInfo.InvariantCulture);
		}
		catch { }
		return "?";
	}

	// ---- the readings themselves -------------------------------------------------------------------

	private static int ReadLife(BattleObject bo, out bool ok)
	{
		ok = false;
		try
		{
			int life = bo.Life;
			ok = true;
			return life;
		}
		catch { return int.MinValue; }
	}

	/// <summary>
	/// The victim's barrier, read through the same route `Diagnostics/UnitStateProbe.cs` uses for
	/// `Character.Status`: `TryCast&lt;Character&gt;` (summons and the training-ground wall are NOT Characters,
	/// and that is counted, not folded into a read failure), then `Character.Barrier`. Returns whether the
	/// barrier is ACTIVE; `life`/`readable` describe its `mLife` and stay `int.MinValue`/false when it could
	/// not be obtained, because "there is no barrier" and "the barrier's life could not be read" must not
	/// look the same to the classifier.
	/// </summary>
	private static bool ReadBarrier(BattleObject bo, out int life, out bool readable)
	{
		life = int.MinValue;
		readable = false;
		try
		{
			Character ch = bo.TryCast<Character>();
			if (ch == null) { NotCharacter++; return false; }

			Barrier bar = ch.Barrier;
			if (bar == null) return false;

			bool active = false;
			try { active = bar.IsActived; }
			catch { return false; }

			try
			{
				// `mLife`'s own type is not established yet (the interop metadata gives signature hashes,
				// not readable types), so it goes through Convert: a count, a float and a numeric string all
				// read, anything else throws and is reported as UNREADABLE rather than as a barrier that
				// absorbed 0.
				life = Convert.ToInt32(bar.mLife);
				readable = true;
			}
			catch { readable = false; }

			return active;
		}
		catch
		{
			CastErrors++;
			return false;
		}
	}

	/// <summary>
	/// The invincibility family, all five flags: each of them can make a hit land for less than it was
	/// accounted for, which is precisely the shape being investigated. A failed read is left false (the
	/// classifier then reports `CarrierUnknown` rather than inventing a carrier).
	/// </summary>
	private static bool ReadInvincible(BattleObject bo)
	{
		try
		{
			if (bo.IsInvincible) return true;
			if (bo.IsImmortal) return true;
			if (bo.IsIndomitable) return true;
			if (bo.IsActingInvincible) return true;
			if (bo.IsLifeChangeDisabled) return true;
		}
		catch { }
		return false;
	}
}
