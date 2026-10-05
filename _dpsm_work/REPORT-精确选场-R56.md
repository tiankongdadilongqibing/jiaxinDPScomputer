# 精确选场功能报告(R56)

> 需求来源:[战斗编号、悬浮窗引用与 AI 精确选场实施规划](<BATTLE-REFERENCE-IMPLEMENTATION-PLAN.md>)。
> 插件版本 **1.7.11**;本轮部署 **F6948470…**(433,664 字节)。
> 证据等级:【实测】= 真实运行/文件输出;【静态】= 读源码;【推断】= 未直接观测。
>
> **后续修正(R57)**:你第一次实机复制回来的引用暴露了 `file:` 字段的一个缺陷(R56 把身份绑到了 battle-end
> 的证据副本上,而该目录会被保留策略轮转删除)。已改为**首次成功写盘绑定路径**,并把那次实机证据写进
> [REFACTOR-BATCH-R57.md](<REFACTOR-BATCH-R57.md>)。本报告以下文字描述的是 R56 当时的行为。

## 1. 一句话

每场战斗现在有一个**稳定、可复制、可审计的编号**;这个编号出现在悬浮窗、导出文件名和导出正文里,
离线工具 `battle_select.py` 能把它**精确解析成文件并只比较这些文件** —— 不再靠"最新文件""文件排序"或历史列表位置猜。

## 2. 你怎么用(三步)

1. **战斗中或刚打完**,主悬浮窗第一行显示短序号 `#003`,下一行显示完整编号 `战斗编号 B-…-003`;
   **点那一行**就把整段引用复制到剪贴板(复制内容见 §5)。
2. 打第二场,同样得到 `#004`。
3. 把两个编号发给 AI(或自己跑工具):

   ```
   python _dpsm_work\battle_select.py resolve --ids B-…-003 B-…-004 --out sel.json
   python _dpsm_work\battle_select.py compare --selection sel.json --out <报告目录>
   ```

   第一条会打印这两个编号**唯一对应的文件**、任务、时长、修订号与 SHA256;第二条**先复核哈希**,
   再把这些绝对路径交给既有的 `contrib.compare`(不重写任何贡献算法)。

## 3. 界面显示【实测:代码路径已接,实机目视待你顺带确认】

| 位置 | 内容 |
|---|---|
| 主悬浮窗(战斗中) | `本场 #003  战斗中 · 尚未导出   [点击此行复制引用]` + `战斗编号 B-…-003` |
| 主悬浮窗(未战斗) | 上一场的短序号/完整编号/状态(F10 图表页、F5 贡献页、F6 明细页同样各有一行) |
| F6 明细 | 钉在面板顶部的信息条追加 `· #003` |
| IMGUI 回退 | 同样两行文字 + 一个 `复制引用` 按钮(复制内容与 uGUI 完全同源) |

状态文字把**两件事分开写**:会话状态(`战斗中` / `暂存(未终局)` / `终局`)与写盘结果(`已导出` / `尚未导出`)。
F9/重置按钮会在编号后追加 `已重置×N`。

## 4. 编号契约(battle-ref/1)

```json
"battleRef": {
  "schemaVersion": "1",
  "id": "B-20261005-143012-7A2C91EF-003",
  "launchId": "20261005-143012-7A2C91EF",
  "sequence": 3, "resetCount": 0, "revision": 1,
  "state": "final", "closeReason": "end"
}
```

| 字段 | 含义 | 何时变 |
|---|---|---|
| `id` / `launchId` / `sequence` | 完整编号 / 本次启动命名空间 / 启动内序号 | **创建后不可变** |
| `resetCount` | 本场被 F9/重置 的次数 | 每次重置 |
| `revision` | 内容快照修订号 | 只由三个事件移动:**软恢复**、**重置**、**新快照导出**;渲染/UI 刷新不动它 |
| `state` | `live` / `provisional` / `final` | 收尾时:`end` **且**有胜负 = final,其余(idle/teardown/restart/无结果的 end)= provisional |
| `closeReason` | 实际的收尾原因 | 收尾时 |

**一个编号 = 一个 BattleSession 统计会话**,不是"一次关卡挑战":波次/竞技场把一个阶段切成多个会话时,
每段各有编号,原来的 `run.id/seq` 分组**保持不动**、也不得被编号功能顺手合并。

## 5. 复制出去的内容

```
DpsMeter battle-ref/1
battleId: B-20261005-143012-7A2C91EF-003
revision: 2
quest: 411001
state: final
resetCount: 0
file: battle_411001_20261005_143012__B-20261005-143012-7A2C91EF-003.json
sha256: <实际文件哈希>
```

战斗还没结束/还没导出时,`file` 行写 `(尚未导出,尚无可分析文件)`、`sha256` 行写 `(尚无可分析文件)` ——
**绝不编造 hash 或文件是否存在**(这是本功能存在的理由之一)。剪贴板写入失败时会打印一行警告,
编号本身仍然可手抄,不会静默失败。

## 6. 文件命名与写盘

    battle_{quest}_{开始时间yyyyMMdd_HHmmss}__{完整编号}.json

- `battle_{quest}_…` 前缀与任务号位置**保持历史形态**(既有的 glob 与按文件名取 quest 的离线工具不受影响);
- 同一秒开始的两场也不会互相覆盖(编号不同);
- 写盘改成**同目录临时文件 → 原子替换/发布**,失败时保留旧的完整文件并返回失败;
- 成功后才计算**实际字节 SHA256** 并写进会话身份("已导出"是写盘结果,不是提前乐观写入的字段)。

## 7. 离线选场:`battle_select.py`

