# ATKADD 模型审计：攻击力加算归因的假设、边界与可证伪性

- 任务：N4 / P1（atkadd 假设验证与归因敏感性）— 只读审计部分
- 审计范围：DpsMeter IL2CPP 插件 `log-share/1` 贡献模型中 kind=atkadd 虚拟因子的公式依据、逐击/全场数据口径、边界行为、多 giver 语义与逐 giver 对账能力
- 本文件为**新建**文件。审计过程**未修改**任何既有源码、脚本、导出、DLL 或官方贡献表；未部署；未运行游戏
- 工作目录：D:\dmmplayer\rlyehshoujotaix_cl

## 0. 结论摘要与证据分级约定

证据等级（全文统一）：

| 代号 | 含义 |
|---|---|
| 实测 | 由运行中的插件现场写入真实导出的计数/字段；本审计仅离线读取并核对，未重新实测 |
| 离线重放 | 本审计用独立 Python/JS 实现，从同一导出事件集重新计算，与导出自带数字逐项对照 |
| 静态审查 | 只读源码得出结论，未运行该代码路径 |
| 推断 | 模型假设，尚无独立观测支持 |

结论类型（全文明确标注）：

- **守恒**：同一事件集内各方分账之和等于该事件集的可分析伤害，或某变换前后总和不变。
- **身份**：某个名字/键是否唯一且稳定地指向一个 actor。
- **公式证据**：公式 `P = B×(1+r/100)+a` 是否被与 P 无关的观测支持。

三条最重要结论（先行）：

1. **公式证据 = 无。B 确实是由 P 反推的。** 源码 `AtkAddFold.cs:177` 直接写 `b = (attackPower - a) / denom`，其中 `attackPower` 就是被解释的 P，`a`/`r` 来自同一击的增益条目。把 B 代回 `P = B×(1+r/100)+a` 只是恒等式，**不能**作为公式成立证据。（静态审查）
2. **逐 giver 对账目前不可完成。** 12 份带 atkAdd 的真实导出共 50,599 行 `calc.atkAdd`，其中携带 giver actor key 的行数为 **0**；每行只有 `byUnit`（显示名）。`paramOwners` 的 owner/keyOwner/unit 也全部是名字而非键。聚合层（rules/links/assistCredit）无法排除 A 的贡献被错记到 B。（离线重放 + 静态审查）
3. **多 giver 因子相乘是分账约定，不是联合反事实。** 乘积 `∏ P/(P−dP_g)` 与一次性删除全部 giver 的 `P/(P−ΣdP_g)` 数值不同（见 §4 手算：480 vs 400）。该约定**守恒**，但不是物理反事实。（静态审查 + 离线重放）

---

## 1. 公式 `P = B × (1 + r/100) + a`：来源、单位、读取时刻、独立可观测量

### 1.1 公式在源码中的位置与写法

- 设计陈述：`_dpsm_work/src/Model/AtkAddFold.cs:47-49`
  > `P = B*(1 + r/100) + a`，removing g leaves `P - dP_g` with `dP_g = B*r_g/100 + a_g`, and `f_g = P / (P - dP_g)`。
- 落点代码：`AtkAddFold.cs:173-178`

```
if (r <= -100.0) { SkippedGuard++; return into; }
double denom = 1.0 + r / 100.0;
if (denom <= 0.0) { SkippedGuard++; return into; }
if (attackPower <= a) { SkippedGuard++; return into; }
double b = (attackPower - a) / denom;      // <-- B 由 P 反推
if (b <= 0.0) { SkippedGuard++; return into; }
```

- 逐 giver 的 `dP` 与 `f`：`AtkAddFold.cs:235, 249`

```
double dp = b * rate[own] / 100.0 + actual[own];
...
row.Factor = attackPower / (attackPower - dp);
```

### 1.2 特别回答：B 是不是由 P 反推的？

**是。**（静态审查；离线重放验证了导出中 B 与 P 的关系）

