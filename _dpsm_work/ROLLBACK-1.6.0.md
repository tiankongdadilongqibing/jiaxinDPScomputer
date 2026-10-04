# 回退预案:阶段 E(贡献进入正式导出 schema)

> 建立时间:2026-10-03 深夜(SESSION-STATE §7.2.83)。**目标版本:1.6.0**(候选;版本号以 `BuildInfo.cs` 为准)。
> 用途:在动高风险文件(`ExportService.cs` / `Aggregator.cs` / `BuildInfo.cs` / `Plugin.cs`)之前,
> 先固定"能一键回到 1.5.5"的全部锚点。**锚点已建立并校验(见下表),当前部署未被改动。**

## 1. 变更前锚点(已建立,均已校验)

| 锚点 | 路径 | 大小 | SHA256 | 校验方式 |
|---|---|---|---|---|
| 部署 DLL 备份 | `BepInEx\plugins\DpsMeter\DpsMeter.dll.1.5.5-verified.bak` | 340,992 B | `18E4D933C9C8724090FE276CE5E9F70CD844BF7E5AB038D3B335554691365FA9` | 与部署 DLL 哈希逐字节一致 |
| 源码快照 | `_dpsm_work\src_snapshot_155.zip` | 288,979 B | `E4A0D4DB16E38BCD6775406C4D87DDF13FA9579A4A66D17049E7DAD281DC080A` | **解包回环:62 个文件(61 .cs + 1 .csproj)全部 SHA256 命中,0 缺失 0 不符;不含 bin/obj** |
| 当前部署 DLL | `BepInEx\plugins\DpsMeter\DpsMeter.dll` | 340,992 B | 同上 `18E4D933…` | 重新构建的 `src\bin\Release\DpsMeter.dll` 与它哈希一致(说明源码=部署) |
| 导出(只读) | `BepInEx\plugins\DpsMeter\exports\*.json` | 11 份 | — | 阶段 E **不改**导出文件;旧导出必须永远可读 |
| 配置 | `BepInEx\config\dev.dpsmeter.cfg` | — | — | 新开关默认值在实现时记录;回退只需置 false |

⚠ `DpsMeter-1.0.48-crash.bak` / `DpsMeter-1.0.49-crash.bak` **绝不回滚、绝不删除**(它们不是可用版本)。

## 2. 回退步骤(按顺序,命令可照抄)

```powershell
# 0) 先关闭游戏(DLL 被占用时复制会失败)
# 1) 恢复 DLL
Copy-Item -LiteralPath 'D:\dmmplayer\rlyehshoujotaix_cl\BepInEx\plugins\DpsMeter\DpsMeter.dll.1.5.5-verified.bak' `
          -Destination 'D:\dmmplayer\rlyehshoujotaix_cl\BepInEx\plugins\DpsMeter\DpsMeter.dll' -Force
# 2) 校验哈希(必须等于 18E4D933C9C8…)
(Get-FileHash -Algorithm SHA256 -LiteralPath 'D:\dmmplayer\rlyehshoujotaix_cl\BepInEx\plugins\DpsMeter\DpsMeter.dll').Hash
# 3) 源码回退(仅在 .cs 已被改过时做;先确认目标路径再覆盖)
& tar -xf 'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\src_snapshot_155.zip' -C 'D:\dmmplayer\rlyehshoujotaix_cl'
#    (覆盖前建议先把当前 src 复制成 src_before_rollback\,不要直接删)
# 4) 配置回退:把阶段 E 新增开关(实现时登记)设为 false,或恢复 cfg 备份
# 5) 验证
& 'D:\dmmplayer\dotnet-sdk6\dotnet.exe' build 'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\src\DpsMeter.csproj' -c Release -v minimal
#    → 必须 0 警 0 错;产物哈希必须回到 18E4D933…
```

## 3. 回退成功的判据(全部满足才算回退完成)

1. `DpsMeter.dll` SHA256 = `18E4D933C9C8724090FE276CE5E9F70CD844BF7E5AB038D3B335554691365FA9`,340,992 字节。
2. Release 构建 0 警 0 错,且重建产物与上面哈希一致(证明源码也回到了 1.5.5)。
3. 五个守卫 exit 0:`check_export_schema.py`(problems=0)、`v150_validate.py`(problems=0)、
   `check_fact_signature.py`、`check_docs_123.py`(TOTAL DAMAGE MARKERS: 0)、`refactor_final_check.py`。
4. `recon_probe` 输出 `ALL CHECKS PASSED`。
5. 游戏启动日志出现 `Harmony patches applied` 且导出 JSON 与 1.5.5 结构一致(schema 检查通过)。

## 4. 实施纪律(阶段 E 期间)

* 高风险文件(`ExportService.cs` / `Aggregator.cs` / `BuildInfo.cs` / `Plugin.cs`)**同一时间只允许一个智能体改**;
  改动必须"新增为主、既有字段不动",任何既有字段的语义变化都要先升级 schemaVersion。
* 每改一处立刻跑构建 + `recon_probe` + 五守卫;不通过就回退,不往前堆。
* 只读离线核心 `_dpsm_work/contrib/` 是**验收基准**:C# 侧算出的 contribution 必须与它在同一份导出上数值一致
  (允许 ≤1e-6 相对误差),这是阶段 E 的硬性验收(交叉实现校验)。
* 任何部署(复制 DLL 到游戏目录)都必须**先问用户**,并明确"需要用户再打一场才能验收新字段"。

## 5. 与本次审查的关系

阶段 E 的设计与风险清单来自子智能体审查 `_dpsm_work/REVIEW-phaseE-design.txt`;
贡献核心的对抗性审计来自 `_dpsm_work/REVIEW-contrib-core.txt`;
阶段 G(攻击力拆分)的可行性侦察来自 `_dpsm_work/REVIEW-phaseG-recon.txt`。
三份报告到齐并确认无误后,才动源码;若审查指出核心有错,先修核心再谈阶段 E。
