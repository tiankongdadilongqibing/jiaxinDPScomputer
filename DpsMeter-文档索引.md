# DpsMeter 项目当前状态索引

> **这是唯一的入口文档**(2026-10-04 第 6 轮更新)。其他智能体读完这一份即可接手,细节按第 8 节指针去查。
> **想知道“此刻项目是什么状态、能不能重构、先动哪里”,读 [`PROJECT-STATUS.md`](<_dpsm_work/PROJECT-STATUS.md>)**;
> 本文件只回答“去哪查”。两条维护规则:①本文件与 PROJECT-STATUS.md 一起更新;②历史报告的历史段落不重写,只补“当前状态句”。
> 逐版说明(1.4.0–1.5.5,8 份)已归档删除到 `_dpsm_work\doc_archive_20261003.zip`
> (39.7KB,SHA256 `76F53B1EEAAE31D6B7773627950A5C2DE051B5A113A700B32ED1FD563AF11997`,逐文件哈希回环校验过)。

## 1. 项目是什么

给单机 DMM 游戏 **邪神戦記 ルルイエ少女隊X**(Unity 6000.3.5f2 / IL2CPP)写的 BepInEx 伤害统计插件
`DpsMeter`:挂游戏钩子实时统计,战斗结束导出一份 JSON 供离线分析。

**核心目标**:插件的**伤害构成与归属**要和游戏里的**真实数字**对得上,且**每处改动都要有证据**。
证据分三级,写文档必须标注:**实测 / 离线重放 / 推断**。

## 2. 现在装的是什么

