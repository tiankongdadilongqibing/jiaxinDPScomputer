# REFACTOR-BATCH-R62 —— 命中记录错配 / 残差口径 / 全局因子栏位(A、B、C 同步)

日期:2026-10-05。**本轮不动归属契约**(`contribution.schemaVersion` 仍为 **1.2**),只修三处会让人读错数的地方。
起因:另一个智能体拿插件导出做分析时提出四条疑问,我逐条回数据核实(见 §1);四条都成立或部分成立,其中三条可改。
用户指令:「ABC 同步进行推进」,并已关闭游戏。

## 0. 一句话

插件 **1.7.13 -> 1.7.14**;`C9D1B0CC9FAF124D5ACF4194B49185C13DC73AFF275E15152F59463AE32FCC5D`(435,712 B)。

- **A**:命中记录(`HitRecord`)与合成(composition)的命中属性互相矛盾时,**拒绝那条记录入账**(`hitMatch=3`),
  并自报 `hitDetail.matchRejected`。修的是「导出里 `source`/`calcHitType`/`calcEffectId` 会把上一击的记录写在下一击上」。
- **B**:离线残差改用**游戏口径** `(applied + absorbed) / theory`(`contrib.model.game_residual`)。
- **C**:战域规则表里 `EnemyTakes`(敌方受伤)的因子从 `dealtMult` **移入 `takenMult`**;乘积不变。

## 1. 四条疑问的核实结果(逐条回数据)

| # | 说法 | 结论 | 实测范围 |
|---|---|---|---|
| 1 | 009/010 两条命中记录配错 | **成立**(但错的是 `HitRecord` 通道,不是 `calc` 块) | 全语料 `calcHitType != calc.hitType` **432/204,168(0.22%)**;贯通命中的 `source` 留 0 的:9999 **3,240/11,096(29%)**、411001 **6/39,467(0.015%)** —— **拆开看最重要**:1.7.13 的 5,616 条里 `source=1` 正确 3,818、**无可用记录 1,771**、**记录属于别的命中仅 27 条**;1.7.15 实机 5,746 条里 **52 条被拒绝(hitMatch=3)、0 条错标留存** |
| 2 | 「72 条全部配对未获结算对象佐证」 | 标签存在,**数字读反了** | 009/010 是 **972 + 1,267 = 2,239** 条;全语料 `pairCorroborated=false` **201,172/204,089(98.6%)** —— `72 = 37+35` 是 `reconcile.byPair.corroborated`(被佐证数) |
| 3 | 有吸收时 `residual` 误导 | **成立** | `absorbed>0` **801 条**,其中 **688 条** `residual<1` |
| 4 | 海魔の残滓 被算进 `dealtMult` | **成立** | 带 `side=vic,kind=global` fold 的事件 **154,781 条,全部**算在 `dealtMult` 一侧 |

第 2 条不需要改代码:文档早已写明它是「佐证可得性」而非配对正确性判据(`PAIRTRUSTED-IMPACT-REPORT.md` §4.2),
每行真正的算式判据是 `calc.valueMatches`(=`theory == applied + absorbed`;全语料 **158,324 真 / 45,765 假**)。

## 2. A —— 命中记录错配:可证伪即拒绝

**证据(实测)**:`B-...D7F-009` `factId=127`(t=22.47,4,220)与 `-010` `factId=173`(t=25.90,3,798)。
两条的 `calc` 块都自洽(`hitType=3` 贯通、`ratio=0.10`、`power = 攻击力x0.10`;009 那条 `theory == applied == 4220`、`residual=1.000`),
而事件层的 `hitValue` 等于**同秒前一条命中的结算值**(34,478 / 7,271),`calcEffectId` 还带着主伤害的技能 id(10114)。

