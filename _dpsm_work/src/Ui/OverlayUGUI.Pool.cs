using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace DpsMeter;

/// <summary>
/// uGUI plumbing: the reusable row/Text pool, the layout pass that places rows and charts, font
/// resolution, the white 1px sprite and the [UI-DIAG] heartbeat line.
/// </summary>
public static partial class OverlayUGUI
{
	// ------------------------------------------------------------------ pool/layout

	private static void EnsurePool(int n)
	{
		while (_rowTexts.Count < n)
		{
			GameObject rowGo = new GameObject("DpsMeterRow");
			rowGo.transform.SetParent(_contentRt, false);
			rowGo.AddComponent<RectTransform>();
			Text t = rowGo.AddComponent<Text>();
			t.font = GetFont();
			t.fontSize = 14;
			t.color = Color.white;
			t.alignment = TextAnchor.MiddleLeft;
			t.horizontalOverflow = HorizontalWrapMode.Overflow;
			t.verticalOverflow = VerticalWrapMode.Truncate;
			t.raycastTarget = false;
			if (!GameRef.IsNull(t.font))
			{
				try { t.material = t.font.material; } catch { }
			}
			_rowTexts.Add(t);
			_rowRts.Add(t.rectTransform);
		}
	}

	// NOTE: a second, older row-layout function used to live here. It was dead code (nothing called it;
	// the live one is OverlayUGUI.Rows.cs -> LayoutCharts, which is the one that reads View and widens
	// the panel for the detail view). Removed in 1.0.39 so there is only ONE layout path to reason about.

	/// <summary>
	/// Show/hide and place the pinned detail-view bar. It hangs off the PANEL (outside the scrolled
	/// content), because the character + page + record counts used to sit in the first rows and scrolled
	/// out of sight exactly when a long list was being read.
	/// </summary>
	private static void LayoutPinBar(float pad, float pinH)
	{
		try
		{
			if (GameRef.IsNull(_pinRt) || GameRef.IsNull(_pinText)) return;
			bool on = pinH > 0f;
			if (_pinText.gameObject.activeSelf != on) _pinText.gameObject.SetActive(on);
			if (!on) return;
			_pinRt.offsetMin = new Vector2(pad, -(pad + pinH));
			_pinRt.offsetMax = new Vector2(-pad, -pad);
			Font f = GetFont();
			if (!GameRef.IsNull(f)) _pinText.font = f;
			_pinText.text = _pinLine;
			_pinText.color = HeaderColor;
		}
		catch { }
	}

	private static void LayoutChart(RectTransform rt, RawImage img, Texture2D tex, float y, float h)
	{
		rt.anchorMin = new Vector2(0f, 1f);
		rt.anchorMax = new Vector2(0f, 1f);
		rt.pivot = new Vector2(0f, 1f);
		rt.anchoredPosition = new Vector2(0f, -y);
		rt.sizeDelta = new Vector2(_contentRt.sizeDelta.x, h);
		if (img != null && !GameRef.IsNull(tex)) img.texture = tex;
	}

