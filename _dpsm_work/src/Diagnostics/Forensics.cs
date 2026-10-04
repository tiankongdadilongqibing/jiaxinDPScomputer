using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DpsMeter;

/// <summary>
/// 1.4.0: per-hit FORENSICS for the hits the composition still cannot explain.
///
/// WHY IT EXISTS. The export already carries every hit's full composition (`comp1..comp4` text plus the
/// structured `calc` object), and offline analysis has been reading those. So when a residual like
/// メアリー's `3.306` (= 2.5 × 1.15²) survives every such reading, the missing factor is by construction
/// something the composition NEVER READS -- and the answer cannot be another regex over data that is
/// already exported. What is missing is a recording of the state the composition does not model, taken
/// AT THE MOMENT of the hit (the live objects do not survive to export time).
///
/// So this channel keeps a bounded, bucketed specimen set: for each distinct
/// (attacker, residual-to-3-decimals) pair that is NOT already explained, the first
/// <see cref="MaxPerBucket"/> hits are rendered in full, live state included, and nothing else is kept.
/// One battle therefore yields a complete worked example of every unexplained class, which is what
/// turns "what is 2.5?" into a lookup instead of another battle.
///
/// The bucketing uses the SAME predicate as the KPI (<see cref="CalcReconcile.IsExplained"/>), so
/// "forensics is empty" and "the reconciliation rate is 100%" can never contradict each other.
///
/// Cost: bounded by <see cref="MaxBuckets"/> × <see cref="MaxPerBucket"/> records per battle, and the
/// expensive live reads (<see cref="UnitStateProbe.Render"/>) run ONLY for kept records.
/// </summary>
internal static class Forensics
{
	/// <summary>Distinct (attacker, residual) classes kept per battle.</summary>
	internal const int MaxBuckets = 80;

	/// <summary>Specimens kept per class.</summary>
	internal const int MaxPerBucket = 2;

	/// <summary>Unexplained records seen (whether or not a specimen was kept).</summary>
	internal static int Seen;
	internal static int Kept;
	internal static int OverflowBuckets;
	internal static int OverflowPerBucket;
	internal static int Errors;
	/// <summary>Specimens whose live state could not be read. Counted so an empty state string is
	/// never mistaken for "the unit has no state".</summary>
	internal static int StateUnreadable;

	internal static int DistinctBuckets { get { return _kept.Count; } }

	private static readonly Dictionary<string, int> _kept = new Dictionary<string, int>();
	private static readonly List<string> _records = new List<string>();

	internal static void Reset()
	{
		Seen = 0;
		Kept = 0;
		OverflowBuckets = 0;
		OverflowPerBucket = 0;
		Errors = 0;
		StateUnreadable = 0;
		_kept.Clear();
		_records.Clear();
	}

	/// <summary>
	/// Called for every damage record right after its composition was built and while the live objects
	/// are still valid. Cheap on the common path: one <see cref="CalcReconcile.IsExplained"/> test.
	/// </summary>
	internal static void Observe(BattleEvent ev, BattleObject attacker, BattleObject victim)
	{
		try
		{
			if (Plugin.CfgForensics == null || !Plugin.CfgForensics.Value) return;
			if (ev == null || !ev.Calc.Valid) return;
			CalcBreakdown b = ev.Calc;
			if (b.Theory <= 0 || b.GameValue <= 0) return;
			// Explained hits are by definition not the question, and this is the same rule the KPI uses.
			if (CalcReconcile.IsExplained(b)) return;
			Seen++;

			string atk = ev.Attacker ?? "?";
			string key = atk + "|" + b.Residual.ToString("F3", CultureInfo.InvariantCulture);
			int n;
			if (!_kept.TryGetValue(key, out n))
			{
				if (_kept.Count >= MaxBuckets) { OverflowBuckets++; return; }
				n = 0;
			}
			if (n >= MaxPerBucket) { OverflowPerBucket++; return; }
			_kept[key] = n + 1;
			Kept++;
			_records.Add(Render(ev, key, attacker, victim));
		}
		catch { Errors++; }
	}

