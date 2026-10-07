# REFACTOR-BATCH-R81 —— 受击来源拆分页的分节配色:每个子表标签行一种鲜艳颜色

> 版本 **1.7.31**(R81)。只改**显示层**(F3「受击来源拆分」页的标签行颜色):**页面字符串一字未改**,导出形状与所有已发布数值/键名一字未动(`contribution.schemaVersion` 仍 **1.2**,`takenBreakdown.schemaVersion` 仍 **1.1**),列宽/行序/滚动/Home/End 全部不变。
> 本文是历史批次文档:写完即冻结,后面的轮次不改写它(状态类文档才是活的)。

## 0. 本轮任务与范围

用户的要求(逐字,`m05741`,配图 `m05740`):

> 能否在我用箭头指到的这几个表格行各用不同的鲜艳颜色标识一下,现在这样全部置灰并不方便观看

配图上箭头指向 R80 成型页面里的五条**子表标签行**:`单位(攻击者) 6 项`、`种类(DamageSource) 2 项`、`属性(eDamageCalcType) 4 项`、`效果(m_effectId) 8 项`、`状态(异常/付与者) 6 项`。

本轮范围:

* **要做的**:①每个子表标签行带一个「这一段是哪个维度」的标签;②渲染层按该标签给六种鲜艳颜色(含第六行 `其他(不属于任何攻击者桶)`);③用例钉住「只有标签行被染色、六种标签两两不同」;④负控钉住这两条。
* **不做的**:改任何页面字符串/列宽/行序;改导出形状、键名或任何数值;改滚动与 Home/End 的行为;改受害单位/子表的行数;新增配置键(配色不可调);给标签行加图例说明颜色含义。

## 1. 为什么这些行是灰的

R79 起,子表标签行由 `src/Ui/TakenPageText.cs` 的 `Add(...)` 以 `TakenLineStyle.Dim` 追加;渲染侧 `src/Ui/OverlayUGUI.Rows.cs` 的 `TakenColor(TakenLineStyle style)` 只按三种样式映射 —— `Header` → `HeaderColor`(琥珀 `1f, 0.83f, 0.45f`)、`Warn` → `WarnColor`(橙 `1f, 0.55f, 0.35f`)、其余 → `AllyColor`(`0.85f, 0.93f, 1f`)—— 而 `Dim` 落在默认分支 ⇒ **一律 `DimColor` 灰 `0.62f, 0.66f, 0.72f`**(`src/Ui/OverlayUGUI.cs:143`)。

R80 删掉折叠与三个上限之后,一个受害单位可以有 200+ 行(实测页面里出现过 `其他攻击者(252)` 那种量级),而子表标签行是那一段里**唯一的界标** ⇒ 全灰时无法快速定位(这正是用户报的现象)。

## 2. 改动清单

| 文件 | 改动 |
|---|---|
| `src/Ui/TakenPageText.cs` | 新增 `internal enum TakenBlock { None = 0, Attacker = 1, HitType = 2, Attr = 3, Effect = 4, Status = 5, Other = 6 }`;`TakenLine` 增加 `public TakenBlock Block;`;`Add(List<TakenLine> lines, string text, TakenLineStyle style, TakenBlock block = TakenBlock.None)` 写入该字段;`AddBuckets(List<TakenLine> lines, string label, List<TakenBucket> buckets, long nominal, TakenBlock block)` 把 block 传给标签行;四个维度的调用点分别传 `TakenBlock.Attacker` / `HitType` / `Attr` / `Effect`;`AddStatuses` 的标签行传 `TakenBlock.Status`;`AddOther` 的标签行传 `TakenBlock.Other`。**页面字符串、列宽、行序一字未改**(类注释加了一段 R81 说明) |
| `src/Ui/OverlayUGUI.cs` | `DimColor` 之后新增六个调色常量(`TakenAttackerColor` / `TakenHitTypeColor` / `TakenAttrColor` / `TakenEffectColor` / `TakenStatusColor` / `TakenOtherColor`)+ R81 注释(说明为什么不复用 header 琥珀与 warn 橙) |
| `src/Ui/OverlayUGUI.Rows.cs` | `TakenColor(TakenLineStyle style)` → **`TakenColor(TakenLine line)`**(`Header`→`HeaderColor`、`Warn`→`WarnColor`、`Row`→`AllyColor`、否则 `TakenBlockColor(line.Block)`);新增 `TakenBlockColor(TakenBlock)` 映射六色、`None` 回 `DimColor`;行表构造处改成 `Color = TakenColor(taken[i])` |
| `tests/BehaviorTests/Cases.TakenPage.cs` | +9 用例(组 `ui/taken-page`),新助手 `TkBlockOf(lines, subHeader)` / `TkTaggedRows(lines)` / `TkDistinctTags()` |
| `tests/BehaviorTests/Program.cs` | `ExpectedCases` **1460 → 1469** |
| `src/BuildInfo.cs` / `src/DpsMeter.csproj` | 版本 **1.7.30 → 1.7.31**(两处必须一致) |
| `tests/negative_control.py` | +5 条 R81 变异(**248 → 253**) |

