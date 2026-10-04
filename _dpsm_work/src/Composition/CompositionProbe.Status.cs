using System.Text;
using UnityEngine;

namespace DpsMeter;

/// <summary>
/// Status abnormalities: the game's status-name table, live lookups and the ATTACK-START
/// snapshot. Conditions must be judged from the state when the calc started, never from the
/// state after the hit applied its own debuff (see SnapshotStatuses).
/// </summary>
public static partial class CompositionProbe
{
	/// <summary>
	/// Snapshot of a unit's statuses taken when the damage calculation STARTS (DamageCalculater.Action).
	///
	/// The game decides whether a status condition holds when it builds/executes the calc, so a status
	/// that this very hit applies (毒 from the same attack) must NOT count for that hit -- reading the
	/// live BuffList at damage time (which happens after the application) made such hits show a
	/// 剩余倍率 of exactly 1/1.15.
	/// </summary>
	private struct StatusSnap
	{
		public long Stamp;
		public long Atk;
		public long Vic;
		public int AtkHp;
		public int VicHp;
		public System.Collections.Generic.HashSet<string> AtkStatuses;
		public System.Collections.Generic.HashSet<string> VicStatuses;
		/// <summary>Attacker's 攻击力 at attack start, and (when it is a different object) its owner's.
		/// The 计算威力 the game reports is fixed when the calc is built, while the attack read at damage
		/// time is LIVE -- for a summon the token can be re-created/rebuffed in between and then read a
		/// base value, which made the derived coefficient nonsense (measured: same 计算威力 98571 printed
		/// with 攻击力 32857 -> 系数 3.00 on 26 hits and with 攻击力 6487 -> 系数 15.20 on the two hits
		/// that landed while the token was mid-rebuild). Snapshotting the attack makes the pair consistent.</summary>
		public int AtkPower;
		public int OwnerPower;
	}

	private static readonly System.Collections.Generic.Dictionary<long, StatusSnap> _statusSnaps = new System.Collections.Generic.Dictionary<long, StatusSnap>();
	private const int StatusSnapMax = 256;
	private static long _statusSnapSeq;

	private static bool _curSnapValid;
	private static StatusSnap _curSnap;

	/// <summary>Record both units' 耐久% and statuses at attack start, keyed by the calc instance.</summary>
	internal static void SnapshotStatuses(DamageCalculater calc)
	{
		try
		{
			if (calc == null) return;
			BattleObject a = null, b = null;
			try { a = calc.Attacker; } catch { }
			try { b = calc.m_blocker; } catch { }
			if (GameRef.IsNull(a)
				&& GameRef.IsNull(b)) return;
			long calcKey = 0;
			try { calcKey = calc.Pointer.ToInt64(); } catch { }
			if (calcKey == 0) return;
			var s = new StatusSnap();
			s.Stamp = ++_statusSnapSeq;
			s.Atk = PtrOf(a);
			s.Vic = PtrOf(b);
			s.AtkHp = SnapHpOf(a);
			s.VicHp = SnapHpOf(b);
			s.AtkStatuses = StatusSetOf(a);
			s.VicStatuses = StatusSetOf(b);
			s.AtkPower = LivePowerOf(a);
			try
			{
				BattleObject own = calc.m_owner;
				if (!GameRef.IsNull(own) && !GameRef.Same(own, a)) s.OwnerPower = LivePowerOf(own);
			}
			catch { }
			_statusSnaps[calcKey] = s;
			if (_statusSnaps.Count > StatusSnapMax)
			{
				long oldest = long.MaxValue, oldestKey = 0;
				foreach (var kv in _statusSnaps)
					if (kv.Value.Stamp < oldest) { oldest = kv.Value.Stamp; oldestKey = kv.Key; }
				if (oldestKey != 0) _statusSnaps.Remove(oldestKey);
			}
		}
		catch { }
	}

	private static long PtrOf(BattleObject bo)
	{
		try
		{
			if (GameRef.IsNull(bo)) return 0;
			return bo.Pointer.ToInt64();
		}
		catch { return 0; }
	}

	internal static int SnapHpOf(BattleObject bo)
	{
		try
		{
			if (GameRef.IsNull(bo)) return -1;
			return bo.LifePercent;
		}
		catch { return -1; }
	}