**成因**:`Aggregator.NoteHitDetail` 会给一次结算产**多条**记录(`DamageAction` / `ActDamageAction` + 被吸收的记账调用),
`BattleSession.ConsumePending` 在 0.35 s 窗口内按「攻击者+目标」配对、值不等时取**第一条**(`hitMatch=2`),
于是下一条同目标命中会吃掉上一条留下的记录。

**改法**(3 处):

1. `Policy/AttributionPolicy.cs` 新增纯判据 `RecordContradictsComposition(recordHitType, compHitType, compValid, compTrusted)`
   与常量 `HitMatchRejected = 3`;
2. `Aggregator.Stats.cs`:`TryGetCompForVictim` **提前到消费记录之前**(两者互不干扰:一个只读 `_active`/`_calcEvents`,一个只动 `PendingHits`),
   若记录被证伪则 `hitRecord = null` + `hitHow = 3` —— 于是 `source`/`calcHitType`/`calcEffectId`/`hitValue`/`crit`/`skills[]`
   **全部退回未知**,而不是抄上一条命中的值;
3. `Runtime/BattleRuntimeCounters.cs` + `Output/ExportService.cs`:`HitMatchRejected` 计数器,导出为 `hitDetail.matchRejected`。

**为什么不是「按值挑更近的记录」**:值不可比 —— 记录的 damage 是计算器自己的数(可能等于计算威力),与入耐久的 `amount` 天然不同
(这条差异有文档:`BattleEvent.HitValue`)。只有**命中属性**两侧读的是同一个游戏成员(`DamageCalculater.m_hitType`),矛盾才是证据。

## 3. B —— 残差改游戏口径

`calc.residual` 的定义就是 `applied / theory`(`Model/CalcBreakdown.cs`),被吸收的部分不在分子里;
`calc.valueMatches` 用的才是游戏口径 `GameValue = applied + absorbed`。插件**内部**的会心推断一直用 `GameValue`
(`CalcReconcile.IsCritExplained`),被误导的是**消费方**:离线核心 `contrib/aggregate.py` 拿 `residual` 分了桶。

改法:`contrib/model.py` 新增 `game_residual(calc)`;`aggregate.py` 用它分桶并计 `residual_absorbed_hits`;
`report_text.py` / `report_json.py` 标签与字段同步;`rule115_census.py`(1.15 阶梯普查)也改用同口径;
`HitResult.residual_mult` 保留导出原值,新增 `residual_game`。

## 4. C —— 全局因子栏位

`CompositionProbe.GlobalRules.ApplyGlobalDebuffs` 把两类规则(`EnemyTakes` = 敌方受伤 / 其补集 = 我方攻击)
乘进**同一个**乘积,`CompositionProbe.Chain.cs` 又把整个乘积乘进 `atkMod`。
改法:新增 `ApplyGlobalDebuffsSplit(... out double vicFactor, out string atkText, out string vicText ...)`,
`Chain.cs` 按 side 分别乘进 `atkMod`/`vicMod`;旧签名保留(诊断路径的行为不变)。

**离线先验证再改**:把语料里每条命中按新栏位重算(把 `vic/global` 因子从 dealt 搬到 taken),
**197,533/197,533** 条带 fold 的命中都满足 `dealtMult == prod(atk fold)` 与 `takenMult == prod(vic fold)`;
这条恒等式现在由 `check_export_schema.py` 对 1.7.14+ 的文件强校验(3 例自测)。

## 4b. 顺带修掉的两处**外部噪声**(否则验收无法归零)

本轮验收第一次跑出 `74 ok / 2 mismatched`,两条都不是本轮代码引起,但都必须按规则处理:

1. **`tool_registry` 红**:并行的另一个分析会话在 `_dpsm_work/tests/` 留下了 35 个 `_as*.tmp.py`(以及更早的 `_traira*.tmp.py` / `_pen*.tmp.py` 等),而 `tool_census.py` 的扫描只按目录白名单跳过、**没有实现仓库自己声明的「临时文件放 `_dpsm_work/tests/_*.tmp` 且被忽略」这条规则**,于是「扫到但未登记」的红。改法:`tool_census.SKIP_NAMES/SKIP_SUFFIXES` 按文件名跳过声明过的临时文件(`_*.tmp` / `_*.tmp.py`)。这是**让扫描器遵守既有规则**,不是放宽:被跳过的名字只可能是临时文件,真正的守卫脚本仍必须登记。
2. **`live_log` 红**:`LogOutput.log` 在 23:25 被一次新的启动重写,里面有一段**还在进行中**的训练场战斗;F5 贡献面板逐秒重绘,那 14 帧的 `未归因` 行是**被打了一半的实时值**(2,189 / 101 / 285 / 508 / 2,747 / 3,598,出现在 3..43 秒),而同一场的导出在结束时是 0.0 —— 拿实时部分去对**已结算**导出,是这一类检查自己早就写明的「范畴错误」(`未归属` 分支已有同样的豁免)。改法:`check_live_log.py` 新增 `_battle_finished_by(frame, parsed)`,要求帧头显示的秒数等于**此前最后一次**同任务 Battle end 的时长(且不等于下一次 Battle end 的时长)才做比对;否则计入 `live_in_domain` 并按「不可验证」告警。自测新增 `live_in_domain_partial_is_unverifiable_not_wrong`;既有的 `in_domain_row_beyond_rounding_is_an_error` 仍是**对照**(真的不一致照样 ERROR),所以豁免没有变成放水。


## 4c. 语料从 82 长到 103 之后,又红了 32 条 —— **两条是检查器自己的假阳性**

提交前把 `check_export_schema.py` 拿到**整份活语料**上跑了一遍(验收只查冻结的 35 份快照,活语料不在它的射程里),
103 份里报 32 条问题。逐条核完:**32 条全是检查器的错**,数据没错。

| 规则 | 命中 | 真因 |
|---|---|---|
| `actors[].friendlyHits > hits` | 26 条(1.7.13×17 / 1.7.15×9) | R61 把 1.2 的 `hits` 改成**只在池内(对敌)命中**,而 `friendlyHits` 是被排除的同队命中 —— 两个**不相交**的桶,没有大小关系。实测 26 条真实行违反它(例:同队 24 击 vs 对敌 10 击,全在训练场) |
| `base+self+received == direct` | 6 条(1.7.11) | 这条只在 1.2 成立(337/337 行);1.7.11 的 **271 行里 49 行**违反它(最大 4%)。原因:该行是被队友规则吃掉的那部分**不在本行**(落在那个队友的 `assistCredit` 里),所以它只是在当年那 17 份样本上恰好闭合 |

**修法**(都不是放宽,是把规则放到它真正成立的契约上):

- `friendlyHits > hits` 规则**门控在 `not is_120`**(<=1.1 时 `hits` 计的是全部命中,那是它当年被测量的对象);
- `base+self+received == direct` 规则**门控在 `is_120`**;对所有版本真正成立的那条(`base+self+assist == total`)**原样保留**,同一循环里就在上面,实测 809/809 行成立;
- 顺手补上 1.2 的**两条聚合恒等式**(实测 38 份 1.2 段、0 失配):`Σ actors[].hits == damageLedger.analyzableHits`、`Σ actors[].directDamage == analyzableDealt` —— 比原来那条假规则**更强**;
  注意 `Σ friendlyHits` **不能**对 `ledger.selfTeamHits`:台账还计攻击者不在分析池里的同队命中(实测一场 61 vs 88),所以那不是恒等式;
- 检查器自己的 1.2 样例夹具原本 `hits=5` 而台账 `analyzableHits=4`,被新恒等式当场抓住,已改齐;
- 自测 **72 → 77 例**(新增 5 例:1.2 允许同队命中多于对敌命中、1.1 仍拒绝、两条聚合恒等式反例、1.1 允许旧式 `direct` 不闭合),并把原「base+self+received」反例搬到 1.2 夹具上,否则它再也打不到。

