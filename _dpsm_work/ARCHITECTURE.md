# DpsMeter 插件架构说明

> 本文档写于版本 **1.0.29**（历史快照，范围以 1.0.29 为准）；当前部署版本为 **1.7.9**。它描述**代码结构、数据流、不可破坏的约定,以及"要加功能该改哪个文件"**。
> 记录式(为什么这样改、证据在哪)见 `SESSION-STATE.md`;文档总入口是根目录 `DpsMeter-文档索引.md`。

---

## 0. 构建 / 部署 / 验证

```powershell
# 构建
D:\dmmplayer\dotnet-sdk6\dotnet.exe build D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\src\DpsMeter.csproj -c Release -v minimal

# 部署(先备份旧 DLL,再复制,最后必须核对 SHA256 一致)
Move-Item BepInEx\plugins\DpsMeter\DpsMeter.dll BepInEx\plugins\DpsMeter\DpsMeter.<旧版本>.bak -Force
Copy-Item _dpsm_work\src\bin\Release\DpsMeter.dll BepInEx\plugins\DpsMeter\DpsMeter.dll -Force
(Get-FileHash _dpsm_work\src\bin\Release\DpsMeter.dll).Hash -eq (Get-FileHash BepInEx\plugins\DpsMeter\DpsMeter.dll).Hash
```

**改代码只用 `edit` / `write` 工具。** 本机 PowerShell 的 `Get-Content -Raw | Set-Content -Encoding utf8`
会破坏 CJK 字符(已发生过一次,`Aggregator.cs` 里的 `按当前动作配对` 等被写坏)。凡是要过
PowerShell 的文本往返,一律改用 Python(`io.open(..., encoding='utf-8')`)。

**⚠️ 发版时必须改 `src/BuildInfo.cs` 的 `Version`(只改 csproj 的 `<Version>` 不够)。**
1.2.0 就是这么翻车的:改了 csproj,忘了 `BuildInfo`,于是**导出 JSON 的 `version` 与启动横幅都写着 1.1.3,
   而跑的其实是 1.2.0 代码** —— 正好是"导出带版本号"这条纪律要防的失效模式。
   `BuildInfo` 是**用户可见版本的唯一来源**(横幅 + 每个导出 JSON);csproj 的 `<Version>` 只影响程序集元数据。
   发版后核对:`ilspycmd -t DpsMeter.BuildInfo` 里的字符串 = 预期版本。

**结构性改动的验证手法**(1.0.29 的三次大改都是这样验的):

1. 行级无损证明 —— 见 `refactor_split_composition.py` / `refactor_split_overlay.py`:
   把原文件按行区间搬进新文件后,统计"原文件所有非空行在新文件中恰好出现一次"。
2. IL 等价证明 —— 用 `ilspycmd -o <dir> <dll>` 反编译改动前后的两个 DLL,比较**行多重集**:
   ```powershell
   python -c "import io,collections; a=io.open(旧,encoding='utf-8').read().splitlines(); b=io.open(新,encoding='utf-8').read().splitlines(); print(collections.Counter(a)-collections.Counter(b), collections.Counter(b)-collections.Counter(a))"
   ```
   成员顺序会变(编译器按源文件顺序发射),所以比"多重集"而不是比文本。集合相同 = 只搬了家。
3. 行为差异清单 —— 上面第 2 步对**有意的**行为改动也会diff出结果,逐条确认"只有我想改的那些"。

---

## 1. 模块地图(61 个 .cs / `Diagnostics/` 与 `Composition/` 为主要扩展点)

```
src/
  Plugin.cs                  组合根:BepInEx 入口、所有 ConfigEntry、Harmony PatchAll、悬浮窗装配
  BuildInfo.cs               版本号唯一来源(GUID/名称/版本)
  GameRef.cs                 Unity/IL2CPP 判空与解密助手(IsNull/IsAlive/Same/Dec)
  GameSystemAccess.cs        GameSystem 单例的安全获取(带缓存,供 Tick 判定换局)
  Aggregator.cs              会话与统计中枢:Tick 时钟、事件入库、攻击者归属、结算与导出触发

  Hooks/                     游戏侧 Harmony 补丁(按被补丁的游戏类型分文件)
    DamageCalculaterHooks.cs   Action(攻击开始)/DamageAction/ActDamageAction/ApplyBarrier+Enchant/反射/会心探针
    BattleObjectHooks.cs       BattleObject.Damage(命中前状态快照 + 伤害入库)、Heal(治疗入库)、SetupAbility(登记全局规则)
    GameSystemHooks.cs         Init / EarlyUpdateMain(每帧驱动) / SetGameResult / SetBattleEndFlag / ActDestroy
    InputHooks.cs              第二个每帧驱动(InputManager.Update)
 TalentActionHooks.cs       1.5.4:MadnessApplyHook(TalentActionAddMadness.ActExecute;Plugin 手动
                            单独补丁,不进 PatchAll 集 —— 解析失败不得连坐)

  Model/                     数据模型(无逻辑)
    BattleSession.cs           一局的状态:时钟、Actor 表、待配对命中、事件流、队伍每秒序列
    ActorStats.cs              单个单位的统计(伤害/承伤/治疗/会心/技能/每秒曲线/耐久曲线)
    BattleEvent.cs             导出用的一击事件(dmg/heal + comp1..comp4 + calc 结构体)
    CalcBreakdown.cs           一击构成的结构化镜像(1.3.0):中文串说的每个数字 + 配对/算术两个布尔
    HitRecord.cs               来自游戏侧的命中明细(1.5.0 起**真的有生产者了**):DamageSource/会心三态/命中类型
    FoldStep.cs                1.5.0 折叠 provenance:`FoldStep`(逐因子 Origin)/`FoldedFactor`(责任集)/
                               `CancelStep`(被取消的授予)/`FoldContext`(每击的显式 sink,不含 IL2CPP 依赖,
                               所以 recon_probe 能离线编译它)
    StatusKey.cs               1.5.2 状态集合的规范形(去重 + 序数排序)。**故意不依赖 IL2CPP/Plugin**,
                               于是 recon_probe 能编译并执行它 —— "集合只有一个规范形"由测试钉住
    ClauseStatusRun.cs         1.5.3 条款文本 -> 状态名列表的**全部**匹配逻辑(StatusNameAt /
                               FollowedByState / StripVerifiedStatusRuns / StatusTokens)。同样纯函数、
                               不含 IL2CPP:因为 と/や 连接词漏认的代价是**整条规则不折**(1.15²),
                               这种下判决的逻辑必须能被离线执行
    BattleSummary.cs           已结束一局的摘要(历史列表用)
    BattleTime.cs              战斗时间的**唯一**格式化入口(界面行/明细行/日志都用它)
    CharacterInfo.cs           显示名/阵营/种类标签

  Composition/               伤害构成模块(见第 3 节)
    CompositionProbe*.cs       10 个 partial 文件,一个类
    TieredModifier.cs          耐久档位表解析("9/6/3割以下 → それぞれ-10/-25/-40%")
    CharacterNames.cs          wiki 角色/召唤物名表(含去括号变体),用于抑制误导性的技能名前缀
    TalentNames.cs             **生成文件**(`_dpsm_work/gen_talent_names.py`),勿手改:
                               `eBuffType` / `TalentDefine.Type` / `TriggerTiming` / `eTalentCondType`
                               四个枚举的完整名字表(共约 560 项)。手写这么多成员必然出错,所以由脚本
                               直接从 interop 反编译件解析生成。中文名是**枚举成员名的翻译**,不是游戏文本;
                               两张表都不认识就保留 `tNNN`/`TNNN`/`cNNN`,猜错不会像是已解码。
                               存在理由:`TalentData.TalentType` 是**两个 id 空间**的并集 ——
                               `>=1000` 是 `TalentDefine.Type`(动作),`1..999` 是 `eBuffType`(参数与**状态**)。
    AbilityRoster.cs           1.1.0 能力**出处**名册:每单位的能力 + 来源槽位(eAbilitySlotType:
                               刻印/神器/觉醒/职业…) + 每条素质的触发条件(eTalentCondType)。
                               三条路线逐级降级(A:游戏自己的槽位表 → B:对象指针配对 → C:统计表连接),
                               配不上就报「未分类」而不猜。名册**缓存在 ActorStats 上**,因为
                               FinalizeLocked 会在导出后置空 Source。导出与明细页都读它。

  Output/
    ExportService.cs           结算时导出 battle_*.json(离线分析用)
    JsonCheck.cs               1.5.0:写盘前的导出**结构自检**(字符串外括号平衡 + 根键重复),
                               结果进 `[DpsMeter][JSON]` 行。离线探针反向验证它能拒绝四种畸形。
    RuntimeLog.cs              轮转式运行日志(BepInEx\config\dpsmeter_runtime.log)

  Ui/                        悬浮窗
    OverlayUGUI*.cs            4 个 partial 文件,一个类
    OverlayCore.cs             IMGUI(实验性)渲染器入口
    OverlayChart.cs            图表绘制(像素级曲线/网格)
    OverlayUI.cs               IMGUI 的 MonoBehaviour 壳

  Diagnostics/
    Probe.cs                   候选伤害路径计数器([PROBE] 输出),用于找"无来源伤害"
    PowerProbe.cs              DamageCalculater 构造/AddBlocker 前缀读数与心跳([POWERHOOK]/[POWER])
    TimeProbe.cs               游戏时钟实测([CLOCKP]:步进率/技能 CD 单位)
    StatusDeltaProbe.cs        本条命中是否给目标挂上异常状态(Damage 前缀快照 + 差分 + 0.35s 复检)。
                               1.5.0 起 `Audit` 读 `CalcBreakdown.VictimStatuses` 字段,**不再回读 comp4 散文**
    FactStore.cs               1.5.0(B1):每击一个 `factId` → 去重后的事实表(四条 comp 串 + 该类的
                               **活体状态**)。实测 5501 击只塌成 331–463 类 ⇒ 覆盖率 3% → 100%
                               ⚠ 1.5.2 实测打脸:1.5.1 的签名仍产生 **1485** 类(> 600 上限),覆盖率
                               只有 36.6%;其中 **1232 类只来自逐实例的 `vicKey`**。身份改成 单位名+阵营
                               后 → **240 类 / 100%**(见第 4 节 rule 41)。上限 600→2400、活体 420→900
                               ✅ 1.5.2 实机(183213):`[FACT] 覆盖=5582/5583`、`类=271/2400`、`类溢出=0`;
                                 离线重放对同一场也独立给出 271 类,与插件逐位相等
    StateTimelineProbe.cs      1.5.0(B4):每击读**受击方**18 个抗性槽 + 10 个状态位,**只在变化时**出行 ⇒
                               抗性曲线从"64 点采样 + 虚线示意"变成实测台阶(`timeline.rows`)
    StatusApplierProbe.cs      1.5.4(贡献归因 C):TalentActionAddMadness.ActExecute 的 POSTFIX 记录
                               (t, owner, guest) → `madnessApplies` 段 + 每单位最近狂気施加者表
                               (逐击 madness 折叠的 `byUnit` 来源)。方向/完备性不假设,与 timeline
                               交叉验收;开关 General/MadnessApplier。见 §7.2.77
    SlotProbe.cs               [SLOT] 1.1「机制观测」改造的**可行性探针**(1.0.58,只读、零新补丁):
                               能力的来源槽位(eAbilitySlotType)、每条素质的触发计数、
                               结构化触发条件(eTalentCondType: IsFrozen/IsIgnoreAttack…)、
                               周期计时器/概率门,以及计数读取成本的实测与外推。
                               开关 Debug/SlotProbe。见 DESIGN-1.1-机制观测改造.md
    TalentRuntime.cs           1.1.0 素质/词条**触发观测**:逐条命中读取游戏自己的触发计数
                               (TalentData 直读字段),与上一次该攻击者的命中比较。口径是
                               「发动所产生的那一跳承载差值」,已写明副作用。每击有 512 次读取硬预算。
                               导出 events[].triggers[] + actors[].talents[];开关 General/TalentTriggers

  MasterData/
    MasterDataDump.cs          1.2.0 **官方主数据导出**:19 张主数据表(刻印/刻印效果/刻印变异/刻印强化/
                               神器/装备/能力/潜在/觉醒潜在/战斗定义/属性/职业/稀有度/持续伤害/
                               追击配置/特性标签/种族/细则文本/说明)+ 游戏自己的表名清单。
                               读法见第 4 节第 17 条(`Values.CopyTo`),混淆字段走 `GameRef.Dec/DecStr`。
                               1.2.1 起:同一表类有多个实例时取**行数最多**的并报告全部行数(实测能力表有 9 个实例);
                               展开刻印每级效果与天赋数值参数(`AbilityTalent.Param.num`);
                               打包 ASCII 的稀有度码(`5460529="SR1"`)解成字符串并列。
                               开关 General/MasterDataDump。
                               背景与"为什么不能解包"见 SESSION-STATE.md §7.2.50-51 与
                               `_dpsm_work\REPORT-解包与游戏内数据获取.md`
    MasterDataNames.cs         1.2.2 **刻印官方命名**:刻印槽的能力自带名字为空(一场实测 39/39),
                               于是用母表补。规则 = `id` → `talent 签名` → **候选名字全同才采用,
                               否则不命名只计数**(本场 15/15)。之所以不能只看 id:刻印槽里有**两套重叠
                               的 id 空间**(基础刻印 1..14 与刻印变异 1..28),详见第 4 节第 19 条。
                               参数不参与匹配(母表 `param[[10]]` 与导出已缩放的 `p=[30,0,0]` 不可比)。
                               导出加 `"nameFrom":"master"` 使来源可辨;计数进 `[ROSTER]` 行尾。
    MasterDataAccess.cs        (未建)1.2.2 的 `MasterDataNames` 自带一份与非泛型表查找等价的实现;
                               `MasterDataDump.Table` 里那份是**已验证可用**的,故本次不动它。
                               下次改 dump 时应把两处合并到这里 —— 记为技术债。

  Output/
    CalcReconcile.cs           1.3.0 **对账 KPI**:从事件流(不是从累加器)算出 exact/approx/unexplained/
                               mispaired、byPair、byTenth(按战斗时间 10 等分)、topResidual(未识别倍率
                               排行榜,前 20)。**在导出时算而不是战斗中累加**,所以汇总永远不可能与它
                               汇总的那些行不一致 —— 离线口径漂移正是"两处各算一遍"造成的。
                               同时负责每条伤害的 `calc` 块与 `[RECON]` 自检行。
                               根级 reconcile 无开关,逐条 calc 受 `General/ReconcileCalc`(默认 true)。
```