	/// <summary>Live 攻击力 read (0 when unavailable). Used for the snapshot and as a fallback.</summary>
	internal static int LivePowerOf(BattleObject bo)
	{
		try
		{
			if (GameRef.IsNull(bo)) return 0;
			return bo.Power;
		}
		catch { return 0; }
	}

	/// <summary>
	/// 攻击力 as it was when the current calc STARTED (0 when no snapshot exists -- the caller then
	/// falls back to the live read). See StatusSnap.AtkPower for why the snapshot matters.
	/// </summary>
	internal static int SnapshotPowerFor(BattleObject bo)
	{
		try
		{
			if (!_curSnapValid || GameRef.IsNull(bo)) return 0;
			long p = bo.Pointer.ToInt64();
			if (p == 0) return 0;
			if (p == _curSnap.Atk) return _curSnap.AtkPower;
			if (p == _curSnap.Vic) return 0;
			return 0;
		}
		catch { return 0; }
	}

	/// <summary>Owner's 攻击力 at attack start (0 when the calc had no separate owner).</summary>
	internal static int SnapshotOwnerPower()
	{
		try { return _curSnapValid ? _curSnap.OwnerPower : 0; }
		catch { return 0; }
	}

	internal static System.Collections.Generic.HashSet<string> StatusSetOf(BattleObject bo)
	{
		var set = new System.Collections.Generic.HashSet<string>();
		try
		{
			if (GameRef.IsNull(bo)) return set;
			var list = bo.BuffList;
			if (list == null) return set;
			for (int i = 0; i < list.Count; i++)
			{
				string n = NormStatus(StatusNameOf(list[i]));
				if (!string.IsNullOrEmpty(n)) set.Add(n);
			}
		}
		catch { }
		return set;
	}

	/// <summary>
	/// One ailment on a unit: the game's own counters PLUS who applied it.
	///
	/// <see cref="OwnerType"/>/<see cref="OwnerId"/>/<see cref="OwnerApp"/> come from
	/// <c>BuffBase.OwnerIdentifier</c> (a BattleObjectIdentifier) -- the game tells us the applier directly,
	/// so the source of an ailment no longer has to be GUESSED from "which damage record happened to land
	/// in the same window". That guess produced a real false attribution (2026-09-27): a 暗闇 was credited
	/// to 混沌 ニャルラトホテプ, a unit that cannot inflict it, only because its chip damage was the nearest
	/// record on the victim when the status appeared.
	/// </summary>
	internal struct StatusEnt
	{
		public int Value;
		public int Remain;
		public int OwnerType;
		public int OwnerId;
		public int OwnerApp;
	}

	/// <summary>
	/// Per-buff read failures inside <see cref="StatusSigOf"/>. These used to be INVISIBLE: the outer
	/// try/catch swallowed the exception and returned a partial (or empty) map, so the whole per-hit status
	/// diff silently produced nothing for a battle. Measured 2026-10-03: an entire session reported 0
	/// infliction records while comp4 -- which only reads the buff NAME, not the counters/owner -- still
	/// listed the statuses. One unreadable buff was enough to wipe the map.
	/// Now reported in the [STAT] audit line so this can never hide again.
	/// </summary>
	private static int _sigErrors;
	private static string _sigErrorMsg = "";

	internal static int SigErrors { get { return _sigErrors; } }
	internal static string SigErrorMsg { get { return _sigErrorMsg; } }
	internal static void ResetSigErrors() { _sigErrors = 0; _sigErrorMsg = ""; }

	/// <summary>
	/// 1.4.0: how many times <see cref="StatusSigOf"/> returned EARLY because the unit or its BuffList
	/// could not be read. Distinct from "the unit carries no ailments": an empty map is a legitimate
	/// result, an unreadable list is a failure, and until now the two were the same value.
	/// </summary>
	private static int _sigNoList;

	internal static int SigNoList { get { return _sigNoList; } }

