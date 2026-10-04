# -*- coding: utf-8 -*-
"""P0-C: offline quantification of the PairTrusted / PairCorroborated effect on contribution.

WHAT THIS IS
------------
A diagnostic. It does NOT change plugin behaviour and it does NOT touch Contribution.Compute.
It re-runs the contribution split three (five) ways over the SAME exports and reports where the
result moves.

IMPLEMENTATION PATH (must be stated, per the task)
--------------------------------------------------
* Owner resolution: contrib.attribution.OwnerIndex.resolve  (the verified offline ladder).
* Fold vocabulary + ln-share split: contrib.aggregate.extract_folds / split_hit.
* The aggregation loop (base / self / assist / rules / links / unattributed / analyzableDealt)
  is re-implemented here line by line after src/Output/Contribution.cs Compute(), because the
  stock contrib.aggregate.analyze() has no per-hit fold-trust predicate. This is an INDEPENDENT
  implementation of the same algorithm, not a call into the C# function.
* Faithfulness is proven by --selftest: variant A is compared field-by-field with the plugin's
  own contribution section (the same side of the crosscheck contrib/crosscheck.py performs),
  and a deliberately broken variant must FAIL that comparison.

CRITERIA (task P0-C)
--------------------
  A  = every calc.fold / calc.atkAdd          (the deployed behaviour; Contribution.Compute)
  B  = folds only where pairTrusted == true
  C  = folds only where pairTrusted == true AND pairCorroborated == true

Section 0.4 discipline: the MAIN experiment keeps the SAME event set and the SAME denominator
(the team-1 hits with a resolvable attacker). An untrusted hit is NOT removed: its folds are
zeroed, so its damage stays in analyzableDealt as attacker base credit. Variants B* and C* --
which DO drop whole hits and therefore change the denominator -- are reported separately and
every one of their rows is flagged denominatorChanged=true.

ASCII-only stdout (the host console is GBK). Names are escaped on stdout; the JSON is UTF-8.
"""
from __future__ import annotations

import argparse
import glob
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
EXPORTS = os.path.join(ROOT, "BepInEx", "plugins", "DpsMeter", "exports")

if HERE not in sys.path:
    sys.path.insert(0, HERE)

from contrib import loader                      # noqa: E402
from contrib.aggregate import extract_folds, split_hit   # noqa: E402
from contrib.attribution import OwnerIndex      # noqa: E402

EPS = 1e-12
TEAM = 1

# The four exports the task requires; 9999 is the training ground and is labelled as such.
REQUIRED = [
    "battle_411001_20261004_115417.json",   # 1.7.6, main sample
    "battle_411001_20261004_041309.json",   # 1.7.4, prior baseline
    "battle_700817_20261004_114821.json",   # 1.7.6, known attrMult quest
    "battle_9999_20261004_042458.json",     # 1.7.5, TRAINING GROUND (no in-domain untrusted)
    # the only exports in the corpus where pairTrusted=false reaches the analysable set:
    "battle_9999_20261003_205129.json",     # 1.5.3, TRAINING GROUND
    "battle_9999_20261003_225530.json",     # 1.5.4, TRAINING GROUND
    "battle_9999_20261003_225647.json",     # 1.5.4, TRAINING GROUND
    "battle_9999_20261004_012138.json",     # 1.5.5, TRAINING GROUND
]

CRITERION_DOC = {
    "A": "all calc.fold + calc.atkAdd (deployed Contribution.Compute)",
    "B": "folds only when calc.pairTrusted == true (same event set / denominator)",
    "C": "folds only when pairTrusted == true AND pairCorroborated == true (same denominator)",
    "B*": "hits only when pairTrusted == true, all their folds (DIFFERENT denominator)",
    "C*": "hits only when pairTrusted && pairCorroborated, all their folds (DIFFERENT denominator)",
}
SUBSET = {"B*", "C*"}


# ---------------------------------------------------------------------------------------------
# trust predicates
# ---------------------------------------------------------------------------------------------
def pair_flags(event):
    """-> (has_calc, pair_trusted, pair_corroborated, pair_str). Missing field is NOT trusted."""
    calc = event.get("calc")
    if not isinstance(calc, dict):
        return False, False, False, None
    pt = calc.get("pairTrusted")
    pc = calc.get("pairCorroborated")
    return True, pt is True, pc is True, calc.get("pair")


def trust_of(criterion, event):
    has_calc, pt, pc, _ = pair_flags(event)
    if criterion == "A":
        return True
    if criterion in ("B", "B*"):
        return pt
    if criterion in ("C", "C*"):
        return pt and pc
    raise ValueError(criterion)


