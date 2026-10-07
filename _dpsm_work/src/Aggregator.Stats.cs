using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace DpsMeter;

public static partial class Aggregator
{

	/// <summary>Same-team damage (heal reversal / self-damage) is not output.
	/// Also used by the battle-wide debuff rules, which only affect a unit's ENEMIES.</summary>
	internal static bool IsSameTeam(BattleObject attacker, BattleObject victim)
	{
		try
		{
			if (GameRef.IsNull(attacker)) return false;
			if (GameRef.IsNull(victim)) return false;
			return attacker.TeamType == victim.TeamType;
		}
		catch { return false; }
	}

	private static int TeamOf(BattleObject b)
	{
		try
		{
			if (!GameRef.IsNull(b)) return (int)b.TeamType;
		}
		catch { }
		return 0;
	}

	/// <summary>Add a damage event to an actor's totals.
	///
	/// Same-team damage (回復反転 / self-damage) is ALWAYS tallied separately into DamageFriendly.
	/// By default it also stays inside DamageDealt, because the game's own damage report counts it:
	/// verified on a real battle, game_given == friendly + normal for the affected unit
	/// (T.O.W.E.R.typeR: 5,252,510 + 1,921 == 5,254,431). Set FilterFriendlyFire=true to drop it
	/// from the totals instead; either way the UI always labels it.</summary>
	private static void Accumulate(ActorStats st, int damage, string attrMode, bool friendly)
	{
		if (st == null) return;
		if (friendly)
		{
			st.DamageFriendly += damage;
			st.FriendlyHits++;
			bool filter = Plugin.CfgFilterFriendlyFire != null && Plugin.CfgFilterFriendlyFire.Value;
			if (filter) return;
		}
		st.DamageDealt += damage;
		st.HitCount++;
		st.AttrMode = attrMode;
		st.LastHitTime = Session.ActiveSeconds;
		if (st.HitCount == 1) st.FirstHitTime = Session.ActiveSeconds;
		// self-injury must not become the "biggest hit" of an attacker
		if (!friendly && damage > st.MaxHitDamage) st.MaxHitDamage = damage;
		st.AddSample(Session.ActiveSeconds, st.DamageDealt);
		st.AddSecondDamage(ClockOriginHoldPolicy.SecondIndex(Session.ActiveSeconds), damage);
	}

	/// <param name="nominal">
	/// BattleObject.Damage's ARGUMENT: the damage the game accounted for (CharacterStatistics.TakenDamage).
	/// <paramref name="damage"/> is the amount this plugin PUBLISHES: the call's RETURN when that return
	/// reports an overflow, and the argument itself otherwise. R78: on the overflow path the return is the
	/// part of the hit that did NOT fit (R76's law `res == max(0, nominal - lifeBefore)`, 798/798 readable
	/// readings), so `nominal &gt; damage` is NOT "part of the hit was 被吸收/无效化" -- it is "the hit
	/// exceeded the victim's remaining Life". The wording lives in `Policy/AbsorbWording.cs`; this round moves
	/// no number. Passing 0 (the default) means "same as damage".
	/// </param>
	public static void RecordDamage(BattleObject victim, BattleObject attacker, BattleObject owner, int damage, int nominal = 0)
	{
		if (damage <= 0 && nominal <= 0) return;
		if (nominal < damage) nominal = damage;
		EnsureSessionStartedFor(attacker, victim);
		// R72: while the battle clock's ORIGIN is undecided this event is HELD, not aggregated (see
		// ClockOriginHoldPolicy). Aggregating it now would stamp it on the old axis, and R71's shift could
		// then no longer be applied without publishing two axes in one battle -- which is exactly how the
		// user's two battles ended up with `reason=events` and no shift at all.
		if (HoldOriginEvent(false, victim, attacker, owner, damage, nominal)) return;
		RecordDamageNow(victim, attacker, owner, damage, nominal);
	}

