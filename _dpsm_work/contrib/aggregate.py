# -*- coding: utf-8 -*-
"""Log-share split and aggregation. Implements dictionary sections 2 and 4."""
from __future__ import annotations
import math
from .model import (ActorCredit, Analysis, FoldEntry, HitLine, HitResult,
                    LinkStat, REASON_CODES, RESOLVED_REASONS, RuleStat)
from .attribution import OwnerIndex, NAME_RE, ability_id_of

EPS = 1e-12


def rule_display_name(fold):
    """Human-facing rule name: [..] if present, else the label, else the origin."""
    m = NAME_RE.search(fold.label or "")
    if m:
        return m.group(1)
    lab = (fold.label or "").strip()
    if lab:
        return lab
    return str(fold.origin or "")


def rule_key(fold, owner, reason):
    """Identity of a rule instance for aggregation.

    Keyed by (name, kind, side, owner) so the same rule reached through the
    text and the global channel stays distinguishable while still grouping hits.
    """
    okey = owner.key if owner is not None else None
    return (rule_display_name(fold), fold.kind, fold.side, okey)


def split_hit(damage, folds):
    """Return (M, base, pool, shares) with shares = [ln(fi)/ln(M) * pool]."""
    M = 1.0
    for f in folds:
        M *= f.factor
    if not folds or abs(M - 1.0) < EPS:
        return 1.0, damage, 0.0, [0.0] * len(folds)
    base = damage / M
    pool = damage - base
    lnM = math.log(M)
    shares = [pool * (math.log(f.factor) / lnM) if lnM != 0 else 0.0 for f in folds]
    return M, base, pool, shares


def extract_folds(event, diag):
    """calc.fold -> FoldEntry list, excluding non-multiplicative factors."""
    out = []
    calc = event.get("calc") or {}
    # 1.7.4 (阶段 G): calc.atkAdd carries the SAME fold vocabulary (side/kind/origin/factor/label/
    # byUnit) for attack-power additions granted by a teammate. It is read, never re-derived: the plugin
    # writes the factor it actually used, so the two cores cannot disagree about the arithmetic.
    for f in (list(calc.get("fold") or []) + list(calc.get("atkAdd") or [])):
        try:
            factor = float(f.get("factor") or 0.0)
        except (TypeError, ValueError):
            factor = 0.0
        if factor <= 0.0:
            diag["zero_factor"] = diag.get("zero_factor", 0) + 1
            continue
        if abs(factor - 1.0) < EPS:
            diag["noop_factor"] = diag.get("noop_factor", 0) + 1
            continue
        if factor < 1.0:
            # A factor < 1 (damage reduction, e.g. training x0.03) is NOT a buff-pool member:
            # D/M would exceed D and the ln-shares would go negative. Excluded from M and
            # reported as a diagnostic; the hit stays in base (documented in the dictionary).
            diag["sub_unity_factor"] = diag.get("sub_unity_factor", 0) + 1
            diag["sub_unity_sample"] = diag.get("sub_unity_sample") or [
                f.get("kind"), f.get("side"), str(f.get("origin"))[:32], factor]
            continue
        out.append(FoldEntry(kind=str(f.get("kind") or ""), side=str(f.get("side") or ""),
                             origin=str(f.get("origin") or ""), factor=factor,
                             label=str(f.get("label") or ""), by_unit=str(f.get("byUnit") or "")))
    return out


