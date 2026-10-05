# 重构批次记录 R60:自伤判定三件套(配置回显 + 贡献段自伤字段 + F5 自伤列)

> 触发:你确认了 R59 查出的三个缺口,要求「按照你说的这三点修改一下,恢复自伤判定」。
> 插件版本 **1.7.12**;**本轮只做了构建,未替换线上 DLL**(替换时游戏正在运行,见 §4)。

## 0. 结论

1. **同队/自我伤害现在是文件可自述的**:导出根新增 `config.filterFriendlyFire`。它是**唯一**会改变**既有数字含义**
   的开关(决定 `totals.dealt` / `perSecDamage` / `命中` 是否含同队伤害),以前不写进文件,读者只能猜。
2. **归属账里它有了自己的字段**:`contribution.actors[]` 新增 `friendly` / `friendlyHits` / `hostileDamage`。
   它们由**逐事件 `friendly` 标志**分类(不是减法),所以 `friendly + hostileDamage == directDamage` **精确**成立,
   而且**不受 `FilterFriendlyFire` 影响** —— 那个开关只动游戏口径的 `dealt`,不动归属账。
3. **F5 表 1 恢复「自伤」列**(1.5.x 曾有):`T1` 由 8 列 83 / 行 85 变 **9 列 92 / 行 94**,Contribution 页面板
   上限 780 → **880**(等宽排版,面板不跟着变宽就会把列画到背景外)。它是**报告列**,不移动任何 credit。
4. **既有数值一个都没动**:`dealt` / `directDamage` / `totalCredit` / `analyzeVsTotalsDelta` 全部与 1.7.11 相同。

## 1. 改动

| 位置 | 改动 |
|---|---|
| `src/Output/ExportService.cs` | 根对象写入 `config.filterFriendlyFire`(取值 = `Plugin.CfgFilterFriendlyFire`,默认 false);文件头格式注释同步 |
| `src/Output/Contribution.cs` | `ContributionHit.Friendly`;`ContributionActorRow` / `Credit` 增加 `Friendly` / `FriendlyHits` / `Hostile`;`Compute` 按事件标志分类;`AppendJson` 写三个新字段 |
| `src/Output/ContributionSession.cs` | 投影 `BattleEvent.Friendly` 进 `ContributionHit`(唯一同时认识两边的地方) |
| `src/Ui/ContributionColumns.cs` | 新增 `T1Friendly = 9` 与「自伤」列;`T1LineWidth` 85 → 94;`T1Row` / `T1TotalsLine` 增参;pending 表同步多一个 `-` |
| `src/Ui/ContributionRowModel.cs` | 行值 `Friendly` + 合计 `SumFriendly`(合计仍覆盖**全部** actor,含未显示的行) |
| `src/Ui/OverlayUGUI.Rows.cs` | 调用处传 `Friendly`;口径说明新增一行「自伤=…已含在自身/直接打出里,读对敌输出要减掉」;面板 780 → 880 |
| `src/Ui/FallbackText.cs` / `src/Ui/OverlayCore.cs` | IMGUI 回退的行文本同样打印自伤(两个渲染器不能各说一套) |
| `src/BuildInfo.cs` / `src/DpsMeter.csproj` | 1.7.11 → **1.7.12** |
| `check_export_schema.py` | 根 `config`(1.7.12 起必需)+ `CONFIG_KEYS`;actor 三个**可选**字段的类型校验与两条恒等式(`friendly+hostile==direct`、`friendlyHits<=hits`);自测 54 → **61** 例 |
| `check_contribution_layout.py` | 副本 T1 加「自伤」列、`T1_W` 85 → 94、`t1_row`/`t1_total` 同步(`.get("friendly", 0.0)` 让旧段仍可重放) |
| `contrib/`(离线核心) | `ActorCredit` 增 `friendly/friendly_hits/hostile`;`aggregate` 按事件标志分类;`report_json` 输出;`crosscheck` 在**插件版本 >= 1.7.12** 时把三字段当必比对项(旧段缺字段不算错);`validate` 新增恒等式 **I2b** |
| `contrib/crosscheck.py`(既有缺陷) | `totalDamage` 期望值按**段内 `schemaVersion`** 选择:1.1 段 = `analyzableDealt` |
| `tests/BehaviorTests/` | 新增 `extract/friendly-split` 组(7 例)与 row-model 的 `Friendly`/`SumFriendly` 断言;列定义/行构造/回退文本/待确认表的期望同步 |
| `tests/negative_control.py` | 新增 5 条变异 + 更新 1 条改名后的期望 |

## 2. 验证

