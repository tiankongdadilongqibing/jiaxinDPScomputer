# -*- coding: utf-8 -*-
"""Contribution JSON draft (schemaVersion 0.1-draft). Not part of the plugin export."""
from __future__ import annotations


def to_json(an, export, issues, summary):
    T = export.totals or {}
    # P0-B (1.7.8): the damage ledger. These buckets were already collected by aggregate.py; they
    # just were not published, so a reader could not explain the difference between totals.dealt and
    # analyzableDealt from the file alone.
    _diag = an.diagnostics or {}

    def _bucket_damage(k):
        return float((_diag.get(k) or {}).get("damage") or 0.0)

    def _bucket_count(k):
        return int((_diag.get(k) or {}).get("count") or 0)

    _totals_dealt = float(T.get("dealt") or 0.0)
    _event_sum_all = float(_diag.get("eventSumAll") or 0.0)
    _excluded_damage = _bucket_damage("unattributed_events") + _bucket_damage("attacker_unresolved")
    _unknown_hits = _bucket_count("unattributed_events") + _bucket_count("attacker_unresolved")
    _outside_damage = _bucket_damage("outside_team_events")
    _outside_hits = _bucket_count("outside_team_events")
    _attributed = an.actor_total_credit()
    doc = {
        "contribution": {
            # 1.1 (P0-B): schemaVersion names the CORE contract and is now shared with the plugin;
            # `producer` says which implementation wrote the file, and offlineExtensionVersion versions
            # the extra fields only this report has. It used to say 0.1-draft while the plugin said 1.0
            # for the same core fields -- the review flagged that as a fork (NEXT-STEPS P1-B).
            "schemaVersion": "1.1",
            "producer": "offline",
            "offlineExtensionVersion": "1",
            "method": "log-share/1",
            "damageBasis": "dealt",
            # plan section 5-E field names (top level so Phase E can adopt them verbatim)
            "totalDamage": an.analyzable,
            "attributedDamage": _attributed,
            "unattributedDamage": an.unattributed_credit,
            "coverage": {
                "creditedShare": summary.get("credited_share"),
                "unattributedShare": summary.get("unattributed_share"),
                "analyzableDealt": an.analyzable,
                "excludedDamage": _excluded_damage,
                "analysisDamageCoverage": (an.analyzable / _totals_dealt) if _totals_dealt else None,
                "creditCoverageWithinAnalyzed": (_attributed / an.analyzable) if an.analyzable else None,
                "overallAttributedCoverage": (_attributed / _totals_dealt) if _totals_dealt else None,
                "hits": an.hits,
                "byUnitGiven": summary.get("byUnit_given"),
                "byUnitMadness": summary.get("byUnit_madness"),
                "keyCoverage": summary.get("key_coverage"),
                "ownerPaths": summary.get("owner_paths"),
                "foldAccounting": summary.get("fold_accounting"),
            },
            "source": export.source(),
            # The ledger is deliberately three separate numbers (review 0.3): excludedDamage is the
            # model's own exclusion, outsideTeamDealt is out of scope by design, reconciliationGap is
            # what no event explains. They must never be merged into one "excluded" figure.
            "damageLedger": {
                "totalsDealt": _totals_dealt,
                # `events` (P0-A found this missing): the schema gate requires it for 1.1, and the type
                # script saw the plugin emit it while the offline draft did not -- a producer fork that
                # would have made phase_e_dryrun fail forever. It is the count of dmg events.
                "events": int((_diag.get("events") or {}).get("dmg", 0)),
                "eventSumAll": _event_sum_all,
                "analyzableHits": an.hits,
                "outsideTeamHits": _outside_hits,
                "outsideTeamDealt": _outside_damage,
                "unknownAttackerHits": _unknown_hits,
                "unknownAttackerDealt": _excluded_damage,
                # NOT totalsDealt - eventSumAll: the unresolvable-attacker pool is not part of
                # totals.dealt (the game reports it separately), so subtracting it again would report
                # a gap that is exactly that pool. Measured 1.7.6: analyzable + outsideTeam == totals.dealt
                # exactly, and unknownAttackerDealt == totals.unattributedDamage exactly.
                "reconciliationGap": _totals_dealt - (an.analyzable + _outside_damage),
            },
            "totals": {
                "dealt": T.get("dealt"),
                "analyzableDealt": an.analyzable,
                "eventSumAll": an.diagnostics.get("eventSumAll"),
                "analyzeVsTotalsDelta": summary.get("analyzable_vs_totals"),
                "unattributedDamageEvents": an.diagnostics.get("unattributed_events"),
                "outsideTeamEvents": an.diagnostics.get("outside_team_events"),
                "unattributedCredit": an.unattributed_credit,
                "dealtWithAbsorbed": T.get("dealtWithAbsorbed"),
                "absorbed": T.get("absorbed"),
                "hits": an.hits,
                "coverage": {
                    "creditedShare": summary.get("credited_share"),
                    "unattributedShare": summary.get("unattributed_share"),
                    "poolShareOfAnalyzable": summary.get("pool_share"),
                    "byUnitGiven": summary.get("byUnit_given"),
                    "byUnitMadness": summary.get("byUnit_madness"),
                    "perHitMaxError": summary.get("per_hit_max_error"),
                },
            },
            "actors": [
                {
                    "key": a.key, "name": a.name, "team": a.team, "kind": a.kind,
                    "summon": a.summon,
                    "directDamage": a.direct,
                    "directShare": (a.direct / an.analyzable) if an.analyzable else 0.0,
                    "friendly": a.friendly, "friendlyHits": a.friendly_hits,
                    "hostileDamage": a.hostile,
                    "baseCredit": a.base, "selfRuleCredit": a.self_rule,
                    "assistCredit": a.assist, "totalCredit": a.total,
                    "totalShare": (a.total / an.analyzable) if an.analyzable else 0.0,
                    "receivedAssist": a.received,
                    "hits": a.hits,
                    "rules": [
                        {"ruleName": an.rules[k].name if k in an.rules else str(k[0]),
                         "kind": k[1], "side": k[2],
                         "hits": a.rule_hits.get(k, 0), "damageEquivalent": v}
                        for k, v in sorted(a.rules.items(), key=lambda x: -x[1])
                    ],
                }
                for a in sorted(an.actors.values(), key=lambda x: -x.total)
                if a.direct > 0 or a.total > 0
            ],
            "rules": [
                {
                    "ownerKey": r.owner_key, "ownerName": r.owner_name,
                    "ruleName": r.name, "kind": r.kind, "side": r.side,
                    "origin": r.origin, "resolved": r.resolved,
                    "hits": r.hits, "folds": r.folds,
                    "coverage": (r.hits / an.hits) if an.hits else 0.0,
                    "damageEquivalent": r.damage,
                    "reasons": r.reasons,
                    "beneficiaries": [
                        {"key": k, "name": an.actors[k].name if k in an.actors else str(k),
                         "amount": v, "hits": r.beneficiary_hits.get(k, 0),
                         "folds": r.beneficiary_folds.get(k, 0)}
                        for k, v in sorted(r.beneficiaries.items(), key=lambda x: -x[1])
                    ],
                }
                for r in sorted(an.rules.values(), key=lambda x: -x.damage)
            ],
            "links": [
                {"fromKey": l.from_key, "fromName": an.actors[l.from_key].name if l.from_key in an.actors else str(l.from_key),
                 "toKey": l.to_key, "toName": an.actors[l.to_key].name if l.to_key in an.actors else str(l.to_key),
                 "amount": l.amount, "hits": l.hits, "folds": l.folds,
                 "rules": [an.rules[k].name if k in an.rules else str(k[0]) for k in l.rules]}
                for l in sorted(an.links.values(), key=lambda x: -x.amount)
            ],
            "unattributed": [
                {"reason": reason, "amount": amt, "folds": n}
                for reason, (amt, n) in sorted(an.unattributed.items(), key=lambda x: -x[1][0])
            ],
            "unattributedByKind": an.unattributed_by_kind,
            "diagnostics": {
                "reasonCounts": an.diagnostics.get("reasonCounts"),
                "channelCensus": an.diagnostics.get("channelCensus"),
                "foldAccounting": summary.get("fold_accounting"),
                "zeroFactorHits": an.diagnostics.get("zero_factor"),
                "noopFactorFolds": an.diagnostics.get("noop_factor"),
                "attackerNameFallbacks": an.diagnostics.get("attackerNameFallbacks"),
                "duplicateInHit": an.diagnostics.get("duplicate_in_hit", 0),
                "summonActors": an.diagnostics.get("summon_actors"),
                "duplicateNames": an.diagnostics.get("duplicate_names"),
                "crits": an.diagnostics.get("crits"),
                "calcMissingHits": an.diagnostics.get("calc_missing", 0),
                "foldDropped": an.diagnostics.get("fold_dropped", 0),
                "foldDroppedHits": an.diagnostics.get("fold_dropped_hits", 0),
                "modelApplicable": an.diagnostics.get("modelApplicable", True),
                "subUnityFolds": an.diagnostics.get("sub_unity_factor", 0),
                "negativeLines": an.diagnostics.get("negative_lines", 0),
                "residualBuckets": {str(k): v for k, v in sorted(
                    an.diagnostics.get("residual_buckets", {}).items(), key=lambda x: -x[1])},
                "checks": [{"level": lv, "code": c, "message": m} for lv, c, m in issues],
                # 1.7.7 rev2: byte-identical to Contribution.cs's list (they used to disagree).
                "knownLimits": [
                    "attackPower addends granted by a teammate are attributed as kind=atkadd (1.7.4+); self-granted addends stay in baseCredit",
                    "analyzableDealt covers team-1 hits with a resolvable attacker only; compare it with totals.dealt before comparing battles",
                    "crit is observed (1.5.0+) but the model does not credit it; crit damage stays in baseCredit",
                    "summons stay separate actors (no owner link in the export)",
                    "credit components are rounded independently (F4 in the export, N0 on screen), so they may not add up to the total",
                    "totals.dealt and the per-event sum differ by ~0.05% (definitional, not an error)",
                ],
            },
        }
    }
    return doc
