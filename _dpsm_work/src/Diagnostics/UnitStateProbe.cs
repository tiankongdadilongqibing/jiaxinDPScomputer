using System;
using System.Collections.Generic;
using System.Text;

namespace DpsMeter;

/// <summary>
/// 1.4.0: the unit's LIVE abnormal-status state, read from the game's own runtime object rather than
/// from the character template.
///
/// WHY THIS EXISTS -- the three dead ends it replaces (all measured 2026-10-03):
///
///   1. `CharacterDataBase.m_statusResistance` (what 1.3.7 read, exported as `resistMasterSample`)
///      never moved: 940-952 throttled re-reads over one battle with ZERO change while 5,120
///      `毒耐性-30` grants were landing on that very unit. It also read 100 in all 11 status slots on
///      the boss, which would make every status impossible, while 毒/火傷/凍結 demonstrably landed.
///      Conclusion: it is a TEMPLATE, not the value the game consults.
///   2. `BattleObject.m_statusSubParams` (`CharacterDataBase`-adjacent) read empty for all 12 units
///      (952 reads, 0 errors). Dead.
///   3. `BuffParamData.mNowBuffParamDataDictionary` was empty for the boss at first sight; 1.3.10
///      re-reads it late. Kept, but it is a *rendering* of applied params, not the decision input.
///
/// The route that IS the decision input was found by decompiling the API rather than by guessing
/// (see `_dpsm_work/api_full/`, one file per type):
///
///   `Character : BattleObject`   ->  `.Status`                  (property)  ->  `CharaStatus`
///   `CharaStatus`                ->  `.Resistance`              (property)  ->  `CharacterStatusResistance`
///   `CharacterStatusResistance`  ->  `.Get(Type)`                            raw live slot
///                                ->  `.CalcValue(Type, value)`                the game's own conversion
///   `CharaStatus.AilmentCommonThreshold`  -- STATIC field: the global threshold the wiki calls 100.
///   `Character.ApplyStatusResistanceBuff(Type, BuffParamData.BuffTarget)` -- private; this is what
///       MUTATES `Status.Resistance` when a 毒耐性-30 style buff lands. That is why the live value is
///       on `CharaStatus` and the immutable one is on `CharacterDataBase`.
///   `Character.GetStatusSubParamValue(eBuffType, bool, Func<...>)` -- the game's own reader for the
///       accumulated status sub-params (the 蓄积 values 1.0.x could not reach).
///
/// `Player : Character` and `Enemy : Character`, so the SAME path reads our units and the boss.
/// `Token` / `CitadelBase` derive straight from `BattleObject` and are counted as NotCharacter rather
/// than silently skipped -- a summons' absence must be a number, not an absence of evidence.
///
/// Every read is individually guarded and every failure has its own counter, because the entire
/// history of this feature is a sequence of reads that returned a plausible-looking constant.
/// </summary>
internal static class UnitStateProbe
{
	/// <summary>The 18 slots of `CharacterStatusResistance`, in the game's own order. Single source:
	/// `CompositionProbe.DumpStatusResistance` renders the template copy with the same two arrays.</summary>
	internal static readonly CharacterStatusResistance.Type[] Types = new CharacterStatusResistance.Type[]
	{
		CharacterStatusResistance.Type.Stun, CharacterStatusResistance.Type.StunTerminal,
		CharacterStatusResistance.Type.Petrifaction, CharacterStatusResistance.Type.PetrifactionTerminal,
		CharacterStatusResistance.Type.Poison, CharacterStatusResistance.Type.PoisonDamage,
		CharacterStatusResistance.Type.KnockBack, CharacterStatusResistance.Type.Burn,
		CharacterStatusResistance.Type.Frozen, CharacterStatusResistance.Type.Darkness,
		CharacterStatusResistance.Type.Madness, CharacterStatusResistance.Type.Fear,
		CharacterStatusResistance.Type.Death, CharacterStatusResistance.Type.TimeStop,
		CharacterStatusResistance.Type.BaseStatus, CharacterStatusResistance.Type.MoveSpeed,
		CharacterStatusResistance.Type.AttackSpeed, CharacterStatusResistance.Type.AttackInterval
	};

	internal static readonly string[] Names = new string[]
	{
		"眩晕", "眩晕蓄积", "石化", "石化蓄积", "毒", "毒伤害", "击退", "火傷", "凍結",
		"暗闇", "狂気", "恐怖", "即死", "时停", "基础状态", "移速", "攻速", "攻隔"
	};

