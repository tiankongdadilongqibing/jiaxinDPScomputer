# REFACTOR-BATCH-R64 —— 直接读自动技能:Skill 侧实例 + 充能 + 发动时刻

日期:2026-10-06。插件 **1.7.15 -> 1.7.16**;`5F9234EF331E3F659FE3BE8336FA943BF353F6EF458DBD5F657869D910D96823`(451,072 B)。
起因:用户指令「下一步做 R64:读 Skill 侧的自动技能实例与充能值、把「发动时刻 + 充能」写进日志」。
这是 R63 §7 列的第 1 项(R63 主表到手后,**上一轮从伤害通道反推的 ≈13.5 s 结论转为存疑**,见 R63 §3c)。

本轮**不移动任何既有数值**:不改公式、不改归属、不改导出形状(`contribution.schemaVersion` 仍 1.2),
**不新增导出根段**(用户要的是「写进日志」);只新增一个只读探针 + 一个隔离的 Harmony postfix + 一条纯换算规则。

## 0. 一句话

- 新增 `src/Diagnostics/AutoSkillProbe.cs`:从**线上 `Skill` 实例**这一侧读自动技能,写两类日志行 ——
  `[AUTOSK] act`(**发动时刻**:墙钟 + 战斗钟 + 单位/槽位/技能名/`Skill.Type`/`GetStatus()`/充能/时长/库存)
  与 `[AUTOSK] chg`(**充能轨迹**:每槽每 2 s 一行,带由**相邻两行本身**可复算的 `dWait`/`upsGame`/`upsWall`/`chargeSec`)。
- 新增 `src/Hooks/AutoSkillHooks.cs` + `Plugin.TryPatchAutoSkillActivation()`:对游戏自己的命令入口
  `GameCmdExecuter.ActExecutePlayerAutoSkillForPassive(Player, int, Vector3)` 打一个**隔离**postfix(§2)。
- 新增纯策略 `src/Policy/AutoSkillCadencePolicy.cs`(充能速率 / 充能折算秒数 / 相邻发动间隔 / 中位数)。
- 行为用例 **987 -> 1016**(29 例新组 `policy/autoskill-cadence`)、变异负控 **144 -> 148**。
- **没有**把「自动技能到底几秒一发」写成结论:那要等下一场实机日志(§5)。

## 1. 先证明「自动技能在哪」,再动手(不靠猜)

R63 已经证明她那张主表(`auto_skill` id 84「暗沌への導き」)存在。R64 要证明的是**运行时这个对象在哪**。
三条都由外部工具/产物先证:

1. **它是普通 Skill**:`ilspycmd -t Rog.MasterData.AutoSkillMasterData` → `public sealed class AutoSkillMasterData : SkillMasterDataBase`。
   所以一行主表最终会变成普通 `SkillData` → 普通 `Skill`,而不是某种专用对象。
2. **它不在 `[CLOCKP]` 走的那个列表里**(这是 R63 §7 的原话,本轮把它量出来)。`TimeProbe` 走的是
   `StandbyController.m_standbyDataList` → `PlayerSkillStandbyData`;实测最后一场(2026-10-06 01:52)该列表渲染出的
   三个技能是 地下からの完全顕現 / 電脳掌都 / 狂気の眼球。把 4 个名字拿去**遍历全部 21 个 `masterdata/*.json`** 搜:
   只有 `auto_skill.json` 命中,且只命中 1 个 —— 暗沌への導き。另外三个在**任何**一张已转储的表里都不出现。
   即:**自动技能不在那个 standby 列表里**,继续读它不会有结果。
3. **游戏自己的判别字段**:`ilspycmd -t Skill` → 嵌套枚举
   `public enum Type { Skill, SpecialSkill, OverSkill, AutoSkill1ForPassiveSkill, AutoSkill2ForPassiveSkill }`
   与 `public enum Status { NotHave, Charge, Usable, Using }`,以及 `public unsafe Type m_type`、
   `public unsafe Status GetStatus()`、`public unsafe int WaitCountFrame`、`public unsafe int CoolTimeFrame`。
   正好**两个自动技能槽** —— 与 `Player.AutoSkill1` / `Player.AutoSkill2`、`PassiveSkill.AutoSkill`、
   `InvokingCondition.IsAutoSkill1Start/2Start`、`UserActionRecordPlayerAutoSkillStart(int index)` 全部一致。

