# REFACTOR-BATCH-R80 —— 受击来源拆分的可用性:活过战斗结束、不再省略,并修掉一个把导出写坏的缺陷

> 版本 **1.7.30**(R80)。代码/用例/负控/守卫/部署全部完成;**不改任何已发布的数值与键名**(`contribution.schemaVersion` 仍 **1.2**);受击来源段的**自述版本**由 `takenBreakdown.schemaVersion 1.0` 升到 **1.1**(唯一差别 = 维度不再有桶上限)。
> 本文是历史批次文档:写完即冻结,后面的轮次不改写它(状态类文档才是活的)。

## 0. 本轮任务与范围

用户的要求(逐字要点,`m04760`):在已交付的 R79 / 1.7.29 上继续,修两个问题 ——

* **问题一**:受击来源只在战斗中累计、战斗一结束就自动清空,无法对上一场战斗进行分析;需让上一场的受击来源在战斗结束后仍可查看。待补充:保留几场、是否跨进程落盘、以什么时点判定一场战斗结束。
* **问题二**:受击来源省略过多,根本没法分析;需要的是**完整的受击来源逐行组成统计**,形态参照已实现的我方伤害贡献表(来源多就多行,向下无限扩展)。现有限制与此直接冲突,须一并处理:`TakenBreakdownPolicy.MaxBuckets = 64` 的折叠桶、每名受害者最多 5 条维度行、页面最多 12 名受害者、每秒最多重算一次。待补充:无限扩展后的滚动/翻页方式,以及现有 80 列对齐布局是否保留。
* **约束**:沿用项目既有做法、不无谓改动已发布数值与键名;若必须改动导出形状或 `contribution.schemaVersion` 需显式列出影响面;既有口径不变(攻击者解析不出/属性未知 ⇒ `unknown`,同队自伤 ⇒ `friendly`,`position` 首次受击快照)。待补充:F3 / Shift+F3(只看前衛)的交互是否允许改动。
* **步骤**:①先给改造方案(受影响文件、口径与导出形状变更点、逐行无限扩展的内存/性能与重算取舍)②用户确认后再实现 ③实现后报告(逐文件改动、全部闸门实测数字、明确未做的越界项、需实机跑一场才能确认的读数)。
* **验收标准**:战斗结束后仍能读到上一场战斗的完整逐行受击来源;来源数超过 64 时不再折叠,全部逐行显示。
* **边界条件**:信息缺失或数据无法获取时直接说明,不猜测、不自行设定未确认的口径。

用户对方案的拍板(逐字要点,`m04852`):

1. 表形态 **B1** —— 按受害单位分块,块内每个维度一个子表;
2. 保留范围 **A1** —— 只读 `Aggregator.History[0]`(最近一场已结束战斗),**不做跨进程落盘**;
3. **接受** `takenBreakdown.schemaVersion` **1.0 → 1.1**(`contribution` 仍 1.2);
4. F3 / Shift+F3 交互**保持**,只补 **Home/End**。

本轮范围:

* **要做的**:①`Output/TakenSession.cs` 的 `Get()` 在非战斗时回退到 `Aggregator.History[0].Session`;②删折叠与页面三个上限,改成**每个受害单位的每个维度一张子表**;③`BucketIndex` 字典索引(去掉 O(hits×buckets));④段自述升 1.1 + 导出桶键去掉「折叠行落盘成 `0`」特例 + 导出守卫同时接受 1.0/1.1;⑤**修掉 1.7.29 的六处 `JsonText.Str` 引号缺陷**并新增字节级用例;⑥滚动新增 Home/End。
* **不做的**:改已发布数值与键名;改 `contribution.schemaVersion`;`←/→` 回看更早场次;跨进程落盘;行虚拟化;**改写 1.7.29 那几份坏导出**(现场残留按「隔离原件」处置,见 §5.4 与 §7)。

## 1. 三个缺陷(用户报的两个 + 顺带发现的一个)

### 1.1 受击来源活不过战斗结束(问题一)

`Output/TakenSession.cs` 的 `Get()`(R79 原文 `:49-68`)第一件事是:

```csharp
BattleSession s = Aggregator.Session;
if (s == null) { Clear(); return null; }
```

而 `Aggregator.Session.cs:186-194` 的 `EndSession(GameResult)` 末尾是 `Session = null;`,`Aggregator.Clock.cs:103` 的 teardown 分支同样是 `FinalizeLocked(...); Session = null;` ⇒ **战斗一结束 `Get()` 必然返回 null**,F3 立刻打 `Ui/TakenPageText.cs:41-43` 的空态文案(`不在战斗中;受击来源拆分只在战斗中累积` —— R79 自己在文案里承认了这个限制)。

可复用的既有事实(全部在 R79 之前就存在):

