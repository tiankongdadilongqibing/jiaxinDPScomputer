# DpsMeter 项目总方向与实施计划

> 用途：作为后续智能体分工、实现、审查和验收的统一依据。
> 当前基线：**源码/构建/线上 1.7.31**(R81:给受击来源拆分页的六个子表标签行各一种鲜艳颜色 —— 只改显示层,页面字符串/布局/导出/数值一字未动);上一版 **1.7.30**(R80:修**受击来源拆分的两个可用性缺陷**并顺带修掉一个**把 1.7.29 的导出整个写坏**的真缺陷 —— ①战斗结束后仍能看:非战斗中用 `Aggregator.History[0].Session`(最近一场已结束战斗)而不是打空态,且只算一次;②不再省略:删掉 `TakenBreakdownPolicy.MaxBuckets = 64` 的折叠与页面的三个上限(12 个受害单位 / 每行 3 个 part / 每单位 5 条维度行),改成**每个受害单位的每个维度一张子表**(报桶数 + 列头 + 每桶一行 + `合计`,超出部分向下无限扩展),分组改走每个受害单位一个 `BucketIndex` 字典索引;③段自述 `takenBreakdown.schemaVersion` 1.0 → 1.1(唯一差别 = 维度不再有桶上限),`contribution.schemaVersion` 仍 **1.2**、**已发布数值与键名一字未动**;④**R79 写出的导出整体不是合法 JSON** —— `src/Output/TakenSession.cs` 六处把 `JsonText.Str(...)` 直拼进对象而它不加首尾引号,`"name":T.O.W.E.R.typeR` 这种裸值让任何解析器都失败(冻结语料 35 份全早于 R79、不含这一段 ⇒ R79 全部闸门对此是盲的,线上语料 schema 检查抓到);现在六处补引号并新增 `tests/BehaviorTests/Cases.TakenExport.cs` 用 `System.Text.Json` 回读字节钉死;⑤滚动新增 Home/End,F3/Shift+F3 交互不变。用例 1439→1460、变异 244→248、守卫口径 159→160、源码 120 个 .cs / 31,887 行。上一版 **1.7.29**(R79:新增**受击来源拆分**(每个单位挨的伤害分别来自谁/什么):受害侧每次命中按**游戏口径** `Nominal` 登记一行 `TakenHit`,站位在**首次受击**时快照,`src/Policy/TakenBreakdownPolicy.cs` 给出 `byAttacker`/`bySource`/`byHitType`/`byEffect`/`byStatus` 五个各自完整划分同一笔 `nominal` 的维度,并把「攻击者不明」「同队自伤」两条独立通道与 `taken + residual == nominal` 一起钉成恒等式(行为用例 + 导出守卫双重);F3 页面 80 列、最多 12 个受害单位、每单位至多 5 条维度行,口径行明写 `超出剩余耐久 = 前者-后者,不是被吸收`;导出新增顶层段 `takenBreakdown`(`schemaVersion 1.0` / `by-event/1` / `basis nominal`),`contribution.schemaVersion` 仍 1.2、**已发布数值一字未动**;用例 1328→1418、变异 233→244、源码 113→120 个 .cs,线上 `exports\` 已 145 份。上一版 **1.7.28**(R78:**给两个词正名** —— 面板 `· 实际伤害 N` 在溢出命中上印的其实是**溢出量**(`BattleObject.Damage` 的返回值,R76 定律 798/798),`· 被吸收/无效化 M` 印的其实是**目标剩余耐久**(`nominal − res`);现在普通命中印 `· 游戏口径 N`、溢出命中印 `· 超出剩余耐久(溢出) A · 目标剩余耐久 B(…非吸收…)`,运行日志 `[ABSORB]` → `[OVERFLOW]`,并解除 `if (absorbed > 0) … else if (theory > 0) …` 的互斥、残差分子改用游戏口径,使会心 ×1.5 的超量命中行同时印出 `×1.500` 与溢出量(旧版只剩 `×0.242` 假异常倍率);关键判定后新增 `[ABSPROBE] note`。新增纯策略 `src/Policy/AbsorbWording.cs`(141 行)+ 新用例文件(18 例)。**不改判定层、不改记账口径、不改导出键与数值**(schemaVersion 1.2,143 份历史导出一字未改);未做 C 档(改 `Hooks/BattleObjectHooks.cs:51`)与面板侧耐久变化显示。用例 **1310 → 1328**、变异 **227 → 233**;见 `REFACTOR-BATCH-R78.md`)。上一版 **1.7.27**(R77:**报告层两处读数缺陷** —— `[ABSPROBE] sum` 的 `ovz=<次数>/<落地>/<溢出>` 原本把两个族群混成一行(金额对**所有** `IsOversized()` 累加、次数只对生命佐证成立的 `Oversized` 计数),2026-10-07 那场因此读出 `ovz=0/6500000/2358285` 却配 `ovzPool=13`(同一行自相矛盾);现在 `ovz=` 的次数与金额同族群、另立 `ovzAll=`(全部 `Oversized*` 族群的独立计数器,恒等于四族群之和),`none=`/`full=` 补上过去只能**靠减法**推的落空判定计数(`full=` **只**认量测到 `lifeDrop == nominal` 的整击落地),顶层 `pool=`/`partial=` 改名 `ovzPool=`/`ovzPartial=`。**不改判定层、不改记账口径、不改导出形状**(schemaVersion 1.2)。用例 **1307 → 1310**、变异 **223 → 227**;见 `REFACTOR-BATCH-R77.md`)。上一版 **1.7.26**(R76:**超量命中单独判定** —— 把「被吸收/无效化」从**推断**改成**判定**。R75b 的两场实机定出定律 `res == max(0, nominal − lifeBefore)`,800 条探针读数里 **798 条可读的全部成立、0 违例** ⇒ 游戏 `BattleObject.Damage` 的返回值是**溢出量**,插件 `被吸收 = nominal − res` 在超量命中上恰好等于**目标剩余 Life**,两个已发布字段**互换**。现在 `res > 0` 自成一族 `Oversized*`(**落地 = `nominal − res`、溢出 = `res`**),**以 Life 移动为准**(`lifeDrop == nominal` ⇒ 整击落地,假阳性桶 `masked` 790 行删除);固定池对象(`ショゴス` 的 `Life` 恒 500,000、`inv=1`)单列 `OversizedPool`;关键行另有**独立行预算**(`Debug/AbsorbProbeKeyRows`,默认 200)。**不改已发布的承伤数值**(影响面 21 + 408 条、单次最大 269,104,留待 R77)。用例 **1298 → 1307**、变异 **218 → 223**;见 `REFACTOR-BATCH-R76.md`)。上一版 **1.7.25**(R75:**给「被吸收/无效化」加了一台「先读后命名」的探针** —— 该标签自 1.5.5 起只是 `nominal − damage` 的别名,却连着被当成机制读:46 份导出 **397 条**被吸收记录、**395 条差额恰好 500,000**、受害方**全是 `ショゴス`**,而 20 张主数据表里**没有任何字段/行/值等于 500,000**,boss 的运行时天赋只有 `1002 ModeChange` + `6 攻击力/150/-1`(无 `1007 伤害吸收`/`1042 护盾`/`1006 被伤害-`)。现在每场在既有 `BattleObject.Damage` 前后读受击方 `Life`、`Character.Barrier`(`IsActived`/`mLife`)与五个无敌族标志,把差额**分类**成 `barrier/pool/unknown/takeover/fixed/invincible/masked/unreadable`,战末打一行 `[ABSPROBE] sum`;**不改记账口径/显示层/导出形状**,障壁类 8 个 Harmony 补丁**默认关闭**(`Debug/AbsorbProbeHooks=false`)且**不进 `PatchAll`**;同时纠正上一轮的错误结论(「500,000 与伤害大小无关」**不成立**:`(__result > 0) ? __result : __0` 会把整击被吸收记成满额伤害,故 397 条只是子集,新增 `masked=` 桶量它)。用例 **1275 → 1298**、变异 **211 → 218**;见 `REFACTOR-BATCH-R75.md`)。上一版 **1.7.24**(R74:**修「测量仪」的读数单位** —— 你第三场还是「初动 6 秒的技能 5 秒就显示放出来了」;前两轮都没错在算术:**校准器把 `Skill.FirstCoolTime`(秒)当成单位**去和 `WaitCountFrame` 比,`waitFrames > firstCoolFrames` 把每个槽、每一帧都拒掉 ⇒ `samples` 恒 0 ⇒ 原点永不 decided ⇒ `[CLOCK] origin=none` 是每场的必然。现在①初动改读**单位制**的 `Skill.m_data.m_firstCoolTimeFrame`(R73 以为它挂在 `Skill` 上,被编译器纠正:那只在**主数据**类上),秒值降级为**记录在案的退路**;②拒绝理由**具名** `LagReason`;③新增 `[CLOCK] calib` 行(attempts/party/slots/usable/via(field/fallback)/first(sec/frame/wait)/rejected(noFirst/wait/units/window/range))—— 因为决定帧**必然**过不了窗口条款、原来的 `samples=0` 天然为 0;④技能时间线进证据包 `skill_timeline.txt`(manifest 里校验),不再只能靠会滚掉的运行日志复核;`[AUTOSK] chg` 增 `first=`/`firstFrame=` 两读数并列,下一场一并定案「6 还是 0」「初始值还是实时剩余」。用例 **1254 → 1275**、变异 **203 → 211**;见 `REFACTOR-BATCH-R74.md`)。上一版 **1.7.23**(R72:**让 R71 的原点修正真正落地** —— 你贴图说「自动技能的发动时间统计得还是比初动时间早,**完全没有改变**」;R71 算对了 +0.90 s 却每场都停在 `[CLOCK] origin=none reason=events`(它只从本场第 2 帧起尝试对齐,而游戏在**创建 session 的同一帧**就打出了开局伤害)⇒ 一次也没应用。现在:①**在创建 session 的当场**尝试对齐;②证据来晚时把开局伤害/治疗**先扣住**(纯策略 `ClockOriginHoldPolicy`,有界 256、溢出即**拒绝** `reason=hold`)再按**到达时刻 + 决定的 lag** 重走记录器;③页面戳/充能采样器/三个诊断导出/`AttackSnapshot.At` 按同一常数搬一次;④`[CLOCK]` 行每条分支都带 `samples=/active=/hits=/held=`。用例 **1205 → 1254**、变异 **189 → 203**;见 `REFACTOR-BATCH-R72.md`)。上一版 **1.7.22**(R71:**战斗钟的原点** —— 你问「大部分自动技能的最早发动时间小于技能的初动,是不是开局少算了一秒」;**是,正好 0.90 s:速率没错、原点错了**;三条独立测量(自动技能充能计数器 0.90–0.93 s 恒定 / 游戏自己的屏幕倒计时不变量 / 页面最早发动对照主表 `FirstCoolTime`)一致,顺带量到「槽计数器开局初始化为 `FirstCoolTime`、发动后重置为 `CoolTime`」;按用户选的**方案 B**:开局第一次采样时一次性把 `ActiveSeconds` 前移实测 lag,边界在纯策略 `BattleClockCalibrationPolicy`,拒绝理由写进 `[CLOCK] origin=none reason=…`;页面/日志/导出时间轴整体 +0.9 s,**旧文件沿用旧原点**,按秒桶边界平移;见 `REFACTOR-BATCH-R71.md`)。上一版 **1.7.21**(R70:时间表**只统计我方**(三路统一团队判据 `SkillSidePolicy`)+ 一行的发动时刻放不下就**往下撑**;见 `REFACTOR-BATCH-R70.md`)。上一版 **1.7.19**(R67:用户截图核对 R66 的时间表 —— 连发标记改 `并N条M格`、时间格 5→6 列,奥义/特殊改挂 `GameCmdExecuter.ActExecutePlayer{ActiveSkill,Skill,SpecialSkill}`(R66 的记录钩子一整场未被调用),`gameIdx` 结案;见 `REFACTOR-BATCH-R67.md`)。上一版 **1.7.17**(R65:实机数据把 R63 的 ×30 证伪 —— `auto_skill` 主表的 `minCoolTime`/`maxCoolTime` **就是游戏自己的充能单位**(线上 `Skill.CoolTimeFrame` 与主表值字面相等 9/9),**删掉** `minCoolTimeFrames`/`maxCoolTimeFrames` 两列错数与产生它们的 `SkillCooldownPolicy`(+16 例用例/2 条变异),信封改给实测 `unitsPerGameSecond`(该除不该乘);同时**给出答案**:トレイラ 的「暗沌への導き」充能 240 单位 = **8.0 游戏秒**,实际发动间隔 **15.20 / 12.03 游戏秒**(真实 10.18 / 8.08 秒,充满后等下一次普攻);R63 §3c 那个 ≈13.5 s 的「存疑」**是伪命题**(它是游戏钟的秒,本来就对);探针修 4 处 + 新增 `[AUTOSK] roster`;见 `REFACTOR-BATCH-R65.md`;当前语料 82 份)。上一版 **1.7.16**(R64:把自动技能的**运行时**读出来 —— 新增只读探针 `AutoSkillProbe`,从**线上 `Skill` 实例**侧把**发动时刻**(`[AUTOSK] act`)/**充能轨迹**(`[AUTOSK] chg`)+ 收尾中位间隔(`[AUTOSK] SUM`)写进运行日志,换算抽成纯规则 `AutoSkillCadencePolicy`;**不动任何既有数值、不改导出形状、不新增导出根段**;见 `REFACTOR-BATCH-R64.md`)。上一版 **1.7.15**(R63:新增「自动技能」主表 `auto_skill` 的转储(20 张表 / 输出 21 个文件)+ 冷却单位纯规则 `SkillCooldownPolicy`(⚠️ **R65 已删除,其 ×30 被实机证伪**);见 `REFACTOR-BATCH-R63.md`)。上一版 **1.7.14**(R62 A/B/C 同步:(1) 命中记录错配改「可证伪即拒绝」+ `hitMatch=3`/`hitDetail.matchRejected`;(2) 离线残差改游戏口径 `(applied+absorbed)/theory`;(3) 全局「敌方受伤」因子从 `dealtMult` 移入 `takenMult`,乘积不变;见 `REFACTOR-BATCH-R62.md`)。上一版 **1.7.13**(R61:同队/自我伤害移出归属池,`contribution.schemaVersion` **1.2**;见 `REFACTOR-BATCH-R61.md`)。上一版 **1.7.12**(R60:自伤三件套 —— 根 `config.filterFriendlyFire`、`contribution.actors[].friendly/friendlyHits/hostileDamage`、F5 表 1 恢复「自伤」列;见 `REFACTOR-BATCH-R60.md`),**线上 DLL 仍是 1.7.11**(R57 的引用绑定 + 战斗编号 + 精确选场);1.7.11 是显示级发布 —— F5 表 1 改为 `自身 | 他人因你 | 被队友分走`,并新增两条逐角色恒等式与「渲染器↔副本」版面守卫,见 `SESSION-STATE.md` §7.2.100)。本计划依据 DpsMeter-文档索引.md、_dpsm_work/HANDOFF.md 和 _dpsm_work/ARCHITECTURE.md 整理。
> 证据等级：实测、离线重放、推断必须明确区分。
> **进度(2026-10-03 深夜)**：阶段 **A / B / C / D(雏形)/ E** 已交付。
> A/B/C:指标字典 `_dpsm_work/CONTRIBUTION-DATA-DICTIONARY.md`、离线核心 `_dpsm_work/contrib/`
> (13 模块 + S1–S13 + golden 回归)、actor key 加固(byUnit 歧义 / 同名 / 召唤物 / 队外);
> D:单场报告 + 跨场比较(按 任务/版本/队伍 分组);
> **E:插件 1.6.0 已实现并构建(0 警 0 错),导出新增 `contribution` 段** —— 上线前离线一致性已证
> (插件真实源码跑真实导出 → schema 守卫 problems=0 + crosscheck 与 Python 核心双向一致 → DRYRUN=PASS),
> `recon_probe` 新增 40 余条断言全过,五守卫全 exit 0。
> **阶段 E 已上线并在实机验收通过(2026-10-04 02:19,插件 1.6.1 `C67055E6AE35…`)**:导出的 `contribution` 段
> 与独立 Python 核心**逐字段一致**(crosscheck status=OK/mismatches=0,11 角色/21 规则/20 关系);
> schema 守卫与 v150 problems=0。1.6.0 上线时抓到并修掉一个可复算性缺陷(导出 factor 截断 → 1.6.1 改往返精度),
> 详见 `SESSION-STATE.md` §7.2.87–§7.2.88。**阶段 F 第一步已上线(1.7.0)**:悬浮窗「总贡献看板」
> (角色级贡献 + 规则当量 top3,与导出同一条计算路径,每秒最多重算一次,不可用显示原因而非 0);
> 1.7.1 追加 **F5 独立表格页**(角色贡献/规则当量/辅助关系);剩余:明细页的规则级展示(覆盖率/证据等级)。
> **阶段 G 第一步已实现并上线(1.7.2→1.7.3,已部署)** —— 用户纠正:『ルナリス 的所有技能都为队友提供大量攻击力』。
> **母表实测推翻了我先前的判断**(§7.2.91):作用范围是能力天赋的 `range` 字段(`TalentDefine.RangeType`),
> 而导出从未携带它,所以「扫文本 → 没有全员加攻」是**用错字段证伪真命题**。正确证据:能力 #12060
> エンチャンター = talent 8(攻击力+)、range **29 = FriendTeamAllExcludeToken**、param [[300],[0],[200,3000]]
> (攻击力+300,参照 ExistenceTime3000);神器 水神クタアト +7%/+10%、クトゥルフの邪神像 +10% 为 range 3。
> 与导出逐条命中:非 token 我方 8 个单位都有 Attack/Actual/300,而火砲/两台炮台没有;
> range 3 的 +10% 连炮台都有 —— **两种范围给出两种「是否含使魔」的结果,全部对上**。
> 缺的不是「加成」,是「**谁给的**」:`ParamData.Owner` / `BuffParam.m_owner` 一直在手边,
> 但全 src 一次都没读过;旧 params 段还有两个硬伤(14 条静默截断、每单位只留最后一次采样)。
> **1.7.2 已加纯增量通道** `paramOwners`(同一遍历内读两侧 owner、并集、双预算、schema 版本门控 + 6 个新自测用例),
> 探针 143→156 条全过,五守卫 exit 0。**归属公式(P = B*(1+ΣRate)+ΣActual 为前提)是第二步**,待一场实测判定 owner 语义。
> 逐条证据与命令见字典 §9/§10 与 `SESSION-STATE.md` §7.2.82–§7.2.84。

## 1. 项目总方向

将 DpsMeter 从“伤害统计插件”发展为可审计的战斗贡献分析系统，回答：

1. 每个成员自己实际造成多少伤害？
2. 每个成员自身规则、天赋和状态带来多少额外伤害？
3. 每个成员为队友提供多少辅助伤害？
4. 每个成员的直接输出、辅助贡献和总贡献比例是多少？
5. 哪些角色、规则和队伍组合值得保留或替换？
6. 每个结论能否回溯到逐击事件、规则来源和原始导出？

核心原则：可复算、可追溯、不重复计数、证据分层、兼容历史、优先离线验证、渐进式演进。

## 2. 当前基线

### 2.1 已有数据链

游戏 Harmony Hooks -> Aggregator -> BattleSession / ActorStats / BattleEvent -> CalcBreakdown / FoldStep -> ExportService -> battle_*.json

关键模块：

- _dpsm_work/src/Aggregator.cs：会话、命中入库、攻击者归属、结算和导出触发。
- _dpsm_work/src/Model/BattleSession.cs：单场战斗状态。
- _dpsm_work/src/Model/ActorStats.cs：角色伤害、承伤、治疗、技能和时间序列。
- _dpsm_work/src/Model/BattleEvent.cs：逐击事件、攻击者、受击者、数值、来源和规则。
- _dpsm_work/src/Model/CalcBreakdown.cs：逐击计算和可识别倍率。
- _dpsm_work/src/Model/FoldStep.cs：倍率来源、因子、责任集合和取消记录。
- _dpsm_work/src/Output/ExportService.cs：正式 JSON 导出。
- _dpsm_work/src/Ui/OverlayUGUI.Rows.cs：游戏内统计行、详细行和图表布局。
- dpsmeter_contrib.py：当前 Stage 0 队伍贡献分析。

### 2.2 已经具备的能力

- 每击事实覆盖率接近 100%，FACT 类表无溢出。
- 逐击构成具备稳定规则来源链。
- 已支持 text、talent、global、given、madness 五类规则通道。
- 已能识别部分跨角色规则来源。
- 已有 byUnit 归因和队伍贡献报告。
- 已有对账、schema、FACT、版本验证和离线探针。
- 已有每角色直接伤害、每秒伤害、承伤、治疗和技能统计。

### 2.3 必须正视的边界

1. attackPower 内队友给予的攻击力加算已按 `kind=atkadd` 归属给出者（1.7.4+）；自己给自己的加算仍留在 baseCredit。
2. 模型仍有约 2%–2.9% 残差，主要涉及 1.15 倍规则和会心观测。
3. 会心主要依靠倍率推断，真实观测通道未稳定匹配。
4. totals.dealt 与逐事件求和存在小幅差异。
5. 训练场特殊 ×0.03 机制未纳入通用模型。
6. 贡献脚本需要全面改用稳定 actor key，而不是角色名。

## 3. 统一指标口径

### 3.1 第一版主口径

- 主伤害口径：dealt，即实际进入耐久的伤害。
- 对账口径：同时保留 dealtWithAbsorbed，但不得与主口径混用。
- 未解释残差：单独列出，不强行分给角色。
- 攻击力加算：队友给予的按 `kind=atkadd` 归属给出者（1.7.4+）；自己给自己的仍归攻击者基础部分，并明确标记。
- 治疗、减伤、控场：第一版作为附加指标，不折算进伤害总贡献。

### 3.2 直接输出

直接输出 = 角色作为攻击者产生的实际伤害。
直接输出比例 = 角色直接输出 / 全队 dealt。

### 3.3 逐击贡献

对于一击最终伤害 D：

- M = 该击已识别倍率的乘积。
- 基础部分 B = D / M。
- 倍率池 P = D - B。
- 某倍率因子份额 = P × ln(该因子) / ln(M)。

无法归因的规则进入未归因池，不得静默塞给攻击者。

### 3.4 总贡献积分

角色总贡献积分 = 自身基础部分 + 自身规则贡献 + 为其他角色提供的辅助规则贡献。

必须满足：

所有角色总贡献积分 + 未归因贡献 = 全队可分析 dealt。

### 3.5 必须并列输出两套排名

1. 直接输出排名：按实际伤害排序。
2. 总贡献排名：按可加和贡献积分排序。

这样可以区分纯输出角色、低直接输出高辅助价值角色、输出和辅助兼具的角色，以及依赖队友的角色。

## 4. 总体技术路线

阶段 A：冻结指标和数据契约
-> 阶段 B：重构离线贡献核心
-> 阶段 C：actor key 和归因正确性加固
-> 阶段 D：验证、报告和跨场比较
-> 阶段 E：贡献结果进入正式导出 schema
-> 阶段 F：战斗结束界面展示
-> 阶段 G：攻击力来源拆分和模型精度提升

原则：先离线、后插件；先数学恒等式、后 UI；先稳定口径、后增加复杂数据。

## 5. 分阶段实施计划

### 阶段 A：冻结指标和数据契约

目标：形成不依赖代码实现的贡献数据字典。

工作项：

1. 定义 directDamage、baseCredit、selfRuleCredit、assistCredit、totalCredit、directShare、totalShare、unattributedCredit、coverage。
2. 明确 dealt、absorbed、dealtWithAbsorbed 的使用场景。
3. 明确重复计算禁止规则。
4. 明确证据等级和展示方式。
5. 设计 contribution JSON 草案，但暂不改正式导出版本。
6. 准备三个手工样例：单人基础伤害、攻击者加辅助者、多倍率加未归因倍率。

交付物：

- _dpsm_work/CONTRIBUTION-DATA-DICTIONARY.md
- contribution JSON 草案。
- 手工样例及预期结果。

验收：每个指标有公式、分子、分母和可加和说明；禁止出现“总贡献 = 直接伤害 + 为团队赋能”的重复口径。

### 阶段 B：重构离线贡献核心

目标：将 dpsmeter_contrib.py 升级为可测试、可复用的分析核心。

建议模块：

- _dpsm_work/contrib/model.py
- _dpsm_work/contrib/loader.py
- _dpsm_work/contrib/attribution.py
- _dpsm_work/contrib/aggregate.py
- _dpsm_work/contrib/validate.py
- _dpsm_work/contrib/report_text.py
- _dpsm_work/contrib/report_json.py

工作项：

1. 分离事件加载、规则归因、角色聚合和报告输出。
2. 以 actor key 为主键。
3. 支持规则级归因明细。
4. 每击输出可审计 credit 列表。
5. 输出角色级、规则级和受益关系汇总。
6. 分类记录无 byUnit、队外来源、同名歧义、规则无法解析和模型残差。
7. 保留当前 Stage 0 文本报告作为兼容输出。

验收恒等式：

- 逐击 credit 总和 + 逐击未归因 = 逐击 damage。
- 角色 credit 总和 + 总未归因 = 全队可分析 damage。
- 角色 totalShare + 未归因 share = 100%。

### 阶段 C：actor key 和归因正确性加固

目标：防止同名角色、同名技能、召唤物和跨阵营单位错误合并。

工作项：

1. 使用 actors[].key 作为主键。
2. 事件通过 attackerKey、victimKey 连接角色。
3. 规则来源优先使用稳定 owner key，缺失时才名称回退。
4. 名称回退必须记录原因和证据等级。
5. 增加同名敌我角色、同队同种召唤物、同名技能多持有者、队外来源和无法唯一确定来源的测试。

验收：正常场景不依赖角色名唯一归并；所有回退都有计数；歧义不静默分配给错误角色。

### 阶段 D：验证、报告和跨场比较

目标：支持单场分析和同任务不同队伍比较。

报告必须包含：

1. 战斗概况。
2. 直接输出排名。
3. 总贡献排名。
4. 角色贡献构成。
5. 规则贡献排名。
6. 角色之间的辅助关系。
7. 未归因和残差。
8. 证据等级和数据质量。
9. 跨场比较。
10. 优化建议及适用边界。

验收：同时提供直接输出比例和总贡献比例；辅助贡献不重复计入；报告有覆盖率、未归因比例、版本、样本数和任务条件。

### 阶段 E：正式导出 schema

仅在阶段 B–D 验收后启动。

建议增加：

- contribution.schemaVersion
- contribution.method
- contribution.damageBasis
- contribution.totalDamage
- contribution.attributedDamage
- contribution.unattributedDamage
- contribution.coverage
- contribution.actors
- contribution.rules
- contribution.links

纪律：schema 变更必须升级版本；BuildInfo.cs 是用户可见版本唯一来源；旧导出仍须通过兼容检查；推断结果不能伪装成实测字段。

必须运行：

- Release 构建。
- recon_probe。
- check_export_schema.py。
- v150_validate.py。
- check_fact_signature.py。
- check_docs_123.py。
- refactor_final_check.py。

### 阶段 F：游戏内界面

实时页只显示：角色、直接伤害、DPS、命中数、直接输出比例。

战斗结束摘要显示：直接输出、自身规则贡献、辅助贡献、总贡献积分、总贡献比例、未归因比例。

详细页显示：规则名称、持有者、覆盖命中数、覆盖率、伤害当量、受益角色、证据等级。

主要扩展点：

- _dpsm_work/src/Ui/OverlayUGUI.Rows.cs
- _dpsm_work/src/Ui/OverlayUGUI.Pool.cs
- _dpsm_work/src/Ui/OverlayCore.cs

验收：不影响既有统计和详细事件；长名称不破坏布局；旧数据不可用时显示“不可用”而不是 0；实时页不执行高成本全量重算。

### 阶段 G：攻击力来源和模型精度

这是高价值但高风险的独立项目，不得阻塞第一版总贡献上线。

攻击力来源应逐步拆为：基础攻击力、固定加成、百分比加成、自身 Buff、队友 Buff、临时快照、无法读取项。

**进度(2026-10-04)**：第一步「队友 Buff 的来源」已落地为**只读通道** paramOwners（插件 1.7.2，已部署）：
在同一次遍历里读 ParamData.Owner 与 BuffParam.m_owner，按 (单位, target, type, value, owner, ref) 记**并集**，
双预算（每读 64 条 / 每场 20000 条）、schema 版本门控、探针 13 条新断言。**它不改任何 KPI**，只把「谁给的攻击力」变成可读数据。

第二步（归属公式）必须建立在**实测判定**的 owner 语义之上，不得先写公式再找证据。已定的形状：
每给予者 f_g = P / (P - dP_g)，其中 dP_g = B*(r_g/100) + a_g、B = (P-a)/(1+r/100)；
插入**既有** fold 列表后按 ln f / ln M 分池 —— 不新增 KPI 定义，owner 缺失时严格退化为无操作。
前提 P = B*(1+r) + a 是**推断**（本场实测 104 击 P<a、18 击 P==a，需 dP<P 守卫 + 计数）。
禁用条件必须先写好：P<=0 / B<=0 / dP_g<=0 / dP_g>=P / P<=a → 跳过该给予者并计数，绝不静默计入。

残差研究顺序：

1. 定位 1.15² 规则实际生效次数。
2. 寻找稳定会心观测通道。
3. 定位 totals 与逐事件求和差异。
4. 评估训练场特殊倍率独立模式。

## 6. 智能体分工建议

### Agent 1：指标和数据契约

负责阶段 A。只交付文档、公式、样例，不修改插件和正式版本。

### Agent 2：离线贡献核心

负责阶段 B。实现贡献计算、归因明细、汇总和恒等式检查，不修改运行时采集。

### Agent 3：actor key 归因审查

负责阶段 C。修正主键、同名、跨阵营和召唤物归并，不擅自更改贡献公式。

### Agent 4：报告和跨场比较

负责阶段 D。实现单场报告、规则报告、关系报告和同任务队伍对比。

### Agent 5：导出 schema 集成

负责阶段 E。只有 B–D 稳定后启动，负责插件导出、版本、schema checker 和兼容测试。

### Agent 6：游戏内 UI

负责阶段 F。只消费稳定接口，不重做底层归因。

### Agent 7：攻击力和残差研究

负责阶段 G。与主线相对独立，不得改变既有 KPI 定义。

## 7. 协作规则

每个智能体提交结果必须包含：

1. 修改了哪些文件。
2. 没有修改哪些边界文件。
3. 使用了哪些导出。
4. 运行了哪些命令。
5. 数字分别属于实测、离线重放还是推断。
6. 测试结果。
7. 已知限制。
8. 给下一个智能体的接口和注意事项。

文件冲突规则：

- 同一阶段只允许一个智能体修改同一文件。
- Aggregator.cs、ExportService.cs、BuildInfo.cs 属于高风险文件，必须单独委派和审查。
- 报告和测试优先新增独立文件，不要先改核心插件。
- 不删除现有证据、备份和导出归档。

## 8. 统一验收清单

### 计算正确性

- [ ] 每击 credit 恒等式成立。
- [ ] 角色总贡献恒等式成立。
- [ ] 直接输出和总贡献的分母明确。
- [ ] 未归因单独列出。
- [ ] 辅助贡献不重复计算。

### 数据质量

- [ ] actor key 覆盖正常。
- [ ] 同名和召唤物不会错误合并。
- [ ] byUnit 覆盖率已报告。
- [ ] 歧义、队外来源和无法解析都有分类。
- [ ] 事实和事件覆盖率已报告。

### 兼容性

- [ ] 旧导出仍可读取。
- [ ] schema 与版本一致。
- [ ] BuildInfo 是版本真源。
- [ ] 旧 KPI 未修改。
- [ ] 训练场特殊机制未误套普通战斗。

### 工程验证

- [ ] Release 构建成功。
- [ ] recon_probe 全部通过。
- [ ] schema checker 自测通过且能拒绝坏输入。
- [ ] FACT checker 自测通过。
- [ ] v150_validate 无异常。
- [ ] 编码检查通过。
- [ ] 修改文档已回读关键行。

## 9. 第一版完成定义

第一版总贡献分析完成的最低标准：

1. 不修改既有对账 KPI 定义。
2. 使用现有导出即可运行，不要求用户为基本功能重新战斗。
3. 以 actor key 作为主归并键。
4. 输出直接输出、基础贡献、自身规则贡献、辅助贡献、总贡献和比例。
5. 每击贡献可以追溯到规则和角色。
6. 所有贡献满足恒等式检查。
7. 未归因池单独列出并解释原因。
8. 能对两组以上同任务队伍进行结构比较。
9. 新增代码有离线测试。
10. 报告明确指出 attackPower 加算（队友给予已归属给出者、自身加算仍在 baseCredit）、会心和残差限制。

## 10. 推荐执行顺序

1. Agent 1 完成指标字典和手工样例。
2. Agent 2 基于样例实现离线核心。
3. Agent 2 使用现有 1.5.5 导出验证。
4. Agent 3 审查并修正 actor key 归因。
5. Agent 4 生成单场和跨场报告。
6. 主负责人审查恒等式、覆盖率和证据等级。
7. Agent 5 集成正式导出 schema。
8. Agent 6 接入战斗结束 UI。
9. Agent 7 独立推进攻击力来源和残差研究。

在第 6 步之前，不建议修改正式插件版本或部署 DLL。

## 11. 项目成功标准

项目成功不是某个角色偶然排名第一，而是能够稳定回答：

> 在相同战斗条件下，某个角色的直接输出、规则贡献、辅助贡献和总贡献分别是多少；这些数字来自哪些逐击事实；结论是否跨多场稳定；更换队员后损失的是直接输出、辅助倍率、覆盖率还是行动次数。

只要每个结论都能回到导出数据、规则来源和明确公式，DpsMeter 就从伤害计数器升级为可用于队伍优化的战斗分析工具。
