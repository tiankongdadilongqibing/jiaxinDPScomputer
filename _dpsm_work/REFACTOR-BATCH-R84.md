# REFACTOR-BATCH-R84 —— 受击来源拆分页(F3):一页一个角色 + 表上方的角色列表

> 版本 **1.7.34**(R84)。只改**受击来源拆分页(F3)的显示与交互形状**:这一页不再把队伍里每个受害单位连着自己的六张子表一路印下去,而是在表格**上方**印出**角色列表**(名字多则多行、每个名字一段、可点),表格里**只印被选中的那一个角色**;点列表里的名字即可换人。键盘行为**一字未动**(`F3` 返回、`Shift+F3` 只看前衛、`Home`/`End` 首尾照旧,也没有新增任何按键)。导出格式/数值/口径/段标签/配置键全未动(`contribution.schemaVersion` 仍 **1.2**,`takenBreakdown` 仍 **1.1**)。
> 本文是历史批次文档:写完即冻结,后面的轮次不改写它(状态类文档才是活的)。

## 0. 本轮任务与范围

用户原话(m07052):

> 当前查看受击来源的话多角色要滚动很久,能不能默认一页只显示一个角色的数据然后加个按钮用来切换所查看角色?

我回问了两点(切换控件长什么样 / 是否保留一个「全部角色」的入口),用户答复(逐字):

> 在上方列出角色列表,鼠标点击切换,默认展示第一个角色
> 不要,永远只显示一个角色

要做:

- 表格**上方**印**角色列表**:每个我方受害角色一段文字,鼠标点得到;
- **默认显示第一个角色**(列表按游戏口径 nominal 降序,第一个就是最大的受害单位);
- 表格只印被选中那一个角色的 `T1Row` + 六张子表(单位(攻击者)/种类(DamageSource)/属性(eDamageCalcType)/效果(m_effectId)/状态(异常/付与者)/其他);
- 列表要能容下**全部**受害角色(单角色视图下它是唯一入口,不能只列前几个);
- 页面自己的合计行改名 `全队合计`。

不做:

- 不加键盘键,F3 页原有键位与行为一字不改(沿 R82 的做法:交互只多一条鼠标通路);
- 不保留「全部角色」入口(用户明确否掉);
- 不动任何桶/子表的内容与折叠规则(R80 的「不折叠」仍然成立——每个人仍然一条桶一行);
- 不动导出格式、口径、`schemaVersion`、段落标签、配色、配置键;
- 不动 F3 之外的任何页面。

## 1. 为什么选中态存 `Key` 而不是下标,为什么列表必须列全部

**下标会在两次刷新之间偷偷换人。** 受害单位列表是按**游戏口径 nominal 降序**排的(`_dpsm_work/src/Model/TakenBreakdown.cs` 的 `Actors`),而进行中的战斗每秒都可能重排:老四挨了一发大的,就会从第 4 位跳到第 1 位。如果选中态存的是「第 3 个」,读者点开的是 A,下一秒屏幕上却是 B——而且**没有任何一行字会说换人了**。所以 R84 的选中态存 `TakenActor.Key`(`_dpsm_work/src/Model/TakenBreakdown.cs:57`,游戏给的单位 id),列表项提交的也是**列表里的位置**,由 `ActTakenActor` 在点下去的那一刻把位置翻译成 `Key`。位置变了、人没变,页面就还看着同一个人;那个人真的走了(不再出现在列表里),才回落到第一个。

**列表必须列全部,不能只列前几个。** 这一页现在一次只显示一个角色,列表是**唯一**的换人入口。R80 已经定过一条规矩:这一页不折叠、不省略,不让人去翻导出文件(见 R80 批文档)。列表要是只列前 5 个,第 6 个角色就**没有路**可以走到——那正是 R80 要消灭的情况。所以列表列全部受害角色,按每行 `TakenActorPerRow = 5` 折行(纯文本层量不到字体宽度,只能定一个常数;标题因此**独占一行**,见 §3)。

## 2. 改动清单

