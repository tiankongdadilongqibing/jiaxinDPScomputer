# DpsMeter 架构审视 —— 面向「更深一层的战斗数据分析」

> 生成日期:2026-10-03
> 审视对象:当时部署中的 1.4.1(`BepInEx\plugins\DpsMeter\DpsMeter.dll`,SHA256 `93E691D8…C379B4`);本文是 1.4.1 的时点审视,当前部署 1.7.9
> 审视方式:三个只读审计(构成/输出/事件管线)+ 本文作者对全部关键断言的就地复核
> **本文只做诊断与提案,不改任何源码。** 所有"实测"数字都可用文末命令复现。

---

## 0. 结论摘要

**当前插件不是"分析工具",而是一台"一次性取证打印机"。**

它能打出一份信息量很大的 JSON,但:

1. **三个导出字段是死的**(`crit` 570,078/570,078 全 false;伤害的 `source` 510,735/510,735 全 0);
2. **折叠只留结果不留过程** —— 84 种不同的攻方因子被乘进**两个 double**,来源只活在中文句子里;
3. **去重按数值、不按来源** —— "哪条规则折了哪一份"在数据结构层面**不可判定**;
4. **游戏事实只在 hook 那一瞬存在**(导出后 `ActorStats.Source = null`),而全场 5501 击里只有 **160 份**取证样本保留了这个瞬间;
5. **45.8% 的导出体积是中文句子**,想读一个数就得写正则(现已有 21 个脚本在复制同一条正则);
6. **加一个已测量但未导出的计数器要动 8~12 处**,而且必须**再打一场**才能拿到值。

第 6 条就是你上次说的「你已经让我打了很多场了」的**机制成因**。它不是态度问题,是架构问题:
事实的采集点(命中瞬间的活体读取)与事实的消费点(离线脚本)之间**没有数据通路**,只隔着一次"重新打一场"。

**三条主线:**

| 主线 | 要解决的问题 | 对应批次 |
|---|---|---|
| **可答** | 残差 → 规则的归因,今天在数据结构上不成立 | A(1.5.0) |
| **一次打完** | 每击都带完整事实,不再靠"再打一场"补探针 | B(1.5.0/1.6.0) |
| **离线可重放** | 改一条规则 → 用 768 份历史导出回归,不碰游戏 | C(1.6.0) |

---

## 1. 现状:一次命中的数据流

```
游戏 DamageCalculater.Action()
  └─ CalcActionHook.Prefix        → NoteActiveCalc(_activeCalc, SnapshotStatuses)   ← 活体读
       ↓ 计算体
  └─ 四个 postfix 之一            → NoteCalcActivity(...) 压入 _calcEvents(环形,上限 64)
       ↓
BattleObject.Damage()  ─ Prefix → StatusDeltaProbe.NoteBefore
                       └ Postfix → Aggregator.RecordDamage(victim, attacker, owner, dmg, nominal)
                                     ├ GetActor            读 DisplayName/TeamType   ← 活体读
                                     ├ ConsumePending      ← ★ 恒 null(见 §2.1)
                                     ├ BuildChainParts     写 comp1..comp4 文本 + CalcBreakdown ← 活体读
                                     ├ StatusDeltaProbe.Bind
                                     ├ TalentRuntime.NoteAttack
                                     ├ Forensics.Observe   ← 只对"未解释"的击、每类最多 2 份
                                     └ Session.AddEvent(ev)   ← 超 100,000 击静默丢弃
       ↓ 战斗结束
FinalizeLocked → ExportService.Export
                   ├ CalcReconcile.Compute(s)   第 1 遍全扫
                   ├ BuildJson(s, rec)          第 2 遍全扫,拼成一个 13 MB 字符串
                   ├ File.WriteAllText          单次 13 MB 写盘
                   └ ActorStats.Source = null   ★ 活体引用在此彻底消失
```

**结构上是一个漏斗,但漏下去的是事实本身**:活体对象只在 hook 内有效 → 事实必须在那一瞬复制 →
而只有"未解释"的击会被完整复制 → 其余只留下中文句子。

---

## 2. 实测证据(本文作者就地复核)

### 2.1 ★ 伤害事件的 `source` / `crit` / 技能归属通道从未接线

