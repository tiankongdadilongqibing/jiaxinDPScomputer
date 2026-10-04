using System;
using System.Collections.Generic;
using System.Text;

namespace DpsMeter;

/// <summary>
/// 1.5.0 (架构审视 B4): the STATE TIMELINE -- when a unit's statuses and status-RESISTANCE slots changed,
/// at FULL resolution, instead of only at the moments a specimen happened to be taken.
///
/// WHY IT EXISTS. The 1.4.1 resistance curve for battle_411001_20261003_150140 could only be drawn from
/// 64 decoded value points out of 5,464 boss hits, because the values were read only when the forensics
/// channel kept a specimen (<= 160 per battle) or by the throttled roster sampling. The chart had to say
/// "the dashed line is a guide, not a measurement" -- the STEPS were verified but their TIMING was not.
/// This channel removes that caveat: it reads the victim's state on every damage event and emits a row
/// only when something CHANGED, so the output is a step function in time rather than a set of samples.
///
/// COST AND SCOPE. The victim only: resistance matters to the damage the victim RECEIVES, the attacker's
/// own resistance slots cannot move an outgoing number, and the attacker's statuses are already recorded
/// per hit in comp4. 18 slot reads per damage event (the same reads `UnitStateProbe.Render` performs,
/// without the string formatting), behind its own switch and with counters, so the cost is measurable and
/// can be turned off without a rebuild.
///
/// TWO STATUS SOURCES, DELIBERATELY. The `st` rows come from `CharaStatus.IsPoison` / `IsBurn` / ... --
/// the game's OWN flags, aligned with the 18 resistance slots. comp4's 受击方状态 comes from the live
/// BuffList NAMES. They are not the same thing (1.4.0 measured that a status applied with BuffValue == 0
/// shows in neither `StatusEnt.Value` nor a buff-value diff), and having both in one export is what makes
/// that difference visible instead of averaged away.
/// </summary>
internal static class StateTimeline
{
	/// <summary>Bound on emitted rows. Overflow is counted; a truncated timeline never looks complete.
	/// A long battle changed a boss's resistance on the order of 31 times, so this is generous.</summary>
	internal const int MaxRows = 4000;

	/// <summary>Status flags tracked, in bit order. Same names as the corresponding resistance slots.</summary>
	private static readonly string[] StatusNames = new string[]
	{
		"眩晕", "石化", "毒", "火傷", "凍結", "暗闇", "狂気", "恐怖", "即死", "时停"
	};

	private struct UnitSnap
	{
		public bool Valid;
		public int StatusBits;
		public int[] Resist;
	}

	private static readonly Dictionary<int, UnitSnap> _last = new Dictionary<int, UnitSnap>();
	private static readonly List<string> _rows = new List<string>(256);
	private static readonly StringBuilder _sb = new StringBuilder(96);

	// ---- counters (exported, never silent) ----
	/// <summary>Damage events at which the victim's state was read.</summary>
	internal static int Observed;
	/// <summary>Distinct units tracked.</summary>
	internal static int Units;
	/// <summary>Rows emitted.</summary>
	internal static int Rows;
	/// <summary>Rows dropped by <see cref="MaxRows"/>.</summary>
	internal static int RowsDropped;
	/// <summary>Status-set changes detected.</summary>
	internal static int StatusChanges;
	/// <summary>Resistance-slot changes detected (one per slot per change).</summary>
	internal static int ResistChanges;
	/// <summary>Units that are not `Character` (Token / CitadelBase) -- no Status.Resistance to read.</summary>
	internal static int NotCharacter;
	internal static int NullStatus;
	internal static int NullResistance;
	internal static int Errors;

	internal static void Reset()
	{
		Observed = 0;
		Units = 0;
		Rows = 0;
		RowsDropped = 0;
		StatusChanges = 0;
		ResistChanges = 0;
		NotCharacter = 0;
		NullStatus = 0;
		NullResistance = 0;
		Errors = 0;
		_last.Clear();
		_rows.Clear();
	}

