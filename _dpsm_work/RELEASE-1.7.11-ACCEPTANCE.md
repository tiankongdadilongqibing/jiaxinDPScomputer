# 1.7.11 验收记录(N0:最新版本验收闭环)

> 本文件是 `CONTRIBUTION-NEXT-PHASE-ROADMAP.md` **N0** 的交付物。该文档写作时的基线是 **1.7.10**;
> 同一天发布了显示级 **1.7.11**(F5 输出分组)。因此本记录覆盖**当前部署版本 1.7.11**,并把 1.7.10 的新样本
> `battle_411001_20261004_144548.json` 一并纳入。基线发生位移这件事写在明处,不静默改范围。
>
> 证据等级标注:【实测】= 本次运行的真实输出;【静态】= 读源码/文件得到;【推断】= 未直接观测。

## 0. 结论

> **第 6 轮补充(语料 30 → 32)见 §10**:两场新导出、运行前钉住的判定全部命中、N0 从 14 条扩到 **50 条检查**
> (新增「逐文件钉住」与「闸门退出码」两道闸门,各自的 11 例负控都已跑过)、30 次运行 **486.3 s**、唯一非零退出是白名单里的
> `crosscheck(batch)`。第 6 轮同时把编码守卫扩到 29 份文档并给它加了自测(见 §10.6)。下表第 1-4 行是**第 2 轮**的 30 份记录,保留为历史快照。

| 维度 | 状态 | 证据 |
|---|---|---|
| **构建通过** | ✅ | `dotnet build -c Release`:0 警 0 错;`BuildInfo=csproj=1.7.11`;部署 DLL 387,072 B / `36EC96D4…`;`SOURCE==DEPLOYED: True`【实测】 |
| **离线通过** | ✅ | **26 次**闸门/测试运行(第 2 轮把 N1 改造后的三个验证器也纳入同一条流水线):除 `crosscheck --batch`(已知 1.6.0 非零)外全部 exit 0;`recon_probe` ALL CHECKS PASSED【实测】 |
| **新版本实战导出通过** | ✅ | `battle_411001_20261004_144548.json`(1.7.10,quest 411001)= crosscheck `PASS` + `modelApplicability=full`【实测】 |
| **UI 目视通过** | ⏳ 待用户 | 离线只能证明「每个渲染行宽恒等于 85 列」;字体/缩放/遮挡/交互只有用户能看(roadmap N7)【推断】 |

四个维度**不合并**为一个「全过」:前三项是机器可复现的,第四项不是。

## 1. 语料快照(不是「30 份全部语义通过」)

- 目录:**30 份**导出(411001×22 / 训练场 9999×7 / 700817×1),逐份 SHA256/大小/版本/任务/段/schema 见
  `acceptance_1.7.11/corpus_manifest.json`。【实测】
- 其中 **17 份**含 `contribution` 段(4 份 schema 1.1 = 1.7.8×3 + 1.7.10×1;13 份 schema 1.0)。【实测】
- **13 份没有** `contribution` 段(版本 1.5.3–1.5.5 / 1.6.0 / 1.6.1 / 1.7.0 / 1.7.2):对它们**不存在**贡献模型判定,
  只有单场适用性之外的结论 —— 这与「30 份都验过」是两句不同的话。

## 2. 可复现命令

```powershell
# 工作目录 C:\...\_dpsm_work(CONTRIBUTION-NEXT-PHASE-ROADMAP.md 的兄弟目录)
$env:PYTHONIOENCODING='utf-8'
& 'C:\Users\24134\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe' n0_acceptance.py
```

该脚本**不使用任何「默认最新文件」**:它显式枚举导出目录、逐份算 SHA256,把每次子命令的 argv、工作目录、
退出码、耗时和原始 stdout 写进 `acceptance_1.7.11/runs/`,`runs.json` 汇总,`corpus_manifest.json` 带输入哈希与
工具源码哈希(15 个脚本 + 部署 DLL + 配置 + BuildInfo 版本)。

