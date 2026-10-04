using System;
using System.Collections.Generic;
using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// RF6a: the master-data official-name rule. The 刻印 slot mixes two OVERLAPPING id spaces (ids 1..14 are
	/// the base engravings, ids 1..28 the variants), so the rule needs the talent-id SIGNATURE as well as the
	/// id. The fixtures below are the measured cases from the source file's documentation.
	/// </summary>
	public static void MasterDataLabel(Runner r)
	{
		Func<string, string, MasterDataLabelPolicy.Candidate> C = (n, s) =>
			new MasterDataLabelPolicy.Candidate { Name = n, Signature = s };

		r.Group("policy/masterdata-signature");
		r.Str("no-talents-is-the-empty-signature", MasterDataLabelPolicy.Signature(null), "");
		r.Str("an-empty-list-is-the-empty-signature", MasterDataLabelPolicy.Signature(new List<int>()), "");
		r.Str("one-talent-is-its-id", MasterDataLabelPolicy.Signature(new List<int> { 1005 }), "1005");
		r.Str("talents-are-sorted-and-joined", MasterDataLabelPolicy.Signature(new List<int> { 5, 3, 3 }), "3,3,5");
		r.Str("the-order-of-the-talents-does-not-matter", MasterDataLabelPolicy.Signature(new List<int> { 3, 5 }),
		      MasterDataLabelPolicy.Signature(new List<int> { 5, 3 }));
		r.True("duplicates-are-a-different-signature",
		       MasterDataLabelPolicy.Signature(new List<int> { 3, 3 }) != MasterDataLabelPolicy.Signature(new List<int> { 3 }));

		r.Group("policy/masterdata-select");
		// MEASURED (source file): ability id 1 / level 1 exists BOTH as 【破壊】の刻印 (talent 6) and as the
		// mutate 魔法与ダメージ+10% (talent 1005). Labelling by id alone would be a confident wrong answer.
		var collision = new List<MasterDataLabelPolicy.Candidate> { C("【破壊】の刻印", "6"), C("魔法与ダメージ+10%", "1005") };
		string label;
		LabelMiss miss;
		r.True("the-base-engraving-resolves-by-its-signature",
		       MasterDataLabelPolicy.Select(collision, "6", out label, out miss));
		r.Str("and-it-resolves-to-the-base-name", label, "【破壊】の刻印");
		r.True("the-variant-resolves-by-the-other-signature",
		       MasterDataLabelPolicy.Select(collision, "1005", out label, out miss));
		r.Str("and-it-resolves-to-the-variant-name", label, "魔法与ダメージ+10%");
		r.True("a-live-signature-nobody-carries-labels-nothing",
		       !MasterDataLabelPolicy.Select(collision, "7", out label, out miss));
		r.True("and-the-label-stays-null", label == null);
		r.Eq("and-it-is-counted-as-no-candidate", (int)miss, (int)LabelMiss.NoCandidate);

		// MEASURED (source file): the base engravings have five LEVEL rows that share one signature and one
		// name, so the name is unambiguous even though the level is not.
		var levels = new List<MasterDataLabelPolicy.Candidate>();
		for (int i = 0; i < 5; i++) levels.Add(C("貫通率+4%", "78,86"));
		r.True("five-level-rows-with-one-name-resolve", MasterDataLabelPolicy.Select(levels, "78,86", out label, out miss));
		r.Str("and-the-shared-name-is-used", label, "貫通率+4%");

		// Two names behind one signature must NOT be guessed.
		var disagree = new List<MasterDataLabelPolicy.Candidate> { C("甲", "9"), C("乙", "9") };
		r.True("disagreeing-names-label-nothing", !MasterDataLabelPolicy.Select(disagree, "9", out label, out miss));
		r.Eq("and-that-is-counted-as-ambiguous", (int)miss, (int)LabelMiss.Ambiguous);

		// A single match whose name is EMPTY, not a missing match: the row exists, it just cannot name it.
		var empty = new List<MasterDataLabelPolicy.Candidate> { C("", "9") };
		r.True("an-empty-resolved-name-labels-nothing", !MasterDataLabelPolicy.Select(empty, "9", out label, out miss));
		r.Eq("and-it-counts-as-ambiguous-not-as-no-candidate", (int)miss, (int)LabelMiss.Ambiguous);
		r.True("the-label-is-null-there-too", label == null);

		// No candidates at all, and the empty signature matching a row that carries no talents.
		r.True("no-candidates-labels-nothing",
		       !MasterDataLabelPolicy.Select(new List<MasterDataLabelPolicy.Candidate>(), "9", out label, out miss));
		r.Eq("and-it-is-no-candidate", (int)miss, (int)LabelMiss.NoCandidate);
		r.True("a-null-list-labels-nothing-too", !MasterDataLabelPolicy.Select(null, "9", out label, out miss));
		r.True("the-empty-signature-matches-a-row-with-no-talents",
		       MasterDataLabelPolicy.Select(new List<MasterDataLabelPolicy.Candidate> { C("無", "") }, "", out label, out miss));
	}
}
