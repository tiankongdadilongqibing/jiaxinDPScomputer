# 贡献指标与数据契约(第一版,草案冻结)

> 阶段 A 交付物。本文件是"DpsMeter 总贡献分析"的指标字典,不依赖任何代码实现。
> 基线:插件 1.5.5;证据来源导出 `battle_411001_20261003_235204.json`(21.9MB,quest 411001,119.07s,result Lose)。
> 证据等级:实测(SS,来自真实导出字段)/ 离线重放(SR,用导出数据复算得到)/ 推断(IN,尚未验证)。
> 上游依据:`PROJECT-DIRECTION-AND-IMPLEMENTATION-PLAN.md` 第 3、5 节;`DpsMeter-文档索引.md`;`_dpsm_work/HANDOFF.md`。

---

## 0. 一句话定义

**总贡献积分 = 每个角色"被记到账上"的伤害之和**:一击的伤害 D 被拆成"基础部分"和"倍率池",
基础部分记给出手者,池子按规则份额记给倍率的提供者;每一份伤害只记一次。

---

## 1. 口径与数据基

### 1.1 三个伤害口径(不得混用)

| 名称 | 来源字段 | 含义 | 用法 |
|---|---|---|---|
| `dealt` | `totals.dealt` | **双方已识别攻击者**的伤害(`atkTeam ∈ {1,2}`,不分敌我;**含同队/自我伤害**,见 §1.4;详见 §1.2) | 对账基准;贡献分母用 `analyzableDealt`,两者的差由 §12.3 的 `damageLedger` 精确拆分 |
| `dealtWithAbsorbed` | `totals.dealtWithAbsorbed` | dealt + 被护盾吸收 | 仅作对账参考,单独列出 |
| `absorbed` | `totals.absorbed` | 被吸收量 | 不折算进贡献 |

1.5.5 实测值:`dealt=203964758`、`absorbed=8500000`、`dealtWithAbsorbed=212464758`、
`taken=204271307`、`unattributedDamage=306549`、`unattributedHits=67`。

### 1.2 可分析池(analyzableDealt)

`analyzableDealt` = Σ 逐事件伤害,条件为:

1. `type == "dmg"`;
2. `atkTeam == 1`(我方出手);
3. `amount > 0`;
4. `attacker` 能解析到我方 `actors[].key`;
5. **受击方不与攻击者同队**(schema **1.2** 起,1.7.13 / 用户决定:同队伤害是敌方 `回復反転` 的效果,
   不是贡献)。同队命中**不被丢弃**:它们进 `damageLedger.selfTeamHits/selfTeamDealt`,并逐角色发布为
   `friendly`/`friendlyHits`;**1.2 之前的文件仍按各自契约读**(`schemaVersion <= 1.1` 时同队命中在池内;
   离线核心按文件自己的 `schemaVersion` 选模式,所以旧档的比对结果不会变)。

实测:本场 `analyzableDealt = 203865411`(5602 条 dmg 事件中 **5521** 条入池;
另有 67 条 `attacker="?"` 与 14 条敌方打我方,`atkKey` 非空的总数是 5535,别混)。

**为什么不是 totals.dealt —— 已定位(离线重放,4 份导出一致)**:

```
totals.dealt              = Σ dmg.amount where atkTeam ∈ {1,2}(攻击者已识别,不分敌我)
totals.unattributedDamage = Σ dmg.amount where attacker == "?"          (67 击 / 306549)
totals.taken              = dealt + unattributedDamage = Σ 全部 dmg 事件
分析池 = Σ dmg.amount where attacker 行为我方(actor.team==1)           (5521 击 / 203865411)
差 = 99347 = 14 条 atkTeam=2 → vicTeam=1 的事件(敌人打我方,已计入 dealt)
```

复现:1.5.5 场 203865411 + 99347 = 203964758;1.5.3 场 198463938 + 125026 = 198588964;
1.5.4 场 196357382 + 147754 = 196505136;试炼场 1948231 + 770078 = 2718309。
即 `dealt` 把"我方打出"和"我方挨打"混在一个数里(插件既有口径,不是计算错误)。
第一版**不强行对齐**,报告并列展示差值并注明构成。→ 证据等级:**离线重放(4 份导出)**。

### 1.3 队外与不可解析

- 攻击者在队外 / 攻击者为 `"?"` → 不进可分析池,单列 `outsideTeamDamage` / `unattributedDamage`。
- 规则提供者在队外或被判歧义 → 其份额进 `unattributedCredit`,**绝不静默塞给攻击者**。

### 1.4 同队与自我伤害(默认**计入**,单独计数)

> **1.7.13(R61,schema 1.2)起,归属侧已不含同队伤害**:`analyzableDealt`、逐角色 `directDamage`、`总贡献`、
> `命中` 都只统计**对敌命中**;同队命中进 `damageLedger.selfTeam*` 与逐角色 `friendly`/`friendlyHits`。
> 下面这一段描述的是**游戏口径**(`dealt` / `perSecDamage`),它**没有变**。

`dealt` / `analyzableDealt` / 逐角色 `directDamage` / `总贡献` / `命中` / 每秒数组 `perSecDamage` **默认都含
"攻击者与受击者同队"的伤害**(`回復反転`与自伤技能;包括 `attacker == victim` 的自我结算,下称同队伤害)。
它由根 `actors[].friendly` + `friendlyHits` 单独计数,但**不从上面任何一个数里减掉**;逐事件上是
`events[].friendly == true`(只有为真时才写该键)。

- 开关 `Overlay/FilterFriendlyFire`(默认 **false**)。置 true 时它不再进 `dealt`/`命中`/`perSecDamage`,
  但 `friendly`/`friendlyHits` **仍照计**;该开关**不写进导出**,所以**不能只看文件判断**。
- **要「对敌输出」必须自己扣**,优先用逐事件(**与开关无关**):
  `Σ events[].amount where type=="dmg" && atkKey==k && vicTeam != team(k)`。
  默认配置下 `dealt − friendly` 等价;开关为 true 时这样减会**减错**(那时 `dealt` 已经不含它)。
- 自伤 = `atkKey == vicKey`;同队友伤 = `atkTeam == 1 && vicTeam == 1 && atkKey != vicKey`
  (`contribution_applicability.py` 就是按这三条分开计数的)。
- `命中`(`hit`)**含**这些命中;`maxHit` **不含**(`Aggregator.Stats.cs` 只对非 friendly 更新最大值)。
- 实测(2026-10-05,`B-20261005-101631-C3F24E1814C94EE5-001`,quest 9999,53.17 s,该场 `not_comparable`):
  actor `T.O.W.E.R.typeR` `dealt = 2,007,881 = 1,439`(对敌 1 击)`+ 2,006,442`(自伤 23 击);`friendly = 2,006,442`、
  `friendlyHits = 23`、`hit = 24`、`perSecDamage` 各秒之和 = 2,007,881、
  `contribution.directDamage = totalCredit = 2,007,881` —— 它的「直接伤害」里 **99.93% 是打自己**。
  同场 `ソフィー` `dealt = 72,805`、`friendly = 34,311`。根 `actors[].dealt` 与 `contribution.actors[].directDamage`
  逐 key **精确相等**(本场 9 个 key、另两份样本 0 处不等)。
- 冻结语料里 411001 / 700817 实测 `friendly = 0`,只有训练场出现(P0-D §4.3 同结论):既有对比不受影响,
  但**换人/换装时若拿训练场 9999 的样本做对照,必须先扣掉同队部分**(9999 本身一律 `not_comparable`)。
- **导出与界面里能直接读到的形态(1.7.12 起)**:根 `config.filterFriendlyFire`(必写)、
  `contribution.actors[].friendly` / `friendlyHits` / `hostileDamage`(可选字段;`friendly + hostileDamage == directDamage`
  由 `check_export_schema.py` 校验),以及 F5 表 1 的「自伤」列(它显示的就是 `friendly`)。

---

## 2. 指标定义

### 2.1 直接输出

- `directDamage(a)` = 角色 a 作为攻击者的入池伤害之和(dmg 事件 amount 求和)。**含同队/自我伤害**(见 §1.4);与根 `actors[].dealt` 逐 key 相等。
- `directShare(a)` = `directDamage(a) / analyzableDealt`。
- 口径:分母永远是 `analyzableDealt`,不是 `totals.dealt`。

### 2.2 逐击拆分

对一击,伤害 D、折叠倍率集合 F = {f1..fn}(来自 `calc.fold`,因子 > 0 且 ≠ 1):

- `M` = ∏ fi(该击已识别倍率之积;`M` 用导出里的 `factor` 原值)
  * **M 与导出里的 `calc.knownMult` 不是恒等,只是近似**(实测:同队最大绝对偏差 6.6e-4、最大相对偏差
    3.8e-4、中位 7.3e-5)。原因:`knownMult` 只保留 3 位小数(且 ≤1.6.0 的 `factor` 也只有 4 位,
    1.15³ = 1.520875 → `1.5209`)。**本模型用 factor 原值自乘,比 knownMult 更精确**;不要把两者当同一个数。
  * **1.6.1 起 `factor` 改为往返精度(`R`)输出**:4 位小数会让文件丢信息,使 `contribution` 段
    **无法从文件复算**(2026-10-04 实测:58 个字段相对偏差 ~1e-5;还原精确因子后两套实现完全一致)。
- `base` = D / M → 记给**攻击者**
- `pool` = D − base
- 因子 fi 的份额 = `pool × ln(fi) / ln(M)`
- 恒等式:Σ 份额 = pool(因为 Σ ln(fi) = ln(M))

`M == 1` 或 F 为空 → 全部 D 记给攻击者。

**因子 < 1 的处理(2026-10-03 由试炼场导出暴露,已修)**:`0 < factor < 1` 表示**减伤**(试炼场 ×0.03、
100% 减免等),不是"增益池"成员 —— 若计入 M,`D/M` 会大于 D、ln 份额转负(实测:quest 9999 场
credited **412%**、unattributed **−312%**,即负贡献)。因此**规则:因子 < 1 不计入 M**,整击回到
基础项,并把该折叠计入 `diagnostics.sub_unity_factor`(reason 标记为 `sub_unity_factor`)。
普通战斗实测该数为 0(1.5.3/1.5.4/1.5.5 的 factor<1 折叠数均为 0),所以**既有数字一个都没变**
(见 §10 的 legacy_diff 回归)。

### 2.3 四个可加和指标

| 指标 | 定义 | 归属 |
|---|---|---|
| `baseCredit` | D/M 之和 | 攻击者(含其会心、**自身**攻击力加算、未识别残差;队友给的加算自 1.7.4 起走 `kind=atkadd`,见 §11) |
| `selfRuleCredit` | 攻击者自己持有的规则份额 | 攻击者 |
| `assistCredit` | 规则提供者 ≠ 攻击者 的份额 | 规则提供者 |
| `totalCredit` | baseCredit + selfRuleCredit + assistCredit | 角色 |

`totalShare(a)` = `totalCredit(a) / analyzableDealt`。

**恒等式(阶段 B 验收)**:Σ_a totalCredit(a) + unattributedCredit = analyzableDealt;
Σ_a totalShare(a) + unattributedShare = 1。

**第二组恒等式(1.7.11 起由 `check_export_schema.py` 强制)**:

- `directDamage(a) == baseCredit(a) + selfRuleCredit(a) + receivedAssist(a)`
  —— 即「直接打出 = 自身 + 被队友分走」。实测 17 份带贡献段的导出、181 行角色,最大偏差 **0.0001**(F4 舍入),
  且 `Σ_a directDamage(a) == analyzableDealt` **精确成立**。
- 因而 `baseCredit + selfRuleCredit` 是「该角色自己命中里归自己的份额」(下称**自身**),
  `assistCredit` 是「别人因他多打出来的份额」(下称**他人因你**),`receivedAssist` 是「他的命中里被别人拿走的份额」。

**版面口径(1.7.11,用户要求)**。`baseCredit` 与 `selfRuleCredit` 是同一侧的两半(都留在该角色自己的命中里),
而 1.7.6–1.7.10 的 F5 表把它们与 `assistCredit` 平列,读起来像「自身规则不属于他的输出」;`receivedAssist` 则
根本没进表。F5 表因此改为八个列:

```
角色 | 总贡献 | 占比 | 自身 | 他人因你 | 被队友分走 | 直接占比 | 命中
```

- `自身` = `baseCredit + selfRuleCredit`(括号里的 基础/自身规则 明细在 F6 明细页与导出里保留);
- `他人因你` = `assistCredit`;`被队友分走` = `receivedAssist`;
- 可对账关系两条:`总贡献 = 自身 + 他人因你`、`直接打出 = 自身 + 被队友分走`。
- **字段名、字段集合、数值一个都没变**:改的是分组与列名,`schemaVersion` 仍是 `1.1`。


### 2.4 非加和的观察量(有明确标注,禁止与上面相加)

- `receivedAssist(a)` = a 直接伤害里由队友倍率带来的部分。它是"被别人赋能"的视角量,
  与 assistCredit 对称、数值上等于 Σ 别人从 a 的伤害中获得的份额。**不可与 totalCredit 相加。**
  * 1.7.11 起它在 F5 表里有自己的列(`被队友分走`):它不是减法或惩罚,而是
    `directDamage = 自身 + 被队友分走` 的另一半 —— 这解释了为什么"直接打出"与"总贡献"本来就是两个数。
    它**仍然不可与 totalCredit 相加**(相加会重复计数)。
- `directDamage` 与 `totalCredit` 是**两套排名**,不得相加:"总贡献 = 直接伤害 + 为团队赋能"
  是重复口径,本项目禁止出现。

### 2.5 规则级与关系级

- 规则 `r`:`ruleName`、`ownerKey`、`kind`、`side`、`hits`、`coverage`(带该规则的入池击数 / 入池击数)、
  `damageEquivalent`(= Σ 份额)。同一规则名可有多条(不同 kind/origin,如 海魔の残滓 同时走
  `text#4/20042` 与 `global#...` 两条通道)。