**为什么 CompositionProbe 和 OverlayUGUI 用 `partial` 拆而不是拆成多个类?**
它们是各自领域里的一组强耦合状态机,拆成独立类需要把私有状态全部改成显式参数传递,
改动面和回归风险远大于收益。`partial` 拆分的收益是**每加一条规则只需打开一个几百行的文件**,
且可被"行级无损 + IL 等价"完全证明。真要把某个 partial 提成独立类,按第 7 节的办法一次提一个。

---

## 2. 数据流(一次命中发生了什么)

```
游戏
 ├─ DamageCalculater.Action()                       ← 攻击开始
 │     └─ Aggregator.NoteActiveCalc(calc)
 │           └─ CompositionProbe.SnapshotStatuses(calc)   ★ 攻击开始快照(双方耐久%+状态)
 │
 ├─ DamageCalculater.GetFlyTextNumberSizeForAttack(attrRate, isCrit, defGreater)
 │     └─ CompositionProbe.NoteCrit(...)                  会心率/属性倍率(别无他处可读)
 │
 ├─ DamageCalculater.ApplyEnchantDamage / ApplyBarrierDamage / DamageAction / ActDamageAction
 │     └─ Probe.NoteCalc(calc, dmg, tag)
 │           └─ Aggregator.NoteCalcActivity(...)           记入"待配对 calc"环形表
 │
 └─ BattleObject.Damage(dmg, attacker, owner)              ← 伤害真正落地
       └─ Aggregator.RecordDamage(victim, attacker, owner, dmg)
             ├─ 归属:attacker → owner → 最近 calc(A/O) → 未归属(记 [PROBE]/gap)
             ├─ ActorStats 累加(承伤/输出/会心/技能/每秒)
             └─ TryGetCompForVictim(...) ─→ CompositionProbe.BuildChainParts(calc, victim, 已发布量:普通命中 = 游戏口径,返回值报溢出时 = 超出剩余耐久)
                                             → comp1 算式 / comp2 攻击方 / comp3 受击方 / comp4 状态异常
                                             → 写入 BattleEvent(导出) + 悬浮窗行

 悬浮窗: Ui/OverlayUGUI.Rows.cs 读 Aggregator.Session → 行列表 → Ui/OverlayUGUI.Pool.cs 布置
 结算:   Aggregator.FinalizeLocked → [CROSS] 与游戏自身统计对照 + ExportService.Export(s)
```

**攻击者归属的顺序**(`Aggregator.TryGetCompForVictim` / `RecordDamage`,顺序有语义):
1. 正在执行的 calc(0.20s 内,且目标一致 → 标"精确");
2. 同一受击方 + 同一伤害值的近期 calc;
3. 同一受击方最早的未使用 calc(FIFO,保持多段命中的顺序)。
非精确配对会在文案里标注(`· 按当前动作配对` / `· 按时间顺序配对`),**不允许让近似冒充精确**。

**两个时钟,别混用**:

| 用途 | 来源 | 说明 |
|---|---|---|
| 战斗计时(界面/导出/DPS/图表) | **游戏自己的时钟**:`ΔGameSystem.GameTime ÷ 每秒游戏单位数`(默认 `ClockSource=game`) | 单位数优先取已加载技能数据的 `Skill.CoolTimeFrame / Skill.CoolTime`(**实测 30.0**),否则配置值,再否则 30。实测:GameTime、`GameTimeLimitCounter.NowTime`、技能的 `wait` 计数三者同单位、同速率(45 单位/真实秒 @timeScale 1.5)→ 1 游戏秒 = 30 单位,游戏时钟 = 1.5 × 真实时间。`ClockSource=real` 用秒表测真实秒;`engine` 用 `Time.deltaTime`。**不要用 `Time.unscaledDeltaTime`**:它在本作是固定逻辑步长(1/45s),逐帧累加会跑成 0.67×。单帧 delta 一律钳制 0.25(卡顿一帧曾携带 2.9s) |
| 游戏自己的时钟(可选 `ClockSource=game`) | 技能 CD 是**单位/帧计数** | `Skill.m_coolTimeFrame` / `m_waitCountFrame` 由 `Skill.Update` 每帧递减;数据侧 `Skill.CoolTime` / `FirstCoolTime` 为整数;缩短 CD 的天赋按计数处理(`TalentActiveSkillWaitTimeShortening`)。`Diagnostics/TimeProbe.cs` 的 `[CLOCKP]` 行打印 stepRate / dNow/s / units-per-s 供复核 |
| 空闲判定(会话关闭) | `CombatSeconds - LastEventCombat` | 单位同当前时钟(game 模式下为游戏秒);不包含暂停。挂钟版本曾在"暂停结束的第一帧"把会话关掉,把一场战斗切成两段(F6 显示尾巴会话,t 全为 0.0s)。软关闭(idle)5 秒内若同一批单位继续产生事件则恢复该会话 |
| 诊断对照 | `GameTime` / `GameTimeLimitCounter.NowTime` | 两者是**同一个计数器**的两个视图(实测 dSteps == -dNow),都是游戏单位,不是秒。只在 `[TIME]`/`[CLOCKP]` 里做对照 |

`Aggregator.Tick` 用 `Time.frameCount` 做**每帧幂等**,因为有两个驱动(`GameSystem.EarlyUpdateMain`
与 `InputManager.Update`),任一缺失都不会停摆。

---

## 3. 伤害构成模块(Composition/)

一个 `static partial class CompositionProbe`,按职责分 10 个文件:

| 文件 | 职责 | 什么时候动它 |
|---|---|---|
| `CompositionProbe.cs` | 共享状态、`Reset()`、`Power()`、约定说明 | 加跨文件共享状态时 |
| `CompositionProbe.Crit.cs` | 会心标志 + 属性倍率的观测与匹配 | 会心/属性相关 |
| `CompositionProbe.Abilities.cs` | 技能文本缓存(`GetAbilityText`)、`AbilityScan`(逐条裁决并累乘) | 需要改"扫描哪些技能/怎么取文本" |
| `CompositionProbe.Rules.cs` | **规则核心**:`JudgeClause` 关键词/位置/属性门控、`EvalConditions` 条件求值、`ParseDamageModifier` 数值解析、`SplitClauses` 分句、`TieredModifier` 档位 | **加一条新的增减伤写法** |
| `CompositionProbe.Status.cs` | 状态名表、`HasStatus/HasStatusLike`、**攻击开始快照** | 状态相关条件 |
| `CompositionProbe.GlobalRules.cs` | 全局规则表(敌方受伤 / 我方攻击)+ 登记与回收 | 加"全队/全体敌人"类规则 |
| `CompositionProbe.Talents.cs` | 只以天赋数据存在的增减伤(`DamageUp=1005`/`DamageCut=1006`) | 天赋类倍率 |
| `CompositionProbe.Chain.cs` | 显示用四行:`BuildChainParts`、增益/参数文本 | 显示内容/格式 |
| `CompositionProbe.Text.cs` | 枚举与标签翻译表 | 新增枚举值的中文名 |
| `CompositionProbe.Diagnostics.cs` | `[ABIL]` 逐条裁决转储、`[COMP]` 单次命中日志 | 排障 |

### 判定链(必须保持"单一裁决点")

```
AbilityScan(攻击方/受击方)
  → SplitClauses(分句,不截断长度)
  → JudgeClause(每条子句)                    ← 唯一的裁决点
       关键词命中? 位置(前衛/後衛)符合? 属性符合? 条件满足?
       → Counted(产出倍率) | Conditional(显示但未计入) | ConditionFailed | Skipped* | NotParsed
  → 累乘得到该侧的合计倍率
  → BuildChainParts 用同一个 JudgeClause 结果同时生成"显示文本"和"算式"
  → Diagnostics 的 [ABIL] 也复用它打印理由
```

`JudgeClause` 是唯一裁决点这件事**不是风格问题**:显示路径与诊断路径曾经各自实现一次,
结果 `[COMP]` 日志与悬浮窗长期不一致(1.0.28 才修好)。新增规则时不要另开一条平行路径。

---

## 4. 不可破坏的约定

1. **条件必须用"攻击开始快照"判定。** `SnapshotStatuses` 在 `DamageCalculater.Action` 里记下双方
   耐久%与状态;`BuildChainParts` 用 `SnapshotHpOf/StatusSetOf`,并在 `finally` 里
   `UseStatusSnapshot(null)` 清掉。原因(都真实发生过):
   * 中毒那次攻击自己施加的毒,不能给这次攻击自己加成(否则出现 `剩余倍率 ×0.870 = 1/1.15`);
   * 按耐久档位减伤的技能必须读"被打之前"的耐久,否则 12.6% 的档位命中会读错档。
