using System;
using System.Collections.Generic;
using System.Text;

namespace DpsMeter;

/// <summary>
/// 1.5.0 (架构审视 B1): EVERY damage hit carries a reference to a full FACT RECORD, deduplicated.
///
/// WHY IT EXISTS. Two facts about the pre-1.5.0 export, both measured on battle_411001_20261003_150140:
///
///  1. Live game state (resistance slots, 蓄积 counters, active statuses, granted talents) can only be
///     read WHILE THE OBJECTS ARE ALIVE -- `Aggregator.FinalizeLocked` sets `ActorStats.Source = null`
///     right after the export, and the game objects are gone by then. So the only way to answer a new
///     question about a past battle is to have recorded it during the battle.
///  2. That recording was done for at most 160 hits per battle (Diagnostics/Forensics.cs: 80 classes x 2
///     specimens), i.e. 3% of 5,501 hits, because a full live read was assumed to be too expensive to do
///     per hit. That assumption is what forced "fight another battle per new hypothesis".
///
/// The same 5,501 hits collapse to only ~500 distinct fact signatures (measured across four exports:
/// 331 / 463 for two ~5,500-hit battles, 33 / 43 for two short ones), because a battle repeats the same
/// few (attacker, target, skill, state) combinations over and over -- 2,838 distinct composition
/// quadruples out of 5,501 events is the same phenomenon seen from the text side. So a bounded, deduped
/// fact table covers 100% of hits for the byte budget that used to buy 3%.
///
/// WHAT IT IS NOT. It does not replace <see cref="Forensics"/>: forensics renders a specimen in DEPTH
/// (both sides' full talent lists, buff tables, granted-talent text) for the classes the chain could not
/// explain. This store renders BREADTH -- one compact record per class, every hit reachable. Keep both.
///
/// COST. The signature is built from data the pipeline has already computed (composition lines, calc
/// scalars, actor keys), so the per-hit cost is one string build and one dictionary lookup. The expensive
/// live read happens ONLY when a new class is inserted, and only while the class budget lasts; both are
/// counted and exported.
/// </summary>
internal static class FactStore
{
	/// <summary>
	/// Distinct fact classes kept per battle. Overflow is counted, never dropped silently.
	///
	/// 1.5.1 SIGNATURE FIX. MEASURED 2026-10-03 (battle_...173710): the 1.5.0 signature included the live
	/// per-hit COUNTS (VictimBuffs / VictimExtraTalents / GivenTalents / GivenApplied) and the pairing mode,
	/// all of which churn every hit -- so 5272 hits produced ~5177 distinct signatures, the table filled at
	/// 600, 4577 hits got NO fact at all and coverage was 13.2% instead of ~100%. Those fields are CONTENT
	/// of a class, not its identity: they are still recorded in the fact record and in `calc.*`, they just
	/// no longer split the class. Only the fields that decide WHICH RULES APPLY are keys now.
	///
	/// 1.5.2 IDENTITY FIX -- THE CAP WAS NEVER THE FIRST PROBLEM. MEASURED 2026-10-03 (battle_...175142,
	/// 5055 damage events): the 1.5.1 signature was replayed offline from the export and produced **1485
	/// distinct signatures**, not the ~5177 of 1.5.0 -- so 1.5.1 did fix the churn 3.5x over. Coverage was
	/// still 36.6% because 1485 > 600, AND because this table fills in FIRST-SIGHT order: it kept the first
	/// 600 signatures it met and served only 2017 hits, whereas the 600 LARGEST classes would have served
	/// 3944. A rare early signature therefore costs a slot that a common one could have used. It also means
	/// `ClassesOverflow` counts repeat-hits that found no room, NOT new keys -- so reading 600+3203 as
	/// "3803 distinct" was wrong, and the replay is what settled it.
	///
	/// Of those 1485, **1232 came from `vicKey` alone**: it is a per-INSTANCE actor key, and this battle has
	/// 187 victim instances of only 3 unit kinds (ショゴス x5040 hits, plus two characters). A rule is
	/// decided by the victim's KIND and its live state, never by which copy of ショゴス it is, so an
	/// instance key is not an identity this table should have. Replacing the instance keys with the unit
	/// names took 1485 -> 240 signatures, and 240 fits the cap with room to spare, so coverage becomes
	/// 100% of events (measured, same battle).
	///
	/// RESIDUAL RISK, stated rather than hidden: a display name is a coarser identity than the game's unit
	/// type. Two variants that share a name would now share a class, and therefore share ONE live-state
	/// read. The class still pins the status set, the block flags and all calc scalars, so the sharing is
	/// visible in the data -- but it is not free, and `facts.items[].vic` records the representative
	/// instance so a reader can see exactly which unit the live read came from.
	/// </summary>
	internal const int MaxClasses = 2400;

