# -*- coding: utf-8 -*-
"""P0-A gate regression: prove the fixed gates now cave in where they used to exit 0.

Every fixture is built in-process from one tiny synthetic export (no 20 MB battle file is read, no
export directory is written). Run it from _dpsm_work:

    python -m contrib.tests.test_gate            # exits 0 only when every expectation holds
    python -m contrib.tests.test_gate --selftest # same thing (kept for CI symmetry)

WHAT THIS PINS (all eight P0-A obligations)
  1. a CURRENT-version export with no contribution section cannot pass   -> DATA_MISSING / exit 3
  2. the status vocabulary and the exit codes are fixed and round-trip   -> gate.EXIT
  3. crosscheck consumes validate.check's ERROR                          -> ERROR / exit 1
  4. a value mismatch is exit 1                                          -> ERROR / exit 1
  5. a missing mandatory contribution field is exit 1                    -> ERROR / exit 1
  6. the golden export being absent is exit 3                            -> (test_golden_155.py, run by CI)
  7. the FACT checker's STARVED/cap/coverage rules are exit != 0          -> (check_fact_signature.py)
  8. quest 9999 has its own applicability, never the normal PASS          -> LEGACY_NOT_APPLICABLE
  9. the atkAdd counters are cross-checked against the export body        -> ERROR on drift
"""
from __future__ import annotations
import copy, io, json, os, sys, tempfile

if __package__ in (None, ""):
    sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))

from contrib import crosscheck, loader
from contrib import validate as vmod
import contribution_gate as gate

WORK = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

FAILS = []
PASSES = []


def chk(label, ok, detail=""):
    (PASSES if ok else FAILS).append("%s%s" % (label, ("" if not detail else " -- " + detail)))
    print("%-4s %s%s" % ("PASS" if ok else "FAIL", label, ("" if not detail else "  [" + detail + "]")))


# ---------------------------------------------------------------------------
# one faithful synthetic export; every other fixture is a mutation of it
# ---------------------------------------------------------------------------

def good_export():
    from contrib import aggregate
    from contrib import report_json
    ex = loader.ExportData(copy.deepcopy(crosscheck.SYNTH), path="synthetic.json")
    an = aggregate.analyze(ex, 1)
    issues, summary = vmod.check(an, ex)
    raw = copy.deepcopy(crosscheck.SYNTH)
    raw["contribution"] = report_json.to_json(an, ex, issues, summary)["contribution"]
    return raw


def write(tmpdir, name, raw):
    p = os.path.join(tmpdir, name)
    with io.open(p, "w", encoding="utf-8") as fh:
        fh.write(json.dumps(raw, ensure_ascii=False))
    return p


