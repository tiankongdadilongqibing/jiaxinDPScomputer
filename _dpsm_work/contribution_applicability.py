# -*- coding: utf-8 -*-
"""P0-D: task applicability scanner for DpsMeter battle exports.

WHY THIS FILE EXISTS
--------------------
The contribution model (log-share/1) is only meaningful when the export it is
computed from actually feeds the model the inputs the model assumes. Today the
corpus mixes quests, plugin versions and battle modes, so "411001 vs 700817 vs
9999" cannot be compared unconditionally. This scanner produces a per-export
verdict: modelApplicability in {full, partial, not_comparable}, the reasons for
that verdict, and an explicit comparison scope.

THE ONLY LEGAL "MISSING FOLD" TEST  (corrected 2026-10-04, report section 9.7)
----------------------------------------------------------------------------
    | prod(calc.fold) / (dealtMult * takenMult) - 1|  >  tolerance

ATTR MULT IS EXCLUDED ON PURPOSE. attrMult (elemental affinity) is, by design,
NOT placed into calc.fold (CompositionProbe.Chain.cs:116), while
knownMult = attrMult * dealtMult * takenMult. Comparing knownMult against the
fold product therefore produces a confident but WRONG "missing fold" verdict.
Measured fact: all 59 hits in battle_700817_20261004_114821.json with
knownMult != 1 and an empty calc.fold have attrMult == knownMult == 2 (offline
replay). Across all 26 exports every such hit is attrMult-explained.

So this file reports BOTH numbers and never conflates them:

  * knownMultWithoutFold      -- the literal NEXT-STEPS detection
                                 (knownMult != 1 and fold empty). Diagnostic.
  * foldIdentity.unexplained  -- the only legal leak judgement.
  * knownMultWithoutFold.unexplainedHits -- attrMult cannot explain these.

TOLERANCE (why 1e-3 alone is not enough)
----------------------------------------
dealtMult / takenMult / attrMult / knownMult are serialized with 3 decimals, so
each carries up to 5e-4 ABSOLUTE rounding. fold[].factor is round-trip since
1.6.1 (4-decimal truncated in 1.5.x/1.6.0). Therefore:

  envelope = 5e-4 * (|dealtMult| + |takenMult|)
           + (1e-3 + 5e-5 * nFoldFactors) * |dealtMult*takenMult|

The absolute term is the one that matters for sub-unity chains: a small factor
like takenMult = 0.194 amplifies the same +-5e-4 absolute rounding to a 2.6e-3
RELATIVE deviation, which a fixed 1e-3 relative gate would misreport as a leak.
Measured: the strict 1e-3 relative gate flags 16 hits in quest 9999 (max 2.06e-3)
and 0 hits elsewhere; every one of those 16 is inside the envelope above.

DEGENERATE HITS. When dealtMult*takenMult == 0 the relative identity is
undefined; those hits are counted separately and checked for the only
degenerate-consistent outcome (fold product == 0 too).

EVIDENCE vs GAP (NEXT-STEPS section 0.3)
----------------------------------------
dealtMinusAnalyzable is NOT one number called excludedDamage:
  * enemy-team damage   -- excluded, normal/expected (enemy damage is not our
                           missed analysis), backed by per-event evidence;
  * own-side excluded   -- our hits that still failed the analysis filter; this
                           is the real defect part;
  * reconciliationGap   -- whatever the event evidence does NOT explain;
  * unknownAttacker     -- attacker unresolvable; not part of totals.dealt at
                           all (reported as totals.unattributed*), so tracked
                           outside the gap.

Outputs are ASCII-only on stdout (the console is GBK).

Usage:
  python contribution_applicability.py --json contribution_applicability_report.json
  python contribution_applicability.py --selftest
  python contribution_applicability.py --only battle_700817_20261004_114821.json --no-crosscheck
"""
from __future__ import print_function, division, absolute_import
from __future__ import unicode_literals

import argparse
import glob
import io
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

ROOT = os.path.dirname(HERE)
EXPORTS = os.path.join(ROOT, "BepInEx", "plugins", "DpsMeter", "exports")
DEFAULT_JSON = os.path.join(HERE, "contribution_applicability_report.json")

STRICT_REL = 1e-3          # the requested relative gate
ABS_ROUND = 5e-4           # 3-decimal serialization of dealtMult/takenMult
FACTOR_REL = 5e-5          # per fold factor: 4-decimal truncation, pre-1.6.1 worst case


# Version policy (report section 5). The plugin section is only a trustworthy
# witness of the in-memory value once factor round-trip precision landed (1.6.1).
ROUNDTRIP_FROM = (1, 6, 1)