	/// <summary>Classes that get the EXPENSIVE live-state render. Ordered by first sight, which favours the
	/// classes that appear early. Raised from 420 in 1.5.2 so the measured 240-class signature is covered
	/// entirely and no class silently loses its live read; the 1.5.2 caps leave 2160 spare class slots, so
	/// a pathological battle still degrades by skipping live reads rather than by dropping hits.</summary>
	internal const int MaxLiveClasses = 900;

	internal sealed class Fact
	{
		public string Key;
		/// <summary>Representative INSTANCE keys -- the actor that first produced this class. Since 1.5.2
		/// these are NOT part of the identity (see <see cref="MaxClasses"/>); they are recorded so a reader
		/// can tell which unit the one live-state read of this class came from.</summary>
		public int AttackerKey;
		public int VictimKey;
		/// <summary>The identity that IS in the key: the unit names, plus the teams.</summary>
		public string AttackerName;
		public string VictimName;
		public int AttackerTeam;
		public int VictimTeam;
		public int EffectId;
		public int HitType;
		public string Pair;
		public double Known;
		public double Residual;
		public string Comp, Comp2, Comp3, Comp4;
		/// <summary>Live state at the moment this class was first seen, or "" when the class did not get a
		/// read (see <see cref="LiveClasses"/> / <see cref="MaxLiveClasses"/>).</summary>
		public string State = "";
		/// <summary>Damage events that referenced this class.</summary>
		public int Hits;
	}

	// ---- counters (exported, never silent) ----
	internal static int Observed;
	internal static int Classes;
	internal static int LiveClasses;
	internal static int ClassesOverflow;
	internal static int LiveSkipped;
	internal static int StateUnreadable;
	internal static int Errors;

	private static readonly Dictionary<string, int> _index = new Dictionary<string, int>(1024);
	private static readonly List<Fact> _facts = new List<Fact>(1024);
	private static readonly StringBuilder _key = new StringBuilder(220);
	/// <summary>Reused scratch for the canonical status set (Model/StatusKey.cs). Sized well above the
	/// 3-5 distinct names a victim carries, and the writer never exceeds <c>src.Length</c>.</summary>
	private static string[] _statusBuf = new string[64];

	internal static void Reset()
	{
		Observed = 0;
		Classes = 0;
		LiveClasses = 0;
		ClassesOverflow = 0;
		LiveSkipped = 0;
		StateUnreadable = 0;
		Errors = 0;
		_index.Clear();
		_facts.Clear();
	}

