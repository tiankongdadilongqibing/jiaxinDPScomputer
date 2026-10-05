using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace DpsMeter;

/// <summary>
/// Row production: turns a BattleSession / BattleSummary into the list of rows the panel draws.
///
/// This is the file to extend for a new row or a new column: add a RowDef in the right producer and
/// the pool/layout in OverlayUGUI.Pool.cs will place it.
/// </summary>
public static partial class OverlayUGUI
{
	// ------------------------------------------------------------------ refresh

	private class RowDef
	{
		public string Text;
		public Color Color;
		public float Height;
		/// <summary>0 = text row, 1 = party chart, 2 = enemy chart.</summary>
		public short ChartSlot;
		/// <summary>1.7.6: null = the default UI font. Set to the monospaced font by the 总贡献 table,
		/// whose column padding is only exact on a 1:2 grid (see OverlayUGUI.Pool.GetMonoFont).</summary>
		public Font Font;
		/// <summary>R56: this row is the battle-reference line; a click on it copies <see cref="CopyText"/>.
		/// The payload is built by the producer, which is the only place that knows WHICH battle this page
		/// is describing.</summary>
		public bool Copyable;
		public string CopyText;
	}

	private static void Refresh()
	{
		List<RowDef> rows = BuildRows();

		// count how many are real text rows (charts don't consume a text object)
		int textCount = 0;
		foreach (var r in rows) if (r.ChartSlot == 0) textCount++;
		EnsurePool(textCount);

		// (re)assign font to rows whenever it (re)becomes available
		Font f = GetFont();
		if (!GameRef.IsNull(f) && !GameRef.Same(f, _assignedFont))
		{
			_assignedFont = f;
			foreach (var t in _rowTexts)
			{
				t.font = f;
				try { t.material = f.material; } catch { }
			}
		}

		// deterministic rebuild: nothing is kept "active" from a previous frame
		foreach (var t in _rowTexts) if (t.gameObject.activeSelf) t.gameObject.SetActive(false);
		if (_chartImage.gameObject.activeSelf) _chartImage.gameObject.SetActive(false);
		if (_chartImageB.gameObject.activeSelf) _chartImageB.gameObject.SetActive(false);
		if (_chartImageC.gameObject.activeSelf) _chartImageC.gameObject.SetActive(false);

		bool chartA = false, chartB = false, chartC = false;
		int ti = 0;
		foreach (var r in rows)
		{
			if (r.ChartSlot == 1) { chartA = true; continue; }
			if (r.ChartSlot == 2) { chartB = true; continue; }
			if (r.ChartSlot == 3) { chartC = true; continue; }
			if (ti < _rowTexts.Count)
			{
				Text t = _rowTexts[ti];
				t.gameObject.SetActive(true);
				t.text = r.Text;
				t.color = r.Color;
				// 1.7.6: the row decides its own font. Without the else branch a row reused from the
				// contribution page would keep the monospaced font on the roster page.
				Font want = GameRef.IsNull(r.Font) ? GetFont() : r.Font;
				if (!GameRef.IsNull(want) && !GameRef.Same(t.font, want))
				{
					t.font = want;
					try { t.material = want.material; } catch { }
				}
				ti++;
			}
		}

		if (chartA) { _chartImage.gameObject.SetActive(true); _chartImage.texture = OverlayChart.PartyTexture; }
		if (chartB) { _chartImageB.gameObject.SetActive(true); _chartImageB.texture = OverlayChart.TakenTexture; }
		if (chartC) { _chartImageC.gameObject.SetActive(true); _chartImageC.texture = OverlayChart.EnemyTexture; }

		LayoutCharts(rows, textCount);
	}

	private static void LayoutCharts(List<RowDef> rows, int textCount)
	{
		float pad = 8f;
		_panelW = View == ViewMode.Detail
			? Mathf.Min((float)Screen.width - 40f, 1400f)   // detail lines are long: widen the panel
			: (View == ViewMode.Contribution
				? Mathf.Min((float)Screen.width - 40f, 780f)   // the contribution table needs its columns
				: Mathf.Min((float)Screen.width - 20f, 560f));

		// R56: the copy target is rebuilt with the layout, so switching pages or battles cannot leave a
		// click pointing at the previous page's id.
		_copyRefRt = null;
		_copyRefPayload = null;
		// R56: the copy target is rebuilt with the layout, so switching pages or battles cannot leave a
		// click pointing at the previous page's id.
		_copyRefRt = null;
		_copyRefPayload = null;
		float totalContent = 0f;
		for (int i = 0; i < rows.Count; i++) totalContent += rows[i].Height;
		_contentH = totalContent;

		// The pinned bar takes its height from the top of the panel, so the scrollable area shrinks by
		// the same amount and the row geometry stays untouched.
		float pinH = (View == ViewMode.Detail && !string.IsNullOrEmpty(_pinLine)) ? PinBarH : 0f;

		float maxPanelH = (float)Screen.height - 8f;
		float panelH = Mathf.Clamp(totalContent + pinH, 40f, maxPanelH);
		_viewH = Mathf.Max(20f, panelH - pad - pinH);
		_scrollable = _contentH > _viewH + 1f;

		_panelRt.sizeDelta = new Vector2(_panelW, panelH);
		_viewportRt.offsetMin = new Vector2(pad, pad);
		_viewportRt.offsetMax = new Vector2(-pad, -(pad + pinH));
		_contentRt.sizeDelta = new Vector2(_panelW - 2 * pad, _contentH);
		LayoutPinBar(pad, pinH);

		float y = 0f;
		int textIdx = 0;
		for (int i = 0; i < rows.Count; i++)
		{
			RowDef r = rows[i];
			float h = r.Height;
			if (r.ChartSlot == 1)
			{
				LayoutChart(_chartRt, _chartImage, OverlayChart.PartyTexture, y, h);
			}
			else if (r.ChartSlot == 2)
			{
				LayoutChart(_chartRtB, _chartImageB, OverlayChart.TakenTexture, y, h);
			}
			else if (r.ChartSlot == 3)
			{
				LayoutChart(_chartRtC, _chartImageC, OverlayChart.EnemyTexture, y, h);
			}
			else if (textIdx < _rowTexts.Count)
			{
				RectTransform rt = _rowRts[textIdx];
				if (r.Copyable) { _copyRefRt = rt; _copyRefPayload = r.CopyText; }
				rt.anchorMin = new Vector2(0f, 1f);
				rt.anchorMax = new Vector2(0f, 1f);
				rt.pivot = new Vector2(0f, 1f);
				rt.anchoredPosition = new Vector2(4f, -y - 1f);
				rt.sizeDelta = new Vector2(_contentRt.sizeDelta.x - 8f, h);
				Text t = _rowTexts[textIdx];
				t.fontSize = h >= 20f ? 14 : 12;
				textIdx++;
			}
			y += h;
		}
		HandleScroll(); // enforce clamp after content sizing
	}

	/// <summary>
	/// The composite multiplier lives in the SECOND composition line, while the first line holds
	/// the power chain; the old code only ever looked at the first line, so the average multiplier
	/// silently stayed "无". Both are checked now, and the pre-rename label is still accepted.
	/// </summary>
	private static double ParseRatio(string comp1, string comp2)
	{
		double v = ParseRatioOne(comp2);
		if (v > 0) return v;
		return ParseRatioOne(comp1);
	}

	private static double ParseRatioOne(string s)
	{
		try
		{
			if (string.IsNullOrEmpty(s)) return 0;
			int i = s.IndexOf("后段倍率 ×");
			int len = "后段倍率 ×".Length;
			if (i < 0)
			{
				i = s.IndexOf("总倍率 ×");
				len = "总倍率 ×".Length;
			}
			if (i < 0) return 0;
			int st = i + len;
			int e = st;
			while (e < s.Length && (char.IsDigit(s[e]) || s[e] == '.')) e++;
			double v;
			return double.TryParse(s.Substring(st, e - st), out v) ? v : 0;
		}
		catch { return 0; }
	}

	/// <summary>Attribute relation. Wiki: the x2 bonus only applies to OUR attacks vs enemies,
	/// and the old disadvantage penalty (x0.5) has been removed -- so 被克 has no damage cut.</summary>
	private static string ParseRel(string comp2)
	{
		if (string.IsNullOrEmpty(comp2)) return "未标注";
		if (comp2.Contains("克制")) return "克制(×2)";
		if (comp2.Contains("被克")) return "被克(无减伤)";
		return "无克制";
	}

	private static readonly string[] SrcWords = new string[]
	{
		"直接攻击","投射物","毒","火伤","持续伤害","反射","自身反射","吸血","吸收","DOT"
	};

	private static string ParseSrc(string comp2)
	{
		if (string.IsNullOrEmpty(comp2)) return "未标注";
		foreach (var w in SrcWords) if (comp2.Contains(w)) return w;
		return "未标注";
	}

	private static List<KeyValuePair<string, long[]>> SortAgg(Dictionary<string, long[]> d)
	{
		var list = new List<KeyValuePair<string, long[]>>(d);
		list.Sort((a, b) => b.Value[0].CompareTo(a.Value[0]));
		if (list.Count > 6) list.RemoveRange(6, list.Count - 6);
		return list;
	}

