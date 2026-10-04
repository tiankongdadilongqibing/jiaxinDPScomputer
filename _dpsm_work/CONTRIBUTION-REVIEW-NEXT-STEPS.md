# DpsMeter 贡献系统审查结论与后续实施建议

> 用途：将贡献表审查结果整理为可直接分派给其他智能体的任务说明。
> 依据：CONTRIBUTION-TABLE-REPORT.md。
> 派发基线：插件 1.7.6（派发任务时的基线；当前 1.7.9），主样本为 quest 411001 的 1.7.6 导出。
> 本文原本只描述后续任务；截至 2026-10-04，**七个任务全部完成**（P0-A/B/C/D = 1.7.8，P1-A = 1.7.8，P1-B = 1.7.9，P2-A = 1.7.7/1.7.8），见下表与各任务节首的「进度」块。

## 0.5 进度总表（一一对应，2026-10-04）

| 任务 | 状态 | 落地版本 | 交付物 | 验收怎么复现（命令） |
|---|---|---|---|---|
| **P0-A** 验证闸门 | ✅ 完成 | 1.7.8 | `contribution_gate.py`（状态词表 + 退出码）、crosscheck/validate/golden/fact_signature 全部接入、`P0-A-VALIDATION-GATE-REPORT.md` | `python -m contrib.tests.test_gate`（**59 passed**）；`python check_fact_signature.py --selftest`；`python -m contrib.crosscheck --selftest` |
| **P0-B** 数学字段与覆盖率 | ✅ 完成 | 1.7.8 | schema **1.1**：`totalDamage = analyzableDealt`、守恒式、`excludedDamage` 与 `unattributedCredit` 分离、三个覆盖率、`damageLedger` | `python check_export_schema.py --selftest`（**52 例**，含 1000/600/100/400/500 验收样例）；`python check_export_schema.py`（problems=0） |
| **P0-C** PairTrusted 影响 | ✅ 完成 | 离线（1.7.8 补测） | `pairtrusted_impact.py` + `PAIRTRUSTED-IMPACT-REPORT.md`；结论 **A≡B 逐字段全等**，`PairCorroborated` 被否决（只会保留 0.013% 池） | `python pairtrusted_impact.py --selftest`（**11/11**）；报告 §A/§B/§C |
| **P0-D** 任务适用性 | ✅ 完成 | 离线 | `contribution_applicability.py` + 报告；`modelApplicability`/`knownMultWithoutFoldHits`/`knownMultWithoutFoldDamage`/`specialMode`/`comparisonAllowed`/`reasons` | `python contribution_applicability.py`（29 份 = **full 12 / partial 9 / not_comparable 8**）；`--selftest` 18/18 |
| **P1-A** atkadd actor key | ✅ 完成 | 1.7.8 | `AtkAddFold` key 优先 + `selfByKey`/`selfByNameFallback`/`nameCollision`/`ownerUnknown` + 分区恒等式 | `recon_probe`（同名队友/无 key 回退/同名冲突 20 条断言，ALL CHECKS PASSED） |
| **P1-B** 文档与 schema 收敛 | ✅ 完成 | 1.7.9 | 五份主文档 + 索引同步；**新增** `check_doc_convergence.py` 把「六项事实一致」变成命令 | `python check_doc_convergence.py`（8 规则 PASS）；`--selftest`（10 例，含 2 个负控）；`python check_docs_123.py` |
| **P2-A** F5 展示与缓存 | ✅ 完成 | 1.7.7 / 1.7.8 | 9 条逐条修完（见 §5 P2-A 节首进度块） | `python check_contribution_layout.py --selftest`（旧语义 198 违规 / 新语义 0）；`python check_p2a_summary_and_lastbattle.py` |

**本轮额外完成（不在原计划内，但属于同一目标）**：

- **1.7.9**：`General/GivenTalent=false` 会把未乘进链条的 fold 登记进导出（开关下传 + 逐击 `calc.givenFoldOn`）；
  `giveApplied` 一直是「条目数 + 击数」（拆出 `rosterAudit.giveFoldHits`）；新增 `check_given_fold_coupling.py`
  与 `check_live_log.py`（日志 ⋈ 导出）。属于 P1-A / P0-A 的同类，见 SESSION-STATE §7.2.98。
