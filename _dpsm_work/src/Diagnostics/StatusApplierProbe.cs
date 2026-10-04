using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DpsMeter;

/// <summary>
/// 1.5.4 (贡献归因 C): WHO applied 狂気 to WHOM, and WHEN.
///
/// WHY IT EXISTS. The per-hit madness folds (madness#ratio on the attacker side, vicmadness#150 on
/// the victim side) were the single largest unattributed block in the contribution split -- measured
/// on battle_411001_20261003_205449: 32.6% of the entire fold-mass (log-share) had no owner, because
/// a STATUS carries no applier: CharaStatus stores mMadness as a plain int and AddMadness(int) /
/// ResetMadness() take no unit (decompiled interop, 2026-10-03).
///
/// The APPLICATION path does carry both units: 狂気 is applied through TalentActionAddMadness, whose
/// ActExecute(BattleObject owner, BattleObject guest, TalentOption option) names them explicitly
/// (decompiled interop, 2026-10-03). One isolated hook therefore turns "this unit is mad" into
/// "X made it mad at t=...", and the per-hit fold stamps the most recent applier as `byUnit`.
///
/// WHAT IS DELIBERATELY NOT ASSUMED:
///   * Direction. owner=applier / guest=target is the natural reading of the game's own parameter
///     names, but BOTH names go out on every row, so the first 1.5.4 battle settles the reading
///     from data (team sides are known: an application onto the boss must come from a team member).
///   * Completeness. Measured 2026-10-03 (5 battles): our side's madness is 100% メアリー's SELF
///     application (813-906 hits per battle, no other unit ever mad); those rows carry an unreadable
///     guest and are now recorded as self-applications (SelfApplied) so the owner's own fold resolves
///     to the owner. The victim-side madness (boss mad) is ルゥ=ルルサ's application (1115 rows) --
///     i.e. BOTH directions are covered, by two different mechanisms.
///
/// Lookup is by DISPLAY NAME -- the same key space as every other per-hit identity in the export
/// (attacker/victim are names). Two same-named units would conflate; accepted, and consistent with
/// the rest of the file format (and with the user's roster: no same-name allies).
/// </summary>
internal static class StatusApplierProbe
{
	internal static int HookCalls;
	internal static int Recorded;
	internal static int NullOwner;
	internal static int NullGuest;
	internal static int Errors;
	internal static int RowsDropped;
	/// <summary>1.5.5: applications whose guest could not be read. Measured 2026-10-03 (5 battles):
	/// these are SELF-applications (メアリー's madness lands on herself; no other unit is ever mad,
	/// and every such row has owner=メアリー). Counted separately from a real read failure.</summary>
	internal static int SelfApplied;

	/// <summary>Hard cap so one pathological battle cannot inflate the export; overflow is counted.</summary>
	internal const int MaxRows = 512;

	private struct Row
	{
		internal double T;
		internal string Owner;
		internal string Guest;
		internal bool Self;
	}

	private static readonly List<Row> _rows = new List<Row>(64);
	private static readonly Dictionary<string, string> _lastApplier = new Dictionary<string, string>();

	internal static void Note(BattleObject owner, BattleObject guest)
	{
		HookCalls++;
		if (Plugin.CfgMadnessApplier != null && !Plugin.CfgMadnessApplier.Value) return;
		try
		{
			string o = Aggregator.NameOf(owner);
			string g = Aggregator.NameOf(guest);
			if (o == null || o == "?") { o = null; NullOwner++; }
			if (g == null || g == "?") { g = null; NullGuest++; }
			double t = 0.0;
			try { var s = Aggregator.Session; if (s != null) t = s.ActiveSeconds; } catch { }
			if (_rows.Count < MaxRows)
			{
				var r = new Row();
				r.T = t;
				r.Owner = o;
				r.Guest = g;
				r.Self = (g == null);
				_rows.Add(r);
			}
			else RowsDropped++;
			if (g != null)
			{
				_lastApplier[g] = o;
			}
			else if (o != null)
			{
				// 1.5.5: guest unreadable means the unit applied it to ITSELF (user-confirmed game fact +
				// 5 battles of data: only メアリー is ever mad on our side, and every unreadable-guest row
				// has owner=メアリー). The per-hit madness fold of the owner then resolves to the owner.
				_lastApplier[o] = o;
				SelfApplied++;
			}
			Recorded++;
		}
		catch { Errors++; }
	}

	/// <summary>The unit that most recently applied 狂気 to `unitName`; null when never seen.</summary>
	internal static string LastMadnessApplier(string unitName)
	{
		if (unitName == null) return null;
		string v;
		return _lastApplier.TryGetValue(unitName, out v) ? v : null;
	}

	internal static void Reset()
	{
		_rows.Clear();
		_lastApplier.Clear();
		HookCalls = 0;
		Recorded = 0;
		NullOwner = 0;
		NullGuest = 0;
		Errors = 0;
		RowsDropped = 0;
		SelfApplied = 0;
	}

	internal static string Summary()
	{
		var sb = new StringBuilder(96);
		sb.Append("钩子=").Append(HookCalls)
		  .Append(" 记录=").Append(Recorded)
		  .Append(" 无owner=").Append(NullOwner)
		  .Append(" 无guest=").Append(NullGuest)
		  .Append(" 行=").Append(_rows.Count);
		if (RowsDropped > 0) sb.Append("(丢").Append(RowsDropped).Append(')');
		sb.Append(" 单位=").Append(_lastApplier.Count)
		  .Append(" 自施加=").Append(SelfApplied)
		  .Append(" 错误=").Append(Errors);
		return sb.ToString();
	}

	internal static void AppendJson(StringBuilder sb)
	{
		sb.Append(",\"madnessApplies\":{\"hookCalls\":").Append(HookCalls)
		  .Append(",\"recorded\":").Append(Recorded)
		  .Append(",\"nullOwner\":").Append(NullOwner)
		  .Append(",\"nullGuest\":").Append(NullGuest)
		  .Append(",\"selfApplied\":").Append(SelfApplied)
		  .Append(",\"errors\":").Append(Errors)
		  .Append(",\"rowsDropped\":").Append(RowsDropped)
		  .Append(",\"maxRows\":").Append(MaxRows)
		  .Append(",\"rows\":[");
		for (int i = 0; i < _rows.Count; i++)
		{
			if (i > 0) sb.Append(',');
			Row r = _rows[i];
			sb.Append("{\"t\":").Append(r.T.ToString("F2", CultureInfo.InvariantCulture))
			  .Append(",\"owner\":").Append(r.Owner == null ? "null" : "\"" + JsonText.Str(r.Owner) + "\"")
			  .Append(",\"guest\":").Append(r.Guest == null ? "null" : "\"" + JsonText.Str(r.Guest) + "\"")
			  .Append(r.Self ? ",\"self\":true" : "")
			  .Append('}');
		}
		sb.Append("]}");
	}
}