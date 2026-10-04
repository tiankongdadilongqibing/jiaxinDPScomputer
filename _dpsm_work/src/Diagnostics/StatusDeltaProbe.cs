using System.Collections.Generic;
using System.Text;

namespace DpsMeter;

/// <summary>
/// Did a damage record inflict a status abnormality (異常状態) on its victim?
///
/// Answered by DIFFING the victim's own buff list around the hit instead of hooking the game's status
/// application (BattleObject.AddBuff). The diff approach is mechanism-agnostic: it works no matter which
/// native function applies the ailment, and it inherits the attribution for free (the hit already knows
/// its attacker and target).
///
/// Three observation points, because the game can apply the ailment at three different moments:
///   1. BEFORE  -- Prefix of BattleObject.Damage  (NoteBefore)
///   2. AFTER   -- the Postfix that records the hit (Bind): catches ailments applied INSIDE Damage
///   3. +0.35 s -- a deferred re-check driven by the battle clock (Tick): catches ailments the skill
///                 applies in a separate step right after the damage
/// The moment is reported with the result, so a delayed hit is never silently presented as immediate.
///
/// A pending re-check is FLUSHED when the same victim is hit again (otherwise the next hit's ailment
/// would be credited to the previous record) and at the end of the battle.
///
/// Why not patch AddBuff: adding another detour is the one thing that has already cost this project two
/// game crashes (see SESSION-STATE §7.2.36/§7.2.37), and this feature needs no new detour at all --
/// BattleObject.Damage is already patched.
/// </summary>
public static class StatusDeltaProbe
{
	/// <summary>
	/// How long after a hit a late ailment may still be credited to it, IN BATTLE SECONDS.
	///
	/// This is a CAP, not the actual window: a pending diff is normally settled by the NEXT damage record
	/// on the same victim (see NoteBefore -> FlushVictim), so the window is "until that victim is observed
	/// again". Evaluating only at a fixed 0.35 s deadline left a real hole that lost an 暗闇 source in
	/// battle 201246: the deadline (hit+0.35) passed 0.08 s BEFORE the next hit, whose own pre-hit
	/// snapshot already contained the ailment -- so neither side of the diff saw the transition.
	/// The cap keeps an unrelated later ailment from being blamed on an old hit when the victim is hit
	/// sparsely.
	/// </summary>
	private const double WindowMax = 1.5;
	private const int PendingMax = 256;

	/// <summary>A remaining-time increase above this (seconds) is a REFRESH, not the natural countdown.</summary>
	private const int RefreshEpsilon = 1;

	private sealed class Pending
	{
		public long VictimPtr;
		public double DueAt;
		public Dictionary<string, CompositionProbe.StatusEnt> Before;
		public BattleEvent Ev;
		/// <summary>Text of the immediate diff ("" when nothing was seen at once).</summary>
		public string Immediate = "";
		public string Applier = "";
	}

	/// <summary>Pre-hit status sets, keyed by victim pointer. Written by the Damage Prefix, read by Bind.</summary>
	private static readonly Dictionary<long, Dictionary<string, CompositionProbe.StatusEnt>> Before = new Dictionary<long, Dictionary<string, CompositionProbe.StatusEnt>>();

	private static readonly List<Pending> Pendings = new List<Pending>();

	internal static void Reset()
	{
		Before.Clear();
		Pendings.Clear();
		CompositionProbe.ResetSigErrors();
		// 1.4.0: same per-battle lifetime for the BuffValue observation table.
		CompositionProbe.ResetStatusValues();
	}

	private static long Ptr(BattleObject bo)
	{
		try
		{
			if (GameRef.IsNull(bo)) return 0L;
			return bo.Pointer.ToInt64();
		}
		catch { return 0L; }
	}

	/// <summary>BattleObject.Damage PREFIX: remember the victim's ailments as they are BEFORE the hit.</summary>
	internal static void NoteBefore(BattleObject victim)
	{
		try
		{
			long p = Ptr(victim);
			if (p == 0L) return;
			// Only sample during a battle: outside one this would just burn IL2CPP calls on objects the
			// meter has no use for (and Damage is only recorded inside a session anyway).
			if (Aggregator.Session == null || !Aggregator.Session.InBattle) return;
			// A second hit on the same target closes the previous re-check FIRST, otherwise this hit's own
			// ailment would be credited to the earlier record.
			FlushVictim(p, Aggregator.Session.ActiveSeconds);
			if (Before.Count > 512) Before.Clear();   // a Prefix whose Postfix never ran must not leak forever
			Before[p] = CompositionProbe.StatusSigOf(victim);
		}
		catch { }
	}

