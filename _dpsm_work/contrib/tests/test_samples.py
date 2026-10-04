# -*- coding: utf-8 -*-
"""Hand-sample tests from CONTRIBUTION-DATA-DICTIONARY.md section 7 (S1-S6).

Runs without any battle export: python -m contrib.tests.test_samples
"""
from __future__ import annotations
import math, os, sys

if __package__ in (None, ""):
    sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))

from contrib import aggregate
from contrib.loader import ExportData

FAILS = []


def close(a, b, eps=1e-6, label=""):
    if abs(a - b) > eps:
        FAILS.append("%s: %.9f != %.9f" % (label, a, b))


def actor(key, name, team=1, abilities=None, summon=False):
    row = {"key": key, "name": name, "team": team, "kind": "P", "summon": summon, "dealt": 0}
    if abilities:
        row["abilities"] = abilities
        row["talents"] = [{"abilityId": ab["id"], "ability": ab.get("name")} for ab in abilities]
    return row


def ability(aid, name):
    return {"slot": 1, "id": aid, "name": name, "talents": []}


def fold(kind, side, origin, factor, label="", by_unit=""):
    f = {"kind": kind, "side": side, "origin": origin, "factor": factor, "label": label}
    if by_unit:
        f["byUnit"] = by_unit
    return f


def dmg(amount, folds, atk_key=1, atk_team=1, victim=1, crit=False, t=0.0, residual=None):
    e = {"t": t, "type": "dmg", "attacker": "A", "victim": "V", "amount": amount, "atkTeam": atk_team,
         "vicTeam": 2, "atkKey": atk_key, "vicKey": victim, "crit": crit}
    if folds is not None:
        e["calc"] = {"fold": folds}
        if residual is not None:
            e["calc"]["residual"] = residual
    return e


def export(actors, events, quest=411001, totals=None):
    return ExportData({"version": "test", "quest": quest, "duration": 1.0, "result": "Test",
                       "totals": totals or {}, "actors": actors, "events": events}, path="synthetic.json")


def run_case(actors, events, **kw):
    ex = export(actors, events, **kw)
    return ex, aggregate.analyze(ex, 1)


A = actor(1, "Alpha", abilities=[ability(900, "AlphaRule")])
B = actor(2, "Beta")
OUT = actor(99, "Outside", team=2)

# S1: single actor, no multipliers
_, an = run_case([A], [dmg(1000, [])])
close(an.analyzable, 1000, label="S1 analyzable")
close(an.actors[1].base, 1000, label="S1 base")
close(an.actors[1].total, 1000, label="S1 total")
close(an.unattributed_credit, 0, label="S1 unattr")

# S2: attacker + one assist multiplier (given x1.5 by Beta)
_, an = run_case([A, B], [dmg(3000, [fold("given", "vic", "given#1/1006/-10", 1.5, "被伤害+50%(赋予)", "Beta")])])
close(an.actors[1].base, 2000, label="S2 base")
close(an.actors[2].assist, 1000, label="S2 assist")
close(an.actors[1].total, 2000, label="S2 attacker total")
close(an.actors[1].received, 1000, label="S2 received")
close(an.identity_error(), 0, label="S2 identity")

# S3: own rule x1.1 + assist x2.0 + unattributed global x1.25; D = 1000 * 2.75
ev = [dmg(2750, [fold("text", "atk", "text#1/900/c0", 1.1, "[AlphaRule] 自身"),
                 fold("given", "vic", "given#2/1006/-10", 2.0, "被伤害+100%(赋予)", "Beta"),
                 fold("global", "vic", "global#123/0", 1.25, "[未知全局规则] x")])]
_, an = run_case([A, B], ev)
close(an.actors[1].base, 1000, eps=1e-6, label="S3 base")
close(an.actors[1].self_rule, 164.88, eps=0.01, label="S3 self")
close(an.actors[2].assist, 1199.10, eps=0.01, label="S3 assist")
close(an.unattributed_credit, 386.02, eps=0.01, label="S3 unattr")
close(an.unattributed["global_ambiguous"][0] > 0, True, label="S3 reason")
close(an.identity_error(), 0, eps=1e-6, label="S3 identity")
close(an.actors[1].total + an.actors[2].total + an.unattributed_credit, 2750, eps=0.01, label="S3 sum")

# S4: byUnit outside the team
_, an = run_case([A, B, OUT], [dmg(1500, [fold("madness", "atk", "madness#250", 1.5, "狂気", "Outside")])])
close(an.actors[1].base, 1000, label="S4 base")
close(an.actors[1].total, 1000, label="S4 attacker total")
close(an.unattributed_credit, 500, label="S4 unattr")
close(an.unattributed.get("byUnit_outside", [0])[0], 500, label="S4 reason amount")

# S5: attacker is also the rule owner -> self rule, no assist, no double count
_, an = run_case([A], [dmg(2000, [fold("text", "atk", "text#1/900/c0", 2.0, "[AlphaRule] 自身")])])
close(an.actors[1].base, 1000, label="S5 base")
close(an.actors[1].self_rule, 1000, label="S5 self")
close(an.actors[1].assist, 0, label="S5 assist")
close(an.actors[1].received, 0, label="S5 received")
close(an.actors[1].total, 2000, label="S5 total")
close(len(an.links), 0, label="S5 no link")

# S6: non-positive factor must not raise (ln(0)) and must be accounted as diagnostics
_, an = run_case([A], [dmg(0, [fold("text", "vic", "text#5/74/c1", 0.0, "100% reduction")])])
close(an.diagnostics["zero_factor"], 1, label="S6 zero_factor")
close(an.analyzable, 0, label="S6 analyzable")
close(an.unattributed_credit, 0, label="S6 unattr")

