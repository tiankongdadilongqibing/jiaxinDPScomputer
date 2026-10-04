using System;

namespace DpsMeter;

/// <summary>Which battle-wide shape an ability clause has. None = "this clause is not a battle-wide
/// rule" (it stays with the ordinary attacker/victim text scan).</summary>
internal enum GlobalRuleKind { None = 0, EnemyTakes = 1, AllyAttack = 2 }

/// <summary>
/// The classification of ONE ability clause: the shape plus every field the rule record needs. The probe
/// fills the IDENTITY fields itself (owner pointer, owner object, owner name, ability name) because those
/// are native reads; everything decided from the TEXT is here.
/// </summary>
internal struct GlobalRuleShape
{
	public GlobalRuleKind Kind;
	public double Factor;
	public bool PerStatus;
	/// <summary>The clause verbatim (the export and the [RULE] log show it).</summary>
	public string Text;
	public System.Collections.Generic.List<string> Tokens;
	public bool MagicOnly;
	public bool PhysOnly;
	public bool Vanguard;
	public bool Rearguard;
}

/// <summary>
/// RF4, family 2 (battle-wide pre-registered rules): the DECISION half of the registration path.
///
/// Why. `CompositionProbe.RegisterGlobalDebuffs` walks a unit's abilities through IL2CPP and turns the
/// ones that describe a battle-wide modifier into a rule record. Which CLAUSES those are was decided by a
/// chain of Japanese-substring gates buried in that loop, so it could only be exercised by fighting -- and
/// it is the reason two real rules (海魔の残滓 "毒/火傷状態の敵全ての被ダメージがそれぞれ+15%" and ミャウラ
/// "編成時、味方ヴァイスの魔法攻撃の与ダメージ+15%") behave the way they do. The gates move here verbatim;
/// the native scan stays in the probe.
///
/// The factor itself is still parsed by the probe's ParseDamageModifier (pure text maths) and is passed
/// in, so this file does not duplicate that parser.
/// </summary>
internal static class GlobalRuleClassifier
{
	/// <summary>The registry is never cleared at battle end (see ClearGlobalRules), so this valve is the
	/// only reclamation. Two steps, measured against the count AFTER the first step, exactly as before.</summary>
	public const int ReclaimThreshold = 400;
	public const int ClearAllThreshold = 600;

	public static bool ShouldReclaim(int count)
	{
		return count > ReclaimThreshold;
	}

	public static bool ShouldClearAll(int countAfterReclaim)
	{
		return countAfterReclaim > ClearAllThreshold;
	}

	/// <summary>
	/// Facts about a pointer that may already be registered: the stored entry (if any) and the name memo
	/// that detects a REUSED IL2CPP pointer. <paramref name="EntryCount"/> is -1 when the stored list itself
	/// is null -- it cannot happen today (`_globalRules[key] = rules` is never null) but the probe guards it,
	/// so the rule has to as well.
	/// </summary>
	public struct RegisterFacts
	{
		public bool HasEntry;
		public int EntryCount;
		/// <summary>`entry[0].OwnerName` when the entry has rules.</summary>
		public string FirstRuleOwnerName;
		public bool HasNameMemo;
		public string NameMemoOwner;
		public string CurrentName;
	}

	/// <summary>
	/// True = this unit was already scanned, so the (cheap but not free) ability walk is skipped. Two ways:
	/// the stored rules belong to a unit with this name, or an EMPTY entry belongs to one (the empty list is
	/// the "scanned, found nothing" memo). A different name means the pointer was reused by another unit and
	/// has to be re-scanned.
	/// </summary>
	public static bool SkipRescan(RegisterFacts f)
	{
		if (!f.HasEntry) return false;
		bool sameRules = f.EntryCount > 0 && f.FirstRuleOwnerName == f.CurrentName;
		bool sameEmptyMemo = f.EntryCount == 0 && f.HasNameMemo && f.NameMemoOwner == f.CurrentName;
		return sameRules || sameEmptyMemo;
	}

	/// <summary>
	/// Classify one already-split clause.
	///
	/// The gates are the production ones, in order: the clause must look like an enemy-takes or an
	/// allied-attack shape at all; an enemy-takes clause must say 全て/すべて AND name the target (敵/相手/
	/// 対象), and must NOT be about ブロック; neither shape may be HP-conditional (耐久/HP), because those
	/// stay with the per-hit scan where the victim's current HP is known; and the parsed factor must not be
	/// 1.0 (no modifier found).
	///
	/// <paramref name="parseFactor"/> is (clause, keyword) -> multiplier; production passes
	/// ParseDamageModifier. It is only called for a clause that already passed every gate, which is itself a
	/// contract worth a test: a rejected clause must not even look at the factor.
	/// </summary>
	public static GlobalRuleShape Classify(string clause, Func<string, string, double> parseFactor)
	{
		var shape = new GlobalRuleShape { Kind = GlobalRuleKind.None, Factor = 1.0, Text = clause };
		if (string.IsNullOrEmpty(clause)) return shape;
		bool enemyShape = clause.IndexOf("被ダメージ", StringComparison.Ordinal) >= 0;
		bool allyShape = clause.IndexOf("与ダメージ", StringComparison.Ordinal) >= 0
			&& clause.IndexOf("味方", StringComparison.Ordinal) >= 0;
		if (!enemyShape && !allyShape) return shape;
		if (enemyShape)
		{
			if (clause.IndexOf("全て", StringComparison.Ordinal) < 0
				&& clause.IndexOf("すべて", StringComparison.Ordinal) < 0) return shape;
			if (clause.IndexOf("敵", StringComparison.Ordinal) < 0
				&& clause.IndexOf("相手", StringComparison.Ordinal) < 0
				&& clause.IndexOf("対象", StringComparison.Ordinal) < 0) return shape;
			// owner-relative or HP-conditional clauses stay with the normal scan
			if (clause.IndexOf("ブロック", StringComparison.Ordinal) >= 0) return shape;
			if (clause.IndexOf("耐久", StringComparison.Ordinal) >= 0
				|| clause.IndexOf("HP", StringComparison.Ordinal) >= 0) return shape;
		}
		else
		{
			if (clause.IndexOf("耐久", StringComparison.Ordinal) >= 0
				|| clause.IndexOf("HP", StringComparison.Ordinal) >= 0) return shape;
		}
		double f = parseFactor(clause, enemyShape ? "被ダメージ" : "与ダメージ");
		if (f == 1.0) return shape;
		bool magic = clause.IndexOf("魔法", StringComparison.Ordinal) >= 0;
		bool phys = clause.IndexOf("物理", StringComparison.Ordinal) >= 0;
		shape.Kind = enemyShape ? GlobalRuleKind.EnemyTakes : GlobalRuleKind.AllyAttack;
		shape.Factor = f;
		shape.PerStatus = clause.IndexOf("それぞれ", StringComparison.Ordinal) >= 0;
		shape.Tokens = ClauseStatusRun.StatusTokens(clause);
		shape.MagicOnly = magic && !phys;
		shape.PhysOnly = phys && !magic;
		shape.Vanguard = clause.Contains("前衛") && !clause.Contains("後衛");
		shape.Rearguard = clause.Contains("後衛") && !clause.Contains("前衛");
		return shape;
	}
}
