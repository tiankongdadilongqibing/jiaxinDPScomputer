# 重构批次记录 R57:把战斗引用绑在**持久文件**上(实机发现)

> 触发:你复制回来的一段引用里 `file: battle.json`、`sha256: db20bcec…`,问"能定位到这一场吗"。
> 答案是**能**(见 §1),但那段文字暴露了 R56 的一个真缺陷(§2)。
> 插件版本 **1.7.11**;本轮部署 **C1DBBD8F…**(433,664 字节)。

## 0. 结论

1. **引用可定位,并且我用工具在真实数据上验证了**:`battle_select.py resolve` 把
   `B-20261005-084431-C090E1C550B84C10-001` 解析到**唯一一个文件**
   `battle_9999_20261005_164431__B-20261005-084431-C090E1C550B84C10-001.json`,报告的 SHA256 与复制文本里的
   `db20bcec…` **逐字相同**。
2. **但 `file:` 那一行指向了会被删掉的副本**:`MarkExported` 原来"记住最后一次写盘",而 battle-end 的
   证据包是在普通导出**之后**写的,于是身份被绑到了 `extract/<bundle>/battle.json` —— 而 `ExtractKeep`=5 的
   保留策略**已经把 -001 那个包删掉了**。这正是方案 §5.1 要求的"实际目标路径首次确定后绑定会话"。已修:**首次
   成功写盘绑定路径,之后只有写同一个路径才更新哈希**,证据副本再也不能移动身份。
3. 本轮**不改任何数字、不改导出的 battleRef 结构**;只改"身份记住哪个文件"这一条规则。

## 1. 实机证据(这是一次真实的端到端确认)

你这一批是**同一次启动**的 6 场训练场(9999)会话,命名空间 `20261005-084431-C090E1C550B84C10`:

| 复制回来的引用 | 结论 |
|---|---|
| `battleId: B-20261005-084431-C090E1C550B84C10-001` | 解析到 `battle_9999_20261005_164431__…-001.json`(859,945 B) |
| `quest: 9999` / `state: provisional` | 与导出正文一致:该场 11.80 s、`result=None`、`closeReason=end` —— **没有胜负的收尾只能是 provisional**,契约如此 |
| `sha256: db20bcec…` | 与该导出文件的实测 SHA256 **相同** |
| `resetCount: 0` / `revision: 1` | 与正文一致 |

```
$ python battle_select.py resolve --exports <live> --ids B-20261005-084431-C090E1C550B84C10-001 --out sel.json
  [1] B-20261005-084431-C090E1C550B84C10-001 -> battle_9999_20261005_164431__B-…-001.json (rev=1 state=provisional sha=db20bcec6151)
battle_select resolve: 1 item(s) selected            (exit 0)
$ python battle_select.py compare --selection sel.json
  UNFINISHED battle_9999_…-001.json state=provisional
battle_select compare: REFUSED (… pass --allow-unfinished …)   (exit 5)
```

另外两点实机事实:
- **同一命名空间内的 6 场都各有独立编号**(001…006),文件互不覆盖;
- **保留策略确实在轮转**:6 场产生了 6 个证据包,盘上只剩 5 个;-001 的那个已被删 —— 所以一个指向
  `extract/` 的"文件路径"是不可靠的,**本轮修的就是这一点**。
- 编号里的时间是 **UTC**(08:44:31),文件名里的时间是**本地时间**(16:44:31):同一时刻,设计如此
  (编号不随机器时区变化)。这不是错位。

## 2. 改动

| 位置 | 改动 |
|---|---|
| `src/Runtime/BattleRefRegistry.cs` | `MarkExported`:路径**首次写盘绑定**,之后只有 `path` 相同才更新哈希;注释里写下这次实机的代价(包被保留策略删掉) |
| `src/Output/ExportService.cs` | 写盘日志分两种:持久导出仍是 `Exported full battle data -> …`,显式目标的**副本**改为 `Exported COPY (explicit target, same serializer) -> …` —— 否则运行日志会把"证据副本"说成"导出",而副本会被轮转删掉 |
| `check_live_log.py` | `extract/<bundle>/battle.json` 路径分类为**按设计轮转的副本**(记一条 note),不再报"exported but the file is gone (truncated corpus?)"。这条分支同时让**已经写进日志的历史行**变得可解释(不能只靠新日志格式) |

行为用例 **950 例**(+3):"同一路径重写会更新哈希""后写的副本不能移动身份""也不能借用身份的哈希"。
变异 **131 例**(+1:`bref-export-rebinds-on-every-copy`)。

## 3. 验证

| 闸门 | 结果 |
|---|---|
| `dotnet build src/DpsMeter.csproj -c Release` | **0 警告 0 错误** |
| BehaviorTests | **950 用例 / 98 组 / 0 失败** |
| 变异负控(新增 1 例单独跑) | 红在具名用例 `a-later-COPY-can-not-move-the-identity` |
| 变异负控(全量) | **131 例 / 0 失败** |
| 真实数据 | `resolve` 命中唯一文件、哈希逐字相同;`compare` 对 provisional 正确拒绝(exit 5) |
| `check_live_log.py`(真实日志) | 修复前 **exit 1**(把轮转掉的证据副本报成"语料被截断");修复后 **exit 0**,并如实记 `6 exported battle(s)` + `6 evidence-bundle copy path(s) skipped`;其 `--selftest` 仍 PASS |
| `n0_acceptance --out acceptance_r57` | **44 条命令 / 76 条检查 / 0 项** |
| 文档收敛 / docs123 / 工具注册表 / 清单 | 收敛 **12/12**;docs123 **93 文件 / 0 损坏**;工具注册表 **100 / 31 / 67 / 2 / 0**(pipeline 25);清单 **drift=0** |

## 4. 部署与回退

- 部署:**F6948470**(R56)→ **0E920269**(绑定修复)→ **C1DBBD8F**(日志区分 + 守卫分类,433,664 B);
  两份备份 `deploy-backup/pre-r57-F6948470/`(R56 那版,回到本轮之前)与 `pre-r57b-0E920269/`(绑定修复版)。
- 复核两个永不改动的崩溃现场哈希不变:`2E1819F2…` / `F7FF1EB8…`。
- 回退:`Copy-Item _dpsm_work/deploy-backup/pre-r57-F6948470/DpsMeter.dll BepInEx/plugins/DpsMeter/DpsMeter.dll -Force`。

## 5. 未做 / 注意

- **已经写出去的旧引用不会改变**:-001 那段文字里 `file: battle.json` 已经发出去了;它的 `sha256` 是正确的,
  所以按编号(或按那个哈希)仍能定位到同一个文件。新构建之后复制的引用会写**导出的文件名**。
- **provisional 不是缺陷**:训练场/波次那种"没有胜负就结束"的会话按契约只能是 provisional,
  `compare` 默认拒绝它是设计(要诊断就 `--allow-unfinished`,并且 9999 本身也不参与可比基线)。
- 训练场的 6 场都在 `q=9999`:按既有准入,它们**不进入跨场比较**(R6)。
- **日志/守卫这条是本轮第二个实机发现**:运行日志是唯一的"机器侧陈述",它把证据副本也说成"导出"时,
  守卫就有理由认为语料被截断。两处都改了(新日志不再含糊;守卫对历史行做分类),没有放宽对**真正**缺文件的报错。
