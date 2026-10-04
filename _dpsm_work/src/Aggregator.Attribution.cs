using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace DpsMeter;

public static partial class Aggregator
{

	internal static void NoteCalcActivity(DamageCalculater calc, BattleObject attacker, BattleObject owner, BattleObject blocker, int dmg)
	{
		try
		{
			_calcEvents.Add(new CalcActivity
			{
				T = (Session != null) ? Session.ActiveSeconds : 0.0,
				A = attacker,
				O = owner,
				B = blocker,
				Dmg = dmg,
				Calc = calc
			});
			while (_calcEvents.Count > CalcEventMax) _calcEvents.RemoveAt(0);
			// RF4: the two "last calc" fields that were written here every hit had no reader (see Aggregator.cs).
		}
		catch { }
	}

	/// <summary>
	/// Composition of the damage calc behind this hit, built with the APPLIED damage so the shown
	/// multiplier is the real one.
	///
	/// Pairing order:
	///   0) the calc that is executing right now (DamageCalculater.Action -> ... -> BattleObject.Damage);
	///      its blocker is the target being damaged, and a single calc legitimately explains every
	///      target of one AoE cast -- this is the closest thing to an exact pairing.
	///   1) a recent calc for this victim with the same damage value.
	///   2) oldest unused calc for this victim (FIFO keeps multi-hit bursts in order).
	/// Anything below (0) is marked in the text so an approximate line never masquerades as exact.
	/// </summary>
	internal static void TryGetCompForVictim(BattleObject victim, int damage, int nominal, out string a, out string b, out string c, out string d, out CalcBreakdown brk)
	{
		a = "";
		b = "";
		c = "";
		d = "";
		brk = default(CalcBreakdown);
		int absorbed = (nominal > damage) ? (nominal - damage) : 0;
		try
		{
			double now = (Session != null) ? Session.ActiveSeconds : 0.0;

			// ---- 0) the currently executing calc ----
			DamageCalculater live = _activeCalc;
			if (live != null && _activeCalcT >= 0.0)
			{
				double age = now - _activeCalcT;
				// RF3: the live-pairing window is a policy decision (AttributionPolicy); the native blocker
				// read stays inside the window check, exactly where it was.
				if (AttributionPolicy.LiveAgeEligible(age, AttributionPolicy.LivePairMinAge,
				                                      AttributionPolicy.LivePairMaxAge))
				{
					BattleObject lb = null;
					try { lb = live.m_blocker; } catch { }
					bool sameTarget = !GameRef.IsNull(lb)
						&& GameRef.Same(lb, victim);
					if (AttributionPolicy.LiveTargetAdmitted(age, sameTarget, AttributionPolicy.LiveBlindAge))
					{
						CompositionProbe.BuildChainParts(live, victim, damage, out string la, out string l2, out string l3, out string l4, absorbed, out CalcBreakdown lbrk);
						if (!string.IsNullOrEmpty(la))
						{
							a = la;
							b = sameTarget ? l2 : (l2 + " · 按当前动作配对");
							// "A calc was running" is not the same as "this calc produced THIS hit". When the
							// damage does not match the calc's own value the composition is borrowed, and the
							// row must say so instead of presenting a wrong 计算威力 as exact.
							//
							// 1.3.0: the wording now says PAIRING, because that is what this check reports.
							// Measured 2026-10-03: 98.6% of the rows whose arithmetic DOES reproduce the game's
							// number still carried the old "本次伤害与该次计算值不符" label, which read as "the
							// composition is wrong". The arithmetic verdict is now the structured
							// calc.valueMatches field; this sentence only reports whether the pairing was
							// corroborated by the calc's own damage value.
							bool corroborated = CalcValueMatches(live, victim, damage, nominal);
							if (!corroborated)
								b += " · 配对未获结算对象佐证(计算威力仅供参考)";
							c = l3;
							d = l4;
							lbrk.Pair = AttributionPolicy.PairLabel(AttributionPolicy.LivePairKind(sameTarget));
							lbrk.PairCorroborated = corroborated;
							brk = lbrk;
							return;
						}
					}
				}
			}

			int best = -1;
			PairKind choice = PairKind.None;
			// ---- 1) same victim AND same damage value: newest first, window from the policy ----
			for (int i = _calcEvents.Count - 1; i >= 0; i--)
			{
				CalcActivity cc = _calcEvents[i];
				if (now - cc.T > AttributionPolicy.ExactValueWindow) break;
				if (cc.Used) continue;
				if (!GameRef.Same(cc.B, victim)) continue;
				if (cc.Calc == null) continue;
				if (AttributionPolicy.DmgMatches(cc.Dmg, damage, nominal)) { best = i; choice = PairKind.ExactValue; break; }
			}
			// ---- 2) oldest unused calc for this victim (FIFO keeps multi-hit bursts in order) ----
			if (best < 0)
			{
				for (int i = 0; i < _calcEvents.Count; i++)
				{
					CalcActivity cc = _calcEvents[i];
					if (now - cc.T > AttributionPolicy.FifoWindow) continue;
					if (cc.Used) continue;
					if (!GameRef.Same(cc.B, victim)) continue;
					if (cc.Calc == null) continue;
					best = i;
					choice = PairKind.Fifo;
					break;
				}
			}
			if (best < 0) return;
			CalcActivity hit = _calcEvents[best];
			hit.Used = true;
			_calcEvents[best] = hit;
			CompositionProbe.BuildChainParts(hit.Calc, hit.B, damage, out a, out b, out c, out d, absorbed, out brk);
			if (choice != PairKind.ExactValue && !string.IsNullOrEmpty(b)) b += " · 按时间顺序配对";
			// RF3: the reason code is explicit and its string has ONE definition (AttributionPolicy).
			// "value" = a recorded calc for this victim whose damage equals this hit (corroborated);
			// "fifo"  = oldest unused calc for this victim (pure time order, NOT corroborated).
			brk.Pair = AttributionPolicy.PairLabel(choice);
			brk.PairCorroborated = (choice == PairKind.ExactValue);
		}
		catch { }
	}

