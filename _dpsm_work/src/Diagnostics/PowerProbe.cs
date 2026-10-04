using System;
using System.Collections.Generic;
using UnityEngine;

namespace DpsMeter;

/// <summary>
/// PROBE (targeted): which attack value did the game use to build 计算威力?
///
/// The composition line prints `攻击力 A × 系数 R = 计算威力 P`. P is the game's own number
/// (DamageCalculater.m_power) and is fixed when the calc is built; A has to be read from the game, and
/// DamageCalculater exposes no "attack used" field. Reading A late (at damage time, or even at
/// DamageCalculater.Action) therefore sometimes catches a DIFFERENT value than the one P was built
/// from -- measured on skill #10024: 44 hits read 攻击力 32435 with P = 32435 (系数 1.00, correct) while
/// 2 hits read 攻击力 6065 with the SAME P = 32435, giving a nonsense 系数 5.35 (the identical damage
/// value 7299 appeared under both readings, proving 32435 is the real basis).
///
/// This probe reads the attacker's BattleObject.Power in the PREFIX of DamageCalculater's attack
/// constructor, i.e. BEFORE the constructor body computes the power. That value should be exactly the
/// one the power is built from. The reading is kept per calc pointer and reported by the [POWER]
/// diagnostic together with the later readings, so one battle tells us which source is authoritative.
///
/// The same candidate list is also used for the DISPLAY tiebreak: when a skill's own hits have
/// established its damage rate, the candidate whose ratio matches that rate wins, whatever the hook order.
/// </summary>
internal static class PowerProbe
{
	private struct Entry
	{
		public long Calc;
		public int Atk;
		public string AtkName;
		public long Stamp;
	}

	private static readonly Dictionary<long, Entry> _byCalc = new Dictionary<long, Entry>();
	/// <summary>Most recent ctor read per ATTACKER object (pointer -> [power, stamp]). Used as a fallback
	/// candidate when the calc pointer is not usable inside the constructor prefix.</summary>
	private static readonly Dictionary<long, int[]> _byAttacker = new Dictionary<long, int[]>();
	private static long _seq;
	private const int MaxEntries = 256;
	private const int MaxLogLines = 300;
	private static int _logged;

	// probe heartbeat: proves whether the constructor prefixes run at all and what they can read
	private static long _ctorCalls;
	private static long _ctorRecorded;
	private static long _ctor2Calls;
	private static long _ctor3Calls;
	private static long _addBlockerCalls;
	private static int _lastAtk;
	private static long _lastPtr;
	private static int _lastPow;
	private static long _lastTick;

	/// <summary>Count a call of one of the other DamageCalculater constructors (signature unknown a priori;
	/// the heartbeat reports which overload the game actually uses).</summary>
	internal static void NoteOtherCtor(int which)
	{
		try
		{
			if (which == 2) _ctor2Calls++;
			else if (which == 3) _ctor3Calls++;
			else if (which == 4) _addBlockerCalls++;
		}
		catch { }
	}

	/// <summary>
	/// Called from the PREFIX of DamageCalculater(BattleObject attacker, BattleObject blocker, …):
	/// runs before the body, so the attacker's Power here is the value the power is about to use.
	/// </summary>
	internal static void NoteCtor(DamageCalculater calc, BattleObject attacker)
	{
		_ctorCalls++;
		try
		{
			if (!Enabled) return;
			if (calc == null) return;
			long key = 0;
			try { key = calc.Pointer.ToInt64(); } catch { }
			long akey = 0;
			try { if (!GameRef.IsNull(attacker)) akey = attacker.Pointer.ToInt64(); } catch { }
			int atk = 0;
			try { if (!GameRef.IsNull(attacker)) atk = attacker.Power; } catch { }
			_lastPtr = key;
			_lastAtk = atk;
			try { _lastPow = CompositionProbe.Power(calc); } catch { }
			_lastTick = DateTime.Now.Ticks;
			if (key == 0 && akey == 0) return;
			if (key != 0)
			{
				var e = new Entry { Calc = key, Stamp = ++_seq, Atk = atk };
				try { if (!GameRef.IsNull(attacker)) e.AtkName = CharacterInfo.DisplayName(attacker); } catch { }
				_byCalc[key] = e;
				if (_byCalc.Count > MaxEntries)
				{
					long oldest = long.MaxValue, oldestKey = 0;
					foreach (var kv in _byCalc)
						if (kv.Value.Stamp < oldest) { oldest = kv.Value.Stamp; oldestKey = kv.Key; }
					if (oldestKey != 0) _byCalc.Remove(oldestKey);
				}
			}
			if (akey != 0)
			{
				_byAttacker[akey] = new int[] { atk, (int)(_seq++) };
				if (_byAttacker.Count > MaxEntries) _byAttacker.Clear();
			}
			_ctorRecorded++;
		}
		catch { }
	}