* `Aggregator.Finalize.cs:35-42` 构造的 `BattleSummary { QuestId, Result, DurationSeconds, Session = s, Ref = s.Ref }` 持有**同一个** `BattleSession` 对象;
* `:43-51` 把 `s.OrderedActors` 与 `s.Events` 搬进 `summary`;`:54` `BattleHistoryRing.Push(History, battleSummary, BattleHistoryRing.Max)`;
* `:228` `ExportService.Export(s)` —— **导出就发生在这一刻**,所以已导出文件里的 `takenBreakdown` 是战斗结束时的快照;
* `:250` `foreach (ActorStats orderedActor4 in s.OrderedActors) orderedActor4.Source = null;` —— **只清原生引用**;`Name`/`Team`/`Position` 快照与各项累计值都留着 ⇒ 结束后对 `summary.Session` 重算完全可行;
* 保留容量:`Runtime/BattleHistoryRing.cs:23` `public const int Max = 20;`(`Push` 最新在前、丢最旧),**纯内存、无落盘**;
* 先例:`Ui/OverlayUGUI.Rows.cs:340-385` 的 `ResolveContributionView(bool useFolds)` —— 贡献表早就是这么做的(长注释:不能在战斗结束时 `ContributionSession.Invalidate()`,因为**那份缓存是已结束战斗的唯一存储**,清掉会把「上一场」变成「暂无战斗数据」)。

⇒ 修法与贡献表**同一规则**:live 用会话,否则用 `Aggregator.History[0].Session`。**不落盘**(用户选了 A1):跨进程要看受击来源就用已导出的 `takenBreakdown` 段。

### 1.2 省略过多(问题二)

| 限制 | 位置 | R80 处置 |
|---|---|---|
| 每维每单位 64 桶,超出折叠成一条(排序**之前**折叠,重尾折行会跑到最前) | `Policy/TakenBreakdownPolicy.cs:26 MaxBuckets`、`:223-239 Fold(...)`、`:29 FoldedKey` | **删除**(`FoldedKey` 与 `Fold` 一并删) |
| 每名受害者最多 5 条维度行 | `Ui/TakenPageText.cs`(`AddDimension`/`AddStatus`/`AddOther`) | 改成**每维度一张子表**,桶多就多行 |
| 页面最多 12 名受害者(其余折成 `另有 N 人`) | `Ui/TakenPageText.cs:34 ShownVictims = 12` | **删除**,受害单位循环不设上限 |
| 每行最多 3 个 part(压成「前 2 项 + `其余N项`」) | `Ui/TakenColumns.cs:43 ShownBuckets = 3` | **删除** |
| 每秒最多重算一次 | `Policy/TakenCachePolicy.cs` → `Policy/ContributionCachePolicy.cs:50 RefreshSeconds = 1.0` | 保留给**进行中**的战斗;已结束的战斗**只算一次**(新增 `live` 参数) |

折叠删除后的复杂度陷阱:`Bump(List<TakenBucket>, int key, ...)` 用**线性扫描**找同 key(命中数 × 桶数 = 二次量级)。R79 有 64 桶上限时这是有界的;去掉上限后长战斗(事件上限 `Model/BattleSession.cs:257 MaxEvents = 100000`)会退化。实测那一场页面上已经出现 `其他攻击者(252)` ⇒ 量级不是假设。⇒ **必须加索引**。

### 1.3 顺带发现的真缺陷:1.7.29 写出的导出根本不是合法 JSON

**触发**:`cd _dpsm_work; python check_export_schema.py`(无参数 = 检查线上最新一份)报

```
FILE battle_9999_20261007_132252__B-20261007-052252-195E64C816444139-001.json
UNREADABLE: Expecting value: line 1 column 2754714 (char 2754713)
```

(文件 3,705,841 B / 2,766,967 字符,mtime 2026-10-07 13:23:29,version 1.7.29。)

**根因**:文件里逐字出现 `"name":T.O.W.E.R.typeR`、`"positionLabel":前衛` —— **字符串值没有双引号**。`Output/JsonText.cs:15` 的

```csharp
internal static string Str(string t)
{
    ...
    return t.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
}
```

**只转义、不加首尾引号**。全仓 `JsonText.Str(` 共 **101 处**,除 `Output/TakenSession.cs` 外的**每一处都自己写引号**(例:`Output/Contribution.cs:756` `sb.Append(",\"name\":\"").Append(JsonText.Str(c.Name ?? "")).Append('"');`)⇒ **R79 新增的 `TakenSession.cs` 是唯一漏写的文件**,六处:`:181`(actor name)、`:185`(positionLabel)、`:207`(status)、`:208`(applier)、`:225`(bucket name)、`:228`(quality)。

**影响面与教训**:

* 凡 **1.7.29 写出的导出 JSON 全部无法被任何解析器读取**(不是「少一个字段」,是整个文件不可解析);页面读的是内存模型,所以 F3 当时看不出任何异常;
* **冻结验收语料 `batch_inputs\rf0` 的 35 份全部早于 R79、不含 `takenBreakdown` 段** ⇒ R79 的全部闸门(构建、1460 用例、244 变异、验收 44/76、docs123、doc_convergence、manifest、refactor_final_check)**对这条缺陷是盲的** —— 这是本轮最重的教训;
* 抓到它的是**线上语料**的 `check_export_schema.py`,不是冻结语料。R80 因此新增字节级用例(见 2.1 的 `Cases.TakenExport.cs`),并把 `src/Output/TakenSession.cs` 拉进行为套件的编译集 —— **「谁写出这段字节」现在有测试盯着**。

## 2. 改动清单

### 2.1 逐文件(改前 → 改后)

