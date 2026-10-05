# REFACTOR-BATCH-R65 —— 实机数据把 R63 的 ×30 证伪:删掉错数、修探针、把口径限定住

日期:2026-10-06。插件 **1.7.16 -> 1.7.17**;`FA8E59188E0A2A5D00E07B27ACCD66B0F6535D01BCF3092A4C88216B7D818ABB`(454,144 B)。
起因:R64 部署后用户打了一场训练场 9999(44 秒),我读 `dpsmeter_runtime.log` 里的 **468 行 `[AUTOSK]`**。
这一场**同时**做成了两件事:给出「自动技能几发一次」的**实测答案**,以及**证伪 R63 的一条口径**。

本轮**不移动任何既有伤害/归属数值、不改导出形状**(`contribution.schemaVersion` 仍 1.2);
改的是**主数据转储的一列错数**、**一条被证伪的纯规则**、以及**探针自身的四个缺陷**。

## 0. 一句话

- **答案(实测)**:`[賢導]トレイラ` 的自动技能「暗沌への導き」充能 `CoolTimeFrame = 240` 单位 =
  **8.0 游戏秒**(率实测 **30.0 单位/游戏秒**);本场**实际发动间隔 15.20 / 12.03 游戏秒**
  (真实 10.18 / 8.08 秒)—— 充满后要等她**下一次普攻**(正文「次回攻撃時のみ」),所以间隔 > 充能。
- **R63 的 ×30 是错的**:`auto_skill` 的 `minCoolTime`/`maxCoolTime` **就是游戏自己的充能单位**。
  实测 9 个自动技能,`Skill.CoolTimeFrame` 与主表值**字面相等 9/9**,**没有一个**等于 ×30。
  于是 R63 导出的 `minCoolTimeFrames`/`maxCoolTimeFrames`(= 9000/7200)**是错数**,连同产生它的
  `src/Policy/SkillCooldownPolicy.cs` 一起**删除**。
- **R63 §3c 的「存疑」是伪命题,由我自己的单位错造成**:那个 ≈13.5/15.9 出自导出 `events[].t`,
  是**游戏钟的秒**。本场实测 15.20/12.03 游戏秒 —— **同一对交替值**。旧结论本来就对。
- 探针修掉 4 个缺陷(游戏 index 的 0/1、垃圾槽挤掉真槽位、`poll` 噪声被当成发动、`chargeSec=0.0s` 误导)。
- 行为用例 **1016 -> 1002**(删 16 例 `policy/skill-cooldown`、自动技能组 29 -> 31 例,组数 102 -> 101)。
- 变异负控 **148 -> 146**(删 2 条 `skill-cooldown-*`;`autoskill-charge-forgets-the-clock` 改指新用例名)。
- 验收归档 `_dpsm_work/acceptance_r65/`(`runs.json` 44 条 + `corpus_manifest.json` 76 条检查 + `RESULTS.md`);
  **44 条命令 / 76 条检查 / 0 项**,第一次跑就是 0 项。

## 1. 先让它可复核:这一场的硬数字

日志:`BepInEx\config\dpsmeter_runtime.log`,任务 9999,时长 44 秒,`[AUTOSK]` 468 行
(`chg=270 / act=197 / SUM=1`,其中 `actCmd=58`、`actPoll=139`、`players=826`、`droppedSlots=9`)。

**充能率**(只取计数器真的在下降的 84 个样本):

| 量 | min | 中位数 | max |
|---|---|---|---|
| 单位 / **游戏**秒 | 0.7 | **30.0** | 30.0 |
| 单位 / **真实**秒 | 1.0 | **44.5** | 45.3 |

比值 1.483 = 游戏钟跑真实时间的 1.5 倍(与 `[CLOCKP]` 一直报的一致)。**30 单位/游戏秒得到独立复现**:
这次不是从主动技能的 `CoolTimeFrame/CoolTime` 推的,而是从自动技能自己的充能计数器量出来的。

**9 个自动技能:主表 vs 线上**(这是把 R63 那条口径按在地上的一表):

