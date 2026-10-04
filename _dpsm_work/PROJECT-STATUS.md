# DpsMeter 项目现状快照(PROJECT-STATUS)

> 生成:2026-10-04,第 6 轮末。**本文件描述“此刻为真”的事实**;历史快照与逐版决策见 §10。
> 与 [`DpsMeter-文档索引.md`](../DpsMeter-文档索引.md) 分工:索引回答“去哪查”,本文件回答“现在是什么状态、能不能重构、先动哪里”。
> 证据等级:【实测】= 真实运行/文件输出;【静态】= 读源码或文件;【推断】= 未直接观测。**没核实过的旧结论一律标【待复核】。**

## 0. 一句话现状

插件 **1.7.11** 已部署且与源码一致;语料 **32 份导出、19 份带 `contribution` 段**;6 个契约都有可复现入口和会变红的负控;
路线图 N0–N6 已闭环、N7 目视 9 项里 1 项由实机日志自动核对通过;**当前没有任何“影响伤害”的已知丢失**;
配队结论仍是**「有倾向但不确定 → 不换人」**(样本增加后等级未变)。
**重构最大的障碍不是代码耦合,而是没有版本控制、没有 C# 单元测试工程** —— 见 §7。

## 1. 快照表(全部【实测】,2026-10-04)

| 项 | 值 |
|---|---|
| 插件版本 | **1.7.11**;`src/BuildInfo.cs` = `DpsMeter.csproj` = 1.7.11(一致) |
| 部署 DLL | `BepInEx\plugins\DpsMeter\DpsMeter.dll`,387,072 B,SHA256 `36EC96D4DBD8E221ED554476C299BD8DB4C9A1220A2A923DB16BC7BB4888BC42` |
| 回退锚点 | `.1.7.10.bak` = `BF2F174A…`(另有 .1.7.9/.1.7.8/.1.7.7/.1.7.6/.1.7.5/.1.7.4/.1.7.3/.1.7.2/.1.7.0/.1.6.1/.1.6.0/.1.5.5-verified);**`1.0.48/1.0.49-crash.bak` 绝不回滚** |
| 源码规模 | `_dpsm_work/src`:**88 个 .cs / 23,026 行**(不含 obj/bin;RF2 拆 `Aggregator` 为 6 个 partial,RF3 新增 `src/Policy/` 3 个纯策略文件,RF4 新增 `src/Runtime/` 状态容器);守卫口径 **120** 个 .cs(src + recon_probe + test + **tests**) |
| 配置 | `BepInEx\config\dev.dpsmeter.cfg` = `247AD5848F1172EAE0D473C6A2F9A56E164814F3013A22E0BD29EFC95DF0DEFD`;贡献相关开关全 true |
| 语料 | **35 份**(冻结快照 [`batch-inputs-rf0.json`](<batch-inputs-rf0.json>),hard-link 目录 `batch_inputs/rf0/`,约 600 MB)。`BepInEx\plugins\DpsMeter\exports\` 是**活的** —— 游戏正在运行,写本文时已 36 份;批次只读快照,见 §4 |
| 导出段 schema | `contribution.schemaVersion` = **1.1**(**22 份带段**:1.0 ×13 / 1.1 ×9);方法 `log-share/1` |
| 版本控制 | **本地 Git**(无远端):基线提交 `a2a09c2`,标签 `baseline-1.7.11`,380 个纳入文件;边界见 [`REPO-BOUNDARY.md`](<../../REPO-BOUNDARY.md>) |
| C# 测试工程 | `tests/BehaviorTests`(RF1+RF3+RF3c+RF4+RF5a–h+RF6a+RF7b):**697 个命名用例 / 27 组**,**100 例变异负控**;**执行生产源码**(含 `src/Policy/`、`src/Runtime/`),不是复制公式 |
| 离线守卫 | **37 条命令 / 69 条检查**的验收流水线(`n0_acceptance.py`,默认读冻结快照写 `--out`);**RF2 起的各轮终验收都是 0 项**;RF0–RF2 的基线轮 59 ok / 4 项(见 §12)。工具本体见 [`TOOL-REGISTRY.md`](<TOOL-REGISTRY.md>):**96 条登记 / 27 条活跃 / 67 条已索引 / 2 条被引用输入 / 0 条未判定**(RF7 工具治理收口)(上限已收紧到 31,守卫的 G 检查复核 `indexed` 的声明) |

## 2. 语料现状(35 份,冻结快照)

| 维度 | 分布 |
|---|---|
| 按任务 | 411001 ×**25** / 训练场 9999 ×**9** / 700817 ×1 |
| 按版本 | 1.5.3×2, 1.5.4×8, 1.5.5×3, 1.6.0, 1.6.1, 1.7.0, 1.7.2–1.7.4, 1.7.5×4, 1.7.6×3, 1.7.8×3, 1.7.10, **1.7.11×5** |
| 带 `contribution` 段 | **22/35**(schema 1.0 ×13 / 1.1 ×9);无段 13 份 = 1.5.3–1.5.5(**1.6.0 有段**,它是坏样本不是段前版本) |
| `crosscheck --batch` | ERROR **1**(已知坏样本)/ LEGACY_NOT_APPLICABLE 16 / PASS 12 / WARNING 3 |
| 适用性(模型) | full **14** / partial 9 / not_comparable **9**(9999 训练场一律 not_comparable) |
| 准入(actor_credit / rule_coverage) | 各 **18/32** 可准入;整体没有可排名指标 ⇒ CLI exit 4 |
| 布局守卫 | 32 份 / **634 行 / 0 违规** |
| 已知负例 | `battle_411001_20261004_015919.json`(1.6.0,58 处 `factor` 四位截断)—— **保留、不放宽、不删除** |
| 折叠丢步 | **0/32 场、0 步、0 伤害**(`FoldContext.MaxSteps=24` 从未触发) |
| 溢出 / 读取失败 / 未知身份 | 31/32 / 8/32 / 16/32 场有非零计数;**只影响记录完整性,不影响逐击倍率与总量** |

第 6 轮新增两场(用户正常游玩,未为验证开战):`battle_9999_20261004_165952.json`(17.9 s,1.7.11)、
`battle_411001_20261004_170157.json`(119.0 s / Lose / 2.12 亿可分析伤害 / 5,525 击,1.7.11)。两场 `compWeak=0`。

## 3. 契约清单(改动前必须知道的“对外承诺”)

| 契约 | 实现入口 | 承诺 | 退出码 | 负控 | 最近结果 |
|---|---|---|---|---|---|
| `log-share/1` | `contrib/`(loader→aggregate→report) + `src/Output/Contribution.cs` | 逐击 `M=∏folds`,`base=D/M` 给攻击者,`pool=D−base` 按 `ln(f_i)/ln(M)` 分给规则持有者;三条恒等式守恒 | — | S1–S13 + `test_gate` + `test_golden_155` | 域内未归因 0.0% |
| `ComparisonEligibility/1` | `comparison_eligibility.py` | 指标级准入 `ELIGIBLE/RESTRICTED/REJECTED/NOT_APPLICABLE`;未跑的检查 = `unknown` = RESTRICTED | 0/4/2/3 | `--selftest` **17** + `--selftest-e2e` **6** | exit 4(无可整体排名指标) |
| `compare/2` | `contrib/compare.py` | 先准入再分层;`entityKey` 行 + `\|inst:` 冲突后缀;四态(观测到/缺席/零/身份未知);份额与累计量分列 | 0/4/2/3 | `--selftest` **10** | 23 份 → 9 个可比较分层 |
| `decision/1` | `decision_report.py` + `decision_prereg.json` | 预注册阈值/alpha/自助单位与种子/每组下限;**只有四条件全满足才允许给最高等级** | 0/4/2/3 | `--selftest` **14** | 有倾向但不确定(不换人) |
| `budget-census/1` | `budget_census.py` | 丢步/溢出/读取失败/未知身份分开计数,按**影响伤害**排序;每个问题卡四必填字段 | — | `--selftest` **8** | 0/32 丢步 |
| 身份映射 | `identity_map.py` | `entityKey = ent:<ns>:<templateId>:<loadoutFingerprint>`;`identityStrength` strong/weak | — | `--selftest` **54 checks** | 0 failed |

补充:`n0_acceptance.py --selftest` **11 例**(逐文件钉住 + 闸门退出码两道新闸门的负控);
`comparison_eligibility` 另把「丢步」判为降级(`fold-dropped`),有丢步 ⇒ 等式降为**下界**。

## 4. 可复现入口(照抄,工作目录一律 `_dpsm_work`)

```
PY = C:\Users\24134\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe

验收(最全,37 条命令 / 69 条检查)   python n0_acceptance.py
冻结批次输入                      python batch_snapshot.py --name rf0 --verify
基线清单核对                      python repo_manifest.py --verify --exports batch_inputs\rf0
C# 行为测试                       dotnet run --project tests\BehaviorTests\BehaviorTests.csproj -c Release -- --quiet
变异负控(--list 看全部 10 例)      python tests\negative_control.py
IL 等价比较                       python tests\il_equiv.py <pre.txt> <post.txt> <report.txt>
RF2 拆分器(含 LOSS CHECK)         python tests\rf2_split.py
验收负控                          python n0_acceptance.py --selftest
比较器                            python -m contrib.compare --applicability acceptance_1.7.11\applicability.json --out contrib\reports\compare2_411001
配队决策                          python decision_report.py --compare contrib\reports\compare2_411001.json
预算普查                          python budget_census.py
准入(全语料)                      python comparison_eligibility.py --applicability acceptance_1.7.11\applicability.json --json acceptance_1.7.11\eligibility.json
文档收敛                          python check_doc_convergence.py(--selftest)
编码守卫                          python check_docs_123.py
实机日志核对                      python check_live_log.py --log ..\BepInEx\LogOutput.log
新样本只读体检                    python sample_intake.py
C# 离线断言                       dotnet run --project recon_probe\ReconProbe.csproj -c Release -v quiet -- out.json
构建                              dotnet build src\DpsMeter.csproj -c Release -v minimal
```

## 5. 代码地图(重构视角;行数为本次实测)

| 层 | 文件 | 行数 | 依赖 | 能不能离线测 |
|---|---|---|---|---|
| 纯函数/模型 | `Model/StatusKey.cs`、`Model/ClauseStatusRun.cs`、`Model/FoldStep.cs`、`Model/BattleTime.cs`、`Composition/TieredModifier.cs` | 约 1.1k | **不依赖 IL2CPP / Plugin** | **能**:`recon_probe` 直接编译执行 |
| **纯判据(RF3–RF6a)** | `Policy/`(8:时钟 / 会话转换 / 归属 / 全局规则分类 / 全局规则算术 / 缓存判据 / 主数据选名 / **composition 容差**) | 740 | **不依赖 IL2CPP / Plugin / 时钟源 / 配置** | **能**:`tests/BehaviorTests` 直接编译执行(RF3 起) |
| **状态容器(RF4)** | `Runtime/`(6:跨场衔接 / 战场规则注册表 / 单场计数 / 活动环 / 攻击快照 / **历史环**) | 436 | 不依赖 IL2CPP / Plugin(只用 `BattleSession`) | **能**:`tests/BehaviorTests` 的 `runtime/*` 组 |
| 数据模型 | `Model/`(12 文件) | 1,958 | 无逻辑 | 部分 |
| 中枢/组合根 | `src` 根(10 文件:Plugin、GameRef、GameSystemAccess、BuildInfo + **Aggregator 6 个 partial**) | 1,970 | 单例 + 静态 + 时间源 | 否(编排留在门面;判据已下沉到 `Policy/`) |
| 取数与补丁 | `Hooks/`(5)+`Diagnostics/`(13) | 495 + 3,692 | IL2CPP | 否(每个探针一个开关) |
| 判定核心 | `Composition/`(14,含 10 个 `CompositionProbe*` partial) | 6,762 | IL2CPP | 否 |
| 主数据 | `MasterData/`(2) | 1,204 | IL2CPP + 反编译件 | 否 |
| 输出 | `Output/`(7) | 2,274 | 读会话状态 | `JsonCheck` 由 recon_probe 反向验证 |
| 界面 | `Ui/`(11) | 3,615 | 分三层:**`DisplayFormat` 纯排版**(43 用例)+ **`ContributionColumns` 列定义与三个行构造器**(39 用例)+ 渲染器(Unity) | 排版层、列定义与行构造能;渲染层靠布局守卫离线复算。**面板与回退同源**(RF5e 查证:数字本来就一份,格式化此前两套;回退的贡献行现由 `FallbackText` 构造) |

**最大的 7 个文件**(拆分候选,按行数):`Ui/OverlayUGUI.Rows.cs` **1,241**、
`Composition/CompositionProbe.Chain.cs` **1,191**、`Composition/CompositionProbe.Talents.cs` **847**、
`MasterData/MasterDataDump.cs` **828**、`Composition/AbilityRoster.cs` **791**、`Output/ExportService.cs` **707**、
`Composition/CompositionProbe.Diagnostics.cs` **684**。

**`Aggregator` 已不在榜上**:RF2 把它拆成 6 个文件,最大的一块是 `Aggregator.Stats.cs` 340 行。

**依赖方向(重构时必须保住)**

1. `Model/` 的少数几个类**故意不依赖 IL2CPP/Plugin**,所以离线探针能执行它们 —— 这类文件是"先动"的安全区。
2. `Hooks/` 只负责**取数与快照**;判定只允许发生在构成模块的**单一裁决点**(见 §8)。
3. `Output/` 在**导出时**从事件流重算(`CalcReconcile` 不从累加器取数),因此汇总永远可能与它不一致时是它说了算。
4. `Ui/` 与导出**共用同一条计算路径**(先 `Compute` 出结构化结果,再分别序列化/渲染);不允许界面各写一套口径。

**静态可变状态清单**(无法单测与生命周期 bug 的根因):`Aggregator.Session`、`CompositionProbe._globalRules/_snap*`、
`Probe.Counts`、`OverlayUGUI` 的若干 static。长期方向:把"一局"的状态挂到 `BattleSession` 实例上。

## 6. 离线工具地图

**常用工具(重构时的护栏,不要当一次性脚本删)**

| 工具 | 职责 | 输入 → 输出 |
|---|---|---|
| `n0_acceptance.py` | 验收流水线 + 50 条可证伪检查(桶 / 退出码 / 白名单 / 逐文件钉住) | exports → `acceptance_1.7.11/{corpus_manifest,runs,RESULTS.md}` |
| `comparison_eligibility.py` | 指标级准入契约 + 闸门映射 + 丢步降级 | exports + applicability → `eligibility.json`(exit 4) |
| `contrib/compare.py` | 跨场比较器 2.0(准入→分层→逐角色统计) | 23 份 411001 → `contrib/reports/compare2_411001.{txt,json}` |
| `decision_report.py` | 预注册配队决策报告(五节 + 交叉校验) | compare2 json → `DECISION-REPORT-411001.{md,json}` |
| `budget_census.py` | 丢步/溢出/读取失败/未知身份/残差分层普查 | exports → `BUDGET-CENSUS.{md,json}` |
| `identity_map.py` | entityKey / 身份强度 / 同实体判定 | export → actor 身份(54 checks) |
| `atkadd_sensitivity.py` | 攻击力加算归因敏感性(隔离实验) | export → `atkadd_sensitivity_result.{json,md}` |
| `pairtrusted_impact.py` | 配对可信度影响(A/B/C 三口径) | exports → 影响报告 |
| `contribution_applicability.py` | 任务适用性 full/partial/not_comparable | exports → applicability.json |
| `contribution_gate.py` | 验证闸门状态机(ERROR>DATA_MISSING>LEGACY>WARNING>PASS) | 单份 → 状态码 |
| `check_export_schema.py` | 键/类型/覆盖率 + 逐角色恒等式(**54 例自测**) | exports |
| `check_contribution_layout.py` | 布局守卫:显示列算术 + 渲染器↔副本对账 | exports + C# 源 |
| `check_live_log.py` | 实机日志:未归属行与导出对账、`hist` 单调 | LogOutput.log |
| `check_doc_convergence.py` | 文档收敛 8 规则(版本/哈希/语料总数/训练场/…)+ 10 例自测 | 5 份状态文档 |
| `check_docs_123.py` | 编码守卫(UTF-8 / U+FFFD / mojibake / 缺失文件 / CJK 存活),6 例自测 | **29 份当前文档集** |
| `check_fact_signature.py` / `check_given_fold_coupling.py` / `check_p2a_summary_and_lastbattle.py` / `refactor_final_check.py` / `v150_validate.py` | FACT 签名 / 开关耦合 20 条 / UI 契约 / 源码结构真阻断 / KPI 基线 | 源码 + exports |
| `contrib/crosscheck.py` | 插件 `contribution` 段 ↔ 离线核心**逐字段**比对 | export 段 |
| `contrib/run.py` / `report_text.py` / `report_json.py` / `loader.py` / `aggregate.py` / `model.py` / `attribution.py` | 单场重放与报告(C# 的独立等价实现) | 单份 export |
| `contrib/tests/`(3 个) | S1–S13 / 闸门 / 1.5.5 金样本回归 | 无导出也能跑 |
| `recon_probe/` | **C# 离线断言工程**(不依赖游戏) | `dotnet run` → out.json |
| `sample_intake.py` | 新导出只读体检(版本/任务/训练场/段/阵容/总量/命中) | 单份 export |

**一次性证据脚本(考古层,重构前建议打包归档,不要接进流水线)**:`evidence_*.py`(约 30 个)、`probe_*.py`、
`contrib_recon_155*.py`、`compare_comps.py`、`rules_census.py`、`verify_155.py`、`victim_check.py`、`umima_check.py`、
`madness_owner_check.py`、`contrib_gap_dealt.py`,以及 `_review/`、`_verify_178_live/`、`_verify_179/`、`contrib_cs/`、`enum_probe/` 等目录。

## 7. 重构就绪评估

### 7.1 护栏矩阵:改了什么,谁会红

| 改动 | 会被谁抓住 |
|---|---|
| 导出键/类型/逐角色恒等式 | `check_export_schema.py`(54 例自测 + 32 份实测) |
| 表宽/列标签/渲染器与副本漂移 | `check_contribution_layout.py`(解析 C# 源与副本对账) |
| 贡献数学(份额/恒等式) | `contrib/tests`、`contrib.crosscheck`、`recon_probe` |
| 文档与版本/哈希/语料总数不一致 | `check_doc_convergence.py`(8 规则 + 10 例) |
| CJK 文件被写坏 / 文档缺失 / 列错文件 | `check_docs_123.py`(29 份文档集 + 6 例自测,四种红灯都跑过) |
| 源码结构(缺文件/partial 缺失/死符号/版本不一致) | `refactor_final_check.py`(真阻断 + 10 例) |
| KPI 相对历史基线下降 | `v150_validate.py`(显式哈希固定的基线) |
| 界面契约(上一场未归属透传) | `check_p2a_summary_and_lastbattle.py` |
| 实机未归属行与导出不一致 | `check_live_log.py`(43 帧核对) |
| 比较/决策/准入语义 | 各自 `--selftest`(10/14/17+6/8 例) |

**护栏空洞(重构中真会漏的)**:①**没有 C# 单元测试工程** —— `Aggregator`/`OverlayUGUI` 的改动只能靠构建 + 实机 + 日志;
②**没有版本控制** —— 任何重构都没有"回到上一版"的能力,只能靠 `.bak` 与手工副本;③界面观感只能靠眼睛(第 6 轮起有部分日志证据)。

### 7.2 高危耦合热点

1. ~~**`Aggregator.cs`(1,359 行)**~~ **已按职责拆成 6 个 partial(RF2,见 §12)**;仍在的债务:硬编码时间窗(0.80/0.60/0.45/0.20/0.08 s)仍散在 `Aggregator.Attribution.cs` / `CompositionProbe*` 里 —— 提常量与判据下沉属 RF3。
2. **`Ui/OverlayUGUI.Rows.cs`(1,258 行)**:表格行构造与渲染(RF5b 已把**文字排版与数字格式**移到 `Ui/DisplayFormat.cs`,那部分有 43 个用例执行生产代码);**布局守卫仍直接解析这个文件的列标签/宽度**,改列宽必须同步跑布局守卫。
3. **`CompositionProbe*`(10 partial / 6.8k 行)**:判定链的单点裁决;拆分时最容易把"单一裁决点"拆成两处而静默改变折叠加法顺序。
4. **静态可变状态**:`Aggregator.Session` / `CompositionProbe._globalRules` / `OverlayUGUI` statics —— 跨场污染型 bug 只会以日志形式出现。
5. **`MasterData/`**:`MasterDataAccess.cs` **至今不存在**(模块地图里标注"下次改 dump 时合并两处")→ 同一份非泛型表查找有两处实现。
6. **一次性脚本与工具链混在同一目录**(60+ 个 `.py` 平铺),新人难以分辨哪些是活的护栏。

### 7.3 建议的重构批次(每批都要能独立验收)

| 批次 | 内容 | 出口验证 |
|---|---|---|
| **R0 前置** | ~~①纳入版本控制~~ **已完成(RF0:本地 git,标签 `baseline-1.7.11`)**,并补上仓库边界 / 基线清单 / 冻结输入快照 / 输出隔离;②把 `evidence_*`/`probe_*`/旧验证目录打包归档(**仍未做**,属 RF7;RF7b 已完成**脚本层索引**(43 条 `indexed`),归档动作本身留待前置满足);~~③文档集加自测~~ 已完成(现 32 份 + 6 例自测) | `repo_manifest --verify` drift=0 + `n0_acceptance.py` 33 命令 / 65 检查全绿 |
| **R1 无风险拆分** | ~~`Aggregator` 拆 partial~~ **已完成(RF2:6 文件,IL 级等价,见 §12)**;时间窗提为命名常量**已完成(RF3c:composition 容差、时钟上界复用)** | 构建 0 警 0 错 + 174 用例 + `recon_probe` + IL 等价 |
| **R2 纯函数下沉** | ~~时钟增量 / run 归组 / 软恢复闸门 / 归属配对判据与窗口~~ **已完成(RF3:3 个策略文件,269 用例,20 例负控,见 §12)**;**composition 链自身窗口**已完成(RF3c),`IdleSeconds` 已完成(RF4e);仅剩候选扫描(要读 IL2CPP 字段) | 用例 + 网格对照 + 变异负控 |
| **R3 界面** | ~~测量/截断/行构造~~ **测量/截断/数字格式**(RF5b:`Ui/DisplayFormat.cs`)+ **列定义**(RF5c:`Ui/ContributionColumns.cs`,表头与合计行由定义构造,守卫对账数据行)已完成;**行数据模型/RowViewModel** 也已完成(RF5f + RF5g:`Ui/ContributionRowModel.cs`) | 布局守卫语料 0 违规 + 目视 |
| **R4 数据侧** | `MasterDataAccess` 合并两处表查找(**RF6a 已定位并逐项对照**,抽取待实机验证机会;选名规则已抽出并覆盖);`HitRecord` 会心通道接线(见 §9) | schema 守卫 + 残差 `exact` 比例不下降 |
| **R5 状态生命周期(方案 RF4)** | ~~先交 `StateLifetimeMatrix`~~ **已交**([STATE-LIFETIME-MATRIX.md](<STATE-LIFETIME-MATRIX.md>));第 1 族(跨场衔接)已迁到 `Runtime/SessionContinuity`,并删掉矩阵查出的死状态;第 2 族**两半都完成**:判据半在 `Policy/GlobalRuleClassifier`,状态半在 `Runtime/GlobalRuleRegistry`(结算不清表/只回收死持有者/枚举序都有用例);其余族的前置条件写在矩阵 §7;**RF5a**:缓存判据抽到 `Policy/ContributionCachePolicy`(行为不变,既有 24 条 `cache/` 用例原样通过),缓存语义的**三个待决问题**写进 [CACHE-SEMANTICS-ADR.md](<CACHE-SEMANTICS-ADR.md>);**RF4d**:应用侧算术(属性门取值/副本数/逐状态幂)抽到 `Policy/GlobalRuleApplyPolicy`,门梯本体经三条代码证据论证**不再抽取**(矩阵 §8);**RF4e**:第三族(单场运行态)的第一片 —— 12 个计数/自报字段迁到 `Runtime/BattleRuntimeCounters`,重置规则从散文变成可执行方法(resume 与 finalize 故意不清) | 475 用例 + 58 例负控 + 全量验收;每族/每半批后重跑 ;第 3 族(单场运行态)已完成(计数 RF4e / 活动环 RF4f / 攻击快照 RF4g;仅剩一条字符串草稿 `_lastCalcSrc`,理由见 RF4g 记录) |

**顺序原则**:先有护栏再动刀;一次只动一层;每批都能单独回滚(回滚锚点 = 上一版 DLL + 源码快照)。

### 7.4 禁区(重构中不可动)

- 两个 `*-crash.bak`:**绝不回滚/删除**。
- `battle_411001_20261004_015919.json` 等已知坏样本:**不修不删不放宽容差**。
- 历史导出、`.bak`、证据 zip、旧报告正文:**只读**(要改"当前状态句"就另起一行,不动历史叙述)。
- 训练场(9999)与普通关卡**不得混进同一个排名**。
- 部署/版本/公共导出契约:**只有主负责人**可以改;重构批次不得自行部署。
- 不为了"看起来更精确"引入未经证据支持的倍数、暴击归因、治疗折伤害。

## 8. 不可破坏的不变量(重构的安全边界)

完整 44 条在 [`ARCHITECTURE.md`](<ARCHITECTURE.md>) §4;以下是**动代码前必须逐条确认**的最小集:

1. `totalCredit = baseCredit + selfRuleCredit + assistCredit`;`ΣtotalCredit + unattributedCredit = analyzableDealt`。
2. `totalDamage = analyzableDealt`;直接伤害与辅助当量**不可相加**当新总量。
3. 逐击折叠 `M = ∏folds`;规则间按 `ln(f_i)/ln(M)` 分池,**顺序无关**。
4. 丢步时等式降为**合法下界**,**不得**伪造补偿因子;有丢步的样本在准入层降级。
5. 属性倍率 / 原生 fold / 贡献虚拟因子**分开**,不拿贡献因子直接对 `knownMult`。
6. 零分母输出**不可用 + 原因**,缺数据不得伪装成 0 或 100%。
7. 覆盖率的**分母必须写明**;整场覆盖率不可用域内 `creditedShare` 替代。
8. 判定链保持**单一裁决点**(`JudgeClause`),诊断/显示/导出三条路径共用。
9. 状态集合只有一个**规范形**(`StatusKey`:去重 + 序数排序)。
10. 时间格式只有一个入口(`BattleTime`);导出结构自检(`JsonCheck`)在写盘前必须跑。

## 9. 未闭合项(按“谁能推进”分层)

**A. 只有用户能做(目视)** —— 见 [`N7-PERF-AND-VISUAL-CHECKLIST.md`](<N7-PERF-AND-VISUAL-CHECKLIST.md>) §7:长名对齐、F9 重置、
回看更早那一场、低覆盖提示、非 CJK 机器字体回退(共 5 项);未归属行已自动核对通过。

**B. 证据不足,当前明确不做**:插件侧 CPU/分配插桩(`MaxSteps=24`/`_statusSnaps=256`/`BuildBuffText` 40 条三个计数器)。
实测 **0/32 场**丢步 ⇒ 没有证据支持为它改插件实时路径。

**C. 需要用户拍板的产品决定**:队伍对比页(UI 变更,会升版本);`dealt` 是否改成"吸收前"口径(会动悬浮窗/DPS/曲线/全部历史对比)。

**D. 已知待核实的旧结论(重构前应逐条复核,别当现状)**:

- [`ARCHITECTURE.md`](<ARCHITECTURE.md>) §7 的债务清单写于 1.3–1.5 时代。**本轮已核实两条**:
  · 「`source` 通道从未接线」**已过期** —— `Aggregator.cs:1291` 现在是 `RecordHitDetail` 的调用点,最新一场 5,628 条事件里
    `source` 取值已分化(14:2592 / 1:1633 / 0:1312 / 3:38 / 4:37 / 8:16)。
  · 「`crit` 恒 false」**仍然成立** —— 同一场 5,628/5,628 全为 false,会心仍只能靠 `critDamageRate` 推断。
- 其余条目(跨单位「被ダメージ+X%」读取路径、全局 +15% 重复登记、会话被波切碎、`dealt` 口径)在 1.5.x 之后是否已闭合,**未逐条复核**。

## 10. 文档年龄表(哪些能当现状,哪些只是历史快照)

| 文档 | 是什么 | 能不能当现状 |
|---|---|---|
| [`DpsMeter-文档索引.md`](../DpsMeter-文档索引.md) | 唯一入口 / 指针 | ✅ 当前(随轮次更新) |
| **本文件** | 现状快照 + 重构地图 | ✅ 当前(随轮次更新) |
| [`CONTRIBUTION-NEXT-PHASE-ROADMAP.md`](<CONTRIBUTION-NEXT-PHASE-ROADMAP.md>) | N0–N7 路线图 + 逐任务进度块 | ✅ 当前(§0 进度表为准) |
| [`RELEASE-1.7.11-ACCEPTANCE.md`](<RELEASE-1.7.11-ACCEPTANCE.md>) | 1.7.11 验收记录(§0–§9 = 30 份时;§10 = 32 份第 6 轮) | ✅ 当前(以 §10 为准) |
| [`HANDOFF.md`](<HANDOFF.md>) | 交接书:环境硬事实 / 编码坑 / 部署表 / 历史验收账 | 🟡 环境章节是现状;历史验收账是快照 |
| [`DECISION-REPORT-411001.md`](<DECISION-REPORT-411001.md>) · [`BUDGET-CENSUS.md`](<BUDGET-CENSUS.md>) · [`N7-PERF-AND-VISUAL-CHECKLIST.md`](<N7-PERF-AND-VISUAL-CHECKLIST.md>) | 决策 / 普查 / 目视记录 | ✅ 当前(工具每次重算覆盖) |
| [`CONTRIBUTION-DATA-DICTIONARY.md`](<CONTRIBUTION-DATA-DICTIONARY.md>) | 口径/公式/字段字典(schema 1.1) | ✅ 当前 |
| [`ARCHITECTURE.md`](<ARCHITECTURE.md>) · [`ARCH-REVIEW-1.5.md`](<ARCH-REVIEW-1.5.md>) | 架构 / 1.5 时代审视 | 🟡 结构仍准;**§7 债务清单需逐条复核**(见 §9D) |
| [`IDENTITY-CENSUS.md`](<IDENTITY-CENSUS.md>) · [`IDENTITY-METADATA-DESIGN.md`](<IDENTITY-METADATA-DESIGN.md>) · [`ATKADD-MODEL-AUDIT.md`](<ATKADD-MODEL-AUDIT.md>) · [`atkadd_sensitivity_result.md`](<atkadd_sensitivity_result.md>) | 30 份时的身份/atkadd 研究 | 🟡 **当时快照**(30 份),结论可用、计数不可当现状 |
| [`REFACTOR-BATCH-RF0-RF2.md`](<REFACTOR-BATCH-RF0-RF2.md>) | **重构第 1 轮记录**(RF0–RF2:文件清单 / 证据 / 未覆盖项 / 回滚) | ✅ 当前(该批次) |
| [`REFACTOR-BATCH-RF3.md`](<REFACTOR-BATCH-RF3.md>) | **重构第 2 轮记录**(RF3 纯判据下沉:抽了什么 / 四处去重 / 未做) | ✅ 当前(该批次) |
| [`STATE-LIFETIME-MATRIX.md`](<STATE-LIFETIME-MATRIX.md>) · [`REFACTOR-BATCH-RF4.md`](<REFACTOR-BATCH-RF4.md>) · [`REFACTOR-BATCH-RF4B.md`](<REFACTOR-BATCH-RF4B.md>) · [`REFACTOR-BATCH-RF4C.md`](<REFACTOR-BATCH-RF4C.md>) · [`CACHE-SEMANTICS-ADR.md`](<CACHE-SEMANTICS-ADR.md>) · [`REFACTOR-BATCH-RF5A.md`](<REFACTOR-BATCH-RF5A.md>) · [`REFACTOR-BATCH-RF4D.md`](<REFACTOR-BATCH-RF4D.md>) · [`REFACTOR-BATCH-RF4E.md`](<REFACTOR-BATCH-RF4E.md>) | **状态生命周期矩阵**、第 3–9 轮记录(状态族迁移 → 规则判据半 → 注册表所有权 → 缓存判据与 ADR → 应用侧算术 → 单场计数与重置转换)、**缓存语义 ADR**(三个待决问题) | ✅ 当前 |
| [`../../REPO-BOUNDARY.md`](<../../REPO-BOUNDARY.md>) · [`baseline-manifest.json`](<baseline-manifest.json>) · [`batch-inputs-rf0.json`](<batch-inputs-rf0.json>) | 仓库边界 / 基线清单 / 本批输入清单 | ✅ 当前(每次输入变化要重新 `--write`) |
| [`CONTRIBUTION-TABLE-REPORT.md`](<CONTRIBUTION-TABLE-REPORT.md>) | 贡献表完整报告(26/29 份时代) | 🟡 历史报告:只改了当前状态句,历史段落保持原样 |
| [`SESSION-STATE.md`](<SESSION-STATE.md>)(377 KB) | 主档:§1–§6 API/限制、§7.2.x 逐版决策 | 🟡 **逐版决策档案**,不是现状摘要 |
| [`P0-A-VALIDATION-GATE-REPORT.md`](<P0-A-VALIDATION-GATE-REPORT.md>) · [`CONTRIBUTION-APPLICABILITY-P0D.md`](<CONTRIBUTION-APPLICABILITY-P0D.md>) · [`PAIRTRUSTED-IMPACT-REPORT.md`](<PAIRTRUSTED-IMPACT-REPORT.md>) · [`CONTRIBUTION-REVIEW-NEXT-STEPS.md`](<CONTRIBUTION-REVIEW-NEXT-STEPS.md>) | 历史验收/审查报告 | 🟡 历史快照 |
| [`ROLLBACK-1.7.11.md`](<ROLLBACK-1.7.11.md>) | **当前**回退预案(锚点 1.7.10) | ✅ 当前 |
| `ROLLBACK-1.7.7/1.7.8/1.7.9/1.7.10/1.6.0.md` | 旧回退预案 | 🟡 已作废(留作锚点哈希表) |
| `_dpsm_work/doc_archive_20261003.zip` · `export_archive_20261003.zip` | 归档:8 份逐版说明 / 3 份证据战场 | 📦 归档 |
| `REPORT-换人后总伤害为什么变低-20261003.txt` · `REPORT-两种搭配对比-20261003.txt` · `REPORT-解包与游戏内数据获取.md` | 早期面向用户的结案报告 | 🟡 历史(结论已被 §N5 取代) |

## 11. 维护约定

- **本文件与索引一起更新**:任何一轮结束时,先更新本文件的事实表,再更新索引指针;历史段落不重写。
- 新增"当前状态句"的数字必须能被 §4 的命令复算,否则不写。
- 每次改动后跑:`python n0_acceptance.py`(33 命令 / 65 检查,全绿);只改文档时至少跑 `check_doc_convergence.py` + `check_docs_123.py`。
- **输入会变**:`exports\` 是游戏写的活目录。每次新开一批先 `python batch_snapshot.py --name <批名>` 并提交清单,再让 `n0` 读快照;批中新增的战斗属于**下一批**。
- **数字与守卫同步**:改了流水线的命令/检查数,必须同步本文与索引中"37 条命令 / 69 条检查"的说法,否则 R9 会红(这是设计,不是麻烦)。注意**检查总数会随数据移动**:桶集合与"本次真正被重写的固定路径数"都会改变行数,所以数字要复算而不是抄。

## 12. 重构批次记录(按 REFACTOR-PLAN-POST-1.7.11.md)

| 批次 | 内容 | 记录 |
|---|---|---|
| **第 1 轮 RF0–RF2** | 本地 git 基线 + 仓库边界 + 基线清单 + 冻结批次输入 + 输出隔离 + 文档勘误;174 用例的规范化行为测试 + 10 例变异负控;`Aggregator` 机械拆成 6 个 partial(IL 级等价) | [REFACTOR-BATCH-RF0-RF2.md](<REFACTOR-BATCH-RF0-RF2.md>) |
| **第 2 轮 RF3** | 时钟增量 / run 归组 / 软恢复闸门 / 归属配对判据与窗口下沉为 `src/Policy/` 3 个纯文件;四处重复(来源规则、伤害匹配条件、存活窗口、pending 上限)各归一处;269 用例 + 20 例负控 + 7 组"旧式表达式"网格对照 | [REFACTOR-BATCH-RF3.md](<REFACTOR-BATCH-RF3.md>) |
| **第 3 轮 RF4(第 1 步)** | 先交 [STATE-LIFETIME-MATRIX.md](<STATE-LIFETIME-MATRIX.md)>(五类生命周期 + `Aggregator` 逐字段表 + 6 项发现);迁移第 1 族"跨场衔接"到 `src/Runtime/SessionContinuity.cs`(118 行);按矩阵证据删除死状态 `_lastCompT`/`_lastPow`;空闲关闭规则并入策略层;320 用例 / 26 例负控 | [REFACTOR-BATCH-RF4.md](<REFACTOR-BATCH-RF4.md>) |
| **第 4 轮 RF4b(第 2 族判据半)** | 抽 `src/Policy/GlobalRuleClassifier.cs`(141 行):分句分类的 11 项字段、登记跳过(含指针复用检测)、阀门两级阈值;登记回路保留原生读取与身份字段;夹具用**线上实测文本**(`[RULE] 敌受伤 ×1.150 …`,18:03:50);378 用例 / 34 例负控 | [REFACTOR-BATCH-RF4B.md](<REFACTOR-BATCH-RF4B.md>) |
| **第 6 轮 RF5a(缓存判据)** | 缓存陈旧判据 / "不可用"四码与文案 / 来源选择抽到 `src/Policy/ContributionCachePolicy.cs`(89 行,行为不变:既有 24 条 `cache/` 用例原样通过);交付 [CACHE-SEMANTICS-ADR.md](<CACHE-SEMANTICS-ADR.md>) 并记录三项发现(四路 OR 不是节流、折叠支不可达、`Invalidate()` 在生产里没有调用者);430 用例 / 46 例负控 | [REFACTOR-BATCH-RF5A.md](<REFACTOR-BATCH-RF5A.md>) |
| **第 7 轮 RF4d(应用侧算术)** | 属性门取值 / 副本数 / 逐状态幂抽到 `src/Policy/GlobalRuleApplyPolicy.cs`(67 行,仍是循环累乘而非 `Math.Pow`:实测差 1 ulp);用三条代码证据论证应用侧**门梯本体不抽**(惰性原生读取、读序即行为、每元素 try/catch)并写入矩阵 §8;452 用例 / 52 例负控 | [REFACTOR-BATCH-RF4D.md](<REFACTOR-BATCH-RF4D.md>) |
| **第 9 轮 RF4e(单场计数)** | 第三族第一片:12 个计数/自报字段迁到 `src/Runtime/BattleRuntimeCounters.cs`(85 行),44 处读写改为 `Rt.X`,重置规则变成 `OnSessionStart`/`OnManualReset`(resume 与 finalize **不清**任何一项,后者故意没有方法);含 12 字段名的结构钉住;475 用例 / 58 例负控 | [REFACTOR-BATCH-RF4E.md](<REFACTOR-BATCH-RF4E.md>) |
| **第 10 轮 RF5b(文字排版)** | 覆盖层的文字排版与数字格式从 `Ui/OverlayUGUI.Rows.cs` 分到 `src/Ui/DisplayFormat.cs`(481 行 / 11 成员;Rows 1,375 → 1,258);**编译进行为套件、43 个用例直接执行生产代码**,含方案要求的 CJK/长名/极值/零/未知与代理对不可切断;澄清布局守卫只对账列宽;518 用例 / 64 例负控 | [REFACTOR-BATCH-RF5B.md](<REFACTOR-BATCH-RF5B.md>) |
| **第 11 轮 RF5c(列定义)** | 三张覆盖层表的列定义(标签/宽度/对齐)与表头/合计行构造移到 `src/Ui/ContributionColumns.cs`;表头与合计行**由定义构造**,数据行宽度由布局守卫与定义对账;实测语义:**行宽 = 列宽和 + 2**(T1 列 83/行 85);542 用例 / 69 例负控 | [REFACTOR-BATCH-RF5C.md](<REFACTOR-BATCH-RF5C.md>) |
| **第 12 轮 RF5d(数据行)** | `T1Row/T2Row/T3Row` 三个构造器进 `ContributionColumns`:数据行不再内联宽度,**"受校验的副本"变成"没有副本"**;布局守卫改为结构性检查(常量顺序 + 禁止裸宽度);用例核心是"任何取值下行宽恒等于表宽";557 用例 / 73 例负控 | [REFACTOR-BATCH-RF5D.md](<REFACTOR-BATCH-RF5D.md>) |
| **第 13 轮 RF5e(回退渲染器)** | 查证方案 §10 的"两渲染器":**数字本来就一份**、**格式化此前两套**;新增 `src/Ui/FallbackText.cs` 两条纯构造器,回退的 9 处直接格式化全部改走 `DisplayFormat`(剩余 0 处;秒伤 `:F0` 故意保留);567 用例 / 76 例负控 | [REFACTOR-BATCH-RF5E.md](<REFACTOR-BATCH-RF5E.md>) |
| **第 14 轮 RF6a(主数据)** | 定位方案 §11 说的"两处重复"(两条表路由共享非泛型查找/多实例取大/失败分类,已逐项对照);抽出**可离线判定**的官方名规则 `src/Policy/MasterDataLabelPolicy.cs`(103 行,用源码里记录的**实测刻印夹具**做用例);表路由抽取因 IL2CPP 泛型无法离线编译而**明确延后**(理由与对照表已留档);591 用例 / 80 例负控 | [REFACTOR-BATCH-RF6A.md](<REFACTOR-BATCH-RF6A.md>) |
| **第 15 轮 RF3c(链容差)** | R1/R2 的最后一项:composition 链自己的 5 个容差从裸字面量变为 `src/Policy/CompositionTolerancePolicy.cs`(53 行;Chain 11 处 + Crit 门 1 处,链内裸容差剩余 0);暴击上界**改为引用** `BattleClockPolicy.MaxFrameDelta`,不再写第二个 0.25;607 用例 / 84 例负控 | [REFACTOR-BATCH-RF3C.md](<REFACTOR-BATCH-RF3C.md>) |
| **第 16 轮 RF4f(活动环)** | 第三族余下的顺序半边:`_calcEvents` 的 FIFO 与上限 64 迁到 `src/Runtime/CalcActivityLog.cs`(泛型,Count/可读写下标器/Add/Clear),**门面只改两处**,其余 14 处读写一行未动;清空仍在会话开始/软恢复/结算三处由调用方决定;625 用例 / 87 例负控 | [REFACTOR-BATCH-RF4F.md](<REFACTOR-BATCH-RF4F.md>) |
| **第 17 轮 RF7b(工具判定)** | 43 条脚本按**机械证据**判定为 `indexed`(无流水线/无导入者/当前文档未点名),未判定上限 74 → **31**;守卫新增 **G**:`indexed` 必须真的没有导入者且不在流水线,否则变红(声明被复核而不是被信任);注册表报告重新生成;插件源码零变化 | [REFACTOR-BATCH-RF7B.md](<REFACTOR-BATCH-RF7B.md>) |
| **第 18 轮 RF5f(行值模型)** | RF5 的最后一项 RowViewModel:贡献表的"哪些演员上表 / 两个占比 / 页脚四个求和"抽到 `src/Ui/ContributionRowModel.cs`;渲染器由**两遍遍历**并为一遍;用例分别钉住"显示集合"与"求和集合"(前者过滤、后者覆盖全部演员);645 用例 / 90 例负控 | [REFACTOR-BATCH-RF5F.md](<REFACTOR-BATCH-RF5F.md>) |
| **第 19 轮 RF5g(两张表)** | 规则表(T2)与关系表(T3)的行值也进 `ContributionRowModel`(过滤/上限 12 具名/端点命名 + `#key` 兜底);渲染器两个循环改为遍历模型行;664 用例 / 93 例负控 | [REFACTOR-BATCH-RF5G.md](<REFACTOR-BATCH-RF5G.md>) |
| **第 20 轮 RF4g(攻击快照)** | 第三族最后一块:`_activeCalc`/`_activeCalcT` 迁到 `src/Runtime/AttackSnapshot.cs`,`Valid = calc 非空 且 戳 ≥ 0`(直接指向 1.5.0"上一场 calc 标注本场命中"的缺陷);接线 8 处;679 用例 / 96 例负控。第三族仅剩 `_lastCalcSrc`(单条草稿,已写明理由) | [REFACTOR-BATCH-RF4G.md](<REFACTOR-BATCH-RF4G.md>) |
| **第 21 轮 RF8a(状态收敛)** | 把 R0–R5 表里"仍未做"的陈述逐条与代码核对并收敛(R2 的链窗口/`IdleSeconds`、R3 的 RowViewModel、R4 的 MasterDataAccess 进度、R5 的第 3 族、R0 的索引 vs 归档);**写明状态句仍只能靠人核对**这一缺口与补法(R12 需要自测才能加);插件源码零变化 | [REFACTOR-BATCH-RF8A.md](<REFACTOR-BATCH-RF8A.md>) |
| **第 22 轮 RF8b(状态可验证)** | 文档收敛守卫新增 **R12**:产物存在时,当前文档集**不得**再说该事项"未做";短语须同时含否定词与特征名词,历史批次记录被豁免;配套对照 **M12** 证明它可红(12 条规则 0 失败);插件源码零变化 | [REFACTOR-BATCH-RF8B.md](<REFACTOR-BATCH-RF8B.md>) |
| **第 23 轮 RF4h(历史环)** | 第四族(进程级)里唯一有真实规则的部分:`History` 的上限/新→旧/淘汰进 `src/Runtime/BattleHistoryRing.cs`;顺带修掉"结算里是字面量 20、`MaxHistory` 也是 20"的双份上限(现在 `MaxHistory = BattleHistoryRing.Max`);691 用例 / 99 例负控 | [REFACTOR-BATCH-RF4H.md](<REFACTOR-BATCH-RF4H.md>) |
| **第 24 轮 RF7c(证据哈希索引)** | 归档的第二前置:新增 `archive_index.py`,把 `evidence_*`/`probe_*`/`review_contrib_core` 的 **56 份 / 212,552 字节**连同 SHA256 记入 `archive-index.json` 与 [ARCHIVE-INDEX.md](<ARCHIVE-INDEX.md>);`--verify` 对改动/丢失/未记录新增报红(4 个自测对照);**不移动不删除任何证据**,也未接进验收(留给单独一轮);新脚本一落地即被注册表守卫 B 检查点名并登记为 active | [REFACTOR-BATCH-RF7C.md](<REFACTOR-BATCH-RF7C.md>) |
| **第 25 轮 RF7d(索引接入)** | 把第 24 轮刻意留在流水线外的 `archive_index.py` 接进来(2 条 run:校验 + 自测),命令/检查数 **35/67 → 37/69** 并同步四处文档说法;自测只改临时副本,故可安全入线;插件源码零变化 | [REFACTOR-BATCH-RF7D.md](<REFACTOR-BATCH-RF7D.md>) |
| **第 26 轮 RF5h(内联格式收尾)** | `DisplayFormat.Whole`(F0,**不带千分位**,刻意不是 `Num`)接入 3 处"秒伤",行内 `:F0` 剩余 0;697 用例 / 100 例负控。**更正**:此前"除一处外已无直接 `:N0`/`:F2`"的说法不准确——`OverlayUGUI.Chart.cs` 仍有 4 处,统一它们会改变用户所见,需一次**决定**;另记录一次 shell 重写源码导致 167 个错误、已还原并用定点编辑重做 | [REFACTOR-BATCH-RF5H.md](<REFACTOR-BATCH-RF5H.md>) |
| **第 27 轮(全面复核,无代码改动)** | 对已达成的部分做一次端到端复核:**部署 DLL** 387,072 B / `36EC96D4…8BC42`(15–27 轮未替换);验收 **37 条命令 / 69 条检查 / 0 项**;行为套件 **697 用例 0 失败**;变异负控 **100 例 0 失败**;`refactor_final_check` blocks=0;文档收敛 **0/12**;docs123 62 份 0 损伤;工具注册表 PASS;布局守卫 35 份 0 违规;证据哈希索引 PASS;`repo_manifest --verify` drift=0;git 干净于 `4c92e10` | 本轮无产物;证据见本行与 [HANDOFF.md](<HANDOFF.md>) |
| **第 28 轮 RF7e(工具判定续)** | 31 条被引用脚本中证据明确的三条:`batch_snapshot.py` → active(它是 `batch_inputs/rf0` 的来源),`compare_comps.py` / `atkadd_sensitivity.py` → indexed(docstring 证明是历史研究);判定证据固定为"导入者/流水线/文档点名 + **脚本自己的 docstring**";未判定上限 31 → **28**;并写明剩下 28 条需要**先扩展守卫 G 到"活跃导入者"**才能判(独立规则改动) | [REFACTOR-BATCH-RF7E.md](<REFACTOR-BATCH-RF7E.md>) |
| **第 29 轮 RF7f(G 放宽 + J)** | 守卫 G 由"任何导入者都没有"改为**传递性活跃**(种子 = active ∪ 流水线),并配两个对照(被活跃脚本导入⇒红;只被已死脚本导入⇒不报);新增 **J**:被索引者不得出现在 `n0_acceptance.py` 文本里。**一次被自己抓住的错误**:按"仅认 import"批量判定时把 `n0_acceptance.py` 与 `tests/negative_control.py` 判成"没人依赖",已从 git 还原注册表——这证明了**按名字调用**才是本仓库的主要使用方式,`imports_local` 不足以判定存活;未判定数仍为 28 | [REFACTOR-BATCH-RF7F.md](<REFACTOR-BATCH-RF7F.md>) |
| **第 30 轮 RF7g(证据表)** | 新增 `tool_census.py --invokers`:对 28 条未判定脚本列出"哪些已登记脚本的**源码文本**里出现它的名字",并单独标出其中的 active 者;**刻意不是闸门**(名字可能只是注释,做成硬规则会因一句注释变红,故先做给人看的证据表);首轮即定一条:`contrib/report_json.py` 被两个 active 脚本点名,不应判为"无人依赖";未判定仍 28 | [REFACTOR-BATCH-RF7G.md](<REFACTOR-BATCH-RF7G.md>) |
| **第 31 轮 RF7h(判定第一批)** | 按第 30 轮证据表判定**证据三面全空**的一批:14 条 → `indexed`,未判定 **28 → 14**(上限同步收紧);保留的 14 条各有机械理由(其中 `n0_acceptance.py` 被 2 个 active 脚本点名,上一轮的错误现被结构性挡住);G+J 复核 PASS;插件源码零变化 | [REFACTOR-BATCH-RF7H.md](<REFACTOR-BATCH-RF7H.md>) |
| **第 32 轮 RF7i(闭包内两条)** | `contribution_gate.py`(被 3 个 active 脚本导入)与 `identity_map.py`(两个导入者本身就是 active)判为 **active**,用途取自各自 docstring;未判定 14 → **12**;过程被守卫纠正两次:输出字段约定(`["none (read-only)"]`)与**我自己的证据字符串不实**(曾把"排序第一的导入者"当"活跃导入者",已改为先筛 active 导入者、否则退回未判定) | [REFACTOR-BATCH-RF7I.md](<REFACTOR-BATCH-RF7I.md>) |
| **第 33 轮 RF7j(入口转正)** | `n0_acceptance.py`(验收入口)与 `tests/negative_control.py`(变异闸门)转为 **active**,输出按实际所见写清;未判定 12 → **10**;机械检查得到一个明确结论:**`contrib/` 包内没有任何成员被 active 脚本导入**——余下判定应以"整包读一次调用图"的方式进行,而非逐文件猜 | [REFACTOR-BATCH-RF7J.md](<REFACTOR-BATCH-RF7J.md>) |
| **第 34 轮 RF7k(孤岛判定)** | 把判据补全为"无活脚本导入 / 无活脚本点名 / 不在流水线脚本里",据此判定 5 条包内子图(死根 `contrib/run.py`);未判定 10 → **5**;留下的 5 条**每条都有活脚本点名**,只需读那一行点名(导入/调用/文本模式)即可全部落定——人工读的边界已收窄到 **5 处** | [REFACTOR-BATCH-RF7K.md](<REFACTOR-BATCH-RF7K.md>) |
| **第 35 轮 RF7l(读点名行)** | 逐处读了第 34 轮留下的 5 处点名,**结论:全部是文本(注释/docstring/列表),没有一处是调用**——即"活点名"从来不是"有活调用者"的证据,第 31 轮把它当保留理由过强;判据回到**真依赖**(流水线文本 J + 活闸门输入清单);3 条判死,未判定 5 → **3**;并自查改正一处误判(`dpsmeter_contrib.py` 在仓库根,我的路径前缀没匹配上);剩下 3 条缺的是状态词汇("被引用的输入")而非证据 | [REFACTOR-BATCH-RF7L.md](<REFACTOR-BATCH-RF7L.md>) |
| **第 36 轮 RF7m(状态词汇)** | 判定的缺口是**词汇**而非证据:新增状态 **`referenced-input`**(有人读、没人跑的输入文件)+ 守卫检查 **K**(必须声明读者、读者须为 active 且其源码文本确实含该文件、且不得在流水线);`contrib/report_text.py` 与 `dpsmeter_contrib.py`(被活闸门 `check_docs_123.py` 读取)归入此类;未判定 3 → **1**;最后 1 条卡在 J 把**注释**也当成"流水线点名",收尾需单独一次 J 判据改动 | [REFACTOR-BATCH-RF7M.md](<REFACTOR-BATCH-RF7M.md>) |
| **第 37 轮 RF7n(J 只看运行行)** | 把 J 的判据从"整份文本出现"改为"**同时含 `PY` 的运行行**出现",配两个对照(RUN 行⇒红;注释⇒不报);据此 `contrib/validate.py`(唯一提及是注释)判为 indexed —— **未判定 1 → 0**,RF7 工具治理收口:96 = 27 active + 67 indexed + 2 referenced-input + 0 unclassified | [REFACTOR-BATCH-RF7N.md](<REFACTOR-BATCH-RF7N.md>) |
| **第 38 轮(全量复核,无代码改动)** | 第 37 轮先后留下一次红提交,故本轮把**整条验收重跑**一遍作最终核对:`acceptance_r38` **37 条命令 / 69 条检查 / 0 项**;行为套件 697 用例 0 失败;`check_tool_registry`(G/J/K 全部有牙)PASS;`refactor_final_check` blocks=0;docs123 72 份 0 损伤;文档收敛 0/12;工具治理 **0 条未判定**;部署 DLL 未替换 | 证据即本轮归档 `acceptance_r38` |
| **第 39 轮(第 4 族收尾结论)** | 矩阵新增 §9:把第 4 族剩下的**帧循环游标**(`Clock`/`_lastTickClock`/`_lastTickFrame`/`_lastGameSteps`/`_hasLastSteps`/`_lastGsPointer`)明确记为**不迁移**,并写出三条理由(无独立规则可搬 / 语义与原生帧循环绑死且离线不可验证 / 热路径风险不对称、收益被方案标注为低);说明为何同族里 `History` 搬了而它们不搬——按"有无可测规则"划分,不是半途而废 | [STATE-LIFETIME-MATRIX.md](<STATE-LIFETIME-MATRIX.md>) |
| **第 41 轮(缓存决定落地 + Chart 格式统一)** | 用户对缓存三问与 Chart 格式给出决定后落地:缓存规则明确"1 秒节流(严格 `>`)、时间窗是唯一时间判据、切换/开关/结算绕过";新增 `ContributionSession.Generation`(**F9 必须失效,且不能只靠事件数**——重置前后事件数可能相同),F9 两个渲染入口调用 `Invalidate()`;Chart 的 5 处 `:N0` 收到 `DisplayFormat.Num`(百分比 `:F0%` 按决定保留);709 用例 / 102 例负控 | [REFACTOR-BATCH-R41.md](<REFACTOR-BATCH-R41.md>) |
| **第 42 轮(R42 快照)** | 把用户这一场冻成批次 **r42**(方案自带 `batch_snapshot.py`,硬链接,`--verify` drift=0):**46 文件 / 628.7 MB**;新那一场的任务号属于**训练场**类、带贡献段、`schemaVersion 1.1`——按 R6 既有判定,该类别**不参与可比基线**;同一时间戳刷新的 **masterdata 转储**才是 `MasterDataAccess`(§11)实机验证缺的那一半;本轮**不含插件改动** | [REFACTOR-BATCH-R42.md](<REFACTOR-BATCH-R42.md>) |
| **第 43 轮(MasterDataAccess 设计)** | 按方案 §11"先定位调用方并读全上下文"复核两处重复(标签路由 MasterDataNames.cs:292/:329;转储路由 MasterDataDump.cs:389,19 个调用点),把方案点名的**七条必须保持的行为**逐条对应到代码位置与处置(非泛型集合路径、判空差异不得统一、解密只调用、布局/计数/重试留在转储侧、存活检查时机不变),并定死下一轮验证法(以 r42 转储逐键逐字段对照,差异须为 0);本轮不含代码改动 | [REFACTOR-BATCH-R43.md](<REFACTOR-BATCH-R43.md>) |
| **第 44 轮(masterdata 基准冻结)** | 确认 r42 快照只含导出(46 战斗文件)不含 masterdata,故把活目录的转储**只读复制**冻结为对照基准:_dpsm_work/batch_inputs/r42-masterdata/ + 带 SHA256 的清单 masterdata-baseline-r42.json,**20 份 / 410,594 字节**;源目录未修改。此后抽取轮只读仓库内副本,不再需要读活目录;插件源码零变化 | [REFACTOR-BATCH-R44.md](<REFACTOR-BATCH-R44.md>) |
| **第 45 轮(重复点精确定位)** | 读全上下文后把"两处重复"精确到**两个具名方法**:MasterDataNames.FindBest(290 行)+ Rows(327 行) <-> MasterDataDump.Table 的内联循环(416 行起);共享的是三条来之不易的知识(非泛型 AOT 安全查找、扫全部实例取行数最多者、逐元素 try/catch 与计数),两侧各自的计数器/布局/重试保留;抽取动作已收敛为"搬运这两个方法 + 两侧改调用 + 以 r42 基准逐键对照";**本轮仍不含代码改动**(读取路径改动需实机验证) | [REFACTOR-BATCH-R45.md](<REFACTOR-BATCH-R45.md>) |
| **第 46 轮(MasterDataAccess 第一阶段)** | 新建 `src/MasterData/MasterDataAccess.cs`(`FindBest` + `Rows` + `BestInstance`),把两处共享的算法(非泛型 AOT 安全查找 / 扫全部实例取行数最多者 / 逐元素 try/catch)搬入,**标签路由**改为薄包装且计数器原样保留(MasterDataNames 338→321 行);**构建 0 警 0 错**。**转储路由仍是内联副本**,故重复此刻尚未消失;本轮**不宣称行为等价**——行为套件不含 `MasterData/*`,真正验证是第三阶段的逐键对照 | [REFACTOR-BATCH-R46.md](<REFACTOR-BATCH-R46.md>) |
| **未做** | RF3b(composition 链自身窗口 / 候选扫描)、RF4 其余族(单场运行态 / 攻击快照 / 进程级 / 展示级)与 `ApplyGlobalDebuffs`、RF5 展示层与缓存、RF6 主数据适配器、RF7 工具归档 | — |

三条要点:

1. **部署始终未变**:DLL 仍 1.7.11 / 387,072 B / `36EC96D4…`;RF0 已经证明"用 `src` 重建得到的产物与部署逐字节相同"。
2. **判据现在可以离线执行**:`tests/BehaviorTests` 的 `<Compile>` 清单包含 `src/Policy/*.cs`,所以任何让策略层依赖 Unity/IL2CPP/配置的改动会**构建失败**,而不是悄悄漂移。
3. **未覆盖的要写下来**:门面(编排、原生读取、热路径候选扫描)没有任何离线测试覆盖;策略用例证明的是"判据正确",不是"调用点正确" —— 调用点靠差异审查 + 构建 + 真实导出回归。


