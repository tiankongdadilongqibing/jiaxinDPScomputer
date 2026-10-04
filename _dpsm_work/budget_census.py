# -*- coding: utf-8 -*-
"""N6 budget & residual census (read-only over the export corpus).

WHAT IT ANSWERS (roadmap N6)
  * how much damage is behind a *lost fold step*, a *dropped row/snapshot*, a *read failure* or an
    *unknown identity* -- ranked by AFFECTED DAMAGE, not by hit count;
  * which budgets were in effect and which of them are observable at all;
  * how the per-hit residual is distributed by quest / hit type / whether folds were present -- with the
    warning that a net k_extra difference is NOT a count of a rule firing more or less often;
  * and, for every issue, the four things the acceptance asks for: scope, minimal evidence, a falsifiable
    hypothesis, and the cost of the next step.

It never writes to an export, never compensates for a dropped step (a lost step makes the equality a
LOWER BOUND; fabricating a factor is exactly what the roadmap forbids), and never touches the plugin.

Usage: python budget_census.py [--exports DIR] [--out BASE] [--selftest]
ASCII-only stdout; the report and JSON are UTF-8.
"""
from __future__ import annotations
import argparse, collections, glob, io, json, os, shutil, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
EXPORTS = os.path.join(ROOT, "BepInEx", "plugins", "DpsMeter", "exports")
CONTRACT = "budget-census/1"

ERROR_KEYS = ["rosterErrors", "triggerErrors", "blockErrors", "giveErrors", "giverErrors", "liveErrors",
              "resistErrors", "subParamErrors", "paramErrors", "sigErrors"]
DROPPED_KEYS = ["rowsDropped", "bucketsOverflow", "perBucketOverflow", "rowsTotal"]
MAX_KEYS = ["maxClasses", "maxLiveClasses", "maxBuckets", "maxPerBucket", "maxRows", "maxEntriesPerRead",
            "maxUnionRows", "maxEntryReadsPerBattle"]


def load(path):
    with io.open(path, encoding="utf-8") as fh:
        return json.load(fh)


def _f(v):
    try:
        return float(v)
    except (TypeError, ValueError):
        return 0.0


def _sum_keys(d, keys):
    out = {}
    for k in keys:
        v = d.get(k)
        if isinstance(v, (int, float)):
            out[k] = v
    return out


