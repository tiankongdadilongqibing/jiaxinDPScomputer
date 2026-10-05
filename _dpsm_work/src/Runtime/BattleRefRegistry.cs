using System;

namespace DpsMeter;

/// <summary>
/// R56 (BID-1, plan §1/§3): the PER-PROCESS launch namespace and the session sequence inside it.
///
/// Why a namespace and not a global counter: a persistent "battle #12345" needs write-interruption,
/// rollback, migration and multi-process handling, and the plan explicitly refused that for the first
/// version. A launch namespace plus a full id already lets a user reference any past battle; the short
/// "#003" is a convenience INSIDE one run and is documented as not unique across runs.
///
/// The time and the random token are injected, so a test can force a collision, a clock rollback or a
/// sequence past 999 without touching the system clock or a global RNG.
/// </summary>
public sealed class BattleRefRegistry
{
	private readonly Func<DateTime> _utcNow;
	private readonly Func<string> _token;
	private string _launchId;
	private int _seq;

	public BattleRefRegistry(Func<DateTime> utcNow, Func<string> token)
	{
		_utcNow = utcNow ?? (() => DateTime.UtcNow);
		_token = token ?? RandomToken;
	}

	/// <summary>The process-wide registry. One launch namespace per plugin load.</summary>
	public static readonly BattleRefRegistry Default =
		new BattleRefRegistry(() => DateTime.UtcNow, RandomToken);

	/// <summary>16 hex characters (64 bits). Not a uniqueness proof -- <see cref="NewBattle"/> still
	/// checks the result against what is already on disk and regenerates when it collides.</summary>
	public static string RandomToken()
	{
		try { return Guid.NewGuid().ToString("N").Substring(0, BattleRefPolicy.TokenHexDigits).ToUpperInvariant(); }
		catch { return "0000000000000000"; }
	}

	public string LaunchId
	{
		get
		{
			if (_launchId == null) _launchId = BattleRefPolicy.FormatLaunchId(_utcNow(), _token());
			return _launchId;
		}
	}

	/// <summary>How many sessions this process has allocated (diagnostics + tests).</summary>
	public int Allocated { get { return _seq; } }

	public int NextSequence() { return ++_seq; }

	/// <summary>Start a fresh namespace (new token, sequence back to 1). Used when the first attempt
	/// collides with an id already on disk.</summary>
	public void Regenerate()
	{
		_launchId = null;
		_seq = 0;
	}

	/// <summary>
	/// Allocate the identity of ONE real session. <paramref name="idExists"/> answers "is this id already
	/// on disk?"; the plan requires a colliding namespace to be REGENERATED rather than reused, so this
	/// loops a bounded number of times and never returns a known-disk id.
	/// </summary>
	public BattleRef NewBattle(DateTime startedWall, Func<string, bool> idExists)
	{
		for (int attempt = 0; attempt < 8; attempt++)
		{
			string launch = LaunchId;
			int seq = NextSequence();
			string id = BattleRefPolicy.FormatId(launch, seq);
			if (idExists == null || !idExists(id))
			{
				return new BattleRef
				{
					Id = id, LaunchId = launch, Sequence = seq, ResetCount = 0, Revision = 1,
					State = BattleRefPolicy.StateLive, CloseReason = "", StartedWall = startedWall,
				};
			}
			Regenerate();
		}
		// Eight colliding namespaces means the collision probe or the token source is broken. Return a
		// well-formed ref anyway: the caller logs it, and a missing id would be far worse than a rare
		// duplicated one (the probe is a cache; the export path check is the real guard).
		string lastLaunch = LaunchId;
		int lastSeq = NextSequence();
		return new BattleRef
		{
			Id = BattleRefPolicy.FormatId(lastLaunch, lastSeq), LaunchId = lastLaunch, Sequence = lastSeq,
			ResetCount = 0, Revision = 1, State = BattleRefPolicy.StateLive, CloseReason = "",
			StartedWall = startedWall,
		};
	}

	/// <summary>Revision coordination (plan §4): the revision belongs to the CONTENT SNAPSHOT, not to the
	/// renderer. It moves only for the three events that make a previously shipped snapshot stale.</summary>
	public static void MarkResumed(BattleRef r)
	{
		if (r == null) return;
		r.State = BattleRefPolicy.StateLive;
		r.CloseReason = "";
		r.Revision++;
	}

	public static void MarkClosed(BattleRef r, string why, int result)
	{
		if (r == null) return;
		r.State = BattleRefPolicy.CloseState(why, result);
		r.CloseReason = why ?? "";
	}

	public static void MarkReset(BattleRef r)
	{
		if (r == null) return;
		r.ResetCount++;
		r.Revision++;
	}

	/// <summary>Called once per successful export. A re-export of an UNCHANGED snapshot keeps the
	/// revision; only a new snapshot (already bumped by the events above) produces a new one.</summary>
	public static void MarkExported(BattleRef r, string path, string sha256)
	{
		if (r == null) return;
		r.ExportPath = path ?? "";
		r.ExportSha256 = sha256 ?? "";
	}
}