`Aggregator.cs:1191` 的 `RecordHitDetail` 是全仓**唯一**的定义,**没有任何调用者**
(`_dpsm_work/src/` 全量 grep 只命中定义行;`backup/`、`refactor_verify/` 的反编译副本同样没有调用点)。
于是 `Session.PendingHits` 恒空、`ConsumePending` 恒 null,而:

```csharp
// Aggregator.cs:1105
Source   = hitRecord != null ? (int)hitRecord.Source : 0,
Crit     = hitRecord != null && hitRecord.Crit,
HealCalc = hitRecord != null && hitRecord.HitType == eDamageCalcType.Heal,
```

**全语料实测(768 份 / 570,078 条事件):**

| 类型 | source | 条数 |
|---|---|---|
| `dmg` | `0` (Unknown) | 510,735 |
| `heal` | `8` (DirectHeal) | 59,343 |

| 字段 | 值 | 覆盖率 |
|---|---|---|
| 事件 `crit` | `false` | **570,078 / 570,078 = 100%** |
| `actors[].crit` | `0` | 全部 201 个 actor 全 0 |
| `actors[].sources` | `{}` | 全部为空 |

**必须更正的细节(子审计的说法不精确)**:治疗事件的 `Source = (int)DamageSource.DirectHeal`
是**在 `RecordHeal` 里显式写死的**(`Aggregator.cs:1182`),不经 `HitRecord` 通道。
所以"字段不可写"是错的 —— **字段可写,是伤害路径没接线**。
这个区别很重要:它说明修复只需在既有 postfix 里补一次调用,不需要新造通道。

**影响**:逐条残留归因的两个基本自变量(这一击是 DirectAttack 还是 DotDamage?有没有会心?)
在数据里**不存在**。而 `CalcReconcile.IsCritExplained` 只能靠残差容差反推会心,
于是"推断容差"变成了 KPI 定义的一部分(1.3.10 那次 305→746 的修正就是容差问题)。

**顺带确认的隐患**:`_activeCalc` / `_activeCalcT`(`Aggregator.cs:106-107`)在
**`StartSession` 里没有清零**(只在 `TryResumeClosedSession:473-474` 清),
而技能归属的回退分支(`:1066-1083`)**没有** composition 路径那样的 age 守卫(`:193-203`)。
→ 新战斗最初几击的技能 id 可能被记到**上一场**的 calc 上,静默。

### 2.2 ★ 折叠只留结果、不留过程

`CompositionProbe.Chain.cs:117-201` 是一串命令式语句,每步都只做两件事:
往一个 running double 里乘,和往一个文本缓冲里追加。

```csharp
double atkMod = 1.0, vicMod = 1.0;          // :117
if (t != 1.0) { atkMod *= t; atkModText = Join(atkModText, tTxt); }   // :128
if (g != 1.0) { atkMod *= g; atkModText = Join(atkModText, gTxt); }   // :147
if (fold && gv != 1.0) { vicMod *= gv; vicModText = Join(vicModText, gvTxt); } // :171
double known = attrMult * atkMod * vicMod;  // :201
```

最终落到 `CalcBreakdown.DealtMult` / `TakenMult` —— **两个标量,没有列表,没有 id**
(`Model/CalcBreakdown.cs:63,66`),导出为 `calc.dealtMult` / `calc.takenMult`
(`Output/CalcReconcile.cs:308-310`)。

**实测:同一场战斗的攻方因子文本里有 84 种不同的因子标签,守方只有 1 种;
而每击折叠的因子个数分布是**

```
0 个:  19 击
1 个:  46 击
2 个: 1111 击
3 个: 2660 击   ← 最常见
4 个: 1558 击
5 个: 107 击
```

**2~5 个不同的因子被乘成一个数。** 这正是「哪条规则折了哪一份」在事后不可判定的**代码形态**:
当你看到残留 `1.15^n` 时,无法判定这条 1.15 是**少折了**还是**多折了**,只能回到中文串做正则试探 ——
而 1.3.5→1.3.8 的三次精确率崩塌(32.5%→1.5%,再 41.9%→68.2%)全是这个病。

### 2.3 ★ 去重按数值、不按来源,且存的计数从不被读

`CompositionProbe.GlobalRules.cs:120-145`(逐字):

