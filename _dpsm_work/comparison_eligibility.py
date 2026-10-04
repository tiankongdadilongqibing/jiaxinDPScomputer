# -*- coding: utf-8 -*-
"""ComparisonEligibility -- the N1 admission contract (CONTRIBUTION-NEXT-PHASE-ROADMAP.md section N1).

WHAT IT IS. The roadmap asks for a per-SAMPLE, per-METRIC admission layer that the comparison stage must
consult BEFORE pooling anything, and it must speak its OWN vocabulary: the contribution gate answers "is
this file a valid log-share/1 section of the version it claims"; this module answers "may THIS metric of
THIS sample enter a cross-battle comparison, and with whom".

  * gate WARNING is NOT a yes;
  * LEGACY is not a statement about the current capability;
  * DATA_MISSING / ERROR samples never enter a formal contribution ranking;
  * an UNRUN check is reported as unknown and counts as RESTRICTED, never as pass.

CONTRACT (frozen here, consumed by N2):
  status   : ELIGIBLE | RESTRICTED | REJECTED | NOT_APPLICABLE   (new vocabulary; gate words are untouched)
  metrics  : team_damage | actor_credit | rule_coverage | outcome
  per check: {"check": name, "ran": bool, "verdict": pass|restrict|reject|unknown, "reason": str}
  set level: any REJECTED member -> the metric is REJECTED for the set; else any RESTRICTED -> RESTRICTED;
             else ELIGIBLE. A RESTRICTED/REJECTED verdict without a reason is a bug.

ASCII-only stdout. Usage:
  python comparison_eligibility.py --exports DIR [--only FILE ...] [--json OUT] [--selftest]
Exit: 0 = at least one metric ELIGIBLE for the whole set; 4 = nothing may be ranked; 2 = no input;
      3 = inputs unreadable; 1 = internal error.
"""
from __future__ import print_function
import argparse, glob, hashlib, io, json, os, re, shutil, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
DEF_EXPORTS = os.path.join(ROOT, "BepInEx", "plugins", "DpsMeter", "exports")

ELIGIBLE, RESTRICTED, REJECTED, NOT_APPLICABLE = "ELIGIBLE", "RESTRICTED", "REJECTED", "NOT_APPLICABLE"
METRICS = ("team_damage", "actor_credit", "rule_coverage", "outcome")
RANK = {ELIGIBLE: 0, NOT_APPLICABLE: 1, RESTRICTED: 2, REJECTED: 3}
BAD = {"pass": 0, "restrict": 1, "unknown": 1, "reject": 2}


def sha256_file(path, chunk=1 << 20):
    h = hashlib.sha256()
    with io.open(path, "rb") as fh:
        while True:
            b = fh.read(chunk)
            if not b:
                break
            h.update(b)
    return h.hexdigest().upper()


def section_of(text, key):
    """The balanced JSON object that follows key (the same reader the layout guard uses)."""
    i = text.rfind(key)
    if i < 0:
        return None
    j = text.find("{", i + len(key))
    if j < 0:
        return None
    depth = 0
    instr = False
    esc = False
    k = j
    n = len(text)
    while k < n:
        ch = text[k]
        if instr:
            if esc:
                esc = False
            elif ch == "\\":
                esc = True
            elif ch == chr(34):
                instr = False
        else:
            if ch == chr(34):
                instr = True
            elif ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    return text[j:k + 1]
        k += 1
    return None


