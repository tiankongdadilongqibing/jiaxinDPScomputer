# 重构批次记录 R55:提取默认开启 + 「阻挡增伤」待确认表(与角色贡献同款列)

> 你的两点要求:①F4 生成证据包和"用面板"本来就没关系,直接在后台默认开启就行;
> ②「阻挡增伤」的显示能不能做成和角色贡献一样的显示效果。
> 插件版本 **1.7.11**;本轮部署带 `390C1340…`(423,424 字节)。

## 0. 结论

1. **提取不再需要按键**:`General/ExtractOnBattleEnd` 的**插件默认值**改为 `true`(R54 只改了你自己那份 cfg)。
   保留上限仍是 `ExtractKeep`=5,所以磁盘上限 ≈ 5 包 × 25 MB;不想写就把它设回 `false`,F4 仍可随时补一包。
2. **「阻挡增伤」现在用角色贡献表自己的列打印**:新增 `【阻挡增伤·待确认】` 小表 —— 与 T1 **同一套列宽**
   (「角色」→「候选角色」、「命中」→「折叠」),逐**候选持有者**一行给出待确认金额 / 占比 / 折叠,
   再加一行 `待确认合计`。**它仍然是未归因**:放在未归因理由行之后、角色表 `合计` 之外,不参与任何求和;
   不适用的信用列打印 `-` 而不是 `0`(实测 0 与"没记账"是两句不同的话)。
3. 本轮**没有移动任何信用**;没有改导出 schema;没有新增任何 `.py` 工具。

## 1. 改动清单

| 位置 | 改动 |
|---|---|
| `src/Plugin.cs` | `ExtractOnBattleEnd` 默认 `false → true`;配置说明与 XML 注释同步改写(不再写"默认关") |
| `src/Ui/ContributionRowModel.cs` | 新增 `ContributionPendingValues` / `ContributionPendingTable` / `BuildPending(res,total)`:只取**有 carrierVerdict 的**普查组(即 given 通道),按 (判定, 名字) 聚合,金额降序 + 名字序输出 |
| `src/Ui/ContributionColumns.cs` | 新增 `T1PendingSpec()`(列宽在**调用时从 T1 复制**,只改两个标签)/ `T1PendingRow` / `T1PendingTotalsLine` / `PendingDash` |
| `src/Ui/FallbackText.cs` | 新增 `PendingHeaderLine`(家族名 + 规则标签 + "未计入任何角色")与 `PendingNoteLine`(候选来源是**推断**;歧义时列出候选名) |
| `src/Ui/OverlayUGUI.Rows.cs` | 新增 `AppendPendingRows`,F5 页与实时总贡献**两处同一实现**调用(未归因理由行之后) |
| `tests/BehaviorTests/Cases.Extraction.cs` | 新增 `extract/pending` 组(**55 例**):过滤、聚合、歧义计数、排序、列宽/行宽、破折号、两个 caption、信用不动 |
| `tests/BehaviorTests/Program.cs` | 钉住用例数 811 → **866** |
| `tests/negative_control.py` | 新增 **7 例**变异(过滤 / 聚合 / 歧义命名 / 排序 / 注释 / 标签 / 破折号),逐例确认红在**具名**用例;全量 **121 例** |
| `check_docs_123.py` | 把本记录加入 FILES |

## 2. 验证

| 闸门 | 结果 |
|---|---|
| `dotnet build src/DpsMeter.csproj -c Release` | **0 警告 0 错误** |
| BehaviorTests | **866 用例 / 0 失败** |
| 变异负控(新增 7 例单独跑) | 7/7 **红在具名用例** |
| 验收 `n0_acceptance.py --out acceptance_r55` | **0 项**(42 条命令 / 74 条检查),档案记录部署 = 390C1340… |
| 文档收敛 / docs123 / 工具注册表 / 清单 | 12/12、0 损坏、99/30/67/2/0、drift=0 |

## 3. 部署与回退

- 替换前确认 DLL **未被占用**(游戏未运行);备份上一版整个包到
  `_dpsm_work/deploy-backup/pre-r55-76CEAC00/DpsMeter.dll`。
- 部署:**76CEAC00**(420,352 B) → **390C1340**(423,424 B)。
- 复核两个**永不改动**的崩溃现场哈希不变:`2E1819F2…`(1.0.48-crash)/ `F7FF1EB8…`(1.0.49-crash)。
- 回退:`Copy-Item _dpsm_work/deploy-backup/pre-r55-76CEAC00/DpsMeter.dll BepInEx/plugins/DpsMeter/DpsMeter.dll -Force`。

## 3b. 实机第一次跑通证据包(R54 遗留的最后一环)

2026-10-05 你打完那一场后,**不需要按任何键**就出现了第一个真实证据包:

    BepInEx/plugins/DpsMeter/extract/extract_20261005_143055_q411001_battle-end/
      battle.json          24,243,145 B
      contrib_census.json       2,196 B
      masterdata/           20 份
      manifest.json             1,242 B

`extract_verify.py --bundle <该目录>` 判定 **PASS**:

- manifest:`extract-manifest/1` / `fnv1a64` / `reason=battle-end` / `plugin=1.7.11` / `quest=411001` / `inputSource=live`;
- 列出文件 **2/2** 校验通过;masterdata 声明 20、盘上 20、失败 0;
- 普查 **4 组 / 35,271 折叠**,与用同一份 `battle.json` 的**独立重算逐字段一致**;
- 覆盖率:`analyzable=189,797,226`,未归因 **7,373,676.891**(= 该场 given 通道的全部),`creditedShare=0.961`;
- assembly 行记录的是产出该包的 R54 构建(420,352 B / `12d09d63…`)。

原始转写留在 `_dpsm_work/extract_verify_report.txt`(工具默认写到工作目录,ASCII),证据包本体仍在
`BepInEx/plugins/DpsMeter/extract/`(24 MB,不入库)。

**这一场的未归因 100% 是「阻挡增伤」**:4 组都是 `given#N/1006/-10`、label `被伤害+10%(赋予)`、因子 1.1,
`carrierVerdict=unique`、候选**同名唯一** = `エヴァラス・フラウ`(共 5,368 折叠 / 7,373,676.891)。
这也正是本轮把那一段改成"和角色贡献同款列"的动机:唯一候选时,读者要看到的是**哪个角色**、**多少钱**。

## 4. 未做 / 注意

- **没有把候选变成归属**。`given_carrier_one`(唯一候选)仍**不移动信用**:名册持有者是**推断**、不是实测;
  把这 7,373,676.891 记过去会改变角色排名,那需要单独批准,并同步导出 schema、离线核心与守卫。
- 面板上"待确认"行与角色表的行**长得一样是故意的**,所以每处都写了"未计入任何角色"、不适用的列打 `-`,
  且它**不进**角色表 `合计`。这个相似性是显示效果,不是归属声明。
- 该包由 R54 构建产出;换成 R55 构建后,包内 assembly 行仍记录 R54 的那一份(这是"记录产出者"的本意)。