	/// <summary>The reference value `CalcValue` is demonstrated with. 150 is the wiki's own worked
	/// example (スタン値150 with 耐性+20 gives 120), so the export can be checked against the
	/// published formula instead of against a guess.</summary>
	internal const int ReferenceValue = 150;

	// ---- counters (exported, never silent) ----
	internal static int Reads;
	internal static int Errors;
	/// <summary>Units whose object is not a `Character` (Token / CitadelBase / enemy buildings).</summary>
	internal static int NotCharacter;
	internal static int NullStatus;
	internal static int NullResistance;
	/// <summary>Throttled re-reads after first sight.</summary>
	internal static int Samples;

	/// <summary>`CharaStatus.AilmentCommonThreshold` -- the global resistance threshold (static).</summary>
	internal static int Threshold;
	internal static bool ThresholdRead;

	private static readonly HashSet<string> _first = new HashSet<string>();
	private static readonly Dictionary<string, string> _last = new Dictionary<string, string>();
	private static readonly Dictionary<string, int> _tick = new Dictionary<string, int>();
	private static readonly StringBuilder _sample = new StringBuilder(2600);

	/// <summary>First-sight rendering per unit, joined. The export's primary evidence.</summary>
	internal static string FirstSample()
	{
		return _sample.ToString();
	}

	/// <summary>Latest rendering per unit. Compared against <see cref="FirstSample"/> by the reader:
	/// if the two differ, the first read was taken before the unit was fully set up.</summary>
	internal static string LastSample()
	{
		var sb = new StringBuilder(2600);
		foreach (var kv in _last)
		{
			if (sb.Length >= 2400) break;
			if (sb.Length > 0) sb.Append(" | ");
			sb.Append(kv.Key).Append(": ").Append(kv.Value);
		}
		return sb.ToString();
	}

	internal static void Reset()
	{
		Reads = 0;
		Errors = 0;
		NotCharacter = 0;
		NullStatus = 0;
		NullResistance = 0;
		Samples = 0;
		RangeSamples = 0;
		_first.Clear();
		_last.Clear();
		_tick.Clear();
		_range.Clear();
		_sample.Length = 0;
		// Threshold / ThresholdRead are deliberately NOT reset: they are a process-wide game constant,
		// and re-reading a static every battle would make a read failure indistinguishable from a change.
	}

	/// <summary>Throttled sampling, called from the same gate as the template-resistance dump so the two
	/// readings are always taken at the same instant and can be compared line for line.</summary>
	internal static void Observe(BattleObject bo, string role, string key, string name, bool first)
	{
		try
		{
			if (Plugin.CfgStatusResist == null || !Plugin.CfgStatusResist.Value) return;
			if (GameRef.IsNull(bo)) return;
			if (!first)
			{
				int now = Environment.TickCount;
				int last;
				if (_tick.TryGetValue(key, out last) && (now - last) < 500) return;
				Samples++;
			}
			_tick[key] = Environment.TickCount;

			string s = Render(bo);
			if (string.IsNullOrEmpty(s)) return;
			_last[key] = s;
			// 1.4.1: the range is tracked on every sample, not only on first sight -- "did the boss's
			// resistance move?" cannot be answered by comparing two endpoints (see TrackRange).
			TrackRange(bo, key);
			if (!first) return;
			_first.Add(key);
			RuntimeLog.Write("[STATE] " + role + " " + name + " " + s);
			if (_sample.Length < 2400)
			{
				if (_sample.Length > 0) _sample.Append(" | ");
				_sample.Append(name).Append(": ").Append(s);
			}
		}
		catch { Errors++; }
	}