| 文件 | 改动 |
|---|---|
| `src/Policy/TakenBreakdownPolicy.cs` | 删 `MaxBuckets` / `FoldedKey` / `Fold(...)` / `FoldStatus(...)`;`Build` 新增 `var index = new Dictionary<int, BucketIndex>();` 并在循环内 `if (!index.TryGetValue(h.VictimKey, out bi)) { bi = new BucketIndex(); index[h.VictimKey] = bi; } Add(a, h, bi);`;`Add` 四维改走 `Bump(a.BySource, index.Source, h.Source, DamageSourceLabelPolicy.Source(h.Source), h.Nominal, record)`(Attacker/HitType/Effect 同形),状态桶经 `StatusKey(h.Status, h.StatusApplier)` + `index.Status`;新增 `private static string StatusKey(string status, string applier) => status + "\0" + (applier ?? "");`;`Bump` 改为带 `Dictionary<int,int> index` 的字典版;**`Finish` 只剩** `a.Residual = a.Nominal - a.Taken;` + 四个 `Sort` + 四个 `Quality` + `SortStatus(a)`(只排序、不折叠);文件末尾新增 `private sealed class BucketIndex { public readonly Dictionary<int,int> Source/HitType/Attacker/Effect; public readonly Dictionary<string,int> Status; }` |
| `src/Output/TakenSession.cs` | ①`Get()`:live → `Aggregator.Session`;否则 `Aggregator.History[0].Session`;两者皆无才 `Clear(); return null;`(读 summary 自己那个 `BattleSession`,因为**它正是 `ExportService` 写 `takenBreakdown` 的同一个对象**,且引用键控缓存才成立);②节流调用补 `s.InBattle` 实参;③`AppendJson` 的 `"schemaVersion":"1.0"` → **`"1.1"`**(带 R80 注释);④`AppendBuckets` 的 `sb.Append("{\"key\":").Append(b.Key)`(删掉 `b.Key == TakenBreakdownPolicy.FoldedKey ? 0 : b.Key`);⑤**六处补引号**(缺陷 1.3),并在 `:181` 前留注释说明「`JsonText.Str` 只转义不加引号 / R79 六处直写导致节不可解析 / 冻结语料早于 R79 故当时无闸门可见 / 线上语料 schema 检查抓到」 |
| `src/Policy/TakenCachePolicy.cs` | `IsStale(bool sameSession, int cachedEvents, int currentEvents, **bool live**, double cachedAt, double now, int cachedGeneration, int currentGeneration)` → `ContributionCachePolicy.IsStale(sameSession, cachedEvents, currentEvents, false, false, cachedAt, now, live ? RefreshSeconds : double.MaxValue, cachedGeneration, currentGeneration)`(已结束战斗的事件表不再变,1 s 节流只对进行中的战斗有意义;用「无节流」表达,而非假装缓存新鲜) |
| `src/Ui/TakenColumns.cs` | 保留 T1 段(`T1Position=8`/`T1Name=18`/`T1Nominal=13`/`T1Taken=13`/`T1Residual=13`/`T1Hits=6`/`T1Share=7`、`T1LineWidth = 80`、`T1[]`、`Header()`、`T1Row()`、`T1TotalsLine()`);**删** `BucketLabel`/`BucketName(常量)`/`BucketAmount`/`ShownBuckets`/`DimensionLine`/`BucketPart`/`RestPart`/`Amount` 与两个 `using`;**新增** `BName=46`、`BAmount=14`、`BHits=7`、`BShare=7`、`BLineWidth = 2+46+14+7+7 = 76`、`static readonly ColumnSpec[] B = { C("名字",BName,false), C("金额",BAmount,true), C("击数",BHits,true), C("占比",BShare,true) }`、`BHeader() = ContributionColumns.HeaderLine(B)`、`BSubHeader(string label,int count) = "  - " + label + "  " + DisplayFormat.Num(count) + " 项"`、`BRow(string name,long amount,long hits,double sharePct,bool approximate)`、`BTotal(string label,long amount,long hits)`(同形,占比格留空)、`private static string BucketName(string name,bool approximate)`(**`*` 切进名字格内** ⇒ 标记行不会比普通行宽) |
| `src/Ui/TakenPageText.cs` | 类注更新为 R80(每个受害单位、每个桶都逐行);删 `ShownVictims`;受害者循环 `for (int i = 0; i < mine.Count; i++)` 且第 2 个单位前插空行,删「另有 N 人」行;`AddDimension`/`AddStatus`/`AddOther` 换成 `AddBuckets(...)`(`BSubHeader` → `BHeader` → 每桶 `BRow(name, amount, hits, Share(amount,nominal), !string.IsNullOrEmpty(bk.Quality))` → `BTotal("合计", sum, hits)`)、`AddStatuses(...)`(名字 = `Applier` 空则 `Status`,否则 `Status + "(" + Applier + ")"`)、`AddOther(...)`(子表标签 `其他(不属于任何攻击者桶)`,行 = 同队自伤 / 攻击者不明,样式 Warn);新增 `private static double Share(long amount, long nominal)`;三条图例改成 `带 * 的名字只对该维度的部分命中权威(别的命中只认到攻击者/目标);效果=技能的 m_effectId` / `状态=命中的异常与游戏记的付与者;每一节的合计等于该单位的受击(口径)总额` / `(数字与导出的 takenBreakdown 段同源;进行中的战斗每秒最多重算一次,已结束的战斗只算一次)`(图例里刻意不再出现「其余」二字);空态第二串改为 `没有可看的受击记录(本场与上一场都没有)`(只有 live 与上一场都没有时才可达)。四个维度标签逐字:`单位(攻击者)` / `种类(DamageSource)` / `属性(eDamageCalcType)` / `效果(m_effectId)`,状态 `状态(异常/付与者)` |
| `src/Ui/OverlayUGUI.Rows.cs` | 在 `_prevSummary*` 之后新增 `_takenLinesFor` / `_takenLinesVanguardOnly` / `_takenLinesInBattle` / `_takenLines` 与 `private static List<TakenLine> ResolveTakenLines(TakenBreakdown b, bool vanguardOnly, bool inBattle)`(**三键记忆化**:breakdown 引用 + 过滤开关 + 是否 live;复用同一批字符串还能让 Unity 的 `Text.text` setter 短路);F3 分支改调 `ResolveTakenLines(TakenSession.Get(), _takenVanguardOnly, inBattle)`;标题行插入 `Home/End 首尾  ` |
| `src/Ui/OverlayUGUI.cs` | `HandleScroll()` 在方向键之后新增 Home(`GetAsyncKeyState(36)` ⇒ `_scrollOffset = 0f`)与 End(`GetAsyncKeyState(35)` ⇒ `_scrollOffset = float.MaxValue`,由既有 clamp 收成真实底部) |
| `tests/BehaviorTests/Cases.TakenBreakdown.cs` | 折叠块换成 R80 的无上限 + 索引块(9 个断言,见 5.1) |
| `tests/BehaviorTests/Cases.TakenPage.cs` | 全文重写(子表形态、宽度、`*` 标记、子表合计、15 个受害单位全出、无「其余」) |
| `tests/BehaviorTests/Cases.TakenExport.cs` | **新增**:用 `System.Text.Json` 把 `AppendJson` 的字节**读回来**再断言(20 个断言,见 5.1) |
| `tests/BehaviorTests/Stubs.cs` | `UnityEngine.Time` 增 `public static float realtimeSinceStartup;`;`DpsMeter.Aggregator` 增 `public static readonly List<BattleSummary> History = new List<BattleSummary>();` |
| `tests/BehaviorTests/BehaviorTests.csproj` | `impure but stubbed` 块追加 `<Compile Include="$(SrcRoot)\Output\TakenSession.cs" />`(末条注释注明 R80 加它**正是因为缺它**) |
| `tests/BehaviorTests/Program.cs` | 登记 `Cases.TakenExportCases(r);`;`ExpectedCases` 1439 → **1460** |
| `tests/negative_control.py` | 删 2 条目标已消失的 R79 变异,新增 6 条 R80 变异(见 5.2) |
| `check_export_schema.py` | 新增 `TAKEN_SCHEMA_110 = (1, 1)` / `TAKEN_PLUGIN_110 = (1, 7, 30)` / `TAKEN_SCHEMAS = ('1.0', '1.1')`;`_check_taken` 增加「版本必须在词表内」与「1.7.30 起必须自称 1.1」两道门;夹具 `_taken_fixture(version='1.1')`、`_taken_base()['version'] = '1.7.30'`;新增 4 个自测用例(见 5.4) |
| `src/BuildInfo.cs` / `src/DpsMeter.csproj` | 1.7.29 → **1.7.30**(两处须一致,`refactor_final_check.py` 会核对) |
| 文档 | `PROJECT-STATUS.md` / `HANDOFF.md` / `DpsMeter-文档索引.md` / `CONTRIBUTION-DATA-DICTIONARY.md`(新增 `##### R80` 段)/ `CONTRIBUTION-TABLE-REPORT.md`(现状补充)/ `PROJECT-DIRECTION-AND-IMPLEMENTATION-PLAN.md`(基线句)/ `ARCHITECTURE.md`(三处注释)/ `check_docs_123.py`(FILES)/ 本文 |

