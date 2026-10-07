# REFACTOR-BATCH-R82 —— 快捷键行可点击:F3 与其他程序冲突时的鼠标通路

> 版本 **1.7.32**(R82)。只改**输入通路**:面板上那些「F3 受击来源 / F5 贡献 / Shift+F3 只看前衛 / Home/End 首尾 / ←/→ 翻页」条目现在**可以用鼠标点**;**键盘行为一字未改**,不新增配置键,导出形状与所有已发布数值/键名一字未动(`contribution.schemaVersion` 仍 **1.2**,`takenBreakdown` 仍 **1.1**)。
> 本文是历史批次文档:写完即冻结,后面的轮次不改写它(状态类文档才是活的)。

## 0. 本轮任务与范围

用户的要求(逐字,`m06201`):

> 当前f3和我电脑上其他按键冲突了,能不能改一下按键,或是将上方的fx快捷键切换视图全部改为鼠标点击切换?

因为「改键」与「加鼠标」是两条不同代价的路(改键会让用户已有的习惯与文档全部失效),本轮先问后做(`ask_user_question` 四选项),用户在 `m06253` 选了:

> F3 保持不动,只加鼠标点击

本轮范围:

* **要做的**:①面板上每一条可视的 `Fx`/方向键提示都成为一个鼠标点击目标;②点击调用的就是该键调用的**同一个方法**(枚举按「键」命名,键与点击不可能分叉);③鼠标悬停时该条目提亮,让「这里能点」看得见;④F6「伤害明细」页原本把键位提示塞在锚定条(单格 `Text`,无法承载点击区)里,本轮把提示搬到一条**新增的普通行**,使 F6 页也有鼠标返回通路;⑤用例把每条栏的**文字**(必须逐字等于改动前印出的字面量)与**点击清单**钉住;⑥负控钉住这两条。
* **不做的**:改任何一个键的行为(含 Shift+F3 只看前衛、F7/Shift+F7 筛选目标、F11/F12 换角色);新增配置键;改导出形状/键名/数值;改列宽、行序、排版、配色(R81 的颜色一字未动);给条目加下划线/边框/idle 图标;改 IMGUI 回退层(`src/Ui/OverlayCore.cs`)的按钮 —— 那一层本来就有真 `GUILayout.Button`。

## 1. 为什么必须自己判矩形

面板里**没有真正的 uGUI Button**:游戏场景没有 `EventSystem`,而这个覆盖层刻意不在那个场景里放任何东西(放了就要和游戏抢输入)。所以本次点击判定与既有的「复制引用」点击走同一条路:R56 起就在用的 `GetAsyncKeyState(1)`(VK_LBUTTON)边沿 + `RectTransformUtility.RectangleContainsScreenPoint(...)`(`src/Ui/OverlayUGUI.cs`)。R82 只把这条边沿的**优先级**改成:先测热键条目(`TryHotkeyClick()`),未命中才回落到复制目标 —— 两者物理上不会重叠,但先测小目标可以保证点在栏上永远不会被读成「复制」。

## 2. 改动清单