	/// <summary>BattleObject.Damage POSTFIX: diff now and schedule the late re-check.</summary>
	internal static void Bind(BattleEvent ev, BattleObject victim, double now)
	{
		try
		{
			if (ev == null) return;
			long p = Ptr(victim);
			if (p == 0L) return;
			Dictionary<string, CompositionProbe.StatusEnt> pre;
			if (!Before.TryGetValue(p, out pre)) return;   // no snapshot (e.g. heal-path damage): nothing to say
			Before.Remove(p);
			var post = CompositionProbe.StatusSigOf(victim);
			string applier;
			string immediate = Diff(pre, post, 0.0, ev.Attacker, out applier);
			if (!string.IsNullOrEmpty(immediate)) ev.StatusDelta = immediate;
			if (!string.IsNullOrEmpty(applier)) ev.StatusApplier = applier;
			var pd = new Pending { VictimPtr = p, DueAt = now + WindowMax, Before = pre, Ev = ev, Immediate = immediate, Applier = applier };
			Pendings.Add(pd);
			if (Pendings.Count > PendingMax) Pendings.RemoveAt(0);
		}
		catch { }
	}

	/// <summary>Battle-clock driven: evaluate re-checks whose window has passed.</summary>
	internal static void Tick(double now)
	{
		try
		{
			for (int i = Pendings.Count - 1; i >= 0; i--)
			{
				if (Pendings[i].DueAt <= now) { Evaluate(Pendings[i], now); Pendings.RemoveAt(i); }
			}
		}
		catch { }
	}

	/// <summary>Battle end: settle everything still pending so no result is lost.</summary>
	internal static void FlushAll(double now)
	{
		try
		{
			for (int i = 0; i < Pendings.Count; i++) Evaluate(Pendings[i], now);
			Pendings.Clear();
		}
		catch { }
	}

	private static void FlushVictim(long victimPtr, double now)
	{
		try
		{
			for (int i = Pendings.Count - 1; i >= 0; i--)
			{
				if (Pendings[i].VictimPtr != victimPtr) continue;
				Evaluate(Pendings[i], now);
				Pendings.RemoveAt(i);
			}
		}
		catch { }
	}

	/// <summary>
	/// Final evaluation of one hit. <paramref name="ev"/> may already be in the session's event list --
	/// BattleEvent is a class, so the export and the overlay both pick the late result up.
	/// </summary>
	private static void Evaluate(Pending pd, double now)
	{
		try
		{
			BattleObject v = SessionVictim(pd);
			if (v == null)
			{
				// Victim no longer resolvable (battle torn down): keep what we saw at hit time rather
				// than diffing against an empty list, which would fake "every status disappeared".
				pd.Ev.StatusDelta = pd.Immediate;
				return;
			}
			var post = CompositionProbe.StatusSigOf(v);
			string applier;
			string late = Diff(pd.Before, post, now - (pd.DueAt - WindowMax), pd.Ev.Attacker, out applier);
			if (!string.IsNullOrEmpty(applier) && string.IsNullOrEmpty(pd.Applier)) pd.Ev.StatusApplier = applier;
			if (string.IsNullOrEmpty(late))
			{
				pd.Ev.StatusDelta = pd.Immediate;
				return;
			}
			if (string.IsNullOrEmpty(pd.Immediate)) pd.Ev.StatusDelta = late;
			else if (!string.Equals(pd.Immediate, late)) pd.Ev.StatusDelta = pd.Immediate + " ; " + late;
		}
		catch { }
	}

