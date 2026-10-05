# 贡献表完整报告

> 目的:以后任何一次战斗数据分析,都从这份文档出发。它把「贡献表」的**每一种产出面、每一步计算、每一个字段、每一条恒等式、每一处已知失效边界**写清楚,并把每句话的证据等级标出来。
>
> 证据等级:【实测】= 导出数据/运行日志/代码可直接证实;【离线重放】= 用 exports 复算得出;【推断】= 由代码读出的模型意图/假设。
>
> 版本绑定:插件 **1.7.10**(DpsMeter.dll 386,560 B,SHA256 前缀 `BF2F174A`);本报告的修复前基线是 **1.7.6**,1.7.7 的列宽修法、1.7.8 的五个任务、1.7.9 的两处修正见 §9.6/§9.8,1.7.10 的闸门与诊断修正见 §9.9。
> **1.7.7 已上线**:§4.1 的列宽修法已实施,§9.1-6 已闭合(离线守卫 13 份/441 行 0 违规;`--selftest` 用 1.7.6 语义反证 164 处违规)。
>
> **1.7.8 已上线**(386,048 B,SHA256 前缀 `0B339836`):按 `CONTRIBUTION-REVIEW-NEXT-STEPS.md` 分派五个任务 ——
> **P0-A** 验证闸门状态机/退出码、**P0-B** 数学字段与覆盖率(§9.1-2 → `totalDamage = analyzableDealt` + `damageLedger`),
> **P0-C** 配对影响量化(§9.1-5 结案)、**P0-D** 任务适用性(26 份时 full 10 / partial 9 / not_comparable 7;29 份重跑 = **full 12 / partial 9 / not_comparable 8**,新三场被分类器吸收、无新类别)、
> **P1-A** atkadd self 改 actor key、**P2-A** 上一场缓存(§9.3 两条已修)。
> **本报告也撤回了两条自己的错误发现**:§9.7(`attrMult` 被当成漏折)与 §9.1-5(域外配对被当成进归属)。两条的根因相同 —— **没有先确认定义域**。
> 数据锚点:`battle_411001_20261004_115417.json`(1.7.6,quest 411001,119.07s,结果 Lose,6182 击)。
> 报告生成时 exports 共 **26 份**;1.7.9 核对时已增至 **29 份**:411001(21) + 700817(1) + 训练场 9999(7);版本跨 1.5.3 → 1.7.8。
>
> **现状(2026-10-04 RF0 批次)**:插件 **1.7.11**(DpsMeter.dll 387,072 B,SHA256 前缀 `36EC96D4`);批次语料 = **冻结快照 35 份**(`batch-inputs-rf0.json`:411001×25 / 训练场 9999×9 / 700817×1;**22 份含 `contribution` 段** = schema 1.0×13 / 1.1×9;13 份无段 = 1.5.3–1.5.5;1 份 1.6.0 不可复算)。**本文档其余数字是各自时点的快照,不要当现状读。**

---

## 0. 一句话结论

贡献表是**同一个计算核心**(`Contribution.Compute`)的三种呈现:游戏内 F5 页、导出 JSON 的 `contribution` 段、离线分析报告。三者的数字在 1.6.1 以后**逐字段一致**(报告生成时 26 份;1.7.9 核对时 29 份批量 crosscheck:除 1.6.0 一份外全部 OK);
但三者的**字段集不同**(离线是超集);`schemaVersion` 自 1.7.8 起三处统一为 `1.1`,由 `producer` 区分插件/离线(此前「插件 1.0 vs 离线 0.1-draft」的不一致已消除)。**列对齐**:1.7.6 有 3 类按名字宽度溢出的行,1.7.7 已修并留离线守卫。另有 13 份 1.5.x 导出根本没有 `contribution` 段、1 份 1.6.0 与离线复算不一致(已知)。

**用之前先记住三条**:
1. 贡献表的分母是「我方 team 1 且攻击者可解析」的伤害,**不等于 `totals.dealt`**。**1.7.8 起文件自己把差额拆开了**:`damageLedger.totalsDealt = analyzableDealt + outsideTeamDealt`(精确)、`unknownAttackerDealt = totals.unattributedDamage`(精确)、`reconciliationGap` 只剩「无事件可解释」的部分(1.7.6 场实测 gap=0)。读旧档(≤1.7.7)没有这个块,差额里含他方伤害,不能直接当 excludedDamage。
2. `总贡献` 全队相加 = 可分析伤害(不是「他打出的伤害」);想看他打出的,用 `直接伤害/直接占比` 两列,两者**不可相加**。
3. 引用辅助数时说「对数份额口径」,不要说「边际贡献」:atkadd 多给出者时 `∏f_g ≠ P/(P−ΣdP_g)`(§9.2)。

---

## 1. 贡献表的三张脸(同核,不同壳)

| 呈现面 | 入口 | 写出者 | schemaVersion | 字段集 |
|---|---|---|---|---|
| 游戏内 F5 页 | `Ui/OverlayUGUI.Rows.cs:352` `AppendContributionTable` | 内存对象直接绘制 | 无 | 表格列(见 §3.1–3.4) |
| 导出 JSON 的 `contribution` 段 | `Output/ExportService.cs:579-580` → `Output/ContributionSession.cs:145` → `Output/Contribution.cs`(AppendJson) | 插件 | `1.1`(`producer=plugin`) | 精简(见 §3.5) |
| 离线分析报告 | `contrib/run.py` → `contrib/report_text.py` / `report_json.py` | Python | `1.1`(`producer=offline`) | 超集(见 §3.6) |

- **共同核心**:`Output/Contribution.cs:303 Contribution.Compute(hits, roster, team)`,与 Plugin/IL2CPP 无依赖,因此 `recon_probe` 可以离线断言它。【实测】
- **共同输入谓词**:`bool useFolds = Plugin.CfgReconcileCalc`(`OverlayUGUI.Rows.cs:356`、`OverlayCore.cs:169`、`ExportService.cs:579` 三处同式),且 `ExportService.cs:304` 用 `e.Calc.Valid && CfgReconcileCalc` 决定是否写 `calc.fold`。**页与文件描述的折叠集合因此必然相同**;关掉 ReconcileCalc 时 F5 页显示「不可用」而不是 0(`ContributionSession.cs:184-191`)。【实测】
- **UI 复用**:`OverlayUGUI.Rows.cs:1021` 与 `OverlayCore.cs:169` 也用 `ContributionSession.Get`(详情页/常驻页的贡献小表),同一缓存(`ContributionSession.cs:160-176`,1 秒节流 + 事件数变化才重算)。【实测】
- ✅ **schemaVersion 已统一(1.7.8)**:插件段与离线都写 `1.1`,`producer`(`plugin`/`offline`)区分实现,`method=log-share/1` 仍标算法版本。图 1.0 的旧档仍在,`check_export_schema.py` 按**段内 `schemaVersion`** 门控(不是按插件版本)。**下游解析仍以 `method` + 字段存在性为主**。【实测】

---

## 2. 组成链路:从一次命中的伤害到贡献表里的数字

### 2.1 命中捕获 → fold 列表(插件侧,逐击)

| # | 环节 | 位置 | 输入 → 输出 | 证据 |
|---|---|---|---|---|
| 1 | 伤害结算钩子 | `Hooks/DamageCalculaterHooks.cs`(入口) | 游戏结算 → 调用链构造 | 【实测】(调用关系见 `CompositionProbe.Chain.cs:27/37`) |
| 2 | 逐击构造 | `Composition/CompositionProbe.Chain.cs:27,37` `BuildChainParts` | `DamageCalculater` + 受击者 + 最终伤害 → 构成文本 + `CalcBreakdown` | 【实测】 |
| 3 | 攻击力读数 | `CompositionProbe.Chain.cs:423` `brk.AttackPower = shownAtk` | 显示用攻击力 P | 【实测】 |
| 4 | 攻击力加算项 | `CompositionProbe.Chain.cs:584-613` `BuildBuffText(..., atkAddItems)` + `ReadAtkItem`(:978) | 攻击者身上的 攻击力 条目 → `AtkAddItem` 列表 | 【实测】 |
| 5 | atkadd fold | `CompositionProbe.Chain.cs:523` `AtkAddFold.Compute(brk.AttackPower, ActorKeyOf(atk), Aggregator.NameOf(atk), atkAddItems, brk.AtkAdd)`(1.7.8 P1-A 起 self 由 **actor key** 判定,名字只是计数回退) | P + 条目 → 每个**给出者**一行 `AtkAddRow`(factor) | 【实测】 |
| 6 | 折叠溢出计数 | `CompositionProbe.Chain.cs:254` `brk.FoldDropped = fctx.StepsDropped + fctx.CancelsDropped`;上限见 `Model/FoldStep.cs:185,217`(`MaxSteps`) | 丢弃的 fold 数 | 【实测】 |
| 7 | 折叠容器 | `Model/CalcBreakdown.cs:179` `List<FoldStep> Fold`;`CalcBreakdown.cs:185` 附近的 `List<AtkAddRow> AtkAdd` | 每击的自带 fold + atkadd fold | 【实测】 |
| 8 | 导出 | `Output/CalcReconcile.cs` `AppendEventCalc` | `calc.fold` / `calc.atkAdd` 逐字段进入 JSON | 【实测】 |

### 2.2 模型输入桥(唯一同时认识两边的地方)

`Output/ContributionSession.cs:41 BuildInputs`:`BattleSession` → `List<ContributionHit>` + `List<ContributionActor>`。

- 只取 `e.Type == "dmg"`(`:50`);攻击者身份**只认 `AttackerKey`**,不做名字 join(`:54`,注释 `:35`)。
- fold 只在 `h.HasCalc && useFolds && e.Calc.Fold != null` 时读(`:58`);**atkadd 从 `e.Calc.AtkAdd` 读入同一个 fold 列表**(`:79-95`)——不重算,所以「页/文件/离线」不可能对给出者各说各话。【实测】
- actor 能力持有者来自**导出写名册用的同两个来源** `a.Roster` + `a.TalentTable`(`:115-130`),这是 byUnit/持有者解析的键空间。【实测】

### 2.3 atkadd 通道:队友给攻击力怎么变成归属

`Model/AtkAddFold.cs:121 Compute(attackPower, selfKey, selfName, items, into)`(1.7.8 P1-A 加了 `selfKey`):

