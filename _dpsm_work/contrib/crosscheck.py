# -*- coding: utf-8 -*-
"""Phase E acceptance: cross-implementation check (plugin contribution vs offline core).

The plugin's export must contain a "contribution" section (schemaVersion, method,
damageBasis, totals, actors, rules, links). This tool recomputes the same numbers with
the verified offline core and compares them field by field. It is the hard acceptance
gate for Phase E: two independent implementations must agree on the same export.

P0-A (2026-10-04): the verdict is now classified with _dpsm_work/contribution_gate.py:
  * a version >= 1.6.0 export WITHOUT a contribution section is DATA_MISSING (exit 3), not the
    old silent "ABSENT ... return 0"; only a pre-1.6.0 export may be LEGACY_NOT_APPLICABLE;
  * the ERROR list contrib.validate.check() returns is consumed -- an analysis that breaks its own
    per-hit/accounting identities fails the gate even when every compared value happens to match;
  * a missing MANDATORY contribution field is an omission (ERROR), not "optional, skip";
  * a training-ground export (quest 9999) gets its own applicability verdict;
  * the root atkAdd counters are checked against the export body (contribution_gate.atkadd_*).

Usage: python -m contrib.crosscheck [export.json] [--tol-rel 1e-6] [--tol-abs 1.0]
       [--outdir DIR] [--selftest]
Exit: 0 = PASS/WARNING/LEGACY_NOT_APPLICABLE, 1 = ERROR (mismatch, validate ERROR, omission),
      3 = DATA_MISSING (a current-version export has no contribution section).
"""
from __future__ import annotations
import argparse, glob, io, json, os, sys

if __package__ in (None, ""):
    # Allow `python _dpsm_work/contrib/crosscheck.py <export>` as well as
    # `python -m contrib.crosscheck`. Registering the package (instead of switching every relative
    # import to an absolute one) keeps this module's identity the same under both invocations, so the
    # helper modules are never imported twice under two names.
    _WORKDIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    if _WORKDIR not in sys.path:
        sys.path.insert(0, _WORKDIR)
    import contrib            # noqa: E402
    sys.modules.setdefault("contrib.crosscheck", sys.modules[__name__])
    __package__ = "contrib"

from . import loader, aggregate
from . import validate as vmod

HERE = os.path.dirname(os.path.abspath(__file__))
WORK = os.path.dirname(HERE)
ROOT = os.path.dirname(WORK)
EXPORTS = os.path.join(ROOT, "BepInEx", "plugins", "DpsMeter", "exports")
OUTDIR = os.path.join(HERE, "reports")
if WORK not in sys.path:
    sys.path.insert(0, WORK)

import contribution_gate as gate          # noqa: E402  (kept in sync: one status vocabulary)


def _num(d, key, default=None):
    v = d.get(key, default)
    return float(v) if isinstance(v, (int, float)) else default


def close(a, b, rel, ab):
    if a is None or b is None:
        return False
    return abs(a - b) <= max(ab, rel * max(abs(a), abs(b)))


def _at_least(version, want):
    """True when a dotted plugin version string is >= want. Unparseable -> False (never guess)."""
    try:
        parts = [int(x) for x in str(version or "").split(".")]
    except (TypeError, ValueError):
        return False
    while len(parts) < len(want):
        parts.append(0)
    return tuple(parts[:len(want)]) >= tuple(want)


def _present(d, key):
    """True when the producer actually emitted the field (distinguishes 0 from 'absent')."""
    return key in d and d.get(key) is not None


