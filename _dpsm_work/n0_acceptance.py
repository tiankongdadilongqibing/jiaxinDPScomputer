# -*- coding: utf-8 -*-
"""N0 acceptance closure for the CONTRIBUTION-NEXT-PHASE-ROADMAP.md (section N0).

Why this exists: the roadmap's N0 asks for a REPRODUCIBLE acceptance record of the currently deployed
version instead of "the deployed DLL is X" plus a 29-file snapshot from an earlier round. Everything
this script writes is bound to input hashes (every export, the deployed DLL, the live config, the guard
scripts themselves) and to the exact command lines, so a reader can re-run it and diff.

Outputs (all inside an INDEPENDENT directory, default _dpsm_work/acceptance_1.7.11):
  corpus_manifest.json   per-file sha256/size/version/quest/section/schema + observed verdicts
  runs.json              every command, its exit code, and where its raw stdout/stderr was kept
  RESULTS.md             the human summary: four status columns, expected-vs-observed, gaps

ASCII-only stdout (this console is GBK). Child stdout goes to FILES, never through a pipe.

Usage: python n0_acceptance.py [--exports DIR] [--out DIR] [--log PATH] [--skip-heavy] [--quick]
"""
from __future__ import print_function
import argparse, hashlib, io, json, os, re, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
PY = sys.executable
DEF_EXPORTS = os.path.join(ROOT, "BepInEx", "plugins", "DpsMeter", "exports")
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
# Per-file pins. EVERY entry here is compared in write_out(): an aggregate bucket that still adds up while
# two files swap PASS<->WARNING must go red. The 144548 pin is kept as history and is re-checked too.
# The 16:59/17:01 exports (round 6) are pinned from SNAPSHOT_30 plus one explicit rule each -- written
# BEFORE the round-6 acceptance run:
#   * battle_411001_20261004_170157.json: version 1.7.11, quest 411001, schema 1.1, has a contribution
#     section, same deployed DLL as 144548 => predict batch PASS + applicability full.
#   * battle_9999_20261004_165952.json: quest 9999 is the training mode; every 9999 sample that carries a
#     contribution section (042458 = 1.7.5/1.0, 135214 = 1.7.8/1.1) was LEGACY_NOT_APPLICABLE for
#     `crosscheck --batch` and not_comparable for the applicability report => predict the same pair.
NEW_FILES_EXPECTED = {
    "battle_411001_20261004_144548.json": {"crosscheck": "PASS", "applicability": "full",
                                           "why": "first 1.7.10 real battle: PASS + full is the N0 sample verdict"},
    "battle_411001_20261004_170157.json": {"crosscheck": "PASS", "applicability": "full",
                                           "why": "1.7.11 normal 411001 with schema 1.1, same DLL as 144548"},
    "battle_9999_20261004_165952.json": {"crosscheck": "LEGACY_NOT_APPLICABLE", "applicability": "not_comparable",
                                         "why": "training quest: excluded from crosscheck, not comparable"},
}
# EXPECT_* = SNAPSHOT_30 + the pinned verdict of the two files that are NOT part of it:
#   battle_9999_20261004_165952.json   -> batch LEGACY (+1), embedded LEGACY (+1), app not_comparable (+1)
#   battle_411001_20261004_170157.json -> batch PASS (+1),     embedded PASS (+1),   app full (+1)
EXPECT_BATCH = {"ERROR": 1, "LEGACY_NOT_APPLICABLE": 16, "PASS": 12, "WARNING": 3}
# applicability.json reports its OWN embedded crosscheck status per file; it says NOT_RUN (not LEGACY) for a
# file with no contribution section, so its histogram is a different one. Both are recorded and the
# divergence is checked as an identity -- a silent vocabulary split is exactly what confuses a reader.
EXPECT_EMBEDDED = {"NOT_RUN": 13, "LEGACY_NOT_APPLICABLE": 3, "PASS": 12, "WARNING": 3, "ERROR": 1}
EXPECT_APP = {"full": 14, "partial": 9, "not_comparable": 9}
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


