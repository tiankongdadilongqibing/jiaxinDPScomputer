# REFACTOR-BATCH-R78 —— 给两个词正名:`实际伤害` → `游戏口径`;`被吸收/无效化` → `超出剩余耐久` / `目标剩余耐久`

> 版本 **1.7.28**(上一版 1.7.27 = R77)。本轮**只改词、解除一处互斥、把关键判定写成一行话** ——
> **不动判定层、不动记账口径、不动 `Hooks/BattleObjectHooks.cs:51` 的取量、不动导出键与任何数值**
> (`contribution.schemaVersion` 仍 **1.2**,143 份历史导出一字未动,`[CROSS]`(`src/Aggregator.Finalize.cs:140`)不变)。
> 历史批次文档(`REFACTOR-BATCH-R75/R76/R77.md`、`SESSION-STATE.md`)按 AGENTS §0.3 **不可改写**,里面的旧词永久保留。

## 0. 本轮任务与范围

用户给定的范围(逐条):

* **A 正名**(字段名/文案/注释,零数值移动):`实际伤害` → 它实际印的量;`被吸收/无效化` → 它实际印的量。
* **B 解除互斥 + 残差改用游戏口径**:`CompositionProbe.Chain.cs` 里 `if (absorbed > 0) … else if (theory > 0) …`
  过去二选一,于是**恰好是会心 ×1.5 的超量命中行**既不印差额也不印 `剩余倍率`。
* **F 文档口径更正**(本文件 + 6 份当前状态文档)。
* **仅日志侧 D**:关键判定行后补一行 `[ABSPROBE] note`,把「耐久未变化 ⇒ 无落地可观测」写出来。

**明确不做**(并在 §3.2 报告):

* **C 档**(改 `Hooks/BattleObjectHooks.cs:51` 的取量 / 改记账口径 / 改导出形状):另开一轮。
* **面板侧 D**(把 `AbsorbObservation` 送进 UI 显示耐久变化):需要新增跨层通道,留待后续。

## 1. 缺陷:两个词各自指的是哪个量

同一场(2026-10-07,quest 411001,目标 `ショゴス`)的四条面板记录,第 1 条与第 2/3/4 条**算术上同源**:

| 面板字段 | 实际印的量 | R78 之后印什么 |
|---|---|---|
| `实际伤害 397575` / `实际伤害 96363` | `BattleObject.Damage` 的**返回值** `res` | ← 见下两行(普通行 / 溢出行的取词不同) |
| `被吸收/无效化 500000` | `nominal − res` = **目标剩余耐久**(超量时) | `目标剩余耐久 500000(…非吸收…)` |
| `游戏口径 596363` | `finalDamage + absorbed` = 游戏入参 `__0` | `游戏口径 596363`(普通行直接印它) |

* 第 1 条:`nom = 397575 < 剩余耐久 500000` ⇒ `res = max(0, nom − life) = 0` ⇒ hook 走 `__result > 0 ? __result : __0`
  的**回退**,发布量 = `__0` = 397,575 = 游戏口径 ⇒ 这一行**看不出问题**(游戏飘字也是 397,575)。
* 第 2/3/4 条:`nom = 596363 > 500000` ⇒ `res = 96363` ⇒ hook 发布 `res`(96363)而当**落地伤害**用,
  再把 `nom − res = 500000` 叫「被吸收」—— 而 Boss 身上飘出的数字是 **596363**(游戏自己的账本与 UI 都用 `__0`,
  `[CROSS]` 逐单位对账 `nominal_taken == game_taken`)。**两个词在超量命中上互换**。
* `596363 / 397575 = 1.5000006` ⇒ 那三条正是**会心**(官方公式里的 `会心ダメージ率 150%`);旧文案走
  `if (absorbed > 0)` 分支,把 `剩余倍率` 那一支整段藏掉 ⇒ 面板上看不见 ×1.5,只看见一个凭空的「被吸收 50 万」。
