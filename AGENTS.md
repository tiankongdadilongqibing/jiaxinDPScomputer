# AGENTS.md — 在本仓库工作的规则

本文件是**给代理(与人类协作者)的操作手册**。它记录的不是"建议",而是**这个仓库已经付出代价换来的约束**:
下面每一条都对应过一次真实的失败或一次被守卫抓到的错误。**先读本文件,再改任何东西。**

## 0. 三条不可协商的约束

1. **绝不修改** BepInEx/plugins/DpsMeter/DpsMeter-1.0.48-crash.bak 与 DpsMeter-1.0.49-crash.bak
   (崩溃现场留存)。任何"整理/重命名/删除"都不要做。
2. **绝不要求用户为了验证而离线打一场**。需要实机数据时,是**请求**用户跑,然后读取产物;
   不能把"先跑一场"塞进自动化流程。
3. **绝不改写历史批次记录**(REFACTOR-BATCH-*.md)中的记述。它们描述的是**各自那一刻**的事实。
   只有 PROJECT-STATUS.md / HANDOFF.md 等"当前状态"文档才随改动更新。

## 1. 仓库边界

- 仓库根目录**就是游戏安装目录**;.gitignore 是 **deny-by-default**(只纳入被显式重新纳入的路径)。
  新增需要纳入的**根级文件**时,必须同时在 .gitignore 里加 include 规则,否则它不会进提交。
- 游戏资源、导出语料、bin/obj、下载缓存**永远不要**纳入。边界理由见 _dpsm_work/REPO-BOUNDARY.md。

## 2. 每一轮改动的工作循环

    1) 改代码/文档
    2) dotnet build _dpsm_work/src/DpsMeter.csproj -c Release        # 必须 0 警告 0 错误
    3) dotnet run --project _dpsm_work/tests/BehaviorTests/BehaviorTests.csproj -c Release -- --quiet
       (若用例数变化,先改 Program.cs 里的 ExpectedCases 钉住值,再跑)
    4) 为新行为加**变异负控**条目到 _dpsm_work/tests/negative_control.py,并单独跑它们,
       确认"变红的是具名用例"——**跑不到红就不是闸门**
    5) python _dpsm_work/tests/negative_control.py                     # 全量,0 失败
    6) 同步文档(见 §3),再跑守卫
    7) python _dpsm_work/n0_acceptance.py --out acceptance_<轮次>       # 必须 0 项
    8) 提交(git commit -F <消息文件>),消息里写清:改了什么、证据是什么、怎么回退

## 3. 文档同步的硬规则(守卫会红)

| 改了 | 必须同步 | 守卫 |
|---|---|---|
| 验收的命令数/检查数 | PROJECT-STATUS.md 与文档索引里的"44 条命令 / 76 条检查" | R9 |
| 部署的 DLL | 索引 / HANDOFF / 报告 / PROJECT-STATUS 里的部署哈希前缀(当前 C1DBBD8F) | R3 |
| 用例数与变异数 | PROJECT-STATUS.md 的计数行 | 人工+自测 |
| 新增守卫脚本(.py) | 必须在 _dpsm_work/tool_registry.json 登记(含 status/outputs/expected_exit),否则 B 检查红 | 工具注册表 |
| 新增 .md | 加进 _dpsm_work/check_docs_123.py 的 FILES 列表 | docs123 |
| 把某事项标为"未做" | 若产物已存在,R12 会红——**别说过期的话** | R12 |
| 清单/最大 N 文件列表 | 列表条数必须与声明的 N 一致 | R11 |

## 4. 编码与工具约束(踩过的坑)

- **控制台是 GBK**:所有工具 stdout 只输出 ASCII;**不要**在脚本里打印中文,否则输出乱码且可能抛错。
- **写源码只用定点编辑工具**(edit/精确替换 + replace_all),**不要**用 shell 的字符串重写整个文件。
  本仓库发生过两次:PowerShell 重写导致 167 个编译错误;长文件 read→write 往返丢掉 29 行。
- **临时文件**一律放 _dpsm_work/tests/_*.tmp(该规则已被忽略),不要污染顶层目录。
- **验收运行期间不要改 _dpsm_work 顶层文件**:output_isolation 会(且应当)报红。
- 编辑前先读文件;改完立刻构建——**编译器是最便宜的审查者**。
- 一次只改一件事;**先提交后读验证结果 = 红提交**(本仓库发生过三次,别再犯)。

