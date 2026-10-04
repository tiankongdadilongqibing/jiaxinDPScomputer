using System.Text;
using UnityEngine;

namespace DpsMeter;

/// <summary>
/// Diagnostics: the [ABIL] per-unit ability dump and the [COMP] per-hit composition log.
/// Both are gated by config flags and are the tools used to chase a residual multiplier.
/// </summary>
public static partial class CompositionProbe
{
	// ---- diagnostic: raw ability texts + per-clause verdict, once per unit per battle ----
	private static readonly System.Collections.Generic.HashSet<string> _abilityDumped = new System.Collections.Generic.HashSet<string>();

	/// <summary>
	/// Log every ability's RAW text for a unit, plus the verdict for each damage-related clause.
	/// The normal composition lines only show clauses that survived filtering, so a modifier that is
	/// dropped (failed condition / attribute gate) would otherwise be invisible.
	/// </summary>
	public static void DumpAbilities(BattleObject bo, string role, int hitType, bool victimSide, BattleObject target)
	{
		try
		{
			// 1.3.7: status resistances are NOT talents, so no ability dump can ever show them -- the
			// boss's own ability (#40009) carries only `1002/0` and `6(攻击力)/150/-1` (measured
			// 2026-10-03). They live one level up, in the unit's CharacterDataBase. Independent of
			// AbilityDump, with its own first-sight gate, so it also works with the [ABIL] dump off.
			DumpStatusResistance(bo, role);
			if (Plugin.CfgAbilityDump == null || !Plugin.CfgAbilityDump.Value) return;
			if (GameRef.IsNull(bo)) return;
			string key;
			try { key = bo.EntryId + "|" + bo.Name; } catch { key = "?"; }
			if (!_abilityDumped.Add(key)) return;

			var list = bo.m_ability;
			string name = "";
			try { name = bo.Name; } catch { }
			int nAbility = (list != null) ? list.Count : -1;
			int nTakeover = -1, nExtraTalent = -1, nGiveTalent = -1, nBuff = -1;
			try { nTakeover = (bo.m_takeoverAbility != null) ? bo.m_takeoverAbility.Count : -1; } catch { }
			try { nExtraTalent = (bo.m_additionalTalents != null) ? bo.m_additionalTalents.Count : -1; } catch { }
			try { nGiveTalent = (bo.m_giveTalentData != null) ? bo.m_giveTalentData.Count : -1; } catch { }
			try { nBuff = (bo.m_buffList != null) ? bo.m_buffList.Count : -1; } catch { }
			RuntimeLog.Write("[ABIL] " + role + " " + name + " 攻击属性=" + HitTypeName(hitType)
				+ " m_ability=" + nAbility + " m_takeoverAbility=" + nTakeover + " m_additionalTalents=" + nExtraTalent
				+ " m_giveTalentData=" + nGiveTalent + " m_buffList=" + nBuff);
			// the extra talent list and every applied buff entry: this is where a modifier that has no
			// ability text (weekly arena class buff, quest blessing) has to show up
			try { RuntimeLog.Write("[ABIL]   额外天赋: " + (ExtraTalents(bo).Length == 0 ? "无" : ExtraTalents(bo))); } catch { }
			try { RuntimeLog.Write("[ABIL]   全部增益: " + (BuildBuffTextVerbose(bo).Length == 0 ? "无" : BuildBuffTextVerbose(bo))); } catch { }
			try { RuntimeLog.Write("[ABIL]   增益列表: " + BuffTalentDump(bo)); } catch { }
			// The applied damage-parameter dictionary -- the same one the "增益:" text on a hit line is
			// built from, but with BOTH sides of every entry. 增益列表 above lists m_buffList
			// (status/class/value), whose BuffTarget is usually None, so it cannot explain a number like
			// "会心率+99999"; this line can (ParamData.Target=CriticalRate, Param=99999 -- the game's own
			// sentinel, also visible in the ability's `talents:` line as 78/99999).
			try { RuntimeLog.Write("[ABIL]   参数表: " + BuffParamTableDump(bo)); } catch { }
			try
			{
				string sk = "status";
				if (_statusTableDumped.Add(sk)) RuntimeLog.Write("[ABIL]   status名表: " + StatusNameTableDump());
			}
			catch { }
			try
			{
				var tk = bo.m_takeoverAbility;
				if (tk != null)
					for (int i = 0; i < tk.Count; i++)
					{
						var sbTk = new StringBuilder();
						try { AppendTalents(sbTk, tk[i].Data, "takeover#" + i); } catch { }
						if (sbTk.Length > 0) RuntimeLog.Write("[ABIL]   継承天赋: " + sbTk.ToString());
					}
			}
			catch { }
			if (list == null) return;

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

			for (int i = 0; i < list.Count; i++)
			{
				try
				{
					var ab = list[i];
					if (ab == null) continue;
					var data = ab.Data;
					if (data == null) continue;
					int id;
					string raw;
					// SAME source as the scan, so the dumped verdict is what the arithmetic really did
					raw = GetAbilityText(bo, ab, i, entry, out id, out raw);
					if (raw == null) raw = "";
					raw = raw.Replace('\n', ' ').Replace('\r', ' ').Trim();
					string nm = "";
					try { nm = data.Name; } catch { }
					int lv = 0, dlv = 0;
					try { lv = ab.Level; } catch { }
					try { dlv = data.Level; } catch { }
					RuntimeLog.Write("[ABIL]   #" + id + " lv=" + lv + "/" + dlv + " name=\"" + nm + "\" text=\"" + raw + "\"");
					// every talent of every list, so an unreadable damage modifier can be located:
					// 与/被ダメージ補正 are talents (DamageUp = 1005 / DamageCut = 1006) and the arena's
					// weekly class buffs are handed out the same way, WITHOUT any ability text.
					try
					{
						var sbT2 = new StringBuilder();
						AppendTalents(sbT2, ab.Data, "ab#" + id);
						RuntimeLog.Write("[ABIL]     talents: " + (sbT2.Length == 0 ? "无" : sbT2.ToString()));
					}
					catch { }
					TieredModifier.Spec tier = null;
					if (TieredModifier.CountOccurrences(raw, keyword) == 1) tier = TieredModifier.Get(raw);
					if (tier != null)
					{
						var sbT = new StringBuilder();
						for (int ti = 0; ti < tier.Tiers.Count; ti++)
						{
							if (ti > 0) sbT.Append('/');
							sbT.Append(tier.Tiers[ti].Threshold).Append('%');
						}
						RuntimeLog.Write("[ABIL]     tier=" + sbT.ToString() + (tier.Below ? " 以下" : " 以上") + " ×" + tier.Tiers.Count + " 档");
					}
					System.Collections.Generic.List<string> cls;
					System.Collections.Generic.List<int> ends;
					SplitClauses(raw, out cls, out ends);
					for (int ci = 0; ci < cls.Count; ci++)
					{
						if (cls[ci].IndexOf("ダメージ", System.StringComparison.Ordinal) < 0) continue;
						string ctx = raw.Substring(0, System.Math.Min(ends[ci], raw.Length));
						double f;
						string note;
						ClauseVerdict v = JudgeClause(cls[ci], ctx, keyword, wantPhys, wantMagic, vanguard, rearguard, hpPct, bo, target, tier, out f, out note);
						string stInfo = "";
						try
						{
							var tk = StatusTokens(cls[ci]);
							if (tk.Count > 0)
							{
								var sbS = new StringBuilder();
								for (int si = 0; si < tk.Count; si++)
								{
									if (si > 0) sbS.Append('/');
									bool kk;
									bool hit = HasStatusLike(victimSide ? bo : target, tk[si], out kk);
									sbS.Append(tk[si]).Append(hit ? "=有" : (kk ? "=无" : "=?"));
								}
								stInfo = " [状态:" + sbS.ToString() + "]";
							}
						}
						catch { }
						RuntimeLog.Write("[ABIL]     clause=\"" + cls[ci] + "\" → " + v
							+ ((v == ClauseVerdict.Counted) ? (" ×" + f.ToString("F2")) : "")
							+ (string.IsNullOrEmpty(note) ? "" : (" [" + note + "]")) + stInfo);
					}
				}
				catch { }
			}
		}
		catch { }
	}

