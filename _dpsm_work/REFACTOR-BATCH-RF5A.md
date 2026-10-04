# 重构批次记录 RF5a(第 6 轮):贡献视图缓存判据抽取 + 缓存语义 ADR

> 依据:[REFACTOR-PLAN-POST-1.7.11.md](<REFACTOR-PLAN-POST-1.7.11.md>) §11(RF5)。该节要求先写清**规则**再改缓存语义,
> 所以本轮做两件事:把已有判据搬进纯函数(行为不变)、把**决策**写成 [ADR](<CACHE-SEMANTICS-ADR.md>)(不改行为)。

## 0. 结论

| 项 | 状态 |
|---|---|
| `Policy/ContributionCachePolicy.cs`(判据 + 四条原因文案) | ✅ 89 行纯文件 |
| `ContributionSession.Get` 接线 | ✅ 行为不变(既有 24 条 `cache/` 用例**原样通过**) |
| 缓存语义 ADR(三个待决问题) | ✅ 交付,见 [CACHE-SEMANTICS-ADR.md](<CACHE-SEMANTICS-ADR.md>) |
| 新增覆盖 | ✅ 26 个用例 + 7 例变异负控 |
| 缓存语义**行为**改变 | ❌ 未做(方案要求先决策) |
| **部署 DLL** | **仍未改**:1.7.11 / 387,072 B / `36EC96D4…` |

## 1. 抽了什么

| 迁移前 | 迁移后 |
|---|---|
| `ContributionSession.Get` 里的四路 OR 陈旧判定(内联 `ReferenceEquals` + 计数 + 折叠标记 + `now - at > RefreshSeconds`) | `ContributionCachePolicy.IsStale(...)`,调用方只负责比较会话身份与传入时间 |
| `RefreshSeconds = 1.0` 私有常量 | 策略常量(带文档说明它**不是**节流上限) |
| 四条"不可用"中文文案(分散在活分支与历史分支) | `CacheUnavailable` 四个码 + `ReasonText` 单一出处(活分支与历史分支对"关闭折叠"的措辞**故意不同**,现在这一点写在枚举注释里) |
| "非活 + 有缓存 ⇒ 显示上一场"的选择 | `SelectSource(live, hasCache)` |
| `ContributionView` 的构造(3 处不可用视图) | 一个 `Unavailable(live, reason)` 私有帮助函数 |

**没有搬走的**:缓存**状态**本体(7 个静态字段,含哨兵初值 `-1` / `-1e9`)与 `Invalidate()`;`Time.unscaledTime` 读取仍在门面
(时钟来源属于门面 —— 与 RF3/RF4 同一条规则)。

## 2. 行为不变的证据

RF1 就有一组 `cache/` 用例(**24 条**)直接驱动 `ContributionSession.Get`,覆盖:活/非活、折叠开关的两种措辞、
"尚无伤害事件"、缓存复用、重算后缓存更新、**同一会话回填到相同计数复用旧缓存**、失效后"暂无战斗数据"。
本轮**没有改这些用例的任何一条**,它们原样通过 —— 这就是"抽出判据没有改变行为"的证据;新增的 26 条只是把
判据本身(每个子条件、边界、文案)单独钉住。

## 3. 调查发现的两件事(都写进 ADR)

| 发现 | 证据 | 处置 |
|---|---|---|
| **源码注释与行为不符**:注释说"refreshed at most once a second, and only when the event count actually moved",但四个条件是 OR,**计数一变就立刻重算**;"至多一秒一次"只在计数不变时成立 | 判据代码 + 新用例 `a-count-change-recomputes-with-no-delay` | 策略里写明真实语义;ADR §1.3 记录,选项 A 就是修文档 |
| **`ContributionSession.Invalidate()` 在生产里没有调用者** | `grep ContributionSession.Invalidate` 在 `src` 下只命中一处**注释**;唯一真正的调用在测试里 | ADR 的 Q2:建议 `F9` 接上它(它正是消除"同会话回填复用旧缓存"这条危险行为的手段);本轮**不改**,因为那是行为决策 |

顺带确认(写进 ADR §3):**方案要求的"暂存摘要 vs 最终摘要"分离已经存在**,只是在展示层 ——
`OverlayUGUI.Rows.ResolveContributionView` 在"上一场"场景下从 `History[0].Session`(导出写贡献段用的同一对象)**重算**并备忘,
缓存只作回退。这解释了为什么"结算时 `Invalidate()`"是错的(注释已论证),而"重置时 `Invalidate()`"是对的。

## 4. 新增覆盖

| 组 | 内容 |
|---|---|
| `policy/cache` | 复用/陈旧各子条件、"计数一变零延迟重算"(证明不是节流)、严格 `>` 边界(恰好 1.0 ⇒ 复用;+1 ulp ⇒ 陈旧)、**不可达的折叠支**若被走到则成立 |
| `policy/cache-source` | 活 / 历史 / 无 三选一与优先级 |
| `policy/cache-reason` | 四条文案逐字(含活分支与历史分支的措辞差异) |

27 个断言里有一条在本轮**先红后修**:`+1 ulp` 边界最初写成 `100.0 + Math.BitIncrement(1.0)`,
而在 100 这个量级上 ulp 约 1.4e-14,**加法把 +1ulp 舍回成恰好 101.0**,于是断言"陈旧"失败。
改为在 0 附近取 ulp 后通过;这条注释留在用例旁,免得下次再踩。

## 5. 验证与回滚

| 项 | 值 |
|---|---|
| 构建 | 0 警 0 错;**未部署** |
| 行为套件 | `cases=430 failed=0 pinned=430`(既有 24 条 `cache/` 用例原样通过) |
| 变异负控 | 46 例(新增 7 例指向缓存判据,含"把严格 `>` 改成 `>=`") |
| 守卫 | `refactor_final_check` blocks=0 + 自测 PASS;文档收敛 0/11;docs123 40 份 0 损伤;`repo_manifest --verify` drift=0 |
| 验收 | `acceptance_rf5a` |
| 回滚 | `git revert`:新文件 + 一处方法体重写,无数据/契约变化;部署 DLL 从未被替换 |

## 6. 未做(诚实清单)

1. 缓存语义的三问未决(Q1 滞后上限 / Q2 F9 是否 `Invalidate` / Q3 历史回退),见 ADR §5;
2. `OverlayUGUI` 的 `_prevSummary*` 备忘与表格渲染未动(方案 §11 的"拆 ColumnSpec/RowViewModel/DisplayFormatter"更大,
   且属展示层重构,需要有对应的离线渲染判据才值得动);
3. `Contribution.Compute` 核心未动(它是纯计算,已被导出数据间接验证)。