2. **显示与算式共用一次裁决。** 见上节。
3. **不硬编码任何临时/赛季数值。** 一律从游戏对象里读(天赋、BuffTalent、全局规则)。
   例:某周盾职业 30% 减伤不在单位数据里(周模式级),插件不写死,只在能读到天赋时读。
4. **全局规则表在结算时不清空**(`FinalizeLocked` 里有注释)。单位是在**上一局结算之前**创建的
   (`SetupAbility`),清表会把新一局的登记一起丢掉,导致"开局前几下没有 ×1.15"。
   回收策略:只回收已死/无主条目(硬上限 >600 才整表清)。
5. **血量/承伤口径与游戏一致**:同队伤害(`回復反転`、自伤)默认仍计入总输出(游戏自己的战报也算),
   但始终单独标注;`FilterFriendlyFire=true` 才剔除。
6. **术语按 wiki**:耐久 / 会心率 / 会心伤害率 / 贯通率 / 回復率;被克制没有 ×0.5 惩罚。
7. **判空分两种类型**(详见 `SESSION-STATE.md` §7.3):只有继承自 `UnityEngine.Object` 的
   (MonoBehaviour 系:`BattleObject`/`GameSystem`/`Font`/`Texture`…)才能用 `GameRef.IsNull/Same`;
   普通 Il2Cpp 对象(如 `Rog.Domain.AttributeModel`)必须用 `== null`,否则 `(UnityEngine.Object)`
   转换会抛 `InvalidCastException`,再被外层 `catch { }` 吞掉 → 静默退化成兜底值。
   历史上这个错误让"属性克制 ×2"失效了很多个版本(245 个导出里 `克制` 全为 0)。
8. **战斗时间只有一个来源、只有一个格式化入口。**
   * 时钟:`BattleSession.Advance/NoteEvent/IdleCombatSeconds`(`Model/BattleSession.cs`)——
     悬浮窗主页"时间 N秒"、F6 明细"t=N.Ns"、图表 x 轴、DPS 分母、导出 `duration`、日志全部读同一个
     `ActiveSeconds`,任何别处都不许累加时间;
   * 格式化:`BattleTime.Seconds/Hit/Log`(`Model/BattleTime.cs`),不许在界面或日志里写 `:F0}秒` 这类内联格式;
   * 收尾时打印 `[DpsMeter][CLOCK] source=… units=… active=… wall=… ratio=…`,`[TIME]` 行也有 `rate=`,
     所以"时钟来源/速率不对"下一场就能在日志里看见;
   * 回归守卫:`python _dpsm_work/check_time_single_source.py`(违反上述归属即报错)。
9. **改配置默认值必须同时改已安装的 `BepInEx\config\dev.dpsmeter.cfg`。**
   BepInEx 会保留配置文件里已有的值,代码里改默认值**不会**迁移用户配置(1.0.34 把
   `ClockSource` 默认改成 `game`,但用户配置里仍是 1.0.33 写入的 `real`,于是"暂停时CD停、我们的表还在走"
   依旧存在)。所以:① 每次改默认值都要同步改 cfg(或用新 key 名);② 诊断行必须打印**实际生效**的值
   (`[CLOCK] source=`、`[TIME] rate=`)——这次就是靠它一眼定位的。
10. **每帧判重必须与"游标/增量推进"绑在一起。** `Tick` 有两个驱动
    (`GameSystem.EarlyUpdateMain` + `InputManager.Update`),用 `Time.frameCount` 去重;
    **时钟差值只能在通过判重的那次调用里计算**(`Aggregator.FrameDelta(val)`),
    因为 `game` 来源的增量(ΔGameTime)只存在于"游戏步进之后"的那次调用里 ——
    算在判重之前会让"应用的是空增量、带增量的被丢弃",时钟永远 0(1.0.35 实测全程 0 秒)。
    `real` 来源掩盖了这个顺序问题(它的增量可从挂钟重算),所以换基准时要重新验证去重与增量读取的相对位置。
    `[TIME]` 已有看门狗:`hits>0` 而 `active<0.05` 时打印 `!!! 时钟未推进`。
11. **给一个方法挂 detour 之前,先确认游戏不会用"无法封送的参数"调用它;崩在参数封送里时,把钩子体写得多干净都没用。**
    两轮实测(2026-09-27)把这条钉死了:
    · 1.0.48 给 `BattleObject.ActDamage` 挂 Postfix,体内调了 `ObjectType` → `AccessViolationException`,
      而且是 **corrupted-state exception,`try/catch` 捕不到**,游戏启动即崩;
    · 1.0.49 把钩子体改成"纯托管 + 会话门禁"后**依然启动即崩**,这次栈是
      `Il2CppObjectPool.Get<BattleObject>(IntPtr)` → `il2cpp_object_get_class` ——
      崩在 Harmony 生成的 `(il2cpp -> managed)` 跳板**把原生指针包成托管对象**的时候,
      也就是**我们的代码还没开始执行**。游戏确实会用无法封送的指针调用 `ActDamage`。
    结论/规则:
    · 对这类方法,**不要挂**;要问的问题优先用零风险的替代观测回答
      (本例:`[CROSS]` 的 `nominal_taken` vs `game_taken` 逐单位对账);
    · 万不得已要挂,Postfix **只声明 `IntPtr`/基元类型参数**(不声明 `BattleObject`,就不走对象池封送);
    · 钩子体里不许无条件调用游戏属性;需要游戏数据就**推迟到战斗结束**,
      用 `Session.OrderedActors` 自建的 `指针→名字` 表解析(`Aggregator.PtrOf` 只读托管字段);
    · 探测补丁一律**手动 Patch**(绝不进 `PatchAll`),并挂在 `Debug/TraceCandidates` 之类的开关上,
      这样出问题不必重新编译就能关。
12. **读 IL2CPP 的集合时:泛型枚举器不可用,但非泛型的是可用的。**
    两条实测(2026-10-03):
    · `TryCast<List<Il2CppSystem.ValueTuple<...>>>()` **永远失败** —— Il2CppInterop 把 `ValueTuple`
      建模成 **class** 包装,而原生集合是 `List<struct ValueTuple<...>>`,泛型类型检查匹配不上;
    · 泛型 `IEnumerator<T>` 的 `MoveNext` 确实没有暴露,但**非泛型 `System.Collections.IEnumerable`
      的 interop 类型是有 `GetEnumerator`/`MoveNext` 的**。所以正确姿势是
      `en.TryCast<Il2CppSystem.Collections.IEnumerable>()` → `GetEnumerator()` → `MoveNext()` →
      `Current`(装箱对象)→ 再按单个元素 `TryCast` 成目标类型。
    推论:**不要再假设"IL2CPP 的枚举永远读不了"**;先试非泛型那条路(`Composition/AbilityRoster.cs` 路线 A)。
13. **一个字段可能承载两个 id 空间,别假设它只属于它声明的那个枚举。**
    `TalentData.TalentType` 声明类型是 `TalentDefine.Type`,但实测里 `>=1000` 才是它,
    `1..999` 是 **`eBuffType`**(参数与状态)。判断依据是枚举成员的**值域**,
    不是字段的声明类型;两表都不认识就保留原值,绝不让猜错看起来像已解码
    (见 `Composition/TalentNames.cs` 与 `gen_talent_names.py`)。
14. **生成的 interop getter 对"枚举作为值类型字段"不可靠;要读枚举字段就自己读 int32 并验证布局。**
    实测(2026-10-03):`Il2CppSystem.ValueTuple<T1,T2,T3>.Item2` 走
    `PointerToValueGeneric<T2>(addr, isFieldPointer:true, ...)`,该 helper 用
    `il2cpp_class_is_valuetype(Il2CppClassPointerStore<T>.NativeClassPtr)` 决定装箱还是**当指针解引用**;
    对 interop 枚举这个类查找不成立 → 枚举字段被当指针解引用,返回垃圾值(实测全是 -1247486736),
    **而同结构体的引用类型字段 `Item1` 完全正常**。
    正确姿势:① `il2cpp_object_unbox` 取数据地址;② 用**能正常工作的字段**验证基址(指针相等);
    ③ 对候选偏移各读一个 int32,**取其中合法的那个**;④ 验证不了就**拒绝并计数**,不要返回兜底值。
15. **兜底显示值绝不能和真实分类同名。** 例:槽位读取失败时 `SlotLabel` 返回「未分类」,
    于是"读失败"与"确实无法归类"在界面上长得一模一样,导致排查时误判方向一整轮。
    合法值与兜底值必须在数据层就分开(1.1.2 的 `ValidSlot`),兜底只用于**展示**。
16. **ACTk(`ObscuredInt`/`ObscuredString`)的字段只能用游戏自己的解密读。**
    游戏带 Anti-Cheat Toolkit(metadata 里 `Obscured`×159、`ACTk`×102、`InjectionDetector`×2,
    interop 有 `ACTk.Runtime.dll`)。`ObscuredInt` 的结构是
    `{ int hiddenValue; int currentCryptoKey; int fakeValue; }`:
    `hiddenValue` 是 XOR 后的值(`fakeValue` 是**蜜罐** —— 检测到内存篡改后返回伪造值)。
    所以裸读这两个字段**不是"精度差一点",而是一个看起来合理的错值**。一律走
    `GameRef.Dec` / `GameRef.DecStr`。注意**同一个游戏里明文与混淆混用**:
    `EngravingMasterData` 是明文 `int/string`,而 `AbilityMasterData`/`EquipmentMasterData`
    是 `ObscuredInt/ObscuredString` —— 必须**逐字段**确认,不能按类型推断。
17. **读 `Dictionary<TKey,TValue>` 要取 ValueCollection,不要碰枚举器,也不要读键。**
    `Dictionary` 的 `GetEnumerator()` 返回的是**结构体包装**(`public sealed class Enumerator : ValueType`),
    其 `Current` 是装箱的 `KeyValuePair` —— 正是第 14 条那个"值类型字段被当指针解引用"的形状。
    而且 `Dictionary` 自身**没有** `CopyTo(Il2CppArrayBase<T>, int)`:那两个重载属于**嵌套的**
    `KeyCollection`/`ValueCollection`。正确姿势(1.2.0 起,`MasterData/MasterDataDump.cs`):
    ```csharp
    var arr = new Il2CppReferenceArray<TRow>(dict.Count);
    dict.Values.CopyTo(arr, 0);      // 一次批量原生复制
    ```
    **键根本不用读** —— 每个行对象自己带 `Key` 属性(且主数据的键类型并不统一:
    刻印/能力是 `int`,装备是 `string`(`item_id`),战斗定义是 `BattleDefine.Id` 枚举,
    刻印强化是 `ValueTuple<int,int>`)。同理字符串数组要用 `Il2CppStringArray`,
    因为 `Il2CppReferenceArray<T>` 要求 `T : Il2CppObjectBase`。