	/// <summary>
	/// 1.4.0: per-status-name [min,max] of the individual `BuffValue` the signature build observed.
	///
	/// WHY THIS IS THE DECISIVE FIELD for `statusAudit.explained = 0`. Measured 2026-10-03 over the
	/// whole corpus: the per-hit infliction diff names exactly ONE status ever (暗闇, 353 records) while
	/// comp4 shows 毒 137,947 / 火傷 135,424 / 凍結 90,271 appearances. `StatusBrief` (comp4) reads only
	/// the buff NAME, so it sees every status; the diff compares `StatusEnt.Value`, which is the SUM of
	/// `BattleObject.BuffList` entries' `BuffValue`. If a newly applied 毒 carries `BuffValue == 0`, then
	/// before = 0 and after = 0, no branch of the diff fires, and a status that demonstrably landed is
	/// reported as never having been inflicted. 暗闇 is the one status measured to raise BuffValue (+50).
	///
	/// This table turns that hypothesis into a number: 毒=[0,0] against 暗闇=[50,50] is the answer,
	/// and any other shape falsifies it.
	/// </summary>
	private static readonly System.Collections.Generic.Dictionary<string, int[]> _statusValues
		= new System.Collections.Generic.Dictionary<string, int[]>();

	internal static void ResetStatusValues() { _statusValues.Clear(); _sigNoList = 0; }

	/// <summary>Compact `name=min..max` rendering, alphabetical, bounded.</summary>
	internal static string StatusValueSample()
	{
		var sb = new System.Text.StringBuilder(400);
		var keys = new System.Collections.Generic.List<string>(_statusValues.Keys);
		keys.Sort(System.StringComparer.Ordinal);
		foreach (string k in keys)
		{
			if (sb.Length >= 500) { sb.Append("…"); break; }
			if (sb.Length > 0) sb.Append(' ');
			int[] mm = _statusValues[k];
			sb.Append(k).Append('=').Append(mm[0]);
			if (mm[1] != mm[0]) sb.Append("..").Append(mm[1]);
		}
		return sb.ToString();
	}


	/// <summary>
	/// 1.5.0 (B2): the structured twin of <see cref="StatusValueSample"/> -- one object per status name
	/// with its observed `BuffValue` min/max, instead of the "毒=0 暗闇=50..50" text.
	///
	/// This table is the whole answer to `statusAudit.explained = 0`, so it is the one sample a script
	/// most wants as numbers: `毒=[0,0]` against `暗闇=[50,50]` is a comparison, and comparing two parsed
	/// strings is how the same regex ends up copied into every analysis script.
	/// </summary>
	internal static void AppendStatusValuesJson(System.Text.StringBuilder sb)
	{
		sb.Append('[');
		var keys = new System.Collections.Generic.List<string>(_statusValues.Keys);
		keys.Sort(System.StringComparer.Ordinal);
		for (int i = 0; i < keys.Count; i++)
		{
			if (i > 0) sb.Append(',');
			int[] mm = _statusValues[keys[i]];
			sb.Append("{\"name\":\"").Append(JsonText.Str(keys[i]))
			  .Append("\",\"min\":").Append(mm[0])
			  .Append(",\"max\":").Append(mm[1]).Append('}');
		}
		sb.Append(']');
	}