def analyze(export, team=1, training=False, keep_hits=False):
    """Run the full contribution analysis on a loaded export.

    keep_hits=True also retains every HitResult (per-hit audit trail); default False keeps the
    memory footprint flat for bulk runs.
    """
    idx = OwnerIndex(export, team)
    an = Analysis(source=export.source(), team=team)
    an.diagnostics = {"reasonCounts": {}, "channelCensus": {}, "kindSideCensus": {},
                      "zero_factor": 0, "noop_factor": 0, "sub_unity_factor": 0,
                      "negative_lines": 0, "fold_total": 0,
                      "events": {"dmg": 0, "dmg_team": 0, "heal": 0, "other": 0},
                      "unattributed_events": {"count": 0, "damage": 0.0},
                      "outside_team_events": {"count": 0, "damage": 0.0},
                      "attacker_unresolved": {"count": 0, "damage": 0.0},
                      "eventSumAll": 0.0, "name_based_lines": 0, "key_based_lines": 0,
                      "attackerNameFallbacks": 0, "byUnit_hits": 0, "fold_hits": 0,
                      "crits": 0, "residual_buckets": {}, "zero_damage_credited": 0,
                      "atkTeam_mismatch": 0, "per_hit_max_error": 0.0, "per_hit_bad": 0,
                      "calc_missing": 0, "fold_dropped": 0, "fold_dropped_hits": 0,
                      "summon_actors": [a.name for a in export.team_actors(team) if a.summon],
                      "duplicate_names": {k: v for k, v in export.duplicate_names.items()},
                      "greatest_unresolved_fold": None}
    diag = an.diagnostics
    an.hits = 0
    rule_seen = {}     # rule key -> set(event index)
    rule_folds = {}    # rule key -> fold instances
    ben_seen = {}      # (rule key, attacker key) -> set(event index)
    link_seen = {}     # (from, to) -> set(event index)
    for ac in export.team_actors(team):
        an.actors[ac.key] = ActorCredit(key=ac.key, name=ac.name, team=ac.team,
                                        kind=ac.kind, summon=ac.summon)
    for i, e in enumerate(export.events):
        t = e.get("type")
        if t != "dmg":
            diag["events"]["heal" if t == "heal" else "other"] += 1
            continue
        diag["events"]["dmg"] += 1
        try:
            damage = float(e.get("amount") or 0.0)
        except (TypeError, ValueError):
            damage = 0.0
        diag["eventSumAll"] += damage
        atk_team = e.get("atkTeam")
        key = e.get("atkKey")
        row = export.by_key.get(key) if key is not None else None
        if row is None:
            if atk_team is None or e.get("attacker") in (None, "", "?"):
                diag["unattributed_events"]["count"] += 1
                diag["unattributed_events"]["damage"] += damage
            else:
                diag["attacker_unresolved"]["count"] += 1
                diag["attacker_unresolved"]["damage"] += damage
            continue
        if atk_team is not None and atk_team != row.team:
            diag["atkTeam_mismatch"] = diag.get("atkTeam_mismatch", 0) + 1
        if row.team != team:
            diag["outside_team_events"]["count"] += 1
            diag["outside_team_events"]["damage"] += damage
            continue
        attacker = idx.by_key.get(row.key) or row
        diag["events"]["dmg_team"] += 1
        an.hits += 1
        if e.get("crit"):
            diag["crits"] += 1
        folds = extract_folds(e, diag)
        diag["fold_total"] += len(folds)
        if folds:
            diag["fold_hits"] += 1
        M, base, pool, shares = split_hit(damage, folds)
        calc = e.get("calc")
        if not isinstance(calc, dict):
            # an analyzable hit with no calc is treated as M=1 (all base); it MUST be counted,
            # otherwise a lost composition chain looks like a clean analysis (audit Q5).
            diag["calc_missing"] = diag.get("calc_missing", 0) + 1
            calc = {}
        fd = calc.get("foldDropped")
        if isinstance(fd, (int, float)) and fd > 0:
            # FoldStep caps the fold list (MaxSteps=24); a dropped fold means M is short a factor.
            # Latent silent truncation -> surface it (audit Q7).
            diag["fold_dropped"] = diag.get("fold_dropped", 0) + int(fd)
            diag["fold_dropped_hits"] = diag.get("fold_dropped_hits", 0) + 1
        resid = calc.get("residual")
        if isinstance(resid, (int, float)):
            bucket = round(float(resid), 4)
            diag["residual_buckets"][bucket] = diag["residual_buckets"].get(bucket, 0) + 1
        hr = HitResult(index=i, time=float(e.get("t") or 0.0), attacker_key=attacker.key,
                       victim_key=e.get("vicKey"), damage=damage, multiplier=M, base=base,
                       pool=pool, folds_total=len(folds), crit=bool(e.get("crit")),
                       residual_mult=float(resid) if isinstance(resid, (int, float)) else None)
        ac = an.actors[attacker.key]
        ac.direct += damage
        ac.hits += 1
        ac.base += base
        # 1.7.12: the same-team split. An EVENT property, not a subtraction: friendly + hostile is
        # exactly direct, and the split is reporting only -- it never moves a credit line.
        if e.get("friendly"):
            ac.friendly += damage
            ac.friendly_hits += 1
        else:
            ac.hostile += damage
        an.analyzable += damage
        an.pool_total += pool
        # the base part is an explicit, auditable credit line (dictionary section 2.2)
        hr.lines.append(HitLine(owner_key=attacker.key, amount=base, reason="base",
                                rule_key=("(base)", "base", "atk", attacker.key),
                                rule_name=u"基础部分", kind="base", side="atk"))
        used_keys = {}
        for f, share in zip(folds, shares):
            owner, reason = idx.resolve(f, attacker)
            diag["reasonCounts"][reason] = diag["reasonCounts"].get(reason, 0) + 1
            nm = "(unresolved)" if owner is None else owner.name
            ch = diag["kindSideCensus"]
            ch[(f.kind, f.side, reason)] = ch.get((f.kind, f.side, reason), 0) + 1
            if reason in ("byUnit", "byUnit_outside", "byUnit_unknown"):
                diag["byUnit_hits"] += 1
            rk = rule_key(f, owner, reason)
            if owner is not None:
                diag["key_based_lines"] += 1
                if reason in ("byUnit", "ability_holder_unique", "global_name_unique"):
                    diag["name_based_lines"] += 1
                if reason == "attacker_default":
                    diag["attackerNameFallbacks"] += 1
            # cross-channel duplicate diagnostic: same rule name AND same factor reached
            # through two different fold kinds within one hit (dictionary section 4, rule 2)
            dup = (rule_display_name(f), round(f.factor, 6))
            prev = used_keys.get(dup)
            if prev is None:
                used_keys[dup] = f.kind
            elif prev != f.kind:
                diag["duplicate_in_hit"] = diag.get("duplicate_in_hit", 0) + 1
            if share < 0:
                diag["negative_lines"] = diag.get("negative_lines", 0) + 1
            line = HitLine(owner_key=(owner.key if owner is not None else None), amount=share,
                           reason=reason, rule_key=rk, rule_name=rule_display_name(f),
                           kind=f.kind, side=f.side, factor=f.factor)
            hr.lines.append(line)
            if owner is None:
                hr.unattributed += share
                an.unattributed_credit += share
                slot = an.unattributed.setdefault(reason, [0.0, 0])
                slot[0] += share
                slot[1] += 1
                an.unattributed_by_kind[f.kind] = an.unattributed_by_kind.get(f.kind, 0.0) + share
                aak = (attacker.key, f.kind)
                an.unattributed_by_attacker_kind[aak] = an.unattributed_by_attacker_kind.get(aak, 0.0) + share
                if diag["greatest_unresolved_fold"] is None:
                    diag["greatest_unresolved_fold"] = [rule_display_name(f), reason, round(share, 3)]
                continue
            if owner.key == attacker.key:
                ac.self_rule += share
                an.actors[owner.key].rules[rk] = an.actors[owner.key].rules.get(rk, 0.0) + share
                an.actors[owner.key].rule_hits[rk] = an.actors[owner.key].rule_hits.get(rk, 0) + 1
            else:
                ac.received += share
                an.actors[owner.key].assist += share
                an.actors[owner.key].rules[rk] = an.actors[owner.key].rules.get(rk, 0.0) + share
                an.actors[owner.key].rule_hits[rk] = an.actors[owner.key].rule_hits.get(rk, 0) + 1
                lk = (owner.key, attacker.key)
                link = an.links.get(lk)
                if link is None:
                    link = LinkStat(from_key=owner.key, to_key=attacker.key)
                    an.links[lk] = link
                link.amount += share
                link.folds += 1
                link_seen.setdefault(lk, set()).add(i)
                link.rules[rk] = link.rules.get(rk, 0.0) + share
            rs = an.rules.get(rk)
            if rs is None:
                rs = RuleStat(key=rk, name=rule_display_name(f), kind=f.kind, side=f.side,
                              owner_key=owner.key, owner_name=owner.name, origin=f.origin)
                an.rules[rk] = rs
            rs.folds += 1
            rs.damage += share
            rule_folds[rk] = rule_folds.get(rk, 0) + 1
            rule_seen.setdefault(rk, set()).add(i)
            ben_seen.setdefault((rk, attacker.key), set()).add(i)
            rs.beneficiaries[attacker.key] = rs.beneficiaries.get(attacker.key, 0.0) + share
            rs.beneficiary_folds[attacker.key] = rs.beneficiary_folds.get(attacker.key, 0) + 1
            rs.reasons[reason] = rs.reasons.get(reason, 0) + 1
        if keep_hits:
            an.hits_detail.append(hr)
        hit_err = abs(hr.credited + hr.unattributed - damage)
        if hit_err > diag.get("per_hit_max_error", 0.0):
            diag["per_hit_max_error"] = hit_err
        if abs(damage) > 0 and hit_err > 1e-6:
            diag["per_hit_bad"] = diag.get("per_hit_bad", 0) + 1
            an.checks.append("hit %d identity broken: %.6f vs %.6f" % (i, hr.credited + hr.unattributed, damage))
    for rk, rs in an.rules.items():
        rs.hits = len(rule_seen.get(rk, ()))
        for k in list(rs.beneficiaries.keys()):
            rs.beneficiary_hits[k] = len(ben_seen.get((rk, k), ()))
    for lk, link in an.links.items():
        link.hits = len(link_seen.get(lk, ()))
    diag["channelCensus"] = {}
    for (k, s, r), n in sorted(diag["kindSideCensus"].items()):
        key = "%s/%s" % (k, s)
        diag["channelCensus"][key] = diag["channelCensus"].get(key, 0) + n
    diag["unattributed_credit"] = an.unattributed_credit
    if not an.checks:
        an.checks.append("per-hit credit identity holds for all %d hits" % an.hits)
    return an