	private static string Render(BattleEvent ev, string key, BattleObject attacker, BattleObject victim)
	{
		CalcBreakdown b = ev.Calc;
		var sb = new StringBuilder(1100);
		sb.Append("{\"class\":\"").Append(JsonText.Str(key)).Append('"')
		  .Append(",\"t\":").Append(ev.T.ToString("F2", CultureInfo.InvariantCulture))
		  .Append(",\"atk\":\"").Append(JsonText.Str(ev.Attacker)).Append('"')
		  .Append(",\"vic\":\"").Append(JsonText.Str(ev.Victim)).Append('"')
		  .Append(",\"atkTeam\":").Append(ev.AttackerTeam)
		  .Append(",\"vicTeam\":").Append(ev.VictimTeam)
		  .Append(",\"friendly\":").Append(ev.Friendly ? "true" : "false")
		  .Append(",\"dmg\":").Append(ev.Amount)
		  .Append(",\"nominal\":").Append(ev.Nominal)
		  .Append(",\"attr\":\"").Append(JsonText.Str(ev.Attr)).Append('"')
		  .Append(",\"source\":").Append(ev.Source)
		  .Append(",\"hypCrit\":").Append(ev.Crit ? "true" : "false");
		// the chain's own arithmetic, inline so a specimen is readable without cross-referencing the event
		sb.Append(",\"calc\":{\"effectId\":").Append(b.EffectId)
		  .Append(",\"hitType\":").Append(b.HitType)
		  .Append(",\"attackPower\":").Append(b.AttackPower)
		  .Append(",\"power\":").Append(b.Power)
		  .Append(",\"ratio\":").Append(b.Ratio.ToString("F2", CultureInfo.InvariantCulture))
		  .Append(",\"defense\":").Append(b.DefenseUsed)
		  .Append(",\"penetration\":").Append(b.Penetration)
		  .Append(",\"base\":").Append(b.BaseDamage)
		  .Append(",\"attrMult\":").Append(b.AttrMult.ToString("F3", CultureInfo.InvariantCulture))
		  .Append(",\"dealtMult\":").Append(b.DealtMult.ToString("F3", CultureInfo.InvariantCulture))
		  .Append(",\"takenMult\":").Append(b.TakenMult.ToString("F3", CultureInfo.InvariantCulture))
		  .Append(",\"knownMult\":").Append(b.KnownMult.ToString("F3", CultureInfo.InvariantCulture))
		  .Append(",\"theory\":").Append(b.Theory)
		  .Append(",\"applied\":").Append(b.Applied)
		  .Append(",\"residual\":").Append(b.Residual.ToString("F3", CultureInfo.InvariantCulture))
		  .Append(",\"critRate\":").Append(b.CritRate)
		  .Append(",\"critDamageRate\":").Append(b.CritDamageRate)
		  .Append(",\"pair\":\"").Append(JsonText.Str(b.Pair)).Append('"')
		  .Append(",\"vicGive\":").Append(b.GivenTalents)
		  .Append(",\"vicGiveApplied\":").Append(b.GivenApplied)
		  .Append(",\"vicBuffs\":").Append(b.VictimBuffs)
		  .Append(",\"vicExtra\":").Append(b.VictimExtraTalents)
		  .Append('}');
		// the four human-readable composition lines: the SAME strings the export carries per hit, but
		// here they sit next to the state below instead of having to be re-paired by timestamp offline
		sb.Append(",\"comp\":\"").Append(JsonText.Str(ev.Comp)).Append('"')
		  .Append(",\"comp2\":\"").Append(JsonText.Str(ev.Comp2)).Append('"')
		  .Append(",\"comp3\":\"").Append(JsonText.Str(ev.Comp3)).Append('"')
		  .Append(",\"comp4\":\"").Append(JsonText.Str(ev.Comp4)).Append('"');
		// THE NEW PART: live state the composition does not model, captured while the objects are alive.
		//  * `state` = live resistance + 蓄积 counters + which statuses are on (UnitStateProbe)
		//  * `vicGiveText` = the granted-talents text, i.e. the cross-unit channel
		sb.Append(",\"atkState\":\"").Append(JsonText.Str(State(attacker))).Append('"')
		  .Append(",\"vicState\":\"").Append(JsonText.Str(State(victim))).Append('"');
		// The TALENT list of both sides, raw. The composition folds only the clauses it can parse out of
		// ability TEXT, so a modifier that exists as a talent parameter with no matching text (or with
		// text the clause parser rejects) is invisible to it by construction. For the 1.4.0 target --
		// メアリー's constant ×2.5, which is attacker-specific and NOT a boss/global state -- the
		// candidates are exactly talent types such as 1005 DamageUp / 1019 DirectAttackRate /
		// 1020 DirectAttackActual / 1044 DirectAttackPower / 1064 FixedDamage, so the raw list is what
		// turns "which of the five?" into a lookup. Types are named where the name is evidence-backed
		// (TalentTypeName); unnamed ids stay bare numbers rather than being guessed at.
		sb.Append(",\"atkTalents\":\"").Append(JsonText.Str(TalentDump(attacker, 360))).Append('"')
		  .Append(",\"vicTalents\":\"").Append(JsonText.Str(TalentDump(victim, 260))).Append('"')
		  .Append(",\"atkBuffs\":\"").Append(JsonText.Str(BuffDump(attacker, 260))).Append('"');
		if (!string.IsNullOrEmpty(b.VictimExtraTalentText))
			sb.Append(",\"vicExtraText\":\"").Append(JsonText.Str(b.VictimExtraTalentText)).Append('"');
		if (!string.IsNullOrEmpty(b.GivenTalentText))
			sb.Append(",\"vicGiveText\":\"").Append(JsonText.Str(b.GivenTalentText)).Append('"');
		// 1.7.9: same per-hit switch as the calc writer, so the forensic copy of one hit can be read without
		// having to infer which General/GivenTalent setting produced it.
		sb.Append(",\"givenFoldOn\":").Append(b.GivenFoldOn ? "true" : "false");
		sb.Append('}');
		return sb.ToString();
	}

