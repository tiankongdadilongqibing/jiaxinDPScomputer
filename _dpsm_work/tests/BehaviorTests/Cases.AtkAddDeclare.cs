using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// R87 (方案A): the self-addend census -- "who could have declared the attack-power addend that the
	/// fold classified as the holder's own".
	///
	/// Why these cases exist at all. Two of the three things this feature rests on cannot be read off the
	/// code:
	///   * the talent-type table (TalentDefine.Type 6/8 to the name ParamData.Type prints) and the
	///     reference-code table (51..54 to the name inside the Ref string) are MEASURED. A wrong row does
	///     not crash; it silently turns a matchable entry into `undeclared`, which reads as "nobody granted
	///     this" and is exactly the kind of quiet wrong answer this repo refuses;
	///   * the team scope is the whole reason the match is decidable (this content fields the same
	///     character on both sides), and dropping it would look like it still worked while handing a
	///     mirrored unit's grant to ours.
	/// So the tables, the key symmetry, the scope and the two identities are executed here rather than
	/// asserted in a comment.
	///
	/// The last case is the R80 lesson: a section whose WRITER emits unquoted strings is a file no parser
	/// can read, and it shipped once because the writer sat in a file the suite cannot compile. The writer
	/// is pure now, so the bytes are parsed back here.
	/// </summary>
	internal static void AtkAddDeclareCases(Runner r)
	{
		r.Group("atkadd/self-declared");

		// ---- the measured tables ----------------------------------------------------------------------
		// ネフェスティス 被动 id20076 「棺の解放」 declares type=6 and the battle emitted Rate+30 from it;
		// [闇惑]モネモネ 潜在 id88 declares type=8 and the battle emitted Actual+4 from it. The other
		// talent types are deliberately NOT mapped: 攻击力%- is not an addition, and inventing a row for
		// it would be a guess.
		r.Str("the-talent-type-table-maps-only-the-two-addend-forms",
			AtkAddDeclarePolicy.TalentTypeName(6) + "/" + AtkAddDeclarePolicy.TalentTypeName(8)
			+ "/" + AtkAddDeclarePolicy.TalentTypeName(7) + "/" + AtkAddDeclarePolicy.TalentTypeName(0),
			"Rate/Actual//");

		// Each row is pinned by an ability whose printed NAME states the same thing, so the table can be
		// re-derived from the corpus: 刻印 id25 「現在耐久の2%…」 = 51, トレイラ 潜在 id84 = 52,
		// 刻印 id23 「現在物理防御の50%…」 = 53, 刻印 id24 「現在魔法防御の50%…」 = 54.
		r.Str("the-reference-code-table-maps-the-four-measured-codes",
			AtkAddDeclarePolicy.RefTypeName(51) + "/" + AtkAddDeclarePolicy.RefTypeName(52) + "/"
			+ AtkAddDeclarePolicy.RefTypeName(53) + "/" + AtkAddDeclarePolicy.RefTypeName(54) + "/"
			+ AtkAddDeclarePolicy.RefTypeName(0),
			"CurrentLife/CurrentPower/CurrentDefense/CurrentMagicDefense/");

		// An unmodelled code must produce NO key, which the census then reports as `undeclared`. Returning
		// a made-up name would silently match nothing OR match the wrong thing; both are worse than saying
		// "we do not know".
		r.True("an-unmodelled-reference-code-is-refused-not-guessed",
			AtkAddDeclarePolicy.DeclKey(8, 4, 0, 99).Length == 0
			&& AtkAddDeclarePolicy.DeclKey(42, 4, 0, 51).Length == 0
			&& AtkAddDeclarePolicy.DeclKey(8, 4, 0, 51).Length > 0);

		// ---- THE invariant: the two sides build the same key -------------------------------------------
		// The declaration side reads a talent (Type, P0, P1, P2); the runtime side reads ParamData. If
		// these two ever disagreed the census could only ever report `undeclared`, and the failure would
		// look like "no unit granted this" instead of "the key builder is wrong".
		string dkey = AtkAddDeclarePolicy.DeclKey(8, 4, 0, 51);
		string rkey = AtkAddDeclarePolicy.RuntimeKey("Actual", 4, 0, "CurrentLife");
		r.Str("a-declared-triple-and-its-runtime-item-produce-the-same-key", rkey, dkey);
		r.Str("and-the-key-names-the-measured-facts", dkey, "Actual|4|0|CurrentLife");

		// ネフェスティス 被动 id20076 declares type=6 p=[30,150,0] and the battle prints `Rate+30` with NO
		// reference at all. P2 == 0 therefore means "no reference", and P1 (150) must NOT become the ref
		// param; if it did, the declaration could never match its own runtime item.
		r.Str("a-declaration-without-a-reference-ignores-its-p1",
			AtkAddDeclarePolicy.DeclKey(6, 30, 150, 0),
			AtkAddDeclarePolicy.RuntimeKey("Rate", 30, 0, null));
		r.Str("a-declaration-without-a-reference-prints-no-ref-in-its-label",
			AtkAddDeclarePolicy.ItemLabel("Rate", 30, null, 0),
			"Rate+30");
		r.Str("the-item-label-reproduces-the-log-text",
			AtkAddDeclarePolicy.ItemLabel("Actual", 4, "CurrentLife", 0),
			"Actual+4(/refCurrentLife0)");
		r.Str("a-fixed-type-addend-is-not-modelled",
			AtkAddDeclarePolicy.RuntimeKey("Fixed", 4, 0, null),
			"");

		// ---- the team-scoped declarer match ------------------------------------------------------------
		// モネモネ (key 14, team 1) is the only unit of OUR side that declares (8, 4, 0, 51) in the
		// reference battle, and the addend was found sitting on 7 different allied holders. 紫の幽霊
		// (key 6) is one of them and declares nothing itself -- which is why the roster has to carry the
		// HOLDER's team independently of who declares what.
		var one = new List<AtkAddRosterUnit>
		{
			Decl(U(14, "[闇惑]モネモネ", 1), 8, 4, 0, 51),
			U(6, "紫の幽霊", 1),
		};
		var entries = new List<AtkAddSelfEntry> { E(6, "Actual|4|0|CurrentLife", "Actual+4(/refCurrentLife0)", 3) };
		AtkAddCensus c1 = AtkAddCensusPolicy.Build(entries, 3, one);
		r.Eq("one-declarer-of-the-holders-team-matches", c1.Matched, 3);
		r.Eq("and-the-giver-is-named", c1.Givers.Count, 1);
		r.Str("and-the-giver-carries-its-own-key-and-name",
			c1.Givers[0].Key + "/" + c1.Givers[0].Name + "/" + c1.Givers[0].Entries,
			"14/[闇惑]モネモネ/3");
		r.Str("and-the-giver-lists-what-it-was-credited-for", c1.Givers[0].Items[0], "Actual+4(/refCurrentLife0)");

		// Two units of the HOLDER'S OWN team declaring the same triple: refused, never picked, and the
		// candidate names are kept so the refusal is inspectable rather than a bare count.
		var two = new List<AtkAddRosterUnit>
		{
			Decl(U(14, "[闇惑]モネモネ", 1), 8, 4, 0, 51),
			Decl(U(21, "别人", 1), 8, 4, 0, 51),
			U(6, "紫の幽霊", 1),
		};
		AtkAddCensus c2 = AtkAddCensusPolicy.Build(entries, 3, two);
		r.Eq("two-declarers-of-the-holders-team-are-refused", c2.Ambiguous, 3);
		r.Eq("and-nobody-is-credited", c2.Matched + c2.Givers.Count, 0);
		r.Str("and-the-refusal-names-its-candidates", c2.AmbiguousItems[0].Candidates, "[闇惑]モネモネ, 别人");

		// The SAME declaration on the ENEMY team must not count: this content fields the same character on
		// both sides, so a name- or key-only match would hand our unit's grant to the enemy copy. The
		// holder here is on team 1, so a team-2 declarer is simply not a candidate.
		var other = new List<AtkAddRosterUnit>
		{
			U(6, "紫の幽霊", 1),
			Decl(U(7, "ソフィー", 2), 8, 4, 0, 51),
		};
		AtkAddCensus c3 = AtkAddCensusPolicy.Build(entries, 3, other);
		r.Eq("a-declarer-of-the-other-team-does-not-count", c3.Undeclared, 3);
		r.Eq("and-it-credits-nobody", c3.Matched + c3.Ambiguous + c3.Givers.Count, 0);

		// A holder with no roster row has no TEAM, so there is no scope to apply and any pick would be a
		// guess. Counted separately from `undeclared`, because the two mean different things: "we know who
		// you are and nobody declares this" vs "we do not even know which side you are on".
		AtkAddCensus c4 = AtkAddCensusPolicy.Build(
			new List<AtkAddSelfEntry> { E(0, "Actual|4|0|CurrentLife", "Actual+4(/refCurrentLife0)", 2) }, 2, one);
		r.Eq("a-holder-without-a-roster-row-is-counted-not-guessed", c4.NoHolder, 2);
		r.Eq("and-it-is-not-filed-as-undeclared", c4.Undeclared, 0);

		// Nothing declares it at all (including the unmodelled-code case, whose runtime key is "").
		AtkAddCensus c5 = AtkAddCensusPolicy.Build(
			new List<AtkAddSelfEntry>
			{
				E(6, "Actual|100|0|CurrentPower", "Actual+100(/refCurrentPower0)", 5),
				E(6, "", "", 1),
			}, 6, new List<AtkAddRosterUnit> { U(6, "紫の幽霊", 1) });
		r.Eq("an-undeclared-addend-is-counted", c5.Undeclared, 6);
		r.Eq("and-an-unmodelled-addend-falls-into-the-same-bucket", c5.UndeclaredItems.Count, 2);

		// A unit may carry the SAME declaration twice (measured: ネフェスティス holds 刻印 id25 twice).
		// That is one declarer, not an ambiguity -- deduplicating by unit key is what keeps it that way.
		var dup = new List<AtkAddRosterUnit>
		{
			Decl(Decl(U(14, "[闇惑]モネモネ", 1), 8, 4, 0, 51), 8, 4, 0, 51),
			U(6, "紫の幽霊", 1),
		};
		r.Eq("a-unit-declaring-the-same-thing-twice-is-one-declarer",
			AtkAddCensusPolicy.Build(entries, 3, dup).Matched, 3);

		// ---- the two identities ------------------------------------------------------------------------
		// Entries is the reference-aware count, Values is the fold's own counter. A fold key can only be
		// SPLIT by the extra reference dimension, so the difference is never negative and must be reported.
		var mixed = new List<AtkAddSelfEntry>
		{
			E(6, "Actual|4|0|CurrentLife", "Actual+4(/refCurrentLife0)", 3),
			E(6, "Actual|100|0|CurrentDefense", "Actual+100(/refCurrentDefense0)", 4),
			E(9, "Actual|2|0|CurrentLife", "Actual+2(/refCurrentLife0)", 2),
		};
		var mixedRoster = new List<AtkAddRosterUnit>
		{
			Decl(U(14, "[闇惑]モネモネ", 1), 8, 4, 0, 51),
			Decl(U(14, "[闇惑]モネモネ", 1), 8, 100, 0, 53),
			U(6, "紫の幽霊", 1),
			U(9, "ネフェスティス", 1),
		};
		AtkAddCensus c6 = AtkAddCensusPolicy.Build(mixed, 8, mixedRoster);
		r.Eq("the-entries-identity-holds", c6.Entries, c6.Values + c6.DupKeyEntries);
		r.Eq("and-its-two-sides-are-what-they-claim", c6.Entries + c6.DupKeyEntries, 9 + 1);
		r.Eq("the-bucket-identity-holds",
			c6.Entries, c6.Matched + c6.Ambiguous + c6.Undeclared + c6.NoHolder);
		r.Str("and-the-buckets-are-the-expected-ones", c6.Matched + "/" + c6.Undeclared, "7/2");

		// ---- the writer's bytes ------------------------------------------------------------------------
		var sb = new StringBuilder();
		AtkAddCensusPolicy.AppendJson(sb, c6);
		bool parsed = true;
		string names = "";
		try
		{
			using (JsonDocument doc = JsonDocument.Parse(sb.ToString()))
			{
				JsonElement root = doc.RootElement;
				names = root.GetProperty("givers")[0].GetProperty("name").GetString();
			}
		}
		catch { parsed = false; }
		r.True("the-census-json-is-parseable-and-quotes-its-strings", parsed);
		r.Str("and-a-cjk-name-reads-back-verbatim", names, "[闇惑]モネモネ");

		var sb2 = new StringBuilder();
		AtkAddCensusPolicy.AppendJson(sb2, null);
		r.Str("a-null-census-writes-an-empty-object", sb2.ToString(), "{}");
	}

	private static AtkAddRosterUnit U(int key, string name, int team)
	{
		return new AtkAddRosterUnit { Key = key, Name = name, Team = team };
	}

	private static AtkAddRosterUnit Decl(AtkAddRosterUnit u, int type, int p0, int p1, int p2)
	{
		string k = AtkAddDeclarePolicy.DeclKey(type, p0, p1, p2);
		if (k.Length > 0 && !u.DeclKeys.Contains(k)) u.DeclKeys.Add(k);
		return u;
	}

	private static AtkAddSelfEntry E(int attackerKey, string key, string label, int count)
	{
		return new AtkAddSelfEntry { AttackerKey = attackerKey, Key = key, Label = label, Count = count };
	}
}