| 文件 | 改动 |
| --- | --- |
| `_dpsm_work/src/Ui/TakenPageText.cs` | 页面形状:表格只印选中的那一个角色(原先遍历 `mine` 的整段换成 `if (pick >= 0) { … }`);新增 `Victims(b, vanguardOnly)`(我方 + 前衛过滤,`null` 安全)、`SelectedIndex(mine, selectedKey)`(空 → `-1`,命中 `Key` → 该下标,否则 `0`)、`AddActorList(lines, mine, pick)`(标题行 + 每 5 个名字一行);`TakenLine` 加 `List<HotkeySeg> Segments`(只有角色列表的名字行非空);`Lines` 签名加第 4 个参数 `int selectedKey`;标题行加 `;单角色视图`;合计行改 `TakenColumns.T1TotalsLine("全队合计", …)` |
| `_dpsm_work/src/Ui/HotkeyBarText.cs` | `HotkeySeg` 加 `public readonly int Arg`(两参构造转为委托三参构造);枚举加 `TakenActor = 17`(唯一一个**不是按键**的成员:列表每个名字一段,没有哪个键能命名「第三个」);新增 `TakenActorPerRow = 5`、`TakenActorIndent = "  "`、`TakenActorLabel(selectedNumber, total)` → `角色 3/15(点名字切换)`、`TakenActorWrap()`、`TakenActorEntry(name, index, selected)` → `(选中 ? "▶ " : "  ") + name + "  "` |
| `_dpsm_work/src/Ui/TakenColumns.cs` | `T1TotalsLine` 加首个参数 `label`,行首改成 `"  " + PadR(Fit(Cell(label), T1Position + T1Name), T1Position + T1Name)`;四列数值与占位一字未动 |
| `_dpsm_work/src/Ui/OverlayUGUI.cs` | `HotkeyHit` 加 `Arg`;`TryHotkeyClick()` 改调 `DispatchHotkey(hit.Action, hit.Arg)`;`DispatchHotkey(HotkeyAction action, int arg)` 加 `case HotkeyAction.TakenActor: ActTakenActor(arg);`;新增 `private static int _takenActorKey;` 与 `internal static void ActTakenActor(int index)`(越界即忽略,否则 `_takenActorKey = mine[index].Key`);`ActF3(bool shift)` 的**进入页面**分支加 `_takenActorKey = 0;`(默认第一个角色;`Shift+F3` 翻过滤时不清,能命中就还是同一个人) |
| `_dpsm_work/src/Ui/OverlayUGUI.Rows.cs` | `ResolveTakenLines` 的记忆化从「三件事」变「四件事」(加 `_takenLinesActorKey`),并透传 `selectedKey`;F3 分支的调用点传 `_takenActorKey`;行构造带上 `Segments = taken[i].Segments`;可点段的 `HotkeyHit` 登记加 `Arg = seg.Arg` |
| `_dpsm_work/src/BuildInfo.cs` / `_dpsm_work/src/DpsMeter.csproj` | `Version` 1.7.33 → **1.7.34** |
| `_dpsm_work/tests/BehaviorTests/Cases.TakenPage.cs` | 全部 `Lines(...)` 调用改四参;两处「一页印两个人」的断言换成「只印一个」;新增 R84 小节 19 条具名用例 + 表格形状 1 条;新增 9 个测试辅助函数(`TkEntries`/`TkEntryActions`/`TkEntryArgs`/`TkEntryNames`/`TkMarked`/`TkEntryName`/`TkNameRows`/`TkMaxEntriesPerRow`/`TkCellOf`) |
| `_dpsm_work/tests/BehaviorTests/Program.cs` | `ExpectedCases` 1506 → **1526**,注释块加一行 R84 |
| `_dpsm_work/tests/negative_control.py` | 新增 4 条 R84 变异(261 → 265) |
| `_dpsm_work/check_docs_123.py` | `FILES` 加 `_dpsm_work/REFACTOR-BATCH-R84.md` |
| 7 份状态类文档 | 版本 / DLL 尺寸与 SHA256 / 用例数与组数 / 变异数 / 语料数收敛(见 §5.5) |

