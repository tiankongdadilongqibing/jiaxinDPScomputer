# REFACTOR-BATCH-R79 —— 受击来源拆分:每个单位挨的伤害分别来自谁/什么

> 版本 **1.7.29**(R79)。代码/用例/负控/守卫/部署全部完成;**不改任何已发布的数值与键名**(`contribution.schemaVersion` 仍 **1.2**)。
> 本文是历史批次文档:写完即冻结,后面的轮次不改写它(状态类文档才是活的)。

## 0. 本轮任务与范围

用户的要求(逐字,`m03889`):按方案实现「各角色受击伤害来源分析」。

本轮范围(由我在实施前确认):

* **要做的**:①受害侧事件投影(每击一行 `TakenHit`,金额取**游戏口径** `Nominal`);②五个**各自完整划分同一笔 `nominal`** 的维度(`byAttacker` / `bySource` / `byHitType` / `byEffect` / `byStatus`);③三条恒等式**(行为用例 + 导出守卫双重钉住)**;④F3 页面(80 列 / 最多 12 个单位 / 每单位至多 5 条维度行 / `Shift+F3` 只看前衛);⑤**新顶层导出段** `takenBreakdown`(`schemaVersion 1.0` / `method by-event/1` / `basis nominal`)。
* **不做的**:改 `contribution` 段与任何已发布数值;改 `contribution.schemaVersion`(仍 1.2);改 `src/Hooks/BattleObjectHooks.cs`(C 档,需先定口径);改判定层(`src/Policy/AbsorbClassifyPolicy.cs`);新增 `Debug/TakenProbe` 配置键;F6 逐条行的受击拆分。

## 1. 为什么需要它

前四轮(R75–R78)把「被吸收/无效化」这个词**正名**完了,但工程里**只有「谁打出了多少」的账**(`contribution` 段),没有「**谁挨了多少、由谁造成**」的账:F6 逐条行只按单位汇总,看不出某一击的受害者 / 来源 / 属性 / 效果 / 状态,更看不出「攻击者不明」与「同队自伤」各占多少。

已有的一手基础:

* R75/R76 的 `[ABSPROBE]` 探针给出 397 行 boss 读数(**全 `inv=1`**)与 378 行玩家读数(**全 `inv=0`**),证明 boss 的 500,000 是 `Life` 恒定池、不是吸收;
* R78 已把 `nominal − res` 命名为「目标剩余耐久」⇒ 本轮的 `residual` 可以**直接沿用**这个名字与口径,不必重新论证;
* 数据来源只用 `Session.Events` 里 `Type="dmg"` 的事件,**不新增任何 Harmony 钩子** ⇒ 运行期风险面为零;唯一新的运行期读取是「站位」,而且只在**每个受害者首次受击**时读一次。

## 2. 改动清单

### 2.1 新增文件(7 个 src 文件 / 933 行)

| 文件 | 行数 | 作用 |
|---|---|---|
| `src/Policy/DamageSourceLabelPolicy.cs` | 61 | `Source(int)` / `IsHealSource(int)` / `HitType(int)` —— 标签的**唯一实现**(与 `CompositionProbe.Text.cs` 原来的字符串逐字一致) |
| `src/Policy/TakenBreakdownPolicy.cs` | 242 | 分组、折叠、排序、`Quality` 标记;`MaxBuckets = 64`、`FoldedKey = int.MinValue` |
| `src/Policy/TakenCachePolicy.cs` | 20 | 「每秒最多重算一次」的判定(委派 `ContributionCachePolicy.IsStale`) |
| `src/Model/TakenBreakdown.cs` | 122 | `TakenBreakdown` / `TakenActor` / `TakenBucket` / `TakenStatus` / `TakenHit` |
| `src/Output/TakenSession.cs` | 198 | 段级缓存 + `AppendJson`(导出形状) |
| `src/Ui/TakenColumns.cs` | 124 | 80 列 T1 行 / 维度行 / 桶 part 的列宽与拼接 |
| `src/Ui/TakenPageText.cs` | 166 | F3 页面的行列表(纯模块,`List<TakenLine>`) |

