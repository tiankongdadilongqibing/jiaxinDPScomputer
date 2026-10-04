# 残差与数据预算普查(contract budget-census/1)

> 只读普查:32 份导出;数据库伤害合计 4621056889,其中**丢失折叠步**相关 0(0.0000%)。
> 本文件不修改任何导出,也不为丢步伪造补偿因子:丢一步就使等式降为**下界**,对应的比较资格也随之降级。

## 1. 问题清单(每项:影响范围 / 最小证据 / 可证伪假设 / 下一步成本)

### N6-1 丢失的折叠步(foldDropped)

- **影响范围**:0/32 份导出出现;共 0 击 / 0 步 / 影响伤害 0(占这些样本总伤害的 0.0000%)
- **涉及战斗**:(无)
- **最小证据**:逐击 calc.foldDropped>0;丢步击的折叠条数分布 = {}
- **可证伪假设**:丢步只在折叠条数达到上下文上限时发生(即引擎上限所致,不是被规则吃掉)
- **证伪方式**:若存在 foldDropped>0 而折叠条数明显低于上限的击,则该假设被否证
- **下一步成本**:离线即可复核(本报告已给出分布);提高上限需插件改动,当前无证据支持

### N6-2 行/快照溢出(timeline·forensics·applies)

- **影响范围**:31/32 份导出出现溢出计数
- **涉及战斗**:battle_411001_20261003_205449.json, battle_411001_20261003_230040.json, battle_411001_20261003_230311.json, battle_411001_20261003_230615.json, battle_411001_20261003_231044.json, battle_411001_20261003_231340.json, battle_411001_20261003_235204.json, battle_411001_20261004_012328.json
- **最小证据**:逐段计数器:battle_411001_20261003_205449.json{forensics.perBucketOverflow=87}; battle_411001_20261003_230040.json{forensics.perBucketOverflow=59,madnessApplies.rowsDropped=603}; battle_411001_20261003_230311.json{forensics.perBucketOverflow=94,madnessApplies.rowsDropped=603}; battle_411001_20261003_230615.json{forensics.perBucketOverflow=80,madnessApplies.rowsDropped=602}; battle_411001_20261003_231044.json{forensics.perBucketOverflow=89,madnessApplies.rowsDropped=603}; battle_411001_20261003_231340.json{forensics.perBucketOverflow=59}
- **可证伪假设**:溢出只影响**诊断/时间线**的记录完整性,不影响逐击伤害与贡献(这两者来自事件与折叠)
- **证伪方式**:若某场溢出同时其台账对账(reconciliationGap)不为 0,则该假设被否证
- **下一步成本**:离线可复核;若确需完整时间线,需插件提高上限(第四批)

### N6-3 读取失败(错误计数器非零)

- **影响范围**:8/32 份导出报告了非零错误计数
- **涉及战斗**:battle_411001_20261003_230040.json, battle_411001_20261003_230311.json, battle_411001_20261003_230615.json, battle_411001_20261003_231044.json, battle_411001_20261003_231340.json, battle_9999_20261003_225530.json, battle_9999_20261003_225647.json, battle_9999_20261003_225905.json
- **最小证据**:逐段计数器:battle_411001_20261003_230040.json{giverErrors=38736}; battle_411001_20261003_230311.json{giverErrors=43264}; battle_411001_20261003_230615.json{giverErrors=41966}; battle_411001_20261003_231044.json{giverErrors=44190}; battle_411001_20261003_231340.json{giverErrors=62486}; battle_9999_20261003_225530.json{giverErrors=54}
- **可证伪假设**:读取失败集中在个别能力/字段,且被跳过而不是被猜测(计数器是它的证据)
- **证伪方式**:若某场的 analyzableDealt 与 totals.dealt 的差超过台账解释范围,则该假设被否证
- **下一步成本**:离线(台账对账已在 N0/N1 里跑);不改插件

### N6-4 未知身份(owner/攻击者不可解析)