- **1.7.10（由一个只读对抗复核查出，都是 1.7.9 自己的问题）**：
  1. **`give_section_reasons` 在真实管线里从未运行** —— 计数器写在 `rosterAudit` 内，而接线传的是**根字典**，
     于是查表永远 None、整条检查退化成一条 NOTE。**它通过了扁平字典单测却从未跑过真实文件** ——
     已改传 `rosterAudit`，并补 **真实文件负控**（`test_gate` CASE3 五例：篡改→ERROR、缺字段→DATA_MISSING/exit 3、
     正确值→无错误）。这条教训直接写进 P0-A 的验收要求：**「闸门必须能证明自己在跑」**。
  2. **`[COMP]` 重算不遵守开关**（`Diagnostics.cs` 不传 `applyToChain`、不看 `CfgMadness`，且从未建模受击方狂気 ×1.5）——
     已三条通道对齐 chain。仅日志文本，不改导出数据。
  3. 复核同时纠正：`giveApplied == 条目 + 击数` 是 **28/28 非零文件**（非 27/27）；`giveFoldHits` 在 `rosterAudit` 里（非根）。

**语料变化**：派发时 26 份，现在 **29 份**（新增 3 场 1.7.8 真实战斗，其中 2 场 411001 = `full`、1 场训练场 = `not_comparable`）。
§5 各任务里凡以「26 份」给出的分布/计数，都已标注为派发时的数，并在 29 份上复算。

## 0. 执行前校正（优先于后续任务中的简写）

1. 本文基于报告和部分 Python 源码审查，未重新执行全部实测或批量重放；报告中的发现须由接任务者先核验，不能一律当成已独立确认的缺陷。插件 UI 与导出共享 C# 核心；Python 是独立实现同一算法，不是直接调用同一个 C# 函数。
2. knownMult 包含属性倍率，fold 未必包含属性倍率；atkadd 又是贡献用虚拟因子。不能直接要求 knownMult 等于贡献全部 fold 的乘积。P0-D 必须先确认 attrMult、dealtMult、takenMult、原生 fold 和 atkadd 的边界，排除属性 ×2、舍入和特殊模式后，才能判定漏折。700817 暂列待核验，不得仅凭任务号永久判 partial，也不能把属性优势自动归给辅助角色。
3. totals.dealt 与 analyzableDealt 的差额不能直接命名为 excludedDamage。须逐事件统一敌我、自伤/友伤、召唤物及伤害口径；只将有明确事件证据的排除项记为 excludedDamage，其余单列 reconciliationGap。敌方正常伤害不计入我方漏分析伤害。三个覆盖率仅在分子分母同口径时有效；零分母用不可用并说明原因，不伪造 100%。
4. P0-C 主实验保持相同事件集和分母，仅隔离不可信构成；不能通过删除整击来制造排名差。另行展示可信子集结果时必须标注不同分母。PairCorroborated 未获佐证不等于配对错误，先核验字段定义和旧版本缺失语义。
5. 恒等式只证明账目守恒，不证明因果归因正确。未归因池为零不能代表攻击力公式、配对、状态快照均被实测证实。模型残差不能直接等同于未归因伤害金额。
6. schema 语义修正须升级适当版本并维护旧数据读取，不覆盖历史文件；method 版本与 schema 版本分开管理。P0-B 负责代码契约，P1-B 只在其冻结后同步文档。
7. 并行任务实行单文件单负责人：P0-C/P0-D 初期只新增独立诊断文件；P2-A 先修布局，涉及 ContributionSession 的缓存修改须等待 P0-B 或由其统一集成。统一由主负责人构建、升版本和部署，不允许各智能体独立覆盖 DLL。
8. 本轮每项修复先增加能够失败的用例，再实现；修改插件后执行构建、recon_probe、五守卫及贡献测试。未跑的测试必须明确列出。UI 验收可安排用户正常游戏时一次性完成，不为每个假设要求重复战斗。

## 1. 总体判断

项目已经完成“贡献模型能够工作”的验证，下一阶段重点是：口径严谨、覆盖率不误导、验证器真正阻断错误、任务可比较、UI 不误读、文档与代码同步。

当前目标不是继续添加规则，而是从“功能已经跑通”提升到“可判定、可阻断错误、可区分覆盖范围、可跨场比较”。

## 2. 已成立的部分

- 游戏内 F5、插件导出 contribution 段、离线报告使用同一 Contribution.Compute 核心。
- 1.6.1 以后，插件 contribution 段与 Python 核心可以逐字段交叉验证。
- actor key 已用于主要事件归并，常见同名和队外归因采用拒绝而不是静默猜测。
- atkadd 已进入统一贡献池，并按 log-share 口径分配。
- 411001 样本的逐击、角色和份额恒等式通过。
- 1.7.6 样本的主要规则来源能够解析。

## 3. 当前关键风险