新增用例文件:`tests/BehaviorTests/Cases.TakenBreakdown.cs`(204 行,组 `policy/taken-breakdown`)、`tests/BehaviorTests/Cases.TakenPage.cs`(172 行,组 `ui/taken-page`)。

### 2.2 逐文件改动

| 文件:行 | 改前 → 改后 |
|---|---|
| `src/Output/ExportService.cs` | `…,"contribution":{…}` 之后、根对象 `}` 之前 → 新增 `sb.Append(",\"takenBreakdown\":"); TakenSession.AppendJson(sb, s);`(`contribution` 段的键、值、`schemaVersion` 一字未动) |
| `src/Composition/CompositionProbe.Text.cs` | `SrcName`/`IsHealSource`/`HitTypeName` 三个方法的方法体 → 改为 `return DamageSourceLabelPolicy.Source(v);` 等(**输出的字符串逐字未变**;该文件不纯,不能进行为套件) |
| `src/Ui/OverlayUGUI.Rows.cs` | `LayoutCharts` 的面板条件 `View == ViewMode.Contribution` → `View == ViewMode.Contribution \|\| View == ViewMode.Taken`(共用 880 px;T1 行 80 列、维度行约 86 列) |
| `src/Ui/OverlayUGUI.Rows.cs` | 新增 `private static Color TakenColor(TakenLineStyle style)`(Header→HeaderColor、Warn→WarnColor、Row→AllyColor、其余→DimColor) |
| `src/Ui/OverlayUGUI.Rows.cs` | `BuildRows()` 的 `// ---- roster ----` 之前 → 新增 Taken 分支:标题行 `受击来源拆分  F3返回  Shift+F3 <只看前衛>` → `AppendBattleRefRow(...)` → `TakenPageText.Lines(TakenSession.Get(), _takenVanguardOnly, inBattle)` 逐行成 `RowDef` → 从 `firstRow` 起套等宽字体 |
| `src/Ui/OverlayUGUI.Rows.cs` | 两条提示行 → 各加 `F3 受击来源` / `F3受击来源` |
| `src/Ui/OverlayUGUI.cs` | `_prevF7` 之后 → 新增 `private static bool _takenVanguardOnly;`;F3 处理器先读 `(GetAsyncKeyState(16) & 0x8000) != 0`,已在 `ViewMode.Taken` 时 Shift 反转过滤、否则照旧返回上一视图 |
| `tests/BehaviorTests/BehaviorTests.csproj` | 登记 6 个新纯文件与 2 个新用例文件(`Detection`/`SDL` 通配符不覆盖它们) |
| `tests/BehaviorTests/Program.cs` | `Cases.AbsorbWordingCases(r);` 之后 → `Cases.TakenBreakdownCases(r); Cases.TakenPageCases(r);`;`ExpectedCases` **1328 → 1418** |

### 2.3 明确**未动**的热点(便于复核)

* `src/Hooks/BattleObjectHooks.cs:51` —— 一字未改 ⇒ 超量命中上已发布的 `taken` **仍记溢出量**;
* `src/Aggregator.Stats.cs` 的 `RecordDamage` / `RecordDamageNow` —— 一字未改(没有新增运行期读取);
* `src/Policy/AbsorbClassifyPolicy.cs`、`src/Diagnostics/AbsorbProbe.cs` —— 判定层与探针未动;
* `src/Output/Contribution*.cs`、`Output/ContributionSession.cs` —— `contribution` 段未动。

## 3. 关键设计决定与偏离

### 3.1 两处故意的偏离(已记账)

1. **不加 `Debug/TakenProbe` 配置键**。导出段 `takenBreakdown` 已经含同样而且更全的数字;新增键要动配置契约(`Plugin.cs` 的绑定)、`check_export_schema.py` 的 `CONFIG_KEYS`、以及四份状态文档的配置行 —— 收益(在日志里多看几行)不抵这个面。
2. **标签搬进纯策略层**。`CompositionProbe.Text.cs` 不纯(`UnityEngine`/`BattleObject`/`GameRef`),行为套件编译不了它;把三个标签方法搬进 `src/Policy/DamageSourceLabelPolicy.cs` 并让原方法**委派**,字符串逐字不变,于是「标签只有一处定义」可以被用例钉住(负控里有两条就打在标签上)。

### 3.2 本轮未做项(与理由)

