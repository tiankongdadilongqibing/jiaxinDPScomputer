using System;
using System.Collections.Generic;
using System.Globalization;

namespace DpsMeter;

/// <summary>
/// One applied 攻击力 entry on the ATTACKER, captured at the instant the hit's composition is built --
/// i.e. the same read that produces the 增益 text, so it describes the same moment as AttackPower.
/// Deliberately a plain class: no IL2CPP type appears here, which is what lets recon_probe drive it.
/// </summary>
public sealed class AtkAddItem
{
	public string Type;      // ParamData.Type.ToString(): Rate | Actual | Fixed
	public int Value;        // ParamData.Param -- the APPLIED value, not the declared one
	public bool Plus;        // BuffParam.IsPlus -- the sign the 增益 text prints
	public string Ref;       // /refExistenceTime3000 style, empty when the entry references nothing
	// R87 (方案A): the two fields Ref is built from, kept STRUCTURED. The census has to match a runtime
	// addend against a loadout declaration, and the declaration only has the reference as a CODE
	// (talent p[2]) plus its param (p[1]) -- parsing them back out of the "/refCurrentLife0" string
	// would be a second, avoidable way to be wrong. Both are already read in ReadAtkItem; this only
	// stops them being thrown away. No new game read.
	public string RefType;   // ParamData.ReferenceType.ToString(): CurrentLife / CurrentPower / ...
	public int RefParam;     // ParamData.ReferenceParam -- the trailing number of the Ref string
	public string Owner;     // ParamData.Owner  (MEASURED to be the GIVER, not the holder)
	public string KeyOwner;  // BuffParam.m_owner (cross-check; their disagreement is counted)
	// 1.7.8 (P1-A): the ACTOR KEY of each side, read from the SAME objects in the SAME instant.
	// 0 means "no actor row yet" -- the identity is then unresolved, the name fallback is taken and
	// counted. A name is not an identity: two actors of one battle can share a DisplayName, which is
	// exactly how a teammate's grant used to be mistaken for the attacker's own.
	public int OwnerKey;
	public int KeyOwnerKey;
}

/// <summary>One giver's share of one hit's attack power, ready to be serialised as a fold step.</summary>
public sealed class AtkAddRow
{
	public string Side = "atk";
	public string Kind = AtkAddFold.Kind;
	public string Origin;
	public string Label;
	public string ByUnit;
	public double Factor;
	public double RateSum;
	public int ActualSum;
	public double Base;
	public double DeltaPower;
	public string Items;
}

/// <summary>
/// 1.7.4 (阶段 G, second step): attribute an 攻击力 ADDITION to the unit that GRANTED it.
///
/// WHY A FACTOR. The contribution model multiplies rule factors. An addition is not a multiplier,
/// but removing giver g from the attack-power formula turns its effect into one exactly:
/// P = B*(1 + r/100) + a, so removing g leaves P - dP_g with dP_g = B*r_g/100 + a_g,
/// and f_g = P / (P - dP_g). That factor is inserted into the SAME fold list, so the existing
/// pool split (ln f / ln M) and the existing byUnit resolution do all the rest -- no new KPI definition.
///
/// WHY OWNER IS READ PER HIT. The battle-scoped union in ParamOwnerProbe records WHERE each entry was
/// seen, and its firstT/lastT is a sampling hull, not a duty cycle (hard evidence: a メアリー hit whose
/// 增益 list has no +300% at all sits inside the union row that claims [7.83, 115.27]). Presence must
/// therefore come from the hit itself, and this class consumes the per-hit capture made by the
/// composition walk instead of the union.
///
/// WHAT IS REFUSED, AND COUNTED (never silently charged):
///   * a (type,value) that appears with MORE THAN ONE owner, or with both this unit and another --
///     measured to happen (攻击力 Rate/300 is granted by ルナリス AND carried by ネーフェ/エヴァラス)
///   * an entry with no readable owner
///   * a non-plus entry (a debuff is not a grant)
///   * Fixed-type entries, which the B*(1+r)+a shape does not model
///   * any hit where P <= 0, P <= a, 1 + r/100 <= 0, B <= 0, dP_g <= 0 or dP_g >= P.
/// The premise P = B*(1+r) + a is an INFERENCE. It is not assumed quietly: the guards above are exactly
/// the cases where it fails, and every refusal is a counter in the export.
/// </summary>
public static class AtkAddFold
{
	public const string Kind = "atkadd";