1. totalDamage 在未归因非零时存在定义错误。→ **已修复（P0-B，1.7.8，§7.2.97）**：`totalDamage=analyzableDealt`。
2. creditedShare=100% 只表示已分析伤害内部归因完成，不表示整场覆盖率 100%。→ **已澄清（P0-B，1.7.8，§7.2.97）**：新增 `analysisDamageCoverage`/`creditCoverageWithinAnalyzed`/`overallAttributedCoverage`。
3. quest 700817 存在 knownMult 不等于 1 但 calc.fold 为空的命中。→ **已判定（P0-D）**：59 处全部由 attrMult 解释，unexplained=0。
4. Contribution 计算没有消费 PairTrusted，可能使用不可信 live-age 配对。→ **已量化（P0-C）**：A≡B，无需引入配对可信度条件。
5. 多个验证器在错误、缺失或跳过时仍可能 exit 0。→ **已修复（P0-A，§7.2.97）**：闸门状态机 PASS/WARNING/LEGACY/DATA_MISSING/ERROR。
6. atkadd 的 self 判定仍有按名字而不是 actor key 的风险。→ **已修复（P1-A，1.7.8，§7.2.97）**。
7. 插件、离线核心和文档的 schemaVersion、knownLimits 和进度状态不一致。→ **已收敛**：`knownLimits` 六条逐字节一致（1.7.5/1.7.7）、`schemaVersion` 1.1（1.7.8）、进度状态随本次文档统一。
8. F5 表格存在列宽、取整、上一场缓存和未归属显示问题。→ **已修复（P2-A，1.7.8，§7.2.97）**。

## 4. 推荐执行顺序

P0-A 修复验证闸门
-> P0-B 修正 contribution 数学字段和覆盖率
-> P0-C 离线量化 PairTrusted 影响
-> P0-D 建立任务适用性判定
-> P1-A 修正 atkadd actor key 语义
-> P1-B 收敛 schema、knownLimits 和项目文档
-> P2-A 修复 F5 展示和缓存问题

在 P0-A 至 P0-D 完成前，不建议继续增加新的复杂归因通道。

# 5. 任务分派

## Agent P0-A：修复贡献验证闸门

> **进度（2026-10-04，完成，1.7.8）**：8 条「必须修正」逐条落地 —— ①≥1.6.0 缺 `contribution` 段 = `DATA_MISSING` exit 3（1.5.x 明确 `LEGACY_NOT_APPLICABLE` 并打印原因，绝不当 PASS）；②状态词表冻结在 `contribution_gate.py`；③crosscheck 消费 `validate.check` 的 ERROR；④mismatch → exit 1；⑤golden 缺失 → exit 3 + 原因；⑥FACT 的 STARVED/cap 漂移/覆盖率异常 → 非零；⑦训练场独立适用性；⑧atkAdd 计数器恒等式。
> 验收复现：`python -m contrib.tests.test_gate`（59 passed，含「删段→3」「validate ERROR→1」「mismatch→1」「未知状态抛错」）、`python -m contrib.crosscheck --selftest`、`python check_fact_signature.py --selftest`。逐条证据见 `P0-A-VALIDATION-GATE-REPORT.md` 与 SESSION-STATE §7.2.97 §三。
> 集成时抓到并修掉一个**域外误报**（`folds` 等式一度按全档比较，训练场报 34 != 509）—— 该等式只在**域内**成立，已按域内比较并留 NOTE。
> **1.7.10 补课（P0-A 的核心教训）**：1.7.9 新增的 give 计数器闸门**在此前从未真正运行** —— 计数器在 `rosterAudit` 内而接线传了根字典，查表永远 None，整条检查退化成一条 NOTE。它通过了自己的扁平字典单测。现已改传 `rosterAudit`，并在 `test_gate` 增加 **CASE3 真实文件负控**五例（篡改 `giveApplied`→ERROR、1.7.9 缺 `giveFoldHits`→DATA_MISSING exit 3、值错→ERROR、正确值→无错误、干净文件→无错误），`test_gate` 59→**64**。**结论写进纪律：任何新增闸门都必须有一条「篡改真实文件 → 必须红灯」的用例，只测扁平数据的检查会在接线上无声失效。**

### 目标

让错误、缺失和不适用状态被自动区分，并在真正需要时返回非零退出码。

### 负责范围

优先检查：

- _dpsm_work/contrib/crosscheck.py
- _dpsm_work/contrib/validate.py 的命令入口或调用方
- _dpsm_work/contrib/tests/test_golden_155.py
- _dpsm_work/check_fact_signature.py
- 必要时新增 _dpsm_work/contribution_gate.py
- 对应 selftest 和错误输入样例

### 必须修正

