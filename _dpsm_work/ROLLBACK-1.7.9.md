# 回退预案:1.7.8 → 1.7.9(开关耦合修正 + 计数器单位修正 + 可核对性)

> **已被 `ROLLBACK-1.7.10.md` 取代**(1.7.10 是补丁级发布,回退锚点改为 `.1.7.9.bak`)。本文件保留为 1.7.9 那一步的记录。

## 前向变更

| 项 | 变更 | 文件 | 风险 |
|---|---|---|---|
| **开关耦合** | `General/GivenTalent=false` 时,被调函数 `GivenTalentDamage` 仍会登记 `kind="given"` 折叠步并按条目计入 `applied`/`GivenApplied`,而调用方不把因子乘进 `vicMod` ⇒ 导出的 fold 列表与 `takenMult` 自相矛盾。修法:开关在调用前读一次,作为 `applyToChain` 参数传下去,四个副作用同进同出;新增纯判定 `GivenFoldApplies(enabled, gv)` | `src/Composition/CompositionProbe.Talents.cs`、`src/Composition/CompositionProbe.Chain.cs`、`src/Model/CalcBreakdown.cs` | **开关打开(现网默认)时行为逐字节不变**;关闭时的导出自此不再自相矛盾 |
| **逐击可见性** | 新增逐击字段 `calc.givenFoldOn`(CalcReconcile 与 Forensics 两处都写),让任何一份导出自证用的是哪套理论 | `src/Output/CalcReconcile.cs`、`src/Diagnostics/Forensics.cs` | 新增字段;`check_export_schema` 已列入 `CALC_OPTIONAL`(按 bool 校验) |
| **计数器单位** | `giveApplied` / `rosterAudit.giveApplied` 一直是「条目数 + 击数」(静态量被两个单位各加一次,1.3.5–1.7.8)。修法:调用方改加新静态 `GivenFoldHits`,`giveApplied` 恢复为纯条目计数;两者都导出(`giveFoldHits` 在 `rosterAudit` 里,不是根) | 上述 Talents/Chain + `src/Output/ExportService.cs` | **强制的正收益**:旧值是任何量都不等于的混合值;若下游有脚本依赖旧值,它依赖的本就是错的量 |
| **日志可核对性** | `[UI-DIAG]` 增加 `unattrRow`(含 `未归因`/`未归属`/不可用的那一行) | `src/Ui/OverlayUGUI.Pool.cs` | 仅多一个日志字段,不改渲染 |
| 闸门增强 | `contribution_gate.give_section_reasons`(两条恒等式,≥1.7.9 要求 `giveFoldHits`);`check_export_schema` 增 `eventSumAll == totals.taken` 与 `taken == dealt+unattributedDamage`;新 `check_given_fold_coupling.py`、`check_live_log.py` | `contribution_gate.py`、`contrib/crosscheck.py`、`check_export_schema.py`、2 个新脚本 | 闸门变严;历史档只在 `GIVE_APPLIED_LEGACY_MIXED` 出 NOTE,**不染红** |

## 回退锚点

- 回退目标:`DpsMeter.dll.1.7.8.bak`(部署前从现网备份)。
- 1.7.8 现网 DLL:386,048 B / SHA256 `0B33983646280E246AA9E5911AEAB0EC499FC22C6C796460299BD4BDC4B34880`。
- 更早链:`.1.7.7.bak` = `7425139C…`(381,952 B)、`.1.7.6.bak` = `BB96DA65…`(379,904 B)、`.1.7.5.bak` = `64EDA364…`、`.1.7.4.bak`、`.1.7.3.bak`、`.1.7.2.bak`、`.1.7.0.bak`。
- ⚠ 绝不回退 `DpsMeter-1.0.48-crash.bak` / `DpsMeter-1.0.49-crash.bak`。
- **历史导出不受影响**:1.7.9 只新增两个字段(逐击 `calc.givenFoldOn`、`rosterAudit.giveFoldHits`),没有任何既有字段改名或改义;`giveApplied` 只有在**新**导出里才是纯条目计数。

## 回退步骤