| 闸门 | 结果 |
|---|---|
| `dotnet build src/DpsMeter.csproj -c Release` | **0 警告 0 错误**;产物 434,688 B / `F791F1CFD0E5A1963EB7669505C3D9CE8D827AD9F35D6F82B8E909A2B0DB7E7` |
| BehaviorTests | **960 用例 / 99 组 / 0 失败**(950 → 960) |
| 变异负控(新增 5 条逐条单跑) | 全部红在具名用例:`the-friendly-hit-is-the-friendly-part` / `and-it-is-one-hit` / `columns/spec/t1-widths` / `the-friendly-sum-covers-the-unshown-actor` / `the-actor-line-is-verbatim` |
| 变异负控(全量) | **136 例 / 0 失败**(131 → 136) |
| `check_export_schema.py --selftest` | **61 例 / 0 失败**(新增:split 恒等式、`friendlyHits>hits`、非数字、1.7.12 缺 `config`、非布尔开关) |
| `check_contribution_layout.py`(--selftest) | 新副本对**当时的活目录**(59 份;游戏在跑,一直在增长)**0 违规**;`--selftest` 仍 PASS(旧 Fit 语义 414 处违规 → 新 0) |
| 冻结语料 schema | `check_export_schema.py` 35 份 **problems=0** |
| 离线核心 | `contrib.tests.test_samples` OK;`test_gate` **64/0**;`test_golden_155` OK;`contrib.validate` 0 ERROR;`contrib.crosscheck --selftest` OK |
| 真机数据(未修改的 1.7.11 导出) | 离线重算 `actors[6].friendly = 2,006,442` / `hostile = 1,439` / `direct = 2,007,881`,与文件里的 `friendly` **逐 key 相等**;`friendly+hostile==direct` 对全部 10 个 actor 成立 |
| 模拟 1.7.12 段落 | 把离线值写进 `contribution.actors[]` 并标 `version=1.7.12` 后 crosscheck:**0 mismatch / 0 omission**(WARNING 仅来自既有的 `ATKADD_COUNTER_GT_HITS`) |
| 文档收敛 / docs123 / 工具注册表 | 收敛 **12/12**;docs123 **94 文件 / 0 损坏**;工具注册表 **100 / 31 / 67 / 2 / 0**(pipeline 25) |
| `repo_manifest.py --write --exports batch_inputs\rf0` | 重新基线(`version=1.7.12 agree=True`、`dll=C1DBBD8F…`、tracked 1996、corpus 35);随后 `--verify` **drift=0** |
| `n0_acceptance --out acceptance_r60` | **44 条命令 / 76 条检查 / 0 项**(第一次跑只有 `repo_manifest_verify` 因 `DRIFT: version` 失败 —— 版本从 1.7.11 变成 1.7.12,刷新基线后复跑 0 项) |

## 3. 顺带修掉的一个既有缺陷

`contrib/crosscheck.py` 的 `totalDamage` 期望值一直是 **1.0 的公式**(`analyzable + unattributed`)。schema 1.1(1.7.8 起)
把 `totalDamage` 重新定义为 `analyzableDealt` 并把残差移进 `unattributedDamage`,于是**只要未归因池非空**,
这个旧公式就会把一个**正确**的 1.7.11 文件判成 MISMATCH/ERROR。实测 `battle_411001_20261005_142931`(未归因 3.885%)
在改动前就是这个状态。现在按**段内 `schemaVersion`** 选公式,与 `check_export_schema.py` 的判据一致。
冻结语料(rf0)里没有「1.1 段 + 非空未归因」的样本,所以这个缺陷此前没有被验收抓到 —— 是 R59 那次分析把它带出来的。

## 4. 部署与回退(已完成)

- **部署已完成**(替换前确认游戏进程未运行、`DpsMeter.dll` 未被占用):`C1DBBD8F…`(433,664 B,1.7.11)→
  **`F791F1CF…`**(434,688 B,1.7.12),替换后实测哈希与构建产物一致。
- 备份:`_dpsm_work/deploy-backup/pre-r60-C1DBBD8F/DpsMeter.dll`(旧 DLL,实测 `C1DBBD8F…`)。
- 两个**永不改动**的崩溃现场 `.bak` 替换前后哈希一致:`2E1819F2…`(1.0.48)/ `F7FF1EB8…`(1.0.49)。
- 部署后已跑 `repo_manifest.py --write --exports batch_inputs\rf0` 重新记录 `deployed_dll`,并同步 4 份现状文档的
  部署哈希前缀(R3)。
- 回退:`Copy-Item _dpsm_work/deploy-backup/pre-r60-C1DBBD8F/DpsMeter.dll BepInEx/plugins/DpsMeter/DpsMeter.dll -Force`。

## 5. 未做 / 注意

- **`config.filterFriendlyFire` 没有离线变异覆盖**:`ExportService.cs` 不在 BehaviorTests 的编译清单里(它依赖 Plugin/文件系统),
  这条与 R56 的「导出/悬浮窗身份无离线负控」是同一类限制,如实记在这里,不用一条咬不到的变异充数。
- **`schemaVersion` 仍是 1.1**:三个新字段是**附加且可选**的,1.0/1.1 的旧读者照旧能读;按项目惯例,附加字段不升版本
  (1.7.9 的 `givenFoldOn` 也是这么处理的)。
- **面板变宽是可见变化**:F5 的 Contribution 页从 780 → 880 px。这是「多一列」的必要代价;列宽本身没有缩。
- **旧导出永远没有这三个字段**:它们按 `legacy` 语义读(`actors[].friendly` 缺失 = 当时还没实现,不是 0)。
- **训练场仍是 `not_comparable`**:自伤列不改变准入;跨场比较仍按 R6。
- `perSecDamage` 仍然没有可扣的同队时间序列:逐秒口径要继续精确扣减,请回到 `events[]`(逐事件 `friendly`)
  —— 这一点已写进 `CONTRIBUTION-DATA-DICTIONARY.md` §1.4。