| unit | 技能 | 线上 `Skill.CoolTimeFrame` | 主表 min/max `CoolTime` | 关系 | ÷30 = 游戏秒 |
|---|---|---|---|---|---|
| レヴナント | 神経の暴走 | 150 | 210/150 | 字面 = max | 5.00 |
| T.O.W.E.R.typeR | 電触補壁 | 300 | 420/300 | 字面 = max | 10.00 |
| ポポロット | 白眼視の瞬き | 210 | 270/210 | 字面 = max | 7.00 |
| ソフィー | 狂骨の祝采 | 420 | 420/300 | 字面 = min(未满级) | 14.00 |
| メルティエル | 邪龍の息吹 | 240 | 360/240 | 字面 = max | 8.00 |
| **[賢導]トレイラ** | **暗沌への導き** | **240** | **300/240** | **字面 = max** | **8.00** |
| マッドシーカー | 実験失敗！ | 2970 | 2970/2970 | 字面 | 99.00 |
| ムスクーマ | 万物不浄の昇華 | 210 | 270/210 | 字面 = max | 7.00 |
| ネフェスティス | 緑炎の生気吸収 | 240 | 300/240 | 字面 = max | 8.00 |

旁证:`マッドシーカー` 主表 2970。当**秒**是 49 分钟(荒谬);当**单位**是 99 游戏秒(≈66 真实秒),
合理 —— 而且本场 44 秒里它**一次都没发**(`wait` 从 2940 单调降到 1662,`no reset seen`)。

**トレイラ 的发动时刻**(从 `wait` 跳回满格定位;游戏钟):

    发动 @ active = 6.27 / 21.47 / 33.50 游戏秒
    → 间隔 15.20 / 12.03 游戏秒  =  真实 10.18 / 8.08 秒
    充能 240 ÷ 30 = 8.00 游戏秒  ⇒  充满后还要等 4~7 游戏秒才打出去(等下一次普攻)

**与 R63 的旧反推对照**(`tests/_as26.tmp.txt`,源是导出 `events[].t` = 游戏秒):

    seq 009 key 16 : [8.57, 22.1, 37.97]  gaps [13.53, 15.87]  (game s)
    seq 011 key 7  :                       gaps [13.5, 15.97, 13.23, 12.8]

`{13.53, 15.87}` 与 `{15.20, 12.03}` **是同一对交替值**。所以旧结论没错,错的是我 R63 拿它去对
「240–300 **秒**」——两边单位不同。**R63 §3c 那两条「可能」都不成立;真正的第三种是「我把主表的单位认错了」。**

## 2. R63 到底错在哪(要写清,别再犯)

1. R63 §1 从**主动技能**读出 `Skill.CoolTimeFrame / Skill.CoolTime = 30`(750/25、1500/50、1050/35),
   这没错。错在**把结论外推**:`SkillMasterDataBase` 的 min/max 对**按表不同单位不同**,而
   `auto_skill` 这张表**压根没有** `coolTime` 字段,只有 min/max 对 —— 恰好是最不能外推的形状。
2. R63 于是让 `SkillCooldownPolicy.Frames(seconds, rate)` 去乘 30,把 300/240 写成 9000/7200。
   一个**没有被任何数据支持的乘法**进了产物。
3. R63 §3c 又拿这两个错数去否定一个**本来正确**的反推,把一个真结论判成存疑。
   **教训**:一条「单位规则」只能由**它自己那张表的实测**支撑;跨表外推必须写出反例检查
   (本轮的检查就是那一列 9/9 的「字面相等 vs ×30」)。

## 3. 改了什么

