using System;

namespace DpsMeter;

/// <summary>
/// R56 (BID-0/BID-1, plan §4): THE identity of one battle session, carried by the session itself and by
/// every artifact derived from it (summary, export, evidence bundle, overlay, selection file).
///
/// ONE allocation point. <see cref="Aggregator.StartSession"/> creates exactly one of these per REAL
/// session; every view rebuild copies the reference instead of asking for a number (plan §3: a view
/// rebuild must never mint a new id). The fields Id/LaunchId/Sequence are immutable after creation --
/// only Revision/State/CloseReason/ResetCount move, and only at the documented events.
///
/// It is deliberately NOT a second RunId: RunId groups the fragments of one continuous stretch of play
/// (a wave can end a session several times), while this identifies ONE session. Merging them would
/// either lose the per-fragment identity or silently re-introduce the wave merge the project refused.
/// </summary>
public sealed class BattleRef
{
	/// <summary>The full, copyable reference: B-{launchId}-{sequence}. Set once.</summary>
	public string Id = "";

	/// <summary>The per-process launch namespace: {utc:yyyyMMdd-HHmmss}-{16 hex}. Set once.</summary>
	public string LaunchId = "";

	/// <summary>1-based position of this session inside its launch namespace. Set once.</summary>
	public int Sequence;

	/// <summary>How many times this session was reset (F9 / the 重置 button). NOT a new session.</summary>
	public int ResetCount;

	/// <summary>Content-snapshot revision. Starts at 1; only the coordination points below move it.</summary>
	public int Revision = 1;

	/// <summary>live | provisional | final. See <see cref="BattleRefPolicy.CloseState"/>.</summary>
	public string State = BattleRefPolicy.StateLive;

	/// <summary>Why the session closed (end/idle/teardown/restart); "" while it is live.</summary>
	public string CloseReason = "";

	/// <summary>Wall clock (local) when the session started -- the same value the file name uses.</summary>
	public DateTime StartedWall;

	/// <summary>The path this session's export was LAST successfully written to. Bound on the first
	/// successful write so a later re-export cannot silently land somewhere else (plan §5.1).</summary>
	public string ExportPath = "";

	/// <summary>SHA256 of the bytes actually written. "" until a write succeeded -- the copy text must
	/// then say "no analysable file yet" instead of inventing a hash (plan §6).</summary>
	public string ExportSha256 = "";

	/// <summary>The overlay's short tag, "#003". Display only: it is NOT unique across launches.</summary>
	public string ShortTag { get { return BattleRefPolicy.ShortTag(Sequence); } }

	/// <summary>A copy that views may hold without being able to move the live identity.</summary>
	public BattleRef Clone()
	{
		return new BattleRef
		{
			Id = Id, LaunchId = LaunchId, Sequence = Sequence, ResetCount = ResetCount,
			Revision = Revision, State = State, CloseReason = CloseReason, StartedWall = StartedWall,
			ExportPath = ExportPath, ExportSha256 = ExportSha256,
		};
	}
}
