# 重构批次记录 RF4d(第 7 轮):应用侧算术 + "门梯为何留在探针里"

> 依据:[REFACTOR-PLAN-POST-1.7.11.md](<REFACTOR-PLAN-POST-1.7.11.md>) §9 第二族的最后一块。
> 矩阵给这一族留的尾巴是"应用侧 `ApplyGlobalDebuffs` 未动"。本轮把其中**能安全抽的部分**抽出来,
> 并把**不能抽的部分**用代码证据写成结论(矩阵 §8),避免下一轮重新论证同一件事。

## 0. 结论

| 项 | 状态 |
|---|---|
| `Policy/GlobalRuleApplyPolicy.cs`(67 行) | ✅ 属性门取值 + 副本数 + 逐状态幂 |
| 探针接线 | ✅ 两处(属性门、状态/乘积) |
| 门梯本体抽取 | ❌ **不抽**,理由见 §2(矩阵 §8) |
| 新增覆盖 | ✅ 22 个用例 + 6 例变异负控(含 `Math.Pow` 替换必须变红) |
| **部署 DLL** | **仍未改**:1.7.11 / 387,072 B / `36EC96D4…` |

## 1. 抽了什么

| 迁移前(探针内联) | 迁移后(`GlobalRuleApplyPolicy`) |
|---|---|
| `(ht == 2 \|\| ht == 5)` / `(ht == 1 \|\| ht == 5)` 两个属性门 | `IsMagicHit` / `IsPhysHit` + 命名常量 `HitTypePhys=1 / HitTypeMagic=2 / HitTypeBoth=5` |
| `int hits = 0; if (Tokens.Count == 0) hits = 1; else ...` | `StatusCopies(tokenCount, matchedTokens)`(0 = 本条规则不触发,调用方必须连同责任登记一起跳过) |
| `if (hits > 1 && PerStatus) { acc=1; for(...) acc*=f; f=acc; }` | `EffectiveFactor(factor, copies, perStatus)` |

**为什么偏偏是这一块**:它是 `ApplyGlobalDebuffs` 里唯一**不做原生读取**的部分,而且是 1.15^n 那段历史的所在 ——
方案与项目文档反复讨论的"逐状态倍率被叠加了几次"就落在这三行上。

## 2. 门梯为什么不抽(三条代码证据)

| # | 证据 | 后果 |
|---|---|---|
| 1 | 每一级门都是**惰性**原生读取:持有者同队只在"敌方受伤"型上读(`Aggregator.IsSameTeam(r.OwnerObj, victim)`);持有者前/後衛只在规则点名位置时读(`r.OwnerObj.IsVanguard`);攻击者同队/位置只在"我方攻击"型上读;"持有者已失效"只在**不是**持有者本人时读(`GameRef.IsNull(r.OwnerObj)`) | 提前把事实读完 = 最热路径上多出"规则数 × 若干"次互操作调用 |
| 2 | `GameRef.IsNull` 的源码注释写明:它会调用原生代码,**可能抛出不可捕获的 AccessViolation**,所以连"读一个字段"都要谨慎 | 为纯函数提前读取 = 平白扩大崩溃面 |
| 3 | 整段循环体包在一个 `try/catch` 里:某条规则读取失败只影响那一条 | 提前读取会把"一条规则的读取失败"升级成"这一击整段失败" |

于是结论写进矩阵 §8:门梯**留在探针**,由真实导出与实机覆盖;纯函数只负责门梯**之后**的算术。
这不是放弃,而是"先证明能不能安全地做,再决定做不做"的产出。

## 3. 行为保持

1. 属性门:比较的值与短路顺序不变(两个常量 1/2/5 逐字对应原来的字面量);
2. 副本数:`Tokens.Count == 0 ⇒ 1`,`否则 = 命中 token 数`;`0` 仍然 `continue`(连责任登记一起跳过,与原来 `hits == 0` 处的位置一致);
3. 逐状态幂:**仍然是循环累乘,不是 `Math.Pow`** —— 实测 `Math.Pow(1.15,3)` 与 `1.15*1.15*1.15` 相差 1 ulp
   (`0x1.855810624dd2e` vs `…2d`),换成 `Pow` 会让每一条逐状态残差悄悄移位;
4. 乘积与登记(`m *= f`、`ctx.Add`、`sb.Append`、日志)全部留在原位与原本顺序。

## 4. 新增覆盖

| 组 | 内容 |
|---|---|
| `policy/globalrule-apply` | 三个命中类型的取值;1/2/5 与 -1/3 的判定;`Both` 同时满足物理与魔法 |
| `policy/globalrule-copies` | 无状态条件 ⇒ 1 次;有状态条件但零命中 ⇒ 0 次(不触发);命中 1/3 次 |
| `policy/globalrule-factor` | 单次即原值;`PerStatus=false` 时多次命中仍只应用一次;**`PerStatus=true` 时是重复乘积**;该乘积与 `Math.Pow` 不等(实测 1 ulp);1.15³ = 1.520875 |

变异负控里特意加了 `apply-factor-uses-pow`:把循环换成 `Math.Pow` **必须变红**,这条用例的价值就在于此。

## 5. 验证与回滚

| 项 | 值 |
|---|---|
| 构建 | 0 警 0 错;**未部署** |
| 行为套件 | `cases=452 failed=0 pinned=452` |
| 变异负控 | 52 例(新增 6 例) |
| 守卫 | `refactor_final_check` blocks=0 + 自测 PASS;文档收敛 0/11;docs123 41 份 0 损伤;`repo_manifest --verify` drift=0 |
| 验收 | `acceptance_rf4d` |
| 回滚 | `git revert`:新文件 + 两处循环体重写,无数据/契约变化;部署 DLL 从未被替换 |

## 6. 未做

1. 门梯本体(见 §2):留待"能把原生对象驱动成事件"的那一天,或有真实导出证明某级门有缺陷时;**不为了整齐而抽**;
2. `_curHitType` / `_ruleLogCount` 仍在探针(前者由链式构建写、后者是诊断计数),属单场运行态,归 RF4 的第三族;
3. 注册扫描的原生读取部分仍未离线驱动(判据部分在 RF4b 已覆盖)。