def census_one(path):
    doc = load(path)
    ev = doc.get("events") or []
    totals = doc.get("totals") or {}
    an = _f(totals.get("dealt"))
    dropped_hits = 0
    dropped_steps = 0
    dropped_damage = 0.0
    dropped_fold_counts = collections.Counter()
    dropped_versions = collections.Counter()
    residual_all = collections.Counter()
    residual_by_kind = {"with_folds": collections.Counter(), "no_folds": collections.Counter()}
    hit_type_damage = collections.Counter()
    residual_by_type = collections.defaultdict(collections.Counter)
    residual_kinds = collections.defaultdict(collections.Counter)
    cancel_marks = collections.Counter()
    for e in ev:
        if e.get("type") != "dmg":
            continue
        c = e.get("calc") or {}
        fd = c.get("foldDropped")
        amt = _f(e.get("amount"))
        if isinstance(fd, (int, float)) and fd > 0:
            dropped_hits += 1
            dropped_steps += int(fd)
            dropped_damage += amt
            dropped_fold_counts[len(c.get("fold") or [])] += 1
        r = c.get("residual")
        if isinstance(r, (int, float)):
            b = round(float(r), 3)
            residual_all[b] += 1
            residual_by_kind["with_folds" if (c.get("fold") or c.get("atkAdd")) else "no_folds"][b] += 1
            ht = str(c.get("hitType", "?"))
            hit_type_damage[ht] += amt
            residual_by_type[ht][b] += 1
            for st in (list(c.get("fold") or []) + list(c.get("atkAdd") or [])):
                residual_kinds[b][str(st.get("kind"))] += 1
            if c.get("cancel"):
                cancel_marks["hits_with_cancel"] += 1
    audit = doc.get("rosterAudit") or {}
    errs = {}
    for k in ERROR_KEYS:
        if isinstance(audit.get(k), (int, float)) and audit.get(k):
            errs[k] = audit[k]
    for sect in ("facts", "hitDetail", "timeline", "statusAudit", "paramOwners", "subParams", "params"):
        d = doc.get(sect) or {}
        for k in ("errors", "sigErrors"):
            if isinstance(d.get(k), (int, float)) and d.get(k):
                errs["%s.%s" % (sect, k)] = d[k]
    overflow = {}
    for sect in ("timeline", "madnessApplies", "givenApplies", "forensics", "facts", "paramOwners"):
        d = doc.get(sect) or {}
        for k in ("rowsDropped", "bucketsOverflow", "perBucketOverflow", "classesOverflow", "unionOverflow"):
            if isinstance(d.get(k), (int, float)) and d.get(k):
                overflow["%s.%s" % (sect, k)] = d[k]
    unknown = {}
    for sect, keys in (("paramOwners", ("ownerUnknown", "ownerNull", "keyOwnerNull", "entriesDropped")),
                       ("atkAdd", ("ownerUnknown", "nameCollision", "skippedOwnerNull", "skippedUnowned")),
                       ("contribution", ("outsideTeamHits",))):
        d = doc.get(sect) or {}
        for k in keys:
            if isinstance(d.get(k), (int, float)) and d.get(k):
                unknown["%s.%s" % (sect, k)] = d[k]
    led = ((doc.get("contribution") or {}).get("damageLedger")) or {}
    if led.get("unknownAttackerHits"):
        unknown["ledger.unknownAttackerHits"] = led["unknownAttackerHits"]
        unknown["ledger.unknownAttackerDealt"] = led.get("unknownAttackerDealt")
    budgets = {}
    for sect in ("facts", "forensics", "paramOwners", "madnessApplies", "givenApplies"):
        d = doc.get(sect) or {}
        for k, v in d.items():
            if k.startswith("max") and isinstance(v, (int, float)):
                budgets["%s.%s" % (sect, k)] = v
    return {"file": os.path.basename(path), "version": str(doc.get("version")), "quest": doc.get("quest"),
            "duration": _f(doc.get("duration")), "dealt": an,
            "events": len([e for e in ev if e.get("type") == "dmg"]),
            "dropped": {"hits": dropped_hits, "steps": dropped_steps, "damage": dropped_damage,
                        "share": (dropped_damage / an) if an else None,
                        "foldCounts": {str(k): v for k, v in sorted(dropped_fold_counts.items())}},
            "readErrors": errs, "overflow": overflow, "unknown": unknown, "budgets": budgets,
            "residual": {"all": dict(residual_all.most_common(12)),
                         "withFolds": dict(residual_by_kind["with_folds"].most_common(8)),
                         "noFolds": dict(residual_by_kind["no_folds"].most_common(8)),
                         "byHitType": {k: dict(v.most_common(5)) for k, v in sorted(residual_by_type.items())},
                         "damageByHitType": dict(hit_type_damage),
                         "kindsByBucket": {str(k): dict(v.most_common(4)) for k, v in
                                           sorted(residual_kinds.items(), key=lambda kv: -sum(kv[1].values()))[:5]}},
            "cancelMarks": dict(cancel_marks)}