	/// <summary>Attack read for this calc before its body ran (0 = not observed).</summary>
	internal static int CtorPowerOf(DamageCalculater calc)
	{
		try
		{
			if (calc == null) return 0;
			long key = 0;
			try { key = calc.Pointer.ToInt64(); } catch { }
			if (key == 0) return 0;
			Entry e;
			return _byCalc.TryGetValue(key, out e) ? e.Atk : 0;
		}
		catch { return 0; }
	}

	/// <summary>
	/// Attack read in a constructor prefix for this ATTACKER, when the read is recent. Fallback candidate
	/// for builds where the calc's own pointer is not usable inside the prefix.
	/// </summary>
	internal static int RecentPowerOfAttacker(BattleObject atk)
	{
		try
		{
			if (GameRef.IsNull(atk)) return 0;
			if (_byAttacker.Count == 0) return 0;
			long key = 0;
			try { key = atk.Pointer.ToInt64(); } catch { }
			if (key == 0) return 0;
			int[] rec;
			if (!_byAttacker.TryGetValue(key, out rec) || rec == null) return 0;
			// freshness: the last ctor read must belong to the last ~40 recorded calls
			if (_seq - rec[1] > 40) return 0;
			return rec[0];
		}
		catch { return 0; }
	}

	/// <summary>One [POWERHOOK] heartbeat line: is the constructor prefix running, and what does it see?</summary>
	internal static string Heartbeat()
	{
		try
		{
			if (!Enabled) return "";
			return "[POWERHOOK] ctor1(attacker,blocker,abilities)=" + _ctorCalls
				+ " ctor2(damage,...)=" + _ctor2Calls
				+ " ctor3(effectId,...)=" + _ctor3Calls
				+ " AddBlocker=" + _addBlockerCalls
				+ " recorded=" + _ctorRecorded
				+ " lastPtr=" + _lastPtr + " lastAtk=" + _lastAtk + " calcPowerInPrefix=" + _lastPow
				+ " attackers=" + _byAttacker.Count + " calcs=" + _byCalc.Count
				+ " lastAgo=" + ((_lastTick > 0) ? ((DateTime.Now.Ticks - _lastTick) / 10000000.0).ToString("F1") + "s" : "-");
		}
		catch { return ""; }
	}

	internal static string CtorNameOf(DamageCalculater calc)
	{
		try
		{
			if (calc == null) return "";
			long key = 0;
			try { key = calc.Pointer.ToInt64(); } catch { }
			if (key == 0) return "";
			Entry e;
			return _byCalc.TryGetValue(key, out e) ? (e.AtkName ?? "") : "";
		}
		catch { return ""; }
	}

	internal static bool Enabled
	{
		get
		{
			try { return Plugin.CfgPowerProbe == null || Plugin.CfgPowerProbe.Value; }
			catch { return false; }
		}
	}

	/// <summary>
	/// One [POWER] line: every candidate attack for this hit plus the rate the skill's own hits agree on,
	/// and which candidate was picked. Written only when the readings disagree, so a normal battle stays
	/// quiet.
	/// </summary>
	internal static void Log(long calcKey, int effectId, double rate, int pow, int ctorAtk, int actionAtk,
		int liveAtk, int ownerAtk, int impliedAtk, string picked, string discarded)
	{
		try
		{
			if (_logged >= MaxLogLines) return;
			_logged++;
			string line = "[POWER] skill#" + effectId + " ptr=" + calcKey
				+ " rate=" + (rate > 0.0 ? rate.ToString("F2") : "?")
				+ " pow=" + pow
				+ " | 构造前=" + ctorAtk
				+ " 攻击开始快照=" + actionAtk
				+ " 结算时=" + liveAtk
				+ (ownerAtk > 0 ? (" 归属者=" + ownerAtk) : "")
				+ " 主档反推=" + impliedAtk
				+ " | 采用=" + picked
				+ (string.IsNullOrEmpty(discarded) ? "" : (" 忽略=" + discarded));
			RuntimeLog.Write(line);
		}
		catch { }
	}

	internal static void Reset()
	{
		try { _byCalc.Clear(); _logged = 0; } catch { }
	}
}
