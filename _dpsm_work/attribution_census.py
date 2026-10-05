# -*- coding: utf-8 -*-
"""attribution_census.py -- WHY a fold landed in the unattributed pool, from ONE export.

WHY THIS EXISTS
    The export already states the totals (contribution.unattributed, contribution.diagnostics.
    reasonCounts), and _dpsm_work/contrib/ explains the ladder. What neither states is the SHAPE of the
    residual: which fold KIND, which ORIGIN, which label and which victim the unresolved folds came from.
    Measured 2026-10-05 (battle_411001_20261005_082525, the deployed 1.7.11 build): 4.329% of the
    damage was unattributed under the single reason "unknown_kind", and answering "what IS that" needed a
    one-off script. This tool makes that answer permanent, reproducible and falsifiable.

WHAT IT COMPUTES (all from the export body -- no game, no master data)
    1. the per-reason fold/amount split, recomputed with the SAME offline ladder the crosscheck pins
       (contrib.attribution.OwnerIndex), and compared FIELD BY FIELD with the section the plugin wrote.
       A disagreement is a hard failure: two implementations of one contract must not differ.
    2. the unresolved DETAIL: group unresolved folds by (reason, kind, side, origin, label, factor) with
       folds / amount / victims / time range. This is the "what is unknown_kind made of" answer.
    3. the CARRIER VERDICT for the granted-modifier channel (kind="given"): a fold whose origin is
       "given#<i>/<type>/<param>" was handed to the victim by a unit; the roster tells us which team
       actors HOLD an ability whose talent list grants exactly that (type,param) through GiveTalent(...).
       Exactly one candidate is a determinate giver; several is an ambiguity that must be counted, not
       guessed. This is an INFERENCE and is reported as one -- it does NOT change any credit.

USAGE
    python _dpsm_work/attribution_census.py                     # newest export under exports/
    python _dpsm_work/attribution_census.py --export PATH
    python _dpsm_work/attribution_census.py --json OUT.json     # full detail, UTF-8
    python _dpsm_work/attribution_census.py --selftest          # prove the census can go red

EXIT CONTRACT (same vocabulary as _dpsm_work/contribution_gate.py)
    0 PASS                      rewrote the section and agreed with it
    1 ERROR                     a mismatch, an unusable input, or an exception
    3 DATA_MISSING              the export carries no contribution section (pre-1.6.0 or wiped)
Stdout is ASCII ONLY (this console is GBK); the human-readable report is written as UTF-8.
"""
from __future__ import annotations

import argparse
import glob
import io
import json
import os
import shutil
import sys
import tempfile
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
EXPORTS = os.path.join(REPO, "BepInEx", "plugins", "DpsMeter", "exports")
DEFAULT_REPORT = os.path.join(HERE, "attribution_census_report.txt")
TOL_ABS = 1e-3     # the export rounds amounts to 4 decimals
TOL_REL = 1e-9

if HERE not in sys.path:
    sys.path.insert(0, HERE)

from contrib import loader                      # noqa: E402
from contrib.aggregate import analyze, extract_folds, split_hit, rule_display_name   # noqa: E402
from contrib.attribution import OwnerIndex      # noqa: E402

TOOL = "attribution_census.py"
EXIT_PASS, EXIT_ERROR, EXIT_MISSING = 0, 1, 3


def A(s):
    """ASCII-only rendering of a possibly-CJK string (the console is GBK)."""
    return str(s).encode("ascii", "backslashreplace").decode("ascii")


def close(a, b):
    return abs(a - b) <= TOL_ABS + TOL_REL * max(abs(a), abs(b))


# ---------------------------------------------------------------------------------------------
# 1. recompute the reason split, and collect the unresolved detail
# ---------------------------------------------------------------------------------------------