```csharp
private static readonly System.Collections.Generic.Dictionary<double, int> _enemyCandidates
    = new System.Collections.Generic.Dictionary<double, int>();
private static double Q(double f) { return System.Math.Round(f, 4); }

internal static bool IsAlreadyModeledEnemyFactor(double f)
{
    foreach (var kv in _enemyCandidates)
    { double d = kv.Key - f; if (d < 0) d = -d; if (d <= 1e-3) return true; }
    return false;
}
```

全仓使用点已逐条核对:`_enemyCandidates` 只在 `GlobalRules.cs:282` / `Talents.cs:343,357` 被 `Clear()`,
在 `:360` / `Talents.cs:344` 被写入,**读取只有两处**:
`IsAlreadyModeledEnemyFactor`(只判 key 存在)与 `LastEnemyFactorCount`(只回 `.Count`)。
**存进去的那个 `int` 计数从来没有被读过。**

**影响**:任何两条"数值相同、来源不同"的 敌方受伤 规则,在数据里**不可区分**。
自测用例把"全有全无"直接写成了规格(`Talents.cs:331-336`:责任集里 1 条 1.15 会抵消授予里 5 条 1.15)。
→ 这个设计**无法区分**「少折了一条本不该抵消的 1.15」与「文本路径完全没见过的一条规则」,
而这两种可能对应完全不同的修法。**只能靠反复打战场来猜。**

**这是本文认为最该先动的一处。**

### 2.4 事实只存在一瞬间,而全场的复制率是 160/5501

`Aggregator.cs:642` 在导出后把 `ActorStats.Source = null`(`ActorStats.cs:62-67` 注明是刻意的)。
→ **任何"导出时再回游戏读一下"的想法都不成立**,1.4.0 的 `Forensics` 正是因此被迫在命中路径上采样。

`Diagnostics/Forensics.cs` 的做法本身是对的:按键 `attacker|residual(F3)` 分桶、每桶留 2 份、
上限 80 桶,把活体状态(`UnitStateProbe.Render`)、双方天赋、buff 表全部抄下来。
**实测这场 5501 击的导出里,`forensics.records` 只覆盖 160 条。**

但**同一套键换成更完整的 12 元组**(attacker, victim, owner, effectId, hitType, knownMult, critRate,
block 三态, vicGive, vicExtra, comp3, comp4),**同一场战斗只有 493 个不同取值**;
在另外两场大战里是 463 / 331。**小战斗更少(33 / 43)。**

→ **"每击都带上完整事实"不需要更多体积,只需要去重。** 这是 B 批的全部依据。

### 2.5 导出是 prose-first、无 schema、13 MB/场、语料 433 MB

最新一场 `battle_411001_20261003_150140.json` 实测:

| 项目 | 字节 | 占整文件 |
|---|---|---|
| 文件总大小 | 13,231,042 | 100% |
| `events` 段 | 12,724,769 | **96.2%** |
| ├ `comp`+`comp2`+`comp3`+`comp4` 中文句子 | 6,062,813 | **45.8%** |
| └ `calc{}` 结构体(25 个键) | 2,436,554 | 18.4% |
| `actors` | 260,286 | 2.0% |
| `forensics` | 194,105 | 1.5% |
| `rosterAudit` | 14,635 | 0.1% |

* 5501 条事件里,四位一体的 comp 串只有 **2838 种不同取值** —— 光句子就冗余 48%。
* 语料全量:**768 份 / 433.5 MB**。
* 顶层 15 个键,其中 **11 个 `rosterAudit` 字段 + 4 个 `statusAudit` 字段 + 5 个 `forensics` 字段
  只有分隔符拼接的字符串形态**,内部格式各自发明(`" | "`、`": "`、`' '`、`'/'`、`'、'`、`-`/`=`/`>`)。
* **没有 schema 版本**:只有 `version` = 插件版本(1.0.29→1.4.1 共 70+ 个值),
  它随**行为**改动而变,不随**结构**改动而变。而 `_dpsm_work/v135_probe.py:8` 已经在读
  `d.get("schemaVersion")` 和 `d.get("pluginVersion")` —— **两个都不存在的键,静默拿到 None**。

**去重 + 结构化的投影实测**(用上面的 12 元组键,事实表存一份、事件只带索引):

| 战斗 | 现状 | 投影 | 比值 |
|---|---|---|---|
| 411001(5585 击) | 14,504,954 | 1,833,543 | **7.9×** |
| 411001(5549 击) | 13,249,055 | 1,628,715 | **8.1×** |
| 9999(1734 击) | 319,156 | 677,182 | 0.47× |
| 700703(211 击) | 87,862 | 492,829 | 0.18× |

