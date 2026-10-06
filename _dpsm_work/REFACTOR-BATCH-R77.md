# REFACTOR-BATCH-R77 —— 报告层:`ovz=` 不再自相矛盾,`none=`/`full=` 可直接读出

## 0. 一句话

R76 引入的 `[ABSPROBE] sum` 把**两个族群**塞进了同一个三元组:`ovz=<次数>/<落地>/<溢出>` 的**次数**只数生命佐证成立的一族(`case AbsorbVerdict.Oversized`),**金额**却对四个族群全加(`if (o.IsOversized())`)—— 于是 1.7.26 首验读出「`ovz=0/6500000/2358285` 配 `ovzPool=13`」这种自相矛盾的行;同时 `None`(落空判定)没有计数器,`calls` 减去可计族群只能**靠减法**推。R77 只修这两处**读数**:判定层(`Classify`/`ClassifyCarrier`)、记账口径、导出形状(`contribution.schemaVersion` 仍 **1.2**)一字未动。

## 1. 上一轮(1.7.26 实机首验)给出的证据

2026-10-07,quest 411001,120 s,**Lose**,`exports\` 第 **132** 份导出;运行日志 `BepInEx/config/dpsmeter_runtime.log`。

```
[ABSPROBE] sum calls=5489 ovz=0/6500000/2358285 pool=13 partial=0 ovzUnread=0 noMove=5456 missing=0
  lifeUnread=1 carrier(barrier/pool/takeover/fixed/invincible/unreadable)=0/0/0/0/0/0
  seen(barrierDmg/addBarrier/takeover/fixed)=0/0/0/0 lifeMismatch=13 active=0 barrUnread=0
  first(nom/res/landed/overflow)=535538/35538/500000/35538 rows=413 key=13 dropped=5057 keyDropped=0
  castErr=0 notChar=0 noBefore=1
```

三条可判读的事实:

1. **同一行自相矛盾**:`ovz` 的次数是 **0**,后面两个金额却是 **6,500,000 / 2,358,285**;而 `pool=13` 说明这 13 条超量命中全部落在固定池族群(`ショゴス`,`Life` 恒 500,000、`lifeDrop == 0`)。⇒ 金额来自**四族全加**,次数来自**一族**。
2. **`None` 只能算出来**:`calls=5489` 减去可计各族的合计 **5470** = **19**;日志里**没有一行** `None`(普通行被 400 行预算丢进 `dropped=5057`),导出里也没有受害方 `Life` 移动可查 ⇒ 本场**无法**得出 `full=` 的实机值。
3. **R76 的两条要求仍然成立**:`key=13` / `keyDropped=0`(关键行独立预算生效,13 条决定性行一条不丢);定律复核 **ok=412 / violated=0**(1 条 Life 读不到,不计入)。

## 2. 改了什么

### 2.1 计数器分族群(`src/Policy/AbsorbClassifyPolicy.cs`)

| 计数器 | 累加条件 | 说明 |
| --- | --- | --- |
| `Oversized`(旧) | `case AbsorbVerdict.Oversized` | 生命佐证成立的整击落地 |
| `OversizedLanded` / `OversizedOverflow`(**新**) | 同上一行(移进 `case AbsorbVerdict.Oversized`) | **与 `Oversized` 同族群** —— 这就是缺陷 A 的修复点 |
| `OversizedAll`(**新**) | `if (o.IsOversized())` 内首行 | 全部 `Oversized*` 族群的**独立**计数器 |
| `OversizedLandedTotal` / `OversizedOverflowTotal`(旧,含义不变) | `if (o.IsOversized())` | 四族全加(与 `OversizedAll` 同族群) |
| `None`(**新**) | `case AbsorbVerdict.None` 首行 | 落空判定的全部次数 |
| `FullLanded`(**新**) | `case AbsorbVerdict.None` 内 `IsMeasuredFullApplication(o)` | **只**认量测到 `lifeDrop == nominal` 的那一条路径 |

`None` 有四条到达路径(`Nominal <= 0`;`res > 0` 但 `landed <= 0`;`drop == o.Nominal` —— 唯一量测;Life 上升/无法解释的落空),所以新增

```csharp
private static bool IsMeasuredFullApplication(AbsorbObservation o)
    => o.Nominal > 0 && o.Result <= 0 && o.LifeReadable && o.LifeDrop() == o.Nominal;
