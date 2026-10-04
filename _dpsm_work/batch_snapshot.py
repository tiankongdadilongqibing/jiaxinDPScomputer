# -*- coding: utf-8 -*-
"""RF0 section 5.5: freeze ONE batch's input list, independent of a live game session.

Why this exists. The export directory is written by the running game: during the first RF0 attempt a
fourth 1.7.11 battle appeared DURING the batch, which moved every corpus total, every applicability
histogram and every crosscheck bucket. "The batch ran over whatever was in the directory at the time"
is not a baseline, so:

  * the input files are HARD-LINKED into _dpsm_work/batch_inputs/<name>/ (same volume, no 500 MB copy,
    and the game never rewrites an existing export -- it only adds new ones);
  * the frozen list + per-file sha256 land in _dpsm_work/batch-inputs-<name>.json, which IS committed,
    so the batch's provenance survives even though the linked JSON files are not tracked;
  * the linked directory is immutable by default: --force is required to replace it, so a batch cannot
    quietly start reading a different input set halfway through.

Usage:
  python batch_snapshot.py --name rf0 [--force]     create/replace the snapshot
  python batch_snapshot.py --name rf0 --verify      re-hash and compare; non-zero on any drift
  python batch_snapshot.py --list
ASCII-only stdout (GBK console).
"""
from __future__ import print_function
import argparse, glob, hashlib, io, json, os, shutil, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
SOURCE = os.path.join(ROOT, "BepInEx", "plugins", "DpsMeter", "exports")
LINKROOT = os.path.join(HERE, "batch_inputs")


def sha256_file(path, chunk=1 << 20):
    h = hashlib.sha256()
    with io.open(path, "rb") as fh:
        while True:
            b = fh.read(chunk)
            if not b:
                break
            h.update(b)
    return h.hexdigest().upper()


def manifest_path(name):
    return os.path.join(HERE, "batch-inputs-%s.json" % name)


def link_dir(name):
    return os.path.join(LINKROOT, name)


def sources():
    return sorted(glob.glob(os.path.join(SOURCE, "battle_*.json")))


def create(name, force):
    ld = link_dir(name)
    mp = manifest_path(name)
    if (os.path.isdir(ld) or os.path.isfile(mp)) and not force:
        print("BLOCK: snapshot %s already exists (it is immutable on purpose; pass --force to replace)" % name)
        print("       links=%s manifest=%s" % (ld, mp))
        return 1
    if os.path.isdir(ld):
        shutil.rmtree(ld)
    os.makedirs(ld)
    entries = []
    for p in sources():
        fn = os.path.basename(p)
        dst = os.path.join(ld, fn)
        try:
            os.link(p, dst)
            linked = True
        except Exception as ex:
            print("  (hard link failed for %s: %r -- falling back to a copy)" % (fn, ex))
            shutil.copy2(p, dst)
            linked = False
        entries.append({"file": fn, "bytes": os.path.getsize(p), "sha256": sha256_file(p),
                        "linked": linked})
    man = {"tool": "batch_snapshot", "schema": "batch-inputs-1", "name": name,
           "created": time.strftime("%Y-%m-%dT%H:%M:%S"),
           "sourceDir": os.path.relpath(SOURCE, ROOT).replace(os.sep, "/"),
           "linkDir": os.path.relpath(ld, ROOT).replace(os.sep, "/"),
           "count": len(entries), "files": entries,
           "note": "the source directory is LIVE (the running game appends to it); this list is the batch's "
                   "frozen input and is checked by n0_acceptance.py input_freeze"}
    io.open(mp, "w", encoding="utf-8").write(json.dumps(man, ensure_ascii=False, indent=1, sort_keys=True))
    print("snapshot %s: %d file(s) -> %s" % (name, len(entries), man["linkDir"]))
    print("  manifest: %s" % os.path.relpath(mp, ROOT).replace(os.sep, "/"))
    print("  total bytes: %d" % sum(e["bytes"] for e in entries))
    return 0


def verify(name):
    mp = manifest_path(name)
    if not os.path.isfile(mp):
        print("BLOCK: no manifest at %s" % mp)
        return 1
    man = json.loads(io.open(mp, encoding="utf-8").read())
    ld = link_dir(name)
    bad = []
    for e in man.get("files") or []:
        p = os.path.join(ld, e["file"])
        if not os.path.isfile(p):
            bad.append("missing: " + e["file"])
            continue
        got = sha256_file(p)
        if got != e["sha256"]:
            bad.append("changed: %s (%s != %s)" % (e["file"], got[:12], e["sha256"][:12]))
    present = sorted(os.path.basename(p) for p in glob.glob(os.path.join(ld, "*.json")))
    extra = sorted(set(present) - set(e["file"] for e in man.get("files") or []))
    for x in extra:
        bad.append("unexpected file in the frozen dir: " + x)
    print("snapshot %s: %d frozen file(s), drift=%d" % (name, len(man.get("files") or []), len(bad)))
    for b in bad[:10]:
        print("  DRIFT: " + b)
    return 1 if bad else 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--name", default="rf0")
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--verify", action="store_true")
    ap.add_argument("--list", action="store_true")
    a = ap.parse_args()
    if a.list:
        for p in sorted(glob.glob(os.path.join(HERE, "batch-inputs-*.json"))):
            m = json.loads(io.open(p, encoding="utf-8").read())
            print("%-10s %4d file(s)  %s  %s" % (m.get("name"), m.get("count"), m.get("created"),
                                                 m.get("linkDir")))
        return 0
    if a.verify:
        return verify(a.name)
    return create(a.name, a.force)


if __name__ == "__main__":
    sys.exit(main())
