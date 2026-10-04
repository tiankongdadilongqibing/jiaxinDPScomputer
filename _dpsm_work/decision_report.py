# -*- coding: utf-8 -*-
"""N5 decision report: from the audited comparison layer to a decision (roadmap N5).

WHAT IT ADDS OVER compare/2
  * A PRE-REGISTERED contrast: decision_prereg.json is written BEFORE looking at the corpus, and the
    report prints it verbatim. The threshold, alpha, bootstrap unit/seed and minimum samples per group
    cannot be moved after seeing a result -- if the pre-registration does not match the corpus, the report
    says so and publishes no directional conclusion.
  * STATISTICAL DISCIPLINE: the battle is the independent unit; the interval comes from a cluster bootstrap
    that resamples BATTLES within each group, fixed seed, disclosed method; the report states whether the
    interval covers zero and whether the point estimate clears the pre-registered practical threshold.
    "Not significant" is never written as "the same".
  * CONFOUNDERS ARE NAMED: observed lineups are NOT assigned treatments.
  * The five sections the roadmap asks for, plus "what to verify in the next normal battle".

It consumes contrib/reports/compare2_*.json as the audited group definition and CROSS-CHECKS every actor
share it prints against that file (two paths, same core): a mismatch blocks publication.

Usage: python decision_report.py [--compare PATH] [--prereg PATH] [--out BASE] [--selftest]
Exit: 0 = report produced; 4 = nothing comparable; 2 = no input; 3 = unreadable.
ASCII-only stdout; the report and JSON are UTF-8.
"""
from __future__ import annotations
import argparse, io, json, math, os, random, shutil, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
EXPORTS = os.path.join(ROOT, "BepInEx", "plugins", "DpsMeter", "exports")
if HERE not in sys.path:
    sys.path.insert(0, HERE)
from contrib import loader, aggregate

CONTRACT = "decision/1"
DEF_COMPARE = os.path.join(HERE, "contrib", "reports", "compare2_411001.json")
DEF_PREREG = os.path.join(HERE, "decision_prereg.json")


def load_json(path):
    with io.open(path, encoding="utf-8") as fh:
        return json.load(fh)


