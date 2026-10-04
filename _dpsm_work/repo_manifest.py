# -*- coding: utf-8 -*-
"""RF0: the machine-readable half of the local Git baseline.

.gitignore decides what a COMMIT may contain; this script records what the
baseline IS, including the heavy inputs a commit deliberately excludes
(REFACTOR-PLAN-POST-1.7.11.md section 5.3: "Git does not replace DLL rollback
or the corpus snapshot"). It binds together the seven things a later batch has
to be able to restore or account for:

  version pair       src/BuildInfo.cs vs src/DpsMeter.csproj
  deployed DLL       the running plugin binary, by SHA256
  rollback anchor    the previous version's .bak, by SHA256
  config             dev.dpsmeter.cfg + every boolean switch value
  tools              every git-tracked file, so "restore the baseline" is
                     checkable file by file (380 files, ~10 MB)
  corpus             every battle_*.json export, by SHA256
  external asm       every IL2CPP/BepInEx assembly the plugin csproj references

  python repo_manifest.py --write    # (re)generate baseline-manifest.json
  python repo_manifest.py --verify   # recompute and diff; non-zero on drift

ASCII-only stdout (this console is GBK). The JSON is written as UTF-8.

Why aggregated for the game trees: rlyehshoujotaix_cl_Data is 3.07 GB / 16k
files and is not evidence -- it is the game. Hashing it per file would turn a
2-second check into a minute for no verification value, so those trees get a
(count, bytes, newest-mtime) fingerprint and every tree that IS evidence
(exports, interop, BepInEx core, _review, _verify_*) is hashed per file.
"""
from __future__ import print_function
import argparse, hashlib, io, json, os, re, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
OUT = os.path.join(HERE, "baseline-manifest.json")
EXPORTS = os.path.join(ROOT, "BepInEx", "plugins", "DpsMeter", "exports")
PLUGIN_DIR = os.path.join(ROOT, "BepInEx", "plugins", "DpsMeter")
CFG = os.path.join(ROOT, "BepInEx", "config", "dev.dpsmeter.cfg")
DLL = os.path.join(PLUGIN_DIR, "DpsMeter.dll")

# Per-file hashed even though they are not committed: this is the "associate
# the excluded assets by hash" requirement, applied where a hash can actually
# catch a swap (binaries, the config, the corpus, evidence dumps).
HASHED_EXCLUDED_DIRS = ["BepInEx/interop", "BepInEx/core", "BepInEx/plugins/DpsMeter/masterdata",
                        "_dpsm_work/_review", "_dpsm_work/_verify_179", "_dpsm_work/_verify_178_live",
                        "_dpsm_work/enum_probe", "DpsMeter-0.9.0-r2"]
HASHED_EXCLUDED_FILES = ["GameAssembly.dll", "UnityPlayer.dll", "baselib.dll", "winhttp.dll",
                         "rlyehshoujotaix_cl.exe", "UnityCrashHandler64.exe",
                         "doorstop_config.ini", ".doorstop_version",
                         # NOT the plugin runtime log: BepInEx/config/dpsmeter_runtime.log is written
                         # continuously by the running game, so its hash is never stable and it is not a
                         # baseline asset. build() records its size as a live fingerprint instead.
                         "BepInEx/config/dev.dpsmeter.cfg.before-1.3.5-give-off"]
# Written continuously while the game runs: recorded so the baseline record includes them, but never
# hashed and never compared -- a hash would go red every few seconds during play.
LIVE_FILES = ["BepInEx/config/dpsmeter_runtime.log", "BepInEx/LogOutput.log"]
# Only counted + sized + newest-mtime: game/vendor payload, no verification value.
AGGREGATED_DIRS = ["rlyehshoujotaix_cl_Data", "D3D12", "dotnet", "汉化"]


def sha256_file(path, chunk=1 << 20):
    h = hashlib.sha256()
    with io.open(path, "rb") as fh:
        while True:
            b = fh.read(chunk)
            if not b:
                break
            h.update(b)
    return h.hexdigest().upper()


def rel(p):
    return os.path.relpath(p, ROOT).replace(os.sep, "/")


def git(*args):
    try:
        out = subprocess.check_output(["git"] + list(args), cwd=ROOT, stderr=subprocess.STDOUT)
        return out.decode("utf-8", "replace").strip()
    except Exception as ex:
        return None


def version_pair():
    bi = os.path.join(HERE, "src", "BuildInfo.cs")
    cs = os.path.join(HERE, "src", "DpsMeter.csproj")
    a = b = None
    if os.path.isfile(bi):
        m = re.search(r'Version\s*=\s*"([0-9.]+)"', io.open(bi, encoding="utf-8").read())
        a = m.group(1) if m else None
    if os.path.isfile(cs):
        m = re.search("<Version>([^<]+)</Version>", io.open(cs, encoding="utf-8").read())
        b = m.group(1) if m else None
    return {"buildinfo": a, "csproj": b, "agree": (a is not None and a == b)}