def main():
    tmpdir = tempfile.mkdtemp(prefix="p0a_gate_")
    good = good_export()

    # ---- 0. the vocabulary and the exit-code contract itself ----------------
    chk("status vocabulary is the frozen five",
        set(gate.STATUSES) == {"PASS", "WARNING", "LEGACY_NOT_APPLICABLE", "DATA_MISSING", "ERROR"})
    chk("exit codes: PASS/WARNING/LEGACY=0 DATA_MISSING=3 ERROR=1",
        (gate.exit_code(gate.PASS) == 0 and gate.exit_code(gate.WARNING) == 0
         and gate.exit_code(gate.LEGACY_NOT_APPLICABLE) == 0
         and gate.exit_code(gate.DATA_MISSING) == 3 and gate.exit_code(gate.ERROR) == 1))
    try:
        gate.exit_code("OK")
        chk("unknown status raises instead of guessed", False, "no exception")
    except ValueError:
        chk("unknown status raises instead of guessed", True)
    chk("priority ERROR > DATA_MISSING > LEGACY > WARNING > PASS",
        (gate.combine([gate.PASS, gate.DATA_MISSING]) == gate.DATA_MISSING
         and gate.combine([gate.DATA_MISSING, gate.ERROR]) == gate.ERROR
         and gate.combine([gate.LEGACY_NOT_APPLICABLE, gate.WARNING]) == gate.LEGACY_NOT_APPLICABLE)
        and gate.combine([gate.PASS, gate.WARNING]) == gate.WARNING)
    chk("crosscheck reuses the gate's exit codes (no second convention)",
        (crosscheck.gate.EXIT_DATA_MISSING == gate.EXIT_DATA_MISSING
         and crosscheck.gate.EXIT_ERROR == gate.EXIT_ERROR))
    chk("the fixed gate conditions carry the documented exit codes",
        (gate.EXIT_ON_VALIDATE_ERROR == 1 and gate.EXIT_ON_MISMATCH == 1
         and gate.EXIT_ON_MISSING_MANDATORY == 1 and gate.EXIT_ON_MISSING_SECTION == 3
         and gate.EXIT_ON_MISSING_GOLDEN == 3 and gate.EXIT_ON_FACT_STARVED == 1
         and gate.EXIT_ON_FACT_CAP_DRIFT == 1 and gate.EXIT_ON_TRAINING_SAME_VERDICT == 0))
    chk("mandatory field list is non-trivial and every name is emitted by the offline producer",
        len(gate.mandatory_fields(good["contribution"])) == 10
        and all(p for _n, p in gate.mandatory_fields(good["contribution"])))

    # ---- 1. version gate: current version missing the section ----------------
    raw = copy.deepcopy(good)
    raw["version"] = "1.7.6"
    raw.pop("contribution", None)
    res = crosscheck.compare(write(tmpdir, "newver_missing.json", raw))
    chk("1.7.6 without contribution -> DATA_MISSING", res["status"] == gate.DATA_MISSING,
        "status=%s" % res["status"])
    chk("1.7.6 without contribution -> exit 3", res["statusCode"] == 3, "exit=%s" % res["statusCode"])
    chk("1.7.6 without contribution -> the reason names the version and the first emitting version",
        any(r["code"] == "SECTION_MISSING" and "1.6.0" in r["message"] and "1.7.6" in r["message"]
            for r in res["reasons"]))

    # ---- 2. legacy: pre-1.6.0 without the section ----------------------------
    raw = copy.deepcopy(good)
    raw["version"] = "1.5.5"
    raw.pop("contribution", None)
    res = crosscheck.compare(write(tmpdir, "legacy_absent.json", raw))
    chk("1.5.5 without contribution -> LEGACY_NOT_APPLICABLE, exit 0",
        res["status"] == gate.LEGACY_NOT_APPLICABLE and res["statusCode"] == 0,
        "status=%s exit=%s" % (res["status"], res["statusCode"]))
    chk("legacy verdict prints WHY it does not apply (never a silent 0)",
        any(r["code"] == "SECTION_LEGACY" and "predates" in r["message"] for r in res["reasons"]))
    chk("a legacy export can never be reported as the current version's PASS",
        res["status"] != gate.PASS)

    # ---- 3. the training ground (quest 9999) has its own applicability -------
    raw = copy.deepcopy(good)
    raw["quest"] = 9999
    res = crosscheck.compare(write(tmpdir, "training_with_section.json", raw))
    chk("quest 9999 -> LEGACY_NOT_APPLICABLE (not the normal PASS)",
        res["status"] == gate.LEGACY_NOT_APPLICABLE, "status=%s" % res["status"])
    chk("quest 9999 -> modelApplicability=not_comparable",
        res["modelApplicability"] == "not_comparable")
    chk("quest 9999 -> the reason names the special-mode rule",
        any(r["code"] == "TRAINING_NOT_COMPARABLE" for r in res["reasons"]))
    raw = copy.deepcopy(good)
    raw["quest"] = 9999
    raw["version"] = "1.7.6"
    raw.pop("contribution", None)
    res = crosscheck.compare(write(tmpdir, "training_missing.json", raw))
    chk("quest 9999 + current version missing section -> DATA_MISSING wins over LEGACY precedence",
        res["status"] == gate.DATA_MISSING and res["statusCode"] == 3,
        "status=%s exit=%s" % (res["status"], res["statusCode"]))

    # ---- 4. a faithful current-version section still passes ------------------
    res = crosscheck.compare(write(tmpdir, "good.json", good))
    chk("faithful 1.7.6 section -> PASS, exit 0",
        res["status"] == gate.PASS and res["statusCode"] == 0,
        "status=%s exit=%s" % (res["status"], res["statusCode"]))
    chk("faithful section -> no validate ERROR, no omission",
        not res["validateErrors"] and not res["omissions"])

    # ---- 5. tampered values -> mismatch -> exit 1 ----------------------------
    raw = copy.deepcopy(good)
    raw["contribution"]["totalDamage"] += 1000000.0
    raw["contribution"]["actors"][0]["assistCredit"] += 1000000.0
    res = crosscheck.compare(write(tmpdir, "tampered.json", raw))
    chk("tampered contribution values -> ERROR, exit 1",
        res["status"] == gate.ERROR and res["statusCode"] == 1,
        "status=%s exit=%s mismatches=%d" % (res["status"], res["statusCode"],
                                             len(res["mismatches"])))
    chk("tampered values are reported as real mismatches", len(res["mismatches"]) >= 2)

    # ---- 6. a mandatory field amputated -> omission -> exit 1 ----------------
    raw = copy.deepcopy(good)
    raw["contribution"].pop("totalDamage")
    res = crosscheck.compare(write(tmpdir, "amputated.json", raw))
    chk("missing mandatory totalDamage -> ERROR, exit 1",
        res["status"] == gate.ERROR and res["statusCode"] == 1,
        "status=%s exit=%s omissions=%s" % (res["status"], res["statusCode"], res["omissions"]))
    chk("the omission names the field it wanted",
        "contribution.totalDamage" in res["omissions"])
    raw = copy.deepcopy(good)
    raw["contribution"]["actors"][0].pop("baseCredit")
    res = crosscheck.compare(write(tmpdir, "amputated_actor.json", raw))
    chk("missing mandatory actor field -> ERROR, exit 1",
        res["status"] == gate.ERROR and res["statusCode"] == 1,
        "status=%s" % res["status"])

    # ---- 7. VALIDATE ERROR is consumed even when all compared values agree ---
    # The mutation is applied to the export BODY only: the section stays byte-identical to what an
    # honest producer wrote for its own (self-consistent) view, so a value-for-value comparison is
    # clean while aggregate.analyze on the body breaks the per-hit identity.
    raw = copy.deepcopy(good)
    ev = raw["events"][0]
    ev["atkKey"] = None            # attacker no longer resolvable from the event
    ev["atkTeam"] = None
    res = crosscheck.compare(write(tmpdir, "validate_broken.json", raw))
    chk("validate.check ERROR is consumed -> ERROR, exit 1",
        res["status"] == gate.ERROR and res["statusCode"] == 1,
        "status=%s exit=%s verrs=%s" % (res["status"], res["statusCode"],
                                        [e["code"] for e in res["validateErrors"]]))
    chk("the reason records the validate ERROR codes",
        any(r["code"] == "VALIDATE_ERROR" for r in res["reasons"]))
    # the consumption helper is asserted directly, so a future caller cannot re-introduce the old
    # "read only summary, drop the ERROR list" shortcut without failing this test
    ex_obj = loader.load(write(tmpdir, "validate_broken2.json", raw))
    from contrib import aggregate as _agg
    importlib_an = _agg.analyze(ex_obj, 1)
    code, status, errs, _warns, _summary = gate.validate_result(importlib_an, ex_obj)
    chk("gate.validate_result turns a validate ERROR into exit 1",
        code == 1 and status == gate.ERROR and len(errs) >= 1,
        "code=%s status=%s errors=%s" % (code, status, [e[1] for e in errs]))

    # ---- 8. atkAdd counter identities ---------------------------------------
    raw = copy.deepcopy(good)
    raw["atkAdd"]["emitted"] = 3          # 3 rows claimed, 1 row actually in the body
    res = crosscheck.compare(write(tmpdir, "atkadd_emitted_drift.json", raw))
    chk("atkAdd.emitted != non-empty calc.atkAdd rows -> ERROR, exit 1",
        res["status"] == gate.ERROR and res["statusCode"] == 1,
        "status=%s" % res["status"])
    chk("the reason is the atkAdd identity",
        any(r["code"] == "ATKADD_EMITTED_VS_BODY" for r in res["reasons"]))
    raw = copy.deepcopy(good)
    raw["atkAdd"]["skippedNegative"] = -1
    res = crosscheck.compare(write(tmpdir, "atkadd_negative.json", raw))
    chk("a negative atkAdd counter -> ERROR",
        res["status"] == gate.ERROR
        and any(r["code"] == "ATKADD_COUNTER_RANGE" for r in res["reasons"]))
    raw = copy.deepcopy(good)
    raw["atkAdd"]["emitted"] = 0          # body has 1 row; folds would have 1 too
    res = crosscheck.compare(write(tmpdir, "atkadd_zero.json", raw))
    chk("atkAdd.emitted=0 with a non-empty body -> ERROR",
        res["status"] == gate.ERROR
        and any(r["code"] == "ATKADD_EMITTED_VS_BODY" for r in res["reasons"]))
    raw = copy.deepcopy(good)
    raw.pop("atkAdd", None)
    res = crosscheck.compare(write(tmpdir, "atkadd_absent.json", raw))
    chk("no root atkAdd block but hits carry calc.atkAdd -> ERROR",
        res["status"] == gate.ERROR
        and any(r["code"] == "ATKADD_SECTION_MISSING" for r in res["reasons"]))
    # a section that authors atkadd rows must restate ALL of them: emit rows for only one of two
    raw = copy.deepcopy(good)
    raw["events"].append({"t": 0.5, "type": "dmg", "attacker": "Alpha", "victim": "V", "amount": 500,
                          "atkTeam": 1, "vicTeam": 2, "atkKey": 1, "vicKey": 99, "crit": False,
                          "calc": {"fold": [],
                                   "atkAdd": [{"kind": "atkadd", "side": "atk",
                                               "origin": "atkadd#Beta", "factor": 1.1,
                                               "label": "x", "byUnit": "Beta"}]}})
    raw["atkAdd"]["emitted"] = 2          # honest about the body (2 rows) ...
    res = crosscheck.compare(write(tmpdir, "atkadd_folds_short.json", raw))
    chk("atkAdd.emitted with an unchanged section -> ERROR (the section under-restates)",
        res["status"] == gate.ERROR
        and any(r["code"] == "ATKADD_FOLDS_VS_BODY" for r in res["reasons"]),
        "status=%s codes=%s" % (res["status"], [r["code"] for r in res["reasons"]]))
    # a producer that authors NO atkadd rule at all (the offline report_json.py cannot restate the
    # plugin's AtkAddRow list) is a NOTE, not an error -- asserted on the gate function directly so
    # the assertion is about the policy and not about crosscheck's other comparisons.
    no_atkadd_section = {"rules": [{"kind": "given", "folds": 3}], "actors": []}
    folds_reasons = gate.atkadd_section_reasons({"emitted": 2, "hits": 2}, no_atkadd_section, 2, 2)
    chk("a section without any atkadd rule -> NOTE, not ERROR",
        all(r["severity"] == gate.REASON_NOTE for r in folds_reasons)
        and any(r["code"] == "ATKADD_FOLDS_ABSENT" for r in folds_reasons),
        "codes=%s" % [(r["severity"], r["code"]) for r in folds_reasons])
    partial = gate.atkadd_section_reasons({"emitted": 2, "hits": 2},
                                          {"rules": [{"kind": "atkadd", "folds": 1}]}, 2, 2, 2)
    chk("a section that authors atkadd rows but under-restates them -> ERROR",
        any(r["severity"] == gate.REASON_ERROR and r["code"] == "ATKADD_FOLDS_VS_BODY"
            for r in partial),
        "codes=%s" % [(r["severity"], r["code"]) for r in partial])
    # forward compatibility: P1-A adds four more counters (SelfByKey / SelfByNameFallback /
    # NameCollision / OwnerUnknown). The range check must validate them, not refuse them.
    a1 = {"hits": 5, "emitted": 3, "selfValues": 4, "skippedGuard": 1, "skippedCollision": 0,
          "skippedUnowned": 0, "skippedOwnerNull": 0, "skippedNegative": 0, "skippedType": 0,
          "selfByKey": 3, "selfByNameFallback": 1, "nameCollision": 0, "ownerUnknown": 0}
    chk("a counter added after this file was written is accepted (and range-checked)",
        not gate.atkadd_counter_bounds(a1, None),
        "reasons=%s" % [r["code"] for r in gate.atkadd_counter_bounds(a1, None)])
    a2 = dict(a1, selfByKey=-2)
    chk("a new counter with a negative value is still caught",
        any(r["code"] == "ATKADD_COUNTER_RANGE" for r in gate.atkadd_counter_bounds(a2, None)))
    a3 = dict(a1)
    a3.pop("skippedType")
    chk("a producer that stops writing a refusal counter -> ERROR",
        any(r["code"] == "ATKADD_COUNTER_MISSING" for r in gate.atkadd_counter_bounds(a3, None)))

    # ---- 9. version parsing ------------------------------------------------
    chk("version parsing handles real strings and junk",
        (gate.version_tuple("1.7.6") == (1, 7, 6)
         and gate.version_tuple("1.6.0-rc1") == (1, 6, 0)
         and gate.version_tuple(None) == (0,)
         and gate.version_at_least("1.5.5") is False
         and gate.version_at_least("1.6.0") is True
         and gate.version_at_least("1.7.6") is True))

    # ---- 10bis. REGRESSION (integration finding): the atkAdd fold relation is a DOMAIN relation
    # The first version compared sum(rules[kind=atkadd].folds) with EVERY non-empty calc.atkAdd row
    # and reported a false ERROR on the training ground (battle_9999_20261004_042458: folds=34 vs
    # 509 rows). Case 1 exercises the REAL export end to end: it is the file that failed, and it
    # must not be an ERROR for that reason any more. Case 2 is the negative control: the check has
    # to stay able to reject a section that under-restates its atkadd folds.
    exports_dir = os.path.join(os.path.dirname(WORK), "BepInEx", "plugins", "DpsMeter", "exports")
    training_path = os.path.join(exports_dir, "battle_9999_20261004_042458.json")
    if os.path.exists(training_path):
        tr = crosscheck.compare(training_path)
        tr_folds_err = [r for r in tr["reasons"] if r["code"] == "ATKADD_FOLDS_VS_BODY"]
        chk("CASE1 training-ground export (9999) is not an atkAdd-domain ERROR",
            not tr_folds_err and tr["status"] != gate.ERROR,
            "status=%s exit=%s codes=%s" % (tr["status"], tr["statusCode"],
                                            [r["code"] for r in tr["reasons"]]))
        chk("CASE1 out-of-domain atkAdd rows are reported as an explicit NOTE",
            any(n["code"] == "ATKADD_FOLDS_OUT_OF_DOMAIN" for n in tr["notes"]),
            "notes=%s" % [n["code"] for n in tr["notes"]])
        chk("CASE1 the in-domain census is strictly smaller than the full body (34 < 509)",
            (tr["atkAddCensus"]["rowsInDomain"] == 34
             and tr["atkAddCensus"]["rows"] == 509
             and tr["atkAddCensus"]["rowsInDomain"] < tr["atkAddCensus"]["rows"]),
            "census=%s" % json.dumps({k: v for k, v in tr["atkAddCensus"].items() if k != "root"}))
        real_raw = loader.load(training_path).raw
        tsec = real_raw.get("contribution") or {}
        real_folds = sum(int(x.get("folds") or 0) for x in (tsec.get("rules") or [])
                         if x.get("kind") == "atkadd")
        chk("CASE1 the section really does restate only the in-domain rows",
            real_folds == tr["atkAddCensus"]["rowsInDomain"] == 34,
            "folds=%d rowsInDomain=%d" % (real_folds, tr["atkAddCensus"]["rowsInDomain"]))
        # CASE 2 (negative control) on the same real export: bump EVERY atkadd folds value by 1 so
        # the section claims more folds than the domain has rows. Incrementing every rule (instead of
        # one) keeps the check independent of which giver carries which row, which the export does
        # not expose.
        bumped = copy.deepcopy(real_raw)
        n_atk_rules = 0
        for x in (bumped["contribution"].get("rules") or []):
            if x.get("kind") == "atkadd":
                x["folds"] = int(x.get("folds") or 0) + 1
                n_atk_rules += 1
        bumped_res = crosscheck.compare(write(tmpdir, "atkadd_folds_real_bumped.json", bumped))
        chk("CASE2 tampered folds on the real 9999 export -> ERROR, exit 1",
            bumped_res["status"] == gate.ERROR and bumped_res["statusCode"] == 1
            and any(r["code"] == "ATKADD_FOLDS_VS_BODY" for r in bumped_res["reasons"]),
            "atkaddRules=%d status=%s exit=%s codes=%s"
            % (n_atk_rules, bumped_res["status"], bumped_res["statusCode"],
               [r["code"] for r in bumped_res["reasons"]]))
        # the same negative control on the export where folds==rows==6006 (no out-of-domain rows, so
        # the relation is a pure equality)
        main_path = os.path.join(exports_dir, "battle_411001_20261004_115417.json")
        if os.path.exists(main_path):
            main_raw = loader.load(main_path).raw
            mb = copy.deepcopy(main_raw)
            for x in (mb["contribution"].get("rules") or []):
                if x.get("kind") == "atkadd":
                    x["folds"] = int(x.get("folds") or 0) + 1
            mb_res = crosscheck.compare(write(tmpdir, "atkadd_folds_411_bumped.json", mb))
            chk("CASE2 same tamper on 411001 (6006 in-domain folds) -> ERROR, exit 1",
                mb_res["status"] == gate.ERROR and mb_res["statusCode"] == 1
                and any(r["code"] == "ATKADD_FOLDS_VS_BODY" for r in mb_res["reasons"]),
                "status=%s exit=%s" % (mb_res["status"], mb_res["statusCode"]))
            good_main = crosscheck.compare(main_path)
            chk("CASE2 control: the untampered 411001 export is not an ERROR",
                good_main["status"] != gate.ERROR, "status=%s" % good_main["status"])
    else:
        chk("CASE1 training-ground export is missing (cannot run the real-file regression)", False,
            training_path)

    # ---- 9c. the granted-talent counter gate must actually RUN on a real file ----------------
    # WHY A REAL-FILE CONTROL: the first version of give_section_reasons was handed the ROOT export dict
    # while the counters live under root.rosterAudit, so every lookup returned None and the whole check
    # degraded to a single NOTE on every file. It passed its own flat-dict unit tests while never
    # evaluating in the pipeline. Only crosscheck.compare on a real export can see that class of bug.
    names = sorted([n for n in os.listdir(exports_dir) if n.startswith("battle_") and n.endswith(".json")],
                   reverse=True)
    real_give = None
    for n in names:
        p = os.path.join(exports_dir, n)
        rr = loader.load(p).raw
        ra = rr.get("rosterAudit") or {}
        if ra.get("giveApplied") is not None and (rr.get("contribution") or {}):
            real_give = (p, rr, ra)
            break
    if real_give is None:
        chk("CASE3 a real export carrying granted-talent counters exists", False, exports_dir)
    else:
        gp, graw, gra = real_give
        gsteps, ghits_any, ghits_applied, gdropped = crosscheck._give_census(loader.load(gp))
        clean_g = crosscheck.compare(gp)
        chk("CASE3 the untampered real export raises no GIVE error",
            not [r for r in clean_g["reasons"]
                 if r["code"].startswith("GIVE_") and r["severity"] == gate.REASON_ERROR],
            "file=%s steps=%d appliedHits=%d giveApplied=%s status=%s"
            % (os.path.basename(gp), gsteps, ghits_applied, gra.get("giveApplied"), clean_g["status"]))
        bad_ga = copy.deepcopy(graw)
        bad_ga["rosterAudit"]["giveApplied"] = 1
        r_ga = crosscheck.compare(write(tmpdir, "give_ga_tampered.json", bad_ga))
        chk("CASE3 a tampered rosterAudit.giveApplied -> ERROR (the gate really runs)",
            r_ga["status"] == gate.ERROR
            and any(r["code"] == "GIVE_APPLIED_VS_BODY" for r in r_ga["reasons"]),
            "status=%s codes=%s" % (r_ga["status"], [r["code"] for r in r_ga["reasons"]]))
        fh_missing = copy.deepcopy(graw)
        fh_missing["version"] = "1.7.9"
        fh_missing["rosterAudit"]["giveApplied"] = gsteps
        fh_missing["rosterAudit"].pop("giveFoldHits", None)
        r_fh = crosscheck.compare(write(tmpdir, "give_fh_missing.json", fh_missing))
        chk("CASE3 a 1.7.9 export without giveFoldHits -> DATA_MISSING exit 3",
            r_fh["status"] == gate.DATA_MISSING and r_fh["statusCode"] == gate.EXIT_DATA_MISSING,
            "status=%s exit=%s codes=%s" % (r_fh["status"], r_fh["statusCode"],
                                            [r["code"] for r in r_fh["reasons"]]))
        fh_wrong = copy.deepcopy(fh_missing)
        fh_wrong["rosterAudit"]["giveFoldHits"] = 7
        r_fw = crosscheck.compare(write(tmpdir, "give_fh_wrong.json", fh_wrong))
        chk("CASE3 a 1.7.9 export with a wrong giveFoldHits -> ERROR",
            r_fw["status"] == gate.ERROR
            and any(r["code"] == "GIVE_FOLDHITS_VS_BODY" for r in r_fw["reasons"]),
            "status=%s codes=%s" % (r_fw["status"], [r["code"] for r in r_fw["reasons"]]))
        fh_ok = copy.deepcopy(fh_missing)
        fh_ok["rosterAudit"]["giveFoldHits"] = ghits_applied
        r_ok = crosscheck.compare(write(tmpdir, "give_fh_ok.json", fh_ok))
        chk("CASE3 control: the correct 1.7.9 counters raise no GIVE error",
            not [r for r in r_ok["reasons"]
                 if r["code"].startswith("GIVE_") and r["severity"] == gate.REASON_ERROR],
            "status=%s codes=%s" % (r_ok["status"], [r["code"] for r in r_ok["reasons"]]))

    # the gate function itself: in-domain equality passes; a shortfall inside the domain fails
    dsec = {"rules": [{"kind": "atkadd", "folds": 34}]}
    dom_ok = gate.atkadd_section_reasons({"emitted": 509, "hits": 558}, dsec, 472, 509, 34)
    chk("gate: folds == in-domain rows passes even when out-of-domain rows exist",
        not [r for r in dom_ok if r["severity"] == gate.REASON_ERROR],
        "codes=%s" % [(r["severity"], r["code"]) for r in dom_ok])
    dom_bad = gate.atkadd_section_reasons({"emitted": 509, "hits": 558}, dsec, 472, 509, 40)
    chk("gate: folds short of the IN-DOMAIN rows still fails",
        any(r["severity"] == gate.REASON_ERROR and r["code"] == "ATKADD_FOLDS_VS_BODY"
            for r in dom_bad),
        "codes=%s" % [(r["severity"], r["code"]) for r in dom_bad])
    dom_none = gate.atkadd_section_reasons({"emitted": 509, "hits": 558}, dsec, 472, 509, None)
    chk("gate: without a domain the check degrades to a NOTE, never an ERROR over a mixed body",
        not [r for r in dom_none if r["severity"] == gate.REASON_ERROR],
        "codes=%s" % [(r["severity"], r["code"]) for r in dom_none])

    # ---- 9b. the granted-talent counter contract (1.7.9) --------------------
    g_ok = gate.give_section_reasons({"giveApplied": 5788, "giveFoldHits": 2894, "giveHits": 6252},
                                     "1.7.9", 5788, 2894)
    chk("gate: a 1.7.9 file with giveApplied == body steps is clean",
        not [r for r in g_ok if r["severity"] in (gate.REASON_ERROR, gate.REASON_MISSING)],
        "codes=%s" % [(r["severity"], r["code"]) for r in g_ok])
    g_legacy = gate.give_section_reasons({"giveApplied": 8682, "giveHits": 6252}, "1.7.8", 5788, 2894)
    chk("gate: the pre-1.7.9 entries+hits counter is a NOTE, never an ERROR",
        not [r for r in g_legacy if r["severity"] == gate.REASON_ERROR]
        and any(r["code"] == "GIVE_APPLIED_LEGACY_MIXED" for r in g_legacy),
        "codes=%s" % [(r["severity"], r["code"]) for r in g_legacy])
    g_bad = gate.give_section_reasons({"giveApplied": 1, "giveFoldHits": 2894}, "1.7.9", 5788, 2894)
    chk("gate: giveApplied disagreeing with the body is an ERROR",
        any(r["severity"] == gate.REASON_ERROR and r["code"] == "GIVE_APPLIED_VS_BODY" for r in g_bad),
        "codes=%s" % [(r["severity"], r["code"]) for r in g_bad])
    g_missing = gate.give_section_reasons({"giveApplied": 5788}, "1.7.9", 5788, 2894)
    chk("gate: a 1.7.9 file without giveFoldHits is DATA_MISSING",
        any(r["severity"] == gate.REASON_MISSING and r["code"] == "GIVE_FOLDHITS_MISSING"
            for r in g_missing), "codes=%s" % [(r["severity"], r["code"]) for r in g_missing])
    g_hits = gate.give_section_reasons({"giveApplied": 5788, "giveFoldHits": 7}, "1.7.9", 5788, 2894)
    chk("gate: giveFoldHits disagreeing with the body is an ERROR",
        any(r["severity"] == gate.REASON_ERROR and r["code"] == "GIVE_FOLDHITS_VS_BODY" for r in g_hits),
        "codes=%s" % [(r["severity"], r["code"]) for r in g_hits])
    g_absent = gate.give_section_reasons({}, "1.3.0", 12, 5)
    chk("gate: no give counters at all is a single NOTE",
        len(g_absent) == 1 and g_absent[0]["severity"] == gate.REASON_NOTE,
        "codes=%s" % [(r["severity"], r["code"]) for r in g_absent])

    # ---- 10. the classify_section contract, directly ------------------------
    st, r = gate.classify_section("1.5.5", False)
    chk("classify_section(1.5.5, absent) == LEGACY_NOT_APPLICABLE",
        st == gate.LEGACY_NOT_APPLICABLE and r["severity"] == gate.REASON_LEGACY)
    st, r = gate.classify_section("1.6.0", False)
    chk("classify_section(1.6.0, absent) == DATA_MISSING",
        st == gate.DATA_MISSING and r["severity"] == gate.REASON_MISSING)
    st, r = gate.classify_section("1.7.7", False)
    chk("classify_section(1.7.7, absent) == DATA_MISSING", st == gate.DATA_MISSING)
    st, r = gate.classify_section("1.7.7", True)
    chk("classify_section(1.7.7, present) == PASS", st == gate.PASS)

    print("")
    print("gate regression: %d passed, %d failed (fixtures in %s)"
          % (len(PASSES), len(FAILS), tmpdir))
    if FAILS:
        for f in FAILS:
            print("  FAIL " + f)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