	public static void Log(DamageCalculater calc, BattleObject blocker, int finalDamage)
	{
		try
		{
			if (Plugin.CfgDamageComposition == null || !Plugin.CfgDamageComposition.Value) return;
			if (_lines >= MaxLines) return;
			_lines++;

			var sb = new StringBuilder(360);
			sb.Append("[COMP] ").Append(Name(TryAttacker(calc)));
			sb.Append(" → 目标 ").Append(Name(blocker));
			sb.Append(" · 最终 ").Append(finalDamage);
			int pow = Power(calc);
			if (pow > 0) sb.Append(" ×").Append(((double)finalDamage / pow).ToString("F3"));
			sb.Append(" · ").Append(BuildChain(calc, blocker, finalDamage)).Append("   ·   ").Append(BuildSummary(calc));
			// print the identified multipliers so the log can be checked against the displayed chain.
			// IMPORTANT: this log has its own text path, so it used to be BLIND to the battle-wide rules
			// (and to the talent values) that BuildChainParts folds in -- the [COMP] line then disagreed
			// with the overlay/export. Apply the same extras here.
			try
			{
				BattleObject atk = TryAttacker(calc);
				int ht = -1;
				try { ht = (int)calc.m_hitType; } catch { }
				_curHitType = ht;
				double mAtk = 1.0, mVic = 1.0;
				// 1.5.0 (B5): a LOCAL FoldContext, so this re-derivation passes through exactly the same
				// channels the real chain does. Before 1.5.0 this block stopped after ApplyGlobalDebuffs:
				// it never called GivenTalentDamage or the 狂気 fold, so `[COMP]`'s "已识别倍率" could not
				// equal CalcBreakdown.KnownMult on any hit carrying a granted 被伤害 modifier or 狂気 --
				// while the file's own contract says the log can never disagree with the number.
				var dctx = new FoldContext();
				try { AbilityScan(atk, 6, ht, victimSide: false, blocker, out mAtk, dctx); } catch { }
				try { AbilityScan(blocker, 6, ht, victimSide: true, atk, out mVic, dctx); } catch { }
				try { string tt; double f = TalentDamage(atk, false, out tt, dctx); if (f != 1.0) mAtk *= f; } catch { }
				try { string tt; double f = TalentDamage(blocker, true, out tt, dctx); if (f != 1.0) mVic *= f; } catch { }
				string gTxt = null;
				try
				{
					RegisterGlobalDebuffs(atk);
					RegisterGlobalDebuffs(blocker);
					double g = ApplyGlobalDebuffs(atk, blocker, out gTxt, dctx);
					if (g != 1.0) mAtk *= g;
				}
				catch { }
				// the two channels that used to be missing here. `measure: false` is essential: this is a
				// RE-DERIVATION for the [COMP] log line, not the production pass, and the production pass
				// is what reports `[GIVE]` / `rosterAudit.give*`. Measured 2026-10-03 (battle_...173710,
				// DamageComposition=true): without it every give counter was double-counted, which made the
				// giveTypes histogram look like the battle had half again as many granted modifiers.
				try
				{
					int gc, ga;
					string gt;
					// 1.7.10: hand this re-derivation the SAME switch the production chain reads, and evaluate the SAME
					// predicate. Until now this call left applyToChain at its default (true) and multiplied gv
					// unconditionally, so with General/GivenTalent=false the [COMP] \"已识别倍率\" still contained a
					// factor the chain had suppressed -- the one thing this block exists to prevent (its own comment
					// promises the log can never disagree with CalcBreakdown.KnownMult). Still measure:false: this must
					// not touch the [GIVE] self-report.
					bool givenFoldOn = Plugin.CfgGivenTalent == null || Plugin.CfgGivenTalent.Value;
					double gv = GivenTalentDamage(blocker, 1006, out gc, out ga, out gt, dctx, false, givenFoldOn);
					if (GivenFoldApplies(givenFoldOn, gv)) mVic *= gv;
				}
				catch { }
				try
				{
					int mr;
					bool mo;
					double mad = UnitStateProbe.MadnessMultiplier(atk, out mr, out mo);
					// 1.7.10: same switch as the chain (Plugin.CfgMadness), for the same reason as the given channel.
					if ((Plugin.CfgMadness == null || Plugin.CfgMadness.Value) && mad != 1.0) mAtk *= mad;
				}
				catch { }
				// 1.7.10: the VICTIM-side 狂気 fold has never been part of this re-derivation, so on any hit whose
				// victim carried 狂気 the [COMP] \"被伤害×\" column was short by exactly ×1.5 while the chain folded it
				// (Chain.cs, CfgMadnessVictim). It is read from the same two helpers the chain uses -- StatusBrief +
				// SplitStatusList -- so the fold and any status list exported elsewhere describe one read, not two.
				try
				{
					string[] vlist = SplitStatusList(StatusBrief(blocker));
					bool vicMad = false;
					if (vlist != null)
						for (int i = 0; i < vlist.Length; i++) if (vlist[i] == "狂気") { vicMad = true; break; }
					if (vicMad && (Plugin.CfgMadnessVictim == null || Plugin.CfgMadnessVictim.Value)) mVic *= 1.5;
				}
				catch { }
				double mAttr = AttrMultiplier(atk, blocker);
				sb.Append("   ·   已识别倍率 ×").Append((mAttr * mAtk * mVic).ToString("F4"))
					.Append("(属性×").Append(mAttr.ToString("F2"))
					.Append("·与伤害×").Append(mAtk.ToString("F4"))
					.Append("·被伤害×").Append(mVic.ToString("F4")).Append(')');
				if (!string.IsNullOrEmpty(gTxt)) sb.Append("   ·   全局:").Append(gTxt);
			}
			catch { }

			RuntimeLog.Write(sb.ToString());
		}
		catch { }
	}

