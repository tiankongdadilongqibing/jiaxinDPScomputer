# REFACTOR-BATCH-R67 —— 用户截图核对出的两处版面错话 + 奥义/特殊换一条能用的通道

日期:2026-10-06。插件 **1.7.18 -> 1.7.19**;`5BA37D6C896E65246A415C43D968E2944F06480965996317C47D70246A36E389`(472,576 B)。
起因:R66 部署后用户打了一场训练场 9999,截下 F4「技能时间表」要求核对。核对结论(见 §1):数字**全部**
对得上,但页面有**两句错话**,而且奥义那一列的通道**根本没被调用**。本轮按用户「继续推进 R67」修这三件事。

本轮**不移动任何伤害/归属数值、不改导出形状**(`contribution.schemaVersion` 仍 1.2)。

## 0. 一句话

- **`xN` 改成 `并N条M格`**:R66 的 `x(合并数+1)` 被图例读成"N 次并进同一格",实测 5 个带标记的行里
  **3 个是假的**(メアリー `x3` 实为两格各并 1 条:49.10+49.10、79.30+79.37;ネーフェ、ルナリス 同理)。
  策略层现在给每个格子记**倍数**(`Multiplicity`),文本层印「并进几条、落在几格」两个数。
- **时间格 5 → 6 列**:截图里 `96.2107.3118.3` 是三个时刻黏在一起("107.3" 恰好占满 5 列)。
  每行格数 12 → 9,行宽 124 → 121,面板宽度不动。
- **奥义/特殊换通道**:R66 挂的 `AddPlayerSkillGameRecord` **装上了但一整场都没被调用**
  (`rec=0`,且 `foreignSide/nonStart/nullSkips/noSession` 全 0 —— 连敌人的记录都没进来)。
  R67 改挂与已证实的自动技能命令同一层的三个入口:`GameCmdExecuter.ActExecutePlayer{ActiveSkill,Skill,SpecialSkill}`
  (新通道 `skl`),并在只读采样里加上 `Player.ActiveSkill`/`SpecialSkill`(槽 10/11)读出它们的 `Skill.Type`。
- **`gameIdx` 悬案结案**(R65 §7.1):见 §2。计数器 `indexAmbiguous` 改名 `indexReadingsDiffer`。
- 行为用例 **1076 -> 1090**;变异负控 **160 -> 165**。

## 1. 核对记录(R66 那一场,训练场 9999)

| 核对项 | 页面 | 独立重算(97 行 `[AUTOSK] act`) |
|---|---|---|
| 原始 / 行 / 格 / 合并 | 97 / 7 / 88 / 9 | 97 / 7 / 88 / 9;`[AUTOSK] SUM actCmd=97` |
| 每行时刻、`n=` | — | 逐个相等 |
| `med` | 7 行 | 无连发的 4 行与 `[AUTOSK] medianActive` 相同;有连发的 3 行不同(页面是**合并后**中位,SUM 是**未合并**中位 —— 两个不同的量,R67 图例写明) |
| 截图 ↔ 日志 | — | 与 `[SKILLTL] panel` 行逐字符一致 |

メアリー 的 `med=10.13` 与我用日志两位小数重算的 10.14 差一个末位:日志只打 2 位,页面用全精度时钟,属打印精度。

## 2. `gameIdx` 的语义(R65 §7.1,结案)

R66 第一次打出 `[AUTOSK] cmdidx` 与 `inst=`:

- `gameIdx=0`:只有 roster 读法能解(`PassiveSkills[0].AutoSkill`),`named=-`;
- `gameIdx=1`:只有 named 读法能解(`Player.AutoSkill1`),它的 roster 读法落在**无名占位符**(`unnamed#k+1`);
- `inst=`:每个单位的真实自动技能**只有一个对象**(tag 1/3/5/7/9/11/13),占位符各是独立对象(2/4/…/14)。

⇒ 两处调用点用两套约定(0 基被动位 / 1 基槽位),**指向同一个对象**。解析器原有的顺序(1/2 用 named、
其余用 roster)是对的;`indexAmbiguous` 改名 `indexReadingsDiffer`,只记录"错误读法会不一样"的次数。
R64 那场 420/300 的"两个对象"是升级瞬间的两个**等级**对象,与此无关(R66 §2a)。

## 3. 改了什么

