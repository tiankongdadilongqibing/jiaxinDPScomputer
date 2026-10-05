using System.Collections.Generic;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// RF5f: the contribution table's row VALUES (the RowViewModel half of plan section 10). The renderer
	/// used to walk the actors twice -- once to emit the visible rows, once to sum the footer -- which is
	/// exactly the shape in which "shown" and "summed" drift apart.
	/// Note: ContributionActorRow.Total is COMPUTED (Base + Self + Assist), so the fixtures set the parts.
	/// </summary>
	public static void ContributionRowModelCases(Runner r)
	{
		var res = new ContributionResult();
		res.Actors.Add(Actor("甲", 40.0, 30.0, 10.0, 5.0, 3.0, 7, false, 12.0));   // total 45
		res.Actors.Add(Actor("乙", 0.0, 20.0, 20.0, 1.0, 0.0, 4, true, 18.0));    // total 41, no direct damage
		res.Actors.Add(Actor("丙", 0.0, 0.0, 0.0, 0.0, 9.0, 0, false));     // total 0: sums only, not shown
		ContributionTableValues v = ContributionRowModel.Build(res, 100.0);

		r.Group("rowmodel/contribution");
		r.Eq("only-contributing-actors-are-shown", v.Rows.Count, 2);
		r.Str("the-first-shown-row-is-the-first-actor", v.Rows[0].Name, "甲");
		r.Str("the-second-is-the-other-contributor", v.Rows[1].Name, "乙");
		r.True("the-summon-marker-travels", v.Rows[1].Summon);
		r.EqD("the-total-comes-from-the-row", v.Rows[0].Total, 45.0);
		r.EqD("the-share-is-a-percent-of-the-given-total", v.Rows[0].Share, 45.0);
		r.EqD("the-direct-share-is-too", v.Rows[0].DirectShare, 40.0);
		r.EqD("base-and-self-are-one-value", v.Rows[0].BaseAndSelf, 40.0);
		r.EqD("assist-passes-through", v.Rows[0].Assist, 5.0);
		r.EqD("hits-pass-through", v.Rows[0].Hits, 7.0);
		// an actor with no direct damage is still shown when it has credit
		r.EqD("a-pure-credit-actor-has-a-zero-direct-share", v.Rows[1].DirectShare, 0.0);

		// THE pin: the sums cover every actor, including 丙 who is not shown
		r.EqD("the-base-sum-covers-the-unshown-actor", v.SumBase, 50.0);
		r.EqD("the-self-sum-covers-the-unshown-actor", v.SumSelf, 30.0);
		r.EqD("the-assist-sum-covers-the-unshown-actor", v.SumAssist, 6.0);
		r.EqD("the-received-sum-covers-the-unshown-actor", v.SumReceived, 12.0);
		// 1.7.12: the self-damage column gets the same treatment -- a per-row value and a sum over ALL
		// actors, including the one the table does not show.
		r.EqD("the-friendly-value-passes-through", v.Rows[0].Friendly, 12.0);
		r.EqD("the-friendly-sum-covers-the-unshown-actor", v.SumFriendly, 30.0);

		ContributionTableValues z = ContributionRowModel.Build(res, 0.0);
		r.EqD("a-zero-total-gives-a-zero-share", z.Rows[0].Share, 0.0);
		r.EqD("and-a-zero-direct-share", z.Rows[0].DirectShare, 0.0);
		r.Eq("the-rows-are-still-listed", z.Rows.Count, 2);
		r.Eq("an-empty-result-has-no-rows", ContributionRowModel.Build(new ContributionResult(), 1.0).Rows.Count, 0);
		r.Eq("and-a-null-result-does-not-throw", ContributionRowModel.Build(null, 1.0).Rows.Count, 0);
	}

	private static ContributionActorRow Actor(string name, double direct, double baseCredit, double self,
	                                         double assist, double received, int hits, bool summon,
	                                         double friendly = 0.0)
	{
		return new ContributionActorRow
		{
			Name = name, Direct = direct, Base = baseCredit, Self = self, Assist = assist,
			Received = received, Hits = hits, Summon = summon, Friendly = friendly,
		};
	}
}
