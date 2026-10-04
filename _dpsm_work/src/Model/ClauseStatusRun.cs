namespace DpsMeter;

/// <summary>
/// 1.5.3: the GAME-FREE half of the clause -> status-name matching in CompositionProbe.Status/Rules.cs.
///
/// WHY IT IS A SEPARATE FILE. This rule has now been wrong twice in the same way, and both times the
/// damage was invisible until a human inspected the data:
///   * "毒/火傷状態" (状態 written once for a slash list) was not recognised, so 毒 and 火傷 were never
///     registered as clause conditions and every such clause stayed 条件性,未计入 FOREVER;
///   * "毒と火傷状態" (the same idea joined with と) met the same fate, and it is worth exactly 1.15^2
///     per hit -- measured on battle_411001_20261003_183213: [ド・マリニーの掛け時計] folded 0 /
///     parked 960, and precisely those hits carried residual 1.3225 = 1.15^2.
/// StatusKey set the precedent for StatusKey: logic that decides damage belongs in a file the offline
/// assertion project can EXECUTE, not behind a comment that needs IL2CPP to reach. So the matching lives
/// here, takes the known-name set as an argument, and is pinned by recon_probe.
/// </summary>
public static class ClauseStatusRun
{
	/// <summary>Status names with a trailing "状態" removed ("毒状態" -> "毒").</summary>
	private static string Body(string n)
	{
		if (string.IsNullOrEmpty(n)) return "";
		return n.EndsWith("状態") ? n.Substring(0, n.Length - 2) : n;
	}

	/// <summary>Longest known status name starting exactly at <paramref name="pos"/>, or null.</summary>
	public static string StatusNameAt(string clause, int pos, System.Collections.Generic.ICollection<string> names)
	{
		string best = null;
		try
		{
			if (clause == null || names == null || pos < 0) return null;
			foreach (string n in names)
			{
				string body = Body(n);
				if (body.Length == 0 || pos + body.Length > clause.Length) continue;
				if (string.CompareOrdinal(clause, pos, body, 0, body.Length) != 0) continue;
				if (best == null || body.Length > best.Length) best = body;
			}
		}
		catch { }
		return best;
	}

	/// <summary>
	/// Is a "状態" right after <paramref name="pos"/>, possibly through a list of further status names
	/// ("毒/火傷状態": after 毒 comes "/火傷" then 状態; "毒と火傷状態": after 毒 comes "と火傷" then 状態)?
	///
	/// と / や / 及 are list JOINERS exactly like the slashes. Leaving them out made the name BEFORE the
	/// joiner invisible while the one after it was found, so the clause kept a bare "毒" in its residual
	/// text and the generic marker list parked it.
	/// </summary>
	public static bool FollowedByState(string clause, int pos, System.Collections.Generic.ICollection<string> names)
	{
		try
		{
			if (clause == null) return false;
			int i = pos, guard = 0;
			while (i < clause.Length && guard++ < 32)
			{
				if (clause[i] == '状' && i + 1 < clause.Length && clause[i + 1] == '態') return true;
				char c = clause[i];
				if (c == '/' || c == '／' || c == '、' || c == '・' || c == '又' || c == '或'
					|| c == 'と' || c == 'や' || c == '及') { i++; continue; }
				string m = StatusNameAt(clause, i, names);
				if (m == null) return false;
				i += m.Length;
			}
		}
		catch { }
		return false;
	}

	/// <summary>
	/// Remove every "◯◯状態" run that contains one of the VERIFIED status names, so a list written with a
	/// single 状態 ("毒/火傷状態", "毒と火傷状態") does not keep its siblings in the residual text and force
	/// the whole clause back to 条件性,未计入.
	///
	/// と/や ARE run connectors (1.5.3). Breaking on them left the FIRST name of the run outside it, which
	/// is the bug above. The stop set keeps the particles that genuinely end a run.
	/// </summary>
	public static string StripVerifiedStateRuns(string s, System.Collections.Generic.List<string> verified)
	{
		try
		{
			if (string.IsNullOrEmpty(s)) return s;
			var sb = new System.Text.StringBuilder(s.Length);
			int i = 0;
			while (i < s.Length)
			{
				int st = s.IndexOf("状態", i, System.StringComparison.Ordinal);
				if (st < 0) { sb.Append(s, i, s.Length - i); break; }
				// walk back over the status-name run ("毒/火傷") but stop at particles / punctuation
				int b = st, guard = 0;
				while (b > i && guard++ < 16)
				{
					char c = s[b - 1];
					if (c == '/' || c == '／' || c == '、' || c == '・' || c == 'と' || c == 'や') { b--; continue; }
					if ("のをがはにへで も敵全自身味方前衛後衛".IndexOf(c) >= 0) break;
					if (!char.IsLetter(c)) break;
					b--;
				}
				string run = s.Substring(b, st - b);
				bool keep = true;
				if (verified != null)
					for (int k = 0; k < verified.Count; k++)
						if (!string.IsNullOrEmpty(verified[k]) && run.IndexOf(verified[k], System.StringComparison.Ordinal) >= 0) { keep = false; break; }
				if (keep) sb.Append(s, i, st + 2 - i);   // not one of ours: keep it as it was
				i = st + 2;
			}
			return sb.ToString();
		}
		catch { return s; }
	}

	/// <summary>
	/// Status words named as conditions by the clause itself: every "◯◯状態" occurrence, and every name
	/// inside the run in front of it ("毒/火傷状態", "毒と火傷状態"), yields one token. Deriving the tokens
	/// from the CLAUSE rather than from the game's status-name table makes the check independent of how
	/// CharacterStatus.GetName happens to spell things.
	///
	/// 1.5.3: と/や/及 are list JOINERS here exactly as in the two functions above, and THIS is the site
	/// that decided [ド・マリニーの掛け時計]「毒と火傷状態の敵への与ダメージがそれぞれ+15%」. The run
	/// stopped on と, so only 火傷 became a token: stHits stayed 1, the それぞれ stacking in JudgeClause
	/// never ran (×1.15 instead of ×1.15^2), and the leftover 毒 in the residual text parked the clause
	/// as 条件性,未计入. Measured on battle_411001_20261003_183213: folded 0 / parked 960, and precisely
	/// those hits carried residual 1.3225 = 1.15^2.
	/// </summary>
	public static System.Collections.Generic.List<string> StatusTokens(string clause)
	{
		var res = new System.Collections.Generic.List<string>();
		try
		{
			if (clause == null) return res;
			int i = 0;
			while (i < clause.Length)
			{
				int st = clause.IndexOf("状態", i, System.StringComparison.Ordinal);
				if (st < 0) break;
				// walk back over the run of status names ("毒/火傷", "毒と火傷") stopping at particles
				int b = st, guard = 0;
				while (b > 0 && guard++ < 24)
				{
					char c = clause[b - 1];
					if (c == '/' || c == '／' || c == '、' || c == '・' || c == 'と' || c == 'や' || c == '及') { b--; continue; }
					if ("のをがはにへで も敵全自身味方前衛後衛るたし".IndexOf(c) >= 0) break;
					if (!char.IsLetter(c)) break;
					b--;
				}
				string run = clause.Substring(b, st - b);
				foreach (string piece in run.Split('/', '／', '、', '・', 'と', 'や', '及'))
				{
					string t = piece.Trim();
					if (t.Length == 0 || t == "異常" || t == "状態") continue;
					if (!res.Contains(t)) res.Add(t);
				}
				i = st + 2;
			}
		}
		catch { }
		return res;
	}
}