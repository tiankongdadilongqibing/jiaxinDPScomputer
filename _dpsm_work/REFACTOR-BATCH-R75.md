# REFACTOR-BATCH-R75 —— 给「被吸收/无效化」加一台**先读后命名**的探针,并把「读不到」与「读到 0」分开

## 0. 一句话

`被吸收/无效化` 自 1.5.5 起一直是 `nominal − damage` 的**别名**,却连着被当成机制来读:46 份导出 397 条记录、
其中 **395 条差额恰好 500,000**,受害方**全是 `ショゴス`**,而 20 张主数据表里**没有任何字段/行/值等于 500,000**,
boss 的运行时天赋只有 `1002 ModeChange` + `6 攻击力/150/-1`。本轮不猜机制:新增一台**只读**探针,在既有的
`BattleObject.Damage` 前缀/后缀里读受击方的 `Life`、`Character.Barrier`(`IsActived`/`mLife`)与五个无敌族标志,
把差额分类成 `barrier / pool / unknown / takeover / fixed / invincible / masked / unreadable`,战末打一行
`[ABSPROBE] sum`。**不改记账口径、不改显示层、不改导出形状**;障壁类 Harmony 补丁**默认关闭**且**不进 `PatchAll`**。

## 1. 为什么必须"先读再命名"(上一轮的证据)

* 差额本身是**游戏侧事实**,不是插件算错:`nominal = 理论值 × (会心 ? 1.5 : 1.0)`(577,295 = 384,863×1.5;700,395 = 466,930×1.5;
  1,103,327 = 735,551×1.5;735,551×1.0),`applied = nominal − 500,000`;而且游戏**自己的账本**含那部分——
  `[CROSS] game_taken=182,233,442` 对插件的游戏口径合计 182,355,313(差 121,871 = `game_given`),
  而真正入耐久的只有 178,355,313。
* 但**载体找不到**:`masterdata/*.json` grep `500000` 零命中;导出里带 500,000/1,000,000/2,000,000/4,000,000 的
  13 条路径**全是插件自己算出来的字段**;`timeline` 488 行对受害的三个战斗对象(key 58/106/178)**零行**;
  boss 天赋无 `1007 伤害吸收`/`1042 护盾`/`1006 被伤害-`;`[ABIL]` 首见 `m_buffList=0`。
* **两个必须记下的口径问题**:
  1. 此前写下的「500,000 与伤害大小无关」**不成立**。`src/Hooks/BattleObjectHooks.cs` 的
     `int damage = (__result > 0) ? __result : __0;` 会把**整击被吸收**(`__result == 0`)记成**满额伤害**
     (`absorbed = 0`),于是那 397 条只是"差额小于游戏口径"的子集,游戏口径 ≤ 500,000 的命中在现有数据里**看不见**。
     本轮的 `masked=` 桶就是为这个盲区设的。
  2. 一次真实命中在导出里留**两条**记录(满值那条 `nominal == amount` + 被吸收那条),`src/Policy/AttributionPolicy.cs`
     自述"一次结算会留下 DamageAction/ActDamageAction 对 + 被吸收记账调用" ⇒ `8 hits / 4,000,000` 可能把同一次结算
     计了两遍(`hits` 是结算次数,不必然是命中次数)。**仍未定案**。

## 2. 改了什么

### 2.1 分类策略(纯,可被测试执行)

新 `src/Policy/AbsorbClassifyPolicy.cs`:

* `internal enum AbsorbVerdict { None, Barrier, BarrierShort, CarrierUnknown, TakeOver, FixedDamage, Invincible, Masked, Unreadable }`
* `internal struct AbsorbObservation`:两个既有数字(`Nominal` = `__0`,`Result` = `__result`)+ 围绕这次调用的读数
  (受击方 `Life` 前后、障壁 `mLife` 前后、障壁是否激活、本次调用期间是否观测到 `Barrier.Damage` / `DamageTakeOver` /
  `TryGetFixedDamage`、无敌族标志)。
* `Classify(...)` 的**顺序就是论证**:`Masked`(`Nominal > 0 && Result <= 0`)优先 → `FixedDamage` → `Invincible` →
  `TakeOver` → 障壁分支 → `CarrierUnknown`。障壁分支里:`mLife` **读不到** ⇒ `Unreadable`;移动量取**绝对值**
  (池的极性未定:`mLife` 可能是"还剩多少"也可能是"已吸多少",判定**不许**依赖这个猜测),`>= withheld` ⇒ `Barrier`,
  `> 0` ⇒ `BarrierShort`(池在这一次命中里被打空——这是区分"总量 N 的池"与"每击 N"的唯一形状),`== 0` ⇒ `Unreadable`。