def _num(d, key, default=1.0):
    v = d.get(key)
    if isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(float(v)):
        return float(v)
    return default


def _prod_fold(folds):
    m = 1.0
    for f in (folds or []):
        try:
            x = float(f.get("factor"))
        except (TypeError, ValueError, AttributeError):
            continue
        if math.isfinite(x):
            m *= x
    return m


def identity_envelope(dealt_mult, taken_mult, n_factors):
    """Absolute deviation the 3-decimal serialization + factor precision can create."""
    target = dealt_mult * taken_mult
    abs_term = ABS_ROUND * (abs(dealt_mult) + abs(taken_mult))
    rel_term = (STRICT_REL + FACTOR_REL * max(0, n_factors)) * abs(target)
    return abs_term + rel_term


def scan_raw(raw, path="", crosscheck=None, offline=None):
    """Core scanner. raw is the parsed export dict.

    crosscheck: dict with at least {"status": ...} or None (not run).
    offline:    dict with {"analyzable": float} or None.
    """
    version = raw.get("version")
    quest = raw.get("quest")
    events = raw.get("events") or []
    totals = raw.get("totals") or {}
    sec = raw.get("contribution")
    actors = raw.get("actors") or []

    key_team = {}
    for a in actors:
        k = a.get("key")
        if k is not None:
            key_team[k] = a.get("team")

    # ---- event census -----------------------------------------------------
    n_dmg = 0
    n_calc = 0
    own_analyzed_hits = 0
    own_analyzed_damage = 0.0
    # R61: the same-team part of the team-1 hits, i.e. the amount schema 1.2 excludes from the pool.
    own_hostile_hits = 0
    own_hostile_damage = 0.0
    enemy_hits = 0
    enemy_damage = 0.0
    own_excluded_hits = 0
    own_excluded_damage = 0.0
    unknown_hits = 0
    unknown_damage = 0.0
    self_hits = 0
    self_damage = 0.0
    friendly_hits = 0
    friendly_damage = 0.0

    ident_checked = 0
    ident_strict = 0
    ident_unexplained = 0
    ident_max_rel = 0.0
    ident_max_abs = 0.0
    ident_max_rec = None
    ident_max_rel_rec = None
    deg_zero_hits = 0
    deg_consistent = 0
    deg_inconsistent = 0

    knf_hits = 0
    knf_damage = 0.0
    knf_attr_hits = 0
    knf_attr_damage = 0.0
    knf_atkadd_hits = 0

    sub_fold_hits = 0
    sub_fold_count = 0
    sub_fold_damage = 0.0
    sub_chain_taken_hits = 0
    sub_chain_dealt_hits = 0
    training_scale_hits = 0

    attr_hits = 0
    attr_damage = 0.0
    attr_nofold_hits = 0

    for e in events:
        if e.get("type") != "dmg":
            continue
        n_dmg += 1
        try:
            amt = float(e.get("amount") or 0.0)
        except (TypeError, ValueError):
            amt = 0.0
        ak = e.get("atkKey")
        vk = e.get("vicKey")
        team = key_team.get(ak)
        if ak is not None and team == 1:
            own_analyzed_hits += 1
            own_analyzed_damage += amt
            if not e.get("friendly"):
                own_hostile_hits += 1
                own_hostile_damage += amt
        elif ak is not None and team == 2:
            enemy_hits += 1
            enemy_damage += amt
        elif ak is not None and ak != 0 and ak in key_team:
            own_excluded_hits += 1
            own_excluded_damage += amt
        else:
            unknown_hits += 1
            unknown_damage += amt
        if ak is not None and ak == vk:
            self_hits += 1
            self_damage += amt
        if e.get("atkTeam") == 1 and e.get("vicTeam") == 1 and ak != vk:
            friendly_hits += 1
            friendly_damage += amt

        c = e.get("calc")
        if not isinstance(c, dict):
            continue
        n_calc += 1
        folds = c.get("fold") or []
        nf = len(folds)
        dm = _num(c, "dealtMult", 1.0)
        tm = _num(c, "takenMult", 1.0)
        am = _num(c, "attrMult", 1.0)
        km = _num(c, "knownMult", 1.0)
        target = dm * tm

        has_sub_fold = any(
            isinstance(f.get("factor"), (int, float)) and 0.0 < float(f.get("factor")) < 1.0
            for f in folds)
        if has_sub_fold:
            sub_fold_hits += 1
            sub_fold_damage += amt
            sub_fold_count += sum(
                1 for f in folds
                if isinstance(f.get("factor"), (int, float)) and 0.0 < float(f.get("factor")) < 1.0)
        if 0.0 < tm < 1.0:
            sub_chain_taken_hits += 1
        if 0.0 < dm < 1.0:
            sub_chain_dealt_hits += 1
        # training-ground x0.03 is serialized as takenMult == 0.028
        if 0.0 < tm < 0.1:
            training_scale_hits += 1

        if am != 1.0:
            attr_hits += 1
            attr_damage += amt
            if nf == 0:
                attr_nofold_hits += 1

        if nf == 0 and km != 1.0:
            knf_hits += 1
            knf_damage += amt
            if abs(am - km) < ABS_ROUND + 1e-9:
                knf_attr_hits += 1
                knf_attr_damage += amt
            if c.get("atkAdd"):
                knf_atkadd_hits += 1

        if target == 0.0:
            deg_zero_hits += 1
            pf = _prod_fold(folds)
            if pf == 0.0:
                deg_consistent += 1
            else:
                deg_inconsistent += 1
            continue

        ident_checked += 1
        pf = _prod_fold(folds)
        rel = abs(pf / target - 1.0)
        absd = abs(pf - target)
        if rel > ident_max_rel:
            ident_max_rel = rel
            ident_max_rel_rec = {
                "effectId": c.get("effectId"),
                "foldProduct": pf, "dealtMult": dm, "takenMult": tm,
                "attrMult": am, "knownMult": km, "foldCount": nf,
                "relDeviation": rel, "absDeviation": absd,
                "envelope": identity_envelope(dm, tm, nf),
                "amount": amt,
            }
        if absd > ident_max_abs:
            ident_max_abs = absd
            ident_max_rec = {
                "effectId": c.get("effectId"),
                "foldProduct": pf, "dealtMult": dm, "takenMult": tm,
                "attrMult": am, "knownMult": km, "foldCount": nf,
                "relDeviation": rel, "absDeviation": absd,
                "envelope": identity_envelope(dm, tm, nf),
                "amount": amt,
            }
        if rel > STRICT_REL:
            ident_strict += 1
        if absd > identity_envelope(dm, tm, nf):
            ident_unexplained += 1

    # ---- reconciliation ---------------------------------------------------
    totals_dealt = float(totals.get("dealt") or 0.0)
    offline_analyzable = None
    if offline is not None:
        offline_analyzable = float(offline.get("analyzable") or 0.0)
    sec_analyzable = None
    sec_hits = None
    if isinstance(sec, dict):
        cov = sec.get("coverage") or {}
        if isinstance(cov.get("analyzableDealt"), (int, float)):
            sec_analyzable = float(cov["analyzableDealt"])
        if isinstance(cov.get("hits"), (int, float)):
            sec_hits = int(cov["hits"])
    analyzable = offline_analyzable
    if analyzable is None:
        analyzable = sec_analyzable
    if analyzable is None:
        analyzable = own_analyzed_damage   # synthetic/segment-less input: our own filter
    # R61: which of our two pools the file itself declares -- same-team inside (<=1.1) or excluded (1.2).
    _own_for_filter = own_analyzed_damage
    try:
        _sv = str((sec or {}).get("schemaVersion") or "") if isinstance(sec, dict) else ""
        if _sv and tuple(int(x) for x in _sv.split(".")[:2]) >= (1, 2):
            _own_for_filter = own_hostile_damage
    except (TypeError, ValueError):
        pass
    deal_minus = (totals_dealt - analyzable) if analyzable is not None else None
    excluded_total = enemy_damage + own_excluded_damage
    gap = None
    if deal_minus is not None:
        gap = deal_minus - excluded_total

    # ---- crosscheck / section --------------------------------------------
    has_sec = isinstance(sec, dict)
    if crosscheck is None:
        cc = {"status": "NOT_RUN", "statusCode": None, "mismatches": 0, "omissions": 0,
              "validateErrors": 0, "reasons": []}
    else:
        cc = {
            "status": crosscheck.get("status"),
            "statusCode": crosscheck.get("statusCode"),
            "mismatches": len(crosscheck.get("mismatches") or []),
            "omissions": len(crosscheck.get("omissions") or []),
            "validateErrors": len(crosscheck.get("validateErrors") or []),
            "reasons": [r.get("code") for r in (crosscheck.get("reasons") or [])
                        if isinstance(r, dict)],
        }
    # P0-A's aggregate status folds in gate-applicability reasons (e.g. LEGACY_NOT_APPLICABLE
    # for pre-1.7.4 exports with no root atkAdd counter block).  Model applicability must not be
    # driven by that: the question here is whether the plugin SECTION VALUES match the offline
    # core, so decide on the value-level counters and keep the status only as evidence.
    # valuesTrusted answers exactly one question: do the plugin section VALUES agree with the
    # independent offline core?  It must NOT absorb the gate's own applicability verdict: P0-A's
    # aggregate status also folds in training-mode and pre-1.7.4 atkAdd-counter reasons whose
    # severity can change independently of the values (observed live during this task).
    cc_values_ok = (cc["mismatches"] == 0 and cc["omissions"] == 0 and cc["validateErrors"] == 0)
    cc_trusted = has_sec and cc_values_ok and cc["status"] != "UNAVAILABLE"
    cc["valuesTrusted"] = cc_trusted

    # ---- special mode -----------------------------------------------------
    training_ground = int(quest or 0) == 9999
    markers = []
    if training_ground:
        markers.append("training_ground")
    if training_ground and training_scale_hits > 0:
        markers.append("training_x0.03")
    if sub_fold_hits > 0:
        markers.append("sub_unity_folds")
    if self_hits > 0:
        markers.append("self_damage")
    if friendly_hits > 0:
        markers.append("friendly_fire")
    if deg_inconsistent > 0:
        markers.append("degenerate_inconsistent")

    # ---- applicability ----------------------------------------------------
    reasons = []
    not_comparable = None
    partial = None

    if ident_unexplained > 0:
        not_comparable = ("%d hits have |prod(fold)/(dealtMult*takenMult)-1| outside the "
                          "rounding envelope (a real missing-fold leak)" % ident_unexplained)
        reasons.append(not_comparable)
    elif knf_hits > knf_attr_hits:
        not_comparable = ("%d knownMult-without-fold hits are NOT explained by attrMult"
                          % (knf_hits - knf_attr_hits))
        reasons.append(not_comparable)

    if not_comparable is None and training_ground:
        not_comparable = ("quest 9999 is the training ground (special x0.03 mode: "
                          "%d hits with 0<takenMult<0.1, %d sub-unity fold hits)"
                          % (training_scale_hits, sub_fold_hits))
        reasons.append(not_comparable)

    if not_comparable is None and has_sec and not cc_trusted:
        not_comparable = ("plugin contribution section does not match the offline core "
                          "(status=%s mismatches=%d omissions=%d validateErrors=%d; 1.6.0 "
                          "factor precision is the known case)"
                          % (cc["status"], cc["mismatches"], cc["omissions"],
                             cc["validateErrors"]))
        reasons.append(not_comparable)

    if not_comparable is None:
        if not has_sec:
            partial = ("no contribution section: offline recompute is possible but the plugin "
                       "in-memory values cannot be verified against the file")
            reasons.append(partial)
        if attr_hits > 0:
            partial = ("%d hits carry attrMult != 1 (%.2f%% of dealt); attribute affinity is "
                       "outside calc.fold by design, so the fold/base split does not describe "
                       "that multiplier" % (attr_hits, 100.0 * attr_damage / totals_dealt
                                            if totals_dealt else 0.0))
            reasons.append(partial)
        if sub_fold_hits > 0:
            partial = ("%d hits contain sub-unity folds, which the pool drops as SubUnity "
                       "(Contribution.cs:384)" % sub_fold_hits)
            reasons.append(partial)
        if gap is not None and abs(gap) > 0.5:
            partial = ("reconciliationGap %.0f is not explained by per-event evidence" % gap)
            reasons.append(partial)
        if deg_inconsistent > 0:
            partial = ("%d hits have dealtMult*takenMult == 0 but a non-zero fold product"
                       % deg_inconsistent)
            reasons.append(partial)

    if not_comparable is not None:
        applicability = "not_comparable"
    elif partial is not None:
        applicability = "partial"
    else:
        applicability = "full"
        reasons.append("contribution section consistent, fold identity within envelope, "
                       "attrMult == 1, no sub-unity folds, gap fully event-evidenced")

    if applicability == "full":
        scope = "normal-benchmark"
    elif applicability == "not_comparable":
        if training_ground:
            scope = "training-ground-only"
        elif has_sec and not cc_trusted:
            scope = "invalid-plugin-value-trust"
        else:
            scope = "invalid-fold-leak"
    else:
        if not has_sec:
            scope = "offline-recompute-only"
        elif attr_hits > 0:
            scope = "caveat-attribute-affinity"
        elif sub_fold_hits > 0:
            scope = "caveat-sub-unity-folds"
        else:
            scope = "caveat-reconciliation-gap"

    comparable_caveat = (applicability in ("full", "partial")
                         and "training_ground" not in markers
                         and ident_unexplained == 0 and knf_hits == knf_attr_hits)

    return {
        "file": os.path.basename(path) if path else "",
        "version": version,
        "quest": quest,
        "hasContributionSection": has_sec,
        "contributionSchemaVersion": sec.get("schemaVersion") if has_sec else None,
        "contributionMethod": sec.get("method") if has_sec else None,
        "damageBasis": sec.get("damageBasis") if has_sec else None,
        "crosscheck": cc,
        "counts": {
            "dmgEvents": n_dmg,
            "dmgWithCalc": n_calc,
            "offlineAnalyzableHits": own_analyzed_hits,
            "contributionHits": sec_hits,
            "reconcileDmgEvents": (raw.get("reconcile") or {}).get("dmgEvents"),
        },
        "damage": {
            "totalsDealt": totals_dealt,
            "offlineAnalyzableDealt": offline_analyzable,
            "contributionAnalyzableDealt": sec_analyzable,
            "ownHostileHits": own_hostile_hits,
            "ownHostileDealt": own_hostile_damage,
            "analyzableFilterMatchesOffline": (
                None if offline_analyzable is None
                else abs(_own_for_filter - offline_analyzable) < 1.0),
        },
        "foldIdentity": {
            "criterion": "prod(calc.fold) vs dealtMult*takenMult (attrMult excluded)",
            "strictRelTol": STRICT_REL,
            "checkedHits": ident_checked,
            "violationsStrictRel1e3": ident_strict,
            "violationsUnexplainedByRounding": ident_unexplained,
            "maxRelDeviation": ident_max_rel,
            "maxAbsDeviation": ident_max_abs,
            "maxRelDeviationRecord": ident_max_rel_rec,
            "maxAbsDeviationRecord": ident_max_rec,
            "degenerateZeroChainHits": deg_zero_hits,
            "degenerateConsistent": deg_consistent,
            "degenerateInconsistent": deg_inconsistent,
        },
        "knownMultWithoutFold": {
            "criterion": "knownMult != 1 and calc.fold empty (literal NEXT-STEPS detection)",
            "hits": knf_hits,
            "damage": knf_damage,
            "explainedByAttrMultHits": knf_attr_hits,
            "explainedByAttrMultDamage": knf_attr_damage,
            "unexplainedHits": knf_hits - knf_attr_hits,
            "unexplainedDamage": knf_damage - knf_attr_damage,
            "withAtkAddHits": knf_atkadd_hits,
        },
        "attributeAffinity": {
            "hits": attr_hits, "damage": attr_damage,
            "hitsWithoutFold": attr_nofold_hits,
            "shareOfDealt": (attr_damage / totals_dealt) if totals_dealt else 0.0,
        },
        "specialMode": {
            "trainingGround": training_ground,
            "trainingScaleX003": training_ground and training_scale_hits > 0,
            "trainingScaleHits": training_scale_hits,
            "subUnityFoldHits": sub_fold_hits,
            "subUnityFoldCount": sub_fold_count,
            "subUnityChainTakenMultHits": sub_chain_taken_hits,
            "subUnityChainDealtMultHits": sub_chain_dealt_hits,
            "markers": markers,
        },
        "reconciliation": {
            "dealtMinusAnalyzable": deal_minus,
            "excludedDamage": {
                "enemyTeamHits": enemy_hits, "enemyTeamDamage": enemy_damage,
                "enemyTeamNormalExpected": True,
                "ownSideExcludedHits": own_excluded_hits,
                "ownSideExcludedDamage": own_excluded_damage,
                "total": excluded_total,
            },
            "reconciliationGap": gap,
            "outsideDealt": {
                "unknownAttackerHits": unknown_hits, "unknownAttackerDamage": unknown_damage,
                "reportedTotalsUnattributedHits": totals.get("unattributedHits"),
                "reportedTotalsUnattributedDamage": totals.get("unattributedDamage"),
            },
            "inAnalyzed": {
                "selfDamageHits": self_hits, "selfDamageDamage": self_damage,
                "friendlyFireHits": friendly_hits, "friendlyFireDamage": friendly_damage,
            },
        },
        "modelApplicability": applicability,
        "comparisonAllowed": applicability == "full",
        "comparisonAllowedWithCaveat": comparable_caveat,
        "comparisonScope": scope,
        "reasons": reasons,
        "retiredKnownMultCriterionLeaks": knf_hits,
        "correctedUnexplainedLeaks": ident_unexplained + (knf_hits - knf_attr_hits),
        # flat aliases with the exact names the P0-D brief asks for
        "knownMultWithoutFoldHits": knf_hits,
        "knownMultWithoutFoldDamage": knf_damage,
        "knownMultWithoutFoldExplainedByAttrMultHits": knf_attr_hits,
        "foldIdentityUnexplainedLeaks": ident_unexplained,
        "specialModeVerdict": (markers[0] if markers else "none"),
    }