	/// <summary>
	/// Status ailments of a unit as name -&gt; <see cref="StatusEnt"/>, used by the per-hit before/after diff
	/// (see Diagnostics/StatusDeltaProbe.cs).
	///
	/// The counters are needed to answer "did this hit inflict a status?" honestly:
	///   value     -- a NEW ailment, or an existing one whose value went up
	///   remaining -- an existing ailment the hit REFRESHED (same value, longer duration); without it a
	///                re-applied 毒 on an already-poisoned target would look like nothing happened
	///   owner     -- the applier, so the row can name the real source instead of the nearest attacker
	/// The accepted names are exactly the ones <see cref="StatusNameOf"/> returns, i.e. real ailments only:
	/// CharacterStatusType.None and IsInvalid entries (耐性/無効化 buffs) are excluded, so a resistance
	/// buff can never be reported as "this hit inflicted X".
	///
	/// EVERY buff is read inside its own try/catch on purpose: one unreadable entry may cost only that
	/// entry, never the whole map (see <see cref="SigErrors"/>).
	/// </summary>
	internal static System.Collections.Generic.Dictionary<string, StatusEnt> StatusSigOf(BattleObject bo)
	{
		var map = new System.Collections.Generic.Dictionary<string, StatusEnt>();
		try
		{
			if (GameRef.IsNull(bo)) { _sigNoList++; return map; }
			var list = bo.BuffList;
			if (list == null) { _sigNoList++; return map; }
			for (int i = 0; i < list.Count; i++)
			{
				try
				{
					string n = StatusNameOf(list[i]);
					if (string.IsNullOrEmpty(n)) continue;
					int v = 1;
					try { v = list[i].BuffValue; } catch { }
					// 1.4.0: record the raw value per status name -- see _statusValues for why this is
					// the field that decides whether the infliction diff can work at all.
					int[] mm;
					if (_statusValues.TryGetValue(n, out mm))
					{
						if (v < mm[0]) mm[0] = v;
						if (v > mm[1]) mm[1] = v;
					}
					else
					{
						_statusValues[n] = new int[] { v, v };
					}
					int rem = 0;
					try { rem = list[i].RemainingTime; } catch { }
					int ot = 0, oid = 0, oap = 0;
					try
					{
						var own = list[i].OwnerIdentifier;
						if (own != null)
						{
							try { ot = (int)own.ObjectType; } catch { }
							try { oid = own.Id; } catch { }
							try { oap = own.AppIndex; } catch { }
						}
					}
					catch { }
					StatusEnt cur;
					if (map.TryGetValue(n, out cur))
					{
						cur.Value += v;
						if (rem > cur.Remain) cur.Remain = rem;
						// keep the first known applier; a re-application by someone else is reported via the diff
						if (cur.OwnerType == 0 && ot != 0) { cur.OwnerType = ot; cur.OwnerId = oid; cur.OwnerApp = oap; }
						map[n] = cur;
					}
					else
					{
						map[n] = new StatusEnt { Value = v, Remain = rem, OwnerType = ot, OwnerId = oid, OwnerApp = oap };
					}
				}
				catch (System.Exception ex)
				{
					_sigErrors++;
					if (_sigErrorMsg.Length == 0) _sigErrorMsg = ex.GetType().Name + ": " + ex.Message;
				}
			}
		}
		catch { }
		return map;
	}

	/// <summary>Select the snapshot belonging to this calc (called when the composition is built).</summary>
	internal static void UseStatusSnapshot(DamageCalculater calc)
	{
		_curSnapValid = false;
		try
		{
			if (calc == null) return;
			long key = calc.Pointer.ToInt64();
			StatusSnap s;
			if (_statusSnaps.TryGetValue(key, out s))
			{
				_curSnap = s;
				_curSnapValid = true;
			}
		}
		catch { }
	}

	/// <summary>
	/// 耐久% of <paramref name="bo"/> AS OF ATTACK START. The game evaluates "耐久がN%以下" conditions when
	/// the calc runs, i.e. BEFORE this hit subtracts the damage; reading LifePercent at damage time puts
	/// the unit one bracket lower and produced residuals above 1 (the stronger reduction counted twice).
	/// Returns -1 when no snapshot applies.
	/// </summary>
	private static int SnapshotHp(BattleObject bo)
	{
		try
		{
			if (!_curSnapValid || GameRef.IsNull(bo)) return -1;
			long p = bo.Pointer.ToInt64();
			if (p == 0) return -1;
			if (p == _curSnap.Atk) return _curSnap.AtkHp;
			if (p == _curSnap.Vic) return _curSnap.VicHp;
			return -1;
		}
		catch { return -1; }
	}

	/// <summary>Statuses of <paramref name="bo"/> as they were when the current calc started (may be null).</summary>
	private static System.Collections.Generic.HashSet<string> SnapshotFor(BattleObject bo)
	{
		try
		{
			if (!_curSnapValid || GameRef.IsNull(bo)) return null;
			long p = bo.Pointer.ToInt64();
			if (p == 0) return null;
			if (p == _curSnap.Vic) return _curSnap.VicStatuses;
			if (p == _curSnap.Atk) return _curSnap.AtkStatuses;
			return null;
		}
		catch { return null; }
	}