def census(export, team=1):
    ix = OwnerIndex(export, team)
    diag = {}
    reasons = {}          # reason -> [amount, folds]
    detail = {}           # key -> row
    analyzable_hits = 0
    fold_bearing_hits = 0
    unresolved_folds = 0
    ev_index = -1
    for ev in export.events:
        ev_index += 1
        if ev.get("type") != "dmg":
            continue
        akey = ev.get("atkKey") or 0
        if akey == 0 or akey not in ix.by_key:
            continue                      # not analyzable -> the plugin never reads its folds
        analyzable_hits += 1
        folds = extract_folds(ev, diag)
        if not folds:
            continue
        fold_bearing_hits += 1
        attacker = ix.by_key[akey]
        _M, _base, _pool, shares = split_hit(ev.get("amount") or 0.0, folds)
        for f, share in zip(folds, shares):
            owner, reason = ix.resolve(f, attacker)
            slot = reasons.setdefault(reason, [0.0, 0])
            slot[0] += share
            slot[1] += 1
            if owner is not None:
                continue
            unresolved_folds += 1
            key = (reason, f.kind, f.side, f.origin, f.label, f.factor)
            row = detail.get(key)
            if row is None:
                row = detail[key] = {
                    "reason": reason, "kind": f.kind, "side": f.side, "origin": f.origin,
                    "label": f.label, "factor": f.factor, "rule_name": rule_display_name(f),
                    "amount": 0.0, "folds": 0, "victims": {}, "hits": 0,
                    "t_min": None, "t_max": None, "_events": set(), "_vkeys": set(),
                }
            row["amount"] += share
            row["folds"] += 1
            row["_events"].add(ev_index)
            v = ev.get("victim") or "?"
            row["victims"][v] = row["victims"].get(v, 0) + 1
            row["_vkeys"].add(ev.get("vicKey"))
            t = ev.get("t")
            if isinstance(t, (int, float)):
                if row["t_min"] is None or t < row["t_min"]:
                    row["t_min"] = t
                if row["t_max"] is None or t > row["t_max"]:
                    row["t_max"] = t
    # unique event count per detail row, so "folds" and "hits" stay distinguishable
    for row in detail.values():
        row["hits"] = len(row.pop("_events"))
        row["victim_instances"] = len(row.pop("_vkeys"))
        row["victims_top"] = sorted(row["victims"].items(), key=lambda kv: (-kv[1], kv[0]))[:5]
        row["victim_count"] = len(row["victims"])
    return {
        "reasons": reasons,
        "detail": sorted(detail.values(), key=lambda r: -r["amount"]),
        "analyzable_hits": analyzable_hits,
        "fold_bearing_hits": fold_bearing_hits,
        "unresolved_folds": unresolved_folds,
    }


# ---------------------------------------------------------------------------------------------
# 2. the plugin's own statement, and the carrier verdict for the "given" channel
# ---------------------------------------------------------------------------------------------

def plugin_section(export):
    co = export.raw.get("contribution") or {}
    if not co:
        return None
    unattr = {}
    for r in (co.get("unattributed") or []):
        unattr[str(r.get("reason"))] = [float(r.get("amount") or 0.0), int(r.get("folds") or 0)]
    return {
        "unattributed": unattr,
        "reasonCounts": dict((str(k), int(v)) for k, v in ((co.get("diagnostics") or {}).get("reasonCounts") or {}).items()),
        "coverage": co.get("coverage") or {},
        "schemaVersion": co.get("schemaVersion"),
        "method": co.get("method"),
    }


def grant_index(export, team=1):
    """(type/param) -> [ActorRef]: team actors that HOLD an ability granting that modifier.

    The talent list is read from the export's own roster rows (actors[].abilities[].talents[]), which the
    plugin fills from AbilityRoster (TalentRef.Type / P0 / Cond). An entry counts when a talent of the
    granting kind is present AND its structured condition names the grant channel (GiveTalent...).
    """
    out = {}
    meta = {}
    for row in (export.raw.get("actors") or []):
        if row.get("team") != team:
            continue
        ref = export.by_key.get(row.get("key"))
        if ref is None:
            continue
        for ab in (row.get("abilities") or []):
            for t in (ab.get("talents") or []):
                cond = str(t.get("cond") or "")
                if "GiveTalent" not in cond:
                    continue
                p = t.get("p") or []
                if not p:
                    continue
                k = "%s/%s" % (t.get("type"), p[0])
                lst = out.setdefault(k, [])
                if ref.key not in [r.key for r in lst]:
                    lst.append(ref)
                meta.setdefault(k, []).append({
                    "abilityId": ab.get("id"), "ability": ab.get("name"),
                    "slot": ab.get("slot"), "slotName": ab.get("slotName"),
                    "talentType": t.get("type"), "p": p, "cond": cond, "timing": t.get("timing"),
                })
    return out, meta


def carrier_verdict(origin, grants):
    """'given#<i>/<type>/<param>' -> (verdict, [actors]). Only the granted channel is probed."""
    parts = str(origin or "").split("/")
    if len(parts) < 3 or not parts[0].startswith("given#"):
        return None, []
    key = "%s/%s" % (parts[1], parts[2])
    cand = grants.get(key) or []
    if len(cand) == 1:
        return "unique", cand
    if len(cand) > 1:
        return "ambiguous", cand
    return "none", []