	/// <summary>1-based fact id for the event, or 0 when nothing was recorded. Returning 0 rather than
	/// silently reusing id 0 keeps "no fact" distinguishable from "fact #1".</summary>
	internal static int Observe(BattleEvent ev, BattleObject attacker, BattleObject victim, int atkKey, int vicKey)
	{
		try
		{
			if (Plugin.CfgFactStore == null || !Plugin.CfgFactStore.Value) return 0;
			if (ev == null || !ev.Calc.Valid) return 0;
			Observed++;
			CalcBreakdown b = ev.Calc;

			// ---- the signature: only data already computed for this hit, and only the fields that decide
			// WHICH RULES APPLY. The live per-hit counts that used to be in here (VictimBuffs,
			// VictimExtraTalents, GivenTalents, GivenApplied) and the pairing mode churn on almost every
			// hit, which made the signature nearly unique per hit and starved the table -- see MaxClasses.
			// 1.5.2: identity is the unit NAME + team, not the per-instance actor key.
			_key.Length = 0;
			_key.Append(ev.Attacker).Append('|').Append(ev.AttackerTeam)
			    .Append('|').Append(ev.Victim).Append('|').Append(ev.VictimTeam)
			    .Append('|').Append(b.EffectId)
			    .Append('|').Append(b.HitType)
			    .Append('|').Append((int)Math.Round(b.KnownMult * 1000.0))
			    .Append('|').Append(b.CritRate)
			    .Append('|').Append(b.CritDamageRate)
			    .Append('|').Append(b.VictimIsBlocking)
			    .Append('|').Append(b.VictimIsUnitBlocking)
			    .Append('|').Append(b.MadnessRatio)
			    // The victim's status set is part of the state that decides which rules apply, and it is
			    // already computed for comp4 -- so it belongs in the signature and costs nothing extra.
			    // 1.5.2: CANONICAL order. StatusBrief emits BuffList order, which is not part of the state,
			    // so an unordered list would split one class into several on ordering alone.
			    .Append('|');
			if (b.VictimStatuses != null)
			{
				if (_statusBuf.Length < b.VictimStatuses.Length) _statusBuf = new string[b.VictimStatuses.Length];
				int ns = StatusKey.Write(b.VictimStatuses, _statusBuf);
				for (int i = 0; i < ns; i++) _key.Append(_statusBuf[i]).Append(',');
			}
			string key = _key.ToString();

			int idx;
			if (!_index.TryGetValue(key, out idx))
			{
				if (_facts.Count >= MaxClasses) { ClassesOverflow++; return 0; }
				var f = new Fact();
				f.Key = key;
				f.AttackerKey = atkKey;
				f.VictimKey = vicKey;
				f.AttackerName = ev.Attacker;
				f.VictimName = ev.Victim;
				f.AttackerTeam = ev.AttackerTeam;
				f.VictimTeam = ev.VictimTeam;
				f.EffectId = b.EffectId;
				f.HitType = b.HitType;
				f.Pair = b.Pair;
				f.Known = b.KnownMult;
				f.Residual = b.Residual;
				f.Comp = ev.Comp;
				f.Comp2 = ev.Comp2;
				f.Comp3 = ev.Comp3;
				f.Comp4 = ev.Comp4;
				// The expensive part, and the only part that cannot be reconstructed later: the live state.
				if (_facts.Count < MaxLiveClasses)
				{
					try
					{
						string s = UnitStateProbe.Render(victim);
						if (string.IsNullOrEmpty(s)) { StateUnreadable++; }
						else { f.State = s; LiveClasses++; }
					}
					catch { StateUnreadable++; }
				}
				else LiveSkipped++;
				_facts.Add(f);
				idx = _facts.Count - 1;
				_index[key] = idx;
				Classes++;
			}
			_facts[idx].Hits++;
			return idx + 1;
		}
		catch { Errors++; return 0; }
	}

	/// <summary>Runtime-log self-report.</summary>
	internal static string Summary()
	{
		var sb = new StringBuilder(140);
		sb.Append("击=").Append(Observed)
		  .Append(" 类=").Append(Classes).Append('/').Append(MaxClasses)
		  .Append(" 含活体=").Append(LiveClasses).Append('/').Append(MaxLiveClasses);
		if (ClassesOverflow > 0) sb.Append(" 类溢出=").Append(ClassesOverflow);
		if (LiveSkipped > 0) sb.Append(" 活体略过=").Append(LiveSkipped);
		sb.Append(" 状态读不到=").Append(StateUnreadable)
		  .Append(" 错误=").Append(Errors);
		return sb.ToString();
	}

