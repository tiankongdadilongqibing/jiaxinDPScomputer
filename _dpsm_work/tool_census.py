#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""RF7 (plan section 12): the MECHANICAL half of the tool registry.

Scans the repository's Python scripts and reports what can be established from the files themselves:
path, size, the module docstring's first line, imports of OTHER repository scripts, whether the script
appears in the acceptance pipeline (n0_acceptance.py), and whether it has a --selftest.

The JUDGEMENT fields (kind, status, expected_exit, outputs, input_versions, evidence) live in
tool_registry.json and are NEVER overwritten by a re-seed: check_tool_registry.py --verify compares the
two and fails when a script is missing from the registry, when a pipeline script is not classified
active, or when the number of still-unclassified scripts grows.

Usage:
    python tool_census.py                 # print what a scan finds (ASCII only)
    python tool_census.py --seed          # merge a scan into tool_registry.json
"""
import io
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
REGISTRY = os.path.join(HERE, "tool_registry.json")
n0_PATH = os.path.join(HERE, "n0_acceptance.py")

# Roots that contain tool scripts, relative to the repository root. "." = the repository root itself.
ROOTS = ["_dpsm_work", ".", "review_contrib_core"]
# R62: the repository DECLARES one temp location (AGENTS section 4: scratch files live at
# `_dpsm_work/tests/_*.tmp` and that rule is ignored by git), so the scanner must honour the same rule --
# otherwise ANOTHER agent's scratch file reddens the tool registry and the acceptance run with it
# (measured 2026-10-05: 35 `_as*.tmp.py` left behind by a parallel analysis session failed rule B).
SKIP_NAMES = ("_",)          # basename prefix for a declared scratch file
SKIP_SUFFIXES = (".tmp", ".tmp.py")
SKIP_PARTS = ("/obj/", "/bin/", "/.git/", "/batch_inputs/", "/acceptance_", "/__pycache__/",
              "/node_modules/", "/Tools/")

# Keys filled from the scan every time. Everything else is hand-owned and preserved by --seed.
MACHINE_KEYS = ("lines", "purpose", "imports_local", "in_pipeline", "has_selftest")

IMPORT_RE = re.compile(r"^[ \t]*(?:from|import)[ \t]+([A-Za-z_][A-Za-z0-9_.]*)", re.M)


def read(path):
    with io.open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def first_purpose(text):
    """The first line of the module docstring, else the first comment that is not a shebang/coding line."""
    m = re.search(r'"""(.*?)(?:"""|$)', text, re.S)
    if m:
        body = m.group(1)
        for raw in body.splitlines():
            line = raw.strip()
            if line:
                return line
    for raw in text.splitlines():
        line = raw.strip()
        if line.startswith("#") and "coding" not in line and not line.startswith("#!"):
            return line.lstrip("# ").strip()
    return ""


def script_paths():
    """Every candidate script, as repo-relative forward-slash paths, sorted."""
    out = []
    seen = set()
    for root in ROOTS:
        abs_root = os.path.join(REPO, root) if root != "." else REPO
        if not os.path.isdir(abs_root):
            continue
        if root == ".":
            # the repository root itself only: subdirectories are covered by the other roots
            names = [n for n in os.listdir(abs_root) if n.endswith(".py")]
            cands = [os.path.join(abs_root, n) for n in names]
        else:
            cands = []
            for dirpath, dirnames, filenames in os.walk(abs_root):
                if "/__pycache__" in dirpath.replace("\\", "/"):
                    continue
                for n in filenames:
                    if n.endswith(".py"):
                        cands.append(os.path.join(dirpath, n))
        for p in cands:
            rel = os.path.relpath(p, REPO).replace("\\", "/")
            if rel in seen:
                continue
            if any(part in ("/" + rel) for part in SKIP_PARTS):
                continue
            base = os.path.basename(rel)
            if base.startswith(SKIP_NAMES) and base.endswith(SKIP_SUFFIXES):
                continue
            seen.add(rel)
            out.append(rel)
    out.sort()
    return out


def pipeline_paths(n0_text=None):
    """The scripts/modules the acceptance pipeline runs, as repo-relative paths."""
    text = n0_text if n0_text is not None else (read(n0_PATH) if os.path.isfile(n0_PATH) else "")
    out = set()
    # only a literal ending in .py is a SCRIPT: join(HERE, ...) is also used to build output paths
    for name in re.findall(r'os\.path\.join\(HERE, "([A-Za-z0-9_/.-]+\.py)"\)', text):
        out.add("_dpsm_work/" + name)
    for mod in re.findall(r'"-m", "([A-Za-z0-9_.]+)"', text):
        # a -m invocation is relative to the pipeline cwd (_dpsm_work), and may be a package
        rel = mod.replace(".", "/")
        cands = ["_dpsm_work/" + rel + ".py", rel + ".py", "_dpsm_work/" + rel + "/__init__.py"]
        found = None
        for c in cands:
            if os.path.isfile(os.path.join(REPO, c)):
                found = c
                break
        out.add(found or cands[0])
    return out


def scan():
    paths = script_paths()
    stems = dict((os.path.basename(p)[:-3], p) for p in paths)
    n0_text = read(n0_PATH) if os.path.isfile(n0_PATH) else ""
    in_pipe = pipeline_paths(n0_text)
    result = {}
    for rel in paths:
        text = read(os.path.join(REPO, rel))
        imports = []
        for name in IMPORT_RE.findall(text):
            head = name.split(".")[0]
            if head in stems and stems[head] != rel:
                imports.append(stems[head])
        result[rel] = {
            "lines": len(text.splitlines()),
            "purpose": first_purpose(text),
            "imports_local": sorted(set(imports)),
            "in_pipeline": rel in in_pipe,
            "has_selftest": "--selftest" in text,
        }
    return result


def load_registry():
    if not os.path.isfile(REGISTRY):
        return {"_comment": "RF7 tool registry. Mechanical keys are refreshed by tool_census.py --seed; the judgement keys are hand-owned.", "entries": {}, "pins": {"unclassified_max": 0}}
    with io.open(REGISTRY, "r", encoding="utf-8") as fh:
        return json.load(fh)


def save_registry(reg):
    with io.open(REGISTRY, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(json.dumps(reg, ensure_ascii=False, indent=1, sort_keys=True))
        fh.write(u"\n")


def seed():
    reg = load_registry()
    entries = reg.setdefault("entries", {})
    fresh = scan()
    added = 0
    for rel, machine in fresh.items():
        ent = entries.get(rel)
        if ent is None:
            ent = {"kind": "unclassified", "status": "unclassified", "expected_exit": "",
                   "outputs": [], "input_versions": "", "evidence": ""}
            entries[rel] = ent
            added += 1
        for key in MACHINE_KEYS:
            if key == "purpose" and (ent.get("purpose") or "").strip():
                continue
            ent[key] = machine[key]
    gone = [rel for rel in entries if rel not in fresh]
    for rel in gone:
        entries.pop(rel)
    save_registry(reg)
    unclassified = len([1 for e in entries.values() if e.get("status") == "unclassified"])
    print("tool census: scanned=%d registered=%d added=%d removed=%d unclassified=%d"
          % (len(fresh), len(entries), added, len(gone), unclassified))
    return 0


def invokers(argv):
    """RF7g (evidence sheet, NOT a gate): who NAMES each unclassified script in their own text?

    The lesson from round 29 is that this repository mostly runs its tools by NAME (the pipeline invokes them
    through subprocess), so "nobody imports it" says nothing about liveness. This prints, for every
    unclassified registry entry, the registered scripts whose SOURCE TEXT contains its basename -- the raw
    evidence a human needs to judge each one. It deliberately does not decide anything and is not wired into
    the acceptance: a mention can be a real call or a comment, and only a reader can tell them apart.
    """
    reg = load_registry()
    ents = reg.get("entries", {})
    todo = sorted(r for r, e in ents.items() if e.get("status") == "unclassified")
    alive = sorted(r for r, e in ents.items() if e.get("status") == "active")
    texts = {}
    for rel in sorted(ents):
        p = os.path.join(REPO, rel)
        try:
            with io.open(p, "r", encoding="utf-8", errors="replace") as fh:
                texts[rel] = fh.read()
        except Exception:
            texts[rel] = ""
    print("invoker sheet: unclassified=%d active=%d" % (len(todo), len(alive)))
    for rel in todo:
        base = os.path.basename(rel)
        named = sorted(s for s, t in texts.items() if s != rel and base in t)
        live_named = sorted(s for s in named if ents.get(s, {}).get("status") == "active")
        print("  %s" % rel)
        print("     named-by(all)=%s" % (", ".join(named) if named else "none"))
        print("     named-by(ACTIVE)=%s" % (", ".join(live_named) if live_named else "none"))
    return 0

def main(argv):
    if "--invokers" in argv:
        return invokers(argv)
    if "--seed" in argv:
        return seed()
    fresh = scan()
    reg = load_registry()
    known = reg.get("entries", {})
    missing = sorted(set(fresh) - set(known))
    stale = sorted(set(known) - set(fresh))
    pipe = sorted(rel for rel, m in fresh.items() if m["in_pipeline"])
    print("tool census: scanned=%d registered=%d missing=%d stale=%d in_pipeline=%d"
          % (len(fresh), len(known), len(missing), len(stale), len(pipe)))
    for rel in missing:
        print("  MISSING  " + rel)
    for rel in stale:
        print("  STALE    " + rel)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))