- `b` 的唯一赋值在 `AtkAddFold.cs:177`，右侧三个量 `attackPower`、`a`、`r` 全部来自同一击的输入，没有任何独立读数。
- 因此 `P = B×(1+r/100)+a` 在此实现中是**由 P 定义 B、再由 B 定义 dP、再由 dP 定义 f** 的分解链。存在恒等式 `(P−a)/(1+r/100)` 代回后必然还原 P，**零信息量**。
- 结论：该公式在当前导出中**不具备公式证据**。它是模型的**定义/约定**，不是被独立验证的规律。要给它证据，必须提供一个“未被加算影响”的攻击力读数（例如排除加算后的攻击力快照），当前导出不含这样的字段。
- 附加强化：被解释量 `attackPower` 本身也不一定是直接读取值。`CompositionProbe.Chain.cs:289-323` 显示它会在「构造前 / 攻击开始快照 / 结算时 live / 归属者 / 按主档反推」多个候选间选择，甚至可由 `计算威力 ÷ 主档系数` 反推得到（`shownSrc = "按主档反推"`），而该来源**没有写进导出**。也就是说：P 可能已经是派生值，B 又由 P 派生，`f_g` 又由两者派生——三级派生，越往上游越无独立观测。（静态审查）

### 1.3 单位

| 量 | 载体 | 单位 | 依据 |
|---|---|---|---|
| `r` | 同一击所有 `Type=="Rate"` 条目的已应用值之和 | 百分数（数值 7 表示 +7%） | `AtkAddFold.cs:146-147`；除以 100 在 `174`、`235`；`readAtkItem` 取 `ParamData.Param` 的“已应用值” |
| `a` | 同一击所有 `Type=="Actual"` 条目的已应用值之和 | 与攻击力同量纲的绝对整数 | `AtkAddFold.cs:147`；`b = (P−a)/denom` 要求同量纲 |
| `P` | `calc.attackPower` | 与攻击力同量纲的整数 | `Chain.cs:429 brk.AttackPower = shownAtk` |
| `B` | 由 P 反推 | 同 P | `AtkAddFold.cs:177` |
| `dP_g` | 逐 giver | 同 P | `AtkAddFold.cs:235` |
| `f_g` | 无量纲倍数（≥1，否则被拒） | — | `AtkAddFold.cs:249` |

### 1.4 读取时刻

- 位置：`CompositionProbe.Chain.cs:516-523`。三个输入在同一处、同一瞬间取得：
  - `atkAddItems`：`BuildBuffText(atk, ...)` 在同一次 `BuffParamData.mNowBuffParamDataDictionary` 遍历中顺带采集（`Chain.cs:619-623`）；
  - `brk.AttackPower`：同一次链构建选定的 `shownAtk`（`Chain.cs:429`）；
  - `selfKey`：`ActorKeyOf(atk)`（`Chain.cs:990-1001`，`GetActor(bo, false)`，不创建 roster 行）。
- 设计意图（静态审查）：采集点与产生「增益」文本的遍历相同，因此 atkAdd 列表与 AttackPower“描述同一瞬间”（`AtkAddFold.cs:7-8`；`Chain.cs:615-618`）。
- 但:**读取时刻一致 ≠ 语义无偏**。P 的候选选择（1.2）说明同一“瞬间”的 P 仍可能是按主档反推。此外 `AttackPower` 采用的是“攻击开始快照”而非结算时 live（`Chain.cs:55-68`），二者可能不同（同文件注释记录了 5x 偏差实例）。

### 1.5 独立可观测量逐条清单