def build(paths):
    rows = [census_one(p) for p in paths]
    dropped = [r for r in rows if r["dropped"]["hits"] > 0]
    err_battles = [r for r in rows if r["readErrors"]]
    ovf_battles = [r for r in rows if r["overflow"]]
    unk_battles = [r for r in rows if r["unknown"]]
    tot_drop_dmg = sum(r["dropped"]["damage"] for r in rows)
    tot_dealt = sum(r["dealt"] for r in rows)
    issues = []

    def card(iid, title, battles, scope, evidence, hypothesis, test, cost):
        return {"id": iid, "title": title, "battles": battles, "scope": scope, "evidence": evidence,
                "hypothesis": hypothesis, "falsifiableTest": test, "nextStepCost": cost}

    issues.append(card(
        "N6-1", "丢失的折叠步(foldDropped)", [r["file"] for r in dropped],
        "%d/%d 份导出出现;共 %d 击 / %d 步 / 影响伤害 %.0f(占这些样本总伤害的 %.4f%%)"
        % (len(dropped), len(rows), sum(r["dropped"]["hits"] for r in dropped),
           sum(r["dropped"]["steps"] for r in dropped), tot_drop_dmg,
           (100.0 * tot_drop_dmg / tot_dealt if tot_dealt else 0.0)),
        "逐击 calc.foldDropped>0;丢步击的折叠条数分布 = %s"
        % {k: sum(r["dropped"]["foldCounts"].get(k, 0) for r in dropped)
           for k in sorted({x for r in dropped for x in r["dropped"]["foldCounts"]}, key=int)},
        "丢步只在折叠条数达到上下文上限时发生(即引擎上限所致,不是被规则吃掉)",
        "若存在 foldDropped>0 而折叠条数明显低于上限的击,则该假设被否证",
        "离线即可复核(本报告已给出分布);提高上限需插件改动,当前无证据支持"),
    )
    issues.append(card(
        "N6-2", "行/快照溢出(timeline·forensics·applies)", [r["file"] for r in ovf_battles],
        "%d/%d 份导出出现溢出计数" % (len(ovf_battles), len(rows)),
        "逐段计数器:%s" % "; ".join("%s{%s}" % (r["file"], ",".join("%s=%s" % kv for kv in sorted(r["overflow"].items())))
                                    for r in ovf_battles[:6]) if ovf_battles else "无",
        "溢出只影响**诊断/时间线**的记录完整性,不影响逐击伤害与贡献(这两者来自事件与折叠)",
        "若某场溢出同时其台账对账(reconciliationGap)不为 0,则该假设被否证",
        "离线可复核;若确需完整时间线,需插件提高上限(第四批)"),
    )
    issues.append(card(
        "N6-3", "读取失败(错误计数器非零)", [r["file"] for r in err_battles],
        "%d/%d 份导出报告了非零错误计数" % (len(err_battles), len(rows)),
        "逐段计数器:%s" % "; ".join("%s{%s}" % (r["file"], ",".join("%s=%s" % kv for kv in sorted(r["readErrors"].items())))
                                    for r in err_battles[:6]) if err_battles else "无",
        "读取失败集中在个别能力/字段,且被跳过而不是被猜测(计数器是它的证据)",
        "若某场的 analyzableDealt 与 totals.dealt 的差超过台账解释范围,则该假设被否证",
        "离线(台账对账已在 N0/N1 里跑);不改插件"),
    )
    issues.append(card(
        "N6-4", "未知身份(owner/攻击者不可解析)", [r["file"] for r in unk_battles],
        "%d/%d 份导出出现未知身份计数" % (len(unk_battles), len(rows)),
        "逐段计数器(仅列非零):%s" % "; ".join("%s{%s}" % (r["file"], ",".join("%s=%s" % kv for kv in sorted(r["unknown"].items())))
                                              for r in unk_battles[:6]) if unk_battles else "无",
        "未知身份不影响**总量**,只影响**归属**:这些伤害进入 unattributed 而不是被塞给攻击者",
        "若某场的 contribution.unattributedDamage 与这些计数器不一致,则该假设被否证",
        "离线(见 contribution 段的台账);需要更强身份才需插件补字段(N3 最小设计)"),
    )
    issues.append(card(
        "N6-5", "预算上限(部分不可观测)", [],
        "观测到的上限:%s" % ", ".join(sorted({k for r in rows for k in r["budgets"]}))[:400],
        "导出里**没有**计数器的上限:`FoldContext.MaxSteps=24`(只能用 foldDropped 间接看)、`_statusSnaps=256`、`BuildBuffText` 40 条上限",
        "这三者的真实触发率当前**不可证伪**,因为文件里没有它们的计数器",
        "补三个计数器后,任何一场都能直接验证是否触发(第四批的一次性插件改动)",
        "低:三个 int 计数器;高:在长战/多召唤场景下排除静默截断"),
    )
    resid = collections.Counter()
    resid_with = collections.Counter()
    resid_without = collections.Counter()
    kinds = collections.defaultdict(collections.Counter)
    for r in rows:
        for k, v in (r["residual"]["all"] or {}).items():
            resid[str(k)] += v
        for k, v in (r["residual"]["withFolds"] or {}).items():
            resid_with[str(k)] += v
        for k, v in (r["residual"]["noFolds"] or {}).items():
            resid_without[str(k)] += v
        for k, v in (r["residual"]["kindsByBucket"] or {}).items():
            for kk, vv in v.items():
                kinds[str(k)][kk] += vv
    rows.sort(key=lambda r: -r["dropped"]["damage"])   # ranked by affected damage, not by hit count
    return {"contract": CONTRACT, "n": len(rows), "exportsDir": EXPORTS,
            "totals": {"dealt": tot_dealt, "droppedDamage": tot_drop_dmg,
                       "droppedShare": (tot_drop_dmg / tot_dealt) if tot_dealt else None},
            "issues": issues, "rows": rows,
            "residualSummary": {"all": dict(resid.most_common(15)), "withFolds": dict(resid_with.most_common(10)),
                                "noFolds": dict(resid_without.most_common(10)),
                                "kindsByBucket": {k: dict(v.most_common(4)) for k, v in kinds.items()}}}


