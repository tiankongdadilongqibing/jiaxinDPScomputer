# 重构批次记录 R52:未识别规则种类的定位 + 证据提取流程的固化

> 你的要求:**深挖"未识别的规则种类"**;可以跑游戏做数据提取;并且**把游戏数据提取固化为插件的一个流程**。
> 本记录给出:结论(§1)、本轮交付(§2)、逐文件改动(§3)、验证阶梯(§4)、部署与回退(§5)、未做(§6)。

## 0. 一句话

未归因的 4.33% **不是"未知的种类"**,而是一条**已知规则缺了"谁给的"**:全部分布在 `kind="given"` 这一个通道上;
根因是一个**可指名的实现不对称**(`GiveApplierProbe` 在 guest 读不出来时什么都不记,而 `StatusApplierProbe` 在同一现象下会记为自施加);
并且规则持有者在名册里**唯一**。围绕"这类问题以后怎么一次问清",本轮把提取做成了插件流程。

## 1. 深挖结论(证据与全部数字见 [REPORT-未识别规则种类-R52.md](<REPORT-未识别规则种类-R52.md>))

| 事实 | 值 |
|---|---|
| 本场(任务 411001,导出 24,601,891 字节,由 28B8CCAF 构建产生) | `contribution.unattributed = [{"reason":"unknown_kind","amount":8375105.6709,"folds":5862}]` |
| 重算一致性 | `attribution_census.py` 重算与导出自述**逐字段一致**(五个理由的折叠数全等,合计 35,961 = `foldAccounting.total`) |
| 这 5,862 个折叠的形状 | **100% 是 `kind="given"`**;只有 4 个 origin(`given#4..7/1006/-10`);label 只有一个 `被伤害+10%(赋予)`;因子恒为 `1.1`;受击方只有 ショゴス |
| 为什么没人认领 | `rosterAudit.giverResolved=0 / giverNull=30269`;`givenApplies.hookCalls={"addTalent":12}` 且 `nullGuest=12` → 目标表 `targets=0` → 34,688 次查询 0 命中 |
| 通道分类 | 全场只有 6 种 kind;前三个走各自分支、`atkadd` 必带 byUnit → **只有 `given` 与 `madness` 可能落进 `unknown_kind`**;本场 madness 全部带 byUnit,于是 100% 来自 given |
| 唯一持有者 | 刻印 id=26 `ブロックしている敵の被ダメージ+10%（前衛のみ）`(素质 `1006 p=[-10] cond=GiveTalent(1)`)在 team=1 里只有 `エヴァラス・フラウ(key=9)`持有,且**两份** → 与文档里"1.1² = 两个副本"吻合 |

**没有改任何归属**。把"唯一持有者"接进归属会移动单位之间的信用、并需要离线核对器同步实现,
那是一个单独的决定(报告 §4 给了 `given_carrier_unique/_ambiguous/_none` 三个理由码的建议)。
本轮的普查会把这些候选**算好并写进证据包**,但**不动分数**。

## 2. 交付:证据提取流程(细节见 [EXTRACTION-FLOW.md](<EXTRACTION-FLOW.md>))

| 触发 | 配置 | 默认 |
|---|---|---|
| 战斗结束自动出一包 | `General/ExtractOnBattleEnd` | false(一包 ≈ 25 MB,按需打开) |
| 按键出一包(战斗中/后都可以) | `General/ExtractKey`(F1–F12 / A–Z / 0–9,`NONE` 关闭) | **F4** |

产物 `BepInEx/plugins/DpsMeter/extract/extract_<时间戳>_q<任务>_<原因>/`:
`battle.json`(**复用同一个序列化器**`ExportService.ExportTo`)、`contrib_census.json`(未归因分组 + 受击方 + 候选判定)、
`masterdata/`(游戏自身主数据副本)、`manifest.json`(逐文件大小 + **FNV-1a 64** 校验 + **部署程序集** + 普查摘要 + 五条"别误读"注记)。
保留 `General/ExtractKeep`(默认 5)份,删除决定是纯函数 `ExtractPolicy.StaleBundles`。

两个离线工具(都已登记进工具注册表,并进了验收流水线):

* `attribution_census.py` —— 用被核对的离线核心重算未归因拆分并**逐字段对比插件自述**,再打印未归因分组与候选判定;7 例 `--selftest`。
* `extract_verify.py` —— 校验 manifest 的每个大小/校验值、把普查文件与"重算同一份 battle.json"**逐组对比**、核对候选判定、并报告部署程序集是否仍是包里那一个;7 例 `--selftest`。

## 3. 改动清单

