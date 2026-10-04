
# 重构批次记录 RF7i(第 32 轮):在活跃闭包里的两条 —— 14 → 12

> 依据:第 31 轮保留的 14 条。本轮判的是其中**被活跃脚本直接导入**的两条,判定依据是**导入者本身的状态**,
> 而不是"我猜它有用"。

## 0. 结论

| 项 | 状态 |
|---|---|
| `contribution_gate.py` | **active** — 被 **3 个 active 脚本**导入(`check_live_log.py`、`contrib/crosscheck.py`、`contrib/tests/test_gate.py`) |
| `identity_map.py` | **active** — 导入者 `contrib/compare.py` 与 `decision_report.py` **本身就是 active** |
| 计数 | active 23 → **25**;未判定 14 → **12**(上限收紧到 12) |
| 用途来源 | **各自的 docstring 首行**(不是我的描述) |
| **部署 DLL** | 未改:1.7.11 / 387,072 B / `36EC96D4…` |

## 1. 两次被守卫纠正(记录在案)

1. **E 检查**:我把 `outputs` 留成 `[]`,守卫报
   `E active entry declares no outputs (use ["none (read-only)"])`——它**直接给出了本仓库的约定**,
   照做即可;
2. **我自己发现的一处不诚实**:第一遍写证据时我取了"排序后第一个导入者"当"活跃导入者",而
   `identity_map.py` 的第一个导入者是 `contrib/compare.py`——它当时**还不是** active。
   于是改为**先筛选出 active 导入者**,没有就**退回未判定**并写明"闭包经死脚本到达它"。
   幸好两个都真有 active 导入者(见上表),但流程本身修好了:**证据字符串必须只写已核实的事实**。

## 2. 为什么这两条可以这样判

`contribution_gate.py` 被三个 active 脚本导入 ⇒ 它**必然**是活依赖链的一部分,`active` 名正言顺。
`identity_map.py` 的两个导入者都是 active ⇒ 同理。两者的**用途**取自其 docstring:
"Contribution validation GATE: one status vocabulary and one exit-code contract." 与
"N3 / P1 stage-1 offline cross-battle identity mapping (read-only)."

## 3. 余下 12 条

含 `contrib/{aggregate,attribution,loader,model,report_json,report_text,run,validate}.py`、
`contrib/tests/test_golden_155.py`、`n0_acceptance.py`、`tests/negative_control.py`、`dpsmeter_contrib.py`。
这些要么自身是**入口/闸门**(应判 active,但要写清输出),要么在 `contrib/` 包内需要**读包内调用关系**
才能判死——留给下一轮按同一套证据做。

## 4. 验证与回滚

| 项 | 值 |
|---|---|
| 注册表 | `registered=96 active=25 unclassified=12 (pin 12) pipeline=21` → **PASS** |
| 守卫自测 | PASS |
| 插件源码 | **零变化** |
| 回滚 | `git revert`:注册表两条 + 上限 + 本记录 |
