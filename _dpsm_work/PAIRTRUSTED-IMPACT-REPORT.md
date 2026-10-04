# P0-C:PairTrusted / PairCorroborated 对贡献结果的离线量化

- 任务编号:P0-C(依据 _dpsm_work/CONTRIBUTION-REVIEW-NEXT-STEPS.md 「Agent P0-C」与 §0.4)
- 证据等级:**离线重放**(全部数字由 exports 复算;未启动游戏、未改动插件)
- 只读基线:插件 1.7.7 已部署(381,952 B,SHA256 前缀 7425139C…;报告当时的基线,当前 1.7.9);本任务不构建、不部署
- 交付物:_dpsm_work/pairtrusted_impact.py(新增)、pairtrusted_impact_report.json(新增)、pairtrusted_impact_report.txt(新增)、本文件(新增)
- 编辑边界:本任务**只新增文件**;仓库中任何既有文件均未修改

---

## 0. 结论(三选一,已标为离线重放)

**差异可忽略**(对当前贡献口径)。在本模型真正分析的事件域(team-1 且攻击者可解析,Contribution.cs:321)
内, pairTrusted=false 的命中数为 **0**;**B 口径(PairTrusted)与 A 口径逐字段完全相等**,不只是“接近”。

- 唯一例外:3 份 **1.5.x 训练场**(1.5.4×2、1.5.5×1)确实有 in-domain 不可信命中。B 只把它们的倍率池
  退回 base,poolTotal 下降 **4.51% / 10.37% / 15.25%**;但**所有角色的 totalCredit 与 totalShare 增量恒为 0**
  (maxAssist=0,maxSelf=maxBase=被移动的池),top1/top3 与排名完全不变,差异只落在**特定规则的 damageEquivalent**。
- **C 口径(PairTrusted && PairCorroborated)不可作为可信度条件**:pairCorroborated=true 在语料里只占
  0.1%–3% 的命中,C 会把 **>99.98% 的倍率池整块丢掉**,从而制造出巨大的“排名差异”(如 411001 1.7.6
  ルナリス 51,955,401 → 368,775,rank 1→7)。这是“把测量不到的佐证当成不可信”的伪差异,**不是**不可信配对造成的。
- 另:语料中确实存在 live-age 不可信配对(如训练场 1.7.5 的 104 击),但它们**全部是 team 2(敌方)命中**,
  早已被 Contribution.cs:321 排除,对贡献表不可见;其“若纳入”的池量级极小(1,958.98)。

一句话:**PairTrusted 影响可忽略;无需引入配对可信度条件;PairCorroborated 是“佐证可得性”标志,不能当可信度闸门。**

> **2026-10-04 更新(1.7.8 训练场导出)**:语料新增的 `battle_9999_20261004_135214.json`(1.7.8)是全语料**第一份当前版本、in-domain 不可信命中非零**的导出:in-domain 12 击(其中 7 击带 fold),B 会把 poolA 842,871.87 中的 **62.7391**(0.007443%)退回 base。**正常战斗(411001 / 700817)仍为 0**,B 仍是 no-op。因此 **T3 的「所有 1.7.x 导出 in-domain 不可信命中为 0」现只对 NON-TRAINING 导出成立**,训练场另立 T3c/T4c;§4.1 的「唯一」措辞也仅指 1.5.x 训练场那一批。

---

## 1. 实现路径(与插件 Contribution.Compute 的对应关系)

Python 是**独立实现同一算法**,不是调用同一个 C# 函数。

| 环节 | 本脚本使用 | 对应 C# |
|---|---|---|
| 归属阶梯 | contrib.attribution.OwnerIndex.resolve | Contribution.Resolve(:227-261) |
| fold 词表 | contrib.aggregate.extract_folds(calc.fold + calc.atkAdd) | Contribution.Compute 的 folds 读取(:328-336)与 ContributionSession.BuildInputs(:58-95) |
| 对数份额拆池 | contrib.aggregate.split_hit | Compute :339-350 |
| 聚合循环(base/self/assist/rule/link/unattributed/analyzableDealt) | 本脚本 compute() 逐行重写 | Compute :317-435 |
| 事件域谓词 | row = by_key[atkKey] 且 row.team==1,否则 continue | Compute :321(AttackerKey==0 或 !ix.Team.TryGetValue) |

**忠实性证明(--selftest)**:
- T1:变体 A 与插件自己写出的 contribution 段逐字段一致(analyzableDealt / attributedDamage / poolTotal /
  actors×5 字段 / rules.damageEquivalent / foldAccounting.total):**OK,mismatches=0**。