def rollback_anchor():
    """The newest DpsMeter.dll.<ver>.bak in the plugin dir: the version a rollback returns to."""
    best = None
    if os.path.isdir(PLUGIN_DIR):
        for fn in os.listdir(PLUGIN_DIR):
            m = re.match(r"DpsMeter\.dll\.([0-9][0-9.]*)\.bak$", fn)
            if not m:
                continue
            key = [int(x) for x in m.group(1).split(".") if x.isdigit()]
            if best is None or key > best[0]:
                best = (key, os.path.join(PLUGIN_DIR, fn))
    if not best:
        return None
    return {"path": rel(best[1]), "sha256": sha256_file(best[1]), "bytes": os.path.getsize(best[1])}


def config_record():
    if not os.path.isfile(CFG):
        return None
    switches = {}
    for line in io.open(CFG, encoding="utf-8", errors="replace").read().splitlines():
        m = re.match(r"\s*([A-Za-z0-9_]+)\s*=\s*(true|false)\s*$", line)
        if m:
            switches[m.group(1)] = m.group(2)
    return {"path": rel(CFG), "sha256": sha256_file(CFG), "bytes": os.path.getsize(CFG),
            "switches": switches}


def corpus(exports_dir=None):
    out = []
    d = exports_dir or EXPORTS
    if os.path.isdir(d):
        for fn in sorted(os.listdir(d)):
            if not (fn.startswith("battle_") and fn.endswith(".json")):
                continue
            p = os.path.join(d, fn)
            head = io.open(p, "rb").read(4096).decode("utf-8", "replace")
            v = re.search(r'"version"\s*:\s*"([0-9.]+)"', head)
            q = re.search(r'"quest"\s*:\s*(\d+)', head)
            out.append({"file": fn, "bytes": os.path.getsize(p), "sha256": sha256_file(p),
                        "version": v.group(1) if v else None, "quest": int(q.group(1)) if q else None})
    return out


def external_assemblies():
    cs = os.path.join(HERE, "src", "DpsMeter.csproj")
    if not os.path.isfile(cs):
        return []
    t = io.open(cs, encoding="utf-8").read()
    out = []
    for name, hint in re.findall(r"<Reference Include=\"([^\"]+)\">\s*<HintPath>([^<]+)</HintPath>", t):
        p = hint.strip()
        rec = {"name": name, "path": rel(p) if os.path.isabs(p) else p}
        if os.path.isfile(p):
            rec["sha256"] = sha256_file(p)
            rec["bytes"] = os.path.getsize(p)
        else:
            rec["sha256"] = None
            rec["missing"] = True
        out.append(rec)
    return out


def tracked_files():
    files = git("ls-files")
    if not files:
        return None
    out = {}
    for f in files.splitlines():
        p = os.path.join(ROOT, f)
        out[f] = sha256_file(p) if os.path.isfile(p) else None
    return out


def hashed_excluded():
    out = {}
    for d in HASHED_EXCLUDED_DIRS:
        base = os.path.join(ROOT, d)
        if not os.path.isdir(base):
            continue
        for r, dirs, fns in os.walk(base):
            dirs[:] = [x for x in dirs if x not in ("bin", "obj", "__pycache__")]
            for fn in sorted(fns):
                p = os.path.join(r, fn)
                out[rel(p)] = sha256_file(p)
    for f in HASHED_EXCLUDED_FILES:
        p = os.path.join(ROOT, f)
        if os.path.isfile(p):
            out[f] = sha256_file(p)
    return out


def aggregated():
    out = {}
    for d in AGGREGATED_DIRS:
        base = os.path.join(ROOT, d)
        if not os.path.isdir(base):
            continue
        n = 0
        b = 0
        newest = 0.0
        for r, dirs, fns in os.walk(base):
            for fn in fns:
                p = os.path.join(r, fn)
                try:
                    st = os.stat(p)
                except Exception:
                    continue
                n += 1
                b += st.st_size
                if st.st_mtime > newest:
                    newest = st.st_mtime
        out[d] = {"files": n, "bytes": b, "newestMtime": int(newest)}
    return out


def build(exports_dir=None):
    t0 = time.time()
    man = {"tool": "repo_manifest", "schema": "repo-baseline-1",
           "generated": time.strftime("%Y-%m-%dT%H:%M:%S"),
           "git": {"head": git("rev-parse", "HEAD"), "tag": "baseline-1.7.11",
                   "trackedFiles": len((git("ls-files") or "").splitlines())},
           "version": version_pair(),
           "deployed_dll": ({"path": rel(DLL), "sha256": sha256_file(DLL), "bytes": os.path.getsize(DLL)}
                            if os.path.isfile(DLL) else None),
           "rollback_anchor": rollback_anchor(),
           "config": config_record(),
           "corpus": corpus(exports_dir),
           "external_assemblies": external_assemblies(),
           "tracked_sources": tracked_files(),
           "excluded_hashed": hashed_excluded(),
           "excluded_aggregate": aggregated(),
           "live_fingerprint": dict((f, {"bytes": os.path.getsize(os.path.join(ROOT, f))})
                                    for f in LIVE_FILES
                                    if os.path.isfile(os.path.join(ROOT, f))),
           "seconds": round(time.time() - t0, 1)}
    return man