1. 新版本导出缺少 contribution 段时不能报告通过。
2. 明确区分 PASS、LEGACY_NOT_APPLICABLE、DATA_MISSING、WARNING、ERROR。
3. crosscheck 必须消费 validate.check 返回的 ERROR。
4. mismatch 必须返回 exit 1。
5. golden 文件缺失不能静默 SKIP + exit 0。
6. FACT checker 的 STARVED、cap 溢出和关键覆盖率异常必须按策略返回非零。
7. 训练场特殊模式应有独立适用性结果。
8. 为 atkAdd 计数器增加至少一条可验证一致性检查。

### 禁止修改

不修改贡献公式、插件运行时采集、既有 KPI 定义，也不能把旧版本不支持伪装成当前版本通过。

### 交付物

- 验证器代码。
- selftest 和错误输入样例。
- 验证状态和 exit code 约定。
- 任务完成报告。

### 验收

篡改 contribution 数值、新版本缺少 contribution、validate 产生 ERROR、golden 缺失、未知 reason code、fold 被截断等情况必须至少有一项失败。明确标记的旧版本可以返回 LEGACY_NOT_APPLICABLE，但不能返回当前版本 PASS。

## Agent P0-B：修正 contribution 数学字段和覆盖率

> **进度（2026-10-04，完成，1.7.8）**：字段定义按本节采用（`totalDamage = analyzableDealt`、`attributed + unattributed == analyzableDealt`）；`excludedDamage`（域外/无法解析攻击者）与 `unattributedCredit`（进入分析但无法归属）**分离**，守卫显式拒绝把两者合并；新增 `producer`/`offlineExtensionVersion` 与 `damageLedger`（三桶不合并）。
> 本节给出的**验收样例**（totals.dealt=1000、analyzableDealt=600、unattributedCredit=100、excludedDamage=400、attributedDamage=500 ⇒ 60% / 83.333% / 50%）就是 `check_export_schema.py --selftest` 的 1.7.8 验收用例，逐值断言。
> 1.7.9 又补两条与游戏自身 KPI 的恒等式（`totals.taken == dealt + unattributedDamage` 29/29、`eventSumAll == totals.taken` 3/3）。

### 目标

修正未归因、排除伤害和整场覆盖率定义。

### 负责范围

- _dpsm_work/src/Output/Contribution.cs
- _dpsm_work/src/Output/ContributionSession.cs
- _dpsm_work/contrib/report_json.py
- _dpsm_work/check_export_schema.py
- 贡献数据字典、贡献报告和离线测试

### 统一字段定义

建议采用：

- contribution.totalDamage = analyzableDealt
- contribution.attributedDamage = analyzableDealt - unattributedCredit
- contribution.unattributedDamage = unattributedCredit

同时明确或增加：

- totals.dealt
- coverage.analyzableDealt
- coverage.excludedDamage
- coverage.analysisDamageCoverage
- coverage.creditCoverageWithinAnalyzed
- coverage.overallAttributedCoverage

公式：

- analysisDamageCoverage = analyzableDealt / totals.dealt
- creditCoverageWithinAnalyzed = attributedDamage / analyzableDealt
- overallAttributedCoverage = attributedDamage / totals.dealt

excludedDamage 是没有进入贡献分析的伤害；unattributedCredit 是进入分析但规则来源无法归属的贡献池，两者不能合并。

### 禁止修改

不改变 dealt 和 dealtWithAbsorbed 的含义，不改变既有对账 KPI，不把排除伤害放入未归因贡献，不用 knownMult 自动替代缺失 fold。

### 验收样例

构造：totals.dealt=1000、analyzableDealt=600、unattributedCredit=100、excludedDamage=400、attributedDamage=500。

必须得到：totalDamage=600、attributedDamage=500、analysisDamageCoverage=60%、creditCoverageWithinAnalyzed=83.333...%、overallAttributedCoverage=50%，并满足 attributedDamage + unattributedCredit = analyzableDealt。

## Agent P0-C：量化 PairTrusted 影响

> **进度（2026-10-04，完成，离线）**：A/B/C 三口径全部量化并输出（analyzableDealt / poolTotal / 每角色 totalCredit 与 share / 每规则当量 / 排除击数与伤害 / 差异排名）。**结论：A ≡ B 逐字段全等**（域内不可信配对 = 0）；**C 必须否决** —— `PairCorroborated` 不是「配对可信」的同义词，用它过滤只保留 A 池的 **0.013%**（ルナリス 51,955,401 → 368,775，排名 1→7），会制造伪排名差。诊断字段 `pairTrustedHits/pairUntrustedHits/pairCorroboratedHits/pairUntrustedDamage/pairUntrustedCredit` 全部实现。
> **2026-10-04 补测（三场 1.7.8）**：两场普通战斗域内不可信仍是 **0 击、B 移动 0.0000**；训练场 `9999_135214` 首次出现域内 12 击（7 击带 fold），B 在该场移动 62.7391 / 842,871.87 = **0.007443%**，且该场本就是 `not_comparable`。因此断言按域收紧（T3 → 非训练场，新增 T3c/T4c），**不是删除或放宽**。
> 复现：`python pairtrusted_impact.py --selftest`（11/11）。