| # | 改动 | 文件 |
|---|---|---|
| A | **删**两列错数 `minCoolTimeFrames`/`maxCoolTimeFrames`;信封新增实测 `unitsPerGameSecond`(该除,不该乘) | `src/MasterData/MasterDataDump.cs` |
| B | **删** `src/Policy/SkillCooldownPolicy.cs` 及其 16 例用例、2 条变异 | 删除 |
| C | 探针:`gameIdx` 原样打印 + 解析方式进 `via=`(`cmd/named`/`cmd/rosterPos`);解析不到只记一次**不建槽** | `src/Diagnostics/AutoSkillProbe.cs` |
| D | 探针:槽位键改为**技能身份**(`EntryId\|name\|skillId\|name`)而不是槽号 | 同上 |
| E | 探针:`via=poll` 降级为 `via=usingEdge`,**不进任何间隔**;`chg` 加 `ct=`(`Skill.CoolTime`) | 同上 |
| F | 探针:`chargeSec` 只在真的在充能时打印(回跳样本不再打 `0.0s`);占位槽打一次后静音;`MaxSlots` 24→64 | 同上 |
| G | 探针:每单位每场一行 `[AUTOSK] roster`(`PassiveSkills` 的 `Index`/`AutoSkill`/`type`/`wait/cool`) | 同上 |
| H | 用例:充能秒数的六例改成**实测值**(240/150/210/420/2970 → 8/5/7/14/99 游戏秒)+ 真实秒更短一例 | `tests/BehaviorTests/Cases.AutoSkillCadence.cs` |
| I | 删 `Cases.SkillCooldown.cs`、编译清单、`Program.cs` 调用;钉住 **1016 -> 1002** | 同上 + `BehaviorTests.csproj` + `Program.cs` |
| J | 变异负控:删 2 条 `skill-cooldown-*`;`autoskill-charge-forgets-the-clock` 改指新用例名 | `tests/negative_control.py` |

**为什么删而不是改**:`SkillCooldownPolicy` 是**纯函数**,算术本身没错(秒→帧)。但它**唯一的调用点**
被证伪了,留着一个没人用、却被 16 例测试和 2 条变异守护的函数,等于给下一个读代码的人留一句
「主表应该是秒的,这里有现成的换算」。**被证伪的知识要连载体一起删掉**,替换它的知识
(「除以实测速率」)已经由 `AutoSkillCadencePolicy.SecondsFor` 承担并有用例。

### 3a. 探针那 4 个缺陷有多实在(都用本场数据量过)

| 缺陷 | 本场实测 | 后果 |
|---|---|---|
| 游戏传的 `index` 是 **0 或 1**,我只认 1/2 | `idx=0` 的 cmd 行 **32** 条(全部 `skill=-`) | 建了 **9 个垃圾槽**,吃掉 `MaxSlots=24` 的额度 ⇒ `droppedSlots=9`,**18 个单位只读到 9 个** |
| `Using` 上升沿被当成一次发动 | トレイラ `medianWall=1.10s`、9 次「发动」 | 而真实间隔是 8.08/10.18 s —— **一条噪声被当成结论** |
| 槽位键用槽号 | `T.O.W.E.R.typeR` 同一槽读到 `CoolTimeFrame` 420(cmd)与 300(chg) | 两个不同的 `Skill` 被并成一行,看起来自相矛盾 |
| 回跳样本也打 `chargeSec` | 反复出现 `chargeSec=0.0s` | 读起来像「冷却 0 秒」,实际是「这次采样测不出速率」 |

`gameIdx=0` **到底选的是哪个技能,R65 没有答案** —— 所以本轮不猜:原样打印 `gameIdx=`、把用过的解析
方式写进 `via=`、解析不到就计入 `cmdUnresolved` 并只写一行,同时每单位打一行 `roster`,把
`PassiveSkills` 的 `Index` 与 `AutoSkill` 摆出来。**下一次实机日志会直接回答它。**

## 4. 验证(本机实测)

| 项 | 结果 |
|---|---|
| 构建 | **0 警告 0 错误** |
| 行为套件 | **1002 用例 / 101 组 / 0 失败**(钉住 1016 -> 1002) |
| 变异负控 | **146 例 / 0 失败**(删 2 条 `skill-cooldown-*`,剩 4 条 `autoskill-*` 逐条确认红在具名用例) |
| 文档收敛 | **12/12**(版本 1.7.17 / 哈希 `FA8E5918` / 语料 35 / schema 1.2 / 验收计数) |
| 文档解码 | `check_docs_123.py` **99 文件 / 0 损坏**(新批次记录已登记进 FILES) |
| 契约与工具 | `check_extract_contract` PASS / `check_tool_registry` PASS(100 登记 / 31 活跃 / 0 未判定)/ `repo_manifest --verify` drift=0 |
| 验收 | `n0_acceptance.py --out acceptance_r65` = **44 条命令 / 76 条检查 / 0 项**(第一次跑就是 0 项) |

