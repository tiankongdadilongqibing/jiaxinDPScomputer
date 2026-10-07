# HANDOFF —— 协作智能体交接书

> 生成时间:2026-10-03 深夜(当日:1.5.3 → 清空旧导出/整理文档 → 20:54 场 1.5.3 验证通过(§7.2.75)
> → 1.5.4 贡献归因第一步实机验收(§7.2.78)→ **1.5.5 实机验收:两条归因 100%、contrib 未归因池 0.0%**
> (§7.2.80)。**现状/待办/目标见 §9c**;1.5.2 实机场在 `export_archive_20261003.zip` 里)
> 读者:接手协作的智能体。**这份文件是自包含的**:先读完它,再按第 10 节去查细节。
> 纪律:本文件里的每个数字都必须能在磁盘上复算出来。若你的结论与本文件冲突,**先复算,再改本文件**,
> 不要凭印象覆盖。
> 例外(2026-10-03 用户明示):旧战场的原始导出已清空,仅 3 份 1.5.3 证据战场留存于
> `_dpsm_work\export_archive_20261003.zip`(2.2MB,回环校验过)。历史小节的旧数字以记录为准;
> **新数字从下一场起照原纪律执行**(已兑现:20:54 场,§7.2.75)。

---

## 1. 项目是什么

给单机 DMM 游戏 **邪神戦記 ルルイエ少女隊X**(Unity 6000.3.5f2 / IL2CPP)写的 BepInEx 伤害统计插件
`DpsMeter`。它挂游戏钩子,实时统计每场战斗的伤害,并在战斗结束时导出一份 JSON 供离线分析。

**核心目标(用户反复强调的那一条):**
> 让插件的**伤害构成与归属**和游戏里的**真实数字**对得上,并且**每一处改动都要有证据**。

用户另外两条明确要求:
1. **不要为了验证一个假设就让用户再打一场** —— 探针要一次上齐(该方法论的原说明已归档:
   `_dpsm_work\doc_archive_20261003.zip` 里的 `DpsMeter-1.4.0-一次性探针全集.txt`)。
2. 如果改一个纯函数的参数(比如去重表的签名)就能离线验证,**必须离线验证完再让用户打**(见第 8 节
   `check_fact_signature.py`)。这条是被连续两版改错签名逼出来的。

---

## 2. 环境硬事实(照抄即可,别重新摸索)

工作目录:`D:\dmmplayer\rlyehshoujotaix_cl`

