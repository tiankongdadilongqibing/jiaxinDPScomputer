# 状态生命周期矩阵(RF4 第 1 步)

> 依据:[REFACTOR-PLAN-POST-1.7.11.md](<REFACTOR-PLAN-POST-1.7.11.md>) §9(RF4)。该节要求**先交付 StateLifetimeMatrix**,
> 逐字段说明 owner / 创建 / 重置 / 软恢复 / 最终释放 / 被谁读取,再决定归属 —— 而不是先把静态字段塞进一个按局销毁的容器。
> 本文件的表由源码逐个字段的"写入点 × 读取点"得出(见 §1 的方法),不是设计意图的复述。

## 1. 怎么得出这张表

1. 枚举 `src` 下全部**可变静态字段**(不含 `const` 与 `readonly` 常量表);
2. 对每个字段列出**全部写入点**与**全部读取点**(`grep` 该标识符,再逐点看上下文);
3. 以"哪些路径会把它清零/替换"为界,划分到五类生命周期之一;
4. 用 `StartSession` / `ResetCurrent`(F9)/ `FinalizeLocked` / `TryResumeClosedSession` 四个入口交叉验证;
5. 任何"只在写入点出现、没有任何读取点"的字段,单独列为**死状态**(见 §6-1)。

## 2. 五类生命周期(方案 §9 的表)+ 本批状态

| 生命周期 | 应包含的状态 | 迁移约束 | 本批状态 |
|---|---|---|---|
| 单击 / 攻击快照 | 状态、耐久、构成上下文 | 攻击开始采样与 `finally` 清理保持 | ⏳ 未迁移(`_activeCalc` / `_calcEvents` / `_lastCalcSrc` / Crit 探针) |
| 单场运行态 | 配对队列、当场计数、时钟游标 | 必须同时考虑 reset / soft close / resume,不只 Start/End | ⏳ 未迁移(见 §3,已列出全部字段) |
| **跨场衔接** | 最近关闭会话、run 标记、预登记全局规则 | **不按 End 一次性清空**;分别定义所有权 | ✅ "最近关闭 + run 标记 + 上次结束"已迁到 `Runtime/SessionContinuity.cs`(RF4a);✅ 预登记规则的**判据半**已抽到 `Policy/GlobalRuleClassifier.cs`(RF4b,含登记跳过/阀门规则);⏳ 注册表**状态**仍在 `CompositionProbe._globalRules` |
| 进程级 | 稳定主数据、明确累计计数、历史容器 | 不强行挂在每场对象上 | ⏳ 未迁移(已确认它们**不该**按场销毁) |
| 展示级 | 当前视图、缓存、筛选状态 | 不反向改变统计模型 | ⏳ 未迁移(RF5:`ContributionSession` 缓存 + `OverlayUGUI*`) |

## 3. `Aggregator` 门面的字段(逐字段)

