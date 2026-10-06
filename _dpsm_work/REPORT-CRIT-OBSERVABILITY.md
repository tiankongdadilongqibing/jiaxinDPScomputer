# 会心(暴击)逐击可观测性调查与折衷方案

日期:2026-10-06 · 语料:插件 1.7.17–1.7.19 的最近 4 场真实战斗 · 性质:**调查报告 + 方案建议**

> 本轮只做调查与方案,**未改插件代码**。文中的全部统计由一次性离线脚本产生,
> 该脚本在写出本报告后已删除;落地时应以正式工具的输出为准。

## 1. 结论摘要

1. **现在确实统计不到"游戏亲口说的会心"**。通道已经接线,但游戏一次都没调用被挂钩的函数,
   因此逐击 `critObserved` 与角色 `crit` 在全部导出里都是 0。
2. **原因是"零调用",不是"配对失败"**。这一点修正了 `SESSION-STATE.md` 阶段 G 中
   "失败在匹配不在接线"的判断:被挂钩的函数在整场战斗里**调用次数为 0**。
3. **有可用的折衷**:用伤害算术推断会心。最近 4 场真实战斗共 18,673 击:
   - **95.87% 的命中(92.19% 的伤害)可按确定规则判定**;
   - 3.37% 的命中(7.20% 的伤害)可按同攻击者倍率簇推断;
   - 仅 0.76% 的命中(0.60% 的伤害)无法判定。
4. 推断出的会心频率与面板会心率吻合(面板 40% → 实测 39.9% / 39.6%)。
5. 推断结果必须**单独成字段、携带判定方法、并显示覆盖率**,不得写进 `crit` / `critObserved`,
   不得冒充游戏实测标志。

## 2. 现状证据

### 2.1 现有通道(已接线)