### 2.2 明确**未动**的热点(便于复核)

* `src/Hooks/BattleObjectHooks.cs` 与 `Aggregator.RecordDamage` 记账路径**一字未动** ⇒ 已发布的承伤数值不变(超量命中上 `taken` 仍记溢出量,C 档口径仍未定);
* `contribution` 段(`Output/Contribution.cs` / `Output/ContributionSession.cs` / `Output/CalcReconcile.cs`)与 `contribution.schemaVersion = 1.2` 一字未动;
* `Ui/ContributionColumns.cs` / `Ui/ContributionRowModel.cs` **一字未动**(R80 只是**照它的形态**做了受击来源的子表;两个页面的列组是各自独立的);
* `Policy/TakenBreakdownPolicy.cs` 中除 `Bump`/`Fold` 之外的分组口径(未知攻击者/同队自伤两条通道、站位首次快照、`quality` 只可能是 `""` 或 `近似 a/h`)一字未改;
* `Debug/TakenProbe` 仍**不加**配置键(理由同 R79)。

## 3. 关键设计决定与偏离

### 3.1 用「每个受害单位一个 `BucketIndex`」而不是「排序后截断」

删掉 64 桶上限后,`Bump` 的线性扫描是 O(hits × buckets)。三种做法:(a) 每维排序后只保留前 N —— **违背本轮的验收标准**(来源多要逐行全出);(b) 把 `List<TakenBucket>` 换成 `Dictionary` 并每个 tick 排序 —— 会改掉既有的稳定排序语义(`Sort` 之后同额按 key 升序)且分配更多;(c) **(`已采用`)** 保留 `List` 作为**顺序**的唯一来源,另建 `Dictionary<int,int>` 作为**定位**索引:命中 O(1) 找到槽位 ⇒ 仍是「首见顺序 + 末尾统一排序」的旧语义,导出键顺序与 R79 逐字一致。