def sha256_file(path, chunk=1 << 20):
    h = hashlib.sha256()
    with io.open(path, "rb") as fh:
        while True:
            b = fh.read(chunk)
            if not b:
                break
            h.update(b)
    return h.hexdigest().upper()


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
    ap.add_argument("--exports", default=DEF_EXPORTS)
    ap.add_argument("--out", default=os.path.join(HERE, "acceptance_1.7.11"))
    ap.add_argument("--dll", default=DEF_DLL)
    ap.add_argument("--cfg", default=DEF_CFG)
    ap.add_argument("--log", default=DEF_LOG)
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
    print("corpus files=%d" % len(corpus))

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
                  "contribution_gate.py", "contrib/crosscheck.py", "contrib/compare.py"]
    tools = git_free_code_hash([os.path.join(HERE, t) for t in tool_files])

    meta = {
        "tool": "n0_acceptance", "schema": "n0-1", "generated": time.strftime("%Y-%m-%dT%H:%M:%S"),
        "exports_dir": exports_dir, "export_count": len(corpus),
        "source_version_BuildInfo": src_version, "deployed_dll": os.path.relpath(args.dll, ROOT).replace("\\", "/"),
        "deployed_dll_sha256": dll_sha, "config": os.path.relpath(args.cfg, ROOT).replace("\\", "/"),
        "config_sha256": cfg_sha, "config_switches": cfg_switches, "tool_sha256": tools,
        "python": PY, "notes": ["logs are associated only when the log names the export path",
                              "no historical export, DLL, config or evidence file is modified by this script"],
    }

    runner = Runner(args.out)
    if args.quick:
        write_out(args.out, meta, corpus, runner.runs, {}, {}, checks_enabled=False)
        print("quick mode: wrote corpus + hashes (no guard runs, no verdicts)")
        return 0

    names = [e["file"] for e in corpus]
    paths = [os.path.join(exports_dir, n) for n in names]

    app_json = os.path.join(args.out, "applicability.json")
    runner.run("applicability(all)", [PY, os.path.join(HERE, "contribution_applicability.py"),
                                      "--exports", exports_dir, "--json", app_json], cwd=HERE)
    cc_dir = os.path.join(args.out, "crosscheck")
    if not args.skip_heavy:
        runner.run("crosscheck(batch)", [PY, "-m", "contrib.crosscheck", "--batch", "battle_*.json",
                                         "--outdir", cc_dir], cwd=HERE)
    runner.run("pairtrusted(all)", [PY, os.path.join(HERE, "pairtrusted_impact.py"), "--files"] + names +
               ["--out-json", os.path.join(args.out, "pairtrusted_impact_report.json"),
                "--out-txt", os.path.join(args.out, "pairtrusted_impact_report.txt")], cwd=HERE)
    runner.run("export_schema(all)", [PY, os.path.join(HERE, "check_export_schema.py")] + paths, cwd=HERE)
    for src, dst in (("export_schema_report.txt", "export_schema_report.txt"),):
        sp = os.path.join(HERE, src)
        if os.path.isfile(sp):
            io.open(os.path.join(args.out, dst), "w", encoding="utf-8").write(io.open(sp, encoding="utf-8").read())
    runner.run("layout(all)", [PY, os.path.join(HERE, "check_contribution_layout.py"), "--dir", exports_dir], cwd=HERE)
    if os.path.isfile(args.log):
        runner.run("live_log", [PY, os.path.join(HERE, "check_live_log.py"), "--log", args.log], cwd=HERE)
    else:
        runner.run("live_log(missing)", [PY, "-c", "print('no log at %s')" % args.log], cwd=HERE)
    for nm, argv in [
            ("selftest/layout", [PY, os.path.join(HERE, "check_contribution_layout.py"), "--selftest"]),
            ("selftest/schema", [PY, os.path.join(HERE, "check_export_schema.py"), "--selftest"]),
            ("selftest/live_log", [PY, os.path.join(HERE, "check_live_log.py"), "--selftest"]),
            ("doc_convergence", [PY, os.path.join(HERE, "check_doc_convergence.py")]),
            ("selftest/doc_convergence", [PY, os.path.join(HERE, "check_doc_convergence.py"), "--selftest"]),
            ("docs123", [PY, os.path.join(HERE, "check_docs_123.py")]),
            ("selftest/docs123", [PY, os.path.join(HERE, "check_docs_123.py"), "--selftest"]),
            ("test_gate", [PY, "-m", "contrib.tests.test_gate"]),
            ("given_coupling", [PY, os.path.join(HERE, "check_given_fold_coupling.py")]),
            ("selftest/given_coupling", [PY, os.path.join(HERE, "check_given_fold_coupling.py"), "--selftest"]),
            ("p2a", [PY, os.path.join(HERE, "check_p2a_summary_and_lastbattle.py")]),
            ("factsig", [PY, os.path.join(HERE, "check_fact_signature.py")]),
            ("selftest/applicability", [PY, os.path.join(HERE, "contribution_applicability.py"), "--selftest"]),
            ("selftest/pairtrusted", [PY, os.path.join(HERE, "pairtrusted_impact.py"), "--selftest"]),
            ("refactor_final_check", [PY, os.path.join(HERE, "refactor_final_check.py")]),
            ("selftest/refactor", [PY, os.path.join(HERE, "refactor_final_check.py"), "--selftest"]),
            ("v150_validate", [PY, os.path.join(HERE, "v150_validate.py")]),
            ("selftest/v150", [PY, os.path.join(HERE, "v150_validate.py"), "--selftest"]),
            ("selftest/eligibility", [PY, os.path.join(HERE, "comparison_eligibility.py"), "--selftest"]),
            ("selftest/eligibility_e2e", [PY, os.path.join(HERE, "comparison_eligibility.py"), "--selftest-e2e"]),
            ("selftest/compare", [PY, "-m", "contrib.compare", "--selftest"]),
            ("selftest/decision", [PY, os.path.join(HERE, "decision_report.py"), "--selftest"]),
            ("selftest/budget", [PY, os.path.join(HERE, "budget_census.py"), "--selftest"])]:
        runner.run(nm, argv, cwd=HERE)
    if not args.skip_heavy and os.path.isfile(DOTNET):
        runner.run("recon_probe", [DOTNET, "run", "--project", os.path.join(HERE, "recon_probe", "ReconProbe.csproj"),
                                   "-c", "Release", "-v", "quiet", "--", os.path.join(args.out, "recon_probe_out.json")], cwd=HERE)

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
    write_out(args.out, meta, corpus, runner.runs, verdicts, {"crosscheckSummary": cc_summary})
    print("wrote %s" % args.out)
    return 0


def write_out(outdir, meta, corpus, runs, verdicts, extra, checks_enabled=True):
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
    L.append("Expectation = the verified 30-file snapshot + the verdict pinned per new file; every pin is compared.")
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
        write_out(outdir, meta, corpus, runs, {}, {}, checks_enabled=True)
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