# ---------------------------------------------------------------------------------------------
# 3. compare, and render
# ---------------------------------------------------------------------------------------------

# R54: the granted channel's codes. A file written BEFORE that split labels those folds "unknown_kind",
# so the recomputed side is normalised into the FILE's vocabulary when the file carries none of them --
# otherwise every older export would look like a mismatch that is really a renamed bucket.
NEW_GIVEN_CODES = ("given_carrier_one", "given_carrier_ambiguous", "given_carrier_none")


def vocabulary_is_legacy(sec):
    """True when the file's own section predates the granted-channel split."""
    file_codes = set(sec["reasonCounts"].keys()) | set(sec["unattributed"].keys())
    return not any(c in file_codes for c in NEW_GIVEN_CODES)


def normalise_vocabulary(reasons, sec):
    if not vocabulary_is_legacy(sec):
        return reasons
    out = {}
    for r, v in reasons.items():
        out["unknown_kind" if r in NEW_GIVEN_CODES else r] = v
    return out


def compare(cen, sec):
    problems = []
    cen = dict(cen)
    cen["reasons"] = normalise_vocabulary(cen["reasons"], sec)
    for reason, (amount, folds) in sorted(sec["unattributed"].items()):
        got = cen["reasons"].get(reason)
        if got is None:
            problems.append("reason %s: plugin says %d folds; recompute never produced it" % (reason, folds))
            continue
        if int(got[1]) != folds:
            problems.append("reason %s: folds plugin=%d recompute=%d" % (reason, folds, int(got[1])))
        if not close(got[0], amount):
            problems.append("reason %s: amount plugin=%.4f recompute=%.4f" % (reason, amount, got[0]))
    for reason, (amount, folds) in sorted(cen["reasons"].items()):
        if reason in sec["unattributed"]:
            continue
        if reason not in ("byUnit", "text/talent", "global"):
            # every reason the plugin recorded must be in its own reasonCounts
            pass
    for reason, folds in sorted(sec["reasonCounts"].items()):
        got = cen["reasons"].get(reason)
        n = int(got[1]) if got else 0
        if n != folds:
            problems.append("reasonCounts %s: plugin=%d recompute=%d" % (reason, folds, n))
    for reason, (amount, folds) in sorted(cen["reasons"].items()):
        if reason in sec["reasonCounts"]:
            continue
        problems.append("reasonCounts %s: recompute=%d folds but the plugin recorded none" % (reason, int(folds)))
    return problems