	/// <summary>
	/// Re-fetch the victim object for a pending re-check. The probe stores only the pointer, so the
	/// object is looked up through the session's actor table (no new detour, no keeping a dead
	/// reference alive across frames).
	/// </summary>
	private static BattleObject SessionVictim(Pending pd)
	{
		try
		{
			BattleSession s = Aggregator.Session;
			if (s == null) return null;
			foreach (ActorStats a in s.OrderedActors)
			{
				BattleObject bo = a.Source;
				if (bo == null) continue;
				if (Ptr(bo) == pd.VictimPtr) return bo;
			}
		}
		catch { }
		return null;
	}

	/// <summary>
	/// Diff two ailment signatures into "附加 X(剩余 300,施加者 Y) / 状态被刷新 Z / 状态消失 W" text.
	/// <paramref name="delaySec"/> &gt; 0 tags the result with the delay, so a late ailment is never
	/// silently presented as immediate.
	///
	/// <paramref name="credited"/> is the attacker of the damage record this diff hangs on. When the
	/// game's own applier (<c>BuffBase.OwnerIdentifier</c>) is a DIFFERENT unit, the text says so and
	/// <paramref name="applier"/> reports the real one -- that mismatch is exactly the false attribution
	/// the user hit, where 混沌 ニャルラトホテプ was blamed for a 暗闇 it cannot inflict.
	///
	/// A duration INCREASE of more than <see cref="RefreshEpsilon"/> counts as a refresh -- the natural
	/// countdown can only make it go down, and the observation window is short, so an increase of that
	/// size cannot be the clock.
	/// </summary>
	private static string Diff(Dictionary<string, CompositionProbe.StatusEnt> pre, Dictionary<string, CompositionProbe.StatusEnt> post, double delaySec, string credited, out string applier)
	{
		var added = new List<string>();
		var refreshed = new List<string>();
		var gone = new List<string>();
		applier = "";
		bool mismatch = false;
		try
		{
			var names = new List<string>();
			foreach (var kv in post) names.Add(kv.Key);
			foreach (var kv in pre) if (!post.ContainsKey(kv.Key)) names.Add(kv.Key);
			names.Sort();
			foreach (var n in names)
			{
				CompositionProbe.StatusEnt a, b;
				pre.TryGetValue(n, out a);
				post.TryGetValue(n, out b);
				int av = a.Value;
				int bv = b.Value;
				int ar = a.Remain;
				int br = b.Remain;
				if (bv > av)
				{
					string own = ResolveApplier(b);
					if (!string.IsNullOrEmpty(own))
					{
						if (applier.Length > 0) applier += "、";
						applier += n + "=" + own;
						if (!string.IsNullOrEmpty(credited) && !string.Equals(own, credited))
						{
							mismatch = true;
							own = "施加者 " + own + " ≠ 本条攻击者 " + credited;
						}
						else
						{
							own = "施加者 " + own;
						}
					}
					// NOTE: BuffValue is NOT necessarily a stack count -- measured 2026-09-27: one 暗闇
					// application added 50 to it, so calling that "50 层" would be an invented number.
					// It is reported as the game's own value delta.
					added.Add(av == 0 ? WithRemain(n, br, own) : (n + "(数值+" + (bv - av) + ",剩余 " + br + (own.Length > 0 ? "," + own : "") + ")"));
				}
				else if (bv == av && bv > 0 && br - ar > RefreshEpsilon)
				{
					refreshed.Add(n + "(剩余 " + ar + "→" + br + ")");
				}
				else if (bv < av && bv > 0)
				{
					gone.Add(n + "(数值-" + (av - bv) + ")");
				}
				else if (bv == 0 && av > 0)
				{
					gone.Add(n);
				}
			}
		}
		catch { }
		if (mismatch) applier += "(归属与施加者不一致)";
		if (added.Count == 0 && refreshed.Count == 0 && gone.Count == 0) { applier = ""; return ""; }
		var sb = new StringBuilder(64);
		if (added.Count > 0)
		{
			sb.Append("附加异常状态 ");
			AppendList(sb, added);
			sb.Append(delaySec > 0.15
				? "(伤害后 " + delaySec.ToString("F2") + " 秒才出现)"
				: "(命中瞬间)");
		}
		if (refreshed.Count > 0)
		{
			if (sb.Length > 0) sb.Append("   ");
			sb.Append("状态被刷新 ");
			AppendList(sb, refreshed);
		}
		if (gone.Count > 0)
		{
			if (sb.Length > 0) sb.Append("   ");
			sb.Append("状态消失 ");
			AppendList(sb, gone);
		}
		return sb.ToString();
	}