| 用途 | 路径 / 命令 |
|---|---|
| 插件源码 | `_dpsm_work\src\DpsMeter.csproj`(**87 个 .cs**;RF2 拆出 6 个 `Aggregator*` partial,RF3–RF6a `src\Policy\`,RF4a/RF4c/RF4e/RF4f/RF4g `src\Runtime\`,RF5b–RF5g `src\Ui\`;守卫脚本扫 src+recon_probe+test+tests 共 **117**(2026-10-04 第 20 轮同步)) |
| 部署目标 | `BepInEx\plugins\DpsMeter\DpsMeter.dll` |
| 战斗导出 | `BepInEx\plugins\DpsMeter\exports\*.json`(**活的**:游戏在跑,写本文时已 **144 份**)。批次读冻结快照 **35 份**(`_dpsm_work\batch-inputs-rf0.json`:411001×25 / 试炼场 9999×9 / 700817×1;版本 1.5.3→1.7.11) |
| 运行时日志 | `BepInEx\config\dpsmeter_runtime.log`(每次启动游戏被删,只留最新一场的 `[RULE]/[STATE]/[PARAM]`) |
| 配置 | `BepInEx\config\dev.dpsmeter.cfg` |
| IL2CPP interop | `BepInEx\interop\Assembly-CSharp.dll`(**纯桩,没有方法体**) |
| 反编译全树 | **已删(可再生)**:`ilspycmd <dll> -p -o <目录>`;单类用 `-t`。2026-10-03 清理后不再保留 dumps |
| 构建 | `& 'D:\dmmplayer\dotnet-sdk6\dotnet.exe' build 'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\src\DpsMeter.csproj' -c Release -v minimal` |
| 反编译单类 | `& 'D:\dmmplayer\dotnet-tools\ilspycmd.exe' -t <命名空间.类型> <dll>`(v8.2.0;**经 Select-String 管道时退出码为 1,但输出是有效的**) |
| Python | `C:\Users\24134\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe`(**没有 matplotlib**;numpy/pandas/PIL/openpyxl 有) |
| 离线探针 | `_dpsm_work\recon_probe\`(`dotnet run --project ReconProbe.csproj -c Release -v quiet -- out.json`) |

### 编码 / 工具坑(踩过多次,必读)

1. **绝对不要往 stdout 打中文** —— 控制台是 GBK。要输出中文就写 UTF-8 文件,再用读文件工具看。
2. **绝对不要用 PowerShell 整文件读写含中文的源文件** —— 曾经把 `recon_probe/Program.cs` 写坏过
   (坏副本留在 `Program.cs.corrupt-gbk-20261003`)。要改文件用编辑工具。
3. `Get-Content` / `Select-String` **必须显式加 `-Encoding utf8`**。不加会猜错编码:
   实测 `ARCHITECTURE.md` 不加编码时被数成 521 行,加 `-Encoding utf8` 才是真实的 851 行。
4. **`edit` 工具的 `old_string` 若以换行结尾,会把下一行"焊"上来。** 这是长期反复踩的坑(历史上 5 次以上),
   **本次会话又踩了一次**(在 `ARCHITECTURE.md` 的模块地图里把 `BattleSummary.cs` 和 `BattleTime.cs`
   焊成了一行)。**改完立刻构建/检查**;注意 `refactor_final_check.py` 只扫 .cs、查不了 .md ——
   改 .md 之后必须自己回读那几行。
5. IL2CPP 类型坑:`Rog.Domain.AttributeModel` 继承的是 `Il2CppSystem.Object`,**不是 `UnityEngine.Object`**,
   对它做 `(UnityEngine.Object)` 转换会抛 `InvalidCastException`,外层 `catch {}` 会吞掉 → 静默返回兜底值。
6. 任何新读游戏对象的代码都要**自带 try/catch 并报告失败计数**;"读不到"和"没有"必须能区分。

---

## 3. 当前部署状态

| 版本 | SHA256(前 12) | 大小 | 状态 |
|---|---|---|---|
| **1.7.23(已部署,R72)** | `F83D369E4C10` | 486,912 | R72:**让 R71 的原点修正真正落地** —— 你打完一场说「自动技能的发动时间统计得还是比初动时间早,**完全没有改变**」。**原因不在算术而在顺序**:R71 算对了 +0.90 s,却每场都停在 `[CLOCK] origin=none reason=events`(它只从本场第 2 帧起尝试对齐,而游戏在**创建 session 的同一帧**就打出了开局伤害:quest 9999 `hits=5` 距 session 开始 0.121 s、quest 411001 `hits=1` 距 0.029 s)⇒ 偏移一次也没应用;而同一刻的 `[AUTOSK] chg` 行(`active=0.03s`)证明五个具名自动槽**全部**给出 **0.903 s**(夢幻の連鎖破裂 92/180 对初动 120、爆ぜる燭光 122/210 对 150、影爪の強制裁断 92/390 对 120、不浄な体液の噴出 212/300 对 240、実験失敗！2942/2970),且与页面上的实际发动时刻自洽。①**在创建 session 的当场**尝试对齐(`StartSession` 末尾,那时本场 `EventCount==0`、`ActiveSeconds==0`);②证据来晚时把开局的伤害/治疗**先扣住**(新纯策略 `ClockOriginHoldPolicy`:有界 **256**、计数、到上限即置 `OriginHoldOverflowed` 并**拒绝**偏移,`reason=hold`),定案后按**到达时刻 + 决定的 lag** 重走记录器 ⇒ `events[].t`、actors 首/末命中、按秒桶、伤害曲线、`PendingHits` 配对、反应窗口、`StateTimeline/FactStore` **一次写对**;③不走记录器的几处按同一常数搬一次(`SkillTimelineEvent.Active`、`AutoSkillProbe` 的 `LastActive/PrevActActive`、三个诊断导出行、`AttackSnapshot.At`),`SampleHp` 在定案前不采样;④`[CLOCK]` 行每条分支都带 `samples=/active=/hits=/held=`。**口径不变**(与 1.7.22 同一条轴,只是现在真的被应用);**不动任何伤害/归属数值、不改导出形状**(`schemaVersion` 1.2);用例 **1205→1254**、变异 **189→203**;备份 `_dpsm_work/deploy-backup/pre-r72-5DB365C6/` |
| **1.7.22(上一版,R71)** | `5DB365C62D35` | 481,792 | R71:**战斗钟的原点** —— 你问「大部分自动技能的最早发动时间小于技能的初动,是不是开局少算了一秒」。**是,正好 0.90 s:速率没错,原点错了**(我们的 0 是「插件看到这场战斗」,游戏自己的初动/冷却/屏幕倒计时都从「游戏开始它」起算)。三条独立测量:①自动技能充能计数器 `lag=(FirstCoolTime-WaitCountFrame)/units-active` = 0.90–0.93 s(12 槽 × 3 场恒定;把初始值当 `CoolTime` 会散成 0.9/2.9/4.9/9.9);②游戏自己的屏幕倒计时 `now/30+active` 是常数(每场 27–40 个采样波动 ≤0.07 s ⇒ 同速),常数 89.10/119.10 对 90/120 s 台面时限;③页面「最早发动」对照主表 `FirstCoolTime` 逐个差 0.9 s。**顺带量到**:自动技能槽计数器**开局初始化为 `FirstCoolTime`(初动)**、每次发动后重置为 `CoolTime`(冷却)。按用户选的**方案 B** 实现:开局第一次采样时(早于任何伤害、2 s 窗口内)一次性把 `ActiveSeconds/CombatSeconds` 前移实测 lag,`[CLOCK] origin=+0.90s samples=N …` 进日志;边界(亚噪声 / >5 s / 超前 / 窗口外 / **已戳过事件** / 可用槽<2)在纯策略 `BattleClockCalibrationPolicy`,拒绝时 `[CLOCK] origin=none reason=off|few|window|events|range`;时间表页面新增「时刻起点 = 游戏自己的战斗开始(已补回 +0.90s)」那行。影响:页面戳/`active=`/导出 `events[].t`/`duration`/按秒桶**整体 +0.9 s**(自检:`now/30+active` 恰好等于台面时限);**1.7.22 之前的文件沿用旧原点**,相对量不变。新开关 `General/ClockAlignToBattleStart`(默认 true)。**不动任何伤害/归属数值、不改导出形状**(`schemaVersion` 1.2);用例 **1171→1205**、变异 **181→189**;**这一版实测一次也没应用成功(见 1.7.23 行)**;备份 `_dpsm_work/deploy-backup/pre-r71-5D0942C7/` |
| **1.7.21(上一版,R70)** | `5D0942C743CE` | 475,648 | R70:**时间表把敌方的技能算进了「我方」,并且一行的发动时刻放不下不让它往下撑**。你截图问的两件事:①**多行行** —— 首行不变(身份 + 9 个时刻 + 尾部 `并/试/n/med`),**续行只印时刻**并与首行时刻列对齐、每行 **14** 个、最多 **3** 个续行 ⇒ 一行最多印 **51** 个时刻,`+N` 移到最后一个续行(含义不变);实测 マッドシーカー 的奥义 14 次发动从「8 个 + `+6`」变成两行全印。②**只统计我方** —— R66 的文档一直写着「只有我方(`CharacterInfo.IsAlly`)」,但三条路里只有 `skl` 真的过滤:`cmd`(自动技能命令后缀)与**充能采样器**从来没有团队判断。实测那一场(任务 9999,60.7 s,导出 `…151957…-001.json`)的 `actors[].team` 把 **ムスクーマ(key 8)/ネフェスティス(key 9)** 放在 **team 2 且无 team 1 同名个体**,页面却把它印在「我方」下;同场 `skl` 的 SUM 报 `foreignSide=9`(敌方 9 次调用被丢)即根因对照。修法:判据抽成纯策略 `SkillSidePolicy`(`team == 1` = 我方,读不出来失败关闭;`CharacterInfo.IsAlly/IsAllyTeam` 都调它),三路统一走 `SkillTimelineProbe.NoteForeign`(计数 `[SKILLTL] SUM foreign(cmd/skl/chg)=`、`[AUTOSK] SUM foreignSide=/foreignUnits=`,并打证据行 `[SKILLTL] foreign …`),页面通道行追加 `剔除非我方 N 条`。**不动任何伤害/归属数值、不改导出形状**(`schemaVersion` 1.2);用例 **1140→1171**、变异 **175→181**;备份 `_dpsm_work/deploy-backup/pre-r70-9E66D3EE/` |
| **1.7.20(上一版,R69)** | `9E66D3EE9D89` | 473,600 | R69:**「调用 ≠ 发动」—— 时间表把 24 次试触发印成了 24 次发动**。你打完正式任务 411001(119.07 s)后问「这个技能应该是 99 秒发动一次」;**你对了**:マッドシーカー 的自动 1 実験失敗！(主表 `minCoolTime=maxCoolTime=2970` 帧 = **99 游戏秒**;天赋只有「播放攻击」+「AttackDataForceUpdate」;伤害来自她自己的攻击)那一场被调用 **25 次** = **2 次 `Using`(同一瞬间 active=99.07 s)+ 23 次 `Charge`**,真发动 **1 次**(她的计数器同场在 active=101.17 s 走满归零)。①**发动判据改成游戏自己的状态**(新纯策略 `SkillActivationPolicy`:调用返回时 `Using` 才算发动,`Charge`/`Usable` 是**试触发**;未知状态失败关闭并计入 `unclassified=`)—— 探针每次调用仍打行,新增 `verdict=act\|try\|unknown` 与 `tries=`,只有 `act` 进 `actCmd`/`n=`/间隔;②**折叠窗口从固定 0.25 s 改成「该技能自己的冷却」**(`CoolTimeFrame/30`,下限 0.25 s):一次发动会在执行窗口里留下一串调用(实测跨度 1.5/2.1/2.9/3.0/4.53 s),固定窗口把一次发动切成几次;两条语料实测 **9 场 342 间隔 184 违规 → 174 间隔 0 违规**、**411001 那场 81 间隔 35 违规 → 47 间隔 0 违规**,且没有哪个折叠簇的跨度达到自己的冷却;③页面加 `试N` 与汇总行「发动 X 条 + 试触发 Y 条(未计入)」,`TailW` 25→33、行宽 121→**129**、面板 900→**960 px**,图例拆两行(合成一行时 189 列放不下),离线渲染那一场 = **60 发动 + 37 试触发 = 97 条**(= `SUM cmd=97`),`lines_over_width=0`;④**撤回 R66 §2b「充能不是发动的闸门」** —— R65 最早的说法才是对的,那 157 次「`Charge` 期发动」就是试触发;⑤**删掉 R66 的记录钩子与整个 `rec` 通道**(训练场 9999 与正式任务 411001 两场都 `rec=0` 且跳过计数全 0,即装上了从没被调用),`Hooks/SkillRecordHooks.cs` → `Hooks/SkillCommandHooks.cs`;`Skill.ActivationType` 仍无解释力(每个单位每个技能 `act=0 actP=0`,不再引用)。**不动任何伤害/归属数值、不改导出形状**(`schemaVersion` 1.2);用例 **1090→1140**、变异 **165→175**;备份 `_dpsm_work/deploy-backup/pre-r69-5BA37D6C/` |
| **1.7.19(上一版,R67)** | `5BA37D6C896E` | 472,576 | R67:**用户截图核对 R66 的时间表**:数字全部对得上(97 → 7 行 / 88 格 / 合并 9,与独立重算逐个相等),但修三件事 —— ①连发标记 `xN` 改 `并N条M格`(R66 的写法对 3/5 个带标记的行是假话:メアリー `x3` 实为两格各并 1 条),策略层改记每格倍数 `Multiplicity`;②时间格 5→6 列(三位数时刻不再黏成 `96.2107.3118.3`),每行 9 格;③奥义/特殊换通道:R66 的 `AddPlayerSkillGameRecord` **一整场没被调用**(`rec=0`,其余计数全 0),改挂 `GameCmdExecuter.ActExecutePlayer{ActiveSkill,Skill,SpecialSkill}`(通道 `skl`,SUM 按入口打印调用次数),采样器加槽 10/11 读 `ActiveSkill/SpecialSkill` 的 `Skill.Type`。`gameIdx` 结案(0 基被动位 / 1 基槽位,同一对象),`indexAmbiguous`→`indexReadingsDiffer`;用例 **1076→1090**、变异 **160→165**;备份 `_dpsm_work/deploy-backup/pre-r67-D17400F0/` |
| **1.7.18(上一版,R66)** | `D17400F05314` | 468,480 | R66:**把「技能发动时刻」做成悬浮窗里的一页(F4 换岗),并用 9 场语料修正 R65 的两条口径**。①**F4 双含义(用户选定)**:面板**可见**时 F4 打开新页「技能时间表」(我方每单位 × 每技能:每次发动的**战斗时钟秒**、`xN` 连发数、`n=` 次数、`med=` 中位间隔 + **两条观测通道各多少条**),面板**隐藏**(F8)时 F4 仍是**按需证据包**(战斗结束的自动抽取不变)。页面文字来自纯层(`Ui/SkillTimelineText.cs` + `Policy/SkillTimelinePolicy.cs`,合并窗口 **0.25 战斗秒**),与战斗结束写的 `[SKILLTL] panel` 行**同一批字符串**。②**R65 口径修正之一**:`auto_skill` 的 `minCoolTime`/`maxCoolTime` 是**技能等级区间**(L1 = min、满级 = max)⇒ `CoolTimeFrame` 随等级变;实测 **ソフィー 420@L1→300@L3、トレイラ 270@L2→240@L3、ポポロット 270@L1→210@L3、ネフェスティス 270@L2→240@L3、T.O.W.E.R.typeR 420@L1→300@L3**,5 单位 × 2 等级共 **10/10** 落在这条规则上 —— 这就是 R65 悬着的「420 vs 300」的答案(两个**等级对象**并存,R65 改身份键的方向对、理由是错的)。③**R65 口径修正之二**:**充能不是发动的闸门** —— 471 次发动里 **305 次** `wait==cool`、**157 次**在 `status=Charge`、9 次 `Usable`;≥6 个单位的中位间隔**短于**充能(マッドシーカー **4.97 s vs 99.0 s**、ソフィー 4.30/14.0、T.O.W.E.R.typeR 4.53/10.0、レヴナント 3.93/5.0、ルーチェルト 2.50/5.0、メアリー 10.10/13.0)⇒ **「充满后等下一次普攻」作为通则撤回**(只对トレイラ那 3 个样本成立)。④トレイラ 的间隔扩到 **47 个**,中位 **9.00 游戏秒**(R65 的 15.20/12.03 是同一分布高位)。⑤**新增只读通道**:隔离后缀 `GameCmdExecuter.AddPlayerSkillGameRecord(Player, Skill, eUserRecordType, Vector3)` —— **唯一**能同时看到奥义/特殊/自动的观测点(`GameCmdExecuter` 没有 `ActExecutePlayerOverSkill`,奥义走 `Player.ExecuteActiveSkill`,**不许**挂钩);「是否等于发动时刻」**尚未证实**,所以页面自报通道条数并在 `rec=0` 时明说。⑥探针另加 `inst=`(对象标签)、`[AUTOSK] cmdidx`、`act=`/`actP=`。**不动任何伤害/归属数值、不改导出形状**(`schemaVersion` 1.2);用例 **1002→1076**、变异 **146→160**;备份 `_dpsm_work/deploy-backup/pre-r66-FA8E5918/` |
| **1.7.17(上一版,R65)** | `FA8E59188E0A` | 454,144 | R65:**实机数据把 R63 的 ×30 证伪,错数已删**。①**答案**:`[賢導]トレイラ`「暗沌への導き」充能 `CoolTimeFrame = 240` 单位 = **8.0 游戏秒**(率实测 **30.0 单位/游戏秒**),本场**实际发动间隔 15.20 / 12.03 游戏秒**(真实 10.18 / 8.08 秒)—— 充满后要等**下一次普攻**(正文「次回攻撃時のみ」)。②**证伪 R63**:`auto_skill` 的 `minCoolTime`/`maxCoolTime` **就是游戏自己的充能单位**,实测 9 个自动技能与线上 `Skill.CoolTimeFrame` **字面相等 9/9**、**没有一个**等于 ×30 ⇒ R63 的 `minCoolTimeFrames`/`maxCoolTimeFrames`(=9000/7200)**是 30 倍错数**,已**删列**,信封改给实测 `unitsPerGameSecond`(该除不该乘);产生错数的 `SkillCooldownPolicy` 连同 16 例用例/2 条变异**一起删除**。③**R63 §3c 的「存疑」是伪命题**:那个 ≈13.5/15.9 出自导出 `events[].t` = **游戏钟的秒**,与本场 15.20/12.03 是同一对交替值,旧反推本来就对。④**探针修 4 处**:游戏 `index` 是 **0/1**(⇒ 32 行落垃圾槽、9 个垃圾槽挤掉真槽位,**18 个单位只读到 9 个**)、`Using` 上升沿**不是发动**(假中位间隔 1.10 s vs 真实 8–10 s)⇒ 降级 `via=usingEdge` 且**排除在间隔之外**、槽位键改**技能身份**、回跳样本不再打 `chargeSec=0.0s`;新增 `[AUTOSK] roster` 一行/单位 与 `gameIdx=`/`cmdUnresolved`。**不动任何伤害/归属数值、不改导出形状**(`schemaVersion` 1.2);用例 **1016→1002**、变异 **148→146** |
| **1.7.16(上一版,R64)** | `5F9234EF331E` | 451,072 | R64:把**自动技能的运行时**读出来 —— 新增只读探针 `AutoSkillProbe`:从**线上 `Skill` 实例**侧读(`Player.AutoSkill1/2` → `Skill.m_type`/`GetStatus()`/`WaitCountFrame` vs `CoolTimeFrame`),写 **`[AUTOSK] act`**/**`[AUTOSK] chg`**/**`[AUTOSK] SUM`**。**关键换路**:`[CLOCKP]` 走的 `PlayerSkillStandbyData` 列表里**没有**自动技能(实测渲染出 地下からの完全顕現/電脳掌都/狂気の眼球;4 个名字遍历 21 个 masterdata JSON 只有 `auto_skill.json` 命中 暗沌への導き)。**不动任何既有数值、不改导出形状、不新增导出根段**;备份 `_dpsm_work/deploy-backup/pre-r65-5F9234EF/` |
| **1.7.15(R63)** | `68E10640CC92` | 438,272 | R63:把「自动技能」主表 dump 出来 —— 新增 `auto_skill` 表(20 张表 / 输出 21 个文件),自动技能的冷却终于有游戏自己的数;**实机已确认** `表成功=20 / 缺失=0 / 行异常=0`、`auto_skill=120` 行;同时新增纯规则 `SkillCooldownPolicy`(⚠️ **R65 已删除:它的 ×30 被实机证伪**)。注意:她那一行(id 84「暗沌への導き」)主表冷却是 **240–300 秒**,与上一轮从伤害反推的 ≈13.5 s 对不上,该反推**转为存疑**(R63 §3c;**R65 已结清:旧反推是对的**);备份 `_dpsm_work/deploy-backup/pre-r64-68E10640/` |
| **1.7.14(上一版,R62)** | `C9D1B0CC9FAF` | 435,712 | R62(A/B/C 同步):①命中记录错配改「可证伪即拒绝」(`hitMatch=3` + `hitDetail.matchRejected`,实测 9999 里 3,240/11,096 条贯通命中的 `source` 留在 0,但其中 97% 是「没有可用记录」、只有约 0.5% 是「记录属于别的命中」;1.7.15 实机 5,746 条里 52 条被拒绝、0 条错标留存);②离线残差改游戏口径 `(applied+absorbed)/theory`;③全局「敌方受伤」因子从 `dealtMult` 移入 `takenMult`(乘积不变);备份 `_dpsm_work/deploy-backup/pre-r63-C9D1B0CC/` |
| **1.7.13(R61)** | `32BFEC3B6A88` | 435,200 | R61(用户判断):同队/自我伤害(敌方 回復反転)移出归属池,`contribution.schemaVersion` **1.2**;`damageLedger` 增 `selfTeam*`;F5 占比按对敌池重算;备份 `_dpsm_work/deploy-backup/pre-r62-32BFEC3B/` |
| **1.7.12(已部署,R60)** | `F791F1CFD0E5` | 434,688 | R60:自伤三件套 —— 导出根 `config.filterFriendlyFire`、`contribution.actors[].friendly/friendlyHits/hostileDamage`、F5 表 1 恢复「自伤」列(T1 85→94、面板 780→880);另修 `contrib/crosscheck.py` 对 schema 1.1 段 `totalDamage` 的 1.0 旧公式 |
| 1.7.11(基线构建) | `36EC96D4DBD8` | 387,072 | 1.7.11(§7.2.100):F5 表 1 改为 `自身 \| 他人因你 \| 被队友分走`(自身=基础+自身规则),F6 明细块写成算式,`receivedAssist` 首次进表;两条逐角色恒等式进 `check_export_schema`,版面守卫新增「渲染器↔副本漂移」检查,并修 `check_live_log` 两处误判 |
| 1.7.10(前一版) | `BF2F174A4059` | 386,560 | 1.7.10(§7.2.99):`give_section_reasons` 曾读错对象而**从未运行**(改读 `rosterAudit` + 真实文件负控);`[COMP]` 重算遵守 GivenTalent/Madness/MadnessVictim |
| 1.7.9 | `F3F73C81FF3D` | 386,560 | 1.7.9 集成(§7.2.98):GivenTalent 开关下传 + 逐击 `calc.givenFoldOn`;`giveApplied` 双计修复 + `rosterAudit.giveFoldHits`;UI-DIAG `unattrRow`;`check_live_log.py` |
| **1.5.5(已部署,已实机验收)** | `18E4D933C9C8` | 340,992 | **两条归因都 100%**(§7.2.80):C = メアリー 自施加 897/897 带上 byUnit;A = 授予路径确为 TalentActionAddTalent,hook 691 次、given 6110/6110 归因,授予者 = **マッドシーカー**(11.6M 当量);贡献表未归因池 **0.0%** |
| 1.5.4 | `E645DAFE3757` | 337,408 | 贡献归因第一步(§7.2.78):C 半覆盖、A 失败;归档 `DpsMeter.dll.1.5.4-verified.bak` |
| 1.5.3 | `26CA67B35FBD` | 332,800 | **实机验证通过**(205449 场,§7.2.75):exact 83.3%(预测 82.7%)、ド・マリニー 942/942、受方狂気 3457/3457;归档 `DpsMeter.dll.1.5.3-verified.bak` |
| 1.5.2 | `A149A59409E5` | 329,728 | **实机打过**(183213 那场):覆盖 99.98%、`类=271/2400`、`类溢出=0`;结算见 `evidence_152live_VERDICT.txt` |
| 1.5.1 | `808235C57B54` | 328,704 | **实机打过**(175142 那场),归档 `DpsMeter.dll.1.5.1-verified.bak` |
| 1.5.0 | `7E55E457D605` | 328,704 | **实机打过**,归档 `DpsMeter.dll.1.5.0-verified.bak` |
| 1.4.1 | `93E691D89C5C` | 302,080 | 归档 `DpsMeter.dll.1.4.1.bak` |

**2026-10-03:导出已清空**(用户指令)。773 份旧导出(527MB)删除,仅 3 份 1.5.3 证据战场存
`_dpsm_work\export_archive_20261003.zip`。**新导出已起步**:20:52 试炼场(quest 9999)+ 20:54 实机场
(quest 411001)各一份,守卫全过 —— 新基准从这两份开始。

* 完整 1.7.11 哈希:`36EC96D4DBD8E221ED554476C299BD8DB4C9A1220A2A923DB16BC7BB4888BC42`(387,072B);前一版 1.7.10 = `BF2F174A4059125376946ABCB0E3E4A9176E6B360CAB209078F219A95389AE85`(386,560B),1.7.9 = `F3F73C81FF3D9CE0B903E5B4BB9C3F60062BF20F93118A853DBB9DCC34E7DBCB`(386,560B),1.7.8 = `0B33983646280E246AA9E5911AEAB0EC499FC22C6C796460299BD4BDC4B34880`(386,048B)
* 完整 1.5.5 哈希:`18E4D933C9C8724090FE276CE5E9F70CD844BF7E5AB038D3B335554691365FA9`
* 完整 1.5.4 哈希:`E645DAFE3757179A356C8024EA0F8091FC61A898735D40D138421FB5232402F6`
* 完整 1.5.3 哈希:`26CA67B35FBD585E60BCC24881C3134A41C6A4974BDE4A08F8CF2001C31E838D`
* 完整 1.5.2 哈希:`A149A59409E5F1E30FCA3D564B448E291EA3E1FE3472F5139F0A4FD76E5EDC73`
* 完整 1.5.1 哈希:`808235C57B5414BB082FD9223DFD642E9367A45F31718B77F4A963E3D6A6CAD3`
* **绝不回滚** `DpsMeter-1.0.48-crash.bak` / `DpsMeter-1.0.49-crash.bak`(启动即崩)
* 部署流程见第 7 节;归档必须**逐份按哈希核对它确实是该版本**,本次会话犯过一次"把 1.4.1 复制成了 1.5.0"。

---

## 4. 目标与用户的长期决定

**总目标**:构成/归属与游戏真实值对账,每处改动给证据。

用户已经拍过板、**不要重新讨论**的决定:

| 议题 | 决定 |
|---|---|
| 一场战斗被"战斗结束"信号切成多段 | **只加归组标记**(零行为改动),不合并统计 |
| 伤害总量口径 | **保持 `dealt` = 进入耐久**,用 `dealtWithAbsorbed` 对账 |
| 对账 KPI 的定义 | **冻结**,1.5.x 全程未动,以便与历史语料可比(定义见第 7 节) |
| 探针策略 | 一次上齐,不要"一个假设打一场" |

---

## 5. 现状总览:什么成立、什么不成立

### 5.1 成立(有实测证据)

| 结论 | 证据 |
|---|---|
| 1.5.1 修掉了一个**真回归**:全局规则持有者自己被双重计入 | `exactWithCrit` 50.1%(1.5.0 场)→ **80.4%**(1.5.1 场),回到同任务基线 72.9–81.7%;`[RULE] skip 持有者本人(仍登记为责任规则)` 在日志中实际出现 |
| 对账 KPI 定义未变,历史可比 | `reconcile.*` 字段名与判据自 1.3.x 未动 |
| 抗性曲线的"生效值"路径成立 | `AilmentCommonThreshold = 100`;`CalcValue(Type,v) = v*(100-抗性)/100` 验证 12/12 |
| 狂気 = **×2.5** | 924/924:残差 == `1.15^n × (狂気 ? 2.5 : 1.0)`;机制 `MadnessAllyBuffRatio/100`(250 开 / 100 中性) |
| 状态时间线通道工作 | `[TIMELINE] 读取=5221 单位=189 行=579 状态变=50 抗性变=529 丢=0 错=0`(1.4.1 只有 64 点采样) |
| 导出 JSON 结构自检有效 | `[JSON] 结构=OK 根键重复=0`;`check_export_schema.py --selftest` **52/52**,且**被证明能拒** |
| FACT 签名问题**已实机确认修好** | 覆盖 36.6%→**99.98%**、类 600/600→**271/2400**、溢出 3203→**0**(1.5.2 场 183213);见 5.2 / 第 6 节 P1 |

### 5.2 不成立 / 已知坏掉(诚实清单)

1. ~~**FACT 覆盖率实测只有 36.6%**(1.5.1)~~ **已结案**:1.5.2 实机 `[FACT] 覆盖=5582/5583`(99.98%)、
   `类=271/2400`、`类溢出=0`;同场离线重放独立给出 distinct=271,与插件逐位相等。
   1.5.1 旧身份在同一场重放只有 35.5%(1980/5582)。
2. ~~**`hitMatch` 判决不可审计**~~ **已结案**:1.5.2 每个事件都带 `hitValue`(5582/5582),
   实测 **`power / hitValue == 1.0` 占 5571/5582(99.8%)** —— 游戏钩子返回的 `__result`
   就是我们重建的**计算威力**(`calc.power`),在防御/属性/被伤害/已识别倍率之前。
   (隐患:`ExportService.cs:273` 仅在 `!=0` 时写 `hitValue`,真实 0 会与没测到同名。)
3. **会心实测通道一场都没匹配上**:`[RECON] 会心实测=0`。`GetFlyTextNumberSizeForAttack` 钩子
   历史上从未匹配(0/228,646)。**会心口径目前只能靠推断。**
4. ~~**`[FACT] 击=5220` 与导出 5055 个伤害事件差 ~165(~3%),机制未查明**~~ **已定位**:
   不是计数错,是 **1.5.1 那一场的事件流从 t=7.530s 才开始**(近 14 份导出里唯一一份
   `min_t != 0.000`),开头 7.53s 的事件丢了而钩子侧 static 计数器照数。1.5.2 同场五个
   计数器完全自洽,缺口 0。
5. **968 条未解释 ≈ 一条规则被少算 ×1.15²**(见 P0)。
6. 训练场城墙的 ×0.03 **故意没有建模**;明细页只是换了措辞。

---

## 6. 开放问题(按优先级;每条都带证据与判据)

### P0 —— 1.15² 少算:目前唯一真正的精度缺口(约占 19% 的命中)

* 现象:未解释 968 条,残留首位 **1.323(=1.15²)×483** + 1.322×295 + 1.15×123,**几乎全部来自 メアリー**。
* 方向:导出的 `residual = 游戏值 / 理论值`;**>1 表示我们少算了**。
* 标本(メアリー,`eff=10002`):`amount=184707`,`theory=139665`,
  `139665 × 1.3225 = 184706.96` —— 精确的 **×1.15²**。
  同一条命中里:全局通道折了 `x1.5209`(=1.15³),并抵消掉 3 份 `given#2/#5/#6/1006/-15`(各 1.15),
  `maxAbsorbed=3`。
* **它不是 1.5.0/1.5.1/1.5.2 引入的**:1.5.0 场里同一签名一模一样地存在
  (メアリー `r=1.323 n=468`、`r=1.322 n=288`、`r=1.15 n=75`)。所以是**先于 1.5.0** 的老问题。
* 待解的具体问题:规则「毒/凍結/火傷状態の敵全ての被ダメージがそれぞれ+15%」在游戏里
  **到底生效几份**?我们折 3 份(1.15³),游戏看起来是 5 份(1.15⁵)。
* **注意历史教训**:1.3.8 的文档注释记着,曾试过"按值消费多重集、每触发一个因子消费一份"的抵消法,
  **失败了**(~1700 条命中上多算 1.15²…1.15³)。所以现在 A5 只记证据、不动"按值无界抵消"的决策。
* 建议的**定向实验**(只测不猜):在某条命中上同时读出受击方
  「毒/凍結/火傷」三个状态位 + 该 +15% 规则的实际生效次数,把 `1.15^k` 与 k 直接对上。

### P1 —— FACT 表 —— **已实机确认、结案(2026-10-03,183213 那场)**

* 病灶有两层,**我第一版诊断是错的**,别重复:
  1. 上限太小:1.5.1 签名真实去重数 **1485 > 600**。覆盖率因此 36.6%。
  2. **身份选错了**:1485 里有 **1232 个只来自 `vicKey`** —— 一个逐实例 actor key。
     本场受害方是 187 个实例 / **只有 3 个单位种类**(ショゴス 5040 击 + 两个角色)。
* ⚠ **`类溢出` 不是"新签名数"**:它数的是"签名是新的、但表满没位置"的**命中次数**,
  同一个溢出签名会被重复计数。把 `600+3203` 读成 3803 是错的;判据只能是**重放**。
* ⚠ **先到先得**:表留住最早遇到的 600 个签名,只服务 2136 击;同样名额按"最大的类"来留能服务 3944 击。
  **一个早出现的稀有签名会永久占掉一个名额。**
* 1.5.2:身份改为 单位名+阵营,上限 600→2400,活体读取 420→900,状态集合规范化排序。
  离线重放:**240 类 / 5055 服务 / 溢出 0 / 余量 2160**。
* **实机结果(183213)**:覆盖 **5582/5583 = 99.98%**、类 **271/2400**、类溢出 **0**;
  离线重放对同一场也给出 271 类,与插件逐位相等。

### P2 —— ~3% 计数缺口 —— **已定位:1.5.1 那场丢了开头 7.53 秒**

* **答案:那一场的事件流不是从头开始的。** 首个事件 `t=7.530`;近 14 份导出(1.3.3→1.5.2)
  里只有这一份 `min_t != 0.000`,其余全是 `0.000`。开头 ~7.5s 的事件不在导出里,而
  `HitMatch*`/`FactStore.Observed`/`StateTimeline.Observed` 是 static,只在各自的 `Reset()`
  清零,所以照数。**为什么那一场在 7.53s 清掉了事件列表仍未证明**(已知触发点是
  `BattleSession.ResetActors()` 的 `Events.Clear()`),不编。
  1.5.2 同场五个计数器完全自洽:RECON 5583 = TIMELINE 5583 = HITDET(31+5551+1)
  = FACT 覆盖分母 5583;导出 events=5600(5583 dmg + 17 heal),其中 5582 条有有效 calc,
  所以 `击=5582`。`[HITDET] 产出=5636` 与匹配的差 53,是 `damage<=0` 早退**之前**就入队的
  `NoteHitDetail`(`Aggregator.NoteHitDetail` → `RecordHitDetail`),不是丢事件。
* 已逐条排除:同一场、同一进程、`run={id:1,seq:0}`(只有一次会话);
  `RecordDamage` 调用点唯一(`Hooks/BattleObjectHooks.cs:45`);
  `damage<=0 && nominal<=0` 的早退在计数之前;
  `FactStore.Observed++` 与 `Session.AddEvent` 是同一个 try 块里紧挨着的两句;
  `AddEvent` 只在 `Events.Count < 100000` 时跳过(5055 远未触及);`events` 导出无截断;
  `[JSON] 结构=OK 根键重复=0`。
* ~~**结论:机制未知。不要编解释。**~~ 1.5.2 加在 `[HITDET]`/`[FACT]` 行尾的 `导出事件=`
  让下一场一眼判定是"丢事件"还是"计数多记"。
* 副作用:因为插件被多喂了 ~165 次 `Observe`,**重放的"服务次数"与插件的计数天然不应相等**
  (2136 vs 2017)。**能下判决的只有 `distinct`**。

### P3 —— 会心实测通道是死的

* `critObserved` 全 0;`GetFlyTextNumberSizeForAttack` 从未匹配。会心只能靠
  `theory × critDamageRate/100 == gamevalue`(相对容差 0.002)推断。
* 现状可接受但不理想:会心是 `1/1.9`、`1/1.65`、`1/1.5` 这些残留的主因,无法与实测交叉验证。

### P4 —— 技术债(长期,优先级低于上面)

* `Aggregator.cs` 仍是巨型文件(1300+ 行),构成/折叠/导出/探针报告混在一起。
* 大量 `static` 状态,靠 `Reset()` 手工清理;新增状态忘了清就会让两场数据串在一起。
* **没有 git、没有测试工程**。回归只能靠"构建 + 反编译复核 + 实机一场 + 导出复核"。
  离线可测的部分已经抽到 `recon_probe`(179 条断言)+ 三个 Python 校验器。

### P5 —— 训练场城墙 ×0.03 未建模(已知,故意)

---

## 7. 不可破坏的纪律

1. **对账 KPI 定义冻结**(1.3.10 起,1.5.x 全程未动):
   * `GameValue = Applied + Absorbed`;`exact` = `Theory == GameValue`(严格整数相等)。
   * `exactWithCrit` = `exact` **或** `|Theory × CritDamageRate/100 − GameValue| / GameValue ≤ 0.002`。
   * `approx` = 相对差 ≤ 0.05;`theoryExceeds` = 未解释 ∧ 未被吸收 ∧ `Residual < 0.10`。
   * 分母是 `withCalc`。**改定义 = 语料报废。**
2. **任何导出 schema 变更都要升版本号**(`src/BuildInfo.cs` 是唯一真源,csproj `<Version>` 手工同步)。
3. **部署流程**:构建 → 反编译复核 → **按哈希核对后**归档旧版 → 复制 → **再核验部署文件哈希**。
4. **没被证明能拒绝东西的校验器不是校验器** —— 新增校验规则时必须同时加一条"必须被拒"的用例。
   同理:**新增 schema 要求时必须加一条"旧版本导出仍应被接受"的用例**,否则会把整个归档标红而自检照样全绿。
5. **每个新读都要有自己的 try/catch 和失败计数**;兜底值**不许**和真实类别同名;
   计数器必须真的被导出/打印,否则不算接线。
6. **凡是在战斗中才能读到的量,当场落盘,并且要能覆盖 100%,不许抽样。**
7. 改任何**纯函数参数**(签名、容差、键)之前,**先用已有导出离线验证**(第 8 节工具)。
8. 归档/回滚前**核对哈希与其声称的版本一致**。

---

## 8. 工具清单(什么时候跑什么)

**离线、不依赖游戏:**

| 命令(`cd _dpsm_work`) | 作用 | 期望 |
|---|---|---|
| `dotnet build src/DpsMeter.csproj -c Release -v minimal` | 构建 | 0 错 0 警 |
| `dotnet run --project recon_probe/ReconProbe.csproj -c Release -v quiet -- out.json` | 对账分类/Fold 语义/JSON 自检/StatusKey **真在执行** | **179 个断言全过(0 FAIL)** |
| `python check_time_single_source.py` | 时间来源唯一 | exit 0 |
| `python refactor_final_check.py` | 68 个 .cs(源码 65)无非法 UTF-8 / 无死符号 | exit 0 |
| `python check_docs_123.py` | 文档口径标记 | `TOTAL DAMAGE MARKERS: 0` |
| `python check_comp_baseline.py` | 构成基线 | exit 0 |
| `python check_export_schema.py --selftest` | schema 校验器自证 | **52/52**(含 1.1 台账、`givenFoldOn`、`taken` 恒等式) |
| `python check_fact_signature.py --selftest` | FACT 签名重放器自证 | **18/18** |

**拿到新导出后:**

| 命令 | 作用 |
|---|---|
| `python check_export_schema.py <export.json>` | 键/类型/覆盖率,必须 `problems=0` |
| `python v150_validate.py <export.json>` | 三条新通道计数 + 会心推断 vs 实测 + **KPI 对照同任务同口径历史** |
| `python check_fact_signature.py <export.json>` | 把 FACT 签名重放到这份导出:类数 / 先到先得覆盖率 / 是否饿死 |
| `python _dpsm_work\evidence_1152_residuals_150_vs_151.py` | 两场残留结构对照(改路径即可复用到任何两场) |

**也能只看日志八行:**`[JSON] 结构=`、`[RECON]`、`[HITDET]`、`[TIMELINE]`、`[FACT]`、`[GIVE]`、
`[MADAPP]`(1.5.4 狂気施加)、`[GIVAPP]`(1.5.5 授予来源)。

---

## 9. 历史验收账(1.5.2/1.5.3/1.5.4/1.5.5 逐条收账;**当前待办见 §9c**)

1.5.2 已于 2026-10-03 实机一场(`battle_411001_20261003_183213`),五条判据逐条结算见
`_dpsm_work/evidence_152live_VERDICT.txt`。**五条全部通过:**

1. ✅ `[FACT] 覆盖` = **5582/5583(99.98%)**;`类` = **271/2400**;`类溢出` = **0**。
2. ✅ 五个计数器自洽(缺口 0);那 ~165 已定位为 **1.5.1 那场丢了开头 7.53s**,本场不复现。
3. ✅ `hitValue` 5582/5582 齐全;实测 **`power/hitValue == 1.0` 占 99.8%**。
   → **已定案**:游戏 calc 的返回值就是我们重建的计算威力。
4. ✅ `[RECON] 计入会心` = **80.6%**,在预期内(会心实测仍 0,P3 不变)。
5. ✅ 968 那条 **×1.15² 结构不变**(473/336/142)。**根因已在 1.5.3 离线结案 —— 见 9b。**

### 9b. 1.5.3(已部署,未实机):P0 与受击方狂気都在离线结案

**没有再打一场,也不需要为它们各打一场。** 两件事都从磁盘上已有的逐击数据判出:

* **P0 的 ×1.15² = `[ド・マリニーの掛け時計]` 被整条搁置**,不是倍率模型错。病灶是状态列表用
  「と」连接(毒**と**火傷状態),而三处状态名匹配只认 `/ ／ 、 ・`。实测该规则全场
  **folded 0 / parked 960**,而那些命中残差恰好是 **1.3225 = 1.15²**。
  为什么代价是「整条不折」而不是「少折一个 ×1.15」:run 在 と 处断掉 → `stHits=1` →
  `JudgeClause` 的 それぞれ 叠加(`if (stHits > 1 ...)`)永不发生;**同时**残留的「毒」
  撞上通用关键词表 → `CondState.Unknown` → 记成 (条件性,未计入)。两个失效叠在一起。
* **受击方狂気 ×1.5**:1.4.1 只折了出手方(×2.5),持有者「挨打更疼」这一侧从来没建过。
  两张干净格 `0.9996→1.4954`、`0.9965→1.5011`,合并受控倍率 **1.4886**。倍率取实测 **1.5**,
  **不是** ratio/100(`MadnessAllyBuffRatio` 是味方增益,敌人身上读到 250,不是这个乘区)。
* **1.9 / 1.65 那批不是 P0**:残差恒等于 `critDamageRate/100`(190→1.9 共 649 击、165→1.65 共 76 击、
  185→1.85 共 2 击),而且是「这一击掷中会心」的差别。`IsCritExplained` 已把它们算进 exactWithCrit。
  **CritObserved 仍是 0/6220 → 会心目前只能推断 —— 那是 P3,不是 P0。**

离线重放(拿已有数据除以 1.5.3 会折的那两个因子,判据 `|residual-1| <= 0.002`):

| | 旧队 183213 | 新队 191159 |
|---|---|---|
| 折算前残差=1.0 | 3746 (67.1%) | 2060 (33.1%) |
| 折算后残差=1.0 | **4675 (83.8%)** | **5146 (82.7%)** |
| 净增 | +929 击 | +3086 击 |

折算后 1.323 / 1.322 / 1.15 / 2.85 / 1.984 **全部消失**,只剩会心带(已收走)、一个 1.5 带
(预测 119 击)、0.87 带(28 击,多算)、1.001 带(84 击,噪声)。

**已验证(2026-10-03 20:54 场,§7.2.75)。** 第六节对照表逐条收账:119 击疑点解除 —— 本场 1.5 带
149 击全是 `critDamageRate=150` 的会心,`victimMadnessOn`/`victimStatuses` 零漏读;ド・マリニー
942/942;受方狂気 3457/3457。剩余 2.3%(144 击)见 §7.2.75 四:先攒场次,不动手。

**攻方倍率已结算:不调。** 本场攻方狂気且受方非狂気 389 击,386 exact —— 旧队 1.3022 缺口确系
ド・マリニー 污染(§7.2.75 二)。

**复算入口**:重放脚本 `evidence_153_replay.py` 吃的两份战场(183213/191159)2026-10-03 起在
`_dpsm_work\export_archive_20261003.zip` 里 —— 解压后按原路径传入即可(清理记录:§7.2.74)。

---

### 9c. 现状 / 待办 / 目标(2026-10-03 深夜,专门写给接手协作的智能体)

**核心目标**:插件的伤害构成与归属要和游戏真实数字对得上,且每处改动都给证据(§1)。
**派生目标(用户当前的主线)**:把「每个角色 自身输出 + 团队 buff 对团队总伤害的贡献」做成可复算的表。

#### 已达成(全部实机,可复算)
* **伤害模型**:1.5.5 各场 `exact` 83–85%、`exactWithCrit` ~98%,`v150_validate.py` `problems=0`。
* **贡献表**(根目录 `dpsmeter_contrib.py` → `_dpsm_work/contrib_report.txt`):
  自伤 / 自身规则 / 受队友赋能 / 为团队赋能 四列**全部有主,未归因池 0.0%**(1.5.5 起,§7.2.80)。
  狂気(メアリー 自施加 ×2.5、ルゥ=ルルサ 给敌方 ×1.5)、赋予(マッドシーカー 的刻印授予)、
  全局规则(マッドシーカー 母なる変異の飛沫、メルティエル 海魔の残滓)都能点名到人。
* **搭配结论**:メルティエル+火砲(B)2 场 204.0 / 209.4M,**全部高于** ルゥ=ルルサ 组(A)4 场
  181.2–197.1M(两档不重叠)。原因 = 海魔の残滓 覆盖 99% 命中(×1.3225)对 狂気 ×1.5 只覆盖 51–60%
  (报告 `REPORT-两种搭配对比-20261003.txt`,记录 §7.2.78/§7.2.80)。
* 归因手段(下次要用同样的套路):**先反编译找字段/方法 → 再逐击导出 → 用数据验收**。
  A 的字段链失败后换成「施加侧钩子」才成功,教训写在 §7.2.78/§7.2.79。
* **离线贡献核心(阶段 A/B/C,2026-10-03,纯离线,未改插件)**:
  `_dpsm_work/CONTRIBUTION-DATA-DICTIONARY.md`(指标字典 + contribution JSON 草案 + S1–S12 手工样例)、
  `_dpsm_work/contrib/`(model / loader / attribution / aggregate / validate / report_text / report_json /
  run / compare;测试 `python -m contrib.tests.test_samples`)。1.5.5 场三条恒等式全过
  (逐击最大误差 1.16e-10、角色 1e-6/相对 3.8e-15、份额 1.000000000000),折叠记账 28369 = 28369 + 0,
  未归因 0.0%,攻击者主键覆盖 98.80%,byUnit given 6110/6110、madness 897/897,名称回退 0,
  总分可加和(基础 46,906,191 + 自身规则 91,670,167 + 辅助 65,289,054 = 203,865,411 = 分析池)。
  跨场(同 quest 411001):1.5.3 归因 73.86% → 1.5.4 85.26% → 1.5.5 **100.00%**(只反映归因元数据补齐)。
  记录 §7.2.82;命令见字典 §9/§10。

#### 待办(按价值排序,均不需要用户为验证专门打一场)
1. ~~D 攻击力加算区~~ → **已完成(1.7.4,`kind=atkadd`)**:队友给的攻击力加算按**给出者**归属;
   自己给自己的仍留 `baseCredit`(1.7.6 实测 `selfValues=7003`)。详见 §7.2.93/§7.2.94 与
   `_dpsm_work/CONTRIBUTION-TABLE-REPORT.md`。
2. **模型残差 2–2.9%**:1.5.3 场 144 击 = 0.87 多算带(≈1/1.15,条件折叠游戏没给)+ 1.15 漏折 17 击
   + `critDamageRate` 快照失配 ~28 击;1.5.5 的 comp B 场 `unexplained` 160(1.5.4 同组场 109)。
   **2026-10-04 增补(离线 8 场 40,269 击普查,§7.2.85)**:1.15 这条规则 **98.3% 完全对齐**;
   少折 ×1.15 164/×1.15² 56,多折 1/1.15 353/71/57 → **多折(481)多于少折(220)**,
   与 0.87=1/1.15 的多算带方向一致。k_extra 是净差,按规则标签分组后才能真正归因(未做)。
   **已确认与 1.5.5 无关**(该版只加归因元数据,一行折叠算术都没动)—— 待观察是否与 海魔の残滓 叠加相关。
3. **P3 会心观测**:`CritObserved` 11 场恒 0,会心只能靠 `critDamageRate` 推断。要真观测得另找通道
   (FlyText/伤害数字);注意 1.0.48/49 的 `ActDamage` 参数物化崩溃教训(绝不要照抄那种补丁)。
4. ~~小口径差~~ → **已定位(2026-10-03,**离线重放**,4 份导出一致)**:`totals.dealt` = "攻击者已识别的全部命中,
   **不分敌我**" = 我方 203,865,411 + 敌人打我方 14 击 99,347;而 `attacker="?"` 的 67 击 306,549 是**另一个**
   池(`totals.unattributedDamage`),`totals.taken` = dealt + unattributed。**不是计算错误,是既有口径定义。**
   复现脚本 `_dpsm_work/contrib_gap_dealt.py`(输出 `contrib_gap_dealt.txt`);1.5.3 / 1.5.4 / 试炼场同式成立。
5. **搭配结论已修正(§7.2.87 四)**:按队伍构成分组后 A(ルゥ=ルルサ)5 场 181.0–198.5M(均值 192.4)、
   B(メルティエル+火砲)4 场 197.1–209.3M(均值 204.3)—— **区间重叠,组内单场波动 5.8–8.8% 与组间差异同量级,
   不能断定 B 更优**。要判定需每组 6–8 场或控制变量;**在那之前不要再引用「B 更优」**。
   「メルティエル + ルゥ=ルルサ 同队」的叠乘假设仍可实测(要牺牲一个输出位)。
6. **阶段 E 已闭环**(1.6.0 实现、1.6.1 实机验收通过,§7.2.88);**F 游戏内 UI、G 攻击力拆分均已交付并部署**
   (1.7.0→1.7.7,§7.2.90–§7.2.96)。审计/设计/侦察三份预审已到齐并落地(§7.2.84)。
7. **跨场身份**:导出里没有持久角色 id(`actors[].key` 只在单场内稳定,`run.id/seq` 是场次计数),
   跨场比较只能按角色名(compare 报告已标注);样本 < 2 的组只列数值不给结论。
   要真正的跨场主键需要在插件里新增身份字段(属于阶段 C 的插件侧延伸,未做)。

#### 盘面快照
* **线上 1.7.29** = `D48E6F778FD993C4…`(525,312B;R79:**受击来源拆分** —— 回答「这一场里每个单位挨的伤害分别来自谁/什么」。受害侧每次命中按**游戏口径** `Nominal` 登记一行 `TakenHit`(站位在**该单位首次受击**时快照:1 前衛 / 2 後衛 / 0 未读出,之后移动不改写),`src/Policy/TakenBreakdownPolicy.cs` 按受害单位分组,给出 `byAttacker`(事件记的攻击者)/`bySource`(DamageSource)/`byHitType`(eDamageCalcType)/`byEffect`(技能 `m_effectId`)/`byStatus`(异常与**游戏记的**付与者)五个**各自完整划分同一笔 `nominal`** 的维度,并把「攻击者不明」「同队自伤」两条独立通道与 `taken + residual == nominal` 一起钉成恒等式(行为用例 + 导出守卫双重);桶上限 64、超出折叠成一条(**折叠发生在排序之前**),桶上的 `quality` 延续 `hitDetail.match` 的规矩(`""` 或 `近似 a/h`)。F3 = 「受击来源拆分」(页内 `Shift+F3` = 只看前衛):80 列单位行 + 每单位至多 5 条维度行(每行最多 3 个 part),最多 12 个受害单位,口径行逐字写着 `超出剩余耐久 = 前者-后者,不是被吸收`(全页 `被吸收` 只出现这一次)。导出新增顶层段 `takenBreakdown`(`schemaVersion 1.0` / `method by-event/1` / `basis nominal`),**`contribution` 段与已发布数值一字未动**(`contribution.schemaVersion` 仍 1.2,历史导出一字未改;线上 `exports\` 已 144 份);新增 7 个 src 文件(共 933 行)与新用例文件 `Cases.TakenBreakdown.cs`(204 行)、`Cases.TakenPage.cs`(172 行)。**两处故意偏离**:不加 `Debug/TakenProbe` 配置键;`CompositionProbe.Text.cs` 的三个标签方法改为委派新的纯策略 `src/Policy/DamageSourceLabelPolicy.cs`(字符串逐字未变)。**未做**:改已发布承伤数值(C 档,需先定口径)与 F6 逐条行的受击拆分。用例 **1328→1418**、变异 **233→244**)
* **上一版 1.7.28** = `2D9A5073468FCB5C…`(512,000B;R78:**给两个词正名** —— 面板上的 `实际伤害` 改成它实际印的量、`被吸收/无效化` 撤下。①过去的 `· 实际伤害 {finalDamage}` 在溢出命中上印的是 `BattleObject.Damage` 的**返回值**(R76 已证它是**溢出量**,定律 798/798),而 `· 被吸收/无效化 {absorbed}` 印的是 `nominal − res` = **目标的剩余耐久** —— 于是 Boss 身上飘 `596363` 的面板却写「实际伤害 96363 + 被吸收/无效化 500000」;现在普通命中印 `· 游戏口径 N`(= 游戏入参 `__0`),返回值报溢出的命中印 `· 超出剩余耐久(溢出) A · 目标剩余耐久 B(…非吸收;是否落地看命中前后 Life,本行不断言)`。②同一行的两个后缀过去 `if (absorbed > 0) … else if (theory > 0) …` **二选一**,恰好把会心 ×1.5 的超量命中行的 `剩余倍率` 藏掉(只剩 `96363/397575 = ×0.242` 这种假异常倍率)—— 现在互不排斥,且残差**分子改用游戏口径**(`AbsorbWording.ResidualBasis`),同一行上 `×1.500` 与「超出剩余耐久 96363」并排可见。③运行日志 `[ABSORB]` 行改名 `[OVERFLOW]`、战末行与 F6 汇总行同步换词;关键判定之后另加一行 `[ABSPROBE] note`(把判定翻成一句话,例:`verdict=oversizedPool ⇒ 耐久未变化(lifeDrop=0),无落地可观测;…非吸收。`)。④新增纯策略 `src/Policy/AbsorbWording.cs`(141 行 —— 正名文案的唯一落点,行为套件直接执行)与新用例文件 `tests/BehaviorTests/Cases.AbsorbWording.cs`(146 行 / 18 例)。**不改判定层、不改记账口径、不改导出键与数值**(`contribution.schemaVersion` 仍 1.2,143 份历史导出一字未改);**未做**:C 档(改 `Hooks/BattleObjectHooks.cs:51` —— 已发布的承伤数值在超量命中上仍记的是溢出量,修订需先定口径)与面板侧显示目标耐久变化(需新增跨层通道)。两处故意偏离已记账:普通命中取词按 `absorbed > 0` 分支;`brk.Residual`/`calc.residual` 保留原基数(它是 `forensics` 分桶键),只把**文本**基数换成游戏口径。用例 **1310→1328**、变异 **227→233**、源码 **112→113 个 .cs**。备份 `_dpsm_work/deploy-backup/pre-r78-42713774/`)
* **上一版 1.7.27** = `42713774815A1A47…`(509,440B;R77:**报告层两处读数缺陷**,判定层与记账一字未动。①`[ABSPROBE] sum` 的 `ovz=<次数>/<落地>/<溢出>` 原本把两个族群混成一行(金额对**所有** `IsOversized()` 累加、次数只对生命佐证的 `Oversized` 计数),于是 2026-10-07 那场读出 `ovz=0/6500000/2358285` 却配 `ovzPool=13` —— 现在 `ovz=` 的次数与金额同族群,另立 `ovzAll=`(全部 `Oversized*` 族群的独立计数器,恒等于四族群之和),顶层 `pool=`/`partial=` 改名 `ovzPool=`/`ovzPartial=` 以消除与 `carrier(...pool...)` 的同名歧义。②`None`(落空判定)没有计数器 —— `calls=5489` 减去可计族群 5470 = 19 次只能靠减法推 ⇒ 新增 `none=` 与 `full=`(**只**认量测到 `lifeDrop == nominal` 的整击落地),并新增用例保证 14 个判定计数之和恒等于 `calls`。③`Debug/AbsorbProbeMaxRows`(默认 400)不动 —— 本场 `rows=413 dropped=5057 key=13` 确已饱和,但属观测体量选择;新的 `none=`/`full=` 已能不靠行预算答出「整击落地多少次」。**不改判定层、不改记账口径、不改导出形状**(schemaVersion 1.2);用例 **1307→1310**、变异 **223→227**。备份 `_dpsm_work/deploy-backup/pre-r78-42713774/`)
* **上一版 1.7.26** = `0165D187…`(508,928B;R76:**超量命中单独判定** —— 把「被吸收/无效化」从**推断**改成**判定**。R75b 两场实机定出定律 `res == max(0, nominal − lifeBefore)`(800 条探针读数里 798 条可读的**全部成立、0 违例**)⇒ 游戏返回值是**溢出量**,插件 `被吸收 = nominal − res` 在超量命中上恰好等于**目标剩余 Life**,两个已发布字段**互换**。本轮:`res > 0` 自成一族 `Oversized*`(`落地 = nominal − res`、`溢出 = res`),**以 Life 移动为准** —— `lifeDrop == nominal` 一律判「整击落地」,R75 的假阳性桶 `masked`(790 行)随之删除;固定池对象(`ショゴス` 的 `Life` 恒 500,000、`inv=1`)单列 `OversizedPool`;固定池行、Life 读不到的行、`dropped` 行都不混进超量命中;关键行另有**独立行预算**(`Debug/AbsorbProbeKeyRows`,默认 200),免得 11 条决定性行再被行上限丢进 `dropped`。**不改任何已发布的承伤数值**(影响面:本轮 21 条 + 历史 408 条,单次最大 269,104;留待 R77 连同「无敌对象算不算承伤」一起定);用例 **1298→1307**、变异 **218→223**。备份 `_dpsm_work/deploy-backup/pre-r76-CFDD2BD5/`)
* **上一版 1.7.25** = `CFDD2BD5…`(506,368B;R75:**给「被吸收/无效化」加了一台「先读后命名」的探针** —— 该标签自 1.5.5 起只是 `nominal − damage` 的**别名**,却连着被当成机制读:46 份导出 **397 条**记录、**395 条差额恰好 500,000**、受害方**全是 `ショゴス`**,而 20 张主数据表里**没有任何字段/行/值等于 500,000**,boss 的运行时天赋只有 `1002 ModeChange` + `6 攻击力/150/-1`;`timeline` 488 行对受害的三个战斗对象**零行**。现在每场在既有的 `BattleObject.Damage` 前缀/后缀里读受击方 `Life`、`Character.Barrier`(`IsActived`/`mLife`)与五个无敌族标志,把差额**分类**成 `barrier/pool/unknown/takeover/fixed/invincible/masked/unreadable`(读不到 ⇒ `Unreadable`,**绝不**当载体),战末打一行 `[ABSPROBE] sum`;**不改记账口径、不改显示层、不改导出形状**(`schemaVersion` 1.2);障壁类 8 个 Harmony 补丁**默认关闭**(`Debug/AbsorbProbeHooks=false`)且**不进 `PatchAll`**(`PatchAll` 会安装所有带 `[HarmonyPatch]` 的类 ⇒ 带特性等于默认开;1.0.48/1.0.49 的启动崩溃发生在 thunk **转换参数**时,所以真正的保险是开关而不是签名)。本轮还纠正上一轮的错误结论:「500,000 与伤害大小无关」**不成立** —— `Hooks/BattleObjectHooks.cs` 的 `(__result > 0) ? __result : __0` 会把**整击被吸收**记成满额伤害,故那 397 条只是子集,新增的 `masked=` 桶专量这个盲区。用例 **1275→1298**、变异 **211→218**。备份 `_dpsm_work/deploy-backup/pre-r75-DF70F1A9/`)
* **上一版 1.7.24** = `DF70F1A97F3A…`(489,984B;R74:**修「测量仪」的读数单位** —— 你第三场仍然「初动 6 秒的技能 5 秒就显示放出来了」。R71 的算术与 R72 的时序都没错,错在校准读的是 `Skill.FirstCoolTime`(**秒**,邪龍の息吹 = 6)却当成**单位**去和 `WaitCountFrame`(= 150)比 ⇒ `waitFrames > firstCoolFrames` 把**每个槽、每一帧**都拒掉 ⇒ `samples` 恒 0 ⇒ 原点永不 decided ⇒ `[CLOCK] origin=none` 是每一场的必然(R73 的证明:`samples=` 是跨帧保留的最大值;扣住让 `EventCount` 恒 0 所以 `events` 抢不了先;units/窗口/名单三条拒因全被排除;按单位算 7 个槽全都 ≤ 主表初动 ⇒ 本该当场 applied)。现在:①初动改读**单位制**的 `Skill.m_data.m_firstCoolTimeFrame`(字段在**主数据**类上;`Skill.m_firstCoolTimeFrame` 是 `CS1061`),秒值 ×units 只作**记录在案的退路**;②拒因**具名** `LagReason`,`TryLag` 成 `Reject` 的薄包装;③新增 `[CLOCK] calib` 行(attempts/party/slots/usable/via(field/fallback)/first(sec/frame/wait)/rejected(noFirst/wait/units/window/range))—— 决定帧**必然**过不了窗口条款,旧的 `samples=0` 天然为 0;④技能时间线进证据包 `skill_timeline.txt`(+manifest 校验),页面时刻从此可文件级复核;⑤`[AUTOSK] chg` 增 `first=`/`firstFrame=`(读不到打 `?` 而不是 0)。**口径与 1.7.23 相同**,只是仪表终于能自证;**不动任何伤害/归属数值、不改导出形状**(`schemaVersion` 1.2);用例 **1254→1275**、变异 **203→211**。**仍未定案**:`m_firstCoolTimeFrame` 是初始/目标值还是实时剩余值(本轮不写死,实时剩余会让 lag≈−active 被既有边界拒),以及实机确认 —— 需要一场新战斗的 `[CLOCK] origin=+0.9Xs` + `calib … usable=N via(field/fallback)=N/0`。备份 `_dpsm_work/deploy-backup/pre-r74-F83D369E/`)
* **上一版 1.7.23** = `F83D369E4C10…`(486,912B;R72:**让 R71 的原点修正真正落地** —— R71 算对了 +0.90 s 却每场都被 `reason=events` 拒绝(它只从第 2 帧起尝试,而游戏在创建 session 的同一帧就打出了开局伤害)⇒ 你看到「发动时刻还是比初动早、完全没有改变」。现在①**在创建 session 的当场**尝试对齐,②证据来晚时把开局伤害/治疗**先扣住**再按**到达时刻 + lag** 重走记录器(纯策略 `ClockOriginHoldPolicy`,有界 256、溢出即拒绝 `reason=hold`),③不走记录器的几处按同一常数搬,④`[CLOCK]` 行每条分支都带 `samples=/active=/hits=/held=`。**口径与 1.7.22 相同**(游戏自己的战斗开始),只是现在真的被应用;**不动任何伤害/归属数值、不改导出形状**;用例 **1205→1254**、变异 **189→203**)。上一版 **1.7.22** = `5DB365C62D35…`(481,792B,备份于 `_dpsm_work/deploy-backup/pre-r72-5DB365C6/`;R71:**战斗钟的原点对齐到游戏自己的战斗开始**(补回开局 0.90 s)。三条独立测量一致:自动技能充能计数器(槽开局初始化为 `FirstCoolTime`、发动后重置为 `CoolTime`)、游戏自己的屏幕倒计时不变量、页面「最早发动」对照主表初动;开局第一次采样一次性前移,边界写在 `BattleClockCalibrationPolicy`(纯策略),拒绝理由进 `[CLOCK] origin=none reason=…`;页面加「时刻起点 = 游戏自己的战斗开始」那行;**旧文件沿用旧原点**,按秒桶边界平移 0.9 s;**不动任何伤害/归属数值、不改导出形状**;**实测该版一次也没应用成功,见 1.7.23**)。上一版 **1.7.21** = `5D0942C743CE…`(475,648B,备份于 `_dpsm_work/deploy-backup/pre-r71-5D0942C7/`;R70:时间表**只统计我方**(三路统一团队判据 `SkillSidePolicy`,页面通道行加 `剔除非我方 N 条`)+ 一行的发动时刻放不下就**往下撑**(续行只印时刻、每行 14 个、最多 3 行,`+N` 移到最后一个续行))。上一版 **1.7.20** = `9E66D3EE9D89…`(473,600B,备份于 `_dpsm_work/deploy-backup/pre-r70-9E66D3EE/`;R69:**「调用 ≠ 发动」** —— 发动判据改为游戏自己的 `status=Using`(新纯策略 `SkillActivationPolicy`),`Charge`/`Usable` 记为试触发;折叠窗口改成**该技能自己的冷却**(9 场语料 184 违规 → 0、411001 那场 35 → 0);页面加 `试N`;撤回 R66「充能不是发动的闸门」;删掉 R66 的记录钩子与 `rec` 通道)。上一版 **1.7.19** = `5BA37D6C896E…`(472,576B,备份于 `_dpsm_work/deploy-backup/pre-r69-5BA37D6C/`;R67:用户截图核对时间表后修连发标记(`并N条M格`)与时间格宽度,奥义/特殊改挂 `GameCmdExecuter.ActExecutePlayer{ActiveSkill,Skill,SpecialSkill}`(R66 的记录钩子一整场未被调用),`gameIdx` 结案)。上一版 **1.7.18** = `D17400F05314…`(468,480B,备份于 `_dpsm_work/deploy-backup/pre-r67-D17400F0/`;R66:**技能发动时刻做成悬浮窗的一页 + F4 换岗** —— 面板可见时 F4 打开「技能时间表」(我方每单位 × 每技能的发动的战斗时钟秒 + `xN`/`n=`/`med=` + 两条通道条数),面板隐藏时 F4 仍是按需证据包;新增只读探针 `SkillTimelineProbe` + 只读后缀 `GameCmdExecuter.AddPlayerSkillGameRecord`(**唯一**能同时看到奥义/特殊/自动的观测点,**是否等于发动时刻尚未证实**);用 9 场语料修正 R65 两条口径 —— `minCoolTime`/`maxCoolTime` 是**技能等级区间**(L1 = min、满级 = max;5 单位 × 2 等级 10/10),~~**充能不是发动的闸门**~~(**R69 已撤回**);トレイラ 中位间隔 **9.00 游戏秒**(47 个样本);探针另加 `inst=`/`cmdidx`/`act=`;**不动任何伤害/归属数值、不改导出形状**)。上一版 **1.7.17** = `FA8E59188E0A…`(454,144B,备份于 `_dpsm_work/deploy-backup/pre-r66-FA8E5918/`;R65:实机数据证伪 R63 的 ×30 —— `auto_skill` 主表的 `minCoolTime`/`maxCoolTime` **就是游戏自己的充能单位**(线上 `Skill.CoolTimeFrame` 字面相等 9/9),**删掉** `*CoolTimeFrames` 两列错数与产生它们的 `SkillCooldownPolicy`(+16 例用例/2 条变异),信封改给实测 `unitsPerGameSecond`;探针修 4 处 + 新增 `[AUTOSK] roster`;**当前语料 82 份**,9999 一律 `not_comparable`)。上一版 **1.7.16** = `5F9234EF331E…`(451,072B,备份于 `_dpsm_work/deploy-backup/pre-r65-5F9234EF/`;R64:自动技能的**运行时**探针 `AutoSkillProbe` —— `[AUTOSK] act`/`chg`/`SUM`,读的是线上 `Skill` 实例;纯规则 `AutoSkillCadencePolicy`;**不动任何既有数值**)。上一版 **1.7.15** = `68E10640CC92…`(438,272B,备份于 `_dpsm_work/deploy-backup/pre-r64-68E10640/`;R63:「自动技能」主表转储(`auto_skill`,20 张表 / 输出 21 个文件)⚠️ 它带的 `SkillCooldownPolicy` 已被 R65 删除)。上一版 **1.7.14** = `C9D1B0CC9FAF…`(435,712B,备份于 `_dpsm_work/deploy-backup/pre-r63-C9D1B0CC/`;R62:命中记录错配「可证伪即拒绝」+`hitMatch=3`/`hitDetail.matchRejected`、离线残差游戏口径、全局敌方受伤因子移入 `takenMult`)。上一版 **1.7.13** = `32BFEC3B6A88…`(435,200B,备份于 `_dpsm_work/deploy-backup/pre-r62-32BFEC3B/`;R61/用户判断:同队/自我伤害移出归属池,`contribution.schemaVersion` 1.2;`damageLedger` 增 `selfTeam*`;离线核心按文件的 `schemaVersion` 选模式)。上一版 **1.7.12** = `F791F1CFD0E5…`(434,688B,备份于 `_dpsm_work/deploy-backup/pre-r61-F791F1CF/`;R60:自伤三件套 + 根 `config.filterFriendlyFire` + crosscheck 的 1.1 `totalDamage` 修正;R57/R60 的记录在 `REFACTOR-BATCH-*.md`,不再新增 `SESSION-STATE.md` §7.2.x)。上一版 **1.7.11** = `C1DBBD8FF43F…`(433,664B;R57:引用绑定持久文件 + 战斗编号 battle-ref/1 + 精确选场 `battle_select.py`;备份于 `_dpsm_work/deploy-backup/pre-r60-C1DBBD8F/`)。更早的**基线 1.7.11** = `36EC96D4DBD8…`(387,072B;F5 输出分组 `自身 \| 他人因你 \| 被队友分走` + 两条逐角色恒等式 + 版面守卫双向化 + `check_live_log` 两处误判修复;§7.2.100)。前一版 **1.7.10** = `BF2F174A4059…`(386,560B;闸门伪运行修复 + `[COMP]` 重算开关对齐;§7.2.99)。1.7.9 = `F3F73C81FF3D…`(GivenTalent 开关下传 + 逐击 `calc.givenFoldOn`、`giveApplied` 双计修复(新增 `rosterAudit.giveFoldHits`)、UI-DIAG `unattrRow`、`check_live_log.py`;§7.2.98),
  前一版 **1.7.8** = `0B3398364628…`(386,048B;**贡献契约 + 五任务集成 §7.2.97**:P0-B `totalDamage=analyzableDealt` 与 `damageLedger`/三覆盖率(P0-A 验证闸门状态机、P0-C 配对影响 A≡B、P0-D 任务适用性 full10/partial9/not_comparable7、P1-A `atkadd` self 改 actor key、P2-A 上一场缓存与未归属透传)),
  1.7.7 = `7425139C3652…`(381,952B;表格对齐打包 §7.2.96:`Fit` 按显示列截断 + 合计行同几何 + 取整统一 + `Amt` 余量 + 字体节流 + `knownLimits` 与离线一致),
  1.7.6 = `BB96DA65E9A8…`(379,904B;F5 改等宽字体 + 口径说明 §7.2.95)、1.7.5 = `64EDA364851D…`(378,368B;改正 `knownLimits` 陈旧断言;`atkadd` 已于 1.7.4 实机验收:ルナリス 25.2%,§7.2.94),1.7.4 = `6B6691977BA3…`(378,368B;**含** F5 表格页 §7.2.90 + `paramOwners` §7.2.91 + 扫描完整性 §7.2.92
  + **攻击力加算归属 atkadd** §7.2.93),备份 `.1.7.3.bak` = `238799AB…` / `.1.7.2.bak` = `71C83819…` / `.1.7.0.bak` = `C1A4F8C1…`;
  **owner 语义已被数据判定为「给予者」**(§7.2.92:ownerOther 12,539 vs ownerSelf 2,944,两侧不一致 0),跨单位攻击力 owner 唯一 = ルナリス;
  回退链:
  `DpsMeter.dll.1.6.1.bak`(`C67055E6AE35…`,**已实机验收通过**)、`DpsMeter.dll.1.6.0.bak`(`A286224C74B2…`)、
  `DpsMeter.dll.1.5.5-verified.bak`(`18E4D933…`)。**1.7.0 的界面还差目视验收**(UI 只能由使用者确认)。
  **1.6.0 的实机验收抓到一个真缺陷**(导出把 factor 截成 4 位 → 贡献段无法从文件复算,58 个字段 ~1e-5),
  1.6.1 改为往返精度输出(§7.2.87);**1.6.1 已于 2026-10-04 02:19 实机验收通过** ——
  `crosscheck` status=OK / mismatches=0,schema 与 v150 problems=0,**阶段 E 闭环完成**(§7.2.88)。
  (`.bak` 档案实测 2026-10-04:插件目录 **105** 项,其中 `.bak` **102** 个、`DpsMeter.dll.*.bak` **34** 个;回滚档案不是文档;**两个 crash.bak 绝不回滚/删除**)。
* **当前语料 = 冻结快照 35 份**(411001×25 / 试炼场 9999×9 / 700817×1;版本 1.5.3→1.7.11;**22 份含 `contribution` 段**(schema 1.0 ×13 / 1.1 ×9),13 份无段 = 1.5.3–1.5.5,1 份 1.6.0 不可复算;**快照外的场次不计入本批**。**历史口径**:按 32 份重跑 = full 14 / partial 9 / not_comparable 9(29 份时为 12 / 9 / 8):新增的 1.7.10/1.7.11 场次全部落进既有类别,9999 训练场一律 `not_comparable` —— **没有出现新类别**)。
  **B 组 5 场**(197.1–209.3M,均值 203.0),A 组 5 场(181.0–198.5M,均值 192.4),**区间重叠**(§7.2.88)。
* 守卫 **17 项 Python 闸门**全 exit 0(export_schema / **check_contribution_layout** / **contrib.tests.test_gate** / **crosscheck --selftest** / fact_signature(含 --selftest)/ refactor_final_check / docs_123 / v150_validate / test_samples / golden155 / **phase_e_dryrun** / **contribution_applicability** / **pairtrusted_impact** / **check_p2a_summary_and_lastbattle**);
  `recon_probe` ALL CHECKS PASSED(179 个断言调用点);构建 0 警 0 错。
* 阶段 A/B/C 的最终状态(§7.2.82):contrib 命令退出码全 0,输出留档 `contrib_verify_155.txt`;
* **1.7.7 上线证据**(§7.2.96):离线布局守卫 `check_contribution_layout.py` 在 13 份导出 / 441 行上 **0 违规**,`--selftest` 用 1.7.6 语义反证 **164 处违规** ⇒ 守卫有牙;两个对抗性预审 subagent 的发现(roster 预算退化、kind/side 未护、数值列无余量)已全部在 rev2 修掉。
  **重新构建的 DLL 与当时部署中的 1.5.5 哈希完全一致**,部署未被改动。
* **高风险改动的前置(§7.2.83,用户指令)**:
  * 回退锚点已建立:`DpsMeter.dll.1.5.5-verified.bak`(340,992B,`18E4D933…`)
    + `_dpsm_work\src_snapshot_155.zip`(288,979B,`E4A0D4DB…`,解包回环 62 文件全命中);
    步骤与判据见 `_dpsm_work/ROLLBACK-1.6.0.md`。
  * 阶段 E 硬验收闸门已备好:`python -m contrib.crosscheck`(插件 `contribution` 段 ↔ 离线核心逐字段比对;
    `--selftest` 实测能接受忠实数据、拒绝被篡改数据)。回归证据 `python -m contrib.legacy_diff`
    裁决 `MATCHES-LEGACY-ON-SHARED-COLUMNS`(新核心没改任何既有数字)。
  * 预审在跑(3 个智能体):贡献核心审计 / 阶段 E 设计 / 阶段 G 侦察 → `_dpsm_work/REVIEW-*.txt`。
  * 阶段 E 干跑已过(`contrib.phase_e_dryrun`):真实导出 + 离线算出的段 → schema 守卫 problems=0 + crosscheck OK;
    体积实测:聚合段 25.9KB(0.116%),逐击明细 1.4MB(6.26%)→ 聚合常开、逐击明细放开关。
    `check_export_schema.py` 已能校验可选的 `contribution` 段,自测 17 例全过。
  * 阶段 E 的**算法本体已成两个独立实现**:Python 核心与 `_dpsm_work\contrib_cs`(C#,src 之外),
    在真实 1.5.5 场上**逐项一致**(总数/前五角色/11 角色/21 规则/20 关系),两者都过两道闸门。
    距落地只差接线(内存事件版 + ExportService 追加 + cfg 开关 + 版本号)。
* **新发现并已修(§7.2.83 五)**:把核心跑在**试炼场导出**上暴露负贡献(credited 412% / unattr −312%);
  根因是 0 < factor < 1 的减伤折叠被计入 M。已改为不计入 M + 默认拒绝分析 quest 9999(须 `--allow-training`),
  新增单测 S13 与负份额断言;**普通战斗数字零变化**。
* `_dpsm_work` 顶层 103 个文件(新增贡献核心的 4 个 recon 探针与输出 + `contrib_gap_dealt.py` + `contrib_verify_155.txt`);
  可再生的 artifact(反编译 dumps、`__pycache__`)用完即删,别囤。`contrib/` 下 17 个文件(含 5 个报告)。

#### 工作方式(硬约束,来自用户)
* **不要为了验证一个假设就要求用户再打一场** —— 能离线判的必须离线判完(§1)。
* 每个数字都要能复算;写文档必须标注**实测 / 离线重放 / 推断**(§11.3)。
* 改完立刻跑:构建 + `recon_probe` + 五个守卫;改 .md 后回读那几行(§2 编码/工具坑 4)。
* 文件交流用编辑/写文件工具,不要用 PowerShell 整文件读写含中文的文件(§2 坑 1–2)。

---
## 10. 细节在哪(别重复劳动)

* `_dpsm_work/SESSION-STATE.md`(**4583 行,主档**)
  * §1–§6:目标、关键路径、热键、已实现功能、关键 API 发现、已知硬限制
  * §7.2.1 … §7.2.100:**逐版决策与实测记录**,`§7.2.100` 是**最后一个带版本号的条目**(R57/R60 改为 `REFACTOR-BATCH-R57.md` / `REFACTOR-BATCH-R60.md`);§7.2.100 = 1.7.11:F5 输出分组 `自身 \| 他人因你 \| 被队友分走` + 两条逐角色恒等式 + 版面守卫双向化 + `check_live_log` 两处误判修复;§7.2.99 = 1.7.10:闸门伪运行修复 + `[COMP]` 开关对齐;§7.2.98 = 1.7.9 集成:GivenTalent 开关下传 + 逐击 `calc.givenFoldOn`、`giveApplied` 双计修复 + `giveFoldHits`、UI-DIAG `unattrRow` + `check_live_log.py`;
  `§7.2.91` = (**母表实测推翻 §7.2.90(B)** + 参数所有者通道 1.7.2);
  `§7.2.90` = F5 独立表格页 + 友军攻击力核查(其 (B) 小节「友军给攻并不存在」**已作废**,原因见 §7.2.91 第一节:
    §7.2.89 = 名单页总贡献看板,1.7.0;
    §7.2.88 = 阶段 E 实机验收通过,1.6.1;
    §7.2.87 = factor 截断缺陷与结论修正;
    §7.2.85 = 第三场 B 组 + 1.15 规则普查;
    §7.2.84 = 阶段 E 实现与三份预审;§7.2.83 = 回退锚点/验收闸门;§7.2.82 = 阶段 A/B/C;§7.2.80 = 1.5.5 验收)
  * §7.3:IL2CPP 类型坑;§9 部署流程;§10 探针开关
  * §4c:验证工具表
* `_dpsm_work/ARCHITECTURE.md`(**907 行**)
  * §1 模块地图(61 个 .cs);§2 一次命中的数据流;§3 构成模块;§4 **不可破坏的约定 1–44 条**(44 是最新的「连接词是语法」)
  * §4c/§6b:加"结构化孪生键"的配方;§7 已知技术债
* `_dpsm_work/ARCH-REVIEW-1.5.md`:1.5.0 那次架构审视(路线 A/B/C 的来源)
* 根目录 `DpsMeter-文档索引.md`:**唯一的当前状态索引 / 总入口**(2026-10-03 起;§7.2.81 起为唯一入口文档)
* 根目录:**只剩 `DpsMeter-使用说明-完整版.txt`(面向玩家)+ `DpsMeter-文档索引.md`(唯一的当前状态
  索引 / 总入口)**。逐版说明 1.4.0–1.5.5(8 份)2026-10-03 深夜归档删除到
  `_dpsm_work\doc_archive_20261003.zip`;0.9–1.3.10 的 84 份更早已删 —— 决策记录仍在 SESSION-STATE §7.2.x
* `_dpsm_work/evidence_1152_*.{py,txt}`:1.5.2 的诊断证据(残留对照、探针输出、日志行)
  * `_dpsm_work/evidence_152live_*`:1.5.2 **实机**这一场的全部复算证据与结论,
    主件 `evidence_152live_VERDICT.txt`(判据逐条结算;1.5.1 反编译对照已于 2026-10-03 删除——可再生)
* 根目录 `dpsmeter_contrib.py`:**队伍贡献分析 Stage 0**(§7.2.76/§7.2.77)—— 自身输出精确;1.5.4 导出起
  given/madness 按 `byUnit` 归因,无来源/队外 → **排除单列**(用户口径);报告 → `_dpsm_work/contrib_report.txt`。
  ⚠ 它是**旧口径**(直接伤害与"为团队赋能"两列重叠,总贡献不可加和);新版核心见下一条,新结论以新版为准。
* `_dpsm_work/CONTRIBUTION-DATA-DICTIONARY.md`:**贡献指标字典(阶段 A)**——口径/公式/归属阶梯/理由码/
  JSON 草案/S1–S12 手工样例/§10 验收数字。**改贡献口径必须先改这里。**
* `_dpsm_work/contrib/`:**阶段 B/C 离线核心**(method=log-share/1,只读导出,不碰插件)
  * `python -m contrib.run` → 单场报告 `contrib/reports/report_<导出名>.txt` + `contribution` JSON
    + `…legacy.txt`(Stage 0 兼容视图)
  * `python -m contrib.tests.test_samples` → S1–S12 单测(不需要任何导出;改核心后必跑)
  * `python -m contrib.compare` → 跨场比较(按 任务/插件版本/队伍构成 分组)`contrib/reports/compare_*.txt`
* `_dpsm_work/ROLLBACK-1.6.0.md`:**回退预案**(1.5.5 锚点、回退步骤、成功判据、实施纪律)。
* `src/Output/Contribution.cs`(纯核心)/ `src/Output/ContributionSession.cs`(战斗模型适配器):
  **阶段 E 1.6.0 的贡献段实现**;核心无 Plugin/IL2CPP 依赖,故 recon_probe 与
  `_dpsm_work/contrib_cs`(把**真实源码**编进离线工程)都能直接跑它。
* `_dpsm_work/contrib/tests/test_golden_155.py`:把已公布的 1.5.5 数字(含每一类理由的观测计数)钉成回归测试。
* `_dpsm_work/review_contrib_core/`:审计方自己的独立脚本与原始输出(可复跑复核审计结论)。
* `_dpsm_work/REVIEW-*.txt`:子智能体预审报告(贡献核心对抗审计 / 阶段 E 设计 / 阶段 G 侦察)。
* `_dpsm_work/contrib/crosscheck.py` / `legacy_diff.py` / `rule115_census.py`:阶段 E 硬验收(交叉实现比对 + 自测)、
  新旧口径回归、以及 **1.15 规则普查**(残差结案材料,计划阶段 G 第 1 项,见 §7.2.85)。
* `_dpsm_work/PROJECT-DIRECTION-AND-IMPLEMENTATION-PLAN.md`:**用户给出的总方向 + A–G 分阶段计划 + 智能体分工**,
  顶部有进度行。**下一步主线以它为准**;阶段 E 已闭环,F/G 已交付并部署。
* `_dpsm_work/export_archive_20261003.zip`:旧导出清空后仅存的 3 份战场(2.2MB;解压可复算 1.5.3 离线重放)
* `_dpsm_work/evidence_153live_{py,txt}`:1.5.3 **实机**验证(205449 场)—— 逐带收账、2.3% 未知面明细
* `_dpsm_work/evidence_contrib_join.*` / `probe_join_detail.*`:贡献归因的联结测试与结构探查(§7.2.76)
* `_dpsm_work/recon_probe/`:离线断言工程(**改对账/Fold/JSON/StatusKey 语义时必须在这里加断言**)
* 关键开关:`BepInEx\config\dev.dpsmeter.cfg` 里 `DamageComposition`/`Forensics`/`ReconcileCalc`/
  `GivenTalent`/`StatusResist`/`Madness`/`StateTimeline`/`FactStore` 现均为 `true`。
  另:1.5.4 起 `MadnessApplier`(狂気自施加/施加者)、1.5.5 起 `GivenGiverHook`(授予来源)默认 true。
  ⚠ `RuntimeLog.Init()` **每次启动游戏会删日志**,所以只有最新一场的 `[RULE]/[STATE]/[PARAM]` 留存。

---

## 11. 协作时请特别注意

1. **不要相信本文件或 SESSION-STATE 里的数字,除非你复算过。** 本次会话里我自己就写错过两次
   (一次把"重放服务次数 = 插件计数"当成交叉校验通过,其实是循环论证;一次把 `类溢出` 当成新签名数)。
   两处都已更正,并且工具现在会主动打印警示。
2. **改动前后都要留下"哪个数字因此变了"的记录。** 用户明确要求每处改动给证据。
3. **区分三件事**:什么是**实测**的、什么是**离线重放**的、什么是**推断**的。写文档时必须标出来。
   目前:伤害模型 = **实机**(exact 83-85%、exactWithCrit ~98%);15² / 受方狂気 / 攻方狂気 = **实机**;
   贡献归因(byUnit)= **域内实机 100%**(§7.2.80);残差 2-2.9% = **实测量出、机制未判**;攻击力加算区 = **已拆**(1.7.4 起队友给的按给出者 `kind=atkadd` 归属,自己给自己的仍留 `baseCredit`,§7.2.93)。
4. 遇到查不出来的东西,**写"没查出来"并说明已排除了什么**,不要编机制(见 P2 的处理方式)。

> **部署状态(R67 起)**:DLL = **5BA37D6C**…(**472,576 字节**,1.7.19;用户截图核对后修连发标记 `并N条M格`、时间格 5→6 列,奥义/特殊改挂 `ActExecutePlayer{ActiveSkill,Skill,SpecialSkill}`(通道 `skl`),`gameIdx` 结案)。上一版 **D17400F0**…(468,480 字节,1.7.18,备份于 `_dpsm_work/deploy-backup/pre-r67-D17400F0/`;①**F4 换岗**(用户选定):面板**可见**时 F4 打开新页「技能时间表」,面板**隐藏**(F8)时同一个键仍是**按需证据包**,战斗结束的自动抽取不变;②**9 场语料修正 R65 的两条口径** —— `auto_skill` 的 `minCoolTime`/`maxCoolTime` 是**技能等级区间**(L1 = min、满级 = max;5 单位 × 2 等级共 10 个观测点),**充能不是发动的闸门**(471 次发动里 157 次在 `Charge`;≥6 个单位的中位间隔短于充能 ⇒「充满后等下一次普攻」作为通则**撤回**);③新增只读探针 `SkillTimelineProbe` + 只读后缀 `GameCmdExecuter.AddPlayerSkillGameRecord`(**唯一**能同时看到奥义/特殊/自动的观测点,**是否等于发动时刻尚未证实**,故页面自报通道条数);探针另加 `inst=`/`cmdidx`/`act=`;**不动任何伤害/归属数值、不改导出形状**)。上一版 **FA8E5918**…(**454,144 字节**,1.7.17;R65:实机数据证伪 R63 的 ×30 —— `auto_skill` 主表的 `minCoolTime`/`maxCoolTime` **就是游戏自己的充能单位**(线上 `Skill.CoolTimeFrame` 与主表值字面相等 9/9),**删掉** `minCoolTimeFrames`/`maxCoolTimeFrames` 两列错数与产生它们的 `SkillCooldownPolicy`(+16 例用例/2 条变异),信封改给实测 `unitsPerGameSecond`;探针修 4 处 + 新增每单位一行 `[AUTOSK] roster`)备份于 `_dpsm_work/deploy-backup/pre-r66-FA8E5918/`。**当前语料 82 份**(411001/700817/试炼场 9999;9999 一律 `not_comparable`)。上一版 **5F9234EF**…(**451,072 字节**,1.7.16;新增只读探针 `AutoSkillProbe` —— 从**线上 `Skill` 实例**侧把自动技能的**发动时刻**(`[AUTOSK] act`)/**充能轨迹**(`[AUTOSK] chg`)+ 收尾中位间隔(`[AUTOSK] SUM`)写进运行日志,并新增纯规则 `AutoSkillCadencePolicy`)备份于 `_dpsm_work/deploy-backup/pre-r65-5F9234EF/`。上一版 **68E10640**…(**438,272 字节**,1.7.15;新增「自动技能」主表 `auto_skill` 的转储(20 张表 / 输出 21 个文件)+ 冷却单位纯规则 `SkillCooldownPolicy`(⚠️ R65 已删);**不动任何既有数值与既有 credit**)备份于 `_dpsm_work/deploy-backup/pre-r64-68E10640/`。上一版 **C9D1B0CC**…(435,712 字节,1.7.14;命中记录错配改「可证伪即拒绝」+`hitMatch=3`/`hitDetail.matchRejected`、离线残差改游戏口径 `model.game_residual`、全局敌方受伤因子移入 `takenMult`)备份于 `_dpsm_work/deploy-backup/pre-r63-C9D1B0CC/`;上一版 **32BFEC3B**…(435,200 字节,1.7.13;同队/自我伤害移出归属池、`schemaVersion` 1.2)备份于 `pre-r62-32BFEC3B/`;上一版 **F791F1CF**…(434,688 字节,1.7.12)备份于 `pre-r61-F791F1CF/`;上一版 **F6948470**…(433,664 字节)备份于 `_dpsm_work/deploy-backup/pre-r57-F6948470/`,更早在 `pre-r56-390C1340/`、`pre-r55-76CEAC00/`、`pre-r54-3A89D30A/`、`pre-r53-AA836C06/`、`pre-r52-28B8CCAF/`,基线 **36EC96D4**…(387,072 字节)在 `baseline-1.7.11/`,回退为一条 Copy-Item。**当前语料 103 份**(411001/700817/试炼场 9999;9999 一律 `not_comparable`)。