```

`Clear()` 同步清零全部新字段。

### 2.2 `Describe()` 的新形状(逐字)

```
calls= ovz=<Oversized>/<OversizedLanded>/<OversizedOverflow>
ovzAll=<OversizedAll>/<OversizedLandedTotal>/<OversizedOverflowTotal>
ovzPool= ovzPartial= ovzUnread= noMove= missing= lifeUnread= full= none=
carrier(barrier/pool/takeover/fixed/invincible/unreadable)= seen(barrierDmg/addBarrier/takeover/fixed)=
lifeMismatch= active= barrUnread= first(nom/res/landed/overflow)= rows= key= dropped= keyDropped=
```

改动点:`ovz=` 的三元组改为**生命佐证成立**的一族(次数与金额同族群);新增 `ovzAll=`(全部 `Oversized*`);顶层 `pool=` / `partial=` 改名 `ovzPool=` / `ovzPartial=`,与 `carrier(...pool...)`(载体读数)不再同名;新增 `none=` 与 `full=`。`carrier(...)` 的槽名未动。

### 2.3 用例与变异

* 新增用例 3 条:
  * `policy/absorb-classify/the-oversized-triple-covers-only-the-corroborated-bucket` —— `OversizedLanded == 500000 && OversizedOverflow == 100000 && OversizedAll == 3`,且 `OversizedAll == Oversized + OversizedPool + OversizedPartial + OversizedUnreadable`;
  * `policy/absorb-classify/every-call-lands-in-exactly-one-bucket` —— 14 个判定计数之和 == `Calls`(**这一条就是缺陷 B 的守卫**);
  * `policy/absorb-classify/only-the-measured-whole-application-counts-as-full` —— 同一份新报告喂 `Plain(0,0)` / `Plain(1000,1000)` / `Live(1000,0,5000,6000)` / `Live(1000,0,5000,4000)` ⇒ `Calls == 4 && None == 4 && FullLanded == 1`。
* 两条钉整行字符串的既有用例(空报告 / 清空后、满报告)按新形状改写。
* 变异负控新增 4 条(净 +4:3 条全新 + 1 条拆成 2 条):`absorb-ovz-triple-amounts-cover-every-bucket`(字段级,复现 R76 的病)、`absorb-ovz-triple-rendered-from-the-all-buckets`(渲染级)、`absorb-none-bucket-not-counted`、`absorb-every-none-counts-as-a-whole-application`。
  * **教训(记在案)**:纯渲染的变异只会红在**钉整行**的用例上;要红在**字段级**用例,变异必须落在**累加处**。第一版把渲染变异挂到字段级 `expect` 上,驱动直接报 `went red on the WRONG case`。

### 2.4 没有改的

`AbsorbClassifyPolicy.Classify` / `ClassifyCarrier`(判定逻辑)、`src/Diagnostics/AbsorbProbe.cs`(打印与行预算)、`Aggregator.RecordDamage`(记账)、显示层、导出形状(`schemaVersion` 1.2)、`Debug/AbsorbProbeMaxRows`(默认 400)。R74/R75/R76 的结论一律不回退。

### 2.5 范围决定:`Debug/AbsorbProbeMaxRows` **不动**

本场 `rows=413 dropped=5057 key=13`,400 的普通行预算确实在 120 s 场饱和。但它属于**观测体量**的选择,与本轮两处「标签自相矛盾 / 缺计数器」无关;新的 `none=` / `full=` 已经能在**不改行预算**的前提下答出「整击落地多少次」,行预算只影响「具体是哪几条」。改默认值还需要**重启**才生效,不适合混进纯文本修复。要逐条清单的用户自行调大该键;关键行由 `Debug/AbsorbProbeKeyRows`(默认 200)另行保护,不受 400 影响。

## 3. 计数与清单

### 3.1 本场(1.7.26 的行)能推出什么

| 量 | 值 | 来源 |
| --- | --- | --- |
| `calls` | 5489 | 行内 |
| `ovz=`(新口径) | `0/0/0` | 本场无生命佐证成立的超量命中 |
| `ovzAll=` | `13/6500000/2358285` | 13 条全部 `OversizedPool`(boss 侧 `ショゴス`,`nominal = 500000`,`lifeDrop = 0`) |
| `ovzPool=` / `ovzPartial=` / `ovzUnread=` | 13 / 0 / 0 | 行内 |
| `none=` | **19** | 5489 − 5470(减法,即缺陷 B) |
| `full=` | **待补充** | 本场没有任何一行 `None` 进过日志,导出里也没有受害方 `Life` 移动 |

导出侧:`totals.absorbed = 6,500,000`(= 13 × 500,000,全部是**那个池子的值**,不是吸收能力),`reconcile.absorbed = 13`、`reconcile.absorbedAmount = 6,500,000`;`contribution.schemaVersion = 1.2`。

### 3.2 1.7.27 那一行应当长什么样(样例)

```
[ABSPROBE] sum calls=5489 ovz=0/0/0 ovzAll=13/6500000/2358285 ovzPool=13 ovzPartial=0 ovzUnread=0
  noMove=5456 missing=0 lifeUnread=1 full=<待重跑> none=19 carrier(...)=0/0/0/0/0/0 ... rows=413 key=13
  dropped=5057 keyDropped=0
