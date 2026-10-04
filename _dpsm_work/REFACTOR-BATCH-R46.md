# 重构批次记录 R46:MasterDataAccess 落地(第一阶段:标签路由)

> 第 45 轮把重复点定到两个具名方法。本轮**开始搬**:新建 MasterDataAccess,把共享算法搬进去,
> 并把**标签路由**改为调用它。**转储路由仍是内联副本**(第二阶段),所以此刻树里重复尚未消失。

## 0. 本轮做了什么

| 项 | 值 |
|---|---|
| 新文件 | src/MasterData/MasterDataAccess.cs(FindBest + Rows + BestInstance 结构) |
| 改动 | MasterDataNames.cs:两个私有方法改为**薄包装**,调用 MasterDataAccess;**计数器保持原样** |
| 行数 | MasterDataNames.cs 338 -> 321(-17,两个方法体换成包装) |
| 构建 | **0 警 0 错** |
| 未做 | 转储路由(第二阶段);逐键对照(第三阶段) |
| **部署 DLL** | 未改:1.7.11 / 387,072 B / 36EC96D4… |

## 1. 为什么"计数器"要留在调用方

两条路由统计的东西不同:标签路由记 _instErrors/_duplicateInstances/_errors,转储记 _tablesMissing/PerTable。
把计数搬进共享层就会**改变两侧行为**(并且抹掉这些数字的含义),所以 MasterDataAccess 只**报告**
(Found/BestCount/CastErrors/Failed),由调用方决定怎么记。标签路由的包装因此是:

- _instErrors += info.CastErrors;
- if (info.Failed) _errors++;
- if (info.Found > 1) { _duplicateInstances++; Note(同一条消息); }

## 2. 搬走的三条知识(注释随之搬走)

1. **非泛型 AOT 安全查找**(Resources.FindObjectsOfTypeAll(Il2CppType.Of<TTable>());泛型重载会运行时失败);
2. **扫全部实例、取行数最多者**(实测:能力表曾 9 个实例、稀有度 2 个;取 [0] 会让 1.2.0 把 28 行当成全部);
3. **逐元素 try/catch,失败只计数不抛出**(一个坏实例不得让整张表消失)。

## 3. 本轮的验证边界(必须说清)

- **行为套件无法验证这次改动**:BehaviorTests 只编译 Model/Output/Policy/Runtime/Ui/Diagnostics,不含 MasterData/*;
- 因此本阶段的证据是**构建通过 + 逐一对照源码**:搬走的算法与原实现逐行相同,包装只把计数换成了 info 字段读取;
- 真正的行为验证是**第三阶段的逐键对照**(以 r42-masterdata 基准),那一步做完之前,**不宣称等价**。

## 4. 下一阶段

1. 把转储路由的内联循环改为调用 MasterDataAccess(其 _tablesMissing/PerTable 统计原样保留);
2. 以 _dpsm_work/batch_inputs/r42-masterdata 的**全部键**做对照输入,逐键逐字段比较两路输出;
3. 差异为 0 才提交;有差异就地回退并记录;
4. 不替换运行中的 DLL。

## 5. 回滚

git revert:新文件 + MasterDataNames 两处包装;转储路由尚未改动。
