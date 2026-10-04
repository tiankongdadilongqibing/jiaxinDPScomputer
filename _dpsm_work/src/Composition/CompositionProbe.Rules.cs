using System.Text;
using UnityEngine;

namespace DpsMeter;

/// <summary>
/// Clause rules: which clauses apply, and what number each one contributes.
///
/// This is the file to extend when the wiki documents a new kind of condition or a new way of
/// writing a modifier: keyword/position/attribute gating (JudgeClause), the 耐久-tier HP table
/// (TieredModifier), condition evaluation (EvalConditions) and value parsing (ParseDamageModifier).
/// </summary>
public static partial class CompositionProbe
{
	private enum CondState { Ok = 0, Failed = 1, Unknown = 2 }

	/// <summary>Why a clause ended up counted (or not). Used for the display and for the [ABIL] dump.</summary>
	private enum ClauseVerdict
	{
		NoKeyword = 0,
		SkippedPosition = 1,
		SkippedAttribute = 2,
		ConditionFailed = 3,
		Conditional = 4,
		NotParsed = 5,
		Counted = 6,
		/// <summary>The clause is real but is NOT a bonus to the carrier's own hits: the talent is
		/// granted to the victim (`type=1004 AddTalent` -> `type=1006`), so it must be read from the
		/// victim's granted-talent list (GivenTalentDamage) instead. See JudgeClause.</summary>
		DeferredGrant = 7
	}

