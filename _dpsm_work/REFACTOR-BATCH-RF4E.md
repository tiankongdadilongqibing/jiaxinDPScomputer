# 重构批次记录 RF4e(第 9 轮):单场运行态的第一片 —— 计数与自报

> 依据:[REFACTOR-PLAN-POST-1.7.11.md](<REFACTOR-PLAN-POST-1.7.11.md>) §9 第三族(单场运行态)。
> 矩阵给这一族的前置是"先把读点收敛";本轮先迁**没有原生读取、没有顺序耦合**的那一片,
> 并把矩阵只能用散文描述的"重置规则"变成可执行的方法。

## 0. 结论

| 项 | 状态 |
|---|---|
| `Runtime/BattleRuntimeCounters.cs`(85 行,12 个字段) | ✅ 计数与自报族 |
| 门面接线 | ✅ 44 处读写改为 `Rt.X`;4 处转换点改为方法调用 |
| 转换语义 | ✅ `OnSessionStart` / `OnManualReset` 可执行;resume 与 finalize **不清**任何一项(后者故意没有方法) |
| 新增覆盖 | ✅ 23 个用例(含 12 字段的**结构钉住**)+ 6 例变异负控 |
| 第三族其余部分 | ❌ `Session` / `_activeCalc` / `_calcEvents` / `_lastCalcSrc` 未动(见 §3) |
| **部署 DLL** | **仍未改**:1.7.11 / 387,072 B / `36EC96D4…` |

## 1. 迁了什么

| 迁移前(`Aggregator` 静态字段) | 迁移后(`Rt.X`) |
|---|---|
| `_eventCount` | `EventCount` |
| `AbsorbedTotal` / `AbsorbedHits` | `AbsorbedTotal` / `AbsorbedHits` |
| `HitDetailProduced` / `HitDetailTrimmed` / `HitDetailErrors` | 同名 |
| `HitMatchExact` / `HitMatchPair` / `HitMatchNone` | 同名 |
| `_lastSummaryLog` / `_lastTimeLog` | `LastSummaryLog` / `LastTimeLog` |
| `_gameTimeAtStart` | `GameTimeAtStart` |

共 12 个字段、12 行声明删除、**44 处读写**改名为 `Rt.X`(6 个文件:`Clock` 10、`ExportService` 12、
`Attribution` 9、`Stats` 7、`Finalize` 5、`CalcReconcile` 1),4 处转换点改为方法调用。构建一次通过,0 警 0 错。

## 2. 转换规则从散文变成代码

| 转换 | 行为 | 矩阵原文 |
|---|---|---|
| `OnSessionStart()` | 11 个字段全部清零;`GameTimeAtStart` **不**清(它的值来自同一处的一次原生读取,由门面写入) | `StartSession` = 0 |
| `OnManualReset()` | **只**清 `EventCount` | `ResetCurrent`(F9)= 0 |
| 软恢复(resume) | 一个方法都不调用 | "不重置" |
| 结算(finalize) | **没有** `OnFinalize` 方法 —— 导出要读这些计数 | "不重置" |

最后一行是这一族最容易搞错的地方,所以它用"方法不存在"来表达,而不是用注释:任何人都不会误以为结算会清空它们。

## 3. 为什么先迁这一片(以及第三族还剩什么)

| 判据 | 这一片(计数/自报) | 剩下的部分 |
|---|---|---|
| 原生读取 | 无 | `Session`(整场对象)、`_activeCalc`(持有 `DamageCalculater`)、`_calcEvents`(持有 `BattleObject`)、`_lastCalcSrc` |
| 顺序耦合 | 无(独立标量赋值) | `_calcEvents` 的 FIFO 淘汰与归属配对**顺序**有关;`_activeCalc` 与攻击开始/清理成对 |
| 可离线执行 | ✅ 容器只有标量 | ❌ 需要 IL2CPP 桩 |

所以这一片是第三族里**唯一**能在离线测试里把"重置规则"钉死的部分;其余部分迁移前需要先有"攻击开始→伤害→清理"的用例
(矩阵 §7 已把这条前置写下)。本轮不为了凑整齐而提前搬它们。

## 4. 覆盖

| 组 | 内容 |
|---|---|
| `runtime/counters` | 开始新场:11 项清零 + `GameTimeAtStart` 保持(由门面写入);F9:只清事件数,吸收总量/自报/节流/里程全部保留 |
| `runtime/counters-shape` | 用反射把 12 个字段的**名字排序后**整个钉住:加第 13 个字段必须是有意识的决定(它属于哪次转换?) |

结构钉住这条用例的价值在本轮就体现了:变异负控里 `counters-thirteenth-field`(随手加一个字段)**必须红**。

## 5. 验证与回滚

| 项 | 值 |
|---|---|
| 构建 | 0 警 0 错;**未部署** |
| 行为套件 | `cases=475 failed=0 pinned=475`(14 组) |
| 变异负控 | 58 例(新增 6 例:清多了、清少了、清错项、多一个字段) |
| 守卫 | `refactor_final_check` 99 个 .cs blocks=0 + 自测 PASS;文档收敛 0/11;docs123 43 份 0 损伤;`repo_manifest --verify` drift=0 |
| 验收 | `acceptance_rf4e` |
| 回滚 | `git revert`:新文件 + 44 处改名 + 4 处方法调用;无数据/契约变化;部署 DLL 从未被替换 |

## 6. 未做

1. 第三族其余部分(§3 表右侧):前置是"攻击开始→伤害→清理"的可离线用例;
2. `Session` 本身的承载方式(是否改为 `BattleSession` 的引用持有者)需要与 `Aggregator.Session` 的公开读法一起设计,
   属结构决策,不宜与改名同批;
3. 快照/序列缓存类状态(`_cache*`、`_prevSummary*`)属 RF5,已在 ADR 里待决。