| # | 改动 | 文件 |
|---|---|---|
| A | `SkillTimelineGroup.Multiplicity`(每格倍数)+ `BurstCells` | `Policy/SkillTimelinePolicy.cs` |
| B | 尾部 `并N条M格`、图例改写(并说明 med 是合并后的)、`StampW` 6、`TailW` 25、`MaxStamps` 9 | `Ui/SkillTimelineText.cs`、`Policy/SkillTimelinePolicy.cs` |
| C | 第三条通道 `skl`:`ChannelLine(cmd, skl, rec)`;"奥义两条通道都是 0"才警告 | `Ui/SkillTimelineText.cs`、`Model/SkillTimelineEvent.cs` |
| D | 三个命令入口后缀(各自独立 try 注册;位置参数不声明) | `Hooks/SkillRecordHooks.cs`、`Plugin.cs` |
| E | `NoteSkillCommand`:按入口计调用次数、拒绝/非我方/槽空分别计数;发动后**回读**槽位并用 `Skill.Type` 标种类(不假设"active = 奥义");`[SKILLTL] skl` 行 | `Diagnostics/SkillTimelineProbe.cs` |
| F | 采样器加 `ActiveSkill`/`SpecialSkill`(槽 10/11,只读,不计入自动技能的 `live`) | `Diagnostics/AutoSkillProbe.cs` |
| G | `indexAmbiguous` -> `indexReadingsDiffer`(含 SUM 行键名) | 同上 |

## 4. 验证

| 项 | 结果 |
|---|---|
| 构建 | 0 警告 0 错误(`ActExecutePlayer*` 签名、`Player.ActiveSkill/SpecialSkill` 由编译器核对) |
| 行为套件 | **1090 用例 / 103 组 / 0 失败**(钉住 1076 -> 1090) |
| 变异负控 | **165 例 / 0 失败**(新增 5 条、改锚 4 条,逐条确认红在具名用例) |
| 文档收敛 / docs123 / 契约 / 工具 / 清单 | 全过(见验收) |
| 验收 | `acceptance_r67` = **44 条命令 / 76 条检查 / 0 项** |

## 5. 覆盖不到的

1. **`skl` 通道是否命中仍要一场实机**。和 R66 不同的是:SUM 行现在**按入口**打印调用次数
   (`sklCalls(active/skill/special)=a/b/c`),若三个都是 0,结论就是"这一层也不跑",不会再和"没发动"混淆;
   若调用次数大于 0 而 `skl=0`,`sklRejected`/`sklNoSkill`/`foreignSide` 会说明是哪一步丢的。
2. **"active 入口 = 奥义"是待测的,不是假设**:行的种类来自发动后回读的 `Skill.Type`;
   `[SKILLTL] skl` 行与槽 10/11 的 `[AUTOSK] chg` 行把这个映射摆出来。
3. 探针、钩子与渲染仍不在 BehaviorTests 的编译清单里(需要 IL2CPP/Unity 面)。

## 6. 部署

`D17400F0`(468,480 B,1.7.18)-> **`5BA37D6C`**(472,576 B,1.7.19);游戏未运行、文件未锁定;备份
`_dpsm_work/deploy-backup/pre-r67-D17400F0/`(哈希复核);两个 crash `.bak` 未变;部署后 `repo_manifest --write`。

**回退**:`Copy-Item _dpsm_work\deploy-backup\pre-r67-D17400F0\DpsMeter.dll BepInEx\plugins\DpsMeter\DpsMeter.dll -Force`

## 7. 下一步(R68 候选)

1. 读下一场的 `[SKILLTL] SUM` 的 `sklCalls`,定 `skl` 通道是否可用;读槽 10/11 的 `type=` 定"哪个槽是奥义"。
2. 读 `act=`/`actP=`(R66 加的 `Skill.ActivationType`),回答"什么触发自动技能"(R66 §2b 的问句)。
3. 若 `rec` 在非训练场任务里有调用,可保留它做第二通道;否则删掉 R66 的记录钩子(不留没用的观测点)。

## 8. 归档与计数

- 行为用例 **1076 -> 1090**;变异负控 **160 -> 165**;源码 102 个 .cs(本轮无新增文件)。
- 验收归档 `_dpsm_work/acceptance_r67/`(**44 条命令 / 76 条检查 / 0 项**)。
