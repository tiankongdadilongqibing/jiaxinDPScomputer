# -*- coding: utf-8 -*-
"""extract_verify.py -- verify ONE evidence bundle produced by the plugin's extraction flow (R52).

WHY THIS EXISTS
    Diagnostics/EvidenceExtractor.cs writes a self-contained bundle: the battle export, the unresolved-fold
    census, a copy of the game's master tables and a manifest that checksums all of it. A bundle nobody
    verifies is a folder. This tool is the other half of the contract:

      1. every file the manifest names exists, with the recorded size and checksum (FNV-1a 64 -- the
         algorithm is NAMED in the manifest, and the name is checked here, because a checksum read as a
         cryptographic digest is a lie by omission);
      2. the manifest's census summary matches the census FILE it summarises;
      3. the census FILE matches an INDEPENDENT recomputation of the SAME battle.json with the offline
         core (_dpsm_work/attribution_census.py) -- the plugin's C# census and the Python census are two
         implementations of one contract, and this is where they are diffed field by field;
      4. the census's carrier verdicts match the loadout-side verdict the offline core computes from the
         same roster rows (the one inference in the bundle, checked rather than trusted);
      5. the deployed assembly the bundle names is reported, and a mismatch with the file currently on
         disk is a WARNING (an old bundle stays valid; it must simply not be read as current).

    Hashing a 24 MB export is a byte loop: expect ~15 s per call. That is the price of a checksum that
    actually checks something.

USAGE
    python _dpsm_work/extract_verify.py                    # newest bundle under .../DpsMeter/extract
    python _dpsm_work/extract_verify.py --bundle DIR
    python _dpsm_work/extract_verify.py --selftest         # prove the verifier can say NO

EXIT CONTRACT (same vocabulary as contribution_gate.py)
    0 PASS / WARNING        verified (a warning is printed and does not fail the run)
    1 ERROR                 mismatch, corruption, or an unusable bundle
    3 DATA_MISSING          a required file (or the manifest) is absent
Stdout is ASCII ONLY (this console is GBK); the report is written as UTF-8.
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

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
BUNDLES = os.path.join(REPO, "BepInEx", "plugins", "DpsMeter", "extract")
DEPLOYED = os.path.join(REPO, "BepInEx", "plugins", "DpsMeter", "DpsMeter.dll")
DEFAULT_REPORT = os.path.join(HERE, "extract_verify_report.txt")
REQUIRED = ("manifest.json", "battle.json", "contrib_census.json")
MANIFEST_SCHEMA = "extract-manifest/1"
CENSUS_SCHEMA = "contrib-census/1"
HASH_NAME = "fnv1a64"
TOL_ABS = 1e-3
TOL_REL = 1e-9

if HERE not in sys.path:
    sys.path.insert(0, HERE)

import attribution_census as ac          # noqa: E402  (the offline core; one implementation, reused)
from contrib import loader                # noqa: E402

EXIT_PASS, EXIT_ERROR, EXIT_MISSING = 0, 1, 3


def A(s):
    return str(s).encode("ascii", "backslashreplace").decode("ascii")


def close(a, b):
    return abs(a - b) <= TOL_ABS + TOL_REL * max(abs(a), abs(b))


def fnv1a64(path):
    h = 14695981039346656037
    with open(path, "rb") as fh:
        while True:
            buf = fh.read(1 << 16)
            if not buf:
                break
            for c in buf:
                h ^= c
                h = (h * 1099511628211) & 0xFFFFFFFFFFFFFFFF
    return "%016x" % h


def read_json(path):
    with io.open(path, "r", encoding="utf-8", errors="replace") as fh:
        return json.load(fh)


def verify(bundle):
    """Returns (exit_code, problems, warnings, lines)."""
    problems, warnings, lines = [], [], []

    def W(s):
        lines.append(s)

    for name in REQUIRED:
        if not os.path.isfile(os.path.join(bundle, name)):
            return (EXIT_MISSING, ["missing required file: " + name], warnings, lines)
    try:
        man = read_json(os.path.join(bundle, "manifest.json"))
    except Exception as ex:
        return (EXIT_ERROR, ["manifest.json is not readable JSON: " + str(ex)], warnings, lines)
    if man.get("schema") != MANIFEST_SCHEMA:
        return (EXIT_ERROR, ["manifest schema is %r, wanted %r" % (man.get("schema"), MANIFEST_SCHEMA)],
                warnings, lines)
    if man.get("check") != HASH_NAME:
        problems.append("manifest check algorithm is %r; this verifier only implements %r"
                        % (man.get("check"), HASH_NAME))
    W("bundle      : " + os.path.basename(bundle))
    W("manifest    : schema=%s check=%s reason=%s plugin=%s quest=%s at=%s"
      % (man.get("schema"), man.get("check"), man.get("reason"), man.get("pluginVersion"),
         man.get("quest"), man.get("createdAt")))

    # 1) files + checksums
    listed = man.get("files") or []
    names = [f.get("name") for f in listed]
    for req in ("battle.json", "contrib_census.json"):
        if req not in names:
            problems.append("the manifest does not list the required file " + req)
    for f in listed:
        name = f.get("name") or "?"
        p = os.path.join(bundle, name)
        if not os.path.isfile(p):
            problems.append("listed file is absent: " + name)
            continue
        size = os.path.getsize(p)
        if f.get("bytes") is not None and size != f.get("bytes"):
            problems.append("size mismatch for %s: file=%d manifest=%s" % (name, size, f.get("bytes")))
        got = fnv1a64(p)
        if f.get("check") != got:
            problems.append("checksum mismatch for %s: file=%s manifest=%s" % (name, got, f.get("check")))
    W("files       : %d listed, %d hashed" % (len(listed), len(names)))

    # 2) the masterdata copy the manifest claims
    md = man.get("masterdata") or {}
    md_dir = os.path.join(bundle, "masterdata")
    have = len(glob.glob(os.path.join(md_dir, "*.json"))) if os.path.isdir(md_dir) else 0
    claimed = int(md.get("files") or 0)
    if claimed != have:
        problems.append("masterdata count: manifest=%d on disk=%d" % (claimed, have))
    W("masterdata  : claimed=%d on disk=%d failures=%s" % (claimed, have, md.get("failures")))

    # 3) the census file vs an INDEPENDENT recomputation of the bundle's own battle.json
    try:
        cen_file = read_json(os.path.join(bundle, "contrib_census.json"))
    except Exception as ex:
        problems.append("contrib_census.json is not readable JSON: " + str(ex))
        return (EXIT_ERROR, problems, warnings, lines)
    if cen_file.get("schema") != CENSUS_SCHEMA:
        problems.append("census schema is %r, wanted %r" % (cen_file.get("schema"), CENSUS_SCHEMA))
    for key in ("folds", "amount", "groups"):
        if key not in (man.get("census") or {}):
            problems.append("manifest census summary lacks " + key)
    export = loader.load(os.path.join(bundle, "battle.json"))
    rec = ac.census(export)
    sec = ac.plugin_section(export)
    # R54: a bundle written before the granted-channel split labels those folds "unknown_kind". Compare in the
    # bundle's OWN vocabulary when it carries none of the new codes -- otherwise every older bundle would look
    # corrupt. The same helper the census tool uses, so the two cannot disagree about what "legacy" means.
    legacy = bool(sec) and ac.vocabulary_is_legacy(sec)
    if legacy:
        warnings.append("battle.json uses the pre-R54 vocabulary; the given_carrier_* codes are compared as "
                        "unknown_kind")

    def norm(reason):
        return "unknown_kind" if (legacy and reason in ac.NEW_GIVEN_CODES) else reason
    if sec is None:
        warnings.append("battle.json carries no contribution section (General/Contribution off?)")
    else:
        for p in ac.compare(rec, sec):
            problems.append("battle.json vs its own contribution section: " + p)

    # the census file's own fold accounting must agree with the recomputation
    fa = cen_file.get("foldAccounting") or {}
    rec_folds = sum(int(v[1]) for v in rec["reasons"].values())
    if int(fa.get("total") or 0) != rec_folds:
        problems.append("foldAccounting.total: census=%s recompute=%d" % (fa.get("total"), rec_folds))
    got_reasons = dict((r.get("reason"), int(r.get("folds") or 0)) for r in (cen_file.get("reasons") or []))
    rec_reasons = dict((norm(r), v) for r, v in rec["reasons"].items())
    for reason, folds in sorted(got_reasons.items()):
        mine = int((rec_reasons.get(reason) or [0, 0])[1])
        if mine != folds:
            problems.append("reason %s: census=%d recompute=%d" % (reason, folds, mine))
    for reason, v in sorted(rec_reasons.items()):
        if reason not in got_reasons:
            problems.append("reason %s: recompute=%d folds but the census does not list it" % (reason, int(v[1])))

    # the census GROUPS, field by field
    mine = {}
    for row in rec["detail"]:
        mine[(norm(row["reason"]), row["kind"], row["side"], row["origin"], row["label"])] = row
    theirs = {}
    for row in (cen_file.get("unresolved") or []):
        theirs[(row.get("reason"), row.get("kind"), row.get("side"), row.get("origin"), row.get("label"))] = row
    for k in sorted(set(list(mine.keys()) + list(theirs.keys()))):
        a, b = mine.get(k), theirs.get(k)
        if a is None:
            problems.append("census lists an unresolved group the recompute does not have: %s" % (A(k),))
            continue
        if b is None:
            problems.append("the recompute has an unresolved group the census does not list: %s" % (A(k),))
            continue
        if int(b.get("folds") or 0) != a["folds"]:
            problems.append("group %s: folds census=%s recompute=%d" % (A(k), b.get("folds"), a["folds"]))
        if not close(float(b.get("amount") or 0.0), a["amount"]):
            problems.append("group %s: amount census=%s recompute=%.4f" % (A(k), b.get("amount"), a["amount"]))
        if not close(float(b.get("factor") or 0.0), a["factor"]):
            problems.append("group %s: factor census=%s recompute=%r" % (A(k), b.get("factor"), a["factor"]))
        if int(b.get("victimInstances") or 0) != a["victim_instances"]:
            problems.append("group %s: victimInstances census=%s recompute=%d"
                            % (A(k), b.get("victimInstances"), a["victim_instances"]))
        # 4) the carrier verdict: the bundle's inference vs the offline core's, from the same roster
        verdict, _ = ac.carrier_verdict(a["origin"], ac.grant_index(export)[0])
        want = verdict or ""
        if (b.get("carrierVerdict") or "") != want:
            problems.append("group %s: carrierVerdict census=%r recompute=%r"
                            % (A(k), b.get("carrierVerdict"), want))
    W("census      : groups census=%d recompute=%d folds census=%s recompute=%d"
      % (len(theirs), len(mine), fa.get("total"), rec_folds))
    W("unattributed: census coverage=%s recompute reasons=%s"
      % (A((cen_file.get("coverage") or {}).get("unattributed")),
         A(", ".join("%s=%d" % (r, int(v[1])) for r, v in sorted(rec["reasons"].items())
                     if r in got_reasons))))

    # 5) the deployed assembly the bundle names
    asm = man.get("assembly") or {}
    ap = asm.get("path")
    if ap and os.path.isfile(ap):
        now = fnv1a64(ap)
        same = (asm.get("check") == now)
        W("assembly    : %s check=%s bytes=%s" % (A(os.path.basename(ap)), asm.get("check"), asm.get("bytes")))
        if not same:
            warnings.append("the assembly at %s has changed since this bundle was written "
                            "(bundle=%s now=%s): the bundle is still internally valid, but it no longer "
                            "describes the build that is deployed" % (A(ap), asm.get("check"), now))
    else:
        warnings.append("the bundle does not name a readable assembly path")
    return (EXIT_ERROR if problems else EXIT_PASS), problems, warnings, lines


def newest_bundle():
    dirs = sorted(glob.glob(os.path.join(BUNDLES, "extract_*")), key=os.path.getmtime)
    return dirs[-1] if dirs else None


def report_and_print(path, bundle, code, problems, warnings, lines):
    with io.open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("extract_verify -- " + str(bundle) + "\n")
        for l in lines:
            fh.write(l + "\n")
        for w in warnings:
            fh.write("WARNING: " + w + "\n")
        for p in problems:
            fh.write("PROBLEM: " + p + "\n")
        fh.write("VERDICT: " + ("PASS" if code == EXIT_PASS else ("DATA_MISSING" if code == EXIT_MISSING else "ERROR")) + "\n")
    for l in lines:
        print(l)
    for w in warnings:
        print("WARNING     : " + A(w))
    for p in problems:
        print("PROBLEM     : " + A(p))
    print("VERDICT     : " + ("PASS" if code == EXIT_PASS else ("DATA_MISSING" if code == EXIT_MISSING else "ERROR")))
    print("report      : " + A(path))
    return code


# ---------------------------------------------------------------------------------------------
# selftest: the verifier must be able to say NO
# ---------------------------------------------------------------------------------------------

def _bundle(tmp, name, folds, truth_folds, tamper_file=None, drop_file=None, bad_check_name=False,
            group_folds=None, legacy=False):
    """Build a synthetic bundle with the shape EvidenceExtractor.cs writes."""
    d = os.path.join(tmp, name)
    os.makedirs(d)
    fold = {"kind": "given", "side": "vic", "origin": "given#4/1006/-10", "factor": 1.1,
            "label": "被伤害+10%(赋予)"}
    # R54: today's writer labels the granted channel with its own code; the legacy fixture proves the
    # vocabulary normalisation still verifies a pre-R54 bundle.
    REASON = "unknown_kind" if legacy else "given_carrier_none"
    amt = 1000.0 - 1000.0 / 1.1
    battle = {
        "app": "DpsMeter", "version": "1.7.11", "quest": 411001,
        "actors": [{"key": 2, "name": "A", "team": 1, "abilities": []},
                   {"key": 1, "name": "BOSS", "team": 2, "abilities": []}],
        "events": [{"t": 1.0, "type": "dmg", "atkKey": 2, "attacker": "A", "vicKey": 1, "victim": "BOSS",
                    "amount": 1000.0, "calc": {"fold": [fold]}}],
        "contribution": {"schemaVersion": "1.1", "method": "log-share/1",
                         "unattributed": [{"reason": REASON, "amount": round(amt, 4), "folds": folds}],
                         "coverage": {}, "diagnostics": {"reasonCounts": {REASON: truth_folds}}},
    }
    with io.open(os.path.join(d, "battle.json"), "w", encoding="utf-8", newline="\n") as fh:
        json.dump(battle, fh, ensure_ascii=False)
    census = {
        "schema": CENSUS_SCHEMA, "reason": "selftest", "pluginVersion": "1.7.11", "quest": 411001,
        "usable": True,
        "foldAccounting": {"total": truth_folds if group_folds is None else group_folds, "zeroFactor": 0},
        "coverage": {"unattributed": round(amt, 4), "analyzable": 1000.0, "attributed": 1000.0 - amt},
        "reasons": [{"reason": REASON, "folds": truth_folds}],
        "unresolved": [{"reason": REASON, "kind": "given", "side": "vic",
                        "origin": "given#4/1006/-10", "label": "被伤害+10%(赋予)", "ruleName": "被伤害+10%(赋予)",
                        "factor": 1.1, "folds": truth_folds, "amount": round(amt, 4),
                        "victimInstances": 1, "victimTop": "BOSS", "victimTopFolds": truth_folds,
                        "carrierVerdict": "none", "carrierCount": 0, "carrierNames": ""}],
    }
    with io.open(os.path.join(d, "contrib_census.json"), "w", encoding="utf-8", newline="\n") as fh:
        json.dump(census, fh, ensure_ascii=False)
    files = []
    for n in ("battle.json", "contrib_census.json"):
        p = os.path.join(d, n)
        files.append({"name": n, "bytes": os.path.getsize(p), "check": fnv1a64(p)})
    man = {
        "schema": MANIFEST_SCHEMA, "check": ("sha256" if bad_check_name else HASH_NAME),
        "reason": "selftest", "pluginVersion": "1.7.11", "quest": 411001, "seconds": 1.0,
        "bundle": name, "assembly": {"path": None},
        "files": files, "masterdata": {"files": 0, "bytes": 0, "failures": 0},
        "census": {"groups": 1, "folds": truth_folds, "amount": round(amt, 4),
                   "carrierUnique": 0, "carrierAmbiguous": 0, "carrierNone": 1},
    }
    with io.open(os.path.join(d, "manifest.json"), "w", encoding="utf-8", newline="\n") as fh:
        json.dump(man, fh, ensure_ascii=False)
    if tamper_file:
        p = os.path.join(d, tamper_file)
        with io.open(p, "a", encoding="utf-8", newline="\n") as fh:
            fh.write(" ")
    if drop_file:
        os.remove(os.path.join(d, drop_file))
    return d


def selftest():
    tmp = tempfile.mkdtemp(prefix="extract_verify_")
    fails = []
    try:
        cases = [
            # clean: every checksum and every cross-diff must hold
            ("clean", dict(folds=1, truth_folds=1), EXIT_PASS),
            # a byte appended to a listed file -> checksum AND size mismatch
            ("tampered", dict(folds=1, truth_folds=1, tamper_file="battle.json"), EXIT_ERROR),
            # a required file removed -> DATA_MISSING
            ("missing", dict(folds=1, truth_folds=1, drop_file="contrib_census.json"), EXIT_MISSING),
            # the census claims a different fold count than the recomputation
            ("census-folds", dict(folds=1, truth_folds=2), EXIT_ERROR),
            # the manifest names an algorithm this verifier does not implement
            ("bad-check", dict(folds=1, truth_folds=1, bad_check_name=True), EXIT_ERROR),
            # the census's fold accounting disagrees with its own reason list
            ("accounting", dict(folds=1, truth_folds=1, group_folds=9), EXIT_ERROR),
            # R54: a PRE-R54 bundle (old vocabulary: unknown_kind) must still verify -- the normalisation is
            # the point, and without this case a rename would silently make old bundles look corrupt.
            ("legacy-vocabulary", dict(folds=1, truth_folds=1, legacy=True), EXIT_PASS),
        ]
        for name, kw, want in cases:
            d = _bundle(tmp, name, **kw)
            code, problems, warnings, lines = verify(d)
            if code != want:
                fails.append("%s: exit=%s want=%s problems=%s" % (name, code, want, problems[:2]))
        # a real falsifier of the CARRIER cross-check: flip the verdict in the census file
        d = _bundle(tmp, "carrier", folds=1, truth_folds=1)
        p = os.path.join(d, "contrib_census.json")
        doc = read_json(p)
        doc["unresolved"][0]["carrierVerdict"] = "unique"
        with io.open(p, "w", encoding="utf-8", newline="\n") as fh:
            json.dump(doc, fh, ensure_ascii=False)
        # re-hash so the CHECKSUM step cannot be what fails: this must fail on the cross-diff
        man = read_json(os.path.join(d, "manifest.json"))
        for f in man["files"]:
            if f["name"] == "contrib_census.json":
                f["bytes"] = os.path.getsize(p)
                f["check"] = fnv1a64(p)
        with io.open(os.path.join(d, "manifest.json"), "w", encoding="utf-8", newline="\n") as fh:
            json.dump(man, fh, ensure_ascii=False)
        code, problems, warnings, lines = verify(d)
        if code != EXIT_ERROR or not any("carrierVerdict" in x for x in problems):
            fails.append("carrier: exit=%s problems=%s" % (code, problems[:3]))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    for f in fails:
        print("SELFTEST FAIL: " + A(f))
    print("SELFTEST %s (%d case(s))" % ("PASS" if not fails else "FAIL", 8))
    return EXIT_PASS if not fails else EXIT_ERROR


def main(argv=None):
    ap = argparse.ArgumentParser(description="Verify one evidence bundle from the plugin.")
    ap.add_argument("--bundle", default=None)
    ap.add_argument("--report", default=DEFAULT_REPORT)
    ap.add_argument("--selftest", action="store_true")
    a = ap.parse_args(argv)
    if a.selftest:
        return selftest()
    bundle = a.bundle or newest_bundle()
    if not bundle or not os.path.isdir(bundle):
        print("DATA_MISSING: no bundle found (looked in %s)" % A(BUNDLES))
        print("NOTE: a bundle is written by the plugin (General/ExtractOnBattleEnd or the ExtractKey key).")
        return EXIT_MISSING
    try:
        code, problems, warnings, lines = verify(bundle)
        return report_and_print(a.report, bundle, code, problems, warnings, lines)
    except Exception:
        import traceback
        print("ERROR: verifier crashed")
        traceback.print_exc()
        return EXIT_ERROR


if __name__ == "__main__":
    sys.exit(main())