### 目标

不修改插件行为，确认不可信配对是否显著改变贡献结论。

### 必须比较

A：全部 calc.fold。
B：只使用 PairTrusted=true。
C：只使用 PairTrusted=true 且 PairCorroborated=true。

### 必须输出

每种口径的 analyzableDealt、poolTotal、attributedDamage；每个角色的 totalCredit 和 totalShare；每条规则的 damageEquivalent；被排除的命中数和伤害量；三种口径的差异排名。

建议新增诊断：pairTrustedHits、pairUntrustedHits、pairCorroboratedHits、pairUntrustedDamage、pairUntrustedCredit。

### 禁止修改

不先修改 Contribution.Compute，不因单场差异直接改变默认口径，不把不可信配对自动认定为错误。

### 验收

必须给出明确结论：差异可忽略、差异集中于特定规则/角色、或差异显著需要引入配对可信度条件。结论标记为离线重放。

## Agent P0-D：建立任务适用性判定

> **进度（2026-10-04，完成，离线）**：`contribution_applicability.py` 实现全部六项检测（knownMult≠1 且 fold 为空、其伤害与占比、与 fold 乘积的差异、训练场 ×0.03/sub-unity、analyzableDealt vs totals.dealt、contribution 段是否存在及能否过 crosscheck），输出 `modelApplicability` 三分类 + `knownMultWithoutFoldHits/Damage` + `specialMode` + `comparisonAllowed` + `reasons`。
> **§0.2 的提醒已执行**：700817 的 59 处不是漏折，**全部由 `attrMult`（属性相性，按设计不进 fold）解释**，`unexplained=0`，所以它的 `partial` 是「属性倍率不在 fold 语义内」，不是「代码有 bug」的判词（判据已改为 `prod(fold) ≈ dealtMult×takenMult` 且排除 `attrMult`）。
> **分布**：派发时 26 份 = full 10 / partial 9 / not_comparable 7；29 份复算 = **full 12 / partial 9 / not_comparable 8**（新三场被吸收，无新类别、无档位翻转）。训练场 `foldIdentity` 的严格违规全部由三位小数序列化解释（`violationsUnexplainedByRounding = 0`）。

### 目标

避免将 411001、700817 和训练场 9999 的贡献结果无条件横向比较。

### 必须检测

- knownMult != 1 且 calc.fold 为空的命中。
- 这些命中的伤害总量和占比。
- knownMult 与 fold 乘积的差异。
- 训练场 ×0.03 或 sub-unity 特殊规则。
- analyzableDealt 与 totals.dealt 的差异。
- contribution 段是否存在以及是否能通过 crosscheck。

### 建议输出

modelApplicability：full、partial 或 not_comparable；knownMultWithoutFoldHits；knownMultWithoutFoldDamage；specialMode；comparisonAllowed；reasons。

### 当前建议分类

- 411001：当前可作为主要贡献比较基准。
- 700817：knownMult/fold 不一致，标记 partial。
- 9999：训练场特殊规则，不能与普通任务直接比较。
- 1.5.x 无 contribution 段：可离线复算，但不能验证插件内存值一致性。
- 1.6.0：factor 精度问题，不作为当前基准。

### 禁止修改

不为了让任务变为 full 而忽略异常，不把训练场机制硬塞进普通任务模型，不修改历史导出。

## Agent P1-A：修正 atkadd 的 actor key 语义

> **进度（2026-10-04，完成，1.7.8）**：`AtkAddFold` 判定改为 **key 优先**（`giverKey == selfKey` → self；不等 → teammate；key 缺失才允许 name fallback 并计数），四个计数器 `selfByKey/selfByNameFallback/nameCollision/ownerUnknown` 全部导出，恒等式 `selfValues == selfByKey + selfByNameFallback` 进守卫。同名队友不再被当 self 丢掉，同名冲突计 `nameCollision` 而**不静默丢进 baseCredit**。
> 验收复现：`recon_probe` 的 P1-A 段 20 条断言（同名队友、无 key 的自/队友、同名冲突、baseCredit 不被吞），**ALL CHECKS PASSED**；真实三场 1.7.8 实测 `selfByNameFallback=0`、`nameCollision=0`（key 判定 100% 覆盖）。
> 1.7.9 追加同类修正：`General/GivenTalent=false` 时被调函数仍登记 fold（登记与相乘必须同一个开关），并导出逐击 `calc.givenFoldOn`；守卫 `check_given_fold_coupling.py`（17 项 + 6 变异自测）。