- 关系 `link`(提供者 → 出手者):`amount`、`hits`、`rules[]`。

### 2.6 覆盖率与质量

- `coverage.totals` = analyzableDealt / 入池口径;
- `coverage.byUnit` = 带 byUnit 的 given/madness 折叠数 / 该类折叠总数;
- `coverage.ruleResolution` = 各 reason 的折叠计数(见 §4);
- `coverage.foldAccounting` = 折叠总数 = 已解析 + 未归因 + 忽略(因子 ≤ 0)+ 空操作(因子 = 1)。

---

## 3. 数据来源(1.5.5 实测字段)

| 字段 | 用途 | 1.5.5 实测 |
|---|---|---|
| `actors[].key` | **主键** | 214 个,唯一、无空 |
| `events[].atkKey` / `vicKey` | 事件↔角色连接 | 非空 5535/5602,67 条 `attacker="?"` |
| `actors[].name` | 回退用 | 与 key 一一对应(0 处 name/key 冲突) |
| `actors[].summon` | 召唤物标记 | 我方 3 个(火砲/クトゥグァ砲台/砲台R) |
| `calc.fold[]` | 倍数来源 | 5 通道:text 12617、global 8795、given 6180、madness 902、talent 91 |
| `fold.byUnit` | 提供者名 | given 6180/6180、madness 902/902 全带;text/talent/global 从不带 |
| `calc.cancel[]` | 上游去重记录 | 23024 条,形如 `{origin,value,by,label,byUnit}`,**不是倍率** |

**推断(IN)**:`calc.fold` 是插件已去重后的最终倍率集;'cancel' 只是"同一规则的另一条实例被
全局通道取代"的记录,因此贡献计算**直接采用 fold,cancel 只做身份与诊断**,不参与 M。

---

## 4. 归属阶梯(每级都计数,理由码固定)

按 fold 的 `kind` / `side` 进入不同阶梯:

| 优先级 | 条件 | reason 码 | 归属 |
|---|---|---|---|
| 1 | 有 `byUnit`,且解析到我方唯一 key | `byUnit` | byUnit 指定者 |
| 2 | 有 `byUnit`,但名字在队外 | `byUnit_outside` | 未归因 |
| 3 | 有 `byUnit`,我方同名 ≥2 个 actor | `byUnit_ambiguous` | 未归因(不合并) |
| 3b | 有 `byUnit`,但名字解析不到 | `byUnit_unknown` | 未归因 |
| 4 | text/talent:origin 解析出 abilityId,我方仅 1 名持有者 | `ability_holder_unique` | 该持有者 |
| 5 | text/talent:多个持有者,且攻击者在其中 | `ability_holder_attacker` | 攻击者 |
| 6 | text/talent:多个持有者,攻击者不在其中 | `ambiguous_multi_holder` | 未归因 |
| 7 | text/talent:无 abilityId 或无持有者 | `attacker_default` | 攻击者(名称回退) |
| 8 | global:label 的 `[规则名]` 在我方唯一命中 | `global_name_unique` | 该持有者 |
| 9 | global:0 名或 ≥2 名持有者 / label 无方括号 | `global_ambiguous` | 未归因 |
| 9a | **given**:名册里没有能授予该 `type/param` 的持有者 | `given_carrier_none` | 未归因(「阻挡增伤」家族) |
| 9b | **given**:恰好 1 名持有者 | `given_carrier_one` | 未归因(候选唯一,但仍未确认) |
| 9c | **given**:≥2 名持有者 | `given_carrier_ambiguous` | 未归因(真歧义) |
| 10 | 其它 kind | `unknown_kind` | 未归因 |
| 11 | 因子 ≤ 0(如 ×0.0 的 100% 减伤) | `zero_factor` | 不计入 M,整击归基础(附诊断) |
| 12 | 因子 = 1 | `noop_factor` | 份额 0 |
| 13 | 0 < 因子 < 1(减伤 / 试炼场 ×0.03) | `sub_unity_factor` | 不计入 M,整击归基础(附诊断) |

**「阻挡增伤」为什么单独三个码(R54,用户要求)**:given 通道(= 别人**授予**给受击方的被伤害修正,典型是刻印 id=26
「ブロックしている敵の被ダメージ+10%」)的失败**不是"种类未知"**,而是"这条已知规则的**提供者**没确认"。把它并进
`unknown_kind` 会让读者把"一条已知规则缺提供者"误读成"一堆没见过的规则"。所以按**名册侧证据的强弱**分成三码
(none / one / ambiguous),**都仍然不归因**:码只是告诉读者离答案有多近。运行时那条路(授予钩子)只有**精确**
(target, type/param) 命中才允许给信用——实测 2026-10-05(第三场)旧版只按目标名做键,把 6,705,889.97 记给了
一个**不可能授予该规则**的单位;那条旧答案现在只计数(`givenApplies.targetOnlyRejected`)、不再用于归属。

**面板怎么显示(R55,用户要求)**:总贡献页与实时总贡献里各有一段 `【阻挡增伤·待确认】` 小表,**列宽与角色贡献表完全相同**
(只把「角色」写成「候选角色」、「命中」写成「折叠」),逐候选持有者一行给出**待确认**金额 / 占比 / 折叠,最后一行 `待确认合计`;
不适用的信用列打印 `-`(不是 `0`)。这张表**不进任何求和**,排在未归因理由行之后、角色表 `合计` 之外;候选 ≥2 时行名只写 `候选N人`,
候选名字放在下面那行注释里,注释同时写明候选是**名册侧推断、不是实测**。

**byUnit 的歧义处理(阶段 C)**:byUnit 是**角色名**,必须先映射成我方 key。
若该名字在我方存在**两个及以上**同名 actor(同队同名召唤物),返回 `byUnit_ambiguous` 并进未归因,
**不得**任选其一;若该名字只在我方之外(敌方同名)存在,返回 `byUnit_outside`。
同理,攻击者一律由 `events[].atkKey` → `actors[].key` 解析,`atkTeam` 只用于交叉核对
(不一致时计入 `diagnostics.atkTeam_mismatch`,以 actor 行的 team 为准)。

**实测支撑**:1.5.5 导出中,所有 side=atk 的 text/talent 折叠(12701 条)的攻击者都确实在其
abilityId 的持有者集合内(0 例外),所以第 5 级不会把"别人的规则"错记给攻击者。

**禁止重复计算**:

1. 同一条 fold 只算一次(即使它与 cancel 里某条同名)。
2. global 与 text 双通道**不会同时出现在同一击的同名规则上**(实测:330/1773/179/924 条命中中
   同名 global 折叠出现 0 次),因此按 fold 逐条计入是安全的。
   **去重必须按 `origin`(稳定 id),不能按 (规则名, factor)**:实测本场已有 **3966 击**存在
   "同名 + 同因子 + 同通道"的**多实例**(例如 赋予 `given#2..given#7` 各自 ×1.1 叠乘),它们是**合法叠层**,
   按 (名,因子) 去重会吃掉真实的倍率。判据:`origin` 在同一击内重复 = 0 次(实测)。
3. `receivedAssist` 只展示,不进入任何积分。
4. 队外提供者的份额进未归因,不得回填攻击者或队友。

---

## 5. 证据等级与展示

- **实测(SS)**:逐击 `amount`、`atkKey`、`fold.factor`、`byUnit`、`totals.*`。
- **离线重放(SR)**:D/M 拆分、ln 份额、角色/规则/关系聚合、全部恒等式检查。
- **推断(IN)**:cancel 语义、训练场 ×0.03 特例、会心观测量、残差分解。

报告必须对每个数字标注来源等级;无法确定的进 `unattributed` 并给出 reason 码,不写成 0。

---

## 6. contribution JSON 草案(**历史文档,已作废**;正式字段见 §12.4/§12.5,当前 `schemaVersion = "1.1"`)

```json
{
  "contribution": {
    "schemaVersion": "1.1",
    "method": "log-share/1",
    "damageBasis": "dealt",
    "totalDamage": 203865411,
    "attributedDamage": 203865411,
    "unattributedDamage": 0,
    "coverage": { "creditedShare": 1.0, "unattributedShare": 0.0,
                  "analyzableDealt": 203865411, "hits": 5521,
                  "byUnitGiven": [6110, 6110], "byUnitMadness": [897, 897],
                  "keyCoverage": 0.988, "ownerPaths": {}, "foldAccounting": {} },
    "source": { "file": "...", "pluginVersion": "1.5.5", "quest": 411001,
                "duration": 119.07, "result": "Lose", "run": "...", "exportedAt": "..." },
    "totals": { "dealt": 203964758, "analyzableDealt": 203865411, "eventSumAll": 204271307,
                "totalsDealt": 203964758, "unattributedDamage": 306549,
                "unattributedCredit": 0, "absororedDealt": 212464758,
                "coverage": { "creditedShare": 1.0, "byUnitGiven": [6180,6180],
                              "byUnitMadness": [902,902], "poolShareOfDealt": 0.0 } },
    "actors": [ { "key": 7, "name": "メアリー", "kind": "P", "summon": false,
                  "directDamage": 58523963, "directShare": 0.287,
                  "baseCredit": 0, "selfRuleCredit": 0, "assistCredit": 0,
                  "totalCredit": 0, "totalShare": 0,
                  "receivedAssist": 0, "hits": 0,
                  "rules": [ { "ruleName": "...", "kind": "madness", "hits": 897,
                               "coverage": 0.16, "damageEquivalent": 0 } ] } ],
    "rules": [ { "ownerKey": 4, "ownerName": "マッドシーカー", "ruleName": "母なる変異の飛沫",
                 "kind": "global", "side": "vic", "hits": 0, "coverage": 0,
                 "damageEquivalent": 0,
                 "beneficiaries": [ { "key": 7, "name": "メアリー", "amount": 0, "hits": 0 } ] } ],
    "links": [ { "fromKey": 4, "fromName": "マッドシーカー", "toKey": 7, "toName": "メアリー",
                 "amount": 0, "hits": 0, "rules": [] } ],
    "unattributed": [ { "reason": "byUnit_outside", "amount": 0, "hits": 0 } ],
    "diagnostics": { "reasonCounts": {}, "foldAccounting": {},
                     "cancelRecords": 23024, "duplicateRules": 0,
                     "zeroFactorHits": 0, "attackerNameFallbacks": 0,
                     "summonActors": 3, "residualMultiplier": {},
                     "knownLimits": ["attackPower addends granted by a teammate are attributed as kind=atkadd (1.7.4+); self-granted addends stay in baseCredit", "analyzableDealt covers team-1 hits with a resolvable attacker only; compare it with totals.dealt before comparing battles", "crit is observed (1.5.0+) but the model does not credit it; crit damage stays in baseCredit", "summons stay separate actors (no owner link in the export)", "credit components are rounded independently (F4 in the export, N0 on screen), so they may not add up to the total", "totals.dealt and the per-event sum differ by ~0.05% (definitional, not an error)"] }
  }
}
```

**字段名对齐计划第 5 节 E**:`contribution.totalDamage / attributedDamage / unattributedDamage / coverage`
放在 `contribution` 顶层(阶段 E 可直接照抄);`totals` / `unattributed` / `diagnostics` 是本核心的补充明细。

纪律:schema 变更必须升版本;BuildInfo.cs 是用户可见版本唯一来源;
**推断字段不得伪装成实测字段**(本草案不改正式导出,因此无兼容风险)。

**阶段 E 的验收闸门已提前备好**:`python -m contrib.crosscheck` 用离线核心与插件导出的
`contribution` 段**逐字段交叉比对**(角色/规则/关系/总数,容差 rel 1e-6、abs 1.0),
两个独立实现必须在同一份导出上一致;`--selftest` 证明它能**接受忠实数据、拒绝被篡改数据**
(实测:忠实 → ACCEPT;把 totalDamage 与某角色 assistCredit 各改 +1e6 → MISMATCH 且 2 处全部报出)。

---

## 7. 手工样例(阶段 A 验收样例,同时是阶段 B 的单元测试)

固定约定:份额用对数比例,`pool = D − D/M`;所有期望值可用闭式公式复算。

### S1 单人基础伤害(无倍率)

输入:A 出手,D = 1000,folds = []。
期望:M = 1;base = 1000;pool = 0;A:baseCredit 1000,selfRule 0,assist 0,total 1000。

### S2 攻击者 + 辅助者(单倍率)

输入:A 出手,D = 3000,folds = [given ×1.5 by B]。
期望:M = 1.5;base = 2000;pool = 1000。
份额:1000 × ln1.5 / ln1.5 = 1000 → B 的 assistCredit = 1000。
A:base 2000,total 2000;B:assist 1000,total 1000。合计 3000。

### S3 多倍率 + 未归因(含自身规则)

输入:A 出手,D = 2750(= 1000 × 1.1 × 2.0 × 1.25),folds =
text ×1.1(持有者 A)、given ×2.0 byUnit B、global ×1.25(label 无唯一持有者)。
期望:M = 2.75;base = 1000;pool = 1750;ln(M) = 1.0116009(以导出值计算)。
份额:自身规则 1750 × ln1.1 / ln2.75 = 164.88;B 1750 × ln2.0 / ln2.75 = 1199.10;
未归因 1750 × ln1.25 / ln2.75 = 386.02(三者之和 = 1750 ± 0.01)。
A:base 1000 + self 164.88 = total 1164.88;B:assist 1199.10;unattributed 386.02。
恒等式:1164.88 + 1199.10 + 386.02 = 2750.00。

### S4 byUnit 在队外

输入:A 出手,D = 1500,folds = [madness ×1.5 byUnit 队外角色]。
期望:M = 1.5;base = 1000;pool = 500;byUnit_outside → unattributed 500。
A:total 1000;unattributed 500。恒等式 1000 + 500 = 1500。

### S5 同一攻击者既是出手者又是规则提供者(禁止重复)