# ---------------------------------------------------------------------------------------------
# one full aggregation pass, mirroring Contribution.Compute
# ---------------------------------------------------------------------------------------------
def compute(export, criterion, idx=None):
    subset = criterion in SUBSET
    idx = idx or OwnerIndex(export, TEAM)

    actors = {}
    for a in export.team_actors(TEAM):
        actors[a.key] = {"key": a.key, "name": a.name, "kind": a.kind, "summon": a.summon,
                         "direct": 0.0, "base": 0.0, "self": 0.0, "assist": 0.0,
                         "received": 0.0, "hits": 0}
    rules = {}          # (name, kind, side, owner_key) -> stat
    rule_events = {}
    links = {}          # (from, to) -> stat
    link_events = {}
    unattr = {}         # reason -> [amount, folds]

    analyzable = 0.0
    pool_total = 0.0
    attributed = 0.0
    unattributed = 0.0
    hits = 0
    folds_total = 0
    calc_missing = 0
    fold_dropped = 0
    fold_dropped_hits = 0
    negative = 0
    reason_counts = {}
    # exclusion bookkeeping
    ex = {"foldHits": 0, "foldDamage": 0.0, "foldPool": 0.0,
          "subsetHits": 0, "subsetDamage": 0.0}
    diag = {}

    keep = True  # set by caller if needed; kept for symmetry with the core

    for i, e in enumerate(export.events):
        if e.get("type") != "dmg":
            continue
        try:
            damage = float(e.get("amount") or 0.0)
        except (TypeError, ValueError):
            damage = 0.0
        key = e.get("atkKey")
        row = export.by_key.get(key) if key is not None else None
        if row is None or row.team != TEAM:
            # same exclusion as Contribution.cs:321 -- not part of analyzableDealt at all
            continue
        trusted = trust_of(criterion, e)
        if subset and not trusted:
            ex["subsetHits"] += 1
            ex["subsetDamage"] += damage
            continue

        hits += 1
        analyzable += damage
        calc = e.get("calc")
        if not isinstance(calc, dict):
            calc_missing += 1
            calc = {}
        fd = calc.get("foldDropped")
        if isinstance(fd, (int, float)) and fd > 0:
            fold_dropped += int(fd)
            fold_dropped_hits += 1

        folds_all = extract_folds(e, diag)
        if not trusted and folds_all:
            ex["foldHits"] += 1
            ex["foldDamage"] += damage

        folds = folds_all if trusted else []
        folds_total += len(folds)

        # pool that the untrusted composition WOULD have moved (diagnostic; A's pool on those hits)
        if not trusted and folds_all:
            _m, _b, pool_would, _s = split_hit(damage, folds_all)
            ex["foldPool"] += pool_would

        M, base, pool, shares = split_hit(damage, folds)
        pool_total += pool

        attacker = idx.by_key.get(row.key) or row
        ac = actors[attacker.key]
        ac["direct"] += damage
        ac["hits"] += 1
        ac["base"] += base

        for f, share in zip(folds, shares):
            if share < 0:
                negative += 1
            owner, reason = idx.resolve(f, attacker)
            reason_counts[reason] = reason_counts.get(reason, 0) + 1
            if owner is None:
                unattributed += share
                slot = unattr.setdefault(reason, [0.0, 0])
                slot[0] += share
                slot[1] += 1
                continue
            if owner.key == attacker.key:
                ac["self"] += share
            else:
                ac["received"] += share
                actors[owner.key]["assist"] += share
                lk = (owner.key, attacker.key)
                ls = links.get(lk)
                if ls is None:
                    ls = links[lk] = {"from": owner.key, "to": attacker.key, "amount": 0.0, "folds": 0}
                ls["amount"] += share
                ls["folds"] += 1
                link_events.setdefault(lk, set()).add(hits - 1)
            name = _rule_name(f)
            rk = (name, f.kind, f.side, owner.key)
            rs = rules.get(rk)
            if rs is None:
                rs = rules[rk] = {"ruleName": name, "kind": f.kind, "side": f.side,
                                  "ownerKey": owner.key, "ownerName": owner.name,
                                  "folds": 0, "damageEquivalent": 0.0}
            rs["folds"] += 1
            rs["damageEquivalent"] += share
            rule_events.setdefault(rk, set()).add(hits - 1)

    attributed = sum(a["base"] + a["self"] + a["assist"] for a in actors.values())

    # per-hit identity check (I1): sum(credit lines)+unattributed == damage for each analyzed hit
    identity_ok, identity_max = _identity_check(export, idx, criterion)

    out_actors = []
    for a in actors.values():
        total = a["base"] + a["self"] + a["assist"]
        out_actors.append({
            "key": a["key"], "name": a["name"], "kind": a["kind"], "summon": a["summon"],
            "directDamage": a["direct"], "baseCredit": a["base"], "selfRuleCredit": a["self"],
            "assistCredit": a["assist"], "totalCredit": total,
            "totalShare": (total / analyzable) if analyzable else 0.0,
            "hits": a["hits"],
        })
    out_actors.sort(key=lambda r: (-r["totalCredit"], r["key"]))
    for n, r in enumerate(out_actors):
        r["rank"] = n + 1

    out_rules = []
    for rk, rs in rules.items():
        rs = dict(rs)
        rs["hits"] = len(rule_events.get(rk, ()))
        out_rules.append(rs)
    out_rules.sort(key=lambda r: (-r["damageEquivalent"], r["ruleName"], r["kind"], r["side"]))

    out_links = []
    for lk, ls in links.items():
        ls = dict(ls)
        ls["hits"] = len(link_events.get(lk, ()))
        out_links.append(ls)
    out_links.sort(key=lambda r: -r["amount"])

    return {
        "criterion": criterion,
        "definition": CRITERION_DOC[criterion],
        "subset": subset,
        "denominatorChanged": bool(subset),
        "analyzableDealt": analyzable,
        "hits": hits,
        "poolTotal": pool_total,
        "attributedDamage": attributed,
        "unattributedDamage": unattributed,
        "creditedShare": (attributed / analyzable) if analyzable else 0.0,
        "folds": folds_total,
        "calcMissingHits": calc_missing,
        "foldDropped": fold_dropped,
        "foldDroppedHits": fold_dropped_hits,
        "negativeLines": negative,
        "reasonCounts": dict(sorted(reason_counts.items(), key=lambda kv: -kv[1])),
        "excluded": ex,
        "identityOk": identity_ok,
        "identityMaxError": identity_max,
        "actors": out_actors,
        "rules": out_rules,
        "links": out_links,
        "unattributed": [{"reason": k, "amount": v[0], "folds": int(v[1])}
                         for k, v in sorted(unattr.items(), key=lambda kv: -kv[1][0])],
    }


def _rule_name(f):
    """Mirror Contribution.RuleName: [..] inside the label, else the label, else the origin."""
    lab = f.label or ""
    a = lab.find("[")
    b = lab.find("]")
    if a >= 0 and b > a:
        return lab[a + 1:b]
    if lab.strip():
        return lab.strip()
    return str(f.origin or "")


def _identity_check(export, idx, criterion):
    """Re-walk the same predicate and assert credit + unattributed == damage per hit (I1)."""
    subset = criterion in SUBSET
    worst = 0.0
    ok = True
    for e in export.events:
        if e.get("type") != "dmg":
            continue
        try:
            damage = float(e.get("amount") or 0.0)
        except (TypeError, ValueError):
            damage = 0.0
        key = e.get("atkKey")
        row = export.by_key.get(key) if key is not None else None
        if row is None or row.team != TEAM:
            continue
        trusted = trust_of(criterion, e)
        if subset and not trusted:
            continue
        attacker = idx.by_key.get(row.key) or row
        folds_all = extract_folds(e, {})
        folds = folds_all if trusted else []
        _M, base, _pool, shares = split_hit(damage, folds)
        credited = base
        unattributed = 0.0
        for f, share in zip(folds, shares):
            owner, _reason = idx.resolve(f, attacker)
            if owner is None:
                unattributed += share
            else:
                credited += share
        err = abs(credited + unattributed - damage)
        if err > worst:
            worst = err
        if damage != 0 and err > 1e-6:
            ok = False
    return ok, worst