	// ================= 1.3.7: status resistances =================
	// The 18-slot table (`CharacterStatusResistance.Type`) lives in `UnitStateProbe.Types` / `.Names`
	// (1.4.0) so the template copy below and the live copy in [STATE] can never drift apart.
	// Enum order (decompiled): None=-1, Stun, StunTerminal, Petrifaction, PetrifactionTerminal, Poison,
	// PoisonDamage, KnockBack, Burn, Frozen, Darkness, Madness, Fear, Death, TimeStop, BaseStatus,
	// MoveSpeed, AttackSpeed, AttackInterval, Max=18.
	// NOTE the two-part poison: `Poison` is the chance to be poisoned, `PoisonDamage` is how much the
	// poison ticks for. Our units hand out BOTH (`type=409 毒耐性-` and `type=411 毒伤害耐性-`), which
	// is why one poison debuff moves two slots.

	/// <summary>First-sight gate for the resistance line, separate from the [ABIL] gate. The 18-slot
	/// table itself lives in `UnitStateProbe` (1.4.0) so the template copy and the live copy can never
	/// drift apart -- the whole 1.3.x saga was two readings of "the resistance" that disagreed.</summary>
	private static readonly System.Collections.Generic.HashSet<string> _resistDumped
		= new System.Collections.Generic.HashSet<string>();