## 3. 关键设计决定与偏离

* **3.1 用枚举标签,不在渲染侧按文本前缀匹配。** 标签字符串含中文与括号(`单位(攻击者)`、`种类(DamageSource)`),拿它们当判据等于把**文案**升级成协议:以后改一个字就会静默改配色,而用例会一起改,红不了。改成 `TakenBlock` 后,文案与分段是两个独立事实。
* **3.2 颜色不写进页面层。** `src/Ui/TakenPageText.cs` 是**纯文件**(`tests/BehaviorTests` 直接编译并执行它),它只能知道「这一段属于哪个维度」;颜色属于渲染层,留在 `src/Ui/OverlayUGUI.cs` 的常量里。这样 +9 条用例能在**不需要 Unity** 的前提下钉住分段与「只有标签行有标签」。
* **3.3 六色都避开既有语义色。** header 琥珀 `1/0.83/0.45` 与 warn 橙 `1/0.55/0.35` 已在用,新六色取青(`0.3/1/1`)、黄绿(`0.62/1/0.3`)、品红(`1/0.35/0.95`)、蓝(`0.45/0.65/1`)、粉(`1/0.5/0.75`)、红(`1/0.35/0.35`),两两可分辨且不撞既有色。
* **3.4 只有标签行被染色。** 每个桶的普通行仍走 `AllyColor`、合计行同理;`ui/taken-page/only-the-section-label-rows-are-tagged`(=8:TkSample 有 2 个我方受害单位 × 4 个维度)把这条钉死。
* **3.5 偏离说明:没有加配置键、没有改图例。** 用户只要求「各用不同的鲜艳颜色」;把配色做成可配置会新增配置键与文档口径,而颜色是否够用要等实机反馈再决定(见 §7)。

## 4. 实际颜色表

| 子表标签行 | 段落标签 | 颜色 RGB |
|---|---|---|
| `单位(攻击者) N 项` | `TakenBlock.Attacker` | `0.3, 1, 1`(青) |
| `种类(DamageSource) N 项` | `TakenBlock.HitType` | `0.62, 1, 0.3`(黄绿) |
| `属性(eDamageCalcType) N 项` | `TakenBlock.Attr` | `1, 0.35, 0.95`(品红) |
| `效果(m_effectId) N 项` | `TakenBlock.Effect` | `0.45, 0.65, 1`(蓝) |
| `状态(异常/付与者) N 项` | `TakenBlock.Status` | `1, 0.5, 0.75`(粉) |
| `其他(不属于任何攻击者桶) N 项` | `TakenBlock.Other` | `1, 0.35, 0.35`(红) |

## 5. 验证

### 5.1 用例

`behavior tests: cases=1469 failed=0 pinned=1469` / `== ALL PASS ==`(**1460 → 1469**;组数 **116 不变**,9 条全落在既有组 `ui/taken-page`)。新增断言名:

`ui/taken-page/the-attacker-label-row-carries-its-section`、`the-source-label-row-carries-its-section`、`the-hit-type-label-row-carries-its-section`、`the-effect-label-row-carries-its-section`、`the-odd-label-row-carries-its-section`、`the-status-label-row-carries-its-section`(自带夹具:`Status = "毒"`、`StatusApplier = "ボス"`)、`only-the-section-label-rows-are-tagged`(=8)、`the-headline-is-not-tagged-as-a-section`、`the-six-sections-plus-none-are-seven-distinct-tags`。