	/// <summary>
	/// Does the unit carry the status named by <paramref name="token"/>? Matching is tolerant in both
	/// directions ("毒" vs "毒状態", "猛毒" vs "毒") because the ability text and the game's status name
	/// are not always spelled the same way. <paramref name="known"/> reports whether the token is a
	/// status the game itself knows about, so an unrelated word is never treated as a failed condition.
	/// For the current calc's TARGET the attack-start snapshot is used instead of the live buff list.
	/// </summary>
	private static bool HasStatusLike(BattleObject bo, string token, out bool known)
	{
		known = false;
		try
		{
			string want = NormStatus(token);
			if (want.Length == 0) return false;
			EnsureStatusNames();
			foreach (var kv in _statusNameToType)
				if (NormStatus(kv.Key) == want) { known = true; break; }
			if (GameRef.IsNull(bo)) return false;
			var snap = SnapshotFor(bo);
			if (snap != null)
			{
				foreach (string n0 in snap)
				{
					string n = NormStatus(n0);
					if (n.Length == 0) continue;
					if (n == want) return true;
					if (want.Length >= 2 && n.Contains(want)) return true;
					if (n.Length >= 2 && want.Contains(n)) return true;
				}
				return false;
			}
			var list = bo.BuffList;
			if (list == null) return false;
			for (int i = 0; i < list.Count; i++)
			{
				string n = NormStatus(StatusNameOf(list[i]));
				if (n.Length == 0) continue;
				if (n == want) return true;
				if (want.Length >= 2 && n.Contains(want)) return true;
				if (n.Length >= 2 && want.Contains(n)) return true;
			}
			return false;
		}
		catch { return false; }
	}


	/// <summary>
	/// Populate the game's status names once per battle.
	/// </summary>
	private static readonly System.Collections.Generic.HashSet<string> _statusTableDumped = new System.Collections.Generic.HashSet<string>();

	internal static string StatusNameTableDump()
	{
		var sb = new StringBuilder(200);
		try
		{
			EnsureStatusNames();
			foreach (var kv in _statusNameToType)
			{
				if (!char.IsLetter(kv.Key[0]) || kv.Key[0] < 0x2000) continue;   // skip the English aliases
				if (sb.Length > 0) sb.Append('、');
				sb.Append(kv.Key).Append('=').Append(kv.Value);
			}
		}
		catch { }
		return sb.Length == 0 ? "空" : sb.ToString();
	}

	// ---- status abnormalities (状態異常) ----
	private static readonly System.Collections.Generic.Dictionary<string, int> _statusNameToType = new System.Collections.Generic.Dictionary<string, int>();

	private static bool _statusNamesComplete;

	private static void EnsureStatusNames()
	{
		// Rebuild while the game's own names are still unavailable: the first call can happen before the
		// localisation table is populated (observed: only 5 of ~20 names came back), and a table frozen
		// at that moment made every "毒状態/火傷状態" clause unmatched forever.
		if (_statusNameToType.Count > 0 && _statusNamesComplete) return;
		try
		{
			_statusNameToType.Clear();
			int named = 0, missing = 0;
			for (int i = 0; i < 40; i++)
			{
				var t = (CharacterStatus.Type)i;
				string en = null;
				try { en = t.ToString(); } catch { }
				// out-of-range enum values render as digits; skip those, they are not real statuses
				if (string.IsNullOrEmpty(en) || en == "None" || char.IsDigit(en[0])) continue;
				string n = null;
				try { n = CharacterStatus.GetName(t); } catch { }
				if (!string.IsNullOrEmpty(n)) { named++; if (!_statusNameToType.ContainsKey(n)) _statusNameToType[n] = i; }
				else missing++;
				// the plugin's own label table (Poison -> 毒, Burn -> 火伤) does not depend on the game's
				// localisation being ready, so register it too; NormStatus makes 傷/伤 equivalent
				try
				{
					string local = StatusName(en);
					if (!string.IsNullOrEmpty(local) && !_statusNameToType.ContainsKey(local)) _statusNameToType[local] = i;
				}
				catch { }
				if (!_statusNameToType.ContainsKey(en)) _statusNameToType[en] = i;
			}
			_statusNamesComplete = named >= 8 || missing == 0;
		}
		catch { }
	}

	/// <summary>
	/// Status abnormalities currently ON a unit, named by the game itself
	/// (CharacterStatus.GetName). Resistance/immunity buffs are skipped -- they are not the ailment.
	/// </summary>
	public static string StatusBrief(BattleObject bo)
	{
		try
		{
			if (GameRef.IsNull(bo)) return "";
			var list = bo.BuffList;
			if (list == null) return "";
			var seen = new System.Collections.Generic.HashSet<string>();
			var sb = new StringBuilder(48);
			for (int i = 0; i < list.Count; i++)
			{
				string n = StatusNameOf(list[i]);
				if (string.IsNullOrEmpty(n)) continue;
				if (!seen.Add(n)) continue;
				if (sb.Length > 0) sb.Append('、');
				sb.Append(n);
			}
			return sb.ToString();
		}
		catch { return ""; }
	}

