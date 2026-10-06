# REFACTOR-BATCH-R76 —— 超量命中:把「被吸收」从**推断**改成**判定**

## 0. 一句话

R75 的探针在第一场就证伪了它自己的判据:游戏 `BattleObject.Damage` 的返回值不是"入耐久",而是**溢出量**
——`res == max(0, nominal − lifeBefore)`,两场 800 条读数里 **798 条可读的全部成立、0 违例**。于是插件的
`被吸收 = nominal − res` 在"超量命中"上恰好等于**目标的剩余 Life**,两个已发布字段**互换**。本轮只做判定层:
`res > 0` 自成一族 `Oversized*`(落地 = `nominal − res`、溢出 = `res`),**以 Life 移动为准**——`lifeDrop == nominal`
一律判为"整击落地"、不再进任何"被削减"桶;并给关键行**独立行预算**,免得决定性的行又被行上限挤进 `dropped`。
**不改任何已发布的承伤数值**(见 §2.5)。

## 1. 上一轮(两场实机)给出的三条证据

1. **定律**:`res == max(0, nominal − LifeBefore)`。玩家侧 10 条超量命中逐条精确吻合
   (`nom=197644 res=79683` ⇒ LifeBefore 必须 117,961 = 打印的 `life=117961`,且该单位随后 Life=0 阵亡)。
2. **boss 侧**:`vic=Boss:ショゴス` 397 行样本中 **396 行 `Life=500000` 且 `lifeDrop=0`**,**397/397 `inv=1`**
   (玩家侧 378 行全 `inv=0`);本场日志里 boss 天赋只有 `1002 ModeChange` + `6 攻击力/150/-1`
   ⇒ 「被吸收 500000」是那些对象的 **Life 值**,不是吸收能力。**boss 无吸收能力 = 否定。**
3. **探针自身两个缺陷**:(a) `masked` 是假阳性(790 行,`lifeDrop` 恰等于 nominal);(b) 400 行上限把 boss 的
   **11 条决定性超量命中全挤进 `dropped=5157`**,证据只剩聚合值。

## 2. 改了什么

### 2.1 判定族(纯策略 `src/Policy/AbsorbClassifyPolicy.cs`,整体重写)

`internal enum AbsorbVerdict` 现在 14 个成员,按**读数**而非按差值命名:

| 判定 | 条件 | 含义 |
|---|---|---|
| `None` | 返回值不报溢出,且 `lifeDrop == nominal`(或无可判之物) | 整击落地 —— **R75 的 `Masked` 桶就是被这一条删掉的** |
| `Oversized` | `res > 0` 且 `lifeDrop == nominal − res` | 超量命中,**Life 佐证**了拆分 |
| `OversizedPool` | `res > 0` 且 `lifeDrop == 0` | 固定池/无敌对象(`ショゴス` 那种 `Life` 恒 50 万),**Life 无法佐证**,单独标注 |
| `OversizedPartial` | `res > 0` 且 Life 动了但量不对 | 读数与拆分不一致,如实上报 |
| `OversizedUnreadable` | `res > 0` 且 Life 读不到 | 拆分无法佐证 |
| `NoLifeMovement` | `res <= 0` 且 `lifeDrop == 0` | 没有观察到任何落地的量 |
| `WithheldNoReturn` | `res <= 0` 且 `0 < lifeDrop < nominal` | **唯一**真正像"被吸收"的形态:削掉一部分而返回值没说 |
| `LifeUnreadable` | `res <= 0` 且 Life 读不到 | 无法判定,**绝不**当 `None` |
| `Barrier` / `BarrierShort` / `TakeOver` / `FixedDamage` / `Invincible` / `Unreadable` | 载体读数 | 只在 **Life 定律解释不了**这次命中时才会被查询(顺序 `DamageCut → TakeOver → 障壁 → Invincible`) |

* `AbsorbObservation` 新增 `IsOversized()`(`Result > 0 && Nominal > Result`)、`Landed()`(`Nominal − Result`)、
  `Overflow()`(`Result`),并在注释里写清:导出里的 `入耐久` 是 `res`、`被吸收` 是这个 `Landed()`,**这两者在超量命中上是互换的**。
* **删掉 `CarrierUnknown`**:在已测定的定律下它对超量命中不可达(缺口**就是**溢出量),而"兜底猜一个机制"正是
  R75 的病根。Life 读数与拆分不一致时给 `OversizedPartial`,不猜。

### 2.2 关键行独立预算(纯策略里,可被变异)

