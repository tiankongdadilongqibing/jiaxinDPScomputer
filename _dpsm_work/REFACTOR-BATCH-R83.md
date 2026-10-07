# REFACTOR-BATCH-R83 —— 受击来源拆分页(F3)的两处显示缺陷:占比列贴字 / 列名「金额」

> 版本 **1.7.33**(R83)。只改**显示层**:①桶子表与受害单位行的**占比列各加宽一格**,百分比不再和前面的**击数**贴成一个数字;②桶子表的金额列名 `金额` → **`伤害`**。除这两处外页面字符串一字未动,导出/数值/口径/键名/配置键全未动(`contribution.schemaVersion` 仍 **1.2**,`takenBreakdown` 仍 **1.1**)。
> 本文是历史批次文档:写完即冻结,后面的轮次不改写它(状态类文档才是活的)。

## 0. 本轮任务与范围

用户原话(m06715,附 F3 页截图):

> 1.我发现击数和占比列的数字贴在一起了,这是不是会导致误解? 2.伤害列写成'金额'是不是有些不妥?

两问的答案都是「是」,而且都是 R80 那张表的缺陷:

- **贴字**:`DisplayFormat.Pct` 把 `100.00%` 印成**恰好 7 个显示列**,而 `DisplayFormat.PadL` 对**已经填满**的单元格**一格都不补**(`_dpsm_work/src/Ui/DisplayFormat.cs:166-170`:`return w >= width ? s : new string(' ', width - w) + s;`)。占比列宽 7 ⇒ 击数 `2` 与 `100.00%` 之间 0 格 ⇒ 屏幕上读成 `2100.00%`。截图里每一条桶行的尾部都是这个形态,用户看到的「2100.00%」不是数值,是两列粘在一起。
- **列名**:`金额`(money / amount)用来指一个**伤害量**是错的。这张表的单元格是「受害单位的受击总口径(nominal)里落在该桶的那部分伤害」,与货币无关;贡献页对同类单元格用的是 `当量`(`_dpsm_work/src/Ui/ContributionColumns.cs:62`、`:69`)。

要做:

1. `_dpsm_work/src/Ui/TakenColumns.cs`:占比列 `T1Share` 7→8、`BShare` 7→8(即把贡献表从 R12 起就在用的 8 拿过来,`_dpsm_work/src/Ui/ContributionColumns.cs:44 T1Share = 8`);
2. 同文件桶子表列名 `金额` → `伤害`;
3. 两处几何 pin 同步(`T1LineWidth` 80→81、`BLineWidth` 76→77);
4. 新增 7 个具名用例(见 §5.1);
5. 负控 258→261(见 §5.2);
6. 版本 **1.7.33**、文档收敛、部署(见 §6)。

不做(明确划出):

- 不改键盘或鼠标行为:R82 的点击通路一字未动,`快捷键行` 文本逐字不变;
- 不改任何数值、口径、导出字段、键名、配置键;`contribution.schemaVersion` 仍 1.2;
- **不**把 `DisplayFormat.PadL` 改成「会收缩满格单元格」:那是全项目所有表格共用的原语,改它等于同时改贡献页/技能时间表/详情页的几何;
- 不改 `_dpsm_work/src/Diagnostics/EvidenceExtractor.cs:204` 那条 `[DpsMeter][EXTRACT] … 金额=…` 日志:它是 F4 证据包的诊断文本(证据包/诊断口径),不是面板列头;本轮只改面板,该条留作未决(§7)。

## 1. 为什么宽度是「原因」而不是「补丁」

`sharePct` 的来源是 `_dpsm_work/src/Ui/TakenPageText.cs:215-218` 的 `Share(amount, nominal)`(`nominal > 0 ? 100.0 * amount / nominal : 0.0`)。模型的每个维度都是对同一个 nominal 的**划分**,所以单个桶的占比**上界就是 100.00%**,即 `Pct` 的合法输出最宽就是 7 列(`100.00%`)。把这列放到 8,任何一个合法值前面都**至少**留 1 格;7 则只有「恰好 100.00%」这一个值会把格子填满(也就是截图里唯一的那个形态)。

宽度也不会顶到面板:该页由 `_dpsm_work/src/Ui/OverlayUGUI.Rows.cs:150-153` 的 `LayoutCharts` 用 `Mathf.Min(Screen.width - 40f, 880f)` 给宽度(fontSize 14 时 1 列 ≈ 7.44 px ⇒ 约 118 列),行宽 77/81 都在里面;测试里另有一条保守上界 `BLineWidth <= 94`(94 = 贡献表最宽行,`ContributionColumns.T1LineWidth`)。

