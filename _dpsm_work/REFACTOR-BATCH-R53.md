# 重构批次记录 R53:第二场的独立复现 + 提取键「战斗结束后」失效的修复

> 你打了第二场(导出 `battle_411001_20261005_132514.json`,24,876,310 字节),并在 13:26:50 按了提取键。
> 本记录:第二场结果(§1)、实机抓到的缺陷(§2)、修法(§3)、验证(§4)、部署与回退(§5)、未做(§6)。

## 1. 第二场:结论被独立复现

| 项 | 第一场(08:25) | 第二场(13:25) |
|---|---|---|
| 贡献账残差(第一口径) | 8,375,105.67 / 5,862 折叠 / 4.329% | **8,425,649.60 / 5,774 折叠 / 4.199%** |
| 残差的 kind | 100% `given` | **100% `given`** |
| 规则 | `given#4..7/1006/-10`,因子 1.1 | **`given#2..7/1006/-10`,因子 1.1** |
| 提供者解析 | `giverResolved=0 / giverNull=30,269` | **`giverResolved=0 / giverNull=31,021`** |
| 授予钩子 | `{addTalent:12}`,12 次全 `nullGuest`,`targets=0` | **`{addTalent:11}`,11 次全 `nullGuest`,`targets=0`** |
| 候选判定 | 唯一 = エヴァラス・フラウ | **唯一 = エヴァラス・フラウ**(同一人) |
| 重算 vs 自述 | MATCH | **MATCH** |

第二场的受击方分组:109 个实例(2251+2251 折叠)、24 个(571+571)、3 个(65+65)——同一条 `-10` 规则、每击两份。
整场的授予索引是:`1006/-10` → エヴァラス・フラウ(**唯一**);`1006/-15` → メルティエル + マッドシーカー(两个,若没被取消就会是 `ambiguous`);
`1006/100` → メアリー;`409/30`、`411/90` → エヴァラス・フラウ。**"唯一持有者"这条线索在两场里都成立。**

第二场的会话口径(第二口径,**不可与上面的数字相加**):`totals.unattributedDamage = 363,554`(75 击,全在 ショゴス)、
队伍之外 116,864(17 击)、`reconciliationGap = 0.0`、`analysisDamageCoverage = 0.9994`。

## 2. 实机抓到的缺陷(这次按键没有白按)

13:26:38 收尾写下 `[EXTRACT] 关闭(General/ExtractOnBattleEnd=false)`;13:26:50 你按了提取键,日志留下:

    [DpsMeter][EXTRACT] 跳过:没有战斗会话

原因:收尾之后 `Aggregator.Session` 被置空(`Aggregator.Clock.cs:220` / `Aggregator.Session.cs:171`),
5 s 的软恢复窗口也已过期 —— **按键路径在用户最想用的那一刻(刚打完)恰好不可用**。
导出文件与收尾时算好的贡献结果都还在,只有会话没了。这条日志本身就是"失败必须被计数、不许静默"的纪律在起作用:
没有它,这次按键只会表现为"什么都没发生"。

## 3. 修法

* 收尾时存一份"最后一场"的快照:`EvidenceExtractor.Remember(s, exportPath)`(在 `ExportService.Export` 之后、
  `ActorStats.Source` 被清空之前),内容 = 导出路径 + **同一份** `ContributionResult`(收尾时算一次,而不是
  从文件重算 —— 从文件重算等于把归属阶梯实现第二遍,这正是本仓库拒绝的失败模式)。
* 按键路径的选择写成纯策略 `ExtractPolicy.SelectSource(liveAvailable, rememberedAvailable)`:
  **实时 → 快照 → 都没有就明确跳过**(不再静默)。`Run(BattleSession, reason)` 签名不变(收尾传会话;
  按键传 `Aggregator.Session`,可能为 null)。
* 普查与 manifest 各加 `inputSource`(`live` / `last-finalised`),并写进 manifest 的 `notes[]`:
  读者必须能分辨"实时提取"和"重发收尾结果"。

## 4. 验证

| 命令 | 结果 |
|---|---|
| 构建 | **0 警告 0 错误**(417,792 字节) |
| 行为套件 | **798 用例 / 0 失败**(+4:`extract/source` 组,钉住优先级) |
| 新增变异 | `extract-source-prefers-the-stale-snapshot`(把优先级倒过来)→ **红在具名用例** |
| 全量变异负控 | **112 例 / 0 失败**(含新增的 `extract-source-prefers-the-stale-snapshot`) |
| 契约守卫 | `check_extract_contract.py` 依旧 PASS(新增的 `inputSource` 已进 manifest 的 EXTRA 表) |
| 验收 | `python n0_acceptance.py --out acceptance_r53`(cwd=`_dpsm_work`):**42 条命令 / 74 条检查 / 0 项**,档案里记录的部署正是 `3A89D30A…`;文档收敛 12/12 |

## 5. 部署与回退

| 项 | 值 |
|---|---|
| 替换前 | `AA836C06…`(416,256 字节) |
| 替换前状态 | 游戏未运行、文件未独占(**UNLOCKED**) |
| 备份 | `_dpsm_work/deploy-backup/pre-r53-AA836C06/DpsMeter.dll` |
| 当前部署 | **`3A89D30A…`**(417,792 字节) |
| 复核 | 两个 crash `.bak` 哈希**未变**(`2E1819F2…` / `F7FF1EB8…`) |
| 回退 | `Copy-Item _dpsm_work/deploy-backup/pre-r53-AA836C06/DpsMeter.dll BepInEx/plugins/DpsMeter/DpsMeter.dll -Force` |

## 5b. 顺带修掉的一个自伤

提交后自查发现 `_dpsm_work/tests/_census_r53.tmp.json`(我这次探针的 JSON 输出)**被提交了**:它长成
「`_*.tmp` 词干 + `.json` 尾巴」,而忽略规则只写了 `/_dpsm_work/tests/_*.tmp`。已 `git rm --cached` 并删除,
同时把规则放宽为 `/_dpsm_work/tests/_*` —— AGENTS §4 说临时文件放这里,那就不该靠后缀自觉,规则要能自己生效
(删前确认:tests/ 下没有任何已跟踪文件以 `_` 开头)。

## 6. 未做(诚实清单)

* **修复版仍未实机验证**:需要你再打一场(或战斗中按一次 F4)才会产生第一个真的证据包;包一出现我立刻跑
  `extract_verify.py`(校验值 + 普查逐组对比 + 契约守卫)并把结果追加到本记录。
* 归属规则仍未改(§1 的候选判定只进普查;是否接进归属是另一个决定,见 REPORT-未识别规则种类-R52.md §4)。
