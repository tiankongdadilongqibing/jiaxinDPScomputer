# -*- coding: utf-8 -*-
"""N0 acceptance closure for the CONTRIBUTION-NEXT-PHASE-ROADMAP.md (section N0).

Why this exists: the roadmap's N0 asks for a REPRODUCIBLE acceptance record of the currently deployed
version instead of "the deployed DLL is X" plus a 29-file snapshot from an earlier round. Everything
this script writes is bound to input hashes (every export, the deployed DLL, the live config, the guard
scripts themselves) and to the exact command lines, so a reader can re-run it and diff.

Outputs (all inside an INDEPENDENT PER-BATCH directory, default _dpsm_work/acceptance_rf0; the round-6
archive stays untouched in acceptance_1.7.11, and that is enforced -- see output_isolation below):
  corpus_manifest.json   per-file sha256/size/version/quest/section/schema + observed verdicts
  runs.json              every command, its exit code, and where its raw stdout/stderr was kept
  RESULTS.md             the human summary: four status columns, expected-vs-observed, gaps

Two checks keep the batch honest about its INPUTS and its SIDE EFFECTS (RF0 sections 5.4 and 5.5):
  input_freeze      the export set must be EXACTLY the frozen list below. Two battles were played after
                    the round-6 archive closed, so those two files start a NEW input snapshot rather
                    than silently changing the old baseline: a file appearing or disappearing mid-batch
                    is a red check, not a new total.
  output_isolation  a batch directory is only independent if nothing else moves. Every file these runs
                    rewrite outside --out must be on the declared CURRENT-OUTPUT list, and the historical
                    archive directories are watched and must not change at all.

ASCII-only stdout (this console is GBK). Child stdout goes to FILES, never through a pipe.

Usage: python n0_acceptance.py [--exports DIR] [--out DIR] [--log PATH] [--skip-heavy] [--quick]
"""
from __future__ import print_function
import argparse, hashlib, io, json, os, re, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
PY = sys.executable
DEF_EXPORTS = os.path.join(ROOT, "BepInEx", "plugins", "DpsMeter", "exports")
# The export directory is written by the RUNNING game (a battle finished at 18:16 while this batch was
# being prepared). A batch therefore reads the frozen hard-linked snapshot when one exists, so the input
# set cannot change under it.
DEF_INPUTS = os.path.join(HERE, "batch_inputs", "rf0")
DEF_DLL = os.path.join(ROOT, "BepInEx", "plugins", "DpsMeter", "DpsMeter.dll")
DEF_CFG = os.path.join(ROOT, "BepInEx", "config", "dev.dpsmeter.cfg")
DEF_LOG = os.path.join(ROOT, "BepInEx", "LogOutput.log")
DOTNET = r"D:\dmmplayer\dotnet-sdk6\dotnet.exe"

# The snapshot the previous round recorded over 29 files (SESSION-STATE 7.2.99 / TABLE-REPORT 9.9.4).
# N0 is only meaningful if the 30-file run can be PREDICTED from it plus the one new file. A mismatch is
# a finding, not something to explain away.
# The 29-file snapshot the previous round recorded (SESSION-STATE 7.2.99 / TABLE-REPORT 9.9.4), expressed
# PER TOOL because the two tools do not share a vocabulary. N0 is only meaningful if the 30-file run can be
# PREDICTED from it plus the one new file; a mismatch is a finding, not something to explain away.
PREV_SNAPSHOT = {"files": 29,
                 "crosscheck_batch": {"ERROR": 1, "LEGACY_NOT_APPLICABLE": 14, "PASS": 10, "WARNING": 3},
                 "applicability": {"full": 12, "partial": 9, "not_comparable": 8},
                 "as_of": "1.7.10 release (SESSION-STATE 7.2.99)"}
# The next verified snapshot: the 30-file run of round 5 over the deployed 1.7.11.
SNAPSHOT_30 = {"files": 30,
               "crosscheck_batch": {"ERROR": 1, "LEGACY_NOT_APPLICABLE": 15, "PASS": 11, "WARNING": 3},
               "crosscheck_embedded": {"NOT_RUN": 13, "LEGACY_NOT_APPLICABLE": 2, "PASS": 11,
                                       "WARNING": 3, "ERROR": 1},
               "applicability": {"full": 13, "partial": 9, "not_comparable": 8},
               "as_of": "round 5 N0 run (acceptance_1.7.11/corpus_manifest.json, 14/14 checks)"}
# The 32-file snapshot of round 6, verified in acceptance_1.7.11 (51/51 checks). Kept as the derivation
# base for the current pins, and as a record that a batch is always described by the batch it ran in.
SNAPSHOT_32 = {"files": 32,
               "crosscheck_batch": {"ERROR": 1, "LEGACY_NOT_APPLICABLE": 16, "PASS": 12, "WARNING": 3},
               "crosscheck_embedded": {"NOT_RUN": 13, "LEGACY_NOT_APPLICABLE": 3, "PASS": 12,
                                       "WARNING": 3, "ERROR": 1},
               "applicability": {"full": 14, "partial": 9, "not_comparable": 9},
               "as_of": "round 6 run (acceptance_1.7.11/RESULTS.md, 51/51 checks)"}
