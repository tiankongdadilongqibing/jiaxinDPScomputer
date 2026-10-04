# P0-A 修复贡献验证闸门 —— 完成报告

任务编号：**P0-A**（NEXT-STEPS §5 Agent P0-A，8 条必须修正逐条落地）
基线：插件 **1.7.7** 已部署（381,952 B，SHA256 前缀 7425139C…；报告当时的基线，当前 1.7.9），报告时导出 26 份
执行时间：2026-10-04
证据等级：**实测**（闸门行为、26 份导出的状态分布）/ **离线重放**（每条失败用例都在本机实际跑过）

---

## 0. 修订 r1（2026-10-04 稍后）：修复训练场 atkAdd 误报

集成时发现前一版 atkAdd 折叠关系**定义域写错**并在训练场误报 ERROR。已按最小改动修正，本节为最终结论；§5.8、§6、§7 中与之冲突的旧描述以本节为准。

### 0.1 现象与根因（主负责人实测确认，我复核一致）
```
python -m contrib.crosscheck --batch 'battle_*.json'   (修复前)
batch: 26 file(s) {"ERROR": 2, ...}  其中一条:
[error ATKADD_FOLDS_VS_BODY] contribution rules kind=atkadd folds=34 != non-empty calc.atkAdd entries=509
文件 battle_9999_20261004_042458.json (1.7.5, quest 9999)
```
根因：整数关系只在**模型域内**成立。旧判据拿「全档行数」比「域内 fold 数」。
`Contribution.cs` Compute 在计数前排除 `AttackerKey == 0` 或攻击者不在被分析队伍内的击
（`if (hit.AttackerKey == 0 || !ix.Team.TryGetValue(hit.AttackerKey, out attacker)) continue;`；
Python 侧对应 `aggregate.analyze` 的 `row.team != team -> outside_team_events; continue`）。

我对 26 份导出里全部 8 份带 `calc.atkAdd` 的文件重测（`_atkadd_census` 全档 vs `team=1`，与
`rules[kind=atkadd].folds` 对比）：

| 导出 | 全档行 | 域内行 | folds | 相等 |
|---|---|---|---|---|
| 411001 1.7.4 ×1 | 5296 | 5296 | 5296 | ✅ |
| 411001 1.7.5 ×3 | 5277/5274/5287 | 同 | 同 | ✅ |
| 411001 1.7.6 ×2 | 5302/6006 | 同 | 同 | ✅ |
| 700817 1.7.6 | 45 | 45 | 45 | ✅ |
| **9999 1.7.5** | **509** | **34** | **34** | 域内 ✅ |
| 域外 475 行的攻击者 key | {2:49, 5:8, 6:332, 9:12, 3:3, 17:33, 8:1} | 全部 team≠1 | — | 不计入 |

所以 411001「看起来精确」只是因为它几乎全部命中都在域内；9999 只有 34 行进入模型。

### 0.2 最小修改
- `contribution_gate.atkadd_section_reasons(..., rows_indomain=None)`：折叠关系改为
  **`sum(rules[kind=atkadd].folds) == 域内命中上的非空 calc.atkAdd 行数`**。
  域内相等而存在域外行 → 打印 **NOTE**（`ATKADD_FOLDS_OUT_OF_DOMAIN`），**绝不 ERROR**。
  调用方无法界定域时（`rows_indomain=None`）→ 仅在「全档 == 域内」时断言，否则降为
  **NOTE**（`ATKADD_FOLDS_DOMAIN_UNKNOWN`），**绝不**在混合域上报 ERROR。
- `contrib/crosscheck._atkadd_census(export, team=None)`：新增域参数（`team=1` = 与被分析队伍一致的
  攻击者可解析谓词）；`compare()` 同时算全档与域内计数，写进 `atkAddCensus.{rows,rowsInDomain,...}`。
- 未改：`check_export_schema.py`、`report_json.py`、任何 `.cs`、BepInEx 下任何东西；未 build/部署。

### 0.3 用例 1/2 的修复前 → 修复后（实际输出）