def load_raw(path):
    with io.open(path, "r", encoding="utf-8") as fh:
        return json.load(fh)


def crosscheck_of(path):
    try:
        from contrib import crosscheck as ccmod
    except Exception as exc:
        return {"status": "UNAVAILABLE", "mismatches": [], "error": str(exc)}
    try:
        return ccmod.compare(path)
    except Exception as exc:
        return {"status": "UNAVAILABLE", "mismatches": [], "error": str(exc)}


def offline_analyzable(raw, path):
    try:
        from contrib import aggregate, loader
    except Exception:
        return None
    try:
        ex = loader.ExportData(raw, path)
        an = aggregate.analyze(ex, 1)
        return {"analyzable": an.analyzable}
    except Exception:
        return None


def scan_file(path, run_crosscheck=True):
    raw = load_raw(path)
    cc = None
    if run_crosscheck and isinstance(raw.get("contribution"), dict):
        cc = crosscheck_of(path)
    off = offline_analyzable(raw, path)
    return scan_raw(raw, path, crosscheck=cc, offline=off)


def scan_all(exports=EXPORTS, only=None, run_crosscheck=True, log=print):
    files = sorted(glob.glob(os.path.join(exports, "battle_*.json")))
    if only:
        keep = set(only)
        files = [f for f in files if os.path.basename(f) in keep]
    out = []
    for f in files:
        r = scan_file(f, run_crosscheck=run_crosscheck)
        out.append(r)
        log("[scan] %s v=%s q=%s sec=%s cc=%s app=%s leaks=%d/%d" % (
            r["file"], r["version"], r["quest"], r["hasContributionSection"],
            r["crosscheck"].get("status"), r["modelApplicability"],
            r["correctedUnexplainedLeaks"], r["retiredKnownMultCriterionLeaks"]))
    return out


