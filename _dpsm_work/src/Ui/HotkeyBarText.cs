using System;
using System.Collections.Generic;
using System.Text;

namespace DpsMeter;

/// <summary>
/// R82: what one clickable entry of a hotkey bar does. The members are named after the KEY, not after
/// the action, and that is deliberate: the click dispatches the very method the key dispatches
/// (see OverlayUGUI.DispatchHotkey), so a click can never drift away from the keyboard behaviour.
/// F3Shift / F7Shift carry the Shift modifier of F3 (前衛 filter) and F7 (previous target).
/// </summary>
internal enum HotkeyAction
{
	/// <summary>Not clickable: the prefix of a bar (page title, quest id, battle time, mode tag).</summary>
	None = 0,
	KeyF3 = 1,
	KeyF3Shift = 2,
	KeyF4 = 3,
	KeyF5 = 4,
	KeyF6 = 5,
	KeyF7 = 6,
	KeyF7Shift = 7,
	KeyF8 = 8,
	KeyF9 = 9,
	KeyF10 = 10,
	KeyF11 = 11,
	KeyF12 = 12,
	KeyHome = 13,
	KeyEnd = 14,
	KeyLeft = 15,
	KeyRight = 16,
}

/// <summary>
/// One segment of a hotkey bar: a piece of text plus what a click on it does. The Text carries its own
/// spacing (the separators belong to the entry before them), so <see cref="HotkeyBarText.Line"/>
/// concatenation reproduces the bar's original single-string text EXACTLY -- that is what the behavior
/// tests pin, because the point of R82 is to make those already-printed entries clickable, not to
/// restyle them.
/// </summary>
internal readonly struct HotkeySeg
{
	public readonly string Text;
	public readonly HotkeyAction Action;

	public HotkeySeg(string text, HotkeyAction action)
	{
		Text = text ?? "";
		Action = action;
	}

	public bool Clickable { get { return Action != HotkeyAction.None; } }
}

/// <summary>
/// R82: the six hotkey bars of the uGUI overlay (roster idle / roster in battle / 受击来源拆分 /
/// 总贡献 / 图表 / 技能时间表, plus the F6 detail view's key row) as ordered segments. Pure: no Unity
/// type is touched here, so the behavior suite executes this file directly and can state, in one
/// assertion per bar, both the exact on-screen text and which entry does what.
///
/// The bar text of every view EXCEPT the F6 detail view is byte-for-byte the literal it replaced. The
/// F6 key row is new: its text used to be the tail of the pinned bar, which is a single Text and
/// therefore cannot carry click targets (R82 moved it into a normal row so the detail view has a mouse
/// route back to the roster as well).
/// </summary>
internal static class HotkeyBarText
{
	/// <summary>Horizontal gap kept between two segments when the overlay lays a bar out. The separators
	/// inside the segment texts are already spaces; this is the minimum, so two entries can never touch
	/// even if the font measurement trims trailing blanks.</summary>
	public const float Gap = 8f;

	/// <summary>Extra width granted to a click target beyond its measured text width (hit area only).</summary>
	public const float Pad = 4f;

	private static HotkeySeg Plain(string text) { return new HotkeySeg(text, HotkeyAction.None); }
	private static HotkeySeg Key(string text, HotkeyAction action) { return new HotkeySeg(text, action); }

	/// <summary>The roster bar while nothing is being fought.</summary>
	public static List<HotkeySeg> RosterIdle()
	{
		return new List<HotkeySeg>
		{
			Plain("未在战斗中   "),
			Key("F8 显隐  ", HotkeyAction.KeyF8),
			Key("F9 重置  ", HotkeyAction.KeyF9),
			Key("F10 图表  ", HotkeyAction.KeyF10),
			Key("F6 明细  ", HotkeyAction.KeyF6),
			Key("F5 贡献  ", HotkeyAction.KeyF5),
			Key("F3 受击来源  ", HotkeyAction.KeyF3),
			Key("F4 时间表", HotkeyAction.KeyF4),
		};
	}

	/// <summary>The roster bar mid-battle: quest id and battle time first, then the same seven entries in
	/// their compact spelling (this bar sits on a 560 px panel).</summary>
	public static List<HotkeySeg> RosterInBattle(string quest, string seconds)
	{
		return new List<HotkeySeg>
		{
			Plain("任务 " + (quest ?? "") + "   时间 " + (seconds ?? "") + "   "),
			Key("F8显隐 ", HotkeyAction.KeyF8),
			Key("F9重置 ", HotkeyAction.KeyF9),
			Key("F10图表 ", HotkeyAction.KeyF10),
			Key("F6明细 ", HotkeyAction.KeyF6),
			Key("F5贡献 ", HotkeyAction.KeyF5),
			Key("F3受击来源 ", HotkeyAction.KeyF3),
			Key("F4时间表", HotkeyAction.KeyF4),
		};
	}

