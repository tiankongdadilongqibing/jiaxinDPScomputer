# REFACTOR-BATCH-R74 —— 校准仪的**读数单位**:初动改读 `m_firstCoolTimeFrame`,拒绝理由**具名**,并把技能时间线放进证据包

## 0. 一句话

R71 算对了 +0.90 s、R72 把「何时决定」也改对了,但**测量仪一次也没返回过数据**:它把 `Skill.FirstCoolTime`(**秒**)
当成**单位**去和 `WaitCountFrame` 比较,于是每个槽、每一帧都被 `waitFrames > firstCoolFrames` 拒掉 ⇒ `samples` 恒 0
⇒ 原点永不 decided ⇒ **`[CLOCK] origin=none` 是每一场的必然结果**。本轮把初动改成读**单位制**的
`Skill.m_data.m_firstCoolTimeFrame`(秒值降级为记录在案的退路),把每条拒绝理由命名,加一行 `[CLOCK] calib` 让
「没样本」不再沉默;顺带把技能时间线写进证据包 —— R73 诊断「邪龍の息吹 5 s vs 初动 6 s」时,唯一的一手材料是**会滚动覆盖**的运行日志。

## 1. R73 的证据(为什么非改不可)

用户第三场(quest 9999,2026-10-06 18:55:42 → 18:56:12)仍然「初动 6 秒的技能 5 秒就显示放出来了」。
一手证据与推理链:

* `[CLOCK] origin=none reason=window samples=0 active=2.0s hits=18 held=18`(`dpsmeter_runtime.log:649`),
  全场**没有**任何更早的 `[CLOCK] origin=` 行;`[SKILLTL] panel ... 邪龍の息吹 5.0 16.2 26.0 37.2`(`:4270`),
  原始发动机戳 `active=5.00s`(`:744`);主表 `auto_skill` id=42 `maxFirstCoolTime=180`(L3)= **6.00 s**。
* `samples=` 打印的是 **`s.ClockOriginSamples`** —— 一个**跨帧保留的最大值**(`Aggregator.Clock.cs:281`/`:339`/`:349`)
  ⇒ `samples=0` 等价于「窗口内**没有任何一帧**拿到过哪怕 1 个样本」,不是「决定帧恰好为 0」。
* 分支顺序 `applied → window → hold → events → range`(`Aggregator.Clock.cs:283-320`)。扣住期间 `Rt.EventCount`
  恒为 0(`Aggregator.Stats.cs:78` 在 `:87` 之前),所以窗口内**不可能**走 `events`;既然没有再早的 `applied` 行
  ⇒ 窗口内 `measured` 恒 false。
* `TryLag` 的五条拒因里,`units<=1`(`units=30.0`)、`active>2.0`(窗口内不成立)、「名单为空」(同一帧
  `Observe` 用同一个 `CollectParty` 打出了 7 个我方单位)全部排除 ⇒ 只剩 `firstCoolFrames<=0` 或
  `waitFrames>firstCoolFrames`。
* **反证**:按**单位**算,7 个槽的首采样 `wait`(60/150/120/270/150/150/2940)全部 ≤ 各自主表初动
  (90/180/150/300/180/180/2970)⇒ `samples` 会是 7、当场 `applied`。这与日志矛盾 ⇒ **读到的不是单位值**。
* **机制**(interop 元数据):`get_FirstCoolTimeFrame` 出现 **0** 次;`m_firstCoolTimeFrame` 只在**主数据**成员
  块里(`m_coolTimeFrame`/`SetCoolTimeFrame`/`CalcFirstCoolTimeFrame`/`GetFirstCoolTimeFrame` 同一块);而插件**自己的**
  换算证明运行时 `CoolTime` 是**秒**(`Skill.CoolTimeFrame / Skill.CoolTime` = 240/8 = 30)。按秒值
  3/5/6/6/6/10/99 对比 `wait` 60/120/150/150/150/270/2940 ⇒ **7/7 被拒**,与打印的 `samples=0` 完全一致。

## 2. 改了什么

### 2.1 读数单位(步骤 1 + 2)