* **改已发布承伤数值(C 档)**:影响面是「本轮 21 条 + 历史 408 条、单次最大 269,104」,且「无敌对象被打 0 血算不算承伤」是**口径问题**,必须先由用户三选一:落地记 0 / 记 `nominal − res` / 记 `nominal`;
* **F6 逐条行的受击拆分**:本轮只做单位汇总(页面第 4 条维度行已经能回答大部分问题),逐条行要新列宽与滚动策略;
* **面板侧显示目标耐久变化**:需要新增跨层通道(R78 已记过同一条)。

### 3.3 一处实现教训:折叠发生在排序**之前**

`TakenBreakdownPolicy` 的顺序是 `Add → Finish(residual) → Fold(四类桶) → Sort → Quality → FoldStatus`。于是**重尾的折叠行会排到最前**(用例数据里折叠桶 4 击 > 其余各 1 击)。我第一版用例断言「折叠行在最后」,首跑即红(`got=62 want=-2147483648`),改成按 `TakenBreakdownPolicy.FoldedKey` 定位后才稳定 —— 这条记在用例注释里,免得后人再按"尾巴在最后"去读导出的 `actors[].bySource[0]`。

## 4. 实际输出示例

### 4.1 页面(F3)行(逐字,取自行为用例钉住的期望串)

```
击 4   受击(游戏口径) 450   已发布 450   超出剩余耐久 0
  我方 350   敌方 100(1 人未列入下表)   同队自伤 0   攻击者不明 0
  站位(该单位首次受击时的快照,之后移动不改写)  前衛 300   後衛 50   站位未知 0

【受击角色】(仅我方;按游戏口径降序)
  口径:受击=游戏自己记账的承伤;已发布=本插件记账;超出剩余耐久=前者-后者,不是被吸收
  站位    单位              受击(口径)   已发布       超出        击数  占比
  前衛    レヴァナント            300        300          0     2   66.7%
  - 单位    レヴァナント 200 / 敵 100
  - 种类    直接攻击 200 / 毒 100
  - 属性    物理 300
  - 效果    效果77 300
  - 状态    毒(レヴァナント) 100
```

三条宽度断言同时钉住:表头、单位行、合计行**恰好 80 显示列**;最坏三-part 维度行 ≤ 94;超长名字不撑宽(截断成 `..`)。全页 `被吸收` 只出现 **1** 次,就在上面那句否定口径里。

`attackers` 解析不出来的那一档的期望串(击数放在被 `Fit` 截断的名字**之外**,否则名字里塞不下):

```
  - 其他    攻击者不明 50(1 击)
```

**实机读数待补**:本轮的页面数字来自用例夹具;实机(新一场)的 F3 截图与 `takenBreakdown` 段要等用户跑一场后补进 §7。

### 4.2 导出段形状(键顺序逐字;数值为夹具)

```json
,"takenBreakdown":{"schemaVersion":"1.0","method":"by-event/1","basis":"nominal",
  "hits":3,"nominal":350,"taken":240,"residual":110,"friendly":100,"friendlyHits":1,"unknown":50,"unknownHits":1,
  "actors":[
    {"key":1,"name":"Alpha","team":1,"ally":true,"position":1,"positionLabel":"前衛",
     "hits":2,"nominal":300,"taken":240,"residual":60,"friendly":100,"friendlyHits":1,"unknown":0,"unknownHits":0,
     "bySource":[{"key":0,"name":"直接攻击","amount":100,"hits":1,"quality":""},
                 {"key":3,"name":"毒","amount":200,"hits":1,"quality":""}],
     "byHitType":[{"key":1,"name":"物理","amount":300,"hits":2,"quality":"近似 1/2"}],
     "byAttacker":[{"key":2,"name":"敵","amount":200,"hits":1,"quality":""}],
     "byEffect":[{"key":77,"name":"效果77","amount":300,"hits":2,"quality":""}],
     "byStatus":[{"status":"毒","applier":"敵","amount":100,"hits":1}]},
    {"key":2,"name":"Bravo","team":2,"ally":false,"position":0,"positionLabel":"站位未知",
     "hits":1,"nominal":50,"taken":0,"residual":50,"friendly":0,"friendlyHits":0,"unknown":50,"unknownHits":1,
     "bySource":[],"byHitType":[],"byAttacker":[],"byEffect":[],"byStatus":[]}]}
```

