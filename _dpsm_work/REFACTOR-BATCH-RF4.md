# 重构批次记录 RF4(第 3 轮):状态生命周期矩阵 + 第一个状态族迁移

> 依据:[REFACTOR-PLAN-POST-1.7.11.md](<REFACTOR-PLAN-POST-1.7.11.md>) §9(RF4)。第 1 步是**先交矩阵再动结构**;
> 本文件是这一轮的交付记录,矩阵本体见 [STATE-LIFETIME-MATRIX.md](<STATE-LIFETIME-MATRIX.md>)。

## 0. 结论

| 项 | 状态 |
|---|---|
| RF4 第 1 步:`StateLifetimeMatrix` | ✅ 交付(五类生命周期 + `Aggregator` 逐字段表 + 其他模块按族 + 6 项发现) |
| RF4 第 2 步:迁移第一个状态族(跨场衔接) | ✅ `Runtime/SessionContinuity.cs`(118 行,1 个实例,10 个字段) |
| 矩阵发现 ×1:死状态 | ✅ 删除 `_lastCompT` / `_lastPow`(每击一次的两次无读者写入) |
| 矩阵发现 ×3(观察,未改) | 预登记全局规则仍是"衔接状态却无 reset 入口";`BeforeLife` 以 static 充当调用栈;三类展示级缓存各有一套失效规则 |
| 其余状态族 | ❌ 未迁移,且**每族的前置条件**已写明(矩阵 §7) |
| **部署 DLL** | **仍未改**:1.7.11 / 387,072 B / `36EC96D4…`;新产物 389,632 B 只在 `src\bin\Release` |

## 1. 为什么先做矩阵而不是先搬代码

方案 §9 的要求是"先分类生命周期,再决定归属",并点名三条约束:全局规则**不能**每次结算一刀清空、
"暂存摘要"与"最终摘要"必须区分、纯模型不得因此获得 Unity/IL2CPP 依赖。
矩阵把每个字段的**写入点**与**读取点**列出来之后,五类生命周期自己就分开了 —— 关键的发现是:
`Aggregator` 里同时存在"每场清零"和"跨场保留"两种状态,**同一个类、同一批静态字段**,而当时的重置路径
(`StartSession`)看起来像"清空一切"。这正是把静态字段整体搬进一个按局销毁容器会静默改变行为的地方。

## 2. 迁移的族:跨场衔接

| 迁移前 | 迁移后 |
|---|---|
| `Aggregator` 的 10 个字段:`_lastClosed` / `_lastClosedWall` / `_lastClosedWhy` / `_runId` / `_runSeq` / `_lastEndWall` / `_hasEnded` / `_lastEndQuest` / `_lastEndResult` / `_lastEndWhy` | 一个 `SessionContinuity` 实例,方法 `BeginSession` / `RememberEnd` / `RememberClosed` / `Gate` / `ForgetClosed` |
| 写入点:`StartSession`(读+推标记)、`FinalizeLocked` **开头**、`FinalizeLocked` **结尾** | 同样的三个点,状态与转换集中到容器 |

**保住的 5 条不变式**(每条都有用例,组名见括号):

1. `RememberEnd` 仍在**结算开头**、`RememberClosed` 仍在**导出之后**(`runtime/continuity-resume`:非 idle 保留、窗口过期遗忘);
2. 只有**窗口过期**会遗忘最近关闭会话;真实结束与未知单位都保留它(`an-unknown-actor-leaves-the-session-remembered`);
3. 真实结束(`result != 0`)永不与下一场合并;同任务 + 无结果 + ≤2.0 s 才继续同一 run(`runtime/continuity-run-marker`);
4. `F9`/`ResetCurrent` **不触碰**跨场状态(`a-manual-reset-does-not-drop-*`);
5. `RunMarker` 是"哪个 run"的唯一产出者,日志与 `BattleSession.RunId/RunSeq/RunGap/RunPrevWhy/RunPrevResult` 都从它取(`a-first-session-is-run-1` …)。