### 3.2 非 live 传 `double.MaxValue` 而不是缩短刷新间隔

已结束战斗的 `Events` 表不再增长 ⇒ 1 s 节流对它没有意义,但也不能简单删掉节流(它是防「每帧重算」的那道闸)。做法:`TakenCachePolicy.IsStale` 增加 `live`,非 live 时把 `refreshSeconds` 传成 `double.MaxValue` ⇒ **保留缓存**,只是永远不因时间而失效(仍然因 generation/session/事件数变化而失效)。这样「一场只算一次」是缓存语义的自然结果,不是额外分支。

### 3.3 三键记忆化(页面层)

`BuildRows()` 每 0.25 s 重建一次整张行表(`Ui/OverlayUGUI.cs:271-273`),文本池 `EnsurePool(n)` 按需增长、**从不收缩**(`:18-40`)。去掉上限后行数可能上千,所以 `ResolveTakenLines` 用三个键(breakdown 引用 / 过滤开关 / 是否 live)记忆化:同一场的字符串列表只生成一次,Unity 的 `Text.text` setter 对相同字符串会短路 ⇒ 0.25 s 的重复成本被压到「比较字符串引用」。**真实的取舍**:长战斗会多占内存(每个桶一行 × 五个维度),但这是用户明确要的形态;**没做**行虚拟化(见 3.4)。

### 3.4 本轮未做项(与理由)

| 未做 | 理由 |
|---|---|
| 改已发布承伤数值 | C 档口径仍三选一未定(落地记 0 / 记 `nominal − res` / 记 `nominal`),且附带「无敌对象被打 0 血算不算承伤」 |
| `←/→` 回看更早场次 | 用户只要求「上一场」;`History` 有 20 场,但翻页交互需要单独设计(与 F3 的进入/返回语义冲突) |
| 跨进程落盘 | 用户选了 A1;已导出的 `takenBreakdown` 段**就是**落盘形态 |
| 行虚拟化 | 需要改 `OverlayUGUI.Pool` 的「只增不减」契约与滚动的坐标模型,风险面大于本轮收益 |
| 改 F3 / Shift+F3 交互 | 用户选了「保持」,只补 Home/End |

## 4. 实际输出示例

### 4.1 页面(F3)行(形态;宽度与文案由行为用例钉住)

* 受害者汇总表沿用 R79 的 80 列(`T1LineWidth = 80`,用例 `the-victim-row-width-is-pinned`);
* 每个维度一张子表,顺序:开头 `BSubHeader`(例逐字:`  - 单位(攻击者)  253 项`)→ `BHeader`(名字/金额/击数/占比,`BLineWidth = 76`)→ 每个桶一行 `BRow` → `BTotal("合计", …)` 收尾(子表合计恒等于该单位的 `nominal`,用例四个维度各钉一次);
* `*` 标记切进名字格:**近似桶**的名字格 = `ショゴス*`(例逐字),权威桶 = `ポポロット`(无标记);名字过长时先截断再补 `*`(用例 `a-marked-overlong-name-does-not-widen-the-row`);
* 「其余」二字在整页**不再可能出现**(用例 `nothing-is-folded-into-a-remainder-row` 对 5 桶 × 宽的夹具断言计数为 0);「另有」也不再出现(15 个受害单位的夹具全出,用例 `every-victim-gets-a-row` = 15、`nothing-is-dropped-from-the-page`、`no-longer-points-at-the-export-for-missing-rows`);
* 空态第二串逐字改为 `  没有可看的受击记录(本场与上一场都没有)`,原「只在战斗中累积」的说法被用例反向钉住(`outside-a-battle-the-page-no-longer-claims-it-only-accumulates-live`)。

### 4.2 导出段形状(键顺序逐字;数值为夹具)

```json
"takenBreakdown":{"schemaVersion":"1.1","method":"by-event/1","basis":"nominal",
 "hits":2,"nominal":500,"taken":200,"residual":300,"friendly":0,"friendlyHits":0,"unknown":0,"unknownHits":0,
 "actors":[{"key":4,"name":"ポポロット","team":1,"ally":true,"position":1,"positionLabel":"前衛",
   "hits":2,"nominal":500,"taken":200,"residual":300,"friendly":0,"friendlyHits":0,"unknown":0,"unknownHits":0,
   "bySource":[{"key":1,"name":"…","amount":500,"hits":2,"quality":""}],
   "byHitType":[…],"byAttacker":[{"key":9,"name":"…","amount":500,"hits":2,"quality":"近似 1/2"}],
   "byEffect":[…],
   "byStatus":[{"status":"毒","applier":"ボス(付与)","amount":…,"hits":…}]}]}
```

* 段位置与段级键顺序与 R79 **逐字一致**(产出点 `Output/ExportService.cs:787`),**唯一差别**是 `schemaVersion` 的值;
* `actors[].byAttacker[].quality` 的权威判据是 **`Attr` 的 `C:` 前缀**(`Policy/TakenBreakdownPolicy.cs:149 bool authoritative = !(h.Attr != null && h.Attr.StartsWith("C:", StringComparison.Ordinal));`),另三个维度走 `:153 bool record = h.HitMatch == 1;` —— 写用例造数据时必须知道这条,否则 `quality` 恒为空;
* 文本字段现在**一定是 JSON 字符串**(`Cases.TakenExport.cs` 递归遍历 `schemaVersion`/`method`/`basis`/`name`/`positionLabel`/`quality`/`status`/`applier`,坏值计数必须为 0,且断言「确实访问到了 ≥12 个文本字段」以防遍历写空)。

