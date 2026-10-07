using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DpsMeter;

/// <summary>
/// R79 受击来源拆分: the section's projection and its export writer.
///
/// Same paradigm as the contribution board (Output/ContributionSession.cs): one whole-session projection,
/// rebuilt only when the session, the event count, the generation counter or the one-second throttle says
/// so, so the live page and the export read the very same object. The event walk is the only place the
/// game-side types are touched; the folding itself is pure (Policy/TakenBreakdownPolicy.cs).
///
/// No new Harmony hook: every input already rides on <see cref="BattleEvent"/>. The one thing that is not in
/// the event and cannot be read afterwards is the victim's POSITION, which is why it is snapshotted by
/// Aggregator.RecordDamageNow on a unit's first damage and read from <see cref="ActorStats.Position"/> here.
/// </summary>
internal static class TakenSession
{
	private static TakenBreakdown _cache;
	private static BattleSession _cacheSession;
	private static int _cacheEvents = -1;
	private static double _cacheAt = -1e9;
	private static int _cacheGen = -1;

	/// <summary>Bumped by <see cref="Invalidate"/>; part of the staleness test so a manual reset cannot be
	/// served from an older cache.</summary>
	public static int Generation { get; private set; }

	/// <summary>Drop the cached projection (battle reset, key reset).</summary>
	public static void Invalidate()
	{
		Generation++;
		Clear();
	}

	/// <summary>Drop the cached projection without bumping the generation (a new session was started).</summary>
	private static void Clear()
	{
		_cache = null;
		_cacheSession = null;
		_cacheEvents = -1;
		_cacheAt = -1e9;
		_cacheGen = -1;
	}

	/// <summary>
	/// The breakdown the PAGE shows: the live session while one is running, otherwise the most recent
	/// FINISHED battle -- the same rule and the same source the contribution board uses (Ui/OverlayUGUI
	/// SessionForView / ResolveContributionView read Aggregator.History[0]).
	///
	/// R80 fixed a real defect here: R79 resolved through <see cref="Aggregator.Session"/> only, and both
	/// EndSession and the teardown set that to null, so the page went blank the moment a battle ended -- the
	/// numbers were never lost, the view simply refused to look. The finished battle keeps its own
	/// BattleSession (BattleSummary.Session, the very object the export wrote), and Compute only reads
	/// Events and OrderedActors, both of which survive finalisation (only the actors' live BattleObject
	/// references are dropped). Reading the summary's own session rather than a BattleSession.FromSummary
	/// copy is also what keeps the reference-keyed cache below usable.
	///
	/// Returns null only when there is neither a live session nor a finished battle to show.
	/// </summary>
	public static TakenBreakdown Get()
	{
		BattleSession s = Aggregator.Session;
		if (s == null && Aggregator.History.Count > 0)
		{
			BattleSummary last = Aggregator.History[0];
			if (last != null) s = last.Session;
		}
		if (s == null)
		{
			Clear();
			return null;
		}
		double now = Time.realtimeSinceStartup;
		if (_cache == null || TakenCachePolicy.IsStale(ReferenceEquals(_cacheSession, s), _cacheEvents, s.Events.Count,
		                                                s.InBattle, _cacheAt, now, _cacheGen, Generation))
		{
			_cache = Compute(s);
			_cacheSession = s;
			_cacheEvents = s.Events.Count;
			_cacheAt = now;
			_cacheGen = Generation;
		}
		return _cache;
	}

	/// <summary>Project one session without touching the cache (the export path).</summary>
	public static TakenBreakdown Compute(BattleSession s)
	{
		if (s == null) return new TakenBreakdown();
		var hits = new List<TakenHit>();
		for (int i = 0; i < s.Events.Count; i++)
		{
			BattleEvent e = s.Events[i];
			if (e == null || e.Type != "dmg") continue;
			var h = new TakenHit();
			h.T = e.T;
			h.VictimKey = e.VictimKey;
			h.Victim = e.Victim;
			h.VictimTeam = e.VictimTeam;
			h.AttackerKey = e.AttackerKey;
			h.Attacker = e.Attacker;
			h.Attr = e.Attr;
			h.Friendly = e.Friendly;
			h.Source = e.Source;
			h.HitType = e.CalcHitType;
			h.EffectId = e.CalcEffectId;
			h.HitMatch = e.HitMatch;
			h.Amount = e.Amount;
			h.Nominal = e.Nominal;
			h.Status = e.StatusDelta;
			h.StatusApplier = e.StatusApplier;
			hits.Add(h);
		}
		TakenBreakdown b = TakenBreakdownPolicy.Build(hits, SkillSidePolicy.OurTeam);
		// The events carry the display name the moment they were published; the actor row is the registry
		// (team + the position snapshot). Prefer the registry, and never invent either of them.
		for (int i = 0; i < b.Actors.Count; i++)
		{
			TakenActor a = b.Actors[i];
			ActorStats st = FindByKey(s, a.Key);
			if (st == null) continue;
			if (!string.IsNullOrEmpty(st.Name)) a.Name = st.Name;
			a.Team = (int)st.Team;
			a.Ally = CharacterInfo.IsAllyTeam(st.Team);
			a.Position = st.Position;
		}
		return b;
	}

	private static ActorStats FindByKey(BattleSession s, int key)
	{
		if (key == 0) return null;
		for (int i = 0; i < s.OrderedActors.Count; i++)
		{
			ActorStats a = s.OrderedActors[i];
			if (a != null && a.Key == key) return a;
		}
		return null;
	}