| # | 量 | 源码位置 | 是否独立于 P | 证据等级 |
|---|---|---|---|---|
| O1 | 该条目 `Type ∈ {Rate, Actual}` | `AtkAddFold.cs:143-144`；`Chain.cs:1019` | 是 | 静态审查 |
| O2 | 该条目已应用值 `ParamData.Param` | `Chain.cs:1020`；`AtkAddFold.cs:13-14` | 是 | 静态审查 |
| O3 | 符号 `BuffParam.IsPlus` | `Chain.cs:1016`；`AtkAddFold.cs:146` | 是 | 静态审查 |
| O4 | 参照 `ReferenceType/ReferenceParam`（如 `/refExistenceTime3000`） | `Chain.cs:1021-1027` | 是 | 静态审查 |
| O5 | giver：`ParamData.Owner`（值侧，实测为 giver）与 `BuffParam.m_owner`（键侧） | `Chain.cs:1031-1042`；`AtkAddFold.cs:17-18` | 是（但导出只落名字，见 §5） | 静态审查 + 离线重放（`paramOwners.mismatch=0`） |
| O6 | giver actor key：`OwnerKey/KeyOwnerKey` | `Chain.cs:1034,1040`；`AtkAddItem.cs:23-24` | 是 | 静态审查。**未序列化进 `calc.atkAdd`** |
| O7 | 攻击力 P（含加算） | `Chain.cs:429` | **否**（可能为反推） | 静态审查 |
| O8 | 计算威力 `pow`（游戏自身数） | `Chain.cs:70,430` | 半独立：加算已包含在 pow 内，且需技能系数才能还原攻击力 | 静态审查 |
| O9 | 防御项与保底 | `Chain.cs:89-113` | 是 | 静态审查 |
| O10 | `calc.power`/`calc.ratio` | 导出 `calc` 段 | `ratio` 由 P 算出，**不独立**；`power` 独立于 P | 静态审查 |
| O11 | 未被加算影响的攻击力 | **不存在** | — | 缺失字段 |

**结论（公式证据）：** 加算“条目本身”是独立可观测的（O1–O6）；但公式左侧的 P 与右侧的 B **没有相互独立的观测**。因此 §1.2 的否定结论成立。

### 1.6 公式证据结论（明确）

- `P = B×(1+r/100)+a` 在当前导出与实现中：**是模型定义，不是被验证的规律**。
- 归属为「公式证据」的部分：**无**。
- 归属为「守恒」的部分：见 §2/§6（分账总和守恒，与公式无关）。
- 归属为「身份」的部分：见 §5（giver 身份到键的链路不完整）。

---

## 2. 逐击有效参数 vs 全场 owner 并集

### 2.1 两个数据源

| 源 | 生成处 | 导出字段 | 语义 |
|---|---|---|---|
| 逐击有效条目 | `Chain.cs:516-523` 的 `BuildBuffText` 遍历 | 每个 dmg 事件 `calc.atkAdd[]` | 该击**实际存在**的攻击力条目及其 giver |
| 全场 owner 并集 | `CompositionProbe.BuffParamTableDump` 的 owner 侧（`Chain.cs:680-801`），由 `ExportService.cs:554-557` 落盘 | 顶层 `paramOwners.rows[]`，含 `unit/owner/keyOwner/tgt/ty/val/firstT/lastT/seen` | 整场战斗内“曾在哪里见过”，`firstT..lastT` 是**采样包络**，不是占空比 |

### 2.2 证据：并集行覆盖不了逐击事实

- 源码自述：`AtkAddFold.cs:52-56` 记录一个 メアリー 的命中，其增益列表**完全不含** `+300%`，却落在并集行声明的 `[7.83, 115.27]` 区间内 → 并集存在“区间内但不生效”的样本。
- 本审计离线核对（离线重放）：以 `battle_411001_20261004_144548.json` 为例，`paramOwners` 有 966 次读取、**141** 条并集行、**12** 个 distinct owner；而 `calc.atkAdd` 逐击行只有 5,317 行。并集行的 `firstT..lastT` 是采样包络：含 `val=300` 的并集行声明覆盖全场，但该场 5,585 次 dmg 中有 **359 次**的 `calc.atkAdd` 完全不提 300——它们仍落在那条包络之内。
- 因此：**归属必须来自逐击 `calc.atkAdd`，不能用 `paramOwners` 的并集代替**。`AtkAddFold.cs:52-56` 与 `ContributionSession.cs:76-95` 均按此实现。

### 2.3 结论

- 「守恒」：逐击口径下的分账守恒成立（§6 离线重放）。
- 「身份」：并集行的 `unit/owner/keyOwner` 只有名字，不能作为逐击身份。
- 「公式证据」：并集不提供任何关于公式的证据。

---

## 3. 边界行为逐条

### 3.1 过期增益（stale buff）

- 定义：某一击的增益列表里**没有**某条目，但该条目出现在全场并集内。
- 处理：逐击采集只看该击的 `mNowBuffParamDataDictionary`，因此过期条目不进 `calc.atkAdd`。
- 源码：`AtkAddFold.cs:52-56`；`Chain.cs:619-623`。
- 证据等级：静态审查；离线重放（逐击行数 < 命中数，并集区间内的缺失样本存在）。
- 风险残留：反方向也成立——若采样时刻恰好错过了某一击的短暂加算，则**漏计**无法从导出中发现（没有“该击本应有一条但没读到”的计数）。