### 目标

消除攻击力加算 self 判定按名字造成的同名误归属风险。

### 负责范围

- _dpsm_work/src/Model/AtkAddFold.cs
- _dpsm_work/src/Composition/CompositionProbe.Chain.cs
- 必要的最小调用方和 recon_probe 断言

### 建议实现

同时保留 selfKey、selfName、giverKey、giverName。

判定优先级：giverKey == attackerKey 为 self；giverKey != attackerKey 为 teammate；giverKey 缺失时才允许 name fallback，并记录计数。

建议计数：atkAddSelfByKey、atkAddSelfByNameFallback、atkAddNameCollision、atkAddOwnerUnknown。

### 禁止修改

不改变 atkadd 的 log-share 公式，不将 log-share 叫作边际贡献，不修改 KPI，不把 owner 缺失条目静默归给攻击者。

### 验收

增加同名攻击者和同名队友样例，证明 key 可正确区分 self 与 teammate，key 缺失进入明确 fallback，同名冲突不会静默丢入 baseCredit。

## Agent P1-B：统一 schema、knownLimits 和项目文档

> **进度（2026-10-04，完成，1.7.9）**：本节列出的「必须统一」九项全部一致 —— 当前部署版本/SHA256、源码版本、`contribution.schemaVersion`、离线 schema、导出总数、任务分布、阶段完成状态、attackPower 状态、knownLimits 唯一措辞；并已明确区分 `schemaVersion`（1.1）/ `producer`（plugin|offline）/ `method`（log-share/1）/ `offlineExtensionVersion`（离线独有）。
> **验收已从「读一遍」升级为命令**：新增 `check_doc_convergence.py`，直接以仓库为真值源（版本取 `BuildInfo.cs`、SHA 取**已部署 DLL 的实算哈希**、schema 取 `Contribution.cs`、导出数取目录、适用性取 P0-D 报告、knownLimits 取插件 vs 离线逐字节比较），对五份主文档做 8 条规则检查，并自带 10 例自测（含「1.7.6 最新一场」与「全部 13 份」两个**负控**，防止规则把「最新的一场战斗」或子集计数误判成版本/总数声明）。
> 复现：`python check_doc_convergence.py`（8/8 PASS）、`--selftest`（10/10）、`python check_docs_123.py`（exit 0，无 mojibake）。
> 过程中修掉三处**规则命中导致的文档歧义**（字典:382 与 HANDOFF:388 的「部署中的 1.5.5」补「当时」；报告 §5 的 26 份标题标为报告生成时）。
> **它随后就抓到了真东西**：1.7.9→1.7.10 的版本/哈希变化让 R1/R3 立刻变红（说明规则不是装饰），而 1.7.10 的对抗复核又暴露出我写进文档的 **27/27**（真值 28/28）与「根 `giveFoldHits`」（真值在 `rosterAudit`）——两处都已按仓库真值更正（含两处 C# 注释）。

### 目标

消除插件、离线核心、数据字典、索引和交接文档的状态分叉。

### 负责范围

- _dpsm_work/CONTRIBUTION-DATA-DICTIONARY.md
- _dpsm_work/CONTRIBUTION-TABLE-REPORT.md
- _dpsm_work/PROJECT-DIRECTION-AND-IMPLEMENTATION-PLAN.md
- _dpsm_work/HANDOFF.md
- DpsMeter-文档索引.md
- 必要时同步 knownLimits 文案

### 必须统一

当前部署版本和 SHA256、源码版本、contribution schema 版本、离线报告 schema 版本、导出总数、任务分布、阶段完成状态、attackPower 状态、会心限制和 knownLimits 唯一措辞。

建议明确区分：

- contribution.schemaVersion：统一正式贡献 schema。
- contribution.producer：plugin 或 offline。
- contribution.method：log-share/1。
- offlineExtensionVersion：离线报告独有扩展字段。

### 禁止修改

不修改历史证据数字，不删除旧版文档和归档，不把尚未实测的结论改成实测。

### 验收

从五份主文档随机抽查当前部署版本、schema 版本、导出总数、411001/700817/9999 适用性、attackPower 状态和 knownLimits，内容必须一致。

