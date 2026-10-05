using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DpsMeter;

/// <summary>
/// One multiplier handed to the contribution core (a projection of <see cref="FoldStep"/>).
/// Kept as a plain record so the core has NO dependency on the composition module or on Plugin:
/// that is what lets recon_probe compile and assert it offline.
/// </summary>
public sealed class ContributionFold
{
	public string Kind = "";
	public string Side = "";
	public string Origin = "";
	public string Label = "";
	public string ByUnit;
	public double Factor = 1.0;
}

/// <summary>One damage event handed to the contribution core (a projection of <see cref="BattleEvent"/>).</summary>
public sealed class ContributionHit
{
	public double Damage;
	public int AttackerKey;
	public bool HasCalc;
	public int FoldDropped;
	public List<ContributionFold> Folds = new List<ContributionFold>();
	/// <summary>R52: the victim of this hit, carried only for the unresolved census (which unit is on the
	/// receiving end of a rule nobody claimed). Empty on the pre-R52 paths that do not set it.</summary>
	public string Victim = "";
	public int VictimKey;
}

/// <summary>One roster entry handed to the contribution core (a projection of <see cref="ActorStats"/>).</summary>
public sealed class ContributionActor
{
	public int Key;
	public string Name = "";
	public int Team;
	public string Kind = "";
	public bool Summon;
	public List<int> AbilityIds = new List<int>();
	public List<string> AbilityNames = new List<string>();
	/// <summary>R52: "&lt;type&gt;/&lt;param&gt;" of every talent this actor holds whose condition names the
	/// GRANT channel (GiveTalent...) -- the loadout-side evidence for "who could have handed this
	/// modifier to the victim". Filled by ContributionSession from the same roster the export writes.</summary>
	public List<string> Grants = new List<string>();
}

/// <summary>Numbers the core computed; returned so the caller can log/assert them.</summary>
public sealed class ContributionStats
{
	public double Analyzable;
	public double Attributed;
	public double Unattributed;
	public double PoolTotal;
	public int Hits;
	public int Folds;
	public int ZeroFactor;
	public int NoopFactor;
	public int SubUnity;
	public int Negative;
	public int CalcMissing;
	public int FoldDropped;
	public int FoldDroppedHits;
	public Dictionary<string, int> ReasonCounts = new Dictionary<string, int>();

	// ---- P0-B (1.7.8): the damage ledger -----------------------------------------------------------------
	// analyzableDealt is NOT "the battle's damage": it contains only hits whose attacker is a team member
	// with a resolvable key. Everything else is counted here, and the buckets stay SEPARATE because they
	// mean different things (see CONTRIBUTION-DATA-DICTIONARY): excludedDamage is a model exclusion with
	// event evidence, outsideTeamDealt is out of scope BY DESIGN, and reconciliationGap is what no event
	// explains. Merging them would blame the model for the other team's damage.
	public int Events;
	public double EventSumAll;
	public int OutsideTeamHits;
	public double OutsideTeamDamage;
	public int UnknownAttackerHits;
	public double UnknownAttackerDamage;
	/// <summary>The game's own dealt total (export totals.dealt), or NaN when the caller does not know it.</summary>
	public double TotalsDealt = double.NaN;

	public double ExcludedDamage { get { return UnknownAttackerDamage; } }
	/// <summary>Damage the game counted that NO bucket explains. Note what is NOT subtracted: the
	/// unresolvable-attacker pool, because that damage is not part of totals.dealt at all (the game
	/// reports it separately as totals.unattributedDamage) -- measured 2026-10-04 on 1.7.6:
	/// analyzable 190,889,625 + outside 94,717 = 190,984,342 = totals.dealt EXACTLY, and
	/// unknown 273,702 = totals.unattributedDamage EXACTLY.</summary>
	public double ReconciliationGap
	{
		get
		{
			if (double.IsNaN(TotalsDealt)) return double.NaN;
			return TotalsDealt - (Analyzable + OutsideTeamDamage);
		}
	}
	public double AnalysisDamageCoverage { get { return TotalsDealt > 0.0 ? Analyzable / TotalsDealt : double.NaN; } }
	public double CreditCoverageWithinAnalyzed { get { return Analyzable != 0.0 ? Attributed / Analyzable : double.NaN; } }
	public double OverallAttributedCoverage { get { return TotalsDealt > 0.0 ? Attributed / TotalsDealt : double.NaN; } }

	public double CreditedShare { get { return Analyzable != 0 ? Attributed / Analyzable : 0.0; } }
}