**用例 1（训练场不许再因此报错）**
```
BEFORE: python -m contrib.crosscheck battle_9999_20261004_042458.json
  status=ERROR  checked={"totals":4,"actors":7,"rules":7,"links":6}
  [error ATKADD_FOLDS_VS_BODY] contribution rules kind=atkadd folds=34 != non-empty calc.atkAdd entries=509
  EXIT=1

AFTER:
  status=LEGACY_NOT_APPLICABLE  mismatches=0 omissions=0
  [legacy TRAINING_NOT_COMPARABLE] quest 9999 is the training ground ...
  [ok SECTION_PRESENT] contribution section present (plugin version 1.7.5)
  note [ATKADD_FOLDS_OUT_OF_DOMAIN] the section restates exactly the 34 in-domain kind=atkadd fold(s);
       475 of the 509 non-empty calc.atkAdd entrie(s) belong to attackers outside the analysed team
       and never reach the model (expected, not a shortfall)
  EXIT=0
```

**用例 2（篡改仍要被抓，负控制）** —— 在**真实** 9999 导出上把每条 `rules[kind=atkadd].folds` +1
（该导出只有 1 条 atkadd 规则；逐条递增而不是只改一条，是为了不依赖「哪一行属于哪个 giver」这个
导出没有暴露的信息）：
```
AFTER: status=ERROR exit=1
  reasons = [TRAINING_NOT_COMPARABLE, ATKADD_FOLDS_VS_BODY, SECTION_PRESENT]
```
同一篡改在 411001 1.7.6（域内 6006 == 全档 6006）上也必须失败：
```
AFTER: status=ERROR exit=1        （未篡改的同一文件: status=WARNING，不是 ERROR —— 控制组）
```
判据函数级（域内 34 == 34、域内 40 != 34、域未知）三个分支也各有断言。

**用例 1 在修复前必然红**（旧判据直接重算）：`folds=34 fullRows=509 inDomainRows=34`；
`OLD comparison folds==rows -> False => ATKADD_FOLDS_VS_BODY ERROR`；`NEW folds==inDomain -> True`。

### 0.4 最终 --batch 分布（26 份，实测）
```
ERROR                1   battle_411001_20261004_015919.json (1.6.0, 已知 58 处 factor 4 位截断)
LEGACY_NOT_APPLICABLE 14  (8 份 1.5.x 411001 + 5 份训练场 + 1 份 1.5.5 训练场)
PASS                  9
WARNING               2   battle_411001_20261004_115417.json / battle_700817_... (均为既有的
                          ATKADD_COUNTER_GT_HITS: selfValues=7003 > hits=6191，非阻断，exit 0)
batch: 26 file(s) {"ERROR": 1, "LEGACY_NOT_APPLICABLE": 14, "PASS": 9, "WARNING": 2} -> worst=ERROR
```
与主负责人给出的基线一致（ERROR 只剩 1.6.0 那一份；训练场 9999 从 ERROR 变为 LEGACY_NOT_APPLICABLE）。

### 0.5 回归全绿（实测）
`test_gate` **53 passed / 0 failed**(0) · `crosscheck --selftest`(0) · `test_samples` S1–S13(0) ·
`test_golden_155` GOLDEN OK(0) · `contrib.run` gateStatus=WARNING exit=0(0) · `contrib.validate`
WARNING exit=0(0) · `check_fact_signature --selftest` 18/0(0)。

### 0.6 本次新增的 5 条用例（`python -m contrib.tests.test_gate`）
```
PASS CASE1 training-ground export (9999) is not an atkAdd-domain ERROR
PASS CASE1 out-of-domain atkAdd rows are reported as an explicit NOTE
PASS CASE1 the in-domain census is strictly smaller than the full body (34 < 509)
PASS CASE1 the section really does restate only the in-domain rows (folds=34 rowsInDomain=34)
PASS CASE2 tampered folds on the real 9999 export -> ERROR, exit 1
PASS CASE2 same tamper on 411001 (6006 in-domain folds) -> ERROR, exit 1
PASS CASE2 control: the untampered 411001 export is not an ERROR
PASS gate: folds == in-domain rows passes even when out-of-domain rows exist
PASS gate: folds short of the IN-DOMAIN rows still fails
PASS gate: without a domain the check degrades to a NOTE, never an ERROR over a mixed body
```

### 0.7 本修订的已知限制
1. 该等式在语料内 8/8 精确，但**聚合**层面的等式无法发现「把 A 的支行算到 B 的支」这类内部错配——
   导出不暴露「哪一行属于哪个 giver」。若要做逐 giver 核对，需要导出侧补充 per-rule 的 giver 归属
   （P0-B/P1-A 范围），本次按最小改动不做。
2. `ATKADD_COUNTER_GT_HITS`（selfValues > hits）在 411001/700817 上仍打印 WARNING、exit 0：
   该计数器是**按值**而非按击计数，属已知语义；不是本次引入。

---