| 文件 | 改动 |
|---|---|
| `src/Ui/HotkeyBarText.cs` | **新建(纯文件)**。`internal enum HotkeyAction { None, KeyF3, KeyF3Shift, KeyF4 … KeyF12, KeyHome, KeyEnd, KeyLeft, KeyRight }`(**按「键」命名,不按页面用途命名**:同一个键在不同页面是不同动作,而点击派发要调的正是「这个键调用的方法」);`internal readonly struct HotkeySeg { string Text; HotkeyAction Action; bool Clickable; }`;**每个可点击段的 `Text` 自带尾随空格**,因此 `Line(...)` 逐字重现改动前界面上的那个字符串;六个栏的构造器 `RosterIdle()` / `RosterInBattle(quest, seconds)` / `Taken(quest, seconds, vanguardOnly)` / `Contribution(quest, seconds)` / `Chart(modeTag)` / `Timeline()` / `Detail()`;`Line(List<HotkeySeg>)`(纯拼接)、`Actions(List<HotkeySeg>)`(点击清单)、`EstimateWidth(text, fontSize)`(字体还测不出宽时的兜底:ASCII ≈ 0.56 em、其余 ≈ 1.0 em)、`const float Gap = 8f` / `Pad = 4f` |
| `src/Ui/SkillTimelineText.cs` | `TimelineLine` 增加 `internal List<HotkeySeg> Segments;`,构造函数改为 `TimelineLine(string text, TimelineLineStyle style, List<HotkeySeg> segments = null)`;时间表表头改为由 `HotkeyBarText.Timeline()` 构造(**字符串不变**,由 `Line()` 复现)。注意:`TimelineLineStyle.Header` 也用于手动技能行,所以携带关系必须挂在字段上,不能按样式判断 |
| `src/Ui/OverlayUGUI.Rows.cs` | `RowDef` 增加 `public List<HotkeySeg> Segments;`;新增 `ApplyRowFont(Text, RowDef)`(把字体判定从 `Refresh()` 抽出)与 `SegmentWidth(Text, string, int)`(`Text.preferredWidth`,≤1 时回退 `EstimateWidth` —— 池里的 `Text` 本来就是 `HorizontalWrapMode.Overflow`,所以能整行测宽);`Refresh()` 的文本数改成**按段数**计;`LayoutCharts` 每轮 `_hotkeyHits.Clear()`,并在普通行分支旁新增**分段分支**:每段一个池内 `Text`,`x` 从 4 累加、`sizeDelta = (实测宽 + Pad, h)`、段间再留 `Gap` 8 px(防止字宽测窄时两段贴在一起),可点击段登记进 `_hotkeyHits`;六个产出行改为 `Text = HotkeyBarText.Line(xxxKeys), Segments = xxxKeys`;F6 页在分隔行后**新增**一条键位行(高度 16),并把锚定条 `_pinLine` 尾段的 ` · ←/→ 翻页(20秒/页) F7 筛选目标 F11/F12 换角色 F6返回` **删掉**(锚定条只留数字) |
| `src/Ui/OverlayUGUI.cs` | 新增 `private struct HotkeyHit { RectTransform Rt; Text Text; Color Base; HotkeyAction Action; }` 与 `private static readonly List<HotkeyHit> _hotkeyHits`;`Tick()` 里 `CheckCopyClick();` → `CheckMouseClick(); UpdateHotkeyHover();`;`CheckCopyClick()` 改名 `CheckMouseClick()`(边沿后**先** `TryHotkeyClick()`);新增 `TryHotkeyClick()`、`UpdateHotkeyHover()`(命中段 `Color.Lerp(Base, white, 0.45f)`,其余回 `Base`)、`DispatchHotkey(HotkeyAction)`;把 `CheckKeys()` 里的动作体抽成 `internal static` 的 `ActF3(bool shift)` / `ActF5()` / `ActF6()` / `ActF7(bool back)` / `ActF8()` / `ActF9()` / `ActF10()` / `ActF11()` / `ActF12()` / `ActTimeline()` / `ActDetailPage(int delta)` —— **方法体与改动前逐字相同**,`CheckKeys()` 现在只做按键读取与边沿判定后调用它们,`DispatchHotkey` 调用的也是它们 |
| `tests/BehaviorTests/Cases.HotkeyBar.cs` | **新建**,+30 用例(新组 `ui/hotkey-bar`):六条栏 + 时间表表头 + F6 键位行的**文字逐字**钉子、七条**点击清单**钉子、以及「页面标题/任务时间前缀/分隔符不可点」「可点段必有标签」「每条栏是单行」「宽度兜底」等守卫 |
| `tests/BehaviorTests/BehaviorTests.csproj` | 纯文件区登记 `$(SrcRoot)\Ui\HotkeyBarText.cs`(否则用例看不见它) |
| `tests/BehaviorTests/Program.cs` | `ExpectedCases` **1469 → 1499** |
| `src/BuildInfo.cs` / `src/DpsMeter.csproj` | 版本 **1.7.31 → 1.7.32**(两处必须一致) |
| `tests/negative_control.py` | +5 条 R82 变异(**253 → 258**) |