def diff_manifest(man, now):
    """Content drift, with one deliberate asymmetry: a file that did NOT exist at baseline time but does
    now is NOT drift. The baseline asserts "the things it recorded are unchanged", not "nothing new
    exists" -- the export directory is written by the running game and the repository is meant to grow
    with commits. A recorded file that VANISHED or CHANGED is drift. The batch-level input set is pinned
    separately, and exactly, by n0_acceptance.py input_freeze against a frozen snapshot.

    Returns (drift, grew, notes): notes are the game/vendor trees, whose size+mtime fingerprint moves
    whenever the game runs and therefore cannot be a baseline asset.
    """
    bad, grew, notes, changed = [], [], [], []
    # tracked_sources are SOURCE files: a refactor batch changes them on purpose, and their correctness is
    # judged by the behaviour suite and the acceptance run, not by a hash. So a changed source is reported
    # (for review) and a MISSING one is drift -- losing a file is the failure this check exists to catch.
    # The corpus is the opposite: a changed export file is corruption, so it stays drift.
    for key in ("version", "deployed_dll", "rollback_anchor", "config", "external_assemblies"):
        if man.get(key) != now.get(key):
            bad.append(key)
    for key in ("corpus", "tracked_sources", "excluded_hashed"):
        a = man.get(key) or {}
        b = now.get(key) or {}
        if isinstance(a, list):
            a = {e["file"]: e for e in a}
            b = {e["file"]: e for e in b}
        for k in sorted(set(list(a) + list(b))):
            if k not in a:
                grew.append("%s/%s" % (key, k))
            elif a.get(k) != b.get(k):
                (changed if key == "tracked_sources" else bad).append("%s/%s" % (key, k))
    a = man.get("excluded_aggregate") or {}
    b = now.get("excluded_aggregate") or {}
    for k in sorted(set(list(a) + list(b))):
        if a.get(k) != b.get(k):
            notes.append("excluded_aggregate/%s" % k)
    return bad, grew, notes, changed


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--verify", action="store_true")
    ap.add_argument("--out", default=OUT)
    ap.add_argument("--exports", default=None,
                    help="export directory the corpus list describes (default: the live one)")
    a = ap.parse_args()
    if a.verify:
        if not os.path.isfile(a.out):
            print("BLOCK: no manifest at %s (run --write first)" % a.out)
            return 1
        man = json.load(io.open(a.out, encoding="utf-8"))
        now = build(a.exports)
        bad, grew, note, changed = diff_manifest(man, now)
        print("verify: tracked=%s hashed_excluded=%s corpus=%s" %
              (len(now.get("tracked_sources") or {}), len(now.get("excluded_hashed") or {}),
               len(now.get("corpus") or [])))
        for k in bad[:40]:
            print("DRIFT: %s" % k)
        for k in grew[:10]:
            print("GREW (new since the baseline; not drift): %s" % k)
        for k in note[:10]:
            print("NOTE (game/vendor tree moved; its fingerprint is informational): %s" % k)
        for k in changed[:40]:
            print("CHANGED (source file this batch works on; review, not drift): %s" % k)
        print("manifest verify: drift=%d changed=%d grew=%d notes=%d"
              % (len(bad), len(changed), len(grew), len(note)))
        return 1 if bad else 0
    # The SAME export directory --write and --verify are given, or the two disagree the moment the
    # running game adds a battle (that mistake was caught by --verify on the first attempt).
    man = build(a.exports)
    io.open(a.out, "w", encoding="utf-8").write(json.dumps(man, ensure_ascii=False, indent=1, sort_keys=True))
    print("wrote %s (%d bytes, %.1fs)" % (a.out, os.path.getsize(a.out), man["seconds"]))
    print("  version    : %s (agree=%s)" % (man["version"]["buildinfo"], man["version"]["agree"]))
    print("  dll        : %s" % (man["deployed_dll"] or {}).get("sha256"))
    print("  rollback   : %s" % (man["rollback_anchor"] or {}).get("path"))
    print("  tracked    : %d files hashed" % len(man["tracked_sources"] or {}))
    print("  corpus     : %d exports" % len(man["corpus"]))
    print("  excluded   : %d files hashed, %d dirs aggregated" %
          (len(man["excluded_hashed"]), len(man["excluded_aggregate"])))
    return 0


if __name__ == "__main__":
    sys.exit(main())
