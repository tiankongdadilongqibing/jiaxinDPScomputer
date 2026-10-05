# -*- coding: utf-8 -*-
"""battle_select.py -- R56 (BID-4, plan section 7): resolve battle references to FILES and compare
exactly those files.

WHY THIS EXISTS. After a session the user knows which fights they mean ("compare #3 and #5"), but the
analysis side only had "the newest file", a glob, or a history-list position. All three are guesses.
This tool turns a copied reference into an explicit file list, refuses to guess when the reference is
missing or ambiguous, and re-checks the content hash before anything is compared.

WHAT IT DOES NOT DO. It never computes damage, contribution or eligibility itself: it prepares a
selection and hands the ABSOLUTE paths to contrib.compare, which keeps its own statuses and exit codes
(plan section 7: "优先调用已有compare的 --files,不重写贡献算法").

RESOLUTION RULES (plan section 7/8)
  * a full id (B-...-003) matches exactly and the user's order is preserved; duplicates are collapsed
    with a notice, never counted as two samples;
  * a bare sequence needs an explicit --launch, because "#3" is only unique inside one launch;
  * old exports (no battleRef block) are addressed as legacy:<sha256>; a shortened hash prefix is
    accepted only when it is unique inside this directory;
  * any missing / ambiguous / conflicting input stops the WHOLE group (non-zero exit). A partial
    comparison that silently drops one battle is exactly the outcome this feature prevents.

EXIT CODES (selection layer; contrib.compare keeps its own)
  0 ok | 2 missing | 3 ambiguous-or-conflict | 4 content changed since the selection
  5 unfinished sample in a formal compare | 6 usage / unreadable input

ASCII-only stdout (the console is GBK).
"""
from __future__ import print_function
import argparse, hashlib, io, json, os, re, sys, tempfile, shutil

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
DEFAULT_EXPORTS = os.path.join(ROOT, "BepInEx", "plugins", "DpsMeter", "exports")
SCHEMA = "battle-selection/1"

EX_OK, EX_MISSING, EX_AMBIGUOUS, EX_CHANGED, EX_UNFINISHED, EX_USAGE = 0, 2, 3, 4, 5, 6

RE_BREF = re.compile(r'"battleRef"\s*:\s*\{([^}]*)\}')
RE_FIELD = re.compile(r'"(\w+)"\s*:\s*("([^"]*)"|(-?\d+))')
RE_QUEST = re.compile(r'"quest"\s*:\s*(-?\d+)')
RE_DURATION = re.compile(r'"duration"\s*:\s*(-?[0-9.]+)')
RE_RESULT = re.compile(r'"result"\s*:\s*"([^"]*)"')
RE_STARTED = re.compile(r'"started"\s*:\s*"([^"]*)"')
LEGACY_PREFIX = "legacy:"
HEAD_BYTES = 8192


def sha256_file(path):
    h = hashlib.sha256()
    with io.open(path, "rb") as fh:
        while True:
            b = fh.read(1 << 20)
            if not b:
                break
            h.update(b)
    return h.hexdigest()


def read_head(path):
    try:
        with io.open(path, "rb") as fh:
            raw = fh.read(HEAD_BYTES)
        return raw.decode("utf-8", "replace")
    except Exception:
        return None


def parse_head(text):
    """The identity + the cheap scalars, from the file's FIRST bytes only.

    A 24 MB export is read head-first on purpose: listing a directory must not hash every file, and the
    root object writes battleRef/quest/duration/result/started before the bulky sections. If a key is not
    in the head the field stays unknown -- this function must never invent one."""
    out = {"hasRef": False, "id": "", "revision": None, "state": "", "resetCount": None,
           "launchId": "", "sequence": None, "quest": None, "duration": None, "result": "", "started": ""}
    m = RE_BREF.search(text)
    if m:
        out["hasRef"] = True
        for k, full, sv, nv in RE_FIELD.findall(m.group(1)):
            # the full match decides the type: an EMPTY string value ("closeReason":"") is a string with
            # no number, and treating it as one used to raise instead of reporting "no reason".
            v = sv if full.startswith('"') else int(nv)
            if k in ("id", "launchId", "state"):
                out[k] = v
            elif k in ("revision", "resetCount", "sequence"):
                out[k] = int(v)
    q = RE_QUEST.search(text)
    if q:
        out["quest"] = int(q.group(1))
    d = RE_DURATION.search(text)
    if d:
        try:
            out["duration"] = float(d.group(1))
        except Exception:
            out["duration"] = None
    r = RE_RESULT.search(text)
    if r:
        out["result"] = r.group(1)
    s = RE_STARTED.search(text)
    if s:
        out["started"] = s.group(1)
    return out