- T1b:变体 A 与既有离线核心 contrib.aggregate.analyze 一致。
- T1c:在**没有 contribution 段**的 1.5.5 导出上,变体 A 仍与离线核心一致(证明 1.5.x 上也已按同一算法验证)。

---

## 2. 口径定义与 §0.4 纪律

| 口径 | 定义 | 事件集 / 分母 |
|---|---|---|
| A | 全部 calc.fold + calc.atkAdd(现网行为) | 相同 |
| B | 仅 pairTrusted==true 的命中的 fold | **相同**(不可信命中的 fold 置零,其伤害仍留在 analyzableDealt,退回攻击者 base) |
| C | 仅 pairTrusted==true 且 pairCorroborated==true 的 fold | **相同** |
| B* | 只保留 pairTrusted==true 的整击 | **不同**(denominatorChanged=true,仅作参考) |
| C* | 只保留 pairTrusted && pairCorroborated 的整击 | **不同**(denominatorChanged=true) |

即:主实验(A/B/C)**只隔离不可信构成,不删整击、不改分母**;任何删整击的结果一律标为 B*/C* 并标注分母不同。

---

## 3. 输入导出(实测引用)

主表 8 份(全部给出 A/B/C/B*/C* 完整明细):

| 文件 | 版本 | quest | contribution 段 |
|---|---|---|---|
| battle_411001_20261004_115417.json | 1.7.6 | 411001 | 有 |
| battle_411001_20261004_041309.json | 1.7.4 | 411001 | 有 |
| battle_700817_20261004_114821.json | 1.7.6 | 700817 | 有 |
| battle_9999_20261004_042458.json | 1.7.5 | 9999(训练场) | 有 |
| battle_9999_20261003_205129.json | 1.5.3 | 9999(训练场) | 无 |
| battle_9999_20261003_225530.json | 1.5.4 | 9999(训练场) | 无 |
| battle_9999_20261003_225647.json | 1.5.4 | 9999(训练场) | 无 |
| battle_9999_20261004_012138.json | 1.5.5 | 9999(训练场) | 无 |

另对 **报告时 exports 全部 26 份**做 pair 普查(--survey);--all 会输出 26 份完整明细到同一 json/txt。

---

## 4. 关键结果

### 4.1 A vs B:in-domain 不可信命中数与池迁移

| 导出 | 版本 | in-domain untrusted | untrusted(有 fold) | pairUntrustedCredit(B 移动的池) | B 后 poolTotal 变化 | 角色排名变化 |
|---|---|---|---|---|---|---|
| 411001_115417 | 1.7.6 | 0 | 0 | 0.00 | 0(0.0000%) | 无 |
| 411001_041309 | 1.7.4 | 0 | 0 | 0.00 | 0(0.0000%) | 无 |
| 700817_114821 | 1.7.6 | 0 | 0 | 0.00 | 0(0.0000%) | 无 |
| 9999_042458 | 1.7.5 | 0 | 0 | 0.00 | 0(0.0000%) | 无 |
| 9999_205129 | 1.5.3 | 19 | 0 | 0.00 | 0(0.0000%) | 无 |
| 9999_225530 | 1.5.4 | 9 | 9 | 29,393.0983 | −29,393.10(−15.25%) | 无(maxAssist=0) |
| 9999_225647 | 1.5.4 | 5 | 5 | 19,668.0386 | −19,668.04(−4.51%) | 无(maxAssist=0) |
| 9999_012138 | 1.5.5 | 6 | 6 | 24,285.0215 | −24,285.02(−10.37%) | 无(maxAssist=0) |

26 份普查(pairtrusted_impact_report.txt 末节)进一步证明:
- **全部 19 份 411001 + 1 份 700817:in-domain untrusted = 0,out-of-domain untrusted = 0。**
- 训练场 1.7.5(042458):in-domain = 0;out-of-domain(team 2)= 104 击(104 有 fold,伤害 11,790,若纳入为池 1,958.98)。
- 训练场 1.5.3(205129):in-domain untrusted 19(其中带 fold 的 0);out-of-domain 184(92 带 fold,池 3,473.85)。
- 训练场 1.5.4/1.5.5 的 3 份:in-domain untrusted 5–9,全部带 fold → 截至本文这是 B 真正生效的地方;2026-10-04 新增的 1.7.8 训练场导出(见 §0 更新)是当前版本的第一例。

### 4.2 A vs C:PairCorroborated 作为条件会毁掉池