### 3.2 同值去重（same (type,value) from multiple owners）

- 实现：pass 1 以 `ty + "|" + value` 为键（`AtkAddFold.cs:148`）。同一 `(type,value)` 第二次出现时：
  - 若 `WinnerPair` 名字不同 → 标记 `ambiguous`，`SkippedCollision++`（`AtkAddFold.cs:167-171`），该值在 pass 2 被整体跳过（`188`），**两个 owner 都不计**。
- 关键细节（静态审查）：`r`/`a` 的累加发生在 owner 判定**之前**（`AtkAddFold.cs:146-147`），因此被拒绝/冲突的值**仍然计入 B**，却无人认领。后果：`Σ dP_g < P − B` 是预期行为，差额（自身加算 + 冲突 + 未知 owner）留在 base。
- 实测计数（12 份 atkAdd 导出）：`skippedCollision` 合计见 §6 表；`144548` 为 56。
- 结论：这是**有意的保守拒绝**，不会把冲突值错记给任一 giver；但它使 B 对“冲突值”敏感，而 B 又是所有 giver 的 `dP` 基数 → **一个条目的冲突会影响其他 giver 的当量**（推断，尚未单独量化）。

### 3.3 多 giver

- 每个 giver 一条 `AtkAddRow`，各自 `Factor = P/(P−dP_g)`，插入同一 fold 列表后由 `ln f / ln M` 分池（`AtkAddFold.cs:232-252`；`Contribution.cs:396-407`）。
- 语义结论见 §4：**分账约定，不是联合反事实**。

### 3.4 未知 owner

- pass 2 中 `owner[k] == null` → `SkippedUnowned++`，不产出行（`AtkAddFold.cs:190`）。
- 无键但有名字的退化路径：`OwnerUnknown++`，并以名字判定是否 self（`AtkAddFold.cs:209-216`）；若是队友则仍可产出（名字回退）。
- 实测：`144548` 中 `skippedUnowned=0`、`skippedOwnerNull=0`、`ownerUnknown=27`（即 27 次走了名字回退，且没有判定为 self）。`9999` 训练场样本 `skippedUnowned=96`（`battle_9999_20261004_135214.json`），说明存在真正读不到 owner 的条目。
- 身份结论：只要走到名字回退，giver 身份就**未经键验证**；这些行在贡献表里与键验证行**不可区分**（同一 `origin/kind`，没有 `ownerUnknown` 标记进入 `calc.atkAdd` 行）。**这是当前导出无法事后分离的审计缺口。**

### 3.5 `P − dP ≤ 0`（单个 giver）

- 守卫：`if (dp <= 0.0) SkippedGuard++`；`if (dp >= attackPower) SkippedGuard++`（`AtkAddFold.cs:236-237`）。
- 整体守卫：`r <= −100`、`denom <= 0`、`attackPower <= a`、`b <= 0`（`AtkAddFold.cs:173-178`）→ 整击不产出任何 atkadd 行，`SkippedGuard++`。
- 实测：所有带 atkAdd 导出的 `factorBad=0`、`dpBad=0`（离线重放：`f == P/(P−dP)` 与 `dP == B·r/100+a` 在 50,599 行上零反例），且 `skippedGuard` 合计见 §6。
- 结论：守卫是**拒绝而非钳制**，因此导出中不应出现 `P−dP≤0` 的因子。合成用例 C1（§脚本）验证：若人为注入这类行，本次敏感性脚本必须拒绝它不是照单全收。
- **潜伏缺口（离线段）**：消费侧 `Contribution.cs:386-393` 只过滤 `factor<=0 / ≈1 / <1`，**不重新检查 `dP<P`**。`factor=+∞`（即 `dP==P`）会通过 `factor<=0` 与 `factor<1` 两道检查被接纳，令 `M=∞`、`lnM=∞`，份额变成 `∞/∞=NaN`，最终经 `Num()` 静默写成 0。生产路径因 §3.5 守卫不会产生该行，故这是**对篡改/外来文件的防御缺口**，不是现网错误。本审计的敏感性脚本为此额外加了一条 `isfinite(factor)` 守卫（见脚本 C1）。

