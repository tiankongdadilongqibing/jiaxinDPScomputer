# -*- coding: utf-8 -*-
"""Golden test on the real 1.5.5 export: pins every number the project publishes.

WHY. The adversarial audit (RSESSION-STATE 7.2.84) showed that most of the attribution ladder's
branches have ZERO real cases in this battle (byUnit_ambiguous/_outside/_unknown,
ambiguous_multi_holder, attacker_default, zero/noop/sub-unity folds, negative shares). Invariants
that are only asserted on synthetic fixtures are assumptions; this file pins the OBSERVED
distribution on real data, so if a future edit moves any of them the test fails and the number has
to be re-published deliberately.

P0-A (2026-10-04): a missing export is NOT a silent SKIP + exit 0 any more. The pinned file is the
PROOF this test exists for; without it the test proves nothing, so its absence is DATA_MISSING
(exit 3), which is deliberately different from a real failure (exit 1). Pass --export to point the
same assertions at another 1.5.5-format file.

Exit contract (contribution_gate.py): 0 = PASS/WARNING/LEGACY_NOT_APPLICABLE, 1 = ERROR,
3 = DATA_MISSING.
"""
from __future__ import annotations
import argparse, io, json, os, sys

if __package__ in (None, ""):
    sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))

from contrib import aggregate, loader
from contrib import validate as vmod

HERE = os.path.dirname(os.path.abspath(__file__))
WORK = os.path.dirname(os.path.dirname(HERE))
ROOT = os.path.dirname(WORK)
if WORK not in sys.path:
    sys.path.insert(0, WORK)
import contribution_gate as gate          # noqa: E402

DEFAULT_EXPORT = os.path.join(ROOT, "BepInEx", "plugins", "DpsMeter", "exports",
                              "battle_411001_20261003_235204.json")
EXPORT = DEFAULT_EXPORT

FAILS = []


def eq(label, got, want, tol=1e-6):
    if isinstance(want, list) or isinstance(got, list):
        ok = list(got) == list(want)
    else:
        ok = abs(got - want) <= tol
    if not ok:
        FAILS.append("%s: got=%r want=%r" % (label, got, want))
    print("%-4s %-46s %r" % ("PASS" if ok else "FAIL", label, got))