18. **不要在 IL2CPP 上给引擎的泛型方法套"游戏自己的类型"。**(1.2.0 实装时抓到)
    `Resources.FindObjectsOfTypeAll<T>()` **能编译**,但读它的 interop 实现会发现它走
    `MethodInfoStoreGeneric_FindObjectsOfTypeAll_...<T>.Pointer`,而那个 `Pointer` 是
    `il2cpp_method_get_from_reflection(...).MakeGenericMethod(...)` 建出来的。
    IL2CPP 是 **AOT**:镜像里**不存在** `FindObjectsOfTypeAll<EngravingMasterTable>` 这个实例化,
    所以它在**运行时**才会失败 —— **编译级验证看不见这一类问题**(1.2.0 的探针工程就漏过了它)。
    正确姿势:用**非泛型**重载 `Resources.FindObjectsOfTypeAll(Il2CppType.Of<T>())`
    (它是直接的原生方法指针),再对每个元素 `TryCast<T>()`。
    判据(两边差别是本质的):
    * 泛型**类型**:若它出现在游戏自己的元数据字段签名里,游戏必然用了它 → 镜像里存在 → **安全**。
      例:`Dictionary<int, EngravingMasterData>` 就是 `EngravingMasterTable.m_cache` 的类型。
    * 泛型**方法**套游戏类型:只有游戏自己**恰好调用过那个实例化**才存在 → **不可假设**。
    (同理别用 `Resources.ConvertObjects<T>()` 兜底,它也是 `MethodInfoStoreGeneric`。)
19. **一个"槽位"里可能混着多套重叠的 id 空间;而且主数据的 id 只有配上表名才有意义。**(1.2.2/1.2.3 实测)
    * 刻印槽:基础刻印 id **1..14**,刻印变异 id **1..28** —— **重叠**。只按 id 查母表会得到
      **自信的错答案**(例:导出 id=14 其实是变异 14「攻撃速度+30%、5秒」,而基础刻印 14 是
      「貫通率+4% 会心率+3%」)。判别键是 **(id, talent 签名)**;基础刻印 5 个等级签名相同,
      但**名字也相同**,所以再用"候选名字是否唯一"收尾。**参数不能当键**(母表 `param[[10]]`
      与导出已缩放的 `p=[30,0,0]` 不可比)——它只能用于**人工确认**行属于哪个空间。
      相关:`Composition/AbilityRoster.cs`、`MasterData/MasterDataNames.cs`。
    * 实测的重叠**不止两套**:`id=84` 同时被 **神器** 与 **潜在** 占用;`id=14` 横跨
      artifact / engraving / engraving_ability / engraving_mutate / engraving_reinforce(engravingId) / potential **六张表**。
      故铁律是:**主数据的 id 必须配上表名(常常还要配上槽位)才可比较。**
    * 主数据连接:**一律按 id,不按名字**。按名字连接踩过 —— 神器 `炎の剣` 有 grade1/2/3 三行同名字,
      取到 grade1(+4%)后就误以为"母表与导出矛盾",按 id 连接(导出 id=78 → grade3 → +12%)完全一致。
      反例也要记住:`id=84` 证明**光有 id 也不够**,必须带表名。
    * 推论:**"看起来矛盾"优先怀疑自己的连接键,而不是数据。**
    * 现场实证(1.2.2 导出):同一个 `(id=1, level=1)` 同时出现为【破壊】の刻印(talent 6)
      与 魔法与ダメージ+10%(talent 1005),两个都被**正确**命名 —— 因为签名不同。
    * **精确化(压力测试代理实测)**:**(id, 签名) 层面零碰撞** —— 同 id 的基础刻印签名与变异签名从不相撞。
      规则能工作正是因为签名;裸 id 才重叠。
    * **已知边界(潜在错标,共 3 处,723 个导出 0 例)**:同签名落在**不同 id** 上 ——
      mutate 8 / mutate 14 的签名 `26` 与 base 10【撃速】相同;mutate 9 的签名 `21` 与 base 11【飛脚】相同。
      若游戏把某条变异挂到别的 id 下(如 `{id:10,type:26,p:[5,300]}`),规则会只剩一个候选并
      **给出自信的错名**。目前唯一屏障是"游戏把变异挂在自己的 id 下"(152 行的强推断,非证明)。
      另:mutate 7 与 mutate 26 签名同为 `1004,1006`,只靠 id 分开。
      **待补守卫**:当 (id,签名) 只有一个名字、但该签名在别的 id 下存在不同名字时,
      追加"live 参数能否被某个候选的等级参数解释"的检查(master `param` 与 live `p` 仅在单天赋行可比,
      且 live 取的是**等级上限值** —— 注意导出里刻印行的 `level` 字段**恒为 1,不可信**)。

20. **"配对对不对"和"算术对不对"是两个问题,永远不要用一句话同时回答。**(1.3.0 实测)
    * 旧标注 `本次伤害与该次计算值不符(近似构成)` 想表达"配对未被结算对象佐证",但**98.6% 的
      算术完全对上**的行也带着它 → 使用者会把精确的行读成不可信。1.3.0 起:
      措辞改为 `配对未获结算对象佐证`,并把三件事分别落成字段 ——
      `pair`(走哪条配对路线)、`pairTrusted`(身份是否同一)、`pairCorroborated`(游戏记录是否印证这一击)、
      `valueMatches`(算术是否复现游戏数字)。
    * **扫描式判定不要在第一条命中上提前返回。** `CalcValueMatches` 原先在"指针相同的第一条记录"上
      `return false`,而一次 AoE = 一个 calc + 每目标一条记录(+ DamageAction/ActDamageAction 各一条),
      于是检查几乎恒假(**实测 2,458/2,458**),这才是历史上 99.5% 标注率的真因。
      配对键必须含**受击者**,并且要扫完所有同键记录。
    * 教训:**"标注率异常高"要当成 bug 查,不要当成机制特征写进文档。**

21. **KPI 必须由"它汇总的那些行"算出来,并且把口径写进数据里。**(1.3.0)
    * `reconcile` 块在**导出时**从事件流计算,不使用战斗中累加的计数器 —— 两处各算一遍正是
      35.8% / 25.4% 两个不可比数字并存的原因。
    * `approxTolerance` / `theoryExceedsResidualMax` 随数据一起导出,换个人重算不会漂。
    * **复现分类要用精确整数,不要用印出来的小数字**:`residual` 以 F3 导出,而分类用的是未取整的
      `applied/theory` —— 实测有一条 0.0996 被印成 `0.100`,于是用 `residual` 复算得到 313、
      插件报 314。(整数 `applied` / `theory` 都在导出里,直接相除即可。)
    * **对账率与场次长度强相关**(同任务 411001:n<200 → 94.3%,n≥3000 → 33.7%),所以
      **单场 A/B 不能用来比较版本**;任何准确率声明必须带 `任务 id + 场次数 + 事件数 + 口径`。

22. **不要把一个"看起来像失败"的统计直接命名为失败。**(1.3.0 首场实机,当天就吃亏一次)
    * 剩余倍率 < 0.1 被命名为 `mispaired`(疑似配对错位)并写进版本说明;
      **第一场实机**证明它 100% 是任务 9999 训练场两座建筑(城塞 / T.O.W.E.R.typeR)上
      一个约 `×0.03` 的**未建模减伤** —— 数字对,名字错。已改名 `theoryExceeds`。
    * 判别方法(照此做):一个异常统计先按 **任务 / 受击者 / 攻击者 / 时间 / 理论值大小** 分解,
      再给它命名。`theoryExceeds` 高 + 集中在少数受击者 + 理论值并不异常大 ⇒ **未建模机制**;
      分散在所有受击者与所有配对路线上 ⇒ 才可能是配对问题。
    * 同源病史:显示标签参与数据匹配、fallback 名与真实类别同名、版本戳撒谎 —— 都是**名字与事实不符**
      这一类。**命名是契约,不是注释。**

23. **一条规则加在"谁"身上,要用"谁的数字动了"来验,不要用文案的自然语感来判。**(1.3.5)
    * 「ブロックしている敵の被ダメージ+10%(前衛のみ)」(`刻印变异 id=26` =
      `{1004 AddTalent, timing=Passive}` + `{1006 被伤害-, timing=Block, p=[-10]}`)读起来像持有者的加成,
      实际是持有者把 `被伤害-10` **赋予被它格挡的敌人**,于是对**所有来源**的伤害生效。
    * **决定性证据来自持有者自己那一边**:持有者 53/53 条命中残留恰为 `0.826 = 1/1.21`,
      逐条 `6142 × 1.44 = 8844.48` 对上游戏 `8844` —— 游戏没给它这 ×1.21,而同一窗口在**另外四个
      攻击者**身上缺失 1791 条。**一处多算、四处少算,是同一个归属错误的两面。**
    * 判别法:**看这一击的倍率是否随"每个瞬间"整齐开关**。同一 `(攻击者,时刻)` 内的全部命中同族,
      且**多个攻击者的开关时刻完全一致** ⇒ 状态属于**受击方/全局**,不可能是攻击者自身。反之
      只有某一个人的命中带某因子 ⇒ 先怀疑他自身。
    * 实现侧:`GlobalRules.cs` 里"含「ブロック」就跳过"这类**无法判定的条件**不要就地猜,
      要找到游戏自己记录该状态的字段(这里是受击方 `m_giveTalentData`)。**猜条件 = 定期产生错误归属。**
    * 该通道只在**受击方**读:字段 `GiveTalentData.give` 的语义(生效标志 vs 方向标志)在反编译的
      interop 里看不到逻辑,只读受击方则在两种解释下都正确 —— 并把 `giveFlagTrue/False` 直方图导出,
      让数据而不是注释来定案。

24. **读一条新通道之前,先问"组合里还有没有别的路径在描述同一件事"。**(1.3.5→1.3.6 的代价)
    * 1.3.5 读受击方 `m_giveTalentData` 是**对的**(刻印 id=26 的 ×1.10² 就是靠它才对上),
      但没排除"同一条规则已被 comp2 的「全局:敌方受伤」路径计入" → `母なる変異の飛沫(×3)` 与
      `海魔の残滓(×2)` 被计两次,**一场战斗 63.9% 的命中落到 `1/1.15^k`**,精确率 32.5% → 1.5%。
      只验证了"通道能读到",没验证"是否已被计入"。
    * 修法不是关掉通道,而是让两条路径**对账**:全局规则路径报出它这一击已计入的因子
      (按满足状态数**展开成多重集**,因为赋予清单也是每状态一条),赋予清单里同值的条目消费式抵消。
    * 判据写作:`giveCancelled` 应为数千量级 + 那四类 `1/1.15^k` 残留应基本消失 + `exact` 回到 30%+。
    * 同类风险的通用形态:**同一个事实有两条读取路径时,必须显式决定"谁拥有它"**,否则默认就是双计。

25. **一个"读得到、但一动不动"的字段,要么是模板,要么不是这条路径 —— 用时间序列而不是单点判断。**(1.3.9)
    * 1.3.7 把 `BattleObject.Data.m_statusResistance` 当作 Boss 的异常抗性报了出去(Boss 11 槽=100、
      我方 18 槽=0),但它与观测直接矛盾:wiki 写「耐性の数値は割合で計算される」,
      `毒耐性=100` 意味着毒永远挂不上,而 Boss 明确吃到毒/火傷/凍結。
    * 单点读数无法判定"值不对"还是"读错字段"。**1.3.8 的做法是可复用的**:改成每 500ms 重采样,
      于是 952 次采样 / 12 单位 / 119 秒 / **18 个槽零变化**,而这期间 5,120 条 `毒耐性-30` 正挂在同一单位上
      → 一句话定案:**该字段是单位模板的静态值,不带战斗中的修正**。
    * 教训写进命名:`resistSample` → **`resistMasterSample`**。**读错字段时,字段名也要跟着改**,
      否则下一个人会继续用那张错表。
    * 生效侧要找的是"施加到单位身上的属性修正"结构(`m_statusSubParams` + `BuffCharacterStatusSubParam`),
      而不是模板上的那一份。