	/// <summary>The 受击来源拆分 page's bar. F3 returns to the view the page was opened from; Shift+F3
	/// narrows the table to 前衛 (the label states the current state); Home/End jump to the page ends.</summary>
	public static List<HotkeySeg> Taken(string quest, string seconds, bool vanguardOnly)
	{
		return new List<HotkeySeg>
		{
			Plain("受击来源拆分  "),
			Key("F3返回  ", HotkeyAction.KeyF3),
			Key("Shift+F3 " + (vanguardOnly ? "只看前衛(开)" : "只看前衛") + "  ", HotkeyAction.KeyF3Shift),
			Key("Home", HotkeyAction.KeyHome),
			Plain("/"),
			Key("End 首尾", HotkeyAction.KeyEnd),
			Plain("  任务 " + (quest ?? "") + "   " + (seconds ?? "")),
		};
	}

	/// <summary>The 总贡献 page's bar.</summary>
	public static List<HotkeySeg> Contribution(string quest, string seconds)
	{
		return new List<HotkeySeg>
		{
			Plain("总贡献  "),
			Key("F5返回", HotkeyAction.KeyF5),
			Plain("  任务 " + (quest ?? "") + "   " + (seconds ?? "")),
		};
	}

	/// <summary>The chart page's bar. <paramref name="modeTag"/> is 累计 / 每秒DPS.</summary>
	public static List<HotkeySeg> Chart(string modeTag)
	{
		return new List<HotkeySeg>
		{
			Plain((modeTag ?? "") + "  上:我方伤害 中:耐久% 下:敌方  "),
			Key("F10列表 ", HotkeyAction.KeyF10),
			Key("F12累计/每秒", HotkeyAction.KeyF12),
		};
	}

	/// <summary>The 技能时间表 page's header: only F4 (return) is clickable there.</summary>
	public static List<HotkeySeg> Timeline()
	{
		return new List<HotkeySeg>
		{
			Plain("技能时间表  我方奥义/特殊/自动技能发动时刻   "),
			Key("F4 返回", HotkeyAction.KeyF4),
		};
	}

	/// <summary>The F6 detail view's key row (new in R82; the text is the pinned bar's old hint tail).
	/// ← / → page the per-hit list, F7 cycles the target filter, F11/F12 switch character, F6 returns.</summary>
	public static List<HotkeySeg> Detail()
	{
		return new List<HotkeySeg>
		{
			Key("←", HotkeyAction.KeyLeft),
			Plain("/"),
			Key("→ 翻页(20秒/页)  ", HotkeyAction.KeyRight),
			Key("F7 筛选目标  ", HotkeyAction.KeyF7),
			Key("F11", HotkeyAction.KeyF11),
			Plain("/"),
			Key("F12 换角色  ", HotkeyAction.KeyF12),
			Key("F6返回", HotkeyAction.KeyF6),
		};
	}

	/// <summary>The bar as one line -- exactly the string the bar printed before R82 (the tests compare
	/// this against the literals it replaced).</summary>
	public static string Line(List<HotkeySeg> segs)
	{
		if (segs == null) return "";
		StringBuilder sb = new StringBuilder();
		for (int i = 0; i < segs.Count; i++) sb.Append(segs[i].Text);
		return sb.ToString();
	}

	/// <summary>The clickable entries of a bar, in display order (used by the tests to state "what does
	/// clicking this bar offer" as one list).</summary>
	public static List<HotkeyAction> Actions(List<HotkeySeg> segs)
	{
		List<HotkeyAction> list = new List<HotkeyAction>();
		if (segs == null) return list;
		for (int i = 0; i < segs.Count; i++) if (segs[i].Clickable) list.Add(segs[i].Action);
		return list;
	}

	/// <summary>
	/// Width fallback for the case where the Unity font cannot measure yet (font not resolved, or the
	/// Text component has not been laid out): ASCII ≈ 0.56 em, everything else (CJK, ←/→) ≈ 1.0 em.
	/// The live path uses Text.preferredWidth; this only has to keep the entries from overlapping during
	/// the first frame.
	/// </summary>
	public static float EstimateWidth(string text, int fontSize)
	{
		if (string.IsNullOrEmpty(text)) return 0f;
		float ascii = 0f, wide = 0f;
		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] < 0x80) ascii += 1f; else wide += 1f;
		}
		return ascii * 0.56f * fontSize + wide * 1.0f * fontSize;
	}
}