def load_facts(path, doc=None):
    """Everything the contract can know from the file alone. Missing data stays None, never 0.

    doc: an already-parsed export. The tail-window scan below is a fast path for the real (minified)
    corpus; it MISSES a section that sits further than 500 KB from EOF, which a re-serialised copy does
    (found by N2's tamper controls: the copies looked section-less and were wrongly REJECTED). When the
    window misses, the file is parsed instead of concluding "absent", and a caller that already parsed it
    passes `doc` to skip both.
    """
    size = os.path.getsize(path)
    head = ""
    tail = ""
    if doc is None:
        with io.open(path, "rb") as fh:
            head = fh.read(8192).decode("utf-8", "replace")
            tail = head   # a small file IS its own tail; b"" here silently stayed bytes and broke section_of
            if size > 8192:
                fh.seek(max(0, size - 500000))
                tail = fh.read().decode("utf-8", "replace")
    else:
        tmp = json.dumps({"version": doc.get("version"), "quest": doc.get("quest"),
                          "result": doc.get("result"), "duration": doc.get("duration")})
        head = tmp
        tail = ""
    f = {"file": os.path.basename(path), "path": path, "size": size, "sha256": sha256_file(path)}
    for k, pat in (("version", r'"version"\s*:\s*"([^"]+)"'),
                   ("quest", r'"quest"\s*:\s*"?(\d+)"?'),
                   ("result", r'"result"\s*:\s*"([^"]*)"'),
                   ("duration", r'"duration"\s*:\s*([0-9.]+)')):
        m = re.search(pat, head)
        f[k] = m.group(1) if m else None
    f["quest"] = int(f["quest"]) if f["quest"] else None
    sec_raw = section_of(tail, '"contribution"')   # tolerant: the plugin minifies, a fixture may not
    if doc is not None:
        f["capability"] = {"atkAdd": "atkAdd" in doc, "paramOwners": "paramOwners" in doc,
                           "damageLedger": bool((doc.get("contribution") or {}).get("damageLedger"))}
        f["hasContributionSection"] = bool(doc.get("contribution"))
        sec_raw = json.dumps(doc["contribution"], ensure_ascii=False) if doc.get("contribution") else None
    elif sec_raw is None and size > 8192:
        # the fast path missed the section: parse the file rather than calling it absent
        try:
            doc2 = json.load(io.open(path, encoding="utf-8"))
            f["capability"] = {"atkAdd": "atkAdd" in doc2, "paramOwners": "paramOwners" in doc2,
                               "damageLedger": bool((doc2.get("contribution") or {}).get("damageLedger"))}
            f["hasContributionSection"] = bool(doc2.get("contribution"))
            if f["hasContributionSection"]:
                sec_raw = json.dumps(doc2["contribution"], ensure_ascii=False)
            else:
                sec_raw = None
        except Exception:
            pass
        f["hasContributionSection"] = sec_raw is not None
    else:
        f["capability"] = {"atkAdd": '"atkAdd":{' in tail, "paramOwners": '"paramOwners":{' in tail,
                           "damageLedger": '"damageLedger":{' in tail}
        # the small-file fast path must set it too: forgetting this made every small fixture look
        # section-less and turned RESTRICTED into REJECTED (caught by the guard's own selftest)
        f["hasContributionSection"] = sec_raw is not None
    sec = {}
    if sec_raw:
        try:
            sec = json.loads(sec_raw)
        except Exception:
            f["sectionUnreadable"] = True
    f["section"] = {"schemaVersion": sec.get("schemaVersion"), "method": sec.get("method"),
                    "damageBasis": sec.get("damageBasis"), "producer": sec.get("producer"),
                    "totalDamage": sec.get("totalDamage"), "attributedDamage": sec.get("attributedDamage"),
                    "unattributedDamage": sec.get("unattributedDamage")}
    f["ledger"] = sec.get("damageLedger") or {}
    # N6: a dropped fold step (FoldContext.MaxSteps) makes the per-hit equality a LOWER BOUND. It is
    # recorded by the plugin only inside the contribution section's diagnostics; without a section the
    # loss cannot be ruled out, and "cannot be ruled out" is NOT a pass.
    f["foldDropped"] = ((sec.get("diagnostics") or {}).get("foldDropped") if sec else None)
    f["analyzableDealt"] = (sec.get("coverage") or {}).get("analyzableDealt")
    f["rules"] = len(sec.get("rules") or [])
    f["actors"] = len(sec.get("actors") or [])
    f["training"] = f.get("quest") == 9999
    # Provenance the export does NOT carry: stated as unknown, never assumed equal.
    f["configProvenanceKnown"] = False
    f["identityStrength"] = "weak-name-only"
    return f


def _c(name, ran, verdict, reason):
    return {"check": name, "ran": bool(ran), "verdict": verdict, "reason": reason}