* 新纯策略 `BattleClockCalibrationPolicy.FirstCoolFrames(firstCoolSeconds, firstCoolTimeFrame, unitsPerSecond,
  out usedFallback)`:单位字段 `> 0` 时**优先**用它;否则退路 `seconds × unitsPerSecond`(四舍五入到最近的单位,
  因为秒值本身是一次除法还原);两条都不行 → `0`。
* **R74 对 R73 推断的一处更正**:`Skill.m_firstCoolTimeFrame` **不存在**(编译器:`CS1061`)。R73 从字符串堆的
  相邻性推出「与 `m_coolTimeFrame` 同块」是对的,但**块属于主数据类**,可达路径是
  `Skill.m_data.m_firstCoolTimeFrame` —— 已按此实现(该表达式编译通过),`Skill` 本身没有这个名字。
* `Diagnostics/AutoSkillProbe.AddClockLagSample`:同一槽**同时**读两个值,交给上面的策略,并把「用了哪条路」记进诊断。
* `[AUTOSK] chg` 行新增 `first=`(秒属性)与 `firstFrame=`(单位字段),配两个新读者
  `ReadFirstCoolSeconds` / `ReadFirstCoolFrame`(读不到 = `int.MinValue`,**绝不静默 0**)。
  这一行是「6 还是 0」「初始值还是实时值」在同一场里一并定案的现场证据。

### 2.2 拒绝理由具名(步骤 3a)

* 新枚举 `BattleClockCalibrationPolicy.LagReason`(`Usable` / `NoFirstCool` / `WaitOutOfRange` / `BadUnits` /
  `OutOfWindow` / `LagOutOfRange`);`Reject(...)` 持原判断顺序与**逐字不变**的原文,`TryLag(...)` 变成它的薄包装
  ⇒ 既有调用点与既有用例的读法一字不改,而那条**把秒当单位**的缺陷从此会自己说出名字。

### 2.3 `[CLOCK] calib` 行(步骤 3b)

* 新纯文件 `Policy/ClockLagDiagnostics.cs`:每次尝试累加
  `attempts / party / slots / usable / via(field/fallback) / first(sec/frame/wait) / rejected(noFirst/wait/units/window/range)`。
  `int.MinValue` 打印成 `?`(「读不到」和「读到 0」必须是两个答案)。
* `AutoSkillProbe.Calib` 每场清空(`Reset()`);`Aggregator.DecideClockOrigin` 在 `[CLOCK] origin=…` 之后打印一行。
* **为什么必须加**:决定帧**必然**过不了窗口条款(`TryAlignClockOrigin` 只在 `ActiveSeconds > WindowSeconds` 才决定),
  所以 `samples=0` 在那条路径上天然为 0 —— "没有一个槽能回答"和"窗口先关"长得一模一样,这正是 R71/R72 两轮
  都说不清原因的地方。

### 2.4 技能时间线进证据包(步骤 3c)

* `Diagnostics/EvidenceExtractor`:`SkillTimelineProbe.Summary()`(与页面、与战末 `[SKILLTL]` 行**同一批字符串**)
  写成包内 `skill_timeline.txt`,并在上一步列入 `manifest.json`(名字/字节/校验),所以 `extract_verify.py` 会一并校验它。
  探针关掉时不出该文件,manifest 的 `notes` 说明「文件缺席 ≠ 没有发动」。
* **不进 `battle.json`**:导出形状是契约(`contribution.schemaVersion` 仍 1.2),本轮不动;时间线是渲染出来的表,
  走 `WriteChecked` 的 JSON 校验只会被判「非法 JSON」。

## 3. 验证