- **影响范围**:16/32 份导出出现未知身份计数
- **涉及战斗**:battle_411001_20261004_033231.json, battle_411001_20261004_035437.json, battle_411001_20261004_041309.json, battle_411001_20261004_042637.json, battle_411001_20261004_042844.json, battle_411001_20261004_113553.json, battle_411001_20261004_115024.json, battle_411001_20261004_115417.json
- **最小证据**:逐段计数器(仅列非零):battle_411001_20261004_033231.json{paramOwners.entriesDropped=8908,paramOwners.keyOwnerNull=56,paramOwners.ownerNull=56}; battle_411001_20261004_035437.json{paramOwners.entriesDropped=8530,paramOwners.keyOwnerNull=44,paramOwners.ownerNull=44}; battle_411001_20261004_041309.json{paramOwners.entriesDropped=8545,paramOwners.keyOwnerNull=50,paramOwners.ownerNull=50}; battle_411001_20261004_042637.json{paramOwners.entriesDropped=8492,paramOwners.keyOwnerNull=54,paramOwners.ownerNull=54}; battle_411001_20261004_042844.json{paramOwners.entriesDropped=8342,paramOwners.keyOwnerNull=37,paramOwners.ownerNull=37}; battle_411001_20261004_113553.json{paramOwners.entriesDropped=8483,paramOwners.keyOwnerNull=27,paramOwners.ownerNull=27}
- **可证伪假设**:未知身份不影响**总量**,只影响**归属**:这些伤害进入 unattributed 而不是被塞给攻击者
- **证伪方式**:若某场的 contribution.unattributedDamage 与这些计数器不一致,则该假设被否证
- **下一步成本**:离线(见 contribution 段的台账);需要更强身份才需插件补字段(N3 最小设计)

### N6-5 预算上限(部分不可观测)

- **影响范围**:观测到的上限:facts.maxClasses, facts.maxLiveClasses, forensics.maxBuckets, forensics.maxPerBucket, givenApplies.maxRows, madnessApplies.maxRows, paramOwners.maxEntriesPerRead, paramOwners.maxEntryReadsPerBattle, paramOwners.maxUnionRows
- **涉及战斗**:(无)
- **最小证据**:导出里**没有**计数器的上限:`FoldContext.MaxSteps=24`(只能用 foldDropped 间接看)、`_statusSnaps=256`、`BuildBuffText` 40 条上限
- **可证伪假设**:这三者的真实触发率当前**不可证伪**,因为文件里没有它们的计数器
- **证伪方式**:补三个计数器后,任何一场都能直接验证是否触发(第四批的一次性插件改动)
- **下一步成本**:低:三个 int 计数器;高:在长战/多召唤场景下排除静默截断

## 2. 逐场明细