---

## 4. 多 giver：`f_g = P/(P−dP_g)` 相乘 ≠ 一次性删除全部 giver

### 4.1 数学陈述

设 `g = 1..n`。模型做法：

```
M = ∏_g P/(P − dP_g)          （AtkAddFold 每 giver 一个因子，Contribution 全部相乘）
base_model = D / M
```

一次性删除全部 giver 的实际反事实（按同一公式 `P = B(1+r/100)+a` 推）为：

```
P_joint = B·(1 + (r − Σr_g)/100) + (a − Σa_g) = P − Σ dP_g
base_joint = D / (P / P_joint) = D · P_joint / P
```

二者一般不相等，因为 `∏ P/(P−dP_g) ≠ P/(P−ΣdP_g)`。

### 4.2 手算用例（n=2）

输入：`P = 1000`，giver1 `Rate +50`，giver2 `Rate +100`，`Actual` 均为 0。

| 量 | 值 |
|---|---|
| `r = 150`，`a = 0` | — |
| `B = (1000 − 0)/(1+1.5)` | **400** |
| `dP_1 = 400×0.50` | 200 |
| `dP_2 = 400×1.00` | 400 |
| `f_1 = 1000/800` | 1.25 |
| `f_2 = 1000/600` | 1.666666… |
| `M = f_1·f_2` | 2.083333… |
| `D = P = 1000` 时 base（模型） | 1000/2.083333 = **480** |
| pool | 520 |
| share1 = 520·ln1.25/ln2.083333 | **158.09** |
| share2 = 520·ln1.66667/ln2.083333 | **361.91** |
| 联合删除后 `P_joint = 1000 − 600` | **400** |
| 联合删除后 base | **400**（模型给 480，**多 80**） |

结论：模型的分账把 base 抬高到 480，比“两个 giver 都不存在”的 400 多 80；这 80 来自 `dP_1·dP_2/P` 的交叉项。因此：

- **守恒**：`base + share1 + share2 = 1000` 恒成立。
- **公式证据**：无（§1）。
- **反事实解释**：**不成立**。这些数字**不是**“换掉 giver 后会少的伤害”。任何把 `assistCredit` 直接读成“移除该角色的伤害损失”的说法都被本用例否证。
- 结论定性：`f_g` 相乘是**分账约定（accounting convention）**，用于把已观测到的 P 在 giver 之间分摊；不是**联合反事实**。

### 4.3 与隔离实验的关系

正因为是分账约定，“隔离 atkadd”必须写清是哪一种重分账（见脚本 §A/B），且都**不模拟真实换人**。

---

## 5. 逐 giver 对账：为什么聚合等式不够，以及最小字段设计

### 5.1 聚合等式为什么无法排除 A 的贡献被错记到 B

现有可对账的量全部是**按 owner 求和**的线性汇总：

- `contribution.actors[].assistCredit`（`Contribution.cs:441, 552`）
- `contribution.links[].amount`（`Contribution.cs:442-445, 586`）
- `contribution.rules[].damageEquivalent`（`Contribution.cs:455-456, 573`）

它们满足的总和恒等式（守恒）对**任何**在 owner 之间的重新分配都成立。设真实分配为 `x_A, x_B`，若错记为 `x'_A = x_A − δ`、`x'_B = x_B + δ`，则：

- `Σ assistCredit` 不变；
- `attributedDamage` 不变；
- 每击的 `credited + unattributed = damage` 不变；
- 只要 `δ` 在两位 owner 之间转移，**所有**列都看不出差别。

尤其：`calc.atkAdd` 序列化了 `byUnit`（名字）却**没有** giver key；审计已核实 **50,599 / 50,599 行都不带键**。因此：

- 身份不唯一时（同名两 actor），逐击 giver 无法区分；
- 一旦发生错记，聚合层没有任何独立见证可以证伪；
- `paramOwners` 的 `owner/keyOwner/unit` 也全是名字（`unit` 形如 `"-1|メルティエル"`），不能补位。

