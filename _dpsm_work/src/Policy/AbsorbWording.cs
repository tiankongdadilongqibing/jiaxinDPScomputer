using System.Globalization;
using System.Text;

namespace DpsMeter;

/// <summary>
/// R78: the WORDS the damage split is published in, and the basis the printed residual is read from.
///
/// WHY IT EXISTS. Until R78 two published labels described quantities the game never reported:
/// `实际伤害` was printed for whatever `BattleObject.Damage` returned, and `被吸收/无效化` for
/// `nominal - return`. R76 measured, on 798/798 readable readings, that the return is the OVERFLOW beyond the
/// victim's remaining Life (`res == max(0, nominal - lifeBefore)`). So on a call with `res > 0` the number
/// published as `实际伤害` is the part of the hit that did NOT fit, and `被吸收/无效化` is the part that did
/// (`nominal - res`, i.e. the victim's remaining Life before the hit) -- not something absorbed. Nothing in
/// the game ever absorbed in that shape: the only shape that would justify the word is R76's
/// `WithheldNoReturn`, and it is empty in every corpus measured so far.
///
/// The classifier that NAMES the shapes lives in `Policy/AbsorbClassifyPolicy.cs`; this file is the single
/// place that turns a shape into the text a reader sees, so the wording can be executed by the behaviour
/// suite instead of eyeballed. PURE by construction: no Unity, no IL2CPP, no Plugin, no Aggregator.
///
/// WHAT IT DELIBERATELY DOES NOT DO. It moves no number. The booked damage, the export keys,
/// `contribution.schemaVersion` and the 143 frozen exports are untouched here; changing the accounting
/// (`Hooks/BattleObjectHooks.cs`'s publish rule) is the C round's decision, and the text basis chosen for
/// the printed residual below is deliberately NOT propagated into the exported `CalcBreakdown.Residual`,
/// because that value is part of the export.
///
/// THE EQUIVALENCE THIS FILE RELIES ON (hook path, R75/R76). With
/// `damage = (__result &gt; 0) ? __result : __0` and `nominal = __0`, the accounting's
/// `absorbed = nominal - damage` is positive exactly when `res &gt; 0 &amp;&amp; nominal &gt; res`, i.e.
/// exactly when `AbsorbObservation.IsOversized()` holds; when `res &lt;= 0` the published amount is
/// `__0` verbatim, so `absorbed == 0` and the number the reader sees IS the game's own value.
/// </summary>
internal static class AbsorbWording
{
	/// <summary>
	/// The part of the detail row's first line that names the damage split. `absorbed == 0` means the return
	/// reported no overflow, and then the published amount is the game's own value verbatim -- so it is named
	/// as the game's value, not as an "actual damage" the game never published.
	/// </summary>
	internal static string DamageTail(int nominal, int published, int absorbed)
	{
		if (absorbed <= 0) return " · 游戏口径 " + N(published);

		var sb = new StringBuilder(180);
		sb.Append(" · 超出剩余耐久(溢出) ").Append(N(published));
		sb.Append(" · 目标剩余耐久 ").Append(N(absorbed));
		sb.Append("(= 游戏口径 ").Append(N(nominal)).Append(" − 超出剩余耐久 ").Append(N(published));
		sb.Append(";非吸收;是否落地看命中前后 Life,本行不断言)");
		return sb.ToString();
	}

	/// <summary>
	/// R78(B): the value the PRINTED `剩余倍率` is read from. On an oversized call the published amount is
	/// only the overflow, so a `×1.500` crit prints as `×0.242` (96363/397575) and reads as a mechanism that
	/// does not exist; the game's own value (`nominal = published + absorbed`) is the basis that matches the
	/// formula's `会心ダメージ率`. This is the TEXT basis only -- `Model/CalcBreakdown.cs`'s `Residual`
	/// (exported as `calc.residual`, and a `forensics` bucket key) keeps its old basis on purpose.
	/// </summary>
	internal static long ResidualBasis(int published, int absorbed)
	{
		return (long)published + absorbed;
	}

