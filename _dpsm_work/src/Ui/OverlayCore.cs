using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace DpsMeter;

/// <summary>
/// IMGUI fallback overlay (OverlayMode=imgui). Dragging clamps the window to the
/// screen and wheel scrolling happens only inside the scroll view. Chart view (F10)
/// is only available in the uGUI renderer.
/// </summary>
public static class OverlayCore
{
	public static bool Visible = true;
	public static bool Minimized;
	public static bool ShowHistory;
	public static Rect WindowRect = new Rect(40f, 120f, 460f, 60f);
	public static Font UiFont;

	private static bool _fontAttempted;
	/// <summary>1.7.7 (P2-A #9, residual): the imgui font used to be looked up exactly ONCE and a
	/// failure was swallowed, so a font that appeared later was never picked up and nothing was logged.
	/// Keep the same 3 s throttle the uGUI path uses, and warn once.</summary>
	private static float _fontLastTry;
	private static bool _fontWarned;
	private static bool _dragging;
	private static float _lastSave;
	private static string _uiPath;
	private static Vector2 _scroll;
	private static float _desiredHeight = 80f;

	public static void Awake()
	{
		_uiPath = Path.Combine(Paths.ConfigPath, "dpsmeter_ui.txt");
		LoadRect();
	}

	public static void Update()
	{
		try { Aggregator.Tick(); } catch { }
	}

	public static void OnDestroy()
	{
		SaveRect();
	}

	public static void OnGUI()
	{
		Event current = Event.current;
		if (current != null && current.type == EventType.KeyDown)
		{
			if (current.keyCode == KeyCode.F8) Visible = !Visible;
			else if (current.keyCode == KeyCode.F9) { Aggregator.ResetCurrent(); ContributionSession.Invalidate(); TakenSession.Invalidate(); }
		}
		if (!Visible) return;

		if (!GameRef.IsNull(UiFont))
			GUI.skin.font = UiFont;
		else if (!_fontAttempted || Time.unscaledTime - _fontLastTry >= 3f)
		{
			_fontLastTry = Time.unscaledTime;
			_fontAttempted = true;
			TryInitFont();
			if (!GameRef.IsNull(UiFont))
				GUI.skin.font = UiFont;
		}

		_desiredHeight = 80f;
		GUI.Box(WindowRect, "");
		GUILayout.BeginArea(new Rect(WindowRect.x + 4f, WindowRect.y + 4f, WindowRect.width - 8f, WindowRect.height - 8f));
		DrawContent();
		GUILayout.EndArea();

		WindowRect.height = Mathf.Clamp(_desiredHeight, 60f, 600f);
		ClampToScreen();
		HandleDrag(current);
		if (Time.unscaledTime - _lastSave > 5f)
		{
			_lastSave = Time.unscaledTime;
			SaveRect();
		}
	}

	private static void ClampToScreen()
	{
		float w = Math.Min(WindowRect.width, Screen.width - 8f);
		WindowRect.width = w;
		if (WindowRect.x < 0f) WindowRect.x = 0f;
		if (WindowRect.y < 0f) WindowRect.y = 0f;
		if (WindowRect.x + w > Screen.width) WindowRect.x = Math.Max(0f, Screen.width - w);
		if (WindowRect.y + WindowRect.height > Screen.height) WindowRect.y = Math.Max(0f, Screen.height - WindowRect.height);
	}