**充能 = `Skill.WaitCountFrame`,满格 = `Skill.CoolTimeFrame`,状态 = `Skill.GetStatus()`。** 这就是本轮要读的三件东西。

## 2. 改了什么

| # | 改动 | 文件 |
|---|---|---|
| A | 纯规则:`UnitsPerSecond` / `SecondsFor` / `IntervalSeconds` / `Median` | `src/Policy/AutoSkillCadencePolicy.cs`(新) |
| B | 探针:每槽充能采样 + 发动行 + 收尾汇总 + 全计数 | `src/Diagnostics/AutoSkillProbe.cs`(新) |
| C | 隔离 postfix(只声明 `Player`/`int`/`ref bool`) | `src/Hooks/AutoSkillHooks.cs`(新) |
| D | 配置 `Debug/AutoSkillProbe`(默认 **true**)+ `TryPatchAutoSkillActivation()` 挂进 Load | `src/Plugin.cs` |
| E | 帧循环接线 `AutoSkillProbe.Observe(val, Session)`;`StartSession` 里 `Reset()` | `src/Aggregator.Clock.cs`、`src/Aggregator.Session.cs` |
| F | 收尾汇总行 `[AUTOSK] SUM`(每槽发动数 + 墙钟/战斗钟中位间隔 + 所有计数器) | `src/Aggregator.Finalize.cs` |
| G | 行为用例 29 例 + 编译清单 + 钉住总数 987 -> 1016 | `tests/BehaviorTests/Cases.AutoSkillCadence.cs`(新)、`BehaviorTests.csproj`、`Program.cs` |
| H | 变异负控 4 条 | `tests/negative_control.py` |

### 2a. 为什么是「两条通道」而不是一条

| 通道 | 来源 | 给出什么 |
|---|---|---|
| `via=cmd` | `GameCmdExecuter.ActExecutePlayerAutoSkillForPassive` 的 postfix | **精确发动时刻** + 游戏自己的返回值 `ok=` |
| `via=poll` | 采样器上 `GetStatus()==Using`(或 `IsActivated`)的上升沿 | patch 万一没解析上时的**兜底时刻**,且与上面互为佐证 |

两条都计数(`actCmd` / `actPoll`),所以「patch 没生效」和「技能没发动」不会长得一样 —— 与 1.5.4 狂气施加通道
「两条独立路线互相佐证」是同一条规则。

### 2b. postfix 只声明三个参数,理由写在代码里

`AutoSkillHooks.PostfixAutoSkillForPassive(Player __0, int __1, ref bool __result)` —— **不声明** `Vector3` 形参。
1.0.48/1.0.49 在 `BattleObject.ActDamage` 上连**惰性** detour 都崩(崩在 Il2CppInterop 的参数封送里),
所以本仓库的既有规矩是「少materialise一个对象就少一份风险」(`Hooks/TalentActionHooks.cs` 顶部同款注释)。
`bool`/`int` 是值类型,封送零成本;位置信息本轮不需要。补丁体整段 `try/catch`,不会把异常抛回游戏的发动路径。

### 2c. 「充能」为什么必须能由打印出来的两行复算

`[AUTOSK] chg` 的 `upsGame`/`upsWall`/`chargeSec` 只用**上一次打印的那一对** `(wait, wall, active)` 算,
所以拿日志里**相邻两行**手算就能核对 —— 这是本仓库对「可复算」的一贯要求(不是「探针内部算了多少遍」)。
`chargeSec = SecondsFor(cool, upsGame)` 就是**把充能折算回主表的秒**,与 R63 的 `*CoolTime`/`*CoolTimeFrames` 对账用。

## 3. 验证(本机实测)

| 项 | 结果 |
|---|---|
| 构建 | **0 警告 0 错误**(这一步同时就是 interop 成员名的验证:`Skill.m_type`/`GetStatus()`/`Player.AutoSkill1`/`Player.PassiveSkills`/`PlayerSkillStandbyData.m_player` 任写错就编译不过) |
| 行为套件 | **1016 用例 / 102 组 / 0 失败**(钉住 987 -> 1016) |
| 变异负控 | **148 例 / 0 失败**;新增 4 条逐条确认红在具名用例(见 §4) |
| 文档收敛 | 12/12 |
| 文档解码 | `check_docs_123.py` 全过(新批次记录已登记进 FILES) |
| 契约与工具 | `check_extract_contract` / `check_tool_registry` / `repo_manifest --verify` 全过(无新增 `.py`) |
| 验收 | `n0_acceptance.py --out acceptance_r64` = **44 条命令 / 76 条检查 / 0 项** |

