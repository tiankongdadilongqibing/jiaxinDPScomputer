# 重构批次记录 R54:「阻挡增伤」单独归类 + 授予者精确匹配 + 提取可见化

> 你的两点要求:①完全无法确认归属时,把「阻挡增伤」单独拎出来,别和别的未归因混在一起让我错看;
> ②为什么需要我按 F4,悬浮面板上没有任何关于这个的提示。

## 0. 结论

* **①已做**:given 通道(= 别人**授予**给受击方的被伤害修正)不再并进 `unknown_kind`,而是按**名册侧证据的强弱**
  分成三个专属理由码 —— `given_carrier_none` / `given_carrier_one` / `given_carrier_ambiguous`,**三者都仍然不归因**
  (码只说明"离答案有多近");贡献面板在未归因总额下**逐个理由列出金额与折叠数**。
* **①顺手修掉了比"没归因"更糟的那半**:授予钩子的表原来**只按目标名做键**,于是它答的是
  「谁最近给这个目标授予过**任意**东西」。第三场就是这么把 6,705,889.97 记给了一个**不能授予该规则**的单位
  (见 [REPORT-未识别规则种类-R52.md](<REPORT-未识别规则种类-R52.md>) §8)。现在**只有精确 (目标, type/param) 命中才给信用**;
  旧的"只按目标名"答案降级为**只计数**(`givenApplies.targetOnlyRejected`)。这条修改的方向是**撤销一个没有证据支持的归属**。
* **②已做**:F4 写进面板底栏(**未战斗中**与**战斗中**两处)+ IMGUI 回退 + 启动日志;并且已把你的
  `ExtractOnBattleEnd` 打开 —— **下一场结束会自动出证据包,不必再按键**(键仍保留为手动触发)。

## 1. 改动清单

| 文件 | 改动 | 为什么 |
|---|---|---|
| `src/Output/Contribution.cs` | given 分支产出三个专属理由码;候选索引复用名册的授予表;carrier 判定的触发条件从「理由 = unknown_kind」改为「无 byUnit 且 origin 是 given#」 | 旧的触发条件是**按理由字符串**写的,改名会把它甩掉(本轮就被测试抓到过一次);按 origin 判断才是它真正的语义 |
| `src/Diagnostics/GiveApplierProbe.cs` | 新增精确键表(`target|type/param`)+ `NewestGrantKey`(读**目标自己**的授予列表,POSTFIX 后条目已存在)+ `LastGiverExact` + `targetOnlyRejected/exactHits/exactMisses/grantKeyReads/grantKeyErrors` 计数;旧的 `LastGiver` 保留为**证据** | 只有记录**授予了什么**,才能回答"谁给了这条修正";每个读取单独 try/catch 并计数 |
| `src/Composition/CompositionProbe.Talents.cs` | 调用点改为 `LastGiverExact(name, wantType, v)` | 精确匹配才允许 `byUnit` |
| `src/Ui/FallbackText.cs` | 理由→中文标签表(单一来源)+ `UnattributedBreakdownLine` | 两个渲染器不能对同一个理由给出两种说法;`FallbackText` 在行为测试里编译,可离线验 |
| `src/Ui/OverlayUGUI.Rows.cs` | 未归因总额下逐个理由列行(贡献页 + 总表页各一处) | 你要的"别混在一起" |
| `src/Ui/OverlayCore.cs` / `OverlayUGUI.Rows.cs` / `src/Plugin.cs` | F4 提示(底栏 ×2 + IMGUI + 启动日志) | 你要的"面板上看不到" |
| `_dpsm_work/contrib/model.py` / `attribution.py` | 三个新码 + 名册授予索引(`by_grant`) | 离线核心是同一份契约的第二个实现,必须同步 |
| `_dpsm_work/attribution_census.py` | **词汇归一**(旧文件用旧词、重算用新词时按文件词汇比较并注明)+ 显示新的授予计数器 + `--selftest` 9 例 | 改名不是分歧;不归一的话 R52/R53 那两份历史导出会看起来"不一致" |
| `tests/BehaviorTests/Cases.Extraction.cs` 等 | +13 例(`extract/given-reasons`、`extract/reason-labels`),并把两处 `unknown_kind` 断言改成新码 | 见 §2 |
| `tests/negative_control.py` | +2 变异(把 one 并进 ambiguous、把标签文案换掉) | 见 §2 |
| `CONTRIBUTION-DATA-DICTIONARY.md` | §4 增 9a/9b/9c 三行与"为什么单独三个码"的说明 | 契约文档必须说出理由码的**含义**与"仍未归因"这件事 |