	private static void RecordDamageNow(BattleObject victim, BattleObject attacker, BattleObject owner, int damage, int nominal)
	{
		int absorbed = nominal - damage;
		EnsureSessionStartedFor(attacker, victim);
		BeginTimingIfNeeded();
		Rt.EventCount++;
		Session.NoteEvent();

		ActorStats victimStats = Session.GetActor(victim, create: true);
		if (victimStats != null)
		{
			// R79: 受击来源拆分 needs the victim's position, and the game can move a unit mid-battle, so it
			// is snapshotted on the FIRST damage this unit takes and never re-read. Guarded like every other
			// native read here: a failure must leave an explicit "unknown" (ActorStats.Position == 0, printed
			// as 站位未知) rather than silently passing the unit off as a rearguard.
			if (!victimStats.PositionProbed)
			{
				victimStats.PositionProbed = true;
				try
				{
					if (victim.IsVanguard) victimStats.Position = 1;
					else if (victim.IsRearguard) victimStats.Position = 2;
				}
				catch { victimStats.Position = 0; }
			}
			victimStats.DamageTaken += damage;
			victimStats.DamageTakenNominal += nominal;
			victimStats.DamageAbsorbed += absorbed;
			victimStats.AddSecondTaken(ClockOriginHoldPolicy.SecondIndex(Session.ActiveSeconds), damage);
			if (CharacterInfo.IsAllyTeam(victimStats.Team))
				Session.AddTeamTaken(ClockOriginHoldPolicy.SecondIndex(Session.ActiveSeconds), damage);
		}
		if (absorbed > 0)
		{
			Rt.AbsorbedTotal += absorbed;
			Rt.AbsorbedHits++;
			// Rare by nature, and the single most confusing row in the log, so it is always logged.
			// R78(A): renamed. On this path `damage` is the call's RETURN, MEASURED to be the overflow
			// (Policy/AbsorbWording.cs), i.e. the part of the hit that did not fit into the victim's remaining
			// Life -- and the difference is that remaining Life, not an absorption. Counting and export are
			// untouched: every number in this file is what it was before this round.
			string l2 = $"[DpsMeter][OVERFLOW] {Desc(victim)} 超出剩余耐久 {damage}(游戏口径 {nominal} = 超出剩余耐久 {damage} + 目标剩余耐久 {absorbed};非吸收)";
			Plugin.LogSource.LogInfo(l2);
			RuntimeLog.Write(l2);
		}

		BattleObject source = attacker;
		string attrMode = "?";
		if (GameRef.IsNull(source)) source = owner;
		if (!GameRef.IsNull(attacker))
			attrMode = (!GameRef.IsNull(owner) && !GameRef.Same(owner, attacker)) ? "A+O" : "A";
		else if (!GameRef.IsNull(owner)) attrMode = "O";

		// Damage dealt to the attacker's OWN team is not output: 回復反転 (heal reversal, wiki:
		// damage = 20% of the heal value) and self-damage skills both land here. Team is read from
		// the objects themselves, so identical names on both sides cannot fool this check --
		// unlike the old name-based heuristics, which is why reversal damage used to be counted
		// as the healer's DPS.
		bool friendly = IsSameTeam(source, victim);

		ActorStats actorStats = null;
		if (!GameRef.IsNull(source))
		{
			actorStats = Session.GetActor(source, create: true);
			if (actorStats != null) Accumulate(actorStats, damage, attrMode, friendly);
		}
		else
		{
			// Try to resolve the attacker from the recent damage-calculation activity
			// (many enemy/area attacks carry no attacker/owner on BattleObject.Damage itself).
			BattleObject resolved = TryResolveCalcSource(victim, damage);
			if (!GameRef.IsNull(resolved))
			{
				// keep the event's attacker name in sync with the resolved source (used to stay "?")
				source = resolved;
				attrMode = "C:" + _lastCalcSrc;
				bool friendlyResolved = IsSameTeam(resolved, victim);
				actorStats = Session.GetActor(resolved, create: true);
				if (actorStats != null) Accumulate(actorStats, damage, attrMode, friendlyResolved);
				friendly = friendlyResolved;
			}
			else
			{
				Session.UnattributedDamage += damage;
				Session.UnattributedHits++;
				if (!GameRef.IsNull(victim))
				{
					try
					{
						string vn = CharacterInfo.DisplayName(victim);
						Session.UnattributedByVictim.TryGetValue(vn, out var acc);
						Session.UnattributedByVictim[vn] = acc + damage;
					}
					catch { }
				}
			}
		}

		// R62 (A): the composition is built BEFORE the damage-detail figure is consumed. The record
		// channel pairs on (attacker, target) inside 0.35 s with no value match, and ONE hit can leave
		// several pending figures behind it (the DamageAction/ActDamageAction pair plus the 被吸收
		// accounting call), so the NEXT hit to the same target can inherit the previous hit's figure.
		// hitType is the one field both channels read from the same game member
		// (DamageCalculater.m_hitType), which makes the contradiction detectable; the decision itself is
		// AttributionPolicy.RecordContradictsComposition. Moving the composition up cannot change its
		// result: nothing between the two positions touches the live calc or the calc-activity log.
		string compA = null, compB = null, compC = null, compD = null;
		CalcBreakdown compCalc = default(CalcBreakdown);
		try { TryGetCompForVictim(victim, damage, nominal, out compA, out compB, out compC, out compD, out compCalc); }
		catch { }

		// 1.5.0 (A2): the damage-detail match. `hitHow` says which kind of match was used (0 none,
		// 1 exact by damage value, 2 by attacker+target only, 3 R62: paired and then REJECTED because the
		// composition read a different hit type) and is exported per hit, so a best-effort label can never
		// be mistaken for an authoritative one.
		int hitHow;
		HitRecord hitRecord = Session.ConsumePending(source, victim, damage, nominal, Session.ActiveSeconds, out hitHow);
		if (hitRecord != null && AttributionPolicy.RecordContradictsComposition(
				(int)hitRecord.HitType, compCalc.HitType, compCalc.Valid, compCalc.PairTrusted))
		{
			// The figure belonged to another hit. Drop it instead of relabelling: `source`, `calcHitType`,
			// `calcEffectId`, `crit` and `skills[]` are ALL read from it, and writing another hit's figure
			// as if it were this one's is exactly the silent wrong number this branch exists to stop.
			hitRecord = null;
			hitHow = AttributionPolicy.HitMatchRejected;
			Rt.HitMatchRejected++;
		}
		else if (hitRecord != null) { if (hitHow == 1) Rt.HitMatchExact++; else Rt.HitMatchPair++; }
		else Rt.HitMatchNone++;
		if (hitRecord != null && actorStats != null)
		{
			// The crit tally only moves on an OBSERVED flag: a record with CritObserved == 0 says nothing
			// about crit, and counting it as "not a crit" is how a metric silently becomes a fiction.
			if (hitRecord.CritObserved == 2)
			{
				actorStats.CritCount++;
				actorStats.CritDamage += damage;
			}
			else
			{
				actorStats.NonCritDamage += damage;
			}
			if (hitRecord.EffectId != 0)
			{
				actorStats.SkillDamage.TryGetValue(hitRecord.EffectId, out var v1);
				actorStats.SkillDamage[hitRecord.EffectId] = v1 + damage;
				actorStats.SkillHits.TryGetValue(hitRecord.EffectId, out var v2);
				actorStats.SkillHits[hitRecord.EffectId] = v2 + 1;
			}
			actorStats.SourceDamage.TryGetValue((int)hitRecord.Source, out var v3);
			actorStats.SourceDamage[(int)hitRecord.Source] = v3 + damage;
		}
		else if (actorStats != null && _active.Calc != null)
		{
			try
			{
				// 1.5.0 (A2): the SAME age window the composition's own pairing uses. This fallback had
				// none, so a calc left over from the previous battle could label this battle's opening
				// hits -- and `_activeCalc` was not even cleared when a session started (fixed in
				// StartSession below). The composition path was protected; this one was not.
				double age = _active.Age(Session.ActiveSeconds);
				// RF3: literally the same window as the composition pairing now, via one definition.
				if (AttributionPolicy.LiveAgeEligible(age, AttributionPolicy.LivePairMinAge,
				                                      AttributionPolicy.LivePairMaxAge)
					&& GameRef.Same(_active.Calc.Attacker, source))
				{
					int effectId = _active.Calc.m_effectId;
					if (effectId != 0)
					{
						actorStats.SkillDamage.TryGetValue(effectId, out var v4);
						actorStats.SkillDamage[effectId] = v4 + damage;
						actorStats.SkillHits.TryGetValue(effectId, out var v5);
						actorStats.SkillHits[effectId] = v5 + 1;
					}
				}
			}
			catch { }
		}

		if (Plugin.CfgVerbose.Value)
		{
			string line = $"[DpsMeter] DMG {damage} {Desc(source)} -> {Desc(victim)} mode={attrMode} src={hitRecord?.Source} crit={hitRecord?.CritObserved} match={hitHow} eff={hitRecord?.EffectId}";
			Plugin.LogSource.LogInfo(line);
			RuntimeLog.Write(line);
		}
		// full event log for offline analysis
		try
		{
			var ev = new BattleEvent
			{
				T = Session.ActiveSeconds,
				Type = "dmg",
				Victim = NameOf(victim),
				Attacker = NameOf(source),
				Owner = NameOf(owner),
				Attr = attrMode,
				Amount = damage,
				Nominal = nominal,
				Source = hitRecord != null ? (int)hitRecord.Source : 0,
				Crit = hitRecord != null && hitRecord.CritObserved == 2,
				// 1.5.0 (A2/A3): a tri-state, so "the game said no" and "we never saw the flag" are
				// distinguishable in the data. `Crit` above keeps its old bool shape for existing scripts
				// and is exactly (CritObserved == 2).
				CritObserved = hitRecord != null ? hitRecord.CritObserved : (byte)0,
				HitMatch = hitHow,
				// 1.5.2: the value the matched record actually carried, so `hitMatch` becomes auditable
				// instead of only assertable -- see BattleEvent.HitValue.
				HitValue = hitRecord != null ? hitRecord.Damage : 0L,
				CalcHitType = hitRecord != null ? (int)hitRecord.HitType : -1,
				CalcEffectId = hitRecord != null ? hitRecord.EffectId : 0,
				HealCalc = hitRecord != null && hitRecord.HitType == eDamageCalcType.Heal,
				AttackerTeam = TeamOf(source),
				VictimTeam = TeamOf(victim),
				AttackerKey = actorStats != null ? actorStats.Key : 0,
				VictimKey = victimStats != null ? victimStats.Key : 0,
				Friendly = friendly,
				Comp = compA,
				Comp2 = compB,
				Comp3 = compC,
				Comp4 = compD,
				Calc = compCalc
			};
			// Did THIS record inflict an ailment on the victim? Diff the status list captured by the
			// Damage prefix against the state now, and schedule a late re-check (StatusDeltaProbe).
			// Bind before AddEvent: the probe keeps the reference and mutates it when the late result
			// lands, and the event is the same object the export and the overlay read.
			StatusDeltaProbe.Bind(ev, victim, Session.ActiveSeconds);
			// Which 素质/词条 fired between this attacker's previous hit and this one? Read the game's own
			// activation counters (Diagnostics/TalentRuntime.cs). The extra hit produced by a follow-up
			// talent is exactly the evidence that the talent fired, so it is attributed to this record.
			TalentRuntime.NoteAttack(source, actorStats, ev);
			// 1.4.0: keep a bounded specimen of the hits the chain could NOT explain, with the LIVE state
			// of both sides. Must run BEFORE AddEvent but while `source`/`victim` are still alive -- the
			// composition reads only what it models, and what it does not model is the whole question.
			Forensics.Observe(ev, source, victim);
			// 1.5.0 (B4): the full-resolution state timeline for the VICTIM. Runs for EVERY damage event
			// (not only the unexplained ones) and emits a row only when something actually changed, so the
			// cost is 18 slot reads here and near-zero output. This is what makes a resistance curve
			// measured rather than sampled -- see Diagnostics/StateTimelineProbe.cs.
			StateTimeline.Observe(victim, victimStats != null ? victimStats.Key : 0, Session.ActiveSeconds);
			// 1.5.0 (B1): the deduplicated fact record. Runs for EVERY damage hit, so the export can
			// answer a question about any past battle instead of only about the 3% forensics sampled.
			ev.FactId = FactStore.Observe(ev, source, victim,
				actorStats != null ? actorStats.Key : 0,
				victimStats != null ? victimStats.Key : 0);
			Session.AddEvent(ev);
		}
		catch { }
	}