26. **读不到的标志位,可以分类,不可以预测。**(1.3.9)
    * 会心倍率完全确定(`会心伤害率/100`),但游戏的会心**标志**读不到。于是:不改 `theory`,
      另立 `exactWithCrit` 分类,并让 `exact` **保持严格**。两个数各有其名,谁也不会被误当成对方。
    * 分类必须带**可证伪的附加约束**:会心率 0 时不可能发生会心,所以 `CritRate > 0` 是硬条件。
      这条约束由离线探针**双向**钉住(正例计入、反例不计),写这条时探针**先拦下了一个真错**。

27. **"恰好相等"几乎总是错的判定 —— 被比较的那个数字是取整过的。**(1.3.10)
    * 会心分类最初写成 `(long)(Theory × 倍率) == 游戏值`。实测:残留形如 1.9 的 720 条里,
      **720/720 都在 0.1% 以内**(中位 0.002%,最大 0.082%),但**恰好整数相等的只有 288 条** ——
      游戏给出的伤害数字是取整的,于是"判定成立"被砍掉六成。
    * 规则:**只要一边是游戏打印/累加过的整数,比较就必须用相对容差**,并且把容差随数据导出
      (`critInferredTolerance`),否则换个人重算会得到另一个数。
    * 容差要**由数据反推、并报告它与最近竞争档位的距离**(这里 0.2% vs 竞争的 13%),而不是拍一个"看起来安全"的值。

28. **在含非 ASCII 的文件上,永远不要用 PowerShell 整文件读写 —— 这条规则已经写在文档里,仍然踩了。**(1.3.10)
    * `(Get-Content -Raw) -replace … | Set-Content -NoNewline` 把 `recon_probe/Program.cs`
      (含中文)按 ANSI 写回 → **非法 UTF-8 且静默丢了一行**。
    * **恢复手段比备份更可靠的是构建产物**:从上次成功构建的 `ReconProbe.dll` 反编译即可拿回
      精确的字符串与全部数值字面量(IL 不丢信息)。坏文件先改名留证,且**不要以 `.cs` 结尾**(否则会被编译)。
    * **守卫必须覆盖所有含非 ASCII 的源码目录**:`refactor_final_check.py` 当时只 walk `src/`,
      `recon_probe/` 从未被扫,所以损坏时它一声没响(被发现的唯一原因是探针输出变了)。
      现在它扫 `src`/`recon_probe`/`test`,并单列 `not valid UTF-8`。
    * 通用形态:**"我早就知道这条规则"不构成防护** —— 防护是守卫脚本覆盖到那个目录。

29. **"字段不动"不是"读错时机",通常是你读的那个类根本不是宿主 —— 去读继承链,不要再多读一次。**(1.4.0)
    * 1.3.7–1.3.10 换了四个候选(`CharacterDataBase.m_statusResistance`、`m_statusSubParams`、
      `m_nowBuffParamDataDictionary`、状态时间线),每个都是"再读一遍看看"。全部为空或恒定。
    * 1.4.0 换成**先把 API 反编译成可检索的源码树**(`ilspycmd -p -o api_full Assembly-CSharp.dll`,806 个文件),
      再顺着 `Character : BattleObject` → `.Status : CharaStatus` → `.Resistance : CharacterStatusResistance`
      找到**真正的宿主**,并且找到了改写它的那个方法(`Character.ApplyStatusResistanceBuff`)。
      同一个类里还有游戏自己的换算 `CalcValue(Type, value)` 与全局阈值 `AilmentCommonThreshold`。
    * 结论分两层:**模板值留在 `CharacterDataBase`,活值在 `CharaStatus`**。两者的差别不是时间,是**属主**。
    * 通用形态:一个"读得到但恒定"的字段,第一反应应该是"谁在改它?"。**找到写它的方法,比再采样一百次更快。**
    * 附带:IL2CPP 的 interop 程序集里**没有方法体**(纯 stub),所以"从反编译里读出游戏算法"这条路是死的;
      能读的只有**类型/成员/签名/继承**。把这一点记住,可以省掉一整轮尝试。

30. **一个假设要一场实战的探针流程,是流程缺陷,不是技术缺陷。**(1.4.0)
    * 1.3.7→1.3.10 每版只加一个探针 → 每个假设一场实战。用户提出"一次把探针全部上完",这是**对流程的批评**。
    * 正确形状:**一次把所有还在等数据的通道全部打开**,并且**同时交付一个把导出翻译成结论的离线脚本**。
      探针只负责"把当时活着的状态留下";把"这些数字说明什么"留给脚本,这样新问题不需要新战斗。
    * 与之配套的两条纪律:(a) 每个通道都要有**自陈计数**(`seen/kept/overflow/stateUnreadable/errors`),
      否则"没有异常"与"通道静默失败"看起来一样;(b) 判据必须**复用同一份代码**
      (`CalcReconcile.IsExplained` 同时被 KPI 与取证通道调用),否则工作清单和 KPI 会各说各话。
    * 采样必须发生在**对象还活着的时候**:命中后的对象活不到导出时刻,所以取证挂在命中路径上,
      而不是导出路径上。

31. **计数只是必要条件,而且会掩盖判据的错误 —— 定期读数,并在判据可疑时把原始证据一起导出。**(1.4.0)
    * `layoutAmbiguous` 被计数了 92–194/场,从没人读。真读一次发现:**它恒等于 `ptrHits+idHits`**,
      也就是说它对 100% 的元组都触发,度量的是"读到了多少个能力",不是"布局有歧义"。
      原因很朴素:0..11 的**索引**与槽位枚举无法区分。
    * 于是本版不只加读数,还把**原始证据**(`layoutSample` 的 w8/w12 对、`layoutSlots` 直方图)一起导出,
      让这个判断可以被别人复核,而不是靠一句注释。
    * 同类:`cJoins` 恒为 0 且**永远不会**非 0(join 键把两个 id 空间混了),
      处理方式是**删掉计数和那条路由**,而不是留着它继续"看起来像个正常的零"。
      **给已删代码保留一个恒零计数器,比没有计数器更糟。**
    * 对照:`statusAudit.explained = 0` 一开始也像"功能坏了",读数后发现是**口径差**
      (审计比较 `BuffValue` 之和,而 comp4 只读状态名)→ 于是加的不是修代码,而是 `statusValueSample`。

32. **`edit` 工具的 `old_string` 若以换行结尾,会把下一行"焊"上来 —— 改完立刻编译。**(1.4.0)
    * 一次把文档注释与 `private static void TakeTuple(` 合成了一行(声明被注释吞掉),
      报错 54 条语法错误,而**根因只是一行**。此前同类事故是 PowerShell 写坏 UTF-8。
    * 纪律:任何源码改动,**改完立刻 `dotnet build`**;不要攒到最后一起编译。
      编译通过是"没有把文件写坏"的唯一廉价证明,守卫脚本(`refactor_final_check.py`)只查编码不查语法。

33. **改 `Config.Bind` 这种"一行一条、参数极长"的语句,替换后必须回读 —— 编译通过不代表没写坏。**(1.4.1)
    * 用 `edit` 替换 `Plugin.cs` 里一行 `Config.Bind` 的开头时,替换串吞掉了**下一行**的开头半句
      (`CfgForensics = Config.Bind<bool>("Debug","Forensics",true,`),于是后一行的长字符串
      变成了前一条语句的延续。**这仍然是一段合法的 C#,编译 0 错误 0 警告**,`CfgForensics` 却永远不再被赋值,
      依赖它的整条取证通道会静默关闭。
    * 这是本项目最贵的一类 bug:**合法的代码 + 静默失效 + 编译器和守卫都不响**。
      发现方式只能是**替换后回读那一段**,或让"关键开关是否存在"变成可检查的断言。
    * 通用形态:凡是"删掉/改坏它不会报错,只会少一个功能"的东西(开关绑定、字段赋值、注册调用),
      改完都要回读。**规则 32 说"改完立刻编译";这条补充:编译过了也还要回读高风险的那几行。**

34. **两点采样看不见"会到期又恢复"的东西 —— 要区间,不要端点。**(1.4.1)
    * 1.4.0 的抗性探针只留"首见"与"最后一次":Boss 在报告里显示"本场未变化"(18 槽全 100),
      而**同一份导出**的逐条取证样本在 t=3.03s 明写着 毒=25 / 火傷=55。两个读数都对 ——
      抵抗减益会**到期**,首尾都回到模板值。
    * 这是本项目第三次犯"单点读数":1.3.8 的教训是"再多采几次",本条的教训更进一步 ——
      **当被测量的东西可能有"去而复返"的形状时,采样次数不是解药,统计量(区间/最值)才是。**
    * 实现上,修法是 `liveResistRange`(逐槽 min..max);**空区间是结果(真的没动),不是失败**,
      所以它不能和"读不到"共用同一个空值。

35. **同值不同源的规则,必须在数据里可分 —— provenance 不是注释。**(1.5.0)
    * 1.5.0 之前,每击折叠 2–5 个因子(实测直方图 0:19 / 1:46 / 2:1111 / 3:2660 / 4:1558 / 5:107),
      84 种攻方因子标签全部乘进 `DealtMult`/`TakenMult` **两个 double**;责任集是
      `Dictionary<double,int>` 且**存的计数从不被读**(7 个使用点逐条核对)。
      于是「这一击的 1.15 是少折还是多折、是哪条规则折的」在数据结构层面不可判定。
    * 铁律:每一条进入算式的因子,都要在 `FoldContext` 里留下 `{Side,Kind,Origin,Factor,Label}`,
      **`Origin` 必须由游戏 id 构造**(能力序号/能力 id/天赋序号/授予条目号/持有者指针),
      **永远不要用显示文本当身份**。
    * 推论:新增任何折叠点,不记录 origin 就不算完成 —— 否则下一场又只能靠正则猜。

36. **折叠路径上的"责任集/取消决策"只能经显式参数传递,禁止跨调用的静态状态。**(1.5.0)
    * 旧的静态 `_enemyCandidates` 会被**任何** `ApplyGlobalDebuffs` 调用覆盖 —— 包括重算算式的诊断路径,
      于是取消决策取决于调用顺序而不是数据。现在它在每击的 `FoldContext` 里。
    * ⚠ **决策规则本身不许凭直觉"改进"**:`GlobalRules` 自己的 1.3.8 注释记录了
      "每触发一份消费一份"的多重集写法**实测失败**(残留 1.15²…1.15³ 约 1700 条)。
      原因写在注释里:责任集是**按规则**记录的,而授予表是**按状态副本**存的,两者基数不同。
      1.5.0 因此只**记录证据**(`cancel[].by` + `responsibility` + `maxAbsorbed`),不改算法。

37. **没有生产者就不算接线 —— 通道要自报产出/匹配/未匹配。**(1.5.0)
    * `RecordHitDetail` 曾是全仓唯一、**没有调用者**的定义:`PendingHits` 恒空、`ConsumePending` 恒 null,
      全语料 570,078 条事件里 `crit` 恒 false、伤害 `source` 恒 0 —— **而没有任何一处说它坏了**。
    * 铁律:一条数据通道必须有 `produced / matched / unmatched / errors` 这样的计数并**进导出**;
      "字段一直是常量"与"字段没被测量"必须能从数据里区分。
    * 匹配要**按值 + 时限**(`ConsumePending` 用伤害值优先、0.35 s 上限),并把匹配强度导出(`hitMatch`),
      免得"尽力猜的配对"被读成测量。