def name_id(name):
    """The id embedded in the new file name (battle_{quest}_{stamp}__{id}.json), or ""."""
    base = name[:-5] if name.endswith(".json") else name
    at = base.find("__")
    if at < 0:
        return ""
    return base[at + 2:]


def name_quest(name):
    """The quest from its HISTORICAL position (second underscore field), so existing globs and the
    offline tools' file-name parsing keep working (plan section 2)."""
    base = name[:-5] if name.endswith(".json") else name
    parts = base.split("_")
    if len(parts) >= 2:
        try:
            return int(parts[1])
        except Exception:
            return None
    return None


def scan(exports_dir):
    """Every battle_*.json in the directory with its identity. No hashing here (list must stay cheap)."""
    entries = []
    if not os.path.isdir(exports_dir):
        return entries
    for name in sorted(os.listdir(exports_dir)):
        if not (name.startswith("battle_") and name.endswith(".json")):
            continue
        path = os.path.join(exports_dir, name)
        if not os.path.isfile(path):
            continue
        head = read_head(path)
        e = {"path": os.path.abspath(path), "name": name, "bytes": os.path.getsize(path),
             "headOk": head is not None, "nameId": name_id(name), "nameQuest": name_quest(name)}
        e.update(parse_head(head) if head is not None else parse_head(""))
        if e["quest"] is None:
            e["quest"] = e["nameQuest"]
        entries.append(e)
    return entries


def ascii_safe(s):
    return (s or "").encode("unicode_escape").decode("ascii")


def fmt_entry(e):
    tag = e["id"] if e["hasRef"] else "legacy"
    state = e["state"] or "-"
    extra = ""
    if e["hasRef"] and e["nameId"] and e["nameId"] != e["id"]:
        extra = "  NAME-ID-MISMATCH(" + ascii_safe(e["nameId"]) + ")"
    if not e["hasRef"] and e["nameId"]:
        extra = "  NAME-HAS-ID-BUT-BODY-HAS-NONE"
    return "  %-42s q=%-7s %-6s rev=%-4s reset=%-3s %s%s" % (
        tag, str(e["quest"]), state, str(e["revision"]), str(e["resetCount"]),
        os.path.basename(e["path"]), extra)


def cmd_list(a):
    entries = scan(a.exports)
    if not entries:
        print("no battle_*.json under " + a.exports)
        return EX_USAGE
    with_ref = [e for e in entries if e["hasRef"]]
    print("battle_select list: %d file(s), %d with a battleRef, %d legacy" % (
        len(entries), len(with_ref), len(entries) - len(with_ref)))
    for e in entries:
        print(fmt_entry(e))
    if a.json:
        with io.open(a.json, "w", encoding="utf-8") as fh:
            fh.write(json.dumps({"schema": SCHEMA, "exportsDir": os.path.abspath(a.exports),
                                 "items": entries}, ensure_ascii=False, indent=1, sort_keys=True))
        print("wrote " + a.json)
    return EX_OK