**必须诚实说明**:短战斗**不会**变小(事实表的固定开销大于收益)。
所以 B 批的真实卖点**不是体积,是覆盖率**:
在同一份体积里,今天的 160 份样本可以变成**每击都有事实**;
长战斗顺带缩小约 8 倍,语料 433 MB 大致降到 ~70 MB。

### 2.6 插件在回读自己的散文

`Diagnostics/StatusDeltaProbe.cs:380-384` 的注释逐字:

```
/// The status a victim carried at each record is already stored on the event as comp4's
/// "受击方状态:…" text (live BuffList at record time), so this parses that instead of duplicating the
/// data into a new event field.
```

调用点 `:411` `StatusSetOfComp4(e.Comp4)`,解析器 `:476-493` 依赖字面前缀 `"受击方状态:"`(`:482`)。
→ **顶层 `statusAudit` 块唯一的输入是中文散文。** 任何改 comp4 措辞的动作(「无」→「なし」、
三段空格→两段)都会静默改掉 `appearances/explained`,而编译不报错。

### 2.7 诊断日志重新推导了一遍算式,与文档化的不变量矛盾

`CompositionProbe.cs:43-44` 声明:"A clause's verdict is decided in exactly ONE place (JudgeClause)
and reused for the display, the arithmetic and the [ABIL] dump, **so the log can never disagree with the number**."

但 `Diagnostics.cs:196-223` 自己又算了一遍,**没有**调用 `GivenTalentDamage`,也**没有**调用
`MadnessMultiplier`:

```csharp
try { AbilityScan(atk, 6, ht, victimSide: false, blocker, out mAtk); } catch { }
try { AbilityScan(blocker, 6, ht, victimSide: true, atk, out mVic); } catch { }
try { string tt; double f = TalentDamage(atk, false, out tt); if (f != 1.0) mAtk *= f; } catch { }
...
double g = ApplyGlobalDebuffs(atk, blocker, out gTxt); if (g != 1.0) mAtk *= g;
sb.Append("   ·   已识别倍率 ×").Append((mAttr * mAtk * mVic).ToString("F4"))
```

→ 任何带"赋予被伤害"或"狂気"的命中,`[COMP]` 行报的"已识别倍率"**必然不等于**
`CalcBreakdown.KnownMult`。而**诊断日志正是离线归因的主要读物** —— 一个与算式不一致的日志
会让每一次归因研究得出错误结论(1.3.5 的双计就是这么被发现的)。

### 2.8 加一个探针的机制成本(你上次反馈的根源)

以「给每个 actor 加一个『本场貫通命中数』」为例,今天**必须**碰:

1. `src/Diagnostics/XxxProbe.cs`:加 static 字段 + 在 `Reset()` 里清零
2. 热路径递增点(`Chain.cs:85-87` 或 `Aggregator.cs:1092` 附近)
3. `src/Output/ExportService.cs` 的 `rosterAudit` 块(`:319-407`)加一行 `.Append(...)`
4. 若要新顶层键:还要在 `:275` 附近加 `sb.Append(...)` + 探针类新写 `AppendJson(StringBuilder)`
5. 版本号**两处同时改**:`src/BuildInfo.cs:14` **和** `src/DpsMeter.csproj:11`(1.2.0 漏过前者)
6. 若复用 `CalcReconcile` 判据:改 `CalcReconcile.cs`,并**重跑 `recon_probe` 断言**
7. 配置开关:`Plugin.cs:105-108` 声明 + `:226-250` 加 `Config.Bind`(现 25 个)
8. 构建 → 反编译复验 → 归档 `.bak` → 复制 → SHA256
9. 4 个守卫脚本
10. 3 处文档(`SESSION-STATE.md` §2 与 §7.2.x、`ARCHITECTURE.md`、根目录 note)
11. 分析侧新写解析代码
12. **然后打赢一场,才有值可取**

而且**没有任何测试断言导出的 schema** —— 键名拼错、漏一个逗号导致 JSON 非法,
只能等下一个脚本报错(1.4.1 那次 `Config.Bind` 被改坏而编译通过,就是同一个盲区)。

---

## 3. 目标架构

把上面六条反过来写,就是一个四层结构。**关键是第 0 层与第 1 层之间要有一个显式的数据契约**。

