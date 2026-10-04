using System.Text;
using UnityEngine;

namespace DpsMeter;

/// <summary>
/// Critical-hit and attribute-advantage observation.
/// DamageCalculater.GetFlyTextNumberSizeForAttack receives exactly the two factors the damage
/// chain cannot read from stats (attribute rate and whether the hit crit), so they are captured
/// here and matched to the calc by target + time (best effort, see Plugin.TryPatchCritProbe).
/// </summary>
public static partial class CompositionProbe
{
	// ---- critical / attribute-rate observation (see Probe.CalcFlyTextSizeHook) ----
	private static BattleObject _critBlocker;
	private static double _critT = -1.0;
	private static bool _critFlag;
	private static float _critAttrRate = 1f;
	private static bool _critHasFlag;
	private static bool _critDefGreater;

	/// <summary>
	/// Record the crit/attribute flags that DamageCalculater passes to the fly-text sizing call.
	/// Best effort: these flags are only usable when that call happens for the same target around
	/// the same time as the damage calc.
	/// </summary>
	public static void NoteCrit(DamageCalculater calc, float attributeRate, bool isCritical, bool defenseGreater)
	{
		try
		{
			_critBlocker = null;
			try { _critBlocker = calc.m_blocker; } catch { }
			_critT = Now();
			_critFlag = isCritical;
			_critAttrRate = attributeRate;
			_critDefGreater = defenseGreater;
			_critHasFlag = true;
		}
		catch { }
	}

	private static double Now()
	{
		try { return (Aggregator.Session != null) ? Aggregator.Session.ActiveSeconds : 0.0; }
		catch { return 0.0; }
	}

	/// <summary>
	/// 1.5.0 (A3): the game's OWN crit flag for this target, as a tri-state --
	/// 0 = not observed, 1 = observed NOT a crit, 2 = observed crit.
	///
	/// This is the single predicate behind both the display and the exported data: "the game said no" and
	/// "we never saw the flag" are different facts, and before 1.5.0 only the display consumed this signal
	/// at all (the per-hit `crit` field came from a channel with no producer, so it was a constant false
	/// in all 570,078 events of the 768-export corpus while this flag sat unread).
	/// </summary>
	internal static byte ObservedCrit(BattleObject blocker)
	{
		try
		{
			if (!_critHasFlag) return 0;
			if (GameRef.IsNull(blocker)) return 0;
			if (!GameRef.Same(_critBlocker, blocker)) return 0;
			double dt = Now() - _critT;
			// RF3c: the two bounds are named, and the upper one IS the clock's frame-delta bound.
			if (!CompositionTolerancePolicy.CritDeltaUsable(dt)) return 0;
			return _critFlag ? (byte)2 : (byte)1;
		}
		catch { return 0; }
	}

	/// <summary>Crit/attribute text for this calc's target, when the flags were observed in time.
	/// Reads <see cref="ObservedCrit"/> so the line and the data can never disagree.</summary>
	private static string CritText(BattleObject blocker)
	{
		try
		{
			byte oc = ObservedCrit(blocker);
			if (oc == 0) return "";
			var sb = new StringBuilder(40);
			sb.Append(oc == 2 ? "会心:是" : "会心:否");
			if (_critAttrRate > 1.001f) sb.Append(" · 属性倍率 ×").Append(_critAttrRate.ToString("F2"));
			if (_critDefGreater) sb.Append(" · 防御>攻击(保底伤害)");
			return sb.ToString();
		}
		catch { return ""; }
	}
}