## 2. 改动清单

| 文件 | 改动 |
| --- | --- |
| `_dpsm_work/src/Ui/TakenColumns.cs` | `T1Share` 7→8、`BShare` 7→8(各带一段说明为什么是 8);桶子表列名 `金额`→`伤害`(带一段说明为什么不是货币) |
| `_dpsm_work/src/BuildInfo.cs` | `Version` 1.7.32 → **1.7.33** |
| `_dpsm_work/src/DpsMeter.csproj` | `<Version>` 1.7.32 → **1.7.33** |
| `_dpsm_work/tests/BehaviorTests/Cases.TakenPage.cs` | 两处宽度 pin 同步(80→81、76→77);新增 R83 小节 7 个用例;类注释加一段 R83 的说明 |
| `_dpsm_work/tests/BehaviorTests/Program.cs` | `ExpectedCases` 1499 → **1506**,注释块加一行 R83 |
| `_dpsm_work/tests/negative_control.py` | 新增 3 条 R83 变异(258 → 261) |
| `_dpsm_work/check_docs_123.py` | `FILES` 加 `_dpsm_work/REFACTOR-BATCH-R83.md` |
| 7 份状态类文档 | 版本/DLL/用例/变异/语料数收敛(见 §5.5) |

## 3. 关键设计决定与偏离

- **只加宽占比列,不引入「每列强制一格分隔符」**。项目既有几何是「每列右对齐 + `PadL` 左侧填充」(`ContributionColumns.HeaderLine` 与 `TakenColumns` 同源),真正会被填满的单元格在本页只有 `100.00%` 一个形态;引入全局分隔符要改所有行宽与所有 pin,收益为零。
- **8,不是 9**。`100.00%` 是合法值的最大宽度(§1),8 已经保证至少一格;9 只是把整张表往右挪一格。
- **列名用 `伤害`,不用贡献页的 `当量`**。这页整页都是**受击**伤害,用户原话也是「伤害列」;`当量` 在贡献页承担的是「规则/链接折算出来的当量」这一专门含义,搬到这里反而把口径说糊。两条列名断言(含「伤害」/不含「金额」)把这个词钉住了。
- **`金额` 只改面板列头**。`EvidenceExtractor` 里那条 `金额=` 属于 F4 证据包的诊断行,改它等于改证据包文本,需要单独一轮(§7)。

## 4. 屏上前后对照(只画行尾)

同一行(名字 46 列 + 伤害 14 列之后的尾部):

```
1.7.32 (R82):        ...   8,626        2100.00%     <- 击数 2 与 100.00% 之间 0 格(7 宽占比列被填满)
                          \______________/ 读成 "2100.00%"
1.7.33 (R83):        ...   8,626        2  100.00%    <- 8 宽的占比列必留一格
```

列头:

```
1.7.32 (R82):   名字            金额        击数      占比
1.7.33 (R83):   名字            伤害        击数      占比
```

## 5. 验证

### 5.1 用例(1469 → 1499 → **1506**,+7;组数 117 不变)

现行 `ui/taken-page` 组新增:

1. `the-share-text-is-seven-columns-wide` —— `DisplayFormat.DispWidth(DisplayFormat.Pct(100.0)) == 7`:把「原因」钉在数字上,谁改 `Pct` 的格式都要先看见它;
2. `a-full-share-cell-would-get-no-padding` —— `DisplayFormat.PadL(DisplayFormat.Pct(100.0), 7) == "100.00%"`:同样一列,7 宽时**零填充**;
3. `a-full-victim-share-cell-carries-its-own-padding` —— 受害单位行(占比 100.0)最后一格 = `" 100.00%"`;
4. `a-full-bucket-share-cell-carries-its-own-padding` —— 桶行(占比 100.0)同上;
5. `a-near-full-bucket-share-is-separated-too` —— 99.99% 时占比格的第一格也是空格(分隔来自**宽度**,不是给 100% 的特例);
6. `the-bucket-amount-column-is-labelled-damage` —— 桶子表列头含 `伤害`;
7. `the-bucket-amount-column-is-not-labelled-money` —— 桶子表列头**不含** `金额`。