## 3. 关键设计决定与偏离

* **3.1 枚举按「键」命名,不按用途命名。** 如果枚举写成 `OpenTaken` / `ReturnRoster` / `ToggleFilter`,那么同一个键在两个页面上会有两个成员,点击派发就要自己判断「现在在哪个页面」—— 那份判断迟早与 `CheckKeys()` 里的判断不一致。按键命名后,`DispatchHotkey` 只有一行一映射,而「同一键同一行为」由 `ui/hotkey-bar/the-return-and-the-open-entry-are-the-same-action`(=贡献页 `F5返回` 与名单 `F5 贡献` 都是 `KeyF5`)钉住。
* **3.2 文字必须是改动前的那一串。** 用户要的是「这些已经印出来的条目变得能点」,不是重新排版。所以每个可点段的 `Text` 自带尾随空格(`"F8 显隐  "`),`Line()` 拼出来与改动前的字面量**逐字相同**,并由 8 条用例一条一条比。**唯一例外是 F6 页**:它的提示原在锚定条里(单格 `Text`,不能承载点击区),R82 把它搬成普通行,`the-detail-key-row-keeps-the-pinned-bars-old-hint` 钉住「词没变、只是位置变了」。
* **3.3 颜色/宽度都留在渲染层。** `src/Ui/HotkeyBarText.cs` 是**纯文件**(`tests/BehaviorTests` 直接编译执行),它不碰任何 Unity 类型,所以 30 条用例能在**没有 Unity** 的前提下同时检查文案与点击清单;颜色、字体、命中矩形、悬停提亮全在 `OverlayUGUI*` 里。测宽优先用 `Text.preferredWidth`(池里的 `Text` 是 `Overflow` 模式),`EstimateWidth` 只在字体还没就绪的第一帧兜底 —— 这条兜底也被用例钉住(否则它会悄悄退化成「所有段宽都是 0、全部叠在左边」)。
* **3.4 没有真 Button,只有矩形。** 见 §1:不往游戏场景里加 `EventSystem`/`Button`(会引入与游戏抢输入的风险),沿用 R56 起的 `GetAsyncKeyState` + 矩形判定;顺带获得「不依赖游戏 UI 缩放/Canvas 射线」的性质。
* **3.5 偏离说明:Home/End 是「点一下就跳」,不是持续按住。** `HandleScroll()` 里的 Home/End 是**电平**判定(按住即持续生效),而一次点击天然只有一次边沿,所以 `DispatchHotkey` 直接赋 `_scrollOffset = 0f` / `float.MaxValue`(后者由 `HandleScroll()` 夹到 `maxOff = _contentH - _viewH`)。键盘那一侧一字未动。

## 4. 点击清单(每条栏的可点条目)

| 页面/栏 | 可点条目(按显示顺序) | 目标动作 |
|---|---|---|
| 名单(未在战斗中) | `F8 显隐` `F9 重置` `F10 图表` `F6 明细` `F5 贡献` `F3 受击来源` `F4 时间表` | `KeyF8` `KeyF9` `KeyF10` `KeyF6` `KeyF5` `KeyF3` `KeyF4` |
| 名单(战斗中) | 同上七项(紧凑写法),前面 `任务 … 时间 …` 不可点 | 同上 |
| F3 受击来源拆分 | `F3返回` `Shift+F3 只看前衛[開]` `Home` `/` `End 首尾`(`/` 不可点) | `KeyF3` `KeyF3Shift` `KeyHome` `KeyEnd` |
| F5 总贡献 | `F5返回` | `KeyF5` |
| F10 图表 | `F10列表` `F12累计/每秒` | `KeyF10` `KeyF12` |
| F4 技能时间表 | `F4 返回` | `KeyF4` |
| F6 伤害明细(**新增行**) | `←` `/` `→ 翻页(20秒/页)` `F7 筛选目标` `F11` `/` `F12 换角色` `F6返回`(`/` 不可点) | `KeyLeft` `KeyRight` `KeyF7` `KeyF11` `KeyF12` `KeyF6` |

## 5. 验证

### 5.1 用例

