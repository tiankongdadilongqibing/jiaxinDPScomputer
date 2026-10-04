#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""RF7 (plan section 12): the tool registry guard.

WHY. The repository has four hand-maintained lists that can silently drift apart: the acceptance
pipeline (n0_acceptance.py), the doc set (check_docs_123.py), the source scanner (refactor_final_check.py),
and the documents that name a tool. This guard makes ONE registry (tool_registry.json) the reference and
fails when reality and the registry disagree:

    A  a registered path does not exist
    B  a scanned script is not registered at all
    C  a script the pipeline RUNS is not classified active (or its in_pipeline flag is missing)
    D  an entry claims in_pipeline but the pipeline does not run it
    E  an active entry does not declare expected_exit / outputs / purpose / kind
    F  the number of still-unclassified scripts grew past the pin in the registry

F is the "index only, no deletion" rule from the plan: every script is registered (so nothing hides),
but the ones nobody has judged yet are counted, and that count may only go DOWN. Classifying a script is
a one-line edit; adding one is a red build until it is classified.

Usage:
    python check_tool_registry.py                 # verify (exit 0 clean / 1 findings)
    python check_tool_registry.py --write         # regenerate TOOL-REGISTRY.md from the registry
    python check_tool_registry.py --selftest      # tamper a temp registry/n0 copy, expect the right failure