	/// <summary>
	/// Read one unit's state and emit a row for every change since the last read. Called for the VICTIM of
	/// every damage event, before the event is added, while the game objects are still alive.
	/// </summary>
	internal static void Observe(BattleObject bo, int key, double t)
	{
		try
		{
			if (Plugin.CfgStateTimeline == null || !Plugin.CfgStateTimeline.Value) return;
			if (GameRef.IsNull(bo)) return;
			Observed++;

			Character ch = null;
			try { ch = bo.TryCast<Character>(); } catch { Errors++; return; }
			if (ch == null) { NotCharacter++; return; }
			CharaStatus st = null;
			try { st = ch.Status; } catch { Errors++; return; }
			if (st == null) { NullStatus++; return; }
			CharacterStatusResistance res = null;
			try { res = st.Resistance; } catch { Errors++; return; }
			if (res == null) { NullResistance++; return; }

			int bits = 0;
			Bit(st, 0, () => st.IsStun, ref bits);
			Bit(st, 1, () => st.IsPetrifaction, ref bits);
			Bit(st, 2, () => st.IsPoison, ref bits);
			Bit(st, 3, () => st.IsBurn, ref bits);
			Bit(st, 4, () => st.IsFrozen, ref bits);
			Bit(st, 5, () => st.IsDarkness, ref bits);
			Bit(st, 6, () => st.IsMadness, ref bits);
			Bit(st, 7, () => st.IsFear, ref bits);
			Bit(st, 8, () => st.IsDeath, ref bits);
			Bit(st, 9, () => st.IsTimeStop, ref bits);

			int n = UnitStateProbe.Types.Length;
			int[] now = new int[n];
			for (int i = 0; i < n; i++)
			{
				int raw = int.MinValue;
				try { raw = res.Get(UnitStateProbe.Types[i]); } catch { Errors++; }
				now[i] = raw;
			}

			UnitSnap prev;
			bool had = _last.TryGetValue(key, out prev) && prev.Valid;
			if (!had) Units++;
			if (had && prev.StatusBits != bits)
			{
				StatusChanges++;
				Row(t, key, "st", StatusDiff(prev.StatusBits, bits));
			}
			if (had && prev.Resist != null)
			{
				for (int i = 0; i < n; i++)
				{
					if (prev.Resist[i] == now[i]) continue;
					ResistChanges++;
					// Both endpoints are stated: a reader must be able to tell a real change from a read
					// that failed on one side (int.MinValue means the read threw, not "zero").
					_sb.Length = 0;
					_sb.Append("\"t\":").Append(t.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
					   .Append(",\"k\":").Append(key)
					   .Append(",\"kind\":\"res\",\"i\":").Append(i)
					   .Append(",\"name\":\"").Append(UnitStateProbe.Names[i]).Append('"')
					   .Append(",\"from\":").Append(Raw(prev.Resist[i]))
					   .Append(",\"to\":").Append(Raw(now[i]));
					RowRaw(_sb.ToString());
				}
			}
			var snap = new UnitSnap();
			snap.Valid = true;
			snap.StatusBits = bits;
			snap.Resist = now;
			_last[key] = snap;
		}
		catch { Errors++; }
	}

	private static string Raw(int v)
	{
		return v == int.MinValue ? "null" : v.ToString();
	}

	/// <summary>Read one boolean flag, counting a failure instead of swallowing it.</summary>
	private static void Bit(CharaStatus st, int idx, Func<bool> f, ref int bits)
	{
		try { if (f()) bits |= (1 << idx); }
		catch { Errors++; }
	}

	private static string StatusDiff(int from, int to)
	{
		var on = new StringBuilder(40);
		var off = new StringBuilder(40);
		for (int i = 0; i < StatusNames.Length; i++)
		{
			bool a = (from & (1 << i)) != 0;
			bool b = (to & (1 << i)) != 0;
			if (a == b) continue;
			if (b) { if (on.Length > 0) on.Append('|'); on.Append(StatusNames[i]); }
			else { if (off.Length > 0) off.Append('|'); off.Append(StatusNames[i]); }
		}
		return "\"on\":\"" + on + "\",\"off\":\"" + off + "\"";
	}

	private static void Row(double t, int key, string kind, string body)
	{
		_sb.Length = 0;
		_sb.Append("\"t\":").Append(t.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
		   .Append(",\"k\":").Append(key)
		   .Append(",\"kind\":\"").Append(kind).Append("\",")
		   .Append(body);
		RowRaw(_sb.ToString());
	}

	private static void RowRaw(string body)
	{
		if (_rows.Count >= MaxRows) { RowsDropped++; return; }
		_rows.Add(body);
		Rows++;
	}

	/// <summary>Runtime-log self-report, so the channel states what it did.</summary>
	internal static string Summary()
	{
		var sb = new StringBuilder(140);
		sb.Append("读取=").Append(Observed)
		  .Append(" 单位=").Append(Units)
		  .Append(" 行=").Append(Rows);
		if (RowsDropped > 0) sb.Append("(丢").Append(RowsDropped).Append(')');
		sb.Append(" 状态变=").Append(StatusChanges)
		  .Append(" 抗性变=").Append(ResistChanges)
		  .Append(" 非角色=").Append(NotCharacter)
		  .Append(" 无状态=").Append(NullStatus)
		  .Append(" 无抗性=").Append(NullResistance)
		  .Append(" 错误=").Append(Errors);
		return sb.ToString();
	}

	/// <summary>Appends the root-level `timeline` object. Always emitted (even empty) so that "no change
	/// happened" and "the channel never ran" are distinguishable in the data.</summary>
	internal static void AppendJson(StringBuilder sb)
	{
		sb.Append(",\"timeline\":{\"observed\":").Append(Observed)
		  .Append(",\"units\":").Append(Units)
		  .Append(",\"rowCount\":").Append(Rows)
		  .Append(",\"rowsDropped\":").Append(RowsDropped)
		  .Append(",\"statusChanges\":").Append(StatusChanges)
		  .Append(",\"resistChanges\":").Append(ResistChanges)
		  .Append(",\"notCharacter\":").Append(NotCharacter)
		  .Append(",\"nullStatus\":").Append(NullStatus)
		  .Append(",\"nullResistance\":").Append(NullResistance)
		  .Append(",\"errors\":").Append(Errors)
		  // The legend: a `res` row's `i` indexes THIS list, and a `st` row's bit order follows the
		  // status-flag list in StateTimeline.StatusNames. Exported so the mapping cannot drift.
		  .Append(",\"resistSlots\":[");
		for (int i = 0; i < UnitStateProbe.Names.Length; i++)
		{
			if (i > 0) sb.Append(',');
			sb.Append('"').Append(UnitStateProbe.Names[i]).Append('"');
		}
		sb.Append("],\"statusFlags\":[");
		for (int i = 0; i < StatusNames.Length; i++)
		{
			if (i > 0) sb.Append(',');
			sb.Append('"').Append(StatusNames[i]).Append('"');
		}
		sb.Append("],\"rows\":[");
		for (int i = 0; i < _rows.Count; i++)
		{
			if (i > 0) sb.Append(',');
			sb.Append('{').Append(_rows[i]).Append('}');
		}
		sb.Append("]}");
	}
}