1. 先做**带符号总量**:`r = Σ(±Rate)`,`a = Σ(±Actual)`(`:110-118`)。
2. 守卫:非 Rate/Actual → `SkippedType`;同 `(type,value)` 出现两个不同给出者 → `SkippedCollision`(`:130-138`);`r <= -100` / `1+r/100 <= 0` / `P <= a` / `B <= 0` → `SkippedGuard`(`:140-145`)。
3. 前提公式(**推断**):`P = B·(1+r/100) + a`,反解 `B = (P-a)/(1+r/100)`(`:141-144`)。
4. 对每个**非自己**的给出者 g:`dP_g = B·r_g/100 + a_g`,`f_g = P/(P-dP_g)`(`:174+`);自己给自己的值不计(`SelfValues`,`:159`),因为那本来就留在 baseCredit。
5. 输出 `kind = atkadd`(`AtkAddFold.cs:64`),`byUnit = 给出者名`,于是归属**复用既有的 byUnit 阶梯**,不需要任何新 KPI。【实测】

守卫不是装饰:1.7.6 最新一场实测计数器 `{"hits":6191,"emitted":6006,"selfValues":7003,"skippedGuard":131,"skippedCollision":50,"skippedUnowned":0,"skippedOwnerNull":0,"skippedNegative":0,"skippedType":0}` —— **131 次按前提失败被拒、50 次按给出者冲突被拒**,这些命中的加算**不算给任何人**。

### 2.4 分池(log-share/1 的全部数学)

`Output/Contribution.cs:317-401`:

1. 过滤 fold:`factor<=0`→`ZeroFactor`,`|factor-1|<1e-12`→`NoopFactor`,`factor<1`→`SubUnity`(减益不进池)(`:332-334`)。
2. `M = ∏ factor`(`:339-340`);`base = D/M`,`pool = D - base`(`:343-346`);每条 fold 的份额 `share_i = pool · ln(f_i)/ln(M)`(`:348-349`)。
3. 落账:`ac.Direct += D`、`ac.Base += base`(`:353-355`);`Resolve` 得到 owner:owner==attacker → `selfRuleCredit`,`owner!=attacker` → 记给自己 `receivedAssist`、记给 owner `assistCredit`,并写一条 link(`:377-391`);owner==null → `unattributed` + reason(`:368-375`)。
4. `attributed = Σ(base+self+assist)`(`:404-405`);角色表/规则表/关系表各自按量降序(`:408,418,424`)。

**口径要点**:份额是 `pool × ln(f)/ln(M)`,**不是**逐击精确归属;它只保证「Σ份额 = pool」和「Σ角色 = 可分析伤害」。所有 fold 的倍率都>1,所以每条 share>0。【实测】

### 2.5 归因阶梯(决定 owner 是谁)

`Output/Contribution.cs:227-261 Resolve`:

1. `byUnit` 非空:`NameUnique` 命中 → `byUnit`;队伍内重名 → `byUnit_ambiguous`(拒绝);全导出有名字但不在我方 → `byUnit_outside`(拒绝);查无 → `byUnit_unknown`(拒绝)。(`:229-236`)
2. `kind` = text/talent:从 `origin` 抠 `abilityId`(`:161-177`),按 `ByAbilityId` 找持有者:唯一 → 归它;多个且含攻击者 → 归攻击者;多个不含 → `ambiguous_multi_holder`(拒绝);无 id → 归攻击者 `attacker_default`。
3. `kind` = global:从 `label` 的 `[名字]` 抠出来按 `ByAbilityName` 找:唯一 → 归它;否则 `global_ambiguous`(拒绝)。
4. 其它 kind → `unknown_kind`(拒绝)。

**原则**:任何歧义都**拒绝归属**(记入 `unattributed` + reason),绝不猜。1.7.6 实测 `attacker_default_fallback = 0`,即**没有一条 fold 是靠兜底落账的**。

我把 13 份带 `contribution` 段的导出全部取了 `reasonCounts`,**只有 4 个码**:`byUnit` 101,846 / `global_name_unique` 91,321 / `ability_holder_attacker` 71,833 / `ability_holder_unique` 67,005;`attacker_default`、`byUnit_unknown`、`unknown_kind`、`byUnit_ambiguous/outside`、`global_ambiguous`、`ambiguous_multi_holder` **一次都没出现**。【实测】

注意区分:`ability_holder_attacker`(71,833 折)不是兜底——它是「该能力在队内有多个持有者、其中一个是攻击者 → 归攻击者」的**设计规则**(`Contribution.cs:243-247`);真正的兜底是 `attacker_default`(`:249`,仅当 text/talent 的 origin 抠不出 abilityId)。

---

## 3. 字段字典

### 3.1 F5 表1「角色贡献」(可加和)

| 列 | 宽 | 值 | JSON 字段 | 定义 |
|---|---|---|---|---|
| 角色 | 16 | 名字截到 16 列 + 使魔标记 `*` | `actors[].name` / `.summon` | 使魔是独立 actor,不并入主人 |
| 总贡献 | 11 | `N0` | `totalCredit` | = 自身 + 他人因你;全队相加 = `analyzableDealt` |
| 占比 | 8 | `F2%` | `totalCredit / analyzableDealt` | **与总贡献同口径**,全队相加 100% |
| 自身 | 11 | `N0` | `baseCredit + selfRuleCredit` | 自己命中里归自己的份额(`Σ D/M` 与自己的 fold),1.7.11 起合并为一列 |
| 他人因你 | 11 | `N0` | `assistCredit` | 别人因他多打出来的份额(记他名下,不是他打出的) |
| 被队友分走 | 11 | `N0` | `receivedAssist` | 他的命中里由别人的倍率拿走、记在提供者名下的部分(1.7.11 起进表) |
| 直接占比 | 9 | `F2%` | `directDamage / analyzableDealt` | **另一套口径**:他实际打出的伤害占比,不与总贡献相加 |
| 命中 | 6 | `N0` | `hits` | 他作为攻击者的 dmg 事件数 |

两条可对账关系(1.7.11 起由 `check_export_schema.py` 逐角色强制;17 份带贡献段的导出 / 181 行实测,最大偏差 0.0001):
**总贡献 = 自身 + 他人因你**;**直接打出 = 自身 + 被队友分走**。

- 合计行(`Rows.cs:564`):`attributedDamage` + 「自身合计」+「他人因你合计」+「被队友分走合计」。
- 未归因行(`Rows.cs:572`):`未归因 X(Y%) 未计入任何角色`。
- 统计行(`Rows.cs:577`):`可分析伤害` `倍率池(poolTotal)` `命中` `折叠(folds)` `无构成(calcMissingHits)`。
- 行过滤:`Total<=0 && Direct<=0` 的行不显示(`Rows.cs:527`)。

**历史**:1.7.6–1.7.10 的三列为 `基础(11) | 自身规则(11) | 辅助(11)`。拆开显示会让「自身规则」看起来像与「辅助」同级的外部加成,
而它其实是该角色自己那半边的另一半;同时 `receivedAssist` 从未进表,读者无法解释「直接打出」与「总贡献」为何不同。

### 3.2 F5 表2「规则当量」(取当量降序前 12 条)

| 列 | 宽 | 值 | JSON 字段 |
|---|---|---|---|
| 规则 | 22 | 名字截到 11 字 + `…` | `rules[].ruleName`(`RuleName()`:取 label 里 `[..]` 的内容,否则 label,否则 origin,`Contribution.cs:152-159`) |
| 通道 | 8 | kind | `rules[].kind` |
| 侧 | 5 | side | `rules[].side` |
| 持有者 | 14 | 名字截到 7 字 + `…` | `rules[].ownerName` |
| 命中 | 7 | 事件数 | `rules[].hits`(去重后的事件数,不是 fold 数) |
| 折叠 | 7 | fold 数 | `rules[].folds` |
| 当量 | 12 | `N0` | `rules[].damageEquivalent` = 该规则份额之和 |

**通道取值**(1.7.6 实测 `channelCensus`):`text`(技能文本)、`talent`(天赋)、`given`(赋予)、`madness`(狂气)、`global`(全局规则)、`atkadd`(攻击力加算)。
**侧取值**:`atk`(攻击方乘区)/ `vic`(受击方乘区)。1.7.6 实测:`atkadd/atk 6006`、`given/vic 4786`、`global/vic 4299`、`madness/atk 900`、`madness/vic 3268`、`talent/atk 26`、`text/atk 12396`。【实测】

### 3.3 F5 表3「辅助关系」(提供者→受益者,取前 12 条)

`提供者(14) →(4) 受益者(14) 命中(7) 当量(12)`,对应 `links[]` 的 `fromKey/toKey/hits/amount`。
**这是唯一会跨角色移动的份额**:`assistCredit(受益者) == Σ links[from=提供者,to=受益者].amount`。【实测】

### 3.4 页脚与其它文案

- 统计行 `(* = 使魔)  可分析伤害 X  倍率池 Y  命中 Z  折叠 N  无构成 M  (自身+被队友分走=直接打出;…)`(`Rows.cs:577`)。
- F6 明细块页脚 `(每秒最多重算一次;数字与导出 contribution 段同源;自身=基础+自身规则,自身+被队友分走=直接打出)`(`Rows.cs:1247`)。
- 口径四行(`Rows.cs:516-519`):`自身 = 基础 + 自身规则`;`总贡献 = 自身 + 他人因你`;`直接打出 = 自身 + 被队友分走`;
  另两行定义 自身/基础/自身规则 与 他人因你/被队友分走,并说明 直接占比 与总贡献口径不同、分池按倍率对数份额。
- 表 1 标题 `【角色贡献】(对数份额口径;总贡献 = 自身 + 他人因你)`(`Rows.cs:514`)。
- F6 明细块(`Rows.cs:1188` 标题、`:1218` 第二行):`自身 X(基础 A + 自身规则 B)   他人因你 C   被队友分走 D`。
- 标题 `总贡献 F5返回 任务 <QuestId> <时长>`(`Rows.cs:492`)—— 取自 `ContributionView.QuestId/Seconds`,不再是实时会话(1.7.6 修)。

### 3.5 导出 JSON `contribution` 段(插件;**1.7.8 起 `schemaVersion 1.1`**,1.6.1–1.7.7 为 `1.0`;字段名一个都没改)

