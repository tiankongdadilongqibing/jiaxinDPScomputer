using System.Text;
using UnityEngine;

namespace DpsMeter;

/// <summary>
/// Ability (skill / artifact) text: caching, clause splitting and the scan that turns
/// 与ダメージ/被ダメージ clauses into multipliers.
/// </summary>
public static partial class CompositionProbe
{
	// ---- ability (skill / artifact) text: the source of 与/被ダメージ補正 ----
	private static readonly System.Collections.Generic.Dictionary<string, string> _abilityText = new System.Collections.Generic.Dictionary<string, string>();

	/// <summary>
	/// Ability description text, cached per (unit identity, list index, ability id, level).
	///
	/// The key needs ALL of these:
	///  * list index -- different abilities share the same id (observed: several id=1 entries on one
	///    unit: "攻撃力+30%" twice and "魔法与ダメージ+10%"); a key of just id|entry made the magic
	///    entries read the first entry's text, so their 与ダメージ clause was never counted.
	///  * unit identity -- EntryId alone can repeat across units (mirror matches), which would leak one
	///    unit's ability text into another's.
	///  * level -- stacked same-name entries may differ only by level.
	/// </summary>
	private static string GetAbilityText(BattleObject bo, Ability ab, int index, int entry, out int id, out string text)
	{
		id = 0;
		text = "";
		try
		{
			if (ab == null) return "";
			var data = ab.Data;
			if (data == null) return "";
			try { id = data.Id; } catch { }
			int lv = 0;
			try { lv = ab.Level; } catch { }
			long uid = entry;
			try { uid = bo.Pointer.ToInt64(); } catch { }
			string key = uid + "|" + index + "|" + id + "|" + lv;
			string cached;
			if (_abilityText.TryGetValue(key, out cached)) { text = cached; return text; }
			try { text = data.GetText(bo.Data); } catch { }
			if (text == null) text = "";
			text = StripMarkup(text);   // "<color=#ffa500>…</color>" tags must not reach the overlay
			if (text.Length > 400) text = text.Substring(0, 400);
			if (_abilityText.Count < 900) _abilityText[key] = text;
			return text;
		}
		catch { return ""; }
	}

	/// <summary>Remove rich-text markup ("&lt;color=#ffa500&gt;", "&lt;/color&gt;", "&lt;link=…&gt;") from an ability
	/// description. The tags used to leak into the overlay (…与ダメージ+15%&lt;/color=#ffa500&gt;).</summary>
	private static string StripMarkup(string s)
	{
		try
		{
			if (string.IsNullOrEmpty(s) || s.IndexOf('<') < 0) return s;
			var sb = new StringBuilder(s.Length);
			int depth = 0;
			for (int i = 0; i < s.Length; i++)
			{
				char c = s[i];
				if (c == '<') { depth = 1; continue; }
				if (depth > 0)
				{
					if (c == '>') depth = 0;
					continue;
				}
				sb.Append(c);
			}
			return sb.ToString();
		}
		catch { return s; }
	}

	/// <summary>
	/// Ability clauses that can affect THIS hit. The game exposes no numeric damage-dealt/taken stat
	/// (BuffParamData has no such BuffTarget, BattleObject has no such property), but the modifiers
	/// come from abilities and AbilityData.GetText() returns their description -- which is where
	/// strings like "与ダメージ+15%(前衛のみ)" come from.
	///
	/// An ability's text is a whole paragraph, so it is split into clauses and only the clauses that
	/// actually mention the damage modifier are kept (this is what used to be shown as a truncated
	/// blob ending in "…"). Clauses are then filtered by:
	///   * keyword      : 被ダメージ for the victim side, 与ダメージ for the attacker side
	///   * position     : "前衛のみ"/"後衛のみ" against this unit's row
	///   * attack type  : "物理"/"魔法" against this hit (a penetration hit gets neither)
	///   * HP condition : "耐久が20%以下の場合 …" is dropped when the unit's HP is above that,
	///                    so only modifiers that are actually in effect are listed
	/// Identical clauses are de-duplicated.
	/// </summary>
	public static string AbilityBrief(BattleObject bo, int max, int hitType, bool victimSide)
	{
		double mult;
		return AbilityScan(bo, max, hitType, victimSide, null, out mult);
	}