def pair_census(export):
    """Light census (no aggregation): where do the pairing flags actually land?

    inDomain  = the exact predicate Contribution.Compute uses (team-1 dmg event with a resolvable
                attacker key, Contribution.cs:321). Only these hits can change a contribution.
    outOfDomain = every other dmg event; Contribution.Compute never receives them, so their
                pairing quality is invisible to the contribution table.
    """
    d = {"pairTrustedHits": 0, "pairUntrustedHits": 0, "pairCorroboratedHits": 0,
         "pairTrustedHitsWithFolds": 0, "pairUntrustedHitsWithFolds": 0,
         "pairCorroboratedHitsWithFolds": 0,
         "pairUntrustedDamage": 0.0, "pairUntrustedDamageWithFolds": 0.0,
         "pairCensus": {}, "pairFlagMissingWithCalc": 0, "calcMissingHits": 0,
         "outOfDomainUntrustedHits": 0, "outOfDomainUntrustedHitsWithFolds": 0,
         "outOfDomainUntrustedDamage": 0.0, "outOfDomainUntrustedPool": 0.0,
         "outOfDomainPairCensus": {}, "pairStringCensus": {}}
    for e in export.events:
        if e.get("type") != "dmg":
            continue
        key = e.get("atkKey")
        row = export.by_key.get(key) if key is not None else None
        in_domain = row is not None and row.team == TEAM
        has_calc, pt, pc, _pair = pair_flags(e)
        calc = e.get("calc") if isinstance(e.get("calc"), dict) else {}
        if isinstance(calc, dict) and calc:
            k = str(calc.get("pair"))
            d["pairStringCensus"][k] = d["pairStringCensus"].get(k, 0) + 1
        folds = extract_folds(e, {}) if has_calc else []
        has_folds = bool(folds)
        try:
            damage = float(e.get("amount") or 0.0)
        except (TypeError, ValueError):
            damage = 0.0
        if in_domain:
            if not has_calc:
                d["calcMissingHits"] += 1
                d["pairCensus"]["no-calc"] = d["pairCensus"].get("no-calc", 0) + 1
                continue
            if "pairTrusted" not in calc:
                d["pairFlagMissingWithCalc"] += 1
            label = "true" if pt else "false"
            d["pairCensus"][label] = d["pairCensus"].get(label, 0) + 1
            if pt:
                d["pairTrustedHits"] += 1
                if has_folds:
                    d["pairTrustedHitsWithFolds"] += 1
            else:
                d["pairUntrustedHits"] += 1
                d["pairUntrustedDamage"] += damage
                if has_folds:
                    d["pairUntrustedHitsWithFolds"] += 1
                    d["pairUntrustedDamageWithFolds"] += damage
            if pc:
                d["pairCorroboratedHits"] += 1
                if has_folds:
                    d["pairCorroboratedHitsWithFolds"] += 1
        else:
            label = "no-calc" if not has_calc else ("true" if pt else "false")
            d["outOfDomainPairCensus"][label] = d["outOfDomainPairCensus"].get(label, 0) + 1
            if has_calc and not pt:
                d["outOfDomainUntrustedHits"] += 1
                d["outOfDomainUntrustedDamage"] += damage
                if has_folds:
                    d["outOfDomainUntrustedHitsWithFolds"] += 1
                    _m, _b, pool, _s = split_hit(damage, folds)
                    d["outOfDomainUntrustedPool"] += pool
    return d


def pair_diagnostics(export, idx=None, a=None, b=None):
    """pair_census plus the credit actually moved by the B criterion."""
    idx = idx or OwnerIndex(export, TEAM)
    d = pair_census(export)
    # pairUntrustedCredit == the A-pool sitting on IN-DOMAIN untrusted hits (what B moves to base)
    a = a if a is not None else compute(export, "A", idx)
    b = b if b is not None else compute(export, "B", idx)
    d["pairUntrustedCredit"] = a["poolTotal"] - b["poolTotal"]
    d["poolTotalA"] = a["poolTotal"]
    return d


# ---------------------------------------------------------------------------------------------
# plugin-section comparison (faithfulness proof for variant A)
# ---------------------------------------------------------------------------------------------
def compare_with_plugin(export, mine, tol_abs=1e-3):
    sec = export.raw.get("contribution")
    if sec is None:
        return {"status": "ABSENT", "mismatches": []}
    mm = []

    def close(a, b):
        return abs(a - b) <= max(tol_abs, 1e-9 * max(abs(a), abs(b)))

    cov = sec.get("coverage") or {}
    for field, theirs, mine_v in (
            ("coverage.analyzableDealt", cov.get("analyzableDealt"), mine["analyzableDealt"]),
            ("coverage.hits", cov.get("hits"), mine["hits"]),
            ("attributedDamage", sec.get("attributedDamage"), mine["attributedDamage"]),
            ("unattributedDamage", sec.get("unattributedDamage"), mine["unattributedDamage"])):
        if theirs is None or not close(float(theirs), float(mine_v)):
            mm.append({"field": field, "offline": mine_v, "plugin": theirs})
    diag = sec.get("diagnostics") or {}
    if diag.get("poolTotal") is not None and not close(float(diag["poolTotal"]), mine["poolTotal"]):
        mm.append({"field": "diagnostics.poolTotal", "offline": mine["poolTotal"],
                   "plugin": diag["poolTotal"]})
    if diag.get("foldAccounting", {}).get("total") is not None and \
            int(diag["foldAccounting"]["total"]) != mine["folds"]:
        mm.append({"field": "diagnostics.foldAccounting.total", "offline": mine["folds"],
                   "plugin": diag["foldAccounting"]["total"]})
    mine_actors = {a["key"]: a for a in mine["actors"]}
    for row in sec.get("actors") or []:
        k = row.get("key")
        m = mine_actors.get(k)
        if m is None:
            mm.append({"field": "actors[%s]" % k, "offline": None, "plugin": row.get("name")})
            continue
        for f, mine_v in (("directDamage", m["directDamage"]), ("baseCredit", m["baseCredit"]),
                          ("selfRuleCredit", m["selfRuleCredit"]), ("assistCredit", m["assistCredit"]),
                          ("totalCredit", m["totalCredit"])):
            if row.get(f) is None or not close(float(row[f]), mine_v):
                mm.append({"field": "actors[%s].%s" % (k, f), "offline": mine_v, "plugin": row.get(f)})
    mine_rules = {"%s|%s|%s|%s" % (r["ruleName"], r["kind"], r["side"], r["ownerName"]): r
                  for r in mine["rules"]}
    for row in sec.get("rules") or []:
        key = "%s|%s|%s|%s" % (row.get("ruleName"), row.get("kind"), row.get("side"), row.get("ownerName"))
        if key not in mine_rules:
            mm.append({"field": "rules[%s]" % key, "offline": None,
                       "plugin": row.get("damageEquivalent")})
            continue
        if row.get("damageEquivalent") is None or not close(float(row["damageEquivalent"]),
                                                            mine_rules[key]["damageEquivalent"]):
            mm.append({"field": "rules[%s].damageEquivalent" % key,
                       "offline": mine_rules[key]["damageEquivalent"],
                       "plugin": row.get("damageEquivalent")})
    for key, m in mine_rules.items():
        if key not in {"%s|%s|%s|%s" % (r.get("ruleName"), r.get("kind"), r.get("side"), r.get("ownerName"))
                       for r in sec.get("rules") or []}:
            mm.append({"field": "rules[%s] missing from plugin" % key,
                       "offline": m["damageEquivalent"], "plugin": None})
    return {"status": "OK" if not mm else "MISMATCH", "mismatches": mm}