/// <summary>
/// 1.6.0 (阶段 E): the CONTRIBUTION section of the export.
///
/// WHY IT EXISTS. The offline analysis (dpsmeter_contrib.py, then _dpsm_work/contrib/) answered
/// "who gets credit for which damage" from the exported per-hit fold list. That made the answer
/// reproducible but not self-contained: every reader had to re-derive the attribution ladder, and any
/// disagreement was invisible. This emits the result AT EXPORT TIME, from the same per-hit data, so the
/// plugin's own export states it -- and two independent implementations (C# here, Python offline) can be
/// diffed field by field (see contrib/crosscheck.py).
///
/// CONTRACT. _dpsm_work/CONTRIBUTION-DATA-DICTIONARY.md. In one line: per hit, M = product of the
/// identified multipliers, base = D/M goes to the attacker, and the pool D-base is split between the
/// rules that produced it by ln(factor)/ln(M); the rule's owner (giver / applier / ability holder /
/// global rule owner) is resolved by a fixed ladder, and anything unresolved goes to an unattributed
/// pool that is listed separately -- never silently charged to the attacker.
///
/// DISCIPLINE. Derived from the event list at export time, never accumulated during the battle (the
/// same rule CalcReconcile follows): a battle-long accumulator would be a second source of truth and
/// exactly the kind of drift this project keeps paying for.
/// </summary>
/// <summary>One character's credit row (what the overlay shows and what the JSON writes).</summary>
public sealed class ContributionActorRow
{
	public int Key;
	public string Name = "";
	public string Kind = "";
	public bool Summon;
	public double Direct, Base, Self, Assist, Received;
	public int Hits;
	public double Total { get { return Base + Self + Assist; } }
}

/// <summary>One rule's damage equivalent, with its owner (the dashboard's rule table).</summary>
public sealed class ContributionRuleRow
{
	public string Name = "";
	public string Kind = "";
	public string Side = "";
	public string OwnerName = "";
	public int OwnerKey = -1;
	public int Hits;
	public int Folds;
	public double Damage;
}

/// <summary>One provider -> beneficiary relation.</summary>
public sealed class ContributionLinkRow
{
	public int From, To, Hits, Folds;
	public double Amount;
}

/// <summary>Unattributed credit, always listed separately (never charged to the attacker).</summary>
public sealed class ContributionUnattributedRow
{
	public string Reason = "";
	public double Amount;
	public int Folds;
}

/// <summary>
/// R52 (证据提取流程): ONE GROUP of unresolved folds, with everything needed to answer "what is this
/// residual made of" without writing a one-off script.
///
/// WHY IT IS NOT IN THE EXPORT. The section's shape is pinned by contrib/crosscheck.py against an
/// independent implementation; adding keys there would move a frozen contract for a diagnostic. The
/// evidence bundle carries it instead (Diagnostics/EvidenceExtractor.cs), computed by THIS same loop so
/// the bundle and the file can never disagree about which folds were unresolved.
///
/// THE CARRIER VERDICT IS AN INFERENCE AND IS LABELLED AS ONE. For the granted channel
/// (kind="given", origin "given#&lt;i&gt;/&lt;type&gt;/&lt;param&gt;") the runtime giver read has measured
/// 0 successes, so this records a SECOND, independent route: which team actors HOLD an ability whose
/// talent list grants exactly that (type,param) through GiveTalent(...). "unique" is a determinate
/// candidate; "ambiguous" is a real ambiguity and must never be merged; "none" means this route found
/// nothing and the fold stays unexplained. Nothing here changes any credit.
/// </summary>
public sealed class ContributionUnresolvedRow
{
	public string Reason = "";
	public string Kind = "";
	public string Side = "";
	public string Origin = "";
	public string Label = "";
	public string RuleName = "";
	public double Factor = 1.0;
	public int Folds;
	public double Amount;
	/// <summary>How many DISTINCT victim actor slots carried this fold (76 ショゴス spawns, not 1 name).</summary>
	public int VictimInstances;
	/// <summary>Most frequent victim display name and its fold count (the export keys victims by name).</summary>
	public string VictimTop = "";
	public int VictimTopFolds;
	/// <summary>"unique" | "ambiguous" | "none" | "" (not a granted-channel fold).</summary>
	public string CarrierVerdict = "";
	public int CarrierCount;
	/// <summary>Candidate holder display names, comma separated, capped for size.</summary>
	public string CarrierNames = "";
}

/// <summary>
/// The computed contribution view. ONE implementation feeds both the export's JSON and the overlay
/// dashboard: a second compute path for the UI would be a second source of truth, which is the exact
/// failure mode this project keeps paying for.
/// </summary>
public sealed class ContributionResult
{
	public readonly List<ContributionActorRow> Actors = new List<ContributionActorRow>();
	public readonly List<ContributionRuleRow> Rules = new List<ContributionRuleRow>();
	public readonly List<ContributionLinkRow> Links = new List<ContributionLinkRow>();
	public readonly List<ContributionUnattributedRow> Unattributed = new List<ContributionUnattributedRow>();
	/// <summary>R52: the grouped unresolved folds (a diagnostic; the export does not write it).</summary>
	public readonly List<ContributionUnresolvedRow> Unresolved = new List<ContributionUnresolvedRow>();
	public ContributionStats Stats = new ContributionStats();

	/// <summary>False when the inputs cannot support the model at all (e.g. no composition data).
	/// The UI must then show "不可用" and never zeros -- a measured 0 and "not computed" are different
	/// statements, and this project has already paid for confusing them once.</summary>
	public bool Usable = true;
	public string Unavailable;
}

public static class Contribution
{
	private const double Eps = 1e-12;