	/// <summary>
	/// Single decision point for a clause, shared by the display path and the diagnostic dump so the
	/// logged reason always matches what the arithmetic did.
	///
	/// <paramref name="tier"/> is the ability's 耐久-tier list (if any) when this ability mentions the
	/// damage keyword exactly once: it supplies the number for clauses that carry the keyword but no
	/// value of their own ("耐久が減少するほど被ダメージが減少"), see TieredModifier.
	/// </summary>
	private static ClauseVerdict JudgeClause(string clause, string ctx, string keyword, bool wantPhys, bool wantMagic,
		bool vanguard, bool rearguard, int hpPct, BattleObject self, BattleObject target, TieredModifier.Spec tier,
		out double factor, out string note)
	{
		factor = 1.0;
		note = null;
		try
		{
			// "被ダメージ" written about the ENEMY. The 刻印 "ブロックしている敵の被ダメージ+10%(前衛のみ)"
			// is carried by the attacker but phrased as the target's damage taken, so the 与ダメージ keyword
			// never matched it and the whole modifier used to fall into the residual. Same shape:
			// "毒状態の敵全ての被ダメージ+12%" (ウロロス AS2), "毒/凍結/火傷状態の敵すべての被ダメージが
			// それぞれ+15%" (マッドシーカー AS2).
			//  * on the ATTACKER side it raises the damage this hit deals  -> accepted here;
			//  * on the VICTIM side the same wording means "my ENEMIES take more" -- it says nothing about
			//    the damage *I* take, so it must not be folded into the victim's 被伤害補正.
			// The attacker's own defensive engravings ("現在耐久が20%以下の場合、被ダメージ-40%") never
			// name an enemy and therefore stay out of the damage-dealt side.
			bool victimSide = victimSideOf(keyword);
			bool enemyTakes = clause.IndexOf("被ダメージ", System.StringComparison.Ordinal) >= 0
				&& (clause.IndexOf("敵", System.StringComparison.Ordinal) >= 0
					|| clause.IndexOf("相手", System.StringComparison.Ordinal) >= 0
					|| clause.IndexOf("対象", System.StringComparison.Ordinal) >= 0);
			string kw = keyword;
			if (enemyTakes)
			{
				if (victimSide) return ClauseVerdict.ConditionFailed;
				kw = "被ダメージ";
			}
			if (clause.IndexOf(kw, System.StringComparison.Ordinal) < 0) return ClauseVerdict.NoKeyword;
			bool saysVan = clause.Contains("前衛");
			bool saysRear = clause.Contains("後衛");
			if (saysVan && !saysRear && !vanguard) return ClauseVerdict.SkippedPosition;
			if (saysRear && !saysVan && !rearguard) return ClauseVerdict.SkippedPosition;
			bool saysPhys = clause.Contains("物理");
			bool saysMagic = clause.Contains("魔法");
			if (saysPhys && !saysMagic && !wantPhys) return ClauseVerdict.SkippedAttribute;
			if (saysMagic && !saysPhys && !wantMagic) return ClauseVerdict.SkippedAttribute;
			if (!TargetAttributeMatches(clause, target)) return ClauseVerdict.ConditionFailed;

			// "ブロックしている敵の被ダメージ+10%（前衛のみ）" (刻印 id=26) is NOT a bonus to the carrier's
			// own hits. Its talents are `type=1004 AddTalent` + payload `type=1006 被伤害- p=[-10]`
			// (master dump: engraving_mutate id=26, timing=Block, cond=GiveTalent(1)): the carrier hands
			// the modifier to the enemy it blocks, so it raises that enemy's damage taken from EVERY
			// attacker, the carrier included only through the grant.
			//
			// MEASURED 2026-10-03 (battle_411001_...125554): this branch used to fold ×1.10 per copy
			// into the carrier's OWN damage. All 53 hits made by the carrier (エヴァラス・フラウ, two
			// copies) came out ×1.210 too high -- residual 0.826 = 1/1.21 on every single one -- while
			// the very same ×1.21 was MISSING on 1791 hits by the other four attackers. Folding it here
			// was therefore wrong in both directions at once.
			//
			// The authoritative channel is the victim's granted-talent list (`m_giveTalentData`), read
			// per hit by GivenTalentDamage. This text branch now only reports and stands aside.
			if (enemyTakes && clause.IndexOf("ブロック", System.StringComparison.Ordinal) >= 0)
			{
				int blocked = BlockGate(self, target);
				note = (blocked == 1) ? "阻挡中→记入受击方赋予天赋通道"
					: (blocked == 0 ? "未阻挡(不成立)" : "阻挡判定不可读");
				GivenClauseDeferred++;
				return ClauseVerdict.DeferredGrant;
			}

			// status-based condition, e.g. "暗闇状態の敵への与ダメージ+50%": the target must actually
			// carry that ailment right now. This is what used to make such clauses 未计入.
			// The words come from the CLAUSE (StatusTokens) and are matched against the unit's own buffs
			// with tolerant comparison; the game's name table is only used to decide whether a word is a
			// status at all (so an unrelated word is never read as a failed condition).
			var stTokens = StatusTokens(clause);
			var stVerified = new System.Collections.Generic.List<string>();
			var stConsume = new System.Collections.Generic.List<string>();
			int stHits = 0;
			if (stTokens.Count > 0)
			{
				BattleObject who = (clause.Contains("敵") || enemyTakes) ? target : self;
				bool known = false;
				for (int i = 0; i < stTokens.Count; i++)
				{
					bool k;
					bool hit = HasStatusLike(who, stTokens[i], out k);
					if (k) known = true;
					if (hit) { stHits++; stVerified.Add(stTokens[i]); }
				}
				if (stHits > 0)
				{
					// consume EVERY token of the clause (also the ones we could not match), so the
					// generic keyword table cannot send the clause back to 条件性,未计入
					stConsume.AddRange(stTokens);
				}
				else if (known) return ClauseVerdict.ConditionFailed;
			}
			else
			{
				var dictNames = ClauseStatusNames(clause);
				if (dictNames.Count > 0)
				{
					BattleObject who = (clause.Contains("敵") || enemyTakes) ? target : self;
					for (int i = 0; i < dictNames.Count; i++)
					{
						bool k;
						if (HasStatusLike(who, dictNames[i], out k)) { stHits++; stVerified.Add(dictNames[i]); }
					}
					if (stHits == 0) return ClauseVerdict.ConditionFailed;
					stConsume.AddRange(dictNames);
				}
			}

			CondState st;
			string condNote;
			st = EvalConditions(ctx, hpPct, stConsume, out condNote);
			if (st == CondState.Failed) return ClauseVerdict.ConditionFailed;
			double f = ParseDamageModifier(clause, kw);
			if (f == 1.0 && tier != null)
			{
				// the value lives in a sibling clause: pick the bracket the unit's 耐久% is in
				double tf;
				string tn;
				if (TieredModifier.TryEvaluate(tier, hpPct, clause, out tf, out tn))
				{
					factor = tf;
					note = tn;
					return ClauseVerdict.Counted;
				}
				// tier list present but no bracket applies (e.g. 耐久100% vs 9/6/3割) -> inactive
				return ClauseVerdict.ConditionFailed;
			}
			if (f == 1.0) return (st == CondState.Unknown) ? ClauseVerdict.Conditional : ClauseVerdict.NotParsed;
			if (st == CondState.Unknown) return ClauseVerdict.Conditional;
			// "それぞれ" clauses stack once per satisfied status (毒+火傷 -> ×1.15² )
			if (stHits > 1 && f > 0.0 && clause.Contains("それぞれ"))
			{
				double acc = 1.0;
				for (int i = 0; i < stHits; i++) acc *= f;
				f = acc;
				condNote = (string.IsNullOrEmpty(condNote) ? "" : (condNote + ",")) + "状态×" + stHits;
			}
			factor = f;
			if (!string.IsNullOrEmpty(condNote)) note = condNote;
			return ClauseVerdict.Counted;
		}
		catch { return ClauseVerdict.NotParsed; }
	}

