using System.Collections.Generic;

namespace DpsMeter;

/// <summary>
/// RF4f (plan section 9, third family): the per-battle ring of recent damage-calculation activities.
///
/// The state was a static List plus a bare trim loop inside the attribution code, so its two real rules --
/// "the newest activity wins the ordering" and "the oldest is dropped once the cap is reached" -- were only
/// visible by reading the writer, and the cap (64) was a private const next to the field. Both are here now,
/// and the readers (which iterate it backwards to find the most recent match) are unchanged.
///
/// What it deliberately does NOT do: it never decides whether an entry is USABLE. The age windows and the
/// value/team/position gates live in AttributionPolicy; this class only owns order, the cap and clearing.
/// Clearing happens at exactly the three points the matrix records: a session starts, a session is resumed,
/// and a session is finalised -- the caller decides, because only the caller knows which transition it is in.
/// </summary>
internal sealed class CalcActivityLog<T>
{
	/// <summary>How many recent activities are kept. 64 was the previous CalcEventMax, and the readers scan
	/// backwards from the newest, so the cap bounds the worst-case scan as well as the memory.</summary>
	public const int Max = 64;

	private readonly List<T> _items = new List<T>();

	public int Count { get { return _items.Count; } }

	/// <summary>Indexed access in insertion order: index 0 is the OLDEST entry still kept. The readers walk
	/// it backwards from Count-1, and one of them rewrites the entry it matched, hence the setter.</summary>
	public T this[int index]
	{
		get { return _items[index]; }
		set { _items[index] = value; }
	}

	/// <summary>Append, then drop from the FRONT until the cap holds. Trimming on the way in (rather than
	/// lazily on read) is what keeps the readers' index arithmetic valid.</summary>
	public void Add(T item)
	{
		_items.Add(item);
		while (_items.Count > Max) _items.RemoveAt(0);
	}

	public void Clear()
	{
		_items.Clear();
	}
}