| 导出 | A poolTotal | C poolTotal | 保留比例 | corroborated 命中 | C 后 top1 | 典型角色位移 |
|---|---|---|---|---|---|---|
| 411001_115417 | 162,689,426.88 | 20,986.30 | **0.013%** | 61 | ルナリス→ネーフェ＝ジアー | ルナリス 51,955,401→368,775(1→7) |
| 411001_041309 | 177,006,361.86 | 16,124.32 | **0.008%** | 57 | ルナリス→ネーフェ＝ジアー | ルナリス 51,403,530→379,990(1→8) |
| 700817_114821 | 231,197.83 | 0.00 | **0%** | 4 | 不变([賢導]トレイラ) | [眩愛]チェイシィ −11,567.89 |
| 9999_042458 | 709,968.43 | 0.00 | **0%** | 1 | 不变 | リヴァシー −157,241.74 |

结论:pairCorroborated 在这些导出里几乎恒为 false(live-same 路径的“游戏自身记录是否恰好等于本次伤害”
大多取不到),它是**佐证可得性**标志,不是“配对正确/错误”的判据。**不能**用它当闸门。

### 4.3 B 的作用机制(为什么角色排名不变)

在唯一生效的 3 份训练场上,B 的组件级增量恒为:

    component deltas: maxBase = maxSelf = pairUntrustedCredit, maxAssist = 0.00, anyAssistMoved = False

即不可信命中携带的 fold **全部归攻击者自己**(self rule)。把 fold 置零后,攻击者的 selfRuleCredit
等额减少、baseCredit 等额增加,**totalCredit 不变**;因此角色 totalCredit/totalShare/排名 0 变化,
只有这些自身规则的 **damageEquivalent** 下降(如 225530:魔法与ダメージ+10% −14,033.30、炎の剣 −8,343.15、
エイボンの指輪 −7,016.65,合计 = −29,393.10)。

---

## 5. 三种口径差异排名(节选;完整见 json)

- **B vs A(同分母)**:所有 8 份导出的 maxAbsActorCreditDelta = 0.00、maxAbsActorShareDelta = 0.0000pp、
  top1Changed=False、top3SetChanged=False、rulesRemoved=0。仅 3 份 1.5.x 训练场的规则当量下降
  (|Δ| 最大 14,033.30),以及 poolTotal 下降 4.5%–15.2%。
- **C vs A(同分母)**:全语料 rulesRemoved=33~34(411001)、7(700817/9999);角色位移巨大且方向由
  “谁持有被删掉的池”决定(atkadd/global/madness 持有者暴跌,纯直伤角色暴涨)。**这是伪差异**。
- **B* vs A / C* vs A**:分母改变,已在 json 中标 denominatorChanged=true,只作参考,不用于排名比较。

---

## 6. 必需诊断字段(每份导出)

    pairTrustedHits, pairUntrustedHits, pairCorroboratedHits,
    pairUntrustedHitsWithFolds, pairUntrustedDamage, pairUntrustedDamageWithFolds,
    pairUntrustedCredit, poolTotalA,
    outOfDomainUntrustedHits, outOfDomainUntrustedHitsWithFolds, outOfDomainUntrustedDamage, outOfDomainUntrustedPool,
    pairCensus, outOfDomainPairCensus, pairStringCensus

每口径另有:analyzableDealt / hits / poolTotal / attributedDamage / creditedShare / folds / calcMissingHits /
foldDropped / negativeLines / reasonCounts / excluded{foldHits,foldDamage,foldPool,subsetHits,subsetDamage} /
identityOk / identityMaxError / actors[](含 totalCredit、totalShare、rank)/ rules[](含 damageEquivalent、hits、folds)/
links[] / unattributed[]。

---

## 7. 失败用例(fail-first 证据)

本任务是诊断,**不改插件行为**,因此“修复”指“把诊断实现改到忠实”。失败用例是**负控制**:
故意把 calc.atkAdd 从 fold 词表里去掉(一个极可能发生的实现错误),同一 cross-check 必须报不一致。