### 5.2 最小字段设计（**先设计、不实施**）

目标：让“A 的加算是否被记到 B”**可被独立证伪**，而不引入新 KPI 定义。建议的最小字段集合（全部为**新增**，不改既有字段语义；按 roadmap §4，实施属后续批次，本任务仅设计）：

逐击（`calc`）：

| 字段 | 类型 | 作用 |
|---|---|---|
| `atkAddByKey` | int[] | 与 `atkAdd[]` 等长，giver actor key；0 = 未知（替代/补充 `byUnit` 名字） |
| `atkAddEntryId` | int[] | 与 `atkAdd[]` 等长，该条目在**本击增益遍历中的序号**，给出实例级身份 |
| `atkPowerTerms` | object | `{rateAll, actualAll, rateSelf, actualSelf, rateRefused, actualRefused}`，使 B 可被**独立重建**，并把自身/冲突/未知从 B 中分离 |
| `atkPowerSrc` | string | P 的来源枚举：`ctor/actionSnapshot/live/owner/rateImplied`（当前缺失，见 §1.2） |
| `foldSetDigest` | string | 该击 fold 集合的稳定摘要，供离线重放证明读的是同一集合 |

逐条目（若将来要真正分离“谁赋予/谁应用”）：

- `unitKey`：该条目所在字典的持有者 actor key（当前 `paramOwners.unit` 只有名字）；
- `giverKey`：赋值方 key；
- `beneficiaryKey`：受益方 key（用于“给队友”关系，当前靠事件 `atkKey` 隐含）；
- `instanceId`：若游戏侧存在稳定实例标识则用之，否则用 `(扫描序号, 应用时刻)` 组合，作为“同值不同 giver”的区分见证。

对账契约（设计）：

1. `Σ_{entry} value(entry) ∈ {rate,actual}` 必须能重建 `atkPowerTerms`；
2. giver→beneficiary 的每对关系，其 `damageEquivalent` 必须能由**实例级**条目重算，而非仅由 owner 汇总；
3. 任何被守卫拒绝的条目必须在 `atkPowerTerms` 的 `refused` 桶里可见，使 B 的重建无缺口。

**明确：本节只设计，未实施。** 未修改任何 .cs，未改 schema。

---

## 6. 真实样本对照

### 6.1 语料

12 份真实导出携带 `atkAdd`（插件版本 1.7.4–1.7.10）。全事件合计 `calc.atkAdd` 行 **50,599**；其中落在“可分析命中（1 队且攻击者键可解析）”上的行为 **49,497**（敏感性脚本的 `atkadd_rows` 用后者，因为它与分母同口径）。

### 6.2 插件现场计数（实测，经离线核对）

以 `battle_411001_20261004_144548.json`（v1.7.10，SHA256 `20b4adc1…`）为准：

| 字段 | 值 |
|---|---|
| `atkAdd.hits` | 5507 |
| `atkAdd.emitted` | 5317 |
| `atkAdd.selfValues` | 5205 |
| `atkAdd.selfByKey` / `selfByNameFallback` | 5205 / 0 |
| `atkAdd.nameCollision` | 0 |
| `atkAdd.ownerUnknown` | 27 |
| `atkAdd.skippedGuard` | 131 |
| `atkAdd.skippedCollision` | 56 |
| `atkAdd.skippedUnowned` / `skippedOwnerNull` / `skippedNegative` / `skippedType` | 0 / 0 / 0 / 0 |

离线核对结果（12 份全部）：`emitted == 导出的 calc.atkAdd 行数`（份额一致）；`selfValues == selfByKey + selfByNameFallback` 在 1.7.8+ 成立（1.7.4–1.7.6 无这两个字段，不适用而非不成立）；`f == P/(P−dP)` 与 `dP == B·r/100+a` 在 50,599 行零反例；每一击内所有 atkAdd 行的 `base` 相同。

### 6.3 贡献表与高份额辅助

`144548` 的 `contribution` 段（官方表，仅读取，未改写）：