`behavior tests: cases=1499 failed=0 pinned=1499` / `== ALL PASS ==`(**1469 → 1499**,+30;组数 **116 → 117**,新组 `ui/hotkey-bar`)。新增断言名(30 条):

文字: `the-idle-roster-bar-keeps-its-exact-text`、`the-in-battle-roster-bar-keeps-its-exact-text`、`the-taken-bar-keeps-its-exact-text`、`the-taken-bar-states-the-filter-when-it-is-on`、`the-contribution-bar-keeps-its-exact-text`、`the-chart-bar-keeps-its-exact-text`、`the-timeline-header-keeps-its-exact-text`、`the-detail-key-row-keeps-the-pinned-bars-old-hint`。

点击清单: `the-idle-roster-bar-offers-the-seven-keys`、`the-in-battle-bar-offers-the-same-entries`、`the-taken-bar-offers-return-filter-and-ends`、`the-contribution-bar-offers-only-return`、`the-chart-bar-offers-list-and-mode`、`the-timeline-offers-only-return`、`the-detail-row-offers-paging-filter-and-character`。

映射与守卫: `the-return-and-the-open-entry-are-the-same-action`、`the-shift-entries-are-distinct-from-their-plain-keys`、`the-taken-bar-filters-on-shift-and-returns-on-plain-f3`、`the-page-title-is-not-clickable`、`the-quest-and-time-prefix-is-not-clickable`、`the-separators-between-two-entries-are-not-clickable`、`every-clickable-entry-has-a-label`、`the-plain-action-is-never-clickable`、`every-bar-is-a-single-line`、`the-width-estimate-is-zero-for-empty-text`、`the-width-estimate-counts-a-cjk-glyph-wider-than-ascii`、`the-width-estimate-grows-with-every-added-glyph`、`the-width-estimate-grows-with-the-font-size`、`the-layout-gap-is-a-safe-positive`、`a-null-segment-list-is-an-empty-bar`。

### 5.2 负控(253 → 258)

| 变异 | find → repl | expect(必须红在) |
|---|---|---|
| `hotkey-bar-taken-entry-loses-its-key` | `Key("F3返回  ", HotkeyAction.KeyF3),` → `Plain("F3返回  "),` | `ui/hotkey-bar/the-taken-bar-offers-return-filter-and-ends` |
| `hotkey-bar-shift-entry-loses-its-modifier` | `Shift+F3 …` 的 `HotkeyAction.KeyF3Shift` → `HotkeyAction.KeyF3` | `ui/hotkey-bar/the-taken-bar-offers-return-filter-and-ends` |
| `hotkey-bar-detail-row-drops-return` | `Key("F6返回", HotkeyAction.KeyF6),` → `Plain("F6返回"),` | `ui/hotkey-bar/the-detail-row-offers-paging-filter-and-character` |
| `hotkey-bar-idle-entry-text-changed` | `Key("F10 图表  ", …)` → `Key("F10 圖表  ", …)` | `ui/hotkey-bar/the-idle-roster-bar-keeps-its-exact-text` |
| `hotkey-bar-cjk-measured-narrower-than-ascii` | 兜底式 `wide * 1.0f` → `wide * 0.2f` | `ui/hotkey-bar/the-width-estimate-counts-a-cjk-glyph-wider-than-ascii` |

逐条 `python tests\negative_control.py --only <name>` 实测:五条全部 `[PASS] … went red`(前两条同时带红 2 个用例,因为点击清单与「F3/Shift+F3 分工」两条都依赖它)。

### 5.3 构建

* 插件:`dotnet build _dpsm_work\src\DpsMeter.csproj -c Release` = **0 警告 / 0 错误**;
* 测试工程:0 错误 / **6 个既有 CS0649 警告**(`src/Runtime/OriginHeldEvent.cs` 5 条 + `src/Policy/AbsorbClassifyPolicy.cs:123` 1 条,均非本轮引入);
* 首跑 `cases=1499 failed=1 pinned=1469` —— 唯一红是 pin 自身,改 `Program.cs` 的 `ExpectedCases` 后全绿。(R82 只改 UI 通路,新增用例首跑即过。)