# The gate answers "is this a valid log-share/1 section of the version it claims"; this layer answers
# "may this metric enter a cross-battle comparison". The mapping is the roadmap's explicit requirement:
#   * the gate's WARNING is NOT a yes -> restrict (pool only with the warning named);
#   * LEGACY / NOT_RUN are not statements about the CURRENT capability -> restrict, never a pass;
#   * DATA_MISSING / ERROR never enter a formal contribution ranking -> reject.
# It is only consulted for the two metrics that read the contribution section.
GATE_MAP = {"PASS": None, "WARNING": "restrict", "LEGACY_NOT_APPLICABLE": "restrict", "NOT_RUN": "restrict",
            "DATA_MISSING": "reject", "ERROR": "reject"}


def load_gate(path):
    """{basename: {status, leaks, knownMultWithoutFoldHits}} from an applicability.json.

    The gate status is the crosscheck verdict; the two leak counters are P0-D's own measurements, so this
    layer CONSUMES them instead of re-deriving them (who detects what is stated in the roadmap).
    """
    gate = {}
    try:
        j = json.load(io.open(path, encoding="utf-8"))
        for e in j.get("exports") or []:
            if not e.get("file"):
                continue
            gate[e["file"]] = {"status": (e.get("crosscheck") or {}).get("status"),
                               "leaks": e.get("correctedUnexplainedLeaks"),
                               "knownMultWithoutFoldHits": e.get("knownMultWithoutFoldHits")}
    except Exception:
        return {}
    return gate


def _gate_checks(ch, f, gate, info):
    """Append the gate-status and fold-leak checks for a contribution-reading metric."""
    # N6: this check does NOT depend on the gate record, so it must run even when no gate status was
    # supplied -- putting it after the early return below silently skipped it (caught by the selftest).
    fd = f.get("foldDropped")
    if fd is None:
        ch.append(_c("fold-dropped", False, "unknown",
                     "no contribution diagnostics in this sample: dropped fold steps cannot be ruled out"))
    elif fd > 0:
        ch.append(_c("fold-dropped", True, "restrict",
                     "%d dropped fold step(s): the per-hit equality is a LOWER BOUND for this sample" % fd))
    else:
        ch.append(_c("fold-dropped", True, "pass", "no dropped fold steps"))
    if not info:
        ch.append(_c("gate-status", False, "unknown",
                     "no gate status was supplied for this sample (--applicability): an unrun check is not a yes"))
        ch.append(_c("fold-leaks", False, "unknown", "no gate record: fold completeness cannot be checked here"))
        return
    status = info.get("status")
    if status is None:
        ch.append(_c("gate-status", False, "unknown", "the gate record carries no crosscheck status"))
    else:
        verdict = GATE_MAP.get(status, "restrict")
        if verdict is None:
            ch.append(_c("gate-status", True, "pass", "gate status PASS"))
        elif verdict == "reject":
            ch.append(_c("gate-status", True, "reject",
                         "gate status %s: this sample never enters a formal contribution ranking" % status))
        else:
            ch.append(_c("gate-status", True, "restrict",
                         "gate status %s is not a yes (LEGACY is not a statement about the current capability)"
                         % status))
    leaks = info.get("leaks")
    if leaks is None:
        ch.append(_c("fold-leaks", False, "unknown",
                     "no correctedUnexplainedLeaks for this sample: an unrun check is not a yes"))
    elif leaks > 0:
        ch.append(_c("fold-leaks", True, "restrict",
                     "%d unexplained fold leak(s) after the attrMult correction: the arithmetic does not close "
                     "for this sample" % leaks))
    else:
        ch.append(_c("fold-leaks", True, "pass", "0 unexplained fold leaks"))


def _fold(checks):
    worst = "pass"
    reasons = []
    for c in checks:
        if BAD.get(c["verdict"], 1) > BAD.get(worst, 0):
            worst = c["verdict"] if c["verdict"] != "unknown" else "restrict"
        if c["verdict"] in ("reject", "restrict", "unknown"):
            reasons.append("%s: %s" % (c["check"], c["reason"]))
    status = {"pass": ELIGIBLE, "restrict": RESTRICTED, "reject": REJECTED}[worst]
    return {"status": status, "reasons": reasons, "checks": checks}


