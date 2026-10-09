using System;

namespace BehaviorTests;

internal static class Program
{
	/// <summary>Pinned case total. Deleting a case, or dropping a whole group from Main, fails the run.
	/// R66: 1002 -> 1076 (+74: 47 in policy/skill-timeline, 27 in ui/skill-timeline-text).
	/// R67: 1076 -> 1090 (+14: burst multiplicity/cells, wider stamp cells, the skl channel).
	/// R69: 1090 -> 1140 (+50: +16 policy/skill-timeline for the cooldown fold, +14 policy/skill-activation
	/// for the Using verdict, +18 policy/skill-attempts for the attempt tallies and their rows, +2 for the
	/// whole-page width pin, and the rewritten page cases -- the deleted `rec` channel cases are inside that
	/// delta).
	/// R71: 1171 -> 1205 (+34: +31 policy/battle-clock-calibration for the origin shift -- the lag arithmetic
	/// from the charge counters, its bounds, the median, the one-origin-per-battle rule -- plus 3 for the
	/// page's origin line).
	/// R72: 1205 -> 1254 (+49: +48 for the hold that makes the shift LAND -- policy/clock-origin-hold 33,
	/// runtime/clock-origin 11, runtime/clock-origin-counter 4 -- plus 1 case for the hold-overflow refusal
	/// in policy/battle-clock-calibration, and the counters-shape pin moved from 13 to 16 members, which is
	/// a rename rather than a new case).
	/// R75: 1275 -> 1298 (+23 for policy/absorb-classify: what withheld part of a hit, the decision order that
	/// names an OBSERVED carrier, and the rule that an unobserved or unreadable one is never named).
	/// R76: 1298 -> 1307 (+9 net: the group was rewritten around the measured law
	/// `res == max(0, nominal - lifeBefore)` -- the OVERSIZED hit became its own family with its own split
	/// (`landed`/`overflow`), the life reading got the veto that removes R75's false `Masked` bucket, and the
	/// two-tier row budget that keeps the deciding rows out of `dropped` is pinned here).
	/// R78: 1307 -> 1328 (+21 for policy/absorb-wording: the two published labels renamed for what R76 measured
	/// them to be, the printed residual read from the game's own value with the `×0.242` counterfactual pinned,
	/// the non-exclusion of the split and the residual on one row, and the `[ABSPROBE] note` sentences).
	/// R81: 1460 -> 1469 (+9 in ui/taken-page: each of the six sub-tables' LABEL row carries its own section
	/// tag -- the thing the renderer turns into six different bright colours so a hundreds-of-rows page can be
	/// scanned by section -- plus the two guard cases that no bucket row is tagged and that the six sections
	/// plus "no section" are seven distinct values, a case that goes red if a tag value is copy-pasted).
	/// R82: 1469 -> 1499 (+30 in ui/hotkey-bar: every bar's text pinned byte-for-byte against the literal
	/// it replaced, the ordered list of what a click on that bar does, the rule that a page title and a
	/// separator are NOT clickable, and the width fallback the first un-measured frame uses).
	/// R83: 1499 -> 1506 (+7 in ui/taken-page: the share columns are 8 wide, so a full `100.00%` -- seven
	/// columns, and `PadL` never shrinks a full cell -- keeps a space of its own instead of being rendered
	/// against the hit count as `2100.00%`; plus the amount column's label, which may say 伤害 and not 金额).
	/// R84: 1506 -> 1526 (+20 in ui/taken-page: the page prints ONE character, picked by clicking a name in a
	/// 角色 list above the table -- the list must offer every character with its own position, mark the one on
	/// screen, wrap at five names, fall back to the first entry for an unknown key, and the whole-team total
	/// row must stay labelled as the whole team's).
	/// R85: 1526 -> 1544 (+16 in ui/detail-perspective: the F6 detail view gained the 承伤明细 perspective, so
	/// the subject/counterparty verdicts, the named 未知来源 bucket, the key/matcher agreement of the F7 filter
	/// and the three words that name the side on screen are executed rather than eyeballed; +2 in ui/hotkey-bar
	/// for the F2 entry and its label in both directions).
	/// R86: 1544 -> 1546 (+2 in ui/detail-perspective: 承伤明细 lists only the units that actually took damage.
	/// The page registers every ally actor of the session before it accumulates anything, so a unit the battle
	/// never touched used to enter the F11/F12 rotation as a zero row whose body could only print
	/// "(该角色本场没有受击事件)"; the list verdict is now pure and pinned in both directions -- dropped on the
	/// taken side, kept on the dealt side, where a unit that dealt nothing IS a real answer).
	/// R87: 1546 -> 1576 (+30 in atkadd/self-declared: 方案A, the census of the addends the fold classified as
	/// the holder's own. The two mapping tables it rests on cannot be read off the code -- they were MEASURED
	/// against abilities whose printed name states the same fact -- and the team scope is the whole reason the
	/// match is decidable in a mirror battle. Both are executed here, together with the two identities the
	/// guard asserts and the writer's bytes, which R80 taught us to parse back rather than trust).
	/// </summary>
	public const int ExpectedCases = 1576;

