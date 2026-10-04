# -*- coding: utf-8 -*-
"""Phase G item 1 (offline, GO per REVIEW-phaseG-recon): the 1.15 census.

For every team hit, the composition folded k_folded copies of the 1.15 rule (a fold whose factor is
1.15/1.3225/1.5209 = 1.15^1..3). Whatever is left over sits in calc.residual, so

    k_extra = round(ln(residual) / ln(1.15))

is how many 1.15 copies the game applied but the composition did NOT fold (k_extra > 0 = under-folded)
or folded but the game did not (k_extra < 0 = over-folded). This tool counts that on real exports.

WHY IT MATTERS. The 2-2.9% unexplained band and the famous "0.87 = 1/1.15" residual are the same
question asked twice. The recon established the arithmetic; this turns it into a census with a paper
trail, per export and per export group. It reads the export directly (no core import) so it is an
INDEPENDENT check of the core's own numbers.

Exclusions: residuals near 0 (< 0.02), and the critical-damage band (residual == critDamageRate/100,
which is a crit multiplier, not a rule). Both are counted so the exclusion is visible.

Usage: python -m contrib.rule115_census [glob]
Writes _dpsm_work/contrib/reports/rule115_census.txt and prints an ASCII summary.
"""
from __future__ import annotations
import glob, io, json, math, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
WORK = os.path.dirname(HERE)
ROOT = os.path.dirname(WORK)
EXPORTS = os.path.join(ROOT, "BepInEx", "plugins", "DpsMeter", "exports")
OUTDIR = os.path.join(HERE, "reports")
LN115 = math.log(1.15)
POW = {1.15: 1, 1.3225: 2, 1.5209: 3, 1.520875: 3}   # 1.15^3: 4-decimal form (<=1.6.0) and exact (>=1.6.1)


def k_folded_of(folds):
    """How many 1.15 copies the composition folded into this hit."""
    k = 0
    for f in folds:
        try:
            fac = float(f.get("factor") or 0.0)
        except (TypeError, ValueError):
            continue
        for cand, m in POW.items():
            if abs(fac - cand) < 1e-4:
                k += m
                break
    return k


def scan(path):
    with io.open(path, encoding="utf-8") as fh:
        d = json.load(fh)
    out = {"file": os.path.basename(path), "version": d.get("version"), "quest": d.get("quest"),
           "hits_team": 0, "no_calc": 0, "no_residual": 0, "near_zero": 0, "crit_band": 0,
           "unexplained_residual": 0, "k_extra": {}, "k_folded_hist": {}, "residual_top": {},
           "missed_115": 0, "missed_1152": 0, "over_115": 0, "over_1152": 0, "over_1153": 0,
           "aligned": 0}
    actors = {a.get("key"): a for a in (d.get("actors") or [])}
    for e in (d.get("events") or []):
        if e.get("type") != "dmg":
            continue
        atk = actors.get(e.get("atkKey"))
        if atk is None or atk.get("team") != 1:
            continue
        out["hits_team"] += 1
        calc = e.get("calc")
        if not isinstance(calc, dict):
            out["no_calc"] += 1
            continue
        resid = calc.get("residual")
        if not isinstance(resid, (int, float)) or resid <= 0:
            out["no_residual"] += 1
            continue
        resid = float(resid)
        kf = k_folded_of(calc.get("fold") or [])
        out["k_folded_hist"][kf] = out["k_folded_hist"].get(kf, 0) + 1
        if resid < 0.02:
            out["near_zero"] += 1
            continue
        cdr = calc.get("critDamageRate")
        if isinstance(cdr, (int, float)) and abs(resid - float(cdr) / 100.0) <= 0.005:
            out["crit_band"] += 1
            continue
        delta = math.log(resid) / LN115
        ke = int(round(delta))
        if abs(delta - ke) > 0.25:
            out["unexplained_residual"] += 1
            continue
        out["k_extra"][ke] = out["k_extra"].get(ke, 0) + 1
        if ke == 0:
            out["aligned"] += 1
        elif ke == 1:
            out["missed_115"] += 1
        elif ke >= 2:
            out["missed_1152"] += 1
        elif ke == -1:
            out["over_115"] += 1
        elif ke == -2:
            out["over_1152"] += 1
        else:
            out["over_1153"] += 1
        key = repr(round(resid, 4))
        out["residual_top"][key] = out["residual_top"].get(key, 0) + 1
    return out