* 定律依据(本轮未再测,直接引用 R76):`res == max(0, nominal − lifeBefore)`,**798/798 条可读读数成立、0 违例**
  (`src/Policy/AbsorbClassifyPolicy.cs:108-111` 的字段文档)。同一形状本会话在**玩家**身上也出现 34 次
  (absorbed 20 … 146,264)⇒ 不是 Boss 专属。

## 2. 改动清单(改前 → 改后)

### 2.1 新增文件

| 文件 | 内容 |
|---|---|
| `src/Policy/AbsorbWording.cs`(**新**,141 行) | 唯一把判定/两个量变成文字的**纯**落点,行为套件可直接执行:  `DamageTail(nominal, published, absorbed)` / `ResidualBasis(published, absorbed)` / `ResidualText(theory, published, absorbed)` / `ChainTail(theory, published, absorbed)` / `VerdictNote(v, o)` |
| `tests/BehaviorTests/Cases.AbsorbWording.cs`(**新**,146 行) | group `policy/absorb-wording`,**18 个用例**(见 §5.1) |

### 2.2 逐文件(数值一律未动)

| 文件:行 | 改前 | 改后 |
|---|---|---|
| `src/Composition/CompositionProbe.Chain.cs:411-425` | `· 实际伤害 {finalDamage}` + `if (absorbed > 0) {…被吸收/无效化…} else if (theory > 0) {…剩余倍率 ×residual…}` | `· 超出剩余耐久(溢出) {finalDamage} · 目标剩余耐久 {absorbed}(= 游戏口径 {gameValue} − 超出剩余耐久 {finalDamage};非吸收;是否落地看命中前后 Life,本行不断言)` + **互不排斥**的 `if (theory > 0) {…剩余倍率 ×residualShown…}` |
| `src/Composition/CompositionProbe.Chain.cs:428` | (无) | `sb1.Append(AbsorbWording.ChainTail(theory, finalDamage, absorbed));` |
| `src/Composition/CompositionProbe.Chain.cs:22-28` | `<param name="absorbed">` 旧说明 | R78 正名说明 + 等价关系 + 「数值一个都不动」 |
| `src/Aggregator.Stats.cs:104-113` | 注释 + `[DpsMeter][ABSORB] {Desc} 被吸收/无效化 {absorbed}(游戏口径 {nominal} = 入耐久 {damage} + 吸收 {absorbed})` | `[DpsMeter][OVERFLOW] {Desc} 超出剩余耐久 {damage}(游戏口径 {nominal} = 超出剩余耐久 {damage} + 目标剩余耐久 {absorbed};非吸收)`(插值表达式逐字不变) |
| `src/Aggregator.Stats.cs:64-70` | `<param name="nominal">` 旧说明 | R78 说明(`nominal > damage` 是**超出剩余耐久**,不是吸收) |
| `src/Aggregator.Finalize.cs:87` | `>>> 被吸收/无效化 {N} / {M} hits  (taken {T} + 吸收 {N} = 游戏口径 {T+N})` | `>>> 超出剩余耐久 {N} / {M} hits  (taken {T} + 超出 {N} = 游戏口径 {T+N};非吸收)` |
| `src/Aggregator.Finalize.cs:94` | R75 探针注释 | 同步(并注明 R76 已把 `masked=` 换成判定名) |
| `src/Ui/OverlayUGUI.Rows.cs:833` | `int absorbN = 0; long absorbSum = 0;` | `int overflowN = 0; long overflowSum = 0;   // R78: 旧名 absorbN/absorbSum —— 这不是被吸收量` |
| `src/Ui/OverlayUGUI.Rows.cs:841-846` | `// 被吸收/无效化: …` + `if (e.Nominal > e.Amount) { absorbN++; absorbSum += … }` | `// R78(A): 超出剩余耐久 = Nominal − Amount,旧文案叫「被吸收/无效化」…` + `if (e.Nominal > e.Amount) { overflowN++; overflowSum += … }` |
| `src/Ui/OverlayUGUI.Rows.cs:886` | `被吸收/无效化 {n} 条/{m}(未入耐久;…)` | `超出剩余耐久 {n} 条/{m}(非吸收:命中值超出目标剩余耐久的部分;未入耐久,游戏自身统计按 本场游戏口径 = 伤害 + 该值 计入)` |
| `src/Hooks/BattleObjectHooks.cs:16-31` | `result = the damage that actually reached 耐久 … the difference is 被吸收/无效化` | `result = the call's OVERFLOW …` + R76 定律 + 「普通命中发布量就是 `__0`;报溢出的调用上差额是**目标剩余 Life**,不是吸收」 |
| `src/Hooks/BattleObjectHooks.cs:51` | `int damage = (__result > 0) ? __result : __0;` | **未动**(C 档) |
| `src/Diagnostics/AbsorbProbe.cs:207-213` | (无) | `if (AbsorbProbeReport.IsKeyVerdict(v)) { string note = AbsorbWording.VerdictNote(v, o); if (note != null) RuntimeLog.Write("[ABSPROBE] note " + note); }`(与 `key=` 同一门限) |
| `src/Diagnostics/AbsorbProbe.cs:11-30` | 类文档旧称谓 | R78 正名 + 「仍然不移动任何数字」 |
| `src/Model/CalcBreakdown.cs:74-90` | `Applied` = 「Damage that reached 耐久」、`Absorbed`、`Residual` | `Applied` = 本插件**已发布**的量(溢出调用上是**没装下**的部分)/ `Absorbed` = 超出剩余耐久、非吸收 / `Residual` = **文本**用游戏口径为分子,本字段保留原基数(导出 `calc.residual`、`forensics` 分桶键) |
| `src/Model/ActorStats.cs:42-47` | `DamageTakenNominal - DamageTaken` = 被吸收/无效化 | = 超出剩余耐久 / 非吸收 |
| `src/Output/CalcReconcile.cs:112-123` | `AbsorbedAmount` 文档 | 超出剩余耐久的量(数值与导出键未动) |
| `src/Policy/AbsorbClassifyPolicy.cs:137-140` | `Landed()` 文档(末句提「两列互换」) | 保留定律,并注明 R78 已把旧名改成 `目标剩余耐久`、只改词 |
| `src/Plugin.cs:185-192`、`:569` | `Debug/AbsorbProbe` 的两处说明 | 旧词保留为**历史引述**,并写明 R78 改名与新增 `[ABSPROBE] note` |
| `src/DpsMeter.csproj:11` | `<Version>1.7.27</Version>` | `<Version>1.7.28</Version>` |
| `src/BuildInfo.cs:14` | `"1.7.27"` | `"1.7.28"` |
| `tests/BehaviorTests/BehaviorTests.csproj`(R78 段) | (无) | `<Compile Include="$(SrcRoot)\Policy\AbsorbWording.cs" />` |
| `tests/BehaviorTests/Program.cs:28` | `ExpectedCases = 1310` | `= 1328`(+`Cases.AbsorbWordingCases(r);`) |
| `tests/negative_control.py`(R78 段) | 227 例变异 | **233** 例(新增 6 条,见 §5.2) |

