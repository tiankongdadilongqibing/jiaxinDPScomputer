# 重构批次记录 RF3(第 2 轮):纯判据下沉与协调器边界

> 依据:[REFACTOR-PLAN-POST-1.7.11.md](<REFACTOR-PLAN-POST-1.7.11.md>) §8(RF3)。前置:RF0–RF2 见
> [REFACTOR-BATCH-RF0-RF2.md](<REFACTOR-BATCH-RF0-RF2.md>)。

## 0. 结论

| 项 | 状态 |
|---|---|
| RF3 时钟增量策略 | ✅ `Policy/BattleClockPolicy.cs`(纯,含 5 个决策) |
| RF3 run 归组判据 | ✅ `Policy/SessionTransitionPolicy.cs`(分组 + 标记推进 + 软恢复闸门) |
| RF3 归属候选判据与时间窗 | ✅ `Policy/AttributionPolicy.cs`(配对窗口 / 存活配对准入 / 伤害匹配 / 理由码 / 来源优先);**候选扫描本身仍在门面** |
| RF3 composition 链自身的窗口 | ❌ 未做(属 `CompositionProbe*`,另列 RF3b) |
| 行为证据 | 269 用例(含 7 组"旧式表达式"网格对照)+ **20 例变异负控**全 PASS |
| **部署 DLL** | **仍未改**:1.7.11 / 387,072 B / `36EC96D4…`;新产物 389,120 B 只在 `src\bin\Release` |

## 1. 抽了什么、原来在哪

| 新位置 | 拥有什么 | 原来在哪(RF3 前) |
|---|---|---|
| `BattleClockPolicy.ResolveSource` | 时钟来源:配置 → 别名归一 → legacy bool → `game` | **两处重复**:`FrameDelta` 与 `ClockSourceName` 各写一遍 |
| `BattleClockPolicy.ResolveUnitsPerGameSecond` | 每秒游戏单位:配置 → 实测技能数据 → 30.0 | `GameUnitsPerSecond` 内联 |
| `BattleClockPolicy.ClampFrameDelta` | 负增量归零 + 停顿上限 | `Tick` 内两行 `if` |
| `BattleClockPolicy.RealDelta` | 首帧不收费、之后为差值 | `FrameDelta` 的 real 分支 |
| `BattleClockPolicy.GameDelta` | 首观测 0、不前进 0、**倒退也 0** | `FrameDelta` 的 game 分支 |
| `SessionTransitionPolicy.RunContinues` / `NextRun` | "同一段连续游玩"的判据 + 标记推进 | `StartSession` 内联 6 行 |
| `SessionTransitionPolicy.ClosedSessionGate` | 软恢复闸门**顺序**与"只有窗口过期才遗忘" | `TryResumeClosedSession` 内联 |
| `AttributionPolicy.LiveAgeEligible` / `LiveTargetAdmitted` | 存活 calc 的两段准入(-0.05/0.20 + 0.08 盲配) | `TryGetCompForVictim` 内联 |
| `AttributionPolicy.DmgMatches` | "候选值等于命中值或游戏口径" | **两处重复**:配对扫描 与 `CalcValueMatches` |
| `AttributionPolicy.PairKind` / `PairLabel` | 五个配对理由码 → 导出/UI 字符串 | 四处分散的字符串字面量 |
| `AttributionPolicy.CalcSourcePriority` / `CalcSourceTag` | 显式攻击者 → 召唤主 → 未知,及 `src=` 标签 | `TryResolveCalcSource` 内联 |
| 窗口常量 0.80 / 0.60 / 0.45 / 0.20 / 0.08 / -0.05 | 全部有了名字,且**同一概念只有一个定义** | 散在 `Aggregator.Attribution` / `Aggregator.Stats` 的字面量 |
| `BattleSession.MaxPending` | pending 上限 | `RecordHitDetail` 里第二份 `2048` |

**四处重复消失**:来源规则 ×2、伤害匹配条件 ×2、存活窗口 ×2(归属与 Stats 的"同一个窗口"此前只是注释里的声明)、pending 上限 ×2。

## 2. 为什么这是"行为保持"的