def _give_census(export):
    """(steps, hits_any, hits_applied, dropped_hits) over the EXPORTED events.

    steps        = kind="given" fold steps (this is what 1.7.9's giveApplied counts);
    hits_any     = hits carrying at least one such step (reported, not asserted);
    hits_applied = hits whose given-factor PRODUCT is not 1.0 -- exactly the hits on which the caller's
                   GivenFoldHits++ fires, because GivenFoldApplies() tests the PRODUCT, not the number of
                   steps. Exactly-cancelling entries (0.5 x 2.0) therefore count as a step and as an entry
                   but not as an applied hit, and an identity written against hits_any would false-ERROR;
    dropped_hits = hits that dropped fold steps at FoldContext.MaxSteps. A dropped step is invisible in the
                   body, so when any exist the identities can only be a lower bound.

    The counters are cumulative for the session, so the comparison is only exact when every composition
    pass is exported. MEASURED 2026-10-04 to hold exactly on all 29 corpus exports.
    """
    steps = hits_any = hits_applied = dropped = 0
    for e in export.events:
        calc = e.get("calc") or {}
        n = 0
        prod = 1.0
        for f in calc.get("fold") or []:
            if f.get("kind") == "given":
                n += 1
                try:
                    prod *= float(f.get("factor") or 1.0)
                except (TypeError, ValueError):
                    prod = None
        if n:
            hits_any += 1
            steps += n
            if prod is None or abs(prod - 1.0) > 1e-9:
                hits_applied += 1
        if calc.get("foldDropped"):
            dropped += 1
    return steps, hits_any, hits_applied, dropped


def _atkadd_census(export, team=None):
    """(nonEmptyHits, rows) over the export body, optionally restricted to one analysed team.

    nonEmptyHits: dmg events whose calc.atkAdd list is non-empty (one per hit)
    rows:         total calc.atkAdd entries (one per giver per hit -- a hit with two givers adds 2)

    team=None counts EVERY dmg event (what atkAdd.emitted counts, because the plugin composition
    pass runs for every hit). team=1 counts only the hits the contribution model walks -- the Python
    twin of Contribution.cs Compute:
        if (hit.AttackerKey == 0 || !ix.Team.TryGetValue(hit.AttackerKey, out attacker)) continue;
    the attacker key must resolve to an actor row AND that row team must be the analysed one.
    (Measured 2026-10-04 on battle_9999_20261004_042458: 509 rows total, 34 in-domain, and the
    section restates exactly those 34.)
    """
    nonempty_hits = 0
    rows = 0
    for e in export.events:
        if e.get("type") != "dmg":
            continue
        a = (e.get("calc") or {}).get("atkAdd")
        if not a:
            continue
        if team is not None:
            key = e.get("atkKey")
            row = export.by_key.get(key) if key is not None else None
            if row is None or row.team != team:
                continue
        nonempty_hits += 1
        rows += len(a)
    return nonempty_hits, rows