# Per-file pins. EVERY entry here is compared in write_out(): an aggregate bucket that still adds up while
# two files swap PASS<->WARNING must go red. The older pins are kept as history and are re-checked too.
# The two files below (17:59 and 18:03, both 1.7.11, played after the round-6 archive closed) are pinned
# from SNAPSHOT_32 plus one explicit rule each -- written BEFORE this batch's acceptance run:
#   * battle_411001_20261004_180353.json: version 1.7.11, quest 411001, schema 1.1, same deployed DLL as
#     the other three 1.7.11 samples => predict batch PASS + applicability full.
#   * battle_9999_20261004_175957.json: quest 9999 is the training ground; every 9999 sample that carries
#     a contribution section (042458 = 1.7.5/1.0, 135214 = 1.7.8/1.1, 165952 = 1.7.11/1.1) was
#     LEGACY_NOT_APPLICABLE for `crosscheck --batch` and not_comparable for the applicability report
#     => predict the same pair.
NEW_FILES_EXPECTED = {
    "battle_411001_20261004_144548.json": {"crosscheck": "PASS", "applicability": "full",
                                           "why": "first 1.7.10 real battle: PASS + full is the N0 sample verdict"},
    "battle_411001_20261004_170157.json": {"crosscheck": "PASS", "applicability": "full",
                                           "why": "1.7.11 normal 411001 with schema 1.1, same DLL as 144548"},
    "battle_9999_20261004_165952.json": {"crosscheck": "LEGACY_NOT_APPLICABLE", "applicability": "not_comparable",
                                         "why": "training quest: excluded from crosscheck, not comparable"},
    "battle_411001_20261004_180353.json": {"crosscheck": "PASS", "applicability": "full",
                                           "why": "1.7.11 normal 411001, schema 1.1, same DLL as 170157"},
    "battle_9999_20261004_175957.json": {"crosscheck": "LEGACY_NOT_APPLICABLE", "applicability": "not_comparable",
                                         "why": "training quest, same rule as the other three 9999 samples"},
    "battle_411001_20261004_180834.json": {"crosscheck": "PASS", "applicability": "full",
                                           "why": "1.7.11 normal 411001, schema 1.1, same DLL as 180353"},
}
# EXPECT_* = SNAPSHOT_32 + the pinned verdict of the THREE files that are NOT part of it. 180834 was
# written at 18:16, i.e. while this batch was already being prepared, and is the reason the batch runs
# against a frozen hard-linked input snapshot instead of the live directory (see FROZEN_MANIFEST).
#   175957 -> batch LEGACY (+1), embedded LEGACY (+1), app not_comparable (+1)
#   180353 -> batch PASS   (+1), embedded PASS   (+1), app full           (+1)
#   180834 -> batch PASS   (+1), embedded PASS   (+1), app full           (+1)
EXPECT_BATCH = {"ERROR": 1, "LEGACY_NOT_APPLICABLE": 17, "PASS": 14, "WARNING": 3}
# applicability.json reports its OWN embedded crosscheck status per file; it says NOT_RUN (not LEGACY) for a
# file with no contribution section, so its histogram is a different one. Both are recorded and the
# divergence is checked as an identity -- a silent vocabulary split is exactly what confuses a reader.
# NOT_RUN stays 13: both new files DO carry the section, so neither is added to that bucket.
EXPECT_EMBEDDED = {"NOT_RUN": 13, "LEGACY_NOT_APPLICABLE": 4, "PASS": 14, "WARNING": 3, "ERROR": 1}
EXPECT_APP = {"full": 16, "partial": 9, "not_comparable": 10}
# A guard run that exits non-zero must FAIL the acceptance. Before this, RESULTS.md printed the exit code
# and nothing went red, so a guard could start failing and the run would still read "checks: N ok".
# Exactly one run is allowed to be non-zero, and the allowance is named with its reason; a stale entry
# (a name that no run reports) is itself a red check.
ALLOWED_NONZERO = {"crosscheck(batch)": (1,
                   "the pinned 1.6.0 sample 015919 is KNOWN_BAD, so `crosscheck --batch` exits 1 by design")}
# The 1.6.0 sample is 015919, NOT 235204: 235204 is version 1.5.5 and is the 1.5.5 GOLDEN sample
# (dictionary section 10). The first version of this line named 235204 and the falsifiable check caught it --
# which is the whole point of pinning an expectation instead of "explaining" the observed error.
# Corroborated by CONTRIBUTION-TABLE-REPORT.md:387, P0-A-VALIDATION-GATE-REPORT.md:87/318,
# CONTRIBUTION-APPLICABILITY-P0D.md:60 and export_schema_report.txt.
KNOWN_BAD = {"battle_411001_20261004_015919.json": "the 1.6.0 sample (58 factor-truncation mismatches): its crosscheck ERROR is EXPECTED (roadmap 2.2)"}
LABELING_DIVERGENCE = ("a file with no contribution section is LEGACY_NOT_APPLICABLE to `crosscheck --batch` "
                       "and NOT_RUN to the applicability report: same fact, two words. Tracked by N1 (an absent "
                       "section must not pass as legacy-ok).")
# RF0 section 5.5: the batch's INPUT LIST comes from a FROZEN SNAPSHOT (batch_snapshot.py), not from the
# export directory. The directory is LIVE: during the first RF0 attempt the running game appended a battle
# in the middle of the batch and moved every corpus total, histogram and crosscheck bucket. Counts alone
# would not catch that either -- they still add up when one file is replaced by another. The manifest is
# committed (batch-inputs-rf0.json); the hard-linked JSON files are not, and do not need to be.
FROZEN_MANIFEST = os.path.join(HERE, "batch-inputs-rf0.json")


def load_frozen(path):
    """(sorted file names, where they came from). An EMPTY list is a red check, never a skip: a batch
    that cannot name its inputs is not a baseline."""
    if path and os.path.isfile(path):
        try:
            m = json.load(io.open(path, encoding="utf-8"))
        except Exception as ex:
            return [], "unreadable manifest %s (%r)" % (path, ex)
        return (sorted(e["file"] for e in (m.get("files") or [])),
                os.path.relpath(path, ROOT).replace(os.sep, "/"))
    return [], "no manifest at %s" % path


# Paths a run must not touch, and how deep to look: (dir, 1 = files directly inside only, -1 = recursive).
WATCH = [(HERE, 1),
         (ROOT, 1),
         (os.path.join(HERE, "acceptance_1.7.11"), -1),
         (os.path.join(HERE, "contrib", "reports"), -1)]
