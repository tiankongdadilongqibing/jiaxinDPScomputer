using System.Collections.Generic;

namespace DpsMeter;

/// <summary>
/// R87 (方案A): one battle-scoped tally of the attack-power addends that <see cref="AtkAddFold"/> judged
/// SELF, keyed by the runtime match key (which, unlike the fold's own key, KEEPS the reference dimension).
///
/// WHY A SEPARATE KEY. The fold keys its values on "type|value" only, and that key is what decides the
/// ambiguity rule and every published number -- so it must not move. But "Actual+100(現在物理防御)" and
/// "Actual+100(現在魔法防御)" are DIFFERENT declarations and must be told apart to find the granter, which
/// is why the census carries the reference too. The two granularities are reconciled by an explicit
/// counter (<see cref="AtkAddCensus.DupKeyEntries"/>) instead of being quietly conflated.
/// </summary>
public sealed class AtkAddSelfEntry
{
	/// <summary>The unit the addend was sitting on (the attacker of that hit). 0 = it had no actor row.</summary>
	public int AttackerKey;

	/// <summary><see cref="AtkAddDeclarePolicy.RuntimeKey"/>; "" when the addend is not one this census models.</summary>
	public string Key = "";

	/// <summary><see cref="AtkAddDeclarePolicy.ItemLabel"/> -- the string the log prints.</summary>
	public string Label = "";

	/// <summary>How many (hit, addend) instances were judged self under this key.</summary>
	public int Count;
}

/// <summary>
/// One unit of the battle's roster, flattened for the census: WHO it is (key, name, TEAM) plus the addends
/// its loadout DECLARES. Built by <c>Output/AtkAddCensusWriter</c> from <c>ActorStats.Roster[].Talents[]</c>;
/// consumed by <see cref="AtkAddCensusPolicy"/>.
///
/// The team is carried on EVERY unit and not only on the declarers, because the census has to know which
/// team the HOLDER is on before it can scope the match -- and a holder frequently declares nothing at all
/// (measured: the 7 allied holders of モネモネ's +4%耐久 addend declare no attack addend of their own).
/// </summary>
public sealed class AtkAddRosterUnit
{
	public int Key;
	public string Name = "";
	public int Team;

	/// <summary><see cref="AtkAddDeclarePolicy.DeclKey"/> of each addend this loadout declares. "" entries
	/// (unmodelled type or reference code) are ignored -- they can never match, and keeping them would only
	/// make the index bigger.</summary>
	public readonly List<string> DeclKeys = new List<string>();
}

/// <summary>One candidate granter of the self-classified addends, with what it was credited for.</summary>
public sealed class AtkAddCensusGiver
{
	public int Key;
	public string Name = "";
	public int Team;

	/// <summary>Σ <see cref="AtkAddSelfEntry.Count"/> of the entries this unit is the unique declarer of.</summary>
	public int Entries;

	public readonly List<string> Items = new List<string>();
}

/// <summary>One refused entry: either several units of the holder's team declare it, or nobody does.</summary>
public sealed class AtkAddCensusItem
{
	public string Item = "";
	public int Entries;

	/// <summary>For an ambiguous entry, the candidate declarers of the holder's team (names, comma joined).
	/// Empty for an undeclared one -- there is no candidate to name.</summary>
	public string Candidates = "";
}

/// <summary>
/// R87 (方案A) 证据普查: where the SELF-classified attack-power addends came from.
///
/// WHAT IT IS NOT. This is an EVIDENCE section. It does not move one published number: every actor's
/// <c>baseCredit</c>, every rule's <c>damageEquivalent</c>, every link and the whole ledger are computed
/// exactly as before. It exists because the user asked "this passive's contribution -- who got it?", and
/// the honest answer today is "nobody; it sits in each holder's own 自身 column", which is only useful if
/// the loadout says who COULD have granted it. That answer is a candidate list, and it is labelled as one.
///
/// TWO IDENTITIES, both flat and asserted by check_export_schema.py, so a reader can reconcile this
/// section against the counter that already exists:
///   Entries == Values + DupKeyEntries       (the reference dimension splits a fold key; never merges it)
///   Entries == Matched + Ambiguous + Undeclared + NoHolder
/// </summary>
public sealed class AtkAddCensus
{
	/// <summary>Σ per-entry counts: every (hit, addend) instance judged self, reference-aware.</summary>
	public int Entries;

	/// <summary><c>atkAdd.selfValues</c> verbatim -- the SAME population at the fold's own (type|value)
	/// granularity. Read here, never recomputed, so the two can only disagree by <see cref="DupKeyEntries"/>.</summary>
	public int Values;

	/// <summary>Entries - Values: a hit that carried the same (type,value) twice with different references.
	/// Always &gt;= 0; the guard asserts it.</summary>
	public int DupKeyEntries;

	/// <summary>Exactly one unit of the holder's own team declares it.</summary>
	public int Matched;

	/// <summary>Two or more units of the holder's team declare it -- refused, never picked (the repo rule).</summary>
	public int Ambiguous;

	/// <summary>No unit of the holder's team declares it (including an unmodelled type/reference code).</summary>
	public int Undeclared;

	/// <summary>The holder has no actor row, so its team is unknown and no team scope can be applied.</summary>
	public int NoHolder;

	public readonly List<AtkAddCensusGiver> Givers = new List<AtkAddCensusGiver>();
	public readonly List<AtkAddCensusItem> AmbiguousItems = new List<AtkAddCensusItem>();
	public readonly List<AtkAddCensusItem> UndeclaredItems = new List<AtkAddCensusItem>();
}