**运行命令**:python pairtrusted_impact.py --selftest

    [PASS] T1 A-variant matches plugin contribution section :: status=OK mismatches=0
    [PASS] T2 broken variant (atkAdd dropped) is rejected :: status=MISMATCH mismatches=64
           first detected: diagnostics.poolTotal offline=141778213.69973457 plugin=162689426.8812
    [PASS] T1b A-variant matches contrib.aggregate.analyze :: core analyzable=190889625.0000 pool=162689426.8812 hits=6182 | mine analyzable=190889625.0000 pool=162689426.8812 hits=6182
    [PASS] T1c core cross-check on a 1.5.5 export (no contribution section) :: status=OK mismatches=0
    [PASS] T3 every 1.7.x export has zero IN-DOMAIN pairUntrustedHits (B is a no-op) :: checked=11 offenders=[]
    [PASS] T3b in-domain untrusted hits exist only in pre-1.7 training exports :: offenders=[('battle_9999_20261003_205129.json', 19), ('battle_9999_20261003_225530.json', 9), ('battle_9999_20261003_225647.json', 5), ('battle_9999_20261004_012138.json', 6)]
    [PASS] T4 training untrusted hits exist but are all OUT of domain (B no-op there too) :: out-of-domain=104 (with folds=104, would-be pool=1958.98) in-domain=0
    [PASS] T4b B moves exactly zero pool on 411001_115417 :: poolA=162689426.881162 poolB=162689426.881162
    [PASS] T5 criterion C retains <1% of A's pool on 411001_115417 :: C pool=20986.30 A pool=162689426.88 retained=0.012900%
    selftest: 9/9 passed

> 注:上列为本文发布时的 9 项;2026-10-04 加入训练场 T3c/T4c 后 `--selftest` 为 11/11,且 T3 已限定为 NON-TRAINING 导出(见 §0 更新)。

- **修复前(错误实现)**:T2 报 MISMATCH,mismatches=64,首个字段 diagnostics.poolTotal 141,778,213.70 vs 162,689,426.88。
- **修复后(忠实实现)**:T1/T1b/T1c 全部 OK,mismatches=0;9/9 通过。
- T3/T3b/T4/T4b/T5 是**可证伪的语料断言**:若某个 NON-TRAINING 1.7.x 导出出现 in-domain 不可信命中,T3 立刻失败;
  若 PairCorroborated 变得常见,T5 也会失败。

---

## 8. 运行命令(可照抄)

    $py = 'C:\Users\24134\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe'
    $env:PYTHONIOENCODING='utf-8'
    cd D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work

    & $py pairtrusted_impact.py --selftest          # 失败用例/守卫,9 项
    & $py pairtrusted_impact.py --survey            # 8 份主表完整 A/B/C/B*/C* + 26 份 pair 普查
    & $py pairtrusted_impact.py --all --survey      # 26 份全部完整明细

输出:_dpsm_work/pairtrusted_impact_report.json、_dpsm_work/pairtrusted_impact_report.txt。

---

## 9. 未修改的边界文件(明确声明)

- **未**修改 _dpsm_work/src/Output/Contribution.cs、ContributionSession.cs、任何 .cs;
- **未**修改 _dpsm_work/contrib/* 任何既有模块、check_export_schema.py、validate.py、crosscheck.py;
- **未**改动 BepInEx 下任何 DLL / .bak / config / 插件版本;
- **未**改动、删除、重命名任何 exports 文件或历史证据;
- **未**执行 dotnet build、**未**部署。

---

## 10. 已知限制

1. **离线**:全部结论为离线重放,不含实机验证;不能替代实机一次验收。
2. **domain 由导出字段判定**:本脚本按 atkKey/atkTeam/actors[].team 复现 Contribution.cs:321 的域;
   这正是插件 crosscheck 已验证过的同一判据(T1 在 1.7.4/1.7.6 上 0 mismatch)。
3. **训练场特殊模式**:9999 的 ×0.03 / sub-unity 规则本任务不解析,仅在结论中标注“训练场,不可与普通任务横向比较”。
4. **C* 的极小样本**:C 口径下 corroborated 命中仅个位数到 61,统计意义有限;本报告只用它证明“C 不可用”,
   不用它推断任何真实角色强弱。
5. **out-of-domain 的“若纳入池”**:只按 fold 直接算池,未做归属解析(那些命中当前不进贡献表)。
6. 未验证 GivenTalent=false 等其它配置分支对 fold 的影响(不属本任务)。

---

## 11. 后续依赖

- **P0-B**(数学字段/覆盖率):本报告的 excluded 口径建议与 analyzableDealt vs totals.dealt 的
  reconciliationGap 一并对齐;C 的结论说明**不应**在 P0-B/P1 中引入 PairCorroborated 作为信任条件。
- **P0-D**(任务适用性):本报告的 modelApplicability 证据可直接引用——1.7.x **主任务(非训练场)** pair 质量干净;
  训练场的 in-domain 不可信构成见 §0 更新(1.5.x 3 份 + 1.7.8 的 1 份)。
- 若未来把攻击者解析域扩大到敌方或不可解析命中(Contribution.cs:321 放宽),必须重新跑本脚本:
  届时 9999_042458 的 104 击 live-age(池 1,958.98)与 205129 的 92 击(池 3,473.85)会进入,B 才会真正生效。