# Files a guard run legitimately rewrites at a FIXED path (i.e. NOT through --out). Every entry needs a
# reason, and the list is filled from OBSERVED behaviour rather than from reading the guards: an
# undeclared change is a red check, so this is the record of what was actually isolated.
ALLOWED_CURRENT_OUTPUTS = {
    # Keys are REPO-relative (the same space changed_paths() reports), which the first run caught: the
    # first version used script-relative names and every one of them was reported as undeclared.
    "_dpsm_work/export_schema_selftest.txt": "check_export_schema.py --selftest spills its transcript next "
                                            "to the script; the RUN report is redirected via --report",
    "_dpsm_work/fact_signature_selftest.txt": "check_fact_signature.py --selftest spills its transcript "
                                             "next to the script; the RUN report goes to --outdir",
    "_dpsm_work/v150_validate.txt": "v150_validate.py writes its report to a fixed path next to the "
                                    "script (this pipeline has no --out for it yet)",
}
def sha256_file(path, chunk=1 << 20):
    h = hashlib.sha256()
    with io.open(path, "rb") as fh:
        while True:
            b = fh.read(chunk)
            if not b:
                break
            h.update(b)
    return h.hexdigest().upper()


def _rel(p):
    return os.path.relpath(p, ROOT).replace(os.sep, "/")


def snapshot_roots():
    """(repo-relative path -> (size, mtime_ns)) for every file a run must not rewrite.

    size+mtime rather than a hash: this is a "did anything touch it" tripwire and it has to stay cheap
    enough to take twice per batch over the repo root. A rewrite with identical content still trips it,
    which is the fail-safe direction."""
    out = {}
    for base, depth in WATCH:
        if not os.path.isdir(base):
            continue
        for r, dirs, fns in os.walk(base):
            for fn in fns:
                p = os.path.join(r, fn)
                try:
                    st = os.stat(p)
                except Exception:
                    continue
                out[_rel(p)] = (st.st_size, st.st_mtime_ns)
            if depth == 1:
                dirs[:] = []
    return out


def changed_paths(before, after):
    keys = set(list(before) + list(after))
    return sorted(k for k in keys if before.get(k) != after.get(k))


def input_freeze_checks(corpus, frozen):
    """Two rows: the COUNT and the SET. A count alone cannot see a swap, so both are compared."""
    names = set(e["file"] for e in corpus)
    want = set(frozen)
    missing = sorted(want - names)
    extra = sorted(names - want)
    return [
        {"kind": "input_freeze", "bucket": "file count", "expected": len(want), "observed": len(names),
         "ok": len(want) == len(names)},
        {"kind": "input_freeze", "bucket": "file set", "expected": "0 missing / 0 extra",
         "observed": "%d missing / %d extra%s" % (len(missing), len(extra),
                                                  ("  " + ", ".join((missing + extra)[:4])) if (missing or extra) else ""),
         "ok": not missing and not extra},
    ]


def isolation_checks(changed, allowed, outdir):
    """One aggregate row plus one informational row per declared fixed-path output."""
    outrel = _rel(os.path.abspath(outdir))
    undeclared = [p for p in changed
                  if not p.startswith(outrel + "/") and p not in allowed]
    checks = [{"kind": "output_isolation", "bucket": "changes outside --out", "expected": "0 undeclared",
               "observed": ("%d undeclared: %s" % (len(undeclared), ", ".join(undeclared[:6]))) if undeclared else "0 undeclared",
               "ok": not undeclared}]
    for p in sorted(set(changed) & set(allowed)):
        checks.append({"kind": "current_output", "bucket": p, "expected": "declared", "observed": "written",
                       "ok": True, "why": allowed[p]})
    return checks


def read_head_tail(path, head_bytes=4096, tail_bytes=400000):
    size = os.path.getsize(path)
    with io.open(path, "rb") as fh:
        head = fh.read(head_bytes)
        tail = b""
        if size > head_bytes:
            fh.seek(max(0, size - tail_bytes))
            tail = fh.read()
    return head.decode("utf-8", "replace"), tail.decode("utf-8", "replace")


def git_free_code_hash(paths):
    out = {}
    for p in paths:
        if os.path.isfile(p):
            out[os.path.relpath(p, ROOT).replace("\\", "/")] = sha256_file(p)
    return out


def collect_corpus(exports_dir):
    files = sorted(f for f in os.listdir(exports_dir) if f.startswith("battle_") and f.endswith(".json"))
    entries = []
    for f in files:
        p = os.path.join(exports_dir, f)
        head, tail = read_head_tail(p)
        v = re.search(r'"version"\s*:\s*"([0-9.]+)"', head)
        q = re.search(r'"quest"\s*:\s*(\d+)', head)
        section = '"contribution":{' in tail
        sv = re.search(r'"schemaVersion"\s*:\s*"([^"]+)"', tail)
        entries.append({"file": f, "size": os.path.getsize(p), "sha256": sha256_file(p),
                        "version": v.group(1) if v else None, "quest": int(q.group(1)) if q else None,
                        "hasContributionSection": section,
                        "contributionSchemaVersion": sv.group(1) if (section and sv) else None})
    return entries