def render_md(rep):
    L = []
    L.append("# 残差与数据预算普查(contract %s)" % rep["contract"])
    L.append("")
    L.append("> 只读普查:%d 份导出;数据库伤害合计 %.0f,其中**丢失折叠步**相关 %.0f(%.4f%%)。"
             % (rep["n"], rep["totals"]["dealt"], rep["totals"]["droppedDamage"],
                100 * (rep["totals"]["droppedShare"] or 0.0)))
    L.append("> 本文件不修改任何导出,也不为丢步伪造补偿因子:丢一步就使等式降为**下界**,对应的比较资格也随之降级。")
    L.append("")
    L.append("## 1. 问题清单(每项:影响范围 / 最小证据 / 可证伪假设 / 下一步成本)")
    L.append("")
    for c in rep["issues"]:
        L.append("### %s %s" % (c["id"], c["title"]))
        L.append("")
        L.append("- **影响范围**:%s" % c["scope"])
        L.append("- **涉及战斗**:%s" % (", ".join(c["battles"][:8]) if c["battles"] else "(无)"))
        L.append("- **最小证据**:%s" % c["evidence"])
        L.append("- **可证伪假设**:%s" % c["hypothesis"])
        L.append("- **证伪方式**:%s" % c["falsifiableTest"])
        L.append("- **下一步成本**:%s" % c["nextStepCost"])
        L.append("")
    L.append("## 2. 逐场明细")
    L.append("")
    L.append("| 文件 | 版本 | 任务 | 伤害 | 丢步击/步/伤害 | 读取错误 | 溢出 | 未知身份 |")
    L.append("|---|---|---|---|---|---|---|---|")
    for r in rep["rows"]:
        L.append("| %s | %s | %s | %.0f | %d/%d/%.0f | %s | %s | %s |"
                 % (r["file"], r["version"], r["quest"], r["dealt"], r["dropped"]["hits"],
                    r["dropped"]["steps"], r["dropped"]["damage"],
                    ",".join(sorted(r["readErrors"])) or "-", ",".join(sorted(r["overflow"])) or "-",
                    ",".join(sorted(r["unknown"])) or "-"))
    L.append("")
    L.append("## 3. 残差分层")
    L.append("")
    L.append("**全体**(残差桶 → 击数):%s" % json.dumps(rep["residualSummary"]["all"], ensure_ascii=False))
    L.append("")
    L.append("**有折叠的击**:%s" % json.dumps(rep["residualSummary"]["withFolds"], ensure_ascii=False))
    L.append("")
    L.append("**无折叠的击**:%s" % json.dumps(rep["residualSummary"]["noFolds"], ensure_ascii=False))
    L.append("")
    L.append("**按残差桶看规则来源**(桶 → 折叠 kind 计数):%s"
             % json.dumps(rep["residualSummary"]["kindsByBucket"], ensure_ascii=False))
    L.append("")
    L.append("⚠ **k_extra 净差不等于某条规则真实多/少生效的次数**:它是一组互相抵消的倍率的净效果;", )
    L.append("把净差读成「这条规则多生效了 N 次」会立刻出错(反例:0.5×2.0 的抵消对净差贡献 0,但两条规则各生效一次)。")
    L.append("")
    L.append("## 4. 结论与下一步")
    L.append("")
    for c in rep["issues"]:
        L.append("- **%s**:%s" % (c["id"], c["nextStepCost"]))
    L.append("- 会心观测保持独立研究,不并入本普查。")
    L.append("- 只有「高影响且离线无法区分」的假设才值得设计一次性组合探针;当前清单里只有 N6-5 属于这一类。")
    return "\n".join(L)


def _doc(events, **kw):
    d = {"version": "1.7.10", "quest": 411001, "duration": 100.0, "result": "Lose",
         "totals": {"dealt": sum(e.get("amount", 0) for e in events)}, "events": events}
    d.update(kw)
    return d


def _ev(amount, foldDropped=0, folds=1, residual=0.87, hitType=2):
    return {"type": "dmg", "amount": amount,
            "calc": {"foldDropped": foldDropped, "residual": residual, "hitType": hitType,
                     "fold": [{"kind": "text", "factor": 1.15}] * folds}}