### 5.4 全套闸门

全部为 2026-10-07 在本机实跑的输出(逐条抄录):

* 行为套件:`tests\BehaviorTests\bin\Release\net6.0\BehaviorTests.exe -- --quiet` = **`cases=1499 failed=0 pinned=1499` / `== ALL PASS ==`**(新组 `ui/hotkey-bar`,组数 116 → 117);
* 全量负控:`python tests\negative_control.py` = **`negative control: 0 failure(s) of 258`**(退出码 0;新增 5 条逐条红在具名用例);
* 文档完整性:`python check_docs_123.py` = **`files=115` / `TOTAL DAMAGE MARKERS: 0`**(R82 批文档已登记进 `FILES`);
* 文档收敛:`python check_doc_convergence.py` = **`P1-B doc convergence: PASS (0/12 failing)`**(R1 六份文档同版本 1.7.32、R3 四份含 `93E6C827`、R5 活语料 151 份、R9 对 `acceptance_r82`);
* 工具登记:`python check_tool_registry.py` = **`registered=100 active=31 unclassified=0 (pin 0) pipeline=25` / PASS**;
* 终检:`python refactor_final_check.py` = **`version truth : BuildInfo=1.7.32 csproj=1.7.32 agree=True`** / **`refactor check: blocks=0 warns=0`**;
* 仓库清单:`python repo_manifest.py --write --exports batch_inputs\rf0` 后 `python repo_manifest.py --verify --exports batch_inputs\rf0` = `tracked=2779 hashed_excluded=348 corpus=35` / **`drift=0 changed=1 grew=0 notes=0`**(唯一的 `changed` 是 `baseline-manifest.json` 自身);
* 终验收:`python n0_acceptance.py --out D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\acceptance_r82` = **44 条命令 / 76 条检查 / 0 项不一致**(先改完文档并重写 manifest 再跑,因此没有出现 R81 首跑那次 `run_exit/repo_manifest_verify` 的非零)。

### 5.5 回填记录

* **§5.4 的数字来源**:上列七项都是本轮实跑的命令回显,不是推算;`acceptance_r82` 的头几行(生成时间 / 语料 35 份 / `source BuildInfo version: 1.7.32` / `deployed DLL sha256: 93E6C827…` / `config sha256: 12BABD45…`)可从 [`acceptance_r82/RESULTS.md`](<acceptance_r82/RESULTS.md>) 逐字复核。
* **文档同步用两遍脚本、每处都断言 `count==1`**:`tests\_r82_docs.tmp.py`(pass 1,7 份文档 26 处)写完后再跑 `tests\_r82_docs2.tmp.py`(pass 2:验收行 + 配置行)。pass 1 首跑 **4 条锚点不匹配 ⇒ 一行未写**(脚本退出码 1),修正后才落盘 —— 记下来供以后照抄:①`PROJECT-STATUS.md` 那条「插件版本 / 部署 DLL」长行不能整行做锚(`1.7.30(R80)已收敛,备份于 …` 的标点与预想不同),拆成三条短锚才过;②同一短语可能在同一份超长行里出现两次(`用例 **1469**、变异 **253**、源码 **120 个 .cs**` 在 `DpsMeter-文档索引.md:21` 里 occ@1532 与 occ@3365 各一次),本轮**放弃**改这两处(它们分别属于 R81 段与 R80 段的历史陈述),当前数字改由新插入的 R82 版头承担;③`SHA256` 行必须用**完整 64 位哈希**做锚(文档里没有 `2846C151…` 那种截断写法);④`147 → 144 份(R81 时 145 份)` 的真实字符是 `147 → 144 份,R81 时 145 份`(逗号、无括号)—— 教训:长行锚点先用 repr 探针把真实字符 dump 出来,不要凭记忆写。
* **配置行是 pass 2 才发现的**:`BepInEx\config\dev.dpsmeter.cfg` 在本轮开工时已是**未提交的修改**(首行 `created by plugin DpsMeter v1.7.31`、mtime 2026-10-07 17:39:32)⇒ 这是用户用 **1.7.31** 进过一次游戏、插件自己重写版本行的结果(键集不变),`PROJECT-STATUS.md` 的配置行因此同时更新为 `12BABD45…`(并把 `B7DBE35A…` 降级为「上一次」)。**R82 自身不改配置契约**,这条只是把既成事实记上。
* **`REFACTOR-BATCH-R81.md` 未动**(历史批次不可改写):它第 106 行当时写的「要等用户用 1.7.31 进一次游戏才会把首行版本号改成 `v1.7.31`」现在已经发生,该行作为历史陈述保留。