| 闸门 | 结果 |
|---|---|
| 构建 | 0 警告 0 错误(`m_firstCoolTimeFrame` 的**主路线**成立:先试 `Skill.m_firstCoolTimeFrame` 得 CS1061,改 `Skill.m_data.m_firstCoolTimeFrame` 通过) |
| 行为套件 | **1275 用例 / 0 失败**(钉住 1254 → 1275;新增 `policy/battle-clock-calibration` 15 例 + 新组 `policy/clock-lag-diagnostics` 6 例) |
| 变异负控 | **211 例 / 0 失败**(新增 8 条 + 重锚 1 条;逐条确认红在**具名用例**,且变异体仍能编译) |
| 验收 | **`acceptance_r74` = 44 条命令 / 76 条检查 / 0 项不一致**。第一次跑出 **1** 项(`repo_manifest_verify`:本轮改了 20 多个被清单跟踪的文件而清单还没重写)⇒ 重写清单后重跑得到 **76 ok / 0 mismatched**。注:第一次跑还把 `doc_convergence` 撞出一条 R6 —— 那是**我把 `--out` 写成仓库根相对路径**、子命令以 `cwd=_dpsm_work` 运行导致 `applicability.json` 写失败(`FileNotFoundError`)的连带损坏,改用绝对路径后消失;与产品代码无关,记在这里免得下次再踩 |
| 静态守卫 | 文档收敛 **12/12**(R1–R12 全绿,含 R5 的现状句刷新)、docs123 **107 文件 / 0 损伤**、`repo_manifest --verify` **drift=0 / changed=1(自身)**、`refactor_final_check` blocks=0、工具注册表 PASS |

## 4. 覆盖不到的和仍未定案的

1. **`m_firstCoolTimeFrame` 的语义未定案**(初始/目标值 vs 实时剩余)。本轮**不写死任何一种**:按原样喂策略
   (若它是实时剩余,`first - wait ≈ 0` ⇒ 算出的 lag ≈ −active,会被既有边界拒),两个值同时打印 ⇒ 下一场一并定案。
2. **实机未验证**。"原点真的 applied"必须由下一场的 `[CLOCK] origin=+0.9Xs …` +
   `[CLOCK] calib attempts=… usable=7 via(field/fallback)=7/0 first(sec/frame/wait)=6/180/150 …` 证明。
3. **退路若在跑**要看清楚:`via(field/fallback)=0/N` 表示字段没读到,那时若 `first=` 也是 0,则两条路都断,
   需要改读游戏自己的 `GetFirstCoolTimeFrame(id, param)` 方法(本轮不实现:它是方法调用,不在本次结论范围内)。
4. **页面刻度仍是 F1 四舍五入** ⇒ 修正生效后那一格应显示 **5.9**(不是 6.0);残差 0.07 s = 1/30 s 帧量化 +
   该槽在计数器未充满时就被执行。
5. 与 R72 相同的实证缺口:**需要你打一场**。

## 5. 部署与验收

| 项 | 值 |
|---|---|
| 部署前 | `BepInEx/plugins/DpsMeter/DpsMeter.dll` = **`F83D369E…CAB49C6`**,486,912 B(1.7.23,R72) |
| 备份 | `_dpsm_work/deploy-backup/pre-r74-F83D369E/DpsMeter.dll`(同哈希同尺寸,已复核) |
| 部署后 | **`DF70F1A97F3A9E63BD596418570EDB8D0530F7B78273A427243929FFA05E1795`**,489,984 B(1.7.24) |
| 替换前置条件 | 替换前确认**无游戏进程**;替换时源码目录**没有任何 .cs 比 DLL 新** |
| 崩溃现场 | `DpsMeter-1.0.48-crash.bak` = `2E1819F2…`、`DpsMeter-1.0.49-crash.bak` = `F7FF1EB8…` **未变**(AGENTS §0.1) |
| **回退** | `Copy-Item _dpsm_work\deploy-backup\pre-r74-F83D369E\DpsMeter.dll BepInEx\plugins\DpsMeter\DpsMeter.dll -Force` |

本轮的算术部分(单位选择、拒因、诊断文本)由 1275 例 + 211 条变异**执行**验证;顺序/现场部分
(`TryAlignClockOrigin` 的调用点、`[CLOCK] calib` 的位置、证据包新文件)只能由下一场与本轮验收覆盖。
证据包新增 `skill_timeline.txt` ⇒ 任何**旧包**不含它、新包含它;`extract_verify.py` 对两者都通过
(它只要求 manifest 列出的文件都在且校验一致)。