def resolve_inputs(entries, ids, launch, seqs, legacy_prefixes):
    """Resolve every input to exactly one entry, or explain why it cannot be done.

    Returns (picked, errors, warnings) where picked preserves the USER's order."""
    by_id = {}
    for e in entries:
        if e["hasRef"]:
            by_id.setdefault(e["id"], []).append(e)
    picked, errors, warnings, seen = [], [], [], set()

    def add(e, raw):
        key = e["id"] if e["hasRef"] else ("legacy:" + e["path"])
        if key in seen:
            warnings.append("duplicate input " + ascii_safe(raw) + " -> same file, counted once")
            return
        seen.add(key)
        picked.append((raw, e))

    for raw in ids or []:
        s = (raw or "").strip()
        if not s:
            errors.append("empty id")
            continue
        if s.lower().startswith(LEGACY_PREFIX):
            hits = [e for e in entries if not e["hasRef"] and e["path"]]
            # a legacy reference is a CONTENT hash; matching it needs the hash, so it is resolved in a
            # second pass below (resolve_legacy) -- here we only record the request.
            picked.append((s, None))
            continue
        hits = by_id.get(s, [])
        if not hits:
            errors.append("not found: " + ascii_safe(s))
            continue
        if len(hits) > 1:
            errors.append("ambiguous: " + ascii_safe(s) + " matches %d files" % len(hits))
            continue
        add(hits[0], s)

    if launch:
        for n in seqs or []:
            want = launch + "-" + ("%03d" % int(n))
            hits = [e for e in entries if e["hasRef"] and e["launchId"] == launch and e["sequence"] == int(n)]
            if not hits:
                errors.append("not found: launch %s seq %d" % (ascii_safe(launch), int(n)))
                continue
            if len(hits) > 1:
                errors.append("ambiguous: launch %s seq %d matches %d files" % (ascii_safe(launch), int(n), len(hits)))
                continue
            add(hits[0], want)

    for pre in legacy_prefixes or []:
        p = (pre or "").strip().lower()
        if p.startswith(LEGACY_PREFIX):
            p = p[len(LEGACY_PREFIX):]
        picked.append(("legacy:" + p, None)) if p else errors.append("empty legacy prefix")

    return picked, errors, warnings


def finish_legacy(picked, entries):
    """Resolve legacy:* by CONTENT hash. A prefix is accepted only when it is unique in this directory
    (plan section 8) -- otherwise the group stops instead of picking one of several files."""
    errors, out = [], []
    legacy = [e for e in entries if not e["hasRef"]]
    cache = {}
    for raw, e in picked:
        if e is not None:
            out.append((raw, e))
            continue
        p = raw[len(LEGACY_PREFIX):].lower()
        hits = []
        for c in legacy:
            if c["path"] not in cache:
                try:
                    cache[c["path"]] = sha256_file(c["path"])
                except Exception:
                    cache[c["path"]] = ""
            if cache[c["path"]].startswith(p):
                hits.append(c)
        if not hits:
            errors.append("legacy prefix not found: " + ascii_safe(p))
            continue
        if len(hits) > 1:
            errors.append("legacy prefix is not unique (%d files): %s" % (len(hits), ascii_safe(p)))
            continue
        out.append((raw, hits[0]))
    return out, errors


def item_of(order, raw, e, with_hash=True):
    it = {"order": order, "input": raw, "path": e["path"], "name": os.path.basename(e["path"]),
          "bytes": e["bytes"], "id": e["id"], "revision": e["revision"], "state": e["state"],
          "resetCount": e["resetCount"], "launchId": e["launchId"], "sequence": e["sequence"],
          "quest": e["quest"], "duration": e["duration"], "result": e["result"], "started": e["started"],
          "kind": "id" if e["hasRef"] else "legacy"}
    if with_hash:
        try:
            it["sha256"] = sha256_file(e["path"])
        except Exception as ex:
            it["sha256"] = ""
            it["hashError"] = str(ex)
    return it


def cmd_resolve(a):
    entries = scan(a.exports)
    if not entries:
        print("no battle_*.json under " + a.exports)
        return EX_USAGE
    if not a.ids and not (a.launch and a.seq) and not a.legacy:
        print("nothing to resolve: pass --ids, --launch+--seq, or --legacy")
        return EX_USAGE
    picked, errors, warnings = resolve_inputs(entries, a.ids, a.launch, a.seq, a.legacy)
    picked, lerr = finish_legacy(picked, entries)
    errors += lerr
    # a name that carries an id must agree with the body: a renamed file is fine, a file whose name
    # claims an id it does not contain is a conflict (plan section 5.2 step 2).
    for raw, e in picked:
        if e["nameId"] and e["hasRef"] and e["nameId"] != e["id"]:
            errors.append("conflict: file name claims %s but the body says %s" % (
                ascii_safe(e["nameId"]), ascii_safe(e["id"])))
    if errors:
        for x in errors:
            print("  ERROR " + x)
        print("battle_select resolve: REFUSED (%d problem(s)); no partial selection is written" % len(errors))
        return EX_AMBIGUOUS if any("ambiguous" in x or "conflict" in x for x in errors) else EX_MISSING
    items = []
    for i, (raw, e) in enumerate(picked):
        items.append(item_of(i + 1, raw, e))
    out = {"schema": SCHEMA, "request": a.request or "", "exportsDir": os.path.abspath(a.exports),
           "createdAt": a.now or "", "order": [it["input"] for it in items],
           "items": items, "errors": [], "warnings": warnings, "allowUnfinished": bool(a.allow_unfinished)}
    if a.out:
        with io.open(a.out, "w", encoding="utf-8") as fh:
            fh.write(json.dumps(out, ensure_ascii=False, indent=1, sort_keys=True))
        print("wrote " + a.out)
    for it in items:
        print("  [%d] %s -> %s (rev=%s state=%s sha=%s)" % (
            it["order"], ascii_safe(it["input"]), it["name"], it["revision"], it["state"],
            (it["sha256"] or "")[:12]))
    for w in warnings:
        print("  WARN " + w)
    print("battle_select resolve: %d item(s) selected" % len(items))
    return EX_OK


