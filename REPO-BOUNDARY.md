# 仓库边界与基线(RF0)

> 状态:生效中。来源:[REFACTOR-PLAN-POST-1.7.11.md](_dpsm_work/REFACTOR-PLAN-POST-1.7.11.md) 第 5 节。
> 本地 Git,**无远端、不上传**。这份文档回答"什么进仓库、什么不进、为什么",机器可读的哈希在
> [_dpsm_work/baseline-manifest.json](_dpsm_work/baseline-manifest.json)。

## 1. 为什么需要它

工作目录是游戏安装目录,不是源码仓库:

| 目录/文件 | 体积 | 进仓库 | 理由 |
|---|---|---|---|
| `_dpsm_work/src/` | 2.2 MB | 是 | 插件源码(唯一真源) |
| `_dpsm_work/recon_probe/`、`test/`、`tests/` | < 1 MB | 是 | 可执行行为测试与探针(排除 `bin/`、`obj/`) |
| `_dpsm_work/contrib/`、`contrib_cs/`、`review_contrib_core/` | 2.1 MB | 是 | Python 契约实现、C# 参考实现 |
| `_dpsm_work/*.py`、`*.md`、`*.txt`、`*.json` | 6.0 MB | 是 | 活跃工具、现状文档、证据产物 |
| `_dpsm_work/acceptance_1.7.11/` | 4.9 MB | 是 | 当前批次的验收档案 |
| `BepInEx/config/dev.dpsmeter.cfg` | 18 KB | 是 | 运行配置口径(基线的一部分) |
| 根目录入口文档、`dpsmeter_*.py` | 40 KB | 是 | 对外入口 |
| `rlyehshoujotaix_cl_Data/` | 3.07 GB | 否 | 游戏资产 |
| `BepInEx/` 其余部分(导出语料、interop、core、日志) | 743 MB | 否 | 大体量输入/派生产物 |
| `GameAssembly.dll`、`UnityPlayer.dll` 等 | 119 MB | 否 | 游戏二进制 |
| `_dpsm_work/_review/`、`_verify_179/`、`enum_probe/` 等 | 280 MB | 否 | 历史证据快照 |
| `bin/`、`obj/`、`__pycache__/` | - | 否 | 构建缓存 |
| `*.bak`、`*.zip`、`*.dll`、`*.exe` | - | 否 | 部署产物、回滚锚点、归档 |

被排除的东西**不删除、不移动**。它们留在原地,由 `baseline-manifest.json` 用 SHA256 与本次基线关联,
因此"恢复到基线"= Git 恢复源码/工具 + 清单核对大资产未被替换。

## 2. 默认拒绝的 ignore 结构

[.gitignore](.gitignore) 的第一条规则是 `/*` —— **根目录下每条记录默认被忽略**,只有显式 `!` 回来的才
能进入提交。所以"误把整个游戏 `git add` 进去"在结构上不可能发生,不依赖操作者记得加 `-A` 之外的参数。

[.gitattributes](.gitattributes) 设置 `* -text`:**按字节原样存取**。若干守卫对文件内容做哈希
(`n0_acceptance.py` 的 `git_free_code_hash`、语料 manifest),而且有历史文件故意不是合法 UTF-8;
一次 CRLF 或编码改写就会让守卫静默失效。因此禁用 `text=auto` 与 EOL 转换。

## 3. 显式纳入清单(commit 允许包含的路径)

```text
.gitignore  .gitattributes  REPO-BOUNDARY.md
DpsMeter-文档索引.md  DpsMeter-使用说明-完整版.txt  安装说明.txt  changelog.txt
dpsmeter_analyze.py  dpsmeter_contrib.py
BepInEx/config/dev.dpsmeter.cfg
_dpsm_work/*.py  *.md  *.txt  *.json  *.cfg
_dpsm_work/src/  tests/  recon_probe/  test/  contrib/  contrib_cs/
_dpsm_work/review_contrib_core/  acceptance_1.7.11/  acceptance_rf2/  acceptance_rf3/
```

**批次档案里刻意不提交的东西**(RF3 起):每个 `acceptance_*/` 目录里的 `pairtrusted_impact_report.json`
(约 4 MB,indent=1 展开成 ~15 万行)与同名 `.txt`。它们是**从冻结快照可重算**的派生产物,工具都已纳管,
所以档案保留证据本体(results / corpus_manifest / runs / applicability / 守卫转录),不保留那份重算结果:`git
`add -A` 曾经让一个轮次插入 15.5 万行。`acceptance_1.7.11`(第 6 轮)早于这条规则,作为历史**保持完整**;
两种情况下文件都留在原地。`_dpsm_work/tests/_*.tmp`(临时脚本/提交信息)也一并排除 —— `tests/` 是被显式
回纳的目录,没有这条规则时 `git add -A` 会把临时文件带进提交。
```

提交前检查(tamper 前先看输出):

```powershell
git status --porcelain                       # 只应出现上表内的路径
git diff --cached --name-only | Measure-Object -Line
git diff --cached --stat | Select-Object -Last 1
```

## 4. 基线记录什么

`python _dpsm_work/repo_manifest.py --write` 生成 [_dpsm_work/baseline-manifest.json](_dpsm_work/baseline-manifest.json):

| 项 | 内容 |
|---|---|
| 源码版本 | `src/BuildInfo.cs` 与 `src/DpsMeter.csproj` 的版本对 |
| 部署 DLL | 路径 + SHA256(`BepInEx/plugins/DpsMeter/DpsMeter.dll`) |
| 回滚锚点 | 上一版 `.bak` 的路径 + SHA256 |
| 配置 | `dev.dpsmeter.cfg` 的 SHA256 + 全部布尔开关取值 |
| 测试与工具 | 每个被测项目/守卫脚本的 SHA256 |
| 语料 | 每个 `battle_*.json` 的 SHA256(32 份) |
| 外部程序集 | csproj 引用的每个 IL2CPP/BepInEx 程序集 + SHA256 |
| 被排除的大资产 | 目录级 SHA256(逐个文件) |

`python _dpsm_work/repo_manifest.py --verify` 重算并逐项比对,任何漂移非零退出。

## 5. 边界之外仍然有效的既有约束

1. Git 不替代 DLL 回滚:回滚要求**源码 + 工具 + 语料**同时匹配,只复制 DLL 不算恢复。
2. 历史正文不改写:发现旧报告里的数字过时,只更新"当前状态"句子,保留快照原文。
3. 已知坏样本 `battle_411001_20261004_015919.json` 不修、不删、不放宽容差。
4. 验收输出每批进独立目录,不覆盖既有档案。