def compare_with_offline_core(export, mine, tol=1e-6):
    """Second faithfulness check: variant A vs the stock verified offline core
    (contrib.aggregate.analyze). This one also works on 1.5.x exports that carry no
    contribution section, so the A/B/C machinery is validated on the old training files too."""
    from contrib import aggregate
    an = aggregate.analyze(export, TEAM)
    mm = []

    def close(x, y):
        return abs(x - y) <= max(1e-3, tol * max(abs(x), abs(y)))

    for field, x, y in (("analyzableDealt", an.analyzable, mine["analyzableDealt"]),
                        ("poolTotal", an.pool_total, mine["poolTotal"]),
                        ("attributedDamage", an.actor_total_credit(), mine["attributedDamage"]),
                        ("hits", float(an.hits), float(mine["hits"])),
                        ("folds", float(an.diagnostics.get("fold_total", 0)), float(mine["folds"]))):
        if not close(float(x), float(y)):
            mm.append({"field": field, "offlineCore": x, "thisRun": y})
    mine_actors = {a["key"]: a for a in mine["actors"]}
    for k, ac in an.actors.items():
        m = mine_actors.get(k)
        if m is None:
            mm.append({"field": "actor[%s]" % k, "offlineCore": ac.total, "thisRun": None})
            continue
        for f, x, y in (("total", ac.total, m["totalCredit"]), ("base", ac.base, m["baseCredit"]),
                        ("self", ac.self_rule, m["selfRuleCredit"]),
                        ("assist", ac.assist, m["assistCredit"]),
                        ("direct", ac.direct, m["directDamage"])):
            if not close(x, y):
                mm.append({"field": "actor[%s].%s" % (k, f), "offlineCore": x, "thisRun": y})
    mine_rules = {"%s|%s|%s|%s" % (r["ruleName"], r["kind"], r["side"], r["ownerName"]): r
                  for r in mine["rules"]}
    for rk, rs in an.rules.items():
        key = "%s|%s|%s|%s" % (rs.name, rs.kind, rs.side, rs.owner_name)
        m = mine_rules.get(key)
        if m is None:
            mm.append({"field": "rule[%s]" % key, "offlineCore": rs.damage, "thisRun": None})
        elif not close(rs.damage, m["damageEquivalent"]):
            mm.append({"field": "rule[%s].damage" % key, "offlineCore": rs.damage,
                       "thisRun": m["damageEquivalent"]})
    return {"status": "OK" if not mm else "MISMATCH", "mismatches": mm[:20],
            "mismatchCount": len(mm)}


# ---------------------------------------------------------------------------------------------
# diffs
# ---------------------------------------------------------------------------------------------
def _diff(base, other, base_name, other_name):
    actors_a = {a["key"]: a for a in base["actors"]}
    actors_b = {a["key"]: a for a in other["actors"]}
    rows = []
    for k in sorted(set(actors_a) | set(actors_b)):
        a = actors_a.get(k)
        b = actors_b.get(k)
        ta = a["totalCredit"] if a else 0.0
        tb = b["totalCredit"] if b else 0.0
        sa = a["totalShare"] if a else 0.0
        sb = b["totalShare"] if b else 0.0
        def gv(x, f):
            return x[f] if x else 0.0
        rows.append({
            "key": k,
            "name": (b or a)["name"],
            "totalCredit_base": ta,
            "totalCredit_other": tb,
            "deltaCredit": tb - ta,
            "deltaSharePct": (sb - sa) * 100.0,
            "baseCredit_base": gv(a, "baseCredit"), "baseCredit_other": gv(b, "baseCredit"),
            "selfRuleCredit_base": gv(a, "selfRuleCredit"), "selfRuleCredit_other": gv(b, "selfRuleCredit"),
            "assistCredit_base": gv(a, "assistCredit"), "assistCredit_other": gv(b, "assistCredit"),
            "rank_base": a["rank"] if a else None,
            "rank_other": b["rank"] if b else None,
            "rankMove": (a["rank"] - b["rank"]) if (a and b) else None,   # >0 = moved up
        })
    rows.sort(key=lambda r: -abs(r["deltaCredit"]))

    rules_a = {"%s|%s|%s|%s" % (r["ruleName"], r["kind"], r["side"], r["ownerName"]): r
               for r in base["rules"]}
    rules_b = {"%s|%s|%s|%s" % (r["ruleName"], r["kind"], r["side"], r["ownerName"]): r
               for r in other["rules"]}
    rrows = []
    for key in set(rules_a) | set(rules_b):
        a = rules_a.get(key)
        b = rules_b.get(key)
        da = a["damageEquivalent"] if a else 0.0
        db = b["damageEquivalent"] if b else 0.0
        rrows.append({"rule": key, "damage_base": da, "damage_other": db,
                      "deltaDamage": db - da,
                      "removed": a is not None and b is None,
                      "appeared": a is None and b is not None})
    rrows.sort(key=lambda r: -abs(r["deltaDamage"]))

    def top1(cred):
        s = sorted(cred["actors"], key=lambda r: (-r["totalCredit"], r["key"]))
        return s[0]["key"] if s else None

    def top3(cred):
        s = sorted(cred["actors"], key=lambda r: (-r["totalCredit"], r["key"]))
        return tuple(r["key"] for r in s[:3])

    pool_delta = other["poolTotal"] - base["poolTotal"]
    summary = {
        "base": base_name, "other": other_name,
        "analyzableDelta": other["analyzableDealt"] - base["analyzableDealt"],
        "denominatorChanged": bool(other["denominatorChanged"] or base["denominatorChanged"]),
        "poolTotalDelta": pool_delta,
        "poolTotalDeltaPctOfBasePool": (pool_delta / base["poolTotal"] * 100.0) if base["poolTotal"] else 0.0,
        "attributedDelta": other["attributedDamage"] - base["attributedDamage"],
        "maxAbsActorCreditDelta": max((abs(r["deltaCredit"]) for r in rows), default=0.0),
        "maxAbsActorShareDeltaPct": max((abs(r["deltaSharePct"]) for r in rows), default=0.0),
        "maxAbsActorBaseDelta": max((abs(r["baseCredit_other"] - r["baseCredit_base"]) for r in rows), default=0.0),
        "maxAbsActorSelfDelta": max((abs(r["selfRuleCredit_other"] - r["selfRuleCredit_base"]) for r in rows), default=0.0),
        "maxAbsActorAssistDelta": max((abs(r["assistCredit_other"] - r["assistCredit_base"]) for r in rows), default=0.0),
        "anyAssistMoved": any(abs(r["assistCredit_other"] - r["assistCredit_base"]) > 1e-6 for r in rows),
        "actorsShareMovedOver01pp": sum(1 for r in rows if abs(r["deltaSharePct"]) > 0.1),
        "top1Changed": top1(base) != top1(other),
        "top1_base": top1(base), "top1_other": top1(other),
        "top3SetChanged": set(top3(base)) != set(top3(other)),
        "rulesRemoved": sum(1 for r in rrows if r["removed"]),
        "rulesAppeared": sum(1 for r in rrows if r["appeared"]),
        "maxAbsRuleDelta": max((abs(r["deltaDamage"]) for r in rrows), default=0.0),
        "actorDeltas": rows,
        "ruleDeltas": rrows,
    }
    return summary