	private static string StatusNameOf(BuffBase b)
	{
		try
		{
			if (b == null) return "";
			var bd = b.BuffData;
			if (bd == null) return "";
			var t = bd.CharacterStatusType;
			if (t.ToString() == "None") return "";
			if (bd.IsInvalid) return "";            // 耐性/无效化 buff, not the ailment itself
			string n = null;
			try { n = CharacterStatus.GetName(t); } catch { }
			if (string.IsNullOrEmpty(n)) n = t.ToString();
			return n;
		}
		catch { return ""; }
	}

	private static bool HasStatus(BattleObject bo, string name)
	{
		try
		{
			if (GameRef.IsNull(bo)) return false;
			var list = bo.BuffList;
			if (list == null) return false;
			for (int i = 0; i < list.Count; i++)
				if (StatusNameOf(list[i]) == name) return true;
			return false;
		}
		catch { return false; }
	}

	/// <summary>
	/// Every status name used as a STATE condition in a clause, e.g. "暗闇状態の敵への与ダメージ+50%"
	/// or "毒/凍結/火傷状態の敵すべての被ダメージがそれぞれ+15%".
	/// Requires the "XX状態" wording so that a damage-type wording like "毒ダメージ+30%" is not
	/// mistaken for a condition. Names contained in a longer match ("毒" vs "猛毒") are dropped.
	///
	/// The 状態 suffix may sit at the END of a '/'-separated list ("毒/火傷状態" = 毒 and 火傷 both
	/// count, 状態 written once), and single-character names (毒) are real statuses -- requiring the
	/// name to be directly followed by 状態 missed both cases, which left such clauses at
	/// 条件性,未计入 forever.
	/// </summary>
	private static System.Collections.Generic.List<string> ClauseStatusNames(string clause)
	{
		var found = new System.Collections.Generic.List<string>();
		try
		{
			EnsureStatusNames();
			foreach (var kv in _statusNameToType)
			{
				string n = kv.Key;
				if (string.IsNullOrEmpty(n) || n.Length > 10) continue;
				string body = n.EndsWith("状態") ? n.Substring(0, n.Length - 2) : n;
				if (body.Length == 0) continue;
				int k = clause.IndexOf(body, System.StringComparison.Ordinal);
				while (k >= 0)
				{
					if (FollowedByState(clause, k + body.Length)) { found.Add(n); break; }
					k = clause.IndexOf(body, k + 1, System.StringComparison.Ordinal);
				}
			}
			// keep only the longest of overlapping names
			for (int i = found.Count - 1; i >= 0; i--)
			{
				for (int j = 0; j < found.Count; j++)
				{
					if (i == j) continue;
					if (found[j].Length > found[i].Length && found[j].Contains(found[i])) { found.RemoveAt(i); break; }
				}
			}
		}
		catch { }
		return found;
	}

	/// <summary>
	/// Is a "状態" right after <paramref name="pos"/>, possibly through a list of further status names
	/// ("毒/火傷状態": after 毒 comes "/火傷" then 状態; "毒と火傷状態": after 毒 comes "と火傷" then 状態)?
	/// 1.5.3: the rule itself moved to <see cref="ClauseStatusRun"/> so recon_probe can EXECUTE it; this
	/// wrapper exists only so the clause pipeline and every call site stay unchanged.
	/// </summary>
	private static bool FollowedByState(string clause, int pos)
	{
		return ClauseStatusRun.FollowedByState(clause, pos, _statusNameToType.Keys);
	}

	/// <summary>Longest known status name starting exactly at <paramref name="pos"/>, or null.
	/// 1.5.3: the rule moved to <see cref="ClauseStatusRun"/> so recon_probe can EXECUTE it.</summary>
	private static string StatusNameAt(string clause, int pos)
	{
		return ClauseStatusRun.StatusNameAt(clause, pos, _statusNameToType.Keys);
	}
}