复跑:**活语料 103 份 / problems=0**。这同时是对本轮 A/C 的一次真实数据验证 —— 语料里有 **21 份 1.7.15 实机导出**,
它们既没触发 `hitDetail.matchRejected` 缺失,也没触发 fold 栏位恒等式。
## 4d. A 的端到端验证(21 份 1.7.15 实机导出)

语料里恰好有 **21 份 1.7.15 实机导出**(2026-10-06 01:23–01:42,任务 9999),于是 A 不必等下一场:

| 口径(10% 贯通命中 = `calc.hitType==3 && calc.ratio==0.1`) | 1.7.13(无 A) | 1.7.15(有 A) |
|---|---|---|
| 总条数 | 5,616 | 5,746 |
| `source=1`(正确) | 3,818 | 4,126 |
| **没有可用记录**(`hitMatch` 缺席,不可修) | 1,771 | 1,568 |
| **记录属于别的命中**(可修) | **27(错标留存)** | **52(全部被拒绝 `hitMatch=3`)** |

结论:A 的效果不是「把 29% 的数字变小」——那 29% 里九成以上本来就没有记录可配;A 消灭的是**「匹配到、但记录属于别的命中」**那一小类:
1.7.15 的 5,746 条里 **0 条错标留存**,52 条被显式拒绝并计数(`hitDetail.matchRejected` = 144,分布于 21 份导出)。
**这一条同时纠正了 R62 最初的说法**:当时把「`source` 为 0」整体当成「记错」,是**高估**(见 §1 的修正与字典 §A 的拆开说明)。

## 5. 验证与部署

| 项 | 结果 |
|---|---|
| 构建 | 0 警告 0 错误 |
| 行为套件 | **987 用例 / 101 组 / 0 失败**(R62 的 8 例 + R63 的 16 例;用例数钉住) |
| 变异负控 | 新增 5 条 `pair-reject-*`,逐条红在具名用例;合并树全量 **144 例 / 0 失败** |
| schema 自测 | **77 例 / 0 失败**(R62 的 6 例 + 本轮修两条假阳性新增的 5 例) |
| 活语料 | 103 份 / problems=0(修前 32) |
| schema 自测 | **72 例 / 0 失败**(新增 matchRejected 3 例 + fold 栏位 3 例) |
| 离线核心 | test_samples(S1-S14)/ test_gate 64-0 / golden 1.5.5 / crosscheck --selftest 全过 |
| 文档收敛 | 12/12 |

**部署**:`32BFEC3B`(435,200 B,1.7.13)-> **`C9D1B0CC`**(435,712 B,1.7.14);
旧 DLL 备份 `_dpsm_work/deploy-backup/pre-r62-32BFEC3B/DpsMeter.dll`;
两个 crash `.bak` 复核未变(`2E1819F2…`、`F7FF1EB8…`);`repo_manifest.py --write --exports batch_inputs/rf0` 已刷新。
**回退**:一条 `Copy-Item` 把 `pre-r62-32BFEC3B/DpsMeter.dll` 覆盖回 `BepInEx/plugins/DpsMeter/DpsMeter.dll`。

## 6. 未覆盖 / 下一场要看什么

- A 的拒绝分支在插件侧 `Aggregator.Stats.RecordDamage`,BehaviorTests 编译不到那层;判据由上表的纯函数用例与 5 条变异负控钉住,
  **端到端**要等下一场 1.7.14 实机导出:看 `hitDetail.matchRejected` 是否非零、`sources[0]` 里的贯通量是否随之下降。
- C 只影响新文件;`<=1.7.13` 的旧导出仍是旧栏位,按各自契约读(`check_export_schema.py` 的该检查版本门控在 1.7.14)。
- 第 2 条的「72」是读表方向错;若那位分析者仍想用 `pairCorroborated` 当门,`PAIRTRUSTED-IMPACT-REPORT.md` §4.2 已给出为什么不能。