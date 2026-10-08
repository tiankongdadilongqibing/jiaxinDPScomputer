using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace DpsMeter;

/// <summary>
/// uGUI overlay: window lifecycle, per-frame tick, key/wheel input and the session accessor.
///
/// The overlay is one class split by responsibility over four files:
///   OverlayUGUI.cs        this file: state, Create/Tick, input, error handling
///   OverlayUGUI.Rows.cs   the row model and every row producer (roster, summary, per-skill, status)
///   OverlayUGUI.Chart.cs  damage-time chart series, legend, axis and palette
///   OverlayUGUI.Pool.cs   GameObject/Text pool, layout pass, font resolution, [UI-DIAG] line
///
/// Rendering rules that are easy to break:
///   * rows are REUSED from a pool -- never destroy and recreate per refresh;
///   * all state is static, so anything per-battle must be reset in Aggregator/StartSession path;
///   * a refresh only happens when the content actually changed (see Tick).
/// </summary>
public static partial class OverlayUGUI
{
	public enum ViewMode { Roster = 0, Chart = 1, Detail = 2, Contribution = 3, Timeline = 4, Taken = 5 }

	public static bool Visible = true;
	public static ViewMode View = ViewMode.Roster;

	private static int _detailIdx;
	private static ViewMode _viewBeforeDetail = ViewMode.Roster;

	/// <summary>R85: which side of the F6 detail view is on screen -- false = 输出明细 (the damage this unit
	/// DEALT, the original page), true = 承伤明细 (the damage this unit TOOK). F2 flips it and the detail
	/// bar's entry dispatches the SAME method, so the words on screen and the key cannot drift apart.
	/// The page, the counterparty filter and the per-hit list are all rebuilt from the selected unit's own
	/// events every render, so the flag only has to pick which side of each event is read.</summary>
	private static bool _detailTaken;

	/// <summary>R85: the unit the detail view shows, held as "name#team" rather than as a position.
	/// Both perspectives re-sort the same party list by a different quantity (dealt vs taken), so an index
	/// would silently point at a DIFFERENT unit after F2 -- exactly the failure the 受击来源拆分 page hit in
	/// R84. F2 keeps this key, so the character survives the switch; F11/F12 clear it and fall back to
	/// <see cref="_detailIdx"/>, which is what "next/previous character" means. "" = nothing picked yet.</summary>
	private static string _detailWhoKey = "";

	/// <summary>Time-window page of the F6 detail list: one page = DetailPageSeconds of battle time.
	/// ← / → move it (see CheckKeys); the list itself is no longer capped by a row count.</summary>
	private static int _detailPage;

	internal const double DetailPageSeconds = 20.0;

	/// <summary>
	/// Victim filter of the F6 detail view (F7 cycles it, Shift+F7 goes back). "" = every target.
	/// The key is "name#team"; team matters because this content fields the same character/unit NAME on
	/// both sides. Two same-named units on the SAME team (this quest has two T.O.W.E.R.typeR) are still
	/// merged -- events carry no victim pointer, and inventing one would change the export format.
	/// The list of selectable targets is rebuilt from the selected attacker's events on every render and
	/// the filter is CLAMPED to it, so switching character (F11/F12) can never leave a dead filter.
	/// </summary>
	private static string _victimFilter = "";

	/// <summary>Pending F7 step (+1 / -1), consumed by the next detail render. It lives here because the
	/// list of selectable targets is derived from the selected attacker's events inside BuildRows.</summary>
	internal static int _filterStep;

	private static bool _prevF7;

	/// <summary>R79: the 受击来源拆分 page (F3) can be narrowed to 前衛 units (Shift+F3 while that page is
	/// open). It filters the VIEW only -- the totals line keeps stating the whole battle, so the narrowed
	/// table can never be mistaken for the whole picture.</summary>
	private static bool _takenVanguardOnly;

	/// <summary>R84: which character the 受击来源拆分 page (F3) shows. It holds the victim's
	/// <see cref="TakenActor.Key"/>, not a position in the list: a live fight re-sorts that list by nominal
	/// every second, and an index would silently change WHICH character is on screen. 0 means "nothing picked
	/// yet", which the page renders as its first entry (the biggest victim).</summary>
	private static int _takenActorKey;

	/// <summary>Pinned info bar of the detail view. It lives on the PANEL (not in the scrolled
	/// content), so the character, the page and the record counts stay visible while scrolling through
	/// a long list -- previously that information sat in the first rows and scrolled out of sight.</summary>
	private static Text _pinText;
	private static RectTransform _pinRt;
	private static string _pinLine = "";
	private const float PinBarH = 18f;

	private static bool _prevLeft, _prevRight;