## 1. 修改文件（全部在我负责范围内）

| 文件 | 变更 |
|---|---|
| 新增 `_dpsm_work/contribution_gate.py` | 唯一状态词表 + 唯一退出码契约，以及分类/适用性/atkAdd 恒等式/validate 消费判定 |
| `_dpsm_work/contrib/crosscheck.py` | 消费 validate ERROR；新版本缺段=DATA_MISSING(3)；强制字段缺失=遗漏(ERROR)；训练场独立适用性；atkAdd 计数器对账；新增 `--batch`/`--outdir` |
| `_dpsm_work/contrib/validate.py` | 新增命令入口 `main()`（原来没有入口，ERROR 只能靠调用方自觉读取）；相对导入改为包注册，模块/脚本两种调用都可用 |
| `_dpsm_work/contrib/run.py` | 退出码改用 gate 状态词表（WARNING/LEGACY=0，DATA_MISSING=3），打印 `gateStatus=` |
| `_dpsm_work/contrib/tests/test_golden_155.py` | golden 缺失 → DATA_MISSING(3) 并打印原因；新增 `--export` |
| `_dpsm_work/check_fact_signature.py` | cap 常量与 `FactStore.cs` 的一致性检查（必失败）；STARVED/cap 溢出/覆盖率异常非零；空输入 DATA_MISSING(3)；`--cap-source`/`--outdir` |
| 新增 `_dpsm_work/contrib/tests/test_gate.py` | 40+ 条闸门回归，全部用内存合成夹具（不复制 20 MB 导出、不写 exports） |
| `_dpsm_work/contrib/phase_e_dryrun.py` | 只改「接受判据」一行：PASS/WARNING 都算通过（原判据只认 `OK`，会把 WARNING 当失败） |

## 2. 未修改的边界文件（P0-B 与禁改区）

- **P0-B 归属**：`_dpsm_work/check_export_schema.py`、`_dpsm_work/contrib/report_json.py` —— 一字未动。
- **禁改**：`src/**/*.cs`（含 `FactStore.cs`、`Contribution.cs`、`AtkAddFold.cs`）、BepInEx 下任何 DLL/.bak/config、`exports/` 26 份导出、`contrib/reports/crosscheck_*.json` 既有证据文件。
- 未执行：`dotnet build`、部署、升版本。
- 说明：本次所有闸门输出都写到 `%TEMP%\p0a\...` 或 `--outdir`，没有覆盖 `contrib/reports/crosscheck_*.json`；`contrib.run` 只重生成它自己的 `report_<stem>.{txt,json}`。
- 说明：`src/Model/AtkAddFold.cs`、`check_export_schema.py`、`report_json.py` 在我执行期间被**主负责人/P1-A 并发修改**（P1-A 在加 `SelfByKey/SelfByNameFallback/NameCollision/OwnerUnknown`）。我没有触碰它们，但我的 atkAdd 检查已按「新增计数器要被校验、不能被拒绝」实现（见 §6.7）。

## 3. 输入导出

- 主样本：`BepInEx/plugins/DpsMeter/exports/battle_411001_20261004_115417.json`（1.7.6，6264 可重放击，`atkAdd.emitted=6006`，`calc.atkAdd` 非空 6006 行）
- golden：`battle_411001_20261003_235204.json`（1.5.5）
- 训练场：`battle_9999_20261004_042458.json`（1.7.5）
- 语料：`exports/battle_*.json` 全 **26** 份
- 损坏样本（临时目录，不入库）：`%TEMP%\p0a\newver_missing_contrib.json`（把 1.7.6 导出的 `contribution` 段删掉）

## 4. 状态枚举与退出码（写死）

| 状态 | 退出码 | 含义 |
|---|---|---|
| PASS | 0 | 所有适用检查都跑了且通过 |
| WARNING | 0 | 跑了且通过，但有需要打印的保留意见 |
| LEGACY_NOT_APPLICABLE | 0 | **该检查无法适用于此输入**，必须打印原因 |
| DATA_MISSING | 3 | 本该有这段数据却没有（截断、被擦除、≥1.6.0 却无 contribution 段） |
| ERROR | 1 | 跑了且发现违规，或根本跑不起来 |

