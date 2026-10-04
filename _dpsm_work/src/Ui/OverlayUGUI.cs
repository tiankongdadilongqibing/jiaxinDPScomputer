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
	public enum ViewMode { Roster = 0, Chart = 1, Detail = 2, Contribution = 3 }

	public static bool Visible = true;
	public static ViewMode View = ViewMode.Roster;

	private static int _detailIdx;
	private static ViewMode _viewBeforeDetail = ViewMode.Roster;

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

	/// <summary>Pinned info bar of the detail view. It lives on the PANEL (not in the scrolled
	/// content), so the character, the page and the record counts stay visible while scrolling through
	/// a long list -- previously that information sat in the first rows and scrolled out of sight.</summary>
	private static Text _pinText;
	private static RectTransform _pinRt;
	private static string _pinLine = "";
	private const float PinBarH = 18f;

	private static bool _prevLeft, _prevRight;

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
	private static bool _prevF5, _prevF6, _prevF8, _prevF9, _prevF10, _prevF11, _prevF12;
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

		if (!_scrollable) _scrollOffset = 0f;
		else
		{
			float maxOff = Math.Max(0f, _contentH - _viewH);
			if (_scrollOffset > maxOff) _scrollOffset = maxOff;
			if (_scrollOffset < 0f) _scrollOffset = 0f;
		}
		try { _contentRt.anchoredPosition = new Vector2(0f, _scrollOffset); } catch { }
	}

	private static void CheckKeys()
	{
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
			if (left) { _detailPage--; _scrollOffset = 0f; _lastRefresh = 0f; }
		}
		if (View == ViewMode.Detail && right != _prevRight)
		{
			if (right) { _detailPage++; _scrollOffset = 0f; _lastRefresh = 0f; }
		}
		if (f5 && !_prevF5)
		{
			if (View == ViewMode.Contribution) View = ViewMode.Roster;
			else { _viewBeforeDetail = View; View = ViewMode.Contribution; }
			_scrollOffset = 0f;
			_lastRefresh = 0f;
		}
		if (f6 && !_prevF6)
		{
			// F6 toggles the damage-detail view independently from F10 (roster/chart).
			if (View == ViewMode.Detail) View = _viewBeforeDetail;
			else { _viewBeforeDetail = View; View = ViewMode.Detail; }
			_detailIdx = 0;
			_detailPage = 0;
			_scrollOffset = 0f;
			_lastRefresh = 0f;
		}
		if (f7 && !_prevF7)
		{
			// F7 / Shift+F7 cycle the TARGET filter of the detail view (see _victimFilter). The actual
			// list of targets is owned by BuildRows (it depends on the selected attacker), so the step is
			// requested here and resolved there: a non-zero _filterStep is consumed by the next render.
			if (View == ViewMode.Detail)
			{
				bool shift = (GetAsyncKeyState(16) & 0x8000) != 0;
				_filterStep = shift ? -1 : 1;
				_scrollOffset = 0f;
				_lastRefresh = 0f;
			}
		}
		if (f8 && !_prevF8)
		{
			Visible = !Visible;
			if (Visible) _lastRefresh = -1f; // force an immediate rebuild when going back visible
		}
		if (f9 && !_prevF9) { Aggregator.ResetCurrent(); ContributionSession.Invalidate(); }
		if (f10 && !_prevF10)
		{
			// F10 only cycles roster <-> chart now (detail has its own key: F6)
			if (View == ViewMode.Chart) View = ViewMode.Roster;
			else View = ViewMode.Chart;
			_detailIdx = 0;
			_scrollOffset = 0f;
			_lastRefresh = 0f;
		}
		if (f11 && !_prevF11)
		{
			if (View == ViewMode.Detail)
			{
				_detailIdx++; // next character in the per-hit detail view
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
		if (f12 && !_prevF12)
		{
			if (View == ViewMode.Detail)
			{
				_detailIdx--; // previous character
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
		_prevF5 = f5; _prevF6 = f6; _prevF8 = f8; _prevF9 = f9; _prevF10 = f10; _prevF11 = f11; _prevF12 = f12;
		_prevF7 = f7;
		_prevLeft = left; _prevRight = right;
	}
}
