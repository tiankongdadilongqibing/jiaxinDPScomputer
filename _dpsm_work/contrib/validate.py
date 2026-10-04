# -*- coding: utf-8 -*-
"""Identity, accounting and coverage checks (dictionary section 9)."""
from __future__ import annotations
import os
import sys

# Make the module runnable both as `python -m contrib.validate` and as a plain script
# (`python _dpsm_work/contrib/validate.py`); the P0-A CLI below is documented both ways.
if __package__ in (None, ""):
    _WORK = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    if _WORK not in sys.path:
        sys.path.insert(0, _WORK)
    import contrib            # noqa: E402
    sys.modules.setdefault("contrib.validate", sys.modules[__name__])
    __package__ = "contrib"

from .model import RESOLVED_REASONS, UNRESOLVED_REASONS

TOL = 1e-9


def check(an, export):
    """Return (issues, summary). issues is a list of (level, code, message)."""
    issues = []
    s = {}

    def add(level, code, msg):
        issues.append((level, code, msg))

    # I1: per-hit credit identity
    bad = an.diagnostics.get("per_hit_bad", 0)
    s["per_hit_max_error"] = an.diagnostics.get("per_hit_max_error", 0.0)
    if bad:
        add("ERROR", "I1", "%d hits break credit+unattributed == damage (max err %.3e)"
            % (bad, s["per_hit_max_error"]))

    # I2: actor identity
    s["actor_identity_error"] = an.identity_error()
    rel = abs(s["actor_identity_error"]) / max(1.0, an.analyzable)
    if rel > 1e-9:
        add("ERROR", "I2", "sum(totalCredit)+unattributed != analyzableDealt (err %.6f, rel %.3e)"
            % (s["actor_identity_error"], rel))

    # I3: share identity
    credited_share = an.actor_total_credit() / an.analyzable if an.analyzable else 0.0
    unattr_share = an.unattributed_credit / an.analyzable if an.analyzable else 0.0
    s["credited_share"] = credited_share
    s["unattributed_share"] = unattr_share
    if abs(credited_share + unattr_share - 1.0) > 1e-9:
        add("ERROR", "I3", "creditedShare+unattributedShare = %.12f" % (credited_share + unattr_share))

    # I4: fold accounting
    rc = an.diagnostics.get("reasonCounts", {})
    resolved = sum(v for k, v in rc.items() if k in RESOLVED_REASONS)
    unresolved = sum(v for k, v in rc.items() if k in UNRESOLVED_REASONS)
    unknown_reason = sum(v for k, v in rc.items()
                         if k not in RESOLVED_REASONS and k not in UNRESOLVED_REASONS)
    zero = an.diagnostics.get("zero_factor", 0)
    noop = an.diagnostics.get("noop_factor", 0)
    total = an.diagnostics.get("fold_total", 0)
    sub_pre = an.diagnostics.get("sub_unity_factor", 0)
    s["fold_accounting"] = {"total": total, "resolved": resolved, "unresolved": unresolved,
                            "zero_factor": zero, "noop_factor": noop, "sub_unity": sub_pre,
                            "unknown_reason": unknown_reason}
    if resolved + unresolved != total:
        add("ERROR", "I4a", "resolved(%d)+unresolved(%d) != folds(%d)" % (resolved, unresolved, total))
    s["fold_pool_total"] = total + zero + noop + sub_pre
    if unknown_reason:
        add("ERROR", "I4b", "%d folds carry an unknown reason code" % unknown_reason)
    s["zero_factor_folds"] = zero
    s["noop_factor_folds"] = noop
    if zero:
        add("WARN", "I4c", "%d folds had factor <= 0 and were excluded from M" % zero)
    sub = an.diagnostics.get("sub_unity_factor", 0)
    s["sub_unity_folds"] = sub
    if sub:
        add("WARN", "I4d", "%d folds had factor < 1 (damage reduction) and were excluded from M: %s"
            % (sub, an.diagnostics.get("sub_unity_sample")))
    cm = an.diagnostics.get("calc_missing", 0)
    s["calc_missing_hits"] = cm
    if cm:
        add("WARN", "I4f", "%d analyzable hits have no calc block (treated as M=1)" % cm)
    fdr = an.diagnostics.get("fold_dropped", 0)
    s["fold_dropped"] = fdr
    if fdr:
        add("ERROR", "I4g", "%d folds were dropped by the FoldStep cap: M is short a factor" % fdr)
    if an.diagnostics.get("negative_lines"):
        add("ERROR", "I4e", "%d credit lines are negative (log-share model violated)"
            % an.diagnostics["negative_lines"])

    # I5: no credit to actors outside the analysed team
    stray = [k for k in an.actors if k not in set(a.key for a in export.team_actors(an.team))]
    for r in an.rules.values():
        if r.owner_key is not None and r.owner_key not in an.actors:
            add("ERROR", "I5", "rule %s owned by non-team key %s" % (r.name, r.owner_key))
            break

    # I6: coverage
    cens = an.diagnostics.get("kindSideCensus", {})
    for kind in ("given", "madness", "atkadd"):
        tot = sum(n for (k, sd, r), n in cens.items() if k == kind)
        with_bu = sum(n for (k, sd, r), n in cens.items() if k == kind and r in ("byUnit",))
        s["byUnit_%s" % kind] = [with_bu, tot]
        if tot and with_bu != tot:
            add("WARN", "I6", "%s folds without byUnit: %d/%d" % (kind, tot - with_bu, tot))
    s["analyzable"] = an.analyzable
    s["unattributed_credit"] = an.unattributed_credit
    s["pool_total"] = an.pool_total
    s["pool_share"] = (an.pool_total / an.analyzable) if an.analyzable else 0.0
    t = export.totals or {}
    s["totals_dealt"] = t.get("dealt")
    if t.get("dealt"):
        s["analyzable_vs_totals"] = an.analyzable - float(t["dealt"])
        if abs(s["analyzable_vs_totals"]) > 0:
            add("WARN", "I7", "analyzableDealt differs from totals.dealt by %.0f (%.4f%%)"
                % (s["analyzable_vs_totals"], 100.0 * s["analyzable_vs_totals"] / float(t["dealt"])))
    ue = an.diagnostics.get("unattributed_events") or {"count": 0}
    s["key_coverage"] = an.hits / float(an.hits + ue.get("count", 0)) if (an.hits + ue.get("count", 0)) else 1.0
    s["owner_paths"] = {
        "byUnit_name_to_key": rc.get("byUnit", 0),
        "abilityId_to_holder": rc.get("ability_holder_unique", 0) + rc.get("ability_holder_attacker", 0),
        "abilityName_to_holder": rc.get("global_name_unique", 0),
        "attacker_default_fallback": rc.get("attacker_default", 0),
    }
    s["unattributed_events"] = an.diagnostics.get("unattributed_events")
    s["outside_team_events"] = an.diagnostics.get("outside_team_events")
    team_dups = [a.name for a in export.team_actors(an.team)
                 if len([b for b in export.team_actors(an.team) if b.name == a.name]) > 1]
    s["duplicate_names_team"] = sorted(set(team_dups))
    s["duplicate_names_export"] = len(export.duplicate_names)
    if team_dups:
        add("WARN", "I8", "duplicate actor names inside the analysed team: %s" % sorted(set(team_dups)))
    elif export.duplicate_names:
        add("INFO", "I8", "%d actor names repeat across the export (outside the analysed team; harmless)"
            % len(export.duplicate_names))
    if int(export.quest or 0) == 9999:
        add("INFO", "I10", "training mode (quest 9999): special x0.03 rules; do not compare across modes")
    if not an.unattributed_credit:
        add("INFO", "I9", "unattributed credit pool is empty")
    else:
        add("WARN", "I9", "unattributed credit %.0f (%.4f%% of analyzable): %s"
            % (an.unattributed_credit, 100.0 * unattr_share, sorted(an.unattributed.keys())))
    return issues, s