## 5. 验证

### 5.1 行为用例

`tests/BehaviorTests/BehaviorTests.exe -- --quiet` ⇒ **`cases=1460 failed=0 pinned=1460`/`== ALL PASS ==`**;用例数 **1439 → 1460**(+21),组数 **115 → 116**(新增 `export/taken-section`)。

被用例钉住的关键事实(新增/改写部分):

* **无上限**:`every-distinct-source-gets-its-own-bucket`(200 个不同来源 ⇒ 200 个桶)、`an-uncapped-dimension-still-sums-to-the-total`、`no-folded-tail-row-exists-any-more`;索引:同 key 仍并成一个桶(`a-repeated-key-still-merges-into-one-bucket`)、并进去的桶带着全部命中与金额(`the-merged-bucket-carries-every-hit` = 50、`the-merged-bucket-carries-their-amount` = 100)、**索引不跨受害单位泄漏**(`the-index-does-not-leak-across-victims`、`another-victim-keeps-its-own-buckets`、`and-its-own-amount` = 250);
* **页面**:`BLineWidth == 76 ≤ 94`、`BHeader`/`BRow`/`BTotal` 宽度各钉一次、`*` 切进名字格且不加宽、`the-sub-header-states-the-bucket-count`、`every-victim-gets-a-row` = 15、`nothing-is-folded-into-a-remainder-row`、`the-five-buckets-are-five-rows`、四个维度子表合计各等于受害单位的 `nominal`(`the-attacker-subtable-totals-the-victims-nominal` / `the-source-subtable-totals-it-too` = "300" / `the-hit-type-subtable-totals-it-too` / `the-effect-subtable-totals-it-too`)、`every-sub-table-closes-on-a-total` = 9(8 张子表 + 页面自己的 `T1TotalsLine`)、`其他` 子表合计 = "150"、三处空态新文案;
* **导出**:`the-section-parses-as-json`(解析失败时**先报** `the-reader-explains-why-not` 再 `return` 掉后面 19 个断言 —— 缺陷复现时套件会打印 `cases=1441`,这条被负控实测)、`the-schema-version-is-1-1`、`the-method-is-a-quoted-string`、`the-basis-is-a-quoted-string`、`the-section-carries-one-victim`、`the-victim-name-round-trips`、`the-position-label-is-a-quoted-string`、`the-victim-team-is-a-number`、`the-victim-position-is-a-number`、`the-victim-carries-both-hits`、`a-name-with-a-quote-and-a-newline-round-trips`(桶名 `ボス"引用"\n改行`)、`the-two-hits-merge-into-one-attacker-bucket`、`a-best-effort-bucket-carries-its-quality`(`近似 1/2`)、`the-status-is-a-quoted-string`、`the-status-applier-is-a-quoted-string`、`no-text-field-came-back-as-a-non-string`、`the-walk-actually-visited-text-fields`;
* **上一场**:`a-finished-battle-is-still-readable`(`Aggregator.Session = null` + `History` 里放 summary ⇒ `Get() != null`)、`and-it-reports-the-finished-battles-hits` = 2、`a-live-session-is-still-readable`、`with-no-battle-at-all-the-page-has-nothing`。

两次首跑红都用例自身的问题(不是产品缺陷),记在案:①`an-overlong-bucket-name-is-cut-and-still-marked` 期望 `非常に長い..*`,但 13 字的日文名(26 列)根本**没到** `BName=46` 的截断点 ⇒ 拆成「短名原样」+ 用 `new string('あ', 40)`(80 列)的三条真截断断言;②`nothing-is-folded-into-a-remainder-row` 被图例里 `(其余命中只认到攻击者/目标)` 的「其余」二字误伤 ⇒ 图例改写掉那两个字的搭配。

### 5.2 负控

删除 2 条目标已消失的 R79 变异(`taken-fold-keeps-every-bucket` —— find `if (list.Count <= MaxBuckets) return;`;`taken-page-cap-raised-by-one` —— find `public const int ShownVictims = 12;`),新增 **6** 条,逐条 `--only` 单跑**全部 PASS、红在具名用例**:

```
taken-bucket-index-never-reuses-a-bucket  -> policy/taken-breakdown/a-repeated-key-still-merges-into-one-bucket   (failed=7)
taken-page-drops-the-last-victim          -> ui/taken-page/every-victim-gets-a-row                                (failed=9)
taken-sub-header-drops-the-bucket-count   -> ui/taken-page/the-sub-header-states-the-bucket-count                 (failed=3)
taken-subtable-total-loses-its-sum        -> ui/taken-page/the-attacker-subtable-totals-the-victims-nominal       (failed=5)
taken-export-writes-unquoted-names-again  -> export/taken-section/the-section-parses-as-json                       (cases=1441)
taken-finished-battle-window-dropped      -> export/taken-section/a-finished-battle-is-still-readable
```

其中 `taken-export-writes-unquoted-names-again` **逐字复现 R79 的缺陷**(把 `sb.Append("\"name\":\"").Append(JsonText.Str(a.Name)).Append("\",");` 改回 `sb.Append("\"name\":").Append(JsonText.Str(a.Name)).Append(",");`)⇒ 这条变异是「1.7.29 的导出坏字节」在测试里的**可执行副本**。

