# CONTRIBUTION-APPLICABILITY-P0D — 任务适用性判定

> 任务: P0-D 建立任务适用性判定(避免把 411001 / 700817 / 训练场 9999 的贡献结果无条件横向比较)。
> 证据等级: **离线重放**(逐击重放全部 26 份导出的 events / calc 字段);crosscheck 一栏是实际运行 contrib.crosscheck.compare 的结果。
> 插件基线 1.7.7(381,952 B,SHA256 前缀 7425139C;报告当时的基线,当前 1.7.9);报告时导出语料为 1.5.3 – 1.7.6 共 26 份。
> 纪律: 本任务只新增文件,未修改仓库任何既有文件;未 dotnet build / 未部署 / 未改 BepInEx 下任何文件/未改或删 exports。

## 0. 交付清单(NEXT-STEPS §10)

| 项 | 内容 |
| --- | --- |
| 任务编号 | P0-D 建立任务适用性判定 |
| 修改文件 | 仅新增 3 个文件: _dpsm_work/contribution_applicability.py; _dpsm_work/contribution_applicability_report.json; _dpsm_work/CONTRIBUTION-APPLICABILITY-P0D.md |
| 未修改的边界文件 | BepInEx 下全部文件(DLL/.bak/config/exports 均只读); _dpsm_work/contrib/*.py; _dpsm_work/contribution_gate.py; _dpsm_work/check_export_schema.py; _dpsm_work/check_contribution_layout.py; src/ 全部 C# |
| 输入导出 | BepInEx/plugins/DpsMeter/exports/battle_*.json 共 26 份(427.3 MB) |
| 证据等级 | 离线重放;crosscheck 为实际运行 |
| 运行命令 | 见 §7 |
| 测试结果 | selftest 18/18 PASS;26 份扫描 unexplained fold leak = 0、reconciliationGap 全 0、分析过滤器与离线 core 逐份一致 |
| 新增失败用例 | 见 §6:修复前 3 例 FAIL,修复后 18/18 PASS;并用真实 700817 展示旧 knownMult 判据会误报 59 处 |
| 已知限制 | 见 §8 |
| 后续依赖 | 见 §9 |

## 1. 判据修正(已写进 contribution_applicability.py 顶部注释)

1. 「漏折」的**唯一合法判据**是 | prod(calc.fold) / (dealtMult × takenMult) − 1 | 超阈值,**排除 attrMult**。attrMult(属性相性)按设计不进 calc.fold(CompositionProbe.Chain.cs:116),而 knownMult = attrMult × dealtMult × takenMult。用 knownMult 直接比 fold 乘积会得到看似确凿的错误结论。
2. knownMultWithoutFold 仍按 NEXT-STEPS 的字面定义统计(knownMult != 1 且 calc.fold 为空),但**只作诊断**;真正判漏折的是 foldIdentity.violationsUnexplainedByRounding 与 knownMultWithoutFold.unexplainedHits。
3. 实测(离线重放,全 26 份):700817 的 59 处与 9999 的 36 处 knownMultWithoutFold **无一例外**满足 attrMult == knownMult(unexplained = 0);411001 的 19 份一处都没有。
4. 退化命中:dealtMult × takenMult == 0 时相对恒等式无定义,单独计数,并检查唯一自洽结果 prod(fold) == 0。26 份的退化命中全部自洽(degenerateInconsistent = 0)。
5. 训练场识别:quest 9999。×0.03 规则在导出里序列化成 takenMult == 0.028(3 位小数),**只出现在 9999**;另有 sub-unity fold(0 < factor < 1,分池时按 SubUnity 丢弃,Contribution.cs:384)。两者都记入 specialMode,不套普通任务模型。
6. 差额拆分(NEXT-STEPS §0.3):dealtMinusAnalyzable **不叫** excludedDamage。已验证:totals.dealt − analyzableDealt = 敌方(team 2)伤害 + 我方被排除伤害;敌方伤害有逐事件证据但属正常预期(不计入我方漏分析);reconciliationGap 单列。attacker 不可解析的命中**根本不进 totals.dealt**(它等于 totals.unattributed*),另立 outsideDealt。
## 2. 阈值选择与理由

主判据: **| prod(calc.fold) / (dealtMult × takenMult) − 1 | > tolerance**。

- 严格相对阈值 **1e-3**(任务建议值):411001 全部 19 份与 700817 **0 违规**;只对 9999 报违规(205129 = 15、225905 = 2、042458 = 16),最大相对偏差 2.0619e-3。
- 但 9999 这些不是漏折,而是 **sub-unity 链上 3 位小数序列化的放大**。例(042458):dealtMult = 1.21, takenMult = 0.194, prod(fold) = 0.235224, 链值 = 0.23474, 绝对差 4.84e-4;相对 2.06e-3 之所以大,是因为分母只有 0.194——同一个 ±5e-4 绝对舍入被小分母放大。
- 所以实现用 **绝对项 + 随因子数增长的相对项** 包络:

  envelope = 5e-4 × (|dealtMult| + |takenMult|) + (1e-3 + 5e-5 × nFoldFactors) × |dealtMult × takenMult|

  - 绝对项:dealtMult / takenMult / attrMult / knownMult 只写 3 位小数,每个 ±5e-4 绝对。
  - 相对项:1e-3 是任务给的底;5e-5 × 因子数**解释参与相乘的因子数带来的舍入累积**(1.6.1+ 的 fold.factor 是往返精度可忽略;1.5.x / 1.6.0 截断到 4 位,每个因子最坏 5e-5,因子越多累积越大)。
- 用该包络,26 份全部 unexplained = 0。042458 的包络 = 5e-4 × 1.404 + (1e-3 + 6 × 5e-5) × 0.23474 = 7.02e-4 + 3.05e-4 = **1.007e-3** > 4.84e-4,判为舍入可解释。
- 阈值**不是**调到不报错的橡皮图章:合成用例 3/4 里 fold 乘积 1.5 对链值 1.0(绝对差 0.5)仍被判 not_comparable(见 §6)。

## 3. 26 份导出分类表(离线重放)

列说明:sec = 是否有 contribution 段;cc = crosscheck 状态(mm = 值不一致数);app = modelApplicability;knf = knownMultWithoutFold(命中 / attrMult 已解释 / 未解释);strictV / unexpV = 1e-3 严格违规数 / 扣除舍入包络后仍未解释数;maxRel = 最大相对偏差;special = specialMode markers;scope = comparisonScope。

| # | 导出 | ver | quest | sec | cc(mm) | app | knf(h/a/u) | strictV/unexpV | maxRel | special | scope |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 411001_20261003_205449 | 1.5.3 | 411001 | - | NOT_RUN | partial | 0/0/0 | 0/0 | 3.782e-4 | - | offline-recompute-only |
| 2 | 411001_20261003_230040 | 1.5.4 | 411001 | - | NOT_RUN | partial | 0/0/0 | 0/0 | 3.782e-4 | - | offline-recompute-only |
| 3 | 411001_20261003_230311 | 1.5.4 | 411001 | - | NOT_RUN | partial | 0/0/0 | 0/0 | 3.782e-4 | sub_unity_folds | offline-recompute-only |
| 4 | 411001_20261003_230615 | 1.5.4 | 411001 | - | NOT_RUN | partial | 0/0/0 | 0/0 | 3.782e-4 | sub_unity_folds | offline-recompute-only |
| 5 | 411001_20261003_231044 | 1.5.4 | 411001 | - | NOT_RUN | partial | 0/0/0 | 0/0 | 3.782e-4 | sub_unity_folds | offline-recompute-only |
| 6 | 411001_20261003_231340 | 1.5.4 | 411001 | - | NOT_RUN | partial | 0/0/0 | 0/0 | 3.782e-4 | sub_unity_folds | offline-recompute-only |
| 7 | 411001_20261003_235204 | 1.5.5 | 411001 | - | NOT_RUN | partial | 0/0/0 | 0/0 | 3.782e-4 | - | offline-recompute-only |
| 8 | 411001_20261004_012328 | 1.5.5 | 411001 | - | NOT_RUN | partial | 0/0/0 | 0/0 | 3.782e-4 | - | offline-recompute-only |
| 9 | 411001_20261004_015919 | 1.6.0 | 411001 | Y | ERROR (58) | not_comparable | 0/0/0 | 0/0 | 3.782e-4 | - | invalid-plugin-value-trust |
| 10 | 411001_20261004_021914 | 1.6.1 | 411001 | Y | PASS (0) | full | 0/0/0 | 0/0 | 3.782e-4 | - | normal-benchmark |
| 11 | 411001_20261004_023627 | 1.7.0 | 411001 | Y | PASS (0) | full | 0/0/0 | 0/0 | 3.782e-4 | - | normal-benchmark |
| 12 | 411001_20261004_033231 | 1.7.2 | 411001 | Y | PASS (0) | full | 0/0/0 | 0/0 | 3.782e-4 | - | normal-benchmark |
| 13 | 411001_20261004_035437 | 1.7.3 | 411001 | Y | PASS (0) | full | 0/0/0 | 0/0 | 3.782e-4 | - | normal-benchmark |
| 14 | 411001_20261004_041309 | 1.7.4 | 411001 | Y | PASS (0) | full | 0/0/0 | 0/0 | 3.782e-4 | - | normal-benchmark |
| 15 | 411001_20261004_042637 | 1.7.5 | 411001 | Y | PASS (0) | full | 0/0/0 | 0/0 | 3.782e-4 | - | normal-benchmark |
| 16 | 411001_20261004_042844 | 1.7.5 | 411001 | Y | PASS (0) | full | 0/0/0 | 0/0 | 3.782e-4 | - | normal-benchmark |
| 17 | 411001_20261004_113553 | 1.7.5 | 411001 | Y | PASS (0) | full | 0/0/0 | 0/0 | 3.782e-4 | - | normal-benchmark |
| 18 | 411001_20261004_115024 | 1.7.6 | 411001 | Y | PASS (0) | full | 0/0/0 | 0/0 | 3.782e-4 | - | normal-benchmark |
| 19 | 411001_20261004_115417 | 1.7.6 | 411001 | Y | WARNING (0) | full | 0/0/0 | 0/0 | 3.782e-4 | - | normal-benchmark |
| 20 | 700817_20261004_114821 | 1.7.6 | 700817 | Y | WARNING (0) | partial | 59/59/0 | 0/0 | 2.220e-16 | - | caveat-attribute-affinity |
| 21 | 9999_20261003_205129 | 1.5.3 | 9999 | - | NOT_RUN | not_comparable | 2/2/0 | 15/0 | 2.062e-3 | training_ground,training_x0.03,sub_unity_folds,self_damage,friendly_fire | training-ground-only |
| 22 | 9999_20261003_225530 | 1.5.4 | 9999 | - | NOT_RUN | not_comparable | 5/5/0 | 0/0 | 6.925e-4 | training_ground,training_x0.03,sub_unity_folds,self_damage,friendly_fire | training-ground-only |
| 23 | 9999_20261003_225647 | 1.5.4 | 9999 | - | NOT_RUN | not_comparable | 7/7/0 | 0/0 | 6.925e-4 | training_ground,training_x0.03,sub_unity_folds,self_damage,friendly_fire | training-ground-only |
| 24 | 9999_20261003_225905 | 1.5.4 | 9999 | - | NOT_RUN | not_comparable | 12/12/0 | 2/0 | 1.033e-3 | training_ground,training_x0.03,sub_unity_folds | training-ground-only |
| 25 | 9999_20261004_012138 | 1.5.5 | 9999 | - | NOT_RUN | not_comparable | 2/2/0 | 0/0 | 6.925e-4 | training_ground,training_x0.03,sub_unity_folds,self_damage,friendly_fire | training-ground-only |
| 26 | 9999_20261004_042458 | 1.7.5 | 9999 | Y | ERROR (0) | not_comparable | 8/8/0 | 16/0 | 2.062e-3 | training_ground,training_x0.03,sub_unity_folds,self_damage,friendly_fire | training-ground-only |

**按任务汇总**

| quest | 份数 | 版本 | 有段 | app 集合 | knf(attr/总) | 未解释漏折 |
| --- | --- | --- | --- | --- | --- | --- |
| 9999 | 6 | 1.5.3,1.5.4,1.5.5,1.7.5 | 1 | not_comparable | 36/36 | 0 |
| 411001 | 19 | 1.5.3,1.5.4,1.5.5,1.6.0,1.6.1,1.7.0,1.7.2,1.7.3,1.7.4,1.7.5,1.7.6 | 11 | full,not_comparable,partial | 0/0 | 0 |
| 700817 | 1 | 1.7.6 | 1 | partial | 59/59 | 0 |

**总计**: full = 10,partial = 9,not_comparable = 7(26 份)。最终一轮 crosscheck 状态分布: {"NOT_RUN":13,"ERROR":2,"PASS":9,"WARNING":2}。
注:跨实现细节(crosscheck 状态)由 P0-A 的 contribution_gate 给出,是活文件;表中状态为最终一轮实跑值,适用性判定按值级计数(mismatches/omissions/validateErrors)解耦。

## 4. 三类任务的明确比较资格(离线重放)

### 4.1 quest 411001(19 份)

| 版本组 | 份数 | app | scope | 依据 |
| --- | --- | --- | --- | --- |
| 1.5.3 / 1.5.4 / 1.5.5 | 8 | partial | offline-recompute-only | 无 contribution 段;可离线复算,但无法验证插件内存值 |
| 1.6.0 | 1 | not_comparable | invalid-plugin-value-trust | 段存在但与离线复算 58 处不一致(factor 4 位截断) |
| 1.6.1 / 1.7.0–1.7.6 | 10 | full | normal-benchmark | 段存在、crosscheck 0 不一致、fold 恒等式 0 违规、attrMult 恒为 1、无 sub-unity fold、差额 100% 有逐事件证据 |

- 当前主基准 = 1.7.6 两份(115024、115417);115417 实测:checkedHits = 6259,违规 0,最大相对偏差 3.782e-4,退化命中 5(全部 prod(fold) == 0 自洽)。
- 411001 的 knownMultWithoutFold = 0(19/19 份),所以 411001 从不触发旧 knownMult 判据的争议。

### 4.2 quest 700817(1 份,1.7.6)

- app = **partial**,scope = caveat-attribute-affinity,crosscheck PASS(0 不一致)。
- fold 恒等式:**0 违规**,最大相对偏差 **2.220e-16**(284 击全部 checked)。
- knownMultWithoutFold = **59 击 / 91,482 伤害**,其中 **59/59 全部满足 attrMult == knownMult == 2**(属性相性),unexplained = 0 => **不是漏折**。
- attrMult != 1 共 62 击 / 102,578 伤害 = dealt 的 4.43%(属性相性按设计不进 fold,该 ×2 留在 baseCredit)。
- 比较资格:可与 411001 的 full 场比较 fold 口径下的贡献,但必须注明「属性相性不建模」;不得把这 62 击的属性优势归给辅助角色。若项目只以 fold 恒等式为标准,700817 是 0 违规。

### 4.3 训练场 9999(6 份)

- app = **not_comparable**,scope = training-ground-only(6/6)。
- specialMode markers 组合:training_ground + training_x0.03 + sub_unity_folds(+ self_damage / friendly_fire)。
- ×0.03 特征:**takenMult == 0.028**(3 位小数序列化),仅 9999 出现;各文件 6 – 225 击。
- sub-unity fold(0 < factor < 1)各文件 38 – 750 击;1.5.x 部分 411001 也有少量(7 – 10 击),所以它不是 9999 的独有标记,必须与 quest 9999 联合判断。
- 其余 specialMode 证据(042458):自身伤害 18 击 / 983,861;友伤 14 击 / 221,669——这些**在 analyzableDealt 内部**,却被贡献模型当作正常伤害分配。
- 严格的 1e-3 相对判据在 9999 报 15 / 2 / 16 击违规(最大 2.0619e-3),包络判定后全部为舍入可解释(unexplained = 0)。
- 比较资格:**不得**与普通任务直接比较;其贡献数可作训练场自身纵向观察。

### 4.4 比较矩阵

| A vs B | 允许? | 说明 |
| --- | --- | --- |
| 411001 full vs 411001 full | 是 | 同一 normal-benchmark 族 |
| 411001 full vs 411001 1.5.x | 仅离线复算口径 | 证据等级不同(无段) |
| 411001 full vs 700817 | 有条件 | 属性相性(0 vs 62 击)不建模 |
| 411001 / 700817 vs 9999 | 否 | 训练场特殊模式 |
| 1.6.0 vs 任何 | 否 | 段值与离线复算不一致(58) |

## 5. dealtMinusAnalyzable 的拆分(离线重放,26/26)

**恒等式(逐份实测,0 例外)**:
totals.dealt − offlineAnalyzableDealt = 敌方(team 2)伤害 + 我方被排除伤害;本语料 **ownSideExcluded = 0**,故
**reconciliationGap = 0(26/26)**。
attacker 不可解析的命中**不在 totals.dealt 内**,其伤害与命中数**逐份等于** totals.unattributedDamage / unattributedHits。

| 导出 | totals.dealt | analyzableDealt | 差值 | 敌方命中/伤害 | 我方被排除 | reconciliationGap | 不可解析(==totals.unattributed*) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 411001_115417 (1.7.6) | 190,984,342 | 190,889,625 | 94,717 | 12 / 94,717 | 0 | **0** | 71 / 273,702(= 273,702 / 71) |
| 700817_114821 (1.7.6) | 2,314,831 | 2,289,375 | 25,456 | 15 / 25,456 | 0 | **0** | 37 / 16,225(= 16,225 / 37) |
| 9999_042458 (1.7.5) | 7,024,899 | 4,351,950 | 2,672,949 | 448 / 2,672,949 | 0 | **0** | 8 / 281,763(= 281,763 / 8) |
| 9999_205129 (1.5.3) | 7,702,608 | 4,680,300 | 3,022,308 | 626 / 3,022,308 | 0 | **0** | 10 / 319,936(= 319,936 / 10) |

- 这修正了旧报告「差额就是没进贡献表的伤害」的措辞:差额**全部是敌方正常伤害**(有逐事件证据,但按 §0.3 不算我方漏分析伤害),不是一个单一黑箱数字。
- 9999_042458 的 −38.05%(旧报告 §9.1-1)=(7,024,899 − 4,351,950) / 7,024,899,本表确认其构成:**敌方 448 击 / 2,672,949 全部解释,gap 0**。
- 自伤/友伤在 analyzableDealt **内部**(9999 才有,411001/700817 全 0),见 4.3。

## 6. 测试结果与新增失败用例

### 6.1 修复前(先写能失败的用例)

首次运行 selftest 输出:**selftest FAIL (15/18)**,3 例失败:

```
  [FAIL] training ground x0.03 marker                         got=False want=True
  [FAIL] dealtMinusAnalyzable                                 got=None want=400.0
  [FAIL] reconciliationGap kept separate                      got=None want=300.0
selftest FAIL (15/18)
```

- 失败 1:合成 9999 用例只放了 takenMult = 0.194,而真实 ×0.03 特征是 takenMult == 0.028;扫描器(正确)没有打出 training_x0.03。=> 用例本身建错,已补 one hit takenMult = 0.028。
- 失败 2/3:合成用例既无 contribution 段也无离线值,analyzable 取不到,差额与 gap 变 None。=> 实现缺陷:扫描器必须能用自身事件过滤器兜底算出 analyzable。已修(offline -> 段 -> 自身过滤器 三级回退)。

### 6.2 修复后

selftest **OK (18/18)**,并且用真实 700817 导出反证旧判据:

```
  real 700817: retired=59 corrected=0 (attrMult-explained=59)
FAILING-BEFORE (retired knownMult criterion): 700817 leaks=59 -> judged a fold leak
PASSING-AFTER  (corrected prod(fold) criterion): unexplained=0
selftest OK (18/18)
```

18 条用例覆盖:attrMult 解释的命中不算漏折;真漏折(attrMult=1、fold 空、knownMult=2)必须被抓住;fold 乘积 1.5 对链值 1.0 必须被判 not_comparable;sub-unity 舍入必须被解释而不是误报;quest 9999 必须 not_comparable 且带 training_x0.03 标记;三者差额拆分;真实 700817 的旧/新判据分歧。

### 6.3 全量扫描结果

- 26 份全部扫描成功,unexplained fold leak = **0**;degenerateInconsistent = **0**;reconciliationGap = **0**(26/26)。
- 分析过滤器(roster team == 1)算出的 analyzableDealt 与离线 core(contrib.aggregate.analyze)逐份一致(analyzableFilterMatchesOffline 全 true)。
- 分类:full 10、partial 9、not_comparable 7。

## 7. 运行命令(工作目录 _dpsm_work)

```powershell
$py = 'C:\Users\24134\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe'
$env:PYTHONIOENCODING='utf-8'

# 自测(18 例,含真漏折与旧判据反证)
& $py contribution_applicability.py --selftest

# 全量扫描 26 份;stdout 为 ASCII,JSON 为 UTF-8
& $py contribution_applicability.py --json contribution_applicability_report.json

# 单文件、跳过 crosscheck
& $py contribution_applicability.py --only battle_700817_20261004_114821.json --no-crosscheck
```

耗时:全量 64 s(含 crosscheck:13 份有段文件各跑一次 contrib.crosscheck.compare)。

## 8. 已知限制

1. **crosscheck 状态词表是 P0-A 的活文件**:本任务进行中观察到 1.6.1 的状态从 LEGACY_NOT_APPLICABLE 变为 PASS。扫描器因此**不**用聚合 status 决定适用性,而用值级计数(mismatches / omissions / validateErrors)判定段值是否可信;status 原样记录。若 P0-A 再改词表,JSON 里仍能追溯。最终一轮:13 份有段文件 = PASS 9 + WARNING 2 + ERROR 2;其中 9999_042458 的 ERROR 来自 P0-A 的 TRAINING_NOT_COMPARABLE / ATKADD_FOLDS_VS_BODY,而值级计数为 0(段值仍与离线一致),所以其 not_comparable 由训练场规则单独给出。
2. 阈值是**经验包络**:按语料现状(3 位小数链字段 + 1.6.1+ 往返 fold 因子)标定。若插件改序列化精度,5e-4 / 5e-5 两系数必须重标。
3. 700817 的 partial **不是**漏折结论,而是「属性相性未进 fold」的覆盖率陈述(62 击 / 4.43%)。若项目只以 fold 恒等式为准,它是 0 违规。
4. 1.5.x 无段文件只能离线复算,无法验证插件内存值一致性;本扫描器的 offlineAnalyzableDealt 来自 Python core,不是插件值。
5. specialMode 的 training_ground 由 quest == 9999 判定;training_x0.03 由经验特征 takenMult < 0.1 判定,若普通任务里出现真实 ×0.03 减伤也会命中该标记(但不会置 training_ground)。
6. 本扫描器只读 calc / events / contribution 段,**不重算**完整贡献池,也不改任何既有 KPI。
7. attributedDamage / 覆盖率字段的语义修正属 P0-B;本报告不涉及。

## 9. 后续依赖

- **P0-A**:contribution_gate 状态词表冻结后,本扫描器只需重跑即可同步(逻辑已按值级计数解耦)。
- **P0-B**:一旦插件(1.7.8 源码)在段里写出 outsideTeam / unknownAttacker 计数,reconciliation 可直接对账段内计数,而不必由事件重放推断。
- **P0-C**:PairTrusted 口径对照可与本适用性合并成「先判适用、再比口径」的两段流程。
- 构建 / 升版本 / 部署:本任务不需要,由主负责人统一处理。
