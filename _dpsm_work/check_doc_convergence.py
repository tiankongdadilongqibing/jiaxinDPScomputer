# -*- coding: utf-8 -*-
"""P1-B acceptance guard: the five main documents must not contradict the repository.

NEXT-STEPS.md P1-B acceptance is a CROSS-DOCUMENT spot check: "pick the deployed version, schema version,
export count, 411001/700817/9999 applicability, attackPower status and knownLimits out of the five main
documents and they must agree". Reading them once satisfies that once; this script makes it a command.
It is the P1-B analogue of what P0-A did for the validators: a claim nobody can re-check is not a claim.

THE SOURCES OF TRUTH ARE THE REPOSITORY, NOT THIS FILE:
  version       <- src/BuildInfo.cs
  sha256        <- the DEPLOYED BepInEx/plugins/DpsMeter/DpsMeter.dll (hashed here)
  schema        <- src/Output/Contribution.cs (the literal the plugin writes)
  export count  <- the exports directory
  applicability <- contribution_applicability_report.json (the P0-D scanner output)
  knownLimits   <- src/Output/Contribution.cs vs contrib/report_json.py

RULES (each is a way the documents contradicted the repo before this round):
  R1 each document names the CURRENT version somewhere;
  R2 no document claims an OLDER version is current/deployed/newest. The version must FOLLOW the claim
     verb, so "1.7.6 latest BATTLE" is not a version claim; historical lines are exempt and a line counts
     as historical only if it says so (history/back-then/report-time/dispatch/previously/rollback/...);
  R3 the status documents carry the CURRENT deployed SHA256 prefix;
  R4 nothing still calls the contribution schema 0.1-draft outside a historical line, and the documents
     that state the schema state the current one;
  R5 a CORPUS-TOTAL claim (total marker + N + files, on a line that names the quest breakdown) must be
     the current count; the status documents must carry the current count. Subset counts such as "all 13
     files that carry the section" are not corpus totals and are not flagged;
  R6 the training ground is not_comparable and no document claims it comparable;
  R7 no live document claims the attack-power addends are unsplit;
  R8 the knownLimits list is IDENTICAL in the plugin and the offline core (parsed as JSON, so a parser
     that matches nothing cannot silently pass), and it has the expected 6 items.

Usage: python check_doc_convergence.py [--selftest]   (ASCII output; exit 0 = converged, 1 = not)
"""
from __future__ import print_function
import argparse, glob, hashlib, io, json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
EXPORTS = os.path.join(ROOT, "BepInEx", "plugins", "DpsMeter", "exports")
LIVE_DLL = os.path.join(ROOT, "BepInEx", "plugins", "DpsMeter", "DpsMeter.dll")
CONTRIB_CS = os.path.join(HERE, "src", "Output", "Contribution.cs")
REPORT_JSON = os.path.join(HERE, "contrib", "report_json.py")

DOCS = [
    ("dictionary", os.path.join(HERE, "CONTRIBUTION-DATA-DICTIONARY.md")),
    ("report", os.path.join(HERE, "CONTRIBUTION-TABLE-REPORT.md")),
    ("plan", os.path.join(HERE, "PROJECT-DIRECTION-AND-IMPLEMENTATION-PLAN.md")),
    ("handoff", os.path.join(HERE, "HANDOFF.md")),
    ("index", os.path.join(ROOT, "DpsMeter-\u6587\u6863\u7d22\u5f15.md")),
]
STATUS_DOCS = ("index", "handoff", "report")

HIST = ["\u5386\u53f2", "\u5f53\u65f6", "\u62a5\u544a\u751f\u6210\u65f6", "\u62a5\u544a\u65f6",
        "\u6d3e\u53d1", "\u6b64\u524d", "\u66fe\u7ecf", "\u65f6\u70b9", "\u57fa\u7ebf", "\u524d\u4e00\u7248",
        "\u56de\u9000", "\u5907\u4efd", "\u4f5c\u5e9f", "\u64a4\u56de", "~~", "\u5df2\u6536\u655b",
        "\u65e7", "\u5f52\u6863", "\u9a8c\u6536", "\u5f53\u65f6\u7684"]