| 文件 | 改动 | 为什么 |
|---|---|---|
| `src/Policy/ExtractPolicy.cs`(新) | 触发键解析/描述、包名、保留策略(纯函数) | 决定必须能离线跑用例;"保留最新的"写反了会删掉唯一有用的那一包,而且打一场也看不出来 |
| `src/Output/Contribution.cs` | `ContributionResult.Unresolved` + `ContributionUnresolvedRow` + 授予索引 `Index.ByGrant` + `GrantKeyOf` + `NoteUnresolved` | 普查由**写出导出的同一个循环**顺带产出,所以包与文件不可能互相矛盾;**不改任何分数** |
| `src/Output/ContributionSession.cs` | 名册 `Grants`(素质 `Cond` 含 `GiveTalent` 的 `type/param`)+ 命中携带 `Victim/VictimKey` | 候选判定的**唯一**证据来源是名册,不是新钩子、不是新读游戏对象 |
| `src/Output/ExportService.cs` | `Export(s)` → `ExportTo(s, file)`(可选路径) | 包必须自包含,但**不允许出现第二个序列化器** |
| `src/Diagnostics/EvidenceExtractor.cs`(新) | 写包 + manifest + 保留清理;**永不向外抛** | 它跑在收尾路径与渲染循环里,一次抛出会毁掉收尾 |
| `src/Plugin.cs` | 三个配置项(`ExtractOnBattleEnd/ExtractKey/ExtractKeep`) | 默认关自动、默认可用按键;键可改避免与游戏冲突 |
| `src/Aggregator.Finalize.cs` | 在导出之后、清理 `Source` 之前调度提取 | 普查必须看到与文件相同的已结束会话 |
| `src/Ui/OverlayUGUI.cs` | 轮询提取键(在 Visible 判断之前)并调用提取 | 面板隐藏时也要能按 |
| `tests/BehaviorTests/{Cases.Extraction.cs(新), Stubs.cs, BehaviorTests.csproj, Program.cs}` | 85 例新用例(键/包名/保留/普查/候选) | 见 §4;`TalentRef` 进 stub 是"编译即边界"的体现 |
| `tests/negative_control.py` | 9 例 R52 变异 + **修好计数缺陷** + 第三个自检用例 | 见 §4.4 |
| `attribution_census.py` / `extract_verify.py`(新) | 见 §2 | 流程的另一半:证明包没被改过、且普查可被独立重算 |
| `check_extract_contract.py`(新) | 写方(C#)与验方(Python)的**契约守卫**:schema 常量、校验算法名、验方读的每个键、三个必需文件名 | 这两半是同一份契约的两个实现;没有它,一次改名只能等到「真的产出一个包」才暴露,而那需要游戏。它的自检在临时副本上**两侧**都篡改得起(§4.3) |
| `n0_acceptance.py` / `tool_registry.json` / `TOOL-REGISTRY.md` / `check_docs_123.py` | 三个新 selftest 进流水线;两个新工具登记为 active;三份新文档进文档集 | 新增 .py/.md 不上名单就是红 |

## 4. 验证阶梯

### 4.1 构建与行为用例

| 命令 | 结果 |
|---|---|
| `dotnet build _dpsm_work/src/DpsMeter.csproj -c Release` | **0 警告 0 错误**(DpsMeter.dll 416,256 字节) |
| `dotnet run --project _dpsm_work/tests/BehaviorTests/BehaviorTests.csproj -c Release -- --quiet` | **794 用例 / 0 失败**(本轮 +85,`ExpectedCases` 709 → 794) |

### 4.2 新增变异负控(全部"红在具名用例")

9 例:`extract-key-f13-enabled`、`extract-retention-drops-the-newest`、`extract-retention-keeps-nothing`、
`extract-sanitise-drops-unknown-characters`、`census-grant-key-cut-at-the-second-slash`、
`census-picks-the-first-of-many-carriers`、`census-groups-copies-of-one-rule`、`census-victim-top-tie-reversed`、
`census-credits-the-unattributed-share-too`(把未归因份额同时记给攻击者 → 具名用例变红,证明"普查不改分数"不是一句注释)。
其中第一版 `census-credits-…` 写成"加 `st.Attributed`",但 `Attributed` 在循环后会被重算 → **闸门自己发现这条变异无效**,改成加 `ac.Self`。

### 4.3 两个离线工具的自检

| 命令 | 结果 |
|---|---|
| `python _dpsm_work/attribution_census.py --selftest` | **7 例通过**(改折叠数/改金额/编造理由/删段 → 各自具名变红) |
| `python _dpsm_work/extract_verify.py --selftest` | **7 例通过**(追加一个字节 → 大小+校验双红;删文件 → DATA_MISSING;普查不符/算法名不符/账不平/候选判定被翻 → 各自具名变红) |
| `python _dpsm_work/check_extract_contract.py`(+ `--selftest`) | 契约一致(read=34 / extra=27 / schemas=2 / files=3);**7 例自检通过**:改一个普查键名、改写方的 schema 常量、改写方的哈希算法名、改一个输出文件名 → 必须报出具名差异;改**验方**的 schema 常量与哈希名同样必须报出 |

### 4.4 本轮**发现并修好的一个"不会失败的闸门"**(重要)

跑全量时出现了一行 `[FAIL] rowmodel-rules-keep-zero-damage suite stayed GREEN`,但同一份输出的结尾却是
"**0 failure(s) of 102**"。查下去发现两件事,都不是本轮引入的:

1. **计数缺陷**:`negative_control.py` 的 `one()` 在每个失败分支只打印、最后无条件 `return 0`;
   于是摘要行与退出码**只统计"变异没贴上"**,行为上的失败(不该绿却绿了 / 红在错的用例上)完全不计分。
   这正好是这个仓库一直在猎的那类缺陷("一个不会说不的闸门"),所以本轮修好:每个失败分支 `return 1`,
   并给 `--selftest` 加了**第三个**用例 —— 一条**能贴上、能编译、但不改变行为**的变异,
   它现在**必须被计为失败**(修好前它与"通过"无法区分)。
2. **一条一直是绿的空转变异**:`rowmodel-rules-keep-zero-damage` 的夹具把 0 伤害规则放在 13 条正规则**之后**,
   而显示上限是 12 —— 轮不到那条 0 伤害规则,过滤器删不删都一样。修法是把 0 伤害规则挪到**最前**,
   让"不过滤就会占掉一个名额"成为可观察的差异;现在该变异会红(4 个具名用例)。

### 4.5 全量变异负控

| 命令 | 结果 |
|---|---|
| `python _dpsm_work/tests/negative_control.py` | **111 例 / 0 失败**(修好计数后,退出码 0 才真的表示"每一条都红在具名用例上") |
| `python _dpsm_work/tests/negative_control.py --selftest` | **3 例 / 0 失败**(未命中 = 驱动失败;纯文字 = 保持绿;空转变异 = 计为失败) |

### 4.6 验收流水线

| 命令 | 结果 |
|---|---|
| `python n0_acceptance.py --out acceptance_r52`(cwd 必须是 `_dpsm_work`) | **42 条命令 / 74 条检查 / 0 项**(`acceptance_r52`);新增的三条 `selftest/attribution_census`、`selftest/extract_verify`、`selftest/negctl_driver` 全部 exit 0 |
| `python repo_manifest.py --write --exports <冻结快照>` | 部署换了 DLL ⇒ `--verify` 报 `DRIFT: deployed_dll`(这是设计);按工具文档重新基线后 `drift=0`、`corpus=35` 与验收输入一致 |

> 收尾补充:`acceptance_r52` **跑过两次** —— 第一次是 40 条命令 / 72 条检查(提交 `2ef5710` 里留档),
> 加上 §3 的契约守卫(`extract_contract` + `selftest/extract_contract` 两条)后**重跑**,得 42 条命令 / 74 条检查 / 0 项;
> 文档里的口径数字按**最新**档案(即重跑后的 `acceptance_r52`)同步,这正是 R9 的用法。

## 5. 部署与回退

| 项 | 值 |
|---|---|
| 替换前 | `BepInEx/plugins/DpsMeter/DpsMeter.dll` 398,336 字节 / `28B8CCAF…C0DCBE76C6` |
| 替换前状态 | **游戏未运行**(进程表里没有 `rlyehshoujotaix_cl.exe`)且文件**未占用**(独占打开测试通过) |
| 备份 | `_dpsm_work/deploy-backup/pre-r52-28B8CCAF/DpsMeter.dll`(替换前那一个,校验值已复核) |
| 替换后(当前部署) | 416,256 字节 / `AA836C0688A3BAFEF080C755A08C40318845CE1131337CB54FC03E86A92A238A` |
| 复核 | `DpsMeter-1.0.48-crash.bak` = `2E1819F2…`、`DpsMeter-1.0.49-crash.bak` = `F7FF1EB8…` **均未改变** |
| 回退(任一步,游戏关闭时) | `Copy-Item _dpsm_work/deploy-backup/pre-r52-28B8CCAF/DpsMeter.dll BepInEx/plugins/DpsMeter/DpsMeter.dll -Force`(回上一版)或把路径换成 `baseline-1.7.11/DpsMeter.dll`(回基线) |
| 关闭新流程 | `ExtractOnBattleEnd=false` + `ExtractKey=NONE`;删除 `extract/` 目录不影响导出与统计 |

## 6. 未做(诚实清单)

* **实机验证待办**:插件侧已构建、用例与变异都过,但"真的产生一个包、再用 `extract_verify.py` 验过"
  **还没有**:收尾路径需要**实际打一场**,按键路径需要**在游戏里按一次**。这两件事**请求**你做其中一件
  (按 AGENTS.md 第 2 条:不把"先跑一场"塞进自动化流程),拿到包之后我会跑 §2 的两个工具并把结果追加到本记录。
* 未改归属:候选判定只写进照/普查,不动分数(§1 末)。
* 包里不含日志(`LogOutput.log` / `dpsmeter_runtime.log`),需要时另外附上。
* `carrierVerdict` 是名册推断,不是运行时实测;"谁给的"最终仍需一条可读的运行时证据。
* 本轮没有重试 `gd.ownerAction` 字段读取(1.5.4 已判死:每个条目都抛异常)。

## 7. 回滚本轮代码改动

`git revert <本轮提交>`(或 `git checkout <上一个提交> -- _dpsm_work/src _dpsm_work/tests _dpsm_work/*.py`)。
DLL 回退见 §5;两件事互相独立:回代码不影响已部署的 DLL,回 DLL 也不影响工作区。