### 2.3 注释类同步(单独列出,均不改变任何行为)

`src/Composition/CompositionProbe.Chain.cs:22-28`、`src/Aggregator.Stats.cs:64-70`、`src/Aggregator.Finalize.cs:94`、
`src/Hooks/BattleObjectHooks.cs:16-31`、`src/Diagnostics/AbsorbProbe.cs:11-30`、`src/Model/CalcBreakdown.cs:74-90`、
`src/Model/ActorStats.cs:42-47`、`src/Output/CalcReconcile.cs:112-123`、`src/Policy/AbsorbClassifyPolicy.cs:137-140`、
`src/Plugin.cs:185-192`/`:569`、`src/Ui/OverlayUGUI.Rows.cs:833`/`:841`。

## 3. 关键设计决定与偏离

### 3.1 两处**故意的偏离**(必须记账)

1. **A 的取词按命中形状分支**:`absorbed == 0` 的行上,已发布量**就是** `__0`(游戏口径),
   把 `实际伤害` 一律改成「超出剩余耐久(溢出)」会把**普通命中**标错。故:
   普通行印 `游戏口径 N`;只有 `absorbed > 0` 的行印 `超出剩余耐久(溢出) A · 目标剩余耐久 B(…非吸收…)`。
2. **B 的残差基数只用于文本**:`Chain.cs:269` 的 `residual` 经 `CalcBreakdown.Residual` 导出成
   `calc.residual`,并且是 `src/Diagnostics/Forensics.cs:82` 的**分桶键** —— 直接改它会移动导出数值,
   与「导出键与数值全不动」冲突。故新增局部 `residualShown`(基数 = `AbsorbWording.ResidualBasis` = 游戏口径),
   `residual` / `brk.Residual` **原样保留**。若日后要导出跟随,归 **C 轮**。