# claim verbs about a DEPLOYED VERSION (not "latest battle")
M_OLD_DEPLOY = ["\u5f53\u524d\u90e8\u7f72", "\u5df2\u90e8\u7f72", "\u90e8\u7f72\u4e2d", "\u6700\u65b0\u7248", "\u6700\u65b0\u4e00\u7248"]
M_UNSPLIT = ["\u672a\u62c6", "\u5c1a\u672a\u5b8c\u5168\u62c6\u5206", "\u5c1a\u672a\u5b9e\u73b0"]
M_COUNT = ["\u5171", "\u7d2f\u8ba1", "\u603b\u8ba1", "\u5f53\u524d"]
M_DRAFT = "0.1-draft"
M_EXPECTED_LIMITS = 6


def read(path):
    with io.open(path, "r", encoding="utf-8") as fh:
        return fh.read()


def lines_of(text):
    return text.split("\n")


def is_historical(line):
    return any(m in line for m in HIST)


def version_tuple(v):
    try:
        return tuple(int(x) for x in str(v).split("."))
    except ValueError:
        return (0,)


def known_limits(cs_text, py_text):
    """(csharp list, python list). The C# writes the array inside a JSON string literal, so the segment
    reads [\"a\",\"b\"]; the Python file writes a plain array. Unescape + json.loads is exact, whereas a
    regex over escaped quotes silently matched NOTHING -- a guard that checks nothing is the failure mode
    this whole round is about."""
    def grab(text, key):
        i = text.find(key)
        if i < 0:
            return None
        j = text.find("[", i)
        if j < 0:
            return None
        depth = 0
        for k in range(j, len(text)):
            if text[k] == "[":
                depth += 1
            elif text[k] == "]":
                depth -= 1
                if depth == 0:
                    return text[j:k + 1]
        return None
    def parse(seg):
        if not seg:
            return None
        s = seg.replace(chr(92) + chr(34), chr(34))          # C# writes [\"a\",\"b\"]
        s = re.sub(r",\s*\]$", "]", s)                      # Python allows a trailing comma; JSON does not
        try:
            return json.loads(s)
        except ValueError:
            return None
    return parse(grab(cs_text, "knownLimits")), parse(grab(py_text, "knownLimits"))


def load_truth():
    b = read(os.path.join(HERE, "src", "BuildInfo.cs"))
    m = re.search(r'Version\s*=\s*"([0-9][0-9.]*)"', b)
    version = m.group(1) if m else None
    c = read(CONTRIB_CS)
    m = re.search(r'schemaVersion\\?":\\?"([0-9][0-9.]*)', c)
    schema = m.group(1) if m else None
    sha = None
    if os.path.isfile(LIVE_DLL):
        with open(LIVE_DLL, "rb") as fh:
            sha = hashlib.sha256(fh.read()).hexdigest().upper()
    app = {}
    rp = os.path.join(HERE, "contribution_applicability_report.json")
    if os.path.isfile(rp):
        for e in json.loads(read(rp)).get("exports", []):
            app.setdefault(str(e.get("quest")), set()).add(e.get("modelApplicability"))
    app = dict((k, sorted(v)) for k, v in app.items())
    return {"version": version, "schema": schema, "sha": sha,
            "exports": len(glob.glob(os.path.join(EXPORTS, "battle_*.json"))),
            "applicability": app,
            "knownLimits": known_limits(c, read(REPORT_JSON))}


