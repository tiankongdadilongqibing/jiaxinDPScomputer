using System.Collections.Generic;
using System.Text;

namespace DpsMeter;

/// <summary>Why a candidate set produced no label. The caller keeps the three cases apart because
/// "nothing matched" and "several names matched" need different counter names in the diagnosis.</summary>
internal enum LabelMiss
{
	None = 0,
	/// <summary>No candidate carried the live signature (or there were no candidates at all).</summary>
	NoCandidate = 1,
	/// <summary>Candidates matched but disagreed (or the single match had an empty name).</summary>
	Ambiguous = 2,
}

/// <summary>
/// RF6a (plan section 11): the OFFICIAL-NAME rule of the master-data layer, as a pure function.
///
/// Why. The 刻印 slot mixes TWO id spaces that overlap (ids 1..14 are the base engravings, ids 1..28 are the
/// 刻印 variants, and 1..14 collide), so labelling by id alone produces a confident WRONG answer -- the
/// source file records the measurement (ability id 14 is officially 【虚突】, but the row carried mutate 14)
/// and the rule that resolves it:
///
///   1. keep the master rows for this id;
///   2. keep those whose talent-id SIGNATURE equals the live one;
///   3. if every survivor carries the same non-empty display name, use it -- this is what resolves the base
///      engravings, whose 5 level rows share one signature and one name;
///   4. otherwise label NOTHING and count it: a guessed official name is indistinguishable from a resolved
///      one, which is the failure mode the project forbids.
///
/// The rule lived in the middle of a file that also owns IL2CPP table walking, so it could only be exercised
/// by fighting. It is pure here, and the measured cases are its fixtures.
/// </summary>
internal static class MasterDataLabelPolicy
{
	/// <summary>One master row that could name an ability: its display name and its talent signature.</summary>
	public struct Candidate
	{
		public string Name;
		public string Signature;
	}

	/// <summary>The live side's discriminator: the talent type ids, sorted and comma-joined. Order must not
	/// matter (the same talents in another order are the same build), and an empty set is the empty string.
	/// Duplicates are kept: two copies of the same talent is a different signature from one copy.</summary>
	public static string Signature(IReadOnlyList<int> talentTypeIds)
	{
		if (talentTypeIds == null || talentTypeIds.Count == 0) return "";
		List<int> ids = new List<int>(talentTypeIds.Count);
		for (int i = 0; i < talentTypeIds.Count; i++) ids.Add(talentTypeIds[i]);
		ids.Sort();
		StringBuilder sb = new StringBuilder();
		for (int i = 0; i < ids.Count; i++)
		{
			if (i > 0) sb.Append(',');
			sb.Append(ids[i]);
		}
		return sb.ToString();
	}

	/// <summary>
	/// The rule above. <paramref name="label"/> is non-null ONLY on success: on failure the caller must leave
	/// the name empty rather than substitute anything, so it is set to null on every failing path.
	/// </summary>
	public static bool Select(List<Candidate> candidates, string liveSignature, out string label,
	                          out LabelMiss miss)
	{
		label = null;
		miss = LabelMiss.None;
		if (candidates == null || candidates.Count == 0)
		{
			miss = LabelMiss.NoCandidate;
			return false;
		}
		string name = null;
		int distinct = 0;
		for (int i = 0; i < candidates.Count; i++)
		{
			Candidate c = candidates[i];
			if (c.Signature != liveSignature) continue;
			if (name == null)
			{
				name = c.Name;
				distinct = 1;
			}
			else if (name != c.Name)
			{
				distinct = 2;
				break;
			}
		}
		if (distinct == 1 && !string.IsNullOrEmpty(name))
		{
			label = name;
			return true;
		}
		// A single match with an EMPTY name counts as ambiguous, not as "no candidate": the master row
		// exists, it just cannot name the ability.
		miss = (distinct == 0) ? LabelMiss.NoCandidate : LabelMiss.Ambiguous;
		return false;
	}
}