| 项 | 值 |
|---|---|
| 版本 | **1.7.15(已部署,R63)** —— R63:把「自动技能」主表 dump 出来(新增 `auto_skill` 表 ⇒ **20 张表 / 输出 21 个文件**),自动技能的冷却终于有**游戏自己的数**可引,不必再从战斗数据反推;并新增纯规则 `src/Policy/SkillCooldownPolicy.cs`(主表存**秒**、线上 `Skill` 数**帧**,30 单位/游戏秒是实测值),同一行**同时**给 `*CoolTime` 与 `*CoolTimeFrames`,免得消费方把两个单位混着用。**实机已确认**(用户自行重启 1.7.15 后):自检 `表成功=20 / 缺失=0 / 行异常=0`、`auto_skill=120` 行、`masterdata/auto_skill.json` 落盘;并附一条**口径警告**:`auto_skill` 表的 id 与伤害里的**效果 id** 不是同一 id 空间,她那一行(`暗沌への導き`)的主表冷却是 **240–300 秒**,与上一轮从伤害反推的 ≈13.5 s 对不上,该反推**转为存疑**(R63 §3c)。**不动任何既有数值与 credit**(`schemaVersion` 仍 1.2)。上一版 **1.7.14(已部署)** —— R62 三件事:①**命中记录错配**:导出里 `source`/`calcHitType`/`calcEffectId` 是「最佳努力配对」的产物,实测训练场 9999 里 **3,240/11,096 条贯通命中被记进了 `sources[0]`**(普通关卡 411001 只有 6/39,467);现在当「合成能证明这一击、而记录说它是另一种命中属性」时**拒绝入账**,并新增 `hitMatch=3` 与 `hitDetail.matchRejected` 自报计数。②离线残差改用**游戏口径** `(applied+absorbed)/theory`(`model.game_residual`),吸收击不再被当成「缺倍率」。③全局「毒/火傷状態の敵全ての被ダメージ+15%」因子从 `dealtMult` **移入 `takenMult`**(乘积不变,分开读与伤害/被伤害才对)。上一版 **1.7.13(已部署)** —— R61:同队/自我伤害(敌方 `回復反転`)移出归属池,`contribution.schemaVersion` 升 **1.2**(实测同一场分析池 6,146,573 → 4,222,552)。上一版 **1.7.12(已部署)** —— R60:根 `config.filterFriendlyFire`(唯一会改变既有数字含义的开关,现在文件自述)、`contribution.actors[].friendly/friendlyHits/hostileDamage`(逐事件分类,`friendly+hostileDamage==directDamage` 由 schema 守卫强制)、F5 表 1 恢复「自伤」列(`T1` 行宽 85→**94**,Contribution 面板 780→**880**),并修 `contrib/crosscheck.py` 对 schema 1.1 段 `totalDamage` 的 1.0 旧公式。上一版说明保留在此:**1.7.11**(F5 表 1 改为 `自身 \| 他人因你 \| 被队友分走`,`自身 = 基础 + 自身规则`;`receivedAssist` 首次进表;两条逐角色恒等式进 `check_export_schema`;版面守卫新增「渲染器↔副本漂移」检查;`check_live_log` 两处误判修复;§7.2.100)。前一版 **1.7.10**(§7.2.99:验证闸门 `give_section_reasons` 读错对象而**从未真正运行** —— 改读 `rosterAudit` + 真实文件负控;`[COMP]` 重算遵守 GivenTalent / Madness / MadnessVictim 三个开关)。更前一版 **1.7.9**(§7.2.98:GivenTalent 开关下传 + `calc.givenFoldOn`;`giveApplied` 双计修复 + `rosterAudit.giveFoldHits`;UI-DIAG `unattrRow`;`check_live_log.py`);备份 `.1.7.9.bak`(1.7.9 现网 DLL)/ `.1.7.7.bak` / `.1.7.6.bak` / `.1.7.5.bak` / `.1.7.4.bak` / `.1.7.3.bak` / `.1.7.2.bak` / `.1.7.0.bak` |
| DLL | `BepInEx\plugins\DpsMeter\DpsMeter.dll`(**438,272 字节**,1.7.15,`68E10640…`;上一版 1.7.14 = `C9D1B0CC…`(435,712 B),备份于 `_dpsm_work\deploy-backup\pre-r63-C9D1B0CC\`;上一版 1.7.13 = `32BFEC3B…`(435,200 B)在 `pre-r62-32BFEC3B\`;更早 1.7.12 = `F791F1CF…`(434,688 B)在 `pre-r61-F791F1CF\`;1.7.11 = `C1DBBD8F…`);回退链 `.1.7.10.bak`(`BF2F174A…`)/ `.1.7.9.bak`(`F3F73C81…`)/ `.1.7.8.bak`(`0B339836…`)/ `.1.7.7.bak`(`7425139C…`)/ `.1.7.6.bak`(`BB96DA65…`)/ `.1.7.5.bak` / `.1.7.4.bak` / `.1.7.3.bak` / `.1.7.2.bak` / `.1.7.0.bak` / `.1.6.1.bak` / `.1.6.0.bak` / `.1.5.5-verified.bak` |
| SHA256 | 线上 = `68E10640CC920B93FE3DE6AEF38460EE6BC6B9BE31C60694DBB32611DEC1E69A`(1.7.15,R63);上一版 = `C9D1B0CC9FAF124D5ACF4194B49185C13DC73AFF275E15152F59463AE32FCC5D`(1.7.14,R62);上一版 = `32BFEC3B6A8884C57997A1BBC36FE5456509278B465A6784A810038FD4446007`(1.7.13,R61);上一版 = `F791F1CFD0E5A1963EB7669505C3D9CE8D827AD9F35D6F82B8E909A2B0DB7E7`(1.7.12,R60);上一版 = `C1DBBD8FF43F0E5A983CB17BD6D8F02FB906B3EB874481E437E1B3C594CC49BC`(1.7.11,R57);历史:1.7.11 基线 = `36EC96D4DBD8E221ED554476C299BD8DB4C9A1220A2A923DB16BC7BB4888BC42`(387,072 B;1.7.10 = `BF2F174A4059125376946ABCB0E3E4A9176E6B360CAB209078F219A95389AE85`;1.7.9 = `F3F73C81FF3D9CE0B903E5B4BB9C3F60062BF20F93118A853DBB9DCC34E7DBCB`;1.7.8 = `0B33983646280E246AA9E5911AEAB0EC499FC22C6C796460299BD4BDC4B34880`,1.7.7 = `7425139C…`) |
| 源码 | `_dpsm_work\src\DpsMeter.csproj`(**95 个 .cs / 25,526 行**,排除 obj;RF2 拆 `Aggregator` 为 6 个 partial,RF3 起新增 `src\Policy\` 纯策略文件(R63 增 `SkillCooldownPolicy.cs`),RF4 新增 `src\Runtime\` 状态容器;守卫口径 **131**(含 `tests/`);最大的几个文件见 PROJECT-STATUS §5) |
| 回滚档案 | `DpsMeter.dll.1.4.1.bak` / `1.5.0–1.5.4-verified.bak` 等(**102 个 .bak**(其中 `DpsMeter.dll.*.bak` 34 个,实测 2026-10-04));⚠ `1.0.48/1.0.49-crash.bak` **绝不回滚** |
| 开关 | `BepInEx\config\dev.dpsmeter.cfg`(DamageComposition/Forensics/ReconcileCalc/GivenTalent/StatusResist/Madness/StateTimeline/FactStore/MadnessApplier/GivenGiverHook/**Contribution/ShowContribution** 全 true) |
| 导出 | **冻结快照 35 份**(`_dpsm_work\batch-inputs-rf0.json`;`exports\` 本身是活的):411001×25 / 试炼场 9999×9 / 700817×1;版本 1.5.3→1.7.11;其中 **22 份含 `contribution` 段**(schema 1.1 ×9 = 1.7.8×3 + 1.7.10×1 + 1.7.11×5;schema 1.0 ×13),13 份无段 = 1.5.3–1.5.5,1 份 1.6.0 不可复算 |
| 运行时日志 | `BepInEx\config\dpsmeter_runtime.log`(每次启动被删,只留最新一场) |
| 验收 | `python n0_acceptance.py` → **44 条命令 / 76 条检查**(默认读冻结快照、写 `--out`);RF2 起的各轮终轮均 **0 项**;RF0–RF2 基线轮 59 ok / 4 项(3 项已修 + 1 项是本轮工具自身产物,见 PROJECT-STATUS §12);白名单允许 `crosscheck --batch`(=1,1.6.0 已知坏样本) |
| 契约 | `log-share/1` / `ComparisonEligibility/1` / `compare/2` / `decision/1` / `budget-census/1` / 身份映射;入口与退出码见 PROJECT-STATUS §3 |

## 3. 已验证的能力(全部实机)

* **伤害模型**:`exact` 83–85%、`exactWithCrit` ~98%、`unexplained` 2–2.9%;`v150_validate` problems=0。
* **每击事实(FACT)**:签名覆盖率 100%,类 271–431/2400,溢出 0。
* **逐规则溯源**:每击 `calc.fold` / `calc.cancel` 带稳定 origin,五通道 text/talent/global/given/madness。
* **归因 byUnit(1.5.4→1.5.5,域内 100%)**:规则来源角色可点名 —— 狂気(メアリー 自施加 ×2.5、
  ルゥ=ルルサ 给敌方 ×1.5)、赋予(マッドシーカー 的刻印授予 +10%)、全局规则(母なる変異の飛沫、海魔の残滓)。
* **队伍贡献表**:每角色 自伤 / 自身规则 / 受队友赋能 / 为团队赋能(对数份额口径),**域内 creditedShare 100%(未归因池 0.0%)**;**整场覆盖率是 `overallAttributedCoverage`,并非 100%**。
* **时间线**:受击方 18 抗性槽 + 10 状态位,只在变化时出行。
* **离线验证**:`recon_probe` ALL CHECKS PASSED;守卫是 **44 条命令 / 76 条检查的验收流水线**(`n0_acceptance.py`,含冻结输入 / 输出隔离 / 桶分布 / 逐文件钉住 / 每条闸门的退出码),另有 **987 用例的 C# 行为测试**(时钟/窗口/会话/序列/缓存/策略/状态机/战场规则分类/规则注册表/缓存判据/规则算术/单场计数/活动环/文字排版与数字格式/列定义/数据行构造/回退渲染器文本/主数据选名/composition 容差)与 **144 例变异负控**;完整清单见 `_dpsm_work\PROJECT-STATUS.md` §4。
* **阶段 E(1.6.0 起,1.6.1 实机验收通过)**:导出新增 `contribution` 段 —— 每角色 基础/自身规则/辅助/总贡献、
  规则当量、提供者→受益者关系,全部由逐击折叠导出现算。**验收 = 与独立 Python 核心逐字段一致**
  (`contrib.crosscheck` status=OK / mismatches=0,11 角色/21 规则/20 关系);schema 与 v150 problems=0。
  1.6.0 上线时抓到并修掉一个可复算性缺陷(导出 factor 截断 → 1.6.1 改往返精度,§7.2.87)。
* **阶段 F(1.7.0 看板 / 1.7.1 表格页)**:悬浮窗新增**总贡献看板**;**F5 打开独立表格页**(角色贡献 / 规则当量 / 辅助关系 三张表,详见 §7.2.90)。每角色 总贡献/占比/直接输出占比 +
  基础/自身规则/辅助、合计/未归因、规则当量 top3;与导出**同一条计算路径**(先 `Compute` 出结构化结果,
  再分别序列化/渲染),**每秒最多重算一次**;不可用时显示原因而不是 0。角色名截断,长名不撑宽面板。
  uGUI 与 IMGUI 两种渲染器都实现。**待目视验收**。
* **阶段 G 第一/二步(1.7.2→1.7.4,已部署;owner 语义 = 给予者;加算已按 `atkadd` 记账,§7.2.93)**:新增**只读**通道 `paramOwners` —— 逐条记录应用参数(攻击力加算等)
  **是谁给的**(`ParamData.Owner` / `BuffParam.m_owner` 两侧都记 + 不一致计数),按单位记**并集**(过期不抹除),
  每读 64 条 / 每场 20000 条双预算并计数。**不改任何 KPI**。起因:用户纠正「ルナリス 的所有技能都给队友大量攻击力」——
  母表实测证实(エンチャンター = range **29** FriendTeamAllExcludeToken、攻击力+300;神器 range 3 +7%/+10%),
  并**推翻**了先前「日志里没有友军给攻」的判断(错在扫文本,而范围字段是 `range`,导出从未携带)。详见 §7.2.91。
  第二步归属公式的形状与前提(`P = B*(1+r)+a` 为**推断**)已写入字典 §11;**量级已按实测更新**:1.7.4 场 ルナリス 辅助 51,230,121 = 25.1% analyzable,1.7.6 场 51,781,995 = 27.2%(旧的 5.7%–6.6% 是废口径)。
* **离线贡献核心(阶段 A/B/C,2026-10-03,未改插件)**:`_dpsm_work\contrib\`(method=log-share/1)。
  1.5.5 场逐击/角色/份额三条恒等式全过,折叠记账 28369=28369+0,**域内未归因 0.0%**,攻击者主键覆盖 98.80%,
  总贡献可加和(基础 46.9M + 自身规则 91.7M + 辅助 65.3M = 203.87M = 分析池);S1–S12 单测全过。
  跨场:1.5.3 归因 73.86% → 1.5.4 85.26% → 1.5.5 **100.00%(域内 creditedShare)**。

## 4. 关键数字

**当前结论(可引用,均为最近一次运行;复现命令见 §5 与 PROJECT-STATUS §4)**

* 配队:**有倾向但不确定 → 不换人**。阵容 A n=16 / 阵容 B n=7;点估计 B−A = **−4.67%**,95% 百分位簇自助区间
  **[−14.67M, −4.74M](不含 0)**,但**未达到预注册的 10% 实际改善阈值**,且两组各自组内换过装、插件版本跨度不同。
  预注册文件 `decision_prereg.json` 两轮之间一字未改。详 `_dpsm_work\DECISION-REPORT-411001.md`。
* 数据完整性:**丢失折叠步 0/32 场、0 伤害**;有溢出计数的 31/32、读取失败 8/32、未知身份 16/32 —— 只影响**记录完整性**,
  不影响逐击倍率与总量。详 `_dpsm_work\BUDGET-CENSUS.md`。
* 语料与判定:32 份(411001×23 / 训练场 9999×8 / 700817×1,9999 一律 `not_comparable`);
  适用性 full 14 / partial 9 / not_comparable 9;布局守卫 32 份 **634 行 0 违规**;
  当前 1.7.11 的按角色贡献恒等式 `base+self+received == directDamage` 全部成立。详 `_dpsm_work\acceptance_1.7.11\corpus_manifest.json`。

**以下为 1.5.5 口径的历史数字(保留作对照,不要当现状)**

* 队伍搭配(同任务 411001 波次战、119s、同样 Lose,生存情况一致):
  * A 组(ルゥ=ルルサ)**5 场**:181.0 / 189.3 / 196.5 / 197.1 / 198.5M(均值 192.4)
  * B 组(メルティエル+火砲)**5 场**:**197.1 / 197.7 / 204.0 / 206.9 / 209.3M**(均值 203.0)
  * ⚠ **结论已修正(§7.2.87)**:A 组 5 场 181.0–198.5M(均值 192.4),**区间重叠**、组内波动 5.8–8.8%,
    与组间差异同量级 → **不能断定 B 更优**(需每组 6–8 场或控制变量)
  * 机制(**仍成立**):メルティエル 的 海魔の残滓(敌方全体 +15% それぞれ)覆盖全场(B 组 4/4 场,
    当量合计 106.9M);ルゥ=ルルサ 的 狂気 ×1.5 只覆盖 51–60%(A 组 4/5 场,87.7M)——
    覆盖率的优势是**机制层面**的,但它带来的净收益被单场波动盖住,总量上分不出来。
* 团队倍率池占总伤害 **74–77%**;伤害当量:母なる変異の飛沫 37.6–40.1M、海魔の残滓 26.4–29.2M、
  狂気 ×2.5(メアリー 自施加)20.5M、赋予 11.6M、狂気 ×1.5(ルゥ=ルルサ 给敌方)21.6M。

## 5. 怎么跑(照抄)

```
构建   & 'D:\dmmplayer\dotnet-sdk6\dotnet.exe' build 'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\src\DpsMeter.csproj' -c Release -v minimal
探针   & 'D:\dmmplayer\dotnet-sdk6\dotnet.exe' run --project '...\_dpsm_work\recon_probe\ReconProbe.csproj' -c Release -v quiet -- '...\_dpsm_work\recon_probe\out.json'
守卫   python check_export_schema.py / v150_validate.py / check_fact_signature.py / check_docs_123.py / refactor_final_check.py
       (在 _dpsm_work 下运行;前三个默认取 exports 里最新的一份,必须 problems=0)
分析   python dpsmeter_analyze.py(离线精细分析)、python dpsmeter_contrib.py(旧口径队伍贡献 → _dpsm_work\contrib_report.txt)
贡献   (在 _dpsm_work 下)python -m contrib.tests.test_samples(离线单测,不需导出)
       python -m contrib.run(单场 → contrib\reports\report_<导出名>.txt / .json / .legacy.txt;
                            试炼场 quest 9999 默认拒绝,须显式 --allow-training 且结果不参与比较)
       python -m contrib.compare(跨场,按 任务/版本/队伍 分组 → contrib\reports\compare_<quest>.txt)
       python -m contrib.crosscheck(阶段E硬验收:插件导出的 contribution 段 ↔ 离线核心逐字段比对;--selftest 自测)
       python -m contrib.legacy_diff(回归证据:新核心 ↔ 旧 Stage-0 表逐列对照,裁决 MATCHES-LEGACY)
       python -m contrib.tests.test_golden_155(把已公布的 1.5.5 数字钉成回归测试;导出缺失时 SKIP)
       python -m contrib.rule115_census(8 场 1.15 生效次数普查:98.3% 对齐,多折 481 vs 少折 220)
验收   python n0_acceptance.py(全量:44 条命令 / 76 条检查;默认读冻结快照、写 `--out`)/ python n0_acceptance.py --selftest(负控用例见工具注册表)
批次   python batch_snapshot.py --name rf0 --verify(冻结输入)/ python repo_manifest.py --verify --exports batch_inputs\rf0(基线清单)
测试   python tests\negative_control.py(130 例变异负控)/ python tests\il_equiv.py <pre> <post> <report>(IL 等价)/ python tests\rf2_split.py(RF2 拆分器)
       python check_doc_convergence.py(--selftest)/ python check_docs_123.py(--selftest)/ python check_live_log.py --log ..\BepInEx\LogOutput.log
       python check_export_schema.py / python check_contribution_layout.py / python refactor_final_check.py / python v150_validate.py
契约   python comparison_eligibility.py --applicability acceptance_1.7.11\applicability.json --json acceptance_1.7.11\eligibility.json
       python -m contrib.compare --applicability acceptance_1.7.11\applicability.json --out contrib\reports\compare2_411001
       python decision_report.py --compare contrib\reports\compare2_411001.json
       python budget_census.py / python identity_map.py --selftest / python sample_intake.py
悬浮窗  F5=总贡献表格页  F6=伤害明细  F8=显隐  F9=重置  F10=图表  F11/F12=图表/明细翻页
       python -m contrib.phase_e_dryrun [导出] [--section <段.json>](阶段E 干跑:过两道闸门;--section 可验任意实现)
CsRef  & 'D:\dmmplayer\dotnet-sdk6\dotnet.exe' run --project '...\_dpsm_work\contrib_cs\ContribCs.csproj' -c Release -- <导出> <段.json>
Python C:\Users\24134\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe(无 matplotlib)
反编译 & 'D:\dmmplayer\dotnet-tools\ilspycmd.exe' -t <类型> 'BepInEx\interop\Assembly-CSharp.dll'(interop 是纯桩,只有签名)
```

## 6. 现状与待办

**已达成**:伤害模型 / 每击事实 / 逐规则溯源 / 贡献表**域内 creditedShare 100%**(整场覆盖率为 `overallAttributedCoverage`);
阶段 A–G 全部落地并部署(1.6.0 起导出 `contribution` 段,1.7.0–1.7.11 为 UI 与加算归因迭代)。**逐版明细见 `SESSION-STATE.md` §7.2.x。**

**主线状态(路线图 N0–N7)**:N0–N6 全部 ✅;N7 🟡(性能部分实测;目视 9 项里 1 项已由实机日志自动核对通过、4 项部分证实)。
逐任务交付物与复现命令见 `_dpsm_work\CONTRIBUTION-NEXT-PHASE-ROADMAP.md` §0;完整事实表见 `_dpsm_work\PROJECT-STATUS.md`。

**待办(按“谁能推进”分层)**:
1. **只有用户能做(目视 5 项)**:长名对齐 / F9 重置 / 回看更早那一场 / 低覆盖提示 / 非 CJK 机器字体回退。
   清单与已有的日志证据见 `_dpsm_work\N7-PERF-AND-VISUAL-CHECKLIST.md` §5/§7;**不要为这些专门开战**。
2. **证据不足,当前明确不做**:插件侧耗时/分配插桩(三个计数器)。实测 **0/32 场**丢步 ⇒ 没有证据支持改实时路径。
3. **需要用户拍板的产品决定**:队伍对比页(UI 变更会升版本);`totals.dealt` 是否改成“吸收前”口径(会动悬浮窗/DPS/曲线/全部历史对比)。
4. **历史遗留,重构前需逐条复核**:`_dpsm_work\ARCHITECTURE.md` §7 的债务清单写于 1.3–1.5 时代。本轮已核实两条:
   `source` 通道**已经接线**(第 6 轮新场 5,628 条事件里取值已分化),而 `crit` **仍恒 false**(会心只能靠 `critDamageRate` 推断)。
   其余(跨单位「被ダメージ+X%」读取路径、全局 +15% 重复登记、会话被波切碎、`dealt` 口径)未逐条复核。
5. **跨场身份**:导出仍无持久角色 id;`identity_map.py` 给出 `entityKey`(模板 + 装备指纹)与身份强度,样本 <2 的组不给结论。

## 7. 硬约束(用户定的,别违反)

1. **不要为了验证一个假设就让用户再打一场** —— 能离线判的必须离线判完;探针一次上齐。
2. 每个数字都要能在磁盘上复算;文档标注**实测 / 离线重放 / 推断**。
3. 改完立刻跑验收:`python n0_acceptance.py`(44 条命令 / 76 条检查,且工具哈希零漂移);只改文档至少跑
   `check_doc_convergence.py` + `check_docs_123.py`;改插件还要构建 + `recon_probe`。
4. 文件交换用编辑/写文件工具,**不要用 PowerShell 整文件读写含中文的文件**(控制台是 GBK)。
5. **两个 crash.bak 绝不回滚/删除**;`.bak` 是回滚档案不是文档。可再生的 artifact(反编译 dumps、`__pycache__`)用完即删。
6. **历史文件与历史报告正文不改写**:要修正现状就补“当前状态句”并标注日期,不动历史叙述(范例:`CONTRIBUTION-TABLE-REPORT.md` 只改了语料总数与布局行数两处当前状态句)。
7. **已知坏样本不修不删不放宽**:`battle_411001_20261004_015919.json` 的 ERROR 是预期结果,`crosscheck --batch` 因此非零退出也已登记进验收白名单。

## 8. 细节指针(按需查,别重复劳动)

| 文件 | 是什么 |
|---|---|
| `_dpsm_work\PROJECT-STATUS.md` | **现状快照 + 重构地图**:事实表 / 契约清单 / 可复现入口 / 代码与工具地图 / 护栏矩阵 / 重构批次与禁区 / 文档年龄表 |
| `_dpsm_work\CONTRIBUTION-NEXT-PHASE-ROADMAP.md` | **下一阶段路线图 N0–N7** + 逐任务进度块(以 §0 进度表为准);§8 = 阶段完成定义 |
| `_dpsm_work\RELEASE-1.7.11-ACCEPTANCE.md` | **1.7.11 验收记录**:§0–§9 = 30 份时;§10 = 32 份第 6 轮复验(两场新样本的运行前预测;当时的检查数见该文件) |
| `_dpsm_work\DECISION-REPORT-411001.md` | **配队决策报告**(预注册 + 五节结构 + 与 compare/2 的逐角色交叉校验) |
| `_dpsm_work\BUDGET-CENSUS.md` | **数据预算普查**:丢步 / 溢出 / 读取失败 / 未知身份 / 残差分层 + 问题卡 |
| `_dpsm_work\N7-PERF-AND-VISUAL-CHECKLIST.md` | **性能与目视记录**:§1–§4 离线耗时与体量;§5 九项目视清单;§7 第 6 轮实机日志证据 |
| `_dpsm_work\comparison_eligibility.py` / `decision_report.py` / `budget_census.py` / `identity_map.py` / `sample_intake.py` | 契约与体检工具:准入 / 决策 / 普查 / 身份 / 新样本只读体检(各有 `--selftest`) |
| `_dpsm_work\IDENTITY-CENSUS.md` · `IDENTITY-METADATA-DESIGN.md` · `ATKADD-MODEL-AUDIT.md` · `atkadd_sensitivity_result.md` | 30 份时的身份与 atkadd 研究(**当时快照**,结论可用、计数不可当现状) |
| `_dpsm_work\acceptance_1.7.11\` | 验收结果目录:`corpus_manifest.json`(逐份 SHA256 + 全部检查)/ `runs.json` / `RESULTS.md` / `eligibility.json` / `applicability.json` |
| `_dpsm_work\HANDOFF.md`(482 行) | 交接书:**§9c = 现状/待办/目标**;§2 环境与编码坑;§3 部署表;§9 历史验收账;§10 细节索引 |
| `_dpsm_work\ARCHITECTURE.md`(906 行) | 架构:模块地图(65 .cs)、一次命中的数据流、**44 条不可破坏约定**、§7 技术债 |
| `_dpsm_work\SESSION-STATE.md` | **主档**:§1–§6 API/限制;§7.2.x 逐版决策(**最新 §7.2.100** = 1.7.11:F5 输出分组 `自身 \| 他人因你 \| 被队友分走` + 两条逐角色恒等式 + 版面守卫双向化 + `check_live_log` 两处误判修复;§7.2.99 = 1.7.10:闸门伪运行修复 + `[COMP]` 重算开关对齐;§7.2.98 = 1.7.9:开关下传 + `givenFoldOn` + `giveApplied` 双计修复 + `check_live_log.py`);§7.3 IL2CPP 坑 |
| `_dpsm_work\CONTRIBUTION-DATA-DICTIONARY.md`(650 行) | **贡献指标字典**:口径/公式/归属阶梯/理由码/JSON 草案/S1–S13 样例/§10 验收数字/**§12 schema 1.1 数学字段与覆盖率(P0-B 契约)** |
| `_dpsm_work\PROJECT-DIRECTION-AND-IMPLEMENTATION-PLAN.md`(410 行) | **总方向 + A–G 分阶段计划 + 智能体分工**(顶部有进度行);下一步主线以它为准 |
| `_dpsm_work\contrib\` | 离线核心(只读导出):14 个模块 + `tests/`(S1–S13 + golden 1.5.5 回归) |
| `_dpsm_work\contrib_cs\` | 阶段 E 的 **C# 参考实现**(在 src 之外,不碰插件):与 Python 核心逐项一致,过两道闸门 |
| `_dpsm_work\ROLLBACK-1.6.0.md` | **回退预案**:1.5.5 锚点(DLL 备份 + 源码快照 zip,含哈希)、回退步骤与成功判据 |
| `_dpsm_work\ROLLBACK-1.7.7.md` | **回退预案**(1.7.6 锚点 `BB96DA65…`、回退步骤、成功/失败判据) |
| `_dpsm_work\CONTRIBUTION-TABLE-REPORT.md` | **贡献表完整报告**(产出面/字段字典/恒等式/版本兼容矩阵/6 份独立审查发现);数据分析从它开始 |
| `_dpsm_work\check_contribution_layout.py` | **表格布局守卫**:按显示列重放渲染算术,**并解析 `OverlayUGUI.Rows.cs` 的列标签/宽度与副本逐项对账**(1.7.11 起);`--selftest` 用 1.7.6 语义 + 篡改渲染器宽度反证自身有牙。**注意**:宽度/截断/数字格式算法自 RF5b 起在 `Ui/DisplayFormat.cs`,由行为套件直接执行;本守卫只对账**列宽** |
| `_dpsm_work\contribution_gate.py` | **验证闸门状态机(P0-A)**:PASS=0/WARNING=0/LEGACY=0/DATA_MISSING=3/ERROR=1,优先级 ERROR>DATA_MISSING>LEGACY>WARNING>PASS |
| `_dpsm_work\contribution_applicability.py` | **任务适用性判定(P0-D)**:32 份时 → full 14 / partial 9 / not_comparable 9(29 份时为 12 / 9 / 8);`--selftest` 18 例 |
| `_dpsm_work\pairtrusted_impact.py` | **配对可信度影响(P0-C)**:A/B/C 三口径;结论 A≡B、C 不可用作信任闸门;`--selftest` 11 例 |
| `_dpsm_work\check_p2a_summary_and_lastbattle.py` | **UI 契约守卫(P2-A)**:上一场未归属透传 + 上一场指向哪一场 |
| `_dpsm_work\ROLLBACK-1.7.8.md` | **回退预案**(已作废;1.7.7 锚点 `7425139C…`) |
| `_dpsm_work\ROLLBACK-1.7.9.md` | **回退预案**(已作废;1.7.8 锚点 `0B339836…`) |
| `_dpsm_work\ROLLBACK-1.7.10.md` | **回退预案**(已作废;1.7.9 锚点 `F3F73C81…`) |
| `_dpsm_work\ROLLBACK-1.7.11.md` | **当前回退预案**(1.7.10 锚点 `BF2F174A…`,387,072B / `36EC96D4…`;显示级发布,回退不需回退守卫) |
| `_dpsm_work\REVIEW-*.txt` | 子智能体预审报告(贡献核心审计 / 阶段 E 设计 / 阶段 G 侦察) |
| `_dpsm_work\contrib_verify_155.txt` | 贡献核心的原始验收输出(三条命令 + 退出码) |
| `_dpsm_work\contrib_gap_dealt.py/.txt` | "totals.dealt 口径差"的复现脚本与 4 份导出对照 |
| `DpsMeter-使用说明-完整版.txt` | 面向玩家的功能说明 |
| `_dpsm_work\ARCH-REVIEW-1.5.md` | 1.5.0 架构审视(路线 A/B/C 来源) |
| `_dpsm_work\REPORT-两种搭配对比-20261003.txt` | 两种人员搭配对比(结论 + 机制 + 口径变化) |
| `_dpsm_work\REPORT-换人后总伤害为什么变低-20261003.txt` | 早期面向用户的结案报告 |
| `_dpsm_work\evidence_*` | 复算脚本与输出(1.15² / 1.5.2 实机 / 1.5.3 实机 / 贡献联结 / 1.5.5 验收) |
| `_dpsm_work\export_archive_20261003.zip` | 3 份证据战场(旧导出清空时留存) |
| `_dpsm_work\doc_archive_20261003.zip` | 8 份逐版说明(1.4.0–1.5.5)归档 |
| `_dpsm_work\tests\BehaviorTests\` | RF1 规范化行为测试(987 用例 / 101 组;`--quiet` 出汇总行、`pinned` 防丢用例) |
| `_dpsm_work\tests\`(IlDump / il_equiv.py / negative_control.py / rf2_split.py) | RF2 等价证据与拆分器、RF1 变异负控 |
| [`REPO-BOUNDARY.md`](<REPO-BOUNDARY.md>) · [`_dpsm_work\baseline-manifest.json`](<_dpsm_work/baseline-manifest.json>) · [`_dpsm_work\batch-inputs-rf0.json`](<_dpsm_work/batch-inputs-rf0.json>) | 仓库边界 / 基线清单(源码·工具·配置·语料·外部程序集)/ 本批冻结输入清单 |
| [`_dpsm_work\REFACTOR-BATCH-RF0-RF2.md`](<_dpsm_work/REFACTOR-BATCH-RF0-RF2.md>) | **重构第 1 轮记录**:RF0–RF2 的文件清单 / 证据 / 未覆盖项 / 回滚 |
| [`_dpsm_work\REFACTOR-BATCH-RF3.md`](<_dpsm_work/REFACTOR-BATCH-RF3.md>) | **重构第 2 轮记录**:RF3 纯判据下沉(抽了什么 / 四处去重 / 行为保持手段 / 未做) |
| [`_dpsm_work\STATE-LIFETIME-MATRIX.md`](<_dpsm_work/STATE-LIFETIME-MATRIX.md>) · [`_dpsm_work\REFACTOR-BATCH-RF4.md`](<_dpsm_work/REFACTOR-BATCH-RF4.md>) · [`_dpsm_work\REFACTOR-BATCH-RF4B.md`](<_dpsm_work/REFACTOR-BATCH-RF4B.md>) · [`_dpsm_work\REFACTOR-BATCH-RF4C.md`](<_dpsm_work/REFACTOR-BATCH-RF4C.md>) · [`_dpsm_work\CACHE-SEMANTICS-ADR.md`](<_dpsm_work/CACHE-SEMANTICS-ADR.md>) · [`_dpsm_work\REFACTOR-BATCH-RF5A.md`](<_dpsm_work/REFACTOR-BATCH-RF5A.md>) · [`_dpsm_work\REFACTOR-BATCH-RF4D.md`](<_dpsm_work/REFACTOR-BATCH-RF4D.md>) · [`_dpsm_work\REFACTOR-BATCH-RF4E.md`](<_dpsm_work/REFACTOR-BATCH-RF4E.md>) | **状态生命周期矩阵** + 第 3–9 轮记录、**缓存语义 ADR**(三个待决问题)、**应用侧算术**、**单场计数与重置转换** |
| `_dpsm_work\recon_probe\` | 离线断言工程(改对账/Fold/JSON/StatusKey 语义时必须加断言) |
| `_dpsm_work\check_*.py` / `refactor_final_check.py` | 五个守卫(编码/导出 schema/FACT 签名/源码结构/v150 通道) |

## 9. 清理记录(两次,均为用户指令)

* **2026-10-03**:导出清空(773 份 / 527MB → 只留 3 份证据战场 zip);文档整理(0.9–1.3.10 共 84 份删除、
  333 个工作文件 + 41 个目录删除、守卫脚本随清理更新);记录见 SESSION-STATE §7.2.74。
* **2026-10-03 深夜**:根目录 8 份逐版说明(1.4.0–1.5.5)打包归档后删除;本索引升级为
  **唯一的当前状态索引**;记录见 §7.2.81。
* **2026-10-04(第 6 轮,纯文档 + 工具)**:新增 [`PROJECT-STATUS.md`](<_dpsm_work/PROJECT-STATUS.md>)(现状快照 + 重构地图);
  本索引改为「现状数字在 §4 顶部、历史数字标注口径」;`check_docs_123.py` 的文档集由 17 份扩到 **29 份**并新增 `--selftest`(6 例,含 U+FFFD / mojibake / 非 UTF-8 / 缺失文件四种红灯),
  同时进入验收流水线(命令数 +1;**当时的计数见当时的 PROJECT-STATUS**,本行不重复写具体数字,因为"命令数"是流动的)。**未删任何文件、未改插件。**
* **2026-10-04(重构第 1 轮 RF0–RF2,按 `REFACTOR-PLAN-POST-1.7.11.md`)**:建立**本地 git 基线**(`a2a09c2` / 标签 `baseline-1.7.11`,默认拒绝的 `.gitignore`)、**冻结批次输入快照** 35 份(hard-link,清单可提交)、**输出隔离**检查(历史档案必须一点不动);新增 **174 用例**的规范化行为测试 + 10 例变异负控;**`Aggregator` 拆成 6 个 partial**(IL 级等价);`check_docs_123.py` 文档集 29 → **32 份**,验收流水线**当时**的命令/检查数为 33/65(这两个数字是流动的,以当时的 PROJECT-STATUS 为准);顺手修掉活日志检查的取整误判与 `test_gate` 的 fixture 选择缺陷,并补 R9/R10/R11 让"文档数字与工具矛盾"会红。**未替换部署 DLL、未改任何历史证据。** 详见 [_dpsm_work\REFACTOR-BATCH-RF0-RF2.md](<_dpsm_work/REFACTOR-BATCH-RF0-RF2.md>)。
* **2026-10-03 深夜(阶段 A/B/C)**:新增贡献指标字典 + `contrib/` 离线核心(纯新增,未删任何文件;
  只删了自己刚生成的旧命名 `compare_all.*`);`check_docs_123.py` 的 FILES 追加 4 个新增 CJK 文件;
  记录见 §7.2.82。
> **部署状态(R63 起)**:DLL = **68E10640**…(**438,272 字节**,1.7.15;新增「自动技能」主表 `auto_skill` 的转储(20 张表 / 输出 21 个文件)+ 冷却单位纯规则 `SkillCooldownPolicy`(主表**秒** ↔ 线上**帧**,30 单位/游戏秒),同一行同时给 `*CoolTime` 与 `*CoolTimeFrames`;**不动任何既有数值与 credit**)。**当前语料 82 份**(411001/700817/试炼场 9999;9999 一律 `not_comparable`)。上一版 **C9D1B0CC**…(435,712 字节,1.7.14;命中记录错配改「可证伪即拒绝」+`hitMatch=3`/`hitDetail.matchRejected`、离线残差改游戏口径、全局敌方受伤因子移入 `takenMult`)备份于 `_dpsm_work/deploy-backup/pre-r63-C9D1B0CC/`;上一版 **32BFEC3B**…(435,200 字节,1.7.13;同队/自我伤害移出归属池、`schemaVersion` 1.2,离线核心同步双模式)备份于 `pre-r62-32BFEC3B/`;上一版 **F791F1CF**…(434,688 字节,1.7.12;自伤三件套 —— 根 `config.filterFriendlyFire`、`contribution.actors[].friendly/friendlyHits/hostileDamage`、F5 表 1「自伤」列,并修 `contrib/crosscheck.py` 对 schema 1.1 段 `totalDamage` 的 1.0 旧公式)备份于 `pre-r61-F791F1CF/`;上一版 **C1DBBD8F**…(433,664 字节,1.7.11)备份于 `_dpsm_work/deploy-backup/pre-r60-C1DBBD8F/`;上一版 **F6948470**…(433,664 字节)备份于 `_dpsm_work/deploy-backup/pre-r57-F6948470/`,更早在 `pre-r56-390C1340/`、`pre-r55-76CEAC00/`、`pre-r54-3A89D30A/`、`pre-r53-AA836C06/`、`pre-r52-28B8CCAF/`,基线 **36EC96D4**…(387,072 字节)在 `baseline-1.7.11/`,回退为一条 Copy-Item。**当前语料 103 份**(411001/700817/试炼场 9999;9999 一律 `not_comparable`)。