## 5. 部署规则

替换运行中的 DLL 之前:**确认游戏未运行且文件未被占用 → 备份整个插件目录 → 记录前后 SHA256 →
替换后复核历史 .bak 哈希未变**。回退是一条 Copy-Item(见 README §4)。
**"运行中不替换被加载的 DLL"**:替换必须在游戏关闭时进行。

## 6. 目录与入口

- 源码:_dpsm_work/src/**;行为测试:_dpsm_work/tests/BehaviorTests/**;
  守卫与工具:_dpsm_work/*.py;**工程文档:_dpsm_work/*.md**。
- 文档入口:DpsMeter-文档索引.md(**仓库根**,导航)、PROJECT-STATUS.md(现状)、HANDOFF.md(交接)、
  REFACTOR-PLAN-POST-1.7.11.md(方案)、REFACTOR-BATCH-*.md(逐轮记录)、CACHE-SEMANTICS-ADR.md(缓存决策)。

## 7. 已知的两个数据口径(别把它们相加)

导出的"未归因伤害"有**两个不同口径**:

1. **贡献账残差**(contribution.unattributed):份额无法记到提供者的部分,原因是规则种类未识别
   (unknown_kind)等;
2. **会话口径**(totals.unattributedDamage / damageLedger.unknownAttacker*):攻击者对象无法识别的命中,
   以及队伍之外的命中,它们被**整体排除**在归属之外。

报告数字时**必须说明用的是哪一个口径**。

**另有一条同类陷阱:同队/自我伤害默认计入输出数**。`dealt` / `directDamage` / `总贡献` / `命中` /
`perSecDamage` 都含"攻击者与受击者同队"的伤害(`回復反転`、自伤、`attacker == victim` 的自我结算),
它们只由根 `actors[].friendly` + `friendlyHits` 单独计数。要「对敌输出」必须自己扣:优先用逐事件 `vicTeam`
(`FilterFriendlyFire` 开关**不写进导出**,不能只看文件判断)。定义、实测样例与扣法见
`_dpsm_work/CONTRIBUTION-DATA-DICTIONARY.md` §1.4。

## 8. 分析一场战斗数据的路径(标准四步)

分析数据**不是**"从 `exports\` 里挑一个最新文件就开始算"。按下面四步走;每步都有权威文档,不要自己造流程。

1. **选场(精确到文件)**:`python _dpsm_work/battle_select.py list|resolve|compare`。编号形如
   `B-{启动命名空间}-{序号}`,旧档按 `legacy:<sha256>` 引用;`resolve` 把编号变成**显式文件清单**,
   `compare` 只比清单里的文件。**`compare` 只接受终局**(`state=final`):训练场 9999 与未终局一律拒绝
   (退出码区分,别当工具坏了)。→ `_dpsm_work/REPORT-精确选场-R56.md` §7
2. **取证(让结论可复算)**:战斗结束时插件**默认**写一个自包含证据包
   (`BepInEx/plugins/DpsMeter/extract/extract_<时间>_q<任务>_<原因>/`:`battle.json` + `contrib_census.json`
   + `manifest.json`),用 `python _dpsm_work/extract_verify.py` 证明它没被改过、普查与独立重算一致。
   → `_dpsm_work/EXTRACTION-FLOW.md`
3. **复算(一手证据永远是导出文件)**:在 `_dpsm_work` 下跑
   `python -m contrib.crosscheck <导出>`(文件里的 `contribution` 段 ↔ 离线核心**逐字段**)与
   `python -m contrib.validate`(恒等式 I1–I10);要文本/JSON 报告用 `python -m contrib.run`。
   → `_dpsm_work/CONTRIBUTION-TABLE-REPORT.md`(§2 链路 / §3 字段字典 / §7 实测样例 / §8 复现命令)
4. **引用(写结论时)**:每个数字都要能回答"哪份文件、哪个字段";口径按
   `_dpsm_work/CONTRIBUTION-DATA-DICTIONARY.md` §1/§12 写明,**两个未归因口径永不相加**(§7)。

**文档当前性**:分析口径的权威是 `CONTRIBUTION-TABLE-REPORT.md` 与 `CONTRIBUTION-DATA-DICTIONARY.md`;
`PROJECT-STATUS.md` §10 的文档年龄表说明哪些只是历史快照——先看它,别把快照当现状。
