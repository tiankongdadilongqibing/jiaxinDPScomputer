namespace DpsMeter;

/// <summary>
/// R85: which END of a damage event the F6 detail page reads.
///
/// The page had one perspective since it existed -- "what did this unit DEAL" -- and every decision it
/// made named that end directly (the attacker builds the unit list, the victim is the F7 filter, the row
/// reads "→ victim 伤害 N"). 承伤明细 asks the mirror question about the SAME events, so the two ends are
/// simply swapped; keeping that swap in one pure place is what stops the two pages from drifting into two
/// subtly different behaviours (a filter the list offers but the matcher never accepts is exactly the
/// class of bug this repo has shipped before).
///
/// Pure on purpose: no Unity, no IL2CPP, no Plugin, no UI state. The behaviour suite compiles this file
/// and executes it, so the key/matcher agreement below is a checked invariant rather than a comment.
/// </summary>
internal static class DetailPerspective
{
	/// <summary>What an unresolvable attacker is called in 承伤明细. It is a real BUCKET, not a gap: a hit
	/// whose attacker could not be read still happened to the unit, so it must be countable and named
	/// rather than dropped. The name doubles as the filter key's name half, which is why it is a constant
	/// and not a literal typed twice.</summary>
	public const string UnknownSource = "未知来源";

	/// <summary>Which unit the page is showing -- the attacker in 输出明细, the victim in 承伤明细.
	/// Matches BOTH name and team: this content fields the same character NAME on both sides, and keying by
	/// name alone used to merge our unit with the enemy copy (that is how the enemy healer's 回復反転
	/// damage ended up on our healer's row). A team of 0 on the event is "unknown", not "enemy", so it is
	/// accepted rather than filtered out.</summary>
	public static bool SubjectMatches(BattleEvent e, string whoName, int whoTeam, bool taken)
	{
		if (e == null || e.Type != "dmg") return false;
		if (taken)
		{
			if (e.Victim != whoName) return false;
			if (e.VictimTeam != 0 && e.VictimTeam != whoTeam) return false;
			return true;
		}
		if (e.Attacker != whoName) return false;
		if (e.AttackerTeam != 0 && e.AttackerTeam != whoTeam) return false;
		return true;
	}

	/// <summary>
	/// R86: may this unit be listed on the page at all?
	///
	/// 输出明细 lists EVERY party unit the session registered, and must keep doing so: a unit that dealt
	/// nothing is still a real answer ("this character contributed 0"), and hiding it would make the page
	/// silently disagree with the roster.
	///
	/// 承伤明细 is the mirror question, but it is NOT the mirror answer. A unit the battle never touched
	/// has no 承伤 record -- there is nothing to answer with -- yet step 1 of the page registers it anyway,
	/// so it used to enter the F11/F12 rotation (and the pinned bar's "n/N") as a row whose body could
	/// only ever print "(该角色本场没有受击事件)". The user reported exactly that. Drop it.
	///
	/// <paramref name="eventCount"/> is the tally the page accumulates with, under the CURRENT
	/// perspective, so the list and the table are decided by one number and cannot disagree about who is
	/// in the list. Kept pure and here (rather than inline in the renderer) because
	/// `Ui/OverlayUGUI.Rows.cs` cannot be compiled into the behaviour suite.
	/// </summary>
	public static bool Listable(bool taken, int eventCount)
	{
		return !taken || eventCount > 0;
	}

	/// <summary>The display name of the source of a hit: the attacker, or <see cref="UnknownSource"/> when
	/// the game gave us none (the "?" the export carries).</summary>
	public static string SourceName(BattleEvent e)
	{
		if (e == null) return UnknownSource;
		return (string.IsNullOrEmpty(e.Attacker) || e.Attacker == "?") ? UnknownSource : e.Attacker;
	}

	/// <summary>The filter key of an event's counterparty -- "name#team". 输出明细 filters by the TARGET,
	/// 承伤明细 by the SOURCE. The key is built from the RAW team, exactly as the selectable list builds it,
	/// so a filter the list offers can never fail to match.</summary>
	public static string CounterpartyKey(BattleEvent e, bool taken)
	{
		if (e == null) return "";
		return taken ? (SourceName(e) + "#" + e.AttackerTeam) : (e.Victim + "#" + e.VictimTeam);
	}

	/// <summary>Does this event pass the F7 counterparty filter? An empty filter means "every one".</summary>
	public static bool CounterpartyMatches(BattleEvent e, string filter, bool taken)
	{
		if (string.IsNullOrEmpty(filter)) return true;
		return CounterpartyKey(e, taken) == filter;
	}

	// ---- the words ---------------------------------------------------------------------------------
	// They live here because a reader checks them: the page title, the amount column and the F7 label are
	// the three strings that tell which side of the fight is on screen.

	public static string PageTitle(bool taken) { return taken ? "承伤明细" : "伤害明细"; }
	public static string AmountWord(bool taken) { return taken ? "承伤" : "伤害"; }
	public static string FilterWord(bool taken) { return taken ? "来源" : "目标"; }
}