def recheck(sel):
    """Before anything is compared, the selection must still describe the same bytes (plan section 7:
    文件已更新则停止并要求重新确认,不默认追踪最新值)."""
    problems = []
    for it in sel.get("items", []):
        p = it.get("path", "")
        if not os.path.isfile(p):
            problems.append("missing now: " + ascii_safe(p))
            continue
        try:
            now = sha256_file(p)
        except Exception as ex:
            problems.append("unreadable: " + ascii_safe(p) + " (" + str(ex) + ")")
            continue
        if it.get("sha256") and now != it["sha256"]:
            problems.append("content changed: " + ascii_safe(it.get("name", p)))
    return problems


def cmd_compare(a):
    with io.open(a.selection, "r", encoding="utf-8") as fh:
        sel = json.load(fh)
    problems = recheck(sel)
    if problems:
        for p in problems:
            print("  ERROR " + p)
        print("battle_select compare: REFUSED (re-resolve the references; the tool never follows a "
              "changed file by itself)")
        return EX_CHANGED
    items = sel.get("items", [])
    if not items:
        print("battle_select compare: the selection is empty")
        return EX_USAGE
    unfinished = [it for it in items if (it.get("state") or "") != "final"]
    if unfinished and not a.allow_unfinished:
        for it in unfinished:
            print("  UNFINISHED %s state=%s" % (ascii_safe(it.get("name", "")), it.get("state")))
        print("battle_select compare: REFUSED (%d sample(s) are not final; pass --allow-unfinished for a "
              "diagnostic run, and label the result as unfinished)" % len(unfinished))
        return EX_UNFINISHED
    reset = [it for it in items if (it.get("resetCount") or 0) > 0]
    for it in reset:
        print("  WARN %s was reset %d time(s): the file does not contain the reset data" % (
            ascii_safe(it.get("name", "")), it.get("resetCount")))
    paths = [it["path"] for it in items]
    print("battle_select compare: %d file(s), in the requested order:" % len(paths))
    for it in items:
        print("  [%d] %s" % (it["order"], ascii_safe(it["path"])))
    cmd = [sys.executable, "-m", "contrib.compare", "--files"] + paths
    if a.out:
        cmd += ["--out", a.out]
    if a.applicability:
        cmd += ["--applicability", a.applicability]
    print("  -> " + ascii_safe(" ".join(cmd[:4])) + " ...")
    import subprocess
    rc = subprocess.call(cmd, cwd=HERE)
    print("battle_select compare: contrib.compare exit=%d" % rc)
    return rc


def _write_fake(path, quest, bid, rev, state, reset, dur, result, extra_pad=""):
    # the launchId itself contains dashes, so it is recovered from the RIGHT (B-{launch}-{seq})
    head, seqtxt = bid.rsplit("-", 1)
    launch = head[2:] if head.startswith("B-") else head
    body = ('{"app":"DpsMeter","version":"1.7.11","battleRef":{"schemaVersion":"1","id":"%s",'
            '"launchId":"%s","sequence":%d,"resetCount":%d,"revision":%d,"state":"%s",'
            '"closeReason":"end"},"quest":%d,"duration":%.2f,"result":"%s","started":"2026-10-05T14:30:12",'
            '"pad":"%s"}' % (bid, launch, int(seqtxt), reset, rev, state, quest, dur, result, extra_pad))
    with io.open(path, "w", encoding="utf-8") as fh:
        fh.write(body)
    return body


