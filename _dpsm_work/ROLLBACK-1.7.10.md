# 回退预案:1.7.9 → 1.7.10(闸门伪运行修复 + `[COMP]` 诊断开关对齐)

> **已被 `ROLLBACK-1.7.11.md` 取代**(1.7.11 是显示级发布,回退锚点改为 `.1.7.10.bak`)。本文件保留为 1.7.10 那一步的记录。

这是**补丁级**发布:不改任何导出字段、不改贡献公式、不改 KPI,只修一个「从不运行的闸门」和一个日志口径。

## 前向变更

| 项 | 变更 | 文件 | 风险 |
|---|---|---|---|
| **闸门伪运行(库 + 接线)** | `give_section_reasons` 原先接收**根**导出字典,而 `giveApplied`/`giveFoldHits` 写在 **`rosterAudit`** 内 ⇒ 查表永远 None、整条检查退化成一条 NOTE。改为传 `rosterAudit`;≥1.7.9 缺 `giveApplied`/`giveFoldHits` 一律 `DATA_MISSING`,不再静默跳过;`giveFoldHits` 改用「given 因子乘积 ≠ 1 的击数」并在有丢步时只作下界 | `contribution_gate.py`、`contrib/crosscheck.py` | **只影响验证**,不改插件行为;修好后对历史档仍是 NOTE(不染红) |
| **真实文件负控** | `test_gate` CASE3 五例:干净文件无 GIVE 错误、篡改 `giveApplied`→ERROR、1.7.9 缺字段→DATA_MISSING exit 3、`giveFoldHits` 错→ERROR、正确值→无错误 | `contrib/tests/test_gate.py` | 59→64 例 |
| **[COMP] 重算开关对齐** | `CompositionProbe.Diagnostics.cs` 的「已识别倍率」重算:given 改走 `GivenFoldApplies` + 下传开关;madness 看 `Plugin.CfgMadness`;并补上**受击方狂気 ×1.5**(按 `Plugin.CfgMadnessVictim`,用 chain 同两个助手读取) | `src/Composition/CompositionProbe.Diagnostics.cs` | **日志文本**变化(`measure:false` + 本地 `dctx`,**不改导出数据**);使日志与 `CalcBreakdown.KnownMult` 的口径一致 |
| 注释/文档数字 | 两处 C# 注释与多处文档:28/28(非 27/27)、`giveFoldHits` 在 `rosterAudit`(非根) | 多处 | 无行为影响 |
| 守卫 | `check_given_fold_coupling.py` 增 7a/7b/7c + 3 变异(20 项 / 自测 9/9) | `check_given_fold_coupling.py` | 仅更严 |

## 回退锚点

- 回退目标:`DpsMeter.dll.1.7.9.bak`(部署前从现网备份)。
- 1.7.9 现网 DLL:386,560 B / SHA256 `F3F73C81FF3D9CE0B903E5B4BB9C3F60062BF20F93118A853DBB9DCC34E7DBCB`。
- 更早链:`.1.7.8.bak` = `0B339836…`(386,048 B)、`.1.7.7.bak` = `7425139C…`(381,952 B)、`.1.7.6.bak` = `BB96DA65…`。
- ⚠ 绝不回退 `DpsMeter-1.0.48-crash.bak` / `DpsMeter-1.0.49-crash.bak`。
- **数据不受影响**:1.7.10 没有新增/改名任何导出字段;1.7.9 的导出仍完全可读,回退也不需要重写任何文件。

## 回退步骤

1. 关闭游戏(或热替换:改名旧文件再复制)。
2. `Copy-Item BepInEx\plugins\DpsMeter\DpsMeter.dll.1.7.9.bak BepInEx\plugins\DpsMeter\DpsMeter.dll -Force`。
3. `(Get-FileHash ...\DpsMeter.dll -Algorithm SHA256).Hash` 必须等于 `F3F73C81FF3D9CE0B903E5B4BB9C3F60062BF20F93118A853DBB9DCC34E7DBCB`。
4. 重启游戏,确认日志 `Loading [DpsMeter 1.7.9]` 与导出 `version = "1.7.9"`。
5. 回退插件**不需要**回退闸门:1.7.10 的闸门修复对 1.7.9 导出同样正确(它们本就是 LEGACY_NOTE),
   而 `check_live_log.py` 本就要求 1.7.9+ 的心跳。

## 成功判据(全部满足才算上线成功)

1. 构建 0 警 0 错;`BuildInfo.Version == csproj <Version> == 1.7.10`。
2. `contrib.tests.test_gate` → **64 passed, 0 failed**(含 CASE3 的真实文件负控)。
3. `check_given_fold_coupling.py` → **20/20**;`--selftest` → **9/9**。
4. `check_doc_convergence.py` → **8/8**;`--selftest` → **10/10**;`check_docs_123.py` → exit 0。
5. `check_live_log.py --selftest` → 10 例 PASS;主跑对 1.7.8 旧日志 → LEGACY(带原因)。
6. `check_export_schema.py --selftest` → **52 failed=0**;其余闸门全 exit 0。
7. `contrib.crosscheck --batch`(29 份)= `{ERROR:1, LEGACY_NOT_APPLICABLE:15, PASS:10, WARNING:3}`(唯一 ERROR 为已知 1.6.0)。
8. `recon_probe` → **ALL CHECKS PASSED**。
9. 部署后 DLL:386,560 B / `BF2F174A…`;`SOURCE==DEPLOYED: True`;DLL 内**不存在** `1.7.9` 字面量。

## 失败判据(任一即回退)

- 任一真实导出的 `rosterAudit.giveApplied` 被报 ERROR(说明「条目 + 击数」的历史档被我误当当前档)。
- 1.7.9+ 导出缺 `giveFoldHits` 却不报 `DATA_MISSING`(说明接线又断了)。
- 历史档 crosscheck 由 OK 变 MISMATCH,或 `crosscheck --batch` 的 PASS/WARNING 计数变化(除已知 1.6.0 外)。
- 导出出现任何新字段或字段改名(本版不应有)。

## 纪律

1. **闸门必须能证明自己在跑**:任何新增检查都要有一个「篡改真实文件 → 必须红灯」的用例;
   只测扁平字典的检查会在接线上无声失效(本版就是这条教训的另一半)。
2. **历史档不染红**:新检查对旧版本只出 NOTE/LEGACY,并打印原因。
3. 回退后必须复核 `Loading [DpsMeter 1.7.9]`,并确认 64 例 `test_gate` 仍全过(闸门与插件版本无关)。