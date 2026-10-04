# N3 / P1：跨场身份与实验元数据（第一阶段设计）

> 任务：路线图 N3 / P1 第一阶段「纯离线设计」。目标：知道「谁在什么配置下参加了哪场」，不再把名称等同于身份。
> 状态：设计 + 离线映射 + 自带负控测试。**未改插件、未改导出契约、未部署、未触碰任何既有文件。**
> 实施边界（按 [CONTRIBUTION-NEXT-PHASE-ROADMAP.md](<CONTRIBUTION-NEXT-PHASE-ROADMAP.md#L157-L169>)）：第一批只提交字段设计、映射与测试，不与 N2 同时修改比较器；插件实施另批由主负责人集成。
> 证据分级：**实测**（真实语料上跑出的数字）/ **离线重放**（用导出与注解文件重算）/ **静态审查**（只读源码/导出结构）/ **推断**（结构推导，未验证）。
> 工具：`_dpsm_work/identity_map.py`，版本 `N3-P1-1.0`，身份输出 schema `identity-map/1.0`。
> 本文所有语料数字来自 [IDENTITY-CENSUS.md](<IDENTITY-CENSUS.md>) 的同一次运行；命令、哈希、退出码都在那份报告里。

---

## 1. 本设计要解决的问题

当前导出把名称当作身份：`actors[].name` 与 `contribution.actors[].name` 是唯一的人类可读标识，比较器（N2）按名称/阵容分组。真实语料显示：

- 「ショゴス」在同一场里出现 206 次（192 个同名 Boss 单位 + 其它），它们是**不同实例**，名称相同；
- 「レヴナント」(模板 47) 在同一场里同时出现在我方(team 1)与敌方(team 2)（实测：`battle_9999_20261003_225530.json`）；
- 「生ける炎 クトゥグア」导出为 `kind=B` 却站在 team 1，并作为 3 个召唤物（火砲/砲台/砲台R）的来源；
- 同名「メルティエル」(模板 42) 在不同场次出现 **2 种构筑指纹**（换装备）。

因此本设计把「身份」拆成多个互相独立的字段，任何合并都必须逐字段给出理由。

## 2. 字段设计

### 2.1 场内 actor 实例 key —— `actors[].key`

| 项 | 内容 |
|---|---|
| 导出字段 | `actors[].key`（整数） |
| 语义 | **仅在本导出内唯一**的 actor 序号；伤害事件经 `events[].atkKey` / `vicKey` 指向它 |
| 身份解析产物 | `actorKey` 原样保留；`instanceKey = "inst:" + sha256(导出)[:16] + ":" + key` |
| 证据 | 实测：30 份导出、4 695 个 actor，`key` 场内唯一；[数据字典](<CONTRIBUTION-DATA-DICTIONARY.md>) 亦记为「主键」 |
| 边界 | `key` **不是**跨场身份：不同场次的 `key=2` 可能是不同角色 |

### 2.2 角色模板 id —— `templateId` / `templateNamespace`

导出没有显式角色 id，所以模板 id 是**从可观测字段推导**的，必须连推导依据一起记录。

| namespace | 来源 | 依据字段 | 证据 |
|---|---|---|---|
| `char` | 角色本身 | `abilities[]` 中 `slot=6`（潜在）的 `id` | 实测：语料 26 个模板 id，与名称**一一对应**（0 处冲突），跨 30 份稳定 |
| `token` | 召唤物/token | `abilities[]` 中 `slot=1`（基础被动）的 `id`（如 30042 火砲、39001 砲台） | 实测：语料中 100% 命中 |
| `enemy` | 无能力行的敌方/中立单位 | `sha256(kind + "\u0000" + name)[:16]` | 推断：**派生 key，不是游戏 id**，只用于「同名同 kind 是否同一个东西」的本地判据 |
| `named` | 无 kind 也无法力的残缺记录 | 不用（保持 null） | — |

输出字段：`templateId`、`templateNamespace`、`templateBasis`（如 `slot6_potential`）、`templateEvidence`（MEASURED / OFFLINE_REPLAY / INFERRED / missing）。

**重要区分**：`templateId` 是「角色是谁」，**不含装备**。模板 id 相同只说明是同一名角色，不说明是同一份构筑（见 §2.5）。语料证据：模板 47 出现 7 种构筑指纹、模板 84 出现 7 种、模板 42 出现 2 种——同一角色的不同配置被正确拆开（实测，人口普查 §4）。

**第二阶段（改插件）才可能拿到真 id**：路线图 §N3 写明「只读侦察持久身份字段」。本阶段不假设存在任何持久角色 id；`char:<slot6 id>` 只是当前导出内的可复现推导，若插件日后提供真 id，应新增 namespace（例如 `char_persistent`）而不是改 `char` 的含义。

### 2.3 队伍槽位 —— `slot`

| 项 | 内容 |
|---|---|
| 现状 | **导出不含玩家编队槽位/前后卫位置** |
| 存在但不可用的线索 | `layout.pairs[]`（八字节/十二字节指针）、`layout.slots[]`（能力槽 id：1 基础被动、2 职业特性、5 被动、6 潜在、9 神器、10 刻印）、`rosterAudit.layoutAmbiguous`（1.7.10 样本中 102 次） |
| 本设计结论 | `slot = null`，`slotBasis = "not_available_in_export"`，**不猜**；人工确认的槽位只能来自 run_annotations.json（`confirmed.roster[].slot`） |
| 证据 | 静态审查：`layout.pairs` 只有指针值，没有可解释的槽位编号语义；1.7.10 样本 `rosterAudit.layoutAmbiguous=102` 说明布局解析本身也不完整 |
| 后续 | 若第二阶段要让插件导出真实槽位，需新增字段并升导出 schema |

### 2.4 召唤物与来源 —— `actors[].summon` / `kind` / `ownerName`

| 字段 | 语义 | 证据 |
|---|---|---|
| `kind` | 单位类型。语料取值：P(340)、Boss(4 279)、B(23)、E(38)、C(15) | 实测 |
| `team` | 1=我方，2=敌方 | 实测 |
| `summon` | 布尔标记。语料中 80 个 true（我方 72、敌方 8） | 实测 |
| `ownerName` | 召唤物来源。**导出没有 owner 字段**，只能从 `events[].owner != events[].attacker` 观测 | 离线重放：80/80 召唤物名称全部解析出来源 |

分类 `class`：character（kind=P 且 team=1 且 summon=false）、summon（summon=true）、enemy_unit（kind=P 但 team=2）、enemy（Boss/B/C/E）、unknown。

**召唤物不得与本体重合**：`same_entity()` 在 summon 标记不同且任一方 class 为 summon 时直接判 `different_entity`（负控用例 C）。语料实测：火砲 → メルティエル、砲台/砲台R → 生ける炎 クトゥグア、邪悪なる死のカラス → [賢導]トレイラ 等 8 组来源，全部解析出来源且**未与本体重合**。

**已知缺口**：「生ける炎 クトゥグア」自身 `kind=B` 却在 team 1、且没有 abilities 行，因此拿不到真实模板 id（本设计只给 enemy 派生 key + 弱身份警告）。它同场的 3 个 token 反而拿到了强身份（token 39001）。这是导出侧的信息缺口，不是映射错误。

### 2.5 角色构筑指纹 —— `loadoutFingerprint`

| 项 | 内容 |
|---|---|
| 定义 | `sha256(canonical(abilities))[:16]`，canonical 元素为 `[slot, id, level, sorted(talents)]`，talents 元素为 `[i, type, timing, p, cond]` |
| 为什么不含名称 | 名称是「标签」不是「配置」：同一 id 的显示名可能随版本/语言变化，改名不应改变身份 |
| 为什么不含 delta/prop/agg | 它们是每次命中的**观测计数**，跨场必然变化，不是配置 |
| 辅助字段 | `loadoutIdFingerprint`（只按 slot→id 集合）、`loadoutDetailHash`（含名称，仅供审计）、`loadoutFingerprintBasis` |
| 证据 | 实测：同一角色跨 30 份导出，指纹不变或按真实换装变化（ネーフェ＝ジアー 22/22 场同一指纹；メルティエル 2 个指纹，对应两套装备 id 集合） |

指纹的**两种用法**必须分开：

1. **同场拆分**：同模板、指纹不同 → `same_template_different_loadout`，不合并；
2. **跨场分组**：`entityKey = "ent:" + namespace + ":" + templateId + ":" + fingerprint` 才是「同一角色的同一套配置」。

### 2.6 各 key 的关系

| key | 形式 | 作用 | 可否跨场 |
|---|---|---|---|
| actorKey | 整数 | 场内事件索引 | 否 |
| instanceKey | `inst:<export sha16>:<actorKey>` | 单场内唯一实例 | 否（含导出哈希） |
| typeKey | `type:<ns>:<templateId>` | 角色/模板身份，**不含装备** | 是（跨场同角色） |
| entityKey | `ent:<ns>:<templateId>:<fingerprint>` | 角色 + 构筑 | 是（同角色同配置） |
| sameEntityKey | `entityKey\|field=<team>\|class=<class>` | 阵营感知 key；同模板可合法出现在两阵营（镜像/训练场） | 是，且不跨阵营 |

`entityKey` 可能同时出现在两个阵营（实测 6 份导出出现，全部是「邪悪なる死のカラス」token 30084 的镜像）。因此**合并必须用 `sameEntityKey`**；`identityGroups[].fieldSplit=true` 标记这种组，`mergeHint` 写明不可跨阵营合并。

## 3. 为什么 run.id / run.seq 不能跨进程当唯一键

| 观察 | 证据 |
|---|---|
| 30 份导出中 13 份的 run.id 与其它文件重复（run.id=1 出现 15 次，run.seq 全部为 0） | 实测（人口普查 §2） |
| 导出版本跨越 1.5.3→1.7.10、多次进程重启，run.id 每次都从 1 重新计数 | 实测：battle_9999_20261003_205129.json(1, 20:51) / battle_411001_20261003_235204.json(1, 23:52) / battle_411001_20261004_144548.json(1, 14:45) 都是 1 |
| 同一进程内的 run.seq 在语料中恒为 0，没有区分度 | 实测 |

结论：

- `(run.id, run.seq)` **只能**表示「本次进程内的第几场」，进程一重启即复位；
- 不能用它做跨场 join、去重、或判断「是不是同一场」；
- 本设计用**导出文件内容的 SHA256** 作为场次主键（`exportSha256`），并写进 `instanceKey`；人工注解也以 SHA256 关联；
- 即使 run.id/seq 相同、started 相同，只要 SHA256 不同，`same_entity()` 一律返回 `different_export`，只额外给 `sameBattleHint=true`（提示可能是同一场的二次导出，需人工确认，不自动合并）。

## 4. run_annotations.json 字段设计

位置：由使用者指定（拟建 `_dpsm_work/run_annotations.json`），**不属于插件导出，不被插件写入**。以导出 SHA256 关联人工确认的阵容、装备、等级、操作模式、关卡阶段与实验组。

```json
{
  "schema": "run-annotations/1.0",
  "generatedBy": "human",
  "generatedAt": "2026-10-04T15:00:00",
  "runs": [
    {
      "exportSha256": "<导出文件 64 位十六进制 SHA256>",
      "exportPath": "BepInEx/plugins/DpsMeter/exports/battle_....json",
      "confirmedBy": "<确认人>",
      "confirmedAt": "2026-10-04T15:00:00",
      "confirmed": {
        "roster": [
          {
            "name": "<导出中的 actors[].name 原文>",
            "templateId": "unknown",
            "slot": "unknown",
            "level": "unknown",
            "loadout": {
              "jobTrait":  [{"id": "unknown", "level": "unknown"}],
              "potential": [{"id": "unknown", "level": "unknown"}],
              "artifact":  [{"id": "unknown", "level": "unknown"}],
              "passive":   [{"id": "unknown", "level": "unknown"}],
              "sigil":     [{"id": "unknown", "level": "unknown"}]
            }
          }
        ],
        "levels": "unknown",
        "formation": "unknown",
        "mode": "unknown",
        "questPhase": "unknown",
        "experimentGroup": "unknown",
        "damageModel": "unknown",
        "notes": ""
      }
    }
  ]
}
```

模板可机械打印：`python _dpsm_work/identity_map.py --print-annotation-template`（ASCII 输出）。

字段语义与「不猜填」规则：

| 字段 | 含义 | 未知的表示 | 缺失时工具行为 |
|---|---|---|---|
| exportSha256 | 关联键，必填 64 位十六进制 | 无 | 结构校验报错，该 run 被忽略匹配 |
| confirmed.roster[].name | 阵容成员，须与导出字符串一致 | 无 | 匹配失败记入 `applied.rosterNamesNotInExport` |
| templateId / slot / level | 人工确认的角色 id/槽位/等级 | 字符串 `"unknown"` | 保持 unknown，**不用于推导身份**；只有显式数字才采用 |
| loadout.*[].id | 人工确认的装备/能力 id | `"unknown"` 或整个 loadout 缺失 | 仅当导出缺 abilities 时作为**降级**指纹来源，并打 `loadout_from_annotation_not_export` 警告、basis=annotation_roster_loadout |
| formation | 编队/前后卫 | `"unknown"` | slot 保持 null |
| mode | 操作模式（手动/自动等） | `"unknown"` | 不参与身份判定 |
| questPhase | 关卡阶段 | `"unknown"` | 不参与身份判定 |
| experimentGroup | 实验组标签 | `"unknown"` | 不参与身份判定 |
| damageModel | 是否含 atkadd 等模型开关 | `"unknown"` | 不参与身份判定 |

**版本化与缺字段兼容规则**：

1. 注解根有 schema 字段（当前 run-annotations/1.0）。不认识的 schema 记校验问题，不崩溃、不猜。
2. 未知顶层字段记 `unknown top-level annotation field`，**保留数据、不解释**（前向兼容）。
3. confirmed 下要求字段（roster/levels/formation/mode/questPhase/experimentGroup/damageModel）缺失 → 校验问题，提示写 `"unknown"`；**缺失本身不会让解析失败**。
4. 用 `""`、null、[] 冒充「未知」→ 记 `field written as empty instead of "unknown"` 问题（notes 允许空字符串，因为它不承载事实）。
5. 导出侧缺字段（旧文件没有 abilities/kind/summon）：解析**仍然成功**，输出弱身份 + `legacy_export_missing_identity_fields` / `abilities_missing_in_export` 等警告，绝不失败、绝不填零。
6. 同一角色在不同导出得到不同 entityKey（换装）是**预期行为**，不视为版本不兼容。

## 5. 身份强度与警告

identityClass：

- `resolved`：有真实模板 id（slot6/slot1 观测，或人工确认）；
- `derived`：模板 id 由 kind+name 哈希派生——只是本地判据，**不构成跨场身份**；
- `unresolved`：连 kind 都没有（残缺/旧文件），或 kind=P 但无 abilities（如 T.O.W.E.R.typeR）。

identityStrength：strong（resolved 且 char 命名空间或有构筑指纹）> medium（resolved 但只有模板 id、无指纹）> weak（其余，含全部 derived/unresolved）。`weakIdentity` 是布尔快捷位。

warnings 取值：abilities_missing_in_export、loadout_fingerprint_missing、template_id_unresolved、template_id_derived_from_kind_name、enemy_without_abilities_derived_template、legacy_export_missing_identity_fields、summon_owner_unknown、summon_flag_false_but_owner_link_seen、loadout_from_annotation_not_export、template_from_annotation_not_export、class_unknown。

## 6. same_entity(a, b) 判定表

判定顺序固定（先判「是不是同一份导出」，再判身份）：

| # | 条件 | verdict | 说明 |
|---|---|---|---|
| 1 | 不同 exportSha256 | different_export | 即使 run.id/seq/started 相同也不合并；附 sameBattleHint |
| 2 | 同导出且同 actorKey | same_entity | 场内唯一 |
| 3 | 同模板但阵营不同 | different_entity | 镜像/训练场单位 |
| 4 | summon 标记不同且一方 class=summon | different_entity | 召唤物不得并入本体 |
| 5 | 同为召唤物、同 owner、同模板 | same_template_distinct_instances | 一主可有多 token |
| 6 | templateId 不同（含 namespace 不同） | different_entity | |
| 7 | 同模板、loadoutFingerprint 不同 | same_template_different_loadout | **可拆组**，绝不合并 |
| 8 | 任一侧身份不全（无模板 id 或无指纹） | weak_identity | 名字相同只作为 reason，不作为依据 |
| 9 | 同模板、同指纹、actorKey 不同 | same_template_distinct_instances | 同场重复实例 |
| 10 | 同模板、同指纹、同名 | same_entity | 全部字段一致 |

**默认拒绝**：如果只满足「名字相同」，第 8 条返回 `weak_identity`，**永远不会**因为同名而返回 same_entity。

## 7. 与 N2 / N5 的接口

| 接口 | 内容 | 状态 |
|---|---|---|
| 输入 | 导出路径（只读）；可选 run_annotations.json | 已实现 |
| 输出 | identity-map/1.0 JSON（§8）：每 actor 一条记录 + 分组 + 同名冲突 + 人口普查 | 已实现 |
| 判定函数 | `same_entity(a, b) -> {verdict, reasons, evidence, sameBattleHint}` | 已实现，可被 N2 直接调用 |
| CLI | `--export PATH [--annotations PATH] [--out JSON] [--selftest] [--print-annotation-template]` | 已实现 |
| 给 N2 的建议 | 分组键用 entityKey（同角色同配置），**不要**用 name；合并前必须过 same_entity()；跨阵营用 sameEntityKey | 建议，非本次改动 |
| 给 N5 的建议 | 用 annotations.applied 判断实验组/模式是否已知；未知配置保持 unknown 并标注，不默认混算 | 建议 |

## 8. 输出 JSON 结构（identity-map/1.0）

```
{
  "schema": "identity-map/1.0",
  "tool": "identity_map.py", "toolVersion": "N3-P1-1.0",
  "generatedFrom": { exportPath, exportSha256, exportVersion, quest, started, result,
                     runId, runSeq, runIdIsGlobalKey:false, runIdNote },
  "annotations": { provided, matchedExportSha256, schema, orphanSha256, applied, validationProblems, validationInfo },
  "actors": [ { actorKey, exportSha256, exportStarted, exportVersion, questId, name, team, class, kind,
                summon, ownerName, ownerKey, role, slot, slotBasis, templateId, templateNamespace,
                templateBasis, templateEvidence, typeKey, instanceKey, entityKey, sameEntityKey,
                loadoutFingerprint, loadoutDetailHash, loadoutIdFingerprint, loadoutFingerprintBasis,
                equipmentIds, equipmentIdsFromAnnotation, jobTraitId, potentialId, tokenId, abilityCount,
                identityClass, identityStrength, weakIdentity, warnings, annotationStatus, evidence } ],
  "identityGroups": [ { entityKey, typeKey, templateId, templateBasis, loadoutFingerprint, names, teamSet,
                        actorKeys, classes, sameEntityKeys, actorCount, weakOnly, fieldSplit, mergeHint } ],
  "nameCollisions": [ { name, count, teamSet, templateIds, loadoutFingerprints, distinctEntities,
                        autoMergeAllowed:false, note } ],
  "census": { ... 见人口普查报告 ... },
  "evidenceLevels": { ... }
}
```

`role` 字段当前为 null：导出的 kind/class 只能说明「P/Boss/召唤物」，不能说明治疗/输出等队内职责；职责只能来自人工注解，本阶段不猜。

## 9. 本设计**不做**的事

- 不改插件、不改导出 schema、不改 DLL、不部署；
- 不修改 BepInEx/ 下任何文件，不改 contrib/compare.py（N2 独占）；
- 不引入持久角色 id 的假设；
- 不把派生哈希当作游戏 id；
- 不用 run.id/seq 做跨场键；
- 不因为「同名」合并任何 actor。