def compare(path, tol_rel=1e-6, tol_abs=1.0):
    export = loader.load(path)
    raw = export.raw
    sec = raw.get("contribution")
    version = export.version
    training = loader.training_mode(export)
    out = {"file": export.name, "pluginVersion": version, "quest": export.quest,
           "sectionPresent": sec is not None, "mismatches": [], "omissions": [],
           "validateErrors": [], "atkAddInvariants": [],
           "checked": {}, "tolerance": {"rel": tol_rel, "abs": tol_abs},
           "statusCode": gate.EXIT_PASS, "reasons": [], "notes": []}

    reasons = []
    applicability, app_reason = gate.training_applicability(export.quest, training)
    out["modelApplicability"] = applicability
    out["trainingMode"] = bool(training)
    if training:
        reasons.append(app_reason)

    # ---- root atkAdd counters vs the export body (independent of the section) ----------------
    root_atkadd = raw.get("atkAdd")
    nonempty_hits, rows = _atkadd_census(export)                  # every hit: what emitted counts
    nonempty_hits_dom, rows_dom = _atkadd_census(export, team=1)  # in-domain: what folds restate
    out["atkAddCensus"] = {"nonEmptyHits": nonempty_hits, "rows": rows,
                           "nonEmptyHitsInDomain": nonempty_hits_dom, "rowsInDomain": rows_dom,
                           "analysedTeam": 1, "root": root_atkadd}
    atk_reasons = gate.atkadd_counter_bounds(root_atkadd, sec)
    atk_reasons += gate.atkadd_section_reasons(root_atkadd, sec, nonempty_hits, rows, rows_dom)
    for r in atk_reasons:
        if r.get("severity") in (gate.REASON_ERROR, gate.REASON_WARN):
            out["atkAddInvariants"].append(r["code"] + ": " + r["message"])
    reasons.extend(atk_reasons)

    # ---- granted-talent counters vs the export body (1.7.9) ---------------------------------
    # The counters live INSIDE root.rosterAudit (ExportService appends ,"rosterAudit":{...). The first
    # version of this wiring passed the root dict, so every lookup was None and the check degraded to one
    # NOTE -- it never ran on any file. Feed it the block the numbers are actually in.
    give_steps, give_hits_any, give_hits_applied, give_dropped = _give_census(export)
    roster_give = raw.get("rosterAudit") or None
    out["giveCensus"] = {"givenFoldSteps": give_steps, "hitsWithAGivenFold": give_hits_any,
                         "hitsWithANonCancellingGivenProduct": give_hits_applied,
                         "hitsWithDroppedFoldSteps": give_dropped,
                         "rosterAudit": {"giveApplied": None if roster_give is None else roster_give.get("giveApplied"),
                                         "giveFoldHits": None if roster_give is None else roster_give.get("giveFoldHits"),
                                         "giveHits": None if roster_give is None else roster_give.get("giveHits")}}
    give_reasons = gate.give_section_reasons(roster_give, version, give_steps, give_hits_applied, give_dropped)
    out["giveInvariants"] = []
    for r in give_reasons:
        if r.get("severity") in (gate.REASON_ERROR, gate.REASON_WARN):
            out["giveInvariants"].append(r["code"] + ": " + r["message"])
    reasons.extend(give_reasons)

    if sec is None:
        st, r = gate.classify_section(version, False)
        reasons.append(r)
        out["status"] = st
        out["reasons"] = reasons
        out["notes"] = []
        out["statusCode"] = gate.exit_code(gate.status_from_reasons(reasons))
        return out

    st, r = gate.classify_section(version, True)
    reasons.append(r)
    an = aggregate.analyze(export, 1)
    issues, summary = vmod.check(an, export)
    verrs = [i for i in issues if i[0] == "ERROR"]
    out["validateErrors"] = [{"code": c, "message": m} for _lv, c, m in verrs]
    if verrs:
        # P0-A: consume validate.check's ERROR, do not just read its summary. The two independent
        # cores can agree value-for-value on a file whose per-hit identity is already broken.
        reasons.append(gate.reason(gate.REASON_ERROR, "VALIDATE_ERROR",
                                   "%d validate ERROR(s): %s" % (len(verrs), verrs[0][1])))

    def mm(what, mine, theirs):
        out["mismatches"].append({"field": what, "offline": mine, "plugin": theirs})

    def omit(what):
        out["omissions"].append(what)
        reasons.append(gate.reason(gate.REASON_ERROR, "MANDATORY_MISSING",
                                   "mandatory contribution field absent from the plugin section: %s" % what))

    # ---- header ----
    for k, want in (("method", "log-share/1"), ("damageBasis", "dealt")):
        if sec.get(k) != want:
            mm("contribution.%s" % k, want, sec.get(k))
    for name, present in gate.mandatory_fields(sec):
        if not present:
            omit(name)
    tot = sec.get("totals") or {}

    def pick(top_key, tot_key=None):
        """Plan 5-E puts the totals at contribution.*; accept the totals.* fallback too."""
        v = _num(sec, top_key)
        if v is None and tot_key:
            v = _num(tot, tot_key)
        return v

    # MANDATORY value checks: the fields the plan (section 5-E) requires at contribution.* level.
    # Presence/types are the schema guard's job; this gate compares VALUES -- and refuses to skip a
    # mandatory field silently, which is what the pre-P0-A version did ("theirs is None -> mismatch"
    # made a missing field indistinguishable from a wrong one and never touched exit priority).
    # 1.7.8 (P0-B): schema 1.1 REDEFINED totalDamage as analyzableDealt and moved the residual into
    # unattributedDamage, so the 1.0 formula (analyzable + unattributed) is wrong for a 1.1 section
    # whenever the unattributed pool is non-empty. MEASURED: on battle_411001_20261005_142931 (3.885%
    # pool) the stale formula reported a correct 1.7.11 section as an ERROR. Keyed on the SECTION schema
    # (like check_export_schema); a section that does not declare one falls back to the plugin version.
    _sec_ver = sec.get("schemaVersion")
    _is_110 = _at_least(_sec_ver, (1, 1)) if _sec_ver else _at_least(version, (1, 7, 8))
    _expect_total = an.analyzable if _is_110 else (an.analyzable + an.unattributed_credit)
    mm_checks = {
        "totalDamage": (_expect_total, pick("totalDamage", "totalDamage"),
                        _present(sec, "totalDamage")),
        "attributedDamage": (an.actor_total_credit(), pick("attributedDamage", "attributedDamage"),
                             _present(sec, "attributedDamage")),
        "unattributedDamage": (an.unattributed_credit, pick("unattributedDamage", "unattributedCredit"),
                               _present(sec, "unattributedDamage")),
        "coverage.analyzableDealt": (an.analyzable, (sec.get("coverage") or {}).get("analyzableDealt"),
                                     "analyzableDealt" in (sec.get("coverage") or {})),
    }
    # OPTIONAL detail blocks: compared only when the producer emitted them (a lean producer that
    # follows the plan's field list exactly is legitimate; a missing MANDATORY field is an omission).
    for k, v in (("totals.analyzableDealt", _num(tot, "analyzableDealt")),
                 ("totals.unattributedCredit", _num(tot, "unattributedCredit")),
                 ("totals.totalDamage", _num(tot, "totalDamage"))):
        if v is not None:
            mm_checks[k] = (an.analyzable if k == "totals.analyzableDealt" else
                            (an.unattributed_credit if k == "totals.unattributedCredit"
                             else an.analyzable + an.unattributed_credit), v, True)
    out["checked"]["totals"] = len(mm_checks)
    for k, (mine, theirs, present) in mm_checks.items():
        if not present:
            continue
        if theirs is None or not close(mine, theirs, tol_rel, tol_abs):
            mm(k, mine, theirs)
    # ---- actors (by key) ----
    mine_actors = {a.key: a for a in an.actors.values()}
    their_actors = {}
    for row in (sec.get("actors") or []):
        if row.get("key") is not None:
            their_actors[row["key"]] = row
    out["checked"]["actors"] = len(their_actors)
    pairs = [(k, mine_actors.get(k), their_actors[k]) for k in their_actors]
    for k, mine, theirs in pairs:
        if mine is None:
            mm("actors[%s] missing offline" % k, None, theirs.get("name"))
            continue
        fields = [("directDamage", mine.direct), ("baseCredit", mine.base),
                  ("selfRuleCredit", mine.self_rule), ("assistCredit", mine.assist),
                  ("totalCredit", mine.total)]
        # 1.7.12: the same-team split. MANDATORY only for a producer new enough to emit it -- every
        # section written before 1.7.12 legitimately lacks it, and treating that as an omission would
        # turn the whole archive into a red ERROR (the schema guard is version-gated for the same
        # reason). A 1.7.12+ section that omits it still gets the MANDATORY_MISSING error below.
        if _at_least(version, (1, 7, 12)):
            fields += [("friendly", mine.friendly), ("friendlyHits", mine.friendly_hits),
                       ("hostileDamage", mine.hostile)]
        for field, mine_v in fields:
            if not _present(theirs, field):
                omit("actors[key=%s].%s" % (k, field))
                continue
            tv = _num(theirs, field)
            if tv is None or not close(mine_v, tv, tol_rel, tol_abs):
                mm("actors[key=%s].%s" % (k, field), mine_v, tv)
    for k, mine in mine_actors.items():
        if k not in their_actors and (mine.direct > 0 or mine.total > 0):
            mm("actors[key=%s] missing from plugin section" % k, mine.total, None)
    # ---- rules (by name/kind/side/owner) ----
    mine_rules = {}
    for r in an.rules.values():
        mine_rules["%s|%s|%s|%s" % (r.name, r.kind, r.side, r.owner_name)] = r
    their_rules = {}
    for row in (sec.get("rules") or []):
        key = "%s|%s|%s|%s" % (row.get("ruleName"), row.get("kind"), row.get("side"), row.get("ownerName"))
        their_rules[key] = row
    out["checked"]["rules"] = len(their_rules)
    for key, row in their_rules.items():
        mine = mine_rules.get(key)
        if mine is None:
            mm("rules[%s] missing offline" % key, None, row.get("damageEquivalent"))
            continue
        if not _present(row, "damageEquivalent"):
            omit("rules[%s].damageEquivalent" % key)
            continue
        tv = _num(row, "damageEquivalent")
        if tv is None or not close(mine.damage, tv, tol_rel, tol_abs):
            mm("rules[%s].damageEquivalent" % key, mine.damage, tv)
    # a rule/link the plugin DROPPED is an omission, not a lean producer: rules and links are
    # mandatory lists, so both directions must agree
    for key, mine in mine_rules.items():
        if key not in their_rules:
            mm("rules[%s] missing from plugin section" % key, mine.damage, None)
    # ---- links ----
    mine_links = {(l.from_key, l.to_key): l for l in an.links.values()}
    their_links = {}
    for row in (sec.get("links") or []):
        if row.get("fromKey") is not None and row.get("toKey") is not None:
            their_links[(row["fromKey"], row["toKey"])] = row
    out["checked"]["links"] = len(their_links)
    for key, row in their_links.items():
        mine = mine_links.get(key)
        if mine is None:
            mm("links[%s] missing offline" % (key,), None, row.get("amount"))
            continue
        if not _present(row, "amount"):
            omit("links[%s].amount" % (key,))
            continue
        tv = _num(row, "amount")
        if tv is None or not close(mine.amount, tv, tol_rel, tol_abs):
            mm("links[%s].amount" % (key,), mine.amount, tv)
    for key, mine in mine_links.items():
        if key not in their_links:
            mm("links[%s] missing from plugin section" % (key,), mine.amount, None)
    # ---- offline side evidence ----
    out["optionalBlocks"] = {"totals": bool(tot), "unattributed": bool(sec.get("unattributed")),
                             "diagnostics": bool(sec.get("diagnostics"))}
    out["offline"] = {"creditedShare": summary.get("credited_share"),
                      "unattributedShare": summary.get("unattributed_share"),
                      "foldAccounting": summary.get("fold_accounting")}
    if out["mismatches"]:
        reasons.append(gate.reason(gate.REASON_ERROR, "MISMATCH",
                                   "%d value mismatch(es) between the plugin section and the offline "
                                   "core; first: %s" % (len(out["mismatches"]),
                                                        out["mismatches"][0]["field"])))
    # "note" reasons record a sub-check that could not run; they never change the status, but they
    # are kept and printed so a skipped check can never look like a passed one.
    out["notes"] = [r for r in reasons if r.get("severity") not in gate.VERDICT_SEVERITIES]
    verdict = [r for r in reasons if r.get("severity") in gate.VERDICT_SEVERITIES]
    status = gate.status_from_reasons(verdict)
    out["status"] = status
    out["reasons"] = verdict
    out["statusCode"] = gate.exit_code(status)
    return out