| 环节 | 位置 | 状态 |
|---|---|---|
| 手动补丁 `DamageCalculater.GetFlyTextNumberSizeForAttack(float attributeRate, bool isCritical, bool defenseGreaterThanAttack)` | [Plugin.cs:188-205](<src/Plugin.cs#L188-L205>) | 已挂上,日志有 `crit/attribute-rate probe patch applied.` |
| Postfix 记录标志并计入探针 | [DamageCalculaterHooks.cs:121-134](<src/Hooks/DamageCalculaterHooks.cs#L121-L134>) | 每次调用会 `Probe.Hit("Calc.GetFlyTextNumberSizeForAttack")` |
| 按目标 + 时间窗取标志 | [CompositionProbe.Crit.cs:57-70](<src/Composition/CompositionProbe.Crit.cs#L57-L70>) | 三态:0 未观测 / 1 非会心 / 2 会心 |
| 写进逐击记录 | [Aggregator.Attribution.cs:227](<src/Aggregator.Attribution.cs#L227>) | 已接 |
| 角色计数 | [Aggregator.Stats.cs:184-194](<src/Aggregator.Stats.cs#L184-L194>) | 只在 `CritObserved == 2` 时累加 |
| 导出 | [ExportService.cs:393](<src/Output/ExportService.cs#L393>)、[444-448](<src/Output/ExportService.cs#L444-L448>) | 角色 `crit`、逐击 `crit` / `critObserved` |

链条文本侧同样有会心的展示位置([CompositionProbe.Crit.cs:74-87](<src/Composition/CompositionProbe.Crit.cs#L74-L87>)),
但因为它读的是同一个三态,同样一直为空。

### 2.2 实测一:函数从未被调用

`BepInEx/LogOutput.log`(DpsMeter 1.7.19,quest 411001)的探针汇总:

```text
Calc.ApplyEnchantDamage            x   5582  sum=  42831622
Calc.ApplyEnchantDamage(comp)      x   5582
Calc.ActDamageAction(noAttacker)   x     92
Calc.DamageAction(noAttacker)      x     92
Calc.ActDamageAction               x     27
Calc.DamageAction                  x     27
```

列表里**没有** `Calc.GetFlyTextNumberSizeForAttack`。`Probe.Hit` 只要被调用一次就会出现,
所以整场的调用次数是 **0**。同一场的 `[RECON]` 也写着 `会心实测=0/是=0`。
对日志全文检索该方法名,出现次数为 **0**。

### 2.3 实测二:导出里没有逐击会心

| 导出 | 版本 | 伤害事件 | `critObserved != 0` | `crit = true` | 汇总 `critInferred` |
|---|---|---:|---:|---:|---:|
| `battle_401004_20261006_110409__B-20261006-025754-C916794113964F30-002.json` | 1.7.17 | 3,710 | 0 | 0 | 767 |
| `battle_401004_20261006_110840__B-20261006-025754-C916794113964F30-003.json` | 1.7.17 | 3,766 | 0 | 0 | 768 |
| `battle_411001_20261006_121130__B-20261006-041130-7AA0C98126494171-001.json` | 1.7.18 | 5,614 | 0 | 0 | 72 |
| `battle_411001_20261006_140741__B-20261006-060741-6441AB90F1C942BA-001.json` | 1.7.19 | 5,583 | 0 | 0 | 64 |

已有的 `critInferred` 只是 [CalcReconcile.cs:68-77](<src/Output/CalcReconcile.cs#L68-L77>)
算出的**整场计数**,用于对账,**没有落到逐击、角色统计或悬浮窗**。
贡献模型也明确把会心伤害留在 `baseCredit` 里(`contrib/report_json.py`:188)。

### 2.4 为什么零调用(候选,离线无法区分)

| 候选 | 依据 | 判别方法 |
|---|---|---|
| 游戏设置关闭了伤害数字 | 游戏存在 `Rog.Domain.BattleOptionModel.DisplayFlyText` 选项;不显示伤害数字时尺寸函数可能根本不被调用 | 确认战斗中是否显示伤害数字 |
| IL2CPP 编译期内联 / 原生直接调用,绕过 detour | 该方法 private 且很小;补丁"挂上"却从未触发 | 开启伤害数字后仍为 0,即可基本归于此因 |
| 伤害数字走其他路径计算尺寸 | `FlyText.DataDamage` 构造函数自带 `eSizeType` 参数 | 同上,再看候选钩子是否有调用 |

**注意**:无论原因是哪一种,都**不能**照抄 1.0.48/1.0.49 那种"参数里带游戏对象"的补丁
(SESSION-STATE §7.2.37:挂在 detour 上就会在参数封送期崩溃)。

## 3. 能从数据推断出多少

### 3.1 依据

每击导出的 `calc` 已带有:`theory`(链条推算值)、`residual`(实际÷推算)、
`valueMatches`(是否与游戏值吻合)、`critRate` / `critDamageRate`(攻击方会心率 / 会心伤害率)。
会心倍率等于 `critDamageRate / 100`。

现在链条精确率已经很高(1.7.19 那场 96.4%),`residual` 基本只剩"是否会心"这一个大因子。
早期语料中"剩余倍率无法反推会心"的结论,是在链条精确率约 25% 时测得的,**不能直接套用到现在**。

### 3.2 分层判定(最近 4 场真实战斗,18,673 击)

| 层 | 规则 | 命中 | 伤害占比 |
|---|---|---:|---:|
| A 确定·不会心(会心率 0) | `critRate <= 0` | 13,179(70.58%) | 65.23% |
| A 确定·不会心(值相符) | `valueMatches` 或 `residual ≈ 1` | 3,055(16.36%) | 19.06% |
| A 确定·会心(值相符) | `residual ≈ critDamageRate/100`,容差 0.2% | 1,668(8.93%) | 7.90% |
| B 推断·会心 | `residual ≈ x × 会心倍率`,`x` 为同攻击者高频残差 | 312(1.67%) | 4.76% |
| B 推断·不会心 | `residual × 会心倍率 ≈` 同攻击者高频残差 | 317(1.70%) | 2.44% |
| C 无法判定·无构成 | 没有 `calc` | 113(0.61%) | 0.09% |
| C 无法判定·歧义/其他 | 两种解释都不成立,或同时成立 | 29(0.16%) | 0.51% |

合计:**A 95.87% / 92.19%;B 3.37% / 7.20%;C 0.76% / 0.60%**(命中占比 / 伤害占比)。

### 3.3 校准

| 场次 | 攻击方组 | 面板会心率 | 会心伤害率 | 判定会心 ÷ 该组命中 | 频率 |
|---|---|---:|---:|---:|---:|
| 401004 #002 | 主力输出 | 40 | 170 | 717 / 1,797 | **39.9%** |
| 401004 #003 | 同上 | 40 | 170 | 711 / 1,797 | **39.6%** |
| 411001 1.7.18 | 会心型单位 | 15 | 165 | 72 / 474 | **15.2%** |
| 411001 1.7.19 | 同上 | 15 | 165 | 64 / 467 | 13.7% |

另外,`critRate = 0` 的 13,179 击里符合会心形状的有 **0** 击,
说明"会心倍率"这条规则没有把其他倍率误判成会心。

面板会心率 47、会心伤害率 190 的一组,残差主要落在 `1.053` 与 `2.0 = 1.053 × 1.9`。
这说明存在一个未识别的 ×1.053 因子,会心叠在它之上 —— 这正是 B 层存在的原因;
一旦该因子被链条识别,这批命中会自动升入 A 层。

### 3.4 已知局限

- `critRate` 是攻击方面板值;受击方的"会心降低率"会压低实际概率。面板为 0 时"不会心"仍成立,
  但频率检验需要扣除受击方降低率。
- 保底伤害(`minRule`)按游戏规则不会心,应直接判为不会心。
- 带 `m_isIgnoreCritical` 的计算应直接判为不会心(链条文本已能读到"无视会心")。
- 若某未识别因子恰好等于会心倍率(例如 ×1.5 遇上会心伤害率 150),A 层会误判。
  `critRate = 0` 时没有这种风险;`critRate > 0` 时只能靠校准频率发现异常。
- 被吸收的命中:`residual` 基于吸收前的量,吸收不影响判定;若日后改口径需复核。

## 4. 折衷方案(推荐路线:先离线、后插件,推断与实测分开存放)

### C1 离线推断器(不改插件,风险最低)

- 在 `contrib/` 新增纯函数模块(建议 `crit_infer.py`)。输入一份导出,输出逐击判定:

  ```json
  {"crit": "yes|no|unknown",
   "method": "rate0|ignore|minRule|exact-noncrit|exact-crit|cluster-crit|cluster-noncrit|no-calc|ambiguous"}
  ```

- 输出角色汇总:可判命中数、覆盖率、会心次数、会心伤害、非会心伤害,以及实测频率与面板会心率的对照。
- **现有导出可直接重算,不需要新的实机战斗。**
- 容差沿用 `CalcReconcile.CritInferredTolerance = 0.002`,不另造常数。
- 要求:新 `.py` 在 `_dpsm_work/tool_registry.json` 登记;至少一个变异负控
  (例如把容差放大到 0.2,应造成"会心率 0 组出现会心");用本报告 §3.2 的数字做回归钉值。

### C2 插件内逐击推断(C1 通过后再做)

- `BattleEvent` 增加独立字段 `CritInferred`(0 未判 / 1 推断不会心 / 2 推断会心)与 `CritMethod`。
- 判定逻辑移植 C1,并与 C1 逐字段交叉验证(参照 `contrib.crosscheck` 的做法)。
- **不得**写入 `crit` / `critObserved`;这两个字段保持"游戏实测"语义。
- `ActorStats` 增加推断口径:`CritInferredCount`、`CritInferredDamage`、`CritJudged`(可判命中数);
  现有 `CritCount` 保持实测口径不变。
- 导出只**加**不改;同步更新 `CONTRIBUTION-DATA-DICTIONARY.md`。

### C3 悬浮窗显示

- 角色明细显示,例如:

  ```text
  会心(推断) 712/1,794 · 39.7% · 面板 40%
  ```

  覆盖率不足时补充 `可判 82%`。
- 逐击明细在链条行追加:

  ```text
  会心:是(推断·值相符)
  会心:否(会心率 0)
  ```

- 文案必须带"推断",与实测文案"会心:是"明确区分。
- **不改变 `log-share/1` 贡献口径**:会心伤害仍归 `baseCredit`。
  把会心拆成虚拟因子属于未来的模型变更,不在本方案范围内。

### C4 修复实测通道(可选,需要用户一场普通实机日志)

- **只请求、不塞进自动化**:用户下次正常游戏时说明"战斗中是否显示伤害数字",并保留那一场的 `LogOutput.log`。
- 若显示伤害数字时探针仍为 0,可评估替代钩子。所有替代钩子都按 `TryPatchCritProbe` 的隔离方式
  单独手动挂载、默认关闭、失败不影响计量器:

| 候选 | 能拿到 | 风险 |
|---|---|---|
| `FlyText.DataDamage..ctor(eType, eAnimType, eSizeType, string, eDamageCalcType)` | 尺寸档位;尺寸可能同时编码会心与属性优势,映射需实测 | 参数含 `string`,有一层封送;日志已出现部分 `DamageCalculater` 构造函数补丁初始化失败,构造函数补丁可能根本不可用 |
| `FlyText.Creator.Create(DataBase, Action)` | 同上(经对象读取) | 需封送 Il2Cpp 对象指针,与 1.0.48/49 崩溃同类风险,**不推荐** |
| `TalentDefine.TriggerTiming.Critical` 素质触发 | 只覆盖带"会心时发动"素质的单位 | 覆盖不全,只能做抽样校验 |

- 实测通道一旦可用,`CalcReconcile` 现有的 `critAgree` / `critInferredButDenied` /
  `critObservedButUnexplained` 会直接给出推断与实测的一致率,届时再决定悬浮窗是否切换到实测口径。
- **禁止**参照 1.0.48/1.0.49 那类"参数里带游戏对象"的补丁。

### 4.5 不推荐的做法

- 把推断写进 `crit` 或 `critObserved`(会让"游戏说的"和"我们算的"无法区分)。
- 用"会心率 × 命中数"估计会心次数(那是期望值,不是逐击事实,也无法用于单场比较)。
- 把 C 层当作"不会心"(未知就是未知,应单独计数并显示覆盖率)。

## 5. 分工与验收

| 任务 | 依赖 | 验收标准 |
|---|---|---|
| C1 离线推断器 | 无 | 4 场回归钉值与 §3.2 一致;会心率 0 组误判 = 0;负控能变红;工具注册表通过 |
| C2 插件逐击推断 | C1 | 构建 0 警告 0 错误;BehaviorTests 新增用例;C1/C2 逐击一致率 100%;旧字段语义不变 |
| C3 悬浮窗显示 | C2 | 推断与实测文案可区分;显示覆盖率;历史页显示所选战斗,不串场 |
| C4 实测通道 | 用户一场普通实机日志 | 探针出现调用计数;补丁失败时计量器不受影响;一致率进入 `[RECON]` |

每项按 `AGENTS.md` §2 的工作循环执行(构建 → 行为测试 → 具名变异负控 → 全量负控 → 文档与守卫 → n0 验收 → 提交)。
