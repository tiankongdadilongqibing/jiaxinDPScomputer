# 回退预案:1.7.7 → 1.7.8(贡献契约 P0-B + 验证闸门 P0-A + UI P2-A 集成)

## 前向变更

| 任务 | 变更 | 文件 | 风险 |
|---|---|---|---|
| **P0-B** | 贡献 schema → **1.1**:`totalDamage = analyzableDealt`(旧 = analyzable+unattributed,双计未归因);新增 `producer`、`coverage.excludedDamage`/`analysisDamageCoverage`/`creditCoverageWithinAnalyzed`/`overallAttributedCoverage`、`damageLedger`(totalsDealt/events/analyzableHits/outsideTeamHits/outsideTeamDealt/unknownAttackerHits/unknownAttackerDealt/eventSumAll/reconciliationGap) | `src/Output/Contribution.cs`、`src/Output/ContributionSession.cs`、`contrib/report_json.py`、`contrib/report_text.py`、`check_export_schema.py`、`recon_probe/Program.cs` | 字段名不变 ⇒ 1.0 读者仍可读;`totalDamage` **含义**变化 |
| P0-A | 验证闸门状态机与退出码(crosscheck/validate/golden/fact_signature + 新 contribution_gate) | contrib/*.py、check_fact_signature.py | 闸门变严,可能让旧档由「绿」变「不适用」 |
| P2-A | F5 上一场摘要传递未归属、Invalidate 时机等 | src/Ui/*、Model/BattleSession.cs | UI 显示 |
| P1-A | atkadd self 判定从名字改 actor key | src/Model/AtkAddFold.cs、CompositionProbe.Chain.cs | 归属语义(语料无同名 ⇒ 数字应逐字节不变) |

## 回退锚点

- 回退目标:`DpsMeter.dll.1.7.7.bak`(部署前从现网备份)。
- 1.7.7 现网 DLL:381,952 B / SHA256 `7425139C36527D9AFD06433D69DFBBB41C3082313150231910CD3E33B106D2CE`。
- 更早链:`.1.7.6.bak` = `BB96DA65…`(379,904 B)、`.1.7.5.bak` = `64EDA364…`、`.1.7.4.bak`、`.1.7.3.bak`、`.1.7.2.bak`、`.1.7.0.bak`。
- ⚠ 绝不回退 `DpsMeter-1.0.48-crash.bak` / `DpsMeter-1.0.49-crash.bak`。
- **历史导出不受影响**:1.0 文件的字段一个都没改;离线工具同时支持 1.0 与 1.1。

## 回退步骤

1. 关闭游戏。
2. `Copy-Item BepInEx\plugins\DpsMeter\DpsMeter.dll.1.7.7.bak BepInEx\plugins\DpsMeter\DpsMeter.dll -Force`。
3. `(Get-FileHash ...\DpsMeter.dll -Algorithm SHA256).Hash` 必须等于 `7425139C36527D9AFD06433D69DFBBB41C3082313150231910CD3E33B106D2CE`。
4. 重启游戏,确认导出 `source.pluginVersion = 1.7.7`、`contribution.schemaVersion = "1.0"`。
5. 离线侧若也要回退:`git`/快照恢复 `contrib/report_json.py`、`report_text.py`、`check_export_schema.py`(1.0 判据仍在,只是 1.1 分支不再需要)。

## 成功判据(全部满足才算上线成功)

1. 构建 0 警 0 错;`BuildInfo.Version == csproj <Version> == 1.7.8`。
2. `check_export_schema.py --selftest` → **cases=45 failed=0**;main → problems=0。
3. `recon_probe` → **ALL CHECKS PASSED**(含 P0-B 台账断言、P1-A key 断言)。
4. 其余守卫全 exit 0:`check_contribution_layout.py`(--selftest 仍 `164 / 0`)、`v150_validate.py`、`check_fact_signature.py`、`check_docs_123.py`、`refactor_final_check.py`、`contrib.tests.test_samples`、`contrib.tests.test_golden_155`。
5. `contrib.crosscheck`(旧档)= OK / mismatches=0 —— **旧档的字段语义没变,所以必须仍然一致**。
6. 部署后 DLL 哈希/字节数已记录;`SOURCE==DEPLOYED: True`。
7. 实机(用户):下一场正常战斗的导出出现 `contribution.schemaVersion = "1.1"` 且 `damageLedger.totalsDealt` 与 `totals.dealt` 相等;F5 页列对齐不变。

## 失败判据(任一即回退)

- 任何导出出现 `contribution.schemaVersion` 仍是 1.0 但带 `damageLedger`(半新半旧)。
- `totals.dealt − (analyzableDealt + outsideTeamDealt)` 超过 0.001%(说明台账把桶算错)。
- 旧档 crosscheck 由 OK 变 MISMATCH。
- F5 页出现新错位/异常,或 `source.pluginVersion != 1.7.8`。

## 纪律

- 部署前必须游戏已关闭;覆盖前先备份现网 DLL。
- 部署后立即核对字节数与 SHA256,并写入 `SESSION-STATE` §7.2.97 / `HANDOFF` / 索引。