def intrinsic(f, gate=None):
    """Per-sample, per-metric verdict from the file alone (no peers needed). gate = {file: status}."""
    out = {}
    ev = f.get("hasContributionSection")
    sec = f.get("section") or {}
    led = f.get("ledger") or {}
    ch = []
    if f.get("quest") is None or f.get("duration") in (None, "0"):
        ch.append(_c("event-domain-basics", True, "reject", "no quest/duration in the file head"))
    else:
        ch.append(_c("event-domain-basics", True, "pass", "quest and duration present"))
    if led:
        gap = led.get("reconciliationGap")
        if gap is None:
            ch.append(_c("ledger-reconciliation", True, "unknown", "ledger without reconciliationGap"))
        elif abs(float(gap)) > 1.0:
            ch.append(_c("ledger-reconciliation", True, "restrict", "reconciliationGap=%s" % gap))
        else:
            ch.append(_c("ledger-reconciliation", True, "pass", "reconciliationGap=0"))
    else:
        ch.append(_c("ledger-reconciliation", False, "unknown",
                     "no damageLedger (pre-1.7.8): the event-domain total cannot be reconciled from the file"))
    # N1 correction: the first version marked this "unknown", which made team_damage permanently
    # RESTRICTED -- the CLI's exit-0 path was unreachable. A whole-battle total IS well defined here; a
    # fixed-window DPS claim is a DIFFERENT metric and is NOT covered by this verdict.
    ch.append(_c("window-definition", True, "pass",
                 "whole-battle total by definition (duration = the battle); any FIXED-WINDOW DPS claim is not covered"))
    out["team_damage"] = _fold(ch)

    ch = []
    if not ev:
        ch.append(_c("section-present", True, "reject", "no contribution section: nothing to attribute"))
    else:
        ch.append(_c("section-present", True, "pass", "contribution section present"))
        if sec.get("method") != "log-share/1":
            ch.append(_c("method", True, "reject", "method=%r is not log-share/1" % sec.get("method")))
        else:
            ch.append(_c("method", True, "pass", "method=log-share/1"))
        if sec.get("damageBasis") != "dealt":
            ch.append(_c("damage-basis", True, "reject", "damageBasis=%r is not dealt" % sec.get("damageBasis")))
        else:
            ch.append(_c("damage-basis", True, "pass", "damageBasis=dealt"))
        if sec.get("schemaVersion") == "1.1" and not f["capability"]["damageLedger"]:
            ch.append(_c("schema-ledger", True, "reject", "schema 1.1 without damageLedger"))
        else:
            ch.append(_c("schema-ledger", True, "pass", "schemaVersion=%s" % sec.get("schemaVersion")))
        if f["training"]:
            ch.append(_c("special-mode", True, "restrict",
                         "quest 9999 (training ground): x0.03/sub-unity mechanics are not comparable"))
        else:
            ch.append(_c("special-mode", True, "pass", "normal quest"))
        ch.append(_c("config-provenance", False, "unknown",
                     "the export records no config switches (GivenTalent/Madness/Contribution): two runs may differ silently"))
        ch.append(_c("identity", False, "restrict",
                     "cross-battle identity is the NAME only: one name may be two entities"))
        _gate_checks(ch, f, gate, (gate or {}).get(f["file"]))
    out["actor_credit"] = _fold(ch)

    ch = []
    if not ev or not f.get("rules"):
        ch.append(_c("rules-present", True, "reject", "no rules in the contribution section"))
    else:
        ch.append(_c("rules-present", True, "pass", "%d rule rows" % f["rules"]))
        ch.append(_c("coverage-denominator", True, "restrict",
                     "coverage is hit-weighted here; it must not be presented as time coverage"))
        _gate_checks(ch, f, gate, (gate or {}).get(f["file"]))
    out["rule_coverage"] = _fold(ch)

    ch = []
    if f.get("result") in (None, ""):
        ch.append(_c("result", True, "reject", "no result in the file head"))
    else:
        ch.append(_c("result", True, "pass", "result=%s" % f.get("result")))
    ch.append(_c("progress-metadata", False, "unknown",
                 "no wave/progress metadata: win rate cannot be derived from total damage"))
    out["outcome"] = _fold(ch)
    return out