SYNTH = {
    "version": "1.7.6", "quest": 411001, "duration": 1.0, "result": "Test", "app": "DpsMeter",
    "totals": {"dealt": 3000},
    # faithful to what a 1.7.4+ producer writes: the root counter block exists, and one hit carries
    # one calc.atkAdd entry (rows == emitted == 1). Without it the section would be complete but the
    # atkAdd identity check would correctly report that it does not apply.
    "atkAdd": {"hits": 1, "emitted": 1, "selfValues": 0, "skippedGuard": 0, "skippedCollision": 0,
               "skippedUnowned": 0, "skippedOwnerNull": 0, "skippedNegative": 0, "skippedType": 0},
    "actors": [{"key": 1, "name": "Alpha", "team": 1, "kind": "P", "summon": False,
                "abilities": [{"id": 900, "name": "AlphaRule"}],
                "talents": [{"abilityId": 900, "ability": "AlphaRule"}]},
               {"key": 2, "name": "Beta", "team": 1, "kind": "P", "summon": False}],
    "events": [{"t": 0.0, "type": "dmg", "attacker": "Alpha", "victim": "V", "amount": 3000,
                "atkTeam": 1, "vicTeam": 2, "atkKey": 1, "vicKey": 99, "crit": False,
                "calc": {
                    "fold": [{"kind": "given", "side": "vic", "origin": "given#1/1006/-10",
                              "factor": 1.5, "label": "被伤害+50%(赋予)", "byUnit": "Beta"}],
                    "atkAdd": [{"kind": "atkadd", "side": "atk", "origin": "atkadd#Beta",
                                "factor": 1.2, "label": "攻击力加算 Rate+20", "byUnit": "Beta",
                                "rateSum": 20.0, "actualSum": 0, "base": 1000.0, "deltaPower": 200.0,
                                "factorIsExact": True}]}}],
}