	/// <summary>Compact per-unit FIRST-sight resistance summary for the export (bounded).</summary>
	private static readonly StringBuilder _resistSample = new StringBuilder(1200);

	/// <summary>Per-unit LATEST resistance rendering (1.3.8), throttled. If this differs from the
	/// first-sight line the first read was taken before the unit was fully set up -- which is the
	/// question the 2026-10-03 numbers raised: every player unit read 0 in all 18 slots while the boss
	/// read 100 in all 11 status slots, yet 毒/火傷/凍結 demonstrably landed on the boss. A percentage
	/// resistance of 100 should make that impossible, so the reading (or the moment of reading, or the
	/// field's meaning) has to be wrong somewhere.</summary>
	private static readonly System.Collections.Generic.Dictionary<string, string> _resistLast
		= new System.Collections.Generic.Dictionary<string, string>();

	private static readonly System.Collections.Generic.Dictionary<string, int> _resistLastTick
		= new System.Collections.Generic.Dictionary<string, int>();

	internal static int ResistUnits;
	internal static int ResistReads;
	internal static int ResistErrors;
	/// <summary>Units whose CharacterDataBase / resistance object was null (counted, never silent).</summary>
	internal static int ResistNull;
	/// <summary>Throttled re-reads after the first sight (1.3.8).</summary>
	internal static int ResistSamples;

	internal static string ResistSample() { return _resistSample.ToString(); }