def verdicts(rows):
    by_quest = {}
    for r in rows:
        by_quest.setdefault(r["quest"], []).append(r)
    out = {}
    for q, rs in sorted(by_quest.items(), key=lambda kv: str(kv[0])):
        out[str(q)] = {
            "exports": len(rs),
            "versions": sorted(set(str(x["version"]) for x in rs)),
            "withSection": sum(1 for x in rs if x["hasContributionSection"]),
            "applicability": sorted(set(x["modelApplicability"] for x in rs)),
            "comparisonScopes": sorted(set(x["comparisonScope"] for x in rs)),
            "knownMultWithoutFoldHits": sum(x["knownMultWithoutFold"]["hits"] for x in rs),
            "knownMultWithoutFoldExplainedByAttr": sum(
                x["knownMultWithoutFold"]["explainedByAttrMultHits"] for x in rs),
            "correctedUnexplainedLeaks": sum(x["correctedUnexplainedLeaks"] for x in rs),
        }
    return out


# ---------------------------------------------------------------------------
# selftest: prove the scanner fails on a real leak and rejects the retired rule
# ---------------------------------------------------------------------------

def _mk(version, quest, totals_dealt, events, actors=None):
    return {
        "app": "DpsMeter", "version": version, "quest": quest, "duration": 1.0,
        "result": "Test", "totals": {"dealt": totals_dealt},
        "actors": actors if actors is not None else [
            {"key": 1, "name": "A", "team": 1}, {"key": 2, "name": "B", "team": 2}],
        "events": events,
    }


