# 重构批次记录 R51:首次部署的实机验证结果 + 本场未归因伤害定位

> 你用重构后的构建(28B8CCAF…)跑了一场(任务 411001,导出 battle_411001_20261005_082525.json,24.6 MB)。
> 本记录给出**验证结论**与你要的**未归因伤害定位**。

## 0. 验证结论(强证据)

| 检查 | 结果 |
|---|---|
| **masterdata 转储逐字节对照** | **IDENTICAL = True**:20 份文件全部与冻结基准一致(无 changed / missing / new) |
| 导出是否正常 | 是:quest 411001(可比类),producer.pluginVersion = 1.7.11,method = log-share/1,damageBasis = dealt |
| 内部算术自洽 | damageLedger.reconciliationGap = **0.0**;coverage.excludedDamage 337,906 == totals.unattributedDamage 337,906 |
| 折叠健康度 | foldAccounting:total 35,961,**zeroFactor 0 / noopFactor 0 / subUnity 0**;foldDropped 0 |
| 结论 | **重构未改变主数据层行为**(这是唯一能宣称等价的路径,现已取得);缓存/格式改动的可见验证需你在游戏内确认(F9 空态、数值逐字符一致) |

## 1. 你要的:本场未归因伤害**是什么**

导出里有**两个不同口径**的"未归因",必须先分开,否则会混:

### (1) 贡献账的残差:8,375,105.67(占 4.329%),全部一个理由

    contribution.unattributed = [{"reason":"unknown_kind","amount":8375105.6709,"folds":5862}]
    contribution.coverage     = creditedShare 0.95671 / unattributedShare 0.04329

即:**5,862 个折叠的"规则种类"没能被归类**,所以这些份额无法记到任何提供者名下。
同一份导出里各原因的折叠数(diagnostics.reasonCounts)是:

| 归属原因 | 折叠数 |
|---|---|
| ability_holder_attacker | 9,180 |
| global_name_unique | 8,739 |
| byUnit | 6,177 |
| ability_holder_unique | 6,003 |
| **unknown_kind** | **5,862** |

合计 35,961 → unknown_kind 占 **16.3% 的折叠**,但只占 **4.33% 的伤害**(因为未识别种类的平均份额较小)。

### (2) 会话口径的未归因:337,906(75 次命中),全部落在**ショゴス**身上

    totals.unattributedDamage = 337906   unattributedHits = 75
    damageLedger              = unknownAttackerHits 75 / unknownAttackerDealt 337906
                                outsideTeamHits 20 / outsideTeamDealt 146810
    unattributedByVictim      = { "ショゴス": 337906 }

这是**攻击者对象无法识别**的命中(75 次),另有 20 次命中属于队伍之外(146,810)。两者都**被排除在归属之外**,
所以 analysisDamageCoverage = 99.92%(而不是 100%)。

## 2. 一句话总结

**这场 1.93 亿伤害里,95.67% 被正确归属;剩下 4.33%(8,375,105)全部是"未识别的规则种类"这一个原因,
另有 337,906(75 击,全在ショゴス)是"攻击者无法识别"而被整体排除。** 两者口径不同,不应相加。

## 3. 想更深定位 unknown_kind 的话(下一步)

需要逐击明细(hitDetail / forensics.records)按未识别折叠过滤,列出涉及的技能/效果 id 分布——
导出 24.6 MB,可做,但要单独跑一次提取。**需要的话我下一轮做。**

## 4. 回滚与部署状态

| 项 | 值 |
|---|---|
| 当前部署 | 28B8CCAF…(398,336 字节,重构构建) |
| 基线备份 | _dpsm_work/deploy-backup/baseline-1.7.11/(105 文件,36EC96D4…) |
| 回滚 | 游戏关闭时 Copy-Item 备份覆盖部署路径 |
