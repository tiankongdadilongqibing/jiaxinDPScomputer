# REFACTOR-BATCH-R63 —— 把「自动技能」主表 dump 出来(冷却终于有游戏自己的数)

日期:2026-10-05。插件 **1.7.14 -> 1.7.15**;`68E10640CC920B93FE3DE6AEF38460EE6BC6B9BE31C60694DBB32611DEC1E69A`(438,272 B)。
起因:用户问「[賢導]トレイラ 的 auto skill 发动间隔,以及与技能冷却时间之间的规律」;上一轮我只能从战斗数据**反推**效果通道的节拍,
拿不到游戏自己的冷却值 —— 因为 `auto_skill_data.asset` 这张表插件从没 dump 过。用户指令:「可以,看一下能否加进去」。

本轮**不移动任何既有数值**:不改公式、不改归属、不改导出形状(`contribution.schemaVersion` 仍 1.2),只**新增**一张主表与一个纯单位换算规则。

## 0. 一句话

- 新增主表转储 `auto_skill`(`Rog.MasterData.AutoSkillMasterTable`,即 `awake_potential.auto_skill_id` 指向的那张):
  冷却/首次冷却/持续/库存/触发时机等字段;输出目录由 **20 个文件变 21 个**(20 张表 + `_table_registry.json`)。
- 新增纯策略 `src/Policy/SkillCooldownPolicy.cs`:主表存**秒**、线上 `Skill` 数**帧**(30 单位/游戏秒,实测),
  导出**同时**给 `*CoolTime` 与 `*CoolTimeFrames`,免得消费方把两个单位混着用。
- 行为用例 **971 -> 987**(16 例新组 `policy/skill-cooldown`)、变异负控 **142 -> 144**。

## 1. 为什么是「能不能加进去」而不是「直接改」

插件读主表的方式是**编译期对真实 interop 验证**(见 `MasterDataDump.cs` 顶部:每张表显式写出
`Table<TKey, TTable, TRow>` 与每个字段,字段名写错 = 编译失败)。所以先回答两件事:

1. **类型在不在**:`D:\dmmplayer\dev\metascan\bin\Release\net6.0\metascan.exe <interop> list-types AutoSkill`
   → `Rog.MasterData.AutoSkillMasterData`、`Rog.MasterData.AutoSkillMasterTable` 存在;
   `ilspycmd -t Rog.MasterData.AutoSkillMasterTable` → `MasterTableBase<AutoSkillMasterData, int>`(与 `AwakePotentialMasterTable` 同形)。
2. **字段在哪**:`AutoSkillMasterData` 自己是空壳(只有 ctor),字段全在基类 `SkillMasterDataBase` ——
   `id/name/text`(Obscured)+ `autoActivate/maxLevel/minFirstCoolTime/maxFirstCoolTime/minCoolTime/maxCoolTime/
   minDurationTime/maxDurationTime/skillRange/stock`(**全部 ObscuredInt**,必须走 `GameRef.Dec`)+
   `activationType/activationTypeParam/activationPositionSortId`(明文 int)+ `isTargetUnnecessary`(bool)+
   `triggerTimings`(嵌套 `SkillMasterDataBase.TriggerTimingData`)+ `talentList`(`List<AbilityTalent>`)。
   **同一张表里明文与混淆混用** —— 只能逐字段确认,不能假定(与 `REPORT-解包与游戏内数据获取.md` §4 的既有教训一致)。

## 2. 改了什么

| # | 改动 | 文件 |
|---|---|---|
| A | 登记 `auto_skill` 表(21 个字段 + talents + triggerTimings) | `src/MasterData/MasterDataDump.cs` |
| B | 新增纯规则 `SkillCooldownPolicy.Frames(seconds[, unitsPerGameSecond])` | `src/Policy/SkillCooldownPolicy.cs`(新) |
| C | 行为用例 16 例 + 编译清单 + 钉住总数 | `tests/BehaviorTests/Cases.SkillCooldown.cs`(新)、`BehaviorTests.csproj`、`Program.cs` |
| D | 变异负控 2 条 | `tests/negative_control.py` |

**单位这件事为什么值得单独成规则**:`minCoolTime = 14` 是**秒**,而线上 `Skill.m_coolTimeFrame` 是**帧**;
`[CLOCKP] … units/s=30.0` 与 `Plugin.cs`:344 记的实测(750/25、1500/50、1050/35)说明 1 游戏秒 = 30 帧。
只 dump 秒数,消费方迟早会拿它跟帧计数或跟战斗秒直接比 —— 这正是「dump 出来是为了不再靠猜」要避免的错。
所以 `*CoolTime`(主表原值,不解释)与 `*CoolTimeFrames`(按**本次进程实测到的** units/s 换算,读不到就用
`BattleClockPolicy.DefaultUnitsPerGameSecond` = 30.0)两组数同时写进同一行。

## 3. 验证(本机实测)