	/// <summary>Best display name of a rule: the [name] inside the label, else the label, else the origin.</summary>
	private static string RuleName(ContributionFold f)
	{
		string lab = f.Label ?? "";
		int a = lab.IndexOf('['), b = lab.IndexOf(']');
		if (a >= 0 && b > a) return lab.Substring(a + 1, b - a - 1);
		if (lab.Trim().Length > 0) return lab.Trim();
		return f.Origin ?? "";
	}

	/// <summary>Ability id out of a text/talent origin ("text#1/10110/c3" -> 10110). 0 when absent.</summary>
	private static int AbilityIdOf(string origin)
	{
		if (string.IsNullOrEmpty(origin)) return 0;
		if (!(origin.StartsWith("text#") || origin.StartsWith("talent#"))) return 0;
		int i = origin.IndexOf('#') + 1;
		if (i <= 0 || i >= origin.Length) return 0;
		if (origin.Substring(i).StartsWith("buff#")) i += 5;
		int slash = origin.IndexOf('/', i);
		if (slash < 0) return 0;
		int j = slash + 1, k = j;
		while (k < origin.Length && char.IsDigit(origin[k])) k++;
		if (k == j) return 0;
		if (k < origin.Length && origin[k] != '/') return 0;
		int id;
		return int.TryParse(origin.Substring(j, k - j), NumberStyles.Integer, CultureInfo.InvariantCulture, out id) ? id : 0;
	}

	/// <summary>R52: "given#4/1006/-10" -&gt; "1006/-10" (the granted modifier's identity, the same key the
	/// actor's Grants list uses). Null for any other channel, so only the granted channel is probed.</summary>
	private static string GrantKeyOf(string origin)
	{
		if (string.IsNullOrEmpty(origin) || !origin.StartsWith("given#", StringComparison.Ordinal)) return null;
		int slash = origin.IndexOf('/');
		if (slash < 0 || slash + 1 >= origin.Length) return null;
		// Everything after "given#<index>/" is the granted modifier's identity ("1006/-10").
		return origin.Substring(slash + 1);
	}

	private sealed class Index
	{
		public readonly Dictionary<int, ContributionActor> Team = new Dictionary<int, ContributionActor>();
		public readonly Dictionary<string, ContributionActor> NameUnique = new Dictionary<string, ContributionActor>();
		public readonly HashSet<string> AmbiguousTeamNames = new HashSet<string>();
		public readonly HashSet<string> AllNames = new HashSet<string>();
		/// <summary>P0-B: keys we know belong to a team other than the analysed one, so a skipped hit
		/// can be classified as "out of scope" instead of "unresolvable".</summary>
		public readonly HashSet<int> OtherTeamKeys = new HashSet<int>();
		public readonly Dictionary<int, List<ContributionActor>> ByAbilityId = new Dictionary<int, List<ContributionActor>>();
		public readonly Dictionary<string, List<ContributionActor>> ByAbilityName = new Dictionary<string, List<ContributionActor>>();
		/// <summary>R52: granted modifier ("1006/-10") -&gt; the team actors that HOLD a rule granting it.</summary>
		public readonly Dictionary<string, List<ContributionActor>> ByGrant = new Dictionary<string, List<ContributionActor>>();
	}

	private static void AddHolder<T>(Dictionary<T, List<ContributionActor>> map, T key, ContributionActor actor)
	{
		List<ContributionActor> list;
		if (!map.TryGetValue(key, out list)) { list = new List<ContributionActor>(); map[key] = list; }
		for (int i = 0; i < list.Count; i++) if (list[i].Key == actor.Key) return;
		list.Add(actor);
	}

	/// <summary>
	/// Team-scoped index. A NAME that denormalises to two or more of our own actors is recorded as
	/// ambiguous on purpose: same-kind summons and mirror tokens exist in this content, and picking one
	/// of them silently is the failure mode the whole stable-key design exists to prevent.
	/// </summary>
	private static Index BuildIndex(IList<ContributionActor> roster, int team)
	{
		var ix = new Index();
		var seen = new Dictionary<string, List<ContributionActor>>();
		for (int i = 0; i < roster.Count; i++)
		{
			ContributionActor a = roster[i];
			ix.AllNames.Add(a.Name);
			if (a.Team != team) { ix.OtherTeamKeys.Add(a.Key); continue; }
			ix.Team[a.Key] = a;
			List<ContributionActor> l;
			if (!seen.TryGetValue(a.Name, out l)) { l = new List<ContributionActor>(); seen[a.Name] = l; }
			l.Add(a);
			for (int j = 0; j < a.AbilityIds.Count; j++) AddHolder(ix.ByAbilityId, a.AbilityIds[j], a);
			for (int j = 0; j < a.AbilityNames.Count; j++) AddHolder(ix.ByAbilityName, a.AbilityNames[j], a);
			for (int j = 0; j < a.Grants.Count; j++) AddHolder(ix.ByGrant, a.Grants[j], a);
		}
		foreach (var kv in seen)
		{
			if (kv.Value.Count == 1) ix.NameUnique[kv.Key] = kv.Value[0];
			else ix.AmbiguousTeamNames.Add(kv.Key);
		}
		return ix;
	}

