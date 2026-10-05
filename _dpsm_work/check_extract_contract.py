# -*- coding: utf-8 -*-
"""check_extract_contract.py -- the WRITER (C#) and the CHECKER (Python) of an evidence bundle must agree.

WHY THIS EXISTS (R52 follow-up)
    Diagnostics/EvidenceExtractor.cs writes the bundle and _dpsm_work/extract_verify.py verifies it. They
    are two implementations of ONE contract: a manifest schema, a census schema, a checksum algorithm name,
    a set of file names and a set of JSON keys. Until this tool, nothing offline compared them -- a rename
    on either side would only surface when somebody happened to produce a real bundle and run the verifier,
    which needs the game. That is the exact shape of failure this repository keeps paying for (the export
    writer and the offline core are diffed field by field every batch; the bundle deserves the same).

WHAT IS CHECKED
    1. the three shared CONSTANTS are equal on both sides:
         manifest schema "extract-manifest/1", census schema "contrib-census/1", hash name "fnv1a64";
    2. every KEY the verifier reads exists as an emitted JSON key literal in the C# writer
       (the C# writes them escaped, so the source is unescaped once before the comparison);
    3. the three REQUIRED file names the verifier insists on are produced by the writer.

    This is a CONTRACT check, not a behavioural one: it cannot tell whether a value is right, only that the
    two sides still speak about the same fields. The behavioural half is extract_verify.py.

USAGE
    python _dpsm_work/check_extract_contract.py            # verify
    python _dpsm_work/check_extract_contract.py --selftest # tamper temp copies, expect the right failures
ASCII-only stdout (the console is GBK). Exit: 0 match / 1 mismatch or unusable input.
"""
from __future__ import annotations

import argparse
import io
import os
import re
import shutil
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
WRITER = os.path.join(HERE, "src", "Diagnostics", "EvidenceExtractor.cs")
VERIFIER = os.path.join(HERE, "extract_verify.py")

# ---- the contract, as the VERIFIER reads it -------------------------------------------------------
# Every key below is one the verifier actually looks up (`doc.get("x")` / `f.get("x")`), grouped by the
# object it belongs to so a failure line says WHERE the disagreement is.
MANIFEST_READ = [
    "schema", "check", "reason", "pluginVersion", "quest", "createdAt", "files", "name", "bytes",
    "masterdata", "failures", "census", "groups", "folds", "amount", "assembly", "path",
]
CENSUS_READ = [
    "schema", "foldAccounting", "total", "reasons", "reason", "folds", "unresolved", "kind", "side",
    "origin", "label", "factor", "amount", "victimInstances", "carrierVerdict", "coverage", "unattributed",
]
# Emitted by the writer for a human reader (or for the manifest's own summary); the verifier deliberately
# does not read them. Kept in a separate list so "the verifier stopped reading this" and "the writer
# stopped emitting this" cannot be confused, which is what the first version of this tool got wrong.
MANIFEST_EXTRA = ["seconds", "bundle", "missing", "source", "carrierUnique", "carrierAmbiguous",
                  "carrierNone", "notes", "inputSource"]
CENSUS_EXTRA = ["foldsEnabled", "usable", "zeroFactor", "noopFactor", "subUnity", "negative",
                "analysisHits", "events", "analyzable", "attributed", "creditedShare",
                "reconciliationGap", "unknownAttackerHits", "outsideTeamHits", "ruleName", "victimTop",
                "victimTopFolds", "carrierCount", "carrierNames", "inputSource"]
REQUIRED_FILES = ["manifest.json", "battle.json", "contrib_census.json"]
SCHEMAS = [("MANIFEST_SCHEMA", "extract-manifest/1"), ("CENSUS_SCHEMA", "contrib-census/1")]
HASH_NAME = "fnv1a64"


def read(path):
    with io.open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def unescape_cs(text):
    """The C# writer emits keys as escaped quotes; unescape once so a plain quoted search is meaningful."""
    return text.replace('\\"', '"')


def py_const(text, name):
    m = re.search(re.escape(name) + r'\s*=\s*"([^"]+)"', text)
    return m.group(1) if m else None


def verify(writer_path=WRITER, verifier_path=VERIFIER):
    """Returns a list of ASCII problem lines (empty = the contract holds)."""
    bad = []
    if not os.path.isfile(writer_path):
        return ["writer not found: " + writer_path]
    if not os.path.isfile(verifier_path):
        return ["verifier not found: " + verifier_path]
    cs = unescape_cs(read(writer_path))
    py = read(verifier_path)

    # 1) shared constants
    for _cs_name, literal in SCHEMAS:
        pv = [n for n in ("MANIFEST_SCHEMA", "CENSUS_SCHEMA") if py_const(py, n) == literal]
        if not pv:
            bad.append("the verifier does not declare the schema %s anywhere" % literal)
        if ('"' + literal + '"') not in cs:
            bad.append("the writer does not emit the schema literal %s" % literal)
    hv = py_const(py, "HASH_NAME")
    if hv != HASH_NAME:
        bad.append("the verifier declares hash name %r, expected %r" % (hv, HASH_NAME))
    if ('"' + HASH_NAME + '"') not in cs:
        bad.append("the writer does not name the checksum algorithm %r" % HASH_NAME)

    # 2) every key the verifier READS must exist as an emitted literal in the writer.
    # Direction only. The reverse direction ("the verifier still mentions it") was tried and REMOVED:
    # extract_verify.py's own --selftest builds synthetic bundles whose dict literals contain the same key
    # names, so a reader that stopped reading a key still "mentions" it -- the check would have been
    # vacuous while looking strict. What holds this list honest is that it is short, grouped, and that the
    # verifier's reader site is one function; the constants below ARE compared in both directions.
    for label, keys in (("manifest", MANIFEST_READ), ("census", CENSUS_READ)):
        for k in keys:
            if ('"' + k + '"') not in cs:
                bad.append("the writer never emits the %s key %r (the verifier reads it)" % (label, k))
    # 2b) the writer's EXTRA keys must exist and must not overlap the read set
    for label, extra, readset in (("manifest", MANIFEST_EXTRA, MANIFEST_READ),
                                  ("census", CENSUS_EXTRA, CENSUS_READ)):
        for k in extra:
            if ('"' + k + '"') not in cs:
                bad.append("the writer no longer emits the %s key %r" % (label, k))
            if k in readset:
                bad.append("the %s key %r is listed both as READ and as EXTRA" % (label, k))

    # 3) the file names the verifier requires
    for f in REQUIRED_FILES:
        if ('"' + f + '"') not in cs:
            bad.append("the writer never produces the file %r (the verifier requires it)" % f)

    # 4) the masterdata copy: the verifier counts *.json under <bundle>/masterdata
    if '"masterdata"' not in cs:
        bad.append("the writer never creates the masterdata/ directory the verifier counts")
    return bad