class Runner(object):
    def __init__(self, outdir):
        self.outdir = outdir
        self.runs = []
        self.logdir = os.path.join(outdir, "runs")
        if not os.path.isdir(self.logdir):
            os.makedirs(self.logdir)

    def run(self, name, argv, cwd=None, timeout=1800):
        safe = re.sub(r"[^A-Za-z0-9_.-]", "_", name)
        outp = os.path.join(self.logdir, safe + ".txt")
        t0 = time.time()
        with io.open(outp, "w", encoding="utf-8") as fh:
            fh.write("# cwd=%s\n# argv=%s\n" % (cwd or os.getcwd(), " ".join(argv)))
            fh.flush()
            try:
                rc = subprocess.call(argv, cwd=cwd, stdout=fh, stderr=subprocess.STDOUT, timeout=timeout)
            except Exception as ex:
                fh.write("\n# EXCEPTION: %r\n" % (ex,))
                rc = -1
        dt = time.time() - t0
        text = io.open(outp, encoding="utf-8", errors="replace").read()
        tail = "\n".join([l for l in text.splitlines()[-6:]])
        rec = {"name": name, "argv": argv, "cwd": cwd or os.getcwd(), "exit": rc,
               "seconds": round(dt, 2), "log": os.path.relpath(outp, ROOT).replace("\\", "/"), "tail": tail}
        self.runs.append(rec)
        print("[%s] exit=%s  %s" % (name, rc, ("(%.1fs)" % dt)))
        return rec, text


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--exports", default=DEF_INPUTS if os.path.isdir(DEF_INPUTS) else DEF_EXPORTS)
    # Per-batch output directory. The default names the CURRENT batch, so a plain run reproduces the
    # archive the documents describe; the round-6 archive stays frozen in acceptance_1.7.11 (watched).
    ap.add_argument("--out", default=os.path.join(HERE, "acceptance_r41"))
    ap.add_argument("--dll", default=DEF_DLL)
    ap.add_argument("--cfg", default=DEF_CFG)
    ap.add_argument("--log", default=DEF_LOG)
    ap.add_argument("--frozen", default=FROZEN_MANIFEST,
                    help="batch snapshot manifest = the frozen input list (default batch-inputs-rf0.json)")
    ap.add_argument("--skip-heavy", action="store_true", help="skip recon_probe and the batch crosscheck")
    ap.add_argument("--quick", action="store_true", help="corpus + build hashes only (no guard runs)")
    ap.add_argument("--selftest", action="store_true", help="negative control for the run_exit checks only")
    args = ap.parse_args()
    if args.selftest:
        return selftest()
    if not os.path.isdir(args.out):
        os.makedirs(args.out)
    exports_dir = os.path.abspath(args.exports)
    corpus = collect_corpus(exports_dir)
    frozen, frozen_source = load_frozen(args.frozen)
    print("corpus files=%d  frozen list=%d (%s)" % (len(corpus), len(frozen), frozen_source))

    buildinfo = os.path.join(HERE, "src", "BuildInfo.cs")
    src_version = None
    if os.path.isfile(buildinfo):
        m = re.search(r'Version\s*=\s*"([0-9.]+)"', io.open(buildinfo, encoding="utf-8").read())
        src_version = m.group(1) if m else None
    dll_sha = sha256_file(args.dll) if os.path.isfile(args.dll) else None
    cfg_sha = sha256_file(args.cfg) if os.path.isfile(args.cfg) else None
    cfg_switches = {}
    if os.path.isfile(args.cfg):
        for line in io.open(args.cfg, encoding="utf-8", errors="replace").read().splitlines():
            m = re.match(r"\s*([A-Za-z0-9_]+)\s*=\s*(true|false)\s*$", line)
            if m:
                cfg_switches[m.group(1)] = m.group(2)
    tool_files = ["n0_acceptance.py", "check_export_schema.py", "check_contribution_layout.py",
                  "check_live_log.py", "check_doc_convergence.py", "check_docs_123.py",
                  "check_given_fold_coupling.py", "check_fact_signature.py", "check_p2a_summary_and_lastbattle.py",
                  "contribution_applicability.py", "pairtrusted_impact.py", "refactor_final_check.py",
                  "contribution_gate.py", "contrib/crosscheck.py", "contrib/compare.py",
                  "repo_manifest.py", "tests/negative_control.py"]
    tool_files += ["tests/BehaviorTests/" + n for n in
                   ("BehaviorTests.csproj", "Program.cs", "Runner.cs", "Stubs.cs", "Cases.Clock.cs",
                    "Cases.HitWindow.cs", "Cases.SessionState.cs", "Cases.Series.cs", "Cases.Cache.cs",
                    "Cases.Tiered.cs")]
    tools = git_free_code_hash([os.path.join(HERE, t) for t in tool_files])

    meta = {
        "tool": "n0_acceptance", "schema": "n0-1", "generated": time.strftime("%Y-%m-%dT%H:%M:%S"),
        "exports_dir": exports_dir, "export_count": len(corpus),
        "source_version_BuildInfo": src_version, "deployed_dll": os.path.relpath(args.dll, ROOT).replace("\\", "/"),
        "deployed_dll_sha256": dll_sha, "config": os.path.relpath(args.cfg, ROOT).replace("\\", "/"),
        "config_sha256": cfg_sha, "config_switches": cfg_switches, "tool_sha256": tools,
        "frozen_input": {"source": frozen_source, "count": len(frozen)}, "python": PY,
        "notes": ["logs are associated only when the log names the export path",
                              "no historical export, DLL, config or evidence file is modified by this script"],
    }

    # Taken BEFORE the first child process: everything these runs write outside --out shows up as a
    # change, and the historical archives must show up as NO change at all.
    before = snapshot_roots()
    runner = Runner(args.out)
    if args.quick:
        write_out(args.out, meta, corpus, runner.runs, {}, {}, checks_enabled=False, frozen=frozen)
        print("quick mode: wrote corpus + hashes (no guard runs, no verdicts)")
        return 0

    names = [e["file"] for e in corpus]
    paths = [os.path.join(exports_dir, n) for n in names]

    app_json = os.path.join(args.out, "applicability.json")
    runner.run("applicability(all)", [PY, os.path.join(HERE, "contribution_applicability.py"),
                                      "--exports", exports_dir, "--json", app_json], cwd=HERE)
    cc_dir = os.path.join(args.out, "crosscheck")
    if not args.skip_heavy:
        # An ABSOLUTE glob: contrib/crosscheck.py resolves a bare pattern against the LIVE export dir,
        # which grows while the game runs.
        runner.run("crosscheck(batch)", [PY, "-m", "contrib.crosscheck",
                                         "--batch", os.path.join(exports_dir, "battle_*.json"),
                                         "--outdir", cc_dir], cwd=HERE)
    runner.run("pairtrusted(all)", [PY, os.path.join(HERE, "pairtrusted_impact.py"), "--files"] + names +
               ["--out-json", os.path.join(args.out, "pairtrusted_impact_report.json"),
                "--out-txt", os.path.join(args.out, "pairtrusted_impact_report.txt")], cwd=HERE)
    # Output isolation (RF0 section 5.4). Both of these guards default to a FIXED report path inside
    # _dpsm_work, so a batch run would rewrite files outside --out and the previous archive could be
    # confused with the new one. Both accept a destination, so the batch writes its own copy and the
    # fixed path is left to standalone use.
    runner.run("export_schema(all)", [PY, os.path.join(HERE, "check_export_schema.py"),
                                      "--report", os.path.join(args.out, "export_schema_report.txt")] + paths,
               cwd=HERE)
    runner.run("layout(all)", [PY, os.path.join(HERE, "check_contribution_layout.py"), "--dir", exports_dir], cwd=HERE)
    if os.path.isfile(args.log):
        runner.run("live_log", [PY, os.path.join(HERE, "check_live_log.py"), "--log", args.log], cwd=HERE)
    else:
        runner.run("live_log(missing)", [PY, "-c", "print('no log at %s')" % args.log], cwd=HERE)
    for nm, argv in [
            ("selftest/layout", [PY, os.path.join(HERE, "check_contribution_layout.py"), "--selftest"]),
            ("selftest/schema", [PY, os.path.join(HERE, "check_export_schema.py"), "--selftest"]),
            ("selftest/live_log", [PY, os.path.join(HERE, "check_live_log.py"), "--selftest"]),
            # Both point at the BATCH inputs: the doc totals must describe what this batch ran on, and the
            # fixed-path applicability report is stale by construction (n0 writes --json into --out).
            ("doc_convergence", [PY, os.path.join(HERE, "check_doc_convergence.py"),
                                "--applicability", os.path.join(args.out, "applicability.json"),
                                "--exports", exports_dir]),
            ("selftest/doc_convergence", [PY, os.path.join(HERE, "check_doc_convergence.py"), "--selftest",
                                          "--applicability", os.path.join(args.out, "applicability.json"),
                                          "--exports", exports_dir]),
            ("docs123", [PY, os.path.join(HERE, "check_docs_123.py")]),
            ("selftest/docs123", [PY, os.path.join(HERE, "check_docs_123.py"), "--selftest"]),
            ("test_gate", [PY, "-m", "contrib.tests.test_gate"]),
            ("given_coupling", [PY, os.path.join(HERE, "check_given_fold_coupling.py")]),
            ("selftest/given_coupling", [PY, os.path.join(HERE, "check_given_fold_coupling.py"), "--selftest"]),
            ("p2a", [PY, os.path.join(HERE, "check_p2a_summary_and_lastbattle.py")]),
            ("factsig", [PY, os.path.join(HERE, "check_fact_signature.py"), "--outdir", args.out]),
            ("selftest/applicability", [PY, os.path.join(HERE, "contribution_applicability.py"), "--selftest"]),
            ("selftest/pairtrusted", [PY, os.path.join(HERE, "pairtrusted_impact.py"), "--selftest"]),
            # R52: the two offline halves of the evidence-extraction flow. Both are self-contained (temp
            # fixtures only) and each has a --selftest that must be able to say NO: attribution_census.py
            # recomputes an export's unattributed split and diffs it against the plugin's own statement;
            # extract_verify.py checks a bundle's manifest checksums and cross-diffs its census against an
            # independent recomputation of the bundle's own battle.json.
            ("selftest/attribution_census", [PY, os.path.join(HERE, "attribution_census.py"), "--selftest"]),
            ("selftest/extract_verify", [PY, os.path.join(HERE, "extract_verify.py"), "--selftest"]),
            # R52: the negative-control DRIVER's own selftest. It has to be in the pipeline because the
            # driver's counter was the defect it now checks: a mutation that applied, compiled and changed
            # nothing used to be counted as a pass (see negative_control.py one()).
            ("selftest/negctl_driver", [PY, os.path.join(HERE, "tests", "negative_control.py"), "--selftest"]),
            # R52 follow-up: the bundle's WRITER (EvidenceExtractor.cs) and its CHECKER (extract_verify.py)
            # must still agree on the schema constants, the file names and every key the verifier reads.
            # Without this the pair could drift silently until somebody produces a real bundle (needs the game).
            ("extract_contract", [PY, os.path.join(HERE, "check_extract_contract.py")]),
            ("selftest/extract_contract", [PY, os.path.join(HERE, "check_extract_contract.py"), "--selftest"]),
            ("refactor_final_check", [PY, os.path.join(HERE, "refactor_final_check.py")]),
            ("selftest/refactor", [PY, os.path.join(HERE, "refactor_final_check.py"), "--selftest"]),
            # RF7: the tool registry. The verify run is the drift check (a script the pipeline runs but the
            # registry does not call active, or an unclassified script added without a pin update, is red);
            # the selftest tampers a temp copy for each of the six checks A-F.
            ("tool_registry", [PY, os.path.join(HERE, "check_tool_registry.py")]),
            ("selftest/tool_registry", [PY, os.path.join(HERE, "check_tool_registry.py"), "--selftest"]),
    # RF7d: the evidence hash index. It verifies the 56 recorded evidence files; its selftest tampering is on
    # TEMP copies, so it cannot touch the real trees and is safe to run inside the pipeline.
    ("archive_index", [PY, os.path.join(HERE, "archive_index.py")]),
    ("selftest/archive_index", [PY, os.path.join(HERE, "archive_index.py"), "--selftest"]),
            ("v150_validate", [PY, os.path.join(HERE, "v150_validate.py"), "--exports", exports_dir]),
            ("selftest/v150", [PY, os.path.join(HERE, "v150_validate.py"), "--selftest"]),
            ("selftest/eligibility", [PY, os.path.join(HERE, "comparison_eligibility.py"), "--selftest"]),
            ("selftest/eligibility_e2e", [PY, os.path.join(HERE, "comparison_eligibility.py"), "--selftest-e2e"]),
            ("selftest/compare", [PY, "-m", "contrib.compare", "--selftest"]),
            ("selftest/decision", [PY, os.path.join(HERE, "decision_report.py"), "--selftest"]),
            # Both read the export directory themselves: without --exports they would take the newest
            # file from a directory the running game appends to (measured 2026-10-04: a 19:59 battle made
            # v150_validate exit 1 and budget_census's real-file control fail). The batch judges its INPUT.
            ("selftest/budget", [PY, os.path.join(HERE, "budget_census.py"), "--selftest",
                                 "--exports", exports_dir]),
            ("repo_manifest_verify", [PY, os.path.join(HERE, "repo_manifest.py"), "--verify",
                                      "--exports", exports_dir])]:
        runner.run(nm, argv, cwd=HERE)
    if not args.skip_heavy and os.path.isfile(DOTNET):
        runner.run("recon_probe", [DOTNET, "run", "--project", os.path.join(HERE, "recon_probe", "ReconProbe.csproj"),
                                   "-c", "Release", "-v", "quiet", "--", os.path.join(args.out, "recon_probe_out.json")], cwd=HERE)
        # RF1: the normalised behaviour suite, then a two-mutation negative control that has to make it
        # go RED. Both execute the real production sources; the control proves the suite can say no.
        runner.run("csharp_behavior", [DOTNET, "run", "--project",
                                       os.path.join(HERE, "tests", "BehaviorTests", "BehaviorTests.csproj"),
                                       "-c", "Release", "-v", "quiet", "--", "--quiet"], cwd=HERE)
        runner.run("csharp_behavior_negctl", [PY, os.path.join(HERE, "tests", "negative_control.py"),
                                              "--only", "window-constant-0.45",
                                              "--only", "comment-only-control"], cwd=HERE)

    # per-file verdicts from the applicability run
    verdicts = {}
    if os.path.isfile(app_json):
        aj = json.load(io.open(app_json, encoding="utf-8"))
        for e in aj.get("exports", []):
            verdicts[e["file"]] = {"crosscheck": (e.get("crosscheck") or {}).get("status"),
                                   "crosscheckCode": (e.get("crosscheck") or {}).get("statusCode"),
                                   "applicability": e.get("modelApplicability"),
                                   "schemaVersion": e.get("contributionSchemaVersion"),
                                   "comparisonScope": e.get("comparisonScope"),
                                   "knownMultWithoutFoldHits": e.get("knownMultWithoutFoldHits"),
                                   "correctedUnexplainedLeaks": e.get("correctedUnexplainedLeaks")}
    cc_summary = None
    for r in runner.runs:
        if r["name"] == "crosscheck(batch)":
            m = re.search(r'batch: \d+ file\(s\) (\{[^}]*\})', r["tail"])
            if m:
                cc_summary = m.group(1)
    changed = changed_paths(before, snapshot_roots())
    isolation = {"checks": isolation_checks(changed, ALLOWED_CURRENT_OUTPUTS, args.out),
                 "changed": changed}
    write_out(args.out, meta, corpus, runner.runs, verdicts, {"crosscheckSummary": cc_summary},
              frozen=frozen, isolation=isolation)
    print("wrote %s" % args.out)
    return 0


