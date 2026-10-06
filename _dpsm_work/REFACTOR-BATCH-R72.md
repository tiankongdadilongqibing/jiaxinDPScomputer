# REFACTOR-BATCH-R72 —— 让原点的**修正真正落地**:开局事件在原点决定前**先扣住、再按到达时刻重放**

日期:2026-10-06。插件 **1.7.22 -> 1.7.23**;`F83D369E4C101A02C522027414B620CCC8FE4A28F9C7703BEBA6CE5A0CAB49C6`(486,912 B)。

## 0. 一句话

R71 算对了 lag(+0.90 s)**却一次也没应用过**:它只在「本场第 2 帧及以后」尝试对齐,而游戏在创建 session 的**同一帧**就把开局伤害打了出去,于是两次尝试都撞在 `reason=events` 上。R72 把两件事改掉:①**在创建 session 的当场**就尝试对齐(那时本场还没有任何事件);②万一证据来晚了,开局的伤害/治疗**先扣住**(有界、计数),等原点定案后用**它们到达时的时钟 + 决定的 lag** 重新走一遍记录器 —— 所有派生值(导出 `events[].t`、actors 首/末次命中、按秒桶、伤害曲线、pending 命中配对、状态反应窗口)**天生就是对的**,不需要事后改任何一处。

## 1. 证据:两次 `origin=none` 的现场(用户新打的这场)

用户打完一场后贴图:「自动技能的发动时间统计得还是比初动时间早,**完全没有改变**」。运行日志(`BepInEx/config/dpsm_runtime.log`)两场连着给出原因:

```
[17:32:08.953] [DpsMeter] Battle session started (quest=9999)   ... run=#1.0
[17:32:09.074] [CLOCK] origin=none reason=events hits=5      <- 距 session 开始 0.121 s
[17:33:53.635] [DpsMeter] Battle session started (quest=411001) ... run=#2.0
[17:33:53.664] [CLOCK] origin=none reason=events hits=1      <- 距 session 开始 0.029 s
```

**而证据当时就在手上。** 第二场同一刻的 `[AUTOSK] chg` 行(`active=0.03s`)把它逐槽写了出来;把 §R71 的公式套上去,五个具名自动槽**全部**给出 0.903 s:

| 槽(quest 411001) | 线上 `wait/CTF` | 主表 `FirstCoolTime` | lag = (初动 − wait)/30 − active |
|---|---|---|---|
| 夢幻の連鎖破裂 | 92/180 | 120 | 0.903 s |
| 爆ぜる燭光 | 122/210 | 150 | 0.903 s |
| 影爪の強制裁断 | 92/390 | 120 | 0.903 s |
| 不浄な体液の噴出 | 212/300 | 240 | 0.903 s |
| 実験失敗！ | 2942/2970 | 2970 | 0.903 s |

而且这五个数与**页面上的实际发动时刻**自洽:计数器在 `active=0.03` 剩 92 单位 → 30 单位/秒 → 归零在 `active≈3.10`,页面该技能首行就是 **3.0**(不浄 212 → 7.07 / 页面 7.2;実験失敗 2942 → 98.07 + 0.9 的偏差由 R65 的「充满后等下一次普攻」解释,页面 99.1)。**结论:R71 的公式与测量都没错,错的是顺序。**

同时量到**两场都会走到这条分支**,不是偶然:9999 训练场是「session 建立后 0.12 s 内到 5 个事件、且探针还没读到待机列表」,411001 正式任务是「第 2 帧就有 1 个事件」。

## 2. 改了什么

### 2.1 在 session 创建处对齐(顺序修正)

`Aggregator.StartSession` 末尾新增一次 `TryAlignClockOrigin(val, battleSession)` —— 那时本场 `Rt.EventCount == 0`(计数器刚被 `Rt.OnSessionStart()` 清零)、`ActiveSeconds == 0`,所以 lag 就是「计数器已经走掉的那段」。`Tick` 里每帧的尝试**保留**(证据来晚时的兜底),但它的守卫再也不该被撞到。

### 2.2 原点未定案前,开局事件**先扣住**(新纯策略 `ClockOriginHoldPolicy`)