	/// <summary>True when <paramref name="keyword"/> is the victim-side keyword.</summary>
	private static bool victimSideOf(string keyword)
	{
		return keyword == "被ダメージ";
	}

	/// <summary>
	/// Is <paramref name="target"/> one of the objects <paramref name="self"/> is currently blocking?
	/// 1 = yes, 0 = no, -1 = cannot tell (the block list could not be read).
	/// `FieldObject.GetBlockObjects()` is the game's own engaged-block list.
	/// </summary>
	private static int BlockGate(BattleObject self, BattleObject target)
	{
		try
		{
			if (GameRef.IsNull(self)) return -1;
			if (GameRef.IsNull(target)) return -1;
			bool blocking = false;
			try { blocking = self.IsBlocking; } catch { }
			try
			{
				var list = self.GetBlockObjects();
				if (list != null)
				{
					long tp = 0;
					try { tp = target.Pointer.ToInt64(); } catch { }
					for (int i = 0; i < list.Count; i++)
					{
						var o = list[i];
						if (o == null) continue;
						long op = 0;
						try { op = ((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)(object)o).Pointer.ToInt64(); } catch { }
						if (op != 0 && op == tp) return 1;
					}
					// list readable: an empty list means "not blocking anything right now"
					return 0;
				}
			}
			catch { }
			// list unreadable: "not blocking at all" is still conclusive
			return blocking ? -1 : 0;
		}
		catch { return -1; }
	}

	/// <summary>
	/// 1.5.3: the rule moved to <see cref="ClauseStatusRun.StatusTokens"/> so recon_probe can EXECUTE it.
	/// This is the site that decides status-conditioned clauses, so the と/や joiner fix had to land there.
	/// </summary>
	private static System.Collections.Generic.List<string> StatusTokens(string clause)
	{
		return ClauseStatusRun.StatusTokens(clause);
	}

	/// <summary>Normalise a status word for comparison (傷/伤 are the same ailment, 状態 is a suffix).</summary>
	private static string NormStatus(string s)
	{
		if (string.IsNullOrEmpty(s)) return "";
		s = s.Trim();
		if (s.EndsWith("状態")) s = s.Substring(0, s.Length - 2);
		s = s.Replace('傷', '伤');
		return s;
	}

	/// <summary>
	/// "火属性への与ダメージが更に2倍" only applies when the target really has that attribute.
	/// Returns true when the clause carries no such restriction, or when it matches.
	/// </summary>
	private static bool TargetAttributeMatches(string clause, BattleObject target)
	{
		try
		{
			int idx = clause.IndexOf("属性への", System.StringComparison.Ordinal);
			if (idx <= 0) return true;
			string attr = clause.Substring(idx - 1, 1);
			if (attr != "火" && attr != "水" && attr != "風" && attr != "土" && attr != "無") return true;
			string tAttr = AttrName(target);
			if (string.IsNullOrEmpty(tAttr)) return true;
			return tAttr.Contains(attr);
		}
		catch { return true; }
	}