def analyze_export(path):
    export = loader.load(path)
    idx = OwnerIndex(export, TEAM)
    crit = {}
    for c in ("A", "B", "C", "B*", "C*"):
        crit[c] = compute(export, c, idx)
    diag = pair_diagnostics(export, idx, crit["A"], crit["B"])
    out = {
        "file": export.name,
        "version": export.version,
        "quest": export.quest,
        "training": int(export.quest or 0) == 9999,
        "duration": export.duration,
        "result": export.result,
        "hasContributionSection": export.raw.get("contribution") is not None,
        "criteria": crit,
        "diagnostics": diag,
        "diff": {
            "B_vs_A": _diff(crit["A"], crit["B"], "A", "B"),
            "C_vs_A": _diff(crit["A"], crit["C"], "A", "C"),
            "C_vs_B": _diff(crit["B"], crit["C"], "B", "C"),
            "Bstar_vs_A": _diff(crit["A"], crit["B*"], "A", "B*"),
            "Cstar_vs_A": _diff(crit["A"], crit["C*"], "A", "C*"),
        },
    }
    out["pluginCrosscheck"] = compare_with_plugin(export, crit["A"])
    out["offlineCoreCrosscheck"] = compare_with_offline_core(export, crit["A"])
    return out


# ---------------------------------------------------------------------------------------------
# survey: pair flag census over every export (cheap; no full aggregation)
# ---------------------------------------------------------------------------------------------
def survey_export(path):
    export = loader.load(path)
    idx = OwnerIndex(export, TEAM)
    d = pair_census(export)
    a = compute(export, "A", idx)
    b = compute(export, "B", idx)
    return {"file": export.name, "version": export.version, "quest": export.quest,
            "training": int(export.quest or 0) == 9999,
            "hasContributionSection": export.raw.get("contribution") is not None,
            "pairTrustedHits": d["pairTrustedHits"],
            "pairUntrustedHits": d["pairUntrustedHits"],
            "pairUntrustedHitsWithFolds": d["pairUntrustedHitsWithFolds"],
            "pairUntrustedDamage": d["pairUntrustedDamage"],
            "pairUntrustedDamageWithFolds": d["pairUntrustedDamageWithFolds"],
            "pairUntrustedCredit": a["poolTotal"] - b["poolTotal"],
            "poolTotalA": a["poolTotal"],
            "poolTotalB": b["poolTotal"],
            "pairCorroboratedHits": d["pairCorroboratedHits"],
            "outOfDomainUntrustedHits": d["outOfDomainUntrustedHits"],
            "outOfDomainUntrustedHitsWithFolds": d["outOfDomainUntrustedHitsWithFolds"],
            "outOfDomainUntrustedPool": d["outOfDomainUntrustedPool"],
            "pairCensus": d["pairCensus"],
            "outOfDomainPairCensus": d["outOfDomainPairCensus"],
            "pairStringCensus": d["pairStringCensus"]}


# ---------------------------------------------------------------------------------------------
# reporting helpers
# ---------------------------------------------------------------------------------------------
def esc(s):
    return json.dumps(s if s is not None else "", ensure_ascii=True)[1:-1]


def fmt_money(v):
    return "%.4f" % v