def selftest():
    """Prove the gate accepts a faithful section and rejects a corrupted one."""
    import copy
    from . import report_json
    os.makedirs(OUTDIR, exist_ok=True)
    tmp = os.path.join(OUTDIR, "_selftest_export.json")
    ex = loader.ExportData(copy.deepcopy(SYNTH), path=tmp)
    an = aggregate.analyze(ex, 1)
    issues, summary = vmod.check(an, ex)
    good = report_json.to_json(an, ex, issues, summary)["contribution"]
    raw = copy.deepcopy(SYNTH)
    raw["contribution"] = good
    with io.open(tmp, "w", encoding="utf-8") as fh:
        fh.write(json.dumps(raw, ensure_ascii=False))
    ok = compare(tmp)
    bad_raw = copy.deepcopy(raw)
    for row in bad_raw["contribution"]["actors"]:
        if row["key"] == 2:
            row["assistCredit"] += 1000000.0
    bad_raw["contribution"]["totalDamage"] += 1000000.0
    with io.open(tmp, "w", encoding="utf-8") as fh:
        fh.write(json.dumps(bad_raw, ensure_ascii=False))
    bad = compare(tmp)
    nover_raw = copy.deepcopy(raw)
    nover_raw["version"] = "1.7.6"      # a CURRENT-version file that lost its section
    nover_raw.pop("contribution", None)
    with io.open(tmp, "w", encoding="utf-8") as fh:
        fh.write(json.dumps(nover_raw, ensure_ascii=False))
    nover = compare(tmp)
    os.remove(tmp)
    ok_pass = ok["status"] == gate.PASS
    bad_pass = bad["status"] == gate.ERROR and len(bad["mismatches"]) >= 2
    nover_pass = (nover["status"] == gate.DATA_MISSING
                  and nover["statusCode"] == gate.EXIT_DATA_MISSING)
    print("selftest faithful-section=%s corrupted-section=%s detected=%d current-version-missing=%s exit=%d"
          % ("ACCEPT" if ok_pass else "FAIL", bad["status"], len(bad["mismatches"]),
             nover["status"], nover["statusCode"]))
    for m in bad["mismatches"][:3]:
        print("  detected %s" % m["field"].encode("ascii", "replace").decode("ascii"))
    return 0 if (ok_pass and bad_pass and nover_pass) else 1