	/// <summary>
	/// Like AbilityBrief, but also returns the product of the PARSED damage modifiers found in those
	/// clauses ("与ダメージ+15%" -> 1.15, "被ダメージ-40%" -> 0.60, "与ダメージが2倍" -> 2.0).
	///
	/// Two rules matter here:
	///  * DUPLICATES MULTIPLY. Engravings allow several identically-named entries, so equal clauses
	///    are counted once per occurrence (the text is only printed once, with a "×N层" marker).
	///  * Only modifiers that are actually IN EFFECT are counted. A clause is evaluated against the
	///    text up to that clause (so a condition split off by clause splitting is still seen); when the
	///    condition cannot be evaluated -- e.g. "耐久が減少するほど…軽減", which scales with missing HP
	///    and is ~0 at full HP -- the clause is shown as 条件性,未计入 instead of being folded in.
	///  * `other` is the counterpart unit (the target for the attacker side), used to check clauses
	///    that are restricted to a target attribute, e.g. "火属性への与ダメージが更に2倍".
	/// </summary>
	public static string AbilityScan(BattleObject bo, int max, int hitType, bool victimSide, BattleObject other, out double mult, FoldContext foldCtx = null)
	{
		mult = 1.0;
		try
		{
			if (GameRef.IsNull(bo)) return "";
			var list = bo.m_ability;
			if (list == null) return "";
			string keyword = victimSide ? "被ダメージ" : "与ダメージ";
			bool vanguard = false, rearguard = false;
			int hpPct = 100;
			try { vanguard = bo.IsVanguard; } catch { }
			try { rearguard = bo.IsRearguard; } catch { }
			// 耐久% AS OF ATTACK START when a snapshot exists: the game evaluates "耐久がN%以下" BEFORE this
			// hit is subtracted, so a late read puts the unit one bracket lower (residual > 1).
			try
			{
				int snap = SnapshotHp(bo);
				hpPct = (snap >= 0) ? snap : bo.LifePercent;
			}
			catch { }
			bool wantPhys = hitType == 1 || hitType == 5;
			bool wantMagic = hitType == 2 || hitType == 5;
			int entry = 0;
			try { entry = bo.EntryId; } catch { }

			// raw-text dump for debugging (once per unit per battle)
			DumpAbilities(bo, victimSide ? "受击方" : "攻击方", hitType, victimSide, other);

			var order = new System.Collections.Generic.List<string>();
			var cnt = new System.Collections.Generic.Dictionary<string, int>();
			var fac = new System.Collections.Generic.Dictionary<string, double>();
			var unc = new System.Collections.Generic.Dictionary<string, ClauseVerdict>();
			var notes = new System.Collections.Generic.Dictionary<string, string>();
			var pres = new System.Collections.Generic.Dictionary<string, string>();
			int accepted = 0;
			int overflow = 0;

			for (int i = 0; i < list.Count; i++)
			{
				string text = null;
				int abId = 0;
				try
				{
					int id;
					text = GetAbilityText(bo, list[i], i, entry, out id, out text);
					abId = id;
				}
				catch { }
				if (string.IsNullOrEmpty(text)) continue;

				// provenance: the ability's own name (artifact / skill). Two cases are NOT shown:
				//  * the 素質 rows that carry the character's own name -- no information gained;
				//  * a name that is a CHARACTER name at all. 素質 rows are named after their owner, and
				//    the row that レヴナント carries is labelled "シゼル＝メ" in the master data (copied
				//    row, old name left in place), which would read like a wrong attribution.
				// Unnamed entries are 刻印 / 変異 (their master data has no name field value).
				string pre = "";
				try
				{
					var ad = list[i].Data;
					string abName = (ad != null) ? ad.Name : null;
					if (!string.IsNullOrEmpty(abName) && abName != bo.Name && !CharacterNames.IsUnitName(abName))
						pre = "[" + abName + "] ";
				}
				catch { }

				// "耐久が減少するほど…軽減 (現在耐久が9/6/3割以下の場合、それぞれ-10/-25/-40%)" keeps
				// the keyword and the numbers in two different clauses. Only look for such a list when
				// this ability mentions the keyword exactly once, so the numbers cannot belong to a
				// DIFFERENT modifier of the same ability (ミューゼ's 魔法防御力 tiers, for instance).
				TieredModifier.Spec tier = null;
				if (TieredModifier.CountOccurrences(text, keyword) == 1) tier = TieredModifier.Get(text);

				System.Collections.Generic.List<string> clauses;
				System.Collections.Generic.List<int> ends;
				SplitClauses(text, out clauses, out ends);
				for (int ci2 = 0; ci2 < clauses.Count; ci2++)
				{
					string clause = clauses[ci2];
					int clauseEnd = ends[ci2];
					// condition context = the ability text up to the end of this clause, so a condition
					// that lives in an earlier clause still applies to this one
					string ctx = text.Substring(0, System.Math.Min(clauseEnd, text.Length));
					double f;
					string note;
					ClauseVerdict v = JudgeClause(clause, ctx, keyword, wantPhys, wantMagic, vanguard, rearguard, hpPct, bo, other, tier, out f, out note);
					// SkippedPosition / SkippedAttribute mean "this clause cannot apply to THIS hit"
					// (wrong row, or physical-only vs a magic hit). They used to be listed with no
					// marker at all, which looked like an unexplained entry -> do not list them.
					if (v == ClauseVerdict.NoKeyword || v == ClauseVerdict.ConditionFailed
						|| v == ClauseVerdict.SkippedPosition || v == ClauseVerdict.SkippedAttribute) continue;
					accepted++;
					if (v == ClauseVerdict.Counted)
					{
						mult *= f;
						// 1.5.0 (A1): record provenance PER OCCURRENCE, matching the arithmetic. The
						// display keeps only the FIRST occurrence's factor (`fac[clause]`), so the export
						// can now show that two copies of one clause folded different numbers.
						if (foldCtx != null)
							foldCtx.Add(victimSide ? "vic" : "atk", "text",
								"text#" + i + "/" + abId + "/c" + ci2, f, pre + clause);
					}
					else f = 1.0;
					int c;
					if (cnt.TryGetValue(clause, out c)) cnt[clause] = c + 1;
					else if (order.Count < max)
					{
						order.Add(clause);
						cnt[clause] = 1;
						fac[clause] = f;
						unc[clause] = v;
						notes[clause] = note;
						pres[clause] = pre;
					}
					else overflow++;   // distinct clause beyond the display cap
				}
			}
			if (accepted == 0) return "";
			var sb = new StringBuilder(140);
			for (int i = 0; i < order.Count; i++)
			{
				if (i > 0) sb.Append('、');
				string cl = order[i];
				string pv;
				if (pres.TryGetValue(cl, out pv)) sb.Append(pv);
				sb.Append(cl);
				string nt;
				if (notes.TryGetValue(cl, out nt) && !string.IsNullOrEmpty(nt)) sb.Append('(').Append(nt).Append(')');
				if (unc[cl] == ClauseVerdict.Conditional) sb.Append("(条件性,未计入)");
				else if (unc[cl] == ClauseVerdict.NotParsed) sb.Append("(未能解析为倍率,未计入)");
				else if (unc[cl] == ClauseVerdict.DeferredGrant) sb.Append("(非持有者自身增益:由受击方赋予天赋通道结算)");
				else if (fac[cl] != 1.0) sb.Append("→×").Append(fac[cl].ToString("F2"));
				if (cnt[cl] > 1) sb.Append("(×").Append(cnt[cl]).Append("层)");
			}
			// if some accepted entries did not fit the display cap, say so instead of hiding them
			if (overflow > 0)
				sb.Append("…(另有 ").Append(overflow).Append(" 条已计入但未显示)");
			if (Plugin.CfgAbilityDump != null && Plugin.CfgAbilityDump.Value)
				RuntimeLog.Write("[ABIL]   === " + (victimSide ? "受击方" : "攻击方") + " 合计倍率 ×" + mult.ToString("F4")
					+ "  已接受 " + accepted + " 条,显示 " + order.Count + " 条 ===");
			return sb.ToString();
		}
		catch { return ""; }
	}
}