def report_text(results, survey=None):
    L = []
    A = L.append
    A("P0-C PairTrusted / PairCorroborated offline impact")
    A("=" * 72)
    A("evidence class: OFFLINE REPLAY (recomputed from exports; no live plugin run)")
    A("implementation: independent Python re-run of Contribution.Compute (see module docstring)")
    A("  owner ladder : contrib.attribution.OwnerIndex.resolve")
    A("  fold vocab   : contrib.aggregate.extract_folds   split: contrib.aggregate.split_hit")
    A("  aggregation  : local loop mirroring src/Output/Contribution.cs Compute()")
    A("")
    A("criteria:")
    for c in ("A", "B", "C", "B*", "C*"):
        A("  %-3s %s" % (c, CRITERION_DOC[c]))
    A("  A/B/C keep the same event set and denominator; only the fold trust varies.")
    A("  B*/C* (marked) drop whole hits -> DIFFERENT denominator, not rank-comparable with A.")
    A("")
    for r in results:
        A("-" * 72)
        A("export : %s" % r["file"])
        A("version: %s   quest: %s%s   contribution section: %s" %
          (r["version"], r["quest"], "  [TRAINING GROUND]" if r["training"] else "",
           r["hasContributionSection"]))
        pc = r["pluginCrosscheck"]
        A("variant A vs plugin contribution section: %s%s" %
          (pc["status"], "" if pc["status"] == "ABSENT" else "  mismatches=%d" % len(pc["mismatches"])))
        oc = r.get("offlineCoreCrosscheck")
        if oc is not None:
            A("variant A vs offline core contrib.aggregate.analyze: %s  mismatches=%d" %
              (oc["status"], oc["mismatchCount"]))
        d = r["diagnostics"]
        A("pairing census IN DOMAIN (team-1 hits Contribution.Compute actually receives):")
        A("  trusted=%d  untrusted=%d (with folds=%d, damage=%.1f)  corroborated=%d" %
          (d["pairTrustedHits"], d["pairUntrustedHits"], d["pairUntrustedHitsWithFolds"],
           d["pairUntrustedDamageWithFolds"], d["pairCorroboratedHits"]))
        A("  pairUntrustedCredit (A-pool on in-domain untrusted hits, i.e. what B moves) = %.4f"
          % d["pairUntrustedCredit"])
        A("pairing census OUT OF DOMAIN (hits excluded at Contribution.cs:321):")
        A("  untrusted=%d (with folds=%d, damage=%.1f, would-be pool=%.2f)" %
          (d["outOfDomainUntrustedHits"], d["outOfDomainUntrustedHitsWithFolds"],
           d["outOfDomainUntrustedDamage"], d["outOfDomainUntrustedPool"]))
        A("  in-domain pair string census: %s" % json.dumps(d["pairStringCensus"], ensure_ascii=True))
        A("")
        A("  %-4s %16s %8s %18s %18s %11s %11s %10s %14s" %
          ("crit", "analyzableDealt", "hits", "poolTotal", "attributedDamage", "pool%",
           "credShare", "exclHits", "exclDamage"))
        for c in ("A", "B", "C", "B*", "C*"):
            x = r["criteria"][c]
            ex = x["excluded"]
            if x["subset"]:
                excl_h, excl_d = ex["subsetHits"], ex["subsetDamage"]
            else:
                excl_h, excl_d = ex["foldHits"], ex["foldDamage"]
            A("  %-4s %16.2f %8d %18.2f %18.2f %10.4f%% %11.6f %10d %14.2f" %
              (c, x["analyzableDealt"], x["hits"], x["poolTotal"], x["attributedDamage"],
               100.0 * x["poolTotal"] / x["analyzableDealt"] if x["analyzableDealt"] else 0.0,
               x["creditedShare"], excl_h, excl_d))
        A("  exclHits/exclDamage: A/B/C = hits whose folds were zeroed but whose damage STAYS in")
        A("    the denominator (only the composition is withdrawn); B*/C* = hits dropped entirely,")
        A("    so their analyzableDealt differs (denominatorChanged=true).")
        A("")
        # ---- full per-criterion actor matrix (the task's mandatory per-role output) ----
        crit_names = ("A", "B", "C", "B*", "C*")
        cred = {c: {a["key"]: a for a in r["criteria"][c]["actors"]} for c in crit_names}

        def cell(x):
            if x is None:
                return "%25s" % "-"
            return "%18.0f/%5.2f%%" % (x["totalCredit"], x["totalShare"] * 100.0)

        keys = sorted(set().union(*[set(d) for d in cred.values()]),
                      key=lambda k: (-cred["A"].get(k, {}).get("totalCredit", 0.0), k))
        A("  actor totalCredit / totalShare%% by criterion (full list; key is the stable actor id):")
        A("  %-6s %-44s %s" % ("key", "actor (escaped)", " ".join("%25s" % c for c in crit_names)))
        for k in keys:
            name = next((cred[c][k]["name"] for c in crit_names if k in cred[c]), "")
            A("  %-6d %-44s %s" % (k, esc(name), " ".join(cell(cred[c].get(k)) for c in crit_names)))
        A("")
        # ---- top rules by A damageEquivalent, by criterion ----
        base_rules = sorted(r["criteria"]["A"]["rules"], key=lambda x: -x["damageEquivalent"])[:12]
        A("  top 12 rules by A damageEquivalent, damageEquivalent by criterion:")
        A("  %s  %s" % ("rule|kind|side|owner (escaped)", " ".join("%18s" % c for c in crit_names)))
        rmap = {c: {"%s|%s|%s|%s" % (rr["ruleName"], rr["kind"], rr["side"], rr["ownerName"]): rr
                    for rr in r["criteria"][c]["rules"]} for c in crit_names}
        for rr in base_rules:
            key = "%s|%s|%s|%s" % (rr["ruleName"], rr["kind"], rr["side"], rr["ownerName"])
            cells = []
            for c in crit_names:
                x = rmap[c].get(key)
                cells.append("%18.2f" % x["damageEquivalent"] if x else "%18s" % "-")
            A("  %s  %s" % (esc(key), " ".join(cells)))
        A("")
        for pair in ("B_vs_A", "C_vs_A", "Bstar_vs_A", "Cstar_vs_A"):
            s = r["diff"][pair]
            A("  diff %-12s poolDelta=%15.2f (%+8.4f%% of A pool) attributedDelta=%15.2f" %
              (pair, s["poolTotalDelta"], s["poolTotalDeltaPctOfBasePool"], s["attributedDelta"]))
            A("       denominatorChanged=%s maxAbsActorCreditDelta=%.2f maxAbsActorShareDelta=%.4fpp "
              "top1Changed=%s top3SetChanged=%s rulesRemoved=%d rulesAppeared=%d" %
              (s["denominatorChanged"], s["maxAbsActorCreditDelta"], s["maxAbsActorShareDeltaPct"],
               s["top1Changed"], s["top3SetChanged"], s["rulesRemoved"], s["rulesAppeared"]))
            A("       component deltas: maxBase=%.2f maxSelf=%.2f maxAssist=%.2f anyAssistMoved=%s" %
              (s["maxAbsActorBaseDelta"], s["maxAbsActorSelfDelta"], s["maxAbsActorAssistDelta"],
               s["anyAssistMoved"]))
            if abs(s["poolTotalDelta"]) > 0 and not s["denominatorChanged"]:
                A("       actor deltas (top 5):")
                for row in s["actorDeltas"][:5]:
                    A("         %-28s credits %16.2f -> %16.2f  d=%+15.2f  share %+7.4fpp  rank %s->%s" %
                      (esc(row["name"]), row["totalCredit_base"], row["totalCredit_other"],
                       row["deltaCredit"], row["deltaSharePct"], row["rank_base"], row["rank_other"]))
                A("       rule deltas (top 5):")
                for row in s["ruleDeltas"][:5]:
                    if abs(row["deltaDamage"]) <= 0:
                        continue
                    A("         %-40s %16.2f -> %16.2f  d=%+15.2f%s" %
                      (esc(row["rule"]), row["damage_base"], row["damage_other"], row["deltaDamage"],
                       " [removed]" if row["removed"] else (" [appeared]" if row["appeared"] else "")))
        A("")
    if survey is not None:
        A("=" * 72)
        A("corpus survey (all exports in %s)" % os.path.relpath(EXPORTS, ROOT))
        A("in-domain untrusted = hits that actually reach Contribution.Compute;")
        A("out-domain untrusted = pairTrusted=false hits already excluded at Contribution.cs:321.")
        A("%-42s %7s %6s %8s %9s %9s %9s %12s %11s %11s" %
          ("file", "version", "quest", "trusted", "unt-dom", "unt(dom)", "out-dom", "out-dom pool",
           "untCredit", "corrob"))
        for s in survey:
            A("%-42s %7s %6s %8d %9d %9d %9d %12.0f %11.2f %11d" %
              (s["file"][7:], s["version"], s["quest"], s["pairTrustedHits"], s["pairUntrustedHits"],
               s["pairUntrustedHitsWithFolds"], s["outOfDomainUntrustedHits"],
               s["outOfDomainUntrustedPool"], s["pairUntrustedCredit"], s["pairCorroboratedHits"]))
        A("")
    return "\n".join(L)