	private static int Main(string[] args)
	{
		bool quiet = false;
		foreach (string a in args) if (a == "--quiet") quiet = true;

		var r = new Runner();
		r.Quiet = quiet;
		Console.WriteLine("BehaviorTests -- game-free execution of the real production sources");

		Cases.Clock(r);
		Cases.HitWindow(r);
		Cases.SessionState(r);
		Cases.Series(r);
		Cases.Cache(r);
		Cases.Tiered(r);
		// RF3: the extracted pure policies, plus grid sweeps against oracles transcribed from the
		// pre-extraction source (git 8f3aafd).
		Cases.Policy(r);
		// RF4: the cross-session state family (Start/End/resume/F9, consecutive battles).
		Cases.Runtime(r);
		// RF4 family 2: the battle-wide rule classification (the decision half of the registration path).
		Cases.GlobalRule(r);
		// RF4c: the battle-wide rule registry container (ownership + the register-before-finalise timing).
		Cases.Registry(r);
		// RF5: the cache decisions (staleness, source selection, reason texts).
		Cases.CachePolicy(r);
		// RF4 apply side: copies and the per-status factor.
		Cases.GlobalRuleApply(r);
		// RF4 third family: the per-battle counters and their reset transitions.
		Cases.Counters(r);
		// RF5b: the display formatter (widths, padding, numbers, fitting), executed as production code.
		Cases.DisplayFormatCases(r);
		// RF5c: the column definition of the three overlay tables.
		Cases.Columns(r);
		// RF5d: the data-row builders, constructed from the column definition.
		Cases.ColumnRows(r);
		// RF5e: the IMGUI fallback's contribution lines (same number formats as the panel).
		Cases.FallbackTextCases(r);
		// RF6a: the master-data official-name rule (the measured 刻印 collision cases).
		Cases.MasterDataLabel(r);
		// RF3c: the composition chain's tolerances and the crit observation window.
		Cases.CompositionTolerance(r);
		// RF4f: the per-battle activity ring (order, cap, clearing).
		Cases.CalcActivityLog(r);
		// RF5f: the contribution table's row values (shown rows vs summed rows).
		Cases.ContributionRowModelCases(r);
		Cases.ContributionRowModelTables(r);
		// RF4g: the attack snapshot (both halves required).
		Cases.AttackSnapshotCases(r);
		// RF4h: the process-level history ring.
		Cases.BattleHistoryRingCases(r);
		// RF5h: the no-separator whole-number formatter.
		Cases.DisplayFormatWholeCases(r);
		// Round 41: the cache decisions (1s throttle, F9 generation).
		Cases.CacheGenerationCases(r);
		// R52: the evidence-extraction flow (trigger key, bundle name, retention) and the unresolved-fold
		// census the bundle carries.
		Cases.ExtractionCases(r);
		// R56: the battle reference -- format, lifecycle, launch namespace, collision handling.
		Cases.BattleRefCases(r);
		// R63's `Cases.SkillCooldown` and the policy it executed were DELETED in R65: the premise ("the
		// auto-skill master stores seconds") was falsified by a live battle, so the group that pinned the
		// x30 conversion is gone with it. 1016 -> 1002 cases (16 removed, 2 added in the cadence group).
		// R64/R65: the auto-skill cadence arithmetic (charge rate, charge in seconds, interval, median).
		Cases.AutoSkillCadence(r);
		// R66: the 技能时间表 -- one row per (unit, skill) out of a raw activation stream (merge window,
		// battle-clock axis, order, burst/overflow marks) plus the page text built from it.
		Cases.SkillTimeline(r);
		// R72: the hold that makes R71's origin shift land (an event that arrives before the evidence is
		// replayed on the corrected axis instead of blocking the shift).
		Cases.ClockOriginHoldCases(r);
		// R74: the calibration sampler's own evidence (why "no samples" happened, one bucket per value) --
		// the line that replaces the structurally-useless `samples=0` R71/R72 shipped, plus the unit choice
		// for the first charge (`m_firstCoolTimeFrame` when it answers, the seconds property x units as the
		// recorded fallback).
		Cases.ClockLagDiagnosticsCases(r);
		// R75: which mechanism withheld part of a damage-application call (a barrier, a flat cut, a takeover,
		// or an honest "we did not see it"), decided from readings instead of from the difference -- plus the
		// `[ABSPROBE]` line that has to stay readable in every battle.
		Cases.AbsorbClassifyCases(r);
		// R78: the WORDS the split is published in (the two labels renamed for what R76 measured them to be,
		// the printed residual read from the game's own value instead of from the overflow, and the
		// `[ABSPROBE] note` sentences that say what a deciding verdict means).
		Cases.AbsorbWordingCases(r);
		// R79 受击来源拆分: the per-unit INCOMING damage projection (five dimensions partitioning one nominal,
		// the two amounts that belong to no attacker bucket, the fold that keeps the sum, the position
		// snapshot taken on a unit's first damage).
		Cases.TakenBreakdownCases(r);
		// R79: that projection's page -- the column geometry every padded row assumes, and the words (the
		// residual is never called an absorption; a best-effort bucket carries its `*`).
		Cases.TakenPageCases(r);
		// R80: the BYTES of the exported section. R79's writer emitted unquoted string values and no
		// compiled test could see the file it produced; this one parses it back.
		Cases.TakenExportCases(r);
		// R82: the clickable hotkey bars. The user's F3 collides with another program, so the printed
		// entries gained a mouse route -- with every key left exactly as it was, and with the bar text
		// still byte-for-byte the literal it replaced (the F6 view's hint tail is the one, deliberate,
		// exception: it moved from the pinned bar, which cannot carry click targets, into its own row).
		Cases.HotkeyBarCases(r);
		// R85: the F6 detail view's second perspective (承伤明细). The page reuses every number the events
		// already carry -- only WHICH END of an event is read changes -- so the verdicts and the words that
		// publish them are pure and live in Ui/DetailPerspective.cs, where the suite can hold them.
		Cases.DetailPerspectiveCases(r);
		Cases.AtkAddDeclareCases(r);

		int fail = r.Failed;
		if (r.Cases != ExpectedCases)
		{
			Console.WriteLine("FAIL cases/pinned-total: got=" + r.Cases + " want=" + ExpectedCases);
			fail++;
		}
		Console.WriteLine("behavior tests: cases=" + r.Cases + " failed=" + fail + " pinned=" + ExpectedCases);
		Console.WriteLine(fail == 0 ? "== ALL PASS ==" : ("== " + fail + " FAILED =="));
		return fail == 0 ? 0 : 1;
	}
}