首跑 `cases=1469 failed=1 pinned=1460` —— 唯一红是 pin 自身(`cases/pinned-total`),改 `Program.cs` 的 `ExpectedCases` 后全绿。

### 5.2 负控(248 → 253)

| 变异 | find | expect(必须红在) |
|---|---|---|
| `taken-section-tag-dropped` | `Add(lines, TakenColumns.BSubHeader(label, buckets.Count), TakenLineStyle.Dim, block);` → 去掉 `, block` | `ui/taken-page/the-attacker-label-row-carries-its-section` |
| `taken-sections-share-one-tag` | 属性那行传 `TakenBlock.HitType` | `ui/taken-page/the-hit-type-label-row-carries-its-section` |
| `taken-status-label-mis-tagged` | 状态标签行传 `TakenBlock.Attacker` | `ui/taken-page/the-status-label-row-carries-its-section` |
| `taken-two-tags-share-a-value` | 枚举 `Status = 5,` → `Status = 4,` | `ui/taken-page/the-six-sections-plus-none-are-seven-distinct-tags` |
| `taken-bucket-rows-get-tagged-too` | `BRow(...)` 的 `Add` 末尾加 `, block` | `ui/taken-page/only-the-section-label-rows-are-tagged` |

逐条 `python tests\negative_control.py --only <name>` 实测:五条全部 `[PASS] … went red`(第一条同时带红 5 个用例,因为丢掉 block 后所有标签行一起变灰)。

### 5.3 构建

* 插件:`dotnet build _dpsm_work\src\DpsMeter.csproj -c Release` = **0 警告 / 0 错误**;
* 测试工程:0 错误 / **6 个既有 CS0649 警告**(`src/Runtime/OriginHeldEvent.cs` 5 条 + `src/Policy/AbsorbClassifyPolicy.cs:123` 1 条,均非本轮引入)。

### 5.4 全套闸门

* **终验收**:`python n0_acceptance.py --out acceptance_r81` = **`checks: 76 ok, 0 mismatched`**(44 条命令 / 76 条检查)。[`acceptance_r81/RESULTS.md`](<acceptance_r81/RESULTS.md>) 头四行逐字:`generated: 2026-10-07T17:23:10` / `exports: 35 files in ...\batch_inputs\rf0` / `source BuildInfo version: 1.7.31 ; deployed DLL sha256: 2846C151E21272EDDC2EF59A3D1173E563E10889FEBE519F8218FE5A6214C9FE` / `config sha256: B7DBE35A0A8052D5CF9B6448C022D74888E30421A4C38E8E896D13F57ACCCE93`(contrib switches 七项全 true)。**首跑**(文档刚改、`baseline-manifest.json` 还没重写)是 `checks: 75 ok, 1 mismatched`,唯一红 = `run_exit/repo_manifest_verify`;manifest 重写后复跑即 0。
* **R80 那 1 项已自愈**:`acceptance_r80` 的唯一红 `run_exit/live_log` 本轮 expected 0 / observed 0 —— `LogOutput.log` 被 1.7.30 那场覆盖后不再指名已隔离的 1.7.29 坏导出(与 R80 当时的预测一致,见 §7)。
* `check_doc_convergence.py` = **12/12 PASS**(`version=1.7.31 schema=1.2 exports=145 (BepInEx/plugins/DpsMeter/exports) sha=2846C151`;R5 = 145 份,R9 现在对 `acceptance_r81`)。
* `check_docs_123.py` = `files=114` / `TOTAL DAMAGE MARKERS: 0`(新增本文件;本文件自身 `U+FFFD=0 mojibake=0 OK` —— **故意不引用 bytes/cjk**,回填会立刻改变它们)。
* `check_tool_registry.py` = `registered=100 active=31 unclassified=0 (pin 0) pipeline=25` / PASS。
* `refactor_final_check.py` = `cs files 165` / `CJK characters 14365` / `dead symbol refs none` / `BuildInfo=1.7.31 csproj=1.7.31 agree=True` / `blocks=0 warns=0`(cs 文件数与 R80 相同:本轮不新增 src 文件)。
* `repo_manifest.py --write --exports batch_inputs\rf0` → `wrote ...baseline-manifest.json (397636 bytes, 9.9s)` / `version 1.7.31 (agree=True)` / `dll 2846C151...` / `tracked 2778 files hashed` / `corpus 35 exports` / `excluded 348 files hashed, 4 dirs aggregated`;`--verify --exports batch_inputs\rf0` → **`drift=0 changed=1 grew=0 notes=0`**(changed 只有 `tracked_sources/_dpsm_work/baseline-manifest.json`)。
* **负控**:全量 `negative control: 0 failure(s) of 253`(exit 0);5 条新变异逐条 `--only` 全 `[PASS] ... went red`,且每行都带 `cases=1469 failed=... pinned=1469`。
* **行为套件**:`cases=1469 failed=0 pinned=1469` / `== ALL PASS ==`。