```
L0  采集层(FactStore)     命中瞬间把"算式消费了什么"复制成纯数据;去重后入库
        │                  产物:facts[] + 每击一个 factId
        ▼
L1  规则层(纯函数)        Fold(HitFacts) -> {factors[], knownMult, ...}
        │                  ★ 不接触任何 Il2Cpp 对象 ⇒ 可编进 recon_probe、可离线重放
        ▼
L2  对账层(已有)          CalcReconcile:exact/crit/approx/unexplained
        │
        ▼
L3  导出层(schema 化)     schema 版本 + 结构化孪生 + IExportSection 注册表
```

**L1 的"纯"是整个方案的核心**:一旦 `Fold` 不接触活体对象、只吃 `HitFacts`,
那么"加一条规则 → 用 768 份历史导出重放 → 比较 exact% 前后"就成为一条命令,
**不再需要打战场**。这就是把 §2.8 的机制成本从"一场战斗"降到"一次运行"。

---

## 4. 路线图

### A 批(建议 1.5.0):让「残差 → 规则」可答 —— 不改算式

| # | 动作 | 行为 | 证据 |
|---|---|---|---|
| A1 | `CalcBreakdown` 加 `FoldStep[] Fold`(`{Side, Kind, OriginId, Factor, Label}`),在 `Chain.cs` 现有 `Join` 的**同一位置**追加;导出 `calc.fold[]` | **保持**(只记录) | §2.2 |
| A2 | 接线 `RecordHitDetail`(在既有 postfix 里,源值取自 calc 的 ctor 参数),并补 `StartSession` 里 `_activeCalc` 的清零 + 与 `:197` 同款的 age 上限 | **改变(修复)** | §2.1 |
| A3 | 会心第一方信号三态 `CritObserved{None/No/Yes}` 进数据;`IsCritExplained` 有观测时以观测为准,无观测时回退推断**并单独计数** | 保持 | §2.1 |
| A4 | 事件加稳定 id `AttackerKey`/`VictimKey`,在 `BattleSession.GetActor` 建 Actor 处顺手分配 | 保持 | 事件身份靠显示名 |
| A5 | `_enemyCandidates` 从 `Dictionary<double,int>` 改成 `List<FoldedFactor>`(带 `OriginId`),消费端改成 `TryConsumeModeled(f, out origin)` | **改变(这就是目的)**,用既有 `GivenCancelsGlobal` 灰度 | §2.3 |

**为什么 A1 和 A5 要分开**:A1 是纯记录,零风险,先把证据拿进数据;
A5 是行为改变,必须能在**同一场**里用 A1 的数据 A/B。
**在 A1 落地之前,不要动 1.15^n 的折叠** —— 那会重演 1.3.5–1.3.8。

### B 批(建议 1.5.0 或 1.6.0):一次打完,不再补探针

| # | 动作 | 收益 |
|---|---|---|
| B1 | `FactStore`:`Forensics` 的取证样本推广成"**每击都有**的 fact 记录 + 去重表",事件只带 `factId` | 覆盖率 160/5501 → **5501/5501**,体积持平或更小 |
| B2 | `UnitStateProbe.AppendJson(sb)` + 各 `*Sample` 字符串加**结构化孪生**(旧 key 保留一期) | 抗性/状态/天赋/布局不再需要正则 |
| B3 | `StatusDeltaProbe` 不再回读 comp4,改读 `BattleEvent.VictimStatuses` | 消除"改措辞静默改数" |
| B4 | 时间线通道:`resist`/`status` 的**变更序列**(仅在变化时写一行) | 抗性曲线从 64 采样点变成全分辨率,`liveResistRange` 的 min..max 局限消失 |
| B5 | 诊断日志改读 `BuildChainResult`,不再重算 | 日志与算式不会再有分歧(§2.7) |

### C 批(建议 1.6.0,最大杠杆):离线可重放

| # | 动作 |
|---|---|
| C1 | 把 `BuildChainParts` 拆成 `GatherFacts(game objects) -> HitFacts` 与 `Fold(HitFacts) -> CalcBreakdown`,`Fold` 不含任何 Il2Cpp 调用 |
| C2 | 把 `Fold` + `CalcReconcile` 一起编进 `recon_probe`,对 768 份历史导出重放 |
| C3 | 回归脚本:`改规则 → 全语料重放 → 输出 exact/exactWithCrit/残差直方图的前后对比` |