def _hit(amount, calc, atk=1, vic=99, atk_team=1, vic_team=2, atk_key=None):
    return {"t": 0.0, "type": "dmg", "attacker": "A", "victim": "V", "amount": amount,
            "atkTeam": atk_team, "vicTeam": vic_team, "atkKey": atk, "vicKey": vic,
            "crit": False, "calc": calc}


def selftest(exports=EXPORTS):
    cases = []
    fails = []

    def check(name, got, want):
        ok = got == want
        cases.append((name, got, want, ok))
        if not ok:
            fails.append(name)
        return ok

    # 1. attrMult-only knownMult-without-fold must NOT be a leak (the 700817 shape)
    r1 = scan_raw(_mk("1.7.6", 411001, 1000, [
        _hit(800, {"dealtMult": 1, "takenMult": 1, "attrMult": 2, "knownMult": 2, "fold": []}),
        _hit(200, {"dealtMult": 1, "takenMult": 1, "attrMult": 1, "knownMult": 1, "fold": []}),
    ]))
    check("attrMult-explained hits are not leaks", r1["correctedUnexplainedLeaks"], 0)
    check("attrMult-explained hits are counted", r1["knownMultWithoutFold"]["hits"], 1)
    check("attrMult-explained hits attributed to attrMult",
          r1["knownMultWithoutFold"]["explainedByAttrMultHits"], 1)
    check("retired knownMult criterion still flags the hit",
          r1["retiredKnownMultCriterionLeaks"], 1)

    # 2. a REAL leak (knownMult != 1, empty fold, attrMult == 1) must be caught
    r2 = scan_raw(_mk("1.7.6", 411001, 1000, [
        _hit(1000, {"dealtMult": 1, "takenMult": 1, "attrMult": 1, "knownMult": 2, "fold": []}),
    ]))
    check("true knownMult-without-fold leak detected",
          r2["knownMultWithoutFold"]["unexplainedHits"], 1)
    check("true leak => not_comparable", r2["modelApplicability"], "not_comparable")

    # 3. a REAL fold-identity violation must be caught (fold 1.5 vs chain 1.0)
    r3 = scan_raw(_mk("1.7.6", 411001, 1000, [
        _hit(1000, {"dealtMult": 1, "takenMult": 1, "attrMult": 1, "knownMult": 1.5,
                    "fold": [{"factor": 1.5, "kind": "text", "label": "x"}]}),
        _hit(1000, {"dealtMult": 1, "takenMult": 1, "attrMult": 1, "knownMult": 1.2,
                    "fold": [{"factor": 1.5, "kind": "text", "label": "x"}]}),
    ]))
    check("fold identity leak detected", r3["foldIdentity"]["violationsUnexplainedByRounding"], 2)
    check("fold identity leak rejects the task", r3["modelApplicability"], "not_comparable")

    # 4. sub-unity rounding must be explained, not flagged (the 9999 shape)
    r4 = scan_raw(_mk("1.7.5", 9999, 1000, [
        _hit(1000, {"dealtMult": 1.21, "takenMult": 0.194, "attrMult": 1, "knownMult": 0.235,
                    "fold": [{"factor": 0.9}, {"factor": 0.6}, {"factor": 0.6},
                             {"factor": 0.6}, {"factor": 1.1}, {"factor": 1.1}]}),
        _hit(500, {"dealtMult": 1, "takenMult": 0.028, "attrMult": 1, "knownMult": 0.028,
                   "fold": [{"factor": 0.028}]}),
    ]))
    check("sub-unity hit is strictly over 1e-3", r4["foldIdentity"]["violationsStrictRel1e3"], 1)
    check("sub-unity rounding is explained",
          r4["foldIdentity"]["violationsUnexplainedByRounding"], 0)
    check("training ground is not_comparable", r4["modelApplicability"], "not_comparable")
    check("training ground x0.03 marker", "training_x0.03" in r4["specialMode"]["markers"], True)

    # 5. reconciliation: enemy damage is evidenced, the rest is the gap
    r5 = scan_raw(_mk("1.7.6", 411001, 1000, [
        _hit(600, None, atk=1, atk_team=1),
        _hit(100, None, atk=2, atk_team=2),
        _hit(300, None, atk=0, atk_team=None),
    ], actors=[{"key": 1, "name": "A", "team": 1}, {"key": 2, "name": "B", "team": 2}]))
    check("dealtMinusAnalyzable", r5["reconciliation"]["dealtMinusAnalyzable"], 400.0)
    check("enemy damage is evidence",
          r5["reconciliation"]["excludedDamage"]["enemyTeamDamage"], 100.0)
    check("reconciliationGap kept separate", r5["reconciliation"]["reconciliationGap"], 300.0)
    check("unknown attacker outside dealt",
          r5["reconciliation"]["outsideDealt"]["unknownAttackerDamage"], 300.0)

    # 6. on the real 700817 file the retired rule and the corrected rule disagree
    real = os.path.join(exports, "battle_700817_20261004_114821.json")
    real_note = "real 700817 export not present"
    if os.path.exists(real):
        rr = scan_raw(load_raw(real), real)
        check("700817 retired rule flags hits", rr["retiredKnownMultCriterionLeaks"], 59)
        check("700817 corrected rule flags no leak", rr["correctedUnexplainedLeaks"], 0)
        real_note = ("real 700817: retired=%d corrected=%d (attrMult-explained=%d)" % (
            rr["retiredKnownMultCriterionLeaks"], rr["correctedUnexplainedLeaks"],
            rr["knownMultWithoutFold"]["explainedByAttrMultHits"]))

    print("== P0-D applicability scanner selftest ==")
    for name, got, want, ok in cases:
        print("  [%s] %-52s got=%s want=%s" % ("PASS" if ok else "FAIL", name, got, want))
    print("  %s" % real_note)
    if os.path.exists(real):
        print("FAILING-BEFORE (retired knownMult criterion): 700817 leaks=59 -> judged a fold leak")
        print("PASSING-AFTER  (corrected prod(fold) criterion): unexplained=%d"
              % scan_raw(load_raw(real), real)["correctedUnexplainedLeaks"])
    print("selftest %s (%d/%d)" % ("OK" if not fails else "FAIL",
                                   len(cases) - len(fails), len(cases)))
    return 0 if not fails else 1