| 手段 | 说明 |
|---|---|
| 表达式原样搬 | 每个判据的布尔结构、比较符号、短路顺序都与搬运前一致;**原生读取留在门面** |
| 副作用顺序保留 | `Used = true` 的时机、候选扫描方向(精确=新→旧并 `break`;FIFO=旧→新)、`_lastCalcSrc` 的赋值,都仍在原来的位置;`ClosedSessionGate` 把"只有窗口过期才 `_lastClosed = null`"写成唯一的副作用 |
| 原生读取不提前 | 存活配对里 `live.m_blocker` 仍在窗口判断**之内**;`_lastClosed` 的 actor 检查仍在两道闸门**之后**(且 A 为空时才读 O) |
| 网格对照 | 7 组网格把"搬运前表达式"与"策略函数"逐点比较:来源解析(10 配置 × 2 legacy)、单位解析(5×5)、钳制(8 个含 ±1ulp)、run 合并(2×2×2×6)、存活年龄/目标(9 年龄 × 2)、伤害匹配(5×3×3)。**267 个点全部一致** |
| 阈值单独钉 | 每个阈值常量都有独立用例(改常量会让它红);边界用例则用**字面量**阈值——这一点是负控驱动器替我发现的:第一版边界用例把常量当阈值传入,于是改常量它们**不会红**,红灯出现在错误的用例上 |

> 网格对照里的"旧式表达式"是从 `8f3aafd` 源码**逐字抄进测试**的,不是生产代码。它证明的是"搬运没有改变参数化",不是"策略正确";策略本身由边界用例与真实导出回归负责。

## 3. 覆盖率的变化

| 项 | RF1 后 | RF3 后 |
|---|---|---|
| 命名用例 | 174 | **269** |
| 变异负控 | 10 | **20**(新增 10 例全部指向策略文件) |
| 可离线执行的生产源码 | 纯模型 + `BattleSession` + 缓存 | **+ 3 个策略文件(270 行)**,并由测试工程的 `<Compile>` 清单强制"必须无 Unity/IL2CPP/Plugin 依赖" |

可以离线执行的**判据**因此从"命中配对窗口的 0.35 s"扩展到:时钟来源与钳制、run 合并与标记推进、软恢复闸门、存活配对两段准入、伤害匹配、理由码、来源优先。

## 4. 验证与回滚

| 项 | 值 |
|---|---|
| 构建 | 0 警 0 错;产物 389,120 B(旧 387,072 B)**未部署** |
| 行为套件 | `cases=269 failed=0 pinned=269` |
| 变异负控 | `0 failure(s) of 20` |
| 源码/编码守卫 | `refactor_final_check` 87 个 .cs,blocks=0;`check_docs_123` 34 份,0 损伤 |
| 基线清单 | `repo_manifest --verify` drift=0 |
| 验收 | `acceptance_rf3`(见该目录 `RESULTS.md`) |
| 回滚 | `git checkout` 上一个提交;部署 DLL 从未被替换;策略文件是纯新增,删除它们即可回到内联形态 |

## 5. 未做 / 未覆盖(诚实清单)

1. **候选扫描没有下沉**:`_calcEvents` 的遍历、`Used` 标记、`GameRef.Same` 比较仍在 `Aggregator.Attribution`。原因是它们要么读原生对象、要么是热路径(每击一次,一场约 5k 次);为了不引入每击分配或不必要的原生调用,本轮只把**判据**下沉。要真正让配对端到端可离线测,需要给扫描一个"候选事实投影"并接受一次分配(或一个复用缓冲),这是**另一个决定**,不在本轮。
2. **composition 链自己的窗口**未动:判定核心(`CompositionProbe*`)里另有 0.20/0.45 的配对窗口与 `-0.05` 容忍,属另一套配对系统;它们与策略层的关系需要在 RF3b 里单独定义。
3. **`IdleSeconds` 空闲关闭规则**未抽:它是"何时关门"的判据,和会话生命周期(RF4)绑在一起更自然。
4. **没有任何离线测试执行门面的运行时路径**(Start/End/软恢复/结算);策略层的用例证明"判据正确",**不证明调用点正确**。调用点的正确性仍靠:搬运时的差异审查、构建、以及真实导出/实机接受。
5. 未改:任何公式、口径、钩子 ABI、schema、UI。

## 6. 下一轮

* **RF3b(可选)**:composition 链窗口与 `IdleSeconds`;若要做"候选投影",先测一次每击分配的成本(预算普查已在盯丢步/溢出,可加一个分配计数)。
* **RF4**:先交 `StateLifetimeMatrix`(owner/创建/重置/软恢复/释放/被谁读),再逐个状态族迁移;`ResetActors` 保留时钟、软恢复续钟这两条已有用例保护。
* **RF5**:展示层与缓存;`_cacheUsedFolds` 在 live 下不可达、reset 回填同样事件数返回旧缓存 —— 两条已被用例钉住,改之前先重新定义预期。
* **RF7**:工具归档(只索引、不移动、不删除历史证据)。
