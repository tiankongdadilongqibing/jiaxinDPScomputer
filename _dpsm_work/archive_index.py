#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""RF7c (plan section 12, the R0-2 prerequisite): the ARCHIVE INDEX.

The plan wants the historical evidence eventually archived, and lists the preconditions: prove nothing live
calls it, record the list with HASHES, and verify the package reads back. This tool does the middle one for
the evidence trees that are already sitting in _dpsm_work (evidence_*, probe_*, review_contrib_core): it
records every file with its size and SHA256, and --verify fails when a recorded file CHANGED, vanished, or a
new one appeared. Nothing is moved or deleted -- the plan is explicit that this stage is index-only.

Why hashes matter here: these files are the measurements the plugin's rules were derived from. A silent edit
to one of them would leave the code citing evidence that no longer says what it said.

Usage:
    python archive_index.py              # verify (exit 0 clean / 1 findings)
    python archive_index.py --write      # (re)record, then regenerate ARCHIVE-INDEX.md
    python archive_index.py --selftest   # tamper temp copies; expect the right failures
"""
import hashlib, io, json, os, shutil, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
INDEX = os.path.join(HERE, "archive-index.json")
REPORT = os.path.join(HERE, "ARCHIVE-INDEX.md")
ROOT_PATTERNS = ("evidence_", "probe_", "review_contrib_core")


def _roots(root):
    out = []
    for name in sorted(os.listdir(root)):
        for pat in ROOT_PATTERNS:
            if name == pat or name.startswith(pat):
                out.append(os.path.join(root, name))
                break
    return out


def _files(root):
    seen = {}
    for base in _roots(root):
        if os.path.isfile(base):
            cands = [base]
        else:
            cands = []
            for dirpath, dirnames, filenames in os.walk(base):
                if "__pycache__" in dirpath:
                    continue
                for n in filenames:
                    cands.append(os.path.join(dirpath, n))
        for p in cands:
            rel = os.path.relpath(p, root).replace("\\", "/")
            with io.open(p, "rb") as fh:
                data = fh.read()
            seen[rel] = {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}
    return seen


def _load(path=None):
    with io.open(path or INDEX, "r", encoding="utf-8") as fh:
        return json.load(fh)


def verify(index_path=None, root=None):
    root = root or HERE
    try:
        rec = _load(index_path)
    except Exception as e:
        return ["the index could not be read: %s" % e]
    now = _files(root)
    old = rec.get("files", {})
    fail = []
    for rel in sorted(old):
        if rel not in now:
            fail.append("recorded evidence is GONE: " + rel)
        elif now[rel]["sha256"] != old[rel]["sha256"]:
            fail.append("recorded evidence CHANGED: %s (%d -> %d bytes)"
                        % (rel, old[rel]["bytes"], now[rel]["bytes"]))
    for rel in sorted(set(now) - set(old)):
        fail.append("new evidence is NOT recorded (run --write): " + rel)
    return fail


def render(rec):
    files = rec.get("files", {})
    total = sum(f["bytes"] for f in files.values())
    lines = [u"# 历史证据索引(RF7c)", u"",
             u"> 由 `archive_index.py --write` 生成;`--verify` 会在任何一份证据被改动/丢失/新增时报红。",
             u"> **不移动、不删除任何证据**(方案 R0 ② 的前置:先记录清单与哈希)。", u"",
             u"| 项 | 值 |", u"|---|---|",
             u"| 已记录文件 | %d |" % len(files),
             u"| 合计字节 | %d |" % total, u"",
             u"| 文件 | 字节 | SHA256(前 16) |", u"|---|---|---|"]
    for rel in sorted(files):
        lines.append(u"| `%s` | %d | `%s` |" % (rel.replace(u"|", u"/"), files[rel]["bytes"],
                                              files[rel]["sha256"][:16]))
    lines.append(u"")
    return u"\n".join(lines)


def write(root=None, index_path=None, report=None):
    root = root or HERE
    files = _files(root)
    rec = {"_comment": "RF7c archive index: every historical evidence file with its size and sha256.",
           "roots": list(ROOT_PATTERNS), "files": files}
    with io.open(index_path or INDEX, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(json.dumps(rec, ensure_ascii=False, indent=1, sort_keys=True))
        fh.write(u"\n")
    with io.open(report or REPORT, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(render(rec))
    print("archive index: recorded=%d bytes=%d" % (len(files), sum(f["bytes"] for f in files.values())))
    return 0


def selftest():
    tmp = tempfile.mkdtemp(prefix="archive_")
    try:
        os.makedirs(os.path.join(tmp, "evidence_demo"))
        p = os.path.join(tmp, "evidence_demo", "m.txt")
        with io.open(p, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(u"measurement\n")
        ip = os.path.join(tmp, "archive-index.json")
        write(root=tmp, index_path=ip, report=os.path.join(tmp, "R.md"))
        fails = []
        if verify(index_path=ip, root=tmp):
            fails.append("a fresh index does not verify")
        with io.open(p, "a", encoding="utf-8", newline="\n") as fh:
            fh.write(u"tampered\n")
        if not any("CHANGED" in f for f in verify(index_path=ip, root=tmp)):
            fails.append("an edited evidence file is not caught")
        with io.open(p, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(u"measurement\n")
        with io.open(os.path.join(tmp, "evidence_demo", "new.txt"), "w", encoding="utf-8", newline="\n") as fh:
            fh.write(u"new\n")
        if not any("NOT recorded" in f for f in verify(index_path=ip, root=tmp)):
            fails.append("an unrecorded new file is not caught")
        os.remove(os.path.join(tmp, "evidence_demo", "new.txt"))
        os.remove(p)
        if not any("GONE" in f for f in verify(index_path=ip, root=tmp)):
            fails.append("a deleted evidence file is not caught")
        for name in ("a fresh index does not verify", "an edited evidence file is not caught",
                     "an unrecorded new file is not caught", "a deleted evidence file is not caught"):
            print("  [%s] %s" % ("FAIL" if name in fails else "PASS", name))
        print("---- selftest: %s" % ("PASS" if not fails else "FAIL %s" % fails))
        return 0 if not fails else 1
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def main(argv):
    if "--selftest" in argv:
        return selftest()
    if "--write" in argv:
        return write()
    fail = verify()
    rec = _load()
    print("archive index: recorded=%d rooted=%s" % (len(rec.get("files", {})), ",".join(rec.get("roots", []))))
    for line in fail:
        print("  FAIL " + line)
    print("---- archive index: %s" % ("PASS" if not fail else "FAIL %d" % len(fail)))
    return 0 if not fail else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