	private static Font GetFont()
	{
		if (!GameRef.IsNull(_font)) return _font;
		if (_fontAttempted && Time.unscaledTime - _fontLastTry < 3f) return _font;
		_fontLastTry = Time.unscaledTime;
		_fontAttempted = true;   // 1.7.7: never reset below -- resetting it made the 3s window unusable

		try
		{
			_font = Font.CreateDynamicFontFromOSFont((Il2CppStringArray)new string[] { "Microsoft YaHei", "Yu Gothic UI", "Meiryo", "SimHei" }, 14);
			if (GameRef.IsNull(_font))
			{
				// some builds mishandle the multi-name overload; fall back to single names
				_font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 14);
			}
			if (GameRef.IsNull(_font))
			{
				_font = Font.CreateDynamicFontFromOSFont("Arial", 14);
			}
		}
		catch (Exception ex)
		{
			// 1.7.7: the flag is NOT reset (resetting it defeated the 3s window above and made every
			// refresh retry AND log). Retry still happens, once per window, silently after the first line.
			if (!_fontWarned) { _fontWarned = true; Plugin.LogSource.LogWarning($"[DpsMeter][UI] font create exception: {ex.Message}"); }
			return _font;
		}
		if (GameRef.IsNull(_font))
		{
			if (!_fontWarned) { _fontWarned = true; Plugin.LogSource.LogWarning("[DpsMeter][UI] CreateDynamicFontFromOSFont returned null for all candidates; the UI keeps the fallback font"); }
		}
		else
		{
			Plugin.LogSource.LogInfo("[DpsMeter][UI] dynamic font created OK");
		}
		return _font;
	}

	/// <summary>
	/// 1.7.6: a MONOSPACED CJK font for the 总贡献 table page.
	///
	/// WHY IT IS NEEDED. The table aligns its columns by padding with SPACES and counting a CJK
	/// character as two columns. That is exact only when the font renders a space and an ASCII digit at
	/// exactly HALF the advance of a CJK glyph. The default UI font (Microsoft YaHei) is proportional,
	/// so a row whose name is 7 CJK characters starts its numeric columns further right than a row with
	/// 4 -- measured by the user as "表头和数据对不齐". NSimSun (the fixed-pitch twin of SimSun, shipped
	/// with Windows) is a true 1:2 grid, which makes the existing arithmetic exact rather than guessed.
	/// Returns null when none is available: the caller then keeps the proportional font and the page
	/// looks exactly as it did before -- a degradation, never a break.
	/// </summary>
	private static Font GetMonoFont()
	{
		if (!GameRef.IsNull(_monoFont)) return _monoFont;
		if (_monoAttempted && Time.unscaledTime - _monoLastTry < 3f) return _monoFont;
		_monoLastTry = Time.unscaledTime;
		_monoAttempted = true;
		try
		{
			_monoFont = Font.CreateDynamicFontFromOSFont((Il2CppStringArray)new string[] { "NSimSun", "MS Gothic", "SimSun" }, 14);
			if (GameRef.IsNull(_monoFont)) _monoFont = Font.CreateDynamicFontFromOSFont("NSimSun", 14);
			if (GameRef.IsNull(_monoFont)) _monoFont = Font.CreateDynamicFontFromOSFont("MS Gothic", 14);
		}
		catch (Exception ex)
		{
			if (!_monoWarned) { _monoWarned = true; Plugin.LogSource.LogWarning($"[DpsMeter][UI] mono font create exception: {ex.Message}"); }
			return _monoFont;
		}
		if (GameRef.IsNull(_monoFont))
		{
			if (!_monoWarned) { _monoWarned = true; Plugin.LogSource.LogWarning("[DpsMeter][UI] no monospaced CJK font (NSimSun/MS Gothic/SimSun); the contribution table stays proportional (alignment cannot be guaranteed)"); }
		}
		else Plugin.LogSource.LogInfo("[DpsMeter][UI] monospaced table font created OK");
		return _monoFont;
	}

	private static void UiDiag()
	{
		if (Time.unscaledTime - _uiDiagLast < 5f) return;
		_uiDiagLast = Time.unscaledTime;
		int active = 0;
		string sample = "";
		// 1.7.9: the header alone could not show whether the panel printed the REAL unattributed value.
		// The two rows that carry it -- "  合计 ... 未归因 N ..." in the F5 contribution table and
		// "! 未归属来源伤害 N(xM)" in the F6 detail (the BattleSession summary value P2-A wired through) -- are
		// added near the END of their table, so a first-row sample can never reach them. Capturing the first
		// row that carries either marker turns "the user read the caption" into a log fact: the number can be
		// compared with the battle's totals.unattributedDamage without another battle.
		string unattr = "";
		foreach (var t in _rowTexts)
		{
			if (t != null && t.gameObject != null && t.gameObject.activeSelf)
			{
				active++;
				string s = t.text ?? "";
				if (sample.Length == 0) sample = s;
				if (unattr.Length == 0 && (s.IndexOf("未归因") >= 0 || s.IndexOf("未归属") >= 0 || s.IndexOf("不可用") >= 0)) unattr = s;
			}
		}
		string line = $"[DpsMeter][UI-DIAG] canvas={( _canvas != null && _canvas.enabled)} visible={Visible} view={View} panel={_panelRt.sizeDelta.x:F0}x{_panelRt.sizeDelta.y:F0} rowsTotal={_rowTexts.Count} rowsActive={active} hist={Aggregator.History.Count} fontNull={GameRef.IsNull(_font)} contentH={_contentH:F0} viewH={_viewH:F0} scroll={_scrollOffset:F0} firstRow='{sample}' unattrRow='{unattr}'";
		Plugin.LogSource.LogInfo(line);
		RuntimeLog.Write(line);
	}

	private static Sprite WhiteSprite()
	{
		if (!GameRef.IsNull(_white)) return _white;
		try
		{
			Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
			var px = new Il2CppStructArray<Color>(4L);
			for (int i = 0; i < 4; i++) px[i] = Color.white;
			tex.SetPixels(px);
			tex.Apply();
			_white = Sprite.Create(tex, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f));
		}
		catch { }
		return _white;
	}
}