| 项 | 结果 |
|---|---|
| 构建 | **0 警告 0 错误**(这一步同时就是 interop 字段名的验证) |
| 行为套件 | **987 用例 / 101 组 / 0 失败**(钉住 971 -> 987) |
| 变异负控 | **144 例 / 0 失败**;新增 2 条逐条确认红在具名用例(见 §4) |
| 文档收敛 | 12/12(版本/哈希/语料/口径/schema/验收计数) |
| 文档解码 | `check_docs_123.py` 全过(新增批次记录已登记进 FILES) |
| 契约与工具 | `check_extract_contract` / `check_tool_registry` / `repo_manifest --verify` 全过(无新增 `.py`) |
| 验收 | `n0_acceptance.py --out acceptance_r63` = **44 条命令 / 76 条检查 / 0 项** |

## 3b. 实机端到端:表真的出来了(用户在跑)

部署后**用户自己重启了游戏**(不是要求他为验证而打):`BepInEx\config\dev.dpsmeter.cfg` 首行被 BepInEx 重写为
`created by plugin DpsMeter v1.7.15`,运行日志自检行:

    [DpsMeter][MASTER] 主数据 表成功=20 表缺失=0 表异常=0 行=2234 空行=0 行异常=0 嵌套跳过=16 多实例表=11 实例异常=0
      明细[registry=209 engraving=14 … awake_potential=1044 auto_skill=120 battle_define=21 … tribe=18]

- **`auto_skill=120` 行**,`表成功` 19 -> **20**,`表缺失=0`/`行异常=0` —— 转储路径整条通了。
- 产物 `BepInEx\plugins\DpsMeter\masterdata\auto_skill.json`(137,467 B,120 行)落盘;
  每行含 `minCoolTime`/`maxCoolTime`/`minCoolTimeFrames`/`maxCoolTimeFrames`/`stock`/`talents`/`triggerTimings`。
- **她的那一行(定位链:unit 84 -> `awake_potential` category 3/acquire_id 403 -> `auto_skill_id=84`)**:

  | 字段 | 值 |
  |---|---|
  | `name` | **暗沌への導き** |
  | `text` | 発動時、攻撃力+[0,0,0]%の効果を**8秒間**付与する / **次回攻撃時のみ**自身と邪悪なる死のカラストークンは低確率で**10秒間暗闇**状態にする**4連続攻撃**を行う |
  | `minCoolTime` / `maxCoolTime` | **300 / 240**(主表单位 = **秒**) |
  | `minCoolTimeFrames` / `maxCoolTimeFrames` | 9000 / 7200 |
  | `minFirstCoolTime` / `maxFirstCoolTime` | 240 / 180 |
  | `stock` | 0 |
  | `talentCount` | 11 |

  talent 里读得出来的部分(用 `TalentNames.cs` 对号):`6 攻击力%+` param 300 / maxParam **500** → 就是实测到的
  那层 **`攻撃力+500%`**;`1013 连射` = 4 连(对应正文的「4連続攻撃」);`516 暗闇` param 末位 300
  (**300 单位 = 10 s**,与正文「10秒間」一致,再次印证 30 单位/秒);`66 投射物=30022`;`1004 AddTalent` 的
  `timing=38`(=`AutoSkill1Start`)与 `timing=48`(=Manual)。

## 3c. 这一行**没有**证实上一轮那个「13.5 s」的推断 —— 必须写明

上一轮我从战斗数据反推出「效果 `10024` 通道每场首发动 ≈8.4 s、之后 ≈13.5/15.9 s 交替」,并**推断**它就是
オートスキル1。主表到手后这个推断**对不上**:`auto_skill` id 84 的 `minCoolTime/maxCoolTime` 是 **240–300 秒**,
不是 13.5 秒。两种可能都还站着:

1. 「效果 `10024`」是另一个东西(它只是 `DamageCalculater.m_effectId`,与 `auto_skill` 表**不是同一个 id 空间** ——
   该表里也存在 id `10024`「恐怖の特異点」,但那是另一件事);真正每 ≈14 s 重复的那层 `攻撃力+500%` 另有来源;
2. 主表这组冷却值不是那条通道的节拍器(`autoActivate=0`,正文写的是「次回攻撃時のみ」—— 它是**改写下一次普攻**
   而不是自己打一发,所以「伤害通道的节拍」本来就不等于「技能的冷却」)。

**结论:自动技能的发动间隔,上一轮给的数字现在存疑**,不能当结论用。要钉死它只能**直接记录自动技能的发动/充能**,
而不是继续从伤害反推 —— 插件现在没有这条记录(`TimeProbe` 只读 `PlayerSkillStandbyData`,自动技能不在那个列表里)。
这是 R64 的候选工作项,已写进 §7。

## 4. 两条新变异负控(为什么它们会咬)

| 变异 | 改法 | 必须变红的具名用例 |
|---|---|---|
| `skill-cooldown-forgets-the-unit-rate` | `f = seconds * unitsPerGameSecond` -> `f = seconds` | `policy/skill-cooldown/the-master-seconds-become-frames`(14 s 必须是 420 帧) |
| `skill-cooldown-decouples-the-fallback` | 回退常量从 `BattleClockPolicy.DefaultUnitsPerGameSecond` 改成裸 `40.0` | `policy/skill-cooldown/the-fallback-rate-is-the-clock-policy-default` |