	/// <summary>
	/// One unit's live status state, or null when it could not be read (each failure counted).
	///
	/// Shape: `判定=1 阈值=100 蓄积:眩晕=0/狂气=0/毒伤=0/烧伤=0/狂气比=0 状态:毒/火傷
	///         抗性: 毒=-30&gt;0 ...` (raw live slot &gt; `CalcValue(slot, 150)`).
	/// The verdict is the GAME'S own `BattleObject.ShouldTakeStatusAilment` (virtual; overridden by
	/// Player, Enemy, Token, CitadelBase), which is the closest thing to "would this unit accept an
	/// ailment right now" the process can be asked.
	/// </summary>
	internal static string Render(BattleObject bo)
	{
		Character ch = null;
		try { ch = bo.TryCast<Character>(); }
		catch { Errors++; return null; }
		if (ch == null)
		{
			// `Token` (summons) and `CitadelBase` (the training-ground wall 城塞 / T.O.W.E.R.typeR, the
			// only content that produces the residual < 0.1 class) are NOT Characters, so they have no
			// `Status.Resistance`. Returning null here would make every such specimen blank, which is
			// precisely the case the forensics channel is meant to illuminate -- so the base-class facts
			// are rendered instead and the absence is stated in the string.
			NotCharacter++;
			return RenderBase(bo);
		}

		CharaStatus st = null;
		try { st = ch.Status; }
		catch { Errors++; return null; }
		if (st == null) { NullStatus++; return null; }

		CharacterStatusResistance res = null;
		try { res = st.Resistance; }
		catch { Errors++; return null; }
		if (res == null) { NullResistance++; return null; }
		Reads++;

		var sb = new StringBuilder(460);

		bool verdict = false;
		try
		{
			verdict = bo.ShouldTakeStatusAilment;
			sb.Append("判定=").Append(verdict ? 1 : 0);
		}
		catch { Errors++; sb.Append("判定=?"); }

		if (!ThresholdRead)
		{
			try { Threshold = CharaStatus.AilmentCommonThreshold; ThresholdRead = true; }
			catch { Errors++; }
		}
		sb.Append(" 阈值=").Append(ThresholdRead ? Threshold.ToString() : "?");

		// ---- accumulators: the 蓄积 values (mStun / mMadness) and the DOT magnitudes the game keeps
		// per unit. A damage-over-time attacker's hit is priced from `PoisonDamage` / `BurnDamage`, NOT
		// from 计算威力, which is exactly the shape of a constant unexplained residual.
		int stun = -1, madness = -1, poisonDmg = -1, burnDmg = -1, madnessRatio = -1;
		try { stun = st.mStun; } catch { Errors++; }
		try { madness = st.mMadness; } catch { Errors++; }
		try { poisonDmg = st.PoisonDamage; } catch { Errors++; }
		try { burnDmg = st.BurnDamage; } catch { Errors++; }
		try { madnessRatio = st.MadnessAllyBuffRatio; } catch { Errors++; }
		sb.Append(" 蓄积:眩晕=").Append(stun).Append("/狂気=").Append(madness)
		  .Append("/毒伤=").Append(poisonDmg).Append("/火傷=").Append(burnDmg)
		  .Append("/狂気比=").Append(madnessRatio);

		// ---- which statuses are actually ON right now (distinguishes "landed" from "we infer landed")
		sb.Append(" 状态:");
		int flagStart = sb.Length;
		Flag(sb, "眩晕", () => st.IsStun);
		Flag(sb, "石化", () => st.IsPetrifaction);
		Flag(sb, "毒", () => st.IsPoison);
		Flag(sb, "火傷", () => st.IsBurn);
		Flag(sb, "凍結", () => st.IsFrozen);
		Flag(sb, "暗闇", () => st.IsDarkness);
		Flag(sb, "狂気", () => st.IsMadness);
		Flag(sb, "恐怖", () => st.IsFear);
		Flag(sb, "即死", () => st.IsDeath);
		Flag(sb, "时停", () => st.IsTimeStop);
		if (sb.Length == flagStart) sb.Append("无");

		// ---- the live resistance: the whole point. `raw>calc` so the game's own conversion is in the
		// data and the wiki's proportional rule can be verified instead of assumed.
		sb.Append(" 抗性:");
		for (int i = 0; i < Types.Length; i++)
		{
			int raw = int.MinValue, calc = int.MinValue;
			try { raw = res.Get(Types[i]); } catch { Errors++; }
			try { calc = res.CalcValue(Types[i], ReferenceValue); } catch { Errors++; }
			sb.Append(i == 0 ? "" : " ");
			sb.Append(Names[i]).Append('=');
			if (raw == int.MinValue) sb.Append('?'); else sb.Append(raw);
			sb.Append('>');
			if (calc == int.MinValue) sb.Append('?'); else sb.Append(calc);
		}
		return sb.ToString();
	}

	private static void Flag(StringBuilder sb, string label, Func<bool> f)
	{
		try
		{
			if (!f()) return;
			if (sb.Length > 0 && sb[sb.Length - 1] != ':') sb.Append('/');
			sb.Append(label);
		}
		catch { Errors++; }
	}