## 3. 关键设计决定与偏离

1. **选中态存 `TakenActor.Key`(游戏单位 id),不存下标** —— 理由见 §1。
2. **列表列全部受害角色**,只按每行 5 个折行 —— 理由见 §1。
3. **标题独占一行,名字行统一以两空格缩进**。渲染层是按**实测**宽度给每段排版的,而标题宽度随 `角色 1/6` 与 `角色 12/15` 变化——想让名字行与标题左右对齐,靠数字符是算不出来的。改成:标题一行(`角色 3/15(点名字切换)`,暗色),名字行每行都以同一个 `"  "` 缩进开头(亮色,即 R82 热键条那种「看着就能点」的颜色)。这样所有名字行的左边缘完全一致,标题变宽也不会挤到名字。
4. **名字行用热键条的 `Header` 样式**。这一页其它行是等宽字体、各自配色;列表行故意做成和上/下热键条一样,因为它们的交互与那几条完全一样(点一下就切视图)。选中项用 `▶` 标记,标记写在**可点段内部**(`▶ 味方0`),这样「亮起来的那一个」与「点下去会切到的那一个」是同一个字符串,不会出现「标记在别处」。
5. **`合计` → `全队合计`**。这一行一直是**全部我方**的求和(与只看前衛过滤无关、与单角色视图无关),而在单角色视图下,它上面只有一个角色的一块,裸 `合计` 会被读成「这个角色自己受击 350」。这是 R83 同一条教训的延续:一个格子不能读成它不代表的东西。(R83 的 `every-sub-table-closes-on-a-total` 计数因此从 5 变 4:全队合计行不再含子串 `  合计`,只有子表的收尾行含。)
6. **不新增键盘键**。用户要的是「按钮」,而这一页的键位已经满了(F3 返回 / Shift+F3 过滤 / Home / End),再塞换人键就要动既有键盘行为。R82 已经把「一行文字 = 可点按钮」这条路铺好了,所以 R84 只加鼠标通路:F3 页的键盘行为与 R83 逐字相同。
7. **`Segments` 为 `null` 的行走老渲染路径**。除角色列表的名字行之外,页面每一行的 `Segments` 都是 `null`,渲染层于是走 R83 的单 `Text` 路径(写文本 + 排版)——所以这一次改动**不可能**弄脏其它任何一行:它们的字符串、颜色、等宽字体、点击登记一个字节都没变。
8. **记忆化要跟着多一个键**。`ResolveTakenLines` 原来按 (数据, 只看前衛, 是否战斗中) 三件事缓存;现在页面还取决于「选中的是谁」,所以缓存键加 `_takenLinesActorKey`。少加这一个键的后果不是崩溃,而是**点了名字页面不变**。

## 4. 屏上前后对照

下面两段用的是测试夹具的真实字符串(`TkSample()` = レヴァナント(key 1,前衛,300)+ 城塞(key 3,後衛,50),外加一个敌方受害单位;`TkManyVictims(15)` = `味方0`..`味方14`)。段落间距是**逐字**的:每个条目自带前后空格(`▶ ` 或 `  ` + 名字 + 两个空格),不依赖字体测量。

**改前**(每个我方受害单位连着自己的六张子表往下印,6 个人要滚 6 段):

```
【受击角色】(仅我方;按游戏口径降序)
  前衛 レヴァナント   … 300 的六张子表 …
  後衛 城塞           … 50 的六张子表 …
  合计   …
```

**改后**(列表在表上方,表格只留选中的那一个):

```
【受击角色】(仅我方;按游戏口径降序;单角色视图)
角色 1/2(点名字切换)
  ▶ レヴァナント    城塞  
  前衛 レヴァナント   … 只这一个的六张子表 …
  全队合计   350   …
```

**改后 · 15 个角色**(每 5 个一行,共 3 行;选中项带 `▶`):