	/// <summary>The attribution ladder (dictionary section 4). Returns null = unattributed + a reason code.</summary>
	private static ContributionActor Resolve(Index ix, ContributionFold f, ContributionActor attacker, out string reason)
	{
		if (!string.IsNullOrEmpty(f.ByUnit))
		{
			ContributionActor r;
			if (ix.NameUnique.TryGetValue(f.ByUnit, out r)) { reason = "byUnit"; return r; }
			if (ix.AmbiguousTeamNames.Contains(f.ByUnit)) { reason = "byUnit_ambiguous"; return null; }
			if (ix.AllNames.Contains(f.ByUnit)) { reason = "byUnit_outside"; return null; }
			reason = "byUnit_unknown"; return null;
		}
		if (f.Kind == "text" || f.Kind == "talent")
		{
			int aid = AbilityIdOf(f.Origin);
			List<ContributionActor> holders = null;
			if (aid != 0) ix.ByAbilityId.TryGetValue(aid, out holders);
			if (holders != null && holders.Count == 1) { reason = "ability_holder_unique"; return holders[0]; }
			if (holders != null && holders.Count > 1)
			{
				for (int i = 0; i < holders.Count; i++)
					if (holders[i].Key == attacker.Key) { reason = "ability_holder_attacker"; return attacker; }
				reason = "ambiguous_multi_holder"; return null;
			}
			reason = "attacker_default"; return attacker;
		}
		if (f.Kind == "global")
		{
			string lab = f.Label ?? "";
			int a = lab.IndexOf('['), b = lab.IndexOf(']');
			List<ContributionActor> holders = null;
			if (a >= 0 && b > a) ix.ByAbilityName.TryGetValue(lab.Substring(a + 1, b - a - 1), out holders);
			if (holders != null && holders.Count == 1) { reason = "global_name_unique"; return holders[0]; }
			reason = "global_ambiguous"; return null;
		}
		if (f.Kind == "given")
		{
			// R54 (user request): the granted 「阻挡增伤」 family gets its OWN reason instead of being mixed into
			// unknown_kind, so the residual can be read as "this much is that one channel, and here is how close
			// we are to naming its provider". STILL UNATTRIBUTED on purpose -- this is a label, not a credit.
			// The holder set comes from the same roster index the census uses, so the two cannot disagree.
			List<ContributionActor> holders = null;
			string gk = GrantKeyOf(f.Origin);
			if (gk != null) ix.ByGrant.TryGetValue(gk, out holders);
			if (holders == null || holders.Count == 0) { reason = "given_carrier_none"; return null; }
			if (holders.Count == 1) { reason = "given_carrier_one"; return null; }
			reason = "given_carrier_ambiguous"; return null;
		}
		reason = "unknown_kind"; return null;
	}

	/// <summary>R52: fold one unresolved fold into the census group, and (for the granted channel only)
	/// record the loadout-side carrier verdict. Credit is NOT touched -- see ContributionUnresolvedRow.</summary>
	private static void NoteUnresolved(Dictionary<string, UnresolvedAcc> map, Index ix, ContributionHit hit,
		ContributionFold f, string reason, double share)
	{
		string key = reason + "|" + f.Kind + "|" + f.Side + "|" + (f.Origin ?? "") + "|" + (f.Label ?? "")
			+ "|" + f.Factor.ToString("R", CultureInfo.InvariantCulture);
		UnresolvedAcc a;
		if (!map.TryGetValue(key, out a))
		{
			a = new UnresolvedAcc
			{
				Reason = reason, Kind = f.Kind ?? "", Side = f.Side ?? "", Origin = f.Origin ?? "",
				Label = f.Label ?? "", RuleName = RuleName(f), Factor = f.Factor,
			};
			map[key] = a;
		}
		a.Amount += share;
		a.Folds++;
		if (!string.IsNullOrEmpty(hit.Victim))
		{
			int n;
			a.Victims.TryGetValue(hit.Victim, out n);
			a.Victims[hit.Victim] = n + 1;
		}
		a.VictimKeys.Add(hit.VictimKey);
		// The carrier verdict answers a DIFFERENT question ("who could have granted it") and only the
		// granted channel has one. Computed once per group, from the same index the ladder uses.
		// R54: the carrier verdict belongs to the GRANTED channel's PROVIDER-UNKNOWN case, whatever code that
		// case got -- the old condition keyed on the reason (unknown_kind), which the split renamed out from
		// under it. A fold that DID carry a byUnit (and failed the NAME lookup instead) is out of scope, so the
		// probe keeps its original meaning: it answers the runtime silent case, not the name-mismatch case.
		if (a.CarrierVerdict.Length == 0 && string.IsNullOrEmpty(f.ByUnit) && GrantKeyOf(f.Origin) != null)
		{
			string gk = GrantKeyOf(f.Origin);
			if (gk != null)
			{
				List<ContributionActor> cand;
				if (!ix.ByGrant.TryGetValue(gk, out cand) || cand == null || cand.Count == 0)
				{
					a.CarrierVerdict = "none";
					a.CarrierCount = 0;
				}
				else if (cand.Count == 1)
				{
					a.CarrierVerdict = "unique";
					a.CarrierCount = 1;
					a.CarrierNames = cand[0].Name ?? "";
				}
				else
				{
					a.CarrierVerdict = "ambiguous";
					a.CarrierCount = cand.Count;
					var sb = new StringBuilder(64);
					for (int i = 0; i < cand.Count && i < 6; i++)
					{
						if (i > 0) sb.Append(", ");
						sb.Append(cand[i].Name ?? "");
					}
					if (cand.Count > 6) sb.Append(", ...");
					a.CarrierNames = sb.ToString();
				}
			}
		}
	}