| 字段 | 生命周期 | 创建 / 写入 | 重置 | 软恢复时 | 最终释放 | 读者 |
|---|---|---|---|---|---|---|
| `Session` | 单场运行态(**公开**) | `StartSession` / `TryResumeClosedSession` | `EndSession`、空闲关闭、teardown → `null` | 复用**同一个对象**(续钟) | 结算后被置空(或被 resume 复用) | 全插件:UI、导出、贡献、探针 |
| `History` | 进程级 | `FinalizeLocked` 追加 | 从不 | `RemoveAll(session)` 撤下自己那条摘要 | 进程结束 | UI 历史列表 / 上一场视图 |
| `_lastSummaryLog`、`_lastTimeLog` | 单场运行态 | `Tick`(节流) | `StartSession` = 0 | 不重置 | — | `Tick` 的日志节流 |
| `_gameTimeAtStart` | 单场运行态 | `StartSession` | `StartSession` | 不重置 | — | `[TIME]` 日志 |
| `_eventCount` | 单场运行态 | 伤害/治疗记录 | `StartSession` = 0、`ResetCurrent`(F9)= 0 | 不重置 | — | 空闲规则、`Tick` 心跳、`Stats` |
| `AbsorbedTotal`、`AbsorbedHits` | 单场运行态 | `RecordDamage`(吸收) | `StartSession` = 0 | 不重置 | — | 结算 / 导出 |
| `_lastTickFrame` | **进程级**(每帧闸门) | `Tick` | 从不 | — | — | `Tick`(同帧去重) |
| `Clock`(`Stopwatch`) | **进程级** | 静态初始化 | 从不 | — | — | `FrameDelta` 的 real 分支 |
| `_lastTickClock` | **进程级**(real 时钟游标) | `FrameDelta` | 从不;用 `-1.0` 表达"还没有上一帧" | — | — | `FrameDelta` 的 real 分支 |
| `_lastGameSteps`、`_hasLastSteps` | **进程级**(game 时钟游标) | `FrameDelta` | 从不 | — | — | `FrameDelta` 的 game 分支 |
| `_lastGsPointer` | **进程级**(实例识别) | `Tick` | teardown = 0 | — | — | `Tick` 判断"新的一场" |
| `_activeCalc`、`_activeCalcT` | 单击 / 攻击快照 | `NoteActiveCalc` | `StartSession`、软恢复 | **清空**(绝不允许跨场) | — | `TryGetCompForVictim`、`Stats` 的技能回退 |
| `_calcEvents`(cap `CalcEventMax=64`) | 单场运行态 | `NoteCalcActivity`(FIFO 淘汰) | `StartSession`、软恢复、`FinalizeLocked` | **清空** | — | 归属配对、`CalcValueMatches` |
| `_lastCalcSrc` | 单击 / 攻击快照 | `TryResolveCalcSource` | 该方法自身在无匹配时清空 | — | — | `RecordDamage` 的日志行 |
| `HitDetailProduced/Trimmed/Errors`、`HitMatchExact/Pair/None` | 单场运行态 | `NoteHitDetail` / `RecordHitDetail` / `RecordDamage` | `StartSession` = 0 | 不重置 | — | 导出自报(`hitDetail.*`) |
| **`Continuity`**(RF4a 新增) | **跨场衔接** | `RememberEnd` / `RememberClosed` / `BeginSession` | **没有任何路径重置它**(F9 不清、StartSession 不清) | `Gate` 只在**窗口过期**时遗忘;`ForgetClosed` 在取走时遗忘 | 进程结束 | `StartSession`(run 标记)、`TryResumeClosedSession` |

## 4. 本批迁移的族:跨场衔接

| 迁移前 | 迁移后 |
|---|---|
| `Aggregator` 的 10 个静态字段:`_lastClosed` / `_lastClosedWall` / `_lastClosedWhy` / `_runId` / `_runSeq` / `_lastEndWall` / `_hasEnded` / `_lastEndQuest` / `_lastEndResult` / `_lastEndWhy` | 一个 `Runtime/SessionContinuity` 实例(`Aggregator.Continuity`),字段同名同义 |
| 写入点分散在三处:`StartSession`(读+推标记)、`FinalizeLocked` **开头**(记住结束)、`FinalizeLocked` **结尾**(记住关闭) | 同一组写入点,但状态与转换集中在一处:容器方法 |

**保住的顺序与不变式**(全部有对应用例,见 §5):

1. `RememberEnd` 仍在**结算开头**,`RememberClosed` 仍在**结算末尾(导出之后)** —— 迟到事件不能加入一场导出还没写完的会话;
2. **只有窗口过期会遗忘**最近关闭会话;真实结束(`end`)与未知单位都**保留**它,以便稍后的事件仍能复归;
3. 已结束的真实战斗(`result != 0`)**永远不与下一场合并**;同任务、无结果、2.0 s 内才继续同一 run;
4. `F9`(`ResetCurrent`)只重置**当局**的演员表与事件,不触碰跨场衔接状态;
5. 窗口比较是 `>`,`2.0 s` 恰好仍算继续、`5.0 s` 恰好仍可复归(边界有 ±1ulp 用例)。

## 5. 覆盖:这批新增的用例(全部离线执行生产代码)

| 场景(方案 RF1 的"生命周期"行) | 用例组 |
|---|---|
| Start → End → Start,真实结束不续 run | `runtime/continuity-run-marker` |
| 空闲关闭 → 同一 run 的连续碎片(run/序号/gap/上一次原因) | `runtime/continuity-run-marker` |
| 空闲 → 复归(闸门合格、取走后遗忘、不能再取) | `runtime/continuity-resume` |
| 窗口过期遗忘 / 真实结束保留 / **未知单位保留** | `runtime/continuity-resume` |
| F9 手动重置不清跨场状态 | `runtime/continuity-resume` |
| 连续 A/B 两场 + 三个竞技场碎片 | `runtime/continuity-consecutive-battles` |
| 空闲关闭规则(暂停永不关闭、无事件不关闭、阈值边界) | `policy/idle-rule` |