容器**不含**任何时钟读取:调用方算好 `runGap`/`gap` 再传进来(与 RF3 策略层同一条规则)。也因此它可以被离线驱动。

## 3. 矩阵发现的死状态(本批删除)

| 证据 | 内容 |
|---|---|
| 唯一写入点 | `Aggregator.Attribution.NoteCalcActivity`(`_lastPow = CompositionProbe.Power(calc)`、`_lastCompT = ...`) |
| 读取点 | **`src` 内 0 处**(`grep _lastPow\|_lastCompT` 只命中定义与这两行写入) |
| 为什么容易误判 | `Diagnostics/PowerProbe.cs` 有一对**同名**字段(53/88/162),那对是活的 |
| 为什么删得安全 | 唯一被调用的 `CompositionProbe.Power` 是纯读(只解密 `m_power`,已读实现确认无计数/无日志) |
| 收益 | 每击一次的路径上少两次写入 + 一次纯读 |

## 4. 这一轮新增的覆盖

方案 RF1 的"生命周期"行(Start→Damage→End;idle→resume;真实 End 后迟到事件;restart;teardown;F9;连续 A/B 场)
在 RF4 之前**一行都没有离线覆盖**——状态在 IL2CPP 绑定的门面里。现在:

| 组 | 内容 |
|---|---|
| `runtime/continuity-run-marker` | 首场标记、真实结束切断 run、idle 续 run、不同任务切 run、连续碎片序号 |
| `runtime/continuity-resume` | 闸门四态(无语料/合格/过期/非 idle)、取走即遗忘、窗口边界、**未知单位保留**、F9 不清 |
| `runtime/continuity-consecutive-battles` | A/B 两场真实战斗 + 三个竞技场碎片的一个完整序列 |
| `policy/idle-rule`(RF3b) | 空闲关闭规则:暂停永不关闭、无事件不关闭、阈值边界 ±1ulp |

用例总数 **320**(RF3 后 269);变异负控 **26**(RF3 后 20),新增 6 例指向 `Runtime/` 与空闲规则。

## 5. 验证与回滚

| 项 | 值 |
|---|---|
| 构建 | 0 警 0 错;389,632 B,**未部署** |
| 行为套件 | `cases=320 failed=0 pinned=320` |
| 变异负控 | `0 failure(s) of 26` |
| 守卫 | `refactor_final_check` 89 个 .cs blocks=0 + 自测 PASS;文档收敛 0/11;docs123 36 份 0 损伤;`repo_manifest --verify` drift=0 |
| 验收 | `acceptance_rf4`(见该目录 `RESULTS.md`) |
| 回滚 | `git revert` 本轮提交即可:`SessionContinuity` 是新增文件,门面改动是"字段取值替换为容器取值",没有数据库/文件格式或外部契约变化;部署 DLL 从未被替换 |

## 6. 未做 / 下一族的前置

| 族 | 前置(缺一不可) |
|---|---|
| 预登记全局规则(`_globalRules`) | 一条"下一场单位**先**注册、上一场**后**结算"的时序用例 —— 需要能把注册路径驱动成事件;目前注册走 IL2CPP 侧 |
| 单场运行态(当场计数/时钟游标) | 先把读点收敛到少数几处(RF3 已拿走判据),否则迁移会碰每击一次的代码 |
| 单击 / 攻击快照(`_activeCalc`/`_calcEvents`) | 一条"攻击开始 → 伤害 → 清理"的用例;当前只能靠差异审查 + 实机 |
| 进程级 | 低收益(它们本就不该按场销毁),只做"显式化所有者" |
| 展示级(RF5) | 缓存语义的 ADR:严格节流?最终快照?两条既有行为已被用例钉住,改动即需重新定义预期 |
| composition 链自身窗口(RF3b 遗留) | 需要先定义它与 `AttributionPolicy` 的关系(两套配对系统) |

**仍然没有离线覆盖的东西**:门面的 IO、导出、日志、探针编排,以及一切原生读取路径。这一批证明的是"**跨场状态机的判据与转移**正确",
不是"调用点接线正确"——后者仍靠差异审查 + 构建 + 真实导出回归 + 实机。