def render(export, cen, sec, problems, grants, gmeta, out):
    w = out.write
    w("export      : %s\n" % A(export.name))
    w("plugin      : version=%s quest=%s app=%s\n" % (A(export.version), A(export.quest), A(export.app)))
    w("contribution: schema=%s method=%s\n" % (A(sec["schemaVersion"]), A(sec["method"])))
    w("hits        : analyzable=%d fold-bearing=%d (a hit with no surviving fold counts as"
      " analyzable but carries no credit line)\n" % (cen["analyzable_hits"], cen["fold_bearing_hits"]))
    w("reasons     : plugin / recompute (folds)\n")
    for reason in sorted(set(list(sec["reasonCounts"].keys()) + list(cen["reasons"].keys()))):
        w("  %-24s plugin=%-8s recompute=%-8d amount=%.4f\n"
          % (reason, sec["reasonCounts"].get(reason, "-"), int((cen["reasons"].get(reason) or [0, 0])[1]),
             (cen["reasons"].get(reason) or [0.0, 0])[0]))
    w("\nunattributed (plugin statement)\n")
    for reason, (amount, folds) in sorted(sec["unattributed"].items()):
        w("  %-24s amount=%.4f folds=%d\n" % (reason, amount, folds))
    ga = export.raw.get("givenApplies") or {}
    ra = export.roster_audit or {}
    if ga or ra:
        w("\ngiven-channel health (the reason the giver was never resolved)\n")
        if ra:
            w("  rosterAudit: giveApplied=%s giveCancelled=%s giveFoldHits=%s giveReads=%s giveErrors=%s\n"
              % (A(ra.get("giveApplied")), A(ra.get("giveCancelled")), A(ra.get("giveFoldHits")),
                 A(ra.get("giveReads")), A(ra.get("giveErrors"))))
            w("  giver       : resolved=%s null=%s errors=%s  (resolved+null+errors must equal"
              " giveApplied+giveCancelled)\n"
              % (A(ra.get("giverResolved")), A(ra.get("giverNull")), A(ra.get("giverErrors"))))
            w("  giveTypes   : %s\n" % A(ra.get("giveTypes")))
        if ga:
            w("  givenApplies: hookCalls=%s recorded=%s nullOwner=%s nullGuest=%s errors=%s"
              " targets=%s lookupHits=%s lookupMisses=%s\n"
              % (A(ga.get("hookCalls")), A(ga.get("recorded")), A(ga.get("nullOwner")),
                 A(ga.get("nullGuest")), A(ga.get("errors")), A(ga.get("targets")),
                 A(ga.get("lookupHits")), A(ga.get("lookupMisses"))))
    w("\nunresolved detail (recomputed; %d rows)\n" % len(cen["detail"]))
    for row in cen["detail"]:
        w("  reason=%-16s kind=%-8s side=%-4s factor=%s folds=%d amount=%.4f\n"
          % (A(row["reason"]), A(row["kind"]), A(row["side"]), A(row["factor"]), row["folds"], row["amount"]))
        w("    origin=%s hits=%d\n" % (A(row["origin"]), row["hits"]))
        w("    label =%s\n" % A(row["label"]))
        w("    victims=%d name(s)/%d instance(s) top=%s t=[%s..%s]\n"
          % (row["victim_count"], row["victim_instances"], A(row["victims_top"]),
             A(row["t_min"]), A(row["t_max"])))
        verdict, cand = carrier_verdict(row["origin"], grants)
        if verdict:
            names = ", ".join("%s#%s" % (A(c.name), A(c.key)) for c in cand)
            w("    CARRIER VERDICT: %s (%d candidate(s))%s\n"
              % (verdict, len(cand), (" [" + names + "]") if names else ""))
            for key, rows in sorted(gmeta.items()):
                if key == "/".join(str(row["origin"]).split("/")[1:3]):
                    for m in rows[:4]:
                        w("      holder evidence: actor=%s ability=%s(id=%s, slot=%s) type=%s p=%s cond=%s\n"
                          % ("see candidates", A(m["ability"]), A(m["abilityId"]), A(m["slotName"]),
                             A(m["talentType"]), A(m["p"]), A(m["cond"])))
                    break
    w("\nVERDICT: %s\n" % ("MATCH" if not problems else "MISMATCH"))
    for p in problems:
        w("  PROBLEM: %s\n" % A(p))
    return not problems


# ---------------------------------------------------------------------------------------------
# 4. selftest: the census must be able to go RED
# ---------------------------------------------------------------------------------------------