第 2 轮起,这条流水线同时跑 N1 改造过的三个验证器与其自测(`refactor_final_check --selftest`、`v150_validate`
+ `--selftest`、`comparison_eligibility --selftest` / `--selftest-e2e`)—— 即「新门槛必须在同一条流水线里被真正执行过」,
而不是只在各自的 README 里成立。【实测】

## 3. 期望 vs 实测(可证伪)

期望值不是「凭感觉」:它由**上一轮 29 份快照**(`SESSION-STATE.md` §7.2.99 / `CONTRIBUTION-TABLE-REPORT.md` §9.9.4)
加上**新样本被钉住的判定**推得。对不上就是发现,不是解释掉。

| 工具 | 桶 | 期望 | 实测 | |
|---|---|---|---|---|
| `crosscheck --batch` | ERROR | 1 | **1** | ✅ |
| `crosscheck --batch` | LEGACY_NOT_APPLICABLE | 15 | **15** | ✅ |
| `crosscheck --batch` | PASS | 11 | **11** | ✅ |
| `crosscheck --batch` | WARNING | 3 | **3** | ✅ |
| 适用性(模型) | full | 13 | **13** | ✅ |
| 适用性(模型) | partial | 9 | **9** | ✅ |
| 适用性(模型) | not_comparable | 8 | **8** | ✅ |

新样本的独立判定:144548 = `PASS` + `full`(1.7.10 首场实战)。上一轮的 29 份里 `full` 是 12、`PASS` 是 10,
加这一份即为 13 / 11,与实测一致 ⇒ **新版本没有引入分类漂移**。【实测】

`corpus_manifest.json` 把上述**全部 14 条**写成可证伪的检查项(12 个桶 + 1 条标签恒等式 + 1 个已知坏样本),
最终 **14/14 通过**。第一版曾把 1.6.0 样本记成 `…235204`(那是 1.5.5 金样本),该检查第一次就抓出来并已更正 ——
记录这件事本身也是证据:期望不是事后附会。【实测】

## 4. 本轮发现的异常:工具间的**标签分歧**(已登记给 N1)

逐份状态在两张表里对不上,差额正好是「没有 contribution 段」的那些文件:

| 同一批 30 份 | ERROR | LEGACY | PASS | WARNING | NOT_RUN |
|---|---|---|---|---|---|
| `crosscheck --batch` | 1 | **15** | 11 | 3 | — |
| 适用性报告内嵌的 crosscheck 状态 | 1 | **2** | 11 | 3 | **13** |

恒等式 `batch.LEGACY(15) == embedded.NOT_RUN(13) + embedded.LEGACY(2)` 成立,所以这不是数据问题,而是
**同一事实的两种措辞**。但它有实际后果:`LEGACY_NOT_APPLICABLE` 听起来像「旧版但适用」,而事实是
**这份文件根本没有可判定的贡献段**;`CONTRIBUTION-TABLE-REPORT.md` §9.5 早已把「crosscheck 对 ABSENT 视为通过」
列为盲区。谁若把 15 读成「15 份旧版文件都验过了」,就会高估覆盖。

- 13 份 `NOT_RUN` 文件名已逐条写进 `corpus_manifest.json` 的 `labelingDivergence.files`。【实测】
- **处理**:不修改历史文件、不修改任一工具既有词表;按 roadmap **N1** 的要求,在**新的准入层**里把
  「段缺失」判为 `REJECTED`(不得进入正式贡献排名),并保留原工具的原始 ERROR/LEGACY 记录。【静态 + 计划】

## 5. 已知负例:保留、不放宽

`battle_411001_20261004_015919.json`(1.6.0,58 处 `factor` 四位截断)仍是唯一的 `ERROR`,期望值就是 1;
`corpus_manifest.json` 的 `known_bad` 检查断言「ERROR 文件集合 == 该文件」。
**不删除、不调整容差使其变绿**(roadmap §2.2/§6.2)。【实测】

## 6. 未检查项(逐条给原因,不冒称通过)