def summarize(rows):
    print("")
    print("%-34s %-7s %-7s %-4s %-21s %-3s %-15s %-8s %-8s" % (
        "file", "version", "quest", "sec", "crosscheck", "mm", "applicability",
        "strictV", "unexpV"))
    for r in rows:
        print("%-34s %-7s %-7s %-4s %-21s %-3d %-15s %-8d %-8d" % (
            r["file"][7:41], r["version"], r["quest"],
            "Y" if r["hasContributionSection"] else "-",
            r["crosscheck"].get("status"), r["crosscheck"].get("mismatches", 0),
            r["modelApplicability"],
            r["foldIdentity"]["violationsStrictRel1e3"],
            r["foldIdentity"]["violationsUnexplainedByRounding"]))
    print("")
    print("%-34s %-8s %-9s %-9s %-11s %-13s" % (
        "file", "knfHits", "knfAttr", "knfUnexp", "attrHits", "subUnityFold"))
    for r in rows:
        k = r["knownMultWithoutFold"]
        print("%-34s %-8d %-9d %-9d %-11d %-13d" % (
            r["file"][7:41], k["hits"], k["explainedByAttrMultHits"], k["unexplainedHits"],
            r["attributeAffinity"]["hits"], r["specialMode"]["subUnityFoldHits"]))
    print("")
    print("verdicts by quest:")
    for q, v in verdicts(rows).items():
        print("  quest %-7s exports=%-2d versions=%s app=%s scope=%s knf=%d/%d unexp=%d" % (
            q, v["exports"], ",".join(v["versions"]), ",".join(v["applicability"]),
            ",".join(v["comparisonScopes"]), v["knownMultWithoutFoldExplainedByAttr"],
            v["knownMultWithoutFoldHits"], v["correctedUnexplainedLeaks"]))