	/// <summary>
	/// Did the calc that is executing right now actually produce this hit?
	///
	/// The pairing key has to be BattleObject.Damage's ARGUMENT as well as its return value: the return is
	/// the damage left after 被吸收/无效化, so for an absorbed hit it can never equal the calc's own value
	/// (measured 2026-09-27: calc 421,140 → 198 applied, and 198 matched nothing).
	///
	/// 1.3.0 fix: this used to return on the FIRST record whose calc pointer matched, so a single AoE cast
	/// -- one calc, one record per target, plus the DamageAction / ActDamageAction pair -- was judged
	/// against whichever target resolved last. Measured on the two 1.2.3 battles (3,317 and 3,848 hits):
	/// the check reported "not corroborated" for 2,458 / 2,458 live-paired hits, which is why the old
	/// sentence was printed on 99.5% of all rows and why it read as "the composition is wrong" instead of
	/// "this pairing was not corroborated". The record's blocker is now part of the key and every record
	/// for (this calc, this victim) is considered, so the answer means what it says.
	/// </summary>
	private static bool CalcValueMatches(DamageCalculater calc, BattleObject victim, int damage, int nominal)
	{
		try
		{
			long want = PtrOf(calc);
			if (want == 0L) return false;
			for (int i = _calcEvents.Count - 1; i >= 0; i--)
			{
				CalcActivity cc = _calcEvents[i];
				if (PtrOf(cc.Calc) != want) continue;
				if (!GameRef.Same(cc.B, victim)) continue;
				// RF3: the same predicate as the pairing scan (this was a second copy of the condition).
				if (AttributionPolicy.DmgMatches(cc.Dmg, damage, nominal)) return true;
			}
		}
		catch { }
		return false;
	}