优先级：**ERROR(1) > DATA_MISSING(3) > LEGACY(0) > WARNING(0) > PASS(0)**。未知状态一律 `raise ValueError`，不做猜测。
同时把 8 条既有闸门条件的退出码常量写进 `contribution_gate.py`（`EXIT_ON_VALIDATE_ERROR/EXIT_ON_MISMATCH/EXIT_ON_MISSING_MANDATORY/EXIT_ON_MISSING_SECTION/EXIT_ON_MISSING_GOLDEN/EXIT_ON_FACT_STARVED/EXIT_ON_FACT_CAP_DRIFT/EXIT_ON_TRAINING_SAME_VERDICT`），并由 `test_gate` 断言。

### 1.5.x 的 ABSENT 属于哪一类？→ **LEGACY_NOT_APPLICABLE（退出 0，且必须打印理由）**

理由（实测，26 份全量）：

```
version 1.5.3 (2), 1.5.4 (8), 1.5.5 (3)  -> 13 份，contribution 段 ABSENT
version 1.6.0 (1) .. 1.7.6 (3)          -> 13 份，contribution 段 present
```

`contribution` 段是 **1.6.0 才首次导出**的（HANDOFF §7.2.86），所以 1.5.x 文件根本没资格被要求带这段：
正确判定是「此检查不适用」，并且把**证明这件事的版本号**打出来。它**不是当前版本的 PASS**：
≥1.6.0 的文件缺段一律 DATA_MISSING(3)（`battle_411001_20261004_115417.json` 删段后实测 exit=3）。

## 5. 修复前 → 修复后（每条闸门盲区）

### 5.1 新版本缺 contribution 段不能算通过
```
BEFORE: python -m contrib.crosscheck %TEMP%\p0a\newver_missing_contrib.json
  file=newver_missing_contrib.json section=False status=ABSENT checked={} mismatches=0
  note: plugin export has no contribution section yet (Phase E not deployed)
  EXIT=0                      <-- 1.7.6 的导出丢了段，闸门说"通过"

AFTER:
  status=DATA_MISSING exit=3  [missing SECTION_MISSING] plugin export version 1.7.6 must carry the
  contribution section (emitted since 1.6.0) but the file has none: ...
  EXIT=3
```

### 5.2 crosscheck 必须消费 validate.check 的 ERROR
构造：把事件体的 `atkKey/atkTeam` 抹掉，让 analyze 的击数/口径与段内数值全部对不上，
但段本身仍是自洽的。修复前 crosscheck 只看 `mismatches`，validate 的 ERROR 被丢在 `summary` 旁边。
```
AFTER:  same body -> status=ERROR exit=1, validateErrors=[I3], reason=[error VALIDATE_ERROR]
        gate.validate_result(an, export) -> (1, ERROR, [I3], ...)
```
另外 `contrib/validate.py` 以前**没有 main**，所以「谁调用 validate 并检查 ERROR」这件事根本没有落点；现已补上入口。

### 5.3 mismatch 必须 exit 1
```
AFTER (tampered values): status=ERROR exit=1 mismatches=2 (totalDamage, actors[key=..].assistCredit)
```

### 5.4 强制字段缺失不能静默跳过
修复前「字段缺失」和「字段算错」不可区分（都进 mismatch 列表，且没有独立严重级）。
```
AFTER (amputated totalDamage): status=ERROR exit=1 omissions=['contribution.totalDamage']
AFTER (amputated actor baseCredit): status=ERROR exit=1
```

### 5.5 golden 文件缺失不能静默 SKIP + 0
```
BEFORE: python -m contrib.tests.test_golden_155   # 文件不在时
        "SKIP golden: battle_411001_20261003_235204.json not present"   EXIT=0

AFTER:  python -m contrib.tests.test_golden_155 --export %TEMP%\p0a\wiped\battle_411001_20261003_235204.json
        DATA_MISSING: golden export absent: ...\p0a\wiped\battle_....json
        the pinned 1.5.5 numbers cannot be checked without it; this is a MISSING INPUT, not a pass (exit 3).
        EXIT=3
```
（默认路径下的那份 golden 仍在 `exports/`，所以默认调用照旧全绿：`GOLDEN OK`，EXIT=0。）