def audit(docs, truth):
    """docs: {name: text}. Returns [(rule, ok, detail)]. Pure, so --selftest can feed tampered text."""
    out = []
    def add(rule, ok, detail):
        out.append((rule, bool(ok), detail))
    ver = truth["version"]
    # R1
    miss = [n for n, t in docs.items() if ver not in t]
    add("R1 every document names the current version %s" % ver, not miss, "missing in: %s" % miss)
    # R2: the version must follow the claim verb
    bad = []
    for n, t in docs.items():
        for i, l in enumerate(lines_of(t)):
            if is_historical(l):
                continue
            for word in M_OLD_DEPLOY:
                for wm in re.finditer(re.escape(word), l):
                    tail = l[wm.end():wm.end() + 14]
                    m = re.search(r"([0-9]+\.[0-9]+\.[0-9]+)", tail)
                    if m and version_tuple(m.group(1)) < version_tuple(ver):
                        bad.append("%s:%d: %s %s" % (n, i + 1, word, m.group(1)))
    add("R2 no document calls an older version current/deployed/newest", not bad, "; ".join(bad[:4]))
    # R3
    pref = (truth["sha"] or "")[:8]
    miss = [n for n in STATUS_DOCS if pref and pref not in docs.get(n, "")]
    add("R3 status documents carry the deployed SHA256 prefix %s" % pref, not miss, "missing in: %s" % miss)
    # R4
    bad = []
    for n, t in docs.items():
        for i, l in enumerate(lines_of(t)):
            if M_DRAFT in l and not is_historical(l):
                bad.append("%s:%d" % (n, i + 1))
    staters = [n for n, t in docs.items() if "schemaVersion" in t]
    miss = [n for n in staters if truth["schema"] not in docs[n]]
    add("R4 schema is %s wherever it is stated (0.1-draft only on historical lines)" % truth["schema"],
        not bad and not miss, "stale=%s missing=%s" % (bad[:3], miss))
    # R5: corpus totals only (a line that names the quest breakdown)
    bad = []
    pat = "(?:%s)\\s*<?\\**\\s*([0-9]+)\\s*\\u4efd" % "|".join(M_COUNT)
    for n, t in docs.items():
        for i, l in enumerate(lines_of(t)):
            if "411001" not in l:
                continue
            for m in re.finditer(pat, l):
                if int(m.group(1)) != truth["exports"] and not is_historical(l):
                    bad.append("%s:%d says %s" % (n, i + 1, m.group(0)))
    cur = str(truth["exports"])
    miss = [n for n in STATUS_DOCS if not re.search(cur + r"\s*\u4efd", docs.get(n, ""))]
    add("R5 a corpus total (with the quest breakdown) is %s everywhere it is stated" % cur,
        not bad and not miss, "stale=%s missing=%s" % (bad[:4], miss))
    # R6
    bad = []
    for n, t in docs.items():
        for i, l in enumerate(lines_of(t)):
            if "9999" in l and "not_comparable" not in l and re.search(r"full|\u53ef\u4f5c\u57fa\u51c6", l):
                bad.append("%s:%d" % (n, i + 1))
    want = truth["applicability"].get("9999")
    add("R6 the training ground is not_comparable and no document claims it comparable",
        not bad and want == ["not_comparable"],
        "offenders=%s applicability[9999]=%s" % (bad[:3], want))
    # R7
    bad = []
    for n, t in docs.items():
        for i, l in enumerate(lines_of(t)):
            if is_historical(l):
                continue
            if any(w in l for w in M_UNSPLIT) and ("attackPower" in l or "\u52a0\u7b97" in l):
                bad.append("%s:%d" % (n, i + 1))
    add("R7 no live document claims the attack-power addends are unsplit", not bad, "offenders=%s" % bad[:4])
    # R8
    cs, py = truth["knownLimits"]
    same = bool(cs) and cs == py and len(cs) == M_EXPECTED_LIMITS
    add("R8 plugin and offline knownLimits are the same %d strings" % (len(cs) if cs else 0), same,
        "cs=%s py=%s" % (len(cs) if cs else None, len(py) if py else None))
    return out


