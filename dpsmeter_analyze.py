#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
DpsMeter 离线精细分析器
=======================
读取 DpsMeter 插件导出的 JSON(默认 BepInEx/plugins/DpsMeter/exports/),
用 matplotlib 对单场战斗做精细图表分析。

用法:
  python dpsmeter_analyze.py                 # 分析导出目录里最新一场
  python dpsmeter_analyze.py battle_9999_*.json
  python dpsmeter_analyze.py <目录|通配符>

依赖: pip install matplotlib numpy
"""

import json, sys, os, glob

SAVE = "--save" in sys.argv
SAVE_DIR = "."
if SAVE:
    i = sys.argv.index("--save")
    if i + 1 < len(sys.argv) and not sys.argv[i + 1].startswith("-"):
        SAVE_DIR = sys.argv[i + 1]
    os.makedirs(SAVE_DIR, exist_ok=True)

try:
    import numpy as np
    import matplotlib
    matplotlib.use("Agg" if SAVE else "TkAgg")
    import matplotlib.pyplot as plt
except Exception as e:
    print("缺少依赖: %s" % e)
    print("请先:  pip install matplotlib numpy")
    sys.exit(1)

# 让中文/日文名字能显示(Windows 雅黑)
def setup_font():
    for name in ("Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "Yu Gothic UI", "Meiryo"):
        try:
            from matplotlib import font_manager
            if any(name.lower() in f.name.lower() for f in font_manager.fontManager.ttflist):
                plt.rcParams["font.sans-serif"] = ["Microsoft YaHei", "SimHei", "Meiryo", "DejaVu Sans"]
                plt.rcParams["axes.unicode_minus"] = False
                return
        except Exception:
            pass

ALLEY = {1: "#4aa3ff", 2: "#ff6b6b"}

def find_default_dir():
    here = os.path.dirname(os.path.abspath(__file__))
    cand = os.path.join(here, "BepInEx", "plugins", "DpsMeter", "exports")
    if os.path.isdir(cand):
        return cand
    return "."

def load(path):
    if os.path.isdir(path):
        files = sorted(glob.glob(os.path.join(path, "battle_*.json")))
    else:
        files = sorted(glob.glob(path))
    if not files:
        print("未找到 JSON 数据:", path)
        sys.exit(1)
    return files

def plot_battle(data):
    actors = data.get("actors", [])
    n = len(actors)
    if n == 0:
        print("该战斗没有角色数据。")
        return

    tmax = max((len(a.get("perSecDamage", [])) for a in actors), default=1)
    tmax = max(tmax, max((len(a.get("perSecTaken", [])) for a in actors), default=1))
    ts = np.arange(tmax)

    def cum(arr):
        return np.cumsum(np.asarray(arr[:tmax] + [0] * max(0, tmax - len(arr)), dtype=float))

    fig = plt.figure(figsize=(16, 9))
    fig.suptitle("战斗任务 %s  时长 %s 秒  结果 %s" % (data.get("quest"), data.get("duration"), data.get("result")),
                 fontsize=14)

    # 1. 各角色 累计伤害
    ax = fig.add_subplot(2, 3, 1)
    for a in actors:
        ax.plot(ts, cum(a.get("perSecDamage", [])), label=a["name"], lw=1.6,
                color=ALLEY.get(a.get("team", 2)))
    ax.set_title("各角色累计伤害"); ax.set_xlabel("秒"); ax.set_ylabel("伤害")
    ax.grid(alpha=.3)

    # 2. 各角色 每秒伤害
    ax = fig.add_subplot(2, 3, 2)
    for a in actors:
        d = a.get("perSecDamage", [])
        y = np.asarray(d[:tmax] + [0] * max(0, tmax - len(d)), dtype=float)
        ax.plot(ts, y, lw=1.1, color=ALLEY.get(a.get("team", 2)))
    ax.set_title("各角色每秒伤害"); ax.set_xlabel("秒"); ax.set_ylabel("每秒伤害")
    ax.grid(alpha=.3)

    # 3. 各角色 累计承伤 与 治疗
    ax = fig.add_subplot(2, 3, 3)
    for a in actors:
        if a.get("team") != 1:
            continue
        ax.plot(ts, cum(a.get("perSecTaken", [])), lw=1.2, color="#ff8c6b")
        ax.plot(ts, cum(a.get("perSecHeal", [])), lw=1.2, color="#5fd08a")
    ax.set_title("我方累计承伤(红) / 治疗(绿)"); ax.set_xlabel("秒"); ax.set_ylabel("量")
    ax.grid(alpha=.3)

    # 4. 事件时间线(每秒伤害事件量, 按归属颜色)
    ax = fig.add_subplot(2, 3, 4)
    events = data.get("events", [])
    if events:
        xs = [e["t"] for e in events]
        ys = [e["amount"] for e in events]
        cols = []
        for e in events:
            w = e.get("victim", "")
            side = "ally" if any(a["name"] == w and a.get("team") == 1 for a in actors) else "enemy"
            cols.append("#4aa3ff" if side == "ally" else "#ff6b6b")
        from matplotlib.collections import LineCollection
        ax.scatter(xs, ys, s=3, c=cols, alpha=.5)
        ax.set_title("逐事件(蓝=我方受击, 红=敌方受击)"); ax.set_xlabel("秒")
        ax.set_ylabel("伤害量")
    else:
        ax.text(.5, .5, "无事件数据", ha="center")
    ax.grid(alpha=.3)

    # 5. 汇总(嵌套滚动表样式)
    ax = fig.add_subplot(2, 3, 5); ax.axis("off")
    lines = [("汇总", ""), ("我方总伤", data["totals"].get("dealt", 0)),
             ("承伤", data["totals"].get("taken", 0)),
             ("治疗", data["totals"].get("healing", 0)),
             ("未归属", "%s (x%s)" % (data["totals"].get("unattributedDamage", 0),
                                     data["totals"].get("unattributedHits", 0)))]
    for i, (k, v) in enumerate(lines):
        ax.text(0, 1 - i * .12, "%s: %s" % (k, v), fontsize=10)

    # 6. 伤害来源分布(前几名)
    ax = fig.add_subplot(2, 3, 6)
    src = {}
    for a in actors:
        for k, v in a.get("sources", {}).items():
            src[int(k)] = src.get(int(k), 0) + v
    if src:
        labels = list(src.keys()); vals = list(src.values())
        ax.bar([str(l) for l in labels], vals, color="#8ac6ff")
        ax.set_title("伤害来源分布(DamageSource)"); ax.set_ylabel("伤害总量")
    else:
        ax.text(.5, .5, "无来源数据", ha="center")

    # 图例(前 12 名)
    handles, labels = [], []
    for a in sorted(actors, key=lambda x: -x.get("dealt", 0))[:12]:
        handles.append(plt.Line2D([], [], color=ALLEY.get(a.get("team", 2)), lw=1.6))
        labels.append("%s  总伤 %s" % (a["name"], a.get("dealt", 0)))
    fig.legend(handles, labels, loc="lower center", ncol=3, fontsize=8)

    plt.tight_layout(rect=[0, .05, 1, .96])
    if SAVE:
        out = os.path.join(SAVE_DIR, "battle_%s_%s.png" % (data.get("quest"), data.get("started", "").replace(":", "-").replace("T", "_")))
        fig.savefig(out, dpi=110)
        print("已保存图表 -> %s" % out)
    else:
        plt.show()

    # 打印文本汇总
    print("=== %s ===" % data.get("quest"))
    for a in sorted(actors, key=lambda x: -x.get("dealt", 0)):
        print("%-24s 总伤 %-9s 承伤 %-9s 治疗 %-9s 命中 %s 最大 %s" %
              (a["name"], a.get("dealt"), a.get("taken"), a.get("healingTaken") or 0,
               a.get("hit"), a.get("maxHit")))

def main():
    setup_font()
    # strip --save [dir] flags to find the real input path
    raw = sys.argv[1:]
    args = []
    skip = 0
    for t in raw:
        if skip > 0:
            skip -= 1
            continue
        if t == "--save":
            skip = 1
            continue
        args.append(t)
    path = args[0] if args else find_default_dir()
    files = load(path)
    print("找到 %d 个战斗文件" % len(files))
    for f in files:
        with open(f, encoding="utf-8") as fp:
            data = json.load(fp)
        print("分析: %s" % f)
        plot_battle(data)

if __name__ == "__main__":
    main()
