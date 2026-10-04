# 回退预案:1.7.6 → 1.7.7(贡献表对齐打包)

## 前向变更(1.7.7)

| # | 变更 | 文件 | 类型 |
|---|---|---|---|
| 1 | `Fit` 改为按**显示列**截断,截断标记改 ASCII `..`(U+2026 在不同字体可能是 1 或 2 列) | `Ui/OverlayUGUI.Rows.cs` | 修复用户报告的列错位 |
| 2 | 所有 `Fit` 调用点改传**真实列宽**(16/22/14),使魔 `*` 移入 `Fit` 内 | 同上(T1/T2/T3 + roster 块) | 同上 |
| 3 | 新增 `Cell()`:表内自由文本把 `×`(U+00D7)规范成 ASCII `x`(1,119 个名称里唯一的宽度可疑字符) | 同上 | 保证列宽可证明 |
| 4 | 表3 表头删掉从未被填的「主要规则」列(表头 63 → 53 列) | 同上 | 显示一致性 |
| 5 | 合计行改为与表头**同一列几何**(85 列),并填入三列真实合计;未归因移到单独一行 | 同上 | 显示一致性 |
| 6 | 金额/百分比统一走 `Fmt`/`Pct`(`N0`/`F2` + InvariantCulture),F5 与 roster 同格式 | 同上 | 消除 F5 截断 vs roster 四舍五入 |
| 7 | 字体失败不再重置 `_fontAttempted/_monoAttempted`(3 秒节流恢复),失败只告警一次 | `Ui/OverlayUGUI.Pool.cs`、`OverlayUGUI.cs`(新增 `_fontWarned/_monoWarned`) | 修 S4 发现的每帧重试+日志刷屏 |
| 8 | `knownLimits` 补/正**六处**(可分析伤害只覆盖 team1、crit 有观测但模型不记账、召唤物单列、独立取整、`totals.dealt` 与逐事件差 ~0.05%、队友加算归 `atkadd`),与离线核心 `contrib/report_json.py` 的列表**逐字节一致** | `Output/Contribution.cs`、`contrib/report_json.py` | 口径文本 |
| 9 | 版本 1.7.6 → 1.7.7 | `BuildInfo.cs`、`DpsMeter.csproj` | 版本 |

**不包含**(留给后续):F5「上一场」可能指向更早一场(`ContributionSession.Invalidate()` 无调用者)、`PairTrusted` 未纳入归属谓词、700817 的 59 击倍率未进 fold、`GivenTalent=false` 的登记未乘。

## 回退锚点

- 回退目标:`DpsMeter.dll.1.7.6.bak`(部署前从现网 DLL 备份)。
- 1.7.6 现网 DLL:SHA256 `BB96DA65E9A824E8227B36325E34CC0F61ADAD7949D0017BEFA09997A314A9F8`,379,904 字节。
- 更早链:`.1.7.5.bak` `64EDA364851DAC8E19423B7754CADBC31A8624835745FBB29D056154CA0E8EAF` / `.1.7.4.bak` / `.1.7.3.bak` / `.1.7.2.bak` / `.1.7.0.bak`。
- ⚠ 绝不回退 `DpsMeter-1.0.48-crash.bak` / `DpsMeter-1.0.49-crash.bak`。

## 回退步骤

1. 关闭游戏(DLL 被加载时无法覆盖)。
2. `Copy-Item BepInEx\plugins\DpsMeter\DpsMeter.dll.1.7.6.bak BepInEx\plugins\DpsMeter\DpsMeter.dll -Force`。
3. `(Get-FileHash ... \DpsMeter.dll -Algorithm SHA256).Hash` 必须等于 `BB96DA65…`。
4. 重启游戏,确认导出 `source.pluginVersion` 回到 `1.7.6`。

## 成功判据(全部满足才算上线成功)

1. 构建 0 警 0 错;`BuildInfo.Version == csproj <Version> == 1.7.7`(`refactor_final_check.py`)。
2. `python check_contribution_layout.py` → `bad=0`;`--selftest` → `PASS`(1.7.6 语义必须报错,否则守卫无牙)。
3. 既有守卫全过:`check_export_schema.py --selftest`(32/32)、`v150_validate.py` problems=0、`check_fact_signature.py`、`check_docs_123.py`、`refactor_final_check.py`、`contrib.tests.test_samples`、`contrib.tests.test_golden_155`、`python -m contrib.crosscheck`(最新一场 OK)。
4. 部署后 DLL 字节数与 SHA256 已记录;`source.pluginVersion` = 1.7.7。
5. 实机(用户):F5 页面在含 `エヴァラス・フラウ` / `生ける炎 クトゥグア` / `ネーフェ＝ジアー` 的场次上,`总贡献…命中` 与表头对齐;日志出现 `monospaced table font created OK`。

## 失败判据(任一即回退)

- F5 页出现新错位、异常或空白;或列数与之前明显不同(合计行除外,它按设计变窄到 85 列)。
- 日志出现新增异常/刷屏;或 `source.pluginVersion` 不是 1.7.7。
- 任一守卫转红。

## 纪律

- 部署时必须游戏已关闭;覆盖前先备份现网 DLL。
- 部署后立即核对字节数与 SHA256,并写入 `SESSION-STATE` / `HANDOFF` / 索引。
