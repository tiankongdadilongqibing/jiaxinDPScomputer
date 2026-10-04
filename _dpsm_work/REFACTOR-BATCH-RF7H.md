
# 重构批次记录 RF7h(第 31 轮):第一批按证据表判定 —— 28 → 14

> 依据:第 30 轮的证据表(`tool_census.py --invokers`)。本轮按它判定**证据三面都空**的一批:
> 没人导入(传递性活跃闭包外)、**没人按名字点名**、也不在流水线脚本里。

## 0. 结论

| 项 | 状态 |
|---|---|
| 本轮判定 | **14 条 → indexed**;未判定 **28 → 14**(上限同步收紧到 14) |
| 判定条件 | 三条机械证据**全空**;任一条命中就**保留未判定** |
| 守卫复核 | **G**(传递性活跃)+ **J**(不得被流水线脚本点名)**PASS**;自测 PASS |
| **部署 DLL** | 未改:1.7.11 / 387,072 B / `36EC96D4…` |

## 1. 被保留的 14 条,以及它们为什么被保留(这就是表的价值)

| 脚本 | 保留原因(机械证据) |
|---|---|
| `n0_acceptance.py` | 被 **2 个 active** 脚本点名——**上一轮的错误现在被结构性挡住** |
| `tests/negative_control.py` | 被 active 脚本与流水线脚本点名 |
| `contrib/report_json.py` | 被 `check_doc_convergence.py`(active)、`contrib/tests/test_gate.py` 点名 |
| `contrib/report_text.py` | 被 `check_docs_123.py`(active)点名 |
| `contrib/{aggregate,attribution,loader,model,run,validate}.py` | 被 `contrib/__init__.py` / `run.py` 点名(调用链仍在) |
| `contrib/tests/test_golden_155.py` | 被 `test_gate.py`、`contribution_gate.py` 点名 |
| `contribution_gate.py`、`identity_map.py` | **在活跃闭包里**(被 active 脚本导入) |
| `dpsmeter_contrib.py` | 被 `check_docs_123.py`(active)点名 |

**注意**:保留不等于"活跃"——它们只是**还没有足够证据判死**。下一批要在**调用链内部**逐条判
(例如 `contrib/run.py` 是否是活入口),那需要读代码,属于第 28 轮说的"逐条人工判断"。

## 2. 判定的三条机械证据(可复算)

1. **不在活跃闭包里**(种子 = active ∪ 流水线,沿 `imports_local` 求闭包);
2. **没有任何已登记脚本的源码文本含它的 basename**;
3. **不在 `n0_acceptance.py` 的文本里**。

任一条命中 ⇒ 不判死。这样判出来的 14 条,其"没有活的东西依赖它"是**三点同时成立**的结论,
而不是"我没找到"。

## 3. 验证与回滚

| 项 | 值 |
|---|---|
| 注册表 | `registered=96 active=23 unclassified=14 (pin 14) pipeline=21` → **PASS** |
| 守卫自测 | PASS(含 G 的两个对照) |
| 插件源码 | **零变化** |
| 未跑新验收 | 无源码/流水线变更,37/69 结论不变(与第 28–30 轮同一处置) |
| 回滚 | `git revert`:注册表 14 条 + 上限 + 本记录 |
