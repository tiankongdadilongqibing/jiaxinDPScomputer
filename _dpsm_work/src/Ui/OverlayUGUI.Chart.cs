using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace DpsMeter;

/// <summary>
/// Damage-time chart: series extraction, legend rows, axis label and palette assignment.
/// </summary>
public static partial class OverlayUGUI
{
	private static string AxisLabel(BattleSession session)
	{
		if (session == null) return "横轴:秒(每5秒一格)";
		int tMax = OverlayChart.DataEnd(session);
		var sb = new System.Text.StringBuilder("t(秒) ");
		for (int t = 0; t <= tMax; t += 5)
		{
			sb.Append(t.ToString().PadLeft(2));
			sb.Append("   ");
		}
		sb.Append($"..{tMax}");
		return sb.ToString();
	}

	private static string ChartCaption(BattleSession session)
	{
		if (session != null)
		{
			long allyDealt = 0, enemyDealt = 0, allyTaken = 0, allyHeal = 0;
			foreach (var a in session.OrderedActors)
			{
				if (CharacterInfo.IsAllyTeam(a.Team)) { allyDealt += a.DamageDealt; allyTaken += a.DamageTaken; allyHeal += a.HealingTaken; }
				else enemyDealt += a.DamageDealt;
			}
			double secs = Math.Max(1.0, session.ActiveSeconds);
			return $"任务 {session.QuestId}  时间 {BattleTime.Seconds(session.ActiveSeconds)}   我方:总伤 {DisplayFormat.Num((long)allyDealt)}  承伤 {DisplayFormat.Num((long)allyTaken)}  受回复 {DisplayFormat.Num((long)allyHeal)}   敌方总伤 {DisplayFormat.Num((long)enemyDealt)}  未归属 {DisplayFormat.Num((long)session.UnattributedDamage)}";
		}
		return "已结束战斗(上一场) 累计曲线";
	}

	private static List<ActorStats> ChartTeamActors(BattleSession session, int mode)
	{
		var list = new List<ActorStats>();
		if (session == null) return list;
		foreach (var a in session.OrderedActors)
		{
			bool ally = CharacterInfo.IsAllyTeam(a.Team);
			if (mode == 0 || mode == 1)
			{
				if (!ally) continue;
				if (mode == 1 && a.DamageTaken <= 0) continue; // only characters that took damage
				list.Add(a);
			}
			else
			{
				if (ally) continue;
				list.Add(a);
			}
		}
		if (mode == 0) list.Sort((x, y) => y.DamageDealt.CompareTo(x.DamageDealt));
		else if (mode == 1) list.Sort((x, y) => y.DamageTaken.CompareTo(x.DamageTaken));
		else list.Sort((x, y) => y.DamageDealt.CompareTo(x.DamageDealt));
		return list;
	}

	/// <summary>Returns true if any legend rows were added. mode 0=party dps, 1=party taken, 2=enemy.</summary>
	private static bool AppendChartLegend(List<RowDef> rows, BattleSession session, int mode)
	{
		var list = ChartTeamActors(session, mode);
		double secs = Math.Max(1.0, session != null ? session.ActiveSeconds : 1.0);
		const int cap = 6;
		int palIdx = 0;
		int shown = 0;
		foreach (var a in list)
		{
			if (shown >= cap) break;
			Color c = PaletteColor(mode < 2, palIdx++);
			string nm = (a.Name ?? "").Length > 12 ? (a.Name.Substring(0, 12) + "…") : (a.Name ?? "");
			string line;
			if (mode == 0)
				line = $"● {nm}   总伤害 {DisplayFormat.Num((long)a.DamageDealt)}   均秒伤 {DisplayFormat.Num((long)(a.DamageDealt / secs))}";
			else if (mode == 1)
			{
				float hp = a.GetHpPct(Math.Max(0, a.MaxSecond() - 1));
				line = $"● {nm}   受击 {DisplayFormat.Num((long)a.DamageTaken)}   剩余耐久 {hp:F0}%";
			}
			else
				line = $"● {nm}   总伤害 {DisplayFormat.Num((long)a.DamageDealt)}   均秒伤 {DisplayFormat.Num((long)(a.DamageDealt / secs))}";
			rows.Add(new RowDef
			{
				Text = line,
				Color = c,
				Height = 16f
			});
			shown++;
		}
		if (list.Count > cap)
			rows.Add(new RowDef { Text = $"… 其余 {list.Count - cap} 个角色(仅显示前 {cap})", Color = DimColor, Height = 14f });
		return list.Count > 0;
	}

	internal static List<ChartSeriesItem> ChartSeries(BattleSession session)
	{
		var result = new List<ChartSeriesItem>();
		if (session == null) return result;
		int palIdx = 0;
		foreach (var a in session.OrderedActors)
		{
			if (a.DamageHistory.Count < 2) continue;
			bool ally = CharacterInfo.IsAllyTeam(a.Team);
			bool both = Plugin.CfgChartBothSides.Value;
			if (!both && !ally) continue;
			result.Add(new ChartSeriesItem
			{
				Stats = a,
				TeamIsAlly = ally,
				Name = a.Name ?? "",
				Kind = a.Kind,
				AttrMode = a.AttrMode,
				Total = a.DamageDealt,
				MaxHitDamage = a.MaxHitDamage,
				HitCount = a.HitCount,
				Color = PaletteColor(ally, palIdx++)
			});
		}
		result.Sort((x, y) => y.Total.CompareTo(x.Total));
		return result;
	}

	internal static UnityEngine.Color PaletteColor(bool ally, int index)
	{
		if (ally)
		{
			switch (index % 12)
			{
				case 0: return new Color(0.40f, 0.78f, 1.00f);
				case 1: return new Color(1.00f, 0.60f, 0.40f);
				case 2: return new Color(0.55f, 0.95f, 0.55f);
				case 3: return new Color(1.00f, 0.90f, 0.40f);
				case 4: return new Color(0.85f, 0.55f, 1.00f);
				case 5: return new Color(0.45f, 1.00f, 0.90f);
				case 6: return new Color(1.00f, 0.45f, 0.65f);
				case 7: return new Color(0.75f, 0.80f, 1.00f);
				case 8: return new Color(0.60f, 1.00f, 0.75f);
				case 9: return new Color(1.00f, 0.75f, 0.60f);
				case 10: return new Color(0.90f, 0.90f, 0.90f);
				default: return new Color(0.50f, 0.55f, 0.60f);
			}
		}
		switch (index % 6)
		{
			case 0: return new Color(1.00f, 0.30f, 0.30f);
			case 1: return new Color(1.00f, 0.60f, 0.20f);
			case 2: return new Color(0.95f, 0.40f, 0.70f);
			case 3: return new Color(1.00f, 0.80f, 0.40f);
			case 4: return new Color(0.90f, 0.35f, 0.40f);
			default: return new Color(0.85f, 0.50f, 0.50f);
		}
	}
}
