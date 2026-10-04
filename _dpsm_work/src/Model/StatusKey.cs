using System;

namespace DpsMeter;

/// <summary>
/// 1.5.2: the CANONICAL form of a unit's status set, used as one component of a FACT signature.
///
/// WHY IT EXISTS. <see cref="CompositionProbe.StatusBrief"/> emits status NAMES in BuffList order --
/// the order the game happens to keep its buff list in, which changes as buffs are applied and expire.
/// A set has no order: two hits that saw the same statuses must produce the same signature, or the fact
/// table spends a slot on a distinction that does not exist. MEASURED 2026-10-03 (battle_...175142,
/// 5055 damage events): the victim status list took 12 distinct RAW forms but only 8 distinct SETS,
/// i.e. 4 of the 12 were the same state written in a different BuffList order.
///
/// WHY IT IS ITS OWN FILE. It is deliberately free of IL2CPP types and of any Plugin reference, so
/// recon_probe compiles and EXECUTES it. The ordering rule is then pinned by a test instead of being
/// promised by a comment -- the same reason Model/FoldStep.cs was split out in 1.5.0.
/// </summary>
internal static class StatusKey
{
	/// <summary>
	/// Writes the DISTINCT non-empty names of <paramref name="src"/> into <paramref name="buf"/>,
	/// sorted by ordinal comparison (so the result cannot depend on the current culture), and returns
	/// how many were written. <paramref name="buf"/> must have room for at least
	/// <c>src.Length</c> entries; nothing is written beyond that.
	/// </summary>
	internal static int Write(string[] src, string[] buf)
	{
		if (src == null || buf == null) return 0;
		int n = 0;
		for (int i = 0; i < src.Length; i++)
		{
			string s = src[i];
			if (string.IsNullOrEmpty(s)) continue;
			bool dup = false;
			for (int j = 0; j < n; j++)
			{
				if (string.Equals(buf[j], s, StringComparison.Ordinal)) { dup = true; break; }
			}
			if (dup) continue;
			// insertion sort: keep `buf[0..n)` ordered while appending
			int k = n;
			while (k > 0 && string.CompareOrdinal(buf[k - 1], s) > 0) { buf[k] = buf[k - 1]; k--; }
			buf[k] = s;
			n++;
		}
		return n;
	}

	/// <summary>Order-independent equality of two status lists -- the property the signature depends on.
	/// Exposed for the test, which asserts that a permutation maps to the same canonical form.</summary>
	internal static bool SameSet(string[] a, string[] b)
	{
		if (a == null || b == null) return ReferenceEquals(a, b);
		var ba = new string[a.Length + 1];
		var bb = new string[b.Length + 1];
		int na = Write(a, ba);
		int nb = Write(b, bb);
		if (na != nb) return false;
		for (int i = 0; i < na; i++)
		{
			if (!string.Equals(ba[i], bb[i], StringComparison.Ordinal)) return false;
		}
		return true;
	}
}
