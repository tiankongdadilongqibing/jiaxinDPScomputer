using System;
using System.Collections.Generic;
using DpsMeter;

namespace BehaviorTests;

/// <summary>
/// R56 (plan section 10): the battle reference. What is pinned here is the part a review cannot see:
/// the format cannot truncate, a close without a result cannot claim to be final, a view rebuild cannot
/// mint a number, a colliding namespace is regenerated, and the copy text must not invent a hash for a
/// battle that was never written.
/// </summary>
internal static partial class Cases
{
	private const string L = "20261005-143012-7A2C91EF";

	public static void BattleRefCases(Runner r)
	{
		// ---------------------------------------------------------------------------------------------
		r.Group("bref/format");
		r.Str("a-launch-namespace-is-utc-time-plus-the-token",
		      BattleRefPolicy.FormatLaunchId(new DateTime(2026, 10, 5, 22, 30, 12, DateTimeKind.Utc), "7a2c91ef00000000"),
		      "20261005-223012-7A2C91EF00000000");
		r.Str("the-full-id-is-prefix-launch-sequence", BattleRefPolicy.FormatId(L, 3), "B-" + L + "-003");
		r.Str("the-lowercase-token-is-upcased", BattleRefPolicy.FormatLaunchId(new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc), "abcdef0123456789"),
		      "20261005-000000-ABCDEF0123456789");
		r.Str("the-short-tag-is-hash-padded-to-three", BattleRefPolicy.ShortTag(3), "#003");
		r.Str("sequence-7-pads", BattleRefPolicy.SequenceText(7), "007");
		r.Str("sequence-1000-is-NEVER-truncated", BattleRefPolicy.SequenceText(1000), "1000");
		r.Str("sequence-12345-keeps-every-digit", BattleRefPolicy.SequenceText(12345), "12345");
		r.Str("a-negative-sequence-clamps-to-zero", BattleRefPolicy.SequenceText(-1), "000");

		string launch;
		int seq;
		r.True("a-well-formed-id-parses", BattleRefPolicy.TryParse("B-" + L + "-003", out launch, out seq));
		r.Str("and-the-launch-namespace-comes-back-whole (it contains dashes itself)", launch, L);
		r.Eq("and-the-sequence-comes-back-as-a-number", seq, 3);
		r.True("a-4-digit-sequence-parses", BattleRefPolicy.TryParse("B-" + L + "-1000", out launch, out seq) && seq == 1000);
		string dummy;
		int dummySeq;
		r.True("null-is-not-an-id", !BattleRefPolicy.TryParse(null, out dummy, out dummySeq));
		r.True("an-empty-string-is-not-an-id", !BattleRefPolicy.TryParse("", out dummy, out dummySeq));
		r.True("a-missing-sequence-is-not-an-id", !BattleRefPolicy.TryParse("B-" + L + "-", out dummy, out dummySeq));
		r.True("a-missing-launch-is-not-an-id", !BattleRefPolicy.TryParse("B--003", out dummy, out dummySeq));
		r.True("a-non-numeric-sequence-is-not-an-id", !BattleRefPolicy.TryParse("B-" + L + "-abc", out dummy, out dummySeq));
		r.True("sequence-zero-is-not-an-id", !BattleRefPolicy.TryParse("B-" + L + "-000", out dummy, out dummySeq));
		r.True("a-bare-timestamp-without-the-prefix-is-not-an-id",
		       !BattleRefPolicy.TryParse(L + "-003", out dummy, out dummySeq));

		r.Str("a-legacy-reference-is-the-content-hash", BattleRefPolicy.LegacyRef("AABB"), "legacy:aabb");
		r.True("legacy-references-are-recognised", BattleRefPolicy.IsLegacyRef("legacy:aabb"));
		r.True("a-full-id-is-not-a-legacy-reference", !BattleRefPolicy.IsLegacyRef("B-" + L + "-003"));
		r.Str("the-legacy-hash-comes-back-normalised", BattleRefPolicy.LegacyHash("LEGACY:AABB"), "aabb");
		r.Str("a-non-legacy-string-has-no-legacy-hash", BattleRefPolicy.LegacyHash("B-x-001"), "");

		r.Str("the-file-name-keeps-the-quest-in-its-historical-position",
		      BattleRefPolicy.FileName(411001, new DateTime(2026, 10, 5, 14, 30, 12), "B-" + L + "-003"),
		      "battle_411001_20261005_143012__B-" + L + "-003.json");
		r.Str("without-an-id-the-name-is-the-historical-one",
		      BattleRefPolicy.FileName(411001, new DateTime(2026, 10, 5, 14, 30, 12), null),
		      "battle_411001_20261005_143012.json");
		r.True("a-name-that-ends-with-the-id-matches",
		       BattleRefPolicy.FileNameMatchesId("/x/battle_411001_20261005_143012__B-" + L + "-003.json", "B-" + L + "-003"));
		r.True("another-id-in-the-name-does-NOT-match",
		       !BattleRefPolicy.FileNameMatchesId("/x/battle_411001_20261005_143012__B-" + L + "-004.json", "B-" + L + "-003"));
		r.True("the-historical-name-cannot-be-the-target-of-an-identified-session",
		       !BattleRefPolicy.FileNameMatchesId("/x/battle_411001_20261005_143012.json", "B-" + L + "-003"));