### 3.2 本轮未做项

* **C 档**:`src/Hooks/BattleObjectHooks.cs:51` 的 `(__result > 0) ? __result : __0` 一字未改。
  ⇒ **已发布的承伤数值在超量命中上仍然记的是溢出量**,`totals.absorbed` 仍是那个算术差
  (影响面:本会话 22 条 500,000 + 玩家侧 34 条;R76 记录的历史面 = 48 份导出 408 条,单次最大 269,104)。
  修订需要先定口径(落地记 0 / 记 `nominal − res` / 记 `nominal`,以及「无敌对象 0 血算不算承伤」)。
* **面板侧 D**:F6 逐条行仍看不到耐久变化(`AbsorbObservation` 有 `life=…/lifeDrop=…`,但没有任何通道把它送进 UI)。
  本轮只把判定写进**运行日志**(`[ABSPROBE] note`)。
* **导出侧残差**(见 §3.1 第 2 点)。
* `src/Composition/CompositionProbe.Text.cs:219` 的「后段倍率」是 F7 汇总「平均后段倍率」的来源,
  **不在 A/B 范围内**(改它会移动汇总读数)。

## 4. A/B 改动前后的实际输出示例

同一击(`nom = 596363`,剩余耐久 500000,`res = 96363`,`lifeDrop = 0`,`inv = 1`,`verdict = oversizedPool`):

* **改前**:`理论 191664 × 2.074 = 397575 · 实际伤害 96363 · 被吸收/无效化 500000(游戏口径 596363 = 入耐久 96363 + 被吸收 500000;游戏自身统计按 596363 计入)`
* **改后**:`理论 191664 × 2.074 = 397575 · 超出剩余耐久(溢出) 96363 · 目标剩余耐久 500000(= 游戏口径 596363 − 超出剩余耐久 96363;非吸收;是否落地看命中前后 Life,本行不断言) · 剩余倍率 ×1.500(会心/未识别部分)`
  (A 正名 + B 让 `×1.500` 回到同一行;旧办法要印这条残差得把分子写成 `96363/397575 = ×0.242`,即**判为异常倍率**。)

普通命中(`nom = res = 397575`,`absorbed = 0`):

* **改前**:`… = 397575 · 实际伤害 397575 · 剩余倍率 ×1.000(会心/未识别部分)`
* **改后**:`… = 397575 · 游戏口径 397575 · 剩余倍率 ×1.000(会心/未识别部分)`

运行日志:

* **改前**:`[DpsMeter][ABSORB] Boss:ショゴス 被吸收/无效化 500000(游戏口径 596363 = 入耐久 96363 + 吸收 500000)`
* **改后**:`[DpsMeter][OVERFLOW] Boss:ショゴス 超出剩余耐久 96363(游戏口径 596363 = 超出剩余耐久 96363 + 目标剩余耐久 500000;非吸收)`
  外加(仅日志侧 D,紧随 `[ABSPROBE] hit`):`[ABSPROBE] note verdict=oversizedPool ⇒ 耐久未变化(lifeDrop=0),无落地可观测;96363 是超出剩余耐久的溢出量,500000 只是(游戏口径 − 溢出)的模型值,非吸收。`

F7 汇总行:

* **改前**:`被吸收/无效化 3 条/1,500,000(未入耐久;游戏自身统计计入:本场游戏口径 = 伤害 + 该值)`
* **改后**:`超出剩余耐久 3 条/1,500,000(非吸收:命中值超出目标剩余耐久的部分;未入耐久,游戏自身统计按 本场游戏口径 = 伤害 + 该值 计入)`

## 5. 验证

### 5.1 行为用例(1310 → 1328)

`tests/BehaviorTests/Cases.AbsorbWording.cs`,group `policy/absorb-wording`,18 个用例:
an-oversized-row-names-the-overflow-and-the-remaining-life / a-hit-with-no-overflow-is-named-as-the-game-value /
the-no-overflow-tail-is-not-called-an-overflow / **an-oversized-crit-row-prints-the-split-and-the-crit-together**(B 的钉子:
期望串含 `· 剩余倍率 ×1.500(会心/未识别部分)`)/ a-normal-row-still-prints-both-suffixes /
the-residual-basis-is-the-game-value / **the-published-amount-would-print-a-0.242-residual**(反例钉子:
`(double)96363 / 397575 = "0.242"`)/ no-theory-means-no-residual-line / the-residual-line-alone-is-pinned /
an-oversized-pool-note-says-nothing-landed / an-oversized-note-quotes-the-life-drop-it-was-checked-against /
a-withheld-hit-is-the-only-shape-called-absorbed / a-barrier-verdict-quotes-the-barrier-life-move /
an-unreadable-barrier-move-prints-a-question-mark / an-unreadable-life-is-stated-not-guessed /
a-verdict-that-decides-nothing-gets-no-note / every-decided-verdict-gets-a-note /
the-old-word-is-used-by-exactly-one-shape。

`& 'D:\dmmplayer\dotnet-sdk6\dotnet.exe' …\tests\BehaviorTests\bin\Release\net6.0\BehaviorTests.dll --quiet`
⇒ `behavior tests: cases=1328 failed=0 pinned=1328` + `== ALL PASS ==`。

### 5.2 负控(227 → 233,6 条新变异全在 `Policy/AbsorbWording.cs`)

| 变异 | 还原的缺陷 | 必须红在 |
|---|---|---|
| `wording-normal-hit-called-actual-damage` | 普通命中重新叫「实际伤害」 | `a-hit-with-no-overflow-is-named-as-the-game-value` |
| `wording-residual-read-from-the-overflow` | 残差分子回到溢出量 | `an-oversized-crit-row-prints-the-split-and-the-crit-together` |
| `wording-residual-hidden-on-an-oversized-row` | 重建 R77 的互斥(超量行不印残差) | 同上 |
| `wording-old-word-leaks-onto-a-pool` | 旧词漏到别的形状上 | `the-old-word-is-used-by-exactly-one-shape` |
| `wording-pool-loses-its-sentence` | 决定性判定丢掉那句话 | `every-decided-verdict-gets-a-note` |
| `wording-oversized-note-quotes-the-nominal` | 叙述引错量 | `an-oversized-note-quotes-the-life-drop-it-was-checked-against` |

单跑 6/6 PASS(每条都在具名用例上变红);**全量 233 例 PASS** —— `python tests/negative_control.py`(cwd `_dpsm_work`)的结束行逐字为 **`negative control: 0 failure(s) of 233`**(2026-10-07)。