	public static int Hits;
	public static int Emitted;
	public static int SelfValues;
	// 1.7.8 (P1-A): SelfValues is split by HOW the self verdict was reached. SelfValues keeps its old
	// meaning (every self value, whatever the path), so the existing export field does not move.
	public static int SelfByKey;            // self by giverKey == attackerKey
	public static int SelfByNameFallback;   // self by name, taken ONLY because a key was missing
	public static int NameCollision;        // giverName == attacker name but giverKey != attackerKey:
	                                        // the name rule WOULD have dropped a teammate's grant
	public static int OwnerUnknown;         // verdict reached without both keys; every such value is
	                                        // counted here (SelfByNameFallback is a subset of it)
	public static int SkippedGuard;
	public static int SkippedCollision;
	public static int SkippedUnowned;
	public static int SkippedOwnerNull;
	public static int SkippedNegative;
	public static int SkippedType;

	// R87 (方案A): the battle-scoped tally of every addend this class judged SELF, keyed by the runtime
	// match key (which KEEPS the reference the fold's own key drops). It is the only surviving evidence of
	// who granted a team-wide "編成時、味方全員に付与" addend, because the game writes such a grant's
	// ParamData.Owner as the HOLDER. Recorded here, classified at export time against the loadout
	// (Output/AtkAddCensusWriter + Policy/AtkAddCensusPolicy) -- the roster is only complete at battle end,
	// so the verdict cannot be reached per hit.
	private static readonly Dictionary<string, AtkAddSelfEntry> _self = new Dictionary<string, AtkAddSelfEntry>();
	private static readonly List<AtkAddSelfEntry> _selfOrder = new List<AtkAddSelfEntry>();

	/// <summary>Read-only view for the census. Never null; empty before the first self value.</summary>
	public static IList<AtkAddSelfEntry> SelfEntries { get { return _selfOrder; } }

	public static void Reset()
	{
		Hits = 0; Emitted = 0; SelfValues = 0; SkippedGuard = 0; SkippedCollision = 0;
		SkippedUnowned = 0; SkippedOwnerNull = 0; SkippedNegative = 0; SkippedType = 0;
		SelfByKey = 0; SelfByNameFallback = 0; NameCollision = 0; OwnerUnknown = 0;
		_self.Clear();
		_selfOrder.Clear();
	}

	/// <summary>One (hit, addend) instance judged self. Bumped once per ITEM, not once per fold key, so the
	/// census can see two addends that share a (type,value) but differ in their reference -- the difference
	/// is reported as <see cref="AtkAddCensus.DupKeyEntries"/> instead of being silently merged.</summary>
	private static void NoteSelf(int attackerKey, AtkAddItem it)
	{
		string key = AtkAddDeclarePolicy.RuntimeKey(it.Type, it.Value, it.RefParam, it.RefType);
		string dk = attackerKey.ToString(CultureInfo.InvariantCulture) + "|" + key;
		AtkAddSelfEntry e;
		if (!_self.TryGetValue(dk, out e))
		{
			e = new AtkAddSelfEntry
			{
				AttackerKey = attackerKey,
				Key = key,
				Label = AtkAddDeclarePolicy.ItemLabel(it.Type, it.Value, it.RefType, it.RefParam),
			};
			_self[dk] = e;
			_selfOrder.Add(e);
		}
		e.Count++;
	}

	/// <summary>Returns the winner of the value-side/key-side cross-check -- the value side when both
	/// agree or only it is readable, else the one that IS readable -- together with THAT side's actor
	/// key. Both null stays (null, 0). Keeping name and key in one call is deliberate: if they were
	/// resolved separately they could come from different sides.
	/// 1.7.8 (P1-A): the key is the identity; the name is only the fallback and the export label.</summary>
	private static void WinnerPair(string value, int valueKey, string key, int keyKey, out string name, out int actorKey)
	{
		if (!string.IsNullOrEmpty(value)) { name = value; actorKey = valueKey; return; }
		if (!string.IsNullOrEmpty(key)) { name = key; actorKey = keyKey; return; }
		name = null; actorKey = 0;
	}