| actor | key | directDamage | baseCredit | selfRuleCredit | assistCredit | totalCredit |
|---|---|---|---|---|---|---|
| ルナリス | 6 | 352,442 | 173,406 | 0 | **49,384,847** | 49,481,853 |
| マッドシーカー | 5 | 28,043,750 | 7,775,738 | 6,972,714 | 30,475,938 | 45,224,391 |
| ネーフェ＝ジアー | 2 | 60,185,329 | 6,471,267 | 24,225,402 | 0 | 30,696,668 |
| メアリー | 7 | 56,396,032 | 1,763,013 | 24,892,618 | 0 | 26,655,631 |
| メルティエル | 3 | 6,856,411 | 1,157,268 | 1,798,542 | 20,793,476 | 23,749,285 |
| エヴァラス・フラウ | 9 | 21,063,563 | 3,123,677 | 7,886,854 | 0 | 11,010,531 |
| テトラ | 4 | 19,210,488 | 5,048,014 | 1,442,501 | 0 | 6,490,515 |

`contribution.rules` 中 `kind=atkadd` 共 16 条，**owner 全部为 ルナリス（key 6）**，其 `damageEquivalent` 合计 **49,308,446.57**；这与离线重放得到的 `atkadd_assist = 49,308,446.5745` 相差 0.0002（F4 舍入）。这是**算术身份**核对，不是独立模型验证。这正是本任务要审计的高份额辅助：ルナリス 的 `assistCredit` **100%**（49,308,446.57 / 49,308,446.57）来自 atkadd 模型假设，而 ルナリス 的 `directDamage` 仅 352,442。**该巨量份额完全来自 §1 所述的、无独立公式证据的分账约定**，因此对模型假设高度敏感。

> 术语纪律：以上数字是**在现行模型下的分账结果**，不是“移除 ルナリス 会少掉的伤害”。敏感性结果见 `_dpsm_work/atkadd_sensitivity_result.md`。

### 6.4 守恒验证

离线重放（独立实现）在 `144548` 上得到 `dmg 事件 5585`、`可分析命中 5493`、`analyzableDealt 194,697,612`，与导出 `contribution.totalDamage = 194,697,612`、`damageLedger.analyzableHits = 5493` **完全一致**。逐击 `credited + unattributed = damage` 的最大误差为 **1.16e-10**（浮点级，守恒）。

---

## 7. 证据分级汇总与三类结论

| 断言 | 结论类型 | 判定 | 证据等级 |
|---|---|---|---|
| 逐击各分项之和 == 该击伤害 | 守恒 | 成立 | 离线重放 |
| 隔离 atkadd（方法 A/B）前后总额 == analyzableDealt | 守恒 | 成立 | 离线重放（脚本） |
| `Σ assistCredit == attributedDamage` | 守恒 | 成立 | 离线重放 |
| `f == P/(P−dP)`、`dP == B·r/100+a` | 守恒/算术 | 成立（50,599 行零反例） | 离线重放 |
| giver 身份由 actor key 稳定决定（含 atkAdd 逐击行） | 身份 | **不成立**：逐击行只有名字，无键 | 离线重放 + 静态审查 |
| 名字在队内唯一时可解析 | 身份 | 队内唯一名字成立，但同名/改名不可区分 | 静态审查 |
| `P = B(1+r/100)+a` 独立成立 | 公式证据 | **无证据**：B 由 P 反推 | 静态审查 + §1.2 |
| `f_g` 相乘代表联合反事实 | 公式证据 | **不成立**：是分账约定 | 静态审查 + 手算 |
| 高份额辅助的份额对模型假设敏感 | — | 由敏感性实验给出 | 见结果文件 |

---

## 8. 未解决问题与限制

1. **无法独立验证公式**：导出不含“排除加算后的攻击力”。除非将来新增该读数（§5.2 设计），公式只能作为约定使用。
2. **逐击 giver 身份不可审计**：0/50,599 行带键；`skippedCollision`/名字回退行无法事后分离。
3. **P 的来源未落盘**：`按主档反推` 的 P 与直接读取的 P 在导出中不可区分。
4. **反向漏计不可见**：没有“本应有一条 atkAdd 但未读到”的计数。
5. **本审计未运行游戏、未改 DLL、未改导出**；所有“实测”数字均来自既有真实导出文件，属现场测量的离线复核，而非本次重新现场测量。
6. 本审计只覆盖 atkadd 通道；不给治疗/控制/生存折算，不模拟真实换人。