### 5.3 构建

`& 'D:\dmmplayer\dotnet-sdk6\dotnet.exe' build …\src\DpsMeter.csproj -c Release` ⇒ **0 警告 0 错误**(SDK 不在 PATH,用仓库自带
`D:\dmmplayer\dotnet-sdk6\dotnet.exe`)。

### 5.4 闸门

| 项 | 结果 |
|---|---|
| `n0_acceptance.py --out …\_dpsm_work\acceptance_r78` | **44 条命令 / 76 条检查 / 0 项不一致**(见 [`acceptance_r78`](<acceptance_r78/RESULTS.md>);`crosscheck(batch)` 退出码 1 是**预期**,已被期望表钉住) |
| `check_doc_convergence.py` | **PASS(0/12 failing)** |
| `check_docs_123.py` | `TOTAL DAMAGE MARKERS: 0` |
| `repo_manifest.py --verify --exports batch_inputs\rf0` | `drift=0 changed=1 grew=0` |
| `check_tool_registry.py` | `registered=100 active=31 unclassified=0 (pin 0) pipeline=25` → PASS |
| `refactor_final_check.py` | `blocks=0` |

**第一轮验收抓到两处红(已修,记录在案)**:`run_exit/tool_registry` = 1(`tests/_sub2_scan.py`、`tests/_sub2_scan2.py`
两个临时脚本被扫描但未登记 ⇒ 改名成 `_sub2_scan.tmp.py` / `_sub2_scan2.tmp.py` 后 PASS);`run_exit/repo_manifest_verify` = 1
(`repo_manifest.py --write` **漏了 `--exports batch_inputs\rf0`**,把语料锚到活的 `exports\` ⇒ 重跑
`--write --exports batch_inputs\rf0` 后 `drift=0`)。注意 `n0_acceptance.py` 即使有 MISMATCH 也**退出 0**
⇒ 判定必须读 `RESULTS.md`,不能只看退出码。

## 6. 部署

| 项 | 值 |
|---|---|
| 新(1.7.28) | `BepInEx\plugins\DpsMeter\DpsMeter.dll`,**512,000 B**,SHA256 **`2D9A5073468FCB5CA887E46A7AF21A173389000DEBDB190B556AFB9808ECC581`** |
| 上一版(1.7.27) | `42713774815A1A472D5E475A14B1B623AC2CD4D246C172AB55852DF907BFED0C`,509,440 B,备份 `_dpsm_work\deploy-backup\pre-r78-42713774\DpsMeter.dll` |
| 回退 | `Copy-Item _dpsm_work\deploy-backup\pre-r78-42713774\DpsMeter.dll BepInEx\plugins\DpsMeter\DpsMeter.dll -Force` |
| 历史 `.bak` | `1.0.48-crash` = `2E1819F2…`、`1.0.49-crash` = `F7FF1EB8…`(**未变**,绝不回滚) |

实机看到新文案需要**重启游戏**(并打一场)。

## 7. 未决 / 待补充

1. **C 档口径**(用户拍板):超量命中记 0 / 记 `nominal − res` / 记 `nominal`;以及「无敌对象被打 0 血算不算承伤」。
2. 面板侧 D(耐久变化进 UI)与导出侧 `calc.residual` 是否跟随(§3.1)。
3. `Debug/AbsorbProbeMaxRows`(默认 400)是否上调(观测体量选择,非本轮范围)。
4. `inv=1` 具体是哪一面旗(`AbsorbProbe` 读五旗的 OR,未逐个打印)。
5. `full=` 的实机值、F4 技能时间表 `5.0 → 5.9` 的实机确认、`ShortenedWaitTime`/`OverwriteCoolTime` 仍未读出(皆为既有遗留)。
6. 运行日志每次插件启动被**重开覆盖**(`BepInEx\config\dpsmeter_runtime.log`)⇒ 实机证据要当场取。