# ---------------------------------------------------------------------------------------------
# selftest
# ---------------------------------------------------------------------------------------------
def selftest():
    """Fail-first guard: the variant-A comparison must ACCEPT a faithful computation and REJECT a
    broken one, and the corpus claims the report relies on must hold (or the run exits non-zero)."""
    checks = []

    def check(name, ok, detail):
        checks.append((name, ok, detail))
        print("[%s] %s :: %s" % ("PASS" if ok else "FAIL", name, detail))

    main = os.path.join(EXPORTS, "battle_411001_20261004_115417.json")
    export = loader.load(main)

    # T1 positive: variant A == plugin's own contribution section
    a = compute(export, "A")
    res = compare_with_plugin(export, a)
    check("T1 A-variant matches plugin contribution section",
          res["status"] == "OK",
          "status=%s mismatches=%d" % (res["status"], len(res["mismatches"])))

    # T2 negative control: dropping calc.atkAdd MUST be detected by the same comparison
    import contrib.aggregate as agg
    saved = agg.extract_folds
    try:
        # compute() resolved extract_folds into its module globals at import time
        broken = _compute_no_atkadd(export)
    finally:
        agg.extract_folds = saved
    badres = compare_with_plugin(export, broken)
    check("T2 broken variant (atkAdd dropped) is rejected",
          badres["status"] == "MISMATCH" and len(badres["mismatches"]) > 0,
          "status=%s mismatches=%d" % (badres["status"], len(badres["mismatches"])))
    if badres["mismatches"]:
        m = badres["mismatches"][0]
        print("       first detected: %s offline=%s plugin=%s" %
              (esc(m["field"]), m["offline"], m["plugin"]))

    # T1b second faithfulness check: variant A == the stock verified offline core
    from contrib import aggregate as _agg
    core_an = _agg.analyze(export, TEAM)
    mine_an = a
    same = (abs(core_an.analyzable - mine_an["analyzableDealt"]) < 1e-6
            and abs(core_an.pool_total - mine_an["poolTotal"]) < 1e-3
            and abs(core_an.actor_total_credit() - mine_an["attributedDamage"]) < 1e-3
            and core_an.hits == mine_an["hits"])
    check("T1b A-variant matches contrib.aggregate.analyze", same,
          "core analyzable=%.4f pool=%.4f hist=%d | mine analyzable=%.4f pool=%.4f hits=%d" %
          (core_an.analyzable, core_an.pool_total, core_an.hits,
           mine_an["analyzableDealt"], mine_an["poolTotal"], mine_an["hits"]))
    old = os.path.join(EXPORTS, "battle_9999_20261004_012138.json")   # 1.5.5, no contribution section
    oex = loader.load(old)
    ores = compare_with_offline_core(oex, compute(oex, "A"))
    check("T1c core cross-check on a 1.5.5 export (no contribution section)",
          ores["status"] == "OK",
          "status=%s mismatches=%d" % (ores["status"], ores["mismatchCount"]))

    # T3 domain claim: in the NORMAL 1.7.x corpus the in-domain untrusted hit count is 0 => B == A
    # exactly. SCOPED 2026-10-04 after three new 1.7.8 battles arrived: the training ground is the one
    # domain where in-domain untrusted hits do occur, so it cannot belong to a claim about the domain in
    # which the gate CHOICE would matter. It is not excused -- T3c and T4c measure what it costs there.
    def ver_tuple(v):
        try:
            return tuple(int(x) for x in str(v).split("."))
        except ValueError:
            return (0,)
    allp = sorted(glob.glob(os.path.join(EXPORTS, "battle_*.json")))
    bad = []
    checked = 0
    hit_anywhere = []          # (name, version, quest, in-domain untrusted hits)
    for p in allp:
        ex = loader.load(p)
        d = pair_census(ex)
        name = os.path.basename(p)
        training = int(ex.quest or 0) == 9999
        if d["pairUntrustedHits"]:
            hit_anywhere.append((name, ex.version, int(ex.quest or 0), d["pairUntrustedHits"]))
        if ver_tuple(ex.version) < (1, 7):
            continue
        checked += 1
        if d["pairUntrustedHits"] != 0 and not training:
            bad.append((name, d["pairUntrustedHits"]))
    check("T3 every 1.7.x NON-TRAINING export has zero IN-DOMAIN pairUntrustedHits (B is a no-op)",
          not bad, "checked=%d offenders=%s" % (checked, bad))

    # T3b: the pre-1.7 exports that show an in-domain untrusted hit are all the training ground
    oldoff = [(n, c) for (n, v, q, c) in hit_anywhere if ver_tuple(v) < (1, 7)]
    check("T3b in-domain untrusted hits in pre-1.7 exports are training-ground only",
          bool(oldoff) and all(n.startswith("battle_9999") for n, _c in oldoff),
          "offenders=%s" % oldoff)

    # T3c (new, 2026-10-04): in-domain untrusted hits appear ONLY outside the comparable domain --
    # a pre-1.7 export or the training ground. This is the precise version of the old T3 claim, and it
    # also covers the 1.7.8 training export that first made the old wording false.
    outliers = [(n, v, q, c) for (n, v, q, c) in hit_anywhere
                if not (ver_tuple(v) < (1, 7) or q == 9999)]
    check("T3c in-domain untrusted hits occur only in pre-1.7 exports or the training ground",
          not outliers, "all=%s outliers=%s" % (hit_anywhere, outliers))

    # T4 domain claim: the training export DOES have untrusted live-age hits, but they are all
    # team-2 hits that Contribution.Compute excludes -> B still changes nothing there
    tr = os.path.join(EXPORTS, "battle_9999_20261004_042458.json")
    ext = loader.load(tr)
    dt = pair_census(ext)
    check("T4 training untrusted hits exist but are all OUT of domain (B no-op there too)",
          dt["outOfDomainUntrustedHits"] > 0 and dt["outOfDomainUntrustedHitsWithFolds"] > 0
          and dt["pairUntrustedHits"] == 0,
          "out-of-domain=%d (with folds=%d, would-be pool=%.2f) in-domain=%d" %
          (dt["outOfDomainUntrustedHits"], dt["outOfDomainUntrustedHitsWithFolds"],
           dt["outOfDomainUntrustedPool"], dt["pairUntrustedHits"]))

    # T4c (new, 2026-10-04): the newly arrived 1.7.8 training export is the first CURRENT-version file
    # with in-domain untrusted hits (12 hits, 7 of them carrying folds). Measure what B would move there
    # instead of asserting it away, and bound it: the training ground is already not_comparable, and the
    # amount is four orders of magnitude below the C-variant's already-rejected retention.
    tr_new = os.path.join(EXPORTS, "battle_9999_20261004_135214.json")
    if os.path.isfile(tr_new):
        en = loader.load(tr_new)
        dn = pair_census(en)
        an, bn = compute(en, "A"), compute(en, "B")
        moved = an["poolTotal"] - bn["poolTotal"]
        frac = (moved / an["poolTotal"]) if an["poolTotal"] else 0.0
        check("T4c 1.7.8 training export: in-domain untrusted hits are measured and bounded", 
              dn["pairUntrustedHits"] > 0 and frac < 1e-3,
              "in-domain=%d (with folds=%d) B moves=%.4f of poolA=%.2f (%.6f%%); quest=%s"
              % (dn["pairUntrustedHits"], dn["pairUntrustedHitsWithFolds"], moved,
                 an["poolTotal"], 100.0 * frac, en.quest))
    else:
        check("T4c 1.7.8 training export is present for the bounded-cost claim", False, tr_new)

    # T4b: B's credit movement is exactly zero on the main export
    b = compute(export, "B")
    check("T4b B moves exactly zero pool on 411001_115417",
          abs(b["poolTotal"] - a["poolTotal"]) == 0.0,
          "poolA=%.6f poolB=%.6f" % (a["poolTotal"], b["poolTotal"]))

    # T5 domain claim: C is not a usable filter -- it discards almost the whole pool everywhere
    c = compute(export, "C")
    frac = c["poolTotal"] / a["poolTotal"] if a["poolTotal"] else 0.0
    check("T5 criterion C retains <1%% of A's pool on 411001_115417",
          frac < 0.01, "C pool=%.2f A pool=%.2f retained=%.6f%%" % (c["poolTotal"], a["poolTotal"], frac * 100))

    ok = all(ok for _n, ok, _d in checks)
    print("selftest: %d/%d passed" % (sum(1 for _n, o, _d in checks if o), len(checks)))
    return 0 if ok else 1


