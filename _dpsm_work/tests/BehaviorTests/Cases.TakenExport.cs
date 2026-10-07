using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// R80 pinned the BYTES of the exported takenBreakdown section, and this file exists because of a real
	/// defect: R79 wrote its six string fields straight into the object, but JsonText.Str ESCAPES a string
	/// without adding the surrounding quotes -- so every 1.7.29 export carried a section that looked fine in
	/// the panel (which never serialises anything) and could not be parsed by any JSON reader. The frozen
	/// acceptance corpus predates R79 and contains no takenBreakdown at all, so no gate in that round could
	/// have seen it; the live-corpus schema check found it instead.
	///
	/// A JSON writer has exactly one property a test can check cheaply and cannot check any other way: that
	/// the bytes it produced can be read back as the thing it claims to be. So these cases PARSE the section
	/// with System.Text.Json and then assert the shape -- the schema tag, the six formerly-unquoted fields,
	/// a value containing a quote and a newline (the escaping path), and that no text field came back as a
	/// non-string. Reading it back is the point: a screenshot of the panel proves nothing about the file.
	///
	/// The second half covers R80's other half: the page must still resolve the most recent FINISHED battle,
	/// which is why Output/TakenSession.cs is now part of this compiled set at all.
	/// </summary>
	internal static void TakenExportCases(Runner r)
	{
		r.Group("export/taken-section");

		// ---- the section must PARSE, and the strings must be strings --------------------------------
		BattleSession s = TkExportSession();
		var sb = new StringBuilder();
		TakenSession.AppendJson(sb, s);
		string json = sb.ToString();

		JsonDocument doc = null;
		string error = "";
		try { doc = JsonDocument.Parse(json); }
		catch (JsonException e) { error = e.Message; }
		r.True("the-section-parses-as-json", doc != null);
		if (doc == null)
		{
			// Surfaces the reader's own complaint (R79's unquoted names fail here with
			// "Expecting value: line 1 column ..."), then stops: every later case needs the document.
			r.Str("the-reader-explains-why-not", error, "");
			return;
		}

		using (doc)
		{
			JsonElement root = doc.RootElement;
			r.Str("the-schema-version-is-1-1", TkText(root, "schemaVersion"), "1.1");
			r.Str("the-method-is-a-quoted-string", TkText(root, "method"), TakenBreakdown.Method);
			r.Str("the-basis-is-a-quoted-string", TkText(root, "basis"), TakenBreakdown.Basis);
			r.Eq("the-section-carries-one-victim", TkCount(root, "actors"), 1);

			JsonElement actor = TkAt(root, "actors", 0);
			r.Str("the-victim-name-round-trips", TkText(actor, "name"), "ポポロット");
			r.Str("the-position-label-is-a-quoted-string", TkText(actor, "positionLabel"), "前衛");
			r.Eq("the-victim-team-is-a-number", TkNum(actor, "team"), 1);
			r.Eq("the-victim-position-is-a-number", TkNum(actor, "position"), 1);
			r.Eq("the-victim-carries-both-hits", TkNum(actor, "hits"), 2);

			// The escaping path: a bucket name that needs both an escaped quote and an escaped newline.
			JsonElement byAttacker = TkAt(actor, "byAttacker", 0);
			r.Str("a-name-with-a-quote-and-a-newline-round-trips", TkText(byAttacker, "name"),
				"ボス\"引用\"\n改行");
			r.Eq("the-two-hits-merge-into-one-attacker-bucket", TkNum(byAttacker, "hits"), 2);
			r.Str("a-best-effort-bucket-carries-its-quality", TkText(byAttacker, "quality"), "近似 1/2");

			JsonElement byStatus = TkAt(actor, "byStatus", 0);
			r.Str("the-status-is-a-quoted-string", TkText(byStatus, "status"), "毒");
			r.Str("the-status-applier-is-a-quoted-string", TkText(byStatus, "applier"), "ボス(付与)");

			// The generic form of R79's bug, so a seventh field added later cannot reintroduce it: every
			// value under a text key must have come back as a JSON string, and the walk must have looked
			// at something (a walk that visits nothing passes vacuously).
			int seen = 0, bad = 0;
			TkWalkText(root, ref seen, ref bad);
			r.Eq("no-text-field-came-back-as-a-non-string", bad, 0);
			r.True("the-walk-actually-visited-text-fields", seen >= 12);
		}

		// ---- R80: the page outlives the battle ------------------------------------------------------
		// Get() resolves the live session first and otherwise the most recent finished battle -- the same
		// source the contribution board reads (Aggregator.History[0]).
		s.InBattle = false;
		Aggregator.Session = null;
		Aggregator.History.Clear();
		Aggregator.History.Add(new BattleSummary { Session = s });
		TakenBreakdown back = TakenSession.Get();
		r.True("a-finished-battle-is-still-readable", back != null);
		r.Eq("and-it-reports-the-finished-battles-hits", back == null ? -1L : back.Hits, 2);

		// A live session still wins, and an empty world still yields nothing (the page's 空态 branch).
		Aggregator.Session = s;
		r.True("a-live-session-is-still-readable", TakenSession.Get() != null);
		Aggregator.Session = null;
		Aggregator.History.Clear();
		r.True("with-no-battle-at-all-the-page-has-nothing", TakenSession.Get() == null);
	}

	/// <summary>Two hits on one victim: same attacker, one authoritative and one best-effort (so the bucket
	/// carries a quality marker), one with a status and one without. The attacker's name deliberately needs
	/// escaping.
	///
	/// The best-effort one is marked with `Attr = "C:calcA"` because THAT is the Attacker dimension's
	/// authoritative rule (a name resolved from recent calc activity, unlike the other three dimensions,
	/// which read HitMatch); the second hit also carries HitMatch 0 so the record-based dimensions see a
	/// non-authoritative hit too.</summary>
	private static BattleSession TkExportSession()
	{
		var s = new BattleSession();
		s.InBattle = true;
		var victim = new ActorStats { Key = 4, Name = "ポポロット", Team = TeamType.Ally };
		victim.Position = 1;
		s.OrderedActors.Add(victim);
		s.Events.Add(TkExpHit(12.5, "A", 1, "毒", "ボス(付与)"));
		s.Events.Add(TkExpHit(13.0, "C:calcA", 0, "", ""));
		return s;
	}

	private static BattleEvent TkExpHit(double t, string attr, int hitMatch, string status, string applier)
	{
		return new BattleEvent
		{
			Type = "dmg",
			T = t,
			Victim = "ポポロット",
			VictimKey = 4,
			VictimTeam = 1,
			Attacker = "ボス\"引用\"\n改行",
			AttackerKey = 9,
			AttackerTeam = 2,
			Attr = attr,
			Friendly = false,
			Source = 1,
			CalcHitType = 1,
			CalcEffectId = 77,
			HitMatch = hitMatch,
			Amount = 100,
			Nominal = 250,
			StatusDelta = status,
			StatusApplier = applier,
		};
	}

	/// <summary>The keys R80 fixed, plus the section's own tags. Anything else is not this test's business.</summary>
	private static readonly string[] TkTextKeys =
	{
		"schemaVersion", "method", "basis", "name", "positionLabel", "quality", "status", "applier",
	};

	private static void TkWalkText(JsonElement e, ref int seen, ref int bad)
	{
		if (e.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty p in e.EnumerateObject())
			{
				if (System.Array.IndexOf(TkTextKeys, p.Name) >= 0)
				{
					seen++;
					if (p.Value.ValueKind != JsonValueKind.String) bad++;
				}
				TkWalkText(p.Value, ref seen, ref bad);
			}
		}
		else if (e.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in e.EnumerateArray()) TkWalkText(item, ref seen, ref bad);
		}
	}

	/// <summary>Read a text field without throwing: a missing key, a non-object or a non-string all come
	/// back as visible markers, so the case fails with the reason instead of an exception.</summary>
	private static string TkText(JsonElement e, string key)
	{
		if (e.ValueKind != JsonValueKind.Object) return "<not-an-object>";
		JsonElement v;
		if (!e.TryGetProperty(key, out v)) return "<missing:" + key + ">";
		if (v.ValueKind != JsonValueKind.String) return "<" + v.ValueKind + ">";
		return v.GetString();
	}

	private static long TkNum(JsonElement e, string key)
	{
		if (e.ValueKind != JsonValueKind.Object) return -1;
		JsonElement v;
		if (!e.TryGetProperty(key, out v) || v.ValueKind != JsonValueKind.Number) return -1;
		return v.GetInt64();
	}

	private static int TkCount(JsonElement e, string key)
	{
		if (e.ValueKind != JsonValueKind.Object) return -1;
		JsonElement v;
		if (!e.TryGetProperty(key, out v) || v.ValueKind != JsonValueKind.Array) return -1;
		int n = 0;
		foreach (JsonElement item in v.EnumerateArray()) n++;
		return n;
	}

	private static JsonElement TkAt(JsonElement e, string key, int index)
	{
		if (e.ValueKind != JsonValueKind.Object) return default(JsonElement);
		JsonElement v;
		if (!e.TryGetProperty(key, out v) || v.ValueKind != JsonValueKind.Array) return default(JsonElement);
		int i = 0;
		foreach (JsonElement item in v.EnumerateArray())
		{
			if (i == index) return item;
			i++;
		}
		return default(JsonElement);
	}
}
