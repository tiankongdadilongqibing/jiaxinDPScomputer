# 重构批次记录 RF0–RF2(第 1 轮)

> 依据:[REFACTOR-PLAN-POST-1.7.11.md](<REFACTOR-PLAN-POST-1.7.11.md>)。该方案第 1 轮要求完成 **RF0(基线/护栏)、RF1(行为测试)、RF2(Aggregator 机械拆分)**。
> 本文是该批次的交付记录(方案 §14 要求的"文件清单 / 命令与退出码 / 变更前后输出 / 未运行项 / 回滚标签 / 产物哈希")。

## 0. 结论

| 项 | 状态 |
|---|---|
| RF0 基线、仓库边界、冻结输入、输出隔离、文档勘误 | ✅ 完成 |
| RF1 规范化 C# 行为测试 + 变异负控 | ✅ 完成(174 用例 / 6 组 / 10 例负控) |
| RF2 `Aggregator` 机械拆分 | ✅ 完成(6 个 partial;IL 级等价) |
| RF3–RF7 | ❌ 未做(方案明确"不要求一轮全部完成") |
| **部署 DLL** | **未改**:仍是 1.7.11 / 387,072 B / `36EC96D4…`;新产物只落在 `src\bin\Release` |
| 本轮改动的性质 | 工具 + 文档 + 源码结构调整;不改公式、口径、钩子 ABI、UI、schema |

## 1. 本轮发现并修掉的 3 个真实缺陷(都不是本轮引入的)

| # | 症状 | 根因 | 修法 | 负控 |
|---|---|---|---|---|
| 1 | `check_live_log.py` 把 **102 帧**判成 ERROR | 面板把未归因值**四舍五入到整数**打印(`9,326,541`),导出里是双精度(`9326541.024`),而检查用的是**精确集合成员判定** → 一个打印正确的值被判"没有任何导出持有" | 改用 ±0.5(**取整区间**,不多不少) | 新增 `in_domain_row_rounds_to_the_export`(必须 PASS)+ `in_domain_row_beyond_rounding_is_an_error`(必须 ERROR) |
| 2 | `contrib.tests.test_gate` CASE3 报 1 failed | fixture 选"目录里**最新**一场带计数的导出";2026-10-04 最新的是**试炼场 9999**,而试炼场按定义是 `LEGACY_NOT_APPLICABLE`,永远到不了 ERROR | 选择时跳过 quest 9999 | 该检查本身的 64 项回归用例 |
| 3 | 文档与工具的数字互相矛盾 | 索引两处 + PROJECT-STATUS 一处仍写"29 条命令 / 50 条检查",而流水线是 30/51;**没有任何守卫检查数字** | 勘误 + **新增 R9/R10/R11**:命令/检查计数必须等于最新验收档案、被称"无段"的版本必须真的没有贡献段、"最大的 N 个文件"必须正好列 N 项 | `check_doc_convergence --selftest` M9/M10/M11 |

## 2. 文件清单

**新增(纳入 git,见 [REPO-BOUNDARY.md](<../../REPO-BOUNDARY.md>))**

```text
.gitignore .gitattributes REPO-BOUNDARY.md
_dpsm_work/repo_manifest.py           基线清单(源码/工具/配置/语料/外部程序集)+ --verify
_dpsm_work/batch_snapshot.py          冻结批次输入(hard-link 快照 + 可提交的清单)
_dpsm_work/baseline-manifest.json     基线清单本体(97 KB)
_dpsm_work/batch-inputs-rf0.json      本批输入清单(35 份 + 每份 SHA256,已提交)
_dpsm_work/tests/rf2_split.py         RF2 拆分器(含 LOSS CHECK,拒绝未知方法)
_dpsm_work/tests/il_equiv.py          token 无关的 IL 等价比较器
_dpsm_work/tests/negative_control.py  RF1 变异负控驱动器(10 例)
_dpsm_work/tests/BehaviorTests/       RF1 行为测试工程(174 用例 / 6 组)
_dpsm_work/tests/IlDump/              IL 指纹工具(供 il_equiv.py 使用)
_dpsm_work/src/Aggregator.Session.cs / .Clock.cs / .Attribution.cs / .Stats.cs / .Finalize.cs
```

**修改**:`n0_acceptance.py`(冻结输入 + 输出隔离 + 3 个新运行 + 20 例自测)、`refactor_final_check.py`(扫 `tests/` + Aggregator partial 组)、`check_doc_convergence.py`(R9/R10/R11 + `--applicability/--exports`)、`check_export_schema.py`(`--report`)、`check_live_log.py`(取整容差)、`contrib/tests/test_gate.py`、`check_docs_123.py`(文档集 29 → 32)、`repo_manifest.py`、4 份入口文档、`src/Aggregator.cs`。