* **规则**:只有**读到该载体自身发生了变化**才命名它;`CarrierUnknown` 与 `Unreadable` 是**一等答案**。
  "something did it and we did not see what"与"我们没能看"必须是两句话。
* `AbsorbProbeReport` 每值一桶 + `Describe()` 单行(`first(nom/res/life/bar)` 那个四元组是**第一次**被削减命中的四个数,
  一场就能定案"削减量是否等于受击方自身掉的血""障壁 mLife 有没有动")。

### 2.2 只读探针(默认开,**不新增任何 Harmony patch**)

新 `src/Diagnostics/AbsorbProbe.cs` + 改 `src/Hooks/BattleObjectHooks.cs`:前缀存受击方状态,**后缀里在
`Aggregator.RecordDamage` 之后**分类——记账调用一字未改,探针抛异常也进不了记账。读数失败一律计数
(`castErr`/`notChar`/`noBefore`),`int.MinValue` 打印 `?`,**绝不静默 0**。行上限 `Debug/AbsorbProbeMaxRows`,
超出的行记 `dropped=`,所以"日志被限流"不会看起来像"这一场很安静"。

战末 `src/Aggregator.Finalize.cs` 打一行:

```
[ABSPROBE] sum calls=… withheld=… sum=… masked=…/… lifeMismatch=…
  verdict(barrier/pool/unknown/takeover/fixed/invincible/unreadable)=…/…/…/…/…/…/…
  seen(barrierDmg/addBarrier/takeover/fixed)=…/…/…/… active=… unreadable=…
  first(nom/res/life/bar)=…/…/…/… rows=… dropped=…
```

### 2.3 载体 hook(默认关,逐个隔离)

新 `src/Hooks/AbsorbCarrierHooks.cs`:8 个目标 —— `Barrier.Activate` / `Deactivate` / `CalcLife` / `Damage`、
`TalentActionAddBarrier.ActExecute`、`BattleObject.DamageTakeOver` / `AddDamageTakeOverChara` / `TryGetFixedDamage`。

* **不带 `[HarmonyPatch]` 特性**:`_harmony.PatchAll()` 会安装本程序集里**所有**带该特性的类,带特性等于默认开启,
  所以它们只在 `Plugin.TryPatchAbsorbCarrierHooks` 里按 `Debug/AbsorbProbeHooks` 逐个 patch(与 crit/power 探针同一做法)。
* **每个 postfix 只声明 `__instance` 加 int/bool 参数**:1.0.48/1.0.49 的启动崩溃发生在生成的 (il2cpp→managed) thunk
  **转换参数**时——在 postfix 体之前,且"把体写成空的"也无效。所以签名只是降低风险,**真正的保险是开关**。
* 取不到目标时按名字回退扫描声明方法,且**只**在同名候选恰好一个时采用:在重载之间猜,就是探针开始测量另一个函数。

### 2.4 开关(改后需重启)

| 键 | 默认 | 作用 |
|---|---|---|
| `Debug/AbsorbProbe` | **true** | 读数 + `[ABSPROBE]` 行 + 战末 `sum` 行 |
| `Debug/AbsorbProbeHooks` | **false** | 8 个载体 hook(启动时决定装不装) |
| `Debug/AbsorbProbeMaxRows` | **400** | 每场逐行输出上限(超出计数) |

### 2.5 明确不改的东西

记账口径不动(`masked` 只计数)、F6/overlay 文案不动、导出形状不动(`contribution.schemaVersion` 仍 1.2)、
既有配置描述不动。第二轮才允许讨论"是否修 `(__result > 0) ? __result : __0`",因为那会移动每一个已发布的承伤合计。

## 3. 验证

| 闸门 | 结果 |
|---|---|
| 构建 | **0 警告 0 错误**。修正两处:`Barrier.mLife` 的类型无法从 interop 元数据读出(编译报 `CS1503: 参数 1: 无法从"System.Globalization.CultureInfo"转换为"string"`)⇒ 读数走 `Convert.ToString`/`Convert.ToInt32`,读不出就报 `Unreadable`;`AbsorbProbe.cs` 补 `using System;` |
| 行为套件 | **1298 用例 / 0 失败**(1275 → 1298:新组 `policy/absorb-classify` 23 例)。套件当场**抓出一个真缺陷**:`AbsorbProbeReport` 的 `FirstLifeDrop`/`FirstBarrierMove` 初值只在 `Clear()` 里置 `int.MinValue`,于是"新建"打印 `0/0`、"清空"打印 `?/?`——同一状态两种答案;已在**声明处**置初值 |
| 变异负控 | **218 例 / 0 失败**(211 → 218:7 条新变异逐条确认红在**具名用例**,且变异体仍编译) |
| 验收 | **`acceptance_r75` = 44 条命令 / 76 条检查 / 0 项不一致**(唯一非 0 退出是报告类的 `crosscheck(batch)`,不计入不一致)。**第 1 跑曾出现 2 项假红**(`selftest/negctl_driver` 与完整 218 条负控里的 1 条):根因是**验收与变异负控并行**运行,二者都往同一个 `_dpsm_work/tests/BehaviorTests/bin/Release/net6.0/BehaviorTests.exe` 写,一方的变异体被另一方当成自己的构建结果跑了出来(假红用例是 `ui/skill-timeline-text/*` 两条)。单跑后 `218/0`、`selftest 3/0`、验收 `76/0` 全绿。**教训:验收与负控绝不并行。** |
| 静态守卫 | 文档收敛 / docs123 / `repo_manifest --verify` / `refactor_final_check` 见 §5 |