	private static void DrawContent()
	{
		GUILayout.BeginHorizontal();
		if (GUILayout.Button(Minimized ? "+" : "-", GUILayout.Width(22f))) Minimized = !Minimized;
		if (GUILayout.Button("Hist", GUILayout.Width(44f))) ShowHistory = !ShowHistory;
		if (GUILayout.Button("Enemy", GUILayout.Width(52f))) Plugin.CfgShowEnemies.Value = !Plugin.CfgShowEnemies.Value;
		if (GUILayout.Button("Skill", GUILayout.Width(48f))) Plugin.CfgShowSkills.Value = !Plugin.CfgShowSkills.Value;
		if (GUILayout.Button("Reset", GUILayout.Width(52f))) Aggregator.ResetCurrent();
		if (GUILayout.Button("X", GUILayout.Width(22f))) Visible = false;
		GUILayout.EndHorizontal();

		if (Minimized)
		{
			GUILayout.Label("DpsMeter (uGUI renderer recommended for charts; F10 chart is uGUI-only)");
			_desiredHeight = 64f;
			return;
		}

		if (ShowHistory)
		{
			DrawHistory();
			return;
		}

		BattleSession session = Aggregator.Session;
		if (session == null || !session.InBattle)
		{
			GUILayout.Label("未在战斗中  F8 显示/隐藏  F9 重置  F4 证据包");
			_desiredHeight = 64f;
			if (Aggregator.History.Count > 0)
			{
				int lastQuest;
				int.TryParse(Aggregator.History[0].QuestId, out lastQuest);
				DrawBattleRef(Aggregator.History[0].Ref, lastQuest, "上一场 ");
			}
			if (Aggregator.History.Count > 0) DrawHistoryMini(Aggregator.History[0]);
			return;
		}

		double secs = Math.Max(1.0, session.ActiveSeconds);
		long allyDealt = 0, allyTaken = 0, enemyDealt = 0;
		foreach (var a in session.OrderedActors)
		{
			if (CharacterInfo.IsAllyTeam(a.Team)) { allyDealt += a.DamageDealt; allyTaken += a.DamageTaken; }
			else enemyDealt += a.DamageDealt;
		}
		GUILayout.Label($"任务 {session.QuestId}  {BattleTime.Hit(secs)}  我方伤害 {DisplayFormat.Num(allyDealt)}  秒伤 {DisplayFormat.Num((long)(allyDealt / secs))}  受击 {DisplayFormat.Num(allyTaken)}  敌伤害 {DisplayFormat.Num(enemyDealt)}");
		DrawBattleRef(session.Ref, session.QuestId, "本场 ");

		var allies = new List<ActorStats>();
		foreach (var a in session.OrderedActors)
			if (CharacterInfo.IsAllyTeam(a.Team)) allies.Add(a);
		allies.Sort((x, y) => y.DamageDealt.CompareTo(x.DamageDealt));

		float extra = 0f;
		if (Plugin.CfgShowSkills.Value)
			foreach (var a in allies) extra += a.SkillDamage.Count * 18f;

		var foes = new List<ActorStats>();
		if (Plugin.CfgShowEnemies.Value)
		{
			foreach (var a in session.OrderedActors)
				if (!CharacterInfo.IsAllyTeam(a.Team)) foes.Add(a);
			foes.Sort((x, y) => y.DamageDealt.CompareTo(x.DamageDealt));
		}

		_desiredHeight = 92f + allies.Count * 20f + extra + foes.Count * 20f;
		_scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(Mathf.Clamp(_desiredHeight - 66f, 30f, 520f)));
		foreach (var a in allies) DrawActorRow(a, secs, Plugin.CfgShowSkills.Value);
		if (foes.Count > 0)
		{
			GUILayout.Label("───── 敌方 ─────");
			foreach (var a in foes) DrawActorRow(a, secs, false);
		}
		// 1.7.0 (阶段 F): the same total-contribution dashboard as the uGUI renderer (one data path).
		if (Plugin.CfgShowContribution == null || Plugin.CfgShowContribution.Value)
			DrawContributionDashboard();
		if (session.UnattributedDamage > 0L)
			GUILayout.Label($"⚠ 未归属来源伤害 {DisplayFormat.Num(session.UnattributedDamage)} x{session.UnattributedHits} (见运行日志 [PROBE])");
		GUILayout.EndScrollView();
	}

	/// <summary>1.7.0 (阶段 F): total-contribution dashboard for the IMGUI fallback renderer.
	/// It reads the SAME cached view the uGUI renderer uses, so both show one set of numbers, and an
	/// unavailable computation prints the reason instead of zeros.</summary>
	/// <summary>
	/// R56 (BID-3, plan §6): the IMGUI fallback's identity line. It prints the FULL id as text (so the
	/// number can always be transcribed by hand) and offers the same copy action as the uGUI row through
	/// the same function, so the two renderers cannot produce different payloads.
	/// </summary>
	private static void DrawBattleRef(BattleRef r, int quest, string label)
	{
		if (r == null)
		{
			GUILayout.Label("  " + label + "无编号(legacy):该场按文件内容哈希引用");
			_desiredHeight += 18f;
			return;
		}
		string state = r.State == BattleRefPolicy.StateFinal ? "终局"
			: (r.State == BattleRefPolicy.StateProvisional ? "暂存(未终局)" : "战斗中");
		if (r.ResetCount > 0) state += " 已重置×" + r.ResetCount;
		string write = string.IsNullOrEmpty(r.ExportSha256) ? "尚未导出" : "已导出";
		GUILayout.Label("  " + label + r.ShortTag + "  " + state + " · " + write);
		_desiredHeight += 18f;
		GUILayout.Label("  战斗编号 " + r.Id);
		_desiredHeight += 18f;
		if (GUILayout.Button("复制引用", GUILayout.Width(80f)))
		{
			OverlayUGUI.CopyToClipboard(BattleRefPolicy.CopyText(r.Id, r.Revision, quest, r.State, r.ResetCount,
				string.IsNullOrEmpty(r.ExportPath) ? "" : System.IO.Path.GetFileName(r.ExportPath), r.ExportSha256));
		}
		_desiredHeight += 22f;
	}

	private static void DrawContributionDashboard()
	{
		bool useFolds = Plugin.CfgReconcileCalc != null && Plugin.CfgReconcileCalc.Value;
		// 1.7.7 (P2-A #7): one data path with the uGUI renderer -- the previous battle must be the
		// most recent FINISHED one, not whatever the cache still holds (see ResolveContributionView).
		ContributionView view = OverlayUGUI.ResolveContributionView(useFolds);
		GUILayout.Label("───── 总贡献(可加和:自身 + 他人因你)─────");
		_desiredHeight += 18f;
		// R56 (plan §6): the title and the identity come from the SAME resolved view, so the fallback
		// cannot label the previous battle's table with the live battle's number.
		DrawBattleRef(OverlayUGUI.DisplayedRef(view != null && view.Live), view == null ? 0 : view.QuestId,
			view != null && view.Live ? "本场 " : "上一场 ");
		if (view == null || !view.Usable || view.Result == null)
		{
			GUILayout.Label("  不可用 —— " + ((view == null) ? "无数据" : (view.Unavailable ?? "无数据")));
			_desiredHeight += 18f;
			return;
		}
		ContributionResult res = view.Result;
		double total = res.Stats.Analyzable;
		if (!view.Live) GUILayout.Label("  (上一场)");
		for (int i = 0; i < res.Actors.Count; i++)
		{
			ContributionActorRow a = res.Actors[i];
			if (a.Total <= 0.0 && a.Direct <= 0.0) continue;
			double share = total > 0.0 ? 100.0 * a.Total / total : 0.0;
			// RF5e: the row text comes from the pure builder, so the fallback formats numbers the same way the
			// panel and the export do (and the suite executes it).
			GUILayout.Label(FallbackText.ContributionActorLine(a.Name, a.Summon, a.Total, share, a.Base, a.Self,
			                                                   a.Assist, a.Received, a.Friendly));
			_desiredHeight += 18f;
			if (i >= 11) break;
		}
		double unattrPct = total > 0.0 ? 100.0 * res.Stats.Unattributed / total : 0.0;
		GUILayout.Label(FallbackText.ContributionTotalsLine(res.Stats.Attributed, res.Stats.Unattributed, unattrPct, res.Stats.Hits));
		_desiredHeight += 18f;
	}

	private static void DrawHistoryMini(BattleSummary bs)
	{
		GUILayout.Label($"上一场 结果 {bs.Result} 任务 {bs.QuestId} {BattleTime.Seconds(bs.DurationSeconds)} 我方{DisplayFormat.Num(SumDealt(bs, true))} 敌{DisplayFormat.Num(SumDealt(bs, false))}");
		_desiredHeight += 24f;
		if (bs.Ref != null)
		{
			GUILayout.Label("  " + bs.Ref.ShortTag + "  战斗编号 " + bs.Ref.Id);
			_desiredHeight += 18f;
		}
	}

	private static long SumDealt(BattleSummary bs, bool ally)
	{
		long s = 0;
		foreach (var a in bs.Actors)
			if (CharacterInfo.IsAllyTeam(a.Team) == ally) s += a.DamageDealt;
		return s;
	}

	private static void DrawActorRow(ActorStats a, double secs, bool withSkills)
	{
		GUILayout.BeginHorizontal();
		GUILayout.Label($"[{(CharacterInfo.IsAllyTeam(a.Team) ? "我" : "敌")}] {a.Name}", GUILayout.Width(150f));
		GUILayout.Label($"伤害 {DisplayFormat.Num(a.DamageDealt)}", GUILayout.Width(110f));
		GUILayout.Label($"秒伤 {a.Dps(secs):F0}", GUILayout.Width(80f));
		GUILayout.Label($"最大 {DisplayFormat.Num(a.MaxHitDamage)}", GUILayout.Width(90f));
		GUILayout.Label($"受击 {DisplayFormat.Num(a.DamageTaken)}", GUILayout.Width(90f));
		GUILayout.EndHorizontal();
		if (!withSkills || a.SkillDamage.Count <= 0) return;
		var sk = new List<KeyValuePair<int, long>>(a.SkillDamage);
		sk.Sort((x, y) => y.Value.CompareTo(x.Value));
		foreach (var kv in sk)
		{
			GUILayout.BeginHorizontal();
			GUILayout.Label($"    技能#{kv.Key}", GUILayout.Width(150f));
			GUILayout.Label(DisplayFormat.Num(kv.Value), GUILayout.Width(100f));
			GUILayout.EndHorizontal();
		}
	}

	private static void DrawHistory()
	{
		if (Aggregator.History.Count == 0)
		{
			GUILayout.Label("暂无历史记录.");
			_desiredHeight = 62f;
			return;
		}
		foreach (var item in Aggregator.History)
		{
			GUILayout.Label($"{item.Result,-8} q{item.QuestId} {BattleTime.Log(item.DurationSeconds),6}  我方 {SumDealt(item, true),10}  敌 {SumDealt(item, false),10}  受击 {item.TotalTaken,10}  ({item.ActorCount} actors)");
		}
		_desiredHeight = 54f + Aggregator.History.Count * 20f;
	}

	private static void HandleDrag(Event ev)
	{
		if (ev == null) return;
		Rect strip = new Rect(WindowRect.x, WindowRect.y, WindowRect.width, 20f);
		if (ev.type == EventType.MouseDown && ev.button == 0 && strip.Contains(ev.mousePosition))
			_dragging = true;
		else if (ev.type == EventType.MouseUp)
			_dragging = false;
		else if (_dragging && ev.type == EventType.MouseDrag)
			WindowRect.position += ev.delta;
	}

	private static void LoadRect()
	{
		try
		{
			if (File.Exists(_uiPath))
			{
				string[] parts = File.ReadAllText(_uiPath).Split(',');
				if (parts.Length == 4)
					WindowRect = new Rect(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]), float.Parse(parts[3]));
			}
		}
		catch { }
	}

	private static void SaveRect()
	{
		try
		{
			File.WriteAllText(_uiPath, $"{WindowRect.x:F0},{WindowRect.y:F0},{WindowRect.width:F0},{WindowRect.height:F0}");
		}
		catch { }
	}

	private static void TryInitFont()
	{
		try
		{
			Font f = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Yu Gothic UI", "Meiryo" }, 13);
			if (!GameRef.IsNull(f)) { UiFont = f; return; }
			if (!_fontWarned) { _fontWarned = true; Plugin.LogSource.LogWarning("[DpsMeter][UI] imgui font lookup returned null; keeping the Unity default and retrying every 3s"); }
		}
		catch (Exception ex)
		{
			if (!_fontWarned) { _fontWarned = true; Plugin.LogSource.LogWarning($"[DpsMeter][UI] imgui font exception: {ex.Message}"); }
		}
	}
}