输入:A 出手,D = 2000,folds = [text ×2.0 持有者 A]。
期望:M = 2;base = 1000;own-rule 份额 = 1000;A:total 2000(只记一次),
且 `assistCredit` 必须为 0、`receivedAssist` 必须为 0。

### S6 非正因子(诊断路径)

输入:A 出手,D = 0,folds = [text ×0.0]。
期望:整击不入 M;unattributed 0;诊断 `zero_factorHits = 1`;不抛异常(禁止 ln(0))。

### S7 混合场(记账与覆盖率)

输入:6 条事件 —— given ×1.5(D = 3000)、无倍率(D = 1000)、因子 ×0.0(D = 500)、
madness ×1.5 byUnit 队外(D = 700)、atkTeam=2(D = 400)、attacker 缺失(D = 250)。
期望:分析池 5200;折叠池 = 3(2 条参与 + 1 条 zero);队外事件 400;未归属事件 250。

### S8 敌方与我方同名(阶段 C)

输入:我方 Twin(key 1)、Solo(key 2),敌方 Twin(key 50);Solo 出手,D = 2000,
folds = [given ×2.0 byUnit "Twin"]。
期望:M = 2;base 1000 归 Solo;assist 1000 归**我方** Twin(key 1);
敌方 key 50 不出现在结果里;未归因 0。

### S9 同队同名召唤物(阶段 C)

输入:我方 A(key 1)与两个同名召唤物 Turret(key 10、11);A 出手,D = 1500,
folds = [given ×1.5 byUnit "Turret"]。
期望:reason = `byUnit_ambiguous`;**两个召唤物都不得分**;未归因 500。

### S10 同名技能多持有者(阶段 C)

输入:H1、H2 都持有 abilityId 900,N 不持有。

- S10a:H1 出手(origin `text#1/900/c0`)→ `ability_holder_attacker`,自身规则 1000,未归因 0。
- S10b:**N** 出手(同 origin)→ `ambiguous_multi_holder`,未归因 1000,N 自身规则 0。

### S11 未知通道(防御路径)

输入:D = 1000,fold kind = "mystery" ×1.5 → `unknown_kind`,未归因 333.33,base 666.67。

### S12 atkTeam 与 actor 行冲突(阶段 C)

输入:actor 行的 team = 2,但事件 atkTeam = 1。
期望:以 actor 行的 key/team 为准 → 该击不计入分析池,`diagnostics.atkTeam_mismatch = 1`,
`outside_team_events.damage = 1000`。

---

## 8. 已知边界(第一版必须打印,不得掩盖)

1. **attackPower 的攻击力加算已拆(1.7.4)**:**队友给**的加算经 `kind=atkadd` 归到给出者的 assistCredit;只有**自施加**的加算仍留在 baseCredit(见 §11.4、§12.5)。
2. **残差 2%–2.9%**:非折叠倍率(会心、未识别)按其定义留在 baseCredit,报告给出残差倍数分布。
3. **会心无稳定观测通道**:只允许推断标注,不做会心单独归因。
4. **训练场 ×0.03 特例**未纳入通用模型:**默认拒绝分析** quest 9999 导出
   (`contrib.run` 直接返回 3 并说明);只有显式 `--allow-training` 才跑,且结果标
   `diagnostics.modelApplicable=false` + 报告首行 `trainingMode = true`,**禁止**与普通战斗比较
   (跨场比较工具本来就过滤训练场)。试炼场的 ×0.03 建模属于阶段 G 第 4 项。
5. **召唤物**:火砲(30042)、クトゥグァ砲台(39001)、砲台R 是独立 actor key,导出里没有
   "所属主人"字段,第一版保持独立并在报告里以 `summon=true` 标记,不与使用者合并。
6. **totals.dealt 与逐事件求和差 0.0487%**:**已定位**(§1.2)—— 差额 = 14 条"敌人打我方"事件,
   `dealt` 是"攻击者已识别的全部命中(不分敌我)"。并列展示,不强行对齐。
7. 因子精度:**1.6.1 起为往返精度**(如 1.520875 原样写出);**≤1.6.0 只有 4 位小数**(1.5209),
   份额对 pool 的相对误差 < 1e-4。这一条从「可接受的误差」改成「必须无损」,因为贡献段要能从文件精确复算。
8. **没有跨场持久角色 id**:`actors[].key` 只在单场内稳定,`run.id/seq` 是场次计数。
   跨场比较只能用角色名(阶段 D 报告已明确标注该限制);要做到稳定跨场主键需要插件新增身份字段。

---

## 9. 阶段 B 验收恒等式(运行 `python -m contrib.run` 输出)

1. 逐击:Σ credit + 未归因 = D(相对误差 ≤ 1e-9)。
2. 角色:Σ totalCredit + unattributedCredit = analyzableDealt(相对误差 ≤ 1e-9)。
3. 份额:Σ totalShare + unattributedShare = 1(≤ 1e-9)。
4. 折叠记账:总数 = 已解析 + 未归因(精确相等);被排除的折叠单独计数:
   `zero_factor`(因子≤0)、`noop_factor`(因子=1)、`sub_unity_factor`(0<因子<1)。
5. 无任何 credit 落到队外 actor key;无 credit 落到 `attacker="?"`。
6. 手工样例 S1–S6 全部通过(不依赖任何导出文件)。

阶段 C 追加:主键覆盖率、名称回退计数必须为已报告值;歧义不静默分配(S8–S12)。
阶段 D 追加:跨场比较必须同 quest、同模式、样本数 ≥ 2 才给结论。

---

## 10. 阶段 B/C/D 验证结果(1.5.5 场 `battle_411001_20261003_235204` 离线重放)

命令:`cd _dpsm_work; python -m contrib.tests.test_samples; python -m contrib.run; python -m contrib.compare`
原始输出留档:`_dpsm_work/contrib_verify_155.txt`(三条命令 + 退出码)。
插件侧同时复核(证明本次零回归):Release 构建 0 警 0 错;`recon_probe` ALL CHECKS PASSED;
重新构建出的 `src/bin/Release/DpsMeter.dll` 与**当时**部署中的 1.5.5 `18E4D933…`(340,992B)**哈希一致**。

| 项 | 结果 |
|---|---|
| 手工样例 S1–S12 | 全部通过(不依赖任何导出) |
| 逐击恒等式 | 5521 击全部成立,最大绝对误差 1.16e-10 |
| 角色恒等式 | 误差 1e-6(相对 3.8e-15);份额合计 1.000000000000 |
| 折叠记账 | 28369 = 已解析 28369 + 未归因 0(因子≤0:0,因子=1:0) |
| 未归因池 | **0.0%**(1.5.5 起**分析域内** 100% 归因)。`creditedShare=100%` **不等于**整场覆盖:域外与他方伤害见 §12.3,整场覆盖率用 `overallAttributedCoverage` |
| 攻击者主键覆盖 | 5521 / 5588 = 98.80%(其余 67 条 `attacker="?"` 已单列) |
| byUnit 覆盖 | given 6110/6110、madness 897/897 |
| 名称回退计数 | 0(无一条规则落到 `attacker_default`) |
| 归属路径 | byUnit 名称→key 7007;abilityId→持有者 12701;abilityName→持有者 8661 |
| 同击跨通道重复 | 0 |
| 倍率池占可分析伤害 | 76.99%(其中 100% 有主) |
| 因子<1(减伤)折叠 | **0**(普通战斗);试炼场有此情形,默认拒绝分析 |
| 负份额 credit 行 | **0**(曾因试炼场 ×0.03 触发 412%/−312%,已修并加断言) |
| 新核心 ↔ 旧 Stage-0 表 | 共有列最大差 ≤0.5(旧表取整误差),裁决 `MATCHES-LEGACY-ON-SHARED-COLUMNS` |
| 阶段 E 干跑(真实导出) | `check_export_schema` problems=0 + `crosscheck` OK → **DRYRUN=PASS** |
| 聚合段体积 | **25.9 KB = 导出的 0.116%**(11 角色/21 规则/20 关系) |
| 逐击明细体积 | **1,403.6 KB = 6.26%**(5521 击/33,890 条 credit 行)→ 必须放开关后面 |
| 外壳守卫自测 | 17 例全过(含"1 单位取整差须通过 / 100 单位错须被拒") |
| C# 参考实现(`_dpsm_work/contrib_cs`) | 与 Python 核心**逐项一致**(总数/前五角色/11 角色/21 规则/20 关系);独自过两道闸门 |
| 段体积(必填字段版 / 明细版 / 逐击版) | **8.9 KB(0.040%)** / 25.9 KB(0.116%) / 1,403.6 KB(6.26%) |
| 对抗审计(独立脚本,不 import 核心) | 逐位一致:5521 击 / 203,865,411 / 相对误差 3.8e-15 / 28369 折叠 / 未归因 0 |
| 审计修正 F1–F7 落地 | 召唤物行 1,553,932;**死字段 `beneficiaries[].folds` 0 → 49**;M≠knownMult 写明;§1.2 统一 5521 |
| 新增诊断(审计 Q5/Q7) | `calcMissingHits = 1`(真实数据)、`foldDropped = 0`;负份额恒 0 且有 ERROR 级守卫 |
| golden 回归(`tests/test_golden_155.py`) | 上述全部数字 + 每一类理由的观测计数全部钉住 → GOLDEN OK |
| **阶段 E 段由插件真实源码产出** | 离线编 `src/Output/Contribution.cs` 跑真实导出 → `check_export_schema` problems=0 + `crosscheck` OK(11/21/20 双向) → **DRYRUN=PASS** |

**总贡献排名(可加和,1.5.5 场)**:

| 角色 | key | 基础 | 自身规则 | 辅助贡献 | 总贡献 | 总贡献占比 | 直接占比 |
|---|---|---|---|---|---|---|---|
| マッドシーカー | 4 | 13,513,395 | 9,155,038 | 40,038,548 | 62,706,981 | 30.76% | 13.57% |
| ネーフェ＝ジアー | 2 | 11,146,143 | 31,034,255 | 0 | 42,180,398 | 20.69% | 31.93% |
| メアリー | 7 | 5,693,540 | 36,433,945 | 0 | 42,127,485 | 20.66% | 28.71% |
| メルティエル | 5 | 2,455,002 | 3,313,229 | 25,250,506 | 31,018,737 | 15.22% | 3.91% |
| エヴァラス・フラウ | 9 | 4,587,652 | 9,520,576 | 0 | 14,108,228 | 6.92% | 10.82% |
| テトラ | 3 | 8,362,128 | 1,807,523 | 0 | 10,169,651 | 4.99% | 9.59% |
| 火砲 / ルナリス / 生ける炎 / 两台炮台 | — | — | — | 0 | 1,553,932 | 0.76% | 1.45% |
| 合计 | — | 46,906,191 | 91,670,167 | 65,289,054 | 203,865,411 | 100.00% | 100.00% |

> **取整说明(审计 F2)**:上表各列独立四舍五入,取整后相加 = 203,865,412(比精确值多 1 单位);
> 精确恒等式以 §10 首表与 `contrib.legacy_diff` 为准(报告 `[0]` 节打印精确值)。

**规则当量 top5**:母なる変異の飛沫(マッドシーカー,global)30,689,082 · 海魔の残滓(メルティエル,global)
25,250,506 · 狂気 ×2.5(メアリー,madness)20,457,926 · 腐食の毒薬(ネーフェ＝ジアー,text)14,098,226 ·
被伤害+10%(マッドシーカー,given)11,620,366。

**跨场比较(阶段 D,同 quest 411001,离线重放)**:7 份导出分 4 组 ——
1.5.3(ルゥ=ルルサ 组,1 场):归因 73.86%,未归因 26.14%;
1.5.4(ルゥ=ルルサ 组,4 场):归因 85.26%,未归因 14.74%;
1.5.4(メルティエル+火砲,1 场):归因 84.92%;1.5.5(メルティエル+火砲,1 场):归因 **100.00%**。
→ 未归因池的下降**只来自归因元数据的补齐**,与伤害模型无关。样本 <2 的组只列数值、不做结论。

## 11. 阶段 G 第一步:`paramOwners` 段(插件 1.7.2,环境实测前的形状契约)

> 目的:贡献模型对**乘区**(`calc.fold`)记账;攻击力**加算**自 1.7.4 起另经 `kind=atkadd` 归到**给出者**(见 §11.4),只有**自施加**的加算留在出手者的「基础」列。
> 本段把「这条增益是**谁**给的」变成可读数据 —— 它**不改变任何 KPI**,只提供第二步归属公式的输入。

### 11.1 位置与开关
* 顶层键 `paramOwners`,紧跟在既有 `params` 段之后(仿 `params`/`subParams` 的「文本 + 结构化孪生」惯例)。
* 开关 `General/ParamOwners`(默认 true);**该通道只在 `General/StatusResist` 打开时被遍历**
  (它与 `params` 共用**同一次**遍历:活字典走两遍会描述两个瞬间,`Chain.cs` / `Diagnostics.cs` 各写明一次)。
* `params` 段的字节形状**不变**(仍 14 条 + 220 字截断);owner 只出现在本段。

### 11.2 字段
| 字段 | 含义 |
|---|---|
| `reads` / `units` | 遍历次数 / 覆盖单位数(每单位约 500ms 节流一次) |
| `entriesSeen` | 累计折进并集的条目数 |
| `entryReads` | 本场真实读取次数(受 `maxEntryReadsPerBattle` 约束) |
| `entriesDropped` | **被 64 条上限裁掉的条目数**(不再静默:旧的 14 条上限正是 owner 不可见的根因) |
| `budgetCapped` | 触到每场预算而停止的次数 |
| `ownerNull` | 数值侧 `ParamData.Owner` 读不出/为空(「游戏根本没填」本身是可报告结果) |
| `ownerSelf` / `ownerOther` | owner 等于/不等于**该字典所属单位**(判定 owner 语义的关键:全为 self = 它是持有者) |
| `keyOwnerNull` | 键侧 `BuffParam.m_owner` 读不出/为空 |
| `ownerErrors` | owner 属性**抛异常**的次数(先例:1.5.4 的 owner getter 100% 抛异常 → 必须计数) |
| `crossChecked` / `mismatch` | 两侧都有值 / 两侧值不一致 |
| `distinctOwners` | 并集中出现过的 owner 名字数 |
| `maxEntriesPerRead` / `maxUnionRows` / `maxEntryReadsPerBattle` | 三个上限(64 / 1024 / 20000) |
| `unionOverflow` | 并集行溢出计数 |
| `rows[]` | 并集行:`unit` / `owner` / `keyOwner` / `tgt` / `ty` / `val` / `vTgt` / `vTy` / `ref` / `firstT` / `lastT` / `seen` |