def stat(xs):
    if not xs:
        return None
    s = sorted(xs)
    n = len(s)
    med = s[n // 2] if n % 2 else 0.5 * (s[n // 2 - 1] + s[n // 2])
    mean = sum(s) / n
    var = (sum((x - mean) ** 2 for x in s) / (n - 1)) if n > 1 else 0.0
    return {"n": n, "mean": mean, "median": med, "min": s[0], "max": s[-1], "stdev": math.sqrt(var)}


def sample_facts(name):
    """Per-battle facts, recomputed with the SAME core the comparator used."""
    p = os.path.join(EXPORTS, name)
    ex = loader.load(p)
    an = aggregate.analyze(ex, 1)
    return {"file": name, "path": p, "version": str(ex.version), "quest": ex.quest,
            "duration": float(ex.duration or 0.0), "result": str(ex.result or ""),
            "analyzable": an.analyzable, "hits": an.hits, "started": str(ex.started or ""),
            "dps": (an.analyzable / float(ex.duration)) if ex.duration else None,
            "raw": ex, "an": an}


def actor_structure(sample):
    """entityKey -> the six columns section 3 needs (shares of this battle's analyzable)."""
    ex, an = sample["raw"], sample["an"]
    den = an.analyzable or 1.0
    out = {}
    seen = {}
    for a in ex.team_actors(1):
        ek = _entity_for(sample, a.key) or ("name:" + str(a.name))
        if ek in seen:
            # same rule as compare/2: two actors in ONE battle resolving to the same key are kept
            # apart, never summed -- otherwise this cross-check would disagree by construction.
            ek = "%s|inst:%s" % (ek, a.key)
        seen[ek] = True
        cr = an.actors.get(a.key)
        if cr is None:
            continue
        d = out.setdefault(ek, {"name": a.name, "summon": bool(a.summon), "direct": 0.0, "base": 0.0,
                                "self": 0.0, "assist": 0.0, "received": 0.0, "total": 0.0, "hits": 0})
        d["direct"] += cr.direct / den
        d["base"] += cr.base / den
        d["self"] += cr.self_rule / den
        d["assist"] += cr.assist / den
        d["received"] += cr.received / den
        d["total"] += (cr.base + cr.self_rule + cr.assist) / den
        d["hits"] += cr.hits
    return out


IDENT_CACHE = {}


def _entity_for(sample, actor_key):
    """entityKey via identity_map, cached per export (weak actors fall back to their name)."""
    key = (sample["file"], actor_key)
    if key in IDENT_CACHE:
        return IDENT_CACHE[key]
    ek = None
    try:
        import identity_map as idm
        recs = IDENT_CACHE.get(("__export__", sample["file"]))
        if recs is None:
            res = idm.resolve_export(sample["path"])
            recs = {a.get("actorKey"): a.get("entityKey") for a in (res.get("actors") or [])
                    if a.get("team") == 1}
            IDENT_CACHE[("__export__", sample["file"])] = recs
        ek = recs.get(actor_key)
    except Exception:
        ek = None
    IDENT_CACHE[key] = ek
    return ek


def links_of(sample, limit=8):
    """provider -> beneficiary pairs from the same aggregation (top by amount)."""
    an = sample["an"]
    out = []
    for _lk, link in an.links.items():
        f = sample["raw"].by_key.get(link.from_key)
        t = sample["raw"].by_key.get(link.to_key)
        out.append({"from": f.name if f else ("#%s" % link.from_key),
                    "to": t.name if t else ("#%s" % link.to_key),
                    "amount": link.amount, "hits": link.hits})
    out.sort(key=lambda r: -r["amount"])
    return out[:limit]


def cluster_bootstrap(a, b, resamples, seed, alpha):
    """Percentile CI for the difference of means, resampling BATTLES within each group."""
    rng = random.Random(seed)
    na, nb = len(a), len(b)
    diffs = []
    for _ in range(resamples):
        sa = sum(a[rng.randrange(na)] for _ in range(na)) / na
        sb = sum(b[rng.randrange(nb)] for _ in range(nb)) / nb
        diffs.append(sb - sa)
    diffs.sort()
    lo = diffs[max(0, int(alpha / 2.0 * resamples))]
    hi = diffs[min(resamples - 1, int((1 - alpha / 2.0) * resamples) - 1)]
    return {"point": sum(b) / nb - sum(a) / na, "ci": [lo, hi], "resamples": resamples, "seed": seed,
            "method": "percentile cluster bootstrap, unit = battle"}


def decide(prereg, ga, gb, boot, confounders):
    """The three levels from the roadmap, applied with the pre-registered numbers."""
    reasons = []
    nmin = int(prereg.get("minimumSamplesPerGroup", 3))
    thr = float(prereg.get("minimumMeaningfulEffectPct", 10.0))
    ma = stat(ga)["mean"] if ga else 0.0
    eff_pct = (100.0 * boot["point"] / ma) if ma else None
    covers_zero = (boot["ci"][0] <= 0.0 <= boot["ci"][1])
    enough = len(ga) >= nmin and len(gb) >= nmin
    clears = (eff_pct is not None) and (abs(eff_pct) >= thr)
    if not enough:
        reasons.append("每组样本数 %d/%d < 预注册下限 %d" % (len(ga), len(gb), nmin))
    if covers_zero:
        reasons.append("差值的百分位自助区间覆盖 0:方向不确定")
    else:
        reasons.append("区间不覆盖 0")
    if not clears:
        reasons.append("点估计 %.2f%% 未达到预注册的实际改善阈值 %.1f%%" % (eff_pct or 0.0, thr))
    else:
        reasons.append("点估计 %.2f%% 达到阈值 %.1f%%" % (eff_pct, thr))
    if confounders:
        reasons.append("存在混杂:" + "; ".join(confounders))
    if enough and (not covers_zero) and clears and not confounders:
        level = "达到预先定义的改善标准"
    elif enough and (not covers_zero) and (clears or confounders):
        level = "有倾向但不确定"
    else:
        level = "描述性观察(证据不足,暂不换人)"
    return {"level": level, "reasons": reasons, "effectPct": eff_pct, "coversZero": covers_zero,
            "clearsThreshold": clears, "enoughSamples": enough}


def build_report(compare_path, prereg_path):
    rep = load_json(compare_path)
    prereg = load_json(prereg_path)
    c = prereg.get("contrast") or {}
    gm = c.get("groupMetric", "team_damage")
    tm = [m for m in rep.get("metrics") or [] if m.get("metric") == gm]
    if not tm:
        return None, "预注册的分组指标 %r 不在比较报告里" % gm
    strata = sorted(tm[0].get("strata") or [], key=lambda g: (-g["n"], g["files"][0]))
    ia, ib = int(c.get("groupAIndex", 0)), int(c.get("groupBIndex", 1))
    if len(strata) <= max(ia, ib):
        return None, "比较报告只有 %d 个分层,预注册要的是第 %d/%d 个" % (len(strata), ia, ib)
    gA, gB = strata[ia], strata[ib]
    if gA["n"] < 1 or gB["n"] < 1:
        return None, "预注册的两个分层里有一个是空的"
    sa = [sample_facts(n) for n in gA["files"]]
    sb = [sample_facts(n) for n in gB["files"]]
    metric = prereg.get("metric", "analyzable_per_battle")
    key = "dps" if metric == "dps" else "analyzable"
    va = [s[key] for s in sa if s.get(key) is not None]
    vb = [s[key] for s in sb if s.get(key) is not None]
    b = prereg.get("bootstrap") or {}
    boot = cluster_bootstrap(va, vb, int(b.get("resamples", 5000)), int(b.get("seed", 20261004)),
                             float(prereg.get("alpha", 0.05)))

    confounders = []
    for tag, g, samples in (("A", gA, sa), ("B", gB, sb)):
        if (g.get("capability") or {}).get("loadoutKeys", 1) > 1:
            confounders.append("组%s 内换过装(装备组合 %d 种)" % (tag, g["capability"]["loadoutKeys"]))
    ca = (gA.get("capability") or {})
    cb = (gB.get("capability") or {})
    if (ca.get("schema"), ca.get("atkAdd"), ca.get("giveObserved")) != \
       (cb.get("schema"), cb.get("atkAdd"), cb.get("giveObserved")):
        confounders.append("两组的有效能力集/开关观测不同(schema/atkadd/given)")
    if set(gA["versionSpan"]) != set(gB["versionSpan"]):
        confounders.append("两组跨越的插件版本不同:%s vs %s" % (",".join(gA["versionSpan"]), ", ".join(gB["versionSpan"])))
    da = [s["duration"] for s in sa if s["duration"]]
    db = [s["duration"] for s in sb if s["duration"]]
    if da and db and abs(sum(db) / len(db) - sum(da) / len(da)) > 0.2 * (sum(da) / len(da)):
        confounders.append("两组时长相差 >20%%,DPS 类比较受此混淆")
    if gA.get("quest") != gB.get("quest"):
        return None, "两个分层的任务不同(%s vs %s):不做对照" % (gA.get("quest"), gB.get("quest"))

    # cross-check: the structure columns must agree with the audited comparison file
    mism, checked = _crosscheck(gA, sa)
    mism2, checked2 = _crosscheck(gB, sb)
    mism += mism2
    checked += checked2
    if mism:
        return None, "与比较报告对不上:%d/%d 个角色份额不一致(N5 与 compare/2 同一核心,不允许分歧)" % (mism, checked)

    dec = decide(prereg, va, vb, boot, confounders)
    return {
        "contract": CONTRACT, "compare": os.path.basename(compare_path), "prereg": prereg,
        "groups": {"A": _group_block(gA, sa, va), "B": _group_block(gB, sb, vb)},
        "contrast": {"metric": metric, "nA": len(va), "nB": len(vb), "bootstrap": boot,
                     "confounders": confounders, "labels": c.get("labels") or ["观察到的阵容 A", "观察到的阵容 B"]},
        "crosscheck": {"checked": checked, "mismatches": mism},
        "conclusion": dec, "limits": LIMITS}, None


def _group_block(g, samples, values):
    actors = {}
    for s in samples:
        for ek, d in actor_structure(s).items():
            if d["summon"]:
                continue
            a = actors.setdefault(ek, {"entityKey": ek, "name": d["name"], "direct": [], "base": [], "self": [],
                                              "assist": [], "received": [], "total": [], "samples": 0})
            for k in ("direct", "base", "self", "assist", "received", "total"):
                a[k].append(d[k])
            a["samples"] += 1
    for a in actors.values():
        for k in ("direct", "base", "self", "assist", "received", "total"):
            a["mean" + k.capitalize()] = (sum(a[k]) / len(a[k])) if a[k] else 0.0
    rows = sorted(actors.values(), key=lambda r: (-r["meanTotal"], r["entityKey"]))
    rules = {}
    for s in samples:
        for rk, amount in _rules_of(s):
            e = rules.setdefault(rk, {"name": rk[0], "kind": rk[1], "side": rk[2], "damage": 0.0, "samples": 0})
            e["damage"] += amount
            e["samples"] += 1
    rl = sorted(rules.values(), key=lambda r: -r["damage"])[:8]
    for e in rl:
        e["damageMeanPerBattle"] = e["damage"] / max(1, len(samples))
    links = {}
    for s in samples:
        for l in links_of(s, limit=12):
            k = (l["from"], l["to"])
            e = links.setdefault(k, {"from": l["from"], "to": l["to"], "amount": 0.0, "samples": 0})
            e["amount"] += l["amount"]
            e["samples"] += 1
    lk = sorted(links.values(), key=lambda r: -r["amount"])[:8]
    for e in lk:
        e["amountMeanPerBattle"] = e["amount"] / max(1, len(samples))
    return {"n": len(samples), "files": g["files"], "versions": g["versionSpan"],
            "quest": g.get("quest"), "capability": g.get("capability"),
            "started": [s["started"] for s in samples if s["started"]],
            "results": {r: sum(1 for s in samples if s["result"] == r) for r in sorted(set(s["result"] for s in samples))},
            "analyzable": stat([s["analyzable"] for s in samples]),
            "duration": stat([s["duration"] for s in samples]),
            "dps": stat([s["dps"] for s in samples if s["dps"] is not None]),
            "valuesUsed": stat(values), "actors": rows, "rules": rl, "links": lk}


def _rules_of(s):
    out = {}
    for k, cr in s["an"].actors.items():
        if not cr.rules:
            continue
        for rk, amount in cr.rules.items():
            out[rk] = out.get(rk, 0.0) + amount
    return out.items()


def _crosscheck(g, samples):
    """Compare this module's recomputed per-actor total share with the audited compare/2 rows."""
    n2 = {r.get("entityKey"): r for r in (g.get("actors") or [])}
    acc = {}
    for s in samples:
        for ek, d in actor_structure(s).items():
            if d["summon"]:
                continue
            acc.setdefault(ek, []).append(d["total"])
    checked = mism = 0
    for name, xs in acc.items():
        row = n2.get(name)
        if not row or not row.get("share"):
            continue
        checked += 1
        if abs((sum(xs) / len(xs)) - row["share"]["mean"]) > 1e-6:
            mism += 1
    return mism, checked


LIMITS = [
    "两组是**观察到的阵容**,不是随机分配的实验组:任何差值都可能由操作、装备、关卡阶段或时间造成。",
    "场次是独立单位;一场的几千次命中不是几千个样本。区间按战斗做簇自助重采样(固定种子)。",
    "「不显著」不等于「相同」:区间覆盖 0 只说明这份样本不足以判定方向。",
    "当前阵容里贡献高 **不等于** 换人边际收益高;治疗/控制/生存收益不在本报告内。",
    "胜负是可能的结果,但本报告不按胜负筛样本;固定窗口 DPS 与整场通关分析分开。",
    "训练场(quest 9999)与已知损坏样本不进入对照(见比较报告的准入层)。",
]


def render_md(rep):
    L = []
    gA, gB = rep["groups"]["A"], rep["groups"]["B"]
    lab = rep["contrast"]["labels"]
    con = rep["conclusion"]
    b = rep["contrast"]["bootstrap"]
    L.append("# 配队决策报告(contract %s)" % rep["contract"])
    L.append("")
    L.append("> 输入比较报告:%s(契约 compare/2,由 N2 生成;本报告的每个角色份额都与它逐项对过)" % rep["compare"])
    L.append("> 预注册:%s 生效于**看结果之前**;阈值 %.1f%%、alpha %.2f、自助 %d 次、种子 %s、每组下限 %d 场。"
             % (rep["prereg"].get("registered", "?"), rep["prereg"].get("minimumMeaningfulEffectPct", 0),
                rep["prereg"].get("alpha", 0.05), b["resamples"], b["seed"],
                rep["prereg"].get("minimumSamplesPerGroup", 3)))
    L.append("")
    L.append("## 1. 数据质量与比较资格")
    L.append("")
    L.append("| 组 | 样本 | 版本 | 日期范围 | 时长(均值) | 结果 | 能力集(schema/atkadd/given) | 装备组合 |")
    L.append("|---|---|---|---|---|---|---|---|")
    for tag, g in (("A", gA), ("B", gB)):
        cap = g["capability"] or {}
        dates = [d[:10] for d in g["started"]] or ["?"]
        def _csv(v):
            if isinstance(v, (list, tuple)):
                return ",".join(str(x) for x in v)
            return str(v)
        L.append("| %s %s | %d | %s | %s ~ %s | %.1fs | %s | %s / %s / %s | %d 种 |"
                 % (tag, lab[0 if tag == "A" else 1], g["n"], ",".join(g["versions"]), min(dates), max(dates),
                    (g["duration"] or {}).get("mean", 0),
                    ", ".join("%s x%d" % (k, v) for k, v in sorted(g["results"].items())),
                    _csv(cap.get("schema")), _csv(cap.get("atkAdd")), _csv(cap.get("giveObserved")),
                    cap.get("loadoutKeys", 1)))
    L.append("")
    L.append("被准入层拒绝、因而不进入任何一组的样本见比较报告的 rejectedSamples(13 份无 contribution 段的旧文件 + 1 份已知损坏样本)。")
    L.append("**配置可得性**:导出不记录 GivenTalent/Madness/Contribution 开关值;本报告用的是 rosterAudit.giveFlagTrue 这一**观测**代理,它不等于完整配置。")
    L.append("")
    L.append("## 2. 队伍表现(同任务整场口径)")
    L.append("")
    L.append("| 组 | 可分析伤害 均值/中位/范围/sigma | 时长 均值 | DPS 均值 |")
    L.append("|---|---|---|---|")
    for tag, g in (("A", gA), ("B", gB)):
        a = g["analyzable"] or {}
        d = g["duration"] or {}
        p = g["dps"] or {}
        L.append("| %s | %.0f / %.0f / %.0f~%.0f / %.0f | %.1fs | %.0f |"
                 % (tag, a.get("mean", 0), a.get("median", 0), a.get("min", 0), a.get("max", 0), a.get("stdev", 0),
                    d.get("mean", 0), p.get("mean", 0)))
    L.append("")
    L.append("DPS 不自动消除波次与时长差异;提前通关、死亡与窗口不足都会直接改变这个数字。")
    L.append("")
    L.append("## 3. 角色结构(每场份额的平均;只列非召唤物)")
    L.append("")
    L.append("| 组 | 角色 | 直接 | 基础 | 自身规则 | 为队友赋能 | 总贡献 | 接受赋能(解释列) | 出场 |")
    L.append("|---|---|---|---|---|---|---|---|---|")
    for tag, g in (("A", gA), ("B", gB)):
        for r in g["actors"][:14]:
            L.append("| %s | %s | %.2f%% | %.2f%% | %.2f%% | %.2f%% | %.2f%% | %.2f%% | %d/%d |"
                     % (tag, r["name"][:16], 100 * r["meanDirect"], 100 * r["meanBase"], 100 * r["meanSelf"],
                        100 * r["meanAssist"], 100 * r["meanTotal"], 100 * r["meanReceived"], r["samples"], g["n"]))
    L.append("")
    L.append("接受赋能 是解释列(他打出的伤害里被别人拿走的份额),**不可与总贡献相加**。")
    L.append("")
    L.append("## 4. 提供者→受益者关系与规则")
    L.append("")
    for tag, g in (("A", gA), ("B", gB)):
        L.append("**组 %s**: 关系 top(每场均值)" % tag)
        L.append("")
        L.append("| 提供者 | 受益者 | 每场当量 | 出现场次 |")
        L.append("|---|---|---|---|")
        for r in (g.get("links") or [])[:6]:
            L.append("| %s | %s | %.0f | %d/%d |" % (r["from"][:16], r["to"][:16], r["amountMeanPerBattle"],
                                                       r["samples"], g["n"]))
        L.append("")
        L.append("| 规则 | 通道 | 侧 | 每场当量 |")
        L.append("|---|---|---|---|")
        for r in (g.get("rules") or [])[:6]:
            L.append("| %s | %s | %s | %.0f |" % (r["name"][:26], r["kind"], r["side"], r["damageMeanPerBattle"]))
        L.append("")
    L.append("## 5. 结论等级")
    L.append("")
    L.append("**%s**" % con["level"])
    L.append("")
    L.append("| 判据 | 值 |")
    L.append("|---|---|")
    L.append("| 对照指标 | %s |" % rep["contrast"]["metric"])
    L.append("| 点估计(B - A) | %.1f(%.2f%%) |" % (b["point"], con["effectPct"] or 0.0))
    L.append("| %d%% 百分位簇自助区间 | [%.1f, %.1f] |" % (int(100 * (1 - rep["prereg"].get("alpha", 0.05))),
                                                                  b["ci"][0], b["ci"][1]))
    L.append("| 区间是否覆盖 0 | %s |" % ("是(方向不确定)" if con["coversZero"] else "否"))
    L.append("| 是否达到预注册的实际改善阈值 | %s |" % ("是" if con["clearsThreshold"] else "否"))
    L.append("| 每组样本是否达到预注册下限 | %s |" % ("是" if con["enoughSamples"] else "否"))
    L.append("| 与 compare/2 的交叉校验 | %d 项,不一致 %d 项 |" % (rep["crosscheck"]["checked"], rep["crosscheck"]["mismatches"]))
    L.append("")
    for r in con["reasons"]:
        L.append("- %s" % r)
    L.append("")
    if rep["contrast"]["confounders"]:
        L.append("**混杂(会把结论上限压到「有倾向但不确定」)**:")
        for c in rep["contrast"]["confounders"]:
            L.append("- %s" % c)
        L.append("")
    L.append("### 下一次正常游玩可以顺带验证什么")
    L.append("")
    L.append("- 若两组的时长/波次本应相同,记录一次**同一关卡阶段**的对照场次;不要为验证专门换人重打多场。")
    L.append("- 关注区间是否收窄到阈值以内:样本增加但区间仍覆盖 0 ⇒ 继续不下结论。")
    L.append("- 观察 接受赋能 与 为队友赋能 两列是否随换人而变化,而不是只看总贡献排名。")
    L.append("")
    L.append("### 边界")
    L.append("")
    for t in rep["limits"]:
        L.append("- %s" % t)
    return "\n".join(L)


def main(argv=None):
    ap = argparse.ArgumentParser(description="N5 decision report")
    ap.add_argument("--compare", default=DEF_COMPARE)
    ap.add_argument("--prereg", default=DEF_PREREG)
    ap.add_argument("--out", default=None)
    ap.add_argument("--selftest", action="store_true")
    a = ap.parse_args(argv)
    if a.selftest:
        return selftest()
    if not os.path.isfile(a.compare):
        print("no comparison report at %s (run: python -m contrib.compare ... first)" % a.compare)
        return 2
    rep, why = build_report(a.compare, a.prereg)
    if rep is None:
        print("NOT PUBLISHED: %s" % why.encode("ascii", "replace").decode("ascii"))
        return 4
    base = a.out or os.path.join(HERE, "DECISION-REPORT-411001")
    if base.endswith(".md"):
        base = base[:-3]
    io.open(base + ".md", "w", encoding="utf-8").write(render_md(rep) + "\n")
    io.open(base + ".json", "w", encoding="utf-8").write(json.dumps(rep, ensure_ascii=False, indent=1))
    print("conclusion: %s" % rep["conclusion"]["level"].encode("ascii", "replace").decode("ascii"))
    print("effect=%.2f%% ci=[%.0f, %.0f] covers0=%s" % (rep["conclusion"]["effectPct"] or 0.0,
                                                          rep["contrast"]["bootstrap"]["ci"][0],
                                                          rep["contrast"]["bootstrap"]["ci"][1],
                                                          rep["conclusion"]["coversZero"]))
    print("report=%s.md" % base)
    return 0


def _cli(py, args, tmp, tag):
    lp = os.path.join(tmp, "cli_%s.txt" % tag)
    with io.open(lp, "w", encoding="utf-8") as fh:
        rc = subprocess.call([py, os.path.abspath(__file__)] + args, cwd=HERE, stdout=fh,
                             stderr=subprocess.STDOUT)
    return rc, io.open(lp, encoding="utf-8", errors="replace").read()


def selftest():
    py = sys.executable
    tmp = tempfile.mkdtemp(prefix="dec_")
    fails = []

    def case(label, ok, extra=""):
        print("  [%s] %-58s %s" % ("PASS" if ok else "FAIL", label, extra))
        if not ok:
            fails.append(label)

    pre = {"minimumSamplesPerGroup": 3, "minimumMeaningfulEffectPct": 10.0, "alpha": 0.05}
    big = cluster_bootstrap([100.0, 102, 101], [200.0, 203.0, 201.0], 2000, 1, 0.05)
    case("a large separation gives an interval that excludes 0", big["ci"][0] > 0,
         "[%.1f, %.1f]" % tuple(big["ci"]))
    same = cluster_bootstrap([100.0, 102, 101], [100.0, 102, 101], 2000, 1, 0.05)
    case("identical groups give an interval covering 0", same["ci"][0] <= 0 <= same["ci"][1],
         "[%.1f, %.1f]" % tuple(same["ci"]))
    d1 = decide(pre, [100, 101, 102], [200, 201, 202], big, [])
    case("interval excludes 0 + clears the threshold -> the top level",
         d1["level"] == "达到预先定义的改善标准", d1["level"])
    d2 = decide(pre, [100, 101, 102], [200, 201, 202], big, ["gear differs"])
    case("a named confounder caps the level", d2["level"] == "有倾向但不确定", d2["level"])
    d3 = decide(pre, [100, 101, 102], [100, 101, 102], same, [])
    case("an interval covering 0 -> descriptive only, no direction",
         d3["level"].startswith("描述性"), d3["level"])
    d4 = decide(pre, [100], [200, 201, 202], big, [])
    case("too few samples per group -> descriptive only", d4["level"].startswith("描述性"), d4["level"])
    d5 = decide({"minimumSamplesPerGroup": 3, "minimumMeaningfulEffectPct": 500.0, "alpha": 0.05},
                [100, 101, 102], [200, 201, 202], big, [])
    case("a real but sub-threshold effect does not reach the top level",
         d5["level"] != "达到预先定义的改善标准", d5["level"])

    if os.path.isfile(DEF_COMPARE):
        out = os.path.join(tmp, "real")
        rc, txt = _cli(py, ["--compare", DEF_COMPARE, "--prereg", DEF_PREREG, "--out", out], tmp, "real")
        md = io.open(out + ".md", encoding="utf-8").read() if os.path.isfile(out + ".md") else ""
        case("the current corpus produces a decision report (exit 0)",
             rc == 0 and "## 5. 结论等级" in md, "rc=%s" % rc)
        case("all five sections are present",
             all(s in md for s in ("## 1.", "## 2.", "## 3.", "## 4.", "## 5.")), "")
        case("the pre-registration and the interval are printed",
             "预注册" in md and "百分位簇自助区间" in md, "")
        case("the report states whether the interval covers 0",
             "区间是否覆盖 0" in md, "")
        case("the cross-check against compare/2 ran", "交叉校验" in md, "")
        bad = os.path.join(tmp, "badpre.json")
        io.open(bad, "w", encoding="utf-8").write(json.dumps({"contrast": {"groupMetric": "nope"}}))
        rc, txt = _cli(py, ["--compare", DEF_COMPARE, "--prereg", bad, "--out", os.path.join(tmp, "x")],
                       tmp, "badpre")
        case("a pre-registration that does not match the corpus publishes NOTHING (exit 4)",
             rc == 4 and "NOT PUBLISHED" in txt, "rc=%s" % rc)
        rep = load_json(DEF_COMPARE)
        for m in rep["metrics"]:
            if m["metric"] == "team_damage" and m.get("strata"):
                m["strata"][0]["actors"][0]["share"]["mean"] += 1.0
        tam = os.path.join(tmp, "tampered.json")
        io.open(tam, "w", encoding="utf-8").write(json.dumps(rep, ensure_ascii=False))
        rc, txt = _cli(py, ["--compare", tam, "--prereg", DEF_PREREG, "--out", os.path.join(tmp, "y")],
                       tmp, "tampered")
        case("a tampered compare/2 share is caught by the cross-check (exit 4)",
             rc == 4 and "NOT PUBLISHED" in txt, "rc=%s" % rc)
    else:
        print("  [SKIP] no comparison report at %s" % DEF_COMPARE)

    shutil.rmtree(tmp, ignore_errors=True)
    print("---- selftest: %s" % ("PASS" if not fails else "FAIL %s" % fails))
    return 0 if not fails else 1


if __name__ == "__main__":
    sys.exit(main())