	/// <summary>
	/// 1.4.1: the 狂気 (Madness) OUTGOING-damage multiplier.
	///
	/// MEASURED on battle_411001_20261003_150140 (1.4.0's own forensics specimens), 924/924 of
	/// メアリー's damage records satisfy
	///     residual == 1.15^n × (狂気 ? 2.5 : 1.0)
	/// with ZERO exceptions, where 狂気 is read independently from the per-hit `comp4.自身状态`:
	/// 834 records with 狂気 all reduce to exactly 2.5 (n = 1 or 2), and the 90 without it reduce to
	/// 1.0 (n = 0 or 2). `CharaStatus.MadnessAllyBuffRatio` reads 250 while 狂気 is on and 100
	/// otherwise -- i.e. 100 is the neutral value, so the multiplier is ratio/100.
	///
	/// The composition never read this: 狂気 appeared only as one of the 18 RESISTANCE slots
	/// (`CharacterStatusResistance.Type.Madness`), never as a damage modifier. The wiki agrees on the
	/// direction (狂気 gives 与ダメージ+50% and 被ダメージ+50%) but the measured outgoing factor for
	/// this unit is 2.5, which is why the fold is keyed on the FIELD and not on the wiki text.
	///
	/// Both conditions are required -- `IsMadness` AND a ratio above the neutral 100 -- so a unit that
	/// merely has the status but a neutral ratio cannot be over-counted, and a garbled read falls back
	/// to ×1.0 instead of inventing a multiplier. `ratio`/`on` are reported so the export can falsify
	/// this rule from data instead of from this comment.
	/// </summary>
	internal static double MadnessMultiplier(BattleObject bo, out int ratio, out bool on)
	{
		ratio = 0;
		on = false;
		try
		{
			if (GameRef.IsNull(bo)) return 1.0;
			Character ch = null;
			try { ch = bo.TryCast<Character>(); } catch { return 1.0; }
			if (ch == null) return 1.0;
			CharaStatus st = null;
			try { st = ch.Status; } catch { return 1.0; }
			if (st == null) return 1.0;
			try { on = st.IsMadness; } catch { return 1.0; }
			if (!on) return 1.0;
			try { ratio = st.MadnessAllyBuffRatio; } catch { return 1.0; }
			if (ratio <= 100) return 1.0;
			return ratio / 100.0;
		}
		catch { return 1.0; }
	}

	// ================= 1.4.1: does the live resistance MOVE? =================

	/// <summary>
	/// Per-unit min/max of every live resistance slot over the whole battle, so "did it move?" is
	/// answered from data.
	///
	/// WHY THIS EXISTS -- a defect in 1.4.0 found by reading its own first output: 1.4.0 kept only
	/// FIRST and LAST readings, and on battle_...150140 the boss reported "未变化" (100 in every status
	/// slot) while the per-hit forensics specimens showed 毒=25 / 火傷=55 / 凍結=55 / 毒伤害=-90 at
	/// t=3.03 s. Both readings were correct: the template-level 100 was never modified, while the
	/// in-battle debuffs moved the value and then EXPIRE, so first == last and a two-point comparison
	/// sees nothing. That is the same single-point-reading mistake this project has now made three
	/// times, so the fix is a RANGE, not another sample.
	/// </summary>
	private static readonly Dictionary<string, int[]> _range = new Dictionary<string, int[]>();

	internal static int RangeSamples;

	/// <summary>Render "which slot moved and between what": `ショゴス: 毒 25..100 火傷 55..100 …`.
	/// Empty when nothing moved, which is a result -- not a failure.</summary>
	internal static string RangeSample()
	{
		var sb = new StringBuilder(600);
		foreach (var kv in _range)
		{
			int[] mm = kv.Value;                       // [min,max] per slot, 2 ints each
			var one = new StringBuilder(90);
			for (int i = 0; i < Types.Length; i++)
			{
				int lo = mm[i * 2], hi = mm[i * 2 + 1];
				if (lo == hi) continue;
				if (one.Length > 0) one.Append(' ');
				one.Append(Names[i]).Append(' ').Append(lo).Append("..").Append(hi);
			}
			if (one.Length == 0) continue;
			if (sb.Length > 0) sb.Append(" | ");
			sb.Append(kv.Key).Append(": ").Append(one);
			if (sb.Length >= 700) { sb.Append(" …"); break; }
		}
		return sb.ToString();
	}