def selftest():
    truth = load_truth()
    base = dict((n, read(p)) for n, p in DOCS)
    res = audit(base, truth)
    bad = [r for r, ok, _d in res if not ok]
    print("baseline: %d rules, %d failing" % (len(res), len(bad)))
    for r in bad:
        print("   FAILING BASELINE: " + r.encode("ascii", "replace").decode("ascii"))
    ver, sha = truth["version"], (truth["sha"] or "")[:8]
    cases = [
        ("M1 a document forgets the current version", "R1",
         lambda d: d.__setitem__("plan", d["plan"].replace(ver, "1.7.6"))),
        ("M2 a document claims an older version is deployed", "R2",
         lambda d: d.__setitem__("handoff", d["handoff"] + "\n> \u5f53\u524d\u90e8\u7f72\u7248\u672c 1.7.6\n")),
        ("M2b a BATTLE described as latest must NOT trip R2 (negative control)", "R2",
         lambda d: d.__setitem__("plan", d["plan"] + "\n\u7528 1.7.6 \u6700\u65b0\u4e00\u573a\u5b9e\u6d4b\u3002\n")),
        ("M3 the deployed hash disappears from a status document", "R3",
         lambda d: d.__setitem__("index", d["index"].replace(sha, "DEADBEEF"))),
        ("M4 the schema is called 0.1-draft again", "R4",
         lambda d: d.__setitem__("dictionary", d["dictionary"] + "\nschemaVersion 0.1-draft\n")),
        ("M5 a corpus total says 15 files", "R5",
         lambda d: d.__setitem__("report", d["report"] + "\n\u5f53\u524d\u5171 15 \u4efd\u5bfc\u51fa:411001\u00d79\u3002\n")),
        ("M5b a SUBSET count must NOT trip R5 (negative control)", "R5",
         lambda d: d.__setitem__("plan", d["plan"] + "\n\u5168\u90e8 13 \u4efd\u5e26\u6bb5\u5bfc\u51fa\u91cd\u653e\u4e00\u81f4\u3002\n")),
        ("M6 the training ground is called comparable", "R6",
         lambda d: d.__setitem__("plan", d["plan"] + "\n9999 \u8bad\u7ec3\u573a\u53ef\u4f5c\u4e3a full \u57fa\u51c6\u3002\n")),
        ("M7 the unsplit attackPower claim returns", "R7",
         lambda d: d.__setitem__("plan", d["plan"] + "\nattackPower \u7684\u653b\u51fb\u529b\u52a0\u7b97\u672a\u62c6\u3002\n")),
    ]
    fails = 0
    for label, rule, mutate in cases:
        d = dict(base)
        mutate(d)
        got = [r for r, ok, _dd in audit(d, truth) if not ok]
        negative = "negative control" in label
        if negative:
            hit = not any(r.startswith(rule) for r in got)
        else:
            hit = any(r.startswith(rule) for r in got)
        print("  [%s] %s -- %s; failing=%s"
              % ("PASS" if hit else "FAIL", label, rule, [r.split()[0] for r in got]))
        if not hit:
            fails += 1
    # R8 must be falsifiable: diverge the offline list and require R8 to fail
    cs = truth["knownLimits"][0]
    py_bad = read(REPORT_JSON).replace(cs[-1] if cs else "zzz", "DIVERGED")
    py2 = known_limits(read(CONTRIB_CS), py_bad)[1]
    r8bad = not (cs == py2 and len(cs or []) == M_EXPECTED_LIMITS)
    print("  [%s] M8 the offline knownLimits list diverges -- R8 fails" % ("PASS" if r8bad else "FAIL"))
    if not r8bad:
        fails += 1
    print("---- selftest: %s" % ("PASS" if (fails == 0 and not bad) else "FAIL (%d)" % (fails + len(bad))))
    return 0 if (fails == 0 and not bad) else 1


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--selftest", action="store_true")
    a = ap.parse_args()
    if a.selftest:
        return selftest()
    truth = load_truth()
    docs = dict((n, read(p)) for n, p in DOCS)
    res = audit(docs, truth)
    print("doc convergence: version=%s schema=%s exports=%d sha=%s applicability=%s"
          % (truth["version"], truth["schema"], truth["exports"], (truth["sha"] or "?")[:8], truth["applicability"]))
    bad = 0
    for rule, ok, detail in res:
        print("  [%s] %s%s" % ("PASS" if ok else "FAIL", rule, ("  -- " + detail) if (detail and not ok) else ""))
        if not ok:
            bad += 1
    print("---- P1-B doc convergence: %s (%d/%d failing)" % ("PASS" if bad == 0 else "FAIL", bad, len(res)))
    return 0 if bad == 0 else 1


if __name__ == "__main__":
    sys.exit(main())