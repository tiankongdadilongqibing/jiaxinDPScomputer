using System.Text;
using UnityEngine;

namespace DpsMeter;

/// <summary>
/// Battle-wide damage rules: modifiers that are not a property of the owner's own hits.
///
/// Two shapes are supported, both keyed by the rule owner:
///   * "毒/火傷状態の敵すべての被ダメージがそれぞれ+15%" -- the VICTIM must be an enemy of the owner;
///   * "編成時、味方ヴァイスの魔法攻撃の与ダメージ+15%"        -- the ATTACKER must be an ally of the owner.
/// Rules are registered when a unit is created (BattleObject.SetupAbility) and are deliberately
/// NOT cleared when a battle ends: the next battle's units are created before the previous
/// session is finalised. Only dead owners are reclaimed (see ClearGlobalRules).
/// </summary>
public static partial class CompositionProbe
{
	// ---- battle-wide "enemy takes more damage" debuffs -------------------------------------------
	//
	// "毒/火傷状態の敵全ての被ダメージがそれぞれ+15%" (海魔の残滓) is not a modifier of the ability
	// OWNER's hits: it marks the enemy itself, so every attacker profits from it. Until now the clause
	// was only folded in while its owner was the attacker, and every other unit's hit carried it in the
	// residual. Rules are collected per battle (owner + condition + value) and applied to any hit whose
	// victim is an ENEMY of the owner, in addition to the ordinary attacker/victim text scan.
	private sealed class GlobalRule
	{
		public long Owner;
		public BattleObject OwnerObj;
		public string OwnerName = "";
		public string Source = "";
		public string Text = "";
		public double Factor = 1.0;
		public bool PerStatus;
		public bool Vanguard;      // enemy-takes: the OWNER's row / ally-buff: the ATTACKER's row
		public bool Rearguard;
		/// <summary>true = "enemy takes more damage" (victim must be an enemy of the owner);
		/// false = "allied attacks deal more damage" (attacker must be an ally of the owner).</summary>
		public bool EnemyTakes;
		public bool MagicOnly;
		public bool PhysOnly;
		public System.Collections.Generic.List<string> Tokens = new System.Collections.Generic.List<string>();
	}

	private static readonly System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<GlobalRule>> _globalRules =
		new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<GlobalRule>>();

	/// <summary>Unit name registered per pointer, so a reused IL2CPP pointer is detected and re-scanned.</summary>
	private static readonly System.Collections.Generic.Dictionary<long, string> _ruleOwnerName =
		new System.Collections.Generic.Dictionary<long, string>();

	/// <summary>Drop the rules of units that no longer exist (called by the table-size valve in
	/// RegisterGlobalDebuffs; the table itself is never cleared when a battle ends).
	///
	/// The table must NOT be wiped as a whole. A battle can be finalised while the units of the SAME
	/// or of the NEXT battle are already alive and registered:
	///   * the idle timeout ("Battle idle" after 3 s without events) fires during a lull while the
	///     units keep fighting, and
	///   * the next battle's units are created (SetupAbility -> RegisterGlobalDebuffs) during the
	///     scene load that can happen BEFORE the previous session is finalised.
	/// Dropping their entries made a battle-wide buff disappear until its owner happened to act again,
	/// because RegisterGlobalDebuffs is only called for the attacker/victim of a hit. Measured in
	/// BepInEx\config\dpsmeter_runtime.log: ミャウラ's 味方ヴァイスの魔法攻撃の与ダメージ+15％ was
	/// registered at 01:29:32.161, wiped by the 01:29:53.111 idle finalize, and re-appeared only at
	/// 01:29:53.215 (her first action) -- so [賢導]トレイラ's hits at t=0.0 of that session show
	/// 与伤害×1.491 instead of ×1.714 (export battle_9999_20260927_012953.json).</summary>
	internal static void ClearGlobalRules()
	{
		try
		{
			var dead = new System.Collections.Generic.List<long>();
			foreach (var kv in _globalRules)
			{
				var list = kv.Value;
				bool alive = false;
				if (list != null)
				{
					for (int i = 0; i < list.Count; i++)
					{
						try
						{
							if (!GameRef.IsNull(list[i].OwnerObj)) { alive = true; break; }
						}
						catch { }
					}
				}
				// an empty list is only the "scanned, found nothing" memo: drop it so the (cheap,
				// ability-text-cached) scan is repeated once for units that are still alive
				if (!alive) dead.Add(kv.Key);
			}
			for (int i = 0; i < dead.Count; i++)
			{
				_globalRules.Remove(dead[i]);
				_ruleOwnerName.Remove(dead[i]);
			}
		}
		catch { }
	}

