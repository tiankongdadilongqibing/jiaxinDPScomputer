using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// R78: the WORDS that describe a damage split (`Policy/AbsorbWording.cs`), and the two defects they fix.
	///
	/// A. THE NAMES WERE WRONG. R76 measured the return of `BattleObject.Damage` to be the OVERFLOW beyond the
	///    victim's remaining Life (`res == max(0, nominal - lifeBefore)`, 798/798 readable readings), so the two
	///    published labels described quantities the game never reported: what was printed as `实际伤害` is the
	///    part of the hit that did NOT fit, and what was printed as `被吸收/无效化` is the victim's remaining
	///    Life before the hit. The battle this round was opened for shows it verbatim: the boss's own damage
	///    number is 596,363 while the detail row said `实际伤害 96363 · 被吸收/无效化 500000`.
	///
	/// B. THE CRIT WAS HIDDEN. The split and the residual were mutually exclusive (`else if`), so on exactly
	///    the rows whose nominal exceeded the remaining Life the ×1.5 crit was not printed at all -- and reading
	///    the residual from the published amount instead prints `×0.242` (96363/397575), a mechanism no game
	///    rule has. Both are now printed, from the game's own value.
	///
	/// Every string here is PINNED verbatim: the row exists to be read next to a screenshot, so "contains" is
	/// not a test. The `[ABSPROBE] note` sentences are pinned the same way, and the note only uses the old word
	/// 被吸收 where R76's evidence says a shape deserves it.
	/// </summary>
	internal static void AbsorbWordingCases(Runner r)
	{
		r.Group("policy/absorb-wording");

		// ---- A: the two names, on the shape that started this round --------------------------------

		r.Str("an-oversized-row-names-the-overflow-and-the-remaining-life",
			AbsorbWording.DamageTail(596363, 96363, 500000),
			" · 超出剩余耐久(溢出) 96363 · 目标剩余耐久 500000"
			+ "(= 游戏口径 596363 − 超出剩余耐久 96363;非吸收;是否落地看命中前后 Life,本行不断言)");

		r.Str("a-hit-with-no-overflow-is-named-as-the-game-value",
			AbsorbWording.DamageTail(397575, 397575, 0),
			" · 游戏口径 397575");

		// The 397,575 hit of the same battle: no overflow, so the published number IS the game's own value and
		// calling it "超出剩余耐久" would be the mirror image of the old mistake.
		r.Str("the-no-overflow-tail-is-not-called-an-overflow",
			AbsorbWording.DamageTail(397575, 397575, 0),
			" · 游戏口径 397575");

		// ---- B: both suffixes on one row, and the basis they are read from -------------------------

		r.Str("an-oversized-crit-row-prints-the-split-and-the-crit-together",
			AbsorbWording.ChainTail(397575, 96363, 500000),
			" · 超出剩余耐久(溢出) 96363 · 目标剩余耐久 500000"
			+ "(= 游戏口径 596363 − 超出剩余耐久 96363;非吸收;是否落地看命中前后 Life,本行不断言)"
			+ " · 剩余倍率 ×1.500(会心/未识别部分)");

		r.Str("a-normal-row-still-prints-both-suffixes",
			AbsorbWording.ChainTail(397575, 397575, 0),
			" · 游戏口径 397575 · 剩余倍率 ×1.000(会心/未识别部分)");

		r.Eq("the-residual-basis-is-the-game-value",
			AbsorbWording.ResidualBasis(96363, 500000), 596363);

		// The counterfactual, and the reason the basis moved: from the published amount the SAME crit reads as a
		// 0.242 "residual" that no game rule explains -- this is the number the old row hid (it printed the
		// split INSTEAD of the residual).
		r.Str("the-published-amount-would-print-a-0.242-residual",
			((double)96363 / 397575).ToString("F3"), "0.242");

		r.Str("no-theory-means-no-residual-line",
			AbsorbWording.ChainTail(0, 12345, 0), " · 游戏口径 12345");

		r.Str("the-residual-line-alone-is-pinned",
			AbsorbWording.ResidualText(191664, 191664, 0),
			" · 剩余倍率 ×1.000(会心/未识别部分)");

		// ---- D (log side only): what a verdict MEANS, in words -------------------------------------

		r.Str("an-oversized-pool-note-says-nothing-landed",
			AbsorbWording.VerdictNote(AbsorbVerdict.OversizedPool, Live(596363, 96363, 500000, 500000)),
			"verdict=oversizedPool ⇒ 耐久未变化(lifeDrop=0),无落地可观测;96363 是超出剩余耐久的溢出量,"
			+ "500000 只是(游戏口径 − 溢出)的模型值,非吸收。");

		r.Str("an-oversized-note-quotes-the-life-drop-it-was-checked-against",
			AbsorbWording.VerdictNote(AbsorbVerdict.Oversized, Live(600000, 100000, 500000, 0)),
			"verdict=oversized ⇒ 耐久按 landed=500000 下降,与 landed 一致;100000 是超出剩余耐久的溢出量,未落地。");

		r.Str("a-withheld-hit-is-the-only-shape-called-absorbed",
			AbsorbWording.VerdictNote(AbsorbVerdict.WithheldNoReturn, Live(1000, 0, 5000, 4500)),
			"verdict=withheldNoReturn ⇒ 返回值未报溢出,而耐久只下降了 500(< 名义 1000);这才是可称为被吸收的形状。");

		r.Str("a-barrier-verdict-quotes-the-barrier-life-move",
			AbsorbWording.VerdictNote(AbsorbVerdict.Barrier, WithBarrierLife(700000, 500000, 520000, 500000)),
			"verdict=barrier ⇒ 障壁生命移动 20000;差异由障壁自己的读数承担,不是通用机制。");

		r.Str("an-unreadable-barrier-move-prints-a-question-mark",
			AbsorbWording.VerdictNote(AbsorbVerdict.BarrierShort, Plain(700000, 500000)),
			"verdict=pool ⇒ 障壁生命移动 ?;差异由障壁自己的读数承担,不是通用机制。");

		r.Str("an-unreadable-life-is-stated-not-guessed",
			AbsorbWording.VerdictNote(AbsorbVerdict.LifeUnreadable, Plain(1000, 0)),
			"verdict=lifeUnreadable ⇒ 读数缺失,不作断言。");

		r.True("a-verdict-that-decides-nothing-gets-no-note",
			AbsorbWording.VerdictNote(AbsorbVerdict.None, Plain(1000, 0)) == null
			&& AbsorbWording.VerdictNote((AbsorbVerdict)999, Plain(1000, 0)) == null);

		r.True("every-decided-verdict-gets-a-note",
			EveryDecidedVerdictHasANote());

		// The old word survives in exactly ONE sentence: the shape R76's evidence reserved it for. If a future
		// edit reintroduces it where the numbers do not support it, this case says so.
		r.Eq("the-old-word-is-used-by-exactly-one-shape",
			CountNotesUsingTheOldWord(), 1);
	}

	private static bool EveryDecidedVerdictHasANote()
	{
		foreach (AbsorbVerdict v in System.Enum.GetValues(typeof(AbsorbVerdict)))
		{
			if (v == AbsorbVerdict.None) continue;
			if (AbsorbWording.VerdictNote(v, Live(596363, 96363, 500000, 500000)) == null) return false;
		}
		return true;
	}

	private static int CountNotesUsingTheOldWord()
	{
		int n = 0;
		foreach (AbsorbVerdict v in System.Enum.GetValues(typeof(AbsorbVerdict)))
		{
			string note = AbsorbWording.VerdictNote(v, Live(596363, 96363, 500000, 500000));
			if (note != null && note.Contains("被吸收")) n++;
		}
		return n;
	}

	/// <summary>An observation whose BARRIER life was read (the classifier's `Barrier`/`BarrierShort` split), used
	/// here only to reach the barrier sentence of the note.</summary>
	private static AbsorbObservation WithBarrierLife(int nominal, int result, int barrierBefore, int barrierAfter)
	{
		AbsorbObservation o = Plain(nominal, result);
		o.BarrierReadable = true;
		o.BarrierLifeBefore = barrierBefore;
		o.BarrierLifeAfter = barrierAfter;
		return o;
	}
}