# ---------------------------------------------------------------------------------------------

def _tmp_copies(tmp, cs_edits=None, py_edits=None):
    d = tempfile.mkdtemp(prefix="extract_contract_", dir=tmp)
    w = os.path.join(d, "EvidenceExtractor.cs")
    v = os.path.join(d, "extract_verify.py")
    cs = read(WRITER)
    py = read(VERIFIER)
    # An edit is (old, new[, "all"]). "one" (the default) requires EXACTLY one occurrence, so a rename
    # that silently stops matching is a driver failure rather than a passing control; "all" is for a
    # literal that legitimately appears at more than one site (a file name written and also listed).
    for edit in (cs_edits or []):
        old, new = edit[0], edit[1]
        mode = edit[2] if len(edit) > 2 else "one"
        n = cs.count(old)
        if (mode == "one" and n != 1) or (mode == "all" and n < 1):
            raise AssertionError("selftest edit target occurs %d times (mode=%s): %r" % (n, mode, old))
        cs = cs.replace(old, new)
    for edit in (py_edits or []):
        old, new = edit[0], edit[1]
        mode = edit[2] if len(edit) > 2 else "one"
        n = py.count(old)
        if (mode == "one" and n != 1) or (mode == "all" and n < 1):
            raise AssertionError("selftest edit target occurs %d times (mode=%s): %r" % (n, mode, old))
        py = py.replace(old, new)
    with io.open(w, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(cs)
    with io.open(v, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(py)
    return w, v


def selftest():
    tmp = tempfile.mkdtemp(prefix="extract_contract_")
    fails = []
    try:
        cases = [
            ("clean", [], [], False),
            # a renamed census key must be reported, with the key named
            ("renamed-key", [('\\"factor\\":', '\\"factorX\\":')], [], True),
            # a changed schema constant on the writer side
            ("schema-drift", [('internal const string ManifestSchema = "extract-manifest/1";',
                               'internal const string ManifestSchema = "extract-manifest/2";')], [], True),
            # a changed hash name on the writer side
            ("hash-drift", [('internal const string HashName = "fnv1a64";',
                             'internal const string HashName = "sha256";')], [], True),
            # a renamed output file
            ("file-drift", [('"contrib_census.json"', '"census.json"', "all")], [], True),
            # a changed schema constant on the VERIFIER side (the constants are compared both ways)
            ("verifier-schema-drift", [], [('MANIFEST_SCHEMA = "extract-manifest/1"',
                                            'MANIFEST_SCHEMA = "extract-manifest/2"')], True),
            # a changed hash name on the VERIFIER side
            ("verifier-hash-drift", [], [('HASH_NAME = "fnv1a64"', 'HASH_NAME = "sha256"')], True),
        ]
        for name, ce, pe, want_bad in cases:
            w, v = _tmp_copies(tmp, ce, pe)
            bad = verify(w, v)
            if bool(bad) != want_bad:
                fails.append("%s: want problems=%s got %s" % (name, want_bad, bad[:2]))
        w, v = _tmp_copies(tmp)
        if verify(w, v):
            fails.append("the real writer/verifier pair does not satisfy the contract: %s" % verify(w, v)[:2])
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    for f in fails:
        print("SELFTEST FAIL: " + f)
    print("SELFTEST %s (%d case(s))" % ("PASS" if not fails else "FAIL", 7))
    return 0 if not fails else 1


def main(argv=None):
    ap = argparse.ArgumentParser(description="The bundle writer and its verifier must agree on the contract.")
    ap.add_argument("--selftest", action="store_true")
    ap.add_argument("--writer", default=WRITER)
    ap.add_argument("--verifier", default=VERIFIER)
    a = ap.parse_args(argv)
    if a.selftest:
        return selftest()
    bad = verify(a.writer, a.verifier)
    print("extract contract: read=%d extra=%d schemas=%d files=%d"
          % (len(MANIFEST_READ) + len(CENSUS_READ), len(MANIFEST_EXTRA) + len(CENSUS_EXTRA),
             len(SCHEMAS), len(REQUIRED_FILES)))
    for b in bad:
        print("  FAIL " + b)
    print("---- extract contract: " + ("PASS" if not bad else ("FAIL %d" % len(bad))))
    return 0 if not bad else 1


if __name__ == "__main__":
    sys.exit(main())