def _compute_no_atkadd(export):
    """compute() with the atkAdd half of extract_folds removed (negative control)."""
    idx = OwnerIndex(export, TEAM)
    import contrib.aggregate as agg
    saved = agg.extract_folds

    def only_fold(event, diag):
        calc = event.get("calc") or {}
        return saved({"calc": {"fold": calc.get("fold")}}, diag)

    # compute() looks up extract_folds in this module's globals
    g = globals()
    g["extract_folds"] = only_fold
    try:
        return compute(export, "A", idx)
    finally:
        g["extract_folds"] = saved
        agg.extract_folds = saved


# ---------------------------------------------------------------------------------------------
def main(argv=None):
    ap = argparse.ArgumentParser(description="P0-C PairTrusted offline impact")
    ap.add_argument("--selftest", action="store_true")
    ap.add_argument("--survey", action="store_true", help="also census every export in exports/")
    ap.add_argument("--all", action="store_true", help="full detail for every export")
    ap.add_argument("--files", nargs="*", default=None, help="explicit export file names or paths")
    ap.add_argument("--out-json", default=os.path.join(HERE, "pairtrusted_impact_report.json"))
    ap.add_argument("--out-txt", default=os.path.join(HERE, "pairtrusted_impact_report.txt"))
    a = ap.parse_args(argv)

    if a.selftest:
        return selftest()

    if a.files:
        paths = [f if os.path.isabs(f) or os.sep in f else os.path.join(EXPORTS, f) for f in a.files]
    elif a.all:
        paths = sorted(glob.glob(os.path.join(EXPORTS, "battle_*.json")))
    else:
        paths = [os.path.join(EXPORTS, n) for n in REQUIRED if os.path.exists(os.path.join(EXPORTS, n))]

    results = []
    for p in paths:
        print("analyzing %s" % os.path.basename(p))
        results.append(analyze_export(p))

    survey = None
    if a.survey:
        survey = []
        for p in sorted(glob.glob(os.path.join(EXPORTS, "battle_*.json"))):
            try:
                survey.append(survey_export(p))
            except Exception as ex:  # noqa: BLE001
                survey.append({"file": os.path.basename(p), "error": repr(ex)})

    doc = {
        "task": "P0-C",
        "evidence": "offline replay",
        "implementation": "independent Python re-run of Contribution.Compute",
        "implementationPath": {
            "ownerLadder": "contrib.attribution.OwnerIndex.resolve",
            "foldVocabulary": "contrib.aggregate.extract_folds",
            "split": "contrib.aggregate.split_hit",
            "aggregation": "local loop mirroring src/Output/Contribution.cs Compute()",
        },
        "criteria": CRITERION_DOC,
        "discipline": ("A/B/C keep the same event set and denominator; B*/C* drop whole hits and "
                       "are flagged denominatorChanged=true"),
        "exports": results,
        "survey": survey,
    }
    with io.open(a.out_json, "w", encoding="utf-8") as fh:
        fh.write(json.dumps(doc, ensure_ascii=False, indent=1))
    txt = report_text(results, survey)
    with io.open(a.out_txt, "w", encoding="utf-8") as fh:
        fh.write(txt + "\n")
    print(txt.encode("ascii", "replace").decode("ascii"))
    print("wrote %s" % a.out_json)
    print("wrote %s" % a.out_txt)
    return 0


if __name__ == "__main__":
    sys.exit(main())