同组另有两处既存 pin 随宽度同步:`the-victim-row-width-is-pinned`(80→81)、`the-bucket-row-width-is-pinned`(76→77);`the-header-is-exactly-one-row-wide` / `the-bucket-header-is-exactly-one-row-wide` 等按常量比较的断言无需改。

### 5.2 负控(253 → 258 → **261**)

| 变异名 | 文件 | find → repl | 变红的用例 |
| --- | --- | --- | --- |
| `taken-bucket-share-cell-narrowed-to-7` | `Ui/TakenColumns.cs` | `BShare = 8` → `7` | `ui/taken-page/a-full-bucket-share-cell-carries-its-own-padding` |
| `taken-victim-share-cell-narrowed-to-7` | `Ui/TakenColumns.cs` | `T1Share = 8` → `7` | `ui/taken-page/a-full-victim-share-cell-carries-its-own-padding` |
| `taken-amount-column-called-money-again` | `Ui/TakenColumns.cs` | `C("伤害", BAmount, true)` → `C("金额", …)` | `ui/taken-page/the-bucket-amount-column-is-not-labelled-money` |

三条都逐条 `--only` 实跑过:各自在**具名用例**上变红(`cases=1506 failed=2 pinned=1506` —— 多出的那 1 条是同行宽 pin,正是宽度变化本该有的连带影响)。

### 5.3 构建

`dotnet build _dpsm_work/src/DpsMeter.csproj -c Release`:**0 警告 / 0 错误**;测试工程 0 错误(只有既有的 6 条 CS0649 警告)。行为套件:`cases=1506 failed=0 pinned=1506` / `== ALL PASS ==`。

### 5.4 全套闸门

| 闸门 | 命令(cwd = `_dpsm_work`) | 实测 |
|---|---|---|
| 插件构建 | `dotnet build src/DpsMeter.csproj -c Release` | **0 警告 / 0 错误** |
| 行为套件 | `tests\BehaviorTests\bin\Release\net6.0\BehaviorTests.exe -- --quiet` | `cases=1506 failed=0 pinned=1506` / `== ALL PASS ==` |
| 全量负控 | `python tests\negative_control.py` | **`negative control: 0 failure(s) of 261`** |
| 文档 123 | `python check_docs_123.py` | **files=116 / TOTAL DAMAGE MARKERS: 0** |
| 文档收敛 | `python check_doc_convergence.py` | **PASS (0/12 failing)** |
| 工具注册表 | `python check_tool_registry.py` | `registered=100 active=31 unclassified=0 pipeline=25` / **PASS** |
| 终局检查 | `python refactor_final_check.py` | `BuildInfo=1.7.33 csproj=1.7.33 agree=True` / `blocks=0 warns=0` |
| 仓库清单 | `python repo_manifest.py --write --exports batch_inputs\rf0` 后 `--verify …` | `tracked=2783 hashed_excluded=348 corpus=35` / `drift=0 changed=1 grew=0 notes=0`(首次写时是 2782:`REFACTOR-BATCH-R83.md` 当时还没被 git 跟踪,而清单只登记已跟踪文件;提交后重写即 2783) |
| 终验收 | `python n0_acceptance.py --out D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\acceptance_r83` | **44 条命令 / 76 条检查 / 0 项不一致**(`checks: 76 ok, 0 mismatched`,generated 2026-10-07T19:51:22) |

`acceptance_r83\RESULTS.md` 头部自述:`source BuildInfo version: 1.7.33 ; deployed DLL sha256: 93E6C827…`(这一格记的是**部署前**的线上件)、`config sha256: 3F0C5802…`(1.7.32 启动写的那一份)、`exports: 35 files in …\batch_inputs\rf0`。

### 5.5 回填记录