## 6. 部署

* 替换前锁检测:`LOCK-TEST: free`(当时只有 5 个 `DMMGamePlayer` 启动器进程,插件 DLL 未被占用)。
* 备份(回退件):`Copy-Item BepInEx\plugins\DpsMeter\DpsMeter.dll _dpsm_work\deploy-backup\pre-r82-2846C151\DpsMeter.dll` —— 备份件 = **526,848 B / `2846C151E21272EDDC2EF59A3D1173E563E10889FEBE519F8218FE5A6214C9FE`**(1.7.31)。
* 线上件替换后:`BepInEx\plugins\DpsMeter\DpsMeter.dll` = **531,456 B / `93E6C8270C84C0896C67B21BC37B9E884B8A2349C374E580C9990C921AFDF3DC`**(1.7.32,与 `_dpsm_work\src\bin\Release\DpsMeter.dll` 同哈希,mtime 2026-10-07 18:30:16)。
* 回退一条命令:`Copy-Item _dpsm_work\deploy-backup\pre-r82-2846C151\DpsMeter.dll BepInEx\plugins\DpsMeter\DpsMeter.dll -Force`。
* 两个**禁止修改**的历史备份复核未动:`BepInEx\plugins\DpsMeter\DpsMeter-1.0.48-crash.bak` = 171,008 B / `2E1819F24E45088E798F8C08D49C31B26C7902AC83887BB54AB52F20A2D31F60`;`DpsMeter-1.0.49-crash.bak` = 171,520 B / `F7FF1EB8A779680D472BB9881DB08982337AF6F63AC7002D745EC5E4615B7ED3`。
* 配置:**R82 没有新增/删除任何配置键**,所以 `BepInEx\config\dev.dpsmeter.cfg` 的键集一字未变;该文件当前 = `12BABD45B47F0B0CB7629197BB2C9684509221B4F0FFA768BA7704538320FE91`(首行 `created by plugin DpsMeter v1.7.31`,2026-10-07 17:39 由插件自己在用户那次进游戏时重写 —— 只改版本号,不是本轮产物)。用户第一次用 1.7.32 进游戏时,插件会把首行版本号再改成 `v1.7.32`(键集仍不变)。
* 语料:`BepInEx\plugins\DpsMeter\exports\battle_*.json` 写本文时 = **151 份**(R81 时 145;新增的 6 份来自用户 1.7.31 那次实机)。

## 7. 未决 / 待补充

* **实机读数(需要用户跑一场)**:①点在栏上是否顺手(条目高度 20 px / 命中宽度 = 实测字宽 + 4 px);②悬停提亮 0.45 是否够看得见(如果太弱,下一轮提高系数,不需要新配置键);③F6 页新增的键位行位置是否合适(它在分隔行之后、按时间轴分页的明细之前);④鼠标点击与游戏本身的左键拾取是否互相干扰(本层的点击只在面板可见时被处理,且不消费事件)。
* **F3 冲突本身没有解决**:本轮按用户选择只加鼠标通路,F3 仍然占用;若仍不够,下一轮的选项是「把 F3 做成可配置键」——那会新增配置键与文档口径,需要用户先拍板。
* **用户 `m05731`「好的,就按你推荐的方案进行实施」的指代仍未澄清**:本轮**没有**把它理解为「修订已发布承伤数值」那一档(那是会移动已发布数字的改动,必须先由用户定口径:落地记 `0` / 记 `nominal − res` / 记 `nominal`)。
* **IMGUI 回退层未动**:`src/Ui/OverlayCore.cs` 有自己的真按钮与 F8/F9 处理,本轮不改它。