def main(argv=None):
    ap = argparse.ArgumentParser(description="P0-D task applicability scanner")
    ap.add_argument("--exports", default=EXPORTS)
    ap.add_argument("--only", action="append")
    ap.add_argument("--json", dest="json_out", default=None)
    ap.add_argument("--no-crosscheck", action="store_true")
    ap.add_argument("--selftest", action="store_true")
    a = ap.parse_args(argv)

    if a.selftest:
        return selftest(a.exports)

    rows = scan_all(a.exports, only=a.only, run_crosscheck=not a.no_crosscheck)
    summarize(rows)
    doc = {
        "tool": "contribution_applicability",
        "schema": "p0d-1",
        "generatedFrom": os.path.abspath(a.exports),
        "exportCount": len(rows),
        "criteria": {
            "missingFoldTest": ("|prod(calc.fold)/(dealtMult*takenMult)-1| > tolerance; "
                                "attrMult excluded by design"),
            "strictRelTol": STRICT_REL,
            "roundingEnvelope": ("5e-4*(|dealtMult|+|takenMult|) + "
                                 "(1e-3 + 5e-5*nFoldFactors)*|dealtMult*takenMult|"),
            "roundingEnvelopeRationale": (
                "dealtMult/takenMult/attrMult/knownMult are serialized with 3 decimals (+-5e-4 "
                "absolute). A sub-unity factor amplifies that absolute rounding: takenMult=0.194 "
                "turns +-5e-4 into 2.6e-3 relative, so a fixed 1e-3 relative gate must be paired "
                "with the absolute term. fold[].factor is round-trip from 1.6.1 and 4-decimal "
                "truncated before, hence the per-factor 5e-5 term scaled by the factor count."),
            "retiredCriterion": ("knownMult != 1 and fold empty (reported for contrast only; "
                                 "NOT a leak test)"),
            "excludedDamageSplit": (
                "enemy-team damage is evidenced but normal/expected; own-side exclusions are the "
                "defect part; anything else is reconciliationGap; unresolved attackers are "
                "outside totals.dealt"),
        },
        "exports": rows,
        "verdicts": verdicts(rows),
    }
    out = a.json_out or DEFAULT_JSON
    with io.open(out, "w", encoding="utf-8") as fh:
        fh.write(json.dumps(doc, ensure_ascii=False, indent=1))
    print("")
    print("wrote %s (%d exports)" % (out, len(rows)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