## 4. 覆盖不到的和仍未定案的

1. **`Barrier.mLife` 的真实类型未定**:编译期只能确定它既不是 `int`(否则 `ToString(IFormatProvider)` 可用)也无法直接当字符串用。
   `Convert` 读不出时打 `?` 并把该次归为 `Unreadable`;日志里 `barrier … life=` 那一格会第一次给出它的真实形态。
2. **`Character` 与 `BattleObject` 的继承方向未确证**:`bo.TryCast<Character>()` 编译通过且与
   `src/Diagnostics/UnitStateProbe.cs` 同路,但方向没有独立证据。
3. **H2(平削 / `DamageCut`)没有可直接读的载体**:天赋动作 `Type` 枚举里有 `008 DamageCut`,但执行它的类没找到。
   目前 H2 只能由 `verdict=unknown` 桶的**恒定性**间接支持——这正是本轮要量出来的东西,而不是结论。
4. **实机未验证**。第一场只需要 `[ABSPROBE] sum …` 一行,就能指出 500,000 落在哪个桶;第二场再开
   `Debug/AbsorbProbeHooks` 手动确证 carrier。**请求你打一场是允许的,插件不会要求你离线。**
5. `masked=` 的规模一旦量出来,**可能意味着**现有承伤合计对某些命中偏大;本轮只测量,不修正。

## 5. 部署与验收

| 项 | 值 |
|---|---|
| 部署前 | `BepInEx/plugins/DpsMeter/DpsMeter.dll` = `DF70F1A97F3A9E63BD596418570EDB8D0530F7B78273A427243929FFA05E1795`,489,984 B(1.7.24,R74) |
| 备份 | `_dpsm_work/deploy-backup/pre-r75-DF70F1A9/DpsMeter.dll`(替换后复核同哈希) |
| 部署后 | `CFDD2BD5CE039554F07110F1E7DCB09A4F2ADE0BAEAE39D5CBE42448C125808C`,506,368 B(1.7.25) |
| 替换前置条件 | **无游戏进程**(替换前实测:只有 `DMMGamePlayer`「マイゲーム」启动器在跑,`rlyehshoujotaix_cl.exe` 未运行);替换时源码目录**没有任何 .cs 比 DLL 新** |
| 崩溃现场 | `DpsMeter-1.0.48-crash.bak` = `2E1819F24E45088E798F8C08D49C31B26C7902AC83887BB54AB52F20A2D31F60`、`DpsMeter-1.0.49-crash.bak` = `F7FF1EB8A779680D472BB9881DB08982337AF6F63AC7002D745EC5E4615B7ED3` **未变**(AGENTS §0.1) |
| 验收 | **44 条命令 / 76 条检查 / 0 项不一致**(`_dpsm_work/acceptance_r75`,`runs.json` 记 44 条;唯一非 0 退出是报告类的 `crosscheck(batch)`) |
| **回退** | `Copy-Item _dpsm_work\deploy-backup\pre-r75-DF70F1A9\DpsMeter.dll BepInEx\plugins\DpsMeter\DpsMeter.dll -Force` |

> **一次重建**:第一次构建(`71A37E20…`)发生在行为套件抓出那个缺陷**之前** —— `AbsorbProbeReport` 的两个
> `int.MinValue` 初值只写在 `Clear()` 里,于是"新建"与"清空"打印不同的 `first(...)` 四元组。修好之后**重新构建**
> 并部署了 `CFDD2BD5…`;两次的字节约数相同(506,368 B)是因为改动只是一个**常量初值**,不是同一份二进制。

本轮的算术部分(分类顺序、`masked` 与 `withheld` 的分离、`first` 四元组、`?` 与 0 的区别)由 1298 例 + 218 条变异**执行**验证;
现场部分(探针在 `SlotProbe` 之外唯一的读取点、`[ABSPROBE] sum` 的位置、8 个载体 hook 是否真的解析到目标)只能由**下一场**与
本轮验收覆盖。