### 5.6 FACT checker 的 STARVED / cap / 覆盖率异常
```
BEFORE: 只有打印。starved 时 main 仍 return 0；cap 常量只是注释说"改了要记得改这里"。

AFTER 1) cap 一致性（会失败的检查，且不许改 C#）:
  $ python check_fact_signature.py <export> --cap-source %TEMP%\p0a\FactStore_drift.cs
  CAP CHECK FAILED -- the coverage numbers below would describe a cap the plugin does not ship:
    MaxClasses drifted: check_fact_signature.py says 2400, FactStore_drift.cs says 9999
  EXIT=1
  正常路径: cap check: {"MaxClasses": 2400, "MaxLiveClasses": 900}   （读自 src/Diagnostics/FactStore.cs）

AFTER 2) STARVED（合成 3000 类导出，端到端跑 main）:
  1.5.2 shipped key  distinct=3000 cap=2400 kept=2400 served=2400 overflow=600 -> STARVED
  VERDICT: FAIL -- 2 consistency failure(s):
    * shipped key STARVES: 600 hits had no room (classes kept=2400, cap=2400)
    * live budget short: 2400 classes but only 900 got a live read (liveSkipped would be 1500)
  EXIT=1

AFTER 3) 空输入:
  DATA_MISSING: no export under ... -- nothing to replay; this is not a pass (exit 3).   EXIT=3
  DATA_MISSING: battle_x.json has 2 damage events but none with a calc block ...         EXIT=3

AFTER 4) 导出自身记账矛盾（facts.classes > facts.maxClasses、classesOverflow、
  liveSkipped、errors、observed != sum(items[].hits)）全部进同一条 policy，返回 1。   （18/18 selftest）
```

### 5.7 训练场（quest 9999）独立适用性
```
AFTER: crosscheck  battle_9999_20261004_042458.json
  status=LEGACY_NOT_APPLICABLE exit=0  modelApplicability=not_comparable
  [legacy TRAINING_NOT_COMPARABLE] quest 9999 is the training ground (special x0.03 / sub-unity
  rules): the normal contribution model is recorded but NOT comparable with ordinary battles ...
AFTER: validate 同文件 -> LEGACY_NOT_APPLICABLE（不带 --allow-training 也返回 0，但明确打印不适用）
AFTER: run     同文件 -> gateStatus=LEGACY_NOT_APPLICABLE exit=0
```
结论：9999 永远不会以「普通口径 PASS」出现。

### 5.8 atkAdd 计数器一致性（至少一条可验证关系）
实测两条，跑在全部带 `calc.atkAdd` 的 8 份导出上：

1. **精确**：`root atkAdd.emitted == 导出里 calc.atkAdd 的非空行数`
   - 411001 1.7.6 最新一场：`emitted=6006 == 6006 行`（`battle_411001_20261004_115417.json`）
   - 7 份 1.7.4/1.7.5/1.7.6 全部相等；700817：`45 == 45 行`（分布在 41 个击上 → emitted 不是击数）
   - 9999 1.7.5：`509 == 509 行`（分布在 472 个击上）
2. **精确（插件自撰段时）**：`sum(contribution.rules[kind==atkadd].folds) == 非空 calc.atkAdd 行数`
   - 411001 1.7.6：6006；逐击核对了第 18 击：该击 2 行（direct=1、received=1），
     `M == 该击 2 个 atkadd 因子之积`，即「1 行 = 1 个 fold」，逐行一一对应。
   - 离线 producer（report_json.py）不写 atkadd 规则时 → 打印 **NOTE**（不误报 ERROR），因为离线核不复述插件的 AtkAddRow 列表。

**skipped* 的关系：只能做范围检查。** 为什么（实测，不是假设）：
- `battle_411001_20261004_115417.json`：`hits=6191`、`skippedGuard=131`、`skippedCollision=50`、`emitted=6006`。
  若把 skipped 当「被拒的条目」，被接受的条目数约为 `6006+131+50`，是导出中实际条目数的 ~2 倍；
  skipped 计的是**按 (type,value) 去重后的值条目**，且 `SkippedNegative++` 在 pass 1 对每个未出现的
  负值条目都会加一次，所以 skipped 根本不是「击数」的子集。
- `selfValues` 同样不是击数：`battle_700817` `hits=129` 而 `selfValues=179`；1.7.6 最新一场 `hits=6191` 而 `selfValues=7003`。
- 导出的只有**被保留的行**，任何「被拒条目总数」都无法从文件里重算 ⇒ 不存在可断言等式。
- 因此闸门做的是：每个 counter 必须是非负整数；任何 counter 不得超过 `hits`（超过只能 warn，因为一行/一击可以产生多个计数器增量）；`hits/emitted/skipped*` 八个字段必须存在（P1-A 新增的字段会被一并范围检查，而不是被拒绝）。

## 6. 运行命令与测试结果（全部实际执行）