**并集**(不是快照)是刻意的:条目过期不得抹掉「它存在过」。这正是旧 `params` 段丢掉 `攻击力/Rate/7` 的原因
——旧的实现只保留**每单位最后一次**节流采样,且按 (target,type,**value**) 合并重复键。
`tgt`/`ty` 用与 `params` 段相同的字符串(可 join),不做数值反查。

### 11.3 母表侧的对应(实测,2026-10-04)
* 天赋 id **6 = `eBuffType.PowerRatePlus`(攻击力%+)**、**8 = `PowerActualPlus`(攻击力+)**;
  作用范围 = 天赋的 `range`(`TalentDefine.RangeType`):**1=Owner、3=FriendTeamAll、29=FriendTeamAllExcludeToken**。
* `ability #12060 エンチャンター` = 天赋 8、**range 29**、`param [[300],[0],[200,3000]]` → 攻击力+300(参照 ExistenceTime3000)、
  **味方全体(不含使魔/トークン)**;210 行能力表里 range 29 的攻击力天赋**仅此一条**。
* `artifact #62/63 水神クタアト` = 攻击力 +7%/+10%、range 3,**文本限定「水属性の味方全て」**(元素门槛不在天赋行里) ——
  这解释了为什么 `攻击力/Rate/7` 只出现在部分我方单位上。
* `artifact #10060 クトゥルフの邪神像` = 自身 +20%(range 1)+ **味方全て +10%(range 3)**;
  实测:连使魔 `火砲`/两台炮台都有 `Rate/10`,而 `Actual/300` 没有 —— **两种范围给出两种「是否含使魔」的结果,全部对上**。
* **仍未 dump 的表**:`potential_skill_data.asset` / `passive_skill_data.asset`(ルナリス 潜在/被动的 +200×2 / +150 在其中)。

### 11.4 第二步(归属公式)的形状与前提(**已实现,1.7.4**;以下为形状契约,落地后仍按此约束)
* 每给予者:`B = (P-a)/(1+r/100)`,`dP_g = B*(r_g/100) + a_g`,`f_g = P/(P-dP_g)`;
  把 `f_g` 插进**既有** fold 列表,核心按 `ln f / ln M` 分池(单给予者时恒等于 `D*dP_g/P`)。
* 输出用新 `kind="atkadd"` + `byUnit=<giver>`,走既有 `byUnit` 解析分支(不要用能力 id/名字 join)。
* **必须无操作**:owner 数据缺失 → 不产生 `f_g` → 段字节不变(768+ 历史导出不得被改变)。
* **必须先计数的退化条件**:`P<=0`、`B<=0`、`dP_g<=0`、`dP_g>=P`、`P<=a`。
* **前提 `P = B*(1+r) + a` 是推断**:本场实测 **104 击 P<a、18 击 P==a**(例:クトゥグア P=12..145 而 a=300),
  说明文本里的攻击力与 `attackPower` **不是同刻**;另有 **72 组/320 击(5.84%)** 同样的增益文本却 P 不同。
* 量级(离线重放,1.7.0 场 `battle_411001_20261004_023627`):解析得出的队友加算当量
  **E = 12,041,920 = 5.667% analyzableDealt = 25.6% baseCredit**(乐观上界 5.852%);
  计入推断的 +200/+150 后 **E = 14,070,595 = 6.621%**。分项:エンチャンター+300 → 2.191%、
  クトゥルフの邪神像+10% → 2.452%、水神クタアト+7% → 1.024%。
  ⚠ **这一段的 5.7%–6.6% 是「按文本解析的乐观上界」,已被实机推翻**:1.7.4 起 `atkadd` 通道实测
  ルナリス 的 `assistCredit = 51,230,121 = 25.1% analyzable`(1.7.6 场 51,781,995 = 27.2%),
  旧估计低了约 4 倍 —— 原因:旧估计只算「她的增益文本能覆盖的那部分」,而实际是**全队每一次命中**都分池。
  最可靠的读数见 `_dpsm_work\CONTRIBUTION-TABLE-REPORT.md` §7。
* **不重复计数**:本场 28,156 条 fold 里带攻击力标签的 = **0**。

---

## 12. schema 1.1 的数学字段与覆盖率(P0-B,插件 1.7.8;契约已冻结)

### 12.1 `totalDamage` 定义被修正

| 字段 | 1.0(1.6.1–1.7.7) | **1.1(1.7.8+)** |
|---|---|---|
| `totalDamage` | `analyzableDealt + unattributedCredit`(**错**) | **`analyzableDealt`** |
| `attributedDamage` | `analyzableDealt − unattributedCredit` | 不变 |
| `unattributedDamage` | `unattributedCredit` | 不变 |

守恒式(1.1):`attributedDamage + unattributedDamage == analyzableDealt`。

为什么 1.0 的写法看不出来:`attributed == analyzable − unattributed`,所以旧的
`attributed + unattributed == totalDamage` **只在 `unattributed == 0` 时成立** —— 而语料里 13/13 份
带该段的导出的 `unattributed` **全部为 0**,恒等式因此从未被触发,也从未被发现。

### 12.2 三个覆盖率(分子分母必须同口径)

| 字段 | 公式 | 含义 |
|---|---|---|
| `coverage.analysisDamageCoverage` | `analyzableDealt / totals.dealt` | 这场伤害里有多少进了分析 |
| `coverage.creditCoverageWithinAnalyzed` | `attributedDamage / analyzableDealt` | 进了分析的部分里有多少归到了人 |
| `coverage.overallAttributedCoverage` | `attributedDamage / totals.dealt` | 整场伤害里有多少既进了分析也归到了人 |

- `coverage.creditedShare` 保留为 `creditCoverageWithinAnalyzed` 的**遗留别名**(值相同)。
- 分母为 0 / 未知时这三个字段写 **JSON `null`**(不是 0):「不适用」和「测得 0」是两件事。
- ⚠ `totals.dealt` 是**双方**的已识别攻击者伤害,所以 `analysisDamageCoverage` 在敌方伤害大的场次会被压低;
  它是「进入分析的比例」,不是「我方伤害的分析比例」。

### 12.3 伤害台账 `damageLedger`(三个桶**不得合并**)

| 字段 | 定义 | 证据等级 |
|---|---|---|
| `totalsDealt` | 游戏自己的 `totals.dealt`(对 OrderedActors 求和) | 实测 |
| `events` | 收到的全部 dmg 事件数 | 实测 |
| `analyzableHits` | 攻击者属我方 team 且可解析的击数 | 实测 |
| `outsideTeamHits` / `outsideTeamDealt` | 攻击者是已知的**他方**单位(设计上不在范围内) | 实测 |
| `unknownAttackerHits` / `unknownAttackerDealt` | 攻击者**无法解析**(key=0 或不在名册) | 实测 |
| `eventSumAll` | 全部 dmg 事件的伤害之和 | 实测 |
| `reconciliationGap` | `totalsDealt − (analyzableDealt + outsideTeamDealt)` | 实测 |

**恒等式(1.7.6 场 `battle_411001_20261004_115417` 实测,全部精确成立)**:

```
analyzableDealt 190,889,625 + outsideTeamDealt 94,717 = totalsDealt 190,984,342   ← 精确
unknownAttackerDealt 273,702 = totals.unattributedDamage 273,702                  ← 精确
events 6,265 = analyzableHits 6,182 + outsideTeamHits 12 + unknownAttackerHits 71  ← 精确
eventSumAll 191,258,044 = 190,889,625 + 94,717 + 273,702                          ← 精确
reconciliationGap = 0
```

**与游戏自身 KPI 的恒等式(1.7.9 补测,全部导出精确成立)**:

```
totals.taken == totals.dealt + totals.unattributedDamage        ← 29/29 份导出一致
damageLedger.eventSumAll == totals.taken                        ← 3/3 份带台账的导出一致
```

第二条把台账与**游戏自己算的** `taken` 绑在一起:§12.3 其余恒等式比较的都是插件从同一份
事件表推出来的数,自洽的漏账骗得过它们,`taken` 骗不过。1.7.8 实测(命名一致的 3 场):
`134524: 195,283,574`、`134853: 201,159,315`、`135214: 11,345,618`。

- **`excludedDamage` = `unknownAttackerDealt`**。它**不含**他方伤害:把敌方正常伤害算成「我方漏分析」
  是口径错误(P0-B 的禁令)。
- `reconciliationGap` **不减** `unknownAttackerDealt`,因为那部分伤害根本不在 `totals.dealt` 里
(游戏把它单独报在 `totals.unattributedDamage`)。这是 411001 长期那个「0.05% 口径差」的完整解释。
- 1.6.1–1.7.7 的导出没有 `damageLedger`;用它们的 `totals.dealt` 与 `analyzableDealt` 相减时,
  得到的差额里**含他方伤害**,不能直接当 excludedDamage。

### 12.4 生产者与版本

| 字段 | 值 |
|---|---|
| `contribution.schemaVersion` | `1.2`(核心契约;插件与离线**共用**同一版本号;1.0/1.1 的旧文件按各自版本读) |
| `contribution.producer` | `plugin` / `offline` |
| `contribution.method` | `log-share/1`(归因算法版本,与 schema 分开) |
| `offlineExtensionVersion` | 仅离线报告:`1`(离线独有字段的版本) |

1.0 的字段名**一个都没改**,所以 1.0 的读者仍能读 1.1 的文件;变的只是 `totalDamage` 的**含义**,以及
新增的覆盖率与台账字段。守卫 `check_export_schema.py` 按**段内 `schemaVersion`**门控(1.0 的读者不会被
写成 1.1 的段跳过,1.1 的段也不会被 1.0 判据放过)。

### 12.5 1.7.9 新增与修正的字段(1.7.10 的两处修正见 §12.6)

| 字段 | 位置 | 含义 | 依据 |
|---|---|---|---|
| `giveFoldHits` | 根(rosterAudit) | 本场**命中**数中真正折进了赋予型被伤害修正的击数 | 实测 |
| `giveApplied` | 根(rosterAudit) | 参与折价的**条目**数(逐条计数) | 修正 |
| `calc.givenFoldOn` | 逐击 | 这一击上 `General/GivenTalent` 开关的取值 | 实测 |

**`giveApplied` 修正的原因(2026-10-04 实测)**:1.3.5–1.7.8 期间同一个静态量被加了两次 ——
`GivenTalentDamage` 里按**条目**加一次,调用方按**击**再加一次,于是导出的
`giveApplied` 是「条目数 + 击数」,不对应文件里任何量。全部 29 份导出上
`giveApplied == (kind="given" 折叠步数) + (带该步的击数)` **精确成立**(28/28 份非零文件;第 29 份 700817 为 0/0/0)。
1.7.9 把逐击增量拆成 `giveFoldHits`,并给逐击加 `givenFoldOn`,这样两个单位再也加不到一起。
守卫 `check_export_schema.py`/`contribution_gate.py` 对 ≥1.7.9 的文件同时校验两条恒等式;
历史文件只出 NOTE(它们无法重写)。

##### 1.7.10 的两处修正(对本节字段语义有直接影响)

1. **两条恒等式的守卫原先从未运行**。`giveApplied` / `giveFoldHits` 写在**根 `rosterAudit`** 里
   (`ExportService.cs` 在此处 append),而 `contrib/crosscheck.py` 最初把**根字典**传给了
   `contribution_gate.give_section_reasons`,于是每次查表都得到 None、整条检查退化成一条 NOTE。
   它通过了自己的扁平字典单测,却在真实管线里一次都没跑过 —— 已改为传 `rosterAudit`,并新增
   **真实文件负控**(`test_gate` CASE3:篡改 `rosterAudit.giveApplied` → ERROR;1.7.9 缺 `giveFoldHits`
   → DATA_MISSING;`giveFoldHits` 错 → ERROR;正确值 → 无 GIVE 错误)。
2. **热修口径**:`giveFoldHits` 比较的是「given 因子**乘积不为 1** 的击数」,而不是「带 given 步的击数」——
   恰好互相抵消的条目(0.5 × 2.0)会登记一步、计入 `giveApplied`,但不让 `GivenFoldApplies` 成立。
   同时若某击在 `FoldContext.MaxSteps` 丢了步,正文看不到那一步,恒等式只能作为**下界**断言(出 NOTE)。

##### 1.7.11:显示口径重组与两条强制恒等式(**无字段语义变化**)

1. **显示**(用户要求):F5 表 1 由 `基础 | 自身规则 | 辅助` 改为 `自身 | 他人因你 | 被队友分走`,
   `自身 = baseCredit + selfRuleCredit`;F6 明细块的第二行改为 `自身 X(基础 A + 自身规则 B)   他人因你 C   被队友分走 D`;
   IMGUI 回退渲染器同步。**导出字段、`schemaVersion`、各数值均未改动**(见 §2.3 的版面口径)。
2. **验证**:`check_export_schema.py` 新增两条强制项 —— `receivedAssist` 从「可选」升为**必需**字段
   (F5 的新列与第二条恒等式都依赖它,缺失会让该列静默显示 0),以及
   `baseCredit + selfRuleCredit + receivedAssist == directDamage` 的逐角色恒等式。
   自测 52 → **54 例**(新增「received 恒等式被破坏 → REJECTS」「缺 receivedAssist → REJECTS」)。