	/// <summary>
	/// Pure: attack power + the attacker's applied 攻击力 entries -> one row per GIVER.
	/// into may be null, in which case a fresh list is returned.
	///
	/// 1.7.8 (P1-A): <paramref name="selfKey"/> is the ATTACKER's stable actor key (0 when it has no
	/// actor row yet). The self verdict now runs on IDENTITY first: giverKey == selfKey is self,
	/// giverKey != selfKey is a teammate. The name is consulted ONLY when one of the two keys is
	/// missing, and every such fallback is counted (OwnerUnknown / SelfByNameFallback) -- never
	/// silent. <paramref name="selfName"/> remains a parameter because that fallback still needs it;
	/// it is no longer the primary identity. The log-share factor, the ambiguity rule and every
	/// refusal guard are deliberately untouched.
	/// </summary>
	public static List<AtkAddRow> Compute(int attackPower, int selfKey, string selfName, List<AtkAddItem> items, List<AtkAddRow> into)
	{
		if (into == null) into = new List<AtkAddRow>(4);
		if (items == null || items.Count == 0 || attackPower <= 0) return into;
		Hits++;

		// pass 1: signed totals (they are part of P) and the owner of every (type,value)
		double r = 0.0;
		int a = 0;
		var owner = new Dictionary<string, string>();
		// 1.7.8 (P1-A): the key belonging to the WINNING owner side, per value. The ambiguity rule
		// below still compares NAMES on purpose, so its verdict -- and the export -- do not move.
		var ownerKey = new Dictionary<string, int>();
		var ambiguous = new HashSet<string>();
		var order = new List<string>();
		var arePlus = new Dictionary<string, bool>();
		var detail = new Dictionary<string, string>();
		var refs = new Dictionary<string, string>();
		for (int i = 0; i < items.Count; i++)
		{
			AtkAddItem it = items[i];
			if (it == null) continue;
			string ty = it.Type ?? "";
			if (ty != "Rate" && ty != "Actual") { SkippedType++; continue; }
			if (it.Value == 0) { SkippedNegative++; continue; }
			int signed = it.Plus ? it.Value : -it.Value;
			if (ty == "Rate") r += signed; else a += signed;
			string k = ty + "|" + it.Value;
			if (!owner.ContainsKey(k) && !ambiguous.Contains(k))
			{
				order.Add(k);
				string who; int whoKey;
				WinnerPair(it.Owner, it.OwnerKey, it.KeyOwner, it.KeyOwnerKey, out who, out whoKey);
				owner[k] = who;
				ownerKey[k] = whoKey;
				arePlus[k] = it.Plus;
				detail[k] = ty + "+" + it.Value + (string.IsNullOrEmpty(it.Ref) ? "" : "(" + it.Ref + ")");
				refs[k] = it.Ref ?? "";
				if (!it.Plus) SkippedNegative++;
				continue;
			}
			string prev;
			if (ambiguous.Contains(k)) continue;
			if (!owner.TryGetValue(k, out prev)) continue;
			string now;
			WinnerPair(it.Owner, it.OwnerKey, it.KeyOwner, it.KeyOwnerKey, out now, out _);
			if (!string.Equals(prev, now, StringComparison.Ordinal))
			{
				ambiguous.Add(k);
				SkippedCollision++;
			}
		}
		if (r <= -100.0) { SkippedGuard++; return into; }
		double denom = 1.0 + r / 100.0;
		if (denom <= 0.0) { SkippedGuard++; return into; }
		if (attackPower <= a) { SkippedGuard++; return into; }
		double b = (attackPower - a) / denom;
		if (b <= 0.0) { SkippedGuard++; return into; }

		// pass 2: aggregate the GRANTS per owner (self and refused values are deliberately excluded)
		var rate = new Dictionary<string, double>();
		var actual = new Dictionary<string, int>();
		var owners = new List<string>();
		var itemsOf = new Dictionary<string, List<string>>();
		// R87 (方案A): which fold keys this hit judged self. Collected here so pass 3 can re-walk the ITEMS
		// and record the reference dimension, which the fold key deliberately does not carry.
		var selfKeys = new HashSet<string>();
		for (int i = 0; i < order.Count; i++)
		{
			string k = order[i];
			if (ambiguous.Contains(k)) continue;
			string own = owner[k];
			if (own == null) { SkippedUnowned++; continue; }
			if (!arePlus[k]) continue;
			// 1.7.8 (P1-A): identity first; the name is only a COUNTED fallback.
			int giverKey;
			if (!ownerKey.TryGetValue(k, out giverKey)) giverKey = 0;
			bool isSelf;
			if (selfKey > 0 && giverKey > 0)
			{
				isSelf = (giverKey == selfKey);
				if (isSelf) SelfByKey++;
				else if (selfName != null && string.Equals(own, selfName, StringComparison.Ordinal))
				{
					// The name rule would have called this SELF and silently left the grant in
					// baseCredit. The key says it is a teammate, so it IS charged -- and the
					// disagreement is counted instead of being resolved by guessing.
					NameCollision++;
				}
			}
			else
			{
				// No usable key on one side: the documented fallback path, taken EXPLICITLY and
				// counted. On the 26-export corpus this is the only path that can be taken, so the
				// emitted rows stay identical there (a keyless giver was already named, not keyed).
				OwnerUnknown++;
				isSelf = (selfName != null && string.Equals(own, selfName, StringComparison.Ordinal));
				if (isSelf) SelfByNameFallback++;
			}
			if (isSelf) { SelfValues++; selfKeys.Add(k); continue; }
			if (!rate.ContainsKey(own))
			{
				owners.Add(own);
				rate[own] = 0.0;
				actual[own] = 0;
				itemsOf[own] = new List<string>(2);
			}
			string[] parts = k.Split('|');
			int v = 0;
			int.TryParse(parts[1], out v);
			if (parts[0] == "Rate") rate[own] = rate[own] + v; else actual[own] = actual[own] + v;
			itemsOf[own].Add(detail[k]);
		}

		// pass 3 (R87 方案A): record every ITEM instance whose fold key was judged self. This changes no
		// number -- SelfValues above is the count the export has always carried and still is. It exists so
		// the census can tell "Actual+100(現在物理防御)" from "Actual+100(現在魔法防御)", which the fold key
		// cannot: a hit carrying both bumps ONE key, so pass 3's count can exceed SelfValues. That excess
		// is reported as AtkAddCensus.DupKeyEntries rather than being folded away. The attacker key rides
		// along (0 when it has no actor row) because the census scopes its match to the holder's TEAM and
		// cannot do that without it.
		if (selfKeys.Count > 0)
		{
			for (int i = 0; i < items.Count; i++)
			{
				AtkAddItem it = items[i];
				if (it == null) continue;
				string k2 = (it.Type ?? "") + "|" + it.Value.ToString(CultureInfo.InvariantCulture);
				if (!selfKeys.Contains(k2)) continue;
				NoteSelf(selfKey, it);
			}
		}

		for (int i = 0; i < owners.Count; i++)
		{
			string own = owners[i];
			double dp = b * rate[own] / 100.0 + actual[own];
			if (dp <= 0.0) { SkippedGuard++; continue; }
			if (dp >= attackPower) { SkippedGuard++; continue; }
			List<string> ds = itemsOf[own];
			ds.Sort(StringComparer.Ordinal);
			var row = new AtkAddRow();
			row.Origin = Kind + "#" + own;
			row.ByUnit = own;
			row.Label = "攻击力加算 " + string.Join("+", ds.ToArray());
			row.Items = string.Join(",", ds.ToArray());
			row.RateSum = rate[own];
			row.ActualSum = actual[own];
			row.Base = b;
			row.DeltaPower = dp;
			row.Factor = attackPower / (attackPower - dp);
			into.Add(row);
			Emitted++;
		}
		return into;
	}
}