	/// <summary>
	/// R78(B): the printed `剩余倍率`, read from the game's value (see <see cref="ResidualBasis"/>). The two
	/// suffixes used to be mutually exclusive: an oversized call printed the split and SUPPRESSED this line, so
	/// the ×1.5 crit that caused the call was invisible on exactly the rows that needed it. They are now
	/// independent -- an oversized crit row prints both. Empty when the theory is not positive.
	/// </summary>
	internal static string ResidualText(long theory, int published, int absorbed)
	{
		if (theory <= 0) return "";
		double rate = (double)ResidualBasis(published, absorbed) / theory;
		return " · 剩余倍率 ×" + rate.ToString("F3") + "(会心/未识别部分)";
	}

	/// <summary>
	/// R78(A+B): everything the detail row's first line says AFTER `理论 … = theory`, in one call, so the
	/// order and the mutual non-exclusion are unit-tested rather than assembled inline. `nominal` is derived
	/// here (`published + absorbed`) because that IS the game's own value on the hook path.
	/// </summary>
	internal static string ChainTail(long theory, int published, int absorbed)
	{
		return DamageTail(published + absorbed, published, absorbed) + ResidualText(theory, published, absorbed);
	}

	/// <summary>
	/// R78(D, log side only): the sentence that says in words what a DECIDING verdict means. The hit line
	/// carries readings (`res=`, `life=`, `lifeDrop=`, `verdict=`); a reader still has to know which of them
	/// the published labels used to get wrong -- `oversizedPool` in particular is the shape that was being
	/// described as an absorption of 500,000. Returns null for a verdict that decides nothing.
	/// </summary>
	internal static string VerdictNote(AbsorbVerdict v, AbsorbObservation o)
	{
		string head = "verdict=" + AbsorbClassifyPolicy.Name(v) + " ⇒ ";
		switch (v)
		{
			case AbsorbVerdict.Oversized:
				return head + "耐久按 landed=" + N(o.Landed()) + " 下降,与 landed 一致;" + N(o.Overflow())
					+ " 是超出剩余耐久的溢出量,未落地。";
			case AbsorbVerdict.OversizedPool:
				return head + "耐久未变化(lifeDrop=0),无落地可观测;" + N(o.Overflow())
					+ " 是超出剩余耐久的溢出量," + N(o.Landed()) + " 只是(游戏口径 − 溢出)的模型值,非吸收。";
			case AbsorbVerdict.OversizedPartial:
				return head + "耐久下降 " + N(o.LifeDrop()) + ",既非 0 也非 landed=" + N(o.Landed())
					+ ";落地与否本行不断言。";
			case AbsorbVerdict.OversizedUnreadable:
				return head + "耐久不可读,溢出 " + N(o.Overflow()) + " 与 landed=" + N(o.Landed()) + " 都无法佐证。";
			case AbsorbVerdict.Barrier:
			case AbsorbVerdict.BarrierShort:
				return head + "障壁生命移动 " + N(BarrierMove(o)) + ";差异由障壁自己的读数承担,不是通用机制。";
			case AbsorbVerdict.TakeOver:
				return head + "BattleObject.DamageTakeOver 在本次命中内运行过;差异由该钩子承担。";
			case AbsorbVerdict.FixedDamage:
				return head + "BattleObject.TryGetFixedDamage 在本次命中内应答过;差异由该钩子承担。";
			case AbsorbVerdict.Invincible:
				return head + "无敌系旗标置位;本次调用无法判定落地。";
			case AbsorbVerdict.NoLifeMovement:
				return head + "返回值未报溢出,耐久也未变化,未观测到落地。";
			case AbsorbVerdict.WithheldNoReturn:
				return head + "返回值未报溢出,而耐久只下降了 " + N(o.LifeDrop()) + "(< 名义 " + N(o.Nominal)
					+ ");这才是可称为被吸收的形状。";
			case AbsorbVerdict.LifeUnreadable:
			case AbsorbVerdict.Unreadable:
				return head + "读数缺失,不作断言。";
			default:
				return null;
		}
	}

	private static int BarrierMove(AbsorbObservation o)
	{
		return o.BarrierReadable ? (o.BarrierLifeBefore - o.BarrierLifeAfter) : int.MinValue;
	}

	private static string N(int v)
	{
		return (v == int.MinValue) ? "?" : v.ToString(CultureInfo.InvariantCulture);
	}
}