`AbsorbProbeReport.IsKeyVerdict(v)`:`Oversized*` 与载体命中算**关键行**;
`TryTakeRow(v, maxRows, maxKeyRows)` 让关键行**只**受自己的上限约束、普通行受普通上限约束,两种拒绝分别计
`Dropped` 与 `KeyDropped` ⇒ 关键行再也不会被普通行挤掉。开关 `Debug/AbsorbProbeKeyRows`(默认 **200**);
`Debug/AbsorbProbeMaxRows` 仍是 400。

### 2.3 打印(`src/Diagnostics/AbsorbProbe.cs`)

行内 `withheld=` 换成 `diff=`(并注明**不是**吸收量),新增 `landed=` / `overflow=`(仅在 `res > 0` 时给出,否则打 `?`)
与 `key=0/1`;战末 `[ABSPROBE] sum` 一行改为:
`calls= ovz=count/landedSum/overflowSum pool= partial= ovzUnread= noMove= missing= lifeUnread= carrier(barrier/pool/takeover/fixed/invincible/unreadable)= seen(barrierDmg/addBarrier/takeover/fixed)= lifeMismatch= active= barrUnread= first(nom/res/landed/overflow)= rows= key= dropped= keyDropped=`。

### 2.4 没有改的

记账调用(`src/Hooks/BattleObjectHooks.cs` 里 `Aggregator.RecordDamage(...)`)、`[ABSORB]` 行、战末 `>>> 被吸收/无效化`
行、显示层与导出形状(`contribution.schemaVersion` 1.2)**一字未动**;载体钩子 `Debug/AbsorbProbeHooks` 保持**默认关闭**。

### 2.5 范围决定:**本轮不改已发布的承伤数值**

判定层与发布层必须分两轮。改成"超量命中按 `min(nominal, Life)` 计入"会移动**每一个已发布的承伤合计**
(本轮 21 条、历史 408 条都受影响,单次最大 269,104),而判定层现在还没在实机验证过。因此本轮只让
**新的判定与清单**可见,**数值保持原样**;是否改、怎么改留到 R77,并且要一并决定"无敌对象被打 0 血算不算承伤"
——那是口径问题,不是算术问题。

## 3. 超量命中清单与计数

### 3.1 本轮两场(来自 `BepInEx/config/dpsmeter_runtime.log`,1.7.25 打印格式)

**玩家侧 10 条**(全部 `inv=0`;`landed` 与 `lifeBefore` 逐条相等):

| 受害对象 | nominal | res(溢出) | landed(落地) | overflow | lifeBefore | 旧判定 |
|---|---|---|---|---|---|---|
| `P:レヴナント` | 110714 | 72750 | 37964 | 72750 | 37964 | unknown |
| `P:[痺夏]シゼル＝メ` | 142380 | 140149 | 2231 | 140149 | 2231 | unknown |
| `P:[痺夏]シゼル＝メ` | 276702 | 7598 | 269104 | 7598 | 269104 | unknown |
| `P:[痺夏]シゼル＝メ` | 197644 | 79683 | 117961 | 79683 | 117961 | unknown |
| `P:T.O.W.E.R.typeR` | 99338 | 42954 | 56384 | 42954 | 56384 | unknown |
| `P:レヴナント` | 43859 | 1939 | 41920 | 1939 | 41920 | unknown |
| `P:T.O.W.E.R.typeR` | 52478 | 46417 | 6061 | 46417 | 6061 | unknown |
| `P:T.O.W.E.R.typeR` | 69432 | 3378 | 66054 | 3378 | 66054 | unknown |
| `P:レヴナント` | 31579 | 19131 | 12448 | 19131 | 12448 | unknown |
| `P:T.O.W.E.R.typeR` | 2686 | 859 | 1827 | 859 | 1827 | unknown |

**boss 侧 11 条**:旧构建把它们**全部丢进 `dropped`**,只剩聚合与 `[ABSORB]` 行可查 —— `verdict` 桶记为
`invincible=11`、`sum=5500000`、`first(nom/res/life/bar)=577331/77331/0/0`;对应 11 条 `[ABSORB]` 的
`游戏口径/入耐久` 分别是 `577331/77331`、`1157650/657650`(×4)、`667481/167481`、`735551/235551`、
`1103327/603327`(×3)、`550624/50624`。按定律反推:`landed = 500000`、`overflow = res`、`lifeBefore = 500000`
(那 11 条**全部**在 `inv=1` 的固定池对象上,因此新判定为 **`OversizedPool`** 而不是可佐证的 `Oversized`)。
**这正是 §2.2 存在的原因** —— R76 的构建会让这 11 条各自成行。

### 3.2 历史数据(48 份 `battle_411001_2026*.json`)