	/// <summary>
	/// A newly applied ailment, with the DURATION the game stamped on it.
	///
	/// The duration is what identifies the source, because different sources apply the same ailment for
	/// different lengths of time (wiki, 2026-09-27: 暗闇 is 10 s from 死のカラス = トレイラ's token, and
	/// 6 s from ディシア AS1「影追いの毒牙」 -- both 中確率, which is exactly why the same attack blinds the
	/// target in one battle and not in another). The raw counter is printed as well as the seconds, so the
	/// number stays useful even if BuffBase.RemainingTime turns out not to be in the 30-units-per-game-second
	/// the skill cooldowns use (measured: Skill.CoolTimeFrame / Skill.CoolTime = 30.0).
	/// </summary>
	private static string WithRemain(string name, int remain, string own)
	{
		string tail = "";
		if (remain > 0) tail = "剩余 " + remain + " ≈" + (remain / 30.0).ToString("F1") + " 秒";
		if (!string.IsNullOrEmpty(own)) tail = (tail.Length > 0) ? (tail + "," + own) : own;
		if (tail.Length == 0) return name;
		return name + "(" + tail + ")";
	}

	/// <summary>
	/// Name the unit the game itself credits for this ailment, using <c>BuffBase.OwnerIdentifier</c>.
	///
	/// The identifier is matched against our actor table by (ObjectType, AppIndex) -- both are plain
	/// field reads on the interop wrappers, so nothing here calls into game logic that could raise an
	/// uncatchable fault (see SESSION-STATE 7.2.36/7.2.37). Unmatched identifiers are reported raw rather
	/// than silently dropped, so a miss stays visible.
	/// </summary>
	private static string ResolveApplier(CompositionProbe.StatusEnt ent)
	{
		try
		{
			if (ent.OwnerType == 0 && ent.OwnerId == 0 && ent.OwnerApp == 0) return "";
			BattleSession s = Aggregator.Session;
			if (s != null)
			{
				foreach (ActorStats a in s.OrderedActors)
				{
					BattleObject bo = a.Source;
					if (bo == null) continue;
					int ot = 0, ap = 0;
					try { ot = (int)bo.ObjectType; } catch { continue; }
					try { ap = bo.AppIndex; } catch { continue; }
					if (ot == ent.OwnerType && ap == ent.OwnerApp && !string.IsNullOrEmpty(a.Name))
						return a.Name;
				}
			}
			return "未登记(type=" + ent.OwnerType + ",id=" + ent.OwnerId + ",app=" + ent.OwnerApp + ")";
		}
		catch { return ""; }
	}

	private static void AppendList(StringBuilder sb, List<string> items)
	{
		for (int i = 0; i < items.Count; i++)
		{
			if (i > 0) sb.Append('、');
			sb.Append(items[i]);
		}
	}

