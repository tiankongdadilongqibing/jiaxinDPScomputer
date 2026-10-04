using System;
using System.Collections.Generic;

namespace DpsMeter;

/// <summary>
/// RF4, second family: the battle-wide rule registry -- the pre-registered rules that must OUTLIVE the
/// battle they were collected in.
///
/// Why it is a container and not two static dictionaries. The matrix found this state to be
/// "cross-session state with no owner and no reset entry point": it is written when a unit is CREATED
/// (during a scene load that can happen BEFORE the previous battle is finalised), it is read on every hit,
/// and the only thing that ever removes an entry is the reclamation valve. The comments record what
/// happens when that is misunderstood: a finalisation that cleared the table made a battle-wide buff
/// disappear until its owner happened to act again (measured -- ミャウラ's ally buff, registered at
/// 01:29:32, wiped by the 01:29:53 idle finalise, back only at 01:29:53.215, so [賢導]トレイラ's opening
/// hits read x1.491 instead of x1.714).
///
/// Three properties this file is responsible for, each pinned by a test:
///   * NOTHING in it is called by a battle finalisation -- survival is the default;
///   * reclamation drops ONLY entries whose owner object is gone, and an EMPTY entry always goes (it is
///     the "scanned, found nothing" memo, so a still-alive unit gets re-scanned once);
///   * enumeration order is insertion order, because the apply path multiplies doubles in that order and
///     floating-point multiplication is not associative (the plan forbids reordering it).
///
/// Generic over the rule type so it stays game-free: the probe passes the aliveness predicate that reads
/// `GameRef.IsNull(rule.OwnerObj)`, and the tests pass a stub.
/// </summary>
internal sealed class GlobalRuleRegistry<TRule>
{
	// The SAME collection types and the same mutation sequence as before, because both the iteration order
	// (see the class comment) and the reuse behaviour of Dictionary depend on them.
	private readonly Dictionary<long, List<TRule>> _rules = new Dictionary<long, List<TRule>>();
	private readonly Dictionary<long, string> _ownerName = new Dictionary<long, string>();

	public int Count { get { return _rules.Count; } }

	/// <summary>Insertion-ordered enumeration, used by the apply path.</summary>
	public Dictionary<long, List<TRule>>.Enumerator GetEnumerator() { return _rules.GetEnumerator(); }

	public bool TryGet(long key, out List<TRule> rules) { return _rules.TryGetValue(key, out rules); }

	public bool HasOwnerName(long key) { return _ownerName.ContainsKey(key); }

	public string OwnerNameOf(long key)
	{
		string n;
		return _ownerName.TryGetValue(key, out n) ? n : null;
	}

	/// <summary>Register even an empty rule list (that is the "scanned, found nothing" memo), and remember
	/// the name so a REUSED pointer can be detected. Two assignments, in this order, as before.</summary>
	public void Set(long key, string ownerName, List<TRule> rules)
	{
		_rules[key] = rules;
		_ownerName[key] = ownerName;
	}

	/// <summary>
	/// Drop the entries whose owner object no longer exists. Called only by the registration valve;
	/// NEVER by a battle finalisation.
	///
	/// The dead keys are collected first and removed afterwards, in discovery order: mutating a Dictionary
	/// while enumerating it is invalid, and the removal order affects its free list, i.e. the enumeration
	/// order of everything that comes later. Returns how many entries were dropped.
	/// </summary>
	public int Reclaim(Func<TRule, bool> isOwnerAlive)
	{
		var dead = new List<long>();
		foreach (var kv in _rules)
		{
			var list = kv.Value;
			bool alive = false;
			if (list != null)
			{
				for (int i = 0; i < list.Count; i++)
				{
					if (isOwnerAlive(list[i])) { alive = true; break; }
				}
			}
			// an empty list is only the memo: drop it so a still-alive unit is re-scanned exactly once
			if (!alive) dead.Add(kv.Key);
		}
		for (int i = 0; i < dead.Count; i++)
		{
			_rules.Remove(dead[i]);
			_ownerName.Remove(dead[i]);
		}
		return dead.Count;
	}

	public void ClearAll()
	{
		_rules.Clear();
		_ownerName.Clear();
	}
}