	/// <summary>
	/// 1.5.0 (B2): the RAW values behind the two sample strings, kept so the export can carry them as
	/// NUMBERS. Before this the only form was "毒=25 火傷=55 …" and every analysis script that needed a
	/// value re-invented the same split; the 1.4.1 resistance chart had to regex it, and the same regex
	/// had already been copied into two scripts. `int.MinValue` marks a slot whose read threw, so it is
	/// exported as null rather than as 0.
	/// </summary>
	private static readonly System.Collections.Generic.Dictionary<string, int[]> _resistFirstRaw
		= new System.Collections.Generic.Dictionary<string, int[]>();

	/// <summary>See <see cref="_resistFirstRaw"/>; the latest reading per unit.</summary>
	private static readonly System.Collections.Generic.Dictionary<string, int[]> _resistLastRaw
		= new System.Collections.Generic.Dictionary<string, int[]>();

	/// <summary>Structured twin of <see cref="ResistSample"/> + <see cref="ResistLastSample"/>.</summary>
	internal static void AppendResistJson(System.Text.StringBuilder sb)
	{
		sb.Append("{\"slots\":[");
		for (int i = 0; i < UnitStateProbe.Names.Length; i++)
		{
			if (i > 0) sb.Append(',');
			sb.Append('"').Append(UnitStateProbe.Names[i]).Append('"');
		}
		sb.Append("],\"first\":[");
		AppendResistUnits(sb, _resistFirstRaw);
		sb.Append("],\"last\":[");
		AppendResistUnits(sb, _resistLastRaw);
		sb.Append("]}");
	}

	private static void AppendResistUnits(System.Text.StringBuilder sb, System.Collections.Generic.Dictionary<string, int[]> src)
	{
		bool firstUnit = true;
		foreach (var kv in src)
		{
			if (!firstUnit) sb.Append(',');
			firstUnit = false;
			sb.Append("{\"unit\":\"").Append(JsonText.Str(kv.Key)).Append("\",\"v\":[");
			int[] a = kv.Value;
			for (int i = 0; i < a.Length; i++)
			{
				if (i > 0) sb.Append(',');
				if (a[i] == int.MinValue) sb.Append("null"); else sb.Append(a[i]);
			}
			sb.Append("]}");
		}
	}

	/// <summary>Latest per-unit reading, joined. Compared against ResistSample() by the reader.</summary>
	internal static string ResistLastSample()
	{
		var sb = new StringBuilder(1200);
		foreach (var kv in _resistLast)
		{
			if (sb.Length >= 1100) break;
			if (sb.Length > 0) sb.Append(" | ");
			sb.Append(kv.Key).Append(": ").Append(kv.Value);
		}
		return sb.ToString();
	}