- 数字来源:构建 / 套件 / 负控 / 各闸门 / 验收全部为本机实跑输出,日志留在 `tests\_r83_*.tmp.txt`(构建 `_r83_build`、测试工程 `_r83_tbuild`、三条负控 `_r83_negctl_<name>` 与全量 `_r83_negctl_full`、收敛 `_r83_convergence1`、docs123 `_r83_docs123`、注册表 `_r83_toolreg`、终局 `_r83_finalcheck`、清单 `_r83_manifest`、验收 `_r83_acceptance`)。
- 计数口径:`_r83_measure.tmp.txt` = `src_cs=121 src_lines=32425`、`tests_cs=41 groups=117 guard_total=162`;**变异条数以全量负控的输出行 `of 261` 为准**(我的临时正则 `dict(name="` 数出 263,含 2 处非变异 dict,该口径已弃用)。
- 两遍文档脚本:`tests\_r83_docs.tmp.py`(pass 1,22 处,逐处 `count==1` 后整批写出)+ `tests\_r83_docs2.tmp.py`(pass 2,5 处:把 `acceptance_r83` 的 44/76/0 写进 `PROJECT-STATUS.md` 与 `DpsMeter-文档索引.md`、`线上 DLL = 1.7.33`、配置哈希换成 2026-10-07 19:26 那份 `3F0C5802…`)。两遍都是「任一锚点不唯一就一行不写、exit 1」。
- 锚点坑(与 R82 同源):`plan` 那条曾把新基线前缀与旧 R82 描述拼成重复的 `上一版 **1.7.31**(R82:…)`,已定点删掉重复子句;`PROJECT-STATUS.md` 的配置行是超长单行,只能拿其中一段做锚。
- **`BepInEx\config\dev.dpsmeter.cfg` 又被启动重写过一次**:首行已是 `created by plugin DpsMeter v1.7.32`(mtime 2026-10-07 19:26:46,哈希 `3F0C5802…`,键集一字未动)⇒ 用户用 R82 进过游戏,线上 `exports\` 从 151 涨到 **152 份**。
- 历史批文档(`REFACTOR-BATCH-R80/R81/R82.md`)一字未动。
- **`repo_manifest.py` 的 `tracked` 只登记 git 已跟踪的文件** —— 本轮首次写清单时 `REFACTOR-BATCH-R83.md` 还是未跟踪状态(2782),提交后重写才把它纳入(2783);`--verify` 两次都是 `drift=0 changed=1 grew=0 notes=0`(changed 恒为 `baseline-manifest.json` 自身)。临时脚本与 `tests\_r83_*.tmp.*` 日志都不进清单。

## 6. 部署

- **备份(替换前)**:`Copy-Item BepInEx\plugins\DpsMeter\DpsMeter.dll _dpsm_work\deploy-backup\pre-r83-93E6C827\DpsMeter.dll` —— 备份件复核 = **531,456 B / `93E6C8270C84C0896C67B21BC37B9E884B8A2349C374E580C9990C921AFDF3DC`**(1.7.32)。
- **替换**(替换前 `LOCK-TEST: free`,只有 5 个 `DMMGamePlayer` 启动器进程,插件 DLL 未被占用):`BepInEx\plugins\DpsMeter\DpsMeter.dll` 由 531,456 B / `93E6C827…` → **531,456 B / `03D021F99817A0208C9628E84FB1D0A3C8DB9F4516DF9F25776C3208943F87B3`**(1.7.33,与 `_dpsm_work\src\bin\Release\DpsMeter.dll` 同哈希)。
- **回退**:`Copy-Item _dpsm_work\deploy-backup\pre-r83-93E6C827\DpsMeter.dll BepInEx\plugins\DpsMeter\DpsMeter.dll -Force`
- **两个禁改件复核未动**:`DpsMeter-1.0.48-crash.bak` = 171,008 B / `2E1819F24E45088E798F8C08D49C31B26C7902AC83887BB54AB52F20A2D31F60`;`DpsMeter-1.0.49-crash.bak` = 171,520 B / `F7FF1EB8A779680D472BB9881DB08982337AF6F63AC7002D745EC5E4615B7ED3`。
- **配置未改**:`BepInEx\config\dev.dpsmeter.cfg` 仍是 `3F0C5802…`(部署不动配置);活语料 `BepInEx\plugins\DpsMeter\exports\battle_*.json` = **152 份**。

## 7. 未决 / 待补充

- `_dpsm_work/src/Diagnostics/EvidenceExtractor.cs:204` 的 F4 证据包诊断行仍写 `金额=`;它与面板列名同源同义,是否一起改成 `伤害=`(会动到证据包文本,需单独一轮 + 该文本的既有断言)。
- 更一般的「两个右对齐数字列可能贴在一起」问题:本页真正会填满格子的只有 `100.00%` 一个形态;若将来某列出现「必然填满」的值(例如 7 位击数),同样的粘字会回来。真正的通法是在几何层引入每列一格分隔,那是全项目级别的改动(§3 第一条)。
- 实机确认:占比列加宽后整页是否仍然对齐美观(尤其 `合计` 行与子表头)。