	/// <summary>
	/// Evaluate the conditions of a clause context against the unit's HP%.
	/// Numeric HP conditions ("耐久がN%以下/未満/以上/超" and the 割 form "耐久が9割以下") are evaluated
	/// and consumed; a failing one rejects the clause. Any remaining state-dependent wording yields
	/// Unknown, so the modifier is displayed but NOT folded into the arithmetic.
	///
	/// A digit that is directly preceded by '/' belongs to a multi-threshold list ("9/6/3割以下の場合、
	/// それぞれ-10/-25/-40%") and is left alone here: TieredModifier owns that shape and picks the
	/// bracket from the unit's current 耐久%.
	/// </summary>
	private static CondState EvalConditions(string ctx, int hpPct, System.Collections.Generic.List<string> statusNames, out string condNote)
	{
		condNote = null;
		try
		{
			if (string.IsNullOrEmpty(ctx)) return CondState.Ok;
			bool[] consumed = new bool[ctx.Length];
			string collected = null;
			for (int i = 0; i < ctx.Length; i++)
			{
				bool isPct = ctx[i] == '%' || ctx[i] == '％';
				bool isWari = ctx[i] == '割';
				if (!isPct && !isWari) continue;
				int s = i - 1;
				while (s >= 0 && char.IsDigit(ctx[s])) s--;
				s++;
				if (s > i - 1) continue;
				if (s - 1 >= 0 && (ctx[s - 1] == '/' || ctx[s - 1] == '／')) continue;   // tier list member
				int v;
				if (!int.TryParse(ctx.Substring(s, i - s), out v)) continue;
				if (isWari)
				{
					if (v <= 0 || v > 10) continue;   // "9割" = 90%; anything else is not a 耐久 bracket
					v *= 10;
				}
				int t = i + 1;
				string tail = ctx.Substring(t, System.Math.Min(6, ctx.Length - t));
				bool le = tail.StartsWith("以下"), lt = tail.StartsWith("未満");
				bool ge = tail.StartsWith("以上"), gt = tail.StartsWith("超");
				if (!le && !lt && !ge && !gt) continue;
				int b = System.Math.Max(0, s - 12);
				string pre = ctx.Substring(b, s - b);
				if (pre.IndexOf("耐久") < 0 && pre.IndexOf("HP") < 0 && pre.IndexOf("ライフ") < 0) continue;
				bool ok = le ? (hpPct <= v) : lt ? (hpPct < v) : ge ? (hpPct >= v) : (hpPct > v);
				if (!ok) return CondState.Failed;
				// the condition is consumed, but it is worth SHOWING: without it the reader cannot tell
				// why a conditional entry ("現在耐久が50%以下の場合、被ダメージ-10%") is in the list at all
				string piece = "耐久" + (le ? "≤" : lt ? "<" : ge ? "≥" : ">") + v + "%";
				collected = (collected == null) ? piece : (collected + "," + piece);
				int te = t + (gt ? 1 : 2);
				int ci = ctx.IndexOf("場合", te);
				if (ci >= 0 && ci - te <= 6) te = ci + 2;
				else
				{
					ci = ctx.IndexOf("とき", te);
					if (ci >= 0 && ci - te <= 6) te = ci + 2;
				}
				for (int k = b; k < System.Math.Min(ctx.Length, te); k++) consumed[k] = true;
			}
			condNote = collected;
			var rest = new StringBuilder(ctx.Length);
			for (int i = 0; i < ctx.Length; i++) if (!consumed[i]) rest.Append(ctx[i]);
			string r = rest.ToString();
			// A status condition that has already been VERIFIED ("火傷状態の敵に対して与ダメージ+15%",
			// target really is 火傷) must not come back as "Unknown" through the generic keyword list
			// below -- that was why status-conditioned modifiers were displayed as 条件性,未计入 forever.
			// Only names the target really has are removed, PLUS the whole "◯◯状態" run they belong to
			// ("毒/火傷状態" writes 状態 once for the list, so the sibling names must go too).
			if (statusNames != null && statusNames.Count > 0)
			{
				for (int i = 0; i < statusNames.Count; i++)
				{
					if (string.IsNullOrEmpty(statusNames[i])) continue;
					r = r.Replace(statusNames[i] + "状態", "");
					r = r.Replace(statusNames[i], "");
				}
				r = StripVerifiedStateRuns(r, statusNames);
			}
			string[] markers = new string[]
			{
				"場合", "ほど", "に応じて", "につれて", "残り", "状態異常", "スタン", "毒", "火傷",
				"凍結", "石化", "恐怖", "暗闇", "狂気", "撃破", "倒した", "死亡", "確率", "毎に", "中は", "間は"
			};
			for (int i = 0; i < markers.Length; i++)
				if (r.IndexOf(markers[i], System.StringComparison.Ordinal) >= 0) return CondState.Unknown;
			return CondState.Ok;
		}
		catch { condNote = null; return CondState.Unknown; }
	}

	/// <summary>
	/// 1.5.3: the rule moved to <see cref="ClauseStatusRun.StripVerifiedStateRuns"/> so recon_probe can
	/// EXECUTE it; this wrapper exists only so the clause pipeline stays unchanged.
	/// </summary>
	private static string StripVerifiedStateRuns(string s, System.Collections.Generic.List<string> verified)
	{
		return ClauseStatusRun.StripVerifiedStateRuns(s, verified);
	}