1. **日志关联**:`LogOutput.log` 只能证明与 `144548` 同源(它命名了该导出路径),其余场次的日志已被 BepInEx 覆盖。
   因此**不宣称整体日志验收**;`check_live_log.py` 本次 exit 0(82 帧与导出一致、16 帧因缺命中数标注**不可核验**)。
2. **配置开关可得性**:导出**不记录** `GivenTalent`/`Madness`/`Contribution` 等开关,所以两份样本是否同配置
   在文件层面**不可知**。本轮只在 `comparison_eligibility.py` 里把它标成 `config-provenance: unknown`,不假设相等。
3. **会心 / 治疗 / 敌方(team 2)视角 / 固定窗口 DPS**:未纳入本记录(N4/N6 及后续)。
4. **耗时与内存**:未测(roadmap N7)。
5. **UI 目视**:见 §0 第四行。

## 7. 输入与凭证

- 部署 DLL:`BepInEx\plugins\DpsMeter\DpsMeter.dll` = 387,072 B /
  `36EC96D4DBD8E221ED554476C299BD8DB4C9A1220A2A923DB16BC7BB4888BC42`;
  回退锚点 `DpsMeter.dll.1.7.10.bak` = `BF2F174A4059125376946ABCB0E3E4A9176E6B360CAB209078F219A95389AE85`。【实测】
- 源码版本:`_dpsm_work\src\BuildInfo.cs` = 1.7.11;`DpsMeter.csproj` `<Version>` = 1.7.11(一致)。【实测】
- 配置:`BepInEx\config\dev.dpsmeter.cfg` = `247AD5848F1172EAE0D473C6A2F9A56E164814F3013A22E0BD29EFC95DF0DEFD`,
  贡献相关开关:`Contribution/ShowContribution/GivenTalent/GivenGiverHook/Madness/MadnessApplier/MadnessVictim = true`。【实测】