	/// <summary>Hit type of the composition currently being built (used by the ally-attack rules).</summary>
	private static int _curHitType = -1;

	/// <summary>How many rule decisions have been logged this battle (diagnostics only).</summary>
	private static int _ruleLogCount;

	/// <summary>
	/// 1.3.6/1.3.8, RELOCATED in 1.5.0: the 敌方受伤 factors this hit's global-rule path is RESPONSIBLE
	/// for -- rules that exist for this victim and passed every owner/team/position/attribute gate.
	/// Consumed by <see cref="GivenTalentDamage"/> so a granted modifier that this path already models is
	/// not added a second time.
	///
	/// 1.3.8 changed the meaning from "factors that FIRED" to "factors that COULD fire", i.e. it is
	/// recorded BEFORE the status-hit test. Reason, measured 2026-10-03 on battle_...140529: the granted
	/// list keeps the per-status copies after the status itself has expired (`give` was true for 39620 of
	/// 39714 entries, `isDeleted` for none), so a multiset "consume one copy per fired factor" left the
	/// stale copies behind and still over-counted by 1.15^2..1.15^3 on ~1,700 hits (residuals
	/// 0.756/0.658/0.657, plus their crit variants 1.249/1.437). What actually decides whether the grant
	/// is in force is the victim's status set, and this path evaluates exactly that -- so its verdict, not
	/// the grant list's length, owns these rules.
	///
	/// 1.5.0 moved the set out of this file into the per-hit <see cref="FoldContext"/>. The old static
	/// <c>Dictionary&lt;double,int&gt;</c> was overwritten by ANY caller of
	/// <see cref="ApplyGlobalDebuffs"/> -- including the diagnostics path, which recomputes the chain --
	/// so the cancellation depended on call ORDER rather than on data; and its stored counts were never
	/// read (verified at all 7 use sites). The DECISION RULE is unchanged (value-only, unbounded -- see
	/// <see cref="FoldedFactor"/>); what is new is that each entry carries the responsible rule's origin
	/// and counts how many granted copies it absorbed.
	/// </summary>

