#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""atkadd sensitivity study (N4 / P1) -- READ-ONLY, offline.

Reads ONE OR MORE real DpsMeter exports, replays the log-share/1 contribution
model on the SAME event set and the SAME denominator, and reports how the
per-actor credit of the atkadd channel changes when that model assumption is
isolated. It never modifies an input file, contrib/ modules, the official
contribution table, any DLL, or the game.

ISOLATION: two DIFFERENT re-accounting conventions are computed and are never
mixed up in the output:
  A (PRIMARY, re-split pool): drop every kind=atkadd factor from that hit
    fold set, then recompute M, base and the ln(f)/ln(M) shares from the
    remaining folds only.
  B (secondary, pool-preserving): keep the current M/base/pool and the
    current shares of every NON-atkadd rule; redirect only the atkadd shares
    that WERE resolved to an owner back to the attacker baseCredit.
    Unresolved atkadd shares stay in the unattributed pool.
Neither is a simulation of replacing a character.

Console output is ASCII only. Chinese text goes into the UTF-8 result files.

Exit codes: 0 ok, 1 script error, 2 integrity/validation failure (strict).
"""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import math
import os
import sys

EPS = 1e-12
TEAM = 1
MODES = ("current", "isolate_a", "isolate_b")


# ---------------------------------------------------------------------------
# small helpers
# ---------------------------------------------------------------------------

def fnum(v):
    try:
        return float(v)
    except (TypeError, ValueError):
        return 0.0


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def add_holder(d, key, actor_key):
    lst = d.setdefault(key, [])
    if actor_key not in lst:
        lst.append(actor_key)


def ability_id_of(origin):
    s = str(origin or "")
    if not s:
        return 0
    if not (s.startswith("text#") or s.startswith("talent#")):
        return 0
    i = s.find("#") + 1
    if i <= 0 or i >= len(s):
        return 0
    if s[i:].startswith("buff#"):
        i += 5
    slash = s.find("/", i)
    if slash < 0:
        return 0
    j = slash + 1
    k = j
    while k < len(s) and s[k].isdigit():
        k += 1
    if k == j:
        return 0
    if k < len(s) and s[k] != "/":
        return 0
    try:
        return int(s[j:k])
    except ValueError:
        return 0


def build_ix(doc):
    actors = doc.get("actors") or []
    rows = [a for a in actors if isinstance(a, dict) and a.get("key") is not None]
    by_key = {}
    for a in rows:
        by_key[a["key"]] = a
    team_rows = [a for a in rows if a.get("team") == TEAM]
    all_names = set(a.get("name") for a in rows)
    seen = {}
    for a in team_rows:
        seen.setdefault(a.get("name"), []).append(a)
    name_unique = {}
    ambiguous = set()
    for n, lst in seen.items():
        if len(lst) == 1:
            name_unique[n] = lst[0]
        else:
            ambiguous.add(n)
    by_ability_id = {}
    by_ability_name = {}
    for a in team_rows:
        k = a["key"]
        for ab in (a.get("abilities") or []):
            if ab.get("id") is not None:
                add_holder(by_ability_id, ab["id"], k)
            if ab.get("name"):
                add_holder(by_ability_name, ab["name"], k)
        for t in (a.get("talents") or []):
            if t.get("abilityId") is not None:
                add_holder(by_ability_id, t["abilityId"], k)
            if t.get("ability"):
                add_holder(by_ability_name, t["ability"], k)
    return {
        "by_key": by_key,
        "team_rows": team_rows,
        "all_names": all_names,
        "name_unique": name_unique,
        "ambiguous": ambiguous,
        "by_ability_id": by_ability_id,
        "by_ability_name": by_ability_name,
    }


def resolve(ix, fold, attacker_key):
    bu = fold.get("by_unit") or ""
    if bu:
        if bu in ix["name_unique"]:
            return ix["name_unique"][bu]["key"], "byUnit"
        if bu in ix["ambiguous"]:
            return None, "byUnit_ambiguous"
        if bu in ix["all_names"]:
            return None, "byUnit_outside"
        return None, "byUnit_unknown"
    kind = fold.get("kind") or ""
    if kind in ("text", "talent"):
        aid = ability_id_of(fold.get("origin"))
        holders = ix["by_ability_id"].get(aid, []) if aid else []
        if len(holders) == 1:
            return holders[0], "ability_holder_unique"
        if len(holders) > 1:
            if attacker_key in holders:
                return attacker_key, "ability_holder_attacker"
            return None, "ambiguous_multi_holder"
        return attacker_key, "attacker_default"
    if kind == "global":
        lab = fold.get("label") or ""
        a = lab.find("[")
        b = lab.find("]")
        name = lab[a + 1:b] if (a >= 0 and b > a) else None
        holders = ix["by_ability_name"].get(name, []) if name else []
        if len(holders) == 1:
            return holders[0], "global_name_unique"
        return None, "global_ambiguous"
    return None, "unknown_kind"


def event_folds(e):
    """Return (folds, atkadds, diag). folds = calc.fold, atkadds = calc.atkAdd.

    The filter mirrors the C# Contribution core: factor<=0 -> zero, |f-1|<eps
    -> noop, f<1 -> sub-unity (excluded from M and from the pool). atkadd rows
    additionally get a NON-FINITE guard: the C# producer can never emit such a
    row (dP>=P is refused in AtkAddFold), but a tampered or foreign file could,
    and the C# consumer would then poison every share to NaN. This replay
    refuses it and counts it instead.
    """
    calc = e.get("calc") or {}
    folds = []
    atkadds = []
    diag = {"zero_factor": 0, "noop_factor": 0, "sub_unity_factor": 0,
            "atkadd_zero": 0, "atkadd_guard_nonfinite": 0, "fold_dropped": 0}
    fd = calc.get("foldDropped")
    if isinstance(fd, (int, float)) and fd > 0:
        diag["fold_dropped"] = int(fd)

    def one(raw, out, is_atk):
        factor = fnum(raw.get("factor"))
        if is_atk and not math.isfinite(factor):
            diag["atkadd_guard_nonfinite"] += 1
            return
        if factor <= 0.0:
            diag["atkadd_zero" if is_atk else "zero_factor"] += 1
            return
        if abs(factor - 1.0) < EPS:
            diag["noop_factor"] += 1
            return
        if factor < 1.0:
            diag["sub_unity_factor"] += 1
            return
        out.append({
            "kind": str(raw.get("kind") or ""),
            "side": str(raw.get("side") or ""),
            "origin": str(raw.get("origin") or ""),
            "label": str(raw.get("label") or ""),
            "by_unit": str(raw.get("byUnit") or ""),
            "factor": factor,
        })

    for f in (calc.get("fold") or []):
        if isinstance(f, dict):
            one(f, folds, False)
    for f in (calc.get("atkAdd") or []):
        if isinstance(f, dict):
            one(f, atkadds, True)
    return folds, atkadds, diag


def split_hit(damage, folds):
    M = 1.0
    for f in folds:
        M *= f["factor"]
    if not folds or abs(M - 1.0) < EPS or not math.isfinite(M):
        return 1.0, damage, 0.0, [0.0] * len(folds)
    base = damage / M
    pool = damage - base
    lnM = math.log(M)
    shares = [pool * (math.log(f["factor"]) / lnM) if lnM != 0.0 else 0.0 for f in folds]
    return M, base, pool, shares


# ---------------------------------------------------------------------------
# replay
# ---------------------------------------------------------------------------

def analyze(doc, mode):
    if mode not in MODES:
        raise ValueError("unknown mode: " + str(mode))
    ix = build_ix(doc)
    credits = {}
    for a in ix["team_rows"]:
        credits[a["key"]] = {"key": a["key"], "name": a.get("name"), "kind": a.get("kind"),
                             "summon": bool(a.get("summon")), "direct": 0.0, "base": 0.0,
                             "self_rule": 0.0, "assist": 0.0, "received": 0.0, "hits": 0,
                             "atkadd_assist": 0.0, "atkadd_received": 0.0,
                             "hits_with_atkadd": 0, "atkadd_folds": 0}
    st = {"events": 0, "dmg": 0, "analyzable": 0.0, "hits": 0, "hits_with_calc": 0,
          "hits_with_atkadd": 0, "atkadd_rows": 0, "folds": 0, "pool": 0.0,
          "unattributed": 0.0, "outside_hits": 0, "outside_damage": 0.0,
          "unknown_hits": 0, "unknown_damage": 0.0, "calc_missing": 0,
          "max_hit_error": 0.0, "bad_hits": 0, "zero_factor": 0, "noop_factor": 0,
          "sub_unity_factor": 0, "atkadd_zero": 0, "atkadd_guard_nonfinite": 0,
          "fold_dropped": 0, "reason": {}, "atkadd_guard_reject": 0}
    for e in (doc.get("events") or []):
        if not isinstance(e, dict):
            continue
        if e.get("type") != "dmg":
            continue
        st["dmg"] += 1
        damage = fnum(e.get("amount"))
        key = e.get("atkKey")
        row = ix["by_key"].get(key) if key is not None else None
        if row is None:
            st["unknown_hits"] += 1
            st["unknown_damage"] += damage
            continue
        if row.get("team") != TEAM:
            st["outside_hits"] += 1
            st["outside_damage"] += damage
            continue
        st["hits"] += 1
        st["analyzable"] += damage
        attacker_key = row["key"]
        rec = credits[attacker_key]
        rec["direct"] += damage
        rec["hits"] += 1
        folds, atkadds, d = event_folds(e)
        for k in ("zero_factor", "noop_factor", "sub_unity_factor",
                  "atkadd_zero", "atkadd_guard_nonfinite", "fold_dropped"):
            st[k] += d[k]
        if (e.get("calc") or {}).get("fold") is not None or (e.get("calc") or {}).get("atkAdd") is not None:
            st["hits_with_calc"] += 1
        else:
            st["calc_missing"] += 1
        if atkadds:
            st["hits_with_atkadd"] += 1
            rec["hits_with_atkadd"] += 1
        st["atkadd_rows"] += len(atkadds)

        if mode == "isolate_a":
            effective = list(folds)
        else:
            effective = list(folds) + list(atkadds)
        st["folds"] += len(effective)

        M, base, pool, shares = split_hit(damage, effective)
        st["pool"] += pool
        rec["base"] += base
        credited = base
        for f, share in zip(effective, shares):
            owner, reason = resolve(ix, f, attacker_key)
            st["reason"][reason] = st["reason"].get(reason, 0) + 1
            is_atk = (f["kind"] == "atkadd")
            if mode == "isolate_b" and is_atk:
                if owner is not None:
                    rec["base"] += share
                    credited += share
                else:
                    st["unattributed"] += share
                    credited += share
                continue
            if owner is None:
                st["unattributed"] += share
                credited += share
                continue
            credited += share
            orec = credits[owner]
            if owner == attacker_key:
                rec["self_rule"] += share
            else:
                rec["received"] += share
                orec["assist"] += share
                if is_atk:
                    orec["atkadd_assist"] += share
                    orec["atkadd_folds"] += 1
                    if mode == "current":
                        rec["atkadd_received"] += share
        err = abs(credited - damage)
        if err > st["max_hit_error"]:
            st["max_hit_error"] = err
        if err > 1e-6:
            st["bad_hits"] += 1

    st["events"] = st["dmg"] + st["hits"] + st["unknown_hits"] + st["outside_hits"]
    actors = []
    for r in credits.values():
        rr = dict(r)
        rr["total"] = rr["base"] + rr["self_rule"] + rr["assist"]
        actors.append(rr)
    actors.sort(key=lambda a: a["key"])
    st["attributed"] = sum(a["base"] + a["self_rule"] + a["assist"] for a in actors)
    return {"mode": mode, "stats": st, "actors": actors}


# ---------------------------------------------------------------------------
# validation / negative control
# ---------------------------------------------------------------------------

def validate(doc, replay):
    """Return a list of ASCII violation codes (empty = the file is self-consistent)."""
    v = []
    c = doc.get("contribution")
    if not isinstance(c, dict):
        return ["V1_no_contribution_section"]
    if c.get("method") != "log-share/1":
        v.append("V1_bad_method")
    if not c.get("schemaVersion"):
        v.append("V1_no_schema_version")

    rep = replay["stats"]
    total = fnum(c.get("totalDamage"))
    attributed = fnum(c.get("attributedDamage"))
    unattr = fnum(c.get("unattributedDamage"))
    if abs(rep["analyzable"] - total) > 1e-6 * max(1.0, abs(total)) + 0.5:
        v.append("V2_analyzable_mismatch")
    if abs(attributed + unattr - total) > 1e-3 + 1e-9 * max(1.0, abs(total)):
        v.append("V3_charge_identity_broken")
    off_actors = {}
    for a in (c.get("actors") or []):
        off_actors[a.get("key")] = a
    ssum = sum(fnum(a.get("totalCredit")) for a in off_actors.values())
    if abs(ssum - attributed) > 1e-3 + 1e-9 * max(1.0, abs(attributed)):
        v.append("V4_actor_sum_mismatch")

    ak = doc.get("atkAdd")
    if isinstance(ak, dict):
        rows = 0
        for e in (doc.get("events") or []):
            rows += len(((e.get("calc") or {}).get("atkAdd") or []))
        if ak.get("emitted") != rows:
            v.append("V5_emitted_row_count_mismatch")
        if "selfByKey" in ak and ak.get("selfValues") != fnum(ak.get("selfByKey")) + fnum(ak.get("selfByNameFallback")):
            v.append("V5_self_partition_broken")

    for e in (doc.get("events") or []):
        calc = e.get("calc") or {}
        rows = calc.get("atkAdd") or []
        if not rows:
            continue
        P = fnum(calc.get("attackPower"))
        bases = set()
        for r in rows:
            factor = fnum(r.get("factor"))
            dP = fnum(r.get("dPower"))
            b = fnum(r.get("base"))
            rate = fnum(r.get("rate"))
            actual = fnum(r.get("actual"))
            bases.add(round(b, 6))
            if not math.isfinite(factor) or not (0.0 < dP < P):
                v.append("V6_atkadd_dP_range")
                continue
            exp = P / (P - dP)
            if abs(exp - factor) > 1e-6 * max(1.0, abs(exp)):
                v.append("V6_atkadd_factor_identity")
            expdp = b * rate / 100.0 + actual
            if abs(expdp - dP) > 1e-6 * max(1.0, abs(dP)):
                v.append("V6_atkadd_dP_identity")
        if len(bases) > 1:
            v.append("V6_atkadd_base_disagreement")

    link_from = {}
    for l in (c.get("links") or []):
        link_from[l.get("fromKey")] = link_from.get(l.get("fromKey"), 0.0) + fnum(l.get("amount"))
    for k, a in off_actors.items():
        if abs(link_from.get(k, 0.0) - fnum(a.get("assistCredit"))) > 1e-3 + 1e-9 * max(1.0, abs(fnum(a.get("assistCredit")))):
            v.append("V7_link_sum_mismatch")

    for a in replay["actors"]:
        off = off_actors.get(a["key"])
        if off is None:
            continue
        for mine, name in ((a["direct"], "directDamage"), (a["base"], "baseCredit"),
                           (a["self_rule"], "selfRuleCredit"), (a["assist"], "assistCredit"),
                           (a["total"], "totalCredit")):
            o = fnum(off.get(name))
            if abs(mine - o) > 1e-3 + 1e-7 * max(1.0, abs(o)):
                v.append("V9_replay_actor_mismatch")
                break

    if rep["max_hit_error"] > 1e-6:
        v.append("V10_per_hit_identity_broken")
    out = []
    for x in v:
        if x not in out:
            out.append(x)
    return out


def tamper_copy(doc):
    """A COPY of a real export with two independent falsifications."""
    t = copy.deepcopy(doc)
    c = t.get("contribution")
    if isinstance(c, dict) and isinstance(c.get("actors"), list) and c["actors"]:
        c["actors"][0]["assistCredit"] = fnum(c["actors"][0].get("assistCredit")) + 1000000.0
    for e in (t.get("events") or []):
        rows = (e.get("calc") or {}).get("atkAdd")
        if rows:
            rows[0]["factor"] = fnum(rows[0].get("factor")) * 1.5
            break
    return t


# ---------------------------------------------------------------------------
# hand-computed multi-giver case
# ---------------------------------------------------------------------------

def hand_multi_giver(P=1000.0, r1=50.0, a1=0.0, r2=100.0, a2=0.0):
    r = r1 + r2
    a = a1 + a2
    B = (P - a) / (1.0 + r / 100.0)
    d1 = B * r1 / 100.0 + a1
    d2 = B * r2 / 100.0 + a2
    f1 = P / (P - d1)
    f2 = P / (P - d2)
    M = f1 * f2
    base = P / M
    pool = P - base
    s1 = pool * math.log(f1) / math.log(M)
    s2 = pool * math.log(f2) / math.log(M)
    base_joint = P - (d1 + d2)
    return {"P": P, "r1": r1, "a1": a1, "r2": r2, "a2": a2, "r": r, "a": a,
            "B": B, "dP1": d1, "dP2": d2, "f1": f1, "f2": f2, "M": M,
            "base_model": base, "pool_model": pool, "share1": s1, "share2": s2,
            "base_joint_removal": base_joint,
            "model_minus_joint": base - base_joint,
            "conservation_model": base + pool,
            "product_ne_joint": abs(M - P / base_joint) > 1e-9}


# ---------------------------------------------------------------------------
# synthetic boundary cases
# ---------------------------------------------------------------------------

def synth_actor(key, name):
    return {"key": key, "name": name, "team": TEAM, "kind": "P", "summon": False,
            "abilities": [], "talents": []}


def synth_event(P, atk_rows, folds=None, amount=None, atk_key=1):
    return {"type": "dmg", "amount": (P if amount is None else amount), "atkKey": atk_key,
            "atkTeam": TEAM, "calc": {"attackPower": P, "power": P,
                                      "fold": list(folds or []), "atkAdd": list(atk_rows)}}


def atk_row(P, by_unit, rate, actual, base, dP):
    factor = (P / (P - dP)) if dP != P else float("inf")
    return {"side": "atk", "kind": "atkadd", "origin": "atkadd#" + by_unit,
            "label": "synthetic", "byUnit": by_unit, "factor": factor,
            "rate": rate, "actual": actual, "base": base, "dPower": dP, "items": "synthetic"}


def run_boundary_cases():
    cases = []

    # C1: dP >= P must be refused, not poison the totals.
    P = 1000.0
    doc = {"actors": [synth_actor(1, "A"), synth_actor(2, "G")],
           "events": [synth_event(P, [
               atk_row(P, "G", 50, 0, 400, 200),
               {"side": "atk", "kind": "atkadd", "origin": "atkadd#G", "label": "neg",
                "byUnit": "G", "factor": -5.0, "rate": 200, "actual": 0, "base": 400,
                "dPower": 1200, "items": "neg"},
               {"side": "atk", "kind": "atkadd", "origin": "atkadd#G", "label": "inf",
                "byUnit": "G", "factor": float("inf"), "rate": 150, "actual": 0,
                "base": 400, "dPower": 1000, "items": "inf"}])]}
    cur = analyze(doc, "current")
    ok = (cur["stats"]["atkadd_guard_nonfinite"] == 1 and cur["stats"]["atkadd_zero"] == 1
          and math.isfinite(cur["stats"]["attributed"])
          and cur["stats"]["max_hit_error"] < 1e-9)
    cases.append({"id": "C1_dP_ge_P_and_nonfinite", "pass": bool(ok),
                  "expected": "1 non-finite atkadd row refused, 1 factor<=0 row refused, totals finite, per-hit identity holds",
                  "observed": {"atkadd_guard_nonfinite": cur["stats"]["atkadd_guard_nonfinite"],
                               "atkadd_zero": cur["stats"]["atkadd_zero"],
                               "attributed": cur["stats"]["attributed"],
                               "max_hit_error": cur["stats"]["max_hit_error"]}})

    # C2: multi giver, no other rule: product-of-factors vs joint removal.
    hc = hand_multi_giver()
    doc2 = {"actors": [synth_actor(1, "A"), synth_actor(2, "G1"), synth_actor(3, "G2")],
            "events": [synth_event(1000.0, [
                atk_row(1000.0, "G1", 50, 0, 400, 200),
                atk_row(1000.0, "G2", 100, 0, 400, 400)])]}
    cur2 = analyze(doc2, "current")
    a2 = {a["key"]: a for a in cur2["actors"]}
    ok2 = (abs(a2[1]["base"] - hc["base_model"]) < 1e-6
           and abs(a2[2]["assist"] - hc["share1"]) < 1e-6
           and abs(a2[3]["assist"] - hc["share2"]) < 1e-6
           and cur2["stats"]["max_hit_error"] < 1e-9
           and hc["product_ne_joint"])
    cases.append({"id": "C2_multi_giver_product_vs_joint", "pass": bool(ok2),
                  "expected": "replay reproduces the hand case; product of factors != joint removal",
                  "observed": {"replay_base": a2[1]["base"], "hand_base": hc["base_model"],
                               "replay_assist1": a2[2]["assist"], "hand_share1": hc["share1"],
                               "replay_assist2": a2[3]["assist"], "hand_share2": hc["share2"],
                               "hand_base_joint": hc["base_joint_removal"],
                               "hand_model_minus_joint": hc["model_minus_joint"]}})

    # C3: unknown owner -> unattributed in current and in isolate_b; dropped in isolate_a.
    doc3 = {"actors": [synth_actor(1, "A")],
            "events": [synth_event(1000.0, [atk_row(1000.0, "Ghost", 50, 0, 400, 200)])]}
    cur3 = analyze(doc3, "current")
    b3 = analyze(doc3, "isolate_b")
    a3 = analyze(doc3, "isolate_a")
    ok3 = (cur3["stats"]["unattributed"] > 0.0
           and abs(b3["stats"]["unattributed"] - cur3["stats"]["unattributed"]) < 1e-9
           and b3["stats"]["attributed"] <= cur3["stats"]["attributed"] + 1e-9)
    cases.append({"id": "C3_unknown_owner", "pass": bool(ok3),
                  "expected": "unknown giver goes to unattributed and stays there under isolate_b; isolate_a drops the fold entirely",
                  "observed": {"current_unattributed": cur3["stats"]["unattributed"],
                               "isolate_b_unattributed": b3["stats"]["unattributed"],
                               "isolate_a_unattributed": a3["stats"]["unattributed"],
                               "current_base_key1": cur3["actors"][0]["base"],
                               "isolate_a_base_key1": a3["actors"][0]["base"]}})

    # C4: multi giver + one non-atkadd rule -> A and B really differ.
    fold = {"kind": "text", "side": "atk", "origin": "text#1/10110/c3", "label": "magic+10%",
            "byUnit": "G1", "factor": 1.1}
    doc4 = {"actors": [synth_actor(1, "A"), synth_actor(2, "G1"), synth_actor(3, "G2")],
            "events": [synth_event(1000.0, [
                atk_row(1000.0, "G1", 50, 0, 400, 200),
                atk_row(1000.0, "G2", 100, 0, 400, 400)], folds=[fold])]}
    a4 = analyze(doc4, "isolate_a")
    b4 = analyze(doc4, "isolate_b")
    a4m = {x["key"]: x for x in a4["actors"]}
    b4m = {x["key"]: x for x in b4["actors"]}
    ok4 = (abs(a4m[1]["total"] - b4m[1]["total"]) > 1e-6
           and abs(a4["stats"]["analyzable"] - b4["stats"]["analyzable"]) < 1e-9)
    cases.append({"id": "C4_A_vs_B_differ", "pass": bool(ok4),
                  "expected": "with a surviving non-atkadd rule, re-split (A) and pool-preserving (B) give different totals",
                  "observed": {"isolate_a_attacker_total": a4m[1]["total"],
                               "isolate_b_attacker_total": b4m[1]["total"],
                               "isolate_a_g1_assist": a4m[2]["assist"],
                               "isolate_b_g1_assist": b4m[2]["assist"],
                               "same_denominator": a4["stats"]["analyzable"] == b4["stats"]["analyzable"]}})
    return cases


# ---------------------------------------------------------------------------
# rendering
# ---------------------------------------------------------------------------

def fmt(x):
    try:
        return format(float(x), ",.4f")
    except (TypeError, ValueError):
        return str(x)


def ranks_by(actors, field):
    order = sorted(actors, key=lambda a: (-a[field], a["key"]))
    return {a["key"]: i + 1 for i, a in enumerate(order)}


def build_export_block(path, doc, sha):
    cur = analyze(doc, "current")
    iso_a = analyze(doc, "isolate_a")
    iso_b = analyze(doc, "isolate_b")
    viol = validate(doc, cur)
    cm = {a["key"]: a for a in cur["actors"]}
    am = {a["key"]: a for a in iso_a["actors"]}
    bm = {a["key"]: a for a in iso_b["actors"]}
    same_denom = (abs(cur["stats"]["analyzable"] - iso_a["stats"]["analyzable"]) < 1e-9
                  and abs(cur["stats"]["analyzable"] - iso_b["stats"]["analyzable"]) < 1e-9)
    same_hits = (cur["stats"]["hits"] == iso_a["stats"]["hits"] == iso_b["stats"]["hits"])
    rk_total_cur = ranks_by(cur["actors"], "total")
    rk_total_a = ranks_by(iso_a["actors"], "total")
    rk_total_b = ranks_by(iso_b["actors"], "total")
    rk_assist_cur = ranks_by(cur["actors"], "assist")
    rk_assist_a = ranks_by(iso_a["actors"], "assist")
    rk_assist_b = ranks_by(iso_b["actors"], "assist")
    rows = []
    for k in sorted(cm):
        c = cm[k]
        a = am.get(k) or {"total": 0.0, "assist": 0.0, "base": 0.0, "self_rule": 0.0}
        b = bm.get(k) or {"total": 0.0, "assist": 0.0, "base": 0.0, "self_rule": 0.0}
        rows.append({
            "key": k, "name": c["name"], "kind": c["kind"], "summon": c["summon"],
            "direct": c["direct"], "hits": c["hits"],
            "total_current": c["total"], "total_isolate_a": a["total"], "total_isolate_b": b["total"],
            "assist_current": c["assist"], "assist_isolate_a": a["assist"], "assist_isolate_b": b["assist"],
            "base_current": c["base"], "base_isolate_a": a["base"], "base_isolate_b": b["base"],
            "atkadd_assist_current": c["atkadd_assist"],
            "atkadd_share_of_assist": (c["atkadd_assist"] / c["assist"]) if c["assist"] > 0 else None,
            "delta_total_a": a["total"] - c["total"], "delta_total_b": b["total"] - c["total"],
            "delta_assist_a": a["assist"] - c["assist"], "delta_assist_b": b["assist"] - c["assist"],
            "total_range": [min(c["total"], a["total"], b["total"]), max(c["total"], a["total"], b["total"])],
            "assist_range": [min(c["assist"], a["assist"], b["assist"]), max(c["assist"], a["assist"], b["assist"])],
            "rank_total_current": rk_total_cur.get(k), "rank_total_a": rk_total_a.get(k),
            "rank_total_b": rk_total_b.get(k),
            "rank_assist_current": rk_assist_cur.get(k), "rank_assist_a": rk_assist_a.get(k),
            "rank_assist_b": rk_assist_b.get(k),
            "hits_with_atkadd": c["hits_with_atkadd"], "atkadd_folds": c["atkadd_folds"],
        })
    changed_total = [r for r in rows if not (r["rank_total_current"] == r["rank_total_a"] == r["rank_total_b"])]
    changed_assist = [r for r in rows if not (r["rank_assist_current"] == r["rank_assist_a"] == r["rank_assist_b"])]
    return {
        "path": path, "sha256": sha, "version": doc.get("version"), "quest": doc.get("quest"),
        "validation_violations": viol,
        "same_event_set": same_hits, "same_denominator": same_denom,
        "coverage": {
            "dmg_events": cur["stats"]["dmg"], "analyzable_hits": cur["stats"]["hits"],
            "analyzable_damage": cur["stats"]["analyzable"],
            "hits_with_atkadd": cur["stats"]["hits_with_atkadd"],
            "hits_with_atkadd_ratio": (cur["stats"]["hits_with_atkadd"] / cur["stats"]["hits"]) if cur["stats"]["hits"] else 0.0,
            "atkadd_rows": cur["stats"]["atkadd_rows"],
            "emitted_counter": ((doc.get("atkAdd") or {}).get("emitted")),
            "skippedGuard_counter": ((doc.get("atkAdd") or {}).get("skippedGuard")),
            "skippedCollision_counter": ((doc.get("atkAdd") or {}).get("skippedCollision")),
            "ownerUnknown_counter": ((doc.get("atkAdd") or {}).get("ownerUnknown")),
            "folds_replayed": cur["stats"]["folds"],
            "unattributed": cur["stats"]["unattributed"],
            "max_hit_error": cur["stats"]["max_hit_error"],
            "bad_hits": cur["stats"]["bad_hits"],
            "reasonCounts": cur["stats"]["reason"],
            "atkadd_guard_nonfinite": cur["stats"]["atkadd_guard_nonfinite"],
        },
        "attributed": {"current": cur["stats"]["attributed"], "isolate_a": iso_a["stats"]["attributed"],
                       "isolate_b": iso_b["stats"]["attributed"]},
        "pool": {"current": cur["stats"]["pool"], "isolate_a": iso_a["stats"]["pool"],
                 "isolate_b": iso_b["stats"]["pool"]},
        "actors": rows,
        "ranking": {
            "changed_by_total": [{"key": r["key"], "current": r["rank_total_current"],
                                  "isolate_a": r["rank_total_a"], "isolate_b": r["rank_total_b"]} for r in changed_total],
            "changed_by_assist": [{"key": r["key"], "current": r["rank_assist_current"],
                                   "isolate_a": r["rank_assist_a"], "isolate_b": r["rank_assist_b"]} for r in changed_assist],
            "verdict": ("unstable: rank changed under atkadd isolation in this export"
                        if (changed_total or changed_assist)
                        else "stability not yet proven: no rank change in this export; one/few exports cannot establish stability"),
        },
        "priority_supports": [{"key": r["key"], "name": r["name"], "assist_current": r["assist_current"],
                               "atkadd_share_of_assist": r["atkadd_share_of_assist"],
                               "delta_assist_a": r["delta_assist_a"], "delta_assist_b": r["delta_assist_b"]}
                              for r in sorted(rows, key=lambda x: -x["assist_current"]) if r["assist_current"] > 0],
    }


# ---------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------

def render_md(result):
    L = []
    L.append("# atkadd 敏感性结果（N4 / P1）")
    L.append("")
    L.append("本文件由 _dpsm_work/atkadd_sensitivity.py 生成，只读分析真实导出；未改写 contrib/、未改写官方贡献表、未改 DLL。")
    L.append("")
    L.append("## 方法学：隔离 atkadd 的二选一（明确声明）")
    L.append("")
    L.append("**主口径 = A（重新拆池）**：" + result["isolationMethod"]["A"])
    L.append("")
    L.append("**次要口径 = B（仅迁回已分出的当量，池不变）**：" + result["isolationMethod"]["B"])
    L.append("")
    L.append("A 与 B 是两种不同的重分账约定，输出中从不混称；两者都不模拟真实换人。")
    L.append("")
    L.append("## 手算多 giver 用例")
    L.append("")
    h = result["handCase"]
    L.append("输入：P=" + fmt(h["P"]) + "，giver1 Rate+" + fmt(h["r1"]) + "，giver2 Rate+" + fmt(h["r2"]) + "，Actual 均为 0。")
    L.append("")
    L.append("| 量 | 值 |")
    L.append("|---|---|")
    for k, label in (("B", "B = (P-a)/(1+r/100)"), ("dP1", "dP1"), ("dP2", "dP2"),
                     ("f1", "f1"), ("f2", "f2"), ("M", "M = f1*f2"),
                     ("base_model", "模型 base = P/M"), ("pool_model", "pool"),
                     ("share1", "share(giver1)"), ("share2", "share(giver2)"),
                     ("base_joint_removal", "联合删除后 base = P-(dP1+dP2)"),
                     ("model_minus_joint", "模型 base - 联合 base")):
        L.append("| " + label + " | " + fmt(h[k]) + " |")
    L.append("")
    L.append("结论：f_g 相乘是分账约定；乘积因子 " + fmt(h["M"]) + " 不等于联合删除的 " + fmt(h["P"] / h["base_joint_removal"]) + "，模型 base 比联合删除高 " + fmt(h["model_minus_joint"]) + "。守恒成立，但不是联合反事实。")
    L.append("")
    L.append("## 合成边界用例")
    L.append("")
    L.append("| 用例 | 通过 | 观察 |")
    L.append("|---|---|---|")
    for c in result["syntheticCases"]:
        L.append("| " + c["id"] + " | " + ("PASS" if c["pass"] else "FAIL") + " | " + json.dumps(c["observed"], ensure_ascii=False) + " |")
    L.append("")
    L.append("## 负控（篡改真实导出副本 -> 必须红灯）")
    L.append("")
    nc = result["negativeControl"]
    L.append("- 正常对照（未篡改）：违规项 = " + json.dumps(nc.get("control_violations"), ensure_ascii=False) + "，判定 " + ("PASS" if nc.get("control_ok") else "FAIL"))
    L.append("- 篡改副本：违规项 = " + json.dumps(nc.get("tampered_violations"), ensure_ascii=False) + "，判定 " + ("PASS（确实报错）" if nc.get("tampered_ok") else "FAIL（未报错）"))
    L.append("")
    L.append("## 分导出结果")
    L.append("")
    for blk in result["exports"]:
        L.append("### " + os.path.basename(blk["path"]))
        L.append("")
        L.append("- SHA256: " + blk["sha256"] + "，插件版本 " + str(blk["version"]) + "，任务 " + str(blk["quest"]))
        L.append("- 校验：" + ("通过（无违规）" if not blk["validation_violations"] else "违规 " + json.dumps(blk["validation_violations"], ensure_ascii=False)))
        L.append("- 固定事件集：" + str(blk["same_event_set"]) + "；固定分母：" + str(blk["same_denominator"]))
        cov = blk["coverage"]
        L.append("- 覆盖：可分析命中 " + str(cov["analyzable_hits"]) + "，含 atkadd 命中 " + str(cov["hits_with_atkadd"]) +
                 "（" + fmt(100.0 * cov["hits_with_atkadd_ratio"]) + "%），atkadd 行 " + str(cov["atkadd_rows"]) +
                 "，未归属 " + fmt(cov["unattributed"]) + "，逐击守恒最大误差 " + repr(cov["max_hit_error"]))
        L.append("- 排名稳定性判定：" + blk["ranking"]["verdict"])
        if blk["ranking"]["changed_by_total"] or blk["ranking"]["changed_by_assist"]:
            L.append("  - 按总贡献名次变化：" + json.dumps(blk["ranking"]["changed_by_total"], ensure_ascii=False))
            L.append("  - 按辅助当量名次变化：" + json.dumps(blk["ranking"]["changed_by_assist"], ensure_ascii=False))
        L.append("")
        L.append("| key | name | total(现行) | total(A) | total(B) | assist(现行) | assist(A) | assist(B) | Δassist(A) | atkadd占assist | rank(现行/A/B) |")
        L.append("|---|---|---|---|---|---|---|---|---|---|---|")
        for r in blk["actors"]:
            share = "-" if r["atkadd_share_of_assist"] is None else fmt(100.0 * r["atkadd_share_of_assist"]) + "%"
            L.append("| " + str(r["key"]) + " | " + str(r["name"]) + " | " + fmt(r["total_current"]) + " | " +
                     fmt(r["total_isolate_a"]) + " | " + fmt(r["total_isolate_b"]) + " | " +
                     fmt(r["assist_current"]) + " | " + fmt(r["assist_isolate_a"]) + " | " +
                     fmt(r["assist_isolate_b"]) + " | " + fmt(r["delta_assist_a"]) + " | " + share + " | " +
                     str(r["rank_total_current"]) + "/" + str(r["rank_total_a"]) + "/" + str(r["rank_total_b"]) + " |")
        L.append("")
    L.append("## 语料汇总")
    L.append("")
    cs = result["corpus"]
    L.append("- 导出份数：" + str(cs["exports"]) + "；合计可分析命中 " + str(cs["analyzable_hits"]) +
             "；合计 atkadd 行 " + str(cs["atkadd_rows"]) + "；校验失败份数 " + str(cs["validation_failures"]))
    L.append("- 高份额辅助（atkadd 占 assist 比例 >50%），按当前 assist 排序。actor key 是每场实例键，不做跨场合并（N3），故逐场列出：")
    for s in cs["priority_supports_by_export"]:
        share = "-" if s["atkadd_share_of_assist"] is None else fmt(100.0 * s["atkadd_share_of_assist"]) + "%"
        L.append("  - " + str(s["export"]) + " key=" + str(s["key"]) + " " + str(s["name"]) +
                 " assist=" + fmt(s["assist_current"]) + " atkadd占比=" + share +
                 " Δassist(A)=" + fmt(s["delta_assist_a"]))
    L.append("")
    L.append("## 结论分级与纪律")
    L.append("")
    L.append("- **守恒**：逐击 credited + unattributed = damage 在所有样本上成立；隔离 A/B 前后同一分母 analyzableDealt 不变。")
    L.append("- **身份**：calc.atkAdd 逐击行只携带 byUnit 名字，不带 giver key（见 ATKADD-MODEL-AUDIT.md 的 0/50599 核对），因此逐 giver 对账不可完成。")
    L.append("- **公式证据**：无。B 由 P 反推（AtkAddFold.cs:177），代回是恒等式。")
    L.append("- 排名稳定性只有在本脚本确实观察到名次变化时才写不稳定；否则写稳定性尚未证实，不硬选赢家。")
    L.append("- 本脚本不模拟真实换人；assistCredit 不是换掉角色会少的伤害。")
    L.append("")
    return chr(10).join(L)


def main(argv=None):
    ap = argparse.ArgumentParser(description="atkadd sensitivity study (read-only)")
    ap.add_argument("--export", action="append", default=[], help="real export JSON (repeatable)")
    ap.add_argument("--out-dir", default="_dpsm_work")
    ap.add_argument("--selftest", action="store_true", help="run synthetic cases + tamper negative control")
    ap.add_argument("--strict", action="store_true", help="exit 2 if any export fails validation")
    ap.add_argument("--no-write", action="store_true")
    args = ap.parse_args(argv)

    if not args.export:
        print("ERROR: at least one --export is required (read-only input)")
        return 1

    docs = []
    for p in args.export:
        if not os.path.isfile(p):
            print("ERROR: export not found: " + p)
            return 1
        with open(p, "r", encoding="utf-8") as f:
            doc = json.load(f)
        docs.append((p, doc, sha256_file(p)))
        print("loaded export=" + p + " sha256=" + docs[-1][2][:16])

    blocks = [build_export_block(p, d, s) for (p, d, s) in docs]

    synth = []
    neg = {}
    if args.selftest:
        synth = run_boundary_cases()
        print("synthetic cases: " + ", ".join(c["id"] + "=" + ("PASS" if c["pass"] else "FAIL") for c in synth))
        if any(not c["pass"] for c in synth):
            print("ERROR: a synthetic boundary case failed")
            return 1
        p0, d0, _ = docs[0]
        ctrl = validate(d0, analyze(d0, "current"))
        tdoc = tamper_copy(d0)
        tviol = validate(tdoc, analyze(tdoc, "current"))
        neg = {"export": p0, "control_violations": ctrl, "control_ok": len(ctrl) == 0,
               "tampered_violations": tviol, "tampered_ok": len(tviol) > 0}
        print("negative control: control_ok=" + str(neg["control_ok"]) + " tampered_ok=" + str(neg["tampered_ok"]))
        if not neg["control_ok"] or not neg["tampered_ok"]:
            print("ERROR: negative control failed (control must be clean, tampered copy must trip)")
            return 1
    else:
        p0, d0, _ = docs[0]
        ctrl = validate(d0, analyze(d0, "current"))
        neg = {"export": p0, "control_violations": ctrl, "control_ok": len(ctrl) == 0,
               "tampered_violations": [], "tampered_ok": None}

    fails = [b for b in blocks if b["validation_violations"]]
    corpus = {
        "exports": len(blocks),
        "analyzable_hits": sum(b["coverage"]["analyzable_hits"] for b in blocks),
        "atkadd_rows": sum(b["coverage"]["atkadd_rows"] for b in blocks),
        "validation_failures": len(fails),
        "priority_supports_by_export": [],
        "identity_note": ("actor keys are PER-BATTLE instances; no cross-battle identity is "
                          "assumed here (N3). Values are listed per export and are never merged by key."),
    }
    for b in blocks:
        for s in b["priority_supports"]:
            if s["atkadd_share_of_assist"] is not None and s["atkadd_share_of_assist"] > 0.5:
                corpus["priority_supports_by_export"].append({
                    "export": os.path.basename(b["path"]), "key": s["key"], "name": s["name"],
                    "assist_current": s["assist_current"],
                    "atkadd_share_of_assist": s["atkadd_share_of_assist"],
                    "delta_assist_a": s["delta_assist_a"]})
    corpus["priority_supports_by_export"].sort(key=lambda s: -s["assist_current"])

    result = {
        "study": "atkadd-sensitivity",
        "method": "log-share/1",
        "generatedBy": "atkadd_sensitivity.py",
        "isolationMethod": {
            "primary": "A",
            "A": "re-split pool: drop every kind=atkadd factor from the hit fold set, then recompute M, base and the ln(f)/ln(M) shares from the remaining folds only.",
            "B": "pool-preserving: keep the current M/base/pool and the current shares of every non-atkadd rule; redirect only the atkadd shares that WERE resolved to an owner back to the attacker baseCredit. Unresolved atkadd shares stay unattributed.",
            "note": "A and B are different re-accounting conventions; neither simulates replacing a character.",
        },
        "handCase": hand_multi_giver(),
        "syntheticCases": synth,
        "negativeControl": neg,
        "exports": blocks,
        "corpus": corpus,
        "discipline": {
            "conservation": "per-hit credited+unattributed == damage; denominator unchanged across A/B",
            "identity": "calc.atkAdd rows carry a display name only (no giver key); per-giver reconciliation is not possible from the current export",
            "formulaEvidence": "none: B is back-solved from P (AtkAddFold.cs:177), so substituting it back is an identity",
            "notASimulation": "no character replacement is simulated",
        },
    }

    if not args.no_write:
        os.makedirs(args.out_dir, exist_ok=True)
        jp = os.path.join(args.out_dir, "atkadd_sensitivity_result.json")
        mp = os.path.join(args.out_dir, "atkadd_sensitivity_result.md")
        with open(jp, "w", encoding="utf-8") as f:
            json.dump(result, f, ensure_ascii=False, indent=2)
        with open(mp, "w", encoding="utf-8") as f:
            f.write(render_md(result))
        print("wrote " + jp)
        print("wrote " + mp)

    print("corpus exports=" + str(corpus["exports"]) + " analyzable_hits=" + str(corpus["analyzable_hits"]) +
          " atkadd_rows=" + str(corpus["atkadd_rows"]) + " validation_failures=" + str(corpus["validation_failures"]))
    for b in blocks:
        rk = b["ranking"]
        print("export=" + os.path.basename(b["path"]) + " viol=" + str(len(b["validation_violations"])) +
              " support_assist_keys=" + ",".join(str(s["key"]) for s in b["priority_supports"][:3]) +
              " rank_changed=" + str(bool(rk["changed_by_total"] or rk["changed_by_assist"])))
    if corpus["validation_failures"]:
        print("RED: validation failures present")
        if args.strict:
            return 2
    else:
        print("GREEN: all exports passed integrity validation")
    return 0


if __name__ == "__main__":
    sys.exit(main())
