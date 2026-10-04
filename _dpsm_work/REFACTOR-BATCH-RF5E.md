# 重构批次记录 RF5e(第 13 轮):"两渲染器"调查结论 + 回退渲染器的格式化统一

> 依据:[REFACTOR-PLAN-POST-1.7.11.md](<REFACTOR-PLAN-POST-1.7.11.md>) §10(RF5):"uGUI/IMGUI 消费同一视图,不各算一套贡献"。
> 这句话此前一直是**假设**。本轮先查证,再按结论做事。

## 0. 结论

| 项 | 状态 |
|---|---|
| 方案那句话的查证 | ✅ **一半已成立、一半是本轮修的**:**数字**从未算两遍;**格式化**是第二套实现 |
| `src/Ui/FallbackText.cs`(纯,2 个构造器) | ✅ 回退渲染器的两条贡献行 |
| 回退渲染器的数字格式 | ✅ 9 处直接 `:N0`/`:F2` → `DisplayFormat`;**剩余 0 处**(仅留 1 处 `:F0`,见 §2) |
| 新增覆盖 | ✅ 10 个用例(含逐字字符串与"两边同格式"不变量)+ 3 例变异负控 |
| **部署 DLL** | **仍未改**:1.7.11 / 387,072 B / `36EC96D4…` |

## 1. 查证:那句话到底对不对

| 问题 | 证据 | 结论 |
|---|---|---|
| 有没有两个渲染器? | `Plugin.CfgOverlayMode`(`OverlayMode = ugui`(默认)/ `imgui` / `none`,解析在 `Plugin.cs:367`);`OverlayUI`(IMGUI MonoBehaviour)把四个回调转给 `OverlayCore` | **有**:uGUI 面板 + IMGUI 回退 |
| 贡献数字算几遍? | `OverlayCore.DrawContributionDashboard` 调用 `OverlayUGUI.ResolveContributionView(useFolds)`;它自己的注释写着 "reads the SAME cached view the uGUI renderer uses, so both show one set of numbers" | **只算一遍** ✅ 方案担心的"各算一套贡献"**不成立** |
| 格式化有几套? | `OverlayCore.cs` 里 **9 处** `:N0`/`:F2`/`ToString("N0")`,`DisplayFormat` 使用次数 **0**;而 uGUI 面板与导出都走 `DisplayFormat`(InvariantCulture) | **两套** ❌ 改 `DisplayFormat` **传不到**回退渲染器 |
| 布局为什么不同? | 回退用 `GUILayout.Label` 自然换行;uGUI 用等宽列 + `ContributionColumns` | **故意不同**(回退没有固定列宽),本轮不动 |

所以准确的表述是:**同一份数字、同一份视图;两套排版,其中一套的格式化此前没走统一层。**

## 2. 本轮统一了什么

| 位置 | 之前 | 之后 |
|---|---|---|
| 回退的角色行 | `$"  {a.Name}... 总贡献 {a.Total:N0}({share:F2}%) ..."` | `FallbackText.ContributionActorLine(...)`(纯函数,用例逐字钉住) |
| 回退的合计行 | `$"  合计 {..:N0}   未归因 {..:N0}({unattrPct:F2}%)   命中 {..:N0}"` | `FallbackText.ContributionTotalsLine(...)` |
| 名册/表头/上一场的 7 处数字 | `{x:N0}` / `ToString("N0")` | `DisplayFormat.Num(...)` |
| 秒伤 `{a.Dps(secs):F0}` | `F0`(无千分位) | **故意保留**:`DisplayFormat` 没有 F0 语义,换成 `Fmt` 会**凭空加上千分位**(可见变化) |
| 窗口矩形写盘 `{x:F0},...` | — | 不是展示文本,不在范围 |

## 3. 一个必须写明的行为变化(文化)

旧插值用**当前文化**,`DisplayFormat` 用 **InvariantCulture**。在发行语言(ja/en)两者都用 `,` 作千分位,**显示完全一致**;
在千分位不是 `,` 的语言下,回退渲染器现在与**面板和导出一致**——也就是说,回退此前是与另外两者不一致的那一个,
而不是参照。这条变化是有意的,写进代码注释与本记录。

## 4. 覆盖

| 组 | 内容 |
|---|---|
| `fallback/contribution` | 角色行与合计行**逐字**字符串;召唤标记后缀;`null` 名不崩;长 CJK 名**不截断**(回退是换行标签,不是定宽表);`NaN` 显示为 0;以及三条**不变量**——回退行里出现的就是 `DisplayFormat.Fmt/Num/Pct` 产出的同一串,且与 uGUI 行对同一个数一致 |

## 5. 验证与回滚

| 项 | 值 |
|---|---|
| 构建 | 0 警 0 错;**未部署** |
| 行为套件 | `cases=567 failed=0 pinned=567`(19 组) |
| 变异负控 | 76 例(新增 3 例:丢召唤标记、把"他人因你/被队友分走"对调、合计行把百分比换成数字) |
| 守卫 | `refactor_final_check` 106 个 .cs blocks=0;文档收敛 0/11;docs123 48 份 0 损伤;工具注册表 PASS |
| 验收 | `acceptance_rf5e` |
| 回滚 | `git revert`:新文件 + 9 处渲染器改为共用格式化;**文化差异**是唯一行为面(§3) |

## 6. 未做

1. **两套排版保持不同**(§1 最后一行):方案要的是"同一视图",不是"同一布局";把回退改成等宽列会是产品决策;
2. **RowViewModel**:数据行的"值 → 单元格"投影仍在渲染器;
3. `:F0` 的秒伤格式要不要也统一(会加上千分位)需要产品决定。