## Agent P2-A：修复 F5 贡献表展示问题

> **进度（2026-10-04，完成，1.7.7 / 1.7.8）**：9 条「必须修复」逐条落地 ——
> ①`Fit` 按**显示列宽**截断（1.7.7）；②截断标记用 ASCII `..`，省略后不超列宽；③表 2 悬空的「主要规则」表头**已删**（`Rows.cs:611` 注释）；④合计行与表头/数据行同为 **85 列**；⑤金额统一用 `:N0`（不再 `(long)` 截断），并且因为四舍五入本身不可加，**页脚显式写出残差**而不是暗示没有；⑥上一场摘要透传 `UnattributedDamage/Hits`；⑦`ResolveContributionView` 取代「调用 `Invalidate`」的错解（清空缓存会把「错场」变成「无数据」）；⑧`ResolveHeaderBattle` 消除「任务 0 / 0 秒」（面板关闭时也走它）；⑨字体失败不再清标志、3 秒窗口有效、告警只打一次。
> 验收复现：`python check_contribution_layout.py --selftest`（1.7.6 语义 198 处违规 / 1.7.7 语义 0；主跑 30 份 **573 行 0 违规**）、`python check_p2a_summary_and_lastbattle.py`（含模型重放：旧流程指向更早一场、新流程指向最近一场）。
> **1.7.11 追加（用户提问驱动，不在本节清单内）**：用户指出「自身装备造成的输出是否算直接贡献、把一个角色的输出切成多部分会误解」。核查后——数值上它一直算在该角色名下（`总贡献 = 自身 + 他人因你`），会误导的是版面：`baseCredit` 按字典 §2.3 本来就含会心/自身加算/未识别残差，所以「基础 vs 自身规则」的分界只是「能不能开票」，两者与 `assistCredit` 平列会被读成外部加成；`receivedAssist` 更从未进过任何界面。因此 F5 表 1 改为 `自身 | 他人因你 | 被队友分走`（仍 85 列），F6 明细块写成算式，口径改四行；并新增两条逐角色恒等式与「渲染器↔副本」漂移检查。详见 `SESSION-STATE.md` §7.2.100 与本报告 §9.9.5/§9.9.6。
> **实机部分**：1.7.8 三场的心跳实测 F5 渲染正常（战斗中跟随 live 场、结束后 hist 1→2、标题为真实任务号），机器侧 unattributed 与导出逐项一致（`check_live_log.py`）；1.7.9 起 caption 那行也进日志（`unattrRow`），下一场打开一次 F5/F6 即自动核对。

### 目标

修复不改变算法但会影响用户信任的 UI 问题。

### 负责范围

- _dpsm_work/src/Ui/OverlayUGUI.Rows.cs
- _dpsm_work/src/Ui/OverlayUGUI.Pool.cs
- _dpsm_work/src/Ui/OverlayCore.cs
- 上一场摘要相关的最小范围代码

### 必须修复

1. Fit 按显示列宽截断，而不是按 UTF-16 字符数。
2. 名称加省略号后仍不得超过列宽。
3. 删除表 2 悬空的“主要规则”表头，或真正补上数据。
4. 合计行与数据行使用一致列宽。
5. 统一金额取整，尽量保证基础 + 自身规则 + 辅助 = 总贡献。
6. 上一场摘要传递未归属伤害和命中。
7. 修正 ContributionSession.Invalidate 调用时机。
8. 数据不可用时显示真实原因，不显示任务 0 和 0 秒假信息。
9. 字体失败时保持合理重试节流并记录日志。

### 禁止修改

不修改 Contribution.Compute，不改变角色排序和贡献公式，不把不可用数据显示成 0。

### 验收

使用真实长名称离线验证行宽；实机确认 F5 表头、数据、合计行对齐，长名称不推动后续列，F5 与上一场 roster 指向同一战斗，未归属字段与导出一致。

# 6. 主负责人验收顺序

1. 数据语义：totalDamage、attributedDamage、unattributedCredit、excludedDamage 和覆盖率是否守恒且分离。
2. 模型适用性：knownMult/fold、训练场特殊模式、pair quality 是否被报告。
3. 验证闸门：错误、缺失、旧版本和 validate ERROR 是否被正确区分。
4. 归因正确性：actor key、atkadd self、同名、队外和歧义是否正确。
5. 展示文档：UI 和五份主文档是否与导出一致。

# 7. 第一批可并行任务

立即并行：