- `RecordDamage`/`RecordHeal`:参数先落进 `Rt.OriginHeld`(有界 **256**,带溢出标记),**不聚合**;
- 定案时(应用或拒绝)由 `Aggregator.FlushOriginHeld(lag)` 重放:把 `Session.ActiveSeconds/CombatSeconds` **临时设成该事件的到达时刻 + lag**,再走一遍记录器。于是 `events[].t`、actors 的 `FirstHitTime/LastHitTime`、`AddSecondDamage/Taken/Heal` 的按秒桶、`AddSample` 伤害曲线、`HitRecord` 配对窗口、`StatusDeltaProbe` 反应截止、`StateTimeline/FactStore` 的记录**全部一次写对**;
- **不是**「先按旧轴记下来再回头改」:那需要改到每一个派生面,漏一个就是一场两条时间轴 —— 正是本仓库最不想留的东西。按秒桶尤其:重放走的是同一个 `(int)秒` 规则(现在单点定义在 `ClockOriginHoldPolicy.SecondIndex`),所以桶边界也精确;
- 溢出**不是静默兜底**:达到上限即把 `OriginHoldOverflowed` 置位并**拒绝本次偏移**(`ShouldRebase` 新增第 4 个参数),理由写进 `[CLOCK] origin=none reason=hold`。一场两条时间轴,比原点晚 0.9 s 更糟;
- 扣住的事件**不会丢**:定案、终局(`FinalizeLocked` 开头)、F9(`Rt.OnManualReset`)三处都会清/放,F9 是**直接丢弃**(重置已经清空了 actors 与事件表,重放回去等于把用户要求忘掉的伤害复活)。

### 2.3 不走记录器的那几处时间戳,按同一个常数搬

一次 `+lag`,因为 lag 是常数偏移:时间表页面的 `SkillTimelineEvent.Active`(用户看的就是它)、充能采样器的 `SlotState.LastActive/PrevActActive`(否则决定后的第一个间隔会长 0.9 s)、诊断导出的 `StatusApplierProbe`/`GiveApplierProbe`/`ParamOwnerProbe` 行、以及 `AttackSnapshot.At`(否则重放的事件会拿旧快照的年龄去配对)。`SampleHp` 在原点未定案前**不采样**(它按秒索引,晚一两帧什么都不丢)。

### 2.4 可诊断性

`[CLOCK]` 行现在**每条分支都带同样的字段**:`origin=+0.90s|none reason=… samples=N active=… hits=N held=N (…)`。R71 的 `reason=events` 分支不打印 `samples=`,这正是这次要猜「是样本没到还是事件先到」的原因,现在不用猜了。页面那行「时刻起点 = 游戏自己的战斗开始」保持 R71 的写法。

## 3. 验证

| 闸门 | 结果 |
|---|---|
| 构建 | 0 警告 0 错误 |
| 行为套件 | **1254 用例 / 111 组 / 0 失败**(钉住 1205 -> 1254;新增 `policy/clock-origin-hold` 32 例、`runtime/clock-origin` 10 例、`runtime/clock-origin-counter` 6 例 + `policy/battle-clock-calibration` 里 1 例 hold-overflow 拒绝) |
| 变异负控 | **203 例 / 0 失败**(新增 14 条:决定后还扣 / 超过上限还扣 / 不报溢出 / 重放不带 lag / 桶四舍五入 / 接受负时钟 / 搬两倍 lag / 搬负 lag / 忘了 no-pause 钟 / 忘了 pending 伤害图 / 造出静默 / 跨场残留 / F9 不丢 / 忽略 hold-overflow;逐条确认红在**具名用例**) |
| 验收 | **`acceptance_r72`:76 条检查 / 0 项不一致**(44 条命令;`repo_manifest_verify` / `recon_probe` / `csharp_behavior` / `csharp_behavior_negctl` 全 0) |
| 静态守卫 | 文档收敛 **12/12**、docs123 **105 文件 / 0 损伤**、`repo_manifest --verify` **drift=0**、`refactor_final_check` blocks=0、工具注册表 PASS |

**离线自检**(R71 那三个现场,加 0.90 s 后):游戏倒计时的不变量 `now/30 + active` 恰好等于台面时限(9999 → 90.00/89.97/90.00,411001 ×2 → 119.97/120.00/120.03)。R72 不改变这个自检,改的是它**能不能被应用**。

## 4. 覆盖不到的

