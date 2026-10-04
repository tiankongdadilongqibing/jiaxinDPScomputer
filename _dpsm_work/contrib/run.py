# -*- coding: utf-8 -*-
"""CLI: python -m contrib.run [export.json] [--team 1] [--legacy]

Defaults to the newest non-training battle export. Writes:
  _dpsm_work/contrib/reports/report_<stem>.txt   full v1 report
  _dpsm_work/contrib/reports/report_<stem>.json  contribution JSON draft
  ... .legacy.txt                                Stage-0 compatible view (with --legacy)
Stdout is ASCII only (console is GBK); the reports themselves are UTF-8.
"""
from __future__ import annotations
import argparse, glob, io, json, os, sys

from . import loader, aggregate
from . import validate as vmod
from . import report_text, report_json

HERE = os.path.dirname(os.path.abspath(__file__))
WORK = os.path.dirname(HERE)
ROOT = os.path.dirname(WORK)
EXPORTS = os.path.join(ROOT, "BepInEx", "plugins", "DpsMeter", "exports")
OUTDIR = os.path.join(HERE, "reports")
if WORK not in sys.path:
    sys.path.insert(0, WORK)

import contribution_gate as gate          # noqa: E402  (one status vocabulary for every gate)

# validate.py issue level -> gate status. INFO is not a caveat, so it maps to PASS.
_LEVEL_STATUS = {"ERROR": gate.ERROR, "WARN": gate.WARNING, "INFO": gate.PASS}


def newest_export(exclude_training=True, pattern="battle_*.json"):
    cands = glob.glob(os.path.join(EXPORTS, pattern))
    if exclude_training:
        cands = [p for p in cands if not os.path.basename(p).startswith("battle_9999")]
    return sorted(cands, key=os.path.getmtime)[-1] if cands else None


def run(path, team=1, text=None, js=None, legacy=None, allow_training=False):
    export = loader.load(path)
    training = loader.training_mode(export)
    if training and not allow_training:
        raise SystemExit(
            "REFUSED: quest %s is the training ground (special x0.03 mechanics).\n"
            "The normal contribution model does not apply there; pass --allow-training to\n"
            "analyse it anyway (results are marked modelApplicable=false and must not be\n"
            "compared with normal battles)." % export.quest)
    an = aggregate.analyze(export, team)
    an.diagnostics["modelApplicable"] = not training
    issues, summary = vmod.check(an, export)
    os.makedirs(OUTDIR, exist_ok=True)
    stem = os.path.splitext(os.path.basename(path))[0]
    text = text or os.path.join(OUTDIR, "report_%s.txt" % stem)
    js = js or os.path.join(OUTDIR, "report_%s.json" % stem)
    full = report_text.render_full(an, export, issues, summary)
    with io.open(text, "w", encoding="utf-8") as fh:
        fh.write(full)
    doc = report_json.to_json(an, export, issues, summary)
    with io.open(js, "w", encoding="utf-8") as fh:
        fh.write(json.dumps(doc, ensure_ascii=False, indent=1))
    legacy_path = None
    if legacy is not False and (legacy or legacy is None):
        legacy_path = legacy if isinstance(legacy, str) else os.path.join(OUTDIR, "report_%s.legacy.txt" % stem)
        with io.open(legacy_path, "w", encoding="utf-8") as fh:
            fh.write(report_text.render_legacy(an, export))
    return {"export": export, "analysis": an, "issues": issues, "summary": summary,
            "text": text, "json": js, "legacy": legacy_path, "training": training}


def _ascii_summary(res):
    an = res["analysis"]
    s = res["summary"]
    errs = [i for i in res["issues"] if i[0] == "ERROR"]
    warns = [i for i in res["issues"] if i[0] == "WARN"]
    print("export=%s version=%s quest=%s training=%s" %
          (res["export"].name, res["export"].version, res["export"].quest, res["training"]))
    print("analyzable=%.0f credited=%.4f%% unattributed=%.4f%% pool=%.4f%%" %
          (an.analyzable, 100 * s.get("credited_share", 0), 100 * s.get("unattributed_share", 0),
           100 * s.get("pool_share", 0)))
    print("perHitMaxErr=%.3e foldResolved=%s foldUnresolved=%s zero=%s noop=%s" %
          (s.get("per_hit_max_error", 0), s["fold_accounting"]["resolved"],
           s["fold_accounting"]["unresolved"], s["fold_accounting"]["zero_factor"],
           s["fold_accounting"]["noop_factor"]))
    print("byUnitGiven=%s byUnitMadness=%s nameFallbacks=%s" %
          (s.get("byUnit_given"), s.get("byUnit_madness"), an.diagnostics.get("attackerNameFallbacks")))
    print("actorIdentityErr=%.6f analyzeVsTotals=%.0f" %
          (s.get("actor_identity_error", 0), s.get("analyzable_vs_totals", 0) or 0))
    print("issues: ERROR=%d WARN=%d" % (len(errs), len(warns)))
    for lv, code, msg in res["issues"]:
        safe = msg.encode("ascii", "replace").decode("ascii")
        print("  [%s %s] %s" % (lv, code, safe))
    top = sorted(an.actors.values(), key=lambda a: -a.total)[:5]
    print("top5 totalCredit (key,base,self,assist,total,share):")
    for a in top:
        print("  key=%s base=%.0f self=%.0f assist=%.0f total=%.0f share=%.4f%%" %
              (a.key, a.base, a.self_rule, a.assist, a.total,
               100 * a.total / an.analyzable if an.analyzable else 0))
    print("paths: text=%s json=%s legacy=%s" % (res["text"], res["json"], res["legacy"]))


def main(argv=None):
    ap = argparse.ArgumentParser(description="DpsMeter contribution analysis v1")
    ap.add_argument("export", nargs="?", help="export json (default: newest battle_*.json, training excluded)")
    ap.add_argument("--team", type=int, default=1)
    ap.add_argument("--text", help="full report path")
    ap.add_argument("--json", help="contribution json path")
    ap.add_argument("--no-legacy", action="store_true")
    ap.add_argument("--allow-training", action="store_true",
                    help="analyse a training-ground export (model does not apply)")
    a = ap.parse_args(argv)
    path = a.export or newest_export()
    if not path or not os.path.exists(path):
        print("%s: export not found (looked under %s)" % (gate.DATA_MISSING, EXPORTS))
        return gate.EXIT_DATA_MISSING
    try:
        res = run(path, team=a.team, text=a.text, js=a.json,
                  legacy=False if a.no_legacy else None, allow_training=a.allow_training)
    except SystemExit as exc:
        # the training-ground refusal is a deliberate "this check does not apply", not a failure
        print("%s: %s" % (gate.LEGACY_NOT_APPLICABLE, str(exc)))
        return gate.EXIT_LEGACY_NOT_APPLICABLE
    _ascii_summary(res)
    # P0-A: the exit code is the gate's, computed from the same status vocabulary the other gates
    # use -- WARNING and LEGACY_NOT_APPLICABLE are 0, DATA_MISSING/ERROR are not.
    statuses = [_LEVEL_STATUS.get(i[0], gate.PASS) for i in res["issues"]]
    if res.get("training"):
        statuses.append(gate.LEGACY_NOT_APPLICABLE)
    status = gate.combine(statuses)
    print("gateStatus=%s exit=%d" % (status, gate.exit_code(status)))
    return gate.exit_code(status)


if __name__ == "__main__":
    sys.exit(main())