3. **版面守卫改为双向**:`check_contribution_layout.py` 原先只用**手写副本**复算渲染宽度,
   副本与 `OverlayUGUI.Rows.cs` 漂移时它仍会全绿。现在它**解析 C# 源里的列标签与宽度**并与副本逐项比对,
   自测里用「把一列从 11 改成 12」的真实篡改证明该检查会红(见该脚本 `check_source_replica`)。

##### R56:根对象新增 `battleRef` 块(战斗编号;不改贡献口径)

编号是**元数据**,不是新的伤害口径:`contribution` 的方法、`schemaVersion`(仍 1.1)与任何数值都没有改动。

```json
"battleRef": {
  "schemaVersion": "1",
  "id": "B-20261005-143012-7A2C91EF-003",
  "launchId": "20261005-143012-7A2C91EF",
  "sequence": 3, "resetCount": 0, "revision": 1,
  "state": "final", "closeReason": "end"
}
```

| 字段 | 语义 | 何时变 |
|---|---|---|
| `id` | 完整编号 `B-{launchId}-{sequence}`;序号**至少 3 位且不截断**(1000 就是 4 位) | 创建后不变 |
| `launchId` | **本次插件启动**的命名空间 `{UTC:yyyyMMdd-HHmmss}-{16 hex}`;短序号只在这个命名空间内唯一 | 每次启动 |
| `sequence` | 启动内第几场会话 | 创建后不变 |
| `resetCount` | 本场被 F9/重置 的次数 | 每次重置 |
| `revision` | **内容快照**修订号 | 软恢复 / 重置 / 新快照导出;UI 渲染不动它 |
| `state` | `live` / `provisional` / `final`;`final` 要求收尾原因为 `end` **且**有胜负 | 收尾 |
| `closeReason` | 实际收尾原因 `end`/`idle`/`teardown`/`restart` | 收尾 |

- **旧导出没有这个块,且永远合法**:它们按 `legacy:<SHA256>` 引用(离线工具 `battle_select.py`),不回填、不改写。
- 校验入口:写盘前 `BattleRefPolicy.ValidateBlock`(id/launchId/sequence/revision/state 自洽),默认导出路径还要求
  **文件名以该编号结尾**;身份校验失败时**不会**被标记为"已导出"。
- 消费者:`battle_select.py`(只读文件头即可列出;解析与比较前复核 SHA256)、悬浮窗(编号与状态)、
  证据包里的 `battle.json`(同一个序列化器,因此同编号同 revision)。

##### R60(插件 1.7.12):同队/自我伤害进导出与 F5 表(**可选字段,既有数值一个都没动**)

用户要求「恢复自伤判定」,三处一起做:

1. **根 `config` 块(自 1.7.12 起必需)**:`{"filterFriendlyFire":bool}` —— 这是唯一会改变**既有数字含义**的
   开关(它决定 `totals.dealt` / `perSecDamage` / `actors[].hit` 是否含同队伤害)。导出不写它时,单看文件无法
   判断 `dealt` 用的是哪套口径;`check_export_schema.py` 现在按版本要求它存在。
2. **`contribution.actors[]` 增加三个可选字段**:`friendly` / `friendlyHits` / `hostileDamage`。
   `friendly` 是「受击方与攻击者同队」的命中金额(含 `attacker == victim` 的自我结算),`hostileDamage` 是其余;
   两者由**逐事件 `friendly` 标志**分类,因此 **`friendly + hostileDamage == directDamage` 精确成立**,且
   **不受 `FilterFriendlyFire` 影响**(该开关只动游戏口径的 `dealt`,不动归属账)。
   字段可选是因为 1.7.12 之前的 15 份带段导出没有它们:`check_export_schema.py` 只在字段存在时校验类型与
   恒等式;`contrib/crosscheck.py` 只在**插件版本 >= 1.7.12** 时把它们当必比对字段(否则整档会因「旧文件
   缺字段」变红)。
3. **F5 表 1 恢复「自伤」列**(1.5.x 曾有;列宽 9):T1 由 8 列 83 / 行 85 变为 **9 列 92 / 行 94**,
   Contribution 页面板上限 780 → **880**(等宽排版下,面板不跟着变宽就会把列画到背景外)。
   自伤是**报告列**:它不移动任何 credit,「自身」「直接打出」的口径与数值不变。

**顺带修掉一个既有缺陷(与本轮直接相关)**:`contrib/crosscheck.py` 的 `totalDamage` 期望值一直是 **1.0 的公式**
(`analyzable + unattributed`),对 schema 1.1 段(1.7.8 起 `totalDamage = analyzableDealt`)只要
`unattributedCredit > 0` 就会把**正确的**文件判成 MISMATCH。实测 `battle_411001_20261005_142931`
(未归因 3.885%)在改动前就是 ERROR。现在按**段内 `schemaVersion`** 选公式,与 `check_export_schema.py` 一致。
冻结语料(rf0)里没有「1.1 段 + 非空未归因」的样本,所以这个缺陷此前没有被验收抓到。

##### R59(文档,2026-10-05):同队/自我伤害的口径补充(**无字段、无数值变化**)

来源:换人对比的独立分析发现「`actors[].dealt` 会把 `attacker == victim` 的自我结算算进去」
(`B-20261005-101631-C3F24E1814C94EE5-001` 的 `T.O.W.E.R.typeR`:`dealt = 2,007,881` 里有 2,006,442 是自伤)。
代码行为**是既有设计、不改**:`Aggregator.Stats.cs` 的 `Accumulate` 规定同队伤害「始终单独计数、默认仍计入
`dealt`」,理由是**游戏自己的战报也算它**(实测 `game_given == 同队 + 正常`;0.9.4 曾反着改过,0.9.5 已纠正),
`Overlay/FilterFriendlyFire` 可剔除。缺的是**口径文档没有把它写成读者必须知道的一条约束**。
本次只补文档:§1.1 / §1.4 / §2.1 写清「默认计入 + 单独计数 + 怎么扣」,报告与根级代理手册同步。

**已知缺口(留给将来的代码轮次,不属本次)**

1. `Overlay/FilterFriendlyFire` 的生效值**没有写进导出**:单看一份文件无法判断 `dealt` 含不含同队伤害
   (逐事件 `vicTeam` 能绕过,但「数据自我描述」这条边界应当补上 —— 与 1.7.5 修掉「导出里一句已经变成谎话的
   自我描述」同类);
2. `contribution.actors[]` 没有 `friendly` 字段,只能 join 根 `actors[]`;
3. `perSecDamage` 没有可扣的同队时间序列,逐秒口径只能回到 `events[]`。

##### R61(插件 1.7.13):同队/自我伤害**移出归属池**(schemaVersion 1.2)

用户判断(依据一场真实战斗):自伤是**敌方的治疗反转(回復反転)**造成的,「打自己并不造成实际贡献」。
实测(`B-20261005-115024-D88099F7C45D4078-001`,任务 9999):同队伤害 **1,924,021 / 30 击 = 当时分析池
6,146,573 的 31.3%**,其中 `T.O.W.E.R.typeR` 的 `directDamage = 1,878,273` 全部是自伤、对敌输出为 **0**,
却因旧口径占了 **30.56%** 的总贡献。

- **契约 1.2**:`analyzableDealt` 只含**对敌命中**(条件见 §1.2 第 5 条);`damageLedger` 新增
  `selfTeamHits` / `selfTeamDealt`;恒等式变为
  `totalsDealt == analyzableDealt + outsideTeamDealt + selfTeamDealt` 与
  `events == analyzableHits + outsideTeamHits + unknownAttackerHits + selfTeamHits`;`reconciliationGap` 同步减去新桶。
- **逐角色**:`directDamage` 变成对敌(入池)伤害;`friendly` / `friendlyHits` 仍在,含义是**被排除并计数**的同队量;
  R60 加的 `hostileDamage` 在 1.2 里**不再写**(它已经等于 `directDamage`)。
- **F5 表**:分母变小,其余角色占比按比例上升;「自伤」列保留(显示被排除的量);口径文案改为
  「自伤=敌方治疗反转/自伤…已从贡献里排除、不归属任何角色,只在此单列」。
- **不改游戏口径**:`totals.dealt` / `actors[].dealt` / `perSecDamage` 照旧含它(游戏自己的战报也算),
  文件仍自述 `config.filterFriendlyFire`。
- **旧档不重算**:离线核心按**文件自己的 `schemaVersion`** 选模式(<=1.1 时同队命中原样在池内),
  所以 rf0 冻结语料与全部历史导出的 crosscheck 结论不变;只有 1.2 的新文件按新口径比对。
- **实测(同一场,1.2 口径)**:`analyzableDealt` 6,146,573 → **4,222,552**,命中 676 → **646**,
  被排除 1,924,021 / 30 击,`attributed == analyzable`(域内 `creditedShare` 仍 **1.0**);
  前四名占比 14.06 / 14.02 / 12.94 / 10.34% → **20.46 / 20.41 / 18.84 / 15.06%**,`T.O.W.E.R.typeR` 退出榜单。

**未覆盖(与 R60 同类)**:`ExportService`/`Contribution.AppendJson` 的 1.2 新形状没有在 BehaviorTests 里逐字段跑;
它由 `check_export_schema.py` 的 1.2 用例(5 例)与离线 `crosscheck` 的版本门控覆盖。
##### R62(插件 1.7.14):三个**读数**问题(A/B/C)

本轮**不动归属口径**(`schemaVersion` 仍 1.2),只修三处会让人读错数的地方。三条都是「另一个智能体拿插件数据做分析时提出、逐条回数据核实」的结果。

**A. 命中记录的配对错位(导出字段层)**

- **现象(实测)**:`B-20261005-134130-83559094DF634D7F-009` 的 `factId=127`(t=22.47,4,220)与 `-010` 的 `factId=173`
  (t=25.90,3,798):同一击的 `calc` 块是 `hitType=3`(**贯通**)/`ratio=0.10`/`power=攻击力x10%`,而事件层的
  `source=0`、`calcHitType=2`(魔法)、`calcEffectId` 还带着主伤害的技能 id。
- **成因**:`source` / `calcHitType` / `calcEffectId` / `hitValue` 来自 `HitRecord` 通道,它按「攻击者+目标」在 0.35 s 内
  配对且**不要求值相等**(`hitMatch=2`);而一次结算可以留下**多条**记录(DamageAction/ActDamageAction + 被吸收的记账调用),
  于是下一次同目标的命中会**吃掉上一条命中的记录**。
- **范围(全语料 82 份 / 204,168 条伤害事件)**:`calcHitType != calc.hitType` 共 **432 条(0.22%)**;10% 贯通命中被记进
  `sources[0]` 的:训练场 **9999 = 3,240/11,096(29%)**,普通关卡 **411001 = 6/39,467(0.015%)**。
  **这个数必须拆开看**(1.7.15 实机复核):1.7.13 的 5,616 条贯通命中里 `source=1` 正确 3,818 条、**根本没有可用记录
  1,771 条**(`hitMatch` 缺席,不可修)、**记录属于别的命中只有 27 条** —— 「`source` 为 0」里约 **97% 是「没有记录」而不是「记错」**。
  A 修的是后一小类:1.7.15 实机 5,746 条贯通命中里 **52 条被拒绝(`hitMatch=3`)、0 条错标留存**。
- **口径(1.7.14 起)**:`hitMatch` 增加 **`3` = 「已配对,但被合成证伪后拒绝」**;这类命中**不再**用那条记录写
  `source`/`calcHitType`/`calcEffectId`/`crit`/`skills[]`,而是留空(未知),并计入 `hitDetail.matchRejected`。
  判据 `AttributionPolicy.RecordContradictsComposition(记录命中属性, 合成命中属性, 合成有效, 合成可信)` —— 只有
  合成是 `live-same`/`value`(可信)**且**两侧都读到了命中属性**且**不相等时才拒绝。
- **读法**:任何用 `source==1` 或 `calcHitType==3` 认「直接攻击/贯通」的分析,在 9999 里会有约 29% 的贯通命中 `source` 为 0(其中约 97% 是**没有可用记录**,并非记错);
  **认贯通请用 `calc.hitType==3`**(它来自合成通道,与 `calc.ratio`/`power` 自洽)。

**B. 有吸收时 `residual` 会误导(离线口径层)**

- `calc.residual` 的定义是 `applied / theory`(入耐久 / 理论),被吸收的部分不在分子里:全语料 **801 条** `absorbed>0`,
  其中 **688 条** `residual<1` —— 一条被屏障吸收的会心看起来就像「缺了 0.03 倍」。
- **1.7.14 起**:离线核心用 **`model.game_residual(calc) = (applied + absorbed) / theory`**(即插件 `valueMatches` 用的同一个量),
  `diagnostics.residual_buckets` 按它分桶,另计 `residual_absorbed_hits`;`HitResult.residual_mult` 仍保留导出原值。
  回答「链算没算对」请用 `calc.valueMatches` / 事件 `nominal`,不要用 `residual`。

**C. 全局「敌方受伤」因子被算进了 `dealtMult`(栏位层)**

- 战域规则表里既有**被伤害**(`EnemyTakes`,如「毒/火傷状態の敵全ての被ダメージ+15%」)也有**与伤害**
  (「味方ヴァイスの魔法攻撃の与ダメージ+15%」);1.7.13 及更早把两半都乘进了 `dealtMult`。
  实测 `battle_411001_20261003_205449` t=3.63:`毒の短剣(与伤害 1.15)` + `母なる変異の飛沫(敌方受伤 1.15)` 导出成
  `与伤害 x1.322 - 被伤害 x1.000`。
- **乘积从来没错**(`knownMult = attrMult x dealtMult x takenMult` 不变,`theory`/`valueMatches` 与离线贡献模型都不受影响 ——
  模型只读 `fold[]` 的 `side`/`kind`,`fold[]` 从 1.5.0 起就是对的)。受影响的是**分开读与伤害/被伤害**的表格与文案。
