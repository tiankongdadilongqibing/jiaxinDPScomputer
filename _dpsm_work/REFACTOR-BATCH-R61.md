# 重构批次记录 R61:同队/自我伤害**移出归属池**(schemaVersion 1.2)

> 触发:你打完一场(1.7.12 已上线)后指出 —— 自伤其实是**敌方的治疗反转(回復反転)**,打自己并不造成实际贡献,
> 要求把它从贡献表的贡献比例里去掉。
> 插件版本 **1.7.13**、`contribution.schemaVersion` **1.2**;本轮只做构建,**替换 DLL 需游戏关闭**。

## 0. 结论

1. **归属池只算对敌命中**:`analyzableDealt`、逐角色 `directDamage`、`总贡献`、`命中` 从此不含同队/自我伤害。
2. **排除的量被计数,不是静默丢弃**:`damageLedger` 新增 `selfTeamHits` / `selfTeamDealt`,逐角色仍发布
   `friendly` / `friendlyHits`;两条 ledger 恒等式各加一项,`reconciliationGap` 同步。
3. **域内归属仍然是 100%**:`attributed == analyzableDealt`、`creditedShare == 1.0` 不变 —— 变的是**分母的定义**,
   不是归属算法。
4. **旧档不重算**:离线核心按**文件自己的 `schemaVersion`** 选模式(<=1.1 时同队命中原样在池内),
   所以冻结语料与全部历史导出的比对结论不变。

## 1. 你那一场的实测(同一份文件、两种契约)

`battle_9999_20261005_195024__B-20261005-115024-D88099F7C45D4078-001.json`(1.7.12,任务 9999):

| 口径 | analyzableDealt | 命中 | 同队(排除) | attributed | creditedShare | T.O.W.E.R.typeR 占比 |
|---|---|---|---|---|---|---|
| `schemaVersion 1.1`(旧) | 6,146,573 | 676 | 0(在池内) | 6,146,573 | 1.0 | **30.56%** |
| `schemaVersion 1.2`(新) | **4,222,552** | **646** | **1,924,021 / 30 击** | 4,222,552 | 1.0 | **0**(对敌输出为 0) |

同一批数据的前四名占比:**14.06 / 14.02 / 12.94 / 10.34% → 20.46 / 20.41 / 18.84 / 15.06%**。

    1.1-contract file: status=LEGACY_NOT_APPLICABLE mismatches=0 omissions=0
    1.1 mode: analyzable=6146573 hits=676 selfTeam=0/0
    1.2 mode: analyzable=4222552 hits=646 selfTeam=1924021/30 attributed=4222552.0 unattr=0.0

## 2. 契约与改动

| 位置 | 改动 |
|---|---|
| `check_export_schema.py` | 1.2 的 ledger 两个新键、两条恒等式与 actor 的 `friendly`/`friendlyHits`;1.0/1.1 的检查全部版本门控保留;自测 61 → 66 |
| `src/Output/Contribution.cs` | `Compute` 里同队命中在解析出 attacker 后**立即 continue**(只累计 `SelfTeam*` 与 `friendly/friendlyHits`);schemaVersion 1.2;ledger 增键;actor 去掉 `hostileDamage` |
| `src/Ui/OverlayUGUI.Rows.cs` | 口径两行改为「自伤是敌方治疗反转,已从贡献排除、不归属任何角色」「总贡献相加 = 可分析伤害(只含对敌命中)」 |
| 离线 `contrib/aggregate.py` | `contract_includes_same_team(export)` + `analyze(..., include_friendly=None)`:按文件契约选模式;排除模式下同队命中不进池、只计数 |
| 离线 `contrib/crosscheck.py` | 版本门控:1.7.13+ 比 `friendly`/`friendlyHits`,1.7.12 才比 `hostileDamage` |
| 离线 `contrib/report_json.py` | 1.2 分析的 ledger 增 `selfTeam*` 并计入 gap;`totals` 增 `selfTeamHits/selfTeamDealt/sameTeamInPool` |
| 离线 `contrib/validate.py` | I2b 按模式选恒等式(`friendly+hostile==direct` 或 `hostile==direct`) |
| `contrib/model.py` | `Analysis` 增 `self_team_*` 与 `same_team_in_pool` |
| 测试 | `extract/friendly-split` 改写为 1.2 语义(直接伤害=对敌、被排除量仍发布、ledger 计数、域内归属仍 100%);950→**963** 用例 |
| 变异负控 | 新增 `same-team-reenters-the-pool`(去掉 `continue` → 同队命中重新进池)并修 2 条指向旧行的变异;136→**137** |

