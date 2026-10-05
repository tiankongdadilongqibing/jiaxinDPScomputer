# 重构批次记录 R56:战斗编号与精确选场(按 BATTLE-REFERENCE-IMPLEMENTATION-PLAN)

> 你的要求:按 `_dpsm_work/BATTLE-REFERENCE-IMPLEMENTATION-PLAN.md` 的设计增加**精确选场**功能,完成后输出 md 报告。
> 面向用户的报告见 [REPORT-精确选场-R56.md](<REPORT-精确选场-R56.md>)。
> 插件版本 **1.7.11**;本轮部署 **F6948470…**(433,664 字节)。

## 0. 结论

1. **每场一个稳定编号**:新建真实会话时**只分配一次**(唯一分配点 `Aggregator.StartSession`),
   格式 `B-{启动命名空间}-{序号≥3位}`;视图重建**复制**编号而不是重新发号,没有编号就明说 legacy。
2. **编号进入导出**:正文新增 `battleRef` 块(契约 `battle-ref/1`),默认文件名
   `battle_{quest}_{时间}__{编号}.json`(保留历史前缀与 quest 位置),写盘改为**临时文件 + 原子发布**,
   成功后计算**实际字节 SHA256** 并绑定会话。
3. **离线精确选场**:新增 `battle_select.py`(`list`/`resolve`/`compare`),把编号解析成**显式文件清单**,
   缺失/歧义/冲突/内容变化一律**拒绝**而不是少比一场;比较仍交给既有的 `contrib.compare`
   (不重写贡献算法)。
4. **编号只是元数据**:本轮**不移动任何伤害/贡献数字**,不改 `contribution` 的 schema 与算法。
5. 按方案 §194 的"第一版完成范围"交付;**运行时索引**与若干可选交互按方案要求留到后续(§4 逐条列出)。

## 1. 改动清单

| 位置 | 改动 |
|---|---|
| `src/Model/BattleRef.cs`(新) | 会话身份对象(id/launchId/sequence/resetCount/revision/state/closeReason/导出路径与哈希);`Clone()` 给视图用 |
| `src/Policy/BattleRefPolicy.cs`(新) | **纯契约**:格式/解析/序号补零(≥3 位不截断)/短序号/legacy 引用/`CloseState`(无结果的 end 只能 provisional)/同号同修订异哈希=SAME-ID 冲突/文件名/内容块校验/复制文本 |
| `src/Runtime/BattleRefRegistry.cs`(新) | 启动命名空间 + 序号分配(**时间与随机源注入**,可测):碰撞则**重新生成命名空间**并重排序号;`MarkResumed/MarkClosed/MarkReset/MarkExported` 是修订号唯一的移动点 |
| `src/Model/BattleSession.cs` | 新增 `Ref`;`FromSummary` **复制**(clone)身份;`ResetActors` 调 `MarkReset`(F9 保留 id、计数、动修订) |
| `src/Model/BattleSummary.cs` | 新增 `Ref`(指向同一身份,视图不新发号) |
| `src/Aggregator.Session.cs` | 唯一的分配点;启动日志打印 `battleId`;软恢复调 `MarkResumed` |
| `src/Aggregator.Finalize.cs` | 收尾先 `MarkClosed`(state/closeReason 与导出、摘要、界面同源);摘要携带同一 `Ref` |
| `src/Output/ExportService.cs` | `battleRef` 块;`BattleRefPolicy.FileName` 命名;`IdExists`(按文件名里的编号,单次扫描缓存);`WriteAtomic`;`FileSha256`;身份校验失败**不标记已导出**;默认路径**校验文件名尾号** |
| `src/Ui/OverlayUGUI.Rows.cs` | `AppendBattleRefRow`/`DisplayedRef`(页面显示哪场就取那场的身份);主窗、未战斗、F5、F6、Chart 五处接入;`RowDef.Copyable/CopyText`;`_pinLine` 追加短序号 |
| `src/Ui/OverlayUGUI.cs` | 点击复制(`GetAsyncKeyState` + 记录下来的矩形,不引入 EventSystem)+`CopyToClipboard`(失败**记录并保留手抄**) |
| `src/Ui/OverlayCore.cs` | IMGUI 回退:完整编号文字 + `复制引用` 按钮(与 uGUI 同一函数) |
| `battle_select.py`(新) | 离线 `list`/`resolve`/`compare` + selection.json + legacy + 严格拒绝 + `--selftest`(16 例) |
| `tests/BehaviorTests/Cases.BattleRef.cs`(新) | `bref/format`、`bref/registry`、`bref/lifecycle` 三个组共 **81 例** |
| `tests/BehaviorTests/Program.cs` | 注册新组;用例钉住值 866 → **947** |
| `tests/BehaviorTests/BehaviorTests.csproj` | 把三个新的纯文件加进 Compile 清单 |
| `tests/negative_control.py` | 新增 **9 例** R56 变异(补零/结束状态/序号 0/文件名/复制文本哈希/序号推进/碰撞重生成/软恢复修订/视图复用身份) |
| `n0_acceptance.py` | 新增 `battle_select`(对冻结批次跑 `list`)与 `selftest/battle_select` |
| `tool_registry.json` | 登记 `battle_select.py`(active,in_pipeline) |
| `check_docs_123.py` | 加入本轮两份 .md |