	/// <summary>
	/// R56 (BID-3, plan §6): the 复制引用 target. The LAYOUT pass records the rect and the payload of the
	/// row that carries the currently displayed battle's identity, so a click can only ever copy the
	/// battle the panel is showing -- never "whatever Aggregator.Session happens to be at that moment".
	/// </summary>
	private static RectTransform _copyRefRt;
	private static string _copyRefPayload;
	private static bool _lmbWasDown;
	private static float _copyFlashUntil;

	/// <summary>
	/// R82: one clickable entry of a hotkey bar. The layout pass records one of these per clickable
	/// segment (see HotkeyBarText / OverlayUGUI.Rows.LayoutCharts), and the click handler dispatches the
	/// SAME method the keyboard key dispatches -- so "F3 受击来源" on screen and the F3 key cannot drift
	/// apart. The list is rebuilt on every layout, exactly like <see cref="_copyRefRt"/>.
	/// </summary>
	private struct HotkeyHit
	{
		public RectTransform Rt;
		public Text Text;
		public Color Base;
		public HotkeyAction Action;
		/// <summary>R84: the GLYPH's argument (see HotkeySeg.Arg) -- only the 角色 list uses it, to say WHICH
		/// character the entry names. 0 for every key-named entry.</summary>
		public int Arg;
	}

	private static readonly List<HotkeyHit> _hotkeyHits = new List<HotkeyHit>();

	/// <summary>True for a couple of seconds after a successful copy, so the row can say so.</summary>
	internal static bool CopyFlashActive
	{
		get
		{
			try { return _copyFlashUntil > 0f && Time.unscaledTime < _copyFlashUntil; }
			catch { return false; }
		}
	}