| 文件 | 版本 | 任务 | 伤害 | 丢步击/步/伤害 | 读取错误 | 溢出 | 未知身份 |
|---|---|---|---|---|---|---|---|
| battle_411001_20261003_205449.json | 1.5.3 | 411001 | 198588964 | 0/0/0 | - | forensics.perBucketOverflow | - |
| battle_411001_20261003_230040.json | 1.5.4 | 411001 | 181236937 | 0/0/0 | giverErrors | forensics.perBucketOverflow,madnessApplies.rowsDropped | - |
| battle_411001_20261003_230311.json | 1.5.4 | 411001 | 189268037 | 0/0/0 | giverErrors | forensics.perBucketOverflow,madnessApplies.rowsDropped | - |
| battle_411001_20261003_230615.json | 1.5.4 | 411001 | 196505136 | 0/0/0 | giverErrors | forensics.perBucketOverflow,madnessApplies.rowsDropped | - |
| battle_411001_20261003_231044.json | 1.5.4 | 411001 | 197087773 | 0/0/0 | giverErrors | forensics.perBucketOverflow,madnessApplies.rowsDropped | - |
| battle_411001_20261003_231340.json | 1.5.4 | 411001 | 209426327 | 0/0/0 | giverErrors | forensics.perBucketOverflow | - |
| battle_411001_20261003_235204.json | 1.5.5 | 411001 | 203964758 | 0/0/0 | - | forensics.perBucketOverflow,givenApplies.rowsDropped | - |
| battle_411001_20261004_012328.json | 1.5.5 | 411001 | 206931707 | 0/0/0 | - | forensics.perBucketOverflow,givenApplies.rowsDropped | - |
| battle_411001_20261004_015919.json | 1.6.0 | 411001 | 197273140 | 0/0/0 | - | forensics.perBucketOverflow,givenApplies.rowsDropped | - |
| battle_411001_20261004_021914.json | 1.6.1 | 411001 | 197859703 | 0/0/0 | - | forensics.perBucketOverflow,givenApplies.rowsDropped | - |
| battle_411001_20261004_023627.json | 1.7.0 | 411001 | 212633192 | 0/0/0 | - | forensics.perBucketOverflow,givenApplies.rowsDropped | - |
| battle_411001_20261004_033231.json | 1.7.2 | 411001 | 200728856 | 0/0/0 | - | forensics.perBucketOverflow,givenApplies.rowsDropped | paramOwners.entriesDropped,paramOwners.keyOwnerNull,paramOwners.ownerNull |
| battle_411001_20261004_035437.json | 1.7.3 | 411001 | 195664426 | 0/0/0 | - | forensics.perBucketOverflow,givenApplies.rowsDropped | paramOwners.entriesDropped,paramOwners.keyOwnerNull,paramOwners.ownerNull |
| battle_411001_20261004_041309.json | 1.7.4 | 411001 | 203853083 | 0/0/0 | - | forensics.perBucketOverflow,givenApplies.rowsDropped | paramOwners.entriesDropped,paramOwners.keyOwnerNull,paramOwners.ownerNull |
| battle_411001_20261004_042637.json | 1.7.5 | 411001 | 200353734 | 0/0/0 | - | forensics.perBucketOverflow,givenApplies.rowsDropped | paramOwners.entriesDropped,paramOwners.keyOwnerNull,paramOwners.ownerNull |
| battle_411001_20261004_042844.json | 1.7.5 | 411001 | 196606611 | 0/0/0 | - | forensics.perBucketOverflow,givenApplies.rowsDropped | paramOwners.entriesDropped,paramOwners.keyOwnerNull,paramOwners.ownerNull |
| battle_411001_20261004_113553.json | 1.7.5 | 411001 | 198797524 | 0/0/0 | - | forensics.perBucketOverflow,givenApplies.rowsDropped | paramOwners.entriesDropped,paramOwners.keyOwnerNull,paramOwners.ownerNull |
| battle_411001_20261004_115024.json | 1.7.6 | 411001 | 202081366 | 0/0/0 | - | forensics.perBucketOverflow,givenApplies.rowsDropped | paramOwners.entriesDropped,paramOwners.keyOwnerNull,paramOwners.ownerNull |
| battle_411001_20261004_115417.json | 1.7.6 | 411001 | 190984342 | 0/0/0 | - | forensics.perBucketOverflow,givenApplies.rowsDropped,madnessApplies.rowsDropped | paramOwners.entriesDropped,paramOwners.keyOwnerNull,paramOwners.ownerNull |
| battle_411001_20261004_134524.json | 1.7.8 | 411001 | 195026518 | 0/0/0 | - | forensics.perBucketOverflow,givenApplies.rowsDropped,madnessApplies.rowsDropped | atkAdd.ownerUnknown,ledger.unknownAttackerDealt,ledger.unknownAttackerHits,paramOwners.entriesDropped |
| battle_411001_20261004_134853.json | 1.7.8 | 411001 | 200853545 | 0/0/0 | - | forensics.perBucketOverflow,givenApplies.rowsDropped | atkAdd.ownerUnknown,ledger.unknownAttackerDealt,ledger.unknownAttackerHits,paramOwners.entriesDropped,paramOwners.keyOwnerNull,paramOwners.ownerNull |
| battle_411001_20261004_144548.json | 1.7.10 | 411001 | 194811623 | 0/0/0 | - | forensics.perBucketOverflow,givenApplies.rowsDropped | atkAdd.ownerUnknown,ledger.unknownAttackerDealt,ledger.unknownAttackerHits,paramOwners.entriesDropped,paramOwners.keyOwnerNull,paramOwners.ownerNull |
| battle_411001_20261004_170157.json | 1.7.11 | 411001 | 211642633 | 0/0/0 | - | forensics.perBucketOverflow,givenApplies.rowsDropped | atkAdd.ownerUnknown,ledger.unknownAttackerDealt,ledger.unknownAttackerHits,paramOwners.entriesDropped,paramOwners.keyOwnerNull,paramOwners.ownerNull |
| battle_700817_20261004_114821.json | 1.7.6 | 700817 | 2314831 | 0/0/0 | - | - | paramOwners.keyOwnerNull,paramOwners.ownerNull |
| battle_9999_20261003_205129.json | 1.5.3 | 9999 | 7702608 | 0/0/0 | - | forensics.bucketsOverflow,forensics.perBucketOverflow | - |
| battle_9999_20261003_225530.json | 1.5.4 | 9999 | 1680568 | 0/0/0 | giverErrors | forensics.perBucketOverflow | - |
| battle_9999_20261003_225647.json | 1.5.4 | 9999 | 2579582 | 0/0/0 | giverErrors | forensics.perBucketOverflow | - |
| battle_9999_20261003_225905.json | 1.5.4 | 9999 | 2718309 | 0/0/0 | giverErrors | forensics.perBucketOverflow | - |
| battle_9999_20261004_012138.json | 1.5.5 | 9999 | 2147664 | 0/0/0 | - | forensics.perBucketOverflow | - |
| battle_9999_20261004_042458.json | 1.7.5 | 9999 | 7024899 | 0/0/0 | - | forensics.perBucketOverflow | atkAdd.skippedUnowned,paramOwners.keyOwnerNull,paramOwners.ownerNull |
| battle_9999_20261004_135214.json | 1.7.8 | 9999 | 10710019 | 0/0/0 | - | forensics.bucketsOverflow,forensics.perBucketOverflow | atkAdd.ownerUnknown,atkAdd.skippedUnowned,ledger.unknownAttackerDealt,ledger.unknownAttackerHits,paramOwners.keyOwnerNull,paramOwners.ownerNull |
| battle_9999_20261004_165952.json | 1.7.11 | 9999 | 1998474 | 0/0/0 | - | forensics.perBucketOverflow | atkAdd.ownerUnknown,atkAdd.skippedUnowned,ledger.unknownAttackerDealt,ledger.unknownAttackerHits,paramOwners.keyOwnerNull,paramOwners.ownerNull |