def _group_key(f, metric):
    sec = f.get("section") or {}
    if metric == "team_damage":
        return (f.get("quest"), "dealt")
    if metric == "actor_credit":
        return (f.get("quest"), sec.get("schemaVersion"), sec.get("damageBasis"), f["capability"]["atkAdd"])
    if metric == "rule_coverage":
        return (f.get("quest"), sec.get("schemaVersion"))
    return (f.get("quest"),)


def set_decision(samples, metric):
    """Pool-level decision: the worst member wins, and peer comparability is checked as well."""
    members = {}
    reasons = []
    worst = ELIGIBLE
    for f, intr in samples:
        st = intr[metric]["status"]
        members[f["file"]] = st
        if RANK[st] > RANK[worst]:
            worst = st
        # the member reasons MUST survive to the set level, or a RESTRICTED set would be unexplained
        # (which this contract calls a bug).
        if RANK[st] >= RANK[RESTRICTED] and st == REJECTED:
            for r in intr[metric]["reasons"][:4]:
                tag = "%s: %s" % (f["file"], r)
                if tag not in reasons:
                    reasons.append(tag)
    # RESTRICTED members second, so a truncated print still shows the REJECTED reason (the decisive one)
    for f, intr in samples:
        st = intr[metric]["status"]
        if st == RESTRICTED:
            for r in intr[metric]["reasons"][:4]:
                tag = "%s: %s" % (f["file"], r)
                if tag not in reasons:
                    reasons.append(tag)
    groups = {}
    for f, intr in samples:
        if intr[metric]["status"] == REJECTED:
            continue
        groups.setdefault(_group_key(f, metric), []).append(f["file"])
    if len(groups) > 1:
        detail = "; ".join("%s -> %d sample(s)" % (list(k), len(v)) for k, v in sorted(groups.items(), key=lambda x: str(x[0])))
        reasons.append("capability/definition split: comparing across it is not like-for-like (%s)" % detail)
        if RANK[RESTRICTED] > RANK[worst]:
            worst = RESTRICTED
    admissible = sorted(sum(groups.values(), []))
    if groups and len(admissible) != len(samples):
        reasons.append("only %d of %d samples are admissible for this metric" % (len(admissible), len(samples)))
    if worst == ELIGIBLE and len(samples) < 2:
        worst = RESTRICTED
        reasons.append("single sample: differences are not evidence")
    return {"metric": metric, "status": worst, "reasons": reasons, "members": members, "admissible": admissible}


def evaluate(paths, gate=None, docs=None):
    docs = docs or {}
    facts = [load_facts(p, docs.get(os.path.basename(p))) for p in paths]
    samples = [(f, intrinsic(f, gate)) for f in facts]
    return {"contract": "ComparisonEligibility/1", "n": len(facts),
            "metrics": [set_decision(samples, m) for m in METRICS],
            "samples": [{"file": f["file"], "sha256": f["sha256"], "version": f["version"],
                         "quest": f["quest"], "schemaVersion": (f.get("section") or {}).get("schemaVersion"),
                         "capability": f["capability"], "identityStrength": f["identityStrength"],
                         "configProvenanceKnown": f["configProvenanceKnown"],
                         "gateStatus": ((gate or {}).get(f["file"]) or {}).get("status"), "intrinsic": intr}
                        for f, intr in samples]}


