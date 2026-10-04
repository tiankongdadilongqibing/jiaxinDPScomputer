
# 重构批次记录 RF7k(第 34 轮):contrib 孤岛判掉大半 —— 10 → 5

> 依据:第 33 轮发现"包内没有任何成员被 active 脚本**导入**"。本轮把判据补全为**三条同时为空**:
> 没有活脚本**导入**它、没有活脚本**点名**它、也不在流水线脚本里。

## 0. 结论

| 项 | 状态 |
|---|---|
| 本轮判定 | **5 条 → indexed**;`contrib/{aggregate,attribution,legacy_diff,loader,model,run}` 一类的包内子图(死根) |
| 计数 | 未判定 10 → **5**(上限收紧到 5);active 27、已索引 64 |
| 判定条件 | 唯一点名它的都是**已死**脚本,且无活脚本导入/点名/运行 |
| **部署 DLL** | 未改:1.7.11 / 387,072 B / `36EC96D4…` |

## 1. 留下的 5 条,以及它们为什么留下(每条都有活脚本点名)

| 脚本 | 活点名者 |
|---|---|
| `contrib/report_json.py` | `check_doc_convergence.py`、`contrib/tests/test_gate.py` |
| `contrib/report_text.py` | `check_docs_123.py` |
| `contrib/tests/test_golden_155.py` | `test_gate.py`、`contribution_gate.py` |
| `contrib/validate.py` | `contribution_gate.py`、`n0_acceptance.py`、`v150_validate.py` |
| `dpsmeter_contrib.py` | `check_docs_123.py` |

**注意"点名"仍不等于"调用"**:可能是 import,也可能是**注释或字符串模式**(例如 `check_docs_123.py` 里的路径模式)。
这 5 条因此不是"判不了",而是**必须读那一行点名**才能定:下一轮只需打开每个点名处看它是导入、调用还是文本模式,
即可把 5 条全部落定——这是**最后一次人工读**的边界,已收窄到 5 处。

## 2. 判死的 5 条为什么可以判

它们的**唯一**点名者是**已死**脚本(如 `contrib/__init__.py`、`contrib/run.py`),
而 `run.py` 这一"包内根"本身**没有活点名者**——所以整条子图从活世界不可达。
这与第 28 轮说的"要与调用者一起判死"一致:根死,子图死。

## 3. 验证与回滚

| 项 | 值 |
|---|---|
| 注册表 | `registered=96 active=27 unclassified=5 (pin 5) pipeline=21` → **PASS** |
| 守卫自测 | PASS |
| 插件源码 | **零变化** |
| 回滚 | `git revert`:注册表 5 条 + 上限 + 本记录 |