38. **命中时刻是事实的唯一保存窗口;事实必须当场落盘,并且要能覆盖 100% 而不是抽样。**(1.5.0)
    * `FinalizeLocked` 导出后立刻 `ActorStats.Source = null`,活体对象随即消失 —— 任何
      "导出时再回游戏读一下"的方案都不成立。`Forensics`(深)与 `FactStore`(广)是同一个理由的两种粒度。
    * 抽样不是省钱,是**把问题推给下一场战斗**:1.4.0 只留 160/5501(3%),于是每提一个新问题就要再打一场。
      实测这 5501 击只塌成 **331–463** 个不同事实类 ⇒ **去重后同样预算可以覆盖 100%**。
    * 铁律:凡是"只能在战斗中读"的量,先想它属于哪个事实类,而不是"这个探针要不要开"。

39. **手写 JSON 必须自检,因为编译器看不见它。**(1.5.0)
    * `Output/JsonCheck.cs` 在写盘前扫成品:字符串外括号平衡 + 根键重复计数,结果进 `[JSON]` 行。
      它自己也被离线探针**反向验证**(能接受合法,能拒绝四种畸形)—— 没被证明能拒绝东西的校验器不算校验器。
    * 本版实际踩到:两处 `edit` 把两条语句并成一行(rule 32 的同型事故),以及 `timeline` 一度
      **重复发出 `rows` 键**(JSON 能解析但静默丢数据)。

---

## 5. 扩展配方

### 5.1 加一条新的"与/被ダメージ補正"写法
1. `Composition/Rules.cs` 的 `JudgeClause` 里看关键词/门控是否已覆盖;不够就扩展。
2. 数值解析:`ParseDamageModifier` 负责 `%` 形式,并按数字前后的词判方向
   (减:`軽減/カット/ダウン/低下/減少/抑制/ダメージ-`;增:`アップ/上昇/増加/増/ダメージ+`);
   `N倍` 形式与耐久档位表(`9/6/3割以下 → それぞれ-10/-25/-40%`)交给
   `TieredModifier.TryParse` / `TryEvaluate`(取**满足条件里最小的那一档**)。
3. 新条件(位置/属性/状态/耐久)分别在 `JudgeClause` 的位置门控、`TargetAttributeMatches`、
   `EvalConditions` + `StatusTokens` 里加。
4. 验证:`AbilityDump=true` 打一场 → `[ABIL]` 行会打印这条子句的裁决与理由;
   再看导出 JSON 里对应事件的 `剩余倍率` 是否收敛到接近 1.000。

### 5.2 加一条"全队/全体敌人"规则
`Composition/GlobalRules.cs` 的 `RegisterGlobalDebuffs`:解析出 `GlobalRule`(owner、条件、
倍率、`EnemyTakes` 方向、物理/魔法限制、前衛/後衛)。**只有「全て/すべて」类子句才进全局**;
带 `ブロック`/耐久条件的仍按局部处理。
> 注意:全局规则是"命中时追加"的,`Log()` 那条诊断路径也要应用同一套附加倍率
> (1.0.28 的教训),新增来源时两处都要过 `ApplyGlobalDebuffs`。

### 5.3 加天赋类倍率
`Composition/Talents.cs` 的 `TalentDamage`(`victimSide` 决定取 DamageCut 还是 DamageUp)。
能力文本里**没有**「ダメージ」字样的能力才交给它,避免与文本解析重复计入。

### 5.4 加一个新的 Harmony 补丁
放到 `Hooks/` 下按**被补丁的游戏类型**命名的文件里。两条纪律:
* 补丁体全部包在 `try/catch` 里(补丁异常绝不能打断游戏);
* 诊断类补丁**不要**用 `[HarmonyPatch]` 属性走 `PatchAll`——`PatchAll` 只要有一个目标解析失败
  就会整体放弃。参照 `Plugin.TryPatchCritProbe` 手动补。

### 5.5 加悬浮窗一行/一列
* 数据 → `Ui/OverlayUGUI.Rows.cs`(在对应 producer 里 `rows.Add(new RowDef{...})`);
* 位置/尺寸 → `Ui/OverlayUGUI.Pool.cs` 的 `Layout`/`EnsurePool`;
* 曲线 → `Ui/OverlayUGUI.Chart.cs` + `OverlayChart.cs`。
行是**池化复用**的,刷新时不要 destroy/recreate。

### 5.6 加一个诊断开关
1. 在 `Plugin.cs` 里 `Config.Bind<bool>("Debug", ...)`;
2. 输出统一走 `RuntimeLog.Write(...)`(轮转、不刷屏 `LogOutput.log`),需要进 BepInEx 日志再叠
   `Plugin.LogSource.LogInfo`;
3. 行首用方括号标签(`[ABIL]`/`[COMP]`/`[TIME]`/`[CROSS]`/`[PROBE]`/`[RULE]`/`[UI-DIAG]`),便于 grep。

### 5.7 大文件继续拆分
用 `refactor_split_composition.py` 的模式:定义"目标文件 → 原文件行区间",脚本负责搬家并证明
"非空行恰好出现一次",随后构建 + 反编译多重集比对。**机械搬运和语义改动不要放在同一步**。

---

## 6. 排障工具箱

| 开关(`BepInEx\config\dev.dpsmeter.cfg`) | 作用 |
|---|---|
| `Debug/AbilityDump` | `[ABIL]`:每个单位一次,打印原始技能文本 + 每条子句的裁决与理由;`[TIME]` 行也靠它触发 |
| `Debug/DamageComposition` | `[COMP]`:每次命中的算式/已识别倍率/剩余倍率/全局规则来源 |
| `Debug/TraceCandidates` | `[PROBE]`:候选伤害路径计数 + 结算时的归属缺口列表 |
| `Debug/VerboseEvents` | 每次伤害/治疗一行(仅验证用,平时关) |
| `Debug/SlotProbe` | `[SLOT]`:每场结束一次,打印各单位的能力**来源槽位**、每条素质的**触发计数**、结构化**触发条件**(`eTalentCondType`)、周期计时器,并实测计数读取成本。只读、零新补丁。1.1 改造的可行性探针 |
| `General/AbilityRoster` | 1.1.0 功能:名册与**出处**(刻印/神器/觉醒/职业…),进导出与明细页。只读 |
| `General/TalentTriggers` | 1.1.0 功能:逐条命中的**素质/词条触发**(游戏自己的计数器)+ 本场素质表。只读;若成为帧时间负担,关掉即可,其余功能不受影响 |
| `Debug/RosterRouteA` | **逃生开关**:名册是否调用 `GetAbilityDetailDataList()`(唯一被调用的游戏方法)。置 `false` 即降级到统计表连接路线,无需重新编译 |
| `General/MasterDataDump` | 1.2.0 功能:每进程一次(战斗结束时)**导出游戏自己的主数据表**到 `BepInEx\plugins\DpsMeter\masterdata\*.json`,外加游戏自己的表名清单 `_table_registry.json`。只读;自检行 `[MASTER]`。**数据只在内存里**(服务器下发 JSON,从不落盘),解包安装目录拿不到 |
| `General/TimerUsesGameTime` | 战斗计时基准切换(见第 2 节) |

日志与产物:
* `BepInEx\config\dpsmeter_runtime.log` —— 游戏重启即轮转,主要排障入口;
* `BepInEx\LogOutput.log` —— 含启动横幅 `DpsMeter 1.0.29 loaded`;
* `BepInEx\plugins\DpsMeter\exports\battle_*.json` —— 整局事件流(每击 `comp1..comp4`、
  理论/实际/剩余倍率),离线分析的**唯一权威数据**;
* `_dpsm_work\verify_*.py`、`analyze*.py` —— 针对历史问题的验证脚本(档位、格挡、毒顺序、
  耐久档位、时钟、全局规则标记等),沿用它们的结论前先看 `SESSION-STATE.md` 对应小节。

**定位"剩余倍率不收敛"的标准流程**:先看导出的 `reconcile.topResidual`(1.3.0 起 **逐场自带**,
不必再写离线脚本)—— 它按出现次数给出本场缺哪几个倍率 → 在 `events` 里按 `calc.residual` 筛出该值的事件
→ 看 `calc` 的 `pair*` / `valueMatches` 先判断这是**配对问题**还是**算术问题**
→ 是算术问题就按 (受害者, 剩余倍率) 聚类 → 找同一时刻的 buff/状态/技能 → 回 `[ABIL]` 看那条子句为什么被
`Conditional/NotParsed` → 改 `Rules.cs` → 重跑同一段导出验证。
**改完必须报 `[RECON]` 行里精确率的**变化**——而且要在同一任务、场次数与事件数量级相当的前提下比**
(对账率与场次长度强相关,见第 4 节第 21 条)。

**判断"某个数值异常是不是机制"**:先数它的出现率。1.3.0 的经验是
**标注率/失败率异常高(>95%)几乎总是自己的代码错**(`CalcValueMatches` 提前 return 那次),
而不是游戏有多古怪;异常低(单场 0/2,458)才是机制特征。

---

## 6b. 加一个"结构化孪生键"的配方(1.5.0 B2,已用过 8 次)

一个量只要以**分隔符拼接的字符串**出现,每个需要它的脚本就得自己写一遍解析 —— 1.4.x 的语料里
同一类正则被复制了 21 份。1.5.0 起新增量的规则是:**先给结构化形态,字符串只作为人类可读的附属**。
对既有字符串(`*Sample` 那批)的做法固定为四步,已用于 `giveTypes` / `resistMaster` / `statusValues` /
`layout` / `liveRange` / `subParams` / `params` / `statsRows`:

1. **在产读的地方顺手存数值** —— 不要把字符串再解析一遍。能存 `int[]` / `Dictionary<string,int[]>`
   就存;读失败的槽用 `int.MinValue` 当哨兵,**导出成 `null` 而不是 0**(哨兵不能和真实 0 混)。
   ⚠ 若数据源是**活体且随时在变**的(技能文本、buff 表),必须让**同一次遍历**同时产出文本与数值 ——
   两次遍历会描述两个瞬间(`params` 就是这样做的);这一点和 comp4 与其状态列表同源是同一个理由。
2. 新增 `Append*Json(StringBuilder)`,只读那份数值状态。**图例一起导出**
   (`slots:[名]` / `statusFlags:[名]`),否则下标又要靠猜;计数(`reads`/`errors`)一起进,
   免得"没读到"被读成"没有"。
3. **旧键保留**。768 份历史导出只有字符串形态,删键等于让所有历史脚本失效(1.4.0 删 `cJoins` 已经
   让引用它的脚本全部要改一次)。新键作为**根级**键发出,而不是塞进已经很大的 `rosterAudit`。
4. 在 `CompositionProbe.Reset()` / `AbilityRoster.Reset()` 里**清掉新状态** —— 忘了清就会让
   上一场的数字和这一场的字符串并排出现在同一份导出里。

⚠ **这条路径无法离线验证**:`ExportService.BuildJson` 依赖 IL2CPP 类型,`recon_probe` 编译不了它。
所以新键的 JSON 正确性只能靠实机那一行 `[DpsMeter][JSON] 结构=OK`(见第 4 节第 39 条);
在它变绿之前,不要把这些键记成"已验证"。

**但"这个键合不合适"是可以离线验证的,而且必须离线验证。** 1.5.1 和 1.5.2 连着两次把
`FactStore` 的签名改错,两次都只能靠"让用户再打一场"才发现(1.5.1:1485 类 > 600 上限;
1.5.2:其中 1232 类来自逐实例的 `vicKey`)。签名是**纯函数**,随时可以拿磁盘上已有的导出重放:

    python check_fact_signature.py [export.json]     # 类数 / 先到先得覆盖率 / 是否饿死 / --selftest