def main(argv=None):
    ap = argparse.ArgumentParser(description="N1 comparison admission contract")
    ap.add_argument("--exports", default=DEF_EXPORTS)
    ap.add_argument("--only", action="append", default=None)
    ap.add_argument("--json", dest="json_out", default=None)
    ap.add_argument("--applicability", default=None,
                    help="applicability.json whose exports[].crosscheck.status is mapped into the contract")
    ap.add_argument("--selftest", action="store_true")
    ap.add_argument("--selftest-e2e", action="store_true",
                    help="negative controls that run THIS CLI as a subprocess")
    a = ap.parse_args(argv)
    if a.selftest:
        return selftest()
    if a.selftest_e2e:
        return selftest_e2e()
    if a.only:
        paths = [p if os.path.isabs(p) else os.path.join(a.exports, p) for p in a.only]
    else:
        paths = sorted(glob.glob(os.path.join(a.exports, "battle_*.json")))
    paths = [p for p in paths if os.path.isfile(p)]
    if not paths:
        print("no input")
        return 2
    gate = load_gate(a.applicability) if a.applicability else None
    try:
        res = evaluate(paths, gate)
    except Exception as ex:
        print("ERROR: %r" % (ex,))
        return 1
    for m in res["metrics"]:
        print("%-13s %-11s admissible=%d/%d" % (m["metric"], m["status"], len(m["admissible"]), res["n"]))
        for r in m["reasons"][:5]:
            print("    " + r.encode("ascii", "replace").decode("ascii"))
    if a.json_out:
        io.open(a.json_out, "w", encoding="utf-8").write(json.dumps(res, ensure_ascii=False, indent=1))
        print("wrote %s" % a.json_out)
    return 0 if [m for m in res["metrics"] if m["status"] == ELIGIBLE] else 4


def _minimal(version, quest, schema, atkadd, method="log-share/1", basis="dealt", result="Lose"):
    sec = {"schemaVersion": schema, "method": method, "damageBasis": basis,
           "totalDamage": 1000, "attributedDamage": 1000, "unattributedDamage": 0,
           "coverage": {"analyzableDealt": 1000}, "actors": [], "rules": [{"ruleName": "x"}]}
    if schema == "1.1":
        sec["damageLedger"] = {"totalsDealt": 1000, "reconciliationGap": 0}
    body = {"version": version, "quest": quest, "result": result, "duration": 100.0,
            "totals": {"dealt": 1000}, "contribution": sec}
    if atkadd:
        body["atkAdd"] = {"hits": 1}
    return body


