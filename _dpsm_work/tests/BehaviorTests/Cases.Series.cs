using System;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// ActorStats number series: the per-second buckets, the HP percent series, the DPS denominator and
	/// the damage-time curve. The curve has two non-obvious rules -- samples closer than 0.04 s MERGE,
	/// and at 6000 samples the list COMPACTS by keeping every second entry -- and neither is visible in
	/// any export total, so only an executed case can hold them still.
	/// </summary>
	public static void Series(Runner r)
	{
		r.Group("series/dps");
		r.Eq("max-damage-samples-is-6000", ActorStats.MaxDamageSamples, 6000);
		var st = new ActorStats();
		r.EqD("dps-at-exactly-half-a-second-is-zero", st.Dps(0.5), 0.0);
		st.DamageDealt = 1000;
		r.EqD("dps-at-two-seconds", st.Dps(2.0), 500.0);
		r.EqD("dps-just-over-half-a-second", st.Dps(0.5000001), 1000.0 / 0.5000001);

		r.Group("series/per-second-buckets");
		var b = new ActorStats();
		b.AddSecondDamage(-1, 5);
		r.Eq("a-negative-second-is-ignored", b.SecondCount, 0);
		b.AddSecondDamage(1, 30);
		r.Eq("the-bucket-holds-the-amount", b.GetSecondDamage(1), 30);
		r.Eq("gaps-are-zero", b.GetSecondDamage(0), 0);
		r.Eq("the-bucket-count-is-the-index-plus-one", b.SecondCount, 2);
		b.AddSecondDamage(1, 12);
		r.Eq("amounts-accumulate", b.GetSecondDamage(1), 42);
		b.AddSecondTaken(3, 9);
		b.AddSecondHeal(5, 4);
		r.Eq("max-second-is-the-maximum-of-the-three", b.MaxSecond(), 6);
		r.Eq("out-of-range-taken-is-zero", b.GetSecondTaken(99), 0);
		r.Eq("negative-heal-index-is-zero", b.GetSecondHeal(-1), 0);

		r.Group("series/hp-percent");
		var hp = new ActorStats();
		r.EqD("an-unset-second-defaults-to-100", hp.GetHpPct(0), 100.0);
		hp.AddHpPct(2, 42.5f);
		r.EqD("the-filled-gap-defaults-to-100", hp.GetHpPct(0), 100.0);
		r.EqD("the-set-second-is-read-back", hp.GetHpPct(2), 42.5);
		hp.AddHpPct(-1, 1f);
		r.EqD("a-negative-second-is-ignored", hp.GetHpPct(0), 100.0);
		r.EqD("an-out-of-range-second-is-100", hp.GetHpPct(99), 100.0);

		r.Group("series/damage-curve");
		var m = new ActorStats();
		m.AddSample(0.0, 100);
		m.AddSample(0.02, 150);
		r.Eq("a-sample-closer-than-0.04s-merges", m.DamageHistory.Count, 1);
		r.Eq("merging-overwrites-the-last-value", m.DamageHistory[0].D, 150);
		m.AddSample(0.04, 200);
		r.Eq("a-sample-at-exactly-0.04s-is-appended", m.DamageHistory.Count, 2);
		r.Eq("the-appended-value-is-the-new-one", m.DamageHistory[1].D, 200);

		var full = new ActorStats();
		for (int i = 0; i < 6000; i++) full.AddSample(i * 0.05, i);
		r.Eq("the-curve-fills-to-the-cap", full.DamageHistory.Count, 6000);
		r.EqD("the-last-sample-before-compaction", full.DamageHistory[5999].T, 5999 * 0.05);
		full.AddSample(6000 * 0.05, 99999);
		r.Eq("compaction-halves-and-then-appends", full.DamageHistory.Count, 3001);
		r.Eq("compaction-keeps-entry-0", full.DamageHistory[0].D, 0);
		r.Eq("compaction-keeps-every-second-entry", full.DamageHistory[5].D, 10);
		r.Eq("compaction-keeps-the-last-even-entry", full.DamageHistory[2999].D, 5998);
		r.Eq("the-appended-sample-is-the-new-one", full.DamageHistory[3000].D, 99999);

		var merging = new ActorStats();
		for (int i = 0; i < 6000; i++) merging.AddSample(i * 0.05, i);
		merging.AddSample(5999 * 0.05 + 0.01, 777);
		r.Eq("a-merge-wins-over-compaction", merging.DamageHistory.Count, 6000);
		r.Eq("the-merged-value-is-the-new-one", merging.DamageHistory[5999].D, 777);
	}
}