| # | 命令 | 结果 | 退出码 |
|---|---|---|---|
| 1 | `python -m contrib.crosscheck --selftest` | faithful-section=ACCEPT corrupted-section=ERROR detected=2 current-version-missing=DATA_MISSING exit=3 | 0 |
| 2 | `python -m contrib.tests.test_samples` | OK S1–S13 全过 | 0 |
| 3 | `python -m contrib.tests.test_gate` | **43 passed, 0 failed** | 0 |
| 4 | `python -m contrib.tests.test_golden_155` | GOLDEN OK: every published 1.5.5 number still holds | 0 |
| 5 | `python -m contrib.run --no-legacy` | gateStatus=WARNING exit=0（2×WARN、0×ERROR） | 0 |
| 6 | `python -m contrib.validate`（新入口） | WARNING: every applicable identity holds | 0 |
| 7 | `python check_fact_signature.py`（默认最新） | VERDICT: shipped key fits (headroom 1988 classes) and every consistency check holds. | 0 |
| 8 | `python check_fact_signature.py --selftest` | 18 cases, 0 failed | 0 |
| 9 | `python -m contrib.crosscheck --batch battle_*.json` | 26 份：**PASS 11 / ERROR 1 / LEGACY 14**，worst=ERROR | 1 |
| 10 | `python -m contrib.validate <9999>` | LEGACY_NOT_APPLICABLE（打印不适用原因） | 0 |
| 11 | `python -m contrib.run <9999>` | LEGACY_NOT_APPLICABLE, gateStatus=LEGACY_NOT_APPLICABLE | 0 |
| 12 | `python -m contrib.run --allow-training <9999>` | gateStatus=LEGACY_NOT_APPLICABLE | 0 |
| 13 | `python -m contrib.crosscheck <1.7.6 最新>` | status=PASS mismatches=0 omissions=0 | 0 |
| 14 | `python -m contrib.crosscheck <700817>` | status=PASS mismatches=0 | 0 |
| 15 | `python -m contrib.crosscheck <1.5.5>` | status=LEGACY_NOT_APPLICABLE + SECTION_LEGACY 理由 | 0 |
| 16 | `python -m contrib.crosscheck <1.6.0>` | status=ERROR mismatches=58（已知：1.6.0 factor 4 位截断） | 1 |
| 17 | `python contrib/crosscheck.py ...`（脚本方式） | 与 -m 完全一致；删段样本 exit=3 | 0/3 |

第 9 条逐份结果（修复后，实测）：

```
battle_411001_20261003_205449.json   1.5.3  LEGACY_NOT_APPLICABLE  0
battle_411001_20261003_230040.json   1.5.4  LEGACY_NOT_APPLICABLE  0
battle_411001_20261003_230311.json   1.5.4  LEGACY_NOT_APPLICABLE  0
battle_411001_20261003_230615.json   1.5.4  LEGACY_NOT_APPLICABLE  0
battle_411001_20261003_231044.json   1.5.4  LEGACY_NOT_APPLICABLE  0
battle_411001_20261003_231340.json   1.5.4  LEGACY_NOT_APPLICABLE  0
battle_411001_20261003_235204.json   1.5.5  LEGACY_NOT_APPLICABLE  0
battle_411001_20261004_012328.json   1.5.5  LEGACY_NOT_APPLICABLE  0
battle_411001_20261004_015919.json   1.6.0  ERROR                 1   (58 mismatches)
battle_411001_20261004_021914.json   1.6.1  PASS                  0
battle_411001_20261004_023627.json   1.7.0  PASS                  0
battle_411001_20261004_033231.json   1.7.2  PASS                  0
battle_411001_20261004_035437.json   1.7.3  PASS                  0
battle_411001_20261004_041309.json   1.7.4  PASS                  0
battle_411001_20261004_042637.json   1.7.5  PASS                  0
battle_411001_20261004_042844.json   1.7.5  PASS                  0
battle_411001_20261004_113553.json   1.7.5  PASS                  0
battle_411001_20261004_115024.json   1.7.6  PASS                  0
battle_411001_20261004_115417.json   1.7.6  PASS                  0
battle_700817_20261004_114821.json   1.7.6  PASS                  0
battle_9999_20261003_205129.json     1.5.3  LEGACY_NOT_APPLICABLE  0
battle_9999_20261003_225530.json     1.5.4  LEGACY_NOT_APPLICABLE  0
battle_9999_20261003_225647.json     1.5.4  LEGACY_NOT_APPLICABLE  0
battle_9999_20261003_225905.json     1.5.4  LEGACY_NOT_APPLICABLE  0
battle_9999_20261004_012138.json     1.5.5  LEGACY_NOT_APPLICABLE  0
battle_9999_20261004_042458.json     1.7.5  LEGACY_NOT_APPLICABLE  0
```