	/// <summary>
	/// Per-battle self-check: does EVERY "status absent -> present" transition have an infliction record
	/// explaining it? This is the automatic form of the user's question ("does every stretch of 暗闇 have a
	/// precise source?"), and it audits the feature itself: a MISS means the application happened in a gap
	/// our diff could not see, i.e. a limitation of the observation, not of the game.
	///
	/// The status a victim carried at each record is stored on the event's chain as
	/// <see cref="CalcBreakdown.VictimStatuses"/>, produced by the same call that writes comp4's
	/// "受击方状态:…" text.
	///
	/// 1.5.0 (架构审视 B3) CHANGED THIS: the audit used to PARSE that Chinese sentence back out of comp4
	/// (depending on the literal prefix and a three-space separator), so rewording the prose would have
	/// silently moved `statusAudit.appearances` with the build still green. It now reads the list.
	/// </summary>
	internal static void Audit(BattleSession s)
	{
		try
		{
			if (s == null) return;
			// infliction records: (victim, status name) -> times
			var inf = new Dictionary<string, List<double>>();
			foreach (BattleEvent e in s.Events)
			{
				if (e.Type != "dmg" || string.IsNullOrEmpty(e.StatusDelta)) continue;
				if (!e.StatusDelta.StartsWith("附加异常状态 ")) continue;
				string tag = Tag(e);
				if (string.IsNullOrEmpty(tag)) continue;
				foreach (string nm in tag.Split('、'))
				{
					if (nm.Length == 0) continue;
					string key = e.Victim + "\u0001" + nm;
					List<double> lst;
					if (!inf.TryGetValue(key, out lst)) { lst = new List<double>(); inf[key] = lst; }
					lst.Add(e.T);
				}
			}
			// per victim, walk the records in order and diff the comp4 status sets
			var prev = new Dictionary<string, HashSet<string>>();
			foreach (BattleEvent e in s.Events)
			{
				if (e.Type != "dmg") continue;
				HashSet<string> cur = StatusSetOf(e.Calc.VictimStatuses);
				HashSet<string> old;
				if (!prev.TryGetValue(e.Victim, out old)) old = new HashSet<string>();
				foreach (string st in cur)
				{
					if (old.Contains(st)) continue;
					s.StatusTransitions++;
					bool ok = false;
					List<double> times;
					if (inf.TryGetValue(e.Victim + "\u0001" + st, out times))
					{
						for (int i = 0; i < times.Count; i++)
							if (times[i] >= e.T - WindowMax - 0.05 && times[i] <= e.T + 0.05) { ok = true; break; }
					}
					if (ok) s.StatusExplained++;
					else if (s.StatusUnexplained.Count < s.StatusUnexplainedCap)
						s.StatusUnexplained.Add($"{e.Victim} {st} @{e.T:F2}s");
					else s.StatusUnexplainedOmitted++;
				}
				prev[e.Victim] = cur;
			}
			if (s.StatusTransitions > 0)
			{
				// Attribution check: for every infliction record where the game told us the applier, does it
				// match the record's attacker? A mismatch means the row's attacker is only "the nearest
				// damage record", which is what produced the false 混沌 ニャルラトホテプ attribution.
				foreach (BattleEvent e in s.Events)
				{
					if (string.IsNullOrEmpty(e.StatusApplier)) continue;
					string ap = e.StatusApplier;
					int mark = ap.IndexOf("(归属与施加者不一致)");
					if (mark >= 0) ap = ap.Substring(0, mark);
					foreach (string pair in ap.Split('、'))
					{
						int eq = pair.IndexOf('=');
						if (eq <= 0) continue;
						string who = pair.Substring(eq + 1).Trim();
						if (who.Length == 0) continue;
						s.StatusApplierKnown++;
						if (!string.Equals(who, e.Attacker) && s.StatusMismatch.Count < 20)
							s.StatusMismatch.Add($"{e.Victim} {pair.Substring(0, eq)}: 施加者={who} / 记录攻击者={e.Attacker} @{e.T:F2}s");
					}
				}
				string line = $"[DpsMeter][STAT] 异常状态来源审计: 状态出现 {s.StatusTransitions} 次, 有附加记录 {s.StatusExplained} 次"
					+ (s.StatusUnexplained.Count > 0
						? $", **未捕获 {s.StatusUnexplained.Count} 次** (来源未知): " + string.Join(" | ", s.StatusUnexplained.ToArray())
						: ", 全部都有精确来源")
					+ (s.StatusMismatch.Count > 0
						? $"   承载记录≠施加者 {s.StatusMismatch.Count} 处(正常:差分只能挂在最近的一击上,施加者见每条明细): " + string.Join(" | ", s.StatusMismatch.ToArray())
						: (s.StatusApplierKnown > 0 ? $"   施加者已知 {s.StatusApplierKnown} 处, 且都等于承载攻击者" : ""))
					+ (CompositionProbe.SigErrors > 0
						? $"   **状态读取异常 {CompositionProbe.SigErrors} 次**(该条状态无法读取,是插件的读取问题,不是游戏数据缺失): {CompositionProbe.SigErrorMsg}"
						: "")
					// 1.4.0: the two numbers that explain an explained=0 audit -- how often the buff list
					// could not be read at all, and what BuffValue each status actually carries.
					+ $"   [列表读不到 {CompositionProbe.SigNoList} 次]"
					+ $"   [各状态 BuffValue: {CompositionProbe.StatusValueSample()}]";
				Plugin.LogSource.LogInfo(line);
				RuntimeLog.Write(line);
			}
		}
		catch { }
	}