## 2. 验证

| 命令 | 结果 |
|---|---|
| 构建 | **0 警告 0 错误**(420,352 字节) |
| 行为套件 | **811 用例 / 0 失败**(+13;`ExpectedCases` 798 → 811) |
| 新增变异 | `given-branch-collapses-one-into-ambiguous`、`reason-label-swaps-the-one-candidate-text` → **都红在具名用例** |
| 全量变异负控 | **114 例 / 0 失败** |
| 离线普查 | `attribution_census.py --selftest` **9 例通过**(+2:R54 词汇);对 R53 那份旧导出仍 **MATCH**,输出注明"文件使用 R54 前的词汇" |
| 契约守卫 | `check_extract_contract.py` PASS |
| 验收 | `python n0_acceptance.py --out acceptance_r54`(cwd=`_dpsm_work`):**74 条检查 / 0 项**,档案里记录的部署 = `76CEAC00…`;文档收敛 12/12 |

## 3. 部署与回退

| 项 | 值 |
|---|---|
| 替换前 | `3A89D30A…`(417,792 字节) |
| 替换前状态 | 游戏未运行、文件未占用(**UNLOCKED**) |
| 备份 | `_dpsm_work/deploy-backup/pre-r54-3A89D30A/DpsMeter.dll` |
| 当前部署 | **`76CEAC00…`**(420,352 字节) |
| 配置 | `BepInEx\config\dev.dpsmeter.cfg` 改为 `ExtractOnBattleEnd = true`(SHA256 `47BC333E…`) |
| 复核 | 两个 crash `.bak` 哈希未变 |
| 回退 | `Copy-Item _dpsm_work/deploy-backup/pre-r54-3A89D30A/DpsMeter.dll BepInEx/plugins/DpsMeter/DpsMeter.dll -Force`;把 `ExtractOnBattleEnd` 改回 `false` 即可关掉自动出包 |

### 3b. 一次不可复现的工具崩溃(记下来,不掩盖)

第一次跑验收时 ¤pairtrusted(all)¤ 以 **3221225477(0xC0000005,访问冲突)** 退出,且它的转写里**一行输出都没有** ——
像是进程刚起来就死了。同一份输入、同一条命令在 r53 的验收里是 0,随后单独重跑(3 个文件 / 绝对路径)也是 0,
把整条验收重跑一遍也是 0。结论:这是**一次不可复现的环境崩溃**,不是本轮改动引起(那个工具本轮没被碰过,
而且它有自己的一份阶梯实现,不引用本轮的 ¤contrib/¤ 改动)。记在这里的原因是:验收里出现过的非零退出码
必须留下痕迹,否则下次有人看到 r54 之前的红记录会以为数据有问题。

## 4. 未做 / 注意

* **实机验证**:新构建已部署,自动出包也已打开 —— 下一场结束会自动产生**第一个真的证据包**;包出现后跑
  `extract_verify.py` 并把结果追加到本记录(这是整条流程唯一还没实机跑通的一环)。
* 历史文件里的错答**不会被追改**:R52/R53 那两份导出(以及第三场)记录的仍是当时那次运行的输出;
  新构建起不再产生这种归属。
* `given_carrier_one` 是"唯一候选但仍未确认" —— 它**不是**归因。要不要把唯一候选真正接进归属,仍是 §8.4 里那个
  会移动信用的决定,本轮不做。
