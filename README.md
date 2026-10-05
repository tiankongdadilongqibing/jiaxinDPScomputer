# DpsMeter — 邪神戦記 ルルイエ少女隊X 的伤害统计插件(DpsMeter)

BepInEx IL2CPP 插件,游戏内叠加层实时显示总伤害 / 秒伤 / 承伤 / 归属贡献 / 未归因伤害,
并把每一场战斗导出为 JSON(供离线核对与回归)。

- 当前版本:**1.7.11**(重构后的构建)
- 依赖:Unity 6000.3.5f2 + BepInEx(IL2CPP)
- 构建:dotnet SDK 6
- 验证纪律:每次改动都要过"构建 + 行为测试 + 变异负控 + 离线验收 + 文档守卫"(见下)

---

## 1. 这个仓库是什么(以及不是什么)

**是**:插件的源码、行为测试、离线验收流水线、守卫脚本与全部工程文档。

**不是**:不含游戏本体资源。仓库根目录**就是游戏安装目录**,因此仓库边界采用 **deny-by-default**
的 .gitignore:只有被显式重新纳入的文件才会进入提交,游戏资源(约 3 GB)、导出语料、下载缓存与 bin/obj
永远不会被顺手提交。边界规则与理由见 _dpsm_work/REPO-BOUNDARY.md。