全量:`python tests/negative_control.py`(cwd `_dpsm_work`)结束行逐字为 **`negative control: 0 failure(s) of 248`**(R79 的 244 − 2 + 6);含 `comment-only-control prose-only mutation stays GREEN`。

### 5.3 构建

* `_dpsm_work/src/DpsMeter.csproj -c Release` ⇒ **0 警告 0 错误**;
* 测试工程 ⇒ 0 错误 + **6 条既有 CS0649 警告**(`src/Runtime/OriginHeldEvent.cs` 5 条、`src/Policy/AbsorbClassifyPolicy.cs:123` 1 条,与 R75/R76 同源,本轮未碰);
* `dotnet` 在 PATH 上**不是 SDK**,必须用 `D:\dmmplayer\dotnet-sdk6\dotnet.exe`;产物在 `_dpsm_work/src/bin/Release/DpsMeter.dll`(**没有 `net6.0/` 子目录**);
* **教训(本轮新增)**:`src` 的 Release 构建**不保证可复现** —— 同一份源码两次构建得到**同尺寸、不同 SHA**(部署候选取 `121E0EBB…` 与 `214E8F10…`,都是 525,824 B)⇒ **先定稿构建、部署、再取哈希;部署之后不得重建**(否则文档里的 SHA 与实际装的 DLL 不再对应)。

### 5.4 闸门

全部在提交前跑完(2026-10-07,部署之后);cwd 除注明外都是 `_dpsm_work`:

* `python n0_acceptance.py --out D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\acceptance_r80` ⇒ **`checks: 75 ok, 1 mismatched`**(**44 条命令 / 76 条检查**),`acceptance_r80\RESULTS.md` 头逐字:`generated: 2026-10-07T14:18:18` / `exports: 35 files in D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\batch_inputs\rf0` / `source BuildInfo version: 1.7.30 ; deployed DLL sha256: 214E8F1028B552A1310399870CB4D29DE98C5EED39315A5507AC87E6AF535169` / `config sha256: 421A18C8FB8AFF3584177B33630CD02DED3EEFD3623377C61796454007A161E0`(contrib 七个开关全 true)。
* 那 **1 项**是 `run_exit/live_log expected 0 observed 1` —— **R79 缺陷的现场残留,说的是实话**:1.7.29 在 2026-10-07 13:17–13:23 写的 3 份训练场(9999)导出不是合法 JSON,读活目录的四条闸门同时被它绊倒(逐字样例:`ERROR: battle_9999_20261007_132252__B-20261007-052252-195E64C816444139-001.json is not readable JSON: Expecting value: line 1 column 2754714 (char 2754713)`;另三条同理,`test_gate`/`factsig` 直接在 `json.load` 抛 `JSONDecodeError`,`selftest/pairtrusted` 把最新那份当夹具)。**处置 = 隔离,不修复**:3 份文件(4,712,364 B / 4,143,761 B / 3,705,841 B;SHA256 `8A6F05ECC4B457A2079D63D6552BE2DB6C2DB8593FC512B61A4094873A7B6AB1` / `CF8EDD31206F783B59687EDD2FF97CFB3C479E6405B03CDBBABB0967109C960C` / `EB2FAB73B889CD0B3F52D3DC8F6842853A4E055D5A73C1A3AB36DCAC5F2D9014`)**Move-Item** 到 `_dpsm_work\quarantine-broken-1.7.29\`(原件一字未改,`README.txt` 写着根因、哈希与一行放回命令),活语料 **147 → 144 份**。取舍理由见 §7 第二条:项目把导出当证据,改字节会让文件与日志里记的 `(2766967 bytes)` 对不上,而下一场战斗自然会用 1.7.30 重写。
* 隔离后复跑:`python -m contrib.tests.test_gate` ⇒ **`gate regression: 64 passed, 0 failed`**;`python check_fact_signature.py --outdir acceptance_r80` ⇒ exit 0;`python pairtrusted_impact.py --selftest` ⇒ **`selftest: 11/11 passed`**;`check_live_log.py` 仍红但**只剩一条、且是事实**:`the log says battle_9999_20261007_132252__B-20261007-052252-195E64C816444139-001.json was exported but the file is gone (truncated corpus?)`。`LogOutput.log`(47,980 B)里只有**一次** `Chainloader initialized`、`Loading [DpsMeter 1.7.29]` ⇒ 日志每次启动被覆盖(累积的只是本会话),用户下次进游戏该格自愈;因此**不**把 `live_log` 加进 `ALLOWED_NONZERO`(那段注释的语义是「恰好一条、且 by design」,而 `:700` 一带又规定失效的免检自身要变红 —— 加进去等于制造一个将来必须清理的红)。
* `python check_doc_convergence.py` ⇒ **12/12 PASS**(R5 语料数 = 144,R9 = `acceptance_r80` 的 44 命令 / 76 检查);`python check_docs_123.py` ⇒ `files=113` / `TOTAL DAMAGE MARKERS: 0`(本文件自身 `U+FFFD=0 mojibake=0 OK`);
* `python check_export_schema.py --selftest` ⇒ **`selftest cases=91 failed=0`**(新增 1.1 词表与「1.7.30 起必须自称 1.1」门);`python check_export_schema.py`(无参数 = 活体最新一份)⇒ `checked 1 file(s), problems=1`,报的仍是 R78 遗留的 `contribution.actors[key=4] identity broken`;两份受版本控制的产物(`export_schema_report.txt` / `export_schema_selftest.txt`)已按同口径重生成;
* `python check_tool_registry.py` ⇒ `registered=100 active=31 unclassified=0 (pin 0) pipeline=25`;`python refactor_final_check.py` ⇒ `cs files 165 / CJK 14316 / dead symbol refs none / version truth BuildInfo=1.7.30 csproj=1.7.30 agree=True / blocks=0 warns=0`(守卫口径 = `src` 120 个 .cs + `tests/BehaviorTests` 40 个 = **160**);
* `python repo_manifest.py --write --exports batch_inputs\rf0` ⇒ `tracked 2776 files hashed` / `version 1.7.30 (agree=True)` / `dll 214E8F10…` / `corpus 35 exports` / `excluded 348 files hashed, 4 dirs aggregated`;`--verify --exports batch_inputs\rf0` ⇒ `manifest verify: drift=0 changed=1 grew=0 notes=0`(changed 只有 `tracked_sources/_dpsm_work/baseline-manifest.json` 自己,与 R78/R79 同形);
* 全量负控 `python tests/negative_control.py` ⇒ `negative control: 0 failure(s) of 248`;`BehaviorTests.exe -- --quiet` ⇒ `cases=1460 failed=0 pinned=1460`。

## 6. 部署

* 新:`BepInEx\plugins\DpsMeter\DpsMeter.dll` = **525,824 B**,SHA256 **`214E8F1028B552A1310399870CB4D29DE98C5EED39315A5507AC87E6AF535169`**(1.7.30,R80);
* 旧:525,312 B,SHA256 `D48E6F778FD993C452710A9F4CC7E587EE7436FED37E1A051F1075923DAF0D5C`(1.7.29,R79),已备份到 `_dpsm_work\deploy-backup\pre-r80-D48E6F77\DpsMeter.dll`;
* 回退:`Copy-Item _dpsm_work\deploy-backup\pre-r80-D48E6F77\DpsMeter.dll BepInEx\plugins\DpsMeter\DpsMeter.dll -Force`;
* 两条禁改归档部署前后逐字节一致:`DpsMeter-1.0.48-crash.bak`(`2E1819F24E45088E798F8C08D49C31B26C7902AC83887BB54AB52F20A2D31F60`)、`DpsMeter-1.0.49-crash.bak`(`F7FF1EB8A779680D472BB9881DB08982337AF6F63AC7002D745EC5E4615B7ED3`);
* 部署前确认游戏未运行(`Get-Process | ? Path -like 'D:\dmmplayer\rlyehshoujotaix_cl\*'` 为空);`BepInEx\config\dev.dpsmeter.cfg` **本轮未新增配置键**,但文件内容确实变了(它受版本控制):1.7.29 那次启动把首行 `## Settings file was created by plugin DpsMeter v1.7.28` 改写成 `… v1.7.29`,故哈希 `3E02C5D2…` → **`421A18C8FB8AFF3584177B33630CD02DED3EEFD3623377C61796454007A161E0`**(就是 `acceptance_r80\RESULTS.md` 记的 `config sha256`);首行要等用户用 1.7.30 进一次游戏才会变成 `v1.7.30`。

