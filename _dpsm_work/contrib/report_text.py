# -*- coding: utf-8 -*-
"""Text reports: the full v1 report and the legacy (Stage 0) compatible table."""
from __future__ import annotations


def _rank(actors, key):
    return sorted(actors, key=lambda a: -key(a))


def render_full(an, export, issues, summary, max_rows=20):
    L = []
    T = export.totals or {}
    L.append("=" * 118)
    # P0-B (schema 1.1): the ledger and BOTH coverage ratios are printed, and the old "口径差" is named
    # instead of left as an unexplained difference. Three buckets, never merged (review NEXT-STEPS 0.3).
    _diag = an.diagnostics or {}

    def _bd(k):
        return float((_diag.get(k) or {}).get("damage") or 0.0)

    def _bc(k):
        return int((_diag.get(k) or {}).get("count") or 0)

    def _pct(num_, den_):
        return (u"%.4f%%" % (100.0 * num_ / den_)) if den_ else u"n/a(分母为0)"

    _td = float(T.get("dealt") or 0.0)
    _outside = _bd("outside_team_events")
    _unknown = _bd("unattributed_events") + _bd("attacker_unresolved")
    _attributed = an.actor_total_credit()
    _ledger_ok = abs(an.analyzable + _outside - _td) <= max(2.0, 1e-5 * abs(_td))
    L.append(u"DpsMeter 队伍贡献分析 v1  ——  method=log-share/1  damageBasis=dealt  schema=1.1  producer=offline")
    L.append(u"  导出: %s" % export.name)
    L.append(u"  version=%s quest=%s duration=%.2fs result=%s" %
             (export.version, export.quest, float(export.duration or 0), export.result))
    if int(export.quest or 0) == 9999:
        L.append(u"  ⚠ trainingMode = true(试炼场 quest 9999):存在特殊 ×0.03 机制,**不参与跨场比较**")
    L.append(u"  伤害台账:totals.dealt = %.0f = 可分析 %.0f + 他方伤害 %.0f   %s"
             % (_td, an.analyzable, _outside, u"[恒等 OK]" if _ledger_ok else u"[恒等 FAIL 差 %.0f]" % (an.analyzable + _outside - _td)))
    L.append(u"            无法解析攻击者 %.0f(游戏单列为 totals.unattributedDamage = %s);残余 reconciliationGap = %.0f"
             % (_unknown, T.get("unattributedDamage"), _td - (an.analyzable + _outside)))
    L.append(u"  覆盖率:整场分析覆盖 %s   已分析内归因 %s   整场归因 %s"
             % (_pct(an.analyzable, _td), _pct(_attributed, an.analyzable), _pct(_attributed, _td)))
    L.append(u"            (totals.dealt 含双方已识别攻击者;他方伤害大的场次会压低「整场分析覆盖」;excludedDamage 只算无法解析攻击者)")
    L.append(u"")
    L.append(u"[0] 校验(阶段B恒等式)")
    L.append(u"  逐击:Σcredit+未归因 = D ............ %s   (最大绝对误差 %.3e, 破坏击数 %d)" %
             ("OK" if summary.get("per_hit_max_error", 0) < 1e-6 else "FAIL",
              summary.get("per_hit_max_error", 0.0), an.diagnostics.get("per_hit_bad", 0)))
    L.append(u"  角色:ΣtotalCredit+未归因 = 可分析池 . %s   (误差 %.6f, 相对 %.3e)" %
             ("OK" if abs(summary.get("actor_identity_error", 0)) / max(1.0, an.analyzable) < 1e-9 else "FAIL",
              summary.get("actor_identity_error", 0.0),
              abs(summary.get("actor_identity_error", 0)) / max(1.0, an.analyzable)))
    L.append(u"  份额:ΣtotalShare+未归因份额 = 1 ... %s   (%.12f)" %
             ("OK" if abs(summary.get("credited_share", 0) + summary.get("unattributed_share", 0) - 1) < 1e-9 else "FAIL",
              summary.get("credited_share", 0) + summary.get("unattributed_share", 0)))
    fa = summary.get("fold_accounting", {})
    L.append(u"  折叠记账:总数 %d = 已解析 %d + 未归因 %d (因子≤0 %d, 因子=1 %d, 因子<1 %d)" %
             (fa.get("total", 0), fa.get("resolved", 0), fa.get("unresolved", 0),
              fa.get("zero_factor", 0), fa.get("noop_factor", 0), summary.get("sub_unity_folds", 0)))
    for lvl, code, msg in issues:
        L.append(u"  [%s %s] %s" % (lvl, code, msg))
    L.append(u"")
    L.append(u"  口径提醒:「直接输出」与「总贡献」是两套排名,不可相加;")
    L.append(u"            「受队友赋能」是观察量,与「辅助贡献」对称,同样不可与总贡献相加。")
    L.append(u"")
    L.append(u"[1] 直接输出排名")
    L.append(u"  %-16s %5s %13s %7s %6s" % (u"角色", u"key", u"直接伤害", u"占比", u"出手"))
    for a in _rank(an.actors.values(), lambda x: x.direct):
        if a.direct <= 0 and a.total <= 0:
            continue
        share = 100.0 * a.direct / an.analyzable if an.analyzable else 0.0
        L.append(u"  %-16s %5s %13.0f %6.2f%% %6d" % (a.name, a.key, a.direct, share, a.hits))
    L.append(u"")
    L.append(u"[2] 总贡献排名(可加和:基础 + 自身规则 + 辅助贡献)")
    L.append(u"  %-16s %5s %12s %12s %12s %13s %8s %8s %6s" %
             (u"角色", u"key", u"基础", u"自身规则", u"辅助贡献", u"总贡献", u"总贡献占比", u"直接占比", u"出手"))
    for a in _rank(an.actors.values(), lambda x: x.total):
        if a.direct <= 0 and a.total <= 0:
            continue
        ts = 100.0 * a.total / an.analyzable if an.analyzable else 0.0
        ds = 100.0 * a.direct / an.analyzable if an.analyzable else 0.0
        L.append(u"  %-16s %5s %12.0f %12.0f %12.0f %13.0f %7.2f%% %7.2f%% %6d" %
                 (a.name, a.key, a.base, a.self_rule, a.assist, a.total, ts, ds, a.hits))
    # F2 fix: the four printed columns are rounded independently, so the printed total is built
    # from the ROUNDED column values; otherwise the row shows a +/-1 contradiction.
    tb = round(sum(a.base for a in an.actors.values()))
    ts_ = round(sum(a.self_rule for a in an.actors.values()))
    ta = round(sum(a.assist for a in an.actors.values()))
    L.append(u"  %-16s %5s %12.0f %12.0f %12.0f %13.0f %7.2f%%" %
             (u"合计", u"", tb, ts_, ta, tb + ts_ + ta,
              100.0 * summary.get("credited_share", 0)))
    L.append(u"  (各列独立四舍五入,行内合计 = 取整后相加;精确恒等式见 [0] 节,误差 ≤1 单位)")
    L.append(u"  %-16s %5s %12s %12s %12s %13.0f %7.2f%%" %
             (u"未归因", u"", u"", u"", u"", an.unattributed_credit,
              100.0 * summary.get("unattributed_share", 0)))
    L.append(u"")
    L.append(u"[3] 角色贡献构成(非加和观察量:受队友赋能)")
    for a in _rank(an.actors.values(), lambda x: -(x.received + x.assist)):
        if a.received <= 0 and a.assist <= 0:
            continue
        L.append(u"  %-16s 直接 %12.0f | 受队友赋能 %12.0f | 为团队赋能 %12.0f" %
                 (a.name, a.direct, a.received, a.assist))
    L.append(u"")
    L.append(u"[4] 规则贡献排名(damageEquivalent = 该规则带来的份额之和)")
    L.append(u"  %-26s %-9s %-4s %-16s %6s %8s %6s %13s %6s" %
             (u"规则", u"通道", u"侧", u"持有者", u"命中", u"覆盖率", u"折叠", u"伤害当量", u"受益者"))
    rules = sorted(an.rules.values(), key=lambda r: -r.damage)
    for r in rules[:max_rows]:
        cov = 100.0 * r.hits / an.hits if an.hits else 0.0
        L.append(u"  %-26s %-9s %-4s %-16s %6d %7.1f%% %6d %13.0f %6d" %
                 (r.name[:26], r.kind, r.side, (r.owner_name or u"(未归因)")[:16],
                  r.hits, cov, r.folds, r.damage, len(r.beneficiaries)))
    if len(rules) > max_rows:
        L.append(u"  ... 共 %d 条规则,余下按 damageEquivalent 递减" % len(rules))
    L.append(u"")
    L.append(u"[5] 辅助关系(提供者 -> 出手者,只列非自身规则)")
    links = sorted(an.links.values(), key=lambda l: -l.amount)
    for l in links[:max_rows]:
        f = an.actors.get(l.from_key)
        t = an.actors.get(l.to_key)
        top = sorted(l.rules.items(), key=lambda x: -x[1])[:2]
        tops = u" · ".join(u"%s=%.0f" % (an.rules[k].name if k in an.rules else str(k), v) for k, v in top)
        L.append(u"  %-16s -> %-16s %13.0f  (%d 击)  %s" %
                 (f.name if f else l.from_key, t.name if t else l.to_key, l.amount, l.hits, tops))
    if not links:
        L.append(u"  (无)")
    L.append(u"")
    L.append(u"[6] 未归因与残差")
    if an.unattributed:
        for reason, (amt, n) in sorted(an.unattributed.items(), key=lambda x: -x[1][0]):
            L.append(u"  %-24s %13.0f  (%d 条折叠)" % (reason, amt, n))
    else:
        L.append(u"  (未归因池为空)")
    # R62 (B): the ladder is the GAME caliber -- (applied + absorbed) / theory. The exported
    # calc.residual is applied/theory, which an absorbed hit drags toward 0.
    L.append(u"  非折叠倍率(会心/攻击力加算/未识别)按定义留在基础项;residual 游戏口径分布(前 8;吸收击 %s):" %
             an.diagnostics.get("residual_absorbed_hits", 0))
    rb = an.diagnostics.get("residual_buckets", {})
    for k in sorted(rb, key=lambda x: -rb[x])[:8]:
        L.append(u"    residual=%-8s %d 击" % (k, rb[k]))
    L.append(u"")
    L.append(u"[7] 数据质量")
    L.append(u"  事件:dmg %s(我方 %s)/ heal %s / 其它 %s" %
             (an.diagnostics["events"]["dmg"], an.diagnostics["events"]["dmg_team"],
              an.diagnostics["events"]["heal"], an.diagnostics["events"]["other"]))
    L.append(u"  未归属事件(attacker 为 ?): %s" % an.diagnostics["unattributed_events"])
    L.append(u"  队外攻击者事件: %s" % an.diagnostics["outside_team_events"])
    L.append(u"  byUnit 覆盖率:given %s, madness %s   (带 byUnit / 该类折叠)" %
             (summary.get("byUnit_given"), summary.get("byUnit_madness")))
    L.append(u"  归属理由计数: %s" % an.diagnostics.get("reasonCounts"))
    L.append(u"  主键覆盖(Phase C):攻击者事件由 actors[].key 解析 %d/%d = %.4f%%;未解析 %s" %
             (an.hits, an.hits + (summary.get("unattributed_events") or {}).get("count", 0),
              100.0 * summary.get("key_coverage", 1.0),
              summary.get("unattributed_events")))
    L.append(u"  规则归属路径(名称→key / id→key / 回退): %s" % summary.get("owner_paths"))
    L.append(u"  通道×侧普查: %s" % an.diagnostics.get("channelCensus"))
    L.append(u"  无 calc 的可分析击(按 M=1 处理): %d ;被 FoldStep 截断丢掉的折叠: %d(命中 %d)" %
             (an.diagnostics.get("calc_missing", 0), an.diagnostics.get("fold_dropped", 0),
              an.diagnostics.get("fold_dropped_hits", 0)))
    L.append(u"  名称回退计数(attacker_default): %d ;  我方同名 actor: %s ;  全导出重名 actor 名数: %s" %
             (an.diagnostics.get("attackerNameFallbacks", 0),
              summary.get("duplicate_names_team") or u"无", summary.get("duplicate_names_export")))
    L.append(u"  召唤物(独立 key,第一版不合并): %s" % an.diagnostics.get("summon_actors"))
    if an.diagnostics.get("duplicate_in_hit"):
        L.append(u"  同一击内重复(同名同通道)折叠: %d" % an.diagnostics["duplicate_in_hit"])
    L.append(u"")
    L.append(u"[8] 证据等级与已知限制")
    L.append(u"  实测(SS):逐击 amount / atkKey / fold.factor / byUnit / totals.*")
    L.append(u"  离线重放(SR):D/M 拆分与 ln 份额、角色/规则/关系聚合、全部恒等式")
    L.append(u"  推断(IN):cancel 语义、残差分解、会心观测、训练场特殊倍率")
    L.append(u"  限制 1:attackPower 内的攻击力加算未拆,留在基础项(阶段G)")
    L.append(u"  限制 2:非折叠残差(会心/未识别)留在基础项,未单独归因")
    L.append(u"  限制 3:召唤物与使用者未合并(导出无主人字段)")
    L.append(u"  限制 4:totals.dealt 与逐事件求和存在 0.0487% 口径差,并列展示")
    return u"\n".join(L)