	/// <summary>P0-B: a ratio whose denominator is undefined is written as JSON null. "Not applicable"
	/// is a different statement from a measured 0, and this project has already paid for that confusion.</summary>
	private static void NumOrNull(StringBuilder sb, double v)
	{
		if (double.IsNaN(v) || double.IsInfinity(v)) { sb.Append("null"); return; }
		sb.Append(v.ToString("F4", CultureInfo.InvariantCulture));
	}

	private static void Num(StringBuilder sb, double v)
	{
		if (double.IsNaN(v) || double.IsInfinity(v)) v = 0.0;   // a guard rail: invalid JSON is worse than a 0
		sb.Append(v.ToString("F4", CultureInfo.InvariantCulture));
	}

	private static void Share(StringBuilder sb, double v)
	{
		if (double.IsNaN(v) || double.IsInfinity(v)) v = 0.0;
		sb.Append(v.ToString("F6", CultureInfo.InvariantCulture));
	}

	private sealed class Credit
	{
		public ContributionActor Actor;
		public double Direct, Base, Self, Assist, Received;
		public int Hits;
	}

	private sealed class RuleOut
	{
		public string Name = "", Kind = "", Side = "", Origin = "", OwnerName = "";
		public int OwnerKey = -1, Folds;
		public double Damage;
		public readonly HashSet<int> Events = new HashSet<int>();
	}

	private sealed class LinkOut
	{
		public int From, To, Folds;
		public double Amount;
		public readonly HashSet<int> Events = new HashSet<int>();
	}

	/// <summary>R52: the mutable accumulator behind one <see cref="ContributionUnresolvedRow"/>.</summary>
	private sealed class UnresolvedAcc
	{
		public string Reason = "", Kind = "", Side = "", Origin = "", Label = "", RuleName = "";
		public double Factor = 1.0;
		public int Folds;
		public double Amount;
		public readonly Dictionary<string, int> Victims = new Dictionary<string, int>();
		public readonly HashSet<int> VictimKeys = new HashSet<int>();
		public string CarrierVerdict = "";
		public int CarrierCount;
		public string CarrierNames = "";
	}