```
contribution{
  schemaVersion, method=log-share/1, damageBasis=dealt,
  totalDamage = analyzableDealt,                       <-- 1.1(1.7.8)起;1.0 曾是 analyzable+unattributed
  coverage{... excludedDamage, analysisDamageCoverage, creditCoverageWithinAnalyzed, overallAttributedCoverage}
  damageLedger{totalsDealt, events, analyzableHits, outsideTeamHits, outsideTeamDealt,
               unknownAttackerHits, unknownAttackerDealt, eventSumAll, reconciliationGap},
  attributedDamage, unattributedDamage,
  coverage{creditedShare, unattributedShare, analyzableDealt, hits},
  source{pluginVersion, quest},
  actors[]{key,name,kind,summon,directDamage,baseCredit,selfRuleCredit,assistCredit,totalCredit,receivedAssist,hits},
  rules[]{ruleName,kind,side,ownerKey,ownerName,hits,folds,damageEquivalent},
  links[]{fromKey,toKey,amount,hits,folds},
  unattributed[]{reason,amount,folds},
  diagnostics{foldAccounting{total,zeroFactor,noopFactor,subUnity}, poolTotal, calcMissingHits,
              foldDropped, foldDroppedHits, negativeLines, reasonCounts{}, knownLimits[]}
}
```

同一文件里与贡献表有关的**其它顶层段**:`calc.fold` / `calc.atkAdd`(逐击原始输入)、根 `atkAdd`(通道计数器)、根 `paramOwners`(参数表 owner 普查)、根 `reconcile`(命中对账)。

### 3.6 离线报告独有字段(超集,`report_json.py`)

| 字段 | 含义 |
|---|---|
| `coverage.byUnitGiven` / `byUnitMadness` | `[给出侧, 计入侧]` 双计数,两者必须相等 |
| `coverage.keyCoverage` | 攻击者 key 可解析的命中占比(非份额) |
| `coverage.ownerPaths` | 四类归属路径的 fold 计数(byUnit / abilityId / abilityName / 兜底) |
| `coverage.foldAccounting` | total = resolved + unresolved |
| `totals{}` | `dealt` / `analyzableDealt` / `eventSumAll` / `analyzeVsTotalsDelta` / `absorbed` … **插件段目前没有这个块** |
| `actors[].team/directShare/totalShare/rules[]` | 每角色的分规则明细 |
| `rules[].origin/resolved/coverage/reasons/beneficiaries[]` | 规则的 origin、是否解决、覆盖率、拒绝原因、**受益者列表** |
| `links[].fromName/toName/rules[]` | 关系两端的名字与涉及规则 |
| `unattributedByKind` | 未归因按通道汇总 |
| `diagnostics.channelCensus` | 通道×侧普查 |
| `diagnostics.residualBuckets` | `M` 的分布桶(见 §7) |
| `diagnostics.checks[]` | I1–I10 的问题清单(见 §6) |

---

## 4. 布局:列位图与实测行宽

对齐假设(`Rows.cs:314-342`):CJK=2 列、其余=1 列;`PadR/PadL` 把内容补到**恰好** N 列。1.7.6 起 F5 页整体改用**等宽 CJK 字体**(`OverlayUGUI.Pool.cs:128 GetMonoFont`,候选 NSimSun→MS Gothic→SimSun),因为比例字体下这套补齐只在 1:2 网格上成立。【实测】

**表1 列起点(实测,行宽 85)**

```
角色@2  总贡献@18  占比@29  基础@37  自身规则@48  辅助@59  直接占比@70  命中@79
```

**表2**:行宽 77(`规则@2 通道@24 侧@32 持有者@37 命中@51 折叠@58 当量@65`)。
**表3**:数据行 53(`提供者@2 →@16 受益者@20 命中@34 当量@41`);**表头 63**,多出的 10 列是 `主要规则` 表头,但**没有任何数据行填它**(`Rows.cs:456-461` vs `:468-473`)。
**合计行**(1.7.6 时 88 列,比表1 的 85 宽 3,`未归因…%` 落在第 79 列起)**→ 1.7.7 已修为与表1 同几何的 85 列**,未归因另起一行/并入合计(`Rows.cs` 合计行构造)。

### 4.1 ⚠ 实测:1.7.6 仍有 3 类行按名字宽度溢出

根因:`Fit(s,max)`(`Rows.cs:1081-1085`)按 **UTF-16 字符数**截断,`PadR/PadL` 按**显示列**补齐。两者单位不同,名字显示宽度超过列宽时 `PadR` **不会截短**(`w>=width` 时原样返回),整行右移。