`positionLabel` 是导出**附带**字段,守卫的键表不要求它(`_check_keys` 只校验声明过的键,多余键容忍)。

## 5. 验证

### 5.1 行为用例

`tests/BehaviorTests/BehaviorTests.exe -- --quiet` ⇒ **`cases=1418 failed=0 pinned=1418 == ALL PASS ==`**;用例数 **1328 → 1418**(+90),运行期**实测不同组名 115**(R78 文档里的 112 是旧数)。新增两组:`policy/taken-breakdown`、`ui/taken-page`。

被用例钉住的关键事实:五个维度**各自**等于同一笔 `nominal`;`nominal == taken + residual`;`ΣbyAttacker + friendly + unknown == nominal`;未知攻击者**不进任何单位桶**;友伤进来源维度但不进单位桶;`quality` 只可能是 `""` 或 `近似 a/h`;折叠后维度总额仍等于 `nominal`;站位用**首次**读数(后到的 `0` 不覆盖先到的 `1`);表头/单位行/合计行恰好 80 列;`被吸收` 全页只出现 1 次且那次是否定句;两个过滤器;12 行上限(`另有 N 人`);三种空态;`攻击者不明 50(1 击)`。

### 5.2 负控

新增 **11** 条变异(`tests/negative_control.py`),逐条实测**红在具名用例**上,例如:

```
taken-residual-drops-the-published        -> policy/taken-breakdown/the-residual-is-nominal-minus-published
taken-page-row-widens-by-one              -> ui/taken-page/the-victim-row-width-is-pinned
taken-page-drops-the-denial               -> ui/taken-page/and-that-one-mention-is-the-denial
```

全量:`python tests/negative_control.py`(cwd `_dpsm_work`)结束行逐字为 **`negative control: 0 failure(s) of 244`**(R78 的 233 + 本轮的 11);含 `comment-only-control prose-only mutation stays GREEN`。每次变异运行时套件都打印 `cases=1418 …`,证明用例总数与 1.7.29 源码一致。

一条教训(与 3.3 同类):变异必须**真的改变**用例结果。给 `AttackerUnknown` 写的第一版变异(`h.AttackerKey == 0` → `< 0`)**套件全绿**,因为夹具的命中同时 `Attacker == ""`,被下一条 `string.IsNullOrEmpty` 子句兜住;改成 `else if (AttackerUnknown(h))` → `else if (false)` 才咬住。

### 5.3 构建

* `_dpsm_work/src/DpsMeter.csproj -c Release` ⇒ **0 警告 0 错误**;
* 测试工程 ⇒ 0 错误 + **6 条既有 CS0649 警告**(`src/Runtime/OriginHeldEvent.cs` 5 条、`src/Policy/AbsorbClassifyPolicy.cs:123` 1 条,与 R75/R76 同源,本轮未碰);
* 注意:`dotnet` 在 PATH 上**不是 SDK**,必须用 `D:\dmmplayer\dotnet-sdk6\dotnet.exe`;产物在 `_dpsm_work/src/bin/Release/DpsMeter.dll`(**没有 `net6.0/` 子目录**,csproj 设了 `AppendTargetFrameworkToOutputPath=false`)。

### 5.4 闸门