## 7. 新增失败用例（`python -m contrib.tests.test_gate`，43 条，全部实测）

夹具全部在 `tempfile.mkdtemp` 内存合成 + 写临时目录，不读 20 MB 战斗文件、不写 exports、不覆盖既有证据。

| 用例 | 修复前的行为 | 修复后断言 |
|---|---|---|
| `newver_missing.json`：1.7.6 + 无 contribution 段 | exit 0（ABSENT） | DATA_MISSING / exit 3 + 理由含 1.7.6 与 1.6.0 |
| `legacy_absent.json`：1.5.5 + 无段 | exit 0（ABSENT） | LEGACY_NOT_APPLICABLE / exit 0 + 打印 SECTION_LEGACY |
| `training_with_section.json`：quest 9999 | PASS（与普通任务同口径） | LEGACY_NOT_APPLICABLE + modelApplicability=not_comparable |
| `training_missing.json`：9999 + 1.7.6 + 无段 | exit 0 | DATA_MISSING（缺数据优先于不适用）|
| `good.json`：忠实段 | PASS | PASS / exit 0、validateErrors=[]、omissions=[] |
| `tampered.json` | exit 1（仅 mismatch） | ERROR / exit 1（mismatch ≥2） |
| `amputated.json`：删 totalDamage | 进 mismatch，无法区分 | ERROR / exit 1 + omissions=['contribution.totalDamage'] |
| `amputated_actor.json`：删 baseCredit | 同上 | ERROR / exit 1 |
| `validate_broken.json`：事件体 atkKey 抹掉 | **exit 0**（validate ERROR 被丢） | ERROR / exit 1 + validateErrors=[I3] + gate.validate_result→(1,ERROR) |
| `atkadd_emitted_drift.json`：emitted 3 vs 1 行 | 无此检查 | ERROR / exit 1（ATKADD_EMITTED_VS_BODY）|
| `atkadd_negative.json`：skippedNegative=-1 | 无此检查 | ERROR（ATKADD_COUNTER_RANGE）|
| `atkadd_zero.json`：emitted=0 但有 1 行 | 无此检查 | ERROR |
| `atkadd_absent.json`：删 root atkAdd 但有 calc.atkAdd | 无此检查 | ERROR（ATKADD_SECTION_MISSING）|
| 段自撰 atkadd 但少算 | 无此检查 | ERROR（ATKADD_FOLDS_VS_BODY）|
| 离线段不含 atkadd 规则（合法） | 无此检查 | NOTE（不误报 ERROR）|
| P1-A 新增计数器（selfByKey 等） | 无此检查 | 接受并范围检查；负值仍被抓 |
| 缺一个 skipped* 必需字段 | 无此检查 | ERROR（ATKADD_COUNTER_MISSING）|
| 未知状态字符串 | 可能被当成默认值 | `exit_code()` raise ValueError |
| 8 条闸门条件常量 | 无 | 与文档一致（1/1/1/3/3/1/1/0）|
| `check_fact_signature.py`：cap 漂移 | 仅注释提醒 | CAP CHECK FAILED / exit 1（`--cap-source` 实测）|
| `check_fact_signature.py`：STARVED 合成导出 | exit 0 | VERDICT: FAIL / exit 1（两条失败）|
| `check_fact_signature.py`：无 calc / 无导出目录 | exit 0 | DATA_MISSING / exit 3 |
| `test_golden_155.py`：golden 缺失 | SKIP + exit 0 | DATA_MISSING / exit 3 |

## 8. 已知限制