## 7. 未决 / 待补充

* **实机读数(需要用户跑一场)**:①战斗**结束后**按 F3 是否还能看到上一场的完整逐行来源(本轮的核心验收);②来源多的场次里滚动是否够用、Home/End 是否好按;③1.7.30 起导出的 `takenBreakdown` 段能否被外部工具解析(缺陷已修,但实机样本才是终证);④训练场(9999)与 `filterFriendlyFire=true` 场次里的呈现。
* **C 档口径三选一**(改已发布承伤数值):落地记 0 / 记 `nominal − res` / 记 `nominal`;附带「无敌对象被打 0 血算不算承伤」。
* **1.7.29 的历史导出**:它们**全部**不合法(六处漏引号),活目录里那 3 份已隔离到 `_dpsm_work\quarantine-broken-1.7.29\`(原件一字未改,README 有根因、哈希与一行放回命令;该目录不在 `.gitignore` 的 include 名单里,故不进 Git)。隔离后活语料 **144 份里 `"version":"1.7.29"` 与 `"takenBreakdown"` 各 0 份** ⇒ **R80 的 1.1 形状在活语料里没有任何样本**(冻结的 35 份也全早于 R79),它只被 `Cases.TakenExport.cs` 的字节级回读与 `check_export_schema.py --selftest` 钉住 —— 要真样本,得等用户用 1.7.30 打一场(本轮的核心实机请求)。R80 **不改写历史文件**(那会动语料、也会与日志里记的字节数对不上);要用旧文件请把原件放回并自行补引号,或重跑那一场。
* R75 遗留:`Debug/AbsorbProbe` 的 `full=` 实机值;`Debug/AbsorbProbeMaxRows` 默认值是否上调。
* R76 遗留:`inv=1` 是五个标志的 OR,未逐个打印;`8 hits / 4,000,000` 是否把同一次结算计两遍(`src/Policy/AttributionPolicy.cs:81-82`)。
* 更早遗留:`Barrier.mLife` 真实类型、谁执行 `DamageCut`;F4 技能时间表那一格 `5.0 → 5.9` 的实机确认。
* R79 遗留(与 R80 无关但活体语料里仍在):`contribution.actors[key=4] identity broken`(`base+self+received` ≠ `direct`)那一份导出。