- Agent P0-A：验证闸门。 → **已交付（1.7.8）**
- Agent P0-C：PairTrusted 离线对照。 → **已交付（离线，1.7.8 补测）**
- Agent P0-D：任务适用性扫描。 → **已交付（离线，29 份复算）**
- Agent P2-A：F5 UI 离线问题清单和修复。 → **已交付（1.7.7 / 1.7.8）**

等待 P0-A 或 P0-B 结论后：

- Agent P1-A：atkadd actor key 修正。 → **已交付（1.7.8）**
- Agent P1-B：文档和 schema 收敛。 → **已交付（1.7.9）**

建议总顺序：P0-A -> P0-B -> 汇总 P0-C/P0-D -> P1-A/P1-B -> P2-A 实机验收。

**批次执行实况**：建议的顺序被遵守；P2-A 的缓存部分（涉及 `ContributionSession`）确实等到了 P0-B 之后才集成；所有构建/升版本/部署都由主负责人统一执行，各子智能体只写各自文件（单文件单负责人）。**P2-A 的「实机验收」已由用户的三场正常战斗覆盖**（见 §5 P2-A 进度块与 SESSION-STATE §7.2.98）。

# 8. 第一轮禁止事项

在 P0 完成前，禁止：

1. 继续增加新的复杂规则通道。
2. 将辅助贡献称为真实边际贡献。
3. 用 knownMult 自动替代缺失 fold。
4. 将 excludedDamage 与 unattributedCredit 合并。
5. 把 creditedShare=100% 当成整场覆盖率 100%。
6. 为单个未验证假设要求用户重复战斗。
7. 修改既有对账 KPI 定义。
8. 修改或删除历史导出、备份和证据归档。
9. 在验证器仍可能 exit 0 时继续扩大数据模型。

# 9. 第一轮完成定义

第一轮 P0 完成必须满足（2026-10-04 逐条复核）：

| # | 条件 | 状态 | 证据 |
|---|---|---|---|
| 1 | contribution 数学字段在未归因非零时仍守恒 | ✅ | 守恒式 `attributed + unattributed == analyzableDealt` 由守卫断言（负控「attributed+unattributed != analyzableDealt」会失败）；真实三场 1.7.8 全部成立 |
| 2 | excludedDamage 与 unattributedCredit 分离 | ✅ | `check_export_schema.py` 有「REJECTS excludedDamage that was merged with out-of-scope damage」；且 `excludedDamage == unknownAttackerDealt` 逐份校验 |
| 3 | 报告同时显示整场覆盖率和已分析归因率 | ✅ | `coverage` 的 `analysisDamageCoverage` / `creditCoverageWithinAnalyzed` / `overallAttributedCoverage` 三个率都写入，`contrib/report_text.py` 三率都打印 |
| 4 | knownMult/fold 不一致会被检测并影响适用性 | ✅ | P0-D 的 `foldIdentity` 判据（排除 `attrMult`）进 `contribution_applicability.py`；700817 的 59 处由 attrMult 解释、`unexplained=0` |
| 5 | PairTrusted 影响已被离线量化 | ✅ | `PAIRTRUSTED-IMPACT-REPORT.md`；A≡B 逐字段全等，C 否决；1.7.8 三场补测（普通 0 击 / 训练场 0.007443%） |
| 6 | 错误输入和关键缺失会让验证闸门失败 | ✅ | `test_gate` 59 passed：删段→3、validate ERROR→1、mismatch→1、golden 缺失→3、FACT STARVED/cap→1、未知状态抛错 |
| 7 | 411001、700817、9999 的比较资格明确 | ✅ | 29 份 = full 12 / partial 9 / not_comparable 8；9999 判 `not_comparable` 且 `comparisonAllowed=false`，crosscheck 对它返回带原因的 LEGACY |
| 8 | 没有修改既有 KPI 定义 | ✅ | 未改 `dealt` / `dealtWithAbsorbed` / `taken` 的含义与 reconcile KPI；1.7.9 只新增字段，并把「旧值不对应任何量」的 `giveApplied` 修正为条目计数（原因记在字典 §12.5） |
| 9 | （补充）历史导出与备份未被修改或删除 | ✅ | 29 份导出全部保留；备份只增不减（实测 102 个 .bak）；闸门对新旧文件的差异只用 NOTE / LEGACY 表达 |

**结论：第一轮完成定义 8+1 条全部满足。**

# 10. 智能体提交格式

每个智能体完成后必须提交：

- 任务编号。
- 修改文件。
- 未修改的边界文件。
- 输入导出。
- 证据等级。
- 运行命令。
- 测试结果。
- 新增失败用例。
- 已知限制。
- 后续依赖。

所有报告数字都必须能回答：哪一份导出、哪个字段、哪条公式、哪条验证命令、证据等级是什么。
