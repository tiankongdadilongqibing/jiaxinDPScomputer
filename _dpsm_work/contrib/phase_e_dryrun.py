# -*- coding: utf-8 -*-
"""Phase E dry run: would the acceptance gates accept a real contribution section?

Takes a real export, computes the contribution section with the verified offline core, merges that
section into the export document (exactly what the plugin will do), then runs BOTH gates on the
merged document:
  1. _dpsm_work/check_export_schema.py  -> shape + value identities
  2. _dpsm_work/contrib/crosscheck.py   -> plugin section vs independent offline recomputation
Also measures the section's JSON size, which is the cost the plugin export will pay.

Usage: python -m contrib.phase_e_dryrun [export.json] [--section section.json]
With --section the given file is used as the contribution section instead of the Python core's own
output -- that is how another implementation (e.g. the C# prototype in _dpsm_work/contrib_cs) is
verified against the same two gates.
Exit 0 only when both gates accept the merged document.
"""
from __future__ import annotations
import glob, io, json, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
WORK = os.path.dirname(HERE)
ROOT = os.path.dirname(WORK)
EXPORTS = os.path.join(ROOT, "BepInEx", "plugins", "DpsMeter", "exports")
OUTDIR = os.path.join(HERE, "reports")
if WORK not in sys.path:
    sys.path.insert(0, WORK)

from . import aggregate, loader, report_json   # noqa: E402
from . import crosscheck                       # noqa: E402
from . import validate as vmod                 # noqa: E402
import check_export_schema as schema           # noqa: E402  (guard lives one level up)


def pick():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if args:
        return args[0]
    cands = [p for p in glob.glob(os.path.join(EXPORTS, "battle_*.json"))
             if not os.path.basename(p).startswith("battle_9999")]
    return sorted(cands, key=os.path.getmtime)[-1]


def section_path():
    if "--section" in sys.argv:
        i = sys.argv.index("--section")
        if i + 1 < len(sys.argv):
            return sys.argv[i + 1]
    return None


def main():
    path = pick()
    export = loader.load(path)
    an = aggregate.analyze(export, 1)
    issues, summary = vmod.check(an, export)
    ext = section_path()
    if ext:
        with io.open(ext, encoding="utf-8") as fh:
            sec = json.load(fh)
        if "contribution" in sec:
            sec = sec["contribution"]
    else:
        sec = report_json.to_json(an, export, issues, summary)["contribution"]
    # exact cost of the per-hit audit trail (same math, keep_hits=True)
    an_det = aggregate.analyze(export, 1, keep_hits=True)
    detail = []
    for h in an_det.hits_detail:
        detail.append({
            "t": round(h.time, 2), "atk": h.attacker_key, "vic": h.victim_key,
            "dmg": h.damage, "m": h.multiplier,
            "lines": [[l.owner_key, l.reason, round(l.amount, 3)] for l in h.lines],
        })
    detail_bytes = len(json.dumps(detail, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))
    merged = dict(export.raw)
    merged["contribution"] = sec
    problems, missing, cov = schema.check_export(merged, os.path.basename(path))
    tmp = os.path.join(OUTDIR, "_phase_e_dryrun_tmp.json")
    os.makedirs(OUTDIR, exist_ok=True)
    with io.open(tmp, "w", encoding="utf-8") as fh:
        fh.write(json.dumps(merged, ensure_ascii=False))
    cc = crosscheck.compare(tmp)
    os.remove(tmp)
    sec_bytes = len(json.dumps(sec, ensure_ascii=False).encode("utf-8"))
    export_bytes = os.path.getsize(path)
    lines = []
    lines.append(u"Phase E 干跑(真实导出 + 离线核心算出的 contribution 段)")
    lines.append(u"  导出:%s(%.2f MB)" % (export.name, export_bytes / 1048576.0))
    lines.append(u"  段的来源:%s" % (u"外部文件 " + os.path.basename(ext) if ext else u"离线 Python 核心"))
    lines.append(u"  段大小:%.1f KB(占导出 %.3f%%;actors %d / rules %d / links %d)" %
                 (sec_bytes / 1024.0, 100.0 * sec_bytes / export_bytes,
                  len(sec.get("actors") or []), len(sec.get("rules") or []), len(sec.get("links") or [])))
    lines.append(u"  逐击明细变体大小:%.1f KB(占导出 %.3f%%;%d 击 / %d 条 credit 行)—— 供阶段 E 取舍" %
                 (detail_bytes / 1024.0, 100.0 * detail_bytes / export_bytes,
                  len(an_det.hits_detail), sum(len(h.lines) for h in an_det.hits_detail)))
    lines.append(u"  闸门1 check_export_schema:problems=%d missing=%d" % (len(problems), len(missing)))
    for p in (problems + missing)[:10]:
        lines.append(u"    - " + p)
    lines.append(u"  闸门2 crosscheck:%s(mismatches=%d,checked=%s)" %
                 (cc["status"], len(cc["mismatches"]), cc["checked"]))
    for m in cc["mismatches"][:10]:
        lines.append(u"    - %s offline=%s plugin=%s" % (m["field"], m["offline"], m["plugin"]))
    out = os.path.join(OUTDIR, "phase_e_dryrun_%s.txt" % os.path.splitext(export.name)[0])
    with io.open(out, "w", encoding="utf-8") as fh:
        fh.write(u"\n".join(lines))
    # P0-A: the gate's statuses are PASS/WARNING (exit 0) and ERROR/DATA_MISSING/LEGACY (not 0).
    # Only the first two mean "accepted"; a WARNING caveat (e.g. an offline section that cannot
    # restate the plugin's atkadd rows) must not be reported as a failed dry run.
    ok = (not problems) and (not missing) and cc["status"] in ("OK", "PASS", "WARNING")
    print("export=%s sectionKB=%.1f (%.3f%%) perHitDetailKB=%.1f (%.3f%%) schemaProblems=%d crosscheck=%s" %
          (export.name, sec_bytes / 1024.0, 100.0 * sec_bytes / export_bytes,
           detail_bytes / 1024.0, 100.0 * detail_bytes / export_bytes,
           len(problems) + len(missing), cc["status"]))
    for p in (problems + missing)[:5]:
        print("  SCHEMA " + p.encode("ascii", "replace").decode("ascii"))
    for m in cc["mismatches"][:5]:
        print("  CROSSCHECK " + m["field"].encode("ascii", "replace").decode("ascii"))
    print("report=%s" % out)
    print("DRYRUN=%s" % ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