**C 的收益不是省时间,是把"规则正确性"从"打一场看 KPI"变成"跑一遍看 diff"。**

### D 批(整理,可与其他批并行)

`schema` 版本键 + `units` 声明 + `IExportSection` 注册表 + `JsonText.Key` 补全(key 转义)
+ 只增不删的兼容约定 + 列式/压缩导出 + prose 改按需生成(`ExportProse` 开关)。
**注意**:0.9.x–1.2.x 的 600+ 份导出没有 `calc`/`reconcile`,只能靠 prose,
所以 21 个脚本里的中文正则**不能删,只能停止扩散**。

---

## 5. 不该做的事(反向清单)

1. **不要重写 `Aggregator`**(1169 行,债务 #1)。它是稳定代码,收益/风险比差;
   按 `Session/Attribution/Stats/Finalize` 拆 partial 可以,整体重写不行。
2. **不要删 prose**。600+ 份历史导出依赖它;正确做法是"结构化孪生 + 旧键保留一年"。
3. **不要改 `dealt` 口径**。这是已定案的产品决定(`dealt` = 进入耐久,用 `dealtWithAbsorbed` 对账)。
4. **不要在 A1 落地前动 1.15^n 的折叠**。见 A 批说明。
5. **不要一次性改所有 schema**。批次内一次改完(减少版本 bump),批次间保持可比 ——
   否则 `reconcile.exact` 的前后对比失去意义。

---

## 6. 附:审计方法与被证伪/修正的断言

三个只读审计分别覆盖 `Composition/`(14 文件)、`Output/`+`Diagnostics/`、
`Aggregator`+`Hooks`+`Model`;本文作者对**所有会影响决策的断言**做了就地复核。复核结果:

| 断言 | 复核结论 |
|---|---|
| `RecordHitDetail` 无调用者 ⇒ `Source`/`Crit` 恒为常量 | **成立但需更正范围**:伤害的 `source` 恒 0(510,735/510,735),但**治疗显式写 `DirectHeal=8`**(59,343 条)。字段可写,是伤害路径没接线。 |
| `crit` 恒 false | **成立**:570,078/570,078,且全语料 0 个 actor 的 `crit` 非 0。 |
| `_enemyCandidates` 按数值去重、计数从不被读 | **成立**:逐条核对全部 7 个使用点。 |
| 折叠只有 `DealtMult`/`TakenMult` 两个标量 | **成立**(`CalcBreakdown.cs:63,66`)。 |
| `StatusDeltaProbe` 回读 comp4 散文 | **成立**(`:411` → `:476-493`,依赖前缀 `"受击方状态:"`)。 |
| `Diagnostics.cs` 重算算式、缺 `GivenTalentDamage`/`MadnessMultiplier` | **成立**(`:203-213`)。 |
| prose 占导出 45.8% | **本作者实测**(6,062,813 / 13,231,042 字节)。 |
| 去重可把长战斗缩到 ~1/8 | **本作者实测**,但**短战斗会变大**,已在 §2.5 标注。 |
| 「1.15^n 的成因是 value-only 抵消」 | **推断,未证实**。本报告只主张"当前设计无法区分两种可能",不主张是哪一种。 |

**可复现命令**(在仓库根执行;`$py` 指向本机 python):

```powershell
# 全语料 source/crit 分布
& $py -c "import json,glob,collections;g=collections.Counter();c=collections.Counter();
[ (g.update([(e.get('type'),e.get('source')) for e in (json.load(open(f,encoding='utf-8')).get('events') or [])]),
   c.update([e.get('crit') for e in (json.load(open(f,encoding='utf-8')).get('events') or [])])) for f in glob.glob(r'BepInEx\plugins\DpsMeter\exports\*.json')]
print(g.most_common(5), c.most_common(5))"
```

---

## 7. 一句话结论

插件的**度量**已经很扎实(KPI、口径、反例纪律都好),
卡住的是**事实的保存与回放**:事实只在命中那一瞬存在、只以中文句子留下痕迹、
只覆盖 3% 的命中,而规则的来源信息在乘进两个 double 的那一刻就消失了。

**A 批把来源找回来,B 批把事实留下来,C 批把这一切变成可离线重放。
这三件事做完,"再打一场"才会从必要步骤变成可选项。**