def write_out(outdir, meta, corpus, runs, verdicts, extra, checks_enabled=True, frozen=None,
              isolation=None):
    for e in corpus:
        v = verdicts.get(e["file"]) or {}
        e.update(v)
    obs_batch = {}
    if extra.get("crosscheckSummary"):
        try:
            obs_batch = json.loads(extra["crosscheckSummary"])
        except Exception:
            obs_batch = {}
    obs_emb = {}
    obs_app = {}
    for e in corpus:
        cc = e.get("crosscheck")
        ap = e.get("applicability")
        if cc:
            obs_emb[cc] = obs_emb.get(cc, 0) + 1
        if ap:
            obs_app[ap] = obs_app.get(ap, 0) + 1
    checks = []
    if checks_enabled:
        for k in sorted(set(list(EXPECT_BATCH.keys()) + list(obs_batch.keys()))):
            checks.append({"kind": "crosscheck_batch", "bucket": k, "expected": EXPECT_BATCH.get(k, 0),
                           "observed": obs_batch.get(k, 0), "ok": EXPECT_BATCH.get(k, 0) == obs_batch.get(k, 0)})
        for k in sorted(set(list(EXPECT_EMBEDDED.keys()) + list(obs_emb.keys()))):
            checks.append({"kind": "crosscheck_embedded", "bucket": k, "expected": EXPECT_EMBEDDED.get(k, 0),
                           "observed": obs_emb.get(k, 0), "ok": EXPECT_EMBEDDED.get(k, 0) == obs_emb.get(k, 0)})
        for k in sorted(set(list(EXPECT_APP.keys()) + list(obs_app.keys()))):
            checks.append({"kind": "applicability", "bucket": k, "expected": EXPECT_APP.get(k, 0),
                           "observed": obs_app.get(k, 0), "ok": EXPECT_APP.get(k, 0) == obs_app.get(k, 0)})
        # The vocabulary split must be an IDENTITY, not a surprise: the files `crosscheck --batch` calls
        # LEGACY are exactly the ones the applicability report calls NOT_RUN plus the real legacy ones.
        lhs = obs_batch.get("LEGACY_NOT_APPLICABLE", 0)
        rhs = obs_emb.get("NOT_RUN", 0) + obs_emb.get("LEGACY_NOT_APPLICABLE", 0)
        checks.append({"kind": "identity", "bucket": "batch.LEGACY == embedded.NOT_RUN + embedded.LEGACY",
                       "expected": rhs, "observed": lhs, "ok": lhs == rhs})
        err_files = sorted(e["file"] for e in corpus if e.get("crosscheck") == "ERROR")
        kb = sorted(KNOWN_BAD.keys())
        checks.append({"kind": "known_bad", "bucket": ",".join(err_files) or "(none)", "expected": len(kb),
                       "observed": len([x for x in err_files if x in KNOWN_BAD]), "ok": err_files == kb})
        for r in runs:
            allow = ALLOWED_NONZERO.get(r["name"])
            want = allow[0] if allow else 0
            checks.append({"kind": "run_exit", "bucket": r["name"], "expected": want,
                           "observed": r["exit"], "ok": r["exit"] == want,
                           "allowListed": bool(allow),
                           "why": (allow[1] if allow else "a guard run must exit 0")})
        seen = set(r["name"] for r in runs)
        for nm in sorted(ALLOWED_NONZERO):
            checks.append({"kind": "allowlist_used", "bucket": nm, "expected": "present",
                           "observed": "present" if nm in seen else "(no such run)", "ok": nm in seen})
        # PINNING MUST BE ENFORCED, not decorative. NEW_FILES_EXPECTED was previously written into the
        # manifest and never compared, so a wrong per-file pin could hide behind correct aggregate buckets.
        by_file = {e["file"]: e for e in corpus}
        for f, exp in sorted(NEW_FILES_EXPECTED.items()):
            row = by_file.get(f) or {}
            for key in ("crosscheck", "applicability"):
                obs = row.get(key) or "(missing)"
                checks.append({"kind": "per_file", "bucket": "%s.%s" % (f, key),
                               "expected": exp[key], "observed": obs, "ok": obs == exp[key]})
        # Input freeze + output isolation. Both are checks, not notes: write_out is the only place that
        # can turn them red, and --selftest drives these same functions with tampered inputs.
        checks.extend(input_freeze_checks(corpus, frozen if frozen is not None else FROZEN_CORPUS))
        checks.extend((isolation or {}).get("checks") or [])
        not_run = sorted(e["file"] for e in corpus if e.get("crosscheck") == "NOT_RUN")
    else:
        not_run = []
    manifest = {"meta": meta,
                "expected": {"previous_snapshot": PREV_SNAPSHOT, "new_files": NEW_FILES_EXPECTED,
                             "known_bad": KNOWN_BAD, "batch": EXPECT_BATCH, "embedded": EXPECT_EMBEDDED,
                             "applicability": EXPECT_APP},
                "observed": {"crosscheck_batch": obs_batch, "crosscheck_embedded": obs_emb,
                             "applicability": obs_app},
                "labelingDivergence": {"files": not_run, "note": LABELING_DIVERGENCE},
                "inputFreeze": {"frozen": sorted(frozen if frozen is not None else FROZEN_CORPUS)},
                "outputIsolation": {"watch": [{"dir": _rel(b), "depth": d} for b, d in WATCH],
                                    "changed": (isolation or {}).get("changed") or [],
                                    "allowed": ALLOWED_CURRENT_OUTPUTS},
                "checks": checks, "corpus": corpus}
    io.open(os.path.join(outdir, "corpus_manifest.json"), "w", encoding="utf-8").write(
        json.dumps(manifest, ensure_ascii=False, indent=1, sort_keys=True))
    io.open(os.path.join(outdir, "runs.json"), "w", encoding="utf-8").write(
        json.dumps({"meta": meta, "runs": runs, "extra": extra}, ensure_ascii=False, indent=1, sort_keys=True))
    bad = [c for c in checks if not c["ok"]]
    L = []
    L.append("# N0 acceptance run (corpus manifest + guard runs)")
    L.append("")
    L.append("- generated: %s" % meta["generated"])
    L.append("- exports: %d files in `%s`" % (meta["export_count"], meta["exports_dir"]))
    L.append("- source BuildInfo version: %s ; deployed DLL sha256: %s" % (meta["source_version_BuildInfo"], meta["deployed_dll_sha256"]))
    L.append("- config sha256: %s (contrib switches: %s)" % (meta["config_sha256"],
                                                             ", ".join("%s=%s" % kv for kv in sorted(meta["config_switches"].items())
                                                                       if "Contrib" in kv[0] or "Given" in kv[0] or "Madness" in kv[0])))
    L.append("")
    L.append("## expected vs observed (falsifiable)")
    L.append("")
    if not checks_enabled:
        L.append("(quick mode: no guard runs and no per-file verdicts were collected)")
        L.append("")
    L.append("Expectation = the verified 32-file snapshot + the verdict pinned per file added since; every pin is compared.")
    L.append("")
    L.append("| kind | bucket | expected | observed | ok |")
    L.append("|---|---|---|---|---|")
    for c in checks:
        L.append("| %s | %s | %s | %s | %s |" % (c["kind"], c["bucket"], c["expected"], c["observed"], "yes" if c["ok"] else "NO"))
    L.append("")
    L.append("| tool | bucket | expected | observed |")
    L.append("|---|---|---|---|")
    for k in sorted(set(list(EXPECT_BATCH.keys()) + list(obs_batch.keys()))):
        L.append("| batch | %s | %d | %d |" % (k, EXPECT_BATCH.get(k, 0), obs_batch.get(k, 0)))
    for k in sorted(set(list(EXPECT_EMBEDDED.keys()) + list(obs_emb.keys()))):
        L.append("| embedded | %s | %d | %d |" % (k, EXPECT_EMBEDDED.get(k, 0), obs_emb.get(k, 0)))
    L.append("")
    L.append("**Labeling divergence (documented, tracked by N1):** %s" % LABELING_DIVERGENCE)
    L.append("")
    L.append("**Batch inputs and side effects.** The input list is frozen by name (%d files); the watch"
             % len(frozen if frozen is not None else FROZEN_CORPUS))
    L.append("roots below are re-stat-ed around the whole run, so the historical archives must show no"
             " change at all:")
    L.append("")
    for b, d in WATCH:
        L.append("- %s (%s)" % (b.replace(ROOT, "").lstrip("\\").replace("\\", "/"),
                               "files only" if d == 1 else "recursive"))
    L.append("")
    L.append("Files the applicability report labels NOT_RUN (%d): %s" % (len(not_run), ", ".join(not_run)))
    L.append("")
    L.append("## guard runs")
    L.append("")
    L.append("| run | exit | seconds | log |")
    L.append("|---|---|---|---|")
    for r in runs:
        L.append("| %s | %s | %s | `%s` |" % (r["name"], r["exit"], r["seconds"], r["log"]))
    L.append("")
    L.append("## per-file verdicts")
    L.append("")
    L.append("| file | version | quest | schema | crosscheck | applicability |")
    L.append("|---|---|---|---|---|---|")
    for e in corpus:
        L.append("| %s | %s | %s | %s | %s | %s |" % (e["file"], e.get("version"), e.get("quest"),
                                                         e.get("contributionSchemaVersion") or "-",
                                                         e.get("crosscheck") or "NOT_RUN",
                                                         e.get("applicability") or "-"))
    L.append("")
    if bad:
        L.append("## MISMATCHES")
        L.append("")
        for c in bad:
            L.append("- %s/%s expected %s observed %s" % (c["kind"], c["bucket"], c["expected"], c["observed"]))
        L.append("")
    io.open(os.path.join(outdir, "RESULTS.md"), "w", encoding="utf-8").write("\n".join(L) + "\n")
    print("checks: %d ok, %d mismatched" % (len(checks) - len(bad), len(bad)))


