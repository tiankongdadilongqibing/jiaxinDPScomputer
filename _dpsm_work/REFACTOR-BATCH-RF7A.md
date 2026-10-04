# 重构批次记录 RF7a(第 8 轮):工具注册表 + 漂移守卫

> 依据:[REFACTOR-PLAN-POST-1.7.11.md](<REFACTOR-PLAN-POST-1.7.11.md>) §12(RF7):**先写工具注册表**,标明活跃 CLI、导入关系、
> 输出副作用、预期退出码、输入版本与历史证据 —— 在移动或删除任何工具**之前**。本轮不做删除,只建立登记与校验。
> 本轮**未触碰任何插件源码**。

## 0. 结论

| 项 | 状态 |
|---|---|
| `tool_census.py`(机械半:扫描 + 合并) | ✅ 95 个脚本 |
| `check_tool_registry.py`(守卫:A–F 六项 + 8 例自测) | ✅ |
| `tool_registry.json` / [TOOL-REGISTRY.md](<TOOL-REGISTRY.md>) | ✅ 95 条登记,21 条已判定为活跃,74 条未判定(上限已钉) |
| 进入验收流水线 | ✅ 新增 2 条命令(tool_registry / selftest/tool_registry) |
| 删除任何工具 | ❌ 未做(方案要求本轮只登记) |
| 部署 DLL | 未改(本轮不碰插件):1.7.11 / 387,072 B / `36EC96D4…` |

## 1. 两个工具

| 文件 | 角色 | 关键行为 |
|---|---|---|
| [`tool_census.py`](<tool_census.py>) | 机械半 | 扫描仓库里的 .py(跳过 obj/bin/acceptance/batch_inputs),从文件本身取出:行数、模块 docstring 首行、对其他仓库脚本的导入、是否出现在验收流水线、是否有 --selftest;--seed 合并进注册表 |
| [`check_tool_registry.py`](<check_tool_registry.py>) | 守卫 | --verify(默认)A–F;--write 由注册表生成 TOOL-REGISTRY.md;--selftest 对每一项做篡改对照 |

**为什么拆成机械 + 人工两半**:imports_local、in_pipeline、has_selftest、行数、首行说明都能从文件里读出来,
不该靠人记;而类别 / 是否活跃 / 预期退出码 / 输出 / 输入版本 / 为什么存在是判断,必须人写。
--seed 只刷新机械字段,永不覆盖判断字段。

## 2. 六项检查 + 八例篡改对照

| 码 | 检查 | 篡改对照(必须红) |
|---|---|---|
| A | 注册表里的路径必须在磁盘上 | 加一条不存在的路径 |
| B | 扫描到的脚本必须已登记 | 删掉某个脚本的条目 |
| C | 流水线真正运行的脚本必须登记且 status=active | 把它改成 unclassified;以及整条删除 |
| D | 声称 in_pipeline 的必须真在流水线里 | 把非流水线脚本标成 in_pipeline |
| E | 活跃条目必须声明 kind / purpose / expected_exit / outputs | 各清空一次 |
| F | 未判定计数不得超过上限 | 把上限压到 0 |

F 是方案只登记、不删除的可执行形式:所有脚本都在册(没有一个能藏起来),但没判定过的被计数,
且只能下降。加一个脚本而不同时判定它,构建就变红。

## 3. 数字与守卫同步

流水线从 33 条命令 / 65 条检查 变成 35 条命令 / 67 条检查。第一次跑验收时,doc_convergence 的 R9 立刻红了,
指出索引仍在说 33/65 —— 这正是那条规则存在的意义。同步后各轮终轮仍为 0 项(RF2/RF3 时代为 65 项检查,RF7a 起为 67)。

## 4. 守卫在自己的第一次批次运行里抓到了漂移(实测)

第一次 acceptance_rf7a 不是全绿:run_exit/tool_registry expected 0 observed 1,日志写着:

    tool registry: registered=95 active=21 unclassified=74 (pin 74) pipeline=20
      FAIL C the pipeline runs this but in_pipeline is not set: _dpsm_work/check_tool_registry.py
    ---- tool registry: FAIL 1

原因:我把守卫接进流水线时,它的 in_pipeline 还是上一次扫描的 false —— 守卫第一次运行就抓到了清单与现实不一致,
而且抓的正是它自己的条目。修法是重跑 --seed(只刷新机械字段)。
这也说明为什么 in_pipeline 必须来自扫描而不是手写:手写的清单会漂移,扫描不会。

## 5. 验证与回滚

| 项 | 值 |
|---|---|
| 构建 | 未改插件源码(0 警 0 错,src 未变) |
| 行为套件 | cases=452 failed=0 pinned=452(未变) |
| 变异负控 | 52 例 0 失败(未变) |
| 新守卫 | check_tool_registry.py PASS + --selftest 8/8 PASS |
| 验收 | acceptance_rf7a:35 条命令 / 67 条检查(见该目录 RESULTS.md) |
| 回滚 | git revert:本轮只新增文件(2 个工具 + 注册表 + 生成的报告),n0 增 2 行运行项;插件源码与行为零变化 |

## 6. 未做 / 后续

1. 74 条未判定:它们是历史一次性脚本与早期普查工具;本轮只登记(方案明确不做删除)。判定的价值在于
   谁还被谁依赖,而 imports_local 已经把依赖关系机械地记下来了;
2. 依赖普查的更细一层(谁读哪个导出目录、哪个环境变量、哪个固定输出路径)没有做:那需要按文件读代码,
   属判定半,建议与归档策略一起做;
3. 没有把 n0 的运行清单改成从注册表读取:那样才是真正的单一来源,但会改动一个已验收的脚本结构;
   现在的做法是守卫交叉检查两边一致,风险更低,且已经在本轮抓到一次真实漂移;
4. RF7 的后续阶段(归档/删除)按方案要求留到工具表稳定之后。