	/// <summary>Collect the battle-wide debuff rules of one unit (once per unit per battle).</summary>
	internal static void RegisterGlobalDebuffs(BattleObject bo)
	{
		try
		{
			if (GameRef.IsNull(bo)) return;
			long key = 0;
			try { key = bo.Pointer.ToInt64(); } catch { }
			if (key == 0) return;
			string me = "";
			try { me = bo.Name; } catch { }
			System.Collections.Generic.List<GlobalRule> existing;
			if (_globalRules.TryGetValue(key, out existing))
			{
				// the pointer can be REUSED by a different unit in a later battle: re-scan when the registered
				// owner is not this unit. RF4 family 2: the rule itself is GlobalRuleClassifier.SkipRescan.
				bool hasMemo = _ruleOwnerName.ContainsKey(key);
				var facts = new GlobalRuleClassifier.RegisterFacts
				{
					HasEntry = true,
					EntryCount = (existing == null) ? -1 : existing.Count,
					FirstRuleOwnerName = (existing != null && existing.Count > 0) ? existing[0].OwnerName : null,
					HasNameMemo = hasMemo,
					NameMemoOwner = hasMemo ? _ruleOwnerName[key] : null,
					CurrentName = me,
				};
				if (GlobalRuleClassifier.SkipRescan(facts)) return;
			}
			var rules = new System.Collections.Generic.List<GlobalRule>();
			// bound the table: entries of destroyed units are skipped at apply time, but they would
			// otherwise accumulate across battles. The table is never cleared at battle end any more,
			// so this valve is the only place that reclaims them -- and it must reclaim ONLY the dead
			// owners: a blanket wipe here would strip the battle-wide rules from the units that are
			// fighting right now, which is exactly the bug the "no clear at battle end" policy avoids.
			// RF4 family 2: the two valve thresholds are named policy constants, and the second test is
			// measured against the count AFTER the reclamation -- exactly as before.
			if (GlobalRuleClassifier.ShouldReclaim(_globalRules.Count))
			{
				ClearGlobalRules();
				if (GlobalRuleClassifier.ShouldClearAll(_globalRules.Count)) { _globalRules.Clear(); _ruleOwnerName.Clear(); }
			}
			_globalRules[key] = rules;   // register even when empty, so the scan happens once
			_ruleOwnerName[key] = me;
			var list = bo.m_ability;
			if (list == null) return;
			int entry = 0;
			try { entry = bo.EntryId; } catch { }
			for (int i = 0; i < list.Count; i++)
			{
				try
				{
					var ab = list[i];
					if (ab == null) continue;
					var data = ab.Data;
					if (data == null) continue;
					int id = 0;
					string raw = "";
					try { raw = GetAbilityText(bo, ab, i, entry, out id, out raw); } catch { }
					if (string.IsNullOrEmpty(raw)) continue;
					if (raw.IndexOf("被ダメージ", System.StringComparison.Ordinal) < 0
						&& raw.IndexOf("与ダメージ", System.StringComparison.Ordinal) < 0) continue;
					string nm = "";
					try { nm = data.Name; } catch { }
					System.Collections.Generic.List<string> cls;
					System.Collections.Generic.List<int> ends;
					SplitClauses(raw, out cls, out ends);
					for (int ci = 0; ci < cls.Count; ci++)
					{
						string cl = cls[ci];
						// RF4 family 2: "is this clause a battle-wide rule, and of which shape?" is decided by
						// GlobalRuleClassifier (offline-testable). The identity fields below stay here: they come
						// from the native object, and the factor still comes from ParseDamageModifier.
						GlobalRuleShape shape = GlobalRuleClassifier.Classify(cl, ParseDamageModifier);
						if (shape.Kind == GlobalRuleKind.None) continue;
						var r = new GlobalRule();
						r.Owner = key;
						r.OwnerObj = bo;
						r.OwnerName = me;
						r.Source = string.IsNullOrEmpty(nm) ? "" : ("[" + nm + "] ");
						r.Text = shape.Text;
						r.Factor = shape.Factor;
						r.EnemyTakes = shape.Kind == GlobalRuleKind.EnemyTakes;
						r.PerStatus = shape.PerStatus;
						r.Tokens = shape.Tokens;
						r.MagicOnly = shape.MagicOnly;
						r.PhysOnly = shape.PhysOnly;
						r.Vanguard = shape.Vanguard;
						r.Rearguard = shape.Rearguard;
						rules.Add(r);
					}
				}
				catch { }
			}
			// diagnostic: what was collected for this unit
			try
			{
				if (rules.Count > 0 && Plugin.CfgAbilityDump != null && Plugin.CfgAbilityDump.Value)
				{
					string on = "";
					try { on = bo.Name; } catch { }
					RuntimeLog.Write("[RULE] reg owner=" + on + " n=" + rules.Count);
					for (int i = 0; i < rules.Count; i++)
						RuntimeLog.Write("[RULE]   " + (rules[i].EnemyTakes ? "敌受伤" : "我攻击")
							+ " ×" + rules[i].Factor.ToString("F3")
							+ (rules[i].MagicOnly ? " 魔法限定" : "")
							+ "  " + rules[i].Text);
				}
			}
			catch { }
		}
		catch { }
	}