	private static Canvas _canvas;
	private static RectTransform _panelRt;
	private static Image _bg;
	private static RectTransform _viewportRt;
	private static RectTransform _contentRt;
	private static readonly List<RectTransform> _rowRts = new List<RectTransform>();
	private static readonly List<Text> _rowTexts = new List<Text>();
	private static RawImage _chartImage;
	private static RectTransform _chartRt;
	private static RawImage _chartImageB;
	private static RectTransform _chartRtB;
	private static RawImage _chartImageC;
	private static RectTransform _chartRtC;
	private static Font _font;
	private static Font _assignedFont;
	/// <summary>1.7.6: monospaced CJK font (NSimSun/MS Gothic) for the 总贡献 table page only. The table
	/// aligns by padding with spaces, which is exact only on a 1:2 grid; the default UI font is
	/// proportional, so rows with longer names pushed the numeric columns right (user-reported).</summary>
	private static Font _monoFont;
	private static bool _monoAttempted;
	private static float _monoLastTry;
	private static bool _fontAttempted;
	private static float _fontLastTry;
	/// <summary>1.7.7: a failed font lookup is warned ONCE, not on every refresh (the reset of
	/// _fontAttempted/_monoAttempted used to defeat the 3s retry window and print a line per frame).</summary>
	private static bool _fontWarned;
	private static bool _monoWarned;
	private static float _lastRefresh;
	private static bool _created;
	private static int _lastFrame = -1;
	private static float _scrollOffset;
	private static bool _scrollable;
	private static float _contentH;
	private static float _viewH;
	private static float _panelW = 460f;
	private static bool _prevF2, _prevF3, _prevF5, _prevF6, _prevF8, _prevF9, _prevF10, _prevF11, _prevF12;
	/// <summary>R52: latch for the on-demand evidence-extraction key (General/ExtractKey, default F4).
	/// Polled in CheckKeys, which runs BEFORE the Visible gate, so it also works with the panel hidden.
	/// R66: that same key now opens the 技能时间表 page while the panel IS visible, and keeps writing the
	/// bundle while it is HIDDEN -- the one state in which a bundle cannot be asked for any other way.</summary>
	private static bool _prevExtract;
	private static Sprite _white;
	private static float _lastChartDraw;
	private static float _uiDiagLast;
	private static float _lastUiErrorLog;
	private static readonly Color HeaderColor = new Color(1f, 0.83f, 0.45f, 1f);
	private static readonly Color AllyColor = new Color(0.85f, 0.93f, 1f, 1f);
	private static readonly Color EnemyColor = new Color(1f, 0.72f, 0.68f, 1f);
	private static readonly Color NeutralColor = new Color(0.88f, 0.88f, 0.88f, 1f);
	private static readonly Color WarnColor = new Color(1f, 0.55f, 0.35f, 1f);
	/// <summary>Row colour for status abnormalities (kept visually distinct from the victim row).</summary>
	private static readonly Color StatusColor = new Color(0.72f, 0.85f, 1f, 1f);
	private static readonly Color DimColor = new Color(0.62f, 0.66f, 0.72f, 1f);
	/// <summary>R81: one bright colour per 受击来源拆分 sub-table. R80 stopped folding, so a victim with 300
	/// attackers makes a page hundreds of rows long; the section's label row is the only landmark, so each
	/// dimension gets a hue far from the others and from the header amber / warn orange used above.</summary>
	private static readonly Color TakenAttackerColor = new Color(0.3f, 1f, 1f, 1f);
	private static readonly Color TakenHitTypeColor = new Color(0.62f, 1f, 0.3f, 1f);
	private static readonly Color TakenAttrColor = new Color(1f, 0.35f, 0.95f, 1f);
	private static readonly Color TakenEffectColor = new Color(0.45f, 0.65f, 1f, 1f);
	private static readonly Color TakenStatusColor = new Color(1f, 0.5f, 0.75f, 1f);
	private static readonly Color TakenOtherColor = new Color(1f, 0.35f, 0.35f, 1f);

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int vKey);

	public static bool Create()
	{
		try
		{
			GameObject go = new GameObject("DpsMeterCanvas");
			UnityEngine.Object.DontDestroyOnLoad(go);
			_canvas = go.AddComponent<Canvas>();
			_canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			_canvas.sortingOrder = 9999;
			CanvasScaler scaler = go.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

			GameObject panelGo = new GameObject("DpsMeterPanel");
			panelGo.transform.SetParent(go.transform, false);
			_bg = panelGo.AddComponent<Image>();
			_bg.sprite = WhiteSprite();
			_bg.color = new Color(0f, 0f, 0f, 0.66f);
			_bg.raycastTarget = false;
			_panelRt = _bg.rectTransform;
			// bottom-centered, slightly above the bottom edge so it does not block gameplay
			_panelRt.anchorMin = new Vector2(0.5f, 0f);
			_panelRt.anchorMax = new Vector2(0.5f, 0f);
			_panelRt.pivot = new Vector2(0.5f, 0f);
			_panelRt.anchoredPosition = new Vector2(0f, 12f);

			GameObject vpGo = new GameObject("DpsMeterViewport");
			vpGo.transform.SetParent(panelGo.transform, false);
			_viewportRt = vpGo.AddComponent<RectTransform>();
			_viewportRt.anchorMin = new Vector2(0f, 1f);
			_viewportRt.anchorMax = new Vector2(1f, 1f);
			_viewportRt.pivot = new Vector2(0f, 1f);
			// NOTE: no RectMask2D. It was culling the row text. We clip by clamping scroll and
			// sizing the panel background to cover content up to the screen edge instead.

			GameObject contentGo = new GameObject("DpsMeterContent");
			contentGo.transform.SetParent(vpGo.transform, false);
			_contentRt = contentGo.AddComponent<RectTransform>();
			_contentRt.anchorMin = new Vector2(0f, 1f);
			_contentRt.anchorMax = new Vector2(0f, 1f);
			_contentRt.pivot = new Vector2(0f, 1f);

			// pinned info bar (detail view): child of the PANEL, so it does not scroll with the rows
			GameObject pinGo = new GameObject("DpsMeterPinBar");
			pinGo.transform.SetParent(panelGo.transform, false);
			_pinRt = pinGo.AddComponent<RectTransform>();
			_pinRt.anchorMin = new Vector2(0f, 1f);
			_pinRt.anchorMax = new Vector2(1f, 1f);
			_pinRt.pivot = new Vector2(0f, 1f);
			_pinText = pinGo.AddComponent<Text>();
			_pinText.font = GetFont();
			_pinText.fontSize = 14;
			_pinText.color = HeaderColor;
			_pinText.alignment = TextAnchor.MiddleLeft;
			_pinText.horizontalOverflow = HorizontalWrapMode.Overflow;
			_pinText.verticalOverflow = VerticalWrapMode.Truncate;
			_pinText.raycastTarget = false;
			pinGo.SetActive(false);

			GameObject chartGo = new GameObject("DpsMeterChartImage");
			chartGo.transform.SetParent(contentGo.transform, false);
			_chartRt = chartGo.AddComponent<RectTransform>();
			_chartImage = chartGo.AddComponent<RawImage>();
			_chartImage.color = Color.white;
			_chartImage.raycastTarget = false;
			chartGo.SetActive(false);

			GameObject chartGoB = new GameObject("DpsMeterChartImageB");
			chartGoB.transform.SetParent(contentGo.transform, false);
			_chartRtB = chartGoB.AddComponent<RectTransform>();
			_chartImageB = chartGoB.AddComponent<RawImage>();
			_chartImageB.color = Color.white;
			_chartImageB.raycastTarget = false;
			chartGoB.SetActive(false);

			GameObject chartGoC = new GameObject("DpsMeterChartImageC");
			chartGoC.transform.SetParent(contentGo.transform, false);
			_chartRtC = chartGoC.AddComponent<RectTransform>();
			_chartImageC = chartGoC.AddComponent<RawImage>();
			_chartImageC.color = Color.white;
			_chartImageC.raycastTarget = false;
			chartGoC.SetActive(false);

			_created = true;
			OverlayChart.UsePerSecond = Plugin.CfgChartPerSecond.Value;
			Plugin.LogSource.LogInfo("[DpsMeter] uGUI canvas created (fixed panel + clipped scroll content).");
			return true;
		}
		catch (Exception ex)
		{
			Plugin.LogSource.LogError($"[DpsMeter] uGUI canvas create failed: {ex}");
			_created = false;
			return false;
		}
	}

	private static void UiError(string where, Exception ex)
	{
		string msg = $"[DpsMeter][UI-ERR] {where}: {ex.GetType().Name}: {ex.Message}";
		if (Time.unscaledTime - _lastUiErrorLog > 5f)
		{
			_lastUiErrorLog = Time.unscaledTime;
			Plugin.LogSource.LogWarning(msg);
			RuntimeLog.Write(msg);
		}
	}

	public static void Tick()
	{
		if (!_created) return;
		try
		{
			// GameSystemTickHook and InputManagerTickHook both call Tick; process once per frame
			if (Time.frameCount == _lastFrame) return;
			_lastFrame = Time.frameCount;
			CheckKeys();
			if (!Visible)
			{
				_canvas.enabled = false;
				return;
			}
			_canvas.enabled = true;
			HandleScroll();
			CheckMouseClick();
			UpdateHotkeyHover();
			if (Time.unscaledTime - _lastRefresh >= 0.25f)
			{
				_lastRefresh = Time.unscaledTime;
				try
				{
					Refresh();
				}
				catch (Exception ex)
				{
					UiError("Refresh", ex);
				}
			}
			if (View == ViewMode.Chart && Time.unscaledTime - _lastChartDraw >= 0.5f)
			{
				_lastChartDraw = Time.unscaledTime;
				try
				{
					OverlayChart.Draw(SessionForView());
				}
				catch (Exception ex)
				{
					UiError("ChartDraw", ex);
				}
			}
			UiDiag();
		}
		catch (Exception ex)
		{
			UiError("Tick", ex);
		}
	}

	internal static void LogSessionStart(int questId)
	{
		try
		{
			string line = $"[DpsMeter][UI] session start quest={questId} canvas={( _canvas != null && _canvas.enabled)} rowsTotal={_rowTexts.Count} fontNull={GameRef.IsNull(_font)} visible={Visible}";
			Plugin.LogSource.LogInfo(line);
			RuntimeLog.Write(line);
		}
		catch { }
	}

	internal static BattleSession SessionForView()
	{
		BattleSession s = Aggregator.Session;
		if (s != null && s.InBattle) return s;
		// One factory for every view of a finished battle (roster summary, F6 detail, chart), so the
		// main page and the detail list can never end up reading different data for the same battle.
		if (Aggregator.History.Count > 0) return BattleSession.FromSummary(Aggregator.History[0]);
		return null;
	}

	private static bool PointerOverPanel()
	{
		try
		{
			Vector3 mp = Input.mousePosition;
			Vector2 lp;
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_panelRt, mp, null, out lp)) return false;
			Rect r = _panelRt.rect;
			return lp.x >= r.x && lp.x <= r.xMax && lp.y >= r.y && lp.y <= r.yMax;
		}
		catch { return true; }
	}

	private static void HandleScroll()
	{
		if (GameRef.IsNull(_contentRt)) return;
		try
		{
			bool wheelAllowed = Plugin.CfgWheelScrolls.Value && PointerOverPanel();
			float y = Input.mouseScrollDelta.y;
			// wheel up (y>0) scrolls back toward the top; wheel down reveals later rows
			if (wheelAllowed && y != 0f) _scrollOffset += y * -60f;
		}
		catch { }
		float num = 1f;
		try { num = Time.unscaledDeltaTime; } catch { }
		if (((uint)GetAsyncKeyState(33) & 0x8000u) != 0) _scrollOffset -= 900f * num;   // PageUp -> top
		if (((uint)GetAsyncKeyState(34) & 0x8000u) != 0) _scrollOffset += 900f * num;   // PageDown -> later
		if (((uint)GetAsyncKeyState(38) & 0x8000u) != 0) _scrollOffset -= 360f * num;   // Up
		if (((uint)GetAsyncKeyState(40) & 0x8000u) != 0) _scrollOffset += 360f * num;   // Down
		// R80: the 受击来源拆分 page can be thousands of rows long, so walking to its ends with the arrow
		// keys is not a usable way to reach them. Home/End jump straight to the first/last row; the clamp
		// below turns End's MaxValue into the real bottom offset, so neither can scroll past the content.
		if (((uint)GetAsyncKeyState(36) & 0x8000u) != 0) _scrollOffset = 0f;            // Home -> first row
		if (((uint)GetAsyncKeyState(35) & 0x8000u) != 0) _scrollOffset = float.MaxValue; // End -> last row

		if (!_scrollable) _scrollOffset = 0f;
		else
		{
			float maxOff = Math.Max(0f, _contentH - _viewH);
			if (_scrollOffset > maxOff) _scrollOffset = maxOff;
			if (_scrollOffset < 0f) _scrollOffset = 0f;
		}
		try { _contentRt.anchoredPosition = new Vector2(0f, _scrollOffset); } catch { }
	}

	/// <summary>
	/// R56 (BID-3): the 复制引用 click. A real uGUI Button needs an EventSystem in the game's scene, and
	/// this overlay deliberately owns nothing in that scene; the click is therefore detected exactly like
	/// the mouse wheel already is (GetAsyncKeyState + a rectangle test), which adds no component and
	/// cannot fight the game for input.
	///
	/// R82: the same edge now feeds the hotkey bars FIRST (the button-like entries the panel prints), and
	/// only falls through to the battle-reference row when no entry was hit. Order matters: the entries
	/// and that row can never overlap, but testing the smaller targets first keeps a click on a bar from
	/// ever being read as a copy.
	/// </summary>
	private static void CheckMouseClick()
	{
		try
		{
			bool down = (GetAsyncKeyState(1) & 0x8000) != 0;   // VK_LBUTTON
			bool pressed = down && !_lmbWasDown;
			_lmbWasDown = down;
			if (!pressed) return;
			if (TryHotkeyClick()) return;
			if (GameRef.IsNull(_copyRefRt) || string.IsNullOrEmpty(_copyRefPayload)) return;
			if (!RectTransformUtility.RectangleContainsScreenPoint(_copyRefRt, Input.mousePosition, null)) return;
			CopyToClipboard(_copyRefPayload);
		}
		catch { }
	}

	/// <summary>R82: fire the hotkey entry under the pointer, if any. Returns true when a click was
	/// consumed (the caller then skips the copy target).</summary>
	private static bool TryHotkeyClick()
	{
		try
		{
			Vector3 mp = Input.mousePosition;
			for (int i = 0; i < _hotkeyHits.Count; i++)
			{
				HotkeyHit hit = _hotkeyHits[i];
				if (GameRef.IsNull(hit.Rt)) continue;
				if (!RectTransformUtility.RectangleContainsScreenPoint(hit.Rt, mp, null)) continue;
				DispatchHotkey(hit.Action, hit.Arg);
				return true;
			}
		}
		catch (Exception ex) { UiError("HotkeyClick", ex); }
		return false;
	}

	/// <summary>R82: brighten the entry under the pointer so "this is clickable" is visible. The layout
	/// re-applies the base colour on every refresh, so this only has to correct the previous frame's
	/// highlight.</summary>
	private static void UpdateHotkeyHover()
	{
		try
		{
			Vector3 mp = Input.mousePosition;
			int found = -1;
			for (int i = 0; i < _hotkeyHits.Count; i++)
			{
				RectTransform rt = _hotkeyHits[i].Rt;
				if (GameRef.IsNull(rt)) continue;
				if (RectTransformUtility.RectangleContainsScreenPoint(rt, mp, null)) { found = i; break; }
			}
			for (int i = 0; i < _hotkeyHits.Count; i++)
			{
				HotkeyHit hit = _hotkeyHits[i];
				if (GameRef.IsNull(hit.Text)) continue;
				Color want = (i == found) ? Color.Lerp(hit.Base, Color.white, 0.45f) : hit.Base;
				if (hit.Text.color != want) hit.Text.color = want;
			}
		}
		catch { }
	}

	/// <summary>
	/// R82: what a click on a hotkey entry does. Every arm calls the same method the keyboard key calls
	/// (see CheckKeys) -- the enum is named after keys precisely so this stays a one-line mapping.
	/// Home/End move the scroll offset the way HandleScroll's own Home/End keys do (level-triggered
	/// there; a click is already one edge, so the same assignments are made once here).
	/// R84: <paramref name="arg"/> carries the entry's <see cref="HotkeySeg.Arg"/> (which character a 角色
	/// list entry names). It is 0 for the key-named entries, which is also what they mean.
	/// </summary>
	private static void DispatchHotkey(HotkeyAction action, int arg)
	{
		try
		{
			switch (action)
			{
				case HotkeyAction.KeyF2: ActF2(); break;
				case HotkeyAction.KeyF3: ActF3(false); break;
				case HotkeyAction.KeyF3Shift: ActF3(true); break;
				case HotkeyAction.KeyF4: if (Visible) ActTimeline(); break;
				case HotkeyAction.KeyF5: ActF5(); break;
				case HotkeyAction.KeyF6: ActF6(); break;
				case HotkeyAction.KeyF7: ActF7(false); break;
				case HotkeyAction.KeyF7Shift: ActF7(true); break;
				case HotkeyAction.KeyF8: ActF8(); break;
				case HotkeyAction.KeyF9: ActF9(); break;
				case HotkeyAction.KeyF10: ActF10(); break;
				case HotkeyAction.KeyF11: ActF11(); break;
				case HotkeyAction.KeyF12: ActF12(); break;
				case HotkeyAction.KeyHome: _scrollOffset = 0f; break;
				case HotkeyAction.KeyEnd: _scrollOffset = float.MaxValue; break;   // clamped by HandleScroll
				case HotkeyAction.KeyLeft: ActDetailPage(-1); break;
				case HotkeyAction.KeyRight: ActDetailPage(1); break;
				case HotkeyAction.TakenActor: ActTakenActor(arg); break;
			}
		}
		catch (Exception ex) { UiError("HotkeyDispatch", ex); }
	}

	/// <summary>The one place text reaches the clipboard. A failure is LOGGED and the row keeps printing
	/// the full id, which is the documented manual fallback (plan §6) -- a click must never look like it
	/// worked when nothing was copied.</summary>
	internal static bool CopyToClipboard(string text)
	{
		if (string.IsNullOrEmpty(text)) return false;
		try
		{
			GUIUtility.systemCopyBuffer = text;
			try { _copyFlashUntil = Time.unscaledTime + 2.5f; } catch { }
			string ok = "[DpsMeter][BREF] 已复制战斗引用(" + text.Length + " 字符)";
			Plugin.LogSource.LogInfo(ok);
			RuntimeLog.Write(ok);
			return true;
		}
		catch (Exception ex)
		{
			string bad = "[DpsMeter][BREF] 复制失败,可手动抄写完整编号: " + ex.Message;
			Plugin.LogSource.LogWarning(bad);
			RuntimeLog.Write(bad);
			return false;
		}
	}

	/// <summary>
	/// R82: the panel's own actions, one method per key. CheckKeys calls these from the keyboard, and
	/// DispatchHotkey calls the SAME methods from a click on the bar's entry -- this is what keeps
	/// "the words on screen" and "what the key does" from ever becoming two different behaviours.
	/// The bodies are exactly the bodies the key handlers had before R82.
	/// </summary>
	/// <summary>R85: F2 flips the F6 detail view between 输出明细 (what our units dealt) and 承伤明细 (what
	/// our units took). It works from any view -- the flag is a property of the detail page, not of the
	/// moment the key is pressed -- so a reader can pick the perspective first and open the page after.
	/// The selected unit is kept (see <see cref="_detailWhoKey"/>); the page and the counterparty filter
	/// are reset because both are derived from a different side of the same events.</summary>
	internal static void ActF2()
	{
		_detailTaken = !_detailTaken;
		_detailPage = 0;
		_victimFilter = "";
		_scrollOffset = 0f;
		_lastRefresh = 0f;
	}

	internal static void ActF3(bool shift)
	{
		// Same remember/restore shape as F5/F6: leaving the page returns to the view it was opened from.
		// R79: Shift+F3 narrows the 受击来源拆分 table to 前衛 (the page title says so) instead of
		// leaving the page -- the filter is only meaningful while that page is open.
		if (View == ViewMode.Taken)
		{
			if (shift) _takenVanguardOnly = !_takenVanguardOnly;
			else View = _viewBeforeDetail;
		}
		// R84: entering the page always starts on its first entry (the biggest victim). Flipping the 前衛
		// filter does NOT clear the selection: the chosen character is remembered by key, so it stays on
		// screen whenever the filter still contains it, and the page falls back to the first entry when it
		// does not.
		else { _takenActorKey = 0; _viewBeforeDetail = View; View = ViewMode.Taken; }
		_scrollOffset = 0f;
		_lastRefresh = 0f;
	}

	/// <summary>R84: show the character the reader clicked in the 角色 list. <paramref name="index"/> is a
	/// position in that list as it was rendered; it is resolved against the CURRENT list (rebuilt from the
	/// same helper the page used), so a click on a stale layout can never select a different character than
	/// the one under the pointer -- out-of-range just does nothing.</summary>
	internal static void ActTakenActor(int index)
	{
		try
		{
			if (index < 0) return;
			List<TakenActor> mine = TakenPageText.Victims(TakenSession.Get(), _takenVanguardOnly);
			if (index >= mine.Count) return;
			_takenActorKey = mine[index].Key;
			_scrollOffset = 0f;
			_lastRefresh = 0f;
		}
		catch (Exception ex) { UiError("TakenActor", ex); }
	}

	internal static void ActF5()
	{
		if (View == ViewMode.Contribution) View = ViewMode.Roster;
		else { _viewBeforeDetail = View; View = ViewMode.Contribution; }
		_scrollOffset = 0f;
		_lastRefresh = 0f;
	}

	internal static void ActF6()
	{
		// F6 toggles the damage-detail view independently from F10 (roster/chart).
		if (View == ViewMode.Detail) View = _viewBeforeDetail;
		else { _viewBeforeDetail = View; View = ViewMode.Detail; }
		_detailIdx = 0;
		_detailWhoKey = "";
		_detailPage = 0;
		_scrollOffset = 0f;
		_lastRefresh = 0f;
	}

	internal static void ActF7(bool back)
	{
		// F7 / Shift+F7 cycle the TARGET filter of the detail view (see _victimFilter). The actual
		// list of targets is owned by BuildRows (it depends on the selected attacker), so the step is
		// requested here and resolved there: a non-zero _filterStep is consumed by the next render.
		if (View != ViewMode.Detail) return;
		_filterStep = back ? -1 : 1;
		_scrollOffset = 0f;
		_lastRefresh = 0f;
	}

	internal static void ActF8()
	{
		Visible = !Visible;
		if (Visible) _lastRefresh = -1f; // force an immediate rebuild when going back visible
	}

	internal static void ActF9()
	{
		Aggregator.ResetCurrent();
		ContributionSession.Invalidate();
		TakenSession.Invalidate();
	}

	internal static void ActF10()
	{
		// F10 only cycles roster <-> chart now (detail has its own key: F6)
		if (View == ViewMode.Chart) View = ViewMode.Roster;
		else View = ViewMode.Chart;
		_detailIdx = 0;
		_scrollOffset = 0f;
		_lastRefresh = 0f;
	}

	internal static void ActF11()
	{
		if (View == ViewMode.Detail)
		{
			_detailIdx++; // next character in the per-hit detail view
			_detailWhoKey = "";   // R85: the step is an INDEX move, so it must not be re-pinned by key
			_detailPage = 0;
			_scrollOffset = 0f;
			_lastRefresh = 0f;
		}
		else
		{
			// F11 only affects the chart (party-only vs both sides); the roster always keeps
			// its own ShowEnemies setting so switching views can never lose enemy rows again.
			Plugin.CfgChartBothSides.Value = !Plugin.CfgChartBothSides.Value;
			RuntimeLog.Write(Plugin.CfgChartBothSides.Value ? "[DpsMeter] Chart both sides" : "[DpsMeter] Chart party only");
			_lastRefresh = 0f;
		}
	}

	internal static void ActF12()
	{
		if (View == ViewMode.Detail)
		{
			_detailIdx--; // previous character
			_detailWhoKey = "";   // R85: same as F11 -- an index move, not a key re-pin
			_detailPage = 0;
			_scrollOffset = 0f;
			_lastRefresh = 0f;
		}
		else
		{
			OverlayChart.UsePerSecond = !OverlayChart.UsePerSecond;
			Plugin.CfgChartPerSecond.Value = OverlayChart.UsePerSecond;
			RuntimeLog.Write(OverlayChart.UsePerSecond ? "[DpsMeter] Chart per-second DPS" : "[DpsMeter] Chart cumulative");
			_lastRefresh = 0f;
		}
	}

	/// <summary>The 技能时间表 page (F4 while the panel is visible): toggles it like the other pages.</summary>
	internal static void ActTimeline()
	{
		if (View == ViewMode.Timeline) View = _viewBeforeDetail;
		else { _viewBeforeDetail = View; View = ViewMode.Timeline; }
		_scrollOffset = 0f;
		_lastRefresh = 0f;
	}

	/// <summary>← / → page the F6 per-hit list by battle time; only meaningful in the detail view.</summary>
	internal static void ActDetailPage(int delta)
	{
		if (View != ViewMode.Detail) return;
		_detailPage += delta;
		_scrollOffset = 0f;
		_lastRefresh = 0f;
	}

	private static void CheckKeys()
	{
		// R85: F2 flips the F6 detail view between 输出明细 (dealt) and 承伤明细 (taken). VK_F2 = 113; it is
		// the only free function key (F1 is left alone, F3-F12 all name other pages/actions).
		bool f2 = (GetAsyncKeyState(113) & 0x8000) != 0;
		// R79: F3 opens the 受击来源拆分 page (per-unit INCOMING damage by source). F5-F12 are taken by the
		// other pages and F4 is the user's own extract key (General/ExtractKey), so F3 is the next free one.
		bool f3 = (GetAsyncKeyState(114) & 0x8000) != 0;
		// 1.7.0 (阶段 F): F5 opens the dedicated 总贡献 table page (F6 detail, F10 chart keep theirs).
		bool f5 = (GetAsyncKeyState(116) & 0x8000) != 0;
		bool f6 = (GetAsyncKeyState(117) & 0x8000) != 0;
		bool f7 = (GetAsyncKeyState(118) & 0x8000) != 0;
		bool f8 = (GetAsyncKeyState(119) & 0x8000) != 0;
		bool f9 = (GetAsyncKeyState(120) & 0x8000) != 0;
		bool f10 = (GetAsyncKeyState(121) & 0x8000) != 0;
		bool f11 = (GetAsyncKeyState(122) & 0x8000) != 0;
		bool f12 = (GetAsyncKeyState(123) & 0x8000) != 0;
		// ← / → page the F6 per-hit list by battle time (20 s per page). Only in the detail view --
		// elsewhere the wheel / PageUp / PageDown / ↑ / ↓ keep scrolling (see HandleScroll).
		bool left = (GetAsyncKeyState(37) & 0x8000) != 0;
		bool right = (GetAsyncKeyState(39) & 0x8000) != 0;
		if (View == ViewMode.Detail && left != _prevLeft)
		{
			if (left) ActDetailPage(-1);
		}
		if (View == ViewMode.Detail && right != _prevRight)
		{
			if (right) ActDetailPage(1);
		}
		if (f2 && !_prevF2) ActF2();
		if (f3 && !_prevF3)
		{
			bool shift3 = (GetAsyncKeyState(16) & 0x8000) != 0;
			ActF3(shift3);
		}
		if (f5 && !_prevF5) ActF5();
		if (f6 && !_prevF6) ActF6();
		if (f7 && !_prevF7)
		{
			bool shift = (GetAsyncKeyState(16) & 0x8000) != 0;
			ActF7(shift);
		}
		if (f8 && !_prevF8) ActF8();
		if (f9 && !_prevF9) ActF9();
		if (f10 && !_prevF10) ActF10();
		if (f11 && !_prevF11) ActF11();
		if (f12 && !_prevF12) ActF12();
		// R52 + R66: the configurable key (General/ExtractKey, default F4). WHICH ACTION it performs depends
		// on whether the panel is on screen, and that is the whole point of the split: while the panel is
		// visible the key is one of the panel's own view keys (技能时间表, like F5/F6/F10), and while the
		// panel is hidden the key still writes an evidence bundle -- the battle-end bundle is automatic, but
		// a bundle requested on the spot is only reachable from a key, and a hidden panel is exactly when no
		// other route exists. The hotkey bar states which meaning is live.
		bool extractDown = false;
		try
		{
			int vk = ExtractPolicy.ParseVirtualKey(Plugin.CfgExtractKey == null ? null : Plugin.CfgExtractKey.Value);
			extractDown = vk > 0 && (GetAsyncKeyState(vk) & 0x8000) != 0;
		}
		catch { }
		if (extractDown && !_prevExtract)
		{
			if (Visible)
			{
				// same remember/restore shape as F6: leaving the page returns to the view it was opened from
				ActTimeline();
			}
			else
			{
				try
				{
					string dir = EvidenceExtractor.Run(Aggregator.Session, "hotkey");
					RuntimeLog.Write(dir == null
						? "[DpsMeter] 证据提取:没有可提取的会话"
						: ("[DpsMeter] 证据提取 -> " + dir));
				}
				catch (Exception ex3) { RuntimeLog.Write("[DpsMeter] 证据提取失败: " + ex3.Message); }
			}
		}
		_prevF2 = f2;
		_prevF3 = f3;
		_prevF5 = f5; _prevF6 = f6; _prevF8 = f8; _prevF9 = f9; _prevF10 = f10; _prevF11 = f11; _prevF12 = f12;
		_prevF7 = f7;
		_prevExtract = extractDown;
		_prevLeft = left; _prevRight = right;
	}
}