# ---------------------------------------------------------------------------
# P0-A: this module used to have NO entry point at all, so an ERROR list it produced could only
# ever be consumed by a caller that remembered to look at it (crosscheck did not). The CLI below is
# the same contract every other gate follows (contribution_gate.py):
#     PASS/WARNING -> 0, LEGACY_NOT_APPLICABLE -> 0 (quest 9999; reason printed), DATA_MISSING -> 3,
#     ERROR -> 1
# ---------------------------------------------------------------------------

def main(argv=None):
    import argparse
    import glob
    import os
    import sys

    work = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    if work not in sys.path:
        sys.path.insert(0, work)
    import contribution_gate as gate
    from . import aggregate, loader

    exports = os.path.join(os.path.dirname(work), "BepInEx", "plugins", "DpsMeter", "exports")
    ap = argparse.ArgumentParser(description="contribution identity/accounting/coverage gate")
    ap.add_argument("export", nargs="?")
    ap.add_argument("--team", type=int, default=1)
    ap.add_argument("--allow-training", action="store_true",
                    help="check a training-ground export anyway (verdict stays not-applicable)")
    ap.add_argument("--json", help="also write the issues+summary to this JSON file")
    a = ap.parse_args(argv)
    path = a.export
    if not path:
        cands = [p for p in glob.glob(os.path.join(exports, "battle_*.json"))
                 if not os.path.basename(p).startswith("battle_9999")]
        if not cands:
            print("DATA_MISSING: no export found under %s" % exports)
            return gate.EXIT_DATA_MISSING
        path = sorted(cands, key=os.path.getmtime)[-1]
    try:
        export = loader.load(path)
    except Exception as exc:
        print("ERROR: could not load %s: %s: %s"
              % (os.path.basename(path), type(exc).__name__,
                 str(exc).encode("ascii", "replace").decode("ascii")))
        return gate.EXIT_ERROR
    try:
        an = aggregate.analyze(export, a.team)
        issues, summary = check(an, export)
    except Exception as exc:
        print("ERROR: validate.check crashed on %s: %s: %s"
              % (os.path.basename(path), type(exc).__name__,
                 str(exc).encode("ascii", "replace").decode("ascii")))
        return gate.EXIT_ERROR
    errs = [i for i in issues if i[0] == "ERROR"]
    warns = [i for i in issues if i[0] == "WARN"]
    applicability, app_reason = gate.training_applicability(export.quest)
    print("file=%s version=%s quest=%s analyzable=%.0f hits=%d" %
          (export.name, export.version, export.quest, an.analyzable, an.hits))
    print("issues: ERROR=%d WARN=%d INFO=%d  modelApplicability=%s" %
          (len(errs), len(warns), len([i for i in issues if i[0] == "INFO"]), applicability))
    for lv, code, msg in issues:
        print("  [%s %s] %s" % (lv, code, msg.encode("ascii", "replace").decode("ascii")))
    if a.json:
        import io
        import json as _json
        with io.open(a.json, "w", encoding="utf-8") as fh:
            fh.write(_json.dumps({"file": export.name, "version": export.version,
                                  "quest": export.quest, "modelApplicability": applicability,
                                  "issues": [{"level": lv, "code": c, "message": m}
                                             for lv, c, m in issues],
                                  "summary": summary}, ensure_ascii=False, indent=1))
        print("report=%s" % a.json)
    if applicability == "not_comparable" and not a.allow_training:
        print("%s: %s" % (gate.LEGACY_NOT_APPLICABLE,
                          app_reason["message"].encode("ascii", "replace").decode("ascii")))
        print("hint: pass --allow-training to run the normal identities anyway (the explicit "
              "not-applicable verdict above is printed either way)")
        return gate.EXIT_LEGACY_NOT_APPLICABLE
    if errs:
        print("%s: %d ERROR issue(s)" % (gate.ERROR, len(errs)))
        return gate.EXIT_ERROR
    if applicability == "not_comparable":
        print("%s: training ground (quest %s): ordinary identities hold, but this is NOT the "
              "normal model's PASS" % (gate.LEGACY_NOT_APPLICABLE, export.quest))
        return gate.EXIT_LEGACY_NOT_APPLICABLE
    status = gate.WARNING if warns else gate.PASS
    print("%s: every applicable identity holds" % status)
    return gate.exit_code(status)


if __name__ == "__main__":
    import sys
    sys.exit(main())