	private static BattleObject TryResolveCalcSource(BattleObject victim, int damage)
	{
		try
		{
			double now = (Session != null) ? Session.ActiveSeconds : 0.0;
			for (int i = _calcEvents.Count - 1; i >= 0; i--)
			{
				CalcActivity c = _calcEvents[i];
				if (now - c.T > AttributionPolicy.CalcSourceWindow) continue;
				if (!GameRef.Same(c.B, victim)) continue;
				// prefer explicit attacker, then summon owner; tiny damage ignored.
				// RF3: the preference order and the src= tag are a policy decision; the native reads stay
				// short-circuited exactly as before (the owner is read only when the attacker is null).
				bool hasA = !GameRef.IsNull(c.A);
				bool hasO = hasA ? false : !GameRef.IsNull(c.O);
				CalcSourceKind kind = AttributionPolicy.CalcSourcePriority(hasA, hasO);
				_lastCalcSrc = AttributionPolicy.CalcSourceTag(kind);
				if (kind == CalcSourceKind.Attacker) return c.A;
				if (kind == CalcSourceKind.Owner) return c.O;
				return null;
			}
		}
		catch { }
		_lastCalcSrc = "";
		return null;
	}

	/// <summary>
	/// 1.5.0 (A2): produce one pending damage figure from the calc that computed it. Called by the four
	/// hooks that return a damage number, so that `DamageSource`, the crit flag, the hit type and the
	/// effect id stop being constants in the export.
	///
	/// The blocker is taken from the argument when the hook has one and from `calc.m_blocker` otherwise
	/// (`ApplyBarrierDamage` / `ApplyEnchantDamage` take only the power, and they carry most of a battle's
	/// damage -- measured 1096 of one battle's hits through ApplyEnchantDamage alone).
	/// </summary>
	internal static void NoteHitDetail(DamageCalculater calc, BattleObject blocker, int damage)
	{
		if (calc == null) return;
		try
		{
			if (blocker == null) { try { blocker = calc.m_blocker; } catch { Rt.HitDetailErrors++; } }
			var src = DamageSource.Unknown;
			try { src = calc.m_damageSource; } catch { Rt.HitDetailErrors++; }
			int ht = -1;
			try { ht = (int)calc.m_hitType; } catch { Rt.HitDetailErrors++; }
			int eff = 0;
			try { eff = calc.m_effectId; } catch { Rt.HitDetailErrors++; }
			BattleObject atk = null;
			try { atk = calc.Attacker; } catch { Rt.HitDetailErrors++; }
			RecordHitDetail(atk, blocker, damage, src, (eDamageCalcType)ht, eff, CompositionProbe.ObservedCrit(blocker));
		}
		catch { Rt.HitDetailErrors++; }
	}

	public static void RecordHitDetail(BattleObject attacker, BattleObject blocker, int damage, DamageSource source, eDamageCalcType hitType, int effectId, byte critObserved)
	{
		if (Session != null && Session.InBattle)
		{
			try
			{
				Session.PendingHits.Add(new HitRecord
				{
					Attacker = attacker,
					Blocker = blocker,
					Damage = damage,
					Source = source,
					HitType = hitType,
					EffectId = effectId,
					CritObserved = critObserved,
					T = Session.ActiveSeconds
				});
				Rt.HitDetailProduced++;
				// RF3: the cap is BattleSession.MaxPending, not a second copy of the number.
				if (Session.PendingHits.Count > BattleSession.MaxPending)
				{
					int n = Session.PendingHits.Count - BattleSession.MaxPending;
					Session.PendingHits.RemoveRange(0, n);
					Rt.HitDetailTrimmed += n;
				}
			}
			catch { Rt.HitDetailErrors++; }
		}
	}

	public static void NoteActiveCalc(DamageCalculater calc)
	{
		_activeCalc = calc;
		_activeCalcT = (Session != null) ? Session.ActiveSeconds : 0.0;
		// attack start: remember the target's statuses, because the game judges status conditions here
		// (a debuff applied by this very hit must not raise its own hit)
		try { CompositionProbe.SnapshotStatuses(calc); } catch { }
	}
}