### 5.5 回填记录

* 2026-10-07:§5.4 的数字取自 `acceptance_r81`(复跑那次,`76 ok / 0 mismatched`)与同批的 `check_doc_convergence` / `check_docs_123` / `check_tool_registry` / `refactor_final_check` / `repo_manifest`,以及全量负控与行为套件;§6 的部署数字取自替换前后的实测。
* §5.4 不引用本文件自身的 bytes/cjk:回填会立刻让它们过期。

## 6. 部署

* **备份(替换前)**:`Copy-Item BepInEx\plugins\DpsMeter\DpsMeter.dll _dpsm_work\deploy-backup\pre-r81-214E8F10\DpsMeter.dll` —— 备份件 = 525,824 B / `214E8F1028B552A1310399870CB4D29DE98C5EED39315A5507AC87E6AF535169`(即 1.7.30 的线上件)。
* **替换前的锁测试**:`LOCK-TEST: free`(只有 `DMMGamePlayer` 启动器进程在跑,插件 DLL 未被占用)。
* **线上(替换后)**:`BepInEx\plugins\DpsMeter\DpsMeter.dll` = **526,848 B**,SHA256 **`2846C151E21272EDDC2EF59A3D1173E563E10889FEBE519F8218FE5A6214C9FE`**(与 `_dpsm_work\src\bin\Release\DpsMeter.dll` 同哈希)。两个禁改备份复核未动:`DpsMeter-1.0.48-crash.bak` = 171,008 B / mtime 2026-09-27 18:33:03 / `2E1819F24E45088E798F8C08D49C31B26C7902AC83887BB54AB52F20A2D31F60`;`DpsMeter-1.0.49-crash.bak` = 171,520 B / mtime 2026-09-27 18:38:32 / `F7FF1EB8A779680D472BB9881DB08982337AF6F63AC7002D745EC5E4615B7ED3`。
* **回退**:`Copy-Item _dpsm_work\deploy-backup\pre-r81-214E8F10\DpsMeter.dll BepInEx\plugins\DpsMeter\DpsMeter.dll -Force`。
* **配置**:**本轮未新增配置键**;`BepInEx\config\dev.dpsmeter.cfg` 仍是 1.7.30 启动时重写的那一份(`B7DBE35A0A8052D5CF9B6448C022D74888E30421A4C38E8E896D13F57ACCCE93`,首行 `## Settings file was created by plugin DpsMeter v1.7.30`),要等用户用 1.7.31 进一次游戏才会把首行版本号改成 `v1.7.31`(键集不变)。
* **生效条件**:重启游戏(热重载不保证);新颜色在**新一场**或重开 F3 页后观察 —— R81 只改渲染层,页面数据仍来自 `takenBreakdown` 的同一份模型。

## 7. 未决 / 待补充

* **实机读数(需要用户跑一场)**:六种颜色在实机上是否够区分、是否与半透明面板背景/暴击等既有着色撞色。本轮只做了离线分段与颜色映射的正确性,没有量化对比度。
* **用户 `m05731`「好的,就按你推荐的方案进行实施」的指代仍未澄清**:本轮**没有**把它理解为「修订已发布承伤数值」那一档(那是会移动已发布数字的改动,必须先由用户定口径:落地记 `0` / 记 `nominal − res` / 记 `nominal`)。
* 配色**不可配置**(无新配置键);若实机反馈颜色不够,下一轮再决定是调色还是做成配置项。