def main(argv=None):
    ap = argparse.ArgumentParser(description="golden numbers on the pinned 1.5.5 export")
    ap.add_argument("--export", default=DEFAULT_EXPORT,
                    help="pinned 1.5.5-format export (default: %s)" % os.path.basename(DEFAULT_EXPORT))
    a = ap.parse_args(argv)
    export_path = a.export
    if not os.path.exists(export_path):
        # P0-A: was "SKIP golden: ... not present" + return 0. A gate that proves nothing must not
        # look like a gate that proved everything.
        print("%s: golden export absent: %s" % (gate.DATA_MISSING, export_path))
        print("  the pinned 1.5.5 numbers cannot be checked without it; this is a MISSING INPUT, "
              "not a pass (exit %d). Restore the export or pass --export <other 1.5.5 export>."
              % gate.EXIT_DATA_MISSING)
        return gate.EXIT_DATA_MISSING
    export = loader.load(export_path)
    applicability, app_reason = gate.training_applicability(export.quest)
    if applicability == "not_comparable":
        print("%s: %s" % (gate.LEGACY_NOT_APPLICABLE,
                          app_reason["message"].encode("ascii", "replace").decode("ascii")))
        return gate.EXIT_LEGACY_NOT_APPLICABLE
    an = aggregate.analyze(export, 1)
    issues, summary = vmod.check(an, export)
    errs = [i for i in issues if i[0] == "ERROR"]

    print("=== golden 1.5.5 (%s) ===" % os.path.basename(export_path))
    eq("no ERROR issues", len(errs), 0)
    eq("analyzableDealt", an.analyzable, 203865411, 0.5)
    eq("hits", an.hits, 5521)
    eq("attributed (= analyzable)", an.actor_total_credit(), 203865411, 1.0)
    eq("unattributed pool", an.unattributed_credit, 0.0, 1e-6)
    eq("multiplier pool", an.pool_total, 156959220.34590647, 1.0)
    eq("base column", sum(a.base for a in an.actors.values()), 46906190.65409332, 1.0)
    eq("self column", sum(a.self_rule for a in an.actors.values()), 91670166.56452306, 1.0)
    eq("assist column", sum(a.assist for a in an.actors.values()), 65289053.7813844, 1.0)

    # fold accounting, exactly as published
    fa = summary["fold_accounting"]
    eq("folds total", fa["total"], 28369)
    eq("folds resolved", fa["resolved"], 28369)
    eq("folds unresolved", fa["unresolved"], 0)
    eq("zero-factor folds (real data)", fa["zero_factor"], 0)
    eq("noop folds (real data)", fa["noop_factor"], 0)
    eq("sub-unity folds (real data)", fa["sub_unity"], 0)
    eq("negative credit lines", an.diagnostics.get("negative_lines", 0), 0)
    eq("calc-missing analyzable hits", an.diagnostics.get("calc_missing", 0), 1)
    eq("foldDropped folds", an.diagnostics.get("fold_dropped", 0), 0)

    # channel census and ladder distribution: the audit's Q1 is exactly this distribution
    eq("channel text/atk", an.diagnostics["channelCensus"].get("text/atk"), 12610)
    eq("channel talent/atk", an.diagnostics["channelCensus"].get("talent/atk"), 91)
    eq("channel global/vic", an.diagnostics["channelCensus"].get("global/vic"), 8661)
    eq("channel given/vic", an.diagnostics["channelCensus"].get("given/vic"), 6110)
    eq("channel madness/atk", an.diagnostics["channelCensus"].get("madness/atk"), 897)
    rc = an.diagnostics["reasonCounts"]
    eq("reason byUnit", rc.get("byUnit"), 7007)
    eq("reason ability_holder_unique", rc.get("ability_holder_unique"), 6199)
    eq("reason ability_holder_attacker", rc.get("ability_holder_attacker"), 6502)
    eq("reason global_name_unique", rc.get("global_name_unique"), 8661)
    # Q1: no real case where a unique holder was NOT the attacker -- the invariant is recorded, not assumed
    eq("reason byUnit_ambiguous (real data)", rc.get("byUnit_ambiguous", 0), 0)
    eq("reason byUnit_outside (real data)", rc.get("byUnit_outside", 0), 0)
    eq("reason byUnit_unknown (real data)", rc.get("byUnit_unknown", 0), 0)
    eq("reason ambiguous_multi_holder (real)", rc.get("ambiguous_multi_holder", 0), 0)
    eq("reason attacker_default (real)", rc.get("attacker_default", 0), 0)
    eq("reason unknown_kind (real)", rc.get("unknown_kind", 0), 0)

    # coverage
    eq("byUnit given coverage", summary["byUnit_given"], [6110, 6110])
    eq("byUnit madness coverage", summary["byUnit_madness"], [897, 897])
    eq("attack key coverage", summary["key_coverage"], 0.9880100214745884, 1e-12)
    eq("unattributed events (attacker ?)", an.diagnostics["unattributed_events"]["count"], 67)
    eq("outside-team events", an.diagnostics["outside_team_events"]["count"], 14)

    # structure published in dictionary section 10
    eq("rule rows", len(an.rules), 21)
    eq("link rows", len(an.links), 20)
    top = sorted(an.actors.values(), key=lambda a: -a.total)[:4]
    eq("top1 key", top[0].key, 4)
    eq("top1 totalCredit", top[0].total, 62706980.5, 2.0)
    eq("top1 baseCredit", top[0].base, 13513395.0, 2.0)
    eq("top1 selfRuleCredit", top[0].self_rule, 9155038.0, 2.0)
    eq("top1 assistCredit", top[0].assist, 40038548.0, 2.0)

    if FAILS:
        print("GOLDEN FAIL %d" % len(FAILS))
        for f in FAILS:
            print("  " + f)
        return gate.EXIT_ERROR
    print("GOLDEN OK: every published 1.5.5 number still holds")
    return gate.exit_code(gate.WARNING if [i for i in issues if i[0] == "WARN"] else gate.PASS)


if __name__ == "__main__":
    sys.exit(main())