def _write_legacy(path, quest, dur, result):
    body = '{"app":"DpsMeter","version":"1.5.5","quest":%d,"duration":%.2f,"result":"%s","totals":{}}' % (
        quest, dur, result)
    with io.open(path, "w", encoding="utf-8") as fh:
        fh.write(body)


def selftest():
    """A gate nobody has seen fail is not a gate: every refusal path is executed here."""
    tmp = tempfile.mkdtemp(prefix="battle_select_")
    cases, bad = [], 0

    def check(label, ok, detail=""):
        cases.append((label, bool(ok), detail))

    try:
        ex = os.path.join(tmp, "exports")
        os.makedirs(ex)
        L = "20261005-143012-7A2C91EF"
        ids = {}
        for seq in (1, 2, 3, 5):
            bid = "B-%s-%03d" % (L, seq)
            p = os.path.join(ex, "battle_411001_20261005_1430%02d__%s.json" % (seq, bid))
            _write_fake(p, 411001, bid, 1, "final", 0, 100.0 + seq, "Lose")
            ids[seq] = (bid, p)
        # a provisional sample and one that was reset
        bid4 = "B-%s-004" % L
        p4 = os.path.join(ex, "battle_411001_20261005_143004__%s.json" % bid4)
        _write_fake(p4, 411001, bid4, 3, "provisional", 2, 90.0, "None")
        legacy = os.path.join(ex, "battle_700817_20260101_000000.json")
        _write_legacy(legacy, 700817, 12.0, "Win")
        legacy_hash = sha256_file(legacy)

        class A(object):
            pass

        a = A(); a.exports = ex; a.json = None
        check("list sees 5 identified files + 1 legacy", len(scan(ex)) == 6, str(len(scan(ex))))

        a = A(); a.exports = ex; a.ids = [ids[3][0], ids[1][0]]; a.launch = None; a.seq = None
        a.legacy = None; a.out = None; a.request = "two ids"; a.now = ""; a.allow_unfinished = False
        with _capture() as cap:
            rc = cmd_resolve(a)
        check("two full ids resolve in the USER order", rc == 0 and cap["text"].find(ids[3][0]) < cap["text"].find(ids[1][0]),
              "rc=%d" % rc)

        a.ids = [ids[3][0], ids[3][0]]
        with _capture() as cap:
            rc = cmd_resolve(a)
        check("a repeated id is counted once",
              rc == 0 and cap["text"].count("[1] ") == 1 and "counted once" in cap["text"], "rc=%d" % rc)

        a.ids = ["B-%s-009" % L]
        with _capture() as cap:
            rc = cmd_resolve(a)
        check("a missing id refuses the whole group", rc == EX_MISSING, "rc=%d" % rc)

        a.ids = None; a.launch = L; a.seq = [2, 5]
        with _capture() as cap:
            rc = cmd_resolve(a)
        check("launch+seq resolves without a full id", rc == 0, "rc=%d" % rc)

        a.launch = L; a.seq = [2, 2]
        with _capture() as cap:
            rc = cmd_resolve(a)
        check("a duplicated sequence is counted once",
              rc == 0 and cap["text"].count("[1] ") == 1 and "counted once" in cap["text"], "rc=%d" % rc)

        a.launch = None; a.seq = None; a.legacy = [legacy_hash[:16]]
        with _capture() as cap:
            rc = cmd_resolve(a)
        check("a legacy content hash resolves", rc == 0 and cap["text"].find("legacy") >= 0, "rc=%d" % rc)

        a.legacy = ["deadbeef"]
        with _capture() as cap:
            rc = cmd_resolve(a)
        check("an unknown legacy prefix refuses", rc == EX_MISSING, "rc=%d" % rc)

        a.legacy = None; a.ids = [ids[2][0]]
        # a file whose NAME claims another id must be refused
        wrong = os.path.join(ex, "battle_411001_20261005_143022__" + ids[1][0] + ".json")
        _write_fake(wrong, 411001, ids[2][0], 1, "final", 0, 102.0, "Lose")
        with _capture() as cap:
            rc = cmd_resolve(a)
        check("a name/body id conflict refuses", rc == EX_AMBIGUOUS, "rc=%d" % rc)
        os.remove(wrong)

        # selection -> re-check
        sel = os.path.join(tmp, "sel.json")
        a.out = sel
        with _capture() as cap:
            rc = cmd_resolve(a)
        check("resolve writes a selection", rc == 0 and os.path.isfile(sel), "rc=%d" % rc)
        with io.open(sel, "r", encoding="utf-8") as fh:
            s = json.load(fh)
        check("the selection records path/id/revision/hash/order",
              s["items"][0]["path"] and s["items"][0]["revision"] == 1 and len(s["items"][0]["sha256"]) == 64
              and s["order"], "")
        check("re-checking an untouched selection passes", recheck(s) == [], "")

        _write_fake(ids[2][1], 411001, ids[2][0], 2, "final", 0, 103.0, "Lose")
        check("re-checking a CHANGED file fails", len(recheck(s)) == 1, str(recheck(s)))

        # unfinished samples are listed but not compared
        a2 = A(); a2.selection = sel; a2.out = None; a2.applicability = None; a2.allow_unfinished = False
        a2.ids = [bid4]; a2.launch = None; a2.seq = None; a2.legacy = None; a2.request = ""; a2.now = ""
        a2.exports = ex; a2.json = None
        sel2 = os.path.join(tmp, "sel2.json")
        a2.out = sel2
        with _capture() as cap:
            rc = cmd_resolve(a2)
        check("an unfinished sample still resolves", rc == 0, "rc=%d" % rc)
        a3 = A(); a3.selection = sel2; a3.out = os.path.join(tmp, "cmp"); a3.applicability = None
        a3.allow_unfinished = False
        with _capture() as cap:
            rc = cmd_compare(a3)
        check("compare refuses an unfinished sample", rc == EX_UNFINISHED, "rc=%d" % rc)
        a3.allow_unfinished = True
        with _capture() as cap:
            rc = cmd_compare(a3)
        check("the diagnostic flag lets it through to contrib.compare", rc != EX_UNFINISHED, "rc=%d" % rc)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    for label, ok, detail in cases:
        print("  [%s] %s%s" % ("PASS" if ok else "FAIL", label, "" if ok else "  (" + detail + ")"))
        if not ok:
            bad += 1
    print("battle_select selftest: %d case(s), %d failed" % (len(cases), bad))
    return 1 if bad else 0