# S7: fold accounting and coverage on a mixed synthetic battle
_, an = run_case([A, B, OUT], [
    dmg(3000, [fold("given", "vic", "given#1/1006/-10", 1.5, "赋予", "Beta")]),
    dmg(1000, []),
    dmg(500, [fold("text", "vic", "text#5/74/c1", 0.0, "reduce")]),
    dmg(700, [fold("madness", "atk", "madness#250", 1.5, "狂気", "Outside")]),
    dmg(400, [], atk_team=2, atk_key=99),
    dmg(250, [], atk_key=None, atk_team=None),
])
acc = an.diagnostics["fold_total"] + an.diagnostics["zero_factor"] + an.diagnostics["noop_factor"]
close(acc, 3, label="S7 fold pool")
close(an.analyzable, 5200, label="S7 analyzable")
close(an.diagnostics["outside_team_events"]["damage"], 400, label="S7 outside")
close(an.diagnostics["unattributed_events"]["damage"], 250, label="S7 unattributed events")

# ---------- Phase C: actor-key primacy and ambiguity (same-name / summon / multi-holder) ----------

# S8: an enemy shares an ally's name; byUnit must resolve inside OUR team, never the enemy key
TWIN_ALLY = actor(1, "Twin", abilities=[ability(910, "TwinRule")])
SOLO = actor(2, "Solo")
TWIN_ENEMY = actor(50, "Twin", team=2)
_, an = run_case([TWIN_ALLY, SOLO, TWIN_ENEMY],
                 [dmg(2000, [fold("given", "vic", "given#1/1/-10", 2.0, "赋予", "Twin")], atk_key=2)])
close(an.actors[2].base, 1000, label="S8 base")
close(an.actors[1].assist, 1000, label="S8 ally gets assist")
close(50 in an.actors, False, label="S8 enemy key absent")
close(an.unattributed_credit, 0, label="S8 no unattributed")

# S9: two same-named summons in our team -> byUnit_ambiguous, never merged
S1 = actor(10, "Turret", summon=True)
S2 = actor(11, "Turret", summon=True)
_, an = run_case([A, S1, S2], [dmg(1500, [fold("given", "vic", "given#1/1/-10", 1.5, "赋予", "Turret")])])
close(an.actors[10].assist + an.actors[11].assist, 0, label="S9 summons not credited")
close(an.unattributed.get("byUnit_ambiguous", [0])[0], 500, label="S9 reason amount")
close(an.unattributed_credit, 500, label="S9 unattributed")

# S10a: same ability id on two holders, attacker holds it -> attacker (stable key)
H1 = actor(1, "H1", abilities=[ability(900, "Shared")])
H2 = actor(2, "H2", abilities=[ability(900, "Shared")])
N = actor(3, "N")
_, an = run_case([H1, H2, N], [dmg(2000, [fold("text", "atk", "text#1/900/c0", 2.0, "Shared")])])
close(an.actors[1].self_rule, 1000, label="S10a holder-attacker self")
close(an.unattributed_credit, 0, label="S10a no unattributed")

# S10b: same ability id on two holders, attacker does NOT hold it -> ambiguous, not silently charged
_, an = run_case([H1, H2, N], [dmg(2000, [fold("text", "atk", "text#1/900/c0", 2.0, "Shared")], atk_key=3)])
close(an.actors[3].self_rule, 0, label="S10b attacker gets nothing")
close(an.unattributed.get("ambiguous_multi_holder", [0])[0], 1000, label="S10b ambiguity amount")

# S11: unknown fold kind -> unattributed with a reason code
_, an = run_case([A], [dmg(1000, [fold("mystery", "atk", "mystery#1", 1.5, "?")])])
close(an.actors[1].base, 666.667, eps=0.01, label="S11 base")
close(an.unattributed.get("unknown_kind", [0])[0], 333.333, eps=0.01, label="S11 unknown_kind")

# S12: atkTeam contradicts the actor-row team -> the actor key wins, event is out of team
_, an = run_case([A, TWIN_ENEMY], [dmg(1000, [], atk_key=50, atk_team=1)])
close(an.analyzable, 0, label="S12 analyzable")
close(an.diagnostics["atkTeam_mismatch"], 1, label="S12 mismatch counted")
close(an.diagnostics["outside_team_events"]["damage"], 1000, label="S12 outside")

# S13: a factor < 1 (damage reduction / training x0.03) must not create negative credit
_, an = run_case([A], [dmg(1000, [fold("text", "vic", "text#9/1/c0", 0.5, "被伤害-50%")])])
close(an.actors[1].base, 1000, label="S13 base keeps whole hit")
close(an.actors[1].self_rule, 0, label="S13 no self rule")
close(an.unattributed_credit, 0, label="S13 no unattributed")
close(an.diagnostics["sub_unity_factor"], 1, label="S13 sub-unity counted")
close(an.diagnostics.get("negative_lines", 0), 0, label="S13 no negative lines")

# analytic cross-check of the log-share formula itself
M = 1.1 * 2.0 * 1.25
pool = 2750 - 2750 / M
close(pool * math.log(1.1) / math.log(M), 164.881, eps=0.01, label="formula self")
close(pool * math.log(2.0) / math.log(M), 1199.096, eps=0.01, label="formula assist")

if FAILS:
    print("FAIL %d" % len(FAILS))
    for f in FAILS:
        print("  " + f)
    sys.exit(1)
print("OK contrib hand samples S1-S13 (S1-S6 math, S7 accounting, S8-S12 actor-key/ambiguity, S13 sub-unity): all passed")