## 2. 目录结构

    _dpsm_work/src/            插件源码(C#),按层组织:
                                 Aggregator*.cs   门面与状态(partial 拆分)
                                 Policy/          纯判据(时钟窗口、会话转换、归属、缓存、容差、主数据选名)
                                 Runtime/         状态容器(跨场衔接、规则注册表、计数、活动环、攻击快照、历史环)
                                 Model/ Output/ Ui/ MasterData/ Composition/ Diagnostics/ Hooks/
    _dpsm_work/tests/          行为测试(BehaviorTests,C#)+ 变异负控(negative_control.py)
    _dpsm_work/*.py            离线守卫与工具(验收流水线、文档收敛、布局、工具注册表、证据索引…)
    _dpsm_work/*.md            工程设计文档:索引、现状、交接、计划、批次记录、ADR
    _dpsm_work/batch_inputs/   冻结输入快照(rf0 = 35 份既有语料;r42 = 新一轮语料与 masterdata 基准)
    BepInEx/plugins/DpsMeter/  部署位置(运行中的插件)+ masterdata 转储 + 历史 .bak

文档入口:**DpsMeter-文档索引.md**(仓库根,全量导航)、**_dpsm_work/PROJECT-STATUS.md**(现状事实)、
**_dpsm_work/HANDOFF.md**(交接)、**REFACTOR-PLAN-POST-1.7.11.md**(本次重构方案)与 REFACTOR-BATCH-*.md(逐轮记录)。

## 3. 构建

    dotnet build _dpsm_work/src/DpsMeter.csproj -c Release

产出:_dpsm_work/src/bin/Release/DpsMeter.dll。要求 0 警告 0 错误。

## 4. 部署与回退(请先关闭游戏)

替换运行中的 DLL 前必须:

1. 确认游戏进程未运行,且部署文件未被占用;
2. 备份 BepInEx/plugins/DpsMeter 整个目录;
3. 记录替换前后的 SHA256;
4. 替换后复核"永不改动"的历史文件哈希未变。

回退(一条命令,游戏关闭时执行):

    Copy-Item _dpsm_work/deploy-backup/baseline-1.7.11/DpsMeter.dll BepInEx/plugins/DpsMeter/DpsMeter.dll -Force

当前部署 = 433,664 字节 / SHA256 C1DBBD8F…(含战斗编号 battle-ref/1 与精确选场);上一版 = 433,664 字节 / F6948470…(备份在 _dpsm_work/deploy-backup/pre-r57-F6948470/);基线 = 387,072 字节 / 36EC96D4…(备份在 _dpsm_work/deploy-backup/baseline-1.7.11/)。

## 5. 验证(改完必须跑的)

| 层 | 命令 | 规模 |
|---|---|---|
| 行为测试 | dotnet run --project _dpsm_work/tests/BehaviorTests/BehaviorTests.csproj -c Release -- --quiet | 950 用例 / 98 组 |
| 变异负控 | python _dpsm_work/tests/negative_control.py | 131 例,每例必须让具名用例变红 |
| 离线验收 | python _dpsm_work/n0_acceptance.py(--out 指定输出目录) | 44 条命令 / 76 条检查 |
| 文档收敛 | python _dpsm_work/check_doc_convergence.py(12 条规则 R1–R12) | 数字、版本、部署哈希、"未做"措辞 |
| 其他守卫 | check_docs_123.py / refactor_final_check.py / check_tool_registry.py / check_contribution_layout.py / repo_manifest.py --verify / archive_index.py | — |

**核心纪律**:每一道闸门都必须有"篡改 ⇒ 变红"的对照;改了流水线的命令/检查数、或部署哈希,
必须同步文档,否则守卫会红(这是设计,不是麻烦)。

## 6. 语料与离线口径

- 冻结快照 _dpsm_work/batch_inputs/rf0(35 份)是验收读取的固定语料;活目录 exports/ 会随游戏增长;
- 任务 9999(训练场)**不参与可比基线**;任务 411001 等为可归属比较对象;
- 归属覆盖率的两个口径**不可相加**:贡献账残差(原因如 unknown_kind)与"攻击者无法识别"的排除伤害
  (例:2026-10-05 那一场为 8,375,105.67（4.33%，unknown_kind）与 337,906（75 击,全在ショゴス）)。
- 插件的 masterdata 转储每次运行都会重写,可用于**逐字节回归对照**(见 _dpsm_work/batch_inputs/r42-masterdata)。

## 7. 分析战斗数据的路径

给代理/新协作者的最短路径(每步都有权威文档,**不要从"挑最新那个文件"开始**):

1. **选场**:`python _dpsm_work/battle_select.py list|resolve|compare` —— 按战斗编号
   (`B-{启动命名空间}-{序号}`)把编号精确解析成文件清单;旧档按 `legacy:<sha256>` 引用。
   训练场 9999 与未终局会被 `compare` 拒绝(这是设计)。详见 `_dpsm_work/REPORT-精确选场-R56.md` §7。
2. **取证**:战斗结束默认写出自包含证据包(`BepInEx/plugins/DpsMeter/extract/`,由 `ExtractOnBattleEnd`
   控制,默认开),用 `python _dpsm_work/extract_verify.py` 校验 manifest 与未归因普查。
   详见 `_dpsm_work/EXTRACTION-FLOW.md`。
3. **复算**(一手证据永远是导出文件):在 `_dpsm_work` 下跑
   `python -m contrib.crosscheck <导出>`(文件里的 `contribution` 段 ↔ 离线核心逐字段)与
   `python -m contrib.validate`(恒等式 I1–I10)。
4. **引用**:字段/公式查 `_dpsm_work/CONTRIBUTION-DATA-DICTIONARY.md`;链路、字段字典、实测样例与复现命令
   查 `_dpsm_work/CONTRIBUTION-TABLE-REPORT.md`(数据分析从它开始)。
   **两个未归因口径永不相加**——贡献账残差与"攻击者无法识别"的会话口径,必须写明用的是哪一个(见 §6)。
   **同队/自我伤害分两套口径**:归属侧(`contribution`,schema 1.2 起)**已排除**它,同队量由 `damageLedger.selfTeam*`
   与 `actors[].friendly` 计数;游戏口径(`totals.dealt`/`perSecDamage`)**仍含**它(数据字典 §1.2/§1.4)。

## 8. 现状与后续

本次(1.7.11 之后)的重构已收口:纯判据层、状态容器、界面格式化与三张表的行值模型、工具治理(100 条全部判定、
0 条未判定)、文档收敛守卫(R1–R12)、缓存语义三问与 Chart 格式按产品决定落地。
主数据层的共享部分已抽取(MasterDataAccess)并**机器证明**搬移等价;转储路由经证据判定**不合并**。
首次部署的实机验证已通过:**masterdata 转储与冻结基准逐字节一致**。

**版本管理约定**:历史批次记录描述各自轮次的事实,不因后续改动而改写;当前状态只由
PROJECT-STATUS.md 与 HANDOFF.md 承担。