	/// <summary>
	/// Append the "contribution" object. Folds are only read when the caller passes useFolds, which
	/// MUST be the same predicate the export used to emit calc.fold (e.Calc.Valid &amp;&amp; CfgReconcileCalc);
	/// otherwise this section would describe folds the file does not contain and the offline crosscheck
	/// would diverge on purpose.
	/// </summary>
	public static ContributionResult Compute(IList<ContributionHit> hits,
		IList<ContributionActor> roster, int team, double totalsDealt = double.NaN)
	{
		Index ix = BuildIndex(roster, team);
		var credits = new Dictionary<int, Credit>();
		for (int i = 0; i < roster.Count; i++)
			if (roster[i].Team == team) credits[roster[i].Key] = new Credit { Actor = roster[i] };

		var st = new ContributionStats();
		st.TotalsDealt = totalsDealt;
		var rules = new Dictionary<string, RuleOut>();
		var links = new Dictionary<string, LinkOut>();
		var unattr = new Dictionary<string, double[]>();
		// R52: the grouped unresolved folds (diagnostic; never written into the export).
		var unresolved = new Dictionary<string, UnresolvedAcc>();
		int hitIndex = -1;

		for (int hi = 0; hi < hits.Count; hi++)
		{
			ContributionHit hit = hits[hi];
			st.Events++;
			st.EventSumAll += hit.Damage;
			ContributionActor attacker;
			if (hit.AttackerKey == 0 || !ix.Team.TryGetValue(hit.AttackerKey, out attacker))
			{
				// P0-B (1.7.8): a hit we cannot analyse is COUNTED, never silently skipped -- the old
				// `continue` made contribution.hits disagree with reconcile.dmgEvents with nothing in
				// the file to explain the difference.
				if (hit.AttackerKey != 0 && ix.OtherTeamKeys.Contains(hit.AttackerKey))
				{ st.OutsideTeamHits++; st.OutsideTeamDamage += hit.Damage; }
				else
				{ st.UnknownAttackerHits++; st.UnknownAttackerDamage += hit.Damage; }
				continue;
			}
			hitIndex++;
			st.Hits++;
			st.Analyzable += hit.Damage;
			if (!hit.HasCalc) st.CalcMissing++;
			if (hit.FoldDropped > 0) { st.FoldDropped += hit.FoldDropped; st.FoldDroppedHits++; }

			var folds = new List<ContributionFold>();
			for (int i = 0; i < hit.Folds.Count; i++)
			{
				ContributionFold f = hit.Folds[i];
				if (f.Factor <= 0.0) { st.ZeroFactor++; continue; }
				if (Math.Abs(f.Factor - 1.0) < Eps) { st.NoopFactor++; continue; }
				if (f.Factor < 1.0) { st.SubUnity++; continue; }   // a reduction is not a buff-pool member
				folds.Add(f);
			}
			st.Folds += folds.Count;

			double M = 1.0;
			for (int i = 0; i < folds.Count; i++) M *= folds[i].Factor;
			double baseCredit = hit.Damage, pool = 0.0;
			var shares = new double[folds.Count];
			if (folds.Count > 0 && Math.Abs(M - 1.0) >= Eps)
			{
				baseCredit = hit.Damage / M;
				pool = hit.Damage - baseCredit;
				double lnM = Math.Log(M);
				for (int i = 0; i < folds.Count; i++)
					shares[i] = lnM != 0.0 ? pool * (Math.Log(folds[i].Factor) / lnM) : 0.0;
			}
			st.PoolTotal += pool;
			Credit ac = credits[attacker.Key];
			ac.Direct += hit.Damage;
			ac.Hits++;
			ac.Base += baseCredit;

			for (int i = 0; i < folds.Count; i++)
			{
				ContributionFold f = folds[i];
				double share = shares[i];
				if (share < 0.0) st.Negative++;
				string reason;
				ContributionActor owner = Resolve(ix, f, attacker, out reason);
				int rc;
				st.ReasonCounts.TryGetValue(reason, out rc);
				st.ReasonCounts[reason] = rc + 1;

				if (owner == null)
				{
					st.Unattributed += share;
					double[] slot;
					if (!unattr.TryGetValue(reason, out slot)) { slot = new double[2]; unattr[reason] = slot; }
					slot[0] += share; slot[1] += 1.0;
					NoteUnresolved(unresolved, ix, hit, f, reason, share);
					continue;
				}
				string rk = RuleName(f) + "|" + f.Kind + "|" + f.Side + "|" + owner.Name;
				if (owner.Key == attacker.Key)
				{
					ac.Self += share;
				}
				else
				{
					ac.Received += share;
					credits[owner.Key].Assist += share;
					string lk = owner.Key + "|" + attacker.Key;
					LinkOut ls;
					if (!links.TryGetValue(lk, out ls)) { ls = new LinkOut { From = owner.Key, To = attacker.Key }; links[lk] = ls; }
					ls.Amount += share;
					ls.Folds++;
					ls.Events.Add(hitIndex);
				}
				RuleOut rs;
				if (!rules.TryGetValue(rk, out rs))
				{
					rs = new RuleOut { Name = RuleName(f), Kind = f.Kind, Side = f.Side, Origin = f.Origin, OwnerKey = owner.Key, OwnerName = owner.Name };
					rules[rk] = rs;
				}
				rs.Folds++;
				rs.Damage += share;
				rs.Events.Add(hitIndex);
			}
		}

		st.Attributed = 0.0;
		foreach (var c in credits.Values) st.Attributed += c.Base + c.Self + c.Assist;

		var res = new ContributionResult { Stats = st };
		foreach (var c in credits.Values.OrderByDescending(x => x.Base + x.Self + x.Assist))
		{
			if (c.Direct <= 0.0 && c.Base + c.Self + c.Assist <= 0.0) continue;
			res.Actors.Add(new ContributionActorRow
			{
				Key = c.Actor.Key, Name = c.Actor.Name, Kind = c.Actor.Kind, Summon = c.Actor.Summon,
				Direct = c.Direct, Base = c.Base, Self = c.Self, Assist = c.Assist,
				Received = c.Received, Hits = c.Hits,
			});
		}
		foreach (var r in rules.Values.OrderByDescending(x => x.Damage))
			res.Rules.Add(new ContributionRuleRow
			{
				Name = r.Name, Kind = r.Kind, Side = r.Side, OwnerKey = r.OwnerKey, OwnerName = r.OwnerName,
				Hits = r.Events.Count, Folds = r.Folds, Damage = r.Damage,
			});
		foreach (var l in links.Values.OrderByDescending(x => x.Amount))
			res.Links.Add(new ContributionLinkRow
			{
				From = l.From, To = l.To, Amount = l.Amount, Hits = l.Events.Count, Folds = l.Folds,
			});
		foreach (var kv in unattr.OrderByDescending(x => x.Value[0]))
			res.Unattributed.Add(new ContributionUnattributedRow
			{
				Reason = kv.Key, Amount = kv.Value[0], Folds = (int)kv.Value[1],
			});
		// R52: the census, ordered by amount and with the victim top-name chosen DETERMINISTICALLY
		// (count desc, then ordinal name) so two runs on one file cannot order it differently.
		foreach (var kv in unresolved.Values.OrderByDescending(x => x.Amount))
		{
			var row = new ContributionUnresolvedRow
			{
				Reason = kv.Reason, Kind = kv.Kind, Side = kv.Side, Origin = kv.Origin, Label = kv.Label,
				RuleName = kv.RuleName, Factor = kv.Factor, Folds = kv.Folds, Amount = kv.Amount,
				VictimInstances = kv.VictimKeys.Count, CarrierVerdict = kv.CarrierVerdict,
				CarrierCount = kv.CarrierCount, CarrierNames = kv.CarrierNames,
			};
			var names = new List<KeyValuePair<string, int>>(kv.Victims);
			names.Sort((x, y) => x.Value != y.Value ? y.Value - x.Value : string.CompareOrdinal(x.Key, y.Key));
			if (names.Count > 0) { row.VictimTop = names[0].Key; row.VictimTopFolds = names[0].Value; }
			res.Unresolved.Add(row);
		}
		return res;
	}