	/// <summary>
	/// Read a unit's 18 status-resistance slots from `BattleObject.Data.m_statusResistance`
	/// (`CharacterDataBase.StatusResistance`) and log them once per unit per battle.
	///
	/// WHY THIS IS THE ONLY ROUTE: the master tables the plugin can dump (`MasterDataManager`'s 209
	/// names) contain no unit/enemy table at all, and the install directory has no copy either (the
	/// 7,937 cached bundles hold only UI/sprites/Spine/scenario -- measured 2026-10-03, documented in
	/// MasterDataDump.cs). Unit stats, resistances included, live in CharacterDataBase in the process.
	///
	/// Read through `Get(Type)` rather than `mValues` on purpose: `Get` is an ordinary method call,
	/// while `mValues` is an `Il2CppStructArray&lt;int&gt;` whose bounds and element reads are the kind of
	/// interop shape this project avoids when a plain accessor exists.
	/// </summary>
	internal static void DumpStatusResistance(BattleObject bo, string role)
	{
		try
		{
			if (Plugin.CfgStatusResist == null || !Plugin.CfgStatusResist.Value) return;
			if (GameRef.IsNull(bo)) return;
			string name = "";
			string key;
			try
			{
				name = bo.Name;
				key = bo.EntryId + "|" + name;
			}
			catch { key = "?"; }

			bool first = !_resistDumped.Contains(key);
			if (first)
			{
				_resistDumped.Add(key);
			}
			else
			{
				// 1.3.8: keep sampling, but throttled to 500 ms per unit. The first-sight read is the
				// one that produced the self-contradictory numbers, so the question "does it move?" has
				// to be answered from data rather than assumed.
				int now = System.Environment.TickCount;
				int last;
				if (_resistLastTick.TryGetValue(key, out last) && (now - last) < 500) return;
				_resistLastTick[key] = now;
				ResistSamples++;
			}

			CharacterStatusResistance res = null;
			CharacterDataBase data = null;
			try { data = bo.Data; } catch { ResistErrors++; }
			if (data != null)
			{
				try { res = data.m_statusResistance; } catch { ResistErrors++; }
				if (res == null)
				{
					// the field is the primary route; the property is the fallback and is what the game's
					// own code calls, so one of the two being unreadable does not lose the measurement
					try { res = data.StatusResistance; } catch { ResistErrors++; }
				}
			}
			if (res == null) { ResistNull++; return; }

			var sb = new StringBuilder(160);
			var vals = new int[UnitStateProbe.Types.Length];
			for (int i = 0; i < UnitStateProbe.Types.Length; i++)
			{
				int v;
				vals[i] = int.MinValue;
				try { v = res.Get(UnitStateProbe.Types[i]); ResistReads++; }
				catch { ResistErrors++; continue; }
				vals[i] = v;
				if (sb.Length > 0) sb.Append(' ');
				sb.Append(UnitStateProbe.Names[i]).Append('=').Append(v);
			}
			if (sb.Length == 0) return;
			_resistLast[key] = sb.ToString();
			// 1.5.0 (B2): the same numbers, structured. First sight and latest are kept separately
			// because their DIFFERENCE is the measurement ("did this field ever move?").
			if (first) _resistFirstRaw[key] = vals; else _resistLastRaw[key] = vals;
			// 1.3.9: the 18 slots above are a MASTER/template value -- measured 952 throttled re-reads
			// over one battle with ZERO movement, while 5,120 毒耐性-30 / 毒伤害耐性-90 grants were
			// landing on that very unit. So the moving half has to be read from somewhere else, and the
			// game's own record of "modifiers applied to this unit's stats" is `m_statusSubParams`
			// (`List<BuffCharacterStatusSubParam.Data>` with `Type: eBuffType` + `SubParam: int`, the
			// same eBuffType codes the talent table uses: 409 = 毒耐性-, 415 = 火傷耐性-, 417 = 凍結耐性-).
			DumpStatusSubParams(bo, key, name, first);
			// 1.3.10: both previous candidates are now dead ends -- the 18 master slots never move
			// (940-952 samples, zero change, while 5,120 毒耐性-30 grants were landing) and
			// `m_statusSubParams` reads empty for all 12 units (952 reads, 0 errors). The remaining
			// place a live stat modifier must live is the applied-param dictionary this plugin already
			// renders as 参数表 -- `BuffParamData.mNowBuffParamDataDictionary`, keyed by
			// (BuffTarget, BuffType, BuffValue). Dumped throttled here and exported once per unit.
			DumpParamTable(bo, key, name, first);
			// 1.4.0: the LIVE resistance. Everything above this version read the TEMPLATE
			// (`CharacterDataBase`), which measured ZERO movement across a whole battle while 5,120
			// resistance debuffs landed. The value the game actually consults is on
			// `Character.Status.Resistance` (`CharaStatus`), and it is reachable from the same
			// `BattleObject` -- same throttle, same instant, so the two readings are directly
			// comparable. See UnitStateProbe for the decompiled route and why the other four
			// candidates were wrong.
			UnitStateProbe.Observe(bo, role, key, name, first);
			if (!first) return;
			ResistUnits++;
			RuntimeLog.Write("[RESIST] " + role + " " + name + " " + sb.ToString());
			if (_resistSample.Length < 1100)
			{
				if (_resistSample.Length > 0) _resistSample.Append(" | ");
				_resistSample.Append(name).Append(": ").Append(sb.ToString());
			}
		}
		catch { ResistErrors++; }
	}

	/// <summary>
	/// 1.5.0 (B2): the structured twin of <see cref="SubParamSample"/> -- `{type, name, param}` per entry
	/// instead of the "毒耐性-/30、凍結耐性-/5" text. This is the table that decides whether a
	/// mid-battle resistance modifier is visible at all, so having it as numbers (keyed by the SAME
	/// `eBuffType` codes the talent table uses) is what lets a script join it to `giveTypeList`.
	/// </summary>
	private static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int[]>>
		_subParamRaw = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int[]>>();