- **1.7.14 起**:`EnemyTakes` 的因子乘进 `takenMult`。判据:`dealtMult == prod(side==atk 的 fold)` 且
  `takenMult == prod(side==vic 的 fold)`(**实测/离线重放**:把因子搬回去后,82 份语料里 **197,533/197,533 条**带 fold 的命中都满足)。
  **`<=1.7.13` 的旧文件仍是旧栏位** —— 它按各自契约读(`check_export_schema.py` 的该检查版本门控在 1.7.14)。

**守卫**:`check_export_schema.py` 新增 1.7.14 起必须存在的 `hitDetail.matchRejected`(3 例自测)与上面的 fold 栏位恒等式(3 例自测);
`contrib/tests/test_samples.py` 新增 S14(`game_residual` 与吸收分桶);变异负控新增 5 条(`pair-reject-*`,逐条红在具名用例)。
**未覆盖**:A 的「拒绝」分支在插件侧(`Aggregator.Stats.RecordDamage`),BehaviorTests 编译不到;它由纯策略函数的行为用例 +
负控钉住判据,端到端效果要等下一场 1.7.14 实机导出的 `hitDetail.matchRejected` 与 `sources[]`。

##### R63(插件 1.7.15):新增「自动技能」主表 + 冷却单位规则

> ⚠️ **R65 更正:本节第 2 条(「单位是两套…」)对这张表是错的,`minCoolTimeFrames`/`maxCoolTimeFrames` 两列已被删除。**
> 原文保留在此作为**当时那一刻**的记述;正确的口径见下面的 `##### R65`。实机实测:线上 `Skill.CoolTimeFrame`
> 与主表 `minCoolTime`/`maxCoolTime` **字面相等 9/9**,没有一个等于 ×30。

本轮**不动归属口径、不动任何既有数值**(`contribution.schemaVersion` 仍 **1.2**,导出形状未变)。
新增的是**取证面**:一张主表和一条单位规则。

- **主数据新增一张表**:`auto_skill`(`Rog.MasterData.AutoSkillMasterTable`),即 `awake_potential.auto_skill_id`
  指向的那张。定位方法(实测):`awake_potential` 里 `unit_id=U / category=3 / acquire_id=403` 的 `auto_skill_id` **就是 U**
  (unit 84 = `[賢導]トレイラ` -> 84),category 4 是空的第二槽。字段:`id/name/text`(混淆)、
  `autoActivate/maxLevel/minFirstCoolTime/maxFirstCoolTime/minCoolTime/maxCoolTime/minDurationTime/maxDurationTime/
  skillRange/stock`(全 **ObscuredInt**,经 `GameRef.Dec`)、`activationType/activationTypeParam/activationPositionSortId`(明文)、
  `isTargetUnnecessary`、`triggerTimings`(嵌套 `SkillMasterDataBase.TriggerTimingData`)、`talents`。
  表数 **19 -> 20**,输出文件 **20 -> 21**(含 `_table_registry.json`)。**旧导出不受影响**(这只改 `masterdata/` 的产物)。
- **单位是两套,所以两套都给**:主表冷却字段的单位是**秒**,而线上 `Skill.m_coolTimeFrame` 数的是**游戏帧**;
  `[CLOCKP] … units/s=30.0`(由 `Skill.CoolTimeFrame / Skill.CoolTime` 反推)与 `Plugin.cs`:344 的实测
  (750/25、1500/50、1050/35)给出 **1 游戏秒 = 30 帧**。因此 `auto_skill` 行里
  `minCoolTime`/`maxCoolTime` 是主表原值,`minCoolTimeFrames`/`maxCoolTimeFrames` 是按**本次进程实测到的** units/s
  (读不到就用 `BattleClockPolicy.DefaultUnitsPerGameSecond`)**换算**的值。判据是纯函数
  `src/Policy/SkillCooldownPolicy.cs`(`Frames(seconds[, unitsPerGameSecond])`),**非正输入与不可用速率一律给 0**
  ——「没有数可发布」不能与「冷却为 0 帧」混为一谈,而主表的秒数无论如何都照原样发布。
- **为什么值得单独成规则**:只 dump 秒数,消费方迟早会拿它跟帧计数、或跟战斗秒直接比 —— 这正是「dump 出来是为了不再靠猜」要避免的错。

**守卫**:`tests/BehaviorTests/Cases.SkillCooldown.cs` 16 例(用实测的 25/50/30 秒 -> 750/1500/900 帧回环);
变异负控 2 条(`skill-cooldown-forgets-the-unit-rate`、`skill-cooldown-decouples-the-fallback`,逐条红在具名用例)。
**未覆盖**:`MasterDataDump.cs` 不在 BehaviorTests 的编译清单里,所以**转储代码本身没有行为测试** ——
被覆盖的是它调用的规则,而「字段名正确」由**编译器**保证。

##### R64(插件 1.7.16):自动技能的**运行时**读法 + 两个 id 空间不是一回事

本轮**不动归属口径、不动任何既有数值**、不改导出形状、**不新增导出根段**
(`contribution.schemaVersion` 仍 **1.2**)。新增的是**取证面**:一条**只读**探针,以及一条必须写下来的**口径**。

- **口径:`auto_skill` 表的 id 空间 ≠ 伤害计算里的效果 id 空间。** R63 的表里存在 id `10024`
  「恐怖の特異点」,而战斗日志里每 ≈14 s 出现一次的「效果 `10024`」只是 `DamageCalculater.m_effectId` ——
  **两者不是同一个编号体系**,不能互相引用。这条是 R63 §7 留下的待办,现在写进来:凡是要把
  「日志里的效果 N」与「`auto_skill` 的第 N 行」对上,都必须先证明那个 N 是从哪来的,**默认假定它们无关**。
- **自动技能在运行时是普通 `Skill`,而且不在 `[CLOCKP]` 走的那个列表里。** `AutoSkillMasterData :
  SkillMasterDataBase`(ilspycmd),所以它最终就是 `SkillData` -> `Skill`。判别靠游戏自己的
  `Skill.m_type : Skill.Type { Skill, SpecialSkill, OverSkill, AutoSkill1ForPassiveSkill,
  AutoSkill2ForPassiveSkill }`,状态靠 `Skill.GetStatus() : Skill.Status { NotHave, Charge, Usable, Using }`。
  实测佐证(2026-10-06):`PlayerSkillStandbyData` 列表渲染出的 3 个技能名
  (地下からの完全顕現 / 電脳掌都 / 狂気の眼球)在**全部 21 个** `masterdata/*.json` 里**一个都不出现**,
  而 4 个候选名字里只有 暗沌への導き 命中,且只在 `auto_skill.json` 里 —— 即 **`TimeProbe` 那条路线永远读不到自动技能**。
- **读什么、写在哪**:`Player.AutoSkill1/2` -> `Skill.WaitCountFrame`(充能)/ `CoolTimeFrame`(满格),
  写成运行日志的 `[AUTOSK] act`(发动时刻:墙钟 + 战斗钟 + 单位/槽位/技能名/类型/状态/充能/时长/库存/游戏返回值)、
  `[AUTOSK] chg`(每槽每 2 s 一行充能轨迹;`upsGame`/`upsWall`/`chargeSec` 只由**相邻两次打印**算出,所以能用手算复核)、
  `[AUTOSK] SUM`(收尾:每槽发动数 + 墙钟/战斗钟**中位发动间隔** + 全部计数器)。
  换算的判据是纯函数 `src/Policy/AutoSkillCadencePolicy.cs`(`UnitsPerSecond` / `SecondsFor` /
  `IntervalSeconds` / `Median`);`chargeSec = SecondsFor(cool, upsGame)` **就是**把充能折算回 R63 那条
  「主表秒数」的口径,两者可对账。
- **为什么值得单独成规则**:它是把「自动技能 240–300 秒」与「从伤害通道反推的 ≈13.5 s」这对矛盾拆开的
  **唯一**办法(继续从伤害反推永远分不开),而这一步的算术必须能被离线执行,不能只活在探针里。

**守卫**:`tests/BehaviorTests/Cases.AutoSkillCadence.cs`(R65 起 31 例:实测的 240/150/210/420/2970 单位必须
分别回到 8/5/7/14/99 **游戏秒**、同一充能在真实钟上更短、回填的计数器保持负号、`0` 是「没测到」的哨兵而不是节奏、
中位数不就地排序);变异负控 4 条(`autoskill-charge-forgets-the-clock`、`autoskill-rate-clamps-a-backwards-counter`、
`autoskill-median-keeps-the-sentinel`、`autoskill-median-sorts-the-callers-array`,逐条红在具名用例)。
**未覆盖**:`AutoSkillProbe.cs` 不在 BehaviorTests 的编译清单里(需要 IL2CPP 面),所以**探针本身没有行为测试**。
(「patch 是否解析上」与「自动技能几秒一发」都已由下一场实机回答,见下面的 `##### R65`。)

**实机结果(已确认)**:部署后用户自行重启,自检行 `表成功=20 表缺失=0 表异常=0 行异常=0`、`auto_skill=120`;
`masterdata/auto_skill.json` 落盘。她那一行(unit 84 -> `awake_potential` category 3/acquire_id 403 ->
`auto_skill_id=84`)是 `暗沌への導き`:`minCoolTime/maxCoolTime = 300/240`(**主表单位 = 秒**)、
`minFirstCoolTime/maxFirstCoolTime = 240/180`、`stock = 0`、talents 里 `6 攻击力%+` param 300 / maxParam **500**
(即实测到的那层 `攻撃力+500%`)、`1013 连射` = 4 连、`516 暗闇` 末位 300(300 单位 = 10 s,与正文「10秒間」一致)。

> **口径警告(必须一起引用)**:`auto_skill` 表的 id 与伤害计算里的**效果 id**(`DamageCalculater.m_effectId`)
> **不是同一个 id 空间** —— 该表里也有 id `10024`(「恐怖の特異点」),与战斗日志里那个每 ≈14 s 出现的
> `效果10024` 不是一件事。用 `auto_skill` 的冷却去解释一条伤害通道的节拍之前,先确认两者指的是同一个技能。

##### R65(插件 1.7.17):实机证伪 R63 的 ×30 —— 主表值 **就是** 充能单位,不是秒

本轮**不动归属口径、不动任何伤害/归属数值、不改导出形状**(`contribution.schemaVersion` 仍 **1.2**)。
它改的是**上一节那条单位规则的结论**,以及**转储里由它产生的两列错数**。

**新口径(实测,可复核):`auto_skill` 的 `minCoolTime`/`maxCoolTime` 与线上 `Skill.CoolTimeFrame` 同单位,
二者字面相等。** 2026-10-06 的一场训练场 9999(44 秒、468 行 `[AUTOSK]`)量到 9 个自动技能:

| unit | 技能 | 线上 `CoolTimeFrame` | 主表 min/max | ÷30 = 游戏秒 |
|---|---|---|---|---|
| レヴナント | 神経の暴走 | 150 | 210/150 | 5.00 |
| T.O.W.E.R.typeR | 電触補壁 | 300 | 420/300 | 10.00 |
| ポポロット | 白眼視の瞬き | 210 | 270/210 | 7.00 |
| ソフィー | 狂骨の祝采 | 420 | 420/300(未满级取 min) | 14.00 |
| メルティエル | 邪龍の息吹 | 240 | 360/240 | 8.00 |
| **[賢導]トレイラ** | **暗沌への導き** | **240** | **300/240** | **8.00** |
| マッドシーカー | 実験失敗！ | 2970 | 2970/2970 | 99.00 |
| ムスクーマ | 万物不浄の昇華 | 210 | 270/210 | 7.00 |
| ネフェスティス | 緑炎の生気吸収 | 240 | 300/240 | 8.00 |

- **要换算就除以速率,不要乘**:这一场的充能速率实测 **30.0 单位/游戏秒**(84 个有效样本的中位数;
  min 0.7 / max 30.0),真实钟上约 **44.5 单位/秒**,比值 1.483 —— 即 30 单位/游戏秒再次独立成立,
  但这次是从**自动技能自己的充能计数器**量的,不再依赖主动技能的 `CoolTimeFrame/CoolTime`。
- **转换**:`auto_skill.json` 的信封现在带 `unitsPerGameSecond`(进程实测,读不到就用策略默认 30.0);
  `minCoolTimeFrames`/`maxCoolTimeFrames` 两列**已删除**(它们是 `×30`,即真值的 30 倍),
  `src/Policy/SkillCooldownPolicy.cs` 一并删除 —— 它的算术没错,但它唯一的调用点被证伪了。
- **答案(用户问的那个)**:トレイラ 的「暗沌への導き」充能 240 单位 = **8.0 游戏秒**(≈5.3 真实秒);
  充满后它停在 `Usable` 等**下一次普攻**才真的打出去(她自己的正文「次回攻撃時のみ」),本场 3 次发动在
  游戏钟 6.27 / 21.47 / 33.50 秒 ⇒ **间隔 15.20 / 12.03 游戏秒**(真实 10.18 / 8.08 秒)。
  **R63 §3c 那个 ≈13.5/15.9 s 的反推因此被判定为「本来就对」** —— 它是从导出 `events[].t` 来的,
  那是**游戏钟的秒**;R63 的错在于拿它去对「240–300 **秒**」。
- **两条审计口径**(这一轮探针自己暴露并修掉的):①`Using` 上升沿**不是发动** —— トレイラ 的 `Using`
  会抖动,曾给出 1.10 s 的假中位间隔(真实 8–10 s),现在它写 `via=usingEdge` 并**排除在所有间隔之外**,
  发布的间隔只来自命令通道 `via=cmd`;②游戏传给 `ActExecutePlayerAutoSkillForPassive` 的 `index` 实测是
  **0 或 1**(我只认 1/2 ⇒ 32 行落进无名垃圾槽,还吃掉了槽位额度把真槽位挤掉),现在 `gameIdx=` 原样打印、
  解析不到就计入 `cmdUnresolved` 且**不建槽**,`gameIdx=0` 的语义留给下一场由 `[AUTOSK] roster` 行定死。