class _capture(object):
    """Capture stdout as TEXT so a case can assert on the printed lines (the console is GBK, so the
    capture is in-memory and never encoded to the console)."""

    def __enter__(self):
        self._buf = io.StringIO()
        self._old = sys.stdout
        sys.stdout = self._buf
        return self

    def __exit__(self, *exc):
        sys.stdout = self._old
        self.__dict__["text"] = self._buf.getvalue()
        return False

    def __getitem__(self, key):
        return self.__dict__[key]


def main():
    ap = argparse.ArgumentParser(description="resolve battle references to files, then compare them")
    sub = ap.add_subparsers(dest="cmd")
    p = sub.add_parser("list")
    p.add_argument("--exports", default=DEFAULT_EXPORTS)
    p.add_argument("--json", default=None)
    p = sub.add_parser("resolve")
    p.add_argument("--exports", default=DEFAULT_EXPORTS)
    p.add_argument("--ids", nargs="*", default=None)
    p.add_argument("--launch", default=None)
    p.add_argument("--seq", nargs="*", type=int, default=None)
    p.add_argument("--legacy", nargs="*", default=None)
    p.add_argument("--out", default=None)
    p.add_argument("--request", default=None)
    p.add_argument("--now", default=None)
    p.add_argument("--allow-unfinished", action="store_true")
    p = sub.add_parser("compare")
    p.add_argument("--selection", required=True)
    p.add_argument("--out", default=None)
    p.add_argument("--applicability", default=None)
    p.add_argument("--allow-unfinished", action="store_true")
    ap.add_argument("--selftest", action="store_true")
    a = ap.parse_args()
    if a.selftest:
        return selftest()
    if a.cmd == "list":
        return cmd_list(a)
    if a.cmd == "resolve":
        return cmd_resolve(a)
    if a.cmd == "compare":
        return cmd_compare(a)
    ap.print_help()
    return EX_USAGE


if __name__ == "__main__":
    sys.exit(main())