## 3a. 验收:第一次跑出 74 ok / 2 项,原因与修法

第一次 `--out acceptance_r64` 是 **74 ok / 2 项**(红的正是 `doc_convergence` 与 `selftest/doc_convergence`,
同一件事)。R1–R5、R7–R12 全过,**红的是 R6**:

    [FAIL] R6 the training ground is not_comparable and no document claims it comparable
           -- offenders=['index:21']

R6 的判据是「一行里同时出现 `9999`、`full`(或「可作基准」)而没有 `not_comparable`」。索引第 21 行是
**版本行**(我把 R64 的描述接在它前面),它本身早就含 `9999`(1.7.14 那段「实测训练场 9999 里 3,240/11,096 条」),
而我在描述 `[AUTOSK] chg` 时写了字段名 `` `full` `` —— **`full` 是适用性分类的档位名**,于是撞上 R6。

这不是误报要放宽,而是**我的字段名起得太糟**:`full=` 三个字母既与 `full/partial/not_comparable`
这个既有词汇冲突,单看也说不清「full of what」。所以把日志字段改名为 **`chargeSec=`**(「整条充能的秒数」),
文档一起改。**顺带修了我自己造成的第二处损坏**:批量替换 `` `full` `` 时把
`CONTRIBUTION-TABLE-REPORT.md` 里「两场 411001 进 `` `full` ``、训练场 9999 进 `not_comparable`」
(那说的是**适用性档位**)误改成了 `chargeSec`,已回改。

改完复跑:**44 条命令 / 76 条检查 / 0 项**(`doc_convergence` 与 `selftest/doc_convergence` 都 exit 0)。
这也是本轮的一个**独立教训**:给日志字段起名时要先看这个仓库的既有词汇表,一个 4 字母的词就能让守卫红。

## 4. 四条新变异负控(为什么它们会咬)

| 变异 | 改法 | 必须变红的具名用例 |
|---|---|---|
| `autoskill-charge-forgets-the-clock` | `v = units / unitsPerSecond` -> `v = units` | `policy/autoskill-cadence/the-9000-frame-charge-is-the-masters-300s`(9000 帧必须回到主表的 300 s) |
| `autoskill-rate-clamps-a-backwards-counter` | 速率里的负值被夹成 0 | `policy/autoskill-cadence/a-counter-that-ran-backwards-keeps-its-sign`(计数回填是「刚重置」,不是「快好了」) |
| `autoskill-median-keeps-the-sentinel` | 中位数不再丢弃 `<= 0` 的项 | `policy/autoskill-cadence/a-duplicate-record-does-not-drag-the-median`(0 是 `IntervalSeconds` 的「没测到」哨兵,不是节奏) |
| `autoskill-median-sorts-the-callers-array` | `copy = new double[...]` -> `copy = values` | `policy/autoskill-cadence/the-median-does-not-reorder-the-callers-array`(就地排序会改掉探针自己的历史) |

四条各钉住本轮新代码里最容易被写错的一件事:单位、符号、哨兵、别名。

## 5. 覆盖不到的(必须写下来)

- **`AutoSkillProbe.cs` 不在 BehaviorTests 的编译清单里**(它需要 IL2CPP 面),所以**探针本身没有行为测试**;
  被测试覆盖的是它调用的**规则**(`AutoSkillCadencePolicy`),以及「成员名正确」这个由编译器保证的性质。
  这与 R63 §5 对 `MasterDataDump.cs` 的限制同类。
- **「自动技能几秒一发」这个数字本轮没有产出**:它只能从下一场实机日志的 `[AUTOSK] act` 行读出。
  本轮**没有**要求用户为此专门打一场(AGENTS §0.2)。用户重启后日志里出现 `[AUTOSK]` 即端到端打通。
- **patch 是否真的解析上,也只能由实机回答**。启动时插件会写一行 `[DpsMeter] auto-skill activation postfix applied`
  或 `... not found; ... skipped`;若 skipped,`[AUTOSK] SUM` 里的 `actCmd=0 / actPoll>0` 会直接说明
  「时刻是靠兜底通道量到的」,而不是让读者以为 `actCmd>0`。
- `Player.AutoSkill1/2` 是**属性**(游戏侧会算),采样器每 0.5 s 才调一次、每槽每 2 s 才打印一行,
  并对**每槽**设 24 个槽位上限、对中位数历史设 400 条上限,溢出都计数(`droppedSlots`/`droppedIntervals`)。
  这是「读游戏的属性」这件事的已知代价,不是零成本。