## 3. 验证

| 闸门 | 结果 |
|---|---|
| `dotnet build src/DpsMeter.csproj -c Release` | **0 警告 0 错误**;产物 **435,200 B** / `32BFEC3B…` |
| BehaviorTests | **963 用例 / 99 组 / 0 失败** |
| 变异负控(新增/受影响 3 条单跑) | 全部红在具名用例(`direct-is-the-enemy-hit-only` / `the-session-ledger-counts-the-same-team-amount` / `and-it-is-one-hit`) |
| `check_export_schema --selftest` | **66 例 / 0 失败**(新增 5 例 1.2:接受、缺 `selfTeamDealt`、三桶不平衡、命中桶不平衡、actor 缺 `friendly`) |
| 离线核心 | `test_samples` OK、`test_gate` 64/0、`test_golden_155` OK、`crosscheck --selftest` OK |
| 真实数据(1.1 契约) | 那份 1.7.12 导出 crosscheck **0 mismatch / 0 omission**(证明旧契约没被改坏) |
| 真实数据(1.2 契约) | 同一份文件按 1.2 重算:池 4,222,552、排除 1,924,021/30、`attributed == analyzable` |
| 变异负控(全量) | **137 例 / 0 失败** |
| `recon_probe` | **ALL CHECKS PASSED**(两处 schema 身份断言已同步到 1.2) |
| 文档收敛 / docs123 / 验收 | 收敛 **12/12**;docs123 **95 文件 / 0 损坏**;`n0_acceptance --out acceptance_r61` = **44 条命令 / 76 条检查 / 0 项** |

## 4. 部署与回退(已完成)

- **部署已完成**(替换前确认游戏未运行、`DpsMeter.dll` 未锁定):`F791F1CF…`(434,688 B,1.7.12)→
  **`32BFEC3B…`**(435,200 B,1.7.13),替换后实测哈希与构建产物一致。
- 备份:`_dpsm_work/deploy-backup/pre-r61-F791F1CF/DpsMeter.dll`(旧 DLL,实测 `F791F1CF…`)。
- 两个**永不改动**的崩溃现场 `.bak` 替换前后哈希一致:`2E1819F2…` / `F7FF1EB8…`。
- 部署后 `repo_manifest.py --write --exports batch_inputs\rf0` 重新记录 `deployed_dll`,并同步 4 份现状文档的
  哈希前缀(R3 = `32BFEC3B`)。
- 回退:`Copy-Item _dpsm_work/deploy-backup/pre-r61-F791F1CF/DpsMeter.dll BepInEx/plugins/DpsMeter/DpsMeter.dll -Force`。

## 5. 未做 / 注意

- **不改游戏口径**:`totals.dealt` / `actors[].dealt` / `perSecDamage` 照旧含同队伤害(游戏自己的战报也算它),
  逐秒口径要继续精确扣减仍要回 `events[]`(逐事件 `friendly`)。
- **1.2 的 JSON 新形状没有在 BehaviorTests 里逐字段跑**(`ExportService`/`AppendJson` 不在编译清单里),
  由 `check_export_schema` 的 1.2 用例与离线 crosscheck 覆盖,与 R60 的限制同类。
- **跨契约比较要看版本**:1.1 与 1.2 的文件在含同队伤害的场次上 `analyzableDealt`/占比是不同口径;
  任务 411001 的样本同队伤害为 0,数字不变。
- 训练场仍 `not_comparable`(R6),这条改动不改变准入。