发版前跑一次,`overflow=0` 才算过。**下一次改签名时先跑这个,再让用户打。**

40. **规则的"持有者跳过"必须只跳过乘积,不能跳过责任登记。**(1.5.1 实机)
    * 全局规则通道对**持有者本人**的命中会 `continue`(否则它自己的技能文本扫描已经折过一次,
      再加就是自己重复)。**但同一条规则还会以「赋予天赋」的形式送到受击方**,而授予通道正是靠
      "责任集"来抵消这些副本的。责任集一空,副本就被折了第二遍。
    * 实测(battle_411001_20261003_173710):这条规则的持有者 **マッドシーカー** 同时是本场最大攻击手
      (1800/5272 击),它自己的 1800 击里 `responsibility` 全为 0、折了 4240 步 `given 1.15`,
      残差集中在 1/1.15²·1/1.15³ 及其会心变体,`exactWithCrit` 从 81.7% 掉到 **50.1%**。
    * 推论:**"持有者跳过"与"跨单位赋予"是同一个规则的两条通路,任何一条的登记缺失都会变成 KPI 上的
      1.15^k**。新增任何"跳过"分支时,必须问一句:这条规则会不会从另一条通路回来?
    * 附带教训:**计数函数被诊断路径复用时必须能"只算不记"**。1.5.0 让诊断重算调用了
      `GivenTalentDamage`,把 `[GIVE]`/`giveTypes` 记了两遍;现以 `measure` 参数隔离。

41. **去重表的"身份"必须是**种类的**身份,不是实例的身份;上限只是第二个问题。**(1.5.2 实机)
    * `facts.items` 的签名里放了 `vicKey` —— 一个**逐实例**的 actor key。一场 5055 击的战斗里受害方是
      **187 个实例 / 3 个单位种类**(ショゴス 5040 击 + 两个角色),1485 个签名里 **1232 个只来自这个 key**。
      而规则取决于受害者的**种类与即时状态**,"这是第几只ショゴス"不是任何一个规则的输入。
      换成 单位名+阵营 后同一场数据:**1485 → 240 类,覆盖率 36.6% → 100%**。
    * 铁律:给去重表选 key 时问"**这真的是一个规则输入吗**";回答不了就别放。实例指针、递增 id、
      活体计数、实时血量都属于"看着像身份、其实不是"的一类。
    * **上限只是第二个问题**。1.5.1 把签名从 5177 压到 1485(3.5×,确实有效),但 1485 > 600,
      所以覆盖率仍是 36.6%。更隐蔽的是:**这张表先到先得** —— 它留住最早遇到的 600 个签名,只服务
      2017 次;若按最大的 600 个类来留能服务 3944 次。**一个早出现的稀有签名会永久占掉一个名额。**
    * ⚠ **`类溢出` 不是"新签名数"**:它数的是"签名是新的、但表满没位置"的**命中次数**,
      同一个溢出签名会被重复计数。把 1.5.1 的 `类=600 溢出=3203` 读成"3803 个不同签名"是错的;
      真正的判据是**把签名离线重放到导出的事件流上** —— 现在有工具:
      `_dpsm_work/check_fact_signature.py`(带 `--selftest`,并且刻意复刻 FactStore 的
      **先到先得**填充规则,同时给出"按大小挑"的上界)。
    * ⚠ **重放的"服务次数"与插件的计数不应相等,别拿它当校验和**:同一场上重放得 2136,
      插件自己的表记了 2017 —— 因为插件被喂了 **5220** 次 `Observe`(比导出多 ~165 次),
      多出来的早期类会占掉名额,后面的导出事件就落空。**可以下判决的只有 `distinct`**
      (它只取决于 key),服务次数同时取决于 key 和那个计数缺口。
    * ✅ **那个计数缺口已定位(1.5.2 实机 183213)**:1.5.1 那一场的事件流从 `t=7.530s` 才开始
      (近 14 份导出里唯一一份 `min_t != 0.000`),开头 7.53s 的事件不在导出里;而 `Observe` 是
      static 计数器,不随 `Events` 清空归零,所以照数。1.5.2 同场缺口为 0。**实机结果:271 类 /
      服务 5582/5582 / 覆盖率 100%,上限 2400(余量 2129)。**

42. **一个"判决"字段如果没把判据一起导出,它就无法被审计也无法被否定。**(1.5.2 实机)
    * `hitMatch` 是判决(1=值对上 / 2=只对上人)。实测 5055 个事件**全部**是 2、
      `匹配精确=39`;可同一批事件里独立算出的 `calc.theory==amount` 有 **3270** 条。
      两个数字同时为真只可能因为:**被比较的那个量(`__result`)从来没被导出过**。
    * 1.5.1 因此"修"了一次(`nominal` 也接受),方向对不上却无法被证否 —— 因为判据不可见。
      1.5.2 不再猜,直接把 `hitValue` 导出来,让下一场**量**出 `__result` 与 `amount`/`nominal` 的关系。
    * 铁律:凡是导出 0/1/2 这种**分类判决**的地方,同一条记录里必须有足以复现该判决的原始量;
      否则这个字段只是"看起来经过了验证"。
    * 同型:**静态计数器与导出实际依据的数字必须打印在同一行**。1.5.1 那场
      `[FACT] 击=5220` 对导出 5055 个伤害事件,差 ~3%,代码逐条查过(调用点唯一、无早退、
      `AddEvent` 上限 100000 远未触及、导出无截断、结构自检 OK)**却查不出机制** ——
      于是不编解释,改为把 `导出事件=` 加到 `[HITDET]`/`[FACT]` 行上,让下一场一眼判定。
      ✅ **下一场就判出来了**:1.5.2 实机(183213)五个计数器完全自洽 —— `[RECON] 伤害事件=5583`
      = `[TIMELINE] 读取=5583` = `[HITDET]` 31+5551+1 = `[FACT]` 覆盖分母 5583;导出 events=5600
      (5583 dmg + 17 heal),其中 5582 条有有效 calc,所以 `击=5582`。**1.5.1 那 165 不是计数错,
      是那一场的事件流从 `t=7.530s` 才开始**。`[HITDET] 产出=5636` 与匹配 5583 的差 53 同理,
      是 `damage<=0` 早退**之前**就入队的 `NoteHitDetail`,不是丢事件。

43. **钩子侧的静态计数器,与「这一局留下的产物」,是两本账;必须逐场对账。**(1.5.2 实机)
    * `FactStore.Observed` / `HitMatchExact|Pair|None` / `StateTimeline.Observed` /
      `HitDetailProduced` 都是 `static`,只在各自的 `Reset()` 里清零;而事件落在
      `BattleSession.Events` —— 一个**随会话生灭**的 List(`ResetActors()` 会 `Events.Clear()`)。
    * 于是「会话被清掉」时计数照数、事件没了。1.5.1 那场(175142)正好如此:计数器报 5221,
      导出只有 5055 个伤害事件,差 166;而那一场的事件流首个 `t=7.530s` ——
      **近 14 份导出(1.3.3→1.5.2)里唯一一份 `min_t != 0`**。
    * 铁律:凡「钩子侧计数 / 导出行数」这一对,**必须在同一行日志里同时打印**,并把导出事件数
      (`导出事件=`)放进 `[HITDET]`/`[FACT]`。1.5.2 就是这样在**下一场**一眼判定的。
    * 另一条同型的账:`[HITDET] 产出` 计的是四个「返回伤害」钩子的调用,而入库发生在
      `RecordDamage` 的 `damage<=0` 早退**之后** —— 所以 `产出 > 匹配` 是**预期**,不是丢事件
      (1.5.0 +97 / 1.5.2 +53)。两本账各自都有不该相等的地方,把它们当校验和会得出假警报。
    * 本节的新证据:`_dpsm_work/evidence_152live_VERDICT.txt`(判据逐条结算)+
      `_dpsm_work/evidence_152live_p2_scan.out.txt`(771 份导出里只有 3 份带 `hitDetail`)+
      `_dpsm_work/evidence_152live_mint.py`(14 份导出的首个事件时间)

44. **状态名列表的「连接词」是语法的一部分,不是标点;少认一个连接词,代价是整条规则不折。**(1.5.3 离线定案)
    * 实测:`[ド・マリニーの掛け時計]「毒と火傷状態の敵への与ダメージがそれぞれ+15%」` 在旧队那场
      **folded 0 / parked 960**,而残差恰好 **1.3225 = 1.15²**。对照:`毒の短剣`(写成「毒状態の敵に
      対して」)折了 2700,`海魔の残滓`(写成「毒/火傷状態」)折了 5510 —— 差别只在连接词。
    * **为什么是「整条不折」而不是「少折一份」**:`JudgeClause` 的 それぞれ 叠加写作
      `if (stHits > 1 && clause.Contains("それぞれ"))`。run 在 と 处断掉 → `stHits=1` → 叠加永不发生;
      同时残留的「毒」撞上通用关键词表 → `CondState.Unknown` → 记成 (条件性,未计入)。
      **两个失效叠在一起,结果是 0。**
    * 铁律:凡是把状态名列表从文本里切出来的地方,**连接词集合必须完全一致**
      (`/ ／ 、 ・ と や 及`),并且只能有一个实现。本版把三处合并进
      `Model/ClauseStatusRun.cs`,并由 `recon_probe` **执行**它(删掉 と 会让 3 条断言变红)。
    * ⚠ 本版踩过的坑:第一轮只修了后备路径(`FollowedByState`/`ClauseStatusNames`)和
      `StripVerifiedStateRuns`,断言全绿,但**真正下判决的 `StatusTokens` 没被碰到,规则仍会折错**。
      **断言必须钉在「下判决的那一处」,不是「名字最像的那一处」。**
    * 同型先例:`StatusKey.cs`(1.5.2)也是从 `FactStore.cs` 里抽出来给 recon_probe 执行的。
    * 证据:`_dpsm_work/evidence_p0_x1152.py`(1.3225 击的 comp2 与折叠清单)、
      `_dpsm_work/evidence_153_replay.py`(离线重放:旧队 67.1%→83.8%、新队 33.1%→82.7%)。

---

## 7. 已知技术债(按优先级)