```
角色 1/15(点名字切换)
  ▶ 味方0    味方1    味方2    味方3    味方4  
    味方5    味方6    味方7    味方8    味方9  
    味方10   味方11   味方12   味方13   味方14  
```

点 `味方2`:列表变成 `角色 3/15(点名字切换)`、`▶` 移到 `味方2`,表格换成味方2 的那一块;点 `味方0` 再回来。选中的 key 在这一场里消失了(人退场/不再挨打),下一次刷新自动回落到列表第一个,并且列表第一行会写清现在看的是谁。

## 5. 验证

### 5.1 用例(1506 → **1526**,+20;组数 117 不变)

R84 新增 19 条在 `ui/taken-page` 的角色列表小节:

`the-character-list-offers-one-entry-per-character`、`the-character-list-submits-each-entry-as-the-character-action`、`the-character-list-passes-each-entries-position`、`the-character-list-names-every-character`、`the-character-list-marks-the-character-on-screen`、`and-the-marker-follows-the-selection`、`the-list-states-which-character-is-on-screen`、`the-character-list-wraps-after-five-names`、`no-name-row-carries-more-than-five-entries`、`an-unknown-selection-lands-on-the-first-character`、`an-unknown-selection-shows-the-first-character`、`an-unknown-selection-says-so-in-the-list`、`the-totals-row-is-labelled-as-the-teams-total`、`and-that-row-keeps-the-whole-team-amount`、`the-victim-list-is-our-side-only`、`the-victim-list-follows-the-front-filter`、`an-empty-victim-list-selects-nothing`、`an-empty-page-offers-no-character-entries`、`and-no-line-of-an-empty-page-carries-segments`。

加上表格形状那一条新的 `and-that-one-is-the-first-entry-by-default`(默认看的就是列表第一个、也就是最大的受害单位),净增 20。另外三条是**换名/换义**(不增计数):`the-table-shows-one-character-at-a-time`(替代原来的「两个我方都印」)、`the-table-still-shows-exactly-one-of-them`(替代 `every-victim-gets-a-row`)、`the-other-characters-subtable-totals-its-own-nominal`(改成显式选第二个角色 `Lines(TkSample(), false, true, 3)`,原来靠「它印在下面」)。

### 5.2 负控(261 → **265**)

| 变异名 | 文件 | find → repl | 变红的用例 |
| --- | --- | --- | --- |
| `taken-character-list-drops-the-selected-marker` | `Ui/HotkeyBarText.cs` | `(selected ? "▶ " : "  ") + (name ?? "") + "  "` → `(name ?? "") + "  "` | `ui/taken-page/the-character-list-marks-the-character-on-screen`(红 2 条) |
| `taken-selection-falls-back-to-the-last` | `Ui/TakenPageText.cs` | `\t\treturn 0;` → `\t\treturn mine.Count - 1;` | `ui/taken-page/an-unknown-selection-shows-the-first-character`(红 8 条) |
| `taken-totals-row-called-just-total` | `Ui/TakenPageText.cs` | `T1TotalsLine("全队合计", …)` → `T1TotalsLine("合计", …)` | `ui/taken-page/the-totals-row-is-labelled-as-the-teams-total`(红 3 条) |
| `taken-character-list-wraps-at-fifty` | `Ui/HotkeyBarText.cs` | `TakenActorPerRow = 5` → `50` | `ui/taken-page/the-character-list-wraps-after-five-names`(红 2 条) |

**本轮踩到的坑(值得留下)**:`taken-selection-falls-back-to-the-last` 第一版被判「变红了,但红在别的用例上」。原因是 `SelectedIndex` 里那句 `return 0;` **同时**服务两种输入:「未知的 key(999)」与「进页面时的 `selectedKey = 0`(还没选)」。把它改成 `mine.Count - 1` 时,先红的是 `and-that-one-is-the-first-entry-by-default`(默认页换人了),而当时那条「未知 key」用例只断言了**行数**(单角色视图下行数恒为 1)所以根本没红。修法不是改变异,而是让用例说实话:改名为 `an-unknown-selection-shows-the-first-character`,并断言**落到谁**(`TkCellOf(fallback, "  前衛", 1) == "レヴァナント"`)。教训:**回退分支的用例必须断言「落到谁」,只断言「有几行」不会红。**