		r.Str("a-self-consistent-block-validates", BattleRefPolicy.ValidateBlock("B-" + L + "-003", L, 3, 1, "final"), "");
		r.True("a-sequence-that-disagrees-with-the-id-is-refused",
		       BattleRefPolicy.ValidateBlock("B-" + L + "-003", L, 4, 1, "final").Length > 0);
		r.True("a-launch-that-disagrees-with-the-id-is-refused",
		       BattleRefPolicy.ValidateBlock("B-" + L + "-003", "other", 3, 1, "final").Length > 0);
		r.True("revision-zero-is-refused", BattleRefPolicy.ValidateBlock("B-" + L + "-003", L, 3, 0, "final").Length > 0);
		r.True("an-unknown-state-is-refused", BattleRefPolicy.ValidateBlock("B-" + L + "-003", L, 3, 1, "done").Length > 0);
		r.True("a-malformed-id-is-refused", BattleRefPolicy.ValidateBlock("nope", L, 3, 1, "final").Length > 0);

		r.Str("a-handed-over-end-with-a-result-is-final", BattleRefPolicy.CloseState("end", 1), "final");
		r.Str("a-lost-end-with-a-result-is-final-too", BattleRefPolicy.CloseState("end", 2), "final");
		r.Str("an-end-WITHOUT-a-result-is-only-provisional", BattleRefPolicy.CloseState("end", 0), "provisional");
		r.Str("an-idle-close-is-provisional", BattleRefPolicy.CloseState("idle", 2), "provisional");
		r.Str("a-teardown-close-is-provisional", BattleRefPolicy.CloseState("teardown", 0), "provisional");
		r.True("only-a-final-session-is-comparable", BattleRefPolicy.IsComparableState("final")
		       && !BattleRefPolicy.IsComparableState("provisional") && !BattleRefPolicy.IsComparableState("live"));

		r.True("same-id-same-revision-different-hash-is-a-conflict",
		       BattleRefPolicy.SameIdDifferentContent("B-x-003", 2, "aa", "B-x-003", 2, "bb"));
		r.True("a-different-revision-is-not-a-conflict", !BattleRefPolicy.SameIdDifferentContent("B-x-003", 2, "aa", "B-x-003", 3, "bb"));
		r.True("a-different-id-is-not-a-conflict", !BattleRefPolicy.SameIdDifferentContent("B-x-003", 2, "aa", "B-x-004", 2, "bb"));

		string copy = BattleRefPolicy.CopyText("B-" + L + "-003", 2, 411001, "final", 1, "f.json", "AABB");
		r.True("the-copy-text-names-the-contract", copy.StartsWith("DpsMeter battle-ref/1"));
		r.True("the-copy-text-carries-the-id", copy.Contains("battleId: B-" + L + "-003"));
		r.True("the-copy-text-carries-the-revision-and-reset-count",
		       copy.Contains("revision: 2") && copy.Contains("resetCount: 1"));
		r.True("the-copy-text-carries-the-file-and-hash", copy.Contains("file: f.json") && copy.Contains("sha256: aabb"));
		string empty = BattleRefPolicy.CopyText("B-" + L + "-003", 1, 411001, "live", 0, null, null);
		r.True("with-no-file-the-copy-text-says-so", empty.Contains("(尚未导出,尚无可分析文件)"));
		r.True("and-it-does-NOT-invent-a-hash", !empty.Contains("sha256: 0") && empty.Contains("sha256: (尚无可分析文件)"));

		// ---------------------------------------------------------------------------------------------
		r.Group("bref/registry");
		int tokens = 0;
		var reg = new BattleRefRegistry(() => new DateTime(2026, 10, 5, 14, 30, 12, DateTimeKind.Utc),
			() => "TOKEN" + (++tokens));
		r.Str("the-first-allocation-gets-sequence-1-and-a-full-id",
		      reg.NewBattle(new DateTime(2026, 10, 5, 14, 30, 12), null).Id, "B-20261005-143012-TOKEN1-001");
		r.Str("the-second-allocation-increments", reg.NewBattle(new DateTime(2026, 10, 5, 14, 30, 20), null).Id,
		      "B-20261005-143012-TOKEN1-002");
		r.Eq("and-the-launch-namespace-did-not-change", reg.Allocated, 2);
		var r3 = reg.NewBattle(new DateTime(2026, 10, 5, 14, 31, 0), null);
		r.Str("every-session-shares-the-launch-namespace", r3.LaunchId, "20261005-143012-TOKEN1");
		r.Str("a-new-session-starts-LIVE-with-revision-1", r3.State + "/" + r3.Revision, "live/1");
		r.Eq("and-with-no-reset", r3.ResetCount, 0);