	/// <summary>
	/// Write the export section's OBJECT (the caller writes the key, exactly as it does for the
	/// contribution section). The method tag and the basis ride inside, so the numbers can never be read
	/// without the statement of what they are.
	/// </summary>
	public static void AppendJson(StringBuilder sb, BattleSession s)
	{
		TakenBreakdown b = Compute(s);
		sb.Append('{');
		// R80: 1.0 -> 1.1. The only difference is that a dimension can no longer fold its tail into one row,
		// so `byAttacker`/`bySource`/`byHitType`/`byEffect`/`byStatus` now carry every distinct bucket the
		// battle produced (R79 stopped at 64). Every other key, unit and identity is unchanged, and the
		// contribution section's own schemaVersion stays 1.2.
		sb.Append("\"schemaVersion\":\"1.1\",");
		sb.Append("\"method\":\"").Append(TakenBreakdown.Method).Append("\",");
		sb.Append("\"basis\":\"").Append(TakenBreakdown.Basis).Append("\",");
		sb.Append("\"hits\":").Append(b.Hits).Append(',');
		sb.Append("\"nominal\":").Append(b.Nominal).Append(',');
		sb.Append("\"taken\":").Append(b.Taken).Append(',');
		sb.Append("\"residual\":").Append(b.Residual).Append(',');
		sb.Append("\"friendly\":").Append(b.Friendly).Append(',');
		sb.Append("\"friendlyHits\":").Append(b.FriendlyHits).Append(',');
		sb.Append("\"unknown\":").Append(b.Unknown).Append(',');
		sb.Append("\"unknownHits\":").Append(b.UnknownHits).Append(',');
		sb.Append("\"actors\":[");
		for (int i = 0; i < b.Actors.Count; i++)
		{
			if (i > 0) sb.Append(',');
			AppendActor(sb, b.Actors[i]);
		}
		sb.Append("]}");
	}

	private static void AppendActor(StringBuilder sb, TakenActor a)
	{
		sb.Append('{');
		sb.Append("\"key\":").Append(a.Key).Append(',');
		// R80 fixes the quoting: JsonText.Str ESCAPES but does not add the surrounding quotes (see its own
		// doc comment), and R79 wrote these six fields straight into the object -- which made every
		// takenBreakdown section valid-looking but unparseable JSON. The frozen acceptance corpus predates
		// R79, so no gate in that round could see it; the live-corpus schema check caught it.
		sb.Append("\"name\":\"").Append(JsonText.Str(a.Name)).Append("\",");
		sb.Append("\"team\":").Append(a.Team).Append(',');
		sb.Append("\"ally\":").Append(a.Ally ? "true" : "false").Append(',');
		sb.Append("\"position\":").Append(a.Position).Append(',');
		sb.Append("\"positionLabel\":\"").Append(JsonText.Str(TakenBreakdownPolicy.PositionLabel(a.Position))).Append("\",");
		sb.Append("\"hits\":").Append(a.Hits).Append(',');
		sb.Append("\"nominal\":").Append(a.Nominal).Append(',');
		sb.Append("\"taken\":").Append(a.Taken).Append(',');
		sb.Append("\"residual\":").Append(a.Residual).Append(',');
		sb.Append("\"friendly\":").Append(a.Friendly).Append(',');
		sb.Append("\"friendlyHits\":").Append(a.FriendlyHits).Append(',');
		sb.Append("\"unknown\":").Append(a.Unknown).Append(',');
		sb.Append("\"unknownHits\":").Append(a.UnknownHits).Append(',');
		sb.Append("\"bySource\":");
		AppendBuckets(sb, a.BySource);
		sb.Append(",\"byHitType\":");
		AppendBuckets(sb, a.ByHitType);
		sb.Append(",\"byAttacker\":");
		AppendBuckets(sb, a.ByAttacker);
		sb.Append(",\"byEffect\":");
		AppendBuckets(sb, a.ByEffect);
		sb.Append(",\"byStatus\":[");
		for (int i = 0; i < a.ByStatus.Count; i++)
		{
			if (i > 0) sb.Append(',');
			TakenStatus t = a.ByStatus[i];
			sb.Append("{\"status\":\"").Append(JsonText.Str(t.Status)).Append('"');
			sb.Append(",\"applier\":\"").Append(JsonText.Str(t.Applier)).Append('"');
			sb.Append(",\"amount\":").Append(t.Amount);
			sb.Append(",\"hits\":").Append(t.Hits).Append('}');
		}
		sb.Append("]}");
	}

	private static void AppendBuckets(StringBuilder sb, List<TakenBucket> list)
	{
		sb.Append('[');
		for (int i = 0; i < list.Count; i++)
		{
			if (i > 0) sb.Append(',');
			TakenBucket b = list[i];
			// R80: no folded tail exists any more, so every key is the real bucket key (0 now means a real
			// bucket whose value is 0, which is why the old int.MinValue -> 0 rewrite had to go).
			sb.Append("{\"key\":").Append(b.Key);
			sb.Append(",\"name\":\"").Append(JsonText.Str(b.Name)).Append('"');
			sb.Append(",\"amount\":").Append(b.Amount);
			sb.Append(",\"hits\":").Append(b.Hits);
			sb.Append(",\"quality\":\"").Append(JsonText.Str(b.Quality)).Append("\"}");
		}
		sb.Append(']');
	}
}