	private static bool IsHealLike(BattleEvent e)
	{
		// first-class signal: the game's own hit type says this calc is a heal
		if (e.HealCalc) return true;
		if (e.Source == 8 || e.Source == 9 || e.Source == 12 || e.Source == 13 || e.Source == 15 || e.Source == 16) return true;
		string c = e.Comp;
		if (string.IsNullOrEmpty(c)) return false;
		return c.Contains("直接回复") || c.Contains("直接治疗") || c.Contains("被动回复") || c.Contains("回复反噬") || c.Contains("反转治疗")
			|| c.Contains("不死结束") || c.Contains("复活奴仆") || c.Contains("调试伤害");
	}

	/// <summary>Heals that came back as damage (回復反転): real damage, but self-inflicted.</summary>
	private static bool IsReversal(BattleEvent e)
	{
		if (e.Source == 15) return true;
		return !string.IsNullOrEmpty(e.Comp) && (e.Comp.Contains("回复反噬") || e.Comp.Contains("反转治疗"));
	}

	/// <summary>
	/// Add a possibly very long composition line as one or more rows, breaking at "·" / "、" so
	/// nothing is hidden behind the panel edge.
	///
	/// A single segment can itself exceed the row budget (long ability names/conditions), so segments
	/// are hard-split as a last resort -- previously such a segment was emitted whole and simply ran
	/// off the panel, which is what "this line is cut off" looked like.
	/// </summary>
	private static void AddWrapped(List<RowDef> rows, string text, Color color, float h, int maxChars)
	{
		if (string.IsNullOrEmpty(text)) return;
		var parts = new List<string>();
		int start = 0;
		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] == '·' || text[i] == '、')
			{
				parts.Add(text.Substring(start, i + 1 - start));
				start = i + 1;
			}
		}
		if (start < text.Length) parts.Add(text.Substring(start));

		var sb = new System.Text.StringBuilder();
		foreach (var raw in parts)
		{
			string p = raw;
			// hard-split any single segment that cannot fit on its own
			while (p.Length > maxChars)
			{
				if (sb.Length > 0)
				{
					rows.Add(new RowDef { Text = "      " + sb.ToString().Trim(), Color = color, Height = h });
					sb.Length = 0;
				}
				rows.Add(new RowDef { Text = "      " + p.Substring(0, maxChars).Trim(), Color = color, Height = h });
				p = p.Substring(maxChars);
			}
			if (sb.Length > 0 && sb.Length + p.Length > maxChars)
			{
				rows.Add(new RowDef { Text = "      " + sb.ToString().Trim(), Color = color, Height = h });
				sb.Length = 0;
			}
			sb.Append(p);
		}
		if (sb.Length > 0)
			rows.Add(new RowDef { Text = "      " + sb.ToString().Trim(), Color = color, Height = h });
	}

	/// <summary>Does this event belong to the attacker row the detail view currently has selected?
	/// Matches BOTH name and team: this content fields the same character NAME on both sides, and keying by
	/// name alone used to merge our unit with the enemy copy (that is how the enemy healer's 回復反転
	/// damage ended up on our healer's row).</summary>
	private static bool AttackerRowMatches(BattleEvent e, string whoName, int whoTeam)
	{
		if (e.Type != "dmg" || e.Attacker != whoName) return false;
		if (e.AttackerTeam != 0 && e.AttackerTeam != whoTeam) return false;
		return true;
	}

	/// <summary>Target (victim) filter of the detail view, cycled with F7 / Shift+F7. "" = every target.
	/// Keyed by "name#team" for the same same-name-on-both-sides reason as the attacker rows; two
	/// same-named units on the SAME team stay merged (events carry no victim pointer).</summary>
	private static bool VictimRowMatches(BattleEvent e)
	{
		if (string.IsNullOrEmpty(_victimFilter)) return true;
		return (e.Victim + "#" + e.VictimTeam) == _victimFilter;
	}

	// ---------------------------------------------------------------------------------------------
	// 1.7.0 (阶段 F): the dedicated 总贡献 page (F5). Three tables on one page.
	//
	// Alignment: the values are printed WITHOUT thousands separators and padded to fixed display
	// widths, counting a CJK character as two columns. That is what makes a proportional UI font read
	// as a table; with separators the digit counts differ per row and every column drifts.
	// ---------------------------------------------------------------------------------------------


	// ---------------------------------------------------------------------------------------------
	// 1.7.7 (P2-A #7): which battle does the "上一场" view describe?
	//
	// ContributionSession.Get() refreshes its cache ONLY in the live branch, and its non-live branch
	// returns whatever the cache happens to hold. So a battle spent with the overlay hidden (F8) or
	// parked on the F6 detail / F10 chart view never touches the cache, and F5 afterwards showed the
	// battle BEFORE that one -- the cache still held it.
	//
	// Calling ContributionSession.Invalidate() at battle end is NOT a fix: that same cache is the only
	// storage of the finished-battle result, so clearing it would turn "the wrong battle" into
	// "暂无战斗数据". The view is therefore RESOLVED here: while live nothing changes (Get() already
	// refreshed the cache from the live session); once the battle is over the result is recomputed
	// from Aggregator.History[0].Session, which is by definition the most recent FINISHED battle and
	// is the very object ExportService wrote the contribution section from. The recompute is memoised
	// per finished summary, so one battle costs exactly one extra computation.
	private static BattleSummary _prevSummary;
	private static ContributionView _prevSummaryView;
	private static bool _prevSummaryUsedFolds;

	internal static ContributionView ResolveContributionView(bool useFolds)
	{
		ContributionView live = ContributionSession.Get(useFolds);
		if (live == null || live.Live) return live;
		if (Aggregator.History.Count == 0) return live;
		BattleSummary last = Aggregator.History[0];
		if (last == null || last.Session == null) return live;
		if (ReferenceEquals(_prevSummary, last) && _prevSummaryUsedFolds == useFolds && _prevSummaryView != null)
			return _prevSummaryView;
		_prevSummary = null;
		_prevSummaryView = null;
		// Mirror the live branch: with ReconcileCalc off there are no folds, so there is nothing to
		// attribute and the stated reason (not a zero table) is the honest answer.
		if (!useFolds) return live;
		ContributionResult res;
		try { res = ContributionSession.Compute(last.Session, useFolds); }
		catch { return live; }
		if (res == null) return live;
		_prevSummary = last;
		_prevSummaryUsedFolds = useFolds;
		_prevSummaryView = new ContributionView
		{
			Result = res,
			Live = false,
			Usable = true,
			QuestId = last.Session.QuestId,
			Seconds = last.Session.ActiveSeconds,
		};
		return _prevSummaryView;
	}

	/// <summary>1.7.7 (P2-A #8, residual): the F5 header must never print "任务 0  0秒": the two
	/// "不可用" views (ReconcileCalc off / no damage events yet) carry no battle id, and neither does
	/// the "no cached battle" view. A missing measurement must fall back to a real battle -- the live
	/// session first, then the most recent finished one -- instead of showing 0 as if it were one.</summary>
	private static void ResolveHeaderBattle(ContributionView view, out int quest, out double seconds)
	{
		if (view != null && view.QuestId != 0) { quest = view.QuestId; seconds = view.Seconds; return; }
		BattleSession liveSession = Aggregator.Session;
		if (liveSession != null) { quest = liveSession.QuestId; seconds = liveSession.ActiveSeconds; return; }
		if (Aggregator.History.Count > 0)
		{
			BattleSummary last = Aggregator.History[0];
			int parsed;
			int.TryParse(last.QuestId, out parsed);
			quest = parsed;
			seconds = last.DurationSeconds;
			return;
		}
		quest = view != null ? view.QuestId : 0;
		seconds = view != null ? view.Seconds : 0.0;
	}

	/// <summary>
	/// R55 (user request): draw the granted 「阻挡增伤」 residual with the CHARACTER table's own column
	/// geometry, so it reads as a contribution table instead of as one loose warning line. Two things are
	/// deliberate:
	///   * it is a SEPARATE block, placed after the 未归因 lines, because the character table's 合计 row must
	///     still equal the sum of the rows above it -- merging a not-yet-charged pool into table 1 would
	///     silently move credit, which is the exact mistake R54 removed;
	///   * every credit column that does not apply prints "-", not 0. A measured zero and "not charged" are
	///     different statements, and this page has already paid for confusing them.
	/// The rows come from ContributionRowModel.BuildPending, so the panel and the tests read ONE model.
	/// </summary>
	private static void AppendPendingRows(List<RowDef> rows, ContributionResult res, double total)
	{
		ContributionPendingTable pend = ContributionRowModel.BuildPending(res, total);
		if (pend.Rows.Count == 0) return;
		rows.Add(new RowDef { Text = "", Color = DimColor, Height = 6f });
		rows.Add(new RowDef { Text = FallbackText.PendingHeaderLine(pend.Labels), Color = WarnColor, Height = 16f });
		rows.Add(new RowDef { Text = ContributionColumns.HeaderLine(ContributionColumns.T1PendingSpec()), Color = DimColor, Height = 15f });
		for (int i = 0; i < pend.Rows.Count; i++)
		{
			ContributionPendingValues p = pend.Rows[i];
			rows.Add(new RowDef
			{
				Text = ContributionColumns.T1PendingRow(p.Name, p.Amount, p.Share, p.Folds),
				Color = WarnColor, Height = 16f,
			});
		}
		rows.Add(new RowDef
		{
			Text = ContributionColumns.T1PendingTotalsLine(pend.Total, pend.Share, pend.Folds),
			Color = NeutralColor, Height = 16f,
		});
		rows.Add(new RowDef { Text = FallbackText.PendingNoteLine(pend), Color = DimColor, Height = 15f });
	}

	/// <summary>
	/// R56 (BID-3, plan §6): the identity line of whatever the page is DISPLAYING. Two rows on purpose --
	/// the short tag is for scanning, the full id is the copyable unambiguous reference -- and the line
	/// states the WRITE result separately from the session state, because "final" is a fact about the
	/// battle while "已导出" is a fact about the disk.
	/// </summary>
	private static void AppendBattleRefRow(List<RowDef> rows, BattleRef r, int quest, string label)
	{
		if (r == null)
		{
			rows.Add(new RowDef
			{
				Text = "  " + label + "无编号(legacy):该场按文件内容哈希引用,见 battle_select.py",
				Color = DimColor, Height = 15f,
			});
			return;
		}
		string state = r.State == BattleRefPolicy.StateFinal ? "终局"
			: (r.State == BattleRefPolicy.StateProvisional ? "暂存(未终局)" : "战斗中");
		if (r.ResetCount > 0) state += " 已重置×" + r.ResetCount;
		string write = string.IsNullOrEmpty(r.ExportSha256) ? "尚未导出" : "已导出";
		rows.Add(new RowDef
		{
			Text = "  " + label + r.ShortTag + "   " + state + " · " + write
				+ (CopyFlashActive ? "   ✓已复制到剪贴板" : "   [点击此行复制引用]"),
			Color = HeaderColor, Height = 16f,
		});
		rows.Add(new RowDef
		{
			Text = "  战斗编号 " + r.Id,
			Color = NeutralColor, Height = 16f,
			Copyable = true,
			CopyText = BattleRefPolicy.CopyText(r.Id, r.Revision, quest, r.State, r.ResetCount,
				string.IsNullOrEmpty(r.ExportPath) ? "" : System.IO.Path.GetFileName(r.ExportPath),
				r.ExportSha256),
		});
	}

	/// <summary>The identity of the battle the panel is currently describing: the live session while one
	/// is running, else the newest finished battle. Views ask THIS instead of re-deriving an id.</summary>
	internal static BattleRef DisplayedRef(bool live)
	{
		try
		{
			if (live)
			{
				BattleSession s = Aggregator.Session;
				return s == null ? null : s.Ref;
			}
			if (Aggregator.History.Count > 0)
			{
				BattleSummary b = Aggregator.History[0];
				if (b == null) return null;
				return b.Ref != null ? b.Ref : (b.Session == null ? null : b.Session.Ref);
			}
		}
		catch { }
		return null;
	}

	private static void AppendContributionTable(List<RowDef> rows)
	{
		int firstRow = rows.Count;
		bool panelOn = Plugin.CfgShowContribution == null || Plugin.CfgShowContribution.Value;
		bool useFolds = Plugin.CfgReconcileCalc != null && Plugin.CfgReconcileCalc.Value;
		ContributionView view = panelOn ? ResolveContributionView(useFolds) : null;
		// 1.7.5: the header used to read the LIVE session, so the "last battle" view printed
		// "任务 0  0秒" above a table that was describing a real battle. The view now carries which
		// battle it is (ContributionView.QuestId/Seconds).
		// 1.7.7: when the panel is switched off, view is null and the header used to print "任务 0  0秒"
		// -- the very string 1.7.5 claimed to have removed. Fall back to the live session instead.
		// 1.7.7 (P2-A #8 residual): a view can be NON-null and still carry no battle (the two 不可用
		// views and the panel-off case all have QuestId 0), so the fallback must key on whether the
		// view names a battle -- live session first, then the most recent finished one.
		int hQuest;
		double hSeconds;
		ResolveHeaderBattle(view, out hQuest, out hSeconds);
		rows.Add(new RowDef { Text = "总贡献  F5返回  任务 " + hQuest + "   " + BattleTime.Seconds(hSeconds), Color = HeaderColor, Height = 20f });
		// R56 (plan §6): the title is bound to the battle this table describes, and it is shown even when
		// the data is unavailable -- an "unavailable" page must still say WHICH battle it is about.
		AppendBattleRefRow(rows, DisplayedRef(view != null && view.Live), hQuest, view != null && view.Live ? "本场 " : "上一场 ");
		if (!panelOn)
		{
			rows.Add(new RowDef { Text = "  总贡献看板已被 General/ShowContribution 关闭", Color = WarnColor, Height = 16f });
			return;
		}
		if (view == null || !view.Usable || view.Result == null)
		{
			rows.Add(new RowDef { Text = "  不可用 —— " + ((view == null) ? "无数据" : (view.Unavailable ?? "无数据")), Color = WarnColor, Height = 16f });
			rows.Add(new RowDef { Text = "  (归属需要逐击折叠;ReconcileCalc 关闭时不能凭空编一个来源)", Color = DimColor, Height = 15f });
			return;
		}
		ContributionResult res = view.Result;
		double total = res.Stats.Analyzable;
		if (!view.Live) rows.Add(new RowDef { Text = "  (上一场)", Color = DimColor, Height = 15f });

		// table 1: per character
		rows.Add(new RowDef { Text = "【角色贡献】(对数份额口径;总贡献 = 自身 + 他人因你)", Color = HeaderColor, Height = 17f });
		// 1.7.6 (user request): the metric needs a plain-language definition or the page is unreadable to
		// anyone who did not build it. Every term is defined before it is used.
		// 1.7.11 (user request): 基础 and 自身规则 are the SAME side of the ledger -- both are credit this
		// character keeps from his OWN hits -- and presenting them as peers of 辅助 read as if 自身规则 were
		// not his damage. They are now one 自身 column, and the mirror quantity (the part of his own hits
		// that another provider takes) is shown next to it, so the row reads as two identities.
		rows.Add(new RowDef { Text = "  【口径】自身 = 基础 + 自身规则;总贡献 = 自身 + 他人因你;直接打出 = 自身 + 被队友分走", Color = DimColor, Height = 15f });
		rows.Add(new RowDef { Text = "   自身=自己命中里归自己的份额;基础=自己的攻击力/属性打出来的;自身规则=自己的装备/能力倍率应得的部分", Color = DimColor, Height = 15f });
		rows.Add(new RowDef { Text = "   他人因你=队友因他多打出来的(记他名下,不是他打出的);被队友分走=自己命中里由他人倍率拿走的部分", Color = DimColor, Height = 15f });
		rows.Add(new RowDef { Text = "   全队总贡献相加 = 可分析伤害(不是他打出的伤害);直接占比=他实际打出的伤害占比;分池按「倍率对数份额」", Color = DimColor, Height = 15f });
		rows.Add(new RowDef
		{
			// RF5c: the header is BUILT from the column spec, so its widths cannot drift from the labels.
			Text = ContributionColumns.HeaderLine(ContributionColumns.T1),
			Color = DimColor, Height = 15f,
		});
		// RF5f: the VALUES come from the pure row model (which actors are shown, both shares, and the four
		// sums the footer prints); the renderer only turns them into rows.
		ContributionTableValues t1 = ContributionRowModel.Build(res, total);
		ContributionRowModel.BuildRules(res, t1);
		ContributionRowModel.BuildLinks(res, t1);
		for (int i = 0; i < t1.Rows.Count; i++)
		{
			ContributionActorValues a = t1.Rows[i];
			rows.Add(new RowDef
			{
				// RF5d: the row is BUILT from the column definition (the widths live there, not here).
				Text = ContributionColumns.T1Row(a.Name, a.Summon, a.Total, a.Share, a.BaseAndSelf, a.Assist,
				                                 a.Received, a.DirectShare, a.Hits),
				Color = AllyColor, Height = 16f,
			});
		}
		// 1.7.7: the totals row now uses the SAME column geometry as the header (85 columns) and fills
		// the credit columns with their real sums. Before it was a hand-made 88-column variant that
		// put "未归因" under 辅助 and its percentage under 命中.
		// 1.7.11: 自身 is the sum of BOTH halves it groups (基础 + 自身规则), and 被队友分走 got its own
		// sum, so the two identities the 口径 line promises can be checked on the totals row itself.
		// RF5f: the sums were computed here in a second walk over the actors; the model owns them now (and
		// they still cover every actor, including the ones the table does not show).
		rows.Add(new RowDef
		{
			Text = ContributionColumns.T1TotalsLine(res.Stats.Attributed, t1.SumBase + t1.SumSelf, t1.SumAssist, t1.SumReceived),
			Color = NeutralColor, Height = 16f,
		});
		double unattrPct = total > 0.0 ? 100.0 * res.Stats.Unattributed / total : 0.0;
		rows.Add(new RowDef
		{
			Text = "  未归因 " + DisplayFormat.Fmt(res.Stats.Unattributed) + "(" + DisplayFormat.Pct(unattrPct) + ")  未计入任何角色",
			Color = DimColor, Height = 15f,
		});
		// R54 (user request): name the families INSIDE the residual. The granted 「阻挡增伤」 channel is the one
		// whose provider is unresolved, and reading it as part of one anonymous total is exactly what misled.
		for (int ui = 0; ui < res.Unattributed.Count; ui++)
		{
			ContributionUnattributedRow ua = res.Unattributed[ui];
			rows.Add(new RowDef
			{
				Text = FallbackText.UnattributedBreakdownLine(ua.Reason, ua.Amount, ua.Folds),
				Color = WarnColor, Height = 15f,
			});
		}
		// R55: the granted family's pending pool, on the character table's geometry.
		AppendPendingRows(rows, res, total);
		rows.Add(new RowDef
		{
			Text = "  (* = 使魔)  可分析伤害 " + DisplayFormat.Fmt(res.Stats.Analyzable) + "   倍率池 " + DisplayFormat.Fmt(res.Stats.PoolTotal)
				 + "   命中 " + DisplayFormat.Num(res.Stats.Hits) + "   折叠 " + DisplayFormat.Num(res.Stats.Folds) + "   无构成 " + res.Stats.CalcMissing
				 + "   (自身+被队友分走=直接打出;各列独立四舍五入,行内相加可能差 1;可分析伤害 ≠ 总伤害,见导出 totals)",
			Color = DimColor, Height = 15f,
		});

		// table 2: rules
		rows.Add(new RowDef { Text = "", Color = DimColor, Height = 6f });
		rows.Add(new RowDef { Text = "【规则当量】(该规则带来的份额之和;归属由 byUnit/持有者/全局规则名解析)", Color = HeaderColor, Height = 17f });
		rows.Add(new RowDef
		{
			Text = ContributionColumns.HeaderLine(ContributionColumns.T2),
			Color = DimColor, Height = 15f,
		});
		// 1.7.7 rev2: kind/side were the last padded cells with no width guard -- a future kind string longer
		// than 8 columns would have shifted the row exactly like the names did. RF5g: the filter and the cap
		// are the row model's now.
		for (int i = 0; i < t1.Rules.Count; i++)
		{
			ContributionRuleValues rr = t1.Rules[i];
			rows.Add(new RowDef
			{
				Text = ContributionColumns.T2Row(rr.Name, rr.Kind, rr.Side, rr.Owner, rr.Hits, rr.Folds, rr.Damage),
				Color = NeutralColor, Height = 15f,
			});
		}
		if (t1.RuleTotal > t1.Rules.Count)
			rows.Add(new RowDef { Text = $"  ... 共 {t1.RuleTotal} 条规则(按当量降序)", Color = DimColor, Height = 14f });

		// table 3: relations
		if (res.Links.Count > 0)
		{
			rows.Add(new RowDef { Text = "", Color = DimColor, Height = 6f });
			rows.Add(new RowDef { Text = "【辅助关系】(提供者 → 受益者;这是唯一会跨角色移动的份额)", Color = HeaderColor, Height = 17f });
			rows.Add(new RowDef
			{
				// 1.7.7: the header used to end with "  主要规则", a column no data row ever filled (the link
				// row carries no rule field) -- it made the header 10 columns wider than its own table.
				Text = ContributionColumns.HeaderLine(ContributionColumns.T3),
				Color = DimColor, Height = 15f,
			});
			// RF5g: the link rows (both endpoint names resolved) come from the row model; there is no value
			// filter here, unlike the rules table.
			for (int i = 0; i < t1.Links.Count; i++)
			{
				ContributionLinkValues l = t1.Links[i];
				rows.Add(new RowDef
				{
					Text = ContributionColumns.T3Row(l.From, l.To, l.Hits, l.Amount),
					Color = AllyColor, Height = 15f,
				});
			}
			if (t1.LinkTotal > t1.Links.Count)
				rows.Add(new RowDef { Text = $"  ... 共 {t1.LinkTotal} 组关系", Color = DimColor, Height = 14f });
		}

		// 1.7.5: the old text still claimed the attack-power addends stay in 基础 -- false since 1.7.4,
		// which attributes the teammate-granted ones as kind=atkadd. A stale claim inside the UI is the
		// same defect class as a stale claim inside the data.
		rows.Add(new RowDef { Text = "  (每秒最多重算一次;数字与导出 contribution 段同源。队友给的攻击力加算已按 kind=atkadd 归属,1.7.4 起)", Color = DimColor, Height = 15f });

		// 1.7.6: the entire page uses the MONOSPACED font, because every PadL/PadR above assumes a 1:2
		// grid (CJK = 2 columns, a space or an ASCII digit = 1). On the proportional UI font that
		// assumption is false and the columns drift row by row -- the user-reported misalignment.
		Font mono = GetMonoFont();
		if (!GameRef.IsNull(mono))
			for (int i = firstRow; i < rows.Count; i++) rows[i].Font = mono;
	}

	private static List<RowDef> BuildRows()
	{
		var rows = new List<RowDef>();
		_pinLine = "";   // only the detail view sets it (shown in the pinned bar, see LayoutPinBar)
		BattleSession session = Aggregator.Session;
		bool inBattle = session != null && session.InBattle;

		if (View == ViewMode.Detail)
		{
			// Claim the pending F7 step up front: it must be discarded if this render cannot build a target
			// list (no session / no rows), otherwise the press would silently apply to some later battle.
			int filterStep = _filterStep;
			_filterStep = 0;
			var ds = inBattle ? session : (Aggregator.History.Count > 0 ? SessionForView() : null);
			if (ds == null)
			{
				rows.Add(new RowDef { Text = "伤害明细  暂无战斗数据", Color = HeaderColor, Height = 20f });
				return rows;
			}
			// R56 (plan §6): the detail timeline and the identity come from the SAME BattleSession (ds), so
			// the number above the records is by construction the number of the file this battle writes.
			AppendBattleRefRow(rows, ds.Ref, ds.QuestId, "本场 ");
			// Party damage, keyed by (name, team).
			// This content can field the SAME character name on BOTH sides (mirror match), and
			// keying by name alone merged our unit with the enemy copy -- which is also how the
			// enemy healer's 回復反転 damage ended up on our healer's row.
			var names = new List<string>();                        // unique key = name#team
			var disp = new Dictionary<string, string>();           // key -> DISPLAY label (may be prefixed)
			var keyName = new Dictionary<string, string>();        // key -> RAW name used to match events
			var keyTeam = new Dictionary<string, int>();
			var totals = new Dictionary<string, long>();
			var counts = new Dictionary<string, int>();
			// 1) every party CHARACTER **and our token units**. Tokens used to be skipped here, so
			//    their damage only showed up in the roster totals and the chart, never as an entry
			//    of its own in the per-unit detail.
			foreach (var a in ds.OrderedActors)
			{
				if (!CharacterInfo.IsAllyTeam(a.Team)) continue;
				if (a.Kind == "C") continue;                       // the citadel is not a unit
				if (string.IsNullOrEmpty(a.Name)) continue;
				string key = a.Name + "#" + (int)a.Team;
				if (!totals.ContainsKey(key))
				{
					names.Add(key);
					totals[key] = 0L;
					counts[key] = 0;
					disp[key] = Aclabel(a);
					keyName[key] = a.Name;
					keyTeam[key] = (int)a.Team;
				}
			}
			// 2) attackers seen in events but never registered as actors (party side only)
			foreach (var e in ds.Events)
			{
				if (e.Type != "dmg" || e.Attacker == "?") continue;
				int tm = (e.AttackerTeam == 0) ? 1 : e.AttackerTeam;
				if (tm != 1) continue;
				if (IsHealLike(e)) continue;
				string key = e.Attacker + "#" + tm;
				if (!totals.ContainsKey(key)) { names.Add(key); totals[key] = 0L; counts[key] = 0; disp[key] = e.Attacker; keyName[key] = e.Attacker; keyTeam[key] = tm; }
			}
			// 3) accumulate damage, matching BOTH name and team
			foreach (var e in ds.Events)
			{
				if (e.Type != "dmg" || e.Attacker == "?") continue;
				string key = e.Attacker + "#" + ((e.AttackerTeam == 0) ? 1 : e.AttackerTeam);
				if (!totals.ContainsKey(key)) continue;
				if (IsHealLike(e)) continue;
				totals[key] += e.Amount;
				counts[key]++;
			}
			if (names.Count == 0)
			{
				rows.Add(new RowDef { Text = "伤害明细  本场没有可归属的伤害事件", Color = HeaderColor, Height = 20f });
				return rows;
			}
			names.Sort((a, b) => totals[b].CompareTo(totals[a]));
			if (_detailIdx < 0) _detailIdx = names.Count - 1;
			if (_detailIdx >= names.Count) _detailIdx = 0;
			string whoKey = names[_detailIdx];
			string who = disp[whoKey];          // display label (tokens get a "[使魔] " prefix)
			string whoName = keyName[whoKey];   // raw name -- events must be matched with THIS, not the label
			int whoTeam = keyTeam[whoKey];

			// ---- 0) target filter (F7 / Shift+F7) ----
			// Every selectable target is derived from THIS attacker's own events, ordered by damage, so the
			// list can never advertise a target the current character has no record against. Resolving the
			// F7 step here (instead of in CheckKeys) is what makes that possible.
			var vKeys = new List<string>();                       // "" = all targets, then "name#team"
			var vDisp = new Dictionary<string, string>();
			var vTotals = new Dictionary<string, long>();
			var vCounts = new Dictionary<string, int>();
			vKeys.Add("");
			foreach (var e in ds.Events)
			{
				if (!AttackerRowMatches(e, whoName, whoTeam)) continue;
				if (IsHealLike(e)) continue;
				string vk = e.Victim + "#" + e.VictimTeam;
				if (!vTotals.ContainsKey(vk))
				{
					vKeys.Add(vk);
					vTotals[vk] = 0L;
					vCounts[vk] = 0;
					vDisp[vk] = e.Victim;
				}
				vTotals[vk] += e.Amount;
				vCounts[vk]++;
			}
			// sort targets by damage, keep the "all" pseudo-entry first
			var rest = vKeys.GetRange(1, vKeys.Count - 1);
			rest.Sort((a, b) => vTotals[b].CompareTo(vTotals[a]));
			vKeys.RemoveRange(1, vKeys.Count - 1);
			vKeys.AddRange(rest);
			long allTotal = 0L; int allCount = 0;
			foreach (var kv in vTotals) { allTotal += kv.Value; allCount += vCounts[kv.Key]; }
			vDisp[""] = "全部目标";
			vTotals[""] = allTotal;
			vCounts[""] = allCount;
			if (filterStep != 0)
			{
				int cur = vKeys.IndexOf(_victimFilter);       // -1 when the current filter is not offered
				if (cur < 0) cur = 0;
				int next = cur + filterStep;
				if (next < 0) next = vKeys.Count - 1;
				if (next >= vKeys.Count) next = 0;
				_victimFilter = vKeys[next];
				_detailPage = 0;
			}
			if (!string.IsNullOrEmpty(_victimFilter) && !vKeys.Contains(_victimFilter))
				_victimFilter = "";                            // clamped: never leave a dead filter behind
			bool filtered = !string.IsNullOrEmpty(_victimFilter);
			string filtName = filtered ? vDisp[_victimFilter] : "";
			// NOTE: the per-character header row is NOT emitted here any more -- the pinned bar at the top
			// of the panel shows the same information (character, totals, page, keys) and is always
			// visible. Two near-identical header lines at the top were pure duplication (1.0.40).

			// ---- 1) summary by attribute relation / source / average multiplier ----
			var relAgg = new Dictionary<string, long[]>();
			var relCnt = new Dictionary<string, int>();
			var srcAgg = new Dictionary<string, long[]>();
			var srcCnt = new Dictionary<string, int>();
			double ratioSum = 0; int ratioN = 0; int noComp = 0;
			int reverseN = 0; long reverseSum = 0;
			int friendlyN = 0; long friendlySum = 0;
			int absorbN = 0; long absorbSum = 0;
			int inflictN = 0;
			int foreignN = 0;
			var inflictAgg = new Dictionary<string, int>();
			foreach (var e in ds.Events)
			{
				if (!AttackerRowMatches(e, whoName, whoTeam)) continue;
				if (!VictimRowMatches(e)) continue;                               // F7 target filter
				// 被吸收/无效化: the game's own damage report counts the full value while only Amount reached
				// 耐久, so this is what makes our total and the game's total reconcile. Counted before the
				// skips below so an absorbed self-hit is still visible here.
				if (e.Nominal > e.Amount) { absorbN++; absorbSum += e.Nominal - e.Amount; }
				// Did this record inflict an ailment on the victim? (diffed around the hit; 1.0.52)
				// Two very different cases: the record's attacker IS the applier (本条附加), or the game
				// credits someone else and this record is just where the change was noticed (他方施加).
				string stt = StatusDeltaProbe.Tag(e);
				if (stt.Length > 0)
				{
					bool own = StatusDeltaProbe.IsOwnInfliction(e);
					if (own) inflictN++; else foreignN++;
					string aggKey = own ? stt : (stt + " ← " + StatusDeltaProbe.Appliers(e));
					foreach (string nm in stt.Split('、'))
					{
						if (nm.Length == 0) continue;
						inflictAgg.TryGetValue(aggKey, out var c0);
						inflictAgg[aggKey] = c0 + 1;
					}
				}
				// 回復反転 is damage, but self-inflicted: counted separately, never as output
				if (IsReversal(e)) { reverseN++; reverseSum += e.Amount; continue; }
				if (e.Friendly) { friendlyN++; friendlySum += e.Amount; continue; }
				if (IsHealLike(e)) continue;
				if (string.IsNullOrEmpty(e.Comp)) { noComp++; continue; }
				double r = ParseRatio(e.Comp, e.Comp2);
				if (r > 0) { ratioSum += r; ratioN++; }
				string rel = ParseRel(e.Comp2);
				string src = ParseSrc(e.Comp2);
				if (!relAgg.ContainsKey(rel)) { relAgg[rel] = new long[1]; relCnt[rel] = 0; }
				relAgg[rel][0] += e.Amount; relCnt[rel]++;
				if (!srcAgg.ContainsKey(src)) { srcAgg[src] = new long[1]; srcCnt[src] = 0; }
				srcAgg[src][0] += e.Amount; srcCnt[src]++;
			}
			string sumScope = filtered ? ("筛选:" + filtName) : "全场";
			string sumCount = filtered
				? (vCounts[_victimFilter].ToString() + " 条 / " + vTotals[_victimFilter].ToString("N0"))
				: (counts[whoKey].ToString() + " 条 / " + totals[whoKey].ToString("N0"));
			rows.Add(new RowDef
			{
				Text = $"汇总({sumScope}):{sumCount} 伤害   平均后段倍率 {(ratioN > 0 ? "×" + (ratioSum / ratioN).ToString("F3") : "无")}   未匹配构成 {noComp} 条"
					+ (reverseN > 0 ? $"   回复反噬 {reverseN} 条/{reverseSum:N0}" : "")
					+ (friendlyN > 0 ? $"   自伤/反噬 {friendlyN} 条/{friendlySum:N0}"
						+ (Plugin.CfgFilterFriendlyFire != null && Plugin.CfgFilterFriendlyFire.Value ? "(已剔除)" : "(已含在伤害内;游戏自身也计入)") : "")
					+ (absorbN > 0 ? $"   被吸收/无效化 {absorbN} 条/{absorbSum:N0}(未入耐久;游戏自身统计计入:本场游戏口径 = 伤害 + 该值)" : "")
					+ (inflictN > 0 ? $"   本条附加异常状态 {inflictN} 条" : "")
					+ (foreignN > 0 ? $"   他方施加(本行仅承载) {foreignN} 条" : ""),
				Color = HeaderColor, Height = 18f
			});
			rows.Add(new RowDef
			{
				Text = "官方公式:(攻击力×技能系数 − 防御×防御补正×贯通补正) × 后段倍率   后段倍率 = 与/被伤害补正 × 会心伤害 × 属性(×2)   游戏未单独暴露其数值,无法再拆分   注:行内的「系数(推算)」= 计算威力÷攻击力,召喚攻击时两者口径可能不同,会标注",
				Color = DimColor, Height = 14f
			});
			foreach (var kv in SortAgg(relAgg))
				rows.Add(new RowDef { Text = $"  属性关系 {kv.Key}: {relCnt[kv.Key]} 条   {kv.Value[0]:N0}", Color = NeutralColor, Height = 16f });
			foreach (var kv in SortAgg(srcAgg))
				rows.Add(new RowDef { Text = $"  来源 {kv.Key}: {srcCnt[kv.Key]} 条   {kv.Value[0]:N0}", Color = NeutralColor, Height = 16f });
			if (inflictAgg.Count > 0)
			{
				// Who the game credits, not who happened to be nearest: "本条附加" means this record's own
				// attacker did it, "他方施加" names the real source (the row is only the observation point).
				var stList = new List<KeyValuePair<string, int>>(inflictAgg);
				stList.Sort((x, y) => y.Value.CompareTo(x.Value));
				var stb = new StringBuilder("  异常状态来源:");
				foreach (var kv in stList) stb.Append(' ').Append(kv.Key).Append('×').Append(kv.Value);
				AddWrapped(rows, stb.ToString(), StatusColor, 16f, 118);
			}
			// 1.1.0: what this unit actually HAS, with provenance (刻印/神器/觉醒/职业...), and which
			// 素质/词条 the game counted as fired. Both come from the game's own data -- the roster is
			// cached on ActorStats during the battle precisely so it survives ActorStats.Source being
			// cleared at finalize.
			if (Plugin.CfgAbilityRoster == null || Plugin.CfgAbilityRoster.Value)
				AppendRosterRows(rows, FindActor(ds, keyName[whoKey], keyTeam[whoKey]));

			// ---- 2) per-hit list, paged by battle time: one page = DetailPageSeconds seconds ----
			// The list used to stop after 60 entries ("其余 N 条已省略"), which silently hid everything
			// after the first minute of a long fight. Now every entry of the selected window is shown and
			// ← / → move the window, so no record is dropped.
			double maxT = 0.0;
			int listTotal = 0;
			foreach (var e in ds.Events)
			{
				if (!AttackerRowMatches(e, whoName, whoTeam)) continue;
				if (!VictimRowMatches(e)) continue;                               // F7 target filter
				if (IsHealLike(e)) continue;
				listTotal++;
				if (e.T > maxT) maxT = e.T;
			}
			int pages = (int)(maxT / DetailPageSeconds) + 1;
			if (pages < 1) pages = 1;
			if (_detailPage < 0) _detailPage = 0;
			if (_detailPage >= pages) _detailPage = pages - 1;
			double pageLo = _detailPage * DetailPageSeconds;
			double pageHi = pageLo + DetailPageSeconds;
			int pageN = 0;
			foreach (var e in ds.Events)
			{
				if (!AttackerRowMatches(e, whoName, whoTeam)) continue;
				if (!VictimRowMatches(e)) continue;                               // F7 target filter
				if (IsHealLike(e)) continue;
				if (e.T < pageLo || e.T >= pageHi) continue;
				pageN++;
			}
			// ---- target selector ----
			// The selectable targets, with their own counts, so the filter is discoverable without
			// documentation: the marked entry is the active one (F7 = next, Shift+F7 = previous).
			var selSb = new StringBuilder();
			selSb.Append("目标筛选(F7 下一个 / Shift+F7 上一个):");
			int selShown = 0;
			foreach (var vk in vKeys)
			{
				if (selShown++ >= 12) { selSb.Append(" …"); break; }
				bool on = (vk == _victimFilter);
				selSb.Append(on ? " 【" : "  ").Append(vDisp[vk]).Append(on ? "】" : "")
					.Append('(').Append(vCounts[vk]).Append(')');
			}
			if (vKeys.Count > 12) selSb.Append($"  共 {vKeys.Count} 个目标(F7 逐个切换)");
			AddWrapped(rows, selSb.ToString(), filtered ? HeaderColor : DimColor, 16f, 118);
			rows.Add(new RowDef
			{
				Text = "—— 逐条伤害(每页 20 秒;←/→ 翻页,滚轮/PgUp·PgDn 滚动)——",
				Color = DimColor, Height = 16f
			});
			// everything numeric lives in the pinned bar only (see LayoutPinBar), so the heading above
			// stays a plain delimiter instead of repeating the counts.
			_pinLine = $"伤害明细 {_detailIdx + 1}/{names.Count} {who} · 总伤害 {totals[whoKey]:N0} / {counts[whoKey]} 条"
				+ (ds.Ref == null ? " · 无编号(legacy)" : " · " + ds.Ref.ShortTag)
				+ (filtered ? $" · 筛选→{filtName} {vTotals[_victimFilter]:N0}/{vCounts[_victimFilter]} 条" : "")
				+ $" · 第 {_detailPage + 1}/{pages} 页({pageLo:F0}~{pageHi:F0} 秒)本页 {pageN} 条 / 列表 {listTotal} 条"
				+ " · ←/→ 翻页(20秒/页) F7 筛选目标 F11/F12 换角色 F6返回";
			if (pageN == 0)
				rows.Add(new RowDef
				{
					Text = filtered
						? $"  (这一页没有 {filtName} 的伤害记录;按 → 看后 20 秒,或按 F7 换目标/回到全部)"
						: "  (这一页没有伤害记录,按 → 看后 20 秒)",
					Color = WarnColor, Height = 16f
				});
			int shown = 0;
			foreach (var e in ds.Events)
			{
				if (!AttackerRowMatches(e, whoName, whoTeam)) continue;
				if (!VictimRowMatches(e)) continue;                               // F7 target filter
				if (IsHealLike(e)) continue;
				if (e.T < pageLo || e.T >= pageHi) continue;
				shown++;
				string stTag = StatusDeltaProbe.Tag(e);
				bool stOwn = StatusDeltaProbe.IsOwnInfliction(e);
				string stApp = StatusDeltaProbe.Appliers(e);
				rows.Add(new RowDef
				{
					// "附加" claims the row DID it, which is only true when the game credits this very
					// attacker; otherwise the row merely carries the observation ("目标被挂").
					Text = $"{shown}. {BattleTime.Hit(e.T)} → {e.Victim}   伤害 {e.Amount:N0}"
						+ (e.Friendly ? "   [自伤/反噬]" : "")
						+ (string.IsNullOrEmpty(e.Triggers) ? "" : "   素质发动:" + e.Triggers)
						+ (stTag.Length == 0 ? ""
							: (stOwn ? "   附加:" + stTag
								: "   目标被挂:" + stTag + (stApp.Length > 0 ? " (施加者 " + stApp + ")" : " (施加者未知)"))),
					Color = e.Friendly ? WarnColor : (stTag.Length == 0 ? NeutralColor : (stOwn ? StatusColor : DimColor)),
					Height = 16f
				});
				if (!string.IsNullOrEmpty(e.StatusDelta))
					rows.Add(new RowDef { Text = "      " + e.StatusDelta, Color = StatusColor, Height = 14f });
				AddWrapped(rows, e.Comp, DimColor, 14f, 118);
				AddWrapped(rows, e.Comp2, NeutralColor, 14f, 118);
				AddWrapped(rows, e.Comp3, WarnColor, 14f, 118);
				AddWrapped(rows, e.Comp4, StatusColor, 14f, 118);
				if (string.IsNullOrEmpty(e.Comp) && string.IsNullOrEmpty(e.Comp2))
					rows.Add(new RowDef
					{
						Text = "      (无结算对象:该伤害未经 DamageCalculater,来源:"
							+ (e.Source != 0 ? CompositionProbe.SrcName(e.Source) : "未知(毒/DOT/反射/使魔 等路径)")
							+ " · 归属:" + (string.IsNullOrEmpty(e.Attr) ? "?" : e.Attr) + ")",
						Color = DimColor, Height = 14f
					});
			}
			if (listTotal == 0)
				rows.Add(new RowDef { Text = "(该角色本场没有伤害事件)", Color = WarnColor, Height = 16f });
			return rows;
		}

		if (View == ViewMode.Chart)
		{
			var viewSession = inBattle ? session : (Aggregator.History.Count > 0 ? SessionForView() : null);
			string modeTag = OverlayChart.UsePerSecond ? "每秒DPS" : "累计";
			rows.Add(new RowDef { Text = $"{modeTag}  上:我方伤害 中:耐久% 下:敌方  F10列表 F12累计/每秒", Color = HeaderColor, Height = 20f });
			rows.Add(new RowDef { Text = ChartCaption(viewSession), Color = DimColor, Height = 16f });
			AppendBattleRefRow(rows, DisplayedRef(inBattle), viewSession == null ? 0 : viewSession.QuestId, "本图 ");

			// 1) party DPS (cumulative damage dealt)
			rows.Add(new RowDef { Text = "── 我方 · 累计伤害(各角色DPS) ──", Color = HeaderColor, Height = 14f });
			rows.Add(new RowDef { ChartSlot = 1, Height = OverlayChart.H });
			rows.Add(new RowDef { Text = AxisLabel(viewSession), Color = DimColor, Height = 12f });
			AppendChartLegend(rows, viewSession, 0);

			// 2) party remaining HP% (only characters that took damage)
			rows.Add(new RowDef { Text = "── 我方 · 剩余耐久%(受过伤的角色)──", Color = HeaderColor, Height = 14f });
			rows.Add(new RowDef { ChartSlot = 2, Height = OverlayChart.H });
			rows.Add(new RowDef { Text = AxisLabel(viewSession), Color = DimColor, Height = 12f });
			AppendChartLegend(rows, viewSession, 1);

			// 3) enemy
			if (Plugin.CfgChartBothSides.Value)
			{
				rows.Add(new RowDef { Text = "─── 敌方 · 输出 ───", Color = EnemyColor, Height = 14f });
				rows.Add(new RowDef { ChartSlot = 3, Height = OverlayChart.H });
				rows.Add(new RowDef { Text = AxisLabel(viewSession), Color = DimColor, Height = 12f });
				bool hasEnemy = AppendChartLegend(rows, viewSession, 2);
				if (!hasEnemy)
					rows.Add(new RowDef { Text = "(敌方曲线仅含已归属单位;未归属的敌方伤害无法按单位拆分)", Color = WarnColor, Height = 14f });
			}
			return rows;
		}

		// ---- 总贡献 table page (F5) ----
		if (View == ViewMode.Contribution)
		{
			AppendContributionTable(rows);
			return rows;
		}

		// ---- roster ----
		if (!inBattle)
		{
			rows.Add(new RowDef { Text = "未在战斗中   F8 显隐  F9 重置  F10 图表  F6 明细  F5 贡献  F4 证据包", Color = HeaderColor, Height = 20f });
			rows.Add(new RowDef { Text = "下方显示上一场记录;F10 可查看上一场曲线", Color = DimColor, Height = 16f });
			if (Aggregator.History.Count > 0)
			{
				int lastQuest;
				int.TryParse(Aggregator.History[0].QuestId, out lastQuest);
				AppendBattleRefRow(rows, DisplayedRef(false), lastQuest, "上一场 ");
			}
			if (Aggregator.History.Count > 0) AppendSummaryRows(rows, Aggregator.History[0], Plugin.CfgShowEnemies.Value);
			return rows;
		}

		long allyDealt = 0, allyTaken = 0, enemyDealt = 0, allyHeal = 0, allyFriendly = 0;
		foreach (var a in session.OrderedActors)
		{
			if (CharacterInfo.IsAllyTeam(a.Team)) { allyDealt += a.DamageDealt; allyTaken += a.DamageTaken; allyHeal += a.HealingTaken; allyFriendly += a.DamageFriendly; }
			else enemyDealt += a.DamageDealt;
		}
		double secs = Math.Max(1.0, session.ActiveSeconds);
		rows.Add(new RowDef { Text = $"任务 {session.QuestId}   时间 {BattleTime.Seconds(session.ActiveSeconds)}   F8显隐 F9重置 F10图表 F6明细 F5贡献 F4证据包", Color = HeaderColor, Height = 20f });
		AppendBattleRefRow(rows, session.Ref, session.QuestId, "本场 ");
		rows.Add(new RowDef { Text = $"我方总伤害 {allyDealt:N0}   秒伤 {(long)(allyDealt / secs):N0}   受击 {allyTaken:N0}   受回复 {allyHeal:N0}", Color = NeutralColor, Height = 18f });
		if (allyFriendly > 0)
			rows.Add(new RowDef
			{
				Text = $"我方自伤/回复反噬 {allyFriendly:N0}"
					+ (Plugin.CfgFilterFriendlyFire != null && Plugin.CfgFilterFriendlyFire.Value
						? "(已从总伤害中剔除)" : "(已含在总伤害内;游戏自身也如此统计)"),
				Color = WarnColor, Height = 16f
			});
		if (Plugin.CfgShowEnemies.Value)
			rows.Add(new RowDef { Text = $"敌方总伤害 {enemyDealt:N0}   未归属(无法归一敌方) {session.UnattributedDamage:N0}   (设置可关闭敌方栏)", Color = EnemyColor, Height = 16f });

		AppendSideRows(rows, session, sideAlly: true, Plugin.CfgShowSkills.Value, secs);
		// 1.7.0 (阶段 F): the total-contribution dashboard. Placed after our rows because it is a
		// different question about the same units (credit vs damage dealt), and cadence-labelled so it
		// is never read as a per-frame number.
		AppendContributionRows(rows, session, live: true);
		if (Plugin.CfgShowEnemies.Value)
		{
			rows.Add(new RowDef { Text = "───── 敌方 ─────", Color = EnemyColor, Height = 16f });
			AppendSideRows(rows, session, sideAlly: false, false, secs);
		}
		if (session.UnattributedDamage > 0L)
			rows.Add(new RowDef { Text = $"! 未归属来源伤害 {session.UnattributedDamage:N0}(x{session.UnattributedHits}) 详见运行日志[PROBE]", Color = WarnColor, Height = 16f });
		return rows;
	}

	/// <summary>Locate the ActorStats behind a (name, team) row key. Team matters: this content can field
	/// the same character name on both sides (mirror match), so matching on the name alone would show the
	/// enemy copy's roster.</summary>
	private static ActorStats FindActor(BattleSession s, string name, int team)
	{
		try
		{
			if (s == null || name == null) return null;
			foreach (var a in s.OrderedActors)
			{
				if (a == null) continue;
				if ((int)a.Team != team) continue;
				if (string.Equals(a.Name, name, StringComparison.Ordinal)) return a;
			}
		}
		catch { }
		return null;
	}

	/// <summary>
	/// 1.1.0 detail rows: the unit's ability roster with its SOURCE SLOT, then the talent table the game
	/// counted. Both are read-only observations; anything the game could not tell us is printed as
	/// 未分类/出处未知 rather than guessed, so a missing slot is visible instead of looking authoritative.
	/// </summary>
	private static void AppendRosterRows(List<RowDef> rows, ActorStats a)
	{
		try
		{
			if (a == null) return;
			var roster = a.Roster;
			if (roster == null || roster.Count == 0) return;
			rows.Add(new RowDef
			{
				Text = "── 能力出处(游戏侧读取)──  " + AbilityRoster.Brief(roster),
				Color = HeaderColor, Height = 16f
			});
			int shown = 0;
			foreach (var r in roster)
			{
				if (shown >= 12)
				{
					rows.Add(new RowDef { Text = "  …另有 " + (roster.Count - shown) + " 条未显示", Color = DimColor, Height = 14f });
					break;
				}
				var tb = new StringBuilder();
				foreach (var t in r.Talents)
				{
					if (t.Type == 0 && t.P0 == 0) continue;
					if (tb.Length > 0) tb.Append(", ");
					tb.Append(AbilityRoster.TypeLabel(t.Type));
					if (t.P0 != 0)
					{
						tb.Append('(').Append(t.P0);
						if (t.P1 != 0) tb.Append('/').Append(t.P1);
						tb.Append(')');
					}
					if (!string.IsNullOrEmpty(t.Cond)) tb.Append('[').Append(t.Cond).Append(']');
				}
				string line = "  " + AbilityRoster.SlotLabel(r.Slot) + " #" + r.Id
					+ (r.Level > 1 ? " Lv" + r.Level : "") + " " + r.Name
					+ (r.Origin == 0 ? "(出处未知)" : "");
				AddWrapped(rows, line + (tb.Length > 0 ? "  → " + tb : ""), NeutralColor, 14f, 118);
				shown++;
			}
			var tbl = a.TalentTable;
			if (tbl == null || tbl.Count == 0) return;
			rows.Add(new RowDef { Text = "── 素质/词条发动(游戏计数)──", Color = HeaderColor, Height = 16f });
			int n = 0;
			foreach (var u in tbl)
			{
				if (n >= 14)
				{
					rows.Add(new RowDef { Text = "  …另有 " + (tbl.Count - n) + " 条", Color = DimColor, Height = 14f });
					break;
				}
				rows.Add(new RowDef
				{
					// MEASURED (battle 9999): prop and agg were identical on all 172 rows, so they are ONE
					// number -- the game's own battle-scoped activation count. delta is only the subset we
					// observed between this actor's hits (it starts at their first hit and misses activations
					// outside a damage window), so it is shown as corroboration, not as the total.
					Text = "  " + AbilityRoster.SlotLabel(u.Slot) + " " + AbilityRoster.TypeLabel(u.Type)
						+ (u.P0 != 0 ? "(" + u.P0 + ")" : "")
						+ "   本场发动 " + (u.Agg != 0 ? u.Agg : u.Prop) + " 次(逐击观测 " + u.Delta + ")"
						+ (u.Ability.Length > 0 ? "   [" + u.Ability + "]" : ""),
					Color = NeutralColor, Height = 14f
				});
				n++;
			}
		}
		catch { }
	}

	/// <summary>
	/// 1.7.0 (阶段 F): the total-contribution dashboard.
	///
	/// It shows the SAME numbers the export writes (one compute path: ContributionSession ->
	/// Contribution.Compute), refreshed at most once a second, and it is deliberately explicit about
	/// what it cannot show: with ReconcileCalc off there are no folds, so there is no attribution and
	/// the panel says 不可用 instead of printing zeros that look like measurements.
	///
	/// Layout rule: name and numbers are on separate lines with a fixed number format, and names are
	/// truncated -- a long character name must not push the panel wide (the plan's F requirement).
	/// </summary>
	private static void AppendContributionRows(List<RowDef> rows, BattleSession session, bool live)
	{
		if (Plugin.CfgShowContribution == null || !Plugin.CfgShowContribution.Value) return;
		bool useFolds = Plugin.CfgReconcileCalc != null && Plugin.CfgReconcileCalc.Value;
		// 1.7.7 (P2-A #7): route the previous-battle page through the resolver as well, or the roster
		// summary of a battle fought while the panel was hidden would still show the older one.
		ContributionView view = ResolveContributionView(useFolds);
		rows.Add(new RowDef { Text = "───── 总贡献(可加和:自身 + 他人因你)─────", Color = HeaderColor, Height = 16f });
		if (view == null || !view.Usable || view.Result == null)
		{
			rows.Add(new RowDef { Text = "  不可用 —— " + ((view == null) ? "无数据" : (view.Unavailable ?? "无数据")), Color = WarnColor, Height = 16f });
			return;
		}
		ContributionResult res = view.Result;
		double total = res.Stats.Analyzable;
		if (!live) rows.Add(new RowDef { Text = "  (上一场)", Color = DimColor, Height = 15f });
		int shown = 0;
		for (int i = 0; i < res.Actors.Count; i++)
		{
			ContributionActorRow a = res.Actors[i];
			if (a.Total <= 0.0 && a.Direct <= 0.0) continue;
			double share = total > 0.0 ? 100.0 * a.Total / total : 0.0;
			double dshare = total > 0.0 ? 100.0 * a.Direct / total : 0.0;
			// 1.7.7 rev2: Fit is width-based now, so the old character budgets (12/10/8) are passed as the
			// equivalent COLUMN budgets (24/20/16). Passing 12 here would have cut every Japanese name to
			// roughly half its previous length (peer review caught this as a visible regression).
			string label = DisplayFormat.Fit(DisplayFormat.Cell(a.Name), 24) + (a.Summon ? "[使魔]" : "");
			rows.Add(new RowDef
			{
				Text = "  " + label + "  总贡献 " + DisplayFormat.Fmt(a.Total) + "(" + DisplayFormat.Pct(share) + ")  直接输出占比 " + DisplayFormat.Pct(dshare),
				Color = AllyColor, Height = 16f,
			});
			// 1.7.11 (user request): the second line states the grouping instead of three peer-looking
			// numbers -- 自身 is the part of his OWN hits he keeps (基础 + 自身规则), and the two mirror
			// quantities 他人因你 / 被队友分走 are what make the first line readable as an identity.
			rows.Add(new RowDef
			{
				Text = "      自身 " + DisplayFormat.Fmt(a.Base + a.Self) + "(基础 " + DisplayFormat.Fmt(a.Base) + " + 自身规则 " + DisplayFormat.Fmt(a.Self) + ")   他人因你 " + DisplayFormat.Fmt(a.Assist) + "   被队友分走 " + DisplayFormat.Fmt(a.Received),
				Color = DimColor, Height = 15f,
			});
			if (++shown >= 12) break;
		}
		double unattrPct = total > 0.0 ? 100.0 * res.Stats.Unattributed / total : 0.0;
		rows.Add(new RowDef
		{
			Text = "  合计 " + DisplayFormat.Fmt(res.Stats.Attributed) + "   未归因 " + DisplayFormat.Fmt(res.Stats.Unattributed) + "(" + DisplayFormat.Pct(unattrPct) + ")   命中 " + DisplayFormat.Num(res.Stats.Hits) + "   倍率池 " + DisplayFormat.Fmt(res.Stats.PoolTotal),
			Color = NeutralColor, Height = 16f,
		});
		// R54 (user request): the families inside the residual, named -- see FallbackText.UnattributedReasonLabel.
		for (int ub = 0; ub < res.Unattributed.Count; ub++)
		{
			ContributionUnattributedRow ua = res.Unattributed[ub];
			rows.Add(new RowDef
			{
				Text = FallbackText.UnattributedBreakdownLine(ua.Reason, ua.Amount, ua.Folds),
				Color = WarnColor, Height = 15f,
			});
		}
		// R55: same pending table on the live 总贡献 rows (one model, two render sites).
		AppendPendingRows(rows, res, total);
		if (res.Rules.Count > 0)
		{
			var sb = new System.Text.StringBuilder();
			sb.Append("  规则当量:");
			int n = 0;
			for (int i = 0; i < res.Rules.Count && n < 3; i++)
			{
				ContributionRuleRow rr = res.Rules[i];
				if (rr.Damage <= 0.0) continue;
				if (n > 0) sb.Append(" · ");
				sb.Append(DisplayFormat.Fit(DisplayFormat.Cell(rr.Name), 20)).Append('(').Append(DisplayFormat.Fit(DisplayFormat.Cell(rr.OwnerName), 16)).Append(')')
				  .Append(DisplayFormat.Fmt(rr.Damage / 1000000.0)).Append("M");
				n++;
			}
			if (n > 0) rows.Add(new RowDef { Text = sb.ToString(), Color = DimColor, Height = 15f });
		}
		rows.Add(new RowDef
		{
			Text = "  (每秒最多重算一次;数字与导出 contribution 段同源;自身=基础+自身规则,自身+被队友分走=直接打出)",
			Color = DimColor, Height = 14f,
		});
	}


	private static void AppendSummaryRows(List<RowDef> rows, BattleSummary bs, bool showEnemies)
	{
		double secs = bs.DurationSeconds > 0.5 ? bs.DurationSeconds : 1.0;
		long allyDealt = 0, enemyDealt = 0, allyHeal = 0;
		foreach (var a in bs.Actors)
			if (CharacterInfo.IsAllyTeam(a.Team)) { allyDealt += a.DamageDealt; allyHeal += a.HealingTaken; }
			else enemyDealt += a.DamageDealt;
		rows.Add(new RowDef { Text = $"上一场  结果 {bs.Result}  任务 {bs.QuestId}  时长 {BattleTime.Seconds(bs.DurationSeconds)}", Color = HeaderColor, Height = 18f });
		rows.Add(new RowDef { Text = $"我方总伤害 {allyDealt:N0}   敌方总伤害 {enemyDealt:N0}   受回复 {allyHeal:N0}", Color = NeutralColor, Height = 16f });
		AppendContributionRows(rows, null, live: false);
		var allies = new List<ActorStats>();
		var foes = new List<ActorStats>();
		foreach (var a in bs.Actors)
			if (CharacterInfo.IsAllyTeam(a.Team)) allies.Add(a); else foes.Add(a);
		allies.Sort((x, y) => y.DamageDealt.CompareTo(x.DamageDealt));
		foes.Sort((x, y) => y.DamageDealt.CompareTo(x.DamageDealt));
		foreach (var a in allies)
		{
			if (a.DamageDealt == 0L && a.DamageTaken == 0L && a.HealingGiven == 0L && a.HealingTaken == 0L) continue;
			rows.Add(new RowDef { Text = Aclabel(a), Color = AllyColor, Height = 18f });
			rows.Add(new RowDef { Text = $"  伤害 {a.DamageDealt:N0}   秒伤 {DisplayFormat.Whole(a.Dps(secs))}   最大 {a.MaxHitDamage:N0}   受击 {a.DamageTaken:N0}   受回复 {a.HealingTaken:N0}", Color = NeutralColor, Height = 16f });
		}
		if (showEnemies)
		{
			rows.Add(new RowDef { Text = "───── 敌方 ─────", Color = EnemyColor, Height = 16f });
			foreach (var a in foes)
			{
				if (a.DamageDealt == 0L && a.DamageTaken == 0L && a.HealingGiven == 0L) continue;
				rows.Add(new RowDef { Text = Aclabel(a), Color = EnemyColor, Height = 18f });
				rows.Add(new RowDef { Text = $"  伤害 {a.DamageDealt:N0}   秒伤 {DisplayFormat.Whole(a.Dps(secs))}   受击 {a.DamageTaken:N0}", Color = NeutralColor, Height = 16f });
			}
		}
	}

	/// <summary>
	/// Display label for an actor. Token (使魔) units are marked, so they are not mistaken for
	/// characters now that they appear in the per-unit detail as well.
	/// </summary>
	private static string Aclabel(ActorStats a)
	{
		if (a == null) return "";
		string n = a.Name ?? "";
		return a.IsSummonMerge ? ("[使魔] " + n) : n;
	}

	private static void AppendSideRows(List<RowDef> rows, BattleSession session, bool sideAlly, bool showSkills, double secs)
	{
		var list = new List<ActorStats>();
		foreach (var a in session.OrderedActors)
			if (CharacterInfo.IsAllyTeam(a.Team) == sideAlly) list.Add(a);
		list.Sort((x, y) => y.DamageDealt.CompareTo(x.DamageDealt));
		Color nameColor = sideAlly ? AllyColor : EnemyColor;
		// in mirror content the same name can exist on both sides: tag those rows so the two
		// identical-looking entries are not read as one unit
		var dup = new HashSet<string>();
		foreach (var a in session.OrderedActors)
		{
			if (string.IsNullOrEmpty(a.Name)) continue;
			int n = 0;
			foreach (var b in session.OrderedActors)
				if (b.Name == a.Name) n++;
			if (n > 1) dup.Add(a.Name);
		}
		foreach (var a in list)
		{
			string tag = dup.Contains(a.Name) ? (sideAlly ? "  [我方]" : "  [敌方]") : "";
			rows.Add(new RowDef { Text = Aclabel(a) + tag, Color = nameColor, Height = 18f });
			rows.Add(new RowDef { Text = $"  伤害 {a.DamageDealt:N0}   秒伤 {DisplayFormat.Whole(a.Dps(secs))}   最大单次 {a.MaxHitDamage:N0}   受击 {a.DamageTaken:N0}", Color = NeutralColor, Height = 16f });
			if (a.DamageFriendly > 0L)
				rows.Add(new RowDef { Text = $"  自伤/回复反噬 {a.DamageFriendly:N0}({a.FriendlyHits} 次,已含在伤害内)", Color = WarnColor, Height = 16f });
			if (sideAlly && (a.HealingTaken != 0L || a.HealingGivenNominal != 0L || a.HealingSelf != 0L || a.HealingGiven != 0L))
				rows.Add(new RowDef { Text = $"  受回复 {a.HealingTaken:N0}(名义{a.HealingGivenNominal:N0})   自回复 {a.HealingSelf:N0}   给予 {a.HealingGiven:N0}", Color = NeutralColor, Height = 16f });
			if (showSkills && a.SkillDamage.Count > 0)
			{
				var sk = new List<KeyValuePair<int, long>>(a.SkillDamage);
				sk.Sort((x, y) => y.Value.CompareTo(x.Value));
				foreach (var kv in sk)
					rows.Add(new RowDef { Text = $"    技能#{kv.Key}  {kv.Value:N0}", Color = DimColor, Height = 16f });
			}
		}
	}

	internal class ChartSeriesItem
	{
		public ActorStats Stats;
		public bool TeamIsAlly;
		public string Name;
		public string Kind;
		public string AttrMode;
		public long Total;
		public long MaxHitDamage;
		public int HitCount;
		public UnityEngine.Color Color;
	}
}