用 1.7.6 最新一场的真实数据离线重放(`Fit` 按字符数、补齐按显示列,与 C# 逐行等价):

| 表 | 行 | 显示宽 | 偏差 |
|---|---|---|---|
| 表1 | `エヴァラス・フラウ`(18 列) | 87 | +2 |
| 表1 | `生ける炎 クトゥグア`(19 列) | 88 | +3 |
| 表2 | 持有者 `ネーフェ＝ジアー`(15 列,`Fit(…,7)` 得 7 字+`…`) | 78 | +1 |
| 表3 | 受益者 `ネーフェ＝ジアー` / `エヴァラス・フラウ`(15 列) | 54 | +1 |

其余 26 行全部等于名义宽度。

S4 独立在**全部 13 份**带段导出上重放同一算法,结论一致且更完整:**角色行 140 行里 25 行(17.9%)标签超 16 列,最大 19 列;规则行 340 行里 78 行(22.9%)持有者列超 14 列,最大 15**。另有使魔标记:名字恰好 8 个全角字时 `*` 拼在 `Fit` 之后,17 > 16 也会右移(`Rows.cs:396`)。

**结论:1.7.6 的等宽字体修复解决了「比例字体逐行漂移」,但没有解决「名字比列宽长」这一类行。**
**已修(1.7.7)**:`Fit` 改为按**显示列**截断(截断标记改 ASCII `..`,因为 U+2026 在不同字体是 1 或 2 列),全部调用点改传真实列宽(16/22/14),使魔 `*` 并入 `Fit`,`Cell()` 把 `×` 归一为 ASCII `x`(13 份导出全部名称里唯一的宽度可疑字符)。列宽未动。
验证:离线守卫 [check_contribution_layout.py](<_dpsm_work/check_contribution_layout.py>) 在 **13 份导出 / 441 行**上 **0 违规**;`--selftest` 用 1.7.6 的 `Fit` 语义重放同一算法得到 **164 处违规**(证明守卫不是空转)。

---

## 5. 版本兼容矩阵(报告生成时 26 份;1.7.9 核对时 29 份的可信度清单)

批量 crosscheck 实测(逐份对比「文件里的 contribution 段」与「用同一份文件的 calc.fold 离线复算」,容差 rel 1e-6 / abs 1.0):

| 版本 | 份数 | `contribution` 段 | `factor` 小数位(实测) | crosscheck | 可否作分析基准 |
|---|---|---|---|---|---|
| 1.5.3 / 1.5.4 / 1.5.5 | **13** | **无** | 全部 4 位(截断) | `ABSENT` | 需离线复算;**不可从文件验证与原内存值的一致性** |
| 1.6.0 | 1 | 有 | 4 位(截断) | **MISMATCH(58 字段,~1e-5)** | ❌ 不可作基准(已知:HANDOFF:374,1.6.1 已修往返精度) |
| 1.6.1 / 1.7.0 / 1.7.2 / 1.7.3 / 1.7.4 / 1.7.5 | **9** | 有 | 往返精度(1/2/15/16 位混合) | **OK,mismatches=0** | ✅ |
| 1.7.6 | **3** | 有 | 往返精度 | **OK,mismatches=0** | ✅ |

- `factor` 小数位分布是**判别文件精度的唯一现成手段**:1.5.x/1.6.0 的因子全为 4 位小数(统计上不可能自然出现),1.6.1+ 出现 15–17 位往返值。【实测】
- 1.5.5 的离线复算**可用**:`contrib.run` 在该文件上 `foldResolved=28369`、`ERROR=0`、份额恒等式成立。【离线重放】
- 训练场 9999 的 1.7.5 那份也 OK,但口径特殊(见 §8 I10)。

---

## 6. 恒等式与守卫台账

### 6.1 离线强校验(`contrib/validate.py`,I1–I10)

| 码 | 级别 | 断言 | 1.7.6 最新一场 |
|---|---|---|---|
| I1 | ERROR | 逐击:`credit + 未归因 == D` | OK(最大误差 5.821e-11) |
| I2 | ERROR | 角色:`ΣtotalCredit + 未归因 == analyzableDealt` | OK(相对 9.4e-16) |
| I3 | ERROR | 份额:`creditedShare + unattributedShare == 1` | OK(1.000000000000) |
| I4a | ERROR | `resolved + unresolved == folds` | OK(31681 = 31681 + 0) |
| I4b | ERROR | 无未知 reason 码 | OK |
| I4c/d | WARN | 被排除的 `factor<=0` / `factor<1` | 0 / 0 |
| I4e | ERROR | 无负份额 | 0 |
| I4f | WARN | 无 calc 的命中(按 M=1) | **1** |
| I4g | ERROR | 无 fold 被上限丢弃 | 0 |
| I5 | ERROR | 不把份额记给我方以外 | OK |
| I6 | WARN | 各通道 byUnit 覆盖 | OK |
| I7 | WARN | `analyzableDealt` vs `totals.dealt` | **-94,717(-0.0496%)**;1.7.8 起该差额被台账精确拆为 `outsideTeamDealt`(+`unknownAttackerDealt` 本就不在 `totals.dealt` 内),`reconciliationGap=0` |
| I8 | INFO | 重名(我方内=WARN) | 1(在我方外) |
| I9 | INFO/WARN | 未归因池 | 空 |
| I10 | INFO | 训练场特殊 ×0.03 规则,跨模式不可比 | — |
| — | — | **Σ各通道当量 = poolTotal**(由构造保证) | OK:global 23,785,228 + madness 31,483,657 + atkadd 51,781,995 + text 49,435,387 + given 6,188,292 + talent 14,868 = 162,689,427 ≈ poolTotal 162,689,426.88 |

### 6.2 插件侧守卫(出错时表现为计数而非静默)

- `ClauseStatusRun`/`FoldStep.MaxSteps` 上限 → `CalcBreakdown.FoldDropped`(`Contribution.cs:326` 计入 `FoldDropped*`)。
- `Num()/Share()` 把 NaN/Inf 强制写 0(`Contribution.cs:263-273`):**宁可写 0 也不写非法 JSON**。
- 无构成数据时 `ContributionResult.Usable=false`,UI 显示「不可用」而不是 0(`Contribution.cs:140-144`、`ContributionSession.cs:9-11`)。
- atkadd 的**13** 个计数器(1.7.8 新增 `selfByKey`/`selfByNameFallback`/`nameCollision`/`ownerUnknown`)全部写进根 `atkAdd`(§2.3),且 1.7.8 起有恒等式闸门(§9.5)。
- `paramOwners` 有恒等式 `entriesTotal == entryReads + entriesDropped` 与 `mismatch`(1.7.6 实测 0)。

### 6.3 ⚠ 已发现的断言不一致

1. ~~`knownLimits` 有两套措辞~~ → **已消除**:插件段与离线核心现在都是**同样 6 条逐字节相同**的文案(含 `totals.dealt and the per-event sum differ by ~0.05%` 那条),字典 §7 的示例也是同一份。
2. ~~字典示例 `knownLimits` 是旧文案~~ → 字典现在就是这 6 条。
3. `Rows.cs:306-312` 的注释仍写「值**不带**千分位;带千分位会逐行漂移」——1.7.6 起已改为带千分位 + 等宽字体,**注释过期**。

---

## 7. 实测样例(1.7.6 最新一场,可直接当口径基准)

`battle_411001_20261004_115417.json` · 1.7.6 · 411001 · 119.07s · Lose · 6182 击

```
totalDamage = 190,889,625 = analyzableDealt 190,889,625 + 未归因 0
attributedDamage = 190,889,625   creditedShare = 1.000000   unattributedShare = 0
倍率池 poolTotal = 162,689,426.88(占可分析伤害 85.23%)
折叠 31,681 = 已解析 31,681 + 未归因 0;无构成命中 1
totals.dealt = 190,984,342(与可分析差 -94,717 = 该场 outsideTeamDealt;1.7.8 起由 damageLedger 精确给出,gap=0)
```

| 角色 | 基础 | 自身规则 | 辅助 | 总贡献 | 占比 | 直接伤害 | 直接占比 | 命中 |
|---|---|---|---|---|---|---|---|---|
| ルナリス | 173,406 | 0 | 51,781,995 | **51,955,401** | 27.22% | 347,789 | 0.18% | 44 |
| マッドシーカー | 7,838,203 | 6,496,300 | 28,642,840 | 42,977,343 | 22.51% | 27,184,075 | 14.24% | 1800 |
| ネーフェ＝ジアー | 7,097,166 | 23,817,263 | 0 | 30,914,429 | 16.19% | 56,189,789 | 29.44% | 482 |
| メアリー | 1,792,706 | 23,832,895 | 0 | 25,625,601 | 13.42% | 54,527,319 | 28.56% | 963 |
| ルゥ=ルルサ | 2,780,182 | 2,400,543 | 16,532,983 | 21,713,707 | 11.38% | 12,924,560 | 6.77% | 1107 |
| エヴァラス・フラウ | 3,346,060 | 7,766,699 | 0 | 11,112,759 | 5.82% | 21,025,193 | 11.01% | 186 |
| テトラ | 5,048,867 | 1,403,042 | 0 | 6,451,909 | 3.38% | 18,430,523 | 9.66% | 1443 |
| 生ける炎 クトゥグア | 103,735 | 0 | 0 | 103,735 | 0.05% | 199,195 | 0.10% | 131 |
| クトゥグァ砲台(*) | 9,937 | 7,434 | 0 | 17,370 | 0.01% | 30,591 | 0.02% | 13 |
| クトゥグァ砲台R(*) | 9,937 | 7,434 | 0 | 17,370 | 0.01% | 30,591 | 0.02% | 13 |

**怎么读这张表**:`ルナリス` 只打出 347,789(0.18%),但**总贡献第一(27.22%)**,因为队友的攻击力加算全部由她给出。离线逐条核对:`kind=atkadd` 的 16 条规则**全部** ownerName=ルナリス,`Σ当量 = 51,781,995.190` 与她的 `assistCredit = 51,781,995.190` **相等(差 2.6e-7)**,她名下**没有**任何非 atkadd 规则 —— 她的总贡献 **99.67% 来自 atkadd 通道**。【离线重放】

规则当量前 5:

| 规则 | 通道 | 侧 | 持有者 | 命中 | 当量 |
|---|---|---|---|---|---|
| 母なる変異の飛沫 | global | vic | マッドシーカー | 4299 | 23,785,228 |
| 狂気(受击方) 被伤害×1.50 | madness | vic | ルゥ=ルルサ | 3268 | 18,062,727 |
| 攻击力加算 Actual+300(…)+Rate+10+Rate+400+Rate+7 | atkadd | atk | ルナリス | 1200 | 16,308,231 |
| 狂気 与伤害×2.50(狂気比250) | madness | atk | メアリー | 900 | 13,420,930 |
| 攻击力加算 Actual+200(…)+Actual+300(…)+Rate+10+Rate+400 | atkadd | atk | ルナリス | 292 | 11,101,562 |

最大辅助关系:`ルナリス → メアリー 18,494,209`(963 折叠),`マッドシーカー → ネーフェ＝ジアー 10,480,917`,`ルナリス → ネーフェ＝ジアー 10,066,037`。

**`residualBuckets` 提醒**:6,182 击里 **5,128 击的 M=1**(没有任何 fold)。也就是说**多数命中不参与分池**,贡献表只重分配了「有倍率的那部分」;而 85.23% 的伤害落在池里,说明倍率主要集中在大额命中上。【离线重放】

---

## 8. 复现命令(以后每次分析照抄)

```powershell
# python(本机)
$py = 'C:\Users\24134\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe'
# 工作目录必须是 _dpsm_work(contrib 是包)

# 1) 全流程:文本报告 + 离线 JSON(默认取最新一份非训练导出)
& $py -m contrib.run --json _review\new.json --text _review\new.txt
# 指定文件:
& $py -m contrib.run ..\BepInEx\plugins\DpsMeter\exports\battle_411001_XXXX.json --json ... --text ...

# 2) 文件里的 contribution 段 vs 离线复算(逐字段,唯一的一致性判据)
& $py -m contrib.crosscheck ..\BepInEx\plugins\DpsMeter\exports\battle_411001_XXXX.json

# 3) 恒等式 I1–I10 -> contrib\reports\report_*.json
& $py -m contrib.validate

# 4) 其它:规则普查 / 旧版对比 / 同批对比 / 1.5.5 金标
& $py -m contrib.rule115_census; & $py -m contrib.legacy_diff; & $py -m contrib.compare
& $py -m contrib.tests.test_golden_155; & $py -m contrib.tests.test_samples

# 5) 结构守卫集合(在 _dpsm_work 下)
& $py check_export_schema.py --selftest   # 32 例
& $py v150_validate.py; & $py check_fact_signature.py; & $py check_docs_123.py; & $py refactor_final_check.py
```

**纪律**:导出文件是**唯一**的一手证据;任何结论先问「哪份文件、哪个字段」;改动前后各跑一次 crosscheck + validate。

---

## 9. 审查发现(6 个只读 subagent 的交叉核对)

本节由 6 个只读 subagent(S1 命中→fold 构造 / S2 归属与分池数学 / S3 导出对账口径 / S4 UI 呈现 / S5 验证证据链 / S6 文档一致性)的独立审查汇总,并由我逐条复核。
标记:`✅我复核` = 我用代码重读或离线重放亲自证实;`⚠待复核` = 只有 subagent 给的文件行,我未独立验证。

### 9.1 会直接改变数据解读的(高)

| # | 发现 | 证据 | 对分析的影响 |
|---|---|---|---|
| 1 | **贡献表只覆盖「我方 team 1 且攻击者可解析」的命中**;排除的击**自 1.7.8 起由 `damageLedger` 计数**(`outsideTeamHits/Dealt`、`unknownAttackerHits/Dealt`),此前段内没有任何计数 | `Contribution.cs`(计数后 `continue`)、`ContributionSession.cs:142`(team 写死 1);✅我复核 | 实测训练场 1.7.5:`analyzableDealt 4,351,950` vs `totals.dealt 7,024,899` = **−38.05%**,而 `creditedShare` 仍报 `1.0`。**纪律:看 `overallAttributedCoverage` 而不是 `creditedShare`**;差额的构成看台账三桶 |
| 2 | ~~`totalDamage = analyzable + unattributed` 定义错误~~ → **已修(P0-B,1.7.8)**:`totalDamage = analyzableDealt`,`attributed + unattributed == analyzableDealt` 成为守恒式;新增 `producer`/`damageLedger`/三个覆盖率,并对 1.1 文件单独门控 | `Contribution.cs`(AppendJson)、`contrib/report_json.py`、`check_export_schema.py`;✅我实证:`check_export_schema --selftest` **45/45**、probe 的 P0-B 断言全过 | 语料 13/13 份 U=0,所以旧写法从未被发现;**1.1 的 16 条新用例各自能失败**(含「用旧约定写 totalDamage」) |
| 3 | ~~quest 700817:59 击的 `knownMult=2` 但 `calc.fold` 为空~~ → **本条已撤回**:那 59 击的 ×2 是 **`attrMult=2`(属性相性)**,而属性倍率**按设计不进 fold**,「M=1、无池」是正确行为 | `battle_700817_20261004_114821.json`;✅重放(59/59 全部 `attrMult=2, dealtMult=1, takenMult=1`) | 判定「漏折」必须用 **`prod(fold) ≈ dealtMult·takenMult`**(排除 `attrMult`);`knownMult` 含属性倍率,不能拿它比。详见 §9.7 |
| 4 | **1.6.0 那一份必然对账失败,而默认闸只查最新一份** | `battle_411001_20261004_015919.json` 58 项 MISMATCH;`crosscheck.py:214-219`;✅我批量复核 26 份 | `factor` 在 1.6.0 及以前被截成 4 位小数(`FoldStep.cs:79-86`,1.6.1 改往返精度)。**该文件与其他 1.5.x 文件的 contribution 数字不可当基准**(见 §5) |
| 5 | ~~不可信配对的 fold 照样进入归属~~ → **已量化(P0-C,结论:无影响)**:`Contribution.Compute` 确实不消费 `PairTrusted`,但**域内没有不可信配对** | ✅我独立重放:411001_115417 域内(team1+calc)6,181 击里 `pairTrusted=false` = **0**;700817 = 0;9999_042458 域内 = 0(域外 team2 有 104 击)。P0-C 全量:A 口径与 B(=只用可信配对)**逐字段全等**,poolDelta=0,排名不变 | S1 的「279 击」是**未按域过滤**的全语料计数,那些击在 `Contribution.cs:321` 就被排除了,从未进入归属。**结论:不需要把 `PairTrusted` 加进谓词**;若未来放宽 `:321` 的域,必须重跑 `pairtrusted_impact.py` |
| 6 | ~~1.7.6 列对齐仍不完整~~ → **1.7.7 已修**:`Fit` 改为按显示列截断,列宽/合计行/取整一并打包 | `Rows.cs:1155-1177`;✅我复核(§4.1),离线守卫 0 违规 + 自测反证 164 处 | 修复前:角色行 17.9%(25/140)、规则行持有者列 22.9%(78/340)超宽。**残留**:表3 的 `→` 未归一(表头与数据同量偏移,列间仍对齐) |

### 9.2 潜伏缺陷(当前语料未触发,配置或数据一变就会误伤)

| 发现 | 证据 | 触发条件 |
|---|---|---|
| `GivenTalent=false` 时 given fold 被登记进 `calc.fold` 但**没乘进链条** → `prod(fold) ≠ attrMult·dealtMult·takenMult`,base 偏小、池偏大,给出者拿到用户已关掉的倍率 | `Chain.cs:168` 无条件传 fctx、`Talents.cs:520-524` 无条件 `ctx.Add`、开关只在 `Chain.cs:174-175` | 关掉 General/GivenTalent。⚠推断(未实机) |
| 无 `IsFinite` 守卫:`factor=NaN` 时三层过滤全不命中 → `base=D`、份额全 0 静默;`factor=+∞` → NaN 份额最终被 `Num()` 写成 `0.00` 且不计数 | `Contribution.cs:332-343`、`:263-273`;设计评审 `REVIEW-phaseE-design.txt:298-299` 要求过 | ⚠推断(现网解析源都有界) |
| atkadd 的**多给出者**不满足边际语义:`f_g=P/(P−dP_g)` 对单个 giver 精确,但 ∏f_g ≠ `P/(P−ΣdP_g)`;池内守恒成立,逐 giver 分配不是「移除他后的伤害差」 | `AtkAddFold.cs:191`、`Contribution.cs:340/349` | 任何多 giver 命中(atkadd 占语料折叠量 26–48%)。**引用辅助数时应说「对数份额口径」而不是「边际贡献」** |
| ~~`self` 加算按**名字**判定~~ → **已修(1.7.8 / P1-A)**:改由 `giverKey == selfKey` 判定,key 缺失才回退名字**并计数**(`selfByNameFallback`),同名冲突计 `nameCollision` 且**不再静默丢进 baseCredit**;恒等式 `selfValues == selfByKey + selfByNameFallback` 已成闸门 | `AtkAddFold.cs:192-216`、`ExportService.cs`(4 个新计数器);实测 3 场 1.7.8:fallback=0、nameCollision=0 | 已闭合 |
| `MaxSteps=24` 只丢清单不丢乘积:M 用清单算,`foldDropped` 只导出不补偿 | `FoldStep.cs:179/217`、`Contribution.cs:337`、`CalcReconcile.cs:447` | 单次命中 >24 个 >1 因子(语料 0) |
| `_statusSnaps` 上限 256 且逐出最旧 → 快照丢失后回退 live 状态,静默破坏「用攻击开始时的状态」不变量,且**无计数** | `Status.cs:41,77-83` | 长战/多召唤。⚠推断 |
| atkAdd 抓取上限 40 条且**无溢出计数**:攻击力条目排在 40 个显示级条目之后就不进 atkItems | `Chain.cs:584-621` | 极端叠层。⚠推断 |
| `Contribution.cs:541` 的 `crit has no observation channel` 已过期:会心通道 1.5.0 起存在(`ExportService.cs:271-275` 三态),真实限制是**模型不消费 crit**、会心量留在 baseCredit | ✅我复核 `reconcile` 里有 `critObserved/critObservedYes/critInferred` 字段 | 任何引用 knownLimits 的分析 |
| 3 处数值格式化未走 `InvariantCulture` 会写出非法 JSON(逗号小数文化) | `ExportService.cs:165,262`;其余数值都传了 | 非 `en`/`ja` 文化。语料全为 `.` |
| 4 处字符串未走唯一转义规则,且 `JsonText` 不转义 0x09/0x0B/0x0C | `CalcReconcile.cs:379,388,406`、`ExportService.cs:233,243,263`、`JsonText.cs:18` | 名字/文本含 `"` 或制表符。语料 26/26 严格解析通过 |
| `AtkAddFold.SkippedOwnerNull` 是**死计数器**(只声明/重置/导出,从无自增点) | `AtkAddFold.cs:72,79`、`ExportService.cs:564`;✅我复核(全仓仅这 3 处) | 任何把它当「无主条目数」读的分析 |
| 敌方视角(team 2)无法计算 | `ContributionSession.cs:142` 硬编码 1 | 需要改代码 |

### 9.3 UI 层(影响读数,不影响算法)

| 发现 | 证据 | 影响 |
|---|---|---|
| ~~F5「上一场」可能指向更早的一场~~ → **已修(P2-A,1.7.8)**:新增 `ResolveContributionView`(`OverlayUGUI.Rows.cs:422`),非 live 时用 `Aggregator.History[0].Session` 重算并按场记忆;三个消费点(表格/roster 块/imgui)`OverlayCore.cs:180` 全部改道 | 为什么**不**在战斗结束调 `Invalidate()`:`_cache` 同时是「上一场」的唯一存储,清空会把「错场」换成「暂无战斗数据」。会话生命周期已核:`Aggregator.cs:379` 每场新建 session,`:480` 续场时撤下摘要 ⇒ `History[0].Session` 是该已完成场的稳定对象 | 修前:整场隐藏(F8)或停在 F6/F10 → F5 显示更早一场;修后:显示 `History[0]`。**仍需一局实机确认**(唯一离线无法替代的部分) |
| ~~表2 表头多出「主要规则」列~~ → **已修(1.7.7)**:悬空表头已删除 | `Rows.cs:611`(1.7.7 注释:「the header used to end with 主要规则, a column no data row ever filled」);`check_contribution_layout.py` 537 行 0 违规 | 已闭合 |
| ~~合计行 **88 列**~~ → **已修(1.7.7)**:合计行与表头/数据行同为 **85 列**,`未归因…%` 不再落在 `命中` 列起点 | `Rows.cs:544-566`、`:1215`;`check_contribution_layout.py` 441 行 0 违规 + 自测反证 164 处 | 已闭合 |
| ~~取整两套~~ → **已修(1.7.7)**:F5 金额列改用与 roster 贡献块相同的 `:N0`,同一个 double 不再在两个面上印出不同值;并且因为**四舍五入本身也不可加**,页脚现在显式写出残差,而不是暗示不存在 | `Rows.cs:366-371`(1.7.7 注释) | 已闭合 |
| ~~上一场图表的「未归属」恒为 0~~ → **已修(P2-A,1.7.8)**:`BattleSession.FromSummary:130-134` 从 `b.Session` 透传 `UnattributedDamage/UnattributedHits` | ✅我核过生命周期:每场新建 session(`Aggregator.cs:379`),续场时 `History.RemoveAll` 撤下摘要(`:480`),`ResetActors` 只作用于 live ⇒ 透传值稳定 | 修前 caption「未归属 0」→ 修后「未归属 273,702」(= 导出真值) |
| ~~字体取不到时重试节流失效~~ → **已修(1.7.7)**:失败分支不再清标志,3 秒窗口因此有效;告警改为一次(`_fontWarned`/`_monoWarned`),等宽字体路径另有 `_monoAttempted && unscaledTime - _monoLastTry < 3f` 的显式节流 | `Rows.cs`/`Pool.cs:99-105`、`:129-134`、`:141-149` | 已闭合 |
| ~~贡献面板被关掉时标题仍打「任务 0 0秒」~~ → **已修(P2-A,1.7.8)**:`view` 仍可为 null,但标题一律经 `ResolveHeaderBattle`,依次回退到 live session / `History[0]` | `Rows.cs:480/491`、`:457-473`;`check_p2a_summary_and_lastbattle.py` 模型重放:旧→0、新→411001 | 已闭合 |
| 键盘滚动(PgUp/PgDn/↑/↓)永远生效,无视 `CfgWheelScrolls` | `OverlayUGUI.cs:307` vs `:315-318` | 与设置说明冲突 |
| 图注按每 5 秒固定打格,图内 >60s 改为 10/15 秒 → 图注列出图上没有的刻度;且图注走比例字体、图内走点阵 | `Chart.cs:20-24` vs `OverlayChart.cs:170-175` | 图读不出准确时间 |
| `AxisLabel` 无上限:600 秒战斗约 700 字符塞进 560 宽面板,且面板无 `RectMask2D` | `Chart.cs:19-26`、`Rows.cs:99`、`OverlayUGUI.cs:145-146` | 轴注冲出面板 |
| `OverlayUGUI.cs:20-22` 注释称「内容变了才刷新」,实际每 0.25 秒无条件刷新;`OverlayUGUI.Chart.cs:106-132 ChartSeries` 是死代码 | 注释与实现不符 | 仅误导维护者 |

### 9.4 文档与代码的口径冲突(S6;两条我已复核)

- ~~**`DpsMeter-文档索引.md:21` 的 SHA256 是错的**~~ → **已修**:索引现在写的是当前部署值(`F3F73C81…` / 1.7.9),不再是 `C1A4F8C1…`(那是 `DpsMeter.dll.1.7.0.bak`)。
- ✅ **`Plugin.cs:349` 的设置说明仍是旧语义**(「bounded at 64 entries/read」):rev2 后每条都读 3 个廉价整数,只有 owner getter 受 64 预算,另有 20000/场预算;字典反而写「已写进设置说明」。
- ~~仍称「攻击力加算未拆 / 留在 baseCredit / 尚未实现」~~ → **已全部同步(P1-B,1.7.9)**:`CONTRIBUTION-DATA-DICTIONARY.md` §8/§11.0/§11.4、`DpsMeter-文档索引.md`、`HANDOFF.md`、`PROJECT-DIRECTION-AND-IMPLEMENTATION-PLAN.md` 均已改为「队友给的加算经 `kind=atkadd` 归到给出者(1.7.4+);只有自施加留在 baseCredit」。
- ~~量级仍是废口径~~ → **已修**:字典 §11.4 与索引的「12–14M / 5.7–6.6%」已改为实机 1.7.4 = ルナリス assist 51,230,121(25.14%)、全队 assist 51.52%。
- ~~语料计数过期~~ → **已修**:索引/HANDOFF 已改为 **29 份**,并列出 quest 700817 与 9999 训练场。
- 强度低报:自测写 9/9 或 17 例(实 **32/32**)、探针写 74/95(实 **179** 个调用点)、源码写 61/64(实 **68/65**)。
- ~~`knownLimits` 两套措辞~~ → **已收敛**:插件与离线都是**同样 6 条**,且那句过期的 `crit has no observation channel` 已不在插件文本里;字典示例同步(见 §9.2 与上一条)。
- 字典 §11.2 缺 `entriesTotal`/`ownerGetters`/`errors`,`entriesDropped` 的解释仍是 1.7.2 语义。
- ~~`schemaVersion` 三处不一致~~ → **已收敛**:插件/离线/字典统一 `1.1`,`producer` 区分实现,且有 `check_export_schema.py` 按段内 `schemaVersion` 门控(见 §1)。
- 进度句与内部矛盾:`HANDOFF.md:70` 部署表停在 1.5.5、`:136/143-158` 仍列已结案问题、`:325-327` 仍写已撤回结论;索引 `:96-97/111-113` 说 E 待部署、F/G 未启动。

### 9.5 验证闸门的盲区(S5 实测)

| 盲区 | 位置 | 后果 |
|---|---|---|
| `refactor_final_check.py` 对非 UTF-8/损坏**只 print**,唯一 exit 1 是版本不一致 | `:33-41` vs `:80` | 它自称的完整性守卫在真实事故场景(2026-10-03 的 Program.cs 事故)下**exit 0** |
| `contrib_cs/`、`enum_probe/` 与所有 `.py` 不在扫描范围 | `:16 CS_DIRS=['src','recon_probe','test']` | 同一类写坏发生在别处无人发现 |
| `crosscheck` 在 `contribution` 段**缺失时视为通过**(`ABSENT` → exit 0) | `crosscheck.py:42-44,233-235` | 段被改名/停写 = 闸门静默消失;语料里有 13 份正是 ABSENT(该闸门在那 13 份上从未生效) |
| `crosscheck` 算了 `validate.check` 却**不消费其 ERROR** | `:46` vs `:149-151` | Python 核心恒等式崩坏而插件与其同源一致时,仍报 OK |
| `check_fact_signature.py` 的 `STARVED` 也 exit 0;cap 常量(2400/900)与 C# 无绑定;声称「selftest 会发现 cap 变化」是假的 | `:45-47,177-188,250-255` | 覆盖率失真不报错 |
| `v150_validate.py` 的 KPI 基线分支在当前语料下**不可达**(pre-1.5.0 = 0 份) | `:155-199` | 「KPI 不下降」目前是空话 |
| `test_golden_155.py` 文件缺失即 SKIP + exit 0 | `:42-44` | 唯一真实数据回归钉会静默消失(该项目有清空 exports 的历史) |
| 训练场(9999)的 sub-unity 只有 WARN,无真实数据断言 | `validate.py:63-65`、golden 只对 411001 断言 0 | sub-unity 语义改错零红灯 |
| ~~**atkAdd 的 9 个计数器没有任何闸门或恒等式**~~ → **已修(1.7.8)**:共 **13** 个计数器,恒等式进闸门(§9.5 更新表) | `ExportService.cs` 写入 + `contribution_gate.atkadd_section_reasons` | 已闭合 |
| `evidence_153live.py` / `verify_155.py` / `contrib_recon_155*.py` 无断言、恒 exit 0 | 各文件 | 只有人眼能发现问题 |

**§9.5 更新(1.7.8 / P0-A 已落地)** —— 上表是「发现时的状态」,以下是关闭情况:

| 盲区 | 现状 | 证据 |
|---|---|---|
| 当前版本缺 `contribution` 段算通过 | **已修**:≥1.6.0 缺段 = `DATA_MISSING` exit **3**;1.5.x 明确 `LEGACY_NOT_APPLICABLE`(打印原因,不是 PASS) | 实测:删段的 1.7.6 导出 前 `EXIT 0` → 后 `EXIT 3` |
| crosscheck 不消费 validate 的 ERROR | **已修**:`validateErrors=[I3]` → `ERROR exit=1`;`validate.py` 补了入口 | 实测(抹掉 `atkKey`) |
| golden 缺失静默 SKIP+0 | **已修**:`DATA_MISSING` exit **3** + 原因 | 实测(`--export` 指向不存在路径) |
| FACT STARVED / cap 漂移只打印 | **已修**:cap 与 `FactStore.cs` 一致性成必失败检查(漂移 → `EXIT 1`);STARVED → `VERDICT: FAIL EXIT 1`;无输入 → **3** | 实测(合成 3000 类) |
| atkAdd 计数器无任何闸门 | **已修**:`emitted == 非空 calc.atkAdd 行数`(全档);折叠关系用**域内**等式;1.7.8 起再加 `selfValues == selfByKey + selfByNameFallback` | 实测 411001 6,006==6,006、9999 域内 **34==34**;域外行只出 NOTE。集成时抓到的域外误报已修,`--batch` 最终 `ERROR 1`(仅已知 1.6.0) |
| 训练场与普通任务同口径 PASS | **已修**:9999 = `LEGACY + not_comparable`;P0-D 独立给出 full/partial/not_comparable 三分类 | `contribution_applicability.py`(26 份时 10/9/7;29 份 = **12/9/8**) |
| `refactor_final_check.py` 损坏只 print | **未修**(仍只 print,唯一 exit 1 是版本不一致) | 待办 |
| `v150_validate.py` 的 KPI 基线分支不可达 | **未修**(pre-1.5.0 语料为 0) | 待办 |
| 9999 的 sub-unity 只有 WARN | **未修**(但仍被 `not_comparable` 拦住跨场比较) | 待办 |
| `evidence_*` / `verify_155.py` 无断言 | **未修** | 待办 |

新增回归(1.7.9 核对后的实况):`contrib/tests/test_gate.py` **59 条**、`check_export_schema.py --selftest` **52 条**、`check_fact_signature.py --selftest` 18 条、`check_golden_155.py` 18 条、`contrib/tests/test_samples.py` 13 条、`contribution_applicability.py --selftest` 18/18、`pairtrusted_impact.py --selftest` **11 条**、`check_given_fold_coupling.py` 17 项 + 自测 6/6、`check_live_log.py` 自测 PASS。集成后核对的闸门:17 个 Python 闸门 + C# `recon_probe`,全部通过;唯一非零是 `crosscheck --batch` 的已知 1.6.0 一份(`{ERROR:1, LEGACY:15, PASS:10, WARNING:3}`)。
### 9.6 复核分歧与我的裁定

| 分歧 | 各 subagent | 我的裁定 |
|---|---|---|
| 带 `contribution` 段的导出份数 | S2 说 14,S3 说 11,S4/S5 说 13 | **13 份**(1 份 1.6.0 MISMATCH + 12 份 OK),13 份 ABSENT,共 26 —— 由我对 26 份逐一跑 crosscheck 定案(§5) |
| F5 表行宽 | S4 说 83/86 | 未计行首 2 空格;计入后 **85 / 88**,与 §4 一致,不矛盾 |
| 700817/9999 的 `prod(fold) ≠ 乘法` | S2 说 700817 3/33、9999 7/127 | **修正了两次**:第一次我读成「59 击漏折」;第二次按 `NEXT-STEPS §0.2` 的提醒重放,**证明那 59 击全是 `attrMult=2`**。正确判据下 411001/700817 **0 处违规**,9999 仅 16 处且最大偏差 2.06e-3(乘数只写 3 位小数的舍入)⇒ **语料内不存在漏折**(§9.7) |
| `quest 700817` 的 `hits` 与 `dmgEvents` 缺口 | — | 实测 `contribution.hits = 233` vs `reconcile.dmgEvents = 285`,差额 52 击 = 我方外/攻击者不可解析(§9.1-1 的同一个洞) |
| `attacker_default` 出现频率 | S3 说它是「最大项之一,占 ~1/4 折」 | **错**:我取了全部 13 份的 `reasonCounts`,`attacker_default` **出现 0 次**;S3 把 `ability_holder_attacker`(71,833 折)误当成兜底 |

### 9.7 更正记录:700817 的「漏折」其实是属性倍率(2026-10-04)

依据 `_dpsm_work/CONTRIBUTION-REVIEW-NEXT-STEPS.md` §0.2 的提醒重新核验,推翻了本报告 §9.1-3:

- 我先前的判定:700817 有 59 击 `knownMult=2` 而 `calc.fold` 为空 ⇒ 判为「漏折、池为 0」。
- **更正**:逐击重放这 59 击,**全部** `attrMult=2, dealtMult=1, takenMult=1`。`attrMult`(属性相性)在 `CompositionProbe.Chain.cs:116` 就**明确不进 fold** —— fold 只装规则/能力倍率。所以「M=1、无池」是**设计如此**,不是缺陷。
- **正确判据**(以后一律用它):`prod(calc.fold) ≈ dealtMult × takenMult`,**排除 `attrMult`**。`knownMult` 含属性倍率,拿它跟 fold 乘积比会得到一个看似确凿的错误结论。

用正确判据重放(离线,本机):

| 导出 | 版本 | dmg+calc | 违规(>1e-3 相对) | attrMult≠1 | 最大相对偏差 |
|---|---|---|---|---|---|
| 411001_115417 | 1.7.6 | 6264 | **0** | 0 | 3.78e-4 |
| 411001_041309 | 1.7.4 | 5562 | **0** | 0 | 3.78e-4 |
| 411001_235204 | 1.5.5 | 5601 | **0** | 0 | 3.78e-4 |
| 700817_114821 | 1.7.6 | 284 | **0** | 62 | 2.22e-16 |
| 9999_042458 | 1.7.5 | 614 | 16 | 16 | 2.06e-3 |

- 3.78e-4 / 2.06e-3 来自**序列化精度**:`attrMult/dealtMult/takenMult/knownMult` 只写 **3 位小数**,而 1.6.1+ 的 `fold[].factor` 是往返精度;参与相乘的因子越多,相对误差越大。**不是漏折。**
- **结论:语料内不存在「倍率没进 fold」的漏折。**
- 方法论教训(与 SESSION-STATE §7.2.90(B) 属同一类):**用错字段做判据**会产出看似确凿的错误结论。凡做「A ≠ B」的判定,先确认 A 与 B 的定义域是否相同。

**第二次同类更正(§9.1-5)**:S1 报「279 击 `pairTrusted=false` 进入归属」,我复核后确认那 279 击**在 `Contribution.cs:321` 就被排除**(域外 team2 / 攻击者不可解析),域内计数是 **0**。两次错误的共同根因完全相同:**没有先确认定义域**——第一次混了 `knownMult` 与 fold 的定义域,第二次混了「全语料事件」与「模型真正消费的事件」。

**结论性纪律**:本报告的每一条「风险」在采纳前都必须写清**它在哪个域上成立**;域外的不算风险,域内的才进清单。

---

## 9.8 1.7.9 集成记录(2026-10-04,实机三场之后)

三场新战斗(`battle_411001_20261004_134524` / `_134853` / `battle_9999_20261004_135214`,插件 1.7.8)
使本报告的语料从 26 份增至 29 份。三份都是 **schema 1.1** 的真实数据,于是此前只能用合成文件验证的
契约第一次有了实机样本。

### 9.8.1 三份新导出的台账恒等式(实测,全部精确)

| 导出 | totalsDealt | analyzable | outsideTeam | eventSumAll | unknownAttacker | gap |
|---|---|---|---|---|---|---|
| 411001_134524 | 195,026,518 | 194,904,570 | 121,948 | 195,283,574 | 257,056 | 0 |
| 411001_134853 | 200,853,545 | 200,752,131 | 101,414 | 201,159,315 | 305,770 | 0 |
| 9999_135214 | 10,710,019 | 7,467,581 | 3,242,438 | 11,345,618 | 635,599 | 0 |

`events = analyzableHits + outsideTeamHits + unknownAttackerHits` 三场全部成立。
`attributedDamage + unattributedDamage == analyzableDealt` 三场全部成立,且
`contribution.unattributedDamage = 0` —— **1.7.6 也是 0**,所以 1.1 没有改这个字段的含义(见 9.8.3)。

### 9.8.2 机内日志与导出互为独立证据(新闸门 `check_live_log.py`)

`BepInEx/LogOutput.log` 自带机器侧陈述:`Battle end: result=R dur=D idle=… quest=Q actors=N unattributed=U(xH)`、
`Exported full battle data -> <path> (N characters)`、以及每 5 秒一条 `[UI-DIAG]` 心跳。三场逐项与导出**完全一致**:

| 导出 | 机器行 | 导出 totals | dur |
|---|---|---|---|
| 134524 | unattributed=257,056(x73) | 257,056 / 73 | 119.1 vs 119.07 |
| 134853 | unattributed=305,770(x76) | 305,770 / 76 | 119.1 vs 119.07 |
| 135214 | unattributed=635,599(x28) | 635,599 / 28 | 85.1 vs 85.1 |

`(N characters)` 是**字符数**而不是字节数(实测 `len(text)` 与日志完全相等,UTF-8 字节数更大)。
心跳还实测到 F5 页真实渲染:战斗中时长从 2 秒涨到 112 秒(显示 live 场),战斗结束后 `hist` 1→2,
标题为 `任务 411001 119秒` —— **不是「任务 0 / 0 秒」**。但心跳只登记首行文本,因此 1.7.9 给
`[UI-DIAG]` 增加 `unattrRow`(含 `未归因`/`未归属`/不可用的那一行),把「用户看过 caption」变成日志事实。

### 9.8.3 两条修正(1.7.9)

1. **`General/GivenTalent=false` 会制造自相矛盾的导出**(§9.2 首条):修前调用方只用开关决定
   `vicMod *= gv`,而 `ctx.Add("vic","given",…)` 在被调函数里**无条件**登记。实测(`GivenTalent=true`
   的 134524):2,894 击带 `kind="given"` 步,`prod(fold)` 与 `dealtMult·takenMult` 最大相对差
   **3.78e-4**(三位小数序列化),即**登记了就在乘积里**。1.7.9 把开关作为 `applyToChain` 交给被调函数,
   四个副作用同进同出;新增逐击 `calc.givenFoldOn`,并加守卫 `check_given_fold_coupling.py`(17 项 + 自测 6/6)。
   两个同族开关(狂気 出/受)本来就耦合在一处,**只有 given 这一个把登记放进了看不见开关的函数里**。
2. **`giveApplied` 一直是「条目数 + 击数」**:1.3.5–1.7.8 同一个静态量被两个单位各加一次。
   实测全部 29 份导出:`giveApplied == (kind="given" 折叠步数) + (带该步的击数)` **精确成立**(28/28 份非零;700817 为 0/0/0)。
   1.7.9 把逐击增量拆为 `giveFoldHits`,`giveApplied` 恢复为条目计数,并加闸门
   `contribution_gate.give_section_reasons`(历史文件只出 NOTE)。

### 9.8.4 又一次「定义域」更正(§9.1-5 / §9.2 的同类)

P0-C 的 T3 断言原为「**所有** 1.7.x 导出的域内不可信配对 = 0」。1.7.8 训练场导出
`9999_135214` 首次出现 **域内 12 击**(7 击带 fold),B 口径在该场会移动 **62.7391 / 842,871.87 = 0.007443%**。
两场普通战斗仍是 **0 击、0.0000 移动**。处理方式:把 T3 **限定为「非训练场」**(它才是闸门选择会带来
差异的域),并新增 **T3c**(域内不可信只出现在 pre-1.7 或训练场)与 **T4c**(把训练场的代价**量出来**并给上界),
而不是把断言删掉或放宽。**「A ≠ B」的判定必须先写清定义域**——这是本报告第三次因同一条纪律而修正措辞。

### 9.8.5 新增的跨段恒等式(1.7.9,语料全量实测)

```
totals.taken == totals.dealt + totals.unattributedDamage      ← 29/29 份导出
damageLedger.eventSumAll == totals.taken                       ← 3/3 份带台账的导出
```

第二条把台账钉在**游戏自己算的** `taken` 上:§12.3 其余恒等式比较的都是同一份事件表推出来的数,
自洽的漏账骗得过它们,`taken` 骗不过。两条都进了 `check_export_schema.py`(自测 52 例)。

### 9.8.6 任务适用性重跑(P0-D,29 份)

`contribution_applicability.py` 在 29 份上的分布 = **full 12 / partial 9 / not_comparable 8**
(26 份时为 full 10 / partial 9 / not_comparable 7)。三份新导出里两场 411001 进 `full`、
训练场 9999 进 `not_comparable`,`partial` 仍只有 700817(属性相性)。**没有出现新类别、没有档位翻转**。
两场普通 1.7.8 战斗的漏折检测 `leaks=0/0`;训练场 `foldIdentity` 13 处严格违规**全部**由三位小数序列化解释
(`violationsUnexplainedByRounding = 0`),与 1.7.5 训练场的 16/16 同因。

### 9.8.7 部署

插件 **1.7.9** / 386,560 B / SHA256 `F3F73C81…`(源码与部署 DLL 逐字节同一);
回退锚点 `DpsMeter.dll.1.7.8.bak` = `0B339836…`(386,048 B)。闸门:17 个 Python 闸门 + `recon_probe` 全通过,
`crosscheck --batch` = `{ERROR:1, LEGACY_NOT_APPLICABLE:15, PASS:10, WARNING:3}`(唯一 ERROR 是已知 1.6.0)。

---

## 9.9 1.7.10:一处「闸门伪运行」与一处诊断口径(2026-10-04,独立对抗复核发现)

这一轮不是新增功能,而是**修掉我自己在 1.7.9 引入/遗留的两个问题**,两条都由一个只读对抗复核发现:

### 9.9.1 `give_section_reasons` 在真实管线里从未运行(HIGH)

- **症状**:`giveApplied` / `giveFoldHits` 写在**根 `rosterAudit`** 里,而 `contrib/crosscheck.py` 把**根字典**
  传给了该函数,于是每次查表都是 None,整条检查退化成一条 `GIVE_COUNTERS_ABSENT` NOTE。它通过了
  自己的扁平字典单测,却在真实文件上一次都没跑过 —— 「一个从不运行的闸门」正是 P0-A 要消灭的那类东西。
- **修法**:传 `rosterAudit`;缺字段在 ≥1.7.9 上是 `DATA_MISSING` 而不是静默跳过;并新增**真实文件负控**
  (`test_gate` CASE3 五例,`64 passed`),把「篡改 → 必须 ERROR」钉在真实导出上。
- **口径同时收紧**:`giveFoldHits` 比较「given 因子乘积 ≠ 1 的击数」(互相抵消的条目会登记步数但不计入击数),
  且正文有丢步时只作为下界断言。

### 9.9.2 `[COMP]` 重算不遵守开关(MED,日志口径)

- **症状**:`CompositionProbe.Diagnostics.cs` 的 `[COMP]`「已识别倍率」重算:调用 `GivenTalentDamage` 时
  **不传 applyToChain**(默认 true)、无条件把 gv 乘进 mVic,也**不看** `Plugin.CfgMadness`;而它自己上方
  的注释承诺「日志永不与 `CalcBreakdown.KnownMult` 不一致」。它还**从未建模受击方 狂気 ×1.5**,
  所以任何受击者带狂気的命中本来就短了 1.5 倍。
- **修法(1.7.10)**:三条通道都对齐 chain —— given 走同一个 `GivenFoldApplies`、madness 看 `Plugin.CfgMadness`、
  受击方狂気用 chain 的同两个助手(`StatusBrief` + `SplitStatusList`)按 `Plugin.CfgMadnessVictim` 折叠。
  仅影响日志文本,**不改导出数据**(`measure:false`,本地 `dctx`)。守卫 `check_given_fold_coupling.py` 增 7a/7b/7c
  三项 + 3 个变异(现 20 项 / 自测 9/9)。

### 9.9.3 复核同时纠正的文档数字

- `giveApplied == 条目 + 击数` 在语料上成立的是 **28/28 非零文件**(第 29 份 700817 为 0/0/0),此前几处写成 27/27。
- `giveFoldHits` 写在 **`rosterAudit`** 里,不是根;`ROLLBACK-1.7.9.md`/SESSION-STATE 已更正。

### 9.9.4 部署

**1.7.10** = 386,560 B / `BF2F174A4059125376946ABCB0E3E4A9176E6B360CAB209078F219A95389AE85`;
回退锚点 `.1.7.9.bak` = `F3F73C81…`。

### 9.9.5 1.7.11:F5 分组与两条强制恒等式(用户提问驱动)

**用户问题**:「角色因为自身非团队性装备而造成的输出是否也应当计算在直接贡献之中,将一个角色的输出切割为多部分可能会导致误解。」

**结论**:数值上它**一直**算在该角色名下(`总贡献 = 自身 + 他人因你`),模型不需要改;会误导的是**版面**。

**为什么「基础 vs 自身规则」不是「自己打出的 vs 自己装备带来的」**:字典 §2.3 明写 `baseCredit` 含「攻击者的会心、
**自身**攻击力加算、未识别残差」;导出的 `knownLimits` 也写死 `self-granted addends stay in baseCredit` 与
`crit damage stays in baseCredit`。也就是说那条分界线是「**找不到具体规则可以开票 vs 找得到一条规则**」——
她的会心、自加攻、未识别残差留在「基础」,而神器「毒の短剣」的 ×1.15 被单列;两者在「算谁的」上同类。
把这一对拆成两列、又和「辅助」平排,读者读成「自身规则不属于她的直接贡献」是版面诱导。【实测:定义来自源码与字典】

**本场实测**(`battle_411001_20261004_144548`,1.7.10,可分析伤害 194,697,612):

| 角色 | 我打出(直接) | 基础 | 自身规则 | 辅助 | 总贡献 | 被队友分走 |
|---|---|---|---|---|---|---|
| ネーフェ＝ジアー | 60,185,329 | 6,471,267 | 24,225,402 | 0 | 30,696,668 | 29,488,661 |
| メアリー | 56,396,032 | 1,763,013 | 24,892,618 | 0 | 26,655,631 | 29,740,401 |
| マッドシーカー | 28,043,750 | 7,775,738 | 6,972,714 | 30,475,938 | 45,224,391 | 13,295,298 |
| **テトラ** | **19,210,488** | **5,048,014** | **1,442,501**(毒の短剣) | **0** | **6,490,515** | **12,719,973** |
| ルナリス | 352,442 | 173,406 | 0 | 49,308,447 | 49,481,853 | 179,036 |

- 误读代价 1:把「基础」当输出排名 → ネーフェ 647 万「赢」テトラ 505 万;真实打出是 **6,019 万 vs 1,921 万**,顺序与量级全错。
- 误读代价 2:把「总贡献」当输出排名 → 打出 **35 万**的ルナリス 靠 **4,931 万**辅助登顶。
- 全场:基础 **13.6%** / 自身规则 **34.7%** / 辅助 **51.7%** —— 总贡献一半以上是「别人因你打出来的」,两列都不能删。
- `receivedAssist` 在 1.7.11 之前**没有出现在任何界面**(`src/Ui` 全目录 grep `Received|受助|被分走` = 0 命中),
  读者看到「打出 1,921 万、基础只有 505 万」无法自证剩下的 1,272 万去哪了。

**改动**(`OverlayUGUI.Rows.cs`、`OverlayCore.cs`):F5 表 1 改为 `角色|总贡献|占比|自身|他人因你|被队友分走|直接占比|命中`
(仍为 **85 列**,与表头/合计行同几何),`自身 = baseCredit + selfRuleCredit`;F6 明细块第二行改为
`自身 X(基础 A + 自身规则 B)   他人因你 C   被队友分走 D`;IMGUI 回退渲染器同步;口径由三行改为四行。
**导出字段、`schemaVersion`、所有数值一个都没变**(唯一的数据侧变化是 `receivedAssist` 由可选升为**必需**)。

**验证**:`check_export_schema.py` 新增逐角色恒等式 `base+self+received == directDamage` 与 `receivedAssist` 必需项,
自测 52 → **54 例**(两条新负控都能红),全 32 份导出 0 问题;`check_contribution_layout.py` 全 32 份 / 634 行 **0 违规**,
并新增**渲染器↔副本漂移检查**(解析 C# 源里的列标签与宽度,自测用「11→12」的真实篡改证明会红);
`check_live_log.py` 由 ERROR 修为 exit 0 并顺带修了它自己的两个缺陷(见 §9.9.6)。

**部署**:见 §9.9.4 的格式 —— 1.7.11 = **387,072 B** / `36EC96D4DBD8E221ED554476C299BD8DB4C9A1220A2A923DB16BC7BB4888BC42`
(回退锚点 `.1.7.10.bak` = `BF2F174A4059125376946ABCB0E3E4A9176E6B360CAB209078F219A95389AE85`)。

### 9.9.6 顺带修掉的两个守卫缺陷(**都不是 1.7.11 引入的**)

1. `check_live_log.py` 用**行内第一个数字**当 `unattrRow` 的值。F6 明细块的合计行是
   `合计 194,697,612   未归因 0(0.00%)   命中 5,493   倍率池 168,197,251`,于是它把 **合计** 当成 in-domain 未归因,
   把一块印着正确 0 的面板判成 ERROR。已改为**取标记之后的数字**,并加两条真实格式的自测(F6 合计行=0 → PASS;改成 7 → ERROR)。
2. 同一守卫把 `敌方总伤害 X   未归属(无法归一敌方) N   (设置可关闭敌方栏)` 也当成「会话未归属总量」去比对导出。
   该行的 N 是**当时实时值**(1.7.10 实机日志里从 0 长到 106,529,而那一场的导出是 325,381),比对必然失败。
   已改为:只有带命中数 `(xN)` 的行才参与比对(那才是会话总量),其余只记一条**不可核验**警告(exit 0),不冒充通过。
   修后实机日志:82 帧与导出一致、16 帧因缺命中数而**明确标注不可核验**,闸门 exit 0。
---

## 10. 待实测 / 未闭合清单

1. **F5 页在实机上的最终对齐**:离线守卫已证 1.7.7 下 **0 违规**(1.7.6 语义 164 处),但仍需用户重开游戏看一次(唯一无法离线替代的验收)。
2. 等宽字体是否真的取到:日志标记 `[DpsMeter][UI] monospaced table font created OK`(成功)/ `[DpsMeter][UI] no monospaced CJK font (NSimSun/MS Gothic/SimSun); the contribution table stays proportional`(失败回退)。
3. `potential_skill_data.asset` / `passive_skill_data.asset` 尚未 dump:潜在/被动技能(+200/+150)的**显示名**缺失,归属本身不受影响。
4. 1.5.3–1.5.5 共 8 份导出**没有** contribution 段:离线复算可用,但无法验证与原内存值的一致性(见 §5)。
5. 自己给自己的攻击力加算仍留在 `baseCredit`(1.7.6 实测该场 `selfValues=7003`):口径正确。**1.7.8 起可观察**:self 改由 actor key 判定,并导出 `selfByKey`/`selfByNameFallback`/`nameCollision`/`ownerUnknown`(三场 1.7.8 实测 fallback=0、nameCollision=0,即 key 判定 100% 覆盖)。
6. ~~F5「上一场」指向哪一场~~ → **代码已修(P2-A,1.7.8)**,并且**已有实机旁证**(§9.8.2):1.7.8 三场的心跳显示战斗中 F5 跟随 live 场(2→112 秒),战斗结束后 hist 1→2、标题为刚结束那场(`任务 411001 119秒`),机器侧 `unattributed` 值与导出一致。**仍未直接观察到**:整场停在 F6/F10 或按 F8 隐藏后开 F5 时,「上一场」caption 那行**文字**(用户这三场没按过 F6/F8/F10,心跳当时也不登记该行)。1.7.9 已把该行并进 `[UI-DIAG]` 的 `unattrRow`,所以**下一场只要打开一次 F5/F6 就能自动核对,不需要为此专门打一场**。
7. ~~`pairTrusted=false` 的 279 击影响~~ → **已量化并结案(P0-C)**:域内 0 击,不需要改谓词。**同时否决一个诱人的做法**:`PairCorroborated` **不得**当信任闸门 —— 它只占 0.1%–3%,用它过滤会保留 A 口径池的 **0.013%**(ルナリス 51,955,401 → 368,775,排名 1→7),制造伪排名差。「未获佐证」≠「配对错误」。
8. ~~`GivenTalent=false` 的真实导出形态~~ → **已由代码契约 + 数据结案(1.7.9,§9.8.3)**:开关关闭时不再登记 fold、不再计 applied,`prod(calc.fold)` 与 `dealtMult·takenMult` 因此在两种设置下都一致;逐击 `givenFoldOn` 让人能从**任何**一份将来的导出判断当时用的是哪套理论。**不再需要改配置专门打一场**;若将来真有 flag-off 导出,`contribution_applicability.py` 的漏折检测与 `give_section_reasons` 会同时给出红灯。
9. **非 CJK Windows 的等宽字体回退**:需另一台机器确认日志与列对齐(缓存节流在失败路径失效,见 §9.3)。
10. **敌方视角(team 2)贡献**:需改代码,当前不可算。

> **部署状态(R52 起)**:DLL = **AA836C06**…(416,256 字节,含证据提取流程);上一版 **28B8CCAF**…(398,336 字节)备份于 `_dpsm_work/deploy-backup/pre-r52-28B8CCAF/`,基线 **36EC96D4**…(387,072 字节)在 `_dpsm_work/deploy-backup/baseline-1.7.11/`,回退为一条 Copy-Item。