> **实践规则(本轮教训,适用于所有主表)**:一条「单位规则」只能由**它自己那张表的实测**支撑,
> 跨表外推必须附一个反例检查。R63 从主动技能(25 秒 ↔ 750 帧)外推到 `auto_skill`,而后者**没有**
> `coolTime` 字段、只有 min/max 对 —— 恰好是最不能外推的形状,结果一个没有数据支持的 ×30 进了产物。
> 检查形式很简单:把「字面相等」与「×30 相等」两列并排数一遍(本轮是 9/9 对 0/9)。

> 本轮正是撞在这上面:主表给她的冷却 240–300 秒,而从伤害反推的节拍是 ≈13.5 s,**两者对不上**,反推结论存疑
> (详见 `REFACTOR-BATCH-R63.md` §3c)。

##### R66(插件 1.7.18):`minCoolTime`/`maxCoolTime` 是**技能等级区间**;充能**不是**发动的闸门

本轮**不动归属口径、不动任何伤害/归属数值、不改导出形状**(`contribution.schemaVersion` 仍 **1.2**)。
它读的是**同一批 `[AUTOSK]` 证据**,但样本从 1 场扩到 **9 场**(训练场 9999,`[AUTOSK]` 共 4,032 行,
其中 **471 行是发动**),于是 R65 的两条结论必须改。

**① 主表那一对数是「技能等级」区间,不是"同一等级下的最小/最大"。** `Skill.CoolTimeFrame` 按技能等级在
L1 的 `minCoolTime` 与满级的 `maxCoolTime` 之间取值 —— 5 个单位 × 2 个等级,10 个观测点全部落在这条规则上:

| 单位 | 技能 | 主表 min/max | L1 | L2 | L3(满级) |
|---|---|---|---|---|---|
| ソフィー | 狂骨の祝采 | 420/300 | **420**(×70) | — | **300**(×3) |
| T.O.W.E.R.typeR | 電触補壁 | 420/300 | **420**(R64 那场) | — | **300**(×79) |
| [賢導]トレイラ | 暗沌への導き | 300/240 | — | **270**(×3) | **240**(×394) |
| ネフェスティス | 緑炎の生気吸収 | 300/240 | — | **270**(×10) | **240**(×48) |
| ポポロット | 白眼視の瞬き | 270/210 | **270**(×2) | — | **210**(×80) |

这**同时回答了 R65 §7 悬着的那件小事**(「T.O.W.E.R.typeR 同一槽一会儿 420 一会儿 300」):R65 那 4 行 420
带 `level=1`,而同一场的 `chg` 行带 `level=3` —— 是**同一技能的两个等级对象并存**(升级瞬间的旧对象还没被
释放),不是"槽位换了技能"。R65 因此把身份键从槽号改成技能身份,方向是对的,但**理由写错了**;R66 再给身份键
加上**对象标签** `inst=`(并在 `SUM` 行附指针),把「同名同 id 的两个对象」从"一行自相矛盾"变成"两行各说各话"。

**② 充能不是发动的闸门 —— R65 那句「充满后等下一次普攻」作为通则撤回。**
> **本节(R66 §2b)已被 R69 撤回:R65 最早的说法才是对的。** 下面那张"中位间隔短于充能"的表是**探针缺陷**的
> 产物 —— 页面/统计把**命令被调用的时刻**当成了**技能发动的时刻**,而 マッドシーカー 那次 25 个调用里只有 1 次
> 真发动(另 23 次是 `Charge` 期的试触发)。R65 用 3 个样本给出的通则是对的,错的是 R66 拿它去跟"调用数"比。
> 详见 `REFACTOR-BATCH-R69.md` §1/§2 与本文件 R69 节。以下原文保留,勿引用。

471 次发动里 **305 次** `wait==cool`、**157 次**发生在 `status=Charge`(没充满)、9 次 `Usable`;并且**至少 6 个单位的中位
间隔短于自己的充能**:

| 单位 | 充能(游戏秒) | 实测中位间隔(游戏秒,战斗钟) |
|---|---|---|
| マッドシーカー 実験失敗！ | **99.0** | **4.97**(91 个间隔;发动时 `wait` 读 2870→2721→2572→2423,一路在降) |
| ソフィー 狂骨の祝采 | 14.0 | 4.30 |
| T.O.W.E.R.typeR 電触補壁 | 10.0 | 4.53 |
| メアリー 影爪の強制裁断 | 13.0 | 10.10 |
| レヴナント 神経の暴走 | 5.0 | 3.93 |
| ルーチェルト 巨人の鉄槌 | 5.0 | 2.50 |
| [賢導]トレイラ 暗沌への導き | 8.0 | **9.00**(47 个间隔;min 0.20 / p25 6.60 / p75 13.13 / max 13.50) |

所以 `WaitCountFrame`/`CoolTimeFrame` 是**给玩家看的资源条**,不是发动判定。R65 报的 15.20/12.03 是
トレイラ 那一场**只有 3 次发动**时的高位值,不是规律;**「什么触发它」现在由游戏自己的字段回答** ——
R66 已把 `Skill.ActivationType`/`ActivationTypeParam` 打进行(`act=`/`actP=`),下一场日志才是判据。

**③ 时钟口径(给所有按时刻对齐的分析)**:战斗钟(`Session.ActiveSeconds`)会**停** —— 实测 `wait` 维持
186/240 达 **6 个真实秒**而 `active` 一动不动(`wall` 照走)。所以间隔只能在**战斗钟**上量:イグナ 的 22 个
间隔里 `dActive` 是 7.57–7.80(极紧)而 `dWall` 是 5.17–27.41(被暂停拉散)。

> **实践规则(本轮教训)**:一个「资源条」不等于「触发条件」。R65 用 3 个样本、一个单位就给出了通则
> ("充满后等下一次普攻"),而 9 场、471 行的语料里有 157 次反例。**下结论前先数反例,并写明样本量**;
> 反例存在时,正确的产物是**把游戏自己的判据字段读出来**(这里是 `Skill.ActivationType`),而不是把
> 观察到的相关当成因果。

##### R67(插件 1.7.19):时间表的两个口径与 `gameIdx` 的定义

本轮**不动归属口径、不动任何伤害/归属数值、不改导出形状**(`contribution.schemaVersion` 仍 **1.2**)。

- **时间表的 `med` 与 `[AUTOSK] SUM medianActive` 是两个量**:前者是**合并后格子**的相邻间隔中位数,
  后者是**未合并**逐次发动的中位数。没有连发的行两者相等(实测 4/4),有连发的行不同(实测 3/3,
  例 エヴァラス 11.25 vs 11.10)。引用时说明用的是哪一个。
- **连发标记 `并N条M格`**:N = 被 0.25 战斗秒窗口并进前一格的原始条数,M = 收到它们的格子数。
  R66 的 `xN` 只给一个数,对"两格各并一条"会写成 `x3`(实测 3/5 个带标记的行是这种情况),已废弃。
  **窗口口径在 R69 改为「该技能自己的冷却」**(0.25 s 只是读不到冷却时的下限),见下面的 R69 节。
- **`gameIdx`(`GameCmdExecuter.ActExecutePlayerAutoSkillForPassive` 的 `index`)**:`0` = `PassiveSkills`
  里第一个带自动技能的被动(0 基),`1` = `Player.AutoSkill1`(1 基槽位);实测两者是**同一个 `Skill` 对象**
  (`inst=` 每个真实自动技能只有一个标签),所以按技能身份汇总是对的、按 `gameIdx` 分开是错的。
- **奥义/特殊的观测通道**:R66 的 `AddPlayerSkillGameRecord` 在训练场 9999 一整场**零调用**
  (连跳过计数都是 0),不能作为数据源;R67 改为 `ActExecutePlayer{ActiveSkill,Skill,SpecialSkill}`
  (`skl`),其可用性以下一场 SUM 的 `sklCalls` 为准,在那之前奥义一列只能读作"已观测"。
  **R69 结案**:正式任务 411001 里该通道 **595 次调用 / 游戏自己拒绝 554 次 / 受理 41 次**,41 条全部
  `Skill.Type=2`(奥义)—— 通道可用;`AddPlayerSkillGameRecord` 在正式任务里**同样是 `rec=0` 且跳过计数全 0**,
  因此 R69 **删掉了那个钩子和整个 `rec` 通道**。

##### R69(插件 1.7.20):一条命令调用**不等于**一次发动;折叠窗口改成技能自己的冷却

本轮**不动归属口径、不动任何伤害/归属数值、不改导出形状**(`contribution.schemaVersion` 仍 **1.2**)。

- **发动的判据是游戏自己的状态**:调用返回时 `Skill.GetStatus() == Using` 才算**发动**;`Charge`(还在充能)与
  `Usable`(充满但没执行)是**试触发**,只计数(`[AUTOSK] SUM tries=`、页面尾部 `试N`),**不上时间轴、不进中位**。
  状态读不出来(`?`)或落在未知值上时**两者都不是**(失败关闭,计入 `unclassified=`)—— 宁可少报,不可凭空报一次发动。
  判据来自实测:マッドシーカー 的 実験失敗！(冷却 2970 帧 = **99 游戏秒**)在一场 119.07 s 的战斗里被调用 **25 次**
  = 2 次 `Using`(同一瞬间,active=99.07 s)+ 23 次 `Charge`;她的计数器在 active=101.17 s 走满后归零。**真发动 1 次。**
- **折叠窗口 = 该技能自己的冷却**(`CoolTimeFrame/30`,下限 0.25 s),不再是 R66/R67 的固定 0.25 s。理由:一次发动
  会在执行窗口里留下一**串**调用,实测跨度 1.5 / 2.1 / 2.9 / 3.0 / 4.53 s;而"同一技能两次发动不可能快于它自己的冷却"
  是**物理约束**,不是调参。实测(候选 = `Using`,按各自冷却折叠):
  **9 场语料 174 个间隔 0 违规**(R66/R67 规则:342 间隔 / 184 违规);**411001 那场 47 个间隔 0 违规**(旧规则:81 / 35);
  两条语料里没有哪个折叠簇的跨度达到自己的冷却。`ct == CoolTimeFrame/30` 在 568 行里 0 处不一致。
- **`并N条M格` 的含义随之改变**:N = 被**该技能自己的冷却**并进前一格的调用条数,M = 收到它们的格子数。
  页面图例已改写(并拆成两行 —— 合成一行时是 **189 列**,超出 129 列的行宽,是页面上唯一放不下的一行)。
- **面板与行宽**:`TailW` 25 -> 33(尾部多了 `试N`),行宽 121 -> **129**,F4 页面面板 900 -> **960 px**
  (fontSize 14 下一列约 7.44 px)。
- **`rec` 通道与钩子已删除**(见上条);`Hooks/SkillRecordHooks.cs` 改名 `Hooks/SkillCommandHooks.cs`。
- **`Skill.ActivationType`/`ActivationTypeParam` 没有解释力**:实测每个单位每个技能都是 `act=0 actP=0`
  (与主表 `activationType=0` 一致)。R66 §2b 把它当作"什么触发它"的答案,该期望不成立,不要再引用。

##### R70(插件 1.7.21):技能时间表**只统计我方**(三路统一),一行的时刻放不下就**往下撑**

本轮**不动归属口径、不动任何伤害/归属数值、不改导出形状**(`contribution.schemaVersion` 仍 **1.2**)。

- **队伍口径(新写的、之前只是"文档里说过了")**:`team == 1` = 我方,判据抽成纯策略 `SkillSidePolicy.IsOurs`
  (`CharacterInfo.IsAlly/IsAllyTeam` 现在都调它;导出的 `actors[].team`、`events[].atkTeam/vicTeam` 用同一套编号,
  实测 `(1,2)` = 我方命中敌方、`(2,1)` = 反向)。**读不出来的 team 一律不算我方**(失败关闭)。
- **R66-R69 的时间表其实只在一路上做了这个判断** —— `skl`(奥义/特殊命令)有,`cmd`(自动技能命令后缀)与
  **充能采样器**都没有。实测(任务 9999,60.7 s,导出 `…151957…-001.json`):页面里有 **ムスクーマ** 与
  **ネフェスティス**,而这两个名字在本场 `actors[].team` **只有 team 2**;同场 `skl` 的 SUM 却报 `foreignSide=9`
  (敌方那 9 次调用被丢掉)。因为同名角色两边都有,`cmd` 的文字身份键会把敌方同名单位的发动**并进我方同一行** ——
  于是既"统计进来了"又"看不出敌我"。三路现在统一走 `SkillTimelineProbe.NoteForeign`:
  计数(`[SKILLTL] SUM foreign(cmd/skl/chg)=a/b/c`、`[AUTOSK] SUM foreignSide=/foreignUnits=`)并打证据行
  (`[SKILLTL] foreign … (not our side; not stored and not timed)`),页面的通道行追加 **`剔除非我方 N 条`**。
- **一行的发动时刻放不下就往下撑**:首行不变(身份 + 9 个时刻 + 尾部),**续行只印时刻**并与首行的时刻列对齐,
  每行 **14** 个,最多 **3** 个续行 ⇒ 一行最多印 51 个时刻;再多时 `+N` 落在最后一个续行的末格(含义不变)。
  引用这一页时:`n=` 是**发动次数**、`试N` 是**调用但没发动**的次数、续行属于**同一行**。

##### R71(插件 1.7.22):战斗钟的**原点**对齐到游戏自己的战斗开始(补回开局那 0.90 s)

本轮**不移动任何伤害/归属数值、不改导出形状**(`contribution.schemaVersion` 仍 **1.2**);移动的是**时间轴的原点**。

- **问题**:钟的**速率**一直是对的(`ClockSource=game`:游戏步进 ÷ 游戏自己的 units/s,实测与技能冷却同速),
  **原点**错了 —— 我们的 0 是「插件第一次看到这场战斗」,而游戏自己的初动/冷却/屏幕倒计时都从「游戏开始这场战斗」
  起算,中间约 **0.90 s** 没被计入 ⇒ 所有发布的时刻(页面戳、`active=`、导出 `events[].t`、`duration`、按秒桶)偏早 0.9 s。