	/// <summary>
	/// 1.5.0 (B2): the structured twin of <see cref="RangeSample"/> -- per unit, only the slots that
	/// actually MOVED, as numbers. This is the field the 1.4.1 resistance work leans on ("the boss's
	/// resistance demonstrably moves in battle"), and as a string it could only be compared by parsing
	/// "毒 25..55". `slot` indexes the `slots` array, so no name matching is needed.
	/// </summary>
	internal static void AppendRangeJson(System.Text.StringBuilder sb)
	{
		try
		{
			sb.Append("{\"slots\":[");
			for (int i = 0; i < Names.Length; i++)
			{
				if (i > 0) sb.Append(',');
				sb.Append('"').Append(Names[i]).Append('"');
			}
			sb.Append("],\"rangeSamples\":").Append(RangeSamples).Append(",\"units\":[");
			bool firstUnit = true;
			foreach (var kv in _range)
			{
				int[] mm = kv.Value;
				if (!firstUnit) sb.Append(',');
				firstUnit = false;
				sb.Append("{\"unit\":\"").Append(JsonText.Str(kv.Key)).Append("\",\"moved\":[");
				bool firstSlot = true;
				for (int i = 0; i < Types.Length; i++)
				{
					int lo = mm[i * 2], hi = mm[i * 2 + 1];
					// An untouched slot keeps its sentinel (MaxValue/MinValue): it was never read, which
					// is NOT the same as "the value did not move" and must not be reported as such.
					if (lo == int.MaxValue || hi == int.MinValue) continue;
					if (lo == hi) continue;
					if (!firstSlot) sb.Append(',');
					firstSlot = false;
					sb.Append("{\"i\":").Append(i).Append(",\"min\":").Append(lo).Append(",\"max\":").Append(hi).Append('}');
				}
				sb.Append("]}");
			}
			sb.Append("]}");
		}
		catch { sb.Append("{}"); }
	}

	/// <summary>Fold one reading into the per-unit min/max. Called on the same 500 ms gate as the rest,
	/// so the range covers the whole battle and not just its two ends.</summary>
	internal static void TrackRange(BattleObject bo, string key)	{
		try
		{
			if (Plugin.CfgStatusResist == null || !Plugin.CfgStatusResist.Value) return;
			Character ch = null;
			try { ch = bo.TryCast<Character>(); } catch { return; }
			if (ch == null) return;
			CharaStatus st = null;
			try { st = ch.Status; } catch { return; }
			if (st == null) return;
			CharacterStatusResistance res = null;
			try { res = st.Resistance; } catch { return; }
			if (res == null) return;

			int[] mm;
			if (!_range.TryGetValue(key, out mm))
			{
				mm = new int[Types.Length * 2];
				for (int i = 0; i < mm.Length; i++) mm[i] = (i % 2 == 0) ? int.MaxValue : int.MinValue;
				_range[key] = mm;
			}
			for (int i = 0; i < Types.Length; i++)
			{
				int v;
				try { v = res.Get(Types[i]); } catch { Errors++; continue; }
				if (v < mm[i * 2]) mm[i * 2] = v;
				if (v > mm[i * 2 + 1]) mm[i * 2 + 1] = v;
			}
			RangeSamples++;
		}
		catch { Errors++; }
	}

	/// <summary>
	/// What can be said about a `BattleObject` that is not a `Character`: the defence pair the chain
	/// subtracts, the blocking state (the channel that produced the ×1.21 window), 耐久 and 计算威力.
	/// The prefix "非角色" is part of the value so a reader can never mistake it for a full reading --
	/// a fallback must not be able to pass itself off as the real thing.
	/// </summary>
	private static string RenderBase(BattleObject bo)
	{
		var sb = new StringBuilder(140);
		sb.Append("(非角色)");
		int def = int.MinValue, mdef = int.MinValue, life = int.MinValue, pow = int.MinValue;
		int blocking = -1, unitBlocking = -1, blockCount = -1;
		try { def = bo.Defense; } catch { Errors++; }
		try { mdef = bo.MagicDefense; } catch { Errors++; }
		try { life = bo.LifePercent; } catch { Errors++; }
		try { pow = bo.Power; } catch { Errors++; }
		try { blocking = bo.IsBlocking ? 1 : 0; } catch { Errors++; }
		try { unitBlocking = bo.IsUnitBlocking ? 1 : 0; } catch { Errors++; }
		try { blockCount = bo.BlockCount; } catch { Errors++; }
		sb.Append(" 物防=").Append(def == int.MinValue ? "?" : def.ToString())
		  .Append(" 魔防=").Append(mdef == int.MinValue ? "?" : mdef.ToString())
		  .Append(" 耐久%=").Append(life == int.MinValue ? "?" : life.ToString())
		  .Append(" 威力=").Append(pow == int.MinValue ? "?" : pow.ToString())
		  .Append(" 阻挡=").Append(blocking).Append('/').Append(unitBlocking).Append('/').Append(blockCount);
		Reads++;
		return sb.ToString();
	}
}
