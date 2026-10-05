# 证据提取流程(EXTRACTION-FLOW,R52)

> 这份文档回答一个问题:**"下一场战斗结束后,我要怎么把'这场到底发生了什么'一次性拿到手,而且以后还能复算?"**
> 它描述的是插件里已经实现、并有离线校验器的流程;未做实机验证的部分在 §7 里**明说**。
> 插件版本 **1.7.11**;流程自 R52 起可用,当前部署 `AA836C06…`(416,256 字节)。

## 0. 一句话

插件现在有一个**按需/可自动**的"证据包"产物:一次触发写出一个自包含目录 —— 战斗导出 + **未归因折叠普查**
+ 游戏自身主数据副本 + 逐文件校验清单(manifest),离线 `extract_verify.py` 负责证明这个目录没有被改过、
且它的普查与"用同一份 battle.json 独立重算"的结果逐字段一致。

## 1. 触发方式(两条,互不影响)

| 方式 | 配置 | 默认 | 说明 |
|---|---|---|---|
| 战斗结束时自动 | `General/ExtractOnBattleEnd` | **false** | 一场一包;一个包 ≈ 25 MB(含导出副本),所以默认关,需要时显式打开 |
| 按一个键 | `General/ExtractKey` | **F4** | 战斗中或战斗后都能按;可填 `F1..F12` / `A..Z` / `0..9`,`NONE` 关闭 |

* 按键走的是界面层已有的 `GetAsyncKeyState` 轮询,且**在面板显示判断之前**处理,所以面板隐藏时也能按。
* 键可选是因为 F5–F12 已被界面占用、而游戏自身还占用一批未知键;冲突了改配置即可,不需要重新编译。
* 战斗结束那条路径挂在收尾流程里,**在导出之后**,所以包里的 `battle.json` 与 `exports/` 下的那一份描述同一个已结束会话。