**两条旧变异必须跟着改指向(本轮全量负控首跑的 2 条 `[FAIL]`)**。R84 把页面形状换了,两条 R79/R80 期留下的变异虽然照样「变红」,但红的是**另一件事**:

- `taken-page-vanguard-filter-always-off` 原本期望 `ui/taken-page/the-vanguard-filter-keeps-only-the-front-row`,实际红在 `ui/taken-page/the-victim-list-follows-the-front-filter`(`got=2 want=1`)。原因:单角色视图下表格恒为 1 行,旧用例对「过滤被关掉」已经**瞎**;真正抓住它的是 R84 新加的列表条目数。⇒ `expect` 改成实际抓住它的那条(find/repl 不动)。
- `taken-page-drops-the-last-victim` 原本期望 `ui/taken-page/every-victim-gets-a-row`,而这条用例已被 R84 删除/改名;它的 `find` 串 `\t\tfor (int i = 0; i < mine.Count; i++)` 现在落在 `SelectedIndex` 的**按 key 查找**循环里(`src/Ui/TakenPageText.cs:257`),语义从「列出每个受害单位」变成「查表跳过最后一个角色」,于是红在 `the-other-characters-subtable-totals-its-own-nominal`(`got=[300] want=[50]`)。⇒ 改名 `taken-character-list-drops-the-last-name`,改去钉列表自身的装填循环(`for (int i = start; i < end; i++)` → `i < end - 1`,`src/Ui/TakenPageText.cs:293`;15 个名字掉成 12 个),`expect` = `ui/taken-page/the-character-list-offers-one-entry-per-character`(已删用例在 R84 的后继)。

两条都单独 `--only` 复验过(红在具名用例,`failed=4` / `failed=1`),变异总数 265 不变。**教训:改名或删掉一条用例时,必须把所有指向它的变异的 `expect` 一起改,并且要确认变异的 `find` 是否因为重构落进了别的函数** —— 否则它照样变红,红的是另一件事,而全量负控只能告诉你「红在错的用例」。

### 5.3 构建

- 插件:`D:\dmmplayer\dotnet-sdk6\dotnet.exe build _dpsm_work\src\DpsMeter.csproj -c Release` → **0 警告 / 0 错误**;产物 `_dpsm_work\src\bin\Release\DpsMeter.dll` = **532,992 B**,SHA256 **`7C8634263827160C3763BA303179710B87B200B3F50A1704E040D959C34AEF52`**(2026-10-07 20:51:53;此后只改测试与文档,src 未再动)。
- 测试工程:`dotnet build tests\BehaviorTests\BehaviorTests.csproj -c Release` → **0 错误**(6 条既有 `CS0649` 警告,与 R81/R82/R83 相同)。
- 行为套件:`tests\BehaviorTests\bin\Release\net6.0\BehaviorTests.exe -- --quiet` → `cases=1526 failed=0 pinned=1526` / `== ALL PASS ==`。

### 5.4 全套闸门

