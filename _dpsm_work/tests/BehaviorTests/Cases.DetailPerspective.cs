using System;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// R85: the F6 detail view gained a second PERSPECTIVE. 输出明细 (what our units dealt) already existed;
	/// 承伤明细 asks the mirror question about the very same events, so the page's two ends are swapped.
	///
	/// The subject is the one invariant a comment cannot hold: the F7 list is BUILT from
	/// <see cref="DetailPerspective.CounterpartyKey"/> and the F7 step is MATCHED with
	/// <see cref="DetailPerspective.CounterpartyMatches"/>. If those two ever disagree, the panel offers a
	/// filter that can never match anything -- it would look like "this unit took no damage from that
	/// source" instead of "the filter is broken", which is the worst kind of wrong here. The case
	/// `the-f7-list-key-is-accepted-by-the-f7-matcher` states that as one assertion.
	///
	/// The second subject is that an UNRESOLVABLE attacker is a named BUCKET, not a gap: a hit whose
	/// attacker the game did not report still landed on the unit, so it has to be countable (未知来源)
	/// rather than silently dropped from 承伤明细 while the roster's 受击 total still includes it.
	/// </summary>
	internal static void DetailPerspectiveCases(Runner r)
	{
		r.Group("ui/detail-perspective");

		// ---- which end of the event the page reads -----------------------------------------------------
		BattleEvent e = new BattleEvent
		{
			Type = "dmg",
			T = 12.5,
			Attacker = "我方甲", AttackerTeam = 1, AttackerKey = 11,
			Victim = "我方乙", VictimTeam = 1, VictimKey = 12,
			Amount = 1234,
		};
		r.True("the-dealt-subject-is-the-attacker",
			DetailPerspective.SubjectMatches(e, "我方甲", 1, false)
			&& !DetailPerspective.SubjectMatches(e, "我方乙", 1, false));
		r.True("the-taken-subject-is-the-victim",
			DetailPerspective.SubjectMatches(e, "我方乙", 1, true)
			&& !DetailPerspective.SubjectMatches(e, "我方甲", 1, true));

		// The same character NAME can be fielded on both sides, so the team has to be part of the match --
		// otherwise our unit's row would absorb the enemy copy's records.
		BattleEvent mirror = new BattleEvent
		{
			Type = "dmg", Attacker = "同名", AttackerTeam = 2,
			Victim = "我方乙", VictimTeam = 1, Amount = 10,
		};
		r.True("the-subject-match-separates-the-two-sides-of-a-mirror-match",
			!DetailPerspective.SubjectMatches(mirror, "同名", 1, false)
			&& DetailPerspective.SubjectMatches(mirror, "同名", 2, false));
		// Team 0 means "the game did not say", which is NOT "the other team": the record must not be
		// thrown away just because its team is unknown.
		BattleEvent teamless = new BattleEvent { Type = "dmg", Attacker = "某单位", AttackerTeam = 0, Victim = "我方乙", VictimTeam = 0, Amount = 7 };
		r.True("an-unknown-team-is-accepted-not-treated-as-the-enemy",
			DetailPerspective.SubjectMatches(teamless, "某单位", 2, false)
			&& DetailPerspective.SubjectMatches(teamless, "我方乙", 2, true));

		r.True("a-non-damage-event-is-never-a-subject",
			!DetailPerspective.SubjectMatches(new BattleEvent { Type = "heal", Attacker = "我方甲", Victim = "我方乙" }, "我方甲", 1, false)
			&& !DetailPerspective.SubjectMatches(new BattleEvent { Type = "heal", Attacker = "我方甲", Victim = "我方乙" }, "我方乙", 1, true)
			&& !DetailPerspective.SubjectMatches(null, "我方甲", 1, false));

		// ---- the counterparty (the F7 filter's subject) -------------------------------------------------
		r.True("the-counterparty-is-the-target-when-dealt-and-the-source-when-taken",
			DetailPerspective.CounterpartyKey(e, false) == "我方乙#1"
			&& DetailPerspective.CounterpartyKey(e, true) == "我方甲#1");

		// ---- an unresolvable attacker is a named bucket, not a gap --------------------------------------
		BattleEvent orphan = new BattleEvent { Type = "dmg", Attacker = "?", AttackerTeam = 0, Victim = "我方乙", VictimTeam = 1, Amount = 99 };
		r.Str("an-unresolvable-attacker-is-named-not-dropped",
			DetailPerspective.SourceName(orphan), DetailPerspective.UnknownSource);
		r.Str("an-empty-attacker-is-the-same-bucket-as-a-question-mark",
			DetailPerspective.SourceName(new BattleEvent { Attacker = "" }),
			DetailPerspective.SourceName(new BattleEvent { Attacker = "?" }));
		r.True("the-unknown-bucket-is-a-filterable-key",
			DetailPerspective.CounterpartyKey(orphan, true) == DetailPerspective.UnknownSource + "#0"
			&& DetailPerspective.CounterpartyMatches(orphan, DetailPerspective.UnknownSource + "#0", true));

		// ---- THE invariant: what the list offers, the matcher accepts -----------------------------------
		string key = DetailPerspective.CounterpartyKey(e, true);
		r.True("the-f7-list-key-is-accepted-by-the-f7-matcher",
			DetailPerspective.CounterpartyMatches(e, key, true)
			&& !DetailPerspective.CounterpartyMatches(e, "别人#1", true));
		r.True("the-same-holds-on-the-dealt-side",
			DetailPerspective.CounterpartyMatches(e, DetailPerspective.CounterpartyKey(e, false), false)
			&& !DetailPerspective.CounterpartyMatches(e, "别人#1", false));
		r.True("an-empty-filter-accepts-every-counterparty",
			DetailPerspective.CounterpartyMatches(e, "", true)
			&& DetailPerspective.CounterpartyMatches(e, null, true)
			&& DetailPerspective.CounterpartyMatches(e, "", false));

		// ---- the words: the page must say which side is on screen ---------------------------------------
		r.Str("the-page-title-names-the-perspective",
			DetailPerspective.PageTitle(false) + "/" + DetailPerspective.PageTitle(true),
			"伤害明细/承伤明细");
		r.Str("the-amount-word-names-the-perspective",
			DetailPerspective.AmountWord(false) + "/" + DetailPerspective.AmountWord(true),
			"伤害/承伤");
		r.Str("the-filter-word-names-the-counterparty",
			DetailPerspective.FilterWord(false) + "/" + DetailPerspective.FilterWord(true),
			"目标/来源");

		// ---- R86: who may appear in the unit list at all ------------------------------------------------
		// The page registers EVERY ally actor of the session before it accumulates anything, so a unit the
		// battle never touched is in the candidate list with a tally of 0. 输出明细 keeps it (a unit that
		// dealt nothing is still a real answer); 承伤明细 must drop it, or the F11/F12 rotation walks
		// through characters whose body can only print "(该角色本场没有受击事件)" -- the reported defect.
		r.True("the-taken-list-drops-units-that-never-took-damage",
			!DetailPerspective.Listable(true, 0)
			&& DetailPerspective.Listable(true, 1)
			&& DetailPerspective.Listable(true, 137));
		r.True("the-dealt-list-keeps-every-registered-unit",
			DetailPerspective.Listable(false, 0)
			&& DetailPerspective.Listable(false, 1));

		// ---- degenerate input ---------------------------------------------------------------------------
		r.Str("a-null-event-has-an-empty-key-and-a-named-source",
			DetailPerspective.CounterpartyKey(null, false) + "/" + DetailPerspective.SourceName(null),
			"/" + DetailPerspective.UnknownSource);
	}
}
