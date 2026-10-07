using System.Collections.Generic;

namespace DpsMeter;

/// <summary>Per-actor accumulated statistics for one battle session.</summary>
public sealed class ActorStats
{
	/// <summary>
	/// 1.5.0 (A4): a STABLE per-session identity, so an event can be joined to this actor without relying
	/// on the display name.
	///
	/// Why: the display name is not unique -- BattleEvent's own comment records that this content can field
	/// the same character name on both sides, and mirror summons / same-kind tokens can collide within one
	/// team. Every join from an event to the roster, the ability table or a live resistance reading had to
	/// key on (name, team) and silently mixed those cases up. Token-merged summons share their owner's
	/// stats object, so they share the key, which is the intended grouping.
	/// </summary>
	public int Key;

	public BattleObject Source;

	public string Name = "";

	public TeamType Team;

	/// <summary>P / E / T / Boss / B / C / U -- raw object-kind label.</summary>
	public string Kind = "";

	public long DamageDealt;

	/// <summary>Damage this actor dealt to its OWN team (heal reversal / self-damage). Not output.</summary>
	public long DamageFriendly;

	public int FriendlyHits;

	public long DamageTaken;

	/// <summary>
	/// Sum of BattleObject.Damage's ARGUMENT (the damage the game accounted for), which is what
	/// CharacterStatistics.TakenDamage accumulates. DamageTaken holds the PUBLISHED amount = that argument for
	/// a hit with no overflow, and the call's RETURN (the overflow) when there is one. R78:
	/// DamageTakenNominal - DamageTaken is 超出剩余耐久 / 非吸收 -- NOT 被吸收/无效化; see
	/// `Policy/AbsorbWording.cs` and R76's law `res == max(0, nominal - lifeBefore)`.
	/// </summary>
	public long DamageTakenNominal;

	/// <summary>R78: 超出剩余耐久/非吸收 (the old name blamed an absorption). On an overflow call it is the
	/// victim's remaining Life before the hit. 游戏口径 = DamageTaken + this.</summary>
	public long DamageAbsorbed;

	public long HealingGiven;

	public long HealingGivenNominal;

	public long HealingTaken;

	public long HealingTakenNominal;

	public long HealingSelf;

	public long MaxHitDamage;

	public bool IsSummonMerge;

	public int HitCount;

	public int CritCount;

	public long CritDamage;

	public long NonCritDamage;

	public double FirstHitTime;

	public double LastHitTime;

	/// <summary>
	/// This unit's ability roster with provenance (Composition/AbilityRoster.cs), built once per battle and
	/// cached here on purpose: Aggregator nulls <see cref="Source"/> right after the export, so anything the
	/// detail view or the export needs must already live in managed state by then. Null = never built.
	/// </summary>
	public List<RosterAbility> Roster;

	/// <summary>Battle-end talent table: only the talents that actually did something this battle.</summary>
	public List<TalentUsage> TalentTable;

	/// <summary>Skill effect-id -> damage (best-effort attribution).</summary>
	public readonly Dictionary<int, long> SkillDamage = new Dictionary<int, long>();

	/// <summary>Skill effect-id -> hit count.</summary>
	public readonly Dictionary<int, int> SkillHits = new Dictionary<int, int>();

	public readonly Dictionary<int, string> SkillNames = new Dictionary<int, string>();

	/// <summary>DamageSource enum value -> damage (DirectAttack / Dot / Reflection ...).</summary>
	public readonly Dictionary<int, long> SourceDamage = new Dictionary<int, long>();

	/// <summary>How damage was attributed: "A" attacker, "O" owner fallback, "?" unknown source.</summary>
	public string AttrMode = "";

	/// <summary>Damage-time curve samples. Cumulative damage at battle-second T.</summary>
	public readonly List<DamageSample> DamageHistory = new List<DamageSample>();

	/// <summary>Per-second damage buckets for the DPS chart; index = battle second.</summary>
	private readonly List<long> _secondDamage = new List<long>();

	/// <summary>Per-second damage TAKEN by this actor; index = battle second.</summary>
	private readonly List<long> _secondTaken = new List<long>();

	/// <summary>Per-second healing RECEIVED by this actor; index = battle second.</summary>
	private readonly List<long> _secondHeal = new List<long>();

	/// <summary>Per-second remaining HP percentage (0..100); index = battle second.</summary>
	private readonly List<float> _hpPct = new List<float>();

	public const int MaxDamageSamples = 6000;

	public void AddHpPct(int sec, float pct)
	{
		if (sec < 0) return;
		while (_hpPct.Count <= sec) _hpPct.Add(100f);
		_hpPct[sec] = pct;
	}

	public float GetHpPct(int sec)
	{
		if (sec < 0 || sec >= _hpPct.Count) return 100f;
		return _hpPct[sec];
	}

	public long GetSecondDamage(int sec)
	{
		if (sec < 0 || sec >= _secondDamage.Count) return 0L;
		return _secondDamage[sec];
	}

	public int SecondCount => _secondDamage.Count;

	public int MaxSecond()
	{
		int m = _secondDamage.Count;
		if (_secondTaken.Count > m) m = _secondTaken.Count;
		if (_secondHeal.Count > m) m = _secondHeal.Count;
		return m;
	}

	public void AddSecondDamage(int sec, long amount)
	{
		if (sec < 0) return;
		while (_secondDamage.Count <= sec) _secondDamage.Add(0L);
		_secondDamage[sec] += amount;
	}

	public long GetSecondTaken(int sec)
	{
		if (sec < 0 || sec >= _secondTaken.Count) return 0L;
		return _secondTaken[sec];
	}

	public void AddSecondTaken(int sec, long amount)
	{
		if (sec < 0) return;
		while (_secondTaken.Count <= sec) _secondTaken.Add(0L);
		_secondTaken[sec] += amount;
	}

	public long GetSecondHeal(int sec)
	{
		if (sec < 0 || sec >= _secondHeal.Count) return 0L;
		return _secondHeal[sec];
	}

	public void AddSecondHeal(int sec, long amount)
	{
		if (sec < 0) return;
		while (_secondHeal.Count <= sec) _secondHeal.Add(0L);
		_secondHeal[sec] += amount;
	}

	public double Dps(double activeSeconds)
	{
		if (!(activeSeconds > 0.5)) return 0.0;
		return (double)DamageDealt / activeSeconds;
	}

	/// <summary>Append a cumulative damage sample at time t (merges events that are closer than ~40ms to bound memory).</summary>
	public void AddSample(double t, long cumulative)
	{
		if (DamageHistory.Count == 0)
		{
			DamageHistory.Add(new DamageSample { T = t, D = cumulative });
			return;
		}
		var last = DamageHistory[DamageHistory.Count - 1];
		if (t - last.T < 0.04)
		{
			last.D = cumulative;
			DamageHistory[DamageHistory.Count - 1] = last;
			return;
		}
		if (DamageHistory.Count >= MaxDamageSamples)
		{
			// compact: keep every 2nd old sample
			for (int i = 0; i < DamageHistory.Count / 2; i++)
				DamageHistory[i] = DamageHistory[i * 2];
			DamageHistory.RemoveRange(DamageHistory.Count / 2, DamageHistory.Count - DamageHistory.Count / 2);
		}
		DamageHistory.Add(new DamageSample { T = t, D = cumulative });
	}
}

public struct DamageSample
{
	public double T;
	public long D;
}