def selftest():
    tmp = tempfile.mkdtemp(prefix="census_")
    fails = []

    def case(label, ok, extra=""):
        print("  [%s] %-58s %s" % ("PASS" if ok else "FAIL", label, extra))
        if not ok:
            fails.append(label)

    def write(name, doc):
        p = os.path.join(tmp, name)
        io.open(p, "w", encoding="utf-8").write(json.dumps(doc, ensure_ascii=False))
        return p

    big = write("big.json", _doc([_ev(1000.0, foldDropped=2, folds=26), _ev(500.0)]))
    small = write("small.json", _doc([_ev(1.0, foldDropped=1, folds=25), _ev(5000.0)]))
    clean = write("clean.json", _doc([_ev(5000.0), _ev(6000.0)]))
    noisy = write("noisy.json", _doc([_ev(100.0)], rosterAudit={"triggerErrors": 3, "giveErrors": 0},
                                      timeline={"errors": 0, "rowsDropped": 5},
                                      paramOwners={"ownerUnknown": 7},
                                      contribution={"damageLedger": {"unknownAttackerHits": 2,
                                                                     "unknownAttackerDealt": 123.0}}))

    c = census_one(big)
    case("a dropped step is counted with its damage",
         c["dropped"]["hits"] == 1 and c["dropped"]["steps"] == 2 and abs(c["dropped"]["damage"] - 1000.0) < 1e-9,
         str(c["dropped"]))
    case("the fold count on the dropped hit is recorded",
         c["dropped"]["foldCounts"].get("26") == 1, str(c["dropped"]["foldCounts"]))
    c = census_one(clean)
    case("a clean export reports no drops", c["dropped"]["hits"] == 0, str(c["dropped"]))
    c = census_one(noisy)
    case("read errors, overflow and unknown identity are kept separate",
         c["readErrors"].get("triggerErrors") == 3 and c["overflow"].get("timeline.rowsDropped") == 5
         and c["unknown"].get("paramOwners.ownerUnknown") == 7
         and c["unknown"].get("ledger.unknownAttackerHits") == 2,
         "%s | %s | %s" % (c["readErrors"], c["overflow"], c["unknown"]))

    rep = build([small, big, clean])
    case("the per-battle table is ranked by affected DAMAGE, not by hit count",
         rep["rows"][0]["file"] == "big.json", "%s first" % rep["rows"][0]["file"])
    case("every issue carries the four required fields",
         all(all(k in cc and cc[k] for k in ("scope", "evidence", "hypothesis", "falsifiableTest", "nextStepCost"))
             for cc in rep["issues"]), "%d issues" % len(rep["issues"]))
    case("the dropped share is a share of dealt",
         rep["totals"]["droppedShare"] is not None and 0.0 <= rep["totals"]["droppedShare"] < 1.0,
         "%.6f" % rep["totals"]["droppedShare"])

    real = sorted(glob.glob(os.path.join(EXPORTS, "battle_411001_*.json")))
    if real:
        c = census_one(real[-1])
        case("a real export censuses without error and has residuals",
             c["dealt"] > 0 and bool(c["residual"]["all"]),
             "%s dealt=%.0f buckets=%d" % (c["file"], c["dealt"], len(c["residual"]["all"])))
    else:
        print("  [SKIP] no real exports found")

    shutil.rmtree(tmp, ignore_errors=True)
    print("---- selftest: %s" % ("PASS" if not fails else "FAIL %s" % fails))
    return 0 if not fails else 1


def main(argv=None):
    ap = argparse.ArgumentParser(description="N6 budget & residual census (read-only)")
    ap.add_argument("--exports", default=EXPORTS)
    ap.add_argument("--out", default=os.path.join(HERE, "BUDGET-CENSUS"))
    ap.add_argument("--selftest", action="store_true")
    a = ap.parse_args(argv)
    if a.selftest:
        return selftest()
    paths = sorted(glob.glob(os.path.join(a.exports, "battle_*.json")))
    if not paths:
        print("no exports at %s" % a.exports)
        return 2
    rep = build(paths)
    base = a.out[:-4] if a.out.endswith(".md") else a.out
    io.open(base + ".md", "w", encoding="utf-8").write(render_md(rep) + "\n")
    io.open(base + ".json", "w", encoding="utf-8").write(json.dumps(rep, ensure_ascii=False, indent=1))
    print("exports=%d dropped_battles=%d dropped_damage=%.0f (%.5f%% of dealt)"
          % (rep["n"], len([r for r in rep["rows"] if r["dropped"]["hits"] > 0]), rep["totals"]["droppedDamage"],
             100 * (rep["totals"]["droppedShare"] or 0.0)))
    print("overflow_battles=%d read_error_battles=%d unknown_identity_battles=%d"
          % (len([r for r in rep["rows"] if r["overflow"]]), len([r for r in rep["rows"] if r["readErrors"]]),
             len([r for r in rep["rows"] if r["unknown"]])))
    print("report=%s.md" % base)
    return 0


if __name__ == "__main__":
    sys.exit(main())