def render_legacy(an, export):
    """Stage-0 compatible table (kept for compatibility; overlapping columns warned)."""
    L = []
    L.append("=" * 110)
    L.append(u"DpsMeter 队伍贡献分析(Stage 0 兼容视图) —— " + export.name)
    L.append(u"  version=%s quest=%s duration=%.1fs result=%s" %
             (export.version, export.quest, float(export.duration or 0), export.result))
    L.append(u"  口径与新版一致(对数份额);本视图保留旧列名与重叠列,禁止把「为团队赋能」与「直接伤害」相加。")
    L.append(u"  校验:入表我方 dmg 合计 = %.0f;插件 totals.dealt = %s" % (an.analyzable, (export.totals or {}).get("dealt")))
    L.append(u"")
    L.append(u"%-14s %13s %6s %12s %12s %12s %12s %12s %13s %5s" %
             (u"角色", u"直接伤害", u"占比", u"基础", u"自身规则", u"受队友赋能", u"狂気(未归因)", u"赋予(未归因)", u"为团队赋能", u"出手"))
    for a in sorted(an.actors.values(), key=lambda x: -x.direct):
        if a.direct <= 0 and a.assist <= 0:
            continue
        mad = an.unattributed_by_attacker_kind.get((a.key, "madness"), 0.0)
        giv = an.unattributed_by_attacker_kind.get((a.key, "given"), 0.0)
        share = 100.0 * a.direct / an.analyzable if an.analyzable else 0.0
        L.append(u"%-14s %13.0f %5.1f%% %12.0f %12.0f %12.0f %12.0f %12.0f %13.0f %5d" %
                 (a.name, a.direct, share, a.base, a.self_rule, a.received, mad, giv, a.assist, a.hits))
    L.append(u"")
    L.append(u"倍率池总量 = %.0f(占总伤害 %.1f%%);未归因:" %
             (an.pool_total, 100.0 * an.pool_total / an.analyzable if an.analyzable else 0.0))
    for reason, (amt, n) in sorted(an.unattributed.items(), key=lambda x: -x[1][0]):
        L.append(u"   %-30s %14.0f" % (reason, amt))
    L.append(u"")
    L.append(u"各角色 top3 赋能规则(伤害当量):")
    for a in sorted(an.actors.values(), key=lambda x: -(x.total)):
        top = sorted(a.rules.items(), key=lambda x: -x[1])[:3]
        if top:
            L.append(u"   %-16s %s" % (a.name, u" · ".join(
                u"%s=%.0f" % (an.rules[k].name if k in an.rules else str(k), v) for k, v in top)))
    return u"\n".join(L)