	/// <summary>
	/// Pull the numeric modifier out of a clause, searching AFTER the damage keyword so that an HP
	/// condition like "耐久が20%以下の場合、被ダメージ-40%" resolves to 40% (not the 20%).
	/// Returns a multiplier (1.15 for "+15%", 0.60 for "-40%"); 1.0 when nothing parseable is found.
	/// </summary>
	private static double ParseDamageModifier(string clause, string keyword)
	{
		try
		{
			int k = clause.IndexOf(keyword, System.StringComparison.Ordinal);
			if (k < 0) return 1.0;
			for (int i = k + keyword.Length; i < clause.Length; i++)
			{
				if (!char.IsDigit(clause[i])) continue;
				int s = i;
				while (i < clause.Length && char.IsDigit(clause[i])) i++;
				if (i >= clause.Length) return 1.0;
				if (clause[i] != '%' && clause[i] != '％') continue;
				int v;
				if (!int.TryParse(clause.Substring(s, i - s), out v)) return 1.0;
				// sign: prefer an explicit +/- right before the number, else scan the surrounding words.
				// The tail matters too: "被ダメージを50%軽減" puts the reduction word AFTER the %.
				string before = clause.Substring(k, s - k);
				string seg = clause.Substring(k, System.Math.Min(clause.Length - k, i + 1 - k + 8));
				bool minus = before.EndsWith("-") || before.EndsWith("−") || before.EndsWith("－")
					|| seg.Contains("軽減") || seg.Contains("カット") || seg.Contains("ダウン")
					|| seg.Contains("低下") || seg.Contains("減少") || seg.Contains("抑制")
					|| seg.Contains("ダメージ-") || seg.Contains("ダメージ−") || seg.Contains("ダメージ－");
				bool plus = before.EndsWith("+") || before.EndsWith("＋")
					|| seg.Contains("アップ") || seg.Contains("上昇") || seg.Contains("増加") || seg.Contains("増")
					|| seg.Contains("ダメージ+") || seg.Contains("ダメージ＋");
				// an explicit minus wins over a stray "増" elsewhere in the clause
				double f = minus ? (1.0 - v / 100.0) : (plus ? (1.0 + v / 100.0) : (1.0 + v / 100.0));
				if (f < 0.0) f = 0.0;
				if (f > 5.0) f = 5.0;
				return f;
			}
			// multiplier written as "N倍" instead of a percentage, e.g. "与ダメージが更に2倍となる"
			for (int i = k + keyword.Length; i < clause.Length; i++)
			{
				if (!char.IsDigit(clause[i])) continue;
				int s2 = i;
				while (i < clause.Length && (char.IsDigit(clause[i]) || clause[i] == '.')) i++;
				if (i < clause.Length && clause[i] == '倍')
				{
					double d;
					if (double.TryParse(clause.Substring(s2, i - s2), out d) && d > 0.0 && d <= 20.0) return d;
				}
			}
			return 1.0;
		}
		catch { return 1.0; }
	}

	/// <summary>
	/// Split an ability description into clauses at common separators, reporting each clause's end
	/// offset so a condition in an earlier clause can still be applied to a later one.
	///
	/// NOTE: clauses are NOT length-capped here. They used to be cut to 70 chars, which silently
	/// deleted the modifier when it sat further into a long sentence (the [ABIL] dump proved this:
	/// "…を作り出す␣␣自身と…の敵への与ダメージ+50％" lost its 与ダメージ clause entirely).
	/// Long text is handled by the row wrapper at render time instead.
	/// </summary>
	private static void SplitClauses(string text, out System.Collections.Generic.List<string> clauses, out System.Collections.Generic.List<int> ends)
	{
		clauses = new System.Collections.Generic.List<string>();
		ends = new System.Collections.Generic.List<int>();
		if (string.IsNullOrEmpty(text)) return;
		int start = 0;
		for (int i = 0; i <= text.Length; i++)
		{
			bool brk = (i == text.Length);
			if (!brk)
			{
				char ch = text[i];
				brk = (ch == '、' || ch == '。' || ch == '\n' || ch == '\r' || ch == '\u3000' || ch == '．' || ch == '，');
				// a run of two or more ASCII spaces is also a separator in these texts
				if (!brk && ch == ' ' && i + 1 < text.Length && text[i + 1] == ' ')
				{
					brk = true;
					int j = i;
					while (j < text.Length && text[j] == ' ') j++;
					string piece2 = text.Substring(start, i - start).Trim();
					if (piece2.Length > 0) { clauses.Add(piece2); ends.Add(i); }
					start = j;
					i = j - 1;
					continue;
				}
			}
			if (!brk) continue;
			string piece = text.Substring(start, i - start).Trim();
			if (piece.Length > 0)
			{
				clauses.Add(piece);
				ends.Add(i);
			}
			start = i + 1;
		}
	}
}