	/// <summary>1.5.0 (B3): the set form of a chain's victim status list. Replaces the old
	/// <c>StatusSetOfComp4</c>, which re-parsed the plugin's own prose (prefix + separator dependent).</summary>
	private static HashSet<string> StatusSetOf(string[] statuses)
	{
		var set = new HashSet<string>();
		try
		{
			if (statuses == null) return set;
			for (int i = 0; i < statuses.Length; i++)
			{
				string t = statuses[i];
				if (string.IsNullOrEmpty(t)) continue;
				t = t.Trim();
				if (t.Length == 0 || t == "无") continue;
				set.Add(t);
			}
		}
		catch { }
		return set;
	}

	/// <summary>
	/// The applier name(s) the game reported for this record, joined by '/'; "" when unknown.
	/// Pairs look like "暗闇=死のカラス" (see Bind) and the "(归属与施加者不一致)" marker is dropped.
	/// </summary>
	internal static string Appliers(BattleEvent ev)
	{
		try
		{
			if (ev == null || string.IsNullOrEmpty(ev.StatusApplier)) return "";
			string ap = ev.StatusApplier;
			int mark = ap.IndexOf("(归属与施加者不一致)");
			if (mark >= 0) ap = ap.Substring(0, mark);
			var sb = new StringBuilder(32);
			foreach (string pair in ap.Split('、'))
			{
				int eq = pair.IndexOf('=');
				if (eq <= 0) continue;
				string who = pair.Substring(eq + 1).Trim();
				if (who.Length == 0) continue;
				if (sb.Length > 0) sb.Append('/');
				sb.Append(who);
			}
			return sb.ToString();
		}
		catch { return ""; }
	}

	/// <summary>
	/// Did THIS record apply the ailment itself, or is it merely the nearest damage record on the victim
	/// where the change was noticed?
	///
	/// True only when the game-confirmed applier is exactly the record's attacker. The distinction is not
	/// cosmetic: a diff can only hang on a damage record, so most rows that "carry" an ailment are NOT its
	/// source -- e.g. a structure's own 反噬 tick was labelled as inflicting 暗闇 that トレイラ applied.
	/// Returns false when the applier is unknown (never claim ownership we cannot prove).
	/// </summary>
	internal static bool IsOwnInfliction(BattleEvent ev)
	{
		try
		{
			if (ev == null || string.IsNullOrEmpty(ev.StatusDelta)) return false;
			string ap = ev.StatusApplier;
			if (string.IsNullOrEmpty(ap)) return false;
			if (ap.IndexOf("(归属与施加者不一致)") >= 0) return false;
			int mark = ap.IndexOf("(归属与施加者不一致)");
			if (mark >= 0) ap = ap.Substring(0, mark);
			bool any = false;
			foreach (string pair in ap.Split('、'))
			{
				int eq = pair.IndexOf('=');
				if (eq <= 0) continue;
				any = true;
				if (!string.Equals(pair.Substring(eq + 1).Trim(), ev.Attacker)) return false;
			}
			return any;
		}
		catch { return false; }
	}

	/// <summary>Compact tag for the one-line summary of a record ("" when no ailment was inflicted).</summary>
	internal static string Tag(BattleEvent ev)
	{
		try
		{
			if (ev == null || string.IsNullOrEmpty(ev.StatusDelta)) return "";
			string s = ev.StatusDelta;
			if (!s.StartsWith("附加异常状态 ")) return "";
			string rest = s.Substring("附加异常状态 ".Length);
			int cut = rest.IndexOf("   ");           // section separator before 状态被刷新 / 状态消失
			if (cut > 0) rest = rest.Substring(0, cut);
			var sb = new StringBuilder(32);
			int depth = 0;
			foreach (char c in rest)
			{
				if (c == '(') { depth++; continue; }
				if (c == ')') { if (depth > 0) depth--; continue; }
				if (depth == 0) sb.Append(c);
			}
			return sb.ToString().Trim();
		}
		catch { return ""; }
	}
}