0. **构成对账率只有约 1/4–1/3,而且"配对"与"数值"两件事曾被混在一起**
   (**1.3.0 已把度量做掉,准确率本身仍未改善**):
   · `理论 W × M = T` 与 `实际伤害 A` **完全相等**的只有 **16,857 条(35.8%)** —— 约 2/3 的伤害现有推算算不出来(R78 起该量在面板上印作「游戏口径」;本行是 1.3.0 当时的旧词「实际伤害」);
   · 99.5% 的行被标注 `本次伤害与该次计算值不符(近似构成)`,但**在"构成精确对上"的 16,857 行里也有
     98.6% 被标注** → 这条标注表达的是「配对未被结算对象自身佐证」,**不等于构成错了**。
   · ~~因此要么把 `CalcValueMatches` 的语义拆成两个布尔(`配对可信` / `数值吻合`),要么至少改掉措辞,
     否则使用者会把一条其实精确的行读成不可信。~~
     **1.3.0 已做**:措辞改为 `配对未获结算对象佐证`,并落成 `pair` / `pairTrusted` /
     `pairCorroborated` / `valueMatches` 四个字段。**同时抓出真因**:`CalcValueMatches` 在第一条
     指针命中的记录上就 `return false`,AoE 下几乎恒假(实测 2,458/2,458)—— 见第 4 节第 20 条。
   · **1.3.0 新增的度量**(`Output/CalcReconcile.cs`):根级 `reconcile{exact,approx,unexplained,
     theoryExceeds,byPair,byTenth*,topResidual[]}` + 逐条 `calc{}` + `[RECON]` 日志行。口径写进数据
     (`approxTolerance` / `theoryExceedsResidualMax`)。**已实机验证**(2026-10-03,quest 9999:
     916 条伤害、JSON 合法、自洽 9/9、`calc` 与中文串逐项一致 916/916)。
   · **仍未改善的**:全语料 725 份 / 234,895 条的重算精确率是 **25.4%**(1.2.3 两场 34.2% / 26.4%,
     1.3.0 首场 28.1%);未解释事件的剩余倍率里 **`1.210`(=1.1×1.1)单独占 21.7%** —— 这是下一块要拿的。
   · **⚠ 更正**:原先写成"疑似配对错位"的那一类(剩余倍率 < 0.1,全语料 9.0%)经 1.3.0 首场实机查明,
     **100.0% 属于任务 9999**(20,685 / 20,691),是**训练场两座建筑上的未建模减伤(~×0.03)**,
     与配对无关,且自 1.0.0 就存在、不是回归。字段已改名 `theoryExceeds`。见第 4 节第 22 条。
   · 另:会心标记 **0/228,646** 从未生效,且**已实测证明无法用剩余倍率反推**
     (20,802 条"会心率>0"里 17,280 条剩余倍率 ≠ 会心伤害率;**1.3.0 又做了单调性检验**:
     会心率 25% 的组命中率 0.6%,反而低于会心率 5% 组的 3.1% —— 代理量必须单调,它不是)。
     唯一权威来源是 `DamageCalculater.GetFlyTextNumberSizeForAttack(float, bool isCritical, bool)`,
     参数全为值类型(无 Il2Cpp 对象封送),但它底层原生方法是 private,须先实测。

1. **`Aggregator.cs`(782 行)仍偏大**:会话生命周期/时钟、攻击者归属、统计累加、结算与
   `[CROSS]` 对照混在一起。建议按 `Session / Attribution / Stats / Finalize` 拆 4 个 partial,
   手法同上;`Attribution` 里那几个硬编码时间窗(0.80 / 0.60 / 0.45 / 0.20 / 0.08 秒)应提成命名常量。
2. **静态可变状态普遍**(`Aggregator.Session`、`CompositionProbe._globalRules/_snap*`、`Probe.Counts`、
   `OverlayUGUI` 一堆 static):无法单元测试,且生命周期 bug 只能靠日志发现(全局规则清空那次)。
   长期方向是把"一局"的状态挂到 `BattleSession` 上。
3. **`HitRecord` 从来没匹配上**:导致导出的 `source` 恒为 `Unknown(0)`,毒/火傷/DOT/反射无法按
   来源分类。这是**功能 bug**,不是结构问题。
   **1.4.0 的复核(更正)**:①治疗事件的 `source=8`(`DirectHeal`)是**有效的**,所以字段本身可写,
   是伤害路径没写;②`DamageSource` 枚举 **0 = Unknown、1 = DirectAttack**,不要把 0 读成"直接攻击";
   ③真正的来源信息已经在 `comp2` 的 `来源 DOT` 文本里(411001 一场 2661 条),`v142_report.py` 已就地提取。
   把它提升为结构化字段仍是遗留项。
4. **诊断路径与显示路径的重复**已消除(共用 `JudgeClause`),但 `Log()` 仍需自己调一遍
   `ApplyGlobalDebuffs`,理想做法抽一个 `BuildChainResult` 结构供三方共用。
5. **`Probe`/诊断开关的读取分散**(`Plugin.CfgXxx.Value` 散落各处):可加一个 `Diag` 门面集中判定。
6. **蓄積値(スタン/狂気 累计值)与状态层数未实现**;`TokenMergeToOwner`(召唤合并开关)、
   过量伤害裁剪、同名合并显示仍是可选项。
   **1.4.0 部分解决**:`UnitStateProbe` 现在读 `CharaStatus.mStun` / `mMadness` /
   `PoisonDamage` / `BurnDamage` / `MadnessAllyBuffRatio` 并随 `[STATE]` 与取证样本导出
   (读取已实现;**尚未并入伤害公式**)。`Character.GetStatusSubParamValue(eBuffType, bool, Func<…>)`
   是游戏自己的蓄积读取器,留作下一步。
7. **`layoutAmbiguous` 不是缺陷,`cJoins` 已删**(1.4.0)。
   · `layoutAmbiguous` 恒等于 `ptrHits+idHits`,度量的是"读到了多少能力",不是歧义 ——
     原因是 0..11 的索引与槽位枚举无法区分。原始证据现在随 `layoutSample`/`layoutSlots` 导出。
   · `cJoins` 的路由 join 键把两个 id 空间混在一起,50/50 场恒为 0,**已连同路由删除**;
     不留"给已删代码用的恒零计数器"。
8. **`battle_define` 的值 id 空间未解**:21 行不残缺(正好是 `BattleDefine.Id` 的 21 个成员),
   但 18/21 的值在任何已转储表里都不存在,只有 2000/2001 能用数字解释(50/100 = 会心率与貫通率上限)。
   要解开必须再转储目标表(最可能是 effect_data.asset);1.4.0 至少把**枚举名**导出了,
   21 行不再是匿名数字。
7. **没有测试工程、没有 git**:回归只能靠"构建 + 反编译多重集 + 实机打一场 + 导出 JSON 复核"。
   若要长期演进,建议把 `_dpsm_work/src` 纳入版本控制(含本文件),并把 `verify_*.py` 作为回归脚本保留。
   (1.3.0 起有 `_dpsm_work\recon_probe\` 这个**不依赖游戏**的可执行测试,覆盖对账模块;
   它是本项目第一个可自动重跑的回归点,新增/修改 `CalcReconcile` 时必须加断言。)
   **1.5.0 补了两层"导出侧"的自动检查** —— 之前这里唯一的手段是人眼:
   · 运行时:`Output/JsonCheck.cs` 在**每次导出写盘前**扫成品(字符串外括号平衡 + 根键重复),
     结果进 `[DpsMeter][JSON] 结构=OK/非法:…`。它自己也由 `recon_probe` 反向验证(能拒绝四种畸形)。
   · 离线:`_dpsm_work\check_export_schema.py` 检查**键与类型**(不是结构),并对只在适用时才发出的
     字段报**覆盖率**;`--selftest` 用 6 个用例证明它既能接受合法导出、也能拒绝 4 种破坏。
     新导出的第一件事就是跑它。
   · 配套:`_dpsm_work\v150_validate.py` 把"一场实机验证"变成一条命令(KPI 对照**同任务**历史,
     且只与**口径一致**的版本比 —— 否则旧版本缺键会让中位数变 0,"没有下降"就成了一句空话)。
8. **会话会被波的"战斗结束"信号切碎**(2026-10-03 实测,待用户定修法):全语料 757 份导出里
   `duration=0.0` 的占 **116 份(15.3%)**,按任务最多的是 `302158` 79.3%;一段约 45 秒的连续竞技场
   被切成 **13 次会话/13 份导出**。成因是 `Aggregator.TryResumeClosedSession` 只在"空闲超时关闭"
   时才续接(`if (_lastClosedWhy != "idle") return false;`),而波次内容会**按波**触发
   `SetBattleEndFlag` / `SetGameResult` / `ActDestroy` 且不带结果。
   **不能简单放宽**:那会让两场真正独立的战斗被静默合并、把总伤害算多(比切碎更糟)。
   三个方案(加归组标记 / 放宽续接+合并计数 / 维持现状)见 SESSION-STATE §7.2 里该版本的记录(原说明已随旧文档清理删除)。
9. **`dealt` 是"进入耐久"的口径,游戏自己的统计是"吸收前"**:实测 1.3.x 的 32 场差 **13.15%**
   (逐场 0–55.6%)。1.3.2 已把两个数并排导出(`totals.absorbed` / `totals.dealtWithAbsorbed`),
   但**显示口径没改** —— 改它会动到悬浮窗/DPS/曲线/全部历史对比,属于需要用户拍板的产品决定。
10. **跨单位的「被ダメージ+X%」规则没有读取路径**(2026-10-03 实测,**已根因定位,待实现**):
   `vicMod` 只扫**受击方自己**的能力(`AbilityScan(blocker, victimSide:true)` + `TalentDamage(blocker,true)`),
   而实战里最大的一块未解释倍率来自**攻方友军**的一条天赋 ——
   `エヴララス・フラウ` 的两条刻印 id=26「ブロックしている敵の被ダメージ+10%（前衛のみ）」
   (`type=1006 被伤害- p=[-10] cond=GiveTalent(1)`)→ **1.1² = 1.210**。
   证据:该 Boss 上 5506 条事件的 `takenMult` 恒为 1.000,而残留在该 Boss 上恰好分成
   1.000(2039)/1.210(1508) 两个值(条件性规则的特征),且 1.21 以乘性形式嵌在 2.299 / 4.001 里,
   连 1/1.21 = 0.826 也在残留里出现。
   → 需要:(a) 判定"该敌人是否正被某个我方单位格挡";(b) 把该单位的该条天赋计入 `vicMod`,
   或并入既有的全局规则通道(`RegisterGlobalDebuffs` / `ApplyGlobalDebuffs`)。
   落地时必须加开关 + 自报计数,让错误立刻在 `reconcile.exact` 上可见。
11. **全局 +15% 规则疑似被重复登记**:同一场里 `dealtMult` 出现 **1.15¹…1.15⁸ 的阶梯**
   (1.521=1.15³、1.749=1.15⁴、2.011=1.15⁵、2.313=1.15⁶、2.66=1.15⁷),
   而名册里同名规则只有 2–3 条 → 需要确认每个**副本**是否都该各算一次。
   (这也是残留里出现 0.87 / 1.15 这类"多算一次"痕迹的来源。)
12. **架构审视(2026-10-03,只读,未改代码)**:见 `_dpsm_work/ARCH-REVIEW-1.5.md`。
   三条结构性结论(均已就地复核;证据文件在 `_dpsm_work/arch_review_evidence/`):
   · **伤害事件的 `source`/`crit` 通道从未接线** —— `Aggregator.RecordHitDetail` 全仓无调用者,
     全语料 768 份 / 570,078 条事件里 `crit` 恒 false、伤害 `source` 恒 0。
     **更正**:治疗事件的 `source=8`(`DirectHeal`)是在 `RecordHeal` 里显式写死的(`Aggregator.cs:1182`),
     所以**字段可写,缺的只是伤害路径的接线**;另 `StartSession` 未清 `_activeCalc`(技能归属可跨场污染)。
   · **折叠只有标量、没有 provenance** —— 84 种攻方因子被乘进 `DealtMult`/`TakenMult` 两个 double
     (每击 2–5 个),来源只存在于中文句子;`_enemyCandidates` 是 `Dictionary<double,int>`,
     **按数值去重且存的计数从不被读**,导致「哪条规则折了哪一份」在数据结构层面不可判定。
   · **事实只在命中瞬间存在、且只覆盖 3% 的命中** —— 导出后 `ActorStats.Source = null`;
     `Forensics` 全场只留 160 份样本(5501 击),而同一套键扩到 12 元组也只有 493 个类。
   对应路线图 A/B/C 三批,**在 A1(`CalcBreakdown.Fold`)落地前不要动 1.15^n 的折叠**。