加上 RF1/RF3 已有的 `session/`(键分配、使魔合并、`ResetActors` **保留时钟**、事件上限、`FromSummary`)与
`cache/`(展示级缓存)。**仍无覆盖**:门面的 IO 与编排(见 §7)。

## 6. 矩阵暴露出来的问题

### 6-1 死状态(本批删除)

`Aggregator._lastCompT` 与 `Aggregator._lastPow`:**只在 `NoteCalcActivity` 被写入,`src` 内没有任何读取点**。
它们的赋值发生在**每击一次**的路径上,其中一次还调用 `CompositionProbe.Power`(该函数本身是纯读,已确认无副作用)。
是 `PowerProbe` 自己有一对**同名**字段(`Diagnostics/PowerProbe.cs:53/88/162`,那对是活的),所以肉眼容易误判 ——
矩阵的"读取点"一列正是为此。

### 6-2 跨场状态却没有跨场生命周期(第二个衔接族,未迁移)

`CompositionProbe._globalRules` / `_ruleOwnerName` 是**故意不随场清空**的(注释记录:下一场的单位在上一场结算**之前**就注册好了,
清空会丢规则)。它们与 `Continuity` 属于同一类"衔接"状态。

**RF4b 进展**:判定半已经搬走并可以离线执行了 ——`Policy/GlobalRuleClassifier.cs` 拥有
"哪个分句算战场级规则 / 是什么形状"(11 项字段)、"这个指针是否已经扫描过"(含指针复用检测)、以及阀门两级阈值。
登记回路里剩下的只有原生读取(能力列表、文本、指针)与记录的身份字段。
**仍未做**:注册表**状态**的所有权(它仍散在判定核心里,且没有 reset 入口),以及"注册发生在结算之前"的时序用例 ——
需要能把注册路径驱动成事件,而驱动它需要 IL2CPP 侧的对象。

### 6-3 每次调用用 static 暂存

`Hooks/BattleObjectHooks.BeforeLife`(`Dictionary<BattleObject,int>`):Prefix 写入、Postfix 读取并删除。
同一实例的 Prefix/Postfix 总是成对,所以当前正确;但它是"以 static 充当调用栈"的写法,重入(嵌套调用同一被补丁方法)会互相覆盖。
属观察项,本轮不改。

### 6-4 三类缓存都是展示级,且都有自己的失效规则

`ContributionSession`(7 字段,四路 OR 失效)、`OverlayUGUI` 的 `_prevSummary*` 三件套、`OverlayChart.UsePerSecond`。
它们**不能**塞进按局销毁的容器(切换视图/查看上一场都要读它们),属 RF5;两条已被用例钉住的既有行为(见 RF3 记录)在改动前必须重新定义预期。

## 7. 其余族的迁移顺序与前置

| 顺序 | 族 | 为什么这个顺序 | 迁移前置 |
|---|---|---|---|
| 1 | **跨场衔接** | 冷路径、状态少、判据已在 RF3 抽成纯函数 | ✅ 已完成(本批) |
| 2 | 预登记全局规则(`_globalRules`) | 与 1 同为衔接状态,但涉及"注册早于结算"的时序 | **判据半已完成**(RF4b:`GlobalRuleClassifier`,58 个用例 / 8 例负控);剩"状态所有权 + 时序用例":需要能把注册路径驱动成事件 |
| 3 | 单场运行态 | 热路径,字段多;迁移会碰每击一次的代码 | 先把"当场计数"的**读点**收敛(RF3 已把判据拿走),再考虑 `BattleSession` 承载 |
| 4 | 单击 / 攻击快照 | 与攻击开始/`finally` 清理耦合 | 需要一条"攻击开始→伤害→清理"的用例;当前只能靠差异审查 |
| 5 | 进程级 | 它们**本来就不该**按场销毁,迁移只是"显式化所有者" | 低收益,可延后 |
| 6 | 展示级(RF5) | 缓存语义要先决策(严格节流?最终快照?) | 先写 ADR,再改行为,再补负控 |

**结论**:本轮完成 1;2、3、4 的**前置**分别是"时序用例""读点收敛""攻击快照用例"——在这些用例存在之前迁移它们,
就是方案 §9 明确反对的"先动结构、再想所有权"。
