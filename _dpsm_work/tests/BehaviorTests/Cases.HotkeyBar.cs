using System;
using System.Collections.Generic;
using System.Text;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// R82: the hotkey bars became CLICKABLE. The user's reason was practical (F3 collides with another
	/// program on their machine) and the chosen scope was deliberately narrow: keep every key exactly as
	/// it was, add a mouse route. Two things follow, and both are checked here rather than by eye.
	///
	/// First the WORDS: every bar's text must be byte-for-byte the literal it replaced, because "make the
	/// printed entries clickable" is not "restyle the panel". The F6 view is the one exception, and a
	/// deliberate one -- its key hints used to be the tail of the pinned bar, which is a single Text
	/// outside the scrolled content and therefore cannot carry click targets; the tail moved to its own
	/// row, and case `the-detail-key-row-keeps-the-pinned-bars-old-hint` pins that the words survived.
	///
	/// Second the MAPPING: <see cref="HotkeyAction"/> is named after KEYS, not after the action a page
	/// happens to give that key, so a click dispatches the very method the key dispatches and the two can
	/// never drift apart. `the-return-and-the-open-entry-are-the-same-action` states that as one
	/// assertion: the contribution page's "F5返回" and the roster's "F5 贡献" are the same behaviour.
	/// </summary>
	internal static void HotkeyBarCases(Runner r)
	{
		r.Group("ui/hotkey-bar");

		// ---- the text is the text that was already on screen -------------------------------------------
		r.Str("the-idle-roster-bar-keeps-its-exact-text",
			HotkeyBarText.Line(HotkeyBarText.RosterIdle()),
			"未在战斗中   F8 显隐  F9 重置  F10 图表  F6 明细  F5 贡献  F3 受击来源  F4 时间表");
		r.Str("the-in-battle-roster-bar-keeps-its-exact-text",
			HotkeyBarText.Line(HotkeyBarText.RosterInBattle("9999", "45秒")),
			"任务 9999   时间 45秒   F8显隐 F9重置 F10图表 F6明细 F5贡献 F3受击来源 F4时间表");
		r.Str("the-taken-bar-keeps-its-exact-text",
			HotkeyBarText.Line(HotkeyBarText.Taken("9999", "45秒", false)),
			"受击来源拆分  F3返回  Shift+F3 只看前衛  Home/End 首尾  任务 9999   45秒");
		r.Str("the-taken-bar-states-the-filter-when-it-is-on",
			HotkeyBarText.Line(HotkeyBarText.Taken("9999", "45秒", true)),
			"受击来源拆分  F3返回  Shift+F3 只看前衛(开)  Home/End 首尾  任务 9999   45秒");
		r.Str("the-contribution-bar-keeps-its-exact-text",
			HotkeyBarText.Line(HotkeyBarText.Contribution("9999", "45秒")),
			"总贡献  F5返回  任务 9999   45秒");
		r.Str("the-chart-bar-keeps-its-exact-text",
			HotkeyBarText.Line(HotkeyBarText.Chart("累计")),
			"累计  上:我方伤害 中:耐久% 下:敌方  F10列表 F12累计/每秒");
		r.Str("the-timeline-header-keeps-its-exact-text",
			HotkeyBarText.Line(HotkeyBarText.Timeline()),
			"技能时间表  我方奥义/特殊/自动技能发动时刻   F4 返回");
		r.Str("the-detail-key-row-keeps-the-pinned-bars-old-hint",
			HotkeyBarText.Line(HotkeyBarText.Detail(false)),
			"←/→ 翻页(20秒/页)  F7 筛选目标  F2 看承伤  F11/F12 换角色  F6返回");
		// R85: the label names the side the key will SHOW, not the side on screen, so the taken variant
		// reads "F2 看输出" and its F7 label says 来源 (the counterparty there is who DID the hitting).
		r.Str("the-taken-detail-key-row-names-the-other-side",
			HotkeyBarText.Line(HotkeyBarText.Detail(true)),
			"←/→ 翻页(20秒/页)  F7 筛选来源  F2 看输出  F11/F12 换角色  F6返回");

		// ---- what a click on each bar offers -----------------------------------------------------------
		r.Str("the-idle-roster-bar-offers-the-seven-keys",
			HbActions(HotkeyBarText.RosterIdle()),
			"KeyF8,KeyF9,KeyF10,KeyF6,KeyF5,KeyF3,KeyF4");
		r.Str("the-in-battle-bar-offers-the-same-entries",
			HbActions(HotkeyBarText.RosterInBattle("9999", "45秒")),
			HbActions(HotkeyBarText.RosterIdle()));
		r.Str("the-taken-bar-offers-return-filter-and-ends",
			HbActions(HotkeyBarText.Taken("9999", "45秒", false)),
			"KeyF3,KeyF3Shift,KeyHome,KeyEnd");
		r.Str("the-contribution-bar-offers-only-return",
			HbActions(HotkeyBarText.Contribution("9999", "45秒")),
			"KeyF5");
		r.Str("the-chart-bar-offers-list-and-mode",
			HbActions(HotkeyBarText.Chart("累计")),
			"KeyF10,KeyF12");
		r.Str("the-timeline-offers-only-return",
			HbActions(HotkeyBarText.Timeline()),
			"KeyF4");
		r.Str("the-detail-row-offers-paging-filter-perspective-and-character",
			HbActions(HotkeyBarText.Detail(false)),
			"KeyLeft,KeyRight,KeyF7,KeyF2,KeyF11,KeyF12,KeyF6");
		r.Str("the-taken-detail-row-offers-the-same-entries",
			HbActions(HotkeyBarText.Detail(true)),
			HbActions(HotkeyBarText.Detail(false)));

		// The mapping is by KEY, so an entry that only exists to leave a page shares its action with the
		// entry that opened it. If someone ever "helpfully" splits those into two actions, this goes red.
		r.True("the-return-and-the-open-entry-are-the-same-action",
			HbHas(HotkeyBarText.Contribution("9999", "45秒"), HotkeyAction.KeyF5)
			&& HbHas(HotkeyBarText.RosterIdle(), HotkeyAction.KeyF5));
		r.True("the-shift-entries-are-distinct-from-their-plain-keys",
			HotkeyAction.KeyF3Shift != HotkeyAction.KeyF3 && HotkeyAction.KeyF7Shift != HotkeyAction.KeyF7);
		r.True("the-taken-bar-filters-on-shift-and-returns-on-plain-f3",
			HbHas(HotkeyBarText.Taken("1", "1秒", false), HotkeyAction.KeyF3)
			&& HbHas(HotkeyBarText.Taken("1", "1秒", false), HotkeyAction.KeyF3Shift));

		// ---- the prefixes are what a click must NOT swallow ---------------------------------------------
		r.Str("the-page-title-is-not-clickable",
			HbTags(HotkeyBarText.RosterIdle(), 0), "None");
		r.Str("the-quest-and-time-prefix-is-not-clickable",
			HbTags(HotkeyBarText.RosterInBattle("9999", "45秒"), 0), "None");
		r.True("the-separators-between-two-entries-are-not-clickable",
			HbTags(HotkeyBarText.Taken("1", "1秒", false), 4) == "None"
			&& HbTags(HotkeyBarText.Detail(false), 1) == "None"
			&& HbTags(HotkeyBarText.Detail(false), 6) == "None");
		r.True("every-clickable-entry-has-a-label",
			HbAllLabelled(HotkeyBarText.RosterIdle())
			&& HbAllLabelled(HotkeyBarText.RosterInBattle("9999", "45秒"))
			&& HbAllLabelled(HotkeyBarText.Taken("9999", "45秒", true))
			&& HbAllLabelled(HotkeyBarText.Contribution("9999", "45秒"))
			&& HbAllLabelled(HotkeyBarText.Chart("累计"))
			&& HbAllLabelled(HotkeyBarText.Timeline())
			&& HbAllLabelled(HotkeyBarText.Detail(false))
			&& HbAllLabelled(HotkeyBarText.Detail(true)));
		r.True("the-plain-action-is-never-clickable",
			!new HotkeySeg("任意", HotkeyAction.None).Clickable
			&& new HotkeySeg("任意", HotkeyAction.KeyF3).Clickable
			&& new HotkeySeg(null, HotkeyAction.None).Text == "");

		// ---- one bar is one line: the layout relies on it ----------------------------------------------
		r.True("every-bar-is-a-single-line",
			!HbHasNewline(HotkeyBarText.RosterIdle())
			&& !HbHasNewline(HotkeyBarText.RosterInBattle("9999", "45秒"))
			&& !HbHasNewline(HotkeyBarText.Taken("9999", "45秒", false))
			&& !HbHasNewline(HotkeyBarText.Contribution("9999", "45秒"))
			&& !HbHasNewline(HotkeyBarText.Chart("累计"))
			&& !HbHasNewline(HotkeyBarText.Timeline())
			&& !HbHasNewline(HotkeyBarText.Detail(false))
			&& !HbHasNewline(HotkeyBarText.Detail(true)));

		// ---- the width fallback (only used until the font can measure) ----------------------------------
		r.True("the-width-estimate-is-zero-for-empty-text",
			HotkeyBarText.EstimateWidth("", 14) == 0f && HotkeyBarText.EstimateWidth(null, 14) == 0f);
		r.True("the-width-estimate-counts-a-cjk-glyph-wider-than-ascii",
			HotkeyBarText.EstimateWidth("前", 14) > HotkeyBarText.EstimateWidth("A", 14));
		r.True("the-width-estimate-grows-with-every-added-glyph",
			HotkeyBarText.EstimateWidth("F8", 14) < HotkeyBarText.EstimateWidth("F8 显隐", 14)
			&& HotkeyBarText.EstimateWidth("F8 显隐", 14) < HotkeyBarText.EstimateWidth("F8 显隐  F9", 14));
		r.True("the-width-estimate-grows-with-the-font-size",
			HotkeyBarText.EstimateWidth("F8 显隐", 14) < HotkeyBarText.EstimateWidth("F8 显隐", 20));
		r.True("the-layout-gap-is-a-safe-positive",
			HotkeyBarText.Gap >= 4f && HotkeyBarText.Pad >= 0f);
		r.True("a-null-segment-list-is-an-empty-bar",
			HotkeyBarText.Line(null) == "" && HotkeyBarText.Actions(null).Count == 0);
	}

	private static string HbActions(List<HotkeySeg> segs)
	{
		List<HotkeyAction> list = HotkeyBarText.Actions(segs);
		StringBuilder sb = new StringBuilder();
		for (int i = 0; i < list.Count; i++)
		{
			if (i > 0) sb.Append(',');
			sb.Append(list[i].ToString());
		}
		return sb.ToString();
	}

	private static string HbTags(List<HotkeySeg> segs, int index)
	{
		if (segs == null || index < 0 || index >= segs.Count) return "<missing>";
		return segs[index].Action.ToString();
	}

	private static bool HbHas(List<HotkeySeg> segs, HotkeyAction action)
	{
		List<HotkeyAction> list = HotkeyBarText.Actions(segs);
		for (int i = 0; i < list.Count; i++) if (list[i] == action) return true;
		return false;
	}

	private static bool HbAllLabelled(List<HotkeySeg> segs)
	{
		if (segs == null) return false;
		for (int i = 0; i < segs.Count; i++)
		{
			if (!segs[i].Clickable) continue;
			if (string.IsNullOrEmpty(segs[i].Text) || segs[i].Text.Trim().Length == 0) return false;
		}
		return true;
	}

	private static bool HbHasNewline(List<HotkeySeg> segs)
	{
		string line = HotkeyBarText.Line(segs);
		return line.IndexOf('\n') >= 0 || line.IndexOf('\r') >= 0;
	}
}