**明确未动**:部署 DLL、`BepInEx/config/dev.dpsmeter.cfg`、任何导出文件、任何历史证据、两个 `*-crash.bak`。

## 3. RF0:基线可恢复、边界可审计、输入不变

### 3.1 本地 Git 基线

| 项 | 值 |
|---|---|
| 提交 / 标签 | `a2a09c2` / `baseline-1.7.11`(无远端) |
| 纳入文件 | 380 个(源码、测试、活跃工具、当前文档、`dev.dpsmeter.cfg`) |
| 边界 | `.gitignore` 首行 `/*` = **默认拒绝**,只 `!` 回显式清单;`.gitattributes` = `* -text`(按字节存取,因为守卫对文件内容做哈希,且历史文件里有故意非 UTF-8 的) |
| 被排除但**不删除** | 游戏资产 3.07 GB、`BepInEx/` 742 MB、`*.bak`、`bin/obj`、`_review/`/`_verify_*` —— 由 `baseline-manifest.json` 用 SHA256 关联 |

**源码可复现部署产物**:用 `src` 重新构建得到的 DLL 与部署中的 `DpsMeter.dll` **SHA256 完全相同**(`36EC96D4DBD8E221…`)。这不是"差不多",是位置敏感构建下的逐字节一致,所以"回滚 = 源码 + 工具 + 语料同时匹配"这句话有可执行内容。

### 3.2 `baseline-manifest.json` 的 verify 语义

`--verify` 报四类,只有第一类是非零退出:

| 类别 | 含义 | 判定 |
|---|---|---|
| `DRIFT` | 基线记录的资产**丢失或被替换**(部署 DLL、回滚锚点、配置、外部程序集、语料、被排除的大文件) | **失败** |
| `CHANGED` | `tracked_sources` 里的源码变了 | **不算漂移**(见下) |
| `GREW` | 基线之后**新增**了文件 | 不算漂移 |
| `NOTES` | 游戏/vendor 树(3 GB 资产)的指纹动了 | 不算漂移 |

**为什么源码变了不算漂移**:基线的承诺是"我记录过的东西没被改动或丢失",不是"没有新东西"。重构批次**本来就要改源码**,它的正确性由行为测试与验收裁决,不由哈希裁决;而**丢失**一个文件仍然是失败。首次实现把三者混在一起,`--verify` 在守卫被修改后立刻变红 —— 这才逼出了这个区分。

### 3.3 冻结批次输入(方案 §5.5)