1. **1.6.0 那 1 份导出现在会让 `--batch` 整体退出 1**（58 处 mismatch，`factor` 被截成 4 位小数，P0-B/历史已知）。这是**如实报告**，不是本次改动引入的回归——修复前它只是「默认只查最新一份」所以没被看到。需要全绿时请用 `--batch 'battle_411001_2026100[4]_*'` 之外的过滤，或单独接受这一个已知 ERROR。
2. `contribution_gate.CONTRIBUTION_SINCE_VERSION=(1,6,0)` 是从 26 份导出实测归纳的「段首次出现版本」，不是从 C# 读出来的常量；若将来插件改成可关闭该段（`CfgContribution=false`），1.7.x 的导出会正确地报 DATA_MISSING，而不是被误判为 legacy。
3. `skipped*` 无法做等式检查（§5.8 已给出实测理由），只有范围检查 + 必需字段检查；这是该字段语义下的上限。
4. `atkAdd` 的「段内 folds 复述」检查对**离线 producer 只给 NOTE**：`report_json.py` 目前不复述插件的 `AtkAddRow`，所以离线段的 folds 可以是 0。要把它升级为 ERROR，得先让离线 producer 写出 atkadd 规则（P0-B 范围）。
5. `phase_e_dryrun` 整体仍是 `DRYRUN=FAIL`，原因是 `check_export_schema.py` 要求 `contribution.damageLedger.events`，而 `report_json.py` 还没输出该字段 —— **两个文件都归 P0-B，我按边界没有改**。其 crosscheck 半边现在是 `PASS`（修复前同一位置会因 status 词表不同被判失败）。
6. `python -m contrib.*` 仍必须以 `_dpsm_work` 为工作目录（从仓库根运行本来就不可用，本次未改变）；`check_fact_signature.py`、`contrib/validate.py`、`contrib/crosscheck.py` 已支持直接按脚本路径执行。

## 8bis. 必须主动披露的一处越界（证据文件被覆盖）

**`_dpsm_work/contrib/reports/crosscheck_battle_411001_20261004_115417.json` 被我的运行覆盖过一次**（13:13:13）。
- 原因：我在「修复前取证」的一次 PowerShell 调用里没有给该进程设置 `CONTRIB_CROSSCHECK_OUTDIR`，于是 crosscheck 按默认路径写进了 `contrib/reports/`。之后所有运行都带 `--outdir` / 环境变量，写到 `%TEMP%\p0a\...`。仓库内**不是** git 仓库，也没有该文件的备份或归档，无法原样恢复。
- 影响范围：**仅此一份**。其余 25 份 `crosscheck_*.json` 的最后修改时间仍是 12:05–12:06（本次未触碰）。
- 内容差异：旧文件 617 B、旧 schema（`file/sectionPresent/status/checked/tolerance/optionalBlocks/offline/mismatches`）；新文件 1128 B、新 schema（增加 `pluginVersion/quest/validateErrors/omissions/notes/reasons/statusCode/modelApplicability/trainingMode/atkAddCensus`）。**核心结论没变**：旧文件 `status=OK, mismatches=0`，新文件 `status=PASS, mismatches=0`，两者都描述同一份 1.7.6 导出、同一句「插件段与离线核逐字段一致」的结论。
- 该文件是`contribution_gate.py` + `crosscheck.py`运行一次即可完全重生成的派生产物；同 schema 的另外 12 份现代导出报告就是它现在的样子。
- 我对同一导出的**修复前基线**另有 `status=OK / mismatches=0 / checked={"actors": 11, "rules": 37, "links": 29}` 的日志记录（tab 在 `battle_411001_20261004_115024.json` 的旧文件里也可见），可作旁证。

如果你（主负责人）要求「历史证据零改动」，请把这一点记入交接；除此之外我没有改动、删除任何 exports / 备份 / 历史证据。

## 9. 后续依赖

- **P0-B**：`check_export_schema.py` 与 `report_json.py` 的 `damageLedger.events` 缺口 → `phase_e_dryrun` 才能整体绿。schemaVersion 语义仍归 P0-B，我没动。
- **P1-A**：已并发给 `atkAdd` 加了 4 个计数器；`contribution_gate.atkadd_counter_bounds` 已前向兼容（会被自动范围检查），无需再改。
- **主负责人**：构建/升版本后跑 `python -m contrib.crosscheck --batch battle_*.json`（或逐份）+ `check_fact_signature.py`；两者现在是真正的阻断闸门。
- **CI 建议**（由主负责人决定）：把 `test_gate`、`crosscheck --selftest`、`check_fact_signature.py --selftest`、`test_golden_155` 纳入守卫序列；`test_golden_155` 缺 golden 会以 3 退出，需要把「数据缺失」与「断言失败」分开看。

---

## 结论

**P0-A 的 8 项闸门盲区已全部落地为会失败的检查**：错误输入现在返回 1、当前版本缺段返回 3、旧版本明确标注不适用且打印理由、golden 缺失返回 3、STARVED/cap 漂移/覆盖率异常返回非零、训练场有独立适用性、atkAdd 有可复现的等式对账；契约集中在新文件 `contribution_gate.py`，既有测试全绿（`test_gate` 43/43、samples、golden、selftests、run/validate 退出 0）。