- **命令 postfix 用 `Aggregator.Session == null` 做门禁**(而不是无条件读游戏对象):它被装在整个进程上,
  而 1.0.48/1.0.49 的崩溃正是「detour 体在不可能的状态下读游戏对象」。会话对象存在 ⇒ 游戏至少走到了
  战斗建立;刻意**不**用 `InBattle`,因为战斗结束序列里也可能发动,丢掉那些行会丢真实数据。
  `[AUTOSK] SUM` 的 `nullPlayers` 单独计数「命令来了但没有 Player」,那是游戏的事实、不是探针的失败。

## 6. 部署

`68E10640`(438,272 B,1.7.15)-> **`5F9234EF`**(451,072 B,1.7.16);
替换前确认**游戏未运行**且目标文件未被锁定;旧 DLL 备份 `_dpsm_work/deploy-backup/pre-r64-68E10640/DpsMeter.dll`
(哈希复核 = `68E10640…`);两个 crash `.bak` 复核未变(`2E1819F2…`、`F7FF1EB8…`);
部署后 `repo_manifest.py --write --exports batch_inputs/rf0` 重新记录(**必须在 `_dpsm_work` 下跑**,见 R63 §6b.1)。

**回退**:一条 `Copy-Item` 把 `pre-r64-68E10640/DpsMeter.dll` 覆盖回 `BepInEx/plugins/DpsMeter/DpsMeter.dll`。

**关于替换了不止一次**:第一次构建产物拷上去之后,又对**源码**做了修正(§5 的 session 门禁;以及
`[AUTOSK] SUM` 里两个会被误读的计数名 —— `emptySlots` 其实是**读取次数**、每个槽的 `act=` 在两条通道
都活着时会被计**两次**),因此重新构建并重新部署,最终线上的是 **`5F9234EF`**。中间那两次拷贝
(`B2212C21`、`DDF57836`)从头到尾只在这台机器上存在过、没有对外的引用,文档里只留最终值;
记这一笔是因为本仓库已经为「把中间状态当结论」付过三次代价(AGENTS §4 末条)。

## 7. 与用户那个问题的关系,以及下一步(R65 候选)

R63 的答案是「主表说 240–300 秒,和之前反推的 ≈13.5 s 对不上,所以那个反推存疑」。本轮把这件事实
**变成可以量出来的东西**:下一次实机之后,`[AUTOSK] act` 行的 `dWall`/`dActive` 与 `SUM` 行的
`medianWall`/`medianActive` 就是**直接观测到的发动间隔**,而 `chg` 行的 `chargeSec=` 就是把充能折算回主表秒数。
两者一致 ⇒ R63 的 240–300 s 口径成立、13.5 s 的反推被否定;不一致 ⇒ §3c 的第 2 种可能(主表冷却不是那条通道的节拍器)。

1. **R65-a**:实机日志到手后,把 `[AUTOSK]` 行与 `[TIME]`/`[CLOCKP]` 行按时间并起来,给出**可引用的发动间隔**,
   并把它与效果 `10024` 通道(≈14 s)在**同一时间轴**上对齐 —— 这是把 R63 §3c 那两个可能分开的唯一办法。
2. **R65-b(本轮已顺手做掉)**:「`auto_skill` 表的 id 空间 ≠ 伤害计算的效果 id 空间」这条口径,
   已按 R63 §7.2 的待办写进 `CONTRIBUTION-DATA-DICTIONARY.md` 的 `##### R64` 小节(含「日志里的效果 N」
   与「`auto_skill` 第 N 行」默认假定无关这条硬规则)。
3. **R65-c**:`repo_manifest.py` 的 `diff_manifest` 加一道形状自检(corpus 为空而输入非空时**报错**而不是抛
   `TypeError`,R63 §6b.1)。

## 8. 归档与计数

- 行为用例 **987 -> 1016**(新组 `policy/autoskill-cadence`,29 例;组数 101 -> 102)。
- 变异负控 **144 -> 148**(4 条 `autoskill-*`,逐条确认红在具名用例)。
- 验收归档 `_dpsm_work/acceptance_r64/`(`runs.json` 44 条 + `corpus_manifest.json` 76 条检查 + `RESULTS.md`);
  最终 **44 条命令 / 76 条检查 / 0 项**(第一次 74/2 的原因与修法见 §3a)。
