# 重构批次记录 R44:把活的 masterdata 冻结成对照基准(抽取前的最后一步)

> 上一轮确认 r42 快照只含**导出**(46 个战斗文件,扁平存放),**不含 masterdata**;
> 而 MasterDataAccess 的对照基准需要 masterdata。本轮把活目录里的转储**复制**出来冻结,
> 使抽取轮不再需要读活目录。**只读 + 复制,源目录未被修改。**

## 0. 结果

| 项 | 值 |
|---|---|
| 源 | BepInEx/plugins/DpsMeter/masterdata/*.json(插件自己写出的转储) |
| 冻结到 | _dpsm_work/batch_inputs/r42-masterdata/ |
| 清单 | _dpsm_work/masterdata-baseline-r42.json(每份的字节数与 SHA256) |
| 规模 | **20 份 / 410,594 字节** |
| 源目录 | **未修改**(只 copyfile) |
| **部署 DLL** | 未改:1.7.11 / 387,072 B / 36EC96D4… |

代表性的几份:ability.json(105,978)、awake_potential.json(86,908)、artifact.json(73,000)、
engraving_ability.json(17,522)、battle_define.json(1,293)、_table_registry.json(5,915)。

## 1. 为什么先冻结再抽取

1. **可复核**:清单带 SHA256,以后任何对照都针对同一份字节,而不是"当时的活目录";
2. **不再需要读活目录**:抽取轮只读 _dpsm_work 内的副本,权限问题消失;
3. **可回退**:副本与清单都在仓库工作区内,删掉即回退,活目录从未被写。

## 2. 下一轮的执行顺序(不变)

1. 按 R43 §3 抽出 MasterDataAccess(两处共享的泛型枚举 + 解密 + 判空);
2. 用本基准的**每张表全部键**做对照输入,逐键逐字段比较;
3. 差异必须为 0 才提交;有差异就地回退并写进记录;
4. 全程不替换运行中的 DLL。

## 3. 验证与回滚

| 项 | 值 |
|---|---|
| 冻结 | 20 份 / 410,594 字节;清单已写出 |
| 插件源码 | **零变化** |
| 回滚 | 删除 _dpsm_work/batch_inputs/r42-masterdata/ 与 _dpsm_work/masterdata-baseline-r42.json |