	/// <summary>Coverage self-check: how many damage events carry a fact id, and how many of those point at
	/// a class with a live-state read. Printed so "every hit has facts" is a measured claim.</summary>
	internal static void Coverage(BattleSession s, out int withFact, out int withLive, out int dmg)
	{
		withFact = 0;
		withLive = 0;
		dmg = 0;
		try
		{
			if (s == null || s.Events == null) return;
			for (int i = 0; i < s.Events.Count; i++)
			{
				BattleEvent e = s.Events[i];
				if (e == null || e.Type != "dmg") continue;
				dmg++;
				if (e.FactId <= 0) continue;
				withFact++;
				int idx = e.FactId - 1;
				if (idx >= 0 && idx < _facts.Count && !string.IsNullOrEmpty(_facts[idx].State)) withLive++;
			}
		}
		catch { Errors++; }
	}

	/// <summary>Appends the root-level `facts` array. Always emitted (even empty).</summary>
	internal static void AppendJson(StringBuilder sb)
	{
		sb.Append(",\"facts\":{\"observed\":").Append(Observed)
		  .Append(",\"classes\":").Append(Classes)
		  .Append(",\"maxClasses\":").Append(MaxClasses)
		  .Append(",\"liveClasses\":").Append(LiveClasses)
		  .Append(",\"maxLiveClasses\":").Append(MaxLiveClasses)
		  .Append(",\"classesOverflow\":").Append(ClassesOverflow)
		  .Append(",\"liveSkipped\":").Append(LiveSkipped)
		  .Append(",\"stateUnreadable\":").Append(StateUnreadable)
		  .Append(",\"errors\":").Append(Errors)
		  .Append(",\"items\":[");
		for (int i = 0; i < _facts.Count; i++)
		{
			if (i > 0) sb.Append(',');
			Fact f = _facts[i];
			sb.Append("{\"id\":").Append(i + 1)
			  .Append(",\"hits\":").Append(f.Hits)
			  .Append(",\"atk\":").Append(f.AttackerKey)
			  .Append(",\"vic\":").Append(f.VictimKey)
			  // 1.5.2: the components the SIGNATURE is actually built from. `atk`/`vic` above are the
			  // representative instance (the one the live read came from), NOT the identity.
			  .Append(",\"atkName\":\"").Append(JsonText.Str(f.AttackerName)).Append('"')
			  .Append(",\"vicName\":\"").Append(JsonText.Str(f.VictimName)).Append('"')
			  .Append(",\"atkTeam\":").Append(f.AttackerTeam)
			  .Append(",\"vicTeam\":").Append(f.VictimTeam)
			  .Append(",\"eff\":").Append(f.EffectId)
			  .Append(",\"ht\":").Append(f.HitType)
			  .Append(",\"pair\":\"").Append(JsonText.Str(f.Pair)).Append('"')
			  .Append(",\"known\":").Append(f.Known.ToString("F3", System.Globalization.CultureInfo.InvariantCulture))
			  .Append(",\"resid\":").Append(f.Residual.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
			if (!string.IsNullOrEmpty(f.Comp)) sb.Append(",\"comp\":\"").Append(JsonText.Str(f.Comp)).Append('"');
			if (!string.IsNullOrEmpty(f.Comp2)) sb.Append(",\"comp2\":\"").Append(JsonText.Str(f.Comp2)).Append('"');
			if (!string.IsNullOrEmpty(f.Comp3)) sb.Append(",\"comp3\":\"").Append(JsonText.Str(f.Comp3)).Append('"');
			if (!string.IsNullOrEmpty(f.Comp4)) sb.Append(",\"comp4\":\"").Append(JsonText.Str(f.Comp4)).Append('"');
			// Present only when this class got the expensive read; its absence means "not read for this
			// class", never "the unit had no state" (that case carries an explicit (无对象)/(读不到) marker).
			if (!string.IsNullOrEmpty(f.State)) sb.Append(",\"state\":\"").Append(JsonText.Str(f.State)).Append('"');
			sb.Append('}');
		}
		sb.Append("]}");
	}
}