def _fixture(path, folds, truth, actors=None):
    """A minimal export with a known per-reason split."""
    ev_given = {"t": 1.0, "type": "dmg", "atkKey": 2, "attacker": "A", "vicKey": 1, "victim": "BOSS",
                "amount": 1000.0, "calc": {"fold": folds}}
    doc = {
        "app": "DpsMeter", "version": "1.7.11", "quest": 411001,
        "actors": actors if actors is not None else [
            {"key": 2, "name": "A", "team": 1, "abilities": []},
            {"key": 1, "name": "BOSS", "team": 2, "abilities": []},
        ],
        "events": [ev_given],
        "contribution": {
            "schemaVersion": "1.1", "method": "log-share/1",
            "unattributed": [{"reason": truth["reason"], "amount": truth["amount"], "folds": truth["folds"]}],
            "coverage": {},
            "diagnostics": {"reasonCounts": truth["counts"]},
        },
    }
    with io.open(path, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(doc, fh, ensure_ascii=False)
    return doc


def selftest():
    tmp = tempfile.mkdtemp(prefix="uk_census_")
    fails = []
    try:
        fold = {"kind": "given", "side": "vic", "origin": "given#4/1006/-10", "factor": 1.1,
                "label": "被伤害+10%(赋予)"}
        # amount the split produces: 1000 - 1000/1.1 = 90.9090...
        amt = 1000.0 - 1000.0 / 1.1
        # 1) clean fixture: the section must agree with the recompute -> PASS
        p = os.path.join(tmp, "clean.json")
        _fixture(p, [fold], {"reason": "unknown_kind", "amount": round(amt, 4), "folds": 1,
                             "counts": {"unknown_kind": 1}})
        rc, _ = run(p, report=os.path.join(tmp, "clean.txt"))
        if rc != EXIT_PASS:
            fails.append("clean fixture did not PASS (exit=%s)" % rc)
        # 2) tampered folds -> MISMATCH
        p = os.path.join(tmp, "bad_folds.json")
        _fixture(p, [fold], {"reason": "unknown_kind", "amount": round(amt, 4), "folds": 2,
                             "counts": {"unknown_kind": 2}})
        rc, _ = run(p, report=os.path.join(tmp, "bad_folds.txt"))
        if rc != EXIT_ERROR:
            fails.append("tampered fold count did not FAIL (exit=%s)" % rc)
        # 3) tampered amount -> MISMATCH
        p = os.path.join(tmp, "bad_amount.json")
        _fixture(p, [fold], {"reason": "unknown_kind", "amount": 1.0, "folds": 1,
                             "counts": {"unknown_kind": 1}})
        rc, _ = run(p, report=os.path.join(tmp, "bad_amount.txt"))
        if rc != EXIT_ERROR:
            fails.append("tampered amount did not FAIL (exit=%s)" % rc)
        # 4) a MADE-UP reason the recompute never produces -> MISMATCH
        p = os.path.join(tmp, "bad_reason.json")
        _fixture(p, [fold], {"reason": "not_a_real_reason", "amount": round(amt, 4), "folds": 1,
                             "counts": {"unknown_kind": 1}})
        rc, _ = run(p, report=os.path.join(tmp, "bad_reason.txt"))
        if rc != EXIT_ERROR:
            fails.append("unknown reason did not FAIL (exit=%s)" % rc)
        # 5) no contribution section -> DATA_MISSING
        p = os.path.join(tmp, "missing.json")
        with io.open(p, "w", encoding="utf-8", newline="\n") as fh:
            json.dump({"app": "DpsMeter", "version": "1.5.0", "quest": 1, "actors": [], "events": []}, fh)
        rc, _ = run(p, report=os.path.join(tmp, "missing.txt"))
        if rc != EXIT_MISSING:
            fails.append("missing section did not return DATA_MISSING (exit=%s)" % rc)
        # 6) carrier verdicts: unique / ambiguous / none
        # R54: a file that already uses the new vocabulary must NOT be normalised away
        p = os.path.join(tmp, "new_vocab.json")
        _fixture(p, [fold], {"reason": "given_carrier_none", "amount": round(amt, 4), "folds": 1,
                             "counts": {"given_carrier_none": 1}})
        rc, _ = run(p, report=os.path.join(tmp, "new_vocab.txt"))
        if rc != EXIT_PASS:
            fails.append("a new-vocabulary fixture did not PASS (exit=%s)" % rc)
        p = os.path.join(tmp, "new_vocab_bad.json")
        _fixture(p, [fold], {"reason": "given_carrier_one", "amount": round(amt, 4), "folds": 1,
                             "counts": {"given_carrier_one": 1}})
        rc, _ = run(p, report=os.path.join(tmp, "new_vocab_bad.txt"))
        if rc != EXIT_ERROR:
            fails.append("a tampered new-vocabulary fixture did not FAIL (exit=%s)" % rc)
        g = {"1006/-10": [loader.ActorRef(key=9, name="X", team=1)]}
        v, c = carrier_verdict("given#4/1006/-10", g)
        if v != "unique" or len(c) != 1:
            fails.append("carrier unique verdict wrong: %s" % v)
        g = {"1006/-10": [loader.ActorRef(key=9, name="X", team=1), loader.ActorRef(key=3, name="Y", team=1)]}
        v, c = carrier_verdict("given#4/1006/-10", g)
        if v != "ambiguous" or len(c) != 2:
            fails.append("carrier ambiguous verdict wrong: %s" % v)
        v, c = carrier_verdict("given#4/1006/-10", {})
        if v != "none":
            fails.append("carrier none verdict wrong: %s" % v)
        v, c = carrier_verdict("text#7/1/c0", {})
        if v is not None:
            fails.append("non-given origin was probed as a carrier")
        # 7) the report must be pure ASCII even when the label is not
        with io.open(os.path.join(tmp, "bad_folds.txt"), "rb") as fh:
            raw = fh.read()
        try:
            raw.decode("ascii")
        except UnicodeDecodeError:
            fails.append("report file is not ASCII")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    for f in fails:
        print("SELFTEST FAIL: " + A(f))
    print("SELFTEST %s (%d case(s))" % ("PASS" if not fails else "FAIL", 9))
    return EXIT_PASS if not fails else EXIT_ERROR


# ---------------------------------------------------------------------------------------------

def run(path, report=DEFAULT_REPORT, json_out=None):
    export = loader.load(path)
    sec = plugin_section(export)
    if sec is None:
        msg = "DATA_MISSING: %s has no contribution section (version=%s)" % (export.name, A(export.version))
        print(A(msg))
        try:
            with io.open(report, "w", encoding="utf-8", newline="\n") as fh:
                fh.write(msg + "\n")
        except Exception:
            pass
        return EXIT_MISSING, None
    cen = census(export)
    problems = compare(cen, sec)
    # second opinion: the aggregate the crosscheck pins must agree with this pass
    an = analyze(export, team=1)
    for reason, pair in an.unattributed.items():
        got = cen["reasons"].get(reason)
        if got is None or int(got[1]) != int(pair[1]) or not close(got[0], pair[0]):
            problems.append("offline core disagrees with this pass on reason %s: %s vs %s"
                            % (reason, pair, got))
    grants, gmeta = grant_index(export)
    with io.open(report, "w", encoding="utf-8", newline="\n") as fh:
        render(export, cen, sec, problems, grants, gmeta, fh)
    print("report      : " + A(report))
    print("export      : " + A(export.name))
    print("reasons     : " + A(", ".join("%s=%d" % (r, int(v[1])) for r, v in sorted(cen["reasons"].items())
                                        if r in sec["reasonCounts"])))
    for reason, (amount, folds) in sorted(sec["unattributed"].items()):
        print("unattributed: %s amount=%.4f folds=%d" % (reason, amount, folds))
    if vocabulary_is_legacy(sec):
        print("note        : file uses the pre-R54 vocabulary; the given_carrier_* codes are compared as unknown_kind")
    ra = export.roster_audit or {}
    if ra:
        ga = export.raw.get("givenApplies") or {}
        print("given health: giveApplied=%s giverResolved(exact)=%s giverNull=%s hookTargets=%s lookupHits=%s"
              % (ra.get("giveApplied"), ra.get("giverResolved"), ra.get("giverNull"),
                 ga.get("targets"), ga.get("lookupHits")))
        if "exactHits" in ga or "targetOnlyRejected" in ga:
            print("given exact : exactHits=%s exactMisses=%s targetOnlyRejected=%s grantKeyReads=%s grantKeyErrors=%s"
                  % (ga.get("exactHits"), ga.get("exactMisses"), ga.get("targetOnlyRejected"),
                     ga.get("grantKeyReads"), ga.get("grantKeyErrors")))
    for row in cen["detail"][:8]:
        verdict, cand = carrier_verdict(row["origin"], grants)
        print("detail      : reason=%s kind=%s origin=%s factor=%s folds=%d amount=%.4f victims=%d verdict=%s(%d)"
              % (A(row["reason"]), A(row["kind"]), A(row["origin"]), A(row["factor"]), row["folds"],
                 row["amount"], row["victim_count"], A(verdict), len(cand)))
    print("VERDICT     : " + ("MATCH" if not problems else "MISMATCH"))
    for p in problems:
        print("  PROBLEM   : " + A(p))
    if json_out:
        with io.open(json_out, "w", encoding="utf-8", newline="\n") as fh:
            json.dump({"export": export.source(), "plugin": sec, "census": cen,
                       "carriers": dict((k, [c.name for c in v]) for k, v in grants.items()),
                       "problems": problems}, fh, ensure_ascii=False, indent=1)
        print("json        : " + A(json_out))
    return (EXIT_PASS if not problems else EXIT_ERROR), cen


def newest_export():
    files = sorted(glob.glob(os.path.join(EXPORTS, "battle_*.json")), key=os.path.getmtime)
    return files[-1] if files else None


def main(argv=None):
    ap = argparse.ArgumentParser(description="Why did a fold land in the unattributed pool?")
    ap.add_argument("--export", default=None)
    ap.add_argument("--report", default=DEFAULT_REPORT)
    ap.add_argument("--json", dest="json_out", default=None)
    ap.add_argument("--selftest", action="store_true")
    args = ap.parse_args(argv)
    if args.selftest:
        return selftest()
    path = args.export or newest_export()
    if not path or not os.path.isfile(path):
        print("ERROR: no export found (looked in %s)" % A(EXPORTS))
        return EXIT_ERROR
    try:
        rc, _ = run(path, args.report, args.json_out)
        return rc
    except Exception:
        print("ERROR: census crashed")
        traceback.print_exc()
        return EXIT_ERROR


if __name__ == "__main__":
    sys.exit(main())