def selftest():
    """Negative control for the run_exit / allowlist_used checks added in round 6.

    A gate nobody has seen go red is not a gate. These cases call write_out() with SYNTHETIC runs so the
    assertion is exercised without re-running the pipeline: one non-allow-listed guard failing must make
    the check red, and the allow-listed run must be red when it exits 0 instead of its declared code.
    """
    import shutil, tempfile
    tmp = tempfile.mkdtemp(prefix="n0_selftest_")
    cases = []

    def zero(n):
        return {"name": n, "exit": 0, "seconds": 0.0, "log": "(synthetic)", "tail": ""}

    def nz(n, e):
        return {"name": n, "exit": e, "seconds": 0.0, "log": "(synthetic)", "tail": ""}

    def case(label, runs, want, want_allow=None):
        outdir = os.path.join(tmp, re.sub(r"[^A-Za-z0-9_.-]", "_", label))
        if not os.path.isdir(outdir):
            os.makedirs(outdir)
        meta = {"generated": "selftest", "export_count": 1, "exports_dir": "(synthetic)",
                "source_version_BuildInfo": "0.0.0", "deployed_dll_sha256": "0" * 64,
                "config_sha256": "0" * 64, "config_switches": {}}
        corpus = [{"file": "synthetic.json", "size": 0, "sha256": "0" * 64, "version": "1.0",
                   "quest": 1, "hasContributionSection": False, "contributionSchemaVersion": None}]
        write_out(outdir, meta, corpus, runs, {}, {}, checks_enabled=True, frozen=["synthetic.json"])
        man = json.load(io.open(os.path.join(outdir, "corpus_manifest.json"), encoding="utf-8"))
        got = {c["bucket"]: c["ok"] for c in man["checks"] if c["kind"] == "run_exit"}
        for name, w in sorted(want.items()):
            cases.append({"case": "%s/%s" % (label, name), "want_ok": w, "got_ok": got.get(name),
                          "pass": got.get(name) == w})
        # a stale allowance (a name no run reports) must be red as well
        got_allow = {c["bucket"]: c["ok"] for c in man["checks"] if c["kind"] == "allowlist_used"}
        for name, w in sorted((want_allow or {}).items()):
            cases.append({"case": "%s/allowlist_used:%s" % (label, name), "want_ok": w,
                          "got_ok": got_allow.get(name), "pass": got_allow.get(name) == w})

    try:
        case("all_zero_nonallowed", [zero("a")], {"a": True}, {"crosscheck(batch)": False})
        case("guard_fail", [zero("a"), nz("b", 3)], {"a": True, "b": False},
             {"crosscheck(batch)": False})
        # The allowance is an EXACT exit code, not "any non-zero is fine": if the named run ever starts
        # exiting 0 the pin no longer describes reality and must be re-decided, so that is red too.
        case("allowlisted_exit1", [nz("crosscheck(batch)", 1)], {"crosscheck(batch)": True},
             {"crosscheck(batch)": True})
        case("allowlisted_exit0", [zero("crosscheck(batch)")], {"crosscheck(batch)": False},
             {"crosscheck(batch)": True})
        case("allowlisted_exit2", [nz("crosscheck(batch)", 2)], {"crosscheck(batch)": False},
             {"crosscheck(batch)": True})

        # --- RF0: the input freeze and the output isolation must both be able to say no ---
        base = ["a.json", "b.json"]
        for label, names, want_ok in (("exact", base, True),
                                      ("extra-file", base + ["c.json"], False),
                                      ("missing-file", ["a.json"], False),
                                      ("empty", [], False)):
            ch = input_freeze_checks([{"file": n} for n in names], base)
            got = all(c["ok"] for c in ch)
            cases.append({"case": "input_freeze/" + label, "want_ok": want_ok, "got_ok": got,
                          "pass": got == want_ok})
        allowed = {"_dpsm_work/current.txt": "declared fixed-path output"}
        acc = os.path.join(HERE, "acc")
        for label, changed, alw, want_ok in (
                ("clean", [], {}, True),
                ("inside-out-is-fine", ["_dpsm_work/acc/corpus_manifest.json"], {}, True),
                ("undeclared-outside", ["_dpsm_work/SESSION-STATE.md"], {}, False),
                ("historical-archive-moved", ["_dpsm_work/acceptance_1.7.11/RESULTS.md"], {}, False),
                ("declared-fixed-path", ["_dpsm_work/current.txt"], allowed, True)):
            ch = isolation_checks(changed, alw, acc)
            got = all(c["ok"] for c in ch)
            cases.append({"case": "output_isolation/" + label, "want_ok": want_ok, "got_ok": got,
                          "pass": got == want_ok})
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    bad = [c for c in cases if not c["pass"]]
    for c in cases:
        print("%-42s want_ok=%-5s got_ok=%-5s %s" % (c["case"], c["want_ok"], c["got_ok"],
                                                     "ok" if c["pass"] else "FAIL"))
    print("selftest: %d case(s), %d failed" % (len(cases), len(bad)))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