| 闸门 | 命令(cwd = `_dpsm_work`) | 实测 |
| --- | --- | --- |
| 插件构建 | `dotnet build src/DpsMeter.csproj -c Release` | **0 警告 / 0 错误** |
| 行为套件 | `tests\BehaviorTests\bin\Release\net6.0\BehaviorTests.exe -- --quiet` | `cases=1526 failed=0 pinned=1526` / `== ALL PASS ==` |
| 全量负控 | `python tests\negative_control.py` | **0 失败 / 265 条**(首跑 2 条红在错用例,改指向后复跑;日志 `tests\_r84_negctl_full2.tmp.txt`,结束时 `comment-only-control` GREEN) |
| 文档 123 | `python check_docs_123.py` | `files=117`(含本轮登记的 `REFACTOR-BATCH-R84.md`)、`TOTAL DAMAGE MARKERS: 0`、全表 `mojibake=0 / U+FFFD=0` |
| 文档收敛 | `python check_doc_convergence.py` | **PASS(0/12 failing)**;R1 六份文档都写 `1.7.34`、R3 四份都写 `7C863426…`(部署后)、R5 活语料 153 份、R9 认最新归档 = `acceptance_r84` |
| 工具注册表 | `python check_tool_registry.py` | PASS(`registered=100 active=31 unclassified=0 pin=0 pipeline=25`) |
| 终局检查 | `python refactor_final_check.py` | `cs files 167` / `CJK characters 14783` / `dead symbol refs none` / `version truth : BuildInfo=1.7.34 csproj=1.7.34 agree=True` / `blocks=0 warns=0` |
| 仓库清单 | `python repo_manifest.py --write --exports batch_inputs\rf0` 后 `--verify --exports batch_inputs\rf0` | 写入 `baseline-manifest.json`(398,191 B):`version 1.7.34 (agree=True)`、`dll 7C863426…`、`rollback BepInEx/plugins/DpsMeter/DpsMeter.dll.1.7.10.bak`、`tracked 2783 files hashed`、`corpus 35 exports`、`excluded 348 files hashed, 4 dirs aggregated`;校验:`drift=0 changed=1 grew=0 notes=0`(changed 恒为清单自身)。**清单在验收前写过一次(那时 `dll 03D021F9…`、文档还没写验收数字),部署后又写了一次** —— 现在记录的是线上件哈希;新批文档未跟踪,两次都不进 `tracked`。 |
| 终验收 | `python n0_acceptance.py --out D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\acceptance_r84` | **44 条命令 / 76 条检查 / 0 项不一致**(生成 `2026-10-07T21:21:14`;`RESULTS.md` 头 = exports 35 份、`source BuildInfo version: 1.7.34`、`deployed DLL sha256: 03D021F9…`(**部署前**那一份)、`config sha256: 4ACE7EA5…`) |

### 5.5 回填记录

- **数字来源**:用例数由 `tests/BehaviorTests/Program.cs` 的 `ExpectedCases` 钉住(套件自报 `pinned=1526`,改前 1506);组数 117(源码里 `r.Group("` 出现 118 次,`extract/pending` 在 `tests/BehaviorTests/Cases.Extraction.cs:326` 与 `:490` 各一次);变异数 = `tests/negative_control.py` 的条目数(261 → 265),全量跑的输出在 `tests\_r84_negctl_full2.tmp.txt`;源码 `121 个 .cs / 32,627 行` 与线上语料 153 份由 `tests\_r84_measure.tmp.py` 实测(只数 `src` 下的 .cs,不含 `obj/`、`bin/`);闸门输出见 `tests\_r84_build.tmp.txt`、`_r84_tbuild.tmp.txt`、本文件 §5.4 各命令。
- **文档同步分两遍**(都是「每处 `count(old) == 1` 才写,任一处不符就整批不写」):`tests\_r84_docs.tmp.py` 21 处(版本、DLL 链、配置链、用例/组/变异数、语料数、七份文档里的 R84 段落),`tests\_r84_docs2.tmp.py` 2 处(验收数字只知道在跑完之后)。HANDOFF 是 CRLF,插入文本按该文件既有行尾替换。
- **三个锚点坑**(下次直接照做):①`PROJECT-STATUS.md:34` 的 DLL 行结尾是 `pre-r83-93E6C827\`)` —— **反斜杠+反引号**,锚串漏掉反斜杠就 `count=0`;②`ARCHITECTURE.md:82` 的目标行以 `;桶子表…` 开头(**分号**,不是空格);③`PROJECT-DIRECTION-AND-IMPLEMENTATION-PLAN.md:4` 里那句「线上 `exports` 已 152 份」落在 **R79 段**(历史叙述)里,本轮**故意不动**它 —— 当前语料数由 status 的「写本文时已 153 份」与 index 的版头承担。
- **配置被上一版自己重写过**:`BepInEx\config\dev.dpsmeter.cfg` 现 28,692 B / `4ACE7EA530539B7E24D0DD83B61570821AC393C3D80DDFB2B4160C7C3460ABC5`,首行 `## Settings file was created by plugin DpsMeter v1.7.33`,mtime 2026-10-07 20:32 ⇒ 用户用 **R83 的线上件**进过游戏;键集一字未动。文档里因此多出一段历史链(`3F0C5802…`(1.7.32)/ `12BABD45…`(1.7.31)/ `B7DBE35A…`(1.7.30))。
- **顺序上的教训**:本轮把文档改完 → `repo_manifest --write` → 终验收,所以 `acceptance_r84` **首跑即 0 项不一致**(R81 那次是先验收后改文档,才需要复跑)。负控与验收**从不并行**(两者都会写 `tests/BehaviorTests/bin/Release/net6.0/BehaviorTests.exe`)。
- 历史批文档 `REFACTOR-BATCH-R80/R81/R82/R83.md` 一字未动;`baseline-manifest.json` 只登记 git 已跟踪文件,所以本轮新批文档要等提交后重写清单才会进 `tracked`。

