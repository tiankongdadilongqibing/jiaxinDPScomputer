# -*- coding: utf-8 -*-
"""Adversarial independent audit of _dpsm_work/contrib (contribution core).

Does NOT import anything from contrib.*. Re-implements the ladder from
CONTRIBUTION-DATA-DICTIONARY.md section 4 and recomputes every number.
All output goes to UTF-8 files; stdout stays ASCII.
"""
import io, json, math, os, sys, collections

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
EXPORTS = os.path.join(ROOT, "BepInEx", "plugins", "DpsMeter", "exports")
EXPORT155 = os.path.join(EXPORTS, "battle_411001_20261003_235204.json")
OUTDIR = os.path.join(ROOT, "_dpsm_work", "review_contrib_core")
REPORT_JSON = os.path.join(ROOT, "_dpsm_work", "contrib", "reports",
                           "report_battle_411001_20261003_235204.json")

LOG = []
def p(*a):
    LOG.append(" ".join(str(x) for x in a))

def main():
    with io.open(EXPORT155, "r", encoding="utf-8") as fh:
        raw = json.load(fh)
    actors = raw["actors"]
    by_key = dict((a["key"], a) for a in actors)
    TEAM = 1
    team_actors = [a for a in actors if a.get("team") == TEAM]

    name_idx = {}
    seen_name = {}
    for a in team_actors:
        seen_name.setdefault(a["name"], []).append(a)
    for n, arr in seen_name.items():
        name_idx[n] = arr[0] if len(arr) == 1 else None
    all_names = set(a["name"] for a in actors if a.get("name"))
    by_ab_id, by_ab_name = {}, {}
    for row in actors:
        if row.get("team") != TEAM:
            continue
        ref = by_key.get(row.get("key"))
        for ab in (row.get("abilities") or []):
            if ab.get("id") is not None:
                by_ab_id.setdefault(ab["id"], []).append(ref)
            if ab.get("name"):
                by_ab_name.setdefault(ab["name"], []).append(ref)
        for t in (row.get("talents") or []):
            if t.get("abilityId") is not None:
                by_ab_id.setdefault(t["abilityId"], []).append(ref)
            if t.get("ability"):
                by_ab_name.setdefault(t["ability"], []).append(ref)
    for d in (by_ab_id, by_ab_name):
        for k in list(d):
            u = {}
            for r in d[k]:
                u[r["key"]] = r
            d[k] = list(u.values())

    import re
    NAME_RE = re.compile(r"\[([^\]]+)\]")
    AID_RE = re.compile(r"^(?:text|talent)#(?:buff)?\d+/(\d+)(?:/|$)")

    def ability_id_of(o):
        m = AID_RE.match(str(o or ""))
        return int(m.group(1)) if m else None

    def rname(f):
        m = NAME_RE.search(f.get("label") or "")
        if m:
            return m.group(1)
        lab = (f.get("label") or "").strip()
        return lab if lab else str(f.get("origin") or "")

    def resolve(f, attacker):
        bu = f.get("byUnit") or ""
        if bu:
            r = name_idx.get(bu)
            if r is not None:
                return r, "byUnit"
            if bu in name_idx and name_idx[bu] is None:
                return None, "byUnit_ambiguous"
            if bu in all_names:
                return None, "byUnit_outside"
            return None, "byUnit_unknown"
        kind = f.get("kind")
        if kind in ("text", "talent"):
            aid = ability_id_of(f.get("origin"))
            h = by_ab_id.get(aid, []) if aid is not None else []
            if len(h) == 1:
                return h[0], "ability_holder_unique"
            if len(h) > 1:
                if any(x["key"] == attacker["key"] for x in h):
                    return attacker, "ability_holder_attacker"
                return None, "ambiguous_multi_holder"
            return attacker, "attacker_default"
        if kind == "global":
            m = NAME_RE.search(f.get("label") or "")
            h = by_ab_name.get(m.group(1), []) if m else []
            if len(h) == 1:
                return h[0], "global_name_unique"
            return None, "global_ambiguous"
        return None, "unknown_kind"

    EPS = 1e-12
    acc = {}
    for a in team_actors:
        acc[a["key"]] = {"key": a["key"], "name": a["name"], "direct": 0.0, "hits": 0,
                         "base": 0.0, "self": 0.0, "assist": 0.0, "received": 0.0}
    analyzable = 0.0
    pool_total = 0.0
    unattr = 0.0
    reasons = collections.Counter()
    per_hit_max_err = 0.0
    per_hit_bad = 0
    hits = 0
    fold_inst = []
    hit_recs = []
    no_calc_analyzable = 0
    amount_zero_analyzable = 0
    atkteam_mismatch = 0
    hits_mult_eq_1_with_folds = 0
    single_fold_hits = 0
    nf_hist = collections.Counter()

    for i, e in enumerate(raw["events"]):
        if e.get("type") != "dmg":
            continue
        key = e.get("atkKey")
        row = by_key.get(key) if key is not None else None
        if row is not None and e.get("atkTeam") is not None and e["atkTeam"] != row.get("team"):
            atkteam_mismatch += 1
        if row is None or row.get("team") != TEAM:
            continue
        attacker = row
        try:
            amount = float(e.get("amount") or 0.0)
        except (TypeError, ValueError):
            amount = 0.0
        calc = e.get("calc") or {}
        if not e.get("calc"):
            no_calc_analyzable += 1
        if amount == 0.0:
            amount_zero_analyzable += 1
        folds = []
        for f in (calc.get("fold") or []):
            try:
                fv = float(f.get("factor") or 0.0)
            except (TypeError, ValueError):
                fv = 0.0
            if not (fv > 0.0):
                continue
            if abs(fv - 1.0) < EPS:
                continue
            g = dict(f)
            g["factor"] = fv
            folds.append(g)
        M = 1.0
        for f in folds:
            M *= f["factor"]
        nf_hist[len(folds)] += 1
        if len(folds) == 1:
            single_fold_hits += 1
        if folds and abs(M - 1.0) < EPS:
            hits_mult_eq_1_with_folds += 1
        if not folds or abs(M - 1.0) < EPS:
            base, pool, shares = amount, 0.0, [0.0] * len(folds)
        else:
            base = amount / M
            pool = amount - base
            lnM = math.log(M)
            shares = [(pool * math.log(f["factor"]) / lnM) if lnM != 0 else 0.0 for f in folds]
        a = acc[attacker["key"]]
        a["direct"] += amount
        a["hits"] += 1
        a["base"] += base
        analyzable += amount
        pool_total += pool
        hits += 1
        credited = base
        for k, f in enumerate(folds):
            owner, reason = resolve(f, attacker)
            reasons[reason] += 1
            share = shares[k]
            fold_inst.append({"i": i, "rule": rname(f), "kind": f.get("kind"),
                              "side": f.get("side"), "origin": f.get("origin"),
                              "factor": f["factor"], "owner": owner["key"] if owner else None,
                              "reason": reason, "share": share})
            if owner is None:
                unattr += share
            elif owner["key"] == attacker["key"]:
                a["self"] += share
            else:
                a["received"] += share
                acc[owner["key"]]["assist"] += share
            credited += share
        err = abs(credited - amount)
        if err > per_hit_max_err:
            per_hit_max_err = err
        if amount != 0.0 and err > 1e-6:
            per_hit_bad += 1
        hit_recs.append({"i": i, "attacker": attacker["key"], "amount": amount, "M": M,
                         "base": base, "pool": pool, "nf": len(folds), "credited": credited})

    tot_base = sum(a["base"] for a in acc.values())
    tot_self = sum(a["self"] for a in acc.values())
    tot_assist = sum(a["assist"] for a in acc.values())
    total_credit = tot_base + tot_self + tot_assist

    p("=== PART 1: independent recomputation (1.5.5) ===")
    p("hits(analyzable) =", hits, " analyzableDealt =", analyzable)
    p("poolTotal =", pool_total, " poolShare =", pool_total / analyzable)
    p("unattributedCredit =", unattr)
    p("reasonCounts =", dict(reasons))
    p("totBase =", repr(tot_base), " totSelf =", repr(tot_self), " totAssist =", repr(tot_assist))
    p("totalCredit =", repr(total_credit))
    p("identity totalCredit+unattr-analyzable =", repr(total_credit + unattr - analyzable),
      " relative =", abs(total_credit + unattr - analyzable) / analyzable)
    p("perHitMaxErr =", repr(per_hit_max_err), " perHitBad =", per_hit_bad)
    p("ROUNDED columns: base=%d self=%d assist=%d sum_of_rounded=%d analyzable_rounded=%d" %
      (round(tot_base), round(tot_self), round(tot_assist),
       round(tot_base) + round(tot_self) + round(tot_assist), round(analyzable)))

    p("")
    p("=== PART 2: knownMult vs product(calc.fold factors) ===")
    km_present = 0
    km_all = 0
    km_filt = 0
    km_zero = 0
    max_abs_filt = 0.0
    max_rel_filt = 0.0
    max_rel_an = 0.0
    max_abs_an = 0.0
    worst = None
    for i, e in enumerate(raw["events"]):
        if e.get("type") != "dmg":
            continue
        c = e.get("calc")
        if not c:
            continue
        km = c.get("knownMult")
        if km is None:
            continue
        km_present += 1
        p_all, p_f, nf = 1.0, 1.0, 0
        for f in (c.get("fold") or []):
            try:
                fv = float(f.get("factor") or 0.0)
            except (TypeError, ValueError):
                fv = 0.0
            p_all *= fv
            if fv > 0.0 and abs(fv - 1.0) >= EPS:
                p_f *= fv
                nf += 1
        if abs(km - p_all) > 1e-9:
            km_all += 1
        if abs(km - p_f) > 1e-9:
            km_filt += 1
        if km == 0:
            km_zero += 1
        if abs(km - p_f) > max_abs_filt:
            max_abs_filt = abs(km - p_f)
            worst = (i, km, p_f, nf)
        rel = abs(km - p_f) / abs(km) if km else (1.0 if p_f else 0.0)
        if rel > max_rel_filt:
            max_rel_filt = rel
        row = by_key.get(e.get("atkKey"))
        if row is not None and row.get("team") == TEAM:
            if abs(km - p_f) > max_abs_an:
                max_abs_an = abs(km - p_f)
            rel2 = abs(km - p_f) / abs(km) if km else (1.0 if p_f else 0.0)
            if rel2 > max_rel_an:
                max_rel_an = rel2
    p("knownMult present on", km_present, "dmg rows")
    p("hits where knownMult != product(fold, incl <=0 and ==1):", km_all)
    p("hits where knownMult != product(fold, filtered >0 and !=1):", km_filt)
    p("hits with knownMult == 0:", km_zero)
    p("max abs deviation (all hits) =", max_abs_filt, " worst =", worst)
    p("max relative deviation (all hits) =", max_rel_filt)
    p("max abs deviation (OUR-TEAM hits only) =", max_abs_an)
    p("max relative deviation (OUR-TEAM hits only) =", max_rel_an)

    facvals = collections.Counter()
    faclt1 = collections.Counter()
    for e in raw["events"]:
        if e.get("type") != "dmg":
            continue
        for f in ((e.get("calc") or {}).get("fold") or []):
            fv = float(f.get("factor") or 0.0)
            facvals[round(fv, 6)] += 1
            if 0 < fv < 1:
                faclt1[round(fv, 6)] += 1
    p("distinct factor values:", dict(sorted(facvals.items())))
    p("factor values strictly between 0 and 1 (negative shares):", dict(faclt1))

    p("")
    p("=== PART 3: duplicate-computation checks ===")
    dup_origin_hits = 0
    dup_origin_inst = 0
    for e in raw["events"]:
        if e.get("type") != "dmg":
            continue
        orgs = [f.get("origin") for f in ((e.get("calc") or {}).get("fold") or [])]
        c = collections.Counter(orgs)
        d = sum(v - 1 for v in c.values() if v > 1)
        if d:
            dup_origin_hits += 1
            dup_origin_inst += d
    p("hits with the SAME fold origin listed more than once:", dup_origin_hits,
      " extra instances:", dup_origin_inst)
    dup_nf_same_kind = 0
    dup_nf_cross_kind = 0
    for e in raw["events"]:
        if e.get("type") != "dmg":
            continue
        c = ((e.get("calc") or {}).get("fold") or [])
        keys = {}
        cross = False
        same = False
        for f in c:
            kk = (rname(f), round(float(f.get("factor") or 0.0), 6))
            if kk in keys:
                if keys[kk] == f.get("kind"):
                    same = True
                else:
                    cross = True
            keys[kk] = f.get("kind")
        if same:
            dup_nf_same_kind += 1
        if cross:
            dup_nf_cross_kind += 1
    p("hits with repeated (ruleName, factor) SAME kind:", dup_nf_same_kind)
    p("hits with repeated (ruleName, factor) CROSS kind (core duplicate_in_hit):", dup_nf_cross_kind)
    fold_in_cancel = 0
    cancel_inst = 0
    cancel_with_by = 0
    for e in raw["events"]:
        if e.get("type") != "dmg":
            continue
        calc = e.get("calc") or {}
        orgs = set(f.get("origin") for f in (calc.get("fold") or []))
        for x in (calc.get("cancel") or []):
            cancel_inst += 1
            if x.get("byUnit"):
                cancel_with_by += 1
            if x.get("origin") in orgs:
                fold_in_cancel += 1
    p("cancel records total:", cancel_inst, " with byUnit:", cancel_with_by,
      " whose origin also appears in the SAME hit's fold list:", fold_in_cancel)
    cross_channel_hits = 0
    cross_channel_same_factor = 0
    global_no_bracket = 0
    global_total = 0
    for e in raw["events"]:
        if e.get("type") != "dmg":
            continue
        calc = e.get("calc") or {}
        textnames = {}
        for f in (calc.get("fold") or []):
            if f.get("kind") in ("text", "talent"):
                textnames.setdefault(rname(f), []).append(round(float(f.get("factor") or 0), 6))
            if f.get("kind") == "global":
                global_total += 1
                if not NAME_RE.search(f.get("label") or ""):
                    global_no_bracket += 1
        for f in (calc.get("fold") or []):
            if f.get("kind") == "global":
                nm = rname(f)
                if nm in textnames:
                    cross_channel_hits += 1
                    if round(float(f.get("factor") or 0), 6) in textnames[nm]:
                        cross_channel_same_factor += 1
    p("hits carrying a global fold whose name ALSO appears as a text/talent fold in the same hit:",
      cross_channel_hits)
    p("  ... and with the SAME factor too:", cross_channel_same_factor)
    p("global folds total:", global_total, " global labels without bracket:", global_no_bracket)

    p("")
    p("=== PART 4: coverage / attribution-path audit ===")
    kind_census = collections.Counter()
    kind_byunit = collections.Counter()
    for fi in fold_inst:
        kind_census[fi["kind"]] += 1
    for i, e in enumerate(raw["events"]):
        if e.get("type") != "dmg":
            continue
        row = by_key.get(e.get("atkKey"))
        if row is None or row.get("team") != TEAM:
            continue
        for f in ((e.get("calc") or {}).get("fold") or []):
            fv = float(f.get("factor") or 0.0)
            if not (fv > 0) or abs(fv - 1) < EPS:
                continue
            if f.get("byUnit"):
                kind_byunit[f.get("kind")] += 1
    p("fold instances counted (analyzable):", dict(kind_census), " total:", sum(kind_census.values()))
    p("fold instances carrying byUnit (analyzable):", dict(kind_byunit))
    for k in sorted(kind_census):
        p("   byUnit coverage %-8s %d/%d" % (k, kind_byunit.get(k, 0), kind_census[k]))
    raw_census = collections.Counter()
    raw_byunit = collections.Counter()
    raw_zero = collections.Counter()
    for e in raw["events"]:
        if e.get("type") != "dmg":
            continue
        for f in ((e.get("calc") or {}).get("fold") or []):
            fv = float(f.get("factor") or 0.0)
            raw_census[f.get("kind")] += 1
            if f.get("byUnit"):
                raw_byunit[f.get("kind")] += 1
            if not (fv > 0):
                raw_zero[f.get("kind")] += 1
    p("fold instances over ALL dmg events:", dict(raw_census), " total:", sum(raw_census.values()))
    p("  ... carrying byUnit:", dict(raw_byunit))
    p("  ... factor<=0:", dict(raw_zero))
    dmg_events = [e for e in raw["events"] if e.get("type") == "dmg"]
    n_dmg = len(dmg_events)
    q_attacker = sum(1 for e in dmg_events if e.get("attacker") == "?")
    key_present = sum(1 for e in dmg_events if e.get("atkKey") is not None)
    our_team_rows = sum(1 for e in dmg_events
                        if by_key.get(e.get("atkKey")) is not None
                        and by_key[e["atkKey"]].get("team") == TEAM)
    p("dmg events:", n_dmg, " attacker=='?':", q_attacker, " atkKey non-null:", key_present,
      " actor-row team==1:", our_team_rows)
    p("core key-coverage denominator (hits + unattributed_events) =",
      hits + q_attacker, "->", hits / float(hits + q_attacker))
    outside = sum(1 for e in dmg_events if by_key.get(e.get("atkKey")) is not None
                  and by_key[e["atkKey"]].get("team") != TEAM)
    p("events whose actor row is team!=1 (excluded from analyzable):", outside)
    p("atkTeam vs actor-row team mismatches:", atkteam_mismatch)
    p("team duplicate names:", sorted(set(n for n, arr in seen_name.items() if len(arr) > 1)))
    all_dups = collections.Counter(a["name"] for a in actors)
    p("export-wide duplicate name count (name->n):", sum(1 for n, c in all_dups.items() if c > 1))

    p("")
    p("=== PART 5: uncovered / edge-case census ===")
    p("analyzable hits missing calc entirely:", no_calc_analyzable)
    p("analyzable hits with amount == 0:", amount_zero_analyzable)
    p("hits with folds where product(M) == 1:", hits_mult_eq_1_with_folds)
    p("single-fold hits:", single_fold_hits)
    p("fold-count histogram (analyzable):", dict(sorted(nf_hist.items())))
    p("folds with factor==1 anywhere:", sum(v for k, v in facvals.items() if k == 1.0))
    multi = {k: [r["name"] for r in v] for k, v in by_ab_id.items() if len(v) > 1}
    p("ability ids held by >1 team actor:", len(multi), " e.g.", list(multi.items())[:5])
    amb_hits = sum(1 for fi in fold_inst if fi["reason"] == "ambiguous_multi_holder")
    p("folds resolved to ambiguous_multi_holder:", amb_hits)
    p("folds resolved to attacker_default (name fallback):", reasons.get("attacker_default", 0))
    p("folds resolved to ability_holder_attacker:", reasons.get("ability_holder_attacker", 0))
    p("folds resolved to ability_holder_unique:", reasons.get("ability_holder_unique", 0))
    p("folds resolved to global_name_unique:", reasons.get("global_name_unique", 0))
    p("folds resolved to byUnit:", reasons.get("byUnit", 0))
    bad_bu = collections.Counter()
    for e in raw["events"]:
        if e.get("type") != "dmg":
            continue
        for f in ((e.get("calc") or {}).get("fold") or []):
            bu = f.get("byUnit") or ""
            if bu and bu not in seen_name:
                bad_bu[bu] += 1
    p("byUnit values not matching any team name:", dict(bad_bu))
    talent_origins = collections.Counter()
    text_origin_parse_fail = collections.Counter()
    for e in raw["events"]:
        if e.get("type") != "dmg":
            continue
        for f in ((e.get("calc") or {}).get("fold") or []):
            if f.get("kind") == "talent":
                talent_origins[str(f.get("origin"))] += 1
            if f.get("kind") in ("text", "talent") and ability_id_of(f.get("origin")) is None:
                text_origin_parse_fail[str(f.get("origin"))] += 1
    p("talent origin shapes:", len(talent_origins), " sample:", list(talent_origins)[:5])
    p("text/talent origins the abilityId regex cannot parse:", dict(text_origin_parse_fail))

    p("")
    p("=== PART 6: dictionary section 10 table recheck ===")
    doc = None
    try:
        with io.open(REPORT_JSON, "r", encoding="utf-8") as fh:
            doc = json.load(fh)["contribution"]
    except Exception as ex:
        p("could not read report json:", ex)
    if doc:
        for a in sorted(acc.values(), key=lambda x: -(x["base"] + x["self"] + x["assist"])):
            rep = None
            for ra in doc["actors"]:
                if ra["key"] == a["key"]:
                    rep = ra
                    break
            tot = a["base"] + a["self"] + a["assist"]
            if rep is None:
                p("ACT %-16s key=%-4s NOT IN REPORT JSON" % (a["name"], a["key"]))
                continue
            p("ACT %-16s key=%-4s base=%.6f self=%.6f assist=%.6f total=%.6f direct=%.1f recv=%.6f"
              % (a["name"], a["key"], a["base"], a["self"], a["assist"], tot, a["direct"], a["received"]))
            p("   delta vs report: dBase=%.3e dSelf=%.3e dAssist=%.3e dTotal=%.3e dDirect=%.3e dRecv=%.3e"
              % (a["base"] - rep["baseCredit"], a["self"] - rep["selfRuleCredit"],
                 a["assist"] - rep["assistCredit"], tot - rep["totalCredit"],
                 a["direct"] - rep["directDamage"], a["received"] - rep["receivedAssist"]))
        p("report totals:", json.dumps(doc["totals"], ensure_ascii=False))
        p("report coverage:", json.dumps(doc["coverage"], ensure_ascii=False))
        p("report totalDamage=%s attributedDamage=%s unattributedDamage=%s"
          % (doc["totalDamage"], doc["attributedDamage"], doc["unattributedDamage"]))
        p("attributed + unattributed = %.6f analyzable=%s"
          % (doc["attributedDamage"] + doc["unattributedDamage"], doc["totals"]["analyzableDealt"]))
        p("report rule rows: %d links: %d actors: %d"
          % (len(doc["rules"]), len(doc["links"]), len(doc["actors"])))
        nz = []
        for r in doc["rules"]:
            for b in r["beneficiaries"]:
                if b.get("folds"):
                    nz.append((r["ruleName"], b["key"], b["folds"]))
        p("beneficiary entries whose folds != 0 in report json:", len(nz))
        rule_agg = {}
        for fi in fold_inst:
            k = (fi["rule"], fi["kind"], fi["side"], fi["owner"])
            r = rule_agg.setdefault(k, {"name": fi["rule"], "kind": fi["kind"], "side": fi["side"],
                                        "owner": fi["owner"], "dmg": 0.0, "folds": 0})
            r["dmg"] += fi["share"]
            r["folds"] += 1
        top = sorted(rule_agg.values(), key=lambda x: -x["dmg"])[:8]
        p("my rule top8:")
        for r in top:
            p("   %-30s %-8s %-4s owner=%-4s dmg=%.6f folds=%d"
              % (r["name"], r["kind"], r["side"], r["owner"], r["dmg"], r["folds"]))
        p("sum(rule damage) =", sum(r["dmg"] for r in rule_agg.values()), " pool_total =", pool_total)
        link_sum = 0.0
        for r in doc["links"]:
            link_sum += r["amount"]
        p("sum(links.amount) in report json =", link_sum, " total assist =", tot_assist)
        recv_total = sum(a["received"] for a in acc.values())
        p("sum(received) =", recv_total, " total assist =", tot_assist,
          " equal:", abs(recv_total - tot_assist) < 1e-6)
        p("sum(direct) =", sum(a["direct"] for a in acc.values()), " analyzable =", analyzable)
        summon_keys = [a["key"] for a in team_actors if a.get("summon")]
        summon_sum = sum(acc[k]["base"] + acc[k]["self"] + acc[k]["assist"] for k in summon_keys
                         if k in acc)
        p("summon actor keys=%s subtotal totalCredit=%.6f (dictionary says 1533932)"
          % (summon_keys, summon_sum))
        p("non-summon totalCredit=%.6f" % (total_credit - summon_sum))
        top6 = sorted([a for a in acc.values() if a["key"] not in summon_keys],
                      key=lambda x: -(x["base"] + x["self"] + x["assist"]))[:6]
        p("sum of top6 non-summon =", sum(x["base"] + x["self"] + x["assist"] for x in top6))

    p("")
    p("=== PART 7: other exports sanity (fold census + knownMult) ===")
    for name in sorted(os.listdir(EXPORTS)):
        if not name.endswith(".json"):
            continue
        epath = os.path.join(EXPORTS, name)
        try:
            with io.open(epath, "r", encoding="utf-8") as fh:
                r2 = json.load(fh)
        except Exception as ex:
            p("  %s READ FAIL %s" % (name, ex))
            continue
        nf = 0
        nz = 0
        kmbad = 0
        kmn = 0
        for e in (r2.get("events") or []):
            if e.get("type") != "dmg":
                continue
            for f in ((e.get("calc") or {}).get("fold") or []):
                nf += 1
                try:
                    fv = float(f.get("factor") or 0)
                except (TypeError, ValueError):
                    fv = 0.0
                if not (fv > 0):
                    nz += 1
            km = (e.get("calc") or {}).get("knownMult")
            if km is not None:
                kmn += 1
                pf = 1.0
                for f in ((e.get("calc") or {}).get("fold") or []):
                    try:
                        fv = float(f.get("factor") or 0)
                    except (TypeError, ValueError):
                        fv = 0.0
                    if fv > 0 and abs(fv - 1) >= EPS:
                        pf *= fv
                if abs(km - pf) > 1e-9:
                    kmbad += 1
        p("  %-42s ver=%-6s folds=%-6d factor<=0=%-3d kmRows=%-5d km!=prod=%d"
          % (name, str(r2.get("version")), nf, nz, kmn, kmbad))

    with io.open(os.path.join(OUTDIR, "audit_output.txt"), "w", encoding="utf-8") as fh:
        fh.write("\n".join(LOG) + "\n")
    print("AUDIT DONE lines=%d" % len(LOG))


main()
