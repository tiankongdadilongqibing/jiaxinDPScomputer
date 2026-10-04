# 重构批次记录 RF6a(第 14 轮):主数据层的两处重复 —— 先定位,再抽可判定的那一半

> 依据:[REFACTOR-PLAN-POST-1.7.11.md](<REFACTOR-PLAN-POST-1.7.11.md>) §11(RF6)。该节的第一句就是要求:
> "主数据查找的**两处重复**目前是状态资料中的线索,**执行者先定位调用方并读全上下文**,再提取 MasterDataAccess"。
> 本轮完成这次定位,并把其中**可离线判定**的一半抽出来。

## 0. 结论

| 项 | 状态 |
|---|---|
| "两处重复"的定位与逐项对照 | ✅ 见 §1(两条路由都在,共享最微妙的部分) |
| 表路由抽取(`MasterDataAccess`) | ⏸ **本轮不做**,理由见 §2(IL2CPP 泛型 → 无法离线编译;方案对"新增真实读取路径"的验收是实机) |
| `Policy/MasterDataLabelPolicy.cs`(103 行,纯) | ✅ 官方名**选择规则**与签名构造 |
| `MasterDataNames` 接线 | ✅ 私有 `Cand` 类删除,改用具名 `Candidate`;规则改调策略 |
| 新增覆盖 | ✅ 24 个用例(**用的是文件里记录的实测夹具**)+ 4 例变异负控 |
| **部署 DLL** | **仍未改**:1.7.11 / 387,072 B / `36EC96D4…` |

## 1. 定位结果:那"两处"是什么

| | `MasterDataNames.FindBest/Rows`(刻印官方名) | `MasterDataDump.Table`(转储) |
|---|---|---|
| 实例发现 | `Resources.FindObjectsOfTypeAll(Il2CppType.Of<TTable>())` | **同一路** |
| 为什么不用泛型重载 | 源码注释给出 IL2CPP AOT 的理由(泛型 `FindObjectsOfTypeAll<T>` 的 interop 体用 `MakeGenericMethod`,镜像里没有该实例化) | **同一段理由**(`MasterDataDump` 写明,`MasterDataNames` 引用它) |
| 多实例 | 取行数最多的,计数 `_duplicateInstances` + 记一条 Note | **同一策略**(注释:盲目取 [0] 曾把 28 行的能力表当成全表) |
| 缺表 | 返回 `null` | `_tablesMissing++` + Note |
| 行提取 | `Values.CopyTo(new Il2CppReferenceArray<TRow>(n), 0)` 并跳过 `null` | 同一路,但**逐行交给回调**写 JSON |
| 失败计数 | `_errors` / `_instErrors` | `_tablesMissing` / … |

**结论**:最微妙、最容易写错的部分(**非泛型查找 + 多实例取大 + 失败分类**)确实被抄了两遍,方案说的是对的。
差别在于"拿到行之后干什么"(收集 vs 回调写 JSON)与失败计数口径。

## 2. 为什么本轮不抽表路由(诚实理由)

抽取 `MasterDataAccess` 的签名必须写成 `where TTable : MasterTableBase where TRow : Il2CppObjectBase`,
内部还要 `Il2CppType.Of<T>()` / `Il2CppReferenceArray<T>` —— 这些类型**不在离线行为套件的编译面里**,
所以这次抽取**拿不到任何离线测试**,只能靠构建 + 差异审查 + 实机转储输出。
方案自己也这样定 RF6 的验收:"新增真实运行读取路径须正常游玩验证"。

本轮把**对照表**留下(§1),下一轮做这件事时不必重新读一遍两个文件;
而先把"能判定的那一半"抽走,让主数据层第一次拥有离线覆盖。

## 3. 抽了什么:官方名规则

`MasterDataNames` 记录的核心问题:**刻印槽混用两套 id 空间**(1..14 是基础刻印,1..28 是刻印变异,1..14 撞车),
所以"只按 id 命名"会给出**自信的错误答案**(实测:能力 id 14 官方是【虚突】,而行里其实是变异 14)。规则是:

1. 取同 id 的 master 行;
2. 只留 **talent-id 签名**与实测一致的;
3. 若幸存者只有一个非空名字 → 用它(基础刻印的 5 条等级行共享同一签名与同一名字,所以名字无歧义);
4. 否则**什么都不标**,只计数(猜出来的官方名与解析出来的无法区分,正是项目禁止的失败模式)。

第 1–4 步现在在 `MasterDataLabelPolicy`(纯),调用方只负责计数与原生读取。

## 4. 用例:实测夹具,不是构造的

| 夹具(来自源码文档的实测) | 断言 |
|---|---|
| id 1 / level 1 同时是【破壊】の刻印(talent 6)与变异 魔法与ダメージ+10%(talent 1005) | 签名 `6` → 基础名;签名 `1005` → 变异名;签名 `7` → **不标**,计数 NoCandidate |
| 基础刻印的 5 条等级行共享签名与名字 | 解析成功,用那个共享名字 |
| 同一签名下两个不同名字 | **不标**,计数 Ambiguous |
| 唯一命中但名字为空 | 计为 **Ambiguous**(不是 NoCandidate)—— 行存在,只是叫不出名字 |
| 无候选 / null 列表 | NoCandidate,`label` 保持 `null` |
| 签名 | 空集为 `""`;排序逗号连接;**顺序无关**;**重复保留**(两份同一 talent 与一份不同) |

## 5. 验证与回滚

| 项 | 值 |
|---|---|
| 构建 | 0 警 0 错;**未部署** |
| 行为套件 | `cases=591 failed=0 pinned=591`(20 组) |
| 变异负控 | 80 例(新增 4 例:忽略签名、签名不排序、空名算成 NoCandidate、签名用 `-` 连接) |
| 守卫 | `refactor_final_check` 108 个 .cs blocks=0;文档收敛 0/11;docs123 49 份 0 损伤;工具注册表 PASS |
| 验收 | `acceptance_rf6a` |
| 回滚 | `git revert`:新策略文件 + `MasterDataNames` 改调用(删除私有 `Cand`);行为不变 |

## 6. 未做

1. **`MasterDataAccess` 表路由**(§2):已定位、已逐项对照,但需在有实机验证机会时做;
2. **导出边界**(方案 §11 后半:按段拆 writer / 保留 JsonCheck / 不改字段顺序与精度)未动;
3. `MasterDataDump` 的转储输出本身没有被离线校验(它需要游戏里已加载的表)。