1. 关闭游戏(或按 §7.2.9x 的热替换法:改名旧文件再复制)。
2. `Copy-Item BepInEx\plugins\DpsMeter\DpsMeter.dll.1.7.8.bak BepInEx\plugins\DpsMeter\DpsMeter.dll -Force`。
3. `(Get-FileHash ...\DpsMeter.dll -Algorithm SHA256).Hash` 必须等于 `0B33983646280E246AA9E5911AEAB0EC499FC22C6C796460299BD4BDC4B34880`。
4. 重启游戏,确认日志 `Loading [DpsMeter 1.7.8]` 与导出 `version = "1.7.8"`。
5. 离线侧若也要回退:`contribution_gate.give_section_reasons` 与 `check_given_fold_coupling.py` 的行为对 1.7.8 导出**本就只出 NOTE/不适用**,不回退也不会误报;但 `check_live_log.py` 需要 1.7.9 的心跳才有 `unattrRow`,回退后它会持续报 LEGACY(带原因),这是正确行为。

## 成功判据(全部满足才算上线成功)

1. 构建 0 警 0 错;`BuildInfo.Version == csproj <Version> == 1.7.9`。
2. `check_export_schema.py --selftest` → **cases=52 failed=0**;main → problems=0。
3. `contrib.tests.test_gate` → **59 passed, 0 failed**。
4. `check_given_fold_coupling.py` → **17/17**;`--selftest` → **6/6 mutations caught**。
5. `check_live_log.py --selftest` → **PASS**;对 1.7.8 的旧日志 → `LEGACY_NOT_APPLICABLE` **并打印原因**(不是静默 PASS)。
6. `pairtrusted_impact.py --selftest` → **11/11**(T3 已限定非训练场,新增 T3c/T4c)。
7. 其余守卫全 exit 0:`check_contribution_layout.py`(--selftest 仍 `198 / 0`)、`v150_validate.py`、`check_fact_signature.py`、`check_docs_123.py`、`refactor_final_check.py`、`test_samples`、`test_golden_155`、`contribution_applicability.py --selftest`(18/18)、`contrib.phase_e_dryrun`、`contrib.validate`、`contrib.crosscheck --selftest`。
8. `contrib.crosscheck --batch`(29 份)= `{ERROR:1, LEGACY_NOT_APPLICABLE:15, PASS:10, WARNING:3}` —— 唯一 ERROR 是已知的 1.6.0(factor 4 位截断)。
9. 部署后 DLL:386,560 B / `F3F73C81…`;`SOURCE==DEPLOYED: True`;UTF-16 含 `givenFoldOn`/`giveFoldHits`/`unattrRow`,UTF-8 含 `GivenFoldApplies`/`applyToChain`,且 DLL 内**不存在** `1.7.8` 字面量。
10. 实机:下一场战斗的导出出现 `calc.givenFoldOn` 与 `rosterAudit.giveFoldHits`;`giveApplied` 等于该场 `kind="given"` 折叠步数;日志 `Loading [DpsMeter 1.7.9]`,且 `[UI-DIAG]` 行带 `unattrRow` ⇒ `check_live_log.py` 由 LEGACY 转为实检并通过。

## 失败判据(任一即回退)

- 开关打开的普通战斗出现 `calc.givenFoldOn=false`(说明开关读取被改坏)。
- `giveApplied != kind="given" 折叠步数`(新导出),或 `giveFoldHits != 带该步的击数`。
- `damageLedger.eventSumAll != totals.taken`,或 `totals.taken != totals.dealt + totals.unattributedDamage`。
- `prod(calc.fold)` 与 `dealtMult × takenMult` 的相对差超过 5e-3 且无法用三位小数序列化解释。
- 旧档 crosscheck 由 OK 变 MISMATCH;或 `source.pluginVersion`/导出 `version` 不是 1.7.9。
- `[UI-DIAG]` 缺 `unattrRow`,或 F5/F6 数值与导出不一致。

## 纪律

1. **不改任何既有字段的含义**:1.7.9 只加字段;`giveApplied` 的修正属于「旧值不是任何量」的修正,已在 `CONTRIBUTION-DATA-DICTIONARY.md` §12.5 记录原因与实测证据(29 份导出,28/28 非零文件精确)。
2. **历史导出与备份一律不动**:闸门对新旧文件的差异一律用 NOTE/LEGACY 表达,不用历史档做失败证据。
3. 回退后必须复核 `Loading [DpsMeter 1.7.8]`,并确认 `check_live_log.py` 回到 LEGACY(带原因)而不是报错。