```
python battle_select.py list    --exports <目录> [--json <out>]
python battle_select.py resolve --exports <目录> --ids <完整编号…>   --out selection.json
python battle_select.py resolve --exports <目录> --launch <启动编号> --seq 3 5 8 --out selection.json
python battle_select.py resolve --exports <目录> --legacy <sha256前缀…> --out selection.json
python battle_select.py compare --selection selection.json [--out <目录>] [--allow-unfinished]
python battle_select.py --selftest
```

**拒绝优先**的设计:

| 情况 | 结果 |
|---|---|
| 编号存在 | 精确保留你给的顺序;重复输入去重并提示(**不算两个样本**) |
| 编号不存在 | **整组停止**,退出码 2,不写部分选择 |
| 短序号没给 `--launch` / 同名多个文件 / 文件名与正文编号冲突 | 退出码 3,列候选,绝不替你选 |
| legacy 前缀在本目录不唯一 | 退出码 3 |
| 写出选择后文件内容变了 | `compare` 退出码 4,要求重新解析(**不默认跟随最新值**) |
| 样本不是 `final` | 默认退出码 5;诊断模式 `--allow-unfinished` 才放行 |
| 被重置过的场次 | 通过,但打印"该文件不含重置前的数据" |

`selection.json` 记录:用户原始请求、解析目录、**每个编号的实际绝对路径**、id/revision/SHA256、任务/时长/结果、
state/resetCount、选择顺序、错误与警告。**选择成功 ≠ 可比较**:任务/模式/装备/能力与训练场准入仍由既有准入逻辑判定。

## 8. 旧导出(legacy)

旧文件**不改写、不补编号**。它们以 `legacy:<完整SHA256>` 引用(允许前缀,但必须在指定目录内唯一),
列表里显示原文件名与开始时间。这是**文件引用**,不是"当时悬浮窗显示过的编号" —— 这一区别写在工具输出里。
本轮对真实导出目录 `BepInEx/plugins/DpsMeter/exports` 实测:**50 份文件 / 0 份带 battleRef / 50 份 legacy**
(全部是 R56 之前的文件,符合预期)。

## 9. 本轮验证【实测】

| 闸门 | 结果 |
|---|---|
| `dotnet build src/DpsMeter.csproj -c Release` | **0 警告 0 错误** |
| 行为套件 | **947 用例 / 98 组 / 0 失败**(R55:866/95;本轮 +81 用例:bref/format、bref/registry、bref/lifecycle) |
| 变异负控(全量) | **130 例 / 0 失败**(本轮 +9,逐例确认红在**具名**用例) |
| `battle_select.py --selftest` | **16 例 / 0 失败**(每条拒绝路径都真的执行过) |
| 真机数据 | `list` 在真实 50 份导出上跑通(全部 legacy) |
| 验收 `n0_acceptance --out acceptance_r56` | **44 条命令 / 76 条检查 / 0 项**(本轮新增 `battle_select` + `selftest/battle_select`) |
| 部署 | 390C1340(423,424 B)→ **F6948470**(433,664 B);备份 `deploy-backup/pre-r56-390C1340/`;两个 crash `.bak` 哈希未变 |

**本轮不移动任何伤害/贡献数字**:编号是元数据,`contribution` 的算法与 schema 未改。

## 10. 已知限制与未做(明确列出)

1. **运行时 `battle_catalog.json` 索引未做**。方案 §5.3 允许"先离线扫描、再接运行时增量索引",本轮只做了前者:
   `battle_select.py list` 直接读目录与文件正文,不依赖任何索引。因此索引损坏/滞后的一类问题目前**不存在**,
   但每次解析都要读文件头并(在 resolve/compare 时)哈希被选中的文件。
2. **UI 没有做真实的 uGUI Button**:游戏场景里没有我们的 EventSystem,加一个会和游戏抢输入。
   点击检测沿用面板已有的方式(`GetAsyncKeyState` + 矩形命中测试),IMGUI 回退里则是真正的 `GUILayout.Button`。
3. **导出侧(ExportService/Overlay)的身份传播没有离线变异负控** —— 这两个文件按设计不在行为套件里,
   变异无法让套件变红,所以本轮**没有**为它们写一条"不会咬"的假闸门。可执行的部分(契约、分配器、视图复制)
   都有具名用例与变异;导出侧靠构建 + 真机一场确认(见 §11)。
4. **证据包的 `manifest.json` 未加 battleRef 键**:包里的 `battle.json` 与普通导出**同一个序列化器**,
   已经携带完整 `battleRef`;给 manifest 加键会改动 R52 已冻结的 bundle 契约(守卫 + 校验器),留待需要时单独做。
5. **历史多选按钮、直接复制多场提示词、自动打包证据、永久累计别名**未做 —— 方案 §194 明确把这些列为后续可选。
6. ~~**`ExportSha256` 是"最后一次成功写盘"的哈希**~~ → **R57 已改**:路径由**首次成功写盘绑定**,之后只有写同一路径
   才更新哈希。原因见 [REFACTOR-BATCH-R57.md](<REFACTOR-BATCH-R57.md>):battle-end 的证据包在普通导出之后写,
   而它的目录会被 `ExtractKeep` 轮转删除,旧规则于是把引用指向了一个已经不存在的文件。
   "之前发出去的固定清单不会跟着变 → `compare` 失败并要求重新确认"这条**不变**。

## 11. 需要你顺带确认的一件事

下一场正常游玩时看一眼主悬浮窗:短序号与完整编号是否显示、**点那一行**能否复制出 §5 的内容。
这一步是"显示 ID = 导出 ID = AI 选中 ID"链条里唯一需要眼睛的部分;如果点击没反应,请把
`LogOutput.log` 里的 `[DpsMeter][BREF]` 行发来(它会说明是复制失败还是没命中)。