| 闸门 | 结果 |
|---|---|
| `check_doc_convergence.py`(cwd `_dpsm_work`) | **12/12 PASS**(version=1.7.29 / sha=D48E6F77 / exports=144) |
| `check_export_schema.py --selftest` | **selftest cases=88 failed=0**(新增 11 条 takenBreakdown 用例) |
| `check_export_schema.py`(活体最新一份) | `problems=1` —— 该问题在 R79 之前就存在(`contribution.actors[key=4] identity broken`,文件 `battle_411001_…-001.json`,version 1.7.28),**与 R79 无关**(用 `HEAD` 的守卫脚本对同一文件也得 `problems=1`);验收走冻结快照,不受影响 |
| `check_docs_123.py` | `files=112` / `TOTAL DAMAGE MARKERS: 0`(exit 0);新批次文档已扫到:`REFACTOR-BATCH-R79.md bytes=16784 cjk=2221 U+FFFD=0 mojibake=0 OK` |
| `check_tool_registry.py` | `tool registry: registered=100 active=31 unclassified=0 (pin 0) pipeline=25` ⇒ PASS(exit 0) |
| `repo_manifest.py --write --exports batch_inputs\rf0` | `wrote …baseline-manifest.json (396241 bytes)`;`version 1.7.29 (agree=True)` / `dll D48E6F77…` / `tracked 2766` / `corpus 35` |
| `repo_manifest.py --verify --exports batch_inputs\rf0` | **`drift=0 changed=1 grew=0 notes=0`**(exit 0);changed 仅 `tracked_sources/_dpsm_work/baseline-manifest.json`。(写 `--write` 时**必须带 `--exports batch_inputs\rf0`**,否则语料被锚到活的 `exports\` —— R78 踩过) |
| `refactor_final_check.py`(cwd `_dpsm_work`) | `cs files 164` / `CJK characters 14163` / `dead symbol refs: none` / `version truth BuildInfo=1.7.29 csproj=1.7.29 agree=True` / `refactor check: blocks=0 warns=0`(exit 0) |
| `n0_acceptance.py --out D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\acceptance_r79` | **`checks: 76 ok, 0 mismatched`**(exit 0);归档 `acceptance_r79/{RESULTS.md,runs.json,corpus_manifest.json,runs/*.txt}`,`runs=44` / `checks=76`;唯一非零 run = `crosscheck(batch) exit=1`,与 `acceptance_r78` 逐条一致(该 run 在期望表里被钉成 1,属设计) |

## 6. 部署

* 新:`BepInEx\plugins\DpsMeter\DpsMeter.dll` = **525,312 B**,SHA256 **`D48E6F778FD993C452710A9F4CC7E587EE7436FED37E1A051F1075923DAF0D5C`**(1.7.29,R79);
* 旧:512,000 B,SHA256 `2D9A5073468FCB5CA887E46A7AF21A173389000DEBDB190B556AFB9808ECC581`(1.7.28,R78),已备份到 `_dpsm_work\deploy-backup\pre-r79-2D9A5073\DpsMeter.dll`;
* 回退:`Copy-Item _dpsm_work\deploy-backup\pre-r79-2D9A5073\DpsMeter.dll BepInEx\plugins\DpsMeter\DpsMeter.dll -Force`;
* 两条禁改归档部署前后逐字节一致:`DpsMeter-1.0.48-crash.bak`(171,008 B / `2E1819F2…`)、`DpsMeter-1.0.49-crash.bak`(171,520 B / `F7FF1EB8…`);
* 部署时无游戏进程占用(只有 `DMMGamePlayer` 启动器);`BepInEx\config\dev.dpsmeter.cfg` 的改动来自游戏自己启动时重写首行版本号(既有惯例,R79 **未新增配置键**)。

## 7. 未决 / 待补充

* **实机读数(需要用户跑一场)**:F3 页面在真实战斗里的数字与 80 列对齐;导出 `takenBreakdown` 段的实机样本;训练场(9999)与 `filterFriendlyFire=true` 场次里的呈现。
* **C 档口径三选一**(改已发布承伤数值):落地记 0 / 记 `nominal − res` / 记 `nominal`;附带问题「无敌对象被打 0 血算不算承伤」。
* **F6 逐条行的受击拆分**(本轮只做单位汇总)。
* R75 遗留:`Debug/AbsorbProbe` 的 `full=` 实机值;`Debug/AbsorbProbeMaxRows` 默认值是否上调。
* R76 遗留:`inv=1` 是五个标志的 OR,未逐个打印;`8 hits / 4,000,000` 是否把同一次结算计两遍(`src/Policy/AttributionPolicy.cs:81-82`)。
* 更早遗留:`Barrier.mLife` 真实类型、谁执行 `DamageCut`;F4 技能时间表那一格 `5.0 → 5.9` 的实机确认(R74 的修正是否真的生效)。
* R78 遗留(与本轮无关但活体语料里仍在):`contribution.actors[key=4] identity broken`(`base+self+received` ≠ `direct`)那一份导出。