- 工具哈希:15 个脚本的 SHA256 在 `corpus_manifest.json` 的 `meta.tool_sha256`(含本记录的生成脚本 `n0_acceptance.py`)。
- 结果目录:`_dpsm_work\acceptance_1.7.11\`(`corpus_manifest.json` / `runs.json` / `RESULTS.md` / `runs\*.txt`
  / `applicability.json` / `export_schema_report.txt` / `pairtrusted_impact_report.*` / `recon_probe_out.json`)。

## 8. 边界(本记录没有做的事)

- 没有修改任何历史导出、DLL、配置或历史证据。
- 没有把「目录里 30 份」说成「30 份都验过」。
- 没有把 1.6.0 的 ERROR 过滤掉,也没有放宽容差。
- 没有把 `LEGACY`/`NOT_RUN` 当作当前能力通过。
## 9. 与后续任务的接口(本轮已交付,供下一轮直接使用)

- **N1 准入契约** `comparison_eligibility.py`(`ComparisonEligibility/1`):把本记录 §6 第 2 条的「配置不可知」
  与「身份只有名字」变成**机器可读的 `RESTRICTED` 理由**;**并把贡献闸门的 `ERROR`/`DATA_MISSING` 映射为 `REJECTED`**,
  于是 1.6.0 的已知损坏样本在契约里也进不了正式排名(可准入 17→**16**/30)。15 例自测 + 6 例 CLI 级负控。
  30 份语料实测见 `acceptance_1.7.11/eligibility.json`(exit 4 = 没有任何指标可以整体排名)。
- **N1 已完成的两处验证器硬化**(详见 `CONTRIBUTION-NEXT-PHASE-ROADMAP.md` 的 N1 进度块):
  `refactor_final_check.py` 现在对无效 UTF-8 / U+FFFD / 缺必需源文件 / partial 声明缺失 / 代码里的死符号**真阻断**
  (10 例负控);`v150_validate.py` 的 KPI 基线改为**显式哈希固定的批准清单**(`kpi_baseline.json`,29 批准 / 1 排除,
  对最新一场可用 20 份,8 例负控)——该分支在本轮之前**不可达**。
- **N3 身份映射** `identity_map.py`:给比较器提供 `entityKey` / `same_entity()`;本轮 30 份实测 team1 有 **32 个弱身份 actor**,确认前不应参与排名。
- **N4 atkadd 敏感性** `atkadd_sensitivity.py` + `ATKADD-MODEL-AUDIT.md`:结论是**公式证据缺失、逐 giver 身份不可对账**,而
  守恒成立;高份额辅助(ルナリス assistCredit 49,308,446.57,**100% 来自 atkadd**)在隔离实验下排名由 1 变 8。
  因此本记录里 `assistCredit` 的任何读法都必须带上「这是分账约定」的前提。
## 10. 第 6 轮:语料 30 → 32 复验(2026-10-04 17:0x)

> 用户**正常游玩两场**后追加。**判定写在运行之前**(§10.1),不在看到结果后改期望;`decision_prereg.json` 一字未动。

### 10.1 两条新样本与运行前钉住的判定

样本事实用一个只读小工具复现:`python sample_intake.py`(版本/任务/时长/结果/是否训练场/schema/atkAdd/阵容 templateId/可分析伤害/命中数)。

| 文件 | 事实 | 运行前预测 | 依据 |
|---|---|---|---|
| `battle_411001_20261004_170157.json` | 1.7.11 / quest 411001 / schema 1.1 / 有贡献段 / 119.0 s / Lose | `crosscheck=PASS` + `applicability=full` | 与 `144548` 同版本同任务同 schema 同一部署 DLL |
| `battle_9999_20261004_165952.json` | 1.7.11 / quest 9999(训练场)/ schema 1.1 | `LEGACY_NOT_APPLICABLE` + `not_comparable` | 此前**所有带段的 9999**(042458=1.7.5/1.0、135214=1.7.8/1.1)都是这一对 |

### 10.2 期望 vs 实测(32 份)

| 工具 | 桶 | 期望 | 实测 | |
|---|---|---|---|---|
| `crosscheck --batch` | ERROR | 1 | **1** | ✅ |
| `crosscheck --batch` | LEGACY_NOT_APPLICABLE | 16 | **16** | ✅ |
| `crosscheck --batch` | PASS | 12 | **12** | ✅ |
| `crosscheck --batch` | WARNING | 3 | **3** | ✅ |
| 适用性(模型) | full | 14 | **14** | ✅ |
| 适用性(模型) | partial | 9 | **9** | ✅ |
| 适用性(模型) | not_comparable | 9 | **9** | ✅ |
| 适用性内嵌 crosscheck | NOT_RUN / LEGACY / PASS / WARNING / ERROR | 13 / 3 / 12 / 3 / 1 | **13 / 3 / 12 / 3 / 1** | ✅ |

逐文件钉住的 6 项(两个新样本的 crosscheck + applicability,以及历史 `144548` 的两项)**全部命中**;
恒等式 `batch.LEGACY(16) == embedded.NOT_RUN(13) + embedded.LEGACY(3)` 成立。
**最终 51/51 检查通过**(14 个桶 + 30 条退出码 + 1 条白名单有效性 + 6 条逐文件钉住),30 次运行合计 **486.3 s**,
唯一非零退出是白名单里的 `crosscheck(batch)`(见下)。

### 10.3 本轮新增的两道闸门,第一件事就是抓到真问题

1. **逐文件钉住必须被执行**。`NEW_FILES_EXPECTED` 以前**只写进 manifest、从不比对** —— 意味着两份文件互相交换
   `PASS`/`WARNING` 而聚合桶仍加得起来时,记录照样全绿。现在每个钉住文件逐项核对(见 10.2)。
2. **闸门退出码必须为 0**。以前 `RESULTS.md` 只**打印**退出码,不红。加上这条后立刻抓到:
   `doc_convergence` 与 `selftest/doc_convergence` **exit 1** —— 规则 R5「语料总数与任务分解」失败:
   `HANDOFF.md:39` 仍写「当前 30 份」、`DpsMeter-文档索引.md:25` 仍写「累计 30 份」、`CONTRIBUTION-TABLE-REPORT.md` 里缺 32 的总数。
   **已按当前语料更正**:32 份 = 411001×23 / 训练场 9999×8 / 700817×1;含 `contribution` 段 **19 份**
   (schema 1.0×13 + 1.1×6);适用性 **full 14 / partial 9 / not_comparable 9**;布局守卫 32 份 / **634 行 0 违规**。
   重跑后 `doc_convergence` **8/8 PASS**、自测 **10 例 PASS**。

两道新闸门都有自己的负控:`n0_acceptance.py --selftest` **11 例**(非白名单闸门 exit 3 必须红灯;白名单运行 exit 0 或 2 也必须红灯;
白名单名字若不再出现必须红灯)。**没有任何一条闸门是“从没见过红”的**。

### 10.4 与 N5 配队决策的联动(只加样本,不改标准)

新的一场 411001 落进阵容 A 分层(A 15 → **16**,B 仍 **7**) —— 分层由 templateId 阵容决定,不是事后挑选:

| 判据 | 第 5 轮 | 第 6 轮 |
|---|---|---|
| 点估计(B − A) | −8,800,730.5(−4.37%) | **−9,440,873.0(−4.67%)** |
| 95% 百分位簇自助区间 | [−13,773,796.6, −4,228,644.9] | **[−14,669,118.3, −4,739,845.9]** |
| 是否覆盖 0 | 否 | **否** |
| 是否达到预注册 10% 阈值 | 否 | **否** |
| 结论等级 | 有倾向但不确定 | **有倾向但不确定(不变)** |
| 是否换人 | 不换 | **不换** |

预注册的阈值(10%)、alpha(0.05)、自助(战斗为簇、5000 次、种子 20261004)、每组下限(3)全部未改;
样本是用户自己接着玩出来的,**没有为了凑结论挑场次**。与 `compare/2` 的逐角色交叉校验 20 项、0 不一致。

### 10.5 第 6 轮的边界

- 插件 DLL、配置、schema、方法**一个字节都没动**(部署仍是 1.7.11 / `36EC96D4…`;本轮只改离线工具与文档)。
- 历史导出、备份、旧报告正文没有被改写;`CONTRIBUTION-TABLE-REPORT.md` 只改了**当前状态句**(语料总数与布局行数),
  其历史段落(26/29 份时的说明)保持原样。
- 目视 9 项里,**只有“未归属行”由日志自动核对通过**;其余按 `N7-PERF-AND-VISUAL-CHECKLIST.md` §7 逐项标注(部分证实 / 仍需眼睛)。
### 10.6 本轮顺带修的一处护栏空洞:编码守卫只看 17 份文档

`check_docs_123.py` 的 `FILES` 还停在 2026-10-03 阶段 A/B/C 时的 17 份,**第 4–6 轮新增的文档(现状快照、路线图、验收记录、决策报告、普查、目视记录、身份/atkadd 研究、当前回退预案)都不在它的看护范围内** ——
也就是说这些 CJK 文件被写坏时,原来没有任何守卫会红。本轮:

- 文档集 **17 → 29 份**;
- 新增 `--selftest` **6 例**:干净文件不红,U+FFFD / mojibake / 非 UTF-8 字节 / **文件缺失**四种都必须红,并断言清单里的文档都还在(清单自己不会烂掉);
- 原实现在非法 UTF-8 上会抛 traceback、在文件缺失时直接崩;现在两者都算**损伤计数**并进 `TOTAL DAMAGE MARKERS`;
- 自测同时进入验收流水线(29 → **30 条命令**,`run_exit` 检查 51 条),所以"它真的会红"这件事每轮都被重新执行一次。

实测:29 份文档全部 `decode=OK`、`U+FFFD=0`、`mojibake=0`;`selftest: 6 case(s), 0 failed`。

- 两场新样本的 `compWeak = 0`(没有弱身份 actor):N3 身份普查的弱身份计数**不因这两场增加**,比较器的身份分层行为不受影响。
- `IDENTITY-CENSUS.md` / `IDENTITY-METADATA-DESIGN.md` 里「30 份导出」是**当时**的测量快照,按纪律**不改写**;新增两场的身份事实单独列在这里。


