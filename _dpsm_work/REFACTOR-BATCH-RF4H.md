# 重构批次记录 RF4h(第 23 轮):进程级历史环(以及一个被复制成字面量的上限)

> 依据:[REFACTOR-PLAN-POST-1.7.11.md](<REFACTOR-PLAN-POST-1.7.11.md>) §9 第四族(进程级)。矩阵说这一族"本来就不该按场销毁,
> 迁移只是显式化所有者,低收益"。本轮只做其中**有一条真实规则**的那部分,并顺手修掉一个漂移隐患。

## 0. 结论

| 项 | 状态 |
|---|---|
| `Runtime/BattleHistoryRing.cs`(纯) | ✅ 上限 `Max = 20`、新→旧顺序、`Push`/`Newest` |
| 发现并修掉的漂移 | ✅ 结算里写的是**字面量 20**,而 `Aggregator.MaxHistory` 也是 20 —— 两处可改,一处容易漏 |
| 唯一来源 | ✅ `Aggregator.MaxHistory = BattleHistoryRing.Max` |
| 新增覆盖 | ✅ 12 个用例 + 3 例变异负控 |
| **部署 DLL** | **仍未改**:1.7.11 / 387,072 B / `36EC96D4…` |

## 1. 这条规则为什么值得单独成类

它原来是结算里的两行:`History.Insert(0, summary)` + `while (History.Count > 20) History.RemoveAt(History.Count - 1)`。
两个问题:

1. **上限在两个地方**(字面量 20 与 `MaxHistory`),改一处漏一处就会让"最多 20 场"悄悄变成别的数;
2. 这条规则**没有任何地方可测**,而它是长时间游玩不会无限累积摘要的唯一保证。

方向也值得记下来:`History` 是**新→旧**(`History[0]` 是最新,UI 与"上一场"视图都这样索引),
而 `CalcActivityLog`(RF4f)是**旧→新**并从前端淘汰——两个环方向相反,**不能**合并成一个容器,
这也是本轮没有复用它的理由。

## 2. 一个类型事实(夹具因此可用)

`BattleSummary.QuestId` 是**字符串**(`BattleSession.QuestId` 才是整数)。夹具最初用整数,编译器的
`==` 类型错误指出这点;用例注释里写明了,免得下一个人再踩。

## 3. 验证与回滚

| 项 | 值 |
|---|---|
| 构建 | 0 警 0 错;**未部署** |
| 行为套件 | `cases=691 failed=0 pinned=691`(26 组) |
| 变异负控 | 99 例(新增 3:插到尾部、从前端淘汰、上限改 21) |
| 守卫 | `refactor_final_check` 119 个 .cs blocks=0;文档收敛 0/12;docs123 58 份 0 损伤;工具注册表 PASS |
| 验收 | `acceptance_rf4h` |
| 回滚 | `git revert`:新文件 + 三处改动(声明/调用点/常量引用);行为不变 |
