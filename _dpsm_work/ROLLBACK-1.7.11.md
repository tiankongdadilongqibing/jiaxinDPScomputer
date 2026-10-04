# 回退预案:1.7.10 → 1.7.11(F5 输出分组)

这是**显示级**发布:不改任何导出字段、不改贡献公式、不改任何数值口径,只改 F5/F6 的**列分组与文案**,
外加两条**新的逐角色强制恒等式**(纯验证)和两处守卫自身的缺陷修复。

## 前向变更

| 项 | 变更 | 文件 | 风险 |
|---|---|---|---|
| **F5 表 1 分组** | 三列 `基础(11) \| 自身规则(11) \| 辅助(11)` 改为 `自身(11) \| 他人因你(11) \| 被队友分走(11)`,`自身 = baseCredit + selfRuleCredit`;仍是 **85 列**同几何。`receivedAssist` 首次进表 | `src/Ui/OverlayUGUI.Rows.cs`(F5 表 1) | **只影响显示**;数值、字段、schema 全不变 |
| **F6 明细块** | 第二行改为 `自身 X(基础 A + 自身规则 B)   他人因你 C   被队友分走 D`,把分组写成算式而不是三个平列数字 | `src/Ui/OverlayUGUI.Rows.cs`(`AppendContributionRows`) | 同上 |
| **口径文案** | 三行 → 四行:`自身 = 基础 + 自身规则`;`总贡献 = 自身 + 他人因你`;`直接打出 = 自身 + 被队友分走` | 同上 | 同上 |
| **IMGUI 回退渲染器** | 同一分组(旧渲染路径) | `src/Ui/OverlayCore.cs` | 同上 |
| **两条恒等式(验证)** | `check_export_schema.py` 新增逐角色 `baseCredit + selfRuleCredit + receivedAssist == directDamage`,并把 `receivedAssist` 由可选升为**必需**字段 | `check_export_schema.py` | 只更严:未来任何丢字段/记错账的文件立刻红灯。17 份实测最大偏差 0.0001 |
| **版面守卫双向化** | `check_contribution_layout.py` 新增 `check_source_replica()`:解析 `OverlayUGUI.Rows.cs` 里的列标签与宽度,与 Python 副本逐项比对;自测用「11→12」真实篡改证明会红 | `check_contribution_layout.py` | 只更严:修 C# 不改副本 → 立刻红灯 |
| **`check_live_log.py` 两处缺陷** | ①从「行内第一个数字」改为「标记之后的数字」;②不带命中数 `(xN)` 的 `未归属` 行不再参与比对,只报不可核验 | `check_live_log.py` | 只影响验证;修前它把一个正确的面板判成 ERROR |
| 版本 | `BuildInfo.cs` / `DpsMeter.csproj` → 1.7.11 | 两处 | 无 |

## 回退锚点

- 回退目标:`DpsMeter.dll.1.7.10.bak`(部署前从现网备份,已核对哈希)。
- 1.7.10 现网 DLL:386,560 B / SHA256 `BF2F174A4059125376946ABCB0E3E4A9176E6B360CAB209078F219A95389AE85`。
- 1.7.11 现网 DLL:**387,072 B** / SHA256 `36EC96D4DBD8E221ED554476C299BD8DB4C9A1220A2A923DB16BC7BB4888BC42`。
- 更早链:`.1.7.9.bak` = `F3F73C81…`、`.1.7.8.bak` = `0B339836…`(386,048 B)、`.1.7.7.bak` = `7425139C…`、`.1.7.6.bak` = `BB96DA65…`。
- ⚠ 绝不回退 `DpsMeter-1.0.48-crash.bak` / `DpsMeter-1.0.49-crash.bak`。
- **数据不受影响**:1.7.11 没有新增/改名任何导出字段(唯一变化是 `receivedAssist` 被验证器要求**更严**,它从 1.6.0 起就一直在写);
  回退不需要重写任何导出文件。**注意**:回退后 `check_export_schema.py` 仍是新版本(它要求 `receivedAssist` 存在),而 1.7.10 的导出满足该要求,所以不会因回退而红灯。

## 回退步骤

1. 关闭游戏(或热替换:改名旧文件再复制)。
2. `Copy-Item BepInEx\plugins\DpsMeter\DpsMeter.dll.1.7.10.bak BepInEx\plugins\DpsMeter\DpsMeter.dll -Force`。
3. `(Get-FileHash ...\DpsMeter.dll -Algorithm SHA256).Hash` 必须等于 `BF2F174A4059125376946ABCB0E3E4A9176E6B360CAB209078F219A95389AE85`。
4. 重启游戏,确认日志 `Loading [DpsMeter 1.7.10]` 与导出 `version = "1.7.10"`。
5. 回退插件**不需要**回退守卫:新版守卫对 1.7.10 导出同样正确(它只是更严地要求一个一直存在的字段)。

## 成功判据(全部满足才算上线成功)

1. 构建 0 警 0 错;`BuildInfo.Version == csproj <Version> == 1.7.11`。
2. `check_contribution_layout.py` → 30 份 / **573 行 0 违规**;`--selftest` → PASS(含源码漂移反证 1 处)。
3. `check_export_schema.py` → 30 份 0 问题;`--selftest` → **54 例 failed=0**(含两条新负控)。
4. `check_live_log.py` → exit 0(实机日志:82 帧与导出一致、16 帧标注不可核验);`--selftest` → PASS。
5. `check_doc_convergence.py` → 8/8;`check_docs_123.py` → exit 0;`test_gate` → 64 passed。
6. `contrib.crosscheck --batch`(30 份)= `{ERROR:1, LEGACY_NOT_APPLICABLE:15, PASS:11, WARNING:3}`(唯一 ERROR 为已知 1.6.0)。
7. `recon_probe` → ALL CHECKS PASSED。
8. 部署后 DLL:387,072 B / `36EC96D4…`;`SOURCE==DEPLOYED: True`;DLL 内**不存在** `1.7.10` 字面量,且含 `他人因你`/`被队友分走`。
9. 反编译已部署 DLL:`PadR("角色", 16) + PadL("总贡献", 11) + PadL("占比", 8) + PadL("自身", 11) + PadL("他人因你", 11) + PadL("被队友分走", 11) + PadL("直接占比", 9) + PadL("命中", 6)`。

## 失败判据(任一即回退)

- 任一真实导出的逐角色恒等式 `base+self+received == direct` 被报 ERROR(说明 1.7.11 的验证器读错了字段)。
- 历史导出因缺 `receivedAssist` 被拒(实测 15 份带贡献段的导出**全部**有该字段;若出现例外,是该文件真有损)。
- `check_contribution_layout.py` 的源码漂移检查在**未改 C#** 的情况下报错(说明它的解析器脆弱,应回退该项)。
- 导出出现任何新字段或字段改名(本版不应有)。

## 纪律

1. **副本必须与渲染器对账**:手写复算的守卫在源码改动后会保持绿色到用户看到错位为止 —— 本版把这条补成了机器检查。
2. **不可核验 ≠ 通过**,也 **≠ 错误**:实时进行中的战斗没有导出可对账,只能显式标注(本版修的就是把前者误判成后者)。
3. **显示改动也要写回退预案**:用户看的就是显示,看错了就是缺陷。