## 6. 部署

- 前置:替换前查占用 = `LOCK-TEST: free`(当时只有 5 个 `DMMGamePlayer` 启动器进程,没有游戏本体跑着)。
- 备份:`Copy-Item BepInEx\plugins\DpsMeter\DpsMeter.dll _dpsm_work\deploy-backup\pre-r84-03D021F9\DpsMeter.dll` ⇒ 备份件 = **531,456 B / `03D021F99817A0208C9628E84FB1D0A3C8DB9F4516DF9F25776C3208943F87B3`**(1.7.33,R83)。
- 替换后线上件 = **532,992 B / `7C8634263827160C3763BA303179710B87B200B3F50A1704E040D959C34AEF52`**(与 `_dpsm_work\src\bin\Release\DpsMeter.dll` 同哈希)。
- 回退(一条命令):`Copy-Item _dpsm_work\deploy-backup\pre-r84-03D021F9\DpsMeter.dll BepInEx\plugins\DpsMeter\DpsMeter.dll -Force`。
- 两个禁改件复核未动:`BepInEx\plugins\DpsMeter\DpsMeter-1.0.48-crash.bak` = 171,008 B / `2E1819F24E45088E798F8C08D49C31B26C7902AC83887BB54AB52F20A2D31F60`;`…-1.0.49-crash.bak` = 171,520 B / `F7FF1EB8A779680D472BB9881DB08982337AF6F63AC7002D745EC5E4615B7ED3`。
- 配置与语料:配置本轮未动(仍是 `4ACE7EA5…`,1.7.33 写的);线上 `BepInEx\plugins\DpsMeter\exports\battle_*.json` = **153 份**。
- 导出契约未动:没有改 schemaVersion、字段名、口径或任何配置键 —— 本轮只动 F3 页的页面形状与鼠标通路。

## 7. 未决 / 待补充

- **实机确认**(需要用户打一场):①名单好不好点(名字短、行距 16px,点错一个就是看错人);②`▶` 与非选中项的两空格前缀在实机字号下是否分得清;③单角色视图是否真的解决了「滚很久」;④列表折行到 2 行时会不会把表格挤下可视区。
- 列表目前**没有拖拽/滚轮之外的导航**:角色多于 5 个就要点,不能键盘跳。用户明确不要键盘键,所以先这样;若实机觉得点太多次,再议。
- 选中态**不跨场保留**:`F3` 重新进入页面时回到列表第一个(`ActF3` 的进入分支把 `_takenActorKey` 清 0)。如果用户希望「记住上一场看的那个人」,那是另一个决定。
- `SelectedIndex` 的回落语义只有「未知 key → 第一个」;「选中的 key 与另一个角色同名」在模型层由 `Key` 区分,不在此列。