- **实机确认要等下一场**(这是本轮唯一的实证缺口):`[CLOCK]` 行应当出现 `origin=+0.9Xs … held=N`,且 `[CLOCKP]` 的 `now/30 + active` 等于台面时限;页面 `夢幻の連鎖破裂` 那类技能的首行应**不再是 3.0** 而是它的初动附近(≈3.9-4.0);
- 行为套件**编译不到 facade**:「在 session 创建处尝试」这件事本身(`Aggregator.Session/Clock/Stats.cs` 引用 Unity/IL2CPP)没有具名用例,它的证据是下一场的日志与验收;可执行的那一半(扣住规则、重放时刻、桶规则、搬常数、容器的生命周期)已全部落在纯策略/纯容器上并被变异覆盖;
- 扣住窗口内的**暂停**没有建模(窗口是 0.03–0.13 s,重放把它当作「未暂停」),已在代码注释与本节写明;
- `Skill.ShortenedWaitTime`/`OverwriteCoolTime` 仍未读 —— 如果某些被动会**缩短冷却**,那可以解释「最早发动 + lag」与初动之间剩下的那点残差(现在一律归因于 R65 的「充满后等下一次普攻」)。

## 5. 部署与验收

`5DB365C6`(481,792 B,1.7.22)-> **`F83D369E`**(486,912 B,1.7.23);部署时游戏未运行、DLL 未被占用;备份
`_dpsm_work\deploy-backup\pre-r72-5DB365C6\`(整个插件目录,其中 `DpsMeter.dll` = `5DB365C6…`);两个 crash `.bak`
哈希未变(`2E1819F2…` / `F7FF1EB8…`)。回退:`Copy-Item _dpsm_work\deploy-backup\pre-r72-5DB365C6\DpsMeter.dll BepInEx\plugins\DpsMeter\DpsMeter.dll -Force`。

验收见 `acceptance_r72/RESULTS.md`:**44 条命令 / 76 条检查 / 0 项不一致**。第一次跑出 4 项,两条是**真实的连带损坏**、两条是本轮尚未落地的产物,都已修掉:
1. `recon_probe` 编译失败 —— 它按需编译 `src/Diagnostics/ParamOwnerProbe.cs`,而这个文件现在引用 `ClockOriginHoldPolicy`;把纯策略文件加进 `ReconProbe.csproj`(与 BehaviorTests 同一做法);
2. 文档收敛 R3 红 —— `CONTRIBUTION-TABLE-REPORT.md` 的 R72 段落只写了「线上 DLL 见 PROJECT-STATUS」,没写当前前缀;补上 `F83D369E…`(R3 的判据是**状态文档必须自带**前缀);
3. `repo_manifest_verify` 红、4 项里的另一条 —— 清单必须在本轮产物落地后重写(`repo_manifest.py --write --exports batch_inputs/rf0`),重写后 `drift=0`。
最终一轮:76 ok / 0 mismatched。

## 6. 下一步(R73 候选)

1. 下一场实机核对 `[CLOCK]`/`[CLOCKP]` 这一对(§4 第一条);
2. 读 `ShortenedWaitTime`/`OverwriteCoolTime` 解释残余的初动残差;
3. 页面可选的「可见行数上限」。

## 7. 归档与计数

- 行为用例 **1205 -> 1254**;变异负控 **189 -> 203**;源码 **106 -> 108 个 .cs**(新增 `Policy/ClockOriginHoldPolicy.cs`、`Runtime/OriginHeldEvent.cs`;`tests/BehaviorTests/Cases.ClockOriginHold.cs` 不算 src;守卫口径 142 = src + tests);
- **全量负控第一次跑出 4 条红,全部是「变异锚点被我自己改坏的」而不是代码错**:`BattleRuntimeCounters.cs` 里 3 条锚在 `OnSessionStart`/`OnManualReset` 的结尾(新增了 hold 那几行),1 条锚在被我改名的 `the-family-has-thirteen-counters` 用例上 —— 已按新函数体重新锚定/改名(`counters-fourteenth-field` -> `counters-seventeenth-field`,期望改成 `the-family-has-sixteen-members`),逐条复验红在具名用例后再跑全量;
- 新增文档:本文件;`check_docs_123.py` 的 FILES 已同步,`.gitignore` 已加 `!/_dpsm_work/acceptance_r72/`;
- 不改历史批次记录(`REFACTOR-BATCH-R71.md` 等原样保留),不改导出形状(`contribution.schemaVersion` 仍 1.2)。