	/// <summary>
	/// Product of all battle-wide debuffs that apply to this hit (victim is an enemy of the rule owner and
	/// carries the required status). The current attacker's own rules are skipped -- its ordinary text scan
	/// already counted them.
	/// </summary>
	internal static double ApplyGlobalDebuffs(BattleObject atk, BattleObject victim, out string text, FoldContext ctx = null)
	{
		double m = 1.0;
		text = null;
		// 1.3.8 / 1.5.0: this hit's 敌方受伤 responsibility set now lives in the per-hit FoldContext. It
		// used to be a static dictionary that ANY other caller of this method silently overwrote --
		// including the diagnostics path, which recomputes the whole chain -- so the cancellation decision
		// depended on call ORDER rather than on data. A null context means "measure only": the arithmetic
		// below is unchanged, the provenance is simply not recorded.
		try
		{
			if (GameRef.IsNull(victim)) return 1.0;
			long atkKey = 0;
			bool atkVan = false, atkRear = false;
			try
			{
				if (!GameRef.IsNull(atk))
				{
					atkKey = atk.Pointer.ToInt64();
					atkVan = atk.IsVanguard;
					atkRear = atk.IsRearguard;
				}
			}
			catch { }
			int ht = -1;
			try { ht = _curHitType; } catch { }
			var sb = new StringBuilder(64);
			bool logThis = false;
			try
			{
				if (_ruleLogCount < 40 && _globalRules.Count > 0
					&& Plugin.CfgAbilityDump != null && Plugin.CfgAbilityDump.Value)
				{
					logThis = true;
					_ruleLogCount++;
					string an = "?", vn = "?";
					try { an = atk.Name; } catch { }
					try { vn = victim.Name; } catch { }
					RuntimeLog.Write("[RULE] hit atk=" + an + " ht=" + ht + " vic=" + vn
						+ " owners=" + _globalRules.Count);
				}
			}
			catch { }
			foreach (var kv in _globalRules)
			{
				var rules = kv.Value;
				if (rules == null || rules.Count == 0) continue;
				for (int i = 0; i < rules.Count; i++)
				{
					try
					{
						var r = rules[i];
						if (r.Owner == atkKey)
						{
							// 1.5.1 (MEASURED 2026-10-03, battle_411001_20261003_173710): the owner's own
							// hit must skip this rule in the PRODUCT (its own text scan already counted it,
							// per status -- the log shows it folding 1.5209 / 1.3225, i.e. 1.15^3 / 1.15^2),
							// but it must still be recorded as RESPONSIBLE.
							//
							// WHY: the same rule travels to the victim as GRANTED copies (`GiveTalent`), and
							// `GivenTalentDamage` cancels granted copies against this set. With the set left
							// empty for the owner, those copies were folded on top of the owner's own text
							// scan -- the same rule counted twice. Measured: 1729 of that attacker's 1800 hits
							// landed on residual 1/1.15^2..1/1.15^3 (0.756/0.657/0.658 and their crit
							// variants 1.249/1.437), the attacker was the rule's carrier AND the battle's
							// biggest (1800 of 5272 hits), and exactWithCrit fell from 81.7% to 50.1%.
							// Recording it here restores the pre-1.5.0 behaviour for this configuration.
							if (r.EnemyTakes && ctx != null)
								ctx.AddEnemy(r.Factor, "global#" + kv.Key + "/" + i, r.Text);
							if (logThis) RuntimeLog.Write("[RULE]   skip 持有者本人(仍登记为责任规则)");
							continue;
						}
						if (GameRef.IsNull(r.OwnerObj)) { if (logThis) RuntimeLog.Write("[RULE]   skip 持有者已失效"); continue; }
						if (r.EnemyTakes)
						{
							if (Aggregator.IsSameTeam(r.OwnerObj, victim)) { if (logThis) RuntimeLog.Write("[RULE]   skip 敌受伤:同队"); continue; }   // victim must be its enemy
							if (r.Vanguard || r.Rearguard)
							{
								bool van = false, rear = false;
								try { van = r.OwnerObj.IsVanguard; } catch { }
								try { rear = r.OwnerObj.IsRearguard; } catch { }
								if (r.Vanguard && !van) { if (logThis) RuntimeLog.Write("[RULE]   skip 敌受伤:持有者非前衛"); continue; }
								if (r.Rearguard && !rear) { if (logThis) RuntimeLog.Write("[RULE]   skip 敌受伤:持有者非後衛"); continue; }
							}
						}
						else
						{
							// "味方ヴァイスの魔法攻撃の与ダメージ+15%": the ATTACKER must be an ally
							if (GameRef.IsNull(atk)) { if (logThis) RuntimeLog.Write("[RULE]   skip 我攻击:无攻击者"); continue; }
							if (!Aggregator.IsSameTeam(r.OwnerObj, atk)) { if (logThis) RuntimeLog.Write("[RULE]   skip 我攻击:攻击者不同队"); continue; }
							if (r.Vanguard && !atkVan) { if (logThis) RuntimeLog.Write("[RULE]   skip 我攻击:攻击者非前衛"); continue; }
							if (r.Rearguard && !atkRear) { if (logThis) RuntimeLog.Write("[RULE]   skip 我攻击:攻击者非後衛"); continue; }
							if (r.MagicOnly && !(ht == 2 || ht == 5)) { if (logThis) RuntimeLog.Write("[RULE]   skip 我攻击:非魔法 ht=" + ht); continue; }
							if (r.PhysOnly && !(ht == 1 || ht == 5)) { if (logThis) RuntimeLog.Write("[RULE]   skip 我攻击:非物理 ht=" + ht); continue; }
						}
						// 1.3.8: record RESPONSIBILITY, not just what fired -- i.e. before the status-hit
						// test below. See the note above for why the "fired" multiset was not enough.
						// Only 敌方受伤 rules are recorded: they are the ones a victim-side
						// granted 1006 can duplicate. The attacker's own text-scan rules are a different
						// mechanism (the 物理与ダメージ+10% engraving is one, and it must NOT suppress the
						// 刻印 id=26 grant that shares its 1.10 value).
						if (r.EnemyTakes && ctx != null)
							ctx.AddEnemy(r.Factor, "global#" + kv.Key + "/" + i, r.Text);
						int hits = 0;
						if (r.Tokens.Count == 0) hits = 1;
						else
							for (int t = 0; t < r.Tokens.Count; t++)
							{
								bool k;
								if (HasStatusLike(victim, r.Tokens[t], out k)) hits++;
							}
						if (hits == 0) continue;
						double f = r.Factor;
						if (hits > 1 && r.PerStatus)
						{
							double acc = 1.0;
							for (int h = 0; h < hits; h++) acc *= f;
							f = acc;
						}
						m *= f;
						if (ctx != null)
							ctx.Add(r.EnemyTakes ? "vic" : "atk", "global", "global#" + kv.Key + "/" + i, f,
								r.Source + r.Text + (r.EnemyTakes ? "(全局:敌方受伤)" : "(全局:我方攻击)"));
						if (logThis) RuntimeLog.Write("[RULE]   APPLY " + r.Text + " ×" + f.ToString("F3"));
						if (sb.Length > 0) sb.Append('、');
						sb.Append(r.Source).Append(r.Text).Append(r.EnemyTakes ? "(全局:敌方受伤)" : "(全局:我方攻击)");
					}
					catch { }
				}
			}
			if (sb.Length > 0) text = sb.ToString();
		}
		catch { }
		return m;
	}
}