def selftest():
    import tempfile, shutil
    tmp = tempfile.mkdtemp(prefix="elig_")
    fails = []

    def mk(name, obj):
        p = os.path.join(tmp, name)
        # minified exactly like the plugin writes it: a fixture with ": " would exercise a code path
        # the real corpus never takes (and did: section_of could not find the key).
        io.open(p, "w", encoding="utf-8").write(json.dumps(obj, ensure_ascii=False, separators=(",", ":")))
        return p

    def metric_of(res, metric):
        for m in res["metrics"]:
            if m["metric"] == metric:
                return m
        return None

    def case(label, ok, extra=""):
        print("  [%s] %-56s %s" % ("PASS" if ok else "FAIL", label, extra))
        if not ok:
            fails.append(label)

    a = mk("a_178.json", _minimal("1.7.8", 411001, "1.1", True))
    b = mk("b_178.json", _minimal("1.7.8", 411001, "1.1", True))
    c = mk("c_173.json", _minimal("1.7.3", 411001, "1.0", False))
    d = mk("d_train.json", _minimal("1.7.8", 9999, "1.1", True))
    f = mk("f_legacy.json", _minimal("1.5.5", 411001, "1.0", False))
    e = os.path.join(tmp, "e_nosec.json")
    io.open(e, "w", encoding="utf-8").write(json.dumps({"version": "1.7.8", "quest": 411001,
                                                        "result": "Lose", "duration": 10.0}))
    g = mk("g_method.json", _minimal("1.7.8", 411001, "1.1", True, method="log-share/2"))

    res = evaluate([a, b])
    m = metric_of(res, "actor_credit")
    case("same-capability pair: config/identity still restrict",
         m["status"] == RESTRICTED and any("config-provenance" in r for r in m["reasons"]), m["status"])
    res = evaluate([a, c])
    m = metric_of(res, "actor_credit")
    case("atkadd capability split restricts",
         m["status"] == RESTRICTED and any("capability/definition split" in r for r in m["reasons"]), m["status"])
    res = evaluate([a, d])
    ma = metric_of(res, "actor_credit")
    mt = metric_of(res, "team_damage")
    case("training ground restricts actor_credit", ma["status"] == RESTRICTED, ma["status"])
    case("different quest restricts team_damage pooling", mt["status"] == RESTRICTED, mt["status"])
    res = evaluate([e])
    m = metric_of(res, "actor_credit")
    case("missing section rejects actor_credit",
         m["status"] == REJECTED and any("section-present" in r for r in m["reasons"]), m["status"])
    res = evaluate([f, c])
    mt = metric_of(res, "team_damage")
    case("pre-1.7.8 ledger absence restricts team_damage",
         mt["status"] == RESTRICTED and any("no damageLedger" in r for r in mt["reasons"]), mt["status"])
    res = evaluate([g])
    m = metric_of(res, "actor_credit")
    case("unknown method rejects actor_credit", m["status"] == REJECTED, m["status"])
    res = evaluate([a])
    m = metric_of(res, "team_damage")
    case("single sample is never ELIGIBLE (no evidence)", m["status"] == RESTRICTED, m["status"])

    res = evaluate([a, b], gate={"a_178.json": {"status": "ERROR"}})
    m = metric_of(res, "actor_credit")
    case("gate ERROR rejects actor_credit",
         m["status"] == REJECTED and any("gate status ERROR" in r for r in m["reasons"]), m["status"])
    res = evaluate([a, b], gate={"a_178.json": {"status": "WARNING"}})
    m = metric_of(res, "actor_credit")
    case("gate WARNING is not a yes",
         any("gate status WARNING" in r for r in m["reasons"]), m["status"])
    res = evaluate([a, b], gate={"a_178.json": {"status": "PASS", "leaks": 3}})
    m = metric_of(res, "actor_credit")
    case("an unexplained fold leak restricts the sample",
         any("fold leak" in r for r in m["reasons"]), m["status"])
    d_trunc = _minimal("1.7.8", 411001, "1.1", True)
    d_trunc["contribution"]["diagnostics"] = {"foldDropped": 3}
    tr = mk("trunc.json", d_trunc)
    res = evaluate([a, tr])
    m = metric_of(res, "actor_credit")
    case("a dropped fold step restricts (the equality becomes a lower bound)",
         any("LOWER BOUND" in r for r in m["reasons"]), m["status"])
    d_ok = _minimal("1.7.8", 411001, "1.1", True)
    d_ok["contribution"]["diagnostics"] = {"foldDropped": 0}
    ok2 = mk("nodrop.json", d_ok)
    res = evaluate([ok2])
    ck = [c for c in res["samples"][0]["intrinsic"]["actor_credit"]["checks"] if c["check"] == "fold-dropped"]
    case("no dropped steps is a pass on that check", bool(ck) and ck[0]["verdict"] == "pass",
         ck[0]["verdict"] if ck else "(missing)")

    res = evaluate([a, b], gate={"a_178.json": {"status": "PASS", "leaks": 0}})
    m = metric_of(res, "actor_credit")
    # assert the CHECK verdict, not a substring of the prose: the "unknown" reason for the OTHER sample
    # also contains the words "gate status", which made the first version of this case pass for the wrong
    # reason and then fail for the wrong reason.
    gchecks = [c for c in res["samples"][0]["intrinsic"]["actor_credit"]["checks"] if c["check"] == "gate-status"]
    gverdict = gchecks[0]["verdict"] if gchecks else "(no gate-status check: the sample was rejected earlier)"
    case("a clean gate record adds no new restriction",
         gverdict == "pass" and m["status"] == RESTRICTED, gverdict)

    real = os.path.join(DEF_EXPORTS, "battle_411001_20261004_144548.json")
    if os.path.isfile(real):
        res = evaluate([real])
        m = metric_of(res, "actor_credit")
        case("real 1.7.10 export is not REJECTED", m["status"] in (RESTRICTED, ELIGIBLE), m["status"])
        txt = io.open(real, encoding="utf-8").read()
        cut = os.path.join(tmp, "cut.json")
        i = txt.find('"contribution":{')
        io.open(cut, "w", encoding="utf-8").write(txt[:i] + '"zz":1}' if i > 0 else txt)
        res = evaluate([cut])
        m = metric_of(res, "actor_credit")
        case("deleted section in a real copy -> REJECTED", m["status"] == REJECTED, m["status"])
        bad = os.path.join(tmp, "badbasis.json")
        io.open(bad, "w", encoding="utf-8").write(txt.replace('"damageBasis":"dealt"', '"damageBasis":"taken"', 1))
        res = evaluate([bad])
        m = metric_of(res, "actor_credit")
        case("changed nested damageBasis in a real copy is caught", m["status"] == REJECTED, m["status"])
    else:
        print("  [SKIP] real export not found")
    shutil.rmtree(tmp, ignore_errors=True)
    print("---- selftest: %s" % ("PASS" if not fails else "FAIL %s" % fails))
    return 0 if not fails else 1