	/// <summary>Structured twin of <see cref="SubParamSample"/>. `read` counts the units whose list was
	/// actually read, so "no modifiers" and "never looked" stay distinguishable.</summary>
	internal static void AppendSubParamJson(StringBuilder sb)
	{
		sb.Append("{\"units\":[");
		bool firstUnit = true;
		foreach (var kv in _subParamRaw)
		{
			if (!firstUnit) sb.Append(',');
			firstUnit = false;
			sb.Append("{\"unit\":\"").Append(JsonText.Str(kv.Key)).Append("\",\"v\":[");
			var list = kv.Value;
			for (int i = 0; i < list.Count; i++)
			{
				if (i > 0) sb.Append(',');
				int[] e = list[i];
				sb.Append("{\"type\":").Append(e[0]).Append(",\"param\":").Append(e[1]).Append('}');
			}
			sb.Append("]}");
		}
		sb.Append("],\"reads\":").Append(SubParamReads)
		  .Append(",\"errors\":").Append(SubParamErrors).Append('}');
	}

	/// <summary>Per-unit latest `m_statusSubParams` rendering (1.3.9), e.g. "毒耐性-/30、凍結耐性-/5".</summary>
	private static readonly System.Collections.Generic.Dictionary<string, string> _subParamLast
		= new System.Collections.Generic.Dictionary<string, string>();

	internal static int SubParamReads;
	internal static int SubParamErrors;
	internal static string SubParamSample()
	{
		var sb = new StringBuilder(900);
		foreach (var kv in _subParamLast)
		{
			if (sb.Length >= 800) break;
			if (sb.Length > 0) sb.Append(" | ");
			sb.Append(kv.Key).Append(": ").Append(kv.Value);
		}
		return sb.ToString();
	}

	/// <summary>
	/// Render a unit's live stat sub-params. This is where a resistance modifier handed out mid-battle
	/// has to appear: the master slots do not move (see the note at the call site).
	/// </summary>
	internal static void DumpStatusSubParams(BattleObject bo, string key, string name, bool first)
	{
		try
		{
			if (Plugin.CfgStatusResist == null || !Plugin.CfgStatusResist.Value) return;
			var list = bo.m_statusSubParams;
			SubParamReads++;
			if (list == null) return;
			var sb = new StringBuilder(120);
			// 1.5.0 (B2): the same entries as numbers, collected while they are being read so the
			// structured and textual forms cannot describe different samples.
			var raw = new System.Collections.Generic.List<int[]>(8);
			int n = list.Count;
			if (n > 24) n = 24;
			for (int i = 0; i < n; i++)
			{
				try
				{
					var d = list[i];
					if (d == null) continue;
					int ty = 0;
					try { ty = (int)d.Type; } catch { SubParamErrors++; }
					int v = 0;
					try { v = d.SubParam; } catch { SubParamErrors++; }
					if (ty == 0 && v == 0) continue;
					if (sb.Length > 0) sb.Append('、');
					string tn = "";
					try { tn = TalentNames.BuffType(ty); } catch { }
					if (string.IsNullOrEmpty(tn)) sb.Append('#').Append(ty);
					else sb.Append(tn);
					sb.Append('/').Append(v);
					raw.Add(new int[] { ty, v });
				}
				catch { SubParamErrors++; }
			}
			if (list.Count > n) sb.Append("…+").Append(list.Count - n);
			string s = sb.Length == 0 ? "(空)" : sb.ToString();
			_subParamLast[key] = s;
			_subParamRaw[key] = raw;
			if (first) RuntimeLog.Write("[SUBPARAM] " + name + " " + s);
		}
		catch { SubParamErrors++; }
	}

	/// <summary>Per-unit latest applied-param dictionary rendering (1.3.10).</summary>
	private static readonly System.Collections.Generic.Dictionary<string, string> _paramLast
		= new System.Collections.Generic.Dictionary<string, string>();

	/// <summary>
	/// 1.5.0 (B2): the STRUCTURED twin of <see cref="_paramLast"/>, collected in the same walk. The text
	/// form is truncated at 220 chars per unit before it is stored, so a script that needed the entries
	/// past the truncation had no route at all; these rows are complete up to the 14-entry enumeration cap.
	/// </summary>
	private static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string[]>>
		_paramRaw = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string[]>>();