## 2. 验证

| 闸门 | 结果 |
|---|---|
| `dotnet build src/DpsMeter.csproj -c Release` | **0 警告 0 错误** |
| BehaviorTests | **947 用例 / 98 组 / 0 失败** |
| 变异负控(新增 9 例单独跑) | 9/9 **红在具名用例** |
| 变异负控(全量) | **130 例 / 0 失败** |
| `battle_select.py --selftest` | **16 例 / 0 失败** |
| 真实导出目录 | `list` 跑通:50 份 / 0 带 battleRef / 50 legacy |
| `n0_acceptance --out acceptance_r56` | **44 条命令 / 76 条检查 / 0 项**(本轮新增 `battle_select` + `selftest/battle_select` 两条命令) |
| 文档收敛 / docs123 / 工具注册表 / 清单 | 收敛 **12/12**;docs123 **92 文件 / 0 损坏**;工具注册表 **100 登记 / 31 活跃 / 67 已索引 / 2 被引用输入 / 0 未判定**(pipeline 25);清单 **drift=0** |

**两处"变异跑不红"的诚实处理**:R56 初稿的 `TryParse` 里有一段手写数字校验,**int.TryParse 已经覆盖**,
变异证明它到不了任何具名用例 —— 于是**删掉那段死代码**,并把变异改成 `value <= 0` 这条真正会咬的判据;
碰撞用例初稿拿"会自增的计数器"当期望值,变异前后都绿 —— 改成断言**探测次数**(正确路径问两次、忽略碰撞只问一次)。
两条都记在这里,因为"跑不到红就不是闸门"。

## 3. 部署与回退

- 替换前确认 DLL **未被占用**;备份上一版到 `_dpsm_work/deploy-backup/pre-r56-390C1340/DpsMeter.dll`。
- 部署:**390C1340**(423,424 B) → **F6948470**(433,664 B)。
- 复核两个**永不改动**的崩溃现场哈希不变:`2E1819F2…`(1.0.48-crash)/ `F7FF1EB8…`(1.0.49-crash)。
- 回退:`Copy-Item _dpsm_work/deploy-backup/pre-r56-390C1340/DpsMeter.dll BepInEx/plugins/DpsMeter/DpsMeter.dll -Force`。
  **回退插件后,新导出仍然带着编号**(那是文件内容),解析器读新旧两种格式。

## 4. 未做 / 注意

1. **运行时 `battle_catalog.json` 索引**未做:方案 §5.3 允许先交离线扫描;本轮 `list` 直接读目录与正文。
   代价是每次解析都要读文件头,resolve/compare 还要哈希被选中的文件。
2. **uGUI 没有真正的 Button**:不给游戏场景塞 EventSystem(会和游戏抢输入),点击用面板已有的命中测试方式;
   IMGUI 回退里是真正的按钮。
3. **导出侧(ExportService/Overlay)没有离线变异负控**:这两个文件按设计不在行为套件里,变异无法让套件变红;
   本轮**没有**为它们造假闸门。可执行的契约、分配器、视图复制三条都有具名用例与变异。
4. **证据包 manifest 未加 battleRef**:包内 `battle.json` 已带同一身份;加键会动 R52 冻结的 bundle 契约。
5. **F5/F6/Chart 的编号取自"正在展示的数据源"**(`DisplayedRef`),不回退到别场缓存;但**没有实机目视确认**
   ——需要你下一场顺带看一眼(见报告 §11)。
6. 训练场/准入规则**未被编号功能放宽**:编号只解决"定位",不解决"能不能比"。