第一条钉住「换算真的发生了」,第二条钉住「回退值不是第二个字面量」—— 两者都是本轮新代码里最容易被写错的地方。

## 5. 覆盖不到的(必须写下来)

- **`MasterDataDump.cs` 不在 BehaviorTests 的编译清单里**(它需要 IL2CPP 面),所以**转储代码本身没有行为测试**;
  被测试覆盖的是它调用的**规则**(`SkillCooldownPolicy`),以及「字段名正确」这个由编译器保证的性质。
  这与 R62 §6 对 `Aggregator.Stats.RecordDamage` 的限制同类。
- **端到端要等下一次实机**:`masterdata/auto_skill.json` 是否真的出现、`[DpsMeter][MASTER]` 自检行里
  `auto_skill` 的行数是否非零,只能由用户在**下一次启动游戏**后从产物读出。本轮**没有**要求用户为此专门打一场。
- `triggerTimings` 用的是嵌套类型 `SkillMasterDataBase.TriggerTimingData`;编译期已确认可达,
  但运行时「基类静态构造是否先跑」只在 CLR 有保证的范围内成立 —— 若它抛异常,表现为该行 `行异常` 计数 +1
  (逐行 try 兜住,不会整表失败),下一次实机的自检行会直接显示出来。

## 6. 部署

`C9D1B0CC`(435,712 B,1.7.14)-> **`68E10640`**(438,272 B,1.7.15);
替换前确认**游戏未运行**(进程表里只有 DMMGamePlayer 启动器)且目标文件未被锁定;
旧 DLL 备份 `_dpsm_work/deploy-backup/pre-r63-C9D1B0CC/DpsMeter.dll`(哈希复核 = `C9D1B0CC…`);
两个 crash `.bak` 复核未变(`2E1819F2…`、`F7FF1EB8…`);部署后 `repo_manifest.py --write --exports batch_inputs/rf0` 重新记录。

**回退**:一条 `Copy-Item` 把 `pre-r63-C9D1B0CC/DpsMeter.dll` 覆盖回 `BepInEx/plugins/DpsMeter/DpsMeter.dll`。

## 6b. 本轮踩到的两个「验收环境」尖角(记下来,别下次再花时间)

1. **`repo_manifest.py` 的 `--exports` 按 cwd 解析**。第一次我在仓库根跑
   `--write --exports batch_inputs/rf0`,路径不存在 → 清单的 corpus 段被写成**空**;而验收自己在
   `cwd=_dpsm_work` 下用**绝对**路径跑 `--verify`,两份形状不一致,`diff_manifest` 直接在
   `set(list(a) + list(b))` 上抛 `TypeError: unhashable type: 'dict'` —— 是**崩溃而不是报红**,
   在 RESULTS.md 里只表现为 `run_exit/repo_manifest_verify expected 0 observed 1`。
   正确姿势:在 `_dpsm_work` 下跑,或传绝对路径。
2. **`repo_manifest_verify` 会因为「游戏正在运行」而红**:用户重启游戏后 `masterdata/auto_skill.json`
   是新文件(报 `GREW … not drift`)、`dev.dpsmeter.cfg` 首行被 BepInEx 重写(报 `DRIFT: config`)。
   这不是代码漂移 —— 处理方式是**重新 `--write`** 让基线吸收这次实机写入,再跑验收(R63 就是这么变绿的:
   第一次 75 ok / 1 项,重新写基线后 **76 ok / 0 项**)。
3. 顺带记一条:`acceptance_r62/` 那次我没留下的归档,是由**我**用错 `--out`(相对路径,落到仓库根)
   产生的半成品,已删除;R62 本轮的归档从未生成 —— 它的 §5 只列了闸门。R63 的归档是
   `_dpsm_work/acceptance_r63/`(44 条命令的日志 + RESULTS.md)。

## 7. 与用户那个问题的关系,以及下一步(R64 候选)

用户问的是 トレイラ 的自动技能发动间隔。本轮的产出**不是那个答案**,而是**让答案可以被引用** ——
而且引用出来的第一个结果就是「上一轮的答案存疑」(§3c)。

1. **直接观测自动技能**:读 `Skill` 侧的自动技能实例/充能(而不是 `PlayerSkillStandbyData`;自动技能不在
   那个列表里),把「发动时刻 + 充能值」写成日志字段。这是唯一能把 §3c 那两个可能分开的办法。
2. 拿到之后再用主表的 `minCoolTime/maxCoolTime`(秒)与实测节拍对账,并把「效果 id 空间 ≠ 技能 id 空间」
   这条口径写进 `CONTRIBUTION-DATA-DICTIONARY.md`。
3. `repo_manifest.py` 的 `diff_manifest` 建议加一道形状自检(corpus 为空而输入非空时**报错**而不是抛异常)。