def selftest_e2e():
    """The roadmap requires the negative controls to reach the FINAL CLI, not just the flat function."""
    me = os.path.abspath(__file__)
    py = sys.executable
    tmp = tempfile.mkdtemp(prefix="elig_e2e_")
    fails = []
    A = "battle_411001_20261004_134853.json"
    B = "battle_411001_20261004_134524.json"
    copies = {}
    for n in (A, B):
        d = os.path.join(tmp, n)
        shutil.copyfile(os.path.join(DEF_EXPORTS, n), d)
        copies[n] = d
    src = io.open(os.path.join(DEF_EXPORTS, A), encoding="utf-8").read()
    cut = os.path.join(tmp, "cut.json")
    i = src.find('"contribution":{')
    io.open(cut, "w", encoding="utf-8").write((src[:i] + '"zz":1}') if i > 0 else src)
    bad = os.path.join(tmp, "badbasis.json")
    io.open(bad, "w", encoding="utf-8").write(src.replace('"damageBasis":"dealt"', '"damageBasis":"taken"', 1))

    def run(tag, args):
        lp = os.path.join(tmp, "o_%s.txt" % tag)
        with io.open(lp, "w", encoding="utf-8") as fh:
            rc = subprocess.call([py, me] + args, stdout=fh, stderr=subprocess.STDOUT)
        return rc, io.open(lp, encoding="utf-8", errors="replace").read()

    def case(label, ok, extra=""):
        print("  [%s] %-56s %s" % ("PASS" if ok else "FAIL", label, extra))
        if not ok:
            fails.append(label)

    rc, txt = run("ok", ["--only", copies[A], "--only", copies[B]])
    case("two comparable samples: the CLI success path exits 0",
         rc == 0 and "ELIGIBLE" in txt, "rc=%s" % rc)
    rc, txt = run("cut", ["--only", cut])
    case("a section-less sample alone: nothing may be ranked (exit 4)", rc == 4, "rc=%s" % rc)
    rc, txt = run("mix", ["--only", copies[A], "--only", copies[B], "--only", cut])
    case("a section-less member restricts the pooled total (no silent pooling)",
         rc == 4 and "RESTRICTED" in txt, "rc=%s" % rc)
    rc, txt = run("basis", ["--only", copies[A], "--only", copies[B], "--only", bad])
    case("a changed nested damageBasis is caught at the CLI",
         rc == 0 and "damageBasis" in txt, "rc=%s" % rc)
    rc, txt = run("noinput", ["--exports", tmp, "--only", os.path.join(tmp, "nope.json")])
    case("no valid input exits 2", rc == 2, "rc=%s" % rc)

    app = os.path.join(tmp, "app.json")
    io.open(app, "w", encoding="utf-8").write(json.dumps({"exports": [
        {"file": os.path.basename(copies[A]), "crosscheck": {"status": "WARNING"},
         "correctedUnexplainedLeaks": 4}]}))
    jp = os.path.join(tmp, "out.json")
    rc, txt = run("app", ["--only", copies[A], "--only", copies[B], "--applicability", app, "--json", jp])
    j = json.load(io.open(jp, encoding="utf-8"))
    st = {s["file"]: s.get("gateStatus") for s in j["samples"]}
    case("--applicability is wired into the CLI (recorded per sample)",
         st.get(os.path.basename(copies[A])) == "WARNING", str(st))
    shutil.rmtree(tmp, ignore_errors=True)
    print("---- selftest-e2e: %s" % ("PASS" if not fails else "FAIL %s" % fails))
    return 0 if not fails else 1


if __name__ == "__main__":
    sys.exit(main())