---

## 附录 A：源码位置索引

| 主题 | 位置 |
|---|---|
| 公式陈述与反事实意图 | `_dpsm_work/src/Model/AtkAddFold.cs:47-49` |
| 拒绝清单（自述） | `AtkAddFold.cs:58-66` |
| r/a 累加（含被拒值） | `AtkAddFold.cs:146-147` |
| `(type,value)` 键与冲突 | `AtkAddFold.cs:148-171` |
| B 反推与整体守卫 | `AtkAddFold.cs:173-178` |
| 逐 giver `dP`/`f_g` | `AtkAddFold.cs:232-252`（`235`、`249`） |
| self 判定（键优先、名字回退） | `AtkAddFold.cs:192-217` |
| fold 过滤与分池 | `_dpsm_work/src/Output/Contribution.cs:386-407` |
| 归属阶梯 | `Contribution.cs:263-297` |
| assist 记入 | `Contribution.cs:434-448` |
| atkAdd 作为 fold 输入 | `_dpsm_work/src/Output/ContributionSession.cs:76-95` |
| 逐击采集点 | `_dpsm_work/src/Composition/CompositionProbe.Chain.cs:516-523` |
| 条目结构与 owner 读取 | `Chain.cs:1010-1048` |
| P 的候选选择 | `Chain.cs:289-323`（`429` 落盘） |
| 全场并集采集 | `Chain.cs:680-801` |
| atkAdd 计数落盘 | `_dpsm_work/src/Output/ExportService.cs:559-579` |
| paramOwners 落盘 | `ExportService.cs:554-557` |
| 逐击 atkAdd 序列化 | `_dpsm_work/src/Output/CalcReconcile.cs:453-478` |
| 离线参考实现 | `_dpsm_work/contrib/aggregate.py:33-76, 79-273` |

## 附录 B：输入哈希（SHA256）

| 文件 | SHA256 |
|---|---|
| `_dpsm_work/src/Model/AtkAddFold.cs` | `3d37f327a75d1c00bd57a7a867a58829b5c29cccb81bafaebb1abe82bfa89e61` |
| `_dpsm_work/src/Output/Contribution.cs` | `22a840f20c07fa5bedeef1849728ac725f69fd3b944a286d30b90febea284d68` |
| `_dpsm_work/src/Output/ContributionSession.cs` | `41b59a036c8d00d4d9f81554f4cec37afaf1427706f015d6934885dde353718d` |
| `_dpsm_work/src/Composition/CompositionProbe.Chain.cs` | `f581b3d386fddafdc88f5efc8925cacfe60ff0ded7da22c7665cc46443682138` |
| `_dpsm_work/src/Output/ExportService.cs` | `db21389d19ca7438757f1efdbe61c7f87499c856584cf0bd69dd8a4912ecfce8` |
| `_dpsm_work/src/Output/CalcReconcile.cs` | `9ebaa3bd659d1bdf6b232d1599be62349c0ab009346edf41d1e2c678d0e4434e` |
| `_dpsm_work/src/Model/CalcBreakdown.cs` | `00c5c688ea7cff7bac2442ec1f1e4b1615b8b2ec9d6a9e64f9c5ffef6a5f1c77` |
| `_dpsm_work/contrib/aggregate.py` | `c7a8e2f8282bd7abffd6a6e5188b48ae1cda0e9353aac52a0817f4895033e122` |
| `_dpsm_work/CONTRIBUTION-NEXT-PHASE-ROADMAP.md` | `f5227b4fc622d255953d6426f2b9e482722ea3c70d6d6f07eb7dd16c4d0f883b` |
| `BepInEx/.../battle_411001_20261004_144548.json`（主样本） | `20b4adc14bebd5e101c45b56b79db54d203b65a5d0fe40d21073638d60c50ca7` |
| `BepInEx/.../battle_411001_20261004_134853.json`（次样本） | `db1c80b48f550d3cc9293beb991bbebd3a3b04fd29b0f79abe1b4bbd9c02c262` |
| `BepInEx/.../battle_9999_20261004_135214.json`（训练场） | `d17a5a5bfd38ec6eeddc7b32171c91255fec63220871392aef7a4e11e894a6bd` |