def main():
    pattern = sys.argv[1] if len(sys.argv) > 1 else "battle_411001_*.json"
    paths = sorted(glob.glob(os.path.join(EXPORTS, pattern)), key=os.path.getmtime)
    if not paths:
        print("no exports matched %s" % pattern)
        return 2
    rows = [scan(p) for p in paths]
    L = [u"1.15 实际生效次数普查(离线重放;公式 k_extra = round(ln(residual)/ln 1.15))", u""]
    L.append(u"%-34s %6s %5s %5s %6s %6s %6s | 少折(1.15/1.15²) 多折(1.15/1.15²/1.15³) 对齐" %
             (u"导出", u"version", u"我方击", u"无calc", u"近零", u"会心带", u"残差不可判"))
    tot = {"missed_115": 0, "missed_1152": 0, "over_115": 0, "over_1152": 0, "over_1153": 0, "aligned": 0,
           "judged": 0}
    for r in rows:
        judged = sum(r["k_extra"].values())
        tot["judged"] += judged
        for k in ("missed_115", "missed_1152", "over_115", "over_1152", "over_1153", "aligned"):
            tot[k] += r[k]
        L.append(u"%-34s %6s %5d %5d %6d %6d %6d | %d/%d   %d/%d/%d   %d" %
                 (r["file"], r["version"], r["hits_team"], r["no_calc"], r["near_zero"], r["crit_band"],
                  r["unexplained_residual"], r["missed_115"], r["missed_1152"],
                  r["over_115"], r["over_1152"], r["over_1153"], r["aligned"]))
    L.append(u"")
    L.append(u"合计:可判 %d 击;少折 ×1.15 %d、×1.15² %d;多折 1/1.15 %d、1/1.15² %d、1/1.15³ %d;对齐 %d" %
             (tot["judged"], tot["missed_115"], tot["missed_1152"],
              tot["over_115"], tot["over_1152"], tot["over_1153"], tot["aligned"]))
    L.append(u"")
    L.append(u"每个导出的 k_extra 分布(0 = 折叠与游戏一致;>0 = 游戏多给了 1.15^k):")
    for r in rows:
        L.append(u"  %-34s %s" % (r["file"], json.dumps({str(k): v for k, v in sorted(r["k_extra"].items())})))
    L.append(u"")
    L.append(u"证据等级:残差与折叠因子来自导出字段(实测);k_extra 由 ln 公式换算(离线重放);")
    L.append(u"         「是否为同一规则」未判(推断)—— k_extra 是净差,按规则标签分组后才能解释。")
    os.makedirs(OUTDIR, exist_ok=True)
    out = os.path.join(OUTDIR, "rule115_census.txt")
    with io.open(out, "w", encoding="utf-8") as fh:
        fh.write(u"\n".join(L))
    print("exports=%d judged=%d missed115=%d missed1152=%d over115=%d over1152=%d over1153=%d aligned=%d" %
          (len(rows), tot["judged"], tot["missed_115"], tot["missed_1152"],
           tot["over_115"], tot["over_1152"], tot["over_1153"], tot["aligned"]))
    for r in rows:
        print("  %-34s ver=%s judged=%d missed=%d/%d over=%d/%d/%d" %
              (r["file"], r["version"], sum(r["k_extra"].values()),
               r["missed_115"], r["missed_1152"], r["over_115"], r["over_1152"], r["over_1153"]))
    print("report=%s" % out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