## 5. 覆盖不到的(必须写下来)

- **`AutoSkillProbe.cs` / `MasterDataDump.cs` 仍不在 BehaviorTests 的编译清单里**(需要 IL2CPP 面),
  所以本轮改的**探针与转储代码没有行为测试**;被覆盖的是纯规则(`AutoSkillCadencePolicy`)与
  「成员名正确」这个由编译器保证的性质。
- **删掉的 16 例是「主动减少覆盖」**,不是丢失:它们守护的规则本身被证伪。用例总数从 1016 降到 1002
  是**有意的**,记录在此以便日后核对(数字下降本身不构成缺陷)。
- `unitsPerGameSecond` 是**进程实测值**,写在 `auto_skill.json` 的信封里;若某次启动没测到
  (TimeProbe 还没看到可用的比值),它写策略默认 **30.0** —— 这个回退是**实测常量**,但读者仍应以
  `[CLOCKP]`/`[AUTOSK] chg` 的 `upsGame` 为准。
- `gameIdx=0` 的语义仍开放(见 §3a);`T.O.W.E.R.typeR` 的 420/300 也仍未解释(可能是同名不同
  `EntryId` 的两个对象,也可能是槽位技能真的会换)。这两件都靠下一场日志的 `roster` 行与新的
  技能身份键来分。
- 本轮**没有**动 `contribution`/导出形状,`schemaVersion` 仍 1.2;`masterdata/auto_skill.json`
  的形状变了(少两列、多一个信封字段),它不在任何导出 schema 守卫里(已核 `check_extract_contract.py`
  只数 `masterdata/*.json` 的个数)。

## 6. 部署

`5F9234EF`(451,072 B,1.7.16)-> **`FA8E5918`**(454,144 B,1.7.17);
替换前确认**游戏未运行**且目标文件未被锁定;旧 DLL 备份 `_dpsm_work/deploy-backup/pre-r65-5F9234EF/`
(哈希复核 = `5F9234EF…`);两个 crash `.bak` 复核未变(`2E1819F2…`、`F7FF1EB8…`);
部署后 `repo_manifest.py --write --exports batch_inputs/rf0` 重新记录(**必须在 `_dpsm_work` 下跑**,R63 §6b.1)。

**回退**:一条 `Copy-Item` 把 `pre-r65-5F9234EF/DpsMeter.dll` 覆盖回 `BepInEx/plugins/DpsMeter/DpsMeter.dll`。

## 7. 下一步(R66 候选)

1. **关掉 `gameIdx` 的悬案**:下一场读 `[AUTOSK] roster` + `cmdunres` 行,把游戏那个 `index` 的语义
   定死(被动槽位?0-based 槽号?),然后只留一种解析。
2. **把 `check_export_schema.py` 那种「恒等式守卫」用到主数据转储上**:本轮的错误是「一列数与它对不上的
   实测既没被检查也没被记录」,而一条「主表值与线上值必须字面相等或有文档化的换算」的自检本可以当场抓住它。
3. 「单位规则必须由本表实测支撑」这条教训,写进 `CONTRIBUTION-DATA-DICTIONARY.md` 的实践条目(与
   R63/R64 那两节并列)—— **本轮已做**,见该文件 `##### R65` 末尾的「实践规则」段。

## 8. 归档与计数

- 行为用例 **1016 -> 1002**(删 16 例 `policy/skill-cooldown`、自动技能组 29 -> 31 例,组数 102 -> 101)。
- 变异负控 **148 -> 146**(删 2 条 `skill-cooldown-*`;`autoskill-charge-forgets-the-clock` 改指新用例名)。
- 验收归档 `_dpsm_work/acceptance_r65/`(`runs.json` 44 条 + `corpus_manifest.json` 76 条检查 + `RESULTS.md`);
  **44 条命令 / 76 条检查 / 0 项**,第一次跑就是 0 项。
- 源码规模 `98 -> 97 个 .cs`(`SkillCooldownPolicy.cs` 删除)、守卫口径 `135 -> 133`。