"""
import io
import json
import os
import shutil
import sys
import tempfile

import tool_census

HERE = os.path.dirname(os.path.abspath(__file__))
REGISTRY = os.path.join(HERE, "tool_registry.json")
REPORT = os.path.join(HERE, "TOOL-REGISTRY.md")


def read(path):
    with io.open(path, "r", encoding="utf-8") as fh:
        return fh.read()


def write(path, text):
    with io.open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)


def load_registry(path=None):
    with io.open(path or REGISTRY, "r", encoding="utf-8") as fh:
        return json.load(fh)


def verify(reg, fresh, pipe, repo=None):
    """Every check, as a list of ASCII failure lines."""
    repo = repo or tool_census.REPO
    entries = reg.get("entries", {})
    fail = []

    # A: the registry does not name a file that is not there
    for rel in sorted(entries):
        if not os.path.isfile(os.path.join(repo, rel)):
            fail.append("A registered path is missing on disk: " + rel)

    # B: every scanned script is registered
    for rel in sorted(set(fresh) - set(entries)):
        fail.append("B scanned but NOT registered: " + rel)

    # C + D: the pipeline and the in_pipeline flags agree, and pipeline scripts are active
    for rel in sorted(pipe):
        ent = entries.get(rel)
        if ent is None:
            fail.append("C the pipeline runs an unregistered script: " + rel)
        elif not ent.get("in_pipeline"):
            fail.append("C the pipeline runs this but in_pipeline is not set: " + rel)
        elif ent.get("status") != "active":
            fail.append("C the pipeline runs this but its status is %r: %s"
                        % (ent.get("status"), rel))
    for rel in sorted(entries):
        if entries[rel].get("in_pipeline") and rel not in pipe:
            fail.append("D in_pipeline is set but the pipeline does not run it: " + rel)

    # E: an active entry declares what a user of it needs to know
    for rel in sorted(entries):
        ent = entries[rel]
        if ent.get("status") != "active":
            continue
        if not (ent.get("purpose") or "").strip():
            fail.append("E active entry has no purpose: " + rel)
        if not (ent.get("expected_exit") or "").strip():
            fail.append("E active entry has no expected_exit: " + rel)
        if not ent.get("outputs"):
            fail.append("E active entry declares no outputs (use [\"none (read-only)\"]): " + rel)
        if ent.get("kind") in (None, "", "unclassified"):
            fail.append("E active entry has no kind: " + rel)

    # G (RF7b, refined in RF7f): an `indexed` entry claims "nothing LIVE depends on it", and LIVENESS is the
    # transitive closure of imports starting from the seeds that are live by definition -- the ACTIVE entries
    # and whatever the pipeline runs. So a script imported only by other indexed (dead) scripts is itself dead
    # and may be indexed; one imported by a live script may not. The earlier rule ("no importer at all") was
    # STRICTER than the claim it checked, which is why a whole dead subtree could not be classified at all.
    live = set(rel for rel, ent in entries.items()
               if ent.get("status") == "active" or ent.get("in_pipeline") or rel in pipe)
    changed = True
    while changed:
        changed = False
        for rel in sorted(live):
            for dep in (entries.get(rel, {}).get("imports_local") or []):
                if dep not in live:
                    live.add(dep)
                    changed = True
    live_importer = {}
    for rel, ent in entries.items():
        if rel not in live:
            continue
        for dep in (ent.get("imports_local") or []):
            live_importer.setdefault(dep, rel)
    for rel in sorted(entries):
        ent = entries[rel]
        if ent.get("status") != "indexed":
            continue
        if ent.get("in_pipeline") or rel in pipe:
            fail.append("G marked indexed but the pipeline runs it: " + rel)
        if rel in live_importer:
            fail.append("G marked indexed but a LIVE script imports it (%s): %s" % (live_importer[rel], rel))
    # K (RF7m): the `referenced-input` status -- a file that nobody runs and nothing imports, but that an ACTIVE
    # gate READS (contrib/report_text.py is read by check_docs_123.py for CJK damage). Calling such a file
    # "indexed" would be false (a live tool breaks if it goes away) and calling it "active" would be false too
    # (nobody invokes it). The claim is computed: each listed reader must be an active entry whose own source
    # text contains the file basename, and the file must not be in the pipeline or a live import target.
    for rel in sorted(entries):
        ent = entries[rel]
        if ent.get("status") != "referenced-input":
            continue
        readers = ent.get("inputs_of") or []
        if not readers:
            fail.append("K referenced-input declares no inputs_of: " + rel)
            continue
        base = os.path.basename(rel)
        ok = False
        for rd in readers:
            if entries.get(rd, {}).get("status") != "active":
                continue
            try:
                with io.open(os.path.join(tool_census.REPO, rd), "r", encoding="utf-8", errors="replace") as fh:
                    txt = fh.read()
            except Exception:
                txt = ""
            if base in txt:
                ok = True
        if not ok:
            fail.append("K referenced-input is not actually read by any ACTIVE listed reader: " + rel)
        if rel in pipe or ent.get("in_pipeline"):
            fail.append("K referenced-input is in the pipeline (it should be active): " + rel)
    # J (RF7f): the liveness closure above follows IMPORTS, and imports are not how this repository mostly
    # runs things -- the tools are CLIs invoked from the pipeline script and from each other by name. Relying
    # on imports alone marked n0_acceptance.py and tests/negative_control.py as dead when I first computed it,
    # which is why this check exists: an `indexed` entry must not be NAMED inside the pipeline script's text.
    try:
        with io.open(os.path.join(tool_census.REPO, "_dpsm_work", "n0_acceptance.py"),
                     "r", encoding="utf-8") as fh:
            n0_text = fh.read()
    except Exception:
        n0_text = ""
    for rel in sorted(entries):
        if entries[rel].get("status") != "indexed":
            continue
        base = os.path.basename(rel)
        if base and base in n0_text:
            fail.append("J marked indexed but the pipeline script names it: " + rel)
    # F: the unclassified count may only go down
    unclassified = len([1 for e in entries.values() if e.get("status") == "unclassified"])
    pin = reg.get("pins", {}).get("unclassified_max")
    if pin is None:
        fail.append("F the registry has no pins.unclassified_max")
    elif unclassified > pin:
        fail.append("F unclassified grew to %d (pin %d): classify the new script(s) in tool_registry.json"
                    % (unclassified, pin))
    return fail


def render_md(reg, fresh, pipe):
    entries = reg.get("entries", {})
    lines = []
    lines.append(u"# 工具注册表(RF7)")
    lines.append(u"")
    lines.append(u"> 本文件由 `check_tool_registry.py --write` 从 `tool_registry.json` **生成**,不要手改。")
    lines.append(u"> 机械字段(行数/首行说明/本地导入/是否在验收流水线/是否有自测)来自 `tool_census.py` 的扫描;")
    lines.append(u"> 判断字段(kind / status / expected_exit / outputs / input_versions / evidence)由人工维护。")
    lines.append(u"")
    unclassified = len([1 for e in entries.values() if e.get("status") == "unclassified"])
    active = len([1 for e in entries.values() if e.get("status") == "active"])
    lines.append(u"| 项 | 值 |")
    lines.append(u"|---|---|")
    lines.append(u"| 已注册脚本 | %d |" % len(entries))
    lines.append(u"| 其中活跃(有 CLI、有预期退出码与输出声明) | %d |" % active)
    lines.append(u"| 尚未判定(只登记,计数只能下降) | %d / 上限 %s |" % (unclassified, reg.get("pins", {}).get("unclassified_max")))
    lines.append(u"| 验收流水线实际运行的脚本 | %d |" % len(pipe))
    lines.append(u"")
    lines.append(u"## 活跃工具")
    lines.append(u"")
    lines.append(u"| 脚本 | 类别 | 用途 | 预期退出码 | 输出 | 自测 | 在流水线 |")
    lines.append(u"|---|---|---|---|---|---|---|")
    for rel in sorted(entries):
        ent = entries[rel]
        if ent.get("status") != "active":
            continue
        lines.append(u"| `%s` | %s | %s | %s | %s | %s | %s |" % (
            rel, ent.get("kind", ""), (ent.get("purpose") or "").strip(),
            (ent.get("expected_exit") or "").strip(),
            u"; ".join(ent.get("outputs") or []),
            u"yes" if ent.get("has_selftest") else u"no",
            u"yes" if ent.get("in_pipeline") else u"no"))
    lines.append(u"")
    lines.append(u"## 尚未判定的脚本(只登记)")
    lines.append(u"")
    lines.append(u"| 脚本 | 行数 | 首行说明 | 本地导入 |")
    lines.append(u"|---|---|---|---|")
    for rel in sorted(entries):
        ent = entries[rel]
        if ent.get("status") == "active":
            continue
        lines.append(u"| `%s` | %d | %s | %s |" % (
            rel, ent.get("lines", 0), (ent.get("purpose") or u"").replace(u"|", u"/"),
            u", ".join(os.path.basename(x) for x in (ent.get("imports_local") or []))))
    lines.append(u"")
    return u"\n".join(lines)


def selftest():
    fresh = tool_census.scan()
    pipe = tool_census.pipeline_paths()
    reg = load_registry()
    entries = reg["entries"]
    pipe_rel = sorted(pipe)[0]
    other_rel = sorted(set(fresh) - pipe)[0]
    indexed_rels = sorted(r for r, e in entries.items() if e.get("status") == "indexed")
    fails = []

    def case(name, mutate, want):
        tampered = json.loads(json.dumps(reg))
        mutate(tampered)
        got = verify(tampered, fresh, pipe)
        ok = any(want in f for f in got)
        print("  [%s] %s" % ("PASS" if ok else "FAIL", name))
        if not ok:
            fails.append(name)

    def _missing(reg2):
        reg2["entries"]["_dpsm_work/definitely_not_here.py"] = {"status": "unclassified"}
    case("a registered path that is not on disk is caught (A)", _missing, "A registered path is missing")

    def _unregistered(reg2):
        reg2["entries"].pop(other_rel, None)
    case("a scanned script missing from the registry is caught (B)", _unregistered, "B scanned but NOT registered")

    def _pipeline_unclassified(reg2):
        reg2["entries"][pipe_rel]["status"] = "unclassified"
    case("a pipeline script that is not active is caught (C)", _pipeline_unclassified, "C the pipeline runs this but its status")

    def _pipeline_missing(reg2):
        reg2["entries"].pop(pipe_rel, None)
    case("a pipeline script missing from the registry is caught (C)", _pipeline_missing, "C the pipeline runs an unregistered script")

    def _bogus_pipeline(reg2):
        reg2["entries"][other_rel]["in_pipeline"] = True
    case("an in_pipeline flag the pipeline disagrees with is caught (D)", _bogus_pipeline, "D in_pipeline is set but the pipeline")

    def _no_exit(reg2):
        reg2["entries"][pipe_rel]["expected_exit"] = ""
    case("an active entry without an expected exit code is caught (E)", _no_exit, "E active entry has no expected_exit")

    def _no_outputs(reg2):
        reg2["entries"][pipe_rel]["outputs"] = []
    case("an active entry without declared outputs is caught (E)", _no_outputs, "E active entry declares no outputs")

    # RF7f: G needs its own falsifiers, and the second one is the point of the refinement -- a DEAD script
    # importing another dead script must not be reported, or the rule would block the very classification it
    # exists to police.
    def _live_imports_indexed(reg2):
        reg2["entries"][pipe_rel]["imports_local"] = [indexed_rels[0]]
    case("an indexed script imported by a LIVE script is caught (G)", _live_imports_indexed,
         "G marked indexed but a LIVE script imports it")

    def _dead_imports_indexed(reg2):
        reg2["entries"][indexed_rels[0]]["imports_local"] = [indexed_rels[1]]

    def case_absent(name, mutate, unjust):
        tampered = json.loads(json.dumps(reg))
        mutate(tampered)
        got = verify(tampered, fresh, pipe)
        ok = not any(unjust in f for f in got)
        print("  [%s] %s" % ("PASS" if ok else "FAIL", name))
        if not ok:
            fails.append(name)
    case_absent("an indexed script imported only by a DEAD script is NOT reported (G)", _dead_imports_indexed,
                "G marked indexed but a LIVE script imports it")

    def _k_no_readers(reg2):
        for r, x in reg2["entries"].items():
            if x.get("status") == "referenced-input":
                x["inputs_of"] = []
                break
    case("a referenced-input with no declared reader is caught (K)", _k_no_readers,
         "K referenced-input declares no inputs_of")

    def _k_fake_reader(reg2):
        for r, x in reg2["entries"].items():
            if x.get("status") == "referenced-input":
                x["inputs_of"] = ["_dpsm_work/contrib/legacy_diff.py"]
                break
    case("a referenced-input whose reader is not active (or does not read it) is caught (K)", _k_fake_reader,
         "K referenced-input is not actually read by any ACTIVE listed reader")

    def _pin_down(reg2):
        reg2["pins"]["unclassified_max"] = 0
    case("the unclassified pin is enforced (F)", _pin_down, "F unclassified grew to")

    print("---- selftest: %s" % ("PASS" if not fails else "FAIL %s" % fails))
    return 0 if not fails else 1


def main(argv):
    if "--selftest" in argv:
        return selftest()
    fresh = tool_census.scan()
    pipe = tool_census.pipeline_paths()
    reg = load_registry()
    if "--write" in argv:
        write(REPORT, render_md(reg, fresh, pipe))
        print("wrote " + REPORT)
        return 0
    fail = verify(reg, fresh, pipe)
    active = len([1 for e in reg["entries"].values() if e.get("status") == "active"])
    unclassified = len([1 for e in reg["entries"].values() if e.get("status") == "unclassified"])
    print("tool registry: registered=%d active=%d unclassified=%d (pin %s) pipeline=%d"
          % (len(reg["entries"]), active, unclassified, reg.get("pins", {}).get("unclassified_max"), len(pipe)))
    for line in fail:
        print("  FAIL " + line)
    print("---- tool registry: %s" % ("PASS" if not fail else "FAIL %d" % len(fail)))
    return 0 if not fail else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