	public static void RecordHeal(BattleObject target, BattleObject healer, int actual, int nominal)
	{
		if (actual <= 0 && nominal <= 0) return;
		EnsureSessionStartedFor(healer, target);
		// R72: same hold as the damage path, so a battle cannot end up with a heal on one axis and a hit on
		// the other (see ClockOriginHoldPolicy).
		if (HoldOriginEvent(true, target, healer, null, actual, nominal)) return;
		RecordHealNow(target, healer, actual, nominal);
	}

	private static void RecordHealNow(BattleObject target, BattleObject healer, int actual, int nominal)
	{
		EnsureSessionStartedFor(healer, target);
		BeginTimingIfNeeded();
		Rt.EventCount++;
		Session.NoteEvent();
		ActorStats actor = Session.GetActor(target, create: true);
		if (actor != null)
		{
			actor.HealingTaken += actual;
			actor.HealingTakenNominal += nominal;
			actor.AddSecondHeal(ClockOriginHoldPolicy.SecondIndex(Session.ActiveSeconds), actual);
			if (CharacterInfo.IsAllyTeam(actor.Team))
				Session.AddTeamHeal(ClockOriginHoldPolicy.SecondIndex(Session.ActiveSeconds), actual);
		}
		// 1.5.0 (A4): resolved OUTSIDE the block below so the event can carry stable keys. `healerStats` is
		// null when the healer object is missing, which is exactly what key 0 means.
		ActorStats healerStats = null;
		if (!GameRef.IsNull(healer))
		{
			ActorStats actor2 = Session.GetActor(healer, create: true);
			healerStats = actor2;
			if (actor2 != null)
			{
				actor2.HealingGiven += actual;
				actor2.HealingGivenNominal += nominal;
			}
		}
		else if (actor != null)
		{
			actor.HealingSelf += nominal;
		}
		if (Plugin.CfgVerbose.Value)
		{
			string line = $"[DpsMeter] HEAL {actual}/{nominal} {Desc(healer)} -> {Desc(target)}";
			Plugin.LogSource.LogInfo(line);
			RuntimeLog.Write(line);
		}
		try
		{
			Session.AddEvent(new BattleEvent
			{
				T = Session.ActiveSeconds,
				Type = "heal",
				Victim = NameOf(target),
				Attacker = NameOf(healer),
				Owner = NameOf(healer),
				Attr = "H",
				Amount = actual,
				Nominal = nominal,
				Source = (int)DamageSource.DirectHeal,
				Crit = false,
				AttackerTeam = TeamOf(healer),
				VictimTeam = TeamOf(target),
				// 1.5.0 (A4): the same stable keys as the damage path. `actor` is the TARGET of the heal
				// and `actor2` the healer, so the names line up with Attacker/Victim above.
				AttackerKey = healerStats != null ? healerStats.Key : 0,
				VictimKey = actor != null ? actor.Key : 0
			});
		}
		catch { }
	}
}
