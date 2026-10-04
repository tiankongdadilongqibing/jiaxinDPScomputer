# -*- coding: utf-8 -*-
"""Regression evidence: does the new core reproduce the legacy Stage-0 table?

The legacy script (root dpsmeter_contrib.py) writes _dpsm_work/contrib_report.txt.
Both it and the new core use the same log-share convention; this tool re-runs the
new core on the SAME export and diffs every shared column, so any change in numbers
caused by the new core is visible with evidence.

Usage: python -m contrib.legacy_diff
"""
from __future__ import annotations
import io, os, re, sys

from . import loader, aggregate

HERE = os.path.dirname(os.path.abspath(__file__))
WORK = os.path.dirname(HERE)
ROOT = os.path.dirname(WORK)
EXPORTS = os.path.join(ROOT, "BepInEx", "plugins", "DpsMeter", "exports")
LEGACY = os.path.join(WORK, "contrib_report.txt")
OUTDIR = os.path.join(HERE, "reports")


def parse_legacy(path):
    rows = {}
    export_name = None
    with io.open(path, encoding="utf-8") as fh:
        for line in fh:
            if u"——" in line and u".json" in line:
                export_name = line.strip().split(u"——")[-1].strip()
            toks = line.rstrip("\n").split()
            if len(toks) < 10:
                continue
            tail = toks[-9:]
            try:
                vals = [float(t.rstrip("%")) for t in tail]
            except ValueError:
                continue
            name = " ".join(toks[:-9])
            if not name or name.startswith(u"角色"):
                continue
            rows[name] = {"direct": vals[0], "share": vals[1], "base": vals[2], "self": vals[3],
                          "received": vals[4], "mad": vals[5], "given": vals[6],
                          "assist": vals[7], "hits": vals[8]}
    return export_name, rows


def main():
    if not os.path.exists(LEGACY):
        print("legacy report not found: %s" % LEGACY)
        return 2
    export_name, old = parse_legacy(LEGACY)
    path = os.path.join(EXPORTS, export_name or "")
    if not os.path.exists(path):
        print("export named by legacy report not found: %s" % export_name)
        return 2
    export = loader.load(path)
    an = aggregate.analyze(export, 1)
    new = {a.name: a for a in an.actors.values()}
    cols = [("direct", "direct"), ("base", "base"), ("self", "self_rule"),
            ("received", "received"), ("assist", "assist"), ("hits", "hits")]
    worst = {c: 0.0 for c, _ in cols}
    missing = [n for n in old if n not in new]
    extra = [n for n in new if n not in old and (new[n].direct > 0 or new[n].total > 0)]
    lines = []
    lines.append(u"新核心 ↔ 旧 Stage-0 表 逐列对照(同一份导出:%s)" % export_name)
    lines.append(u"口径相同(对数份额);仅比较两表共有的列。差 = 新 - 旧。")
    lines.append(u"")
    hdr = u"%-16s %14s %14s %14s %14s %14s %8s" % (
        u"角色", u"直接(差)", u"基础(差)", u"自身规则(差)", u"受队友赋能(差)", u"为团队赋能(差)", u"出手(差)")
    lines.append(hdr)
    for name in sorted(old, key=lambda n: -old[n]["direct"]):
        a = new.get(name)
        if a is None:
            lines.append(u"%-16s  (新核心无此角色)" % name[:16])
            continue
        cells = []
        for c, attr in cols:
            oldv = old[name][c]
            newv = getattr(a, attr)
            d = newv - oldv
            if c != "hits":
                worst[c] = max(worst[c], abs(d))
            cells.append(u"%13.0f(%+7.0f)" % (newv, d) if c != "hits" else u"%6d(%+4d)" % (newv, d))
        lines.append(u"%-16s %s" % (name[:16], u" ".join(cells)))
    lines.append(u"")
    lines.append(u"最大绝对差:" + u", ".join(
        u"%s=%.0f" % (c, worst[c]) for c, _ in cols if c != "hits"))
    if missing:
        lines.append(u"旧表有、新核心没有的角色: %s" % missing)
    if extra:
        lines.append(u"新核心有、旧表没有的角色: %s" % extra)
    os.makedirs(OUTDIR, exist_ok=True)
    stem = os.path.splitext(os.path.basename(path))[0]
    out = os.path.join(OUTDIR, "legacy_diff_%s.txt" % stem)
    with io.open(out, "w", encoding="utf-8") as fh:
        fh.write(u"\n".join(lines))
    print("rows=%d missing=%d extra=%d" % (len(old), len(missing), len(extra)))
    for c, _ in cols:
        if c != "hits":
            print("  maxAbsDiff %-9s = %.3f" % (c, worst[c]))
    print("report=%s" % out)
    tol = 1.0
    bad = [c for c, _ in cols if c != "hits" and worst[c] > tol]
    print("VERDICT=%s" % ("REGRESSION" if bad else "MATCHES-LEGACY-ON-SHARED-COLUMNS"))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
