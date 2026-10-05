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