## 3. 残差分层

**全体**(残差桶 → 击数):{"1.0": 112698, "1.9": 14988, "1.65": 1550, "1.5": 1145, "0.87": 753, "1.899": 600, "1.15": 461, "1.901": 278, "1.898": 219, "0.826": 216, "0.756": 190, "1.25": 173, "0.0": 165, "1.6": 111, "1.652": 82}

**有折叠的击**:{"1.0": 111495, "1.9": 14941, "1.65": 1550, "1.5": 1128, "0.87": 753, "1.899": 596, "1.15": 445, "1.25": 170, "1.901": 167, "0.756": 140}

**无折叠的击**:{"1.0": 1203, "1.9": 47, "1.5": 17, "1.85": 14, "1.848": 13, "2.0": 12, "0.048": 11, "0.03": 9, "0.059": 7, "1.598": 5}

**按残差桶看规则来源**(桶 → 折叠 kind 计数):{"1.0": {"text": 249368, "global": 153850, "given": 106772, "atkadd": 45895}, "1.9": {"given": 14952, "text": 14882, "global": 10388, "atkadd": 6533}, "1.65": {"text": 12393, "global": 2601, "given": 1576, "atkadd": 696}, "1.5": {"text": 1345, "given": 1160, "global": 939, "atkadd": 379}, "0.87": {"text": 2750, "global": 1003, "given": 750, "atkadd": 316}, "1.15": {"text": 597, "global": 401, "given": 186, "atkadd": 90}, "1.899": {"given": 210, "text": 178, "global": 178, "atkadd": 127}, "0.667": {"text": 3, "talent": 1}, "1.496": {"text": 2}, "0.399": {"text": 2}, "0.29": {"text": 2}, "1.25": {"text": 744, "atkadd": 78, "given": 4, "talent": 1}, "0.011": {"text": 144}, "1.6": {"text": 351, "atkadd": 75}, "4.688": {"text": 36, "given": 27}, "3.126": {"text": 12, "given": 9}, "0.056": {"text": 20}, "1.875": {"text": 19}, "3.125": {"text": 8, "given": 6}, "3.604": {"given": 15}, "14.293": {"given": 12}, "0.058": {"text": 15}, "2.6": {"given": 54, "text": 36, "atkadd": 18}, "1.441": {"given": 105, "atkadd": 35}, "3.574": {"given": 21, "atkadd": 6}, "3.573": {"given": 12, "atkadd": 8}, "3.572": {"given": 12, "atkadd": 4}}

⚠ **k_extra 净差不等于某条规则真实多/少生效的次数**:它是一组互相抵消的倍率的净效果;
把净差读成「这条规则多生效了 N 次」会立刻出错(反例:0.5×2.0 的抵消对净差贡献 0,但两条规则各生效一次)。

## 4. 结论与下一步

- **N6-1**:离线即可复核(本报告已给出分布);提高上限需插件改动,当前无证据支持
- **N6-2**:离线可复核;若确需完整时间线,需插件提高上限(第四批)
- **N6-3**:离线(台账对账已在 N0/N1 里跑);不改插件
- **N6-4**:离线(见 contribution 段的台账);需要更强身份才需插件补字段(N3 最小设计)
- **N6-5**:低:三个 int 计数器;高:在长战/多召唤场景下排除静默截断
- 会心观测保持独立研究,不并入本普查。
- 只有「高影响且离线无法区分」的假设才值得设计一次性组合探针;当前清单里只有 N6-5 属于这一类。