	private static string State(BattleObject bo)
	{
		try
		{
			if (GameRef.IsNull(bo)) { StateUnreadable++; return "(无对象)"; }
			string s = UnitStateProbe.Render(bo);
			if (string.IsNullOrEmpty(s)) { StateUnreadable++; return "(读不到)"; }
			return s;
		}
		catch { StateUnreadable++; return "(异常)"; }
	}

	/// <summary>Raw talent dump of a unit's own ability list, via the same renderer the [ABIL] dump uses
	/// (`CompositionProbe.AppendTalents`), so a talent reads identically in both places. Truncated to
	/// `cap` characters -- and the truncation is visible ("…"), never silent.</summary>
	private static string TalentDump(BattleObject bo, int cap)
	{
		try
		{
			if (GameRef.IsNull(bo)) return "";
			var list = bo.m_ability;
			if (list == null) return "";
			var sb = new StringBuilder(cap + 40);
			for (int i = 0; i < list.Count && sb.Length < cap; i++)
			{
				try
				{
					var ab = list[i];
					if (ab == null) continue;
					CompositionProbe.AppendTalents(sb, ab.Data, "a" + i);
				}
				catch { Errors++; }
			}
			if (sb.Length == 0) return "";
			string s = sb.ToString();
			if (s.Length > cap) s = s.Substring(0, cap) + "…";
			return s;
		}
		catch { Errors++; return ""; }
	}

	/// <summary>The APPLIED buff list's talents (`CompositionProbe.BuffTalentDump`): what is actually on
	/// the unit right now, as opposed to what its abilities could grant. A ×2.5 that switches on and off
	/// mid-battle has to appear here or in the status flags -- if it appears in neither, it is not a buff.</summary>
	private static string BuffDump(BattleObject bo, int cap)
	{
		try
		{
			if (GameRef.IsNull(bo)) return "";
			string s = CompositionProbe.BuffTalentDump(bo);
			if (string.IsNullOrEmpty(s)) return "";
			if (s.Length > cap) s = s.Substring(0, cap) + "…";
			return s;
		}
		catch { Errors++; return ""; }
	}

	/// <summary>Runtime-log summary, so the channel reports what it did rather than being inferred.</summary>
	internal static string Summary()
	{
		var sb = new StringBuilder(180);
		sb.Append("未解释=").Append(Seen)
		  .Append(" 取样=").Append(Kept)
		  .Append(" 类别=").Append(DistinctBuckets).Append('/').Append(MaxBuckets)
		  .Append(" 每类上限=").Append(MaxPerBucket)
		  .Append(" 类别溢出=").Append(OverflowBuckets)
		  .Append(" 样本溢出=").Append(OverflowPerBucket)
		  .Append(" 状态读不到=").Append(StateUnreadable)
		  .Append(" 错误=").Append(Errors);
		return sb.ToString();
	}

	/// <summary>Appends the root-level `forensics` object. Always emitted (even empty) so that
	/// "no unexplained hits" and "the channel never ran" are distinguishable in the data.</summary>
	internal static void AppendJson(StringBuilder sb)
	{
		sb.Append(",\"forensics\":{\"seen\":").Append(Seen)
		  .Append(",\"kept\":").Append(Kept)
		  .Append(",\"buckets\":").Append(DistinctBuckets)
		  .Append(",\"maxBuckets\":").Append(MaxBuckets)
		  .Append(",\"maxPerBucket\":").Append(MaxPerBucket)
		  .Append(",\"bucketsOverflow\":").Append(OverflowBuckets)
		  .Append(",\"perBucketOverflow\":").Append(OverflowPerBucket)
		  .Append(",\"stateUnreadable\":").Append(StateUnreadable)
		  .Append(",\"errors\":").Append(Errors)
		  .Append(",\"records\":[");
		for (int i = 0; i < _records.Count; i++)
		{
			if (i > 0) sb.Append(',');
			sb.Append(_records[i]);
		}
		sb.Append("]}");
	}
}
