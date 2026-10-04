
# 重构批次记录 RF7l(第 35 轮):读那 5 处点名 —— 结论是"点名从来不是调用"

> 依据:第 34 轮把"需要人工读"的边界收窄到 5 处点名行。本轮逐处读了它们,**结果推翻了"活点名"这个判据本身**。

## 0. 结论

| 项 | 状态 |
|---|---|
| 5 处点名的真相 | **全部是文本**:注释、docstring、列表条目——**没有一处是调用** |
| 因此判定 | 3 条 → `indexed`(无活调用者);未判定 5 → **3**(上限收紧到 3) |
| 后 2 条的真相 | `report_text.py`/`dpsmeter_contrib.py` 是**活闸门 `check_docs_123.py` 的输入文件**;`validate.py` 只被流水线脚本当**数据字符串**提到 |
| 一次自查并改正 | 上一版把 `dpsmeter_contrib.py` 判成 indexed——因为我的输入清单用 `_dpsm_work/` 前缀拼路径,而它在**仓库根**;本轮已改回未判定 |
| **部署 DLL** | 未改:1.7.11 / 387,072 B / `36EC96D4…` |

## 1. 五处点名的原文(这就是全部证据)

| 目标 | 点名处 | 性质 |
|---|---|---|
| `contrib/report_json.py` | `check_doc_convergence.py:15` `knownLimits <- src/Output/Contribution.cs vs contrib/report_json.py` | **注释** |
| 同上 | `test_gate.py:242` "…the offline report_json.py cannot restate…" | **注释** |
| `contrib/report_text.py` | `check_docs_123.py:35` 路径列表条目 | **列表(输入)** |
| `contrib/tests/test_golden_155.py` | `test_gate.py:16` docstring 用例说明;`contribution_gate.py:15` 注释 | **docstring / 注释** |
| `contrib/validate.py` | `contribution_gate.py:12` 注释;`n0_acceptance.py:157` **数据字符串** | **注释 / 数据** |
| `dpsmeter_contrib.py` | `check_docs_123.py:38` 路径列表条目 | **列表(输入)** |

**结论**:"某个活脚本的文本里出现了它"**从来不是**"有活调用者"的证据——第 30 轮坚持"证据表不是闸门"是对的,
而第 31 轮把它当作"保留未判定"的理由**过强**了。本轮的判据因此回到**真依赖**上:
**流水线脚本的文本(J)** 与 **活闸门的输入清单** —— 这两类是真依赖,注释不是。

## 2. 剩下 3 条缺的是"词汇",不是证据

- `contrib/report_text.py`、`dpsmeter_contrib.py`:**被活闸门当作输入文件读取**(删掉它们会弄坏 `check_docs_123.py`),
  但它们**不是谁运行的 CLI**;
- `contrib/validate.py`:只被流水线脚本当**数据字符串**提到,`J` 因此拒绝给它 indexed 状态(宁可保守)。

三者都指向同一个缺口:**注册表的状态词汇表里没有"被引用的输入"这一类**。
加这一类需要同时改守卫(G/J 的语义)与自测,是**一次独立的改动**,不该顺手塞进判定轮。

## 3. 验证与回滚

| 项 | 值 |
|---|---|
| 注册表 | `registered=96 active=27 unclassified=3 (pin 3) pipeline=21` → **PASS** |
| 守卫自测 | PASS |
| 插件源码 | **零变化** |
| 回滚 | `git revert`:注册表 3 条 + 上限 + 本记录 |