## 2. 产物

    BepInEx/plugins/DpsMeter/extract/
      extract_<yyyyMMdd_HHmmss>_q<quest>_<reason>/
        battle.json          与正常导出同一个序列化器(ExportService.ExportTo),不是第二份实现
        contrib_census.json  未归因折叠普查(见 §3)
        masterdata/*.json    游戏自身主数据转储的**副本**(没有转储过就不生成,并在 manifest 里如实写 0)
        manifest.json        文件清单 + 校验 + 部署程序集 + 普查摘要(见 §4)

`reason` 只有两个值:`battle-end` / `hotkey`;目录名按时间戳排序,保留策略因此是一次普通字符串排序。

## 3. contrib_census.json:它比导出多说了什么

导出里只有 `contribution.unattributed`(按理由聚合)与 `contribution.diagnostics.reasonCounts`。
普查文件把**未归因的那些折叠分组**列出来,每组带:

| 字段 | 含义 |
|---|---|
| `reason` / `kind` / `side` / `origin` / `label` / `ruleName` / `factor` | 这一组的身份(同一条规则的多个副本会按 origin 分开) |
| `folds` / `amount` | 组内折叠数与金额(log-share 份额之和) |
| `victimInstances` / `victimTop` / `victimTopFolds` | 受击方**实例数**(不是同名数量)与出现最多的那个名字 |
| `carrierVerdict` / `carrierCount` / `carrierNames` | 仅"赋予"通道(`kind="given"`):名册里**持有**能授予该 `(type,param)` 的规则的单位。`unique` = 唯一候选;`ambiguous` = 多个候选(**仍然不归因**);`none` = 这条线索也没找到 |

**它不改任何分数**。这是刻意的:普查是报告,归属是决定。把它接进归属会移动单位之间的信用,
那是一个需要单独批准的决定(见 `_dpsm_work/REPORT-未识别规则种类-R52.md` §4)。

普查由 `Contribution.Compute` 的**同一个循环**顺带填出(`ContributionResult.Unresolved`),所以普查与导出段**不可能互相矛盾**。

## 4. manifest.json 与"校验"这个词

| 字段 | 含义 |
|---|---|
| `schema` / `check` / `reason` / `createdAt` / `pluginVersion` / `quest` / `seconds` | 这份包是谁、什么时候、哪一场写的 |
| `bundle` | 目录名 |
| `assembly` | 部署的 `DpsMeter.dll` 路径 + 字节数 + 校验值(拿不到路径就写 `null`) |
| `files[]` | 每个文件的 `name` / `bytes` / `check`(缺文件时 `missing:true`) |
| `masterdata` | 复制了多少个文件、字节数、失败数、来源目录 |
| `census` | 普查摘要:组数、折叠数、金额、候选(唯一/歧义/无)计数 |
| `notes[]` | 这份包**不该被误读**的五句话(见下) |

`check` 固定是 **FNV-1a 64**,并且算法名写在 manifest 里。**这不是密码学摘要**,如此命名的理由:
这个插件从来没有链接过 `System.Security.Cryptography`,在 IL2CPP 里用一个可能被裁剪掉的加密实现、
还是在收尾/渲染路径上,是拿一个"可能不存在"的保证换一个"确实能用"的校验。名字写清楚就不会有人读错。

`notes[]` 里那几句就是纪律本身,例如"battle.json 与正常导出是同一个序列化器"、
"普查与导出段由同一个核心算出"、"carrierVerdict 是名册推断,不是运行时实测"。

## 5. 保留策略

`General/ExtractKeep`(默认 **5**,范围 1..50)。删除决定是纯函数 `ExtractPolicy.StaleBundles`:
按目录名排序、**保留最新的 N 个**、删掉更早的。所以按键连按不会把磁盘填满,也不会把最新的那份删掉。

## 6. 离线两件套

| 工具 | 命令 | 退出码 | 做什么 |
|---|---|---|---|
| `attribution_census.py` | `python _dpsm_work/attribution_census.py [--export PATH] [--json OUT] [--selftest]` | 0 一致 / 1 不一致或输入不可用 / 3 没有 contribution 段 | 用被核对的离线核心重算未归因拆分,与插件自述**逐字段**对比;打印未归因分组与候选判定 |
| `extract_verify.py` | `python _dpsm_work/extract_verify.py [--bundle DIR] [--selftest]` | 0 通过(可有 WARNING)/ 1 不一致或损坏 / 3 缺必需文件 | 校验 manifest 的每个大小/校验值;把普查文件与"重算同一份 battle.json"逐组对比;报告部署程序集是否仍是包里那一个 |

两者都只输出 ASCII 到控制台(控制台是 GBK),人读的报告写成 UTF-8。`extract_verify.py` 对 24 MB 的
`battle.json` 做逐字节 FNV,所以一次调用大约十几秒 —— 这是"真的校验了"的价格。

WARNING 与 ERROR 的区别是刻意的:包里的程序集哈希与当前部署的 `DpsMeter.dll` 不一致时,包**仍然有效**
(它描述的是当时的构建),但必须标出来,免得被当成"现在这份构建的产物"。

## 7. 已知边界(诚实清单)

* **实机验证待办**:本流程的插件侧代码已构建、行为用例与变异负控都已通过,但"真的产生一个包、
  再用 `extract_verify.py` 验过"需要**实际打一场**(收尾路径)或按一次键(需要你在游戏里按)。用户跑过之后本节会更新。
* `carrierVerdict` 是**名册推断**(持有这条规则 ≠ 此刻一定在阻挡该敌人),它不是运行时实测;
  真正"谁给的"仍需要一条可读的运行时证据。
* 包里**不含** `LogOutput.log` / `DpsMeter-runtime.log`:日志可能很大,且导出已经携带了自检行。
  需要日志时请另外附上,`extract_verify.py` 不会因此失败。
* 主数据是**复制**收尾时刻 `masterdata/` 目录里已有的那份转储,不是重新转储;没转储过就是 0 个文件,manifest 如实写。
* 关闭方式:`ExtractOnBattleEnd=false` + `ExtractKey=NONE`(或删掉 `extract/` 目录,互不影响导出与统计)。