```

每个计数都与它标注的族群一致;14 个判定计数之和 == `calls`;`ovzAll` == 四族群之和。

## 4. 定律复核

R77 不触碰判定,定律读数应与 R76 相同:本场 **ok=412 / violated=0**(1 条 Life 读不到,单独计数)。历史 48 份 `battle_411001_2026*.json` 的 408 条超量记录未复算(本轮不改判定),见 `REFACTOR-BATCH-R76.md` §3.2。

## 5. 验证

| 闸门 | 结果 |
| --- | --- |
| 构建 | **0 警告 0 错误** |
| 行为套件 | **1310 用例 / 0 失败**(1307 → 1310:`policy/absorb-classify` 增 3 例,另改写 2 条钉行用例) |
| 变异负控 | **227 条 / 0 失败**(223 → 227;本轮 4 条逐条红在具名用例) |
| 验收 | `acceptance_r77` = 见 §7 |
| 静态守卫 | 文档收敛 / docs123 / `repo_manifest --verify` 见 §7 |

只读闸门复跑(对 1.7.26 首验那份证据包与导出):

| 闸门 | 结果 |
| --- | --- |
| `extract_verify.py --bundle extract_20261007_015104_q411001_battle-end` | **VERDICT PASS**(masterdata 21/21 failures=0;census 4/4、30096 folds 全复算;1 条**预期的** WARNING —— 证据包是 1.7.26 写的,而线上已是 1.7.27,`bundle=3a6a885eb0221db7 now=7042fffbb366aa78`,包本身仍内部自洽) |
| `python -m contrib.crosscheck <导出>` | `status=WARNING`、`mismatches=0`、`omissions=0`(2 条 `ATKADD_COUNTER_GT_HITS` 是既有口径说明:计数器按值不按命中) |
| `python -m contrib.validate <导出>` | `ERROR=0 WARN=4 INFO=1`;`analyzable=185669363 hits=5396`;WARN 为既有 I4f/I6/I7/I9、INFO 为 I8 |

## 6. 覆盖不到的和仍未定案的

1. **`full=` 的实机值**:本场取不到(见 §3.1),需要用户再打一场。
2. **`Debug/AbsorbProbeMaxRows` 默认值**:本场 400 已饱和(413 行 + 5057 丢弃),是否上调留待后续(§2.5)。
3. **`inv=1` 具体是哪一面旗**:仍是五个标志(`IsInvincible`/`IsImmortal`/`IsIndomitable`/`IsActingInvincible`/`IsLifeChangeDisabled`)的 OR,未逐个打印。
4. **是否修订已发布承伤数值**(R76 遗留:影响 21 + 408 条、单次最大 269,104)与「无敌对象被打 0 血算不算承伤」—— 口径问题,不在 R77 范围。
5. **`8 hits / 4,000,000` 是否把同一次结算计两遍**(`src/Policy/AttributionPolicy.cs:81-82`)。
6. **`Barrier.mLife` 的真实类型**、`Character` vs `BattleObject` 的继承方向、谁执行 `DamageCut`。
7. **F4 技能时间表那一格 `5.0 → 5.9`** 仍需一场带参照技能 `邪龍の息吹` 的战斗才能实机确认(R74 遗留)。

## 7. 部署与验收

| 项 | 值 |
| --- | --- |
| 部署前 | `BepInEx/plugins/DpsMeter/DpsMeter.dll` = `0165D18779D410E0BFFCB79F6C0D5E31782F260440B9C3BC2E9EF76DCC686062`,508,928 B(1.7.26,R76) |
| 备份 | `_dpsm_work/deploy-backup/pre-r77-0165D187/DpsMeter.dll`(替换后复核同哈希) |
| 部署后 | **`42713774815A1A472D5E475A14B1B623AC2CD4D246C172AB55852DF907BFED0C`**,509,440 B(1.7.27) |
| 替换前置条件 | 无游戏进程(仓库根的 `rlyehshoujotaix_cl.exe` 未运行;`DMMGamePlayer` 若干是启动器)、线上 DLL 未被占用 |
| 部署后实机加载 | 2026-10-07 **02:34:07** 游戏以 1.7.27 启动:配置文件首行被插件重写为 `## Settings file was created by plugin DpsMeter v1.7.27`(**没有新增/删除任何键**,配置哈希 `BAC78D26…`),运行日志另起一段(`=== DpsMeter runtime log started ===`,旧段被覆盖)。该段战斗进行到 `active=50.7s` 未终局即退出(`[TIME]` 末行 `paused=1`,`dGameTime` 冻结),**没有导出、没有 `[ABSPROBE] sum`** ⇒ `full=` 的实机值仍未取到 |
| 崩溃现场 | `DpsMeter-1.0.48-crash.bak` = `2E1819F24E45088E798F8C08D49C31B26C7902AC83887BB54AB52F20A2D31F60`、`DpsMeter-1.0.49-crash.bak` = `F7FF1EB8A779680D472BB9881DB08982337AF6F63AC7002D745EC5E4615B7ED3` **未变**(AGENTS §0.1) |
| **回退** | `Copy-Item _dpsm_work\deploy-backup\pre-r77-0165D187\DpsMeter.dll BepInEx\plugins\DpsMeter\DpsMeter.dll -Force` |
| 验收 | `acceptance_r77`(命令/检查数与结果见 PROJECT-STATUS §1 与 §12) |
| 执行说明 | 部署发生在只读闸门复跑**之前**;`extract_verify` 那条 assembly WARNING 正是这个顺序的产物,不是缺陷 |