* **超量记录 408 条**,其中 **boss 侧 406 条、玩家侧 2 条**;40/48 份文件至少带一条。
* 反推的 `lifeBefore = nominal − amount` 直方图:**500000 ×406**、`2821` ×1、`19010` ×1
  —— 也就是说,历史上每一击"超量"的判定量都精确落在同一个池值 50 万上,另外两条是玩家自己的剩余 Life。

## 4. 定律复核(要求 4)

```
res == max(0, nominal − lifeBefore)      ok=798   violated=0   life 读不到=2
```

复核脚本与输出:`_dpsm_work/tests/_r76_oversized.tmp.py` / `_r76_oversized.tmp.txt`(临时文件,gitignored)。
其中**读不到的 2 行**、**`dropped` 行**、以及 **`inv=1` 的 397 行固定池对象**都在报告里单独计数,**没有**被算作超量命中:

* boss 行 397 条,其中 **396 条是 `life=500000` + `res=0` 的固定池**,1 条 Life 读不到;
* `inv=1` 共 397 行,**全部是 boss 侧**(非 boss 侧 0 行);
* 整场 Life 读不到行 2 条;
* 被取舍的行数只体现在 `[ABSPROBE] sum` 的 `dropped=` / `keyDropped=` 上 —— 新构建把关键行移出前者。

## 5. 验证

| 闸门 | 结果 |
|---|---|
| 构建 | **0 警告 0 错误**(插件与行为套件) |
| 行为套件 | **1307 用例 / 0 失败**(1298 → 1307:`policy/absorb-classify` 由 23 例改写为 32 例) |
| 变异负控 | **223 条 / 0 失败**(218 → 223;R76 的 12 条逐条红在具名用例,其中 `absorb-full-application-called-withholding` 直接复现 R75 的病) |
| 验收 | `acceptance_r76` = 见 §7 |
| 静态守卫 | 文档收敛 / docs123 / `repo_manifest --verify` 见 §7 |

## 6. 覆盖不到的和仍未定案的

1. **实机未验证**:新判定族与两档行预算都只在离线执行过;下一场才会产出 `r76` 格式的 `[ABSPROBE]` 行。
2. **`inv=1` 到底是哪一面旗**:`inv=` 是五个标志的 OR(`IsInvincible`/`IsImmortal`/`IsIndomitable`/`IsActingInvincible`/
   `IsLifeChangeDisabled`)。要解释"固定池对象为什么 Life 恒定",必须**逐个**打印,R76 未做(留给下一轮)。
3. **是否改已发布数值**:本轮明确不改(§2.5);影响面已量化:本轮 21 条 + 历史 408 条,单次最大 269,104。
4. **`WithheldNoReturn` 目前为空**:两场里没有一条命中符合"削掉一部分而返回值没报"——这本身是证据:
   现有的 `被吸收` 全部由超量解释,没有残留。
5. `8 hits / 4,000,000` 是否重复计一次结算,仍未定案(与 R75 相同)。

## 7. 部署与验收

| 项 | 值 |
|---|---|
| 部署前 | `BepInEx/plugins/DpsMeter/DpsMeter.dll` = `CFDD2BD5CE039554F07110F1E7DCB09A4F2ADE0BAEAE39D5CBE42448C125808C`,506,368 B(1.7.25,R75) |
| 备份 | `_dpsm_work/deploy-backup/pre-r76-CFDD2BD5/DpsMeter.dll`(替换后复核同哈希) |
| 部署后 | **`0165D18779D410E0BFFCB79F6C0D5E31782F260440B9C3BC2E9EF76DCC686062`**,508,928 B(1.7.26) |
| 替换前置条件 | 无游戏进程;替换时源码目录**没有任何 .cs 比 DLL 新** |
| 崩溃现场 | `DpsMeter-1.0.48-crash.bak` = `2E1819F24E45088E798F8C08D49C31B26C7902AC83887BB54AB52F20A2D31F60`、`DpsMeter-1.0.49-crash.bak` = `F7FF1EB8A779680D472BB9881DB08982337AF6F63AC7002D745EC5E4615B7ED3` **未变**(AGENTS §0.1) |
| **回退** | `Copy-Item _dpsm_work\deploy-backup\pre-r76-CFDD2BD5\DpsMeter.dll BepInEx\plugins\DpsMeter\DpsMeter.dll -Force` |
| 验收 | `acceptance_r76`(命令/检查数与结果见 PROJECT-STATUS §1 与 §12) |

本轮的判定部分由 1307 例 + 223 条变异**执行**验证;超量命中清单与定律复核由
`_dpsm_work/tests/_r76_oversized.tmp.py` 产出(`ok=798 violated=0`);实机部分只能由下一场覆盖 ——
下一场会产出 `r76` 格式的 `[ABSPROBE]` 行(`diff=/landed=/overflow=/key=`),届时 boss 侧那 11 条会各自成行。
