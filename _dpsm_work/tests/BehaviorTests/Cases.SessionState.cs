using System;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// Session state: actor-key allocation, the summon merge, ResetActors, the event cap and the
	/// team-per-second series. ResetActors is the reset path RF4 has to migrate, and "does the clock
	/// survive a reset" is exactly the kind of question a cross-session refactor gets wrong quietly.
	/// </summary>
	public static void SessionState(Runner r)
	{
		var x = new BattleObject { DebugName = "X" };
		var y = new BattleObject { DebugName = "Y" };

		r.Group("session/actor-keys");
		var s = new BattleSession();
		r.True("create-false-on-unknown-returns-null", s.GetActor(x, false) == null);
		r.Eq("create-false-adds-no-row", s.OrderedActors.Count, 0);
		var ax = s.GetActor(x, true);
		r.Eq("first-key-is-1", ax.Key, 1);
		r.Same("the-same-object-returns-the-same-row", s.GetActor(x, true), ax);
		r.Eq("second-key-is-2", s.GetActor(y, true).Key, 2);
		r.Eq("two-rows-are-ordered", s.OrderedActors.Count, 2);
		r.True("a-null-object-gets-no-row", s.GetActor(null, true) == null);
		r.Eq("first-hit-time-is-the-clock-at-creation", (long)ax.FirstHitTime, 0);

		r.Group("session/summon-merge");
		var owner = new BattleObject { DebugName = "OWNER" };
		var token = new BattleObject { TeamType = TeamType.Ally, TokenOwner = owner, DebugName = "TOK" };
		var at = s.GetActor(token, true);
		r.True("a-token-row-is-marked-summon-merge", at.IsSummonMerge);
		r.Eq("token-row-keeps-the-object-team", (int)at.Team, (int)TeamType.Ally);
		var token2 = new BattleObject { TeamType = TeamType.Ally, TokenOwner = owner, DebugName = "TOK" };
		r.Same("a-same-name-same-team-token-merges", s.GetActor(token2, true), at);
		var token3 = new BattleObject { TeamType = TeamType.Enemy, TokenOwner = owner, DebugName = "TOK" };
		r.True("a-token-on-the-other-team-does-not-merge", !ReferenceEquals(s.GetActor(token3, true), at));

		r.Group("session/reset");
		var c = new BattleSession();
		c.Advance(5.0, false);
		c.AddEvent(new BattleEvent { Type = "dmg", Amount = 5 });
		c.UnattributedDamage = 7;
		c.UnattributedHits = 3;
		c.UnattributedByVictim["v"] = 1;
		c.AddTeamTaken(2, 100);
		c.ResetActors();
		r.Eq("reset-clears-events", c.Events.Count, 0);
		r.Eq("reset-clears-the-actor-table", c.OrderedActors.Count, 0);
		r.Eq("reset-restarts-actor-keys-at-1", c.GetActor(x, true).Key, 1);
		r.Eq("reset-clears-unattributed-damage", c.UnattributedDamage, 0);
		r.Eq("reset-clears-unattributed-hits", c.UnattributedHits, 0);
		r.Eq("reset-clears-the-per-victim-map", c.UnattributedByVictim.Count, 0);
		r.Eq("reset-clears-the-team-series", c.TeamMaxSecond(), 0);
		r.True("reset-clears-the-timing-flag", !c.TimingStarted);
		r.EqD("reset-KEEPS-the-battle-clock", c.ActiveSeconds, 5.0);

		r.Group("session/event-cap");
		var list = new System.Collections.Generic.List<BattleEvent>();
		var ev = new BattleEvent { Type = "dmg", Amount = 1 };
		for (int i = 0; i < 3; i++) BattleEventList.AddNew(list, ref ev, 3);
		r.Eq("explicit-cap-is-respected", list.Count, 3);
		BattleEventList.AddNew(list, ref ev, 3);
		r.Eq("nothing-is-added-past-the-cap", list.Count, 3);
		var ev2 = new BattleEvent { Type = "heal", Amount = 2 };
		BattleEventList.AddNew(list, ref ev2, 10);
		r.Eq("a-larger-cap-accepts", list.Count, 4);

		r.Group("session/team-series");
		var t = new BattleSession();
		t.AddTeamTaken(-1, 5);
		r.Eq("a-negative-second-is-ignored", t.TeamMaxSecond(), 0);
		t.AddTeamTaken(2, 100);
		r.Eq("the-series-grows-to-the-index", t.TeamMaxSecond(), 3);
		r.Eq("the-index-holds-the-amount", t.GetTeamTaken(2), 100);
		r.Eq("gaps-default-to-zero", t.GetTeamTaken(1), 0);
		t.AddTeamTaken(2, 50);
		r.Eq("amounts-accumulate", t.GetTeamTaken(2), 150);
		t.AddTeamHeal(4, 7);
		r.Eq("the-heal-series-extends-the-maximum", t.TeamMaxSecond(), 5);
		r.Eq("the-heal-amount-is-readable", t.GetTeamHeal(4), 7);
		r.Eq("taken-out-of-range-is-zero", t.GetTeamTaken(99), 0);
		r.Eq("heal-negative-is-zero", t.GetTeamHeal(-3), 0);

		r.Group("session/from-summary");
		r.True("a-null-summary-yields-null", BattleSession.FromSummary(null) == null);
		var sum = new BattleSummary { QuestId = "411001", DurationSeconds = 119.03, Result = "Lose" };
		sum.Actors.Add(new ActorStats { Key = 1 });
		sum.Events.Add(new BattleEvent { Type = "dmg", Amount = 10 });
		var live = new BattleSession { UnattributedDamage = 273702, UnattributedHits = 71 };
		sum.Session = live;
		var v = BattleSession.FromSummary(sum);
		r.True("a-finished-view-is-not-in-battle", !v.InBattle);
		r.Eq("the-quest-id-is-parsed", v.QuestId, 411001);
		r.EqD("the-duration-becomes-the-clock", v.ActiveSeconds, 119.03);
		r.Eq("the-actors-are-carried-over", v.OrderedActors.Count, 1);
		r.Eq("the-events-are-carried-over", v.Events.Count, 1);
		r.Eq("the-unattributed-pool-is-copied", v.UnattributedDamage, 273702);
		r.Eq("the-unattributed-hits-are-copied", v.UnattributedHits, 71);
		r.Eq("the-result-is-nothing-observed", (int)v.Result, 0);
		var sum2 = new BattleSummary { QuestId = "1", DurationSeconds = 1.0 };
		var v2 = BattleSession.FromSummary(sum2);
		r.Eq("a-summary-without-a-session-does-not-invent-a-pool", v2.UnattributedDamage, 0);
		var sum3 = new BattleSummary { QuestId = "not-a-number", DurationSeconds = 1.0 };
		r.Eq("an-unparsable-quest-id-becomes-zero", BattleSession.FromSummary(sum3).QuestId, 0);
	}
}