输入目录 `BepInEx\plugins\DpsMeter\exports\` 是**活的**:准备本批时游戏正在运行,期间又落了一场(18:16)。所以本批不读目录,读快照:

```text
python batch_snapshot.py --name rf0        # 35 份 hard-link 到 _dpsm_work/batch_inputs/rf0/,清单提交到 batch-inputs-rf0.json
python batch_snapshot.py --name rf0 --verify
python n0_acceptance.py                    # 默认 --exports 即该快照,输入冻结为 35 份
```

`input_freeze` 检查**文件名集合**(计数 + 集合两项):只有计数相等时,一个文件换成另一个仍能蒙过去。语料内容哈希由同一次运行里的 `repo_manifest.py --verify` 覆盖。

### 3.4 输出隔离(方案 §5.4)

`n0_acceptance.py` 在整批前后对各 watch 根做 `stat` 快照:

| watch 根 | 含义 |
|---|---|
| `_dpsm_work/`(仅顶层文件)、仓库根(仅顶层文件) | 固定路径的"当前报告" |
| `_dpsm_work/acceptance_1.7.11/`(递归) | **历史档案:必须一点都不动** |
| `_dpsm_work/contrib/reports/`(递归) | 对比报告目录 |

任何 `--out` 之外、且不在声明清单里的改动 ⇒ 红灯。声明清单目前 3 项(两个 `--selftest` 的转录 + `v150_validate.txt`),每一项都写了原因;两个真正会写固定路径的**运行报告**已经改成 `--report` / `--outdir` 落到批次目录。

**这条检查第一次运行就抓到了东西**:它报出 3 个未声明改动 —— 其中 1 个是**我自己在批次运行期间创建的拆分器文件**。检查是对的。

### 3.5 文档勘误 + 让守卫能查数字

方案 §5.6 指出"现有 guard 只验证关键词存在,没抓到命令计数和版本分布矛盾"。已核对并修正:

* 索引 2 处、PROJECT-STATUS 1 处把流水线写成 "29 条命令 / 50 条检查"(实为 30/51);
* 状态表把 **1.6.0** 混进"无贡献段的段前版本"—— 1.6.0 那 1 份**有**贡献段(它是坏样本,不是段前版本);无段 13 份 = 1.5.3–1.5.5;
* "最大的 6 个文件"实际列了 7 个。

并新增三条规则(R9/R10/R11)与对应变异负控,把"数字不一致"变成红灯。

## 4. RF1:规范化 C# 行为测试

`_dpsm_work/tests/BehaviorTests` 编译**真实生产源码**(不复制公式),输出用例名与非零失败码:

```text
behavior tests: cases=174 failed=0 pinned=174
```

| 组 | 覆盖 | 为什么它值得存在 |
|---|---|---|
| `clock/`(9) | `Advance`(≤0 忽略、暂停只进 active、**不做停顿钳制**)、`NoteEvent`/`IdleCombatSeconds`、`BattleTime` 三种格式 | 时间只在一个地方累加;钳制在调用方,搬进 `Advance` 会改变每一场的时钟 |
| `window/`(19) | 0.35 s 配对窗的**边界前/等于/后一 ulp**(`Math.BitIncrement`)、精确优先于配对、候选顺序、已消费不可再匹配、身份不匹配 | 这决定每个事件的 `source`/`crit`/`hitValue`;错了不改任何总量,只静默换标签 |
| `session/`(36) | 键分配、使魔合并、`ResetActors`(**保留时钟**,清计数)、事件上限、队伍每秒序列、`FromSummary` | `ResetActors` 是 RF4 要迁移的状态族;"时钟是否跨 reset 存活"正是跨场重构最容易弄错的地方 |
| `series/`(30) | 每秒桶、HP 百分比、DPS 阈值、伤害曲线(0.04 s 合并 / 6000 点压缩 / **合并优先于压缩**) | 三处规则在任何导出总量里都看不见 |
| `cache/`(26) | `ContributionSession.Get` 的四路 OR 失效、历史选择、`Invalidate` | 方案 §3.3 明确警告"它不是一秒节流" |
| `tiered/`(54) | 从 `test/TierTest` **迁入**(那个工程从未进过验收流水线) | 9/6/3 割边界决定实机倍率 |

**负控([negative_control.py](<tests/negative_control.py>),10 例,全 PASS)**:把 `src` 复制到临时目录、做一处变异、用 `-p:SrcRoot` 重建同一套测试,要求**指定用例**变红。规则:变异文本必须恰好命中 1 处(否则是驱动器失败)、必须是指定用例红(不是"有什么红了就算")、以及一例**只改散文**的变异必须保持全绿 —— 这一例才证明红来自行为而不是"我们重建过"。

**两个由测试固定下来的既有行为(记录,不是修)**:

1. `ContributionSession.Get` 的 `_cacheUsedFolds != useFolds` 子句在 live 下**不可达**(`!useFolds` 已提前返回不可用视图);
2. 同一会话内 `ResetActors` 后再填回**相同**事件数,一秒内仍返回**旧缓存**(面板会显示上一份结果)。

两条都属于 RF5 的缓存重做范围,已由用例钉住。

**本轮明确未覆盖(诚实)**:`Aggregator` 的 Start/End/软恢复/`FinalizeLocked` 编排、原生读取时机、导出副作用顺序、UI。它们要么依赖 IL2CPP,要么需要把判据抽成纯函数(RF3)之后才能离线执行;方案也把"不能只靠旧 JSON 重放证明新钩子正确"写进了 §2 的校正表。

## 5. RF2:`Aggregator` 机械拆分

| 文件 | 内容 | 行 |
|---|---|---|
| `Aggregator.cs` | **全部字段/常量/嵌套 struct**(静态初始化顺序集中在一处)+ `PtrOf`/`Desc`/`NameOf`/`KindTag` | 168 |
| `Aggregator.Session.cs` | `InBattle`/`PendingCount`/`StartSession`/`EnsureSessionStarted(For)`/`TryResumeClosedSession`/`EndSession`/`ResetCurrent` | 204 |
| `Aggregator.Clock.cs` | `GameUnitsPerSecond`/`FrameDelta`/`ClockSourceName`/`Tick`/`SampleHp`/`BeginTimingIfNeeded` | 275 |
| `Aggregator.Attribution.cs` | `NoteCalcActivity`/`TryGetCompForVictim`/`CalcValueMatches`/`TryResolveCalcSource`/`NoteHitDetail`/`RecordHitDetail`/`NoteActiveCalc` | 263 |
| `Aggregator.Stats.cs` | `IsSameTeam`/`TeamOf`/`Accumulate`/`RecordDamage`/`RecordHeal` | 340 |
| `Aggregator.Finalize.cs` | `FinalizeLocked` | 169 |

### 5.1 四层证据

| 层 | 做法 | 结果 |
|---|---|---|
| **源码无损** | [rf2_split.py](<tests/rf2_split.py>) 只移动方法,并做 **LOSS CHECK**:输出的"类体代码行多重集"必须与输入完全相等;任何未登记的方法名直接 BLOCK(不猜归属) | `1273 行进 / 1273 行出,multiset equal` |
| **编译** | `dotnet build -c Release` | 0 警 0 错;产物 **387,072 B**(与旧 DLL 同尺寸) |
| **IL/元数据等价** | [il_equiv.py](<tests/il_equiv.py>) 比较整个程序集:每个类型的**字段列表(声明顺序)**、每个方法的**名字 + IL 字节长度** | `EQUIVALENT: True`(120 类型 / 1083 字段 / 754 方法) |
| **守卫** | `refactor_final_check.py` 新增 `src/Aggregator*.cs` partial 组;`repo_manifest --verify` | `Aggregator 6 partial files, all declare partial: True`;`drift=0 changed=11` |

**为什么不做"IL 哈希相等"**:签名 blob 与 IL 里的成员操作数编码的是 **coded token(元数据行号)**;把一个类拆到多个文件会改变编译器发出行的顺序,于是一次**没有任何代码变化**的搬动也会让 292/567 个方法的 `il=` 哈希和 119 个签名变化。把这一点写清楚,比给它一个好看的绿色更有用 —— 所以比较器只用**token 无关**的事实,并把生成的 lambda/闭包类型名按数字归一化(编译器按方法序号命名它们,拆分合法地改变了序号)。

**未覆盖(诚实)**:没有任何离线测试执行 `Aggregator` 的运行时行为(见 §4 末)。因此本批**不声称**降低了运行耦合,只声称"搬动是机械的、可复核的、可回滚的" —— 这正是方案为 RF2 设定的出口。

## 6. 验收与回滚

| 项 | 值 |
|---|---|
| 基线轮(拆分前) | `acceptance_rf0`:`33 条命令 / 63 条检查 / 59 ok / 4 项` —— 3 项见 §1,第 4 项是本轮工具自身产物(见 §3.4) |
| 终轮(拆分后) | `acceptance_rf2`:**`33 条命令 / 65 条检查 / 65 ok / 0 项`**。逐文件钉住的预测**全部命中**:batch 1/17/14/3、embedded 13/4/14/3/1、applicability 16/9/10,且 `batch.LEGACY 17 == embedded.NOT_RUN 13 + embedded.LEGACY 4` |
| 检查数 63 → 65 | 隔离检查里 `current_output` 的行数 = 本次真正被重写的固定路径数。基线轮声明清单的键写成了脚本相对路径(实际是仓库相对),那两行当时落进 `undeclared`;键改对后 +2。这是**数据/记账差别**,不是行为变化 |
| 全量命令 | `python n0_acceptance.py`(默认读快照、写 `acceptance_rf2`) |
| 回滚 | 源码:`git checkout baseline-1.7.11`;DLL:本轮**从未替换**部署产物;语料:快照清单 + `baseline-manifest.json` |

## 7. 下一轮(RF3 起)的建议与不做事项

* **RF3 纯判据下沉**:RF1 已经把时钟、0.35 s 窗口、缓存三条判据钉住;下一步是把时间窗判定/run 归组判据抽成纯函数(输入显式时间,不引用 Unity 时钟),再用现有夹具做**旧新对照**。
* **RF4 状态生命周期**:先交 `StateLifetimeMatrix`;`ResetActors` 保留时钟、软恢复可恢复同会话这两条已有用例保护。
* **RF5 展示层**:两处已知缓存行为(§4)必须与新行为一起重新定义预期,不能顺手改。
* **RF7 工具治理**:本轮已把批次输入/基线/测试放进独立目录,`_dpsm_work` 顶层仍有 60+ 个一次性脚本 —— 下一批做依赖普查与只读索引,**不移动、不删除**历史证据。
* **不做**:改 `dealt`/吸收口径、改会话合并、接新的暴击观测、加配队页、调归因模型、重写 `CompositionProbe`、升级 Unity/.NET/Interop、引入 DI/事件总线。