		// a colliding namespace must be REGENERATED, not reused (plan section 1)
		var collide = new BattleRefRegistry(() => new DateTime(2026, 10, 5, 14, 30, 12, DateTimeKind.Utc),
			() => "TOKEN" + (++tokens));
		int asked = 0;
		BattleRef fresh = collide.NewBattle(new DateTime(2026, 10, 5, 14, 30, 12), id =>
		{
			asked++;
			return asked == 1;   // only the first candidate already exists on disk
		});
		// The count of PROBES is the mutation-sensitive fact: the correct path asks about the first
		// candidate, sees it on disk, regenerates the namespace and asks about the second. An "ignore the
		// collision" edit stops after one question, and a test that compared the id against the counter
		// this code advances would have stayed green either way (measured: R56's first draft did).
		r.Eq("a-colliding-candidate-is-probed-then-regenerated (the probe runs twice)", asked, 2);
		r.Eq("and-the-regenerated-namespace-restarts-at-1", fresh.Sequence, 1);
		r.True("and-the-id-is-well-formed", BattleRefPolicy.TryParse(fresh.Id, out launch, out seq) && seq == 1);

		BattleRef live = reg.NewBattle(new DateTime(2026, 10, 5, 14, 30, 0), null);
		int before = live.Revision;
		BattleRefRegistry.MarkResumed(live);
		r.Str("a-soft-resume-keeps-the-id-and-goes-back-to-live", live.State, "live");
		r.Eq("and-clears-the-close-reason", live.CloseReason.Length, 0);
		r.Eq("and-moves-the-revision (the provisional snapshot is stale)", live.Revision, before + 1);
		int revAfterResume = live.Revision;
		r.Eq("a-render-does-NOT-move-the-revision", live.Revision, revAfterResume);
		BattleRefRegistry.MarkClosed(live, "end", 2);
		r.Str("an-end-with-a-result-marks-it-final", live.State, "final");
		r.Str("and-remembers-why", live.CloseReason, "end");
		BattleRefRegistry.MarkReset(live);
		r.Eq("a-reset-counts", live.ResetCount, 1);
		r.Eq("and-moves-the-revision", live.Revision, revAfterResume + 1);
		BattleRefRegistry.MarkExported(live, "/x/f.json", "deadbeef");
		r.Str("a-successful-write-binds-the-path", live.ExportPath, "/x/f.json");
		r.Str("and-the-hash", live.ExportSha256, "deadbeef");
		// R57 (real machine, 2026-10-05): the evidence bundle is written AFTER the export, and binding the
		// LAST path made the copied reference name extract/<bundle>/battle.json -- a directory the retention
		// policy deletes. The identity must keep pointing at the durable export.
		BattleRefRegistry.MarkExported(live, "/x/f.json", "cafebabe");
		r.Str("a-rewrite-of-the-SAME-path-updates-the-hash", live.ExportSha256, "cafebabe");
		BattleRefRegistry.MarkExported(live, "/extract/extract_x/battle.json", "00112233");
		r.Str("a-later-COPY-can-not-move-the-identity (the bundle is rotated away)", live.ExportPath, "/x/f.json");
		r.Str("and-can-not-borrow-the-hash-either", live.ExportSha256, "cafebabe");

		// ---------------------------------------------------------------------------------------------
		r.Group("bref/lifecycle");
		var origin = new BattleRef
		{
			Id = "B-" + L + "-007", LaunchId = L, Sequence = 7, Revision = 4, State = "final",
			ResetCount = 2, ExportPath = "/x/f.json", ExportSha256 = "aa",
		};
		var summary = new BattleSummary { QuestId = "411001", Ref = origin, Session = null };
		BattleSession view = BattleSession.FromSummary(summary);
		r.True("a-view-rebuild-CARRIES-the-identity", view.Ref != null && view.Ref.Id == origin.Id);
		r.Eq("and-its-revision", view.Ref.Revision, 4);
		r.Eq("and-its-reset-count", view.Ref.ResetCount, 2);
		r.Str("and-the-file-it-points-at", view.Ref.ExportPath, "/x/f.json");
		r.True("the-view-holds-a-COPY (a view must not be able to move the live identity)",
		       !ReferenceEquals(view.Ref, origin));
		view.Ref.Revision = 99;
		r.Eq("so-mutating-the-view-changes-nothing-of-the-summary", origin.Revision, 4);

		var noRef = BattleSession.FromSummary(new BattleSummary { QuestId = "411001", Ref = null });
		r.True("a-summary-without-an-id-yields-a-view-without-one (never a fabricated number)", noRef.Ref == null);

		var liveSession = new BattleSession { Ref = new BattleRef { Id = "B-" + L + "-008", LaunchId = L, Sequence = 8, Revision = 1 } };
		liveSession.ResetActors();
		r.Eq("F9-on-a-session-keeps-the-id-and-counts-the-reset", liveSession.Ref.ResetCount, 1);
		r.Eq("and-moves-the-revision", liveSession.Ref.Revision, 2);
		r.Str("the-id-itself-did-not-change", liveSession.Ref.Id, "B-" + L + "-008");
		liveSession.ResetActors();
		r.Eq("a-second-reset-counts-again", liveSession.Ref.ResetCount, 2);
		var bare = new BattleSession();
		bare.ResetActors();
		r.True("a-session-with-no-identity-resets-without-crashing (legacy path)", bare.Ref == null);
	}
}