	/// <summary>Structured twin of <see cref="ParamSample"/>. Row layout:
	/// `[i, buffTargetName, buffTypeName, buffValue, valueTargetName, valueTypeName, valueParam, refText]`.</summary>
	internal static void AppendParamJson(StringBuilder sb)
	{
		sb.Append("{\"units\":[");
		bool firstUnit = true;
		foreach (var kv in _paramRaw)
		{
			if (!firstUnit) sb.Append(',');
			firstUnit = false;
			sb.Append("{\"unit\":\"").Append(JsonText.Str(kv.Key)).Append("\",\"rows\":[");
			var rows = kv.Value;
			for (int i = 0; i < rows.Count; i++)
			{
				if (i > 0) sb.Append(',');
				string[] r = rows[i];
				sb.Append("{\"i\":").Append(r[0])
				  .Append(",\"tgt\":\"").Append(JsonText.Str(r[1]))
				  .Append("\",\"ty\":\"").Append(JsonText.Str(r[2]))
				  .Append("\",\"val\":").Append(r[3])
				  .Append(",\"vTgt\":\"").Append(JsonText.Str(r[4]))
				  .Append("\",\"vTy\":\"").Append(JsonText.Str(r[5]))
				  .Append("\",\"vParam\":").Append(r[6])
				  .Append(",\"vRef\":\"").Append(JsonText.Str(r[7])).Append("\"}");
			}
			sb.Append("]}");
		}
		sb.Append("],\"reads\":").Append(ParamReads)
		  .Append(",\"errors\":").Append(ParamErrors).Append('}');
	}

	internal static int ParamReads;
	internal static int ParamErrors;
	internal static string ParamSample()
	{
		var sb = new StringBuilder(1400);
		foreach (var kv in _paramLast)
		{
			if (sb.Length >= 1300) break;
			if (sb.Length > 0) sb.Append(" | ");
			sb.Append(kv.Key).Append(": ").Append(kv.Value);
		}
		return sb.ToString();
	}

	/// <summary>
	/// Render a unit's live applied-param dictionary (`BuffParamData.mNowBuffParamDataDictionary`) once
	/// per throttle window. This is the same structure the 参数表 line prints at first sight, but read
	/// LATE and per unit -- the first-sight dump was empty for the boss, and resistances handed out
	/// mid-battle (毒耐性-30 etc.) can only appear once they have been applied.
	/// </summary>
	internal static void DumpParamTable(BattleObject bo, string key, string name, bool first)
	{
		try
		{
			if (Plugin.CfgStatusResist == null || !Plugin.CfgStatusResist.Value) return;
			ParamReads++;
			// 1.5.0 (B2): ONE walk fills both the text (truncated for the log/string key) and the
			// structured rows. The dictionary is live and churning, so two walks could describe two
			// different instants -- the same reason comp4 and its status list are produced together.
			// 1.7.2 (阶段 G): the OWNER list rides THAT SAME walk for the same reason, and keeps its own
			// cap (ParamOwnerProbe.MaxEntriesPerRead, i.e. past the historical 14) and its own gate. The
			// consequence -- this channel is only walked while General/StatusResist is on -- is stated in
			// the ParamOwners setting's description instead of being left implicit.
			var rows = new System.Collections.Generic.List<string[]>(14);
			var owners = (Plugin.CfgParamOwners == null || Plugin.CfgParamOwners.Value)
				? new System.Collections.Generic.List<string[]>(ParamOwnerProbe.MaxEntriesPerRead) : null;
			string s = BuffParamTableDump(bo, rows, owners);
			if (owners != null)
			{
				string selfName = "";
				try { selfName = Aggregator.NameOf(bo); } catch { }
				ParamOwnerProbe.Observe(key, selfName, owners);
			}
			if (string.IsNullOrEmpty(s)) { ParamErrors++; return; }
			_paramRaw[key] = rows;
			if (s.Length > 220) s = s.Substring(0, 220);
			_paramLast[key] = s;
			if (first) RuntimeLog.Write("[PARAM] " + name + " " + s);
		}
		catch { ParamErrors++; }
	}
}