	/// <summary>Serialize a computed result. Order and spelling are pinned by
	/// _dpsm_work/contrib/crosscheck.py against the offline core.</summary>
	public static void AppendJson(StringBuilder sb, ContributionResult res, string pluginVersion, int quest)
	{
		ContributionStats st = res.Stats;
		sb.Append('{');
		// P0-B (1.7.8): schemaVersion 1.1. NO field was renamed, so a 1.0 reader still finds every key it
		// looked for. What changed is the DEFINITION of totalDamage: it is now analyzableDealt. It used to
		// be analyzableDealt + unattributed, which double-counted the unattributed pool (attributedCredit
		// == analyzableDealt - unattributed, so the old identity attributed+unattributed==totalDamage only
		// held while unattributed was 0 -- true in all 13 exports that carry the section, so invisible).
		sb.Append("\"schemaVersion\":\"1.1\"");
		sb.Append(",\"producer\":\"plugin\"");
		sb.Append(",\"method\":\"log-share/1\"");
		sb.Append(",\"damageBasis\":\"dealt\"");
		sb.Append(",\"totalDamage\":"); NumOrNull(sb, st.Analyzable);
		sb.Append(",\"attributedDamage\":"); Num(sb, st.Attributed);
		sb.Append(",\"unattributedDamage\":"); Num(sb, st.Unattributed);
		sb.Append(",\"coverage\":{\"creditedShare\":"); Share(sb, st.CreditedShare);
		sb.Append(",\"unattributedShare\":"); Share(sb, st.Analyzable != 0 ? st.Unattributed / st.Analyzable : 0.0);
		sb.Append(",\"analyzableDealt\":"); Num(sb, st.Analyzable);
		sb.Append(",\"excludedDamage\":"); Num(sb, st.ExcludedDamage);
		sb.Append(",\"analysisDamageCoverage\":"); NumOrNull(sb, st.AnalysisDamageCoverage);
		sb.Append(",\"creditCoverageWithinAnalyzed\":"); NumOrNull(sb, st.CreditCoverageWithinAnalyzed);
		sb.Append(",\"overallAttributedCoverage\":"); NumOrNull(sb, st.OverallAttributedCoverage);
		sb.Append(",\"hits\":").Append(st.Hits);
		sb.Append('}');
		// The damage ledger. Kept separate on purpose: excludedDamage is the model's own exclusion (an
		// attacker it cannot resolve), outsideTeamDealt is out of scope BY DESIGN, and reconciliationGap
		// is what NO event explains. One merged "excluded" number would blame the model for the other
		// team's damage -- explicitly forbidden by the review (NEXT-STEPS section 0.3).
		sb.Append(",\"damageLedger\":{\"totalsDealt\":"); NumOrNull(sb, st.TotalsDealt);
		sb.Append(",\"events\":").Append(st.Events);
		sb.Append(",\"analyzableHits\":").Append(st.Hits);
		sb.Append(",\"outsideTeamHits\":").Append(st.OutsideTeamHits);
		sb.Append(",\"outsideTeamDealt\":"); Num(sb, st.OutsideTeamDamage);
		sb.Append(",\"unknownAttackerHits\":").Append(st.UnknownAttackerHits);
		sb.Append(",\"unknownAttackerDealt\":"); Num(sb, st.UnknownAttackerDamage);
		sb.Append(",\"eventSumAll\":"); Num(sb, st.EventSumAll);
		sb.Append(",\"reconciliationGap\":"); NumOrNull(sb, st.ReconciliationGap);
		sb.Append('}');
		sb.Append(",\"source\":{\"pluginVersion\":\"").Append(JsonText.Str(pluginVersion ?? "")).Append('"');
		sb.Append(",\"quest\":").Append(quest);
		sb.Append('}');
		sb.Append(",\"actors\":[");
		bool first = true;
		for (int ai = 0; ai < res.Actors.Count; ai++)
		{
			ContributionActorRow c = res.Actors[ai];
			if (!first) sb.Append(',');
			first = false;
			sb.Append("{\"key\":").Append(c.Key);
			sb.Append(",\"name\":\"").Append(JsonText.Str(c.Name ?? "")).Append('"');
			sb.Append(",\"kind\":\"").Append(JsonText.Str(c.Kind ?? "")).Append('"');
			sb.Append(",\"summon\":").Append(c.Summon ? "true" : "false");
			sb.Append(",\"directDamage\":"); Num(sb, c.Direct);
			sb.Append(",\"baseCredit\":"); Num(sb, c.Base);
			sb.Append(",\"selfRuleCredit\":"); Num(sb, c.Self);
			sb.Append(",\"assistCredit\":"); Num(sb, c.Assist);
			sb.Append(",\"totalCredit\":"); Num(sb, c.Base + c.Self + c.Assist);
			sb.Append(",\"receivedAssist\":"); Num(sb, c.Received);
			sb.Append(",\"hits\":").Append(c.Hits);
			sb.Append('}');
		}
		sb.Append(']');
		sb.Append(",\"rules\":[");
		first = true;
		for (int ri = 0; ri < res.Rules.Count; ri++)
		{
			ContributionRuleRow r = res.Rules[ri];
			if (!first) sb.Append(',');
			first = false;
			sb.Append("{\"ruleName\":\"").Append(JsonText.Str(r.Name)).Append('"');
			sb.Append(",\"kind\":\"").Append(JsonText.Str(r.Kind)).Append('"');
			sb.Append(",\"side\":\"").Append(JsonText.Str(r.Side)).Append('"');
			sb.Append(",\"ownerKey\":").Append(r.OwnerKey);
			sb.Append(",\"ownerName\":\"").Append(JsonText.Str(r.OwnerName)).Append('"');
			sb.Append(",\"hits\":").Append(r.Hits);
			sb.Append(",\"folds\":").Append(r.Folds);
			sb.Append(",\"damageEquivalent\":"); Num(sb, r.Damage);
			sb.Append('}');
		}
		sb.Append(']');
		sb.Append(",\"links\":[");
		first = true;
		for (int li = 0; li < res.Links.Count; li++)
		{
			ContributionLinkRow l = res.Links[li];
			if (!first) sb.Append(',');
			first = false;
			sb.Append("{\"fromKey\":").Append(l.From);
			sb.Append(",\"toKey\":").Append(l.To);
			sb.Append(",\"amount\":"); Num(sb, l.Amount);
			sb.Append(",\"hits\":").Append(l.Hits);
			sb.Append(",\"folds\":").Append(l.Folds);
			sb.Append('}');
		}
		sb.Append(']');
		sb.Append(",\"unattributed\":[");
		first = true;
		for (int ui = 0; ui < res.Unattributed.Count; ui++)
		{
			ContributionUnattributedRow kv = res.Unattributed[ui];
			if (!first) sb.Append(',');
			first = false;
			sb.Append("{\"reason\":\"").Append(JsonText.Str(kv.Reason)).Append('"');
			sb.Append(",\"amount\":"); Num(sb, kv.Amount);
			sb.Append(",\"folds\":").Append(kv.Folds);
			sb.Append('}');
		}
		sb.Append(']');
		sb.Append(",\"diagnostics\":{\"foldAccounting\":{\"total\":").Append(st.Folds)
		  .Append(",\"zeroFactor\":").Append(st.ZeroFactor)
		  .Append(",\"noopFactor\":").Append(st.NoopFactor)
		  .Append(",\"subUnity\":").Append(st.SubUnity)
		  .Append("},\"poolTotal\":"); Num(sb, st.PoolTotal);
		sb.Append(",\"eventsTotal\":").Append(st.Events);
		sb.Append(",\"outsideTeamHits\":").Append(st.OutsideTeamHits);
		sb.Append(",\"unknownAttackerHits\":").Append(st.UnknownAttackerHits);
		sb.Append(",\"calcMissingHits\":").Append(st.CalcMissing);
		sb.Append(",\"foldDropped\":").Append(st.FoldDropped);
		sb.Append(",\"foldDroppedHits\":").Append(st.FoldDroppedHits);
		sb.Append(",\"negativeLines\":").Append(st.Negative);
		sb.Append(",\"reasonCounts\":{");
		first = true;
		foreach (var kv in st.ReasonCounts.OrderByDescending(x => x.Value))
		{
			if (!first) sb.Append(',');
			first = false;
			sb.Append('"').Append(JsonText.Str(kv.Key)).Append("\":").Append(kv.Value);
		}
		// 1.7.7: three claims in this contract text had gone stale or were incomplete. Crit IS observed
		// since 1.5.0 (reconcile.critObserved) -- what is true is that the model does not credit it. And
		// analyzableDealt is not "the battle's damage": it only counts team-1 hits with a resolvable
		// attacker, which is the first thing a reader must know before comparing it with totals.dealt.
		// 1.7.7 rev2: these six strings are now byte-identical to contrib/report_json.py's list. The two
		// producers used to ship different sentences (and a different item count) for the same contract
		// field, so a reader comparing the plugin export with the offline report saw two "contracts".
		sb.Append("},\"knownLimits\":[\"attackPower addends granted by a teammate are attributed as kind=atkadd (1.7.4+); self-granted addends stay in baseCredit\",\"analyzableDealt covers team-1 hits with a resolvable attacker only; compare it with totals.dealt before comparing battles\",\"crit is observed (1.5.0+) but the model does not credit it; crit damage stays in baseCredit\",\"summons stay separate actors (no owner link in the export)\",\"credit components are rounded independently (F4 in the export, N0 on screen), so they may not add up to the total\",\"totals.dealt and the per-event sum differ by ~0.05% (definitional, not an error)\"]}}");
	}

	/// <summary>Compute + write in one call (the export path). Returns the stats for logging/tests.</summary>
	public static ContributionStats AppendJson(StringBuilder sb, IList<ContributionHit> hits,
		IList<ContributionActor> roster, int team, string pluginVersion, int quest)
	{
		ContributionResult res = Compute(hits, roster, team);
		AppendJson(sb, res, pluginVersion, quest);
		return res.Stats;
	}
}