def main(argv=None):
    ap = argparse.ArgumentParser(description="Phase E cross-implementation check")
    ap.add_argument("--selftest", action="store_true")
    ap.add_argument("export", nargs="?")
    ap.add_argument("--tol-rel", type=float, default=1e-6)
    ap.add_argument("--tol-abs", type=float, default=1.0)
    ap.add_argument("--outdir", help="where to write crosscheck_<stem>.json (default contrib/reports; "
                                     "CONTRIB_CROSSCHECK_OUTDIR overrides)")
    ap.add_argument("--batch", metavar="GLOB",
                    help="check every matching export (default %s) and exit with the WORST status; "
                         "one line per file, no report written" % os.path.join(EXPORTS, "battle_*.json"))
    a = ap.parse_args(argv)
    if a.selftest:
        return selftest()
    if a.batch:
        pattern = a.batch if os.path.isabs(a.batch) or os.sep in a.batch else os.path.join(EXPORTS, a.batch)
        paths = sorted(glob.glob(pattern))
        if not paths:
            print("%s: no export matched %s" % (gate.DATA_MISSING, pattern))
            return gate.EXIT_DATA_MISSING
        worst = gate.PASS
        counts = {}
        for p in paths:
            try:
                r = compare(p, a.tol_rel, a.tol_abs)
            except Exception as exc:
                r = {"file": os.path.basename(p), "status": gate.ERROR, "statusCode": gate.EXIT_ERROR,
                     "mismatches": [], "omissions": [], "checked": {},
                     "reasons": [gate.reason(gate.REASON_ERROR, "UNUSABLE",
                                             "%s: %s" % (type(exc).__name__, exc))]}
            line = ("%-42s version=%-6s status=%-21s exit=%d mismatches=%d omissions=%d"
                    % (r["file"], r.get("pluginVersion"), r["status"], r.get("statusCode", 1),
                       len(r["mismatches"]), len(r.get("omissions") or [])))
            print(line.encode("ascii", "replace").decode("ascii"))
            for rr in r.get("reasons") or []:
                print("    [%s] %s" % (rr["severity"],
                                       rr["message"].encode("ascii", "replace").decode("ascii")))
            counts[r["status"]] = counts.get(r["status"], 0) + 1
            worst = gate.combine([worst, r["status"]])
        print("batch: %d file(s) %s -> worst=%s exit=%d"
              % (len(paths), json.dumps(counts, sort_keys=True), worst, gate.exit_code(worst)))
        return gate.exit_code(worst)
    if a.export:
        path = a.export
    else:
        cands = [p for p in glob.glob(os.path.join(EXPORTS, "battle_*.json"))
                 if not os.path.basename(p).startswith("battle_9999")]
        if not cands:
            print("no export found under %s" % EXPORTS)
            return gate.EXIT_DATA_MISSING
        path = sorted(cands, key=os.path.getmtime)[-1]
    try:
        res = compare(path, a.tol_rel, a.tol_abs)
    except Exception as exc:                       # unusable input is an ERROR, never a silent 0
        print("ERROR: could not check %s: %s: %s"
              % (os.path.basename(path), type(exc).__name__,
                 str(exc).encode("ascii", "replace").decode("ascii")))
        return gate.EXIT_ERROR
    outdir = a.outdir or os.environ.get("CONTRIB_CROSSCHECK_OUTDIR") or OUTDIR
    os.makedirs(outdir, exist_ok=True)
    stem = os.path.splitext(os.path.basename(path))[0]
    out = os.path.join(outdir, "crosscheck_%s.json" % stem)
    with io.open(out, "w", encoding="utf-8") as fh:
        fh.write(json.dumps(res, ensure_ascii=False, indent=1))
    print("file=%s version=%s quest=%s training=%s section=%s status=%s checked=%s mismatches=%d omissions=%d"
          % (res["file"], res.get("pluginVersion"), res.get("quest"), res.get("trainingMode"),
             res["sectionPresent"], res["status"], json.dumps(res["checked"]),
             len(res["mismatches"]), len(res["omissions"])))
    for r in res.get("reasons") or []:
        print("  [%s %s] %s" % (r["severity"], r["code"],
                                r["message"].encode("ascii", "replace").decode("ascii")))
    for r in res.get("notes") or []:
        print("  note [%s] %s" % (r["code"],
                                  r["message"].encode("ascii", "replace").decode("ascii")))
    for m in res["mismatches"][:10]:
        print("  MISMATCH %s offline=%s plugin=%s" %
              (m["field"].encode("ascii", "replace").decode("ascii"), m["offline"], m["plugin"]))
    for m in res.get("atkAddInvariants") or []:
        print("  ATKADD %s" % m.encode("ascii", "replace").decode("ascii"))
    print("modelApplicability=%s" % res.get("modelApplicability"))
    print("report=%s" % out)
    if res["status"] == gate.LEGACY_NOT_APPLICABLE:
        print("note: this gate does not apply to this export -- see the SECTION_LEGACY reason above. "
              "It is NOT a pass for the current version.")
    return res["statusCode"]


if __name__ == "__main__":
    sys.exit(main())