- **三条独立测量都是 0.90 s**:①自动技能计数器 `lag = (FirstCoolTime - WaitCountFrame)/units - active`
  = 0.90–0.93 s(12 槽 × 3 场恒定;用 `CoolTime` 当初始值会散成 0.9/2.9/4.9/9.9 ⇒ 不通);②游戏自己的屏幕倒计时
  `GameTimeLimitCounter.NowTime` 满足 `now/30 + active = 常数`(每场 27–40 个采样波动 0.03–0.07 s ⇒ 同速),常数
  89.10/119.10 对 90 s / 120 s 的台面时限;③页面「最早发动」对照主表 `FirstCoolTime`(逐个差 0.9 s)。
- **新量到的游戏口径**:自动技能槽的计数器**开局初始化为 `FirstCoolTime`(初动)**,每次发动后重置为 `CoolTime`
  (冷却)—— 主表为什么给两个数,这就是答案。
- **口径(1.7.22 起)**:
  - `Session.ActiveSeconds` 的原点 = **游戏自己的战斗开始**;开局一次性前移实测的 lag(`[CLOCK] origin=+0.90s …`),
    边界写在 `BattleClockCalibrationPolicy`(lag<0.05 s 或 >5 s 或超前>0.5 s 不加;窗口 2 s 之外不加;
    **已经戳过事件就不加**;可用槽<2 不加),不满足时 `[CLOCK] origin=none reason=off|few|window|events|range`;
  - **1.7.22 之前的文件沿用旧原点**(绝对时刻早约 0.9 s),**相对量不受影响**;跨版本比较绝对时刻要记住这 0.9 s;
  - **按秒分桶(`perSec*`)的桶边界平移 0.9 s**,总量不变;
  - 自检:同一场 `[CLOCKP]` 的 `now/30 + active` 应**恰好等于台面时限**(90.0 / 120.0)。

##### R72(插件 1.7.23):让上面这条口径**真的生效**(R71 一次也没应用过)

R71 算对了 lag,却因为**顺序**而每场都拒绝:它只在「本场第 2 帧及以后」尝试对齐,而游戏在创建 session 的**同一帧**
就把开局伤害打了出去 ⇒ 两次实测都是 `reason=events`(quest 9999:`hits=5`,距 session 开始 0.121 s;quest 411001:
`hits=1`,0.029 s),偏移一次也没落地。R72 不改任何算术,改的是**何时决定**与**事件何时被聚合**:

- **在创建 session 的当场尝试对齐**(那时本场事件数为 0、`ActiveSeconds` 为 0),每帧的尝试保留作兜底;
- **原点未定案前,开局的伤害/治疗先扣住**(有界 256、计数、溢出不静默),定案后用**它们到达时的时钟 + 决定的 lag**
  **重新走一遍记录器**:`events[].t`、actors 首/末命中、`perSec*` 桶、伤害曲线、pending 命中配对、反应窗口
  **一次写对**;不是「先按旧轴写再回头改」(那样每个派生面都得改,漏一个就是一场两条轴);
- 不走记录器的时间戳按同一个常数搬一次(页面 `SkillTimelineEvent.Active`、充能采样器的 `LastActive`、
  三个诊断导出的行、`AttackSnapshot.At`);`SampleHp` 在定案前不采样(按秒索引,晚一两帧不丢数据);
- **拒绝理由多一个 `hold`**:扣住上限(256)被击穿时,事件已经落在旧轴上,本次偏移**拒绝**(一场两条轴比原点晚 0.9 s 更糟);
- `[CLOCK]` 行**每条分支都带同样的字段**:`origin=+0.90s|none reason=… samples=N active=… hits=N held=N`;
- **口径不变**:1.7.23 与 1.7.22 的 `ActiveSeconds` 是同一条轴(游戏自己的战斗开始);变的只是**它现在真的被应用**。
  引用 1.7.23 之前的文件时,先看那一场的 `[CLOCK]` 行:`origin=none reason=events` 表示**那场仍是旧原点**。

##### R77(插件 1.7.27):`[ABSPROBE] sum` 的**读数自相矛盾** —— `ovz=` 的次数与金额本来就不是同一个族群

R76 首验(2026-10-07,quest 411001,120 s / Lose)打出这样一行:

```
[ABSPROBE] sum calls=5489 ovz=0/6500000/2358285 pool=13 partial=0 …
```

「0 次超量」配「650 万落地」不可能同时为真。原因:R76 的 `ovz=<次数>/<落地>/<溢出>` 里,**金额**对所有 `o.IsOversized()` 累加(四个族群都算),**次数**只对 `case AbsorbVerdict.Oversized`(生命佐证成立的整击落地)计数 ⇒ 次数与金额分属两个族群。本场的真相是 `ovz` 族群 0 条、`pool` 族群 13 条(固定池 `ショゴス`),那三个金额是这 13 条的。

* 现在 `ovz=<次数>/<落地>/<溢出>` 的**次数与金额同族群**(生命佐证成立的那一族);
* 新增 `ovzAll=<次数>/<落地>/<溢出>` = **全部** `Oversized*` 族群,用**独立计数器**累加,因此恒等于 `ovz + ovzPool + ovzPartial + ovzUnread`(恒等式由用例守着);
* 顶层 `pool=` / `partial=` 改名 `ovzPool=` / `ovzPartial=`,与 `carrier(...pool...)`(载体读数)不再同名;
* 新增 `none=`(全部落空判定的次数)与 `full=`(**只**认量测到 `lifeDrop == nominal` 的整击落地 —— `None` 有四条到达路径,只有那一条是量测);
* 于是 **14 个判定计数之和恒等于 `calls`**(新增用例),过去 `None` 只能靠减法推(本场 5489 − 5470 = 19)。
* **引用注意**:变的只有**日志行的形状与可读性**;导出字段、`schemaVersion`(1.2)、判定层、记账口径**一字未动**。按 1.7.26 及更早的行抄过 `ovz=` 第一个数字的,请记住它是**生命佐证成立**那一族的次数,而它后面两个金额是**全族群**的;`full=` 的实机值本轮**尚未取到**(那 19 条 `None` 没有一条进过日志),需重跑一场。

##### R76(插件 1.7.26):`被吸收/无效化` **不是吸收** —— 它是目标的剩余 Life(`入耐久` 与它互换)

R75b 的两场实机(quest 9999 + quest 411001)定出了一条定律,复核 800 条探针读数里 **798 条可读的全部成立、0 违例**:

```
res == max(0, nominal - lifeBefore)          # res = BattleObject.Damage 的返回值(__result)
```

也就是说 `__result` 是**溢出量**(超出目标当前 `Life` 的那部分),`Life` 实际掉的是 `min(nominal, Life)`。由此:

* 短字段口径:`入耐久`(导出 `events[].amount` / `calc.applied`)= `res` = **溢出量**;
  `被吸收/无效化`(`totals.absorbed`、`reconcile.absorbed(Amount)`、`events[].calc.absorbed`、`actors[].absorbed`、
  `[ABSORB]` 行)= `nominal − res` = **真正落地的量**(等于命中发生时目标的剩余 `Life`)。
  ⇒ **在"超量命中"(`res > 0`)上这两列是互换的**;`res == 0` 的命中(绝大多数)两列本来就对。
* 老板(`ショゴス`)侧:`Life` 恒为 **500,000** 且 `inv=1`(无敌族标志至少一个为真),因此每次超过 50 万的命中都得到
  「被吸收 500000」—— 那是**那个池子的值**,不是吸收能力;boss 天赋只有 `1002 ModeChange` + `6 攻击力/150/-1`。
* 量化:本轮 21 条超量命中(玩家 10 + boss 11);历史 48 份导出 408 条(反推 `lifeBefore` = 500000×406 + 2821 + 19010)。
  单次最大差 269,104。
* **引用注意**:导出字段本身**一个都没改**(schemaVersion 仍 1.2),R76 只加了**运行日志**里的 `[ABSPROBE]` 判定
  (`diff=` / `landed=` / `overflow=` / `key=`)与 `[ABSPROBE] sum` 的桶。修订已发布数值**不在 R76 范围内**。

##### R75(插件 1.7.25):「被吸收/无效化」的**载体**仍未命名 —— 本轮把它变成**可测**

`被吸收/无效化` 自 1.5.5 起就是 `nominal − damage`(游戏口径 − 入耐久)的**别名**,不是游戏文案:产地是
`src/Aggregator.Stats.cs` 的 `int absorbed = nominal - damage;` 与 `[ABSORB]` 行、战末 `>>> 被吸收/无效化`;
显示层在 `src/Composition/CompositionProbe.Chain.cs`;`totals.absorbed`、`dealtWithAbsorbed`、`reconcile.absorbed/absorbedAmount`、
`events[].nominal`、`events[].calc.absorbed`、`actors[].absorbed` 全部由它派生。**同名的第二样东西(无关)**:
`src/Composition/CompositionProbe.Text.cs` 里 `case 11: return "吸收";` 是游戏 `DamageSource` 枚举第 11 项的中文直译,
而实测那一场 5,501 条事件里 `source=11` **一次都没出现**。

**本轮结论(实测)**:46 份 `battle_411001_2026*.json` 共 **397 条**被吸收记录,**395 条差额恰好 500,000**,
受害方**全是 `ショゴス`**,攻击者跨四个角色(エヴァラス・フラウ / ネーフェ＝ジアー / メアリー / [賢導]トレイラ)
⇒ 恒定值属**受害方侧**;另 2 条是**友方受击**的不规则值(2,821 / 19,010),形状与 boss 侧不同。载体**仍然找不到**:
`masterdata/*.json` 里没有任何字段/行/值等于 500,000,boss 的运行时天赋只有 `1002 ModeChange` + `6 攻击力/150/-1`,
`timeline` 488 行对受害的三个战斗对象(key 58/106/178)**零行** ⇒ **待验证**,不再用「护盾」命名。

新增 `[ABSPROBE]` 诊断行(**只进运行日志,不进导出**):`calls=` / `withheld=` / `sum=` / `masked=` /
`lifeMismatch=` / `verdict(barrier/pool/unknown/takeover/fixed/invincible/unreadable)=` /
`seen(barrierDmg/addBarrier/takeover/fixed)=` / `active=` / `unreadable=` / `first(nom/res/life/bar)=` / `rows=` / `dropped=`。

**引用注意**:`totals.absorbed` 等字段的口径**未变**;`masked=` 揭示的是现有口径的**盲区** ——
`src/Hooks/BattleObjectHooks.cs` 的 `(__result > 0) ? __result : __0` 会把**整击被吸收**(`__result <= 0`)记成**满额伤害**,
所以现有 taken 合计对这类命中**可能偏大**;本轮**只测不改**(改它会移动每一个已发布的承伤合计)。

##### R74(插件 1.7.24):校准仪的**读数单位** —— 1.7.22/1.7.23 的原点修正**一次都没生效**

R71 算对了 0.90 s、R72 把「何时决定」也改对了,但**测量仪一次也没返回过数据**:

- 校准读的是 `Skill.FirstCoolTime`,它是 `Skill.CoolTime` 的**同级属性 = 秒**(插件自己用
  `Skill.CoolTimeFrame / Skill.CoolTime` = 240/8 = 30 得到「每游戏秒 30 单位」),却被当成**单位**去和
  `WaitCountFrame`(=150 之类)比较 ⇒ `waitFrames > firstCoolFrames` 把**每个槽、每一帧**都拒掉 ⇒
  `samples` 恒 0 ⇒ 原点永不 decided ⇒ `[CLOCK] origin=none` 是每一场的**必然**。
- **口径后果(引用文件时必须先看这一行)**:凡 `[CLOCK] origin=none` 的场次(实测 1.7.22 与 1.7.23 **每一场**都是),
  其绝对时刻**仍旧是旧原点**(比游戏自己的初动/冷却/屏幕倒计时早约 0.9 s);相对量(份额、DPS、间隔)不受影响。
- R74 改读**单位制**的 `Skill.m_data.m_firstCoolTimeFrame`(字段在**主数据**类上;`Skill` 本身没有这个名字 ——
  R73 的「挂在 `Skill` 上」被编译器以 CS1061 纠正),秒值 `FirstCoolTime × unitsPerGameSecond` 只作**记录在案的退路**
  (`FirstCoolFrames(..., out usedFallback)`),因为「用哪条路」必须能被分辨。
- 新增诊断(**不是导出契约**,只是可诊断性):
  - `[CLOCK] calib attempts=… party=… slots=… usable=… via(field/fallback)=…/… first(sec/frame/wait)=…/…/…
    rejected(noFirst/wait/units/window/range)=…/…/…/…/…`
    —— 决定帧**必然**过不了窗口条款,所以旧的 `samples=0` 在 `window` 路径上天然为 0,「没槽能回答」与「窗口先关」
    长得一模一样(这正是 R71/R72 两轮说不清原因的地方);
  - `[AUTOSK] chg` 行新增 `first=`(秒属性)与 `firstFrame=`(单位字段);**读不到打印 `?...` 而不是 0**
    (「读不到」和「读到 0」必须是两个答案)。
- **证据包新增 `skill_timeline.txt`**:技能时间线(与 F4 页面、战末 `[SKILLTL]` 行**同一批字符串**),在上一步列入
  `manifest.json` 并校验 ⇒ **旧包没有它、新包有**;探针关掉时不生成该文件(manifest 的 `notes` 说明这一点),所以
  「文件缺席」不能被读成「没有发动」。导出契约(`contribution.schemaVersion`)与 `battle.json` 的形状**都没变**。
- **仍未定案**:`m_firstCoolTimeFrame` 是初始/目标值还是实时剩余值(本轮不写死 —— 实时剩余会让算出的 lag ≈ −active
  被既有边界拒),以及实机确认(需要一场新战斗的 `[CLOCK] origin=+0.9Xs` + `calib … usable=N via(field/fallback)=N/0`)。

