# 工具注册表(RF7)

> 本文件由 `check_tool_registry.py --write` 从 `tool_registry.json` **生成**,不要手改。
> 机械字段(行数/首行说明/本地导入/是否在验收流水线/是否有自测)来自 `tool_census.py` 的扫描;
> 判断字段(kind / status / expected_exit / outputs / input_versions / evidence)由人工维护。

| 项 | 值 |
|---|---|
| 已注册脚本 | 96 |
| 其中活跃(有 CLI、有预期退出码与输出声明) | 27 |
| 尚未判定(只登记,计数只能下降) | 5 / 上限 5 |
| 验收流水线实际运行的脚本 | 21 |

## 活跃工具

| 脚本 | 类别 | 用途 | 预期退出码 | 输出 | 自测 | 在流水线 |
|---|---|---|---|---|---|---|
| `_dpsm_work/archive_index.py` | cli | RF7c: record every historical evidence file (evidence_*/probe_*/review_contrib_core) with size and sha256, and fail when one changes, vanishes or appears unrecorded. | 0 clean / 1 findings | archive-index.json; ARCHIVE-INDEX.md | no | yes |
| `_dpsm_work/batch_snapshot.py` | cli | RF0 section 5.5: freeze ONE batch input list as a hard-linked snapshot, independent of a live game session. | 0 ok / 1 error | batch_inputs/<name> (hard-linked snapshot) | no | no |
| `_dpsm_work/budget_census.py` | census | N6 budget & residual census (read-only over the export corpus). | 0 = 普查/自测通过;1 = 自测红或有预算异常 | BUDGET-CENSUS.md/.json 或 --out | yes | yes |
| `_dpsm_work/check_contribution_layout.py` | guard | Contribution table LAYOUT guard (added with 1.7.7). | 0 = 布局合规;1 = 有布局回归 | none (read-only over exports) | yes | yes |
| `_dpsm_work/check_doc_convergence.py` | guard | P1-B acceptance guard: the five main documents must not contradict the repository. | 0 = 无矛盾(0/11);1 = 有 R1–R11 违规 | none (read-only) | yes | yes |
| `_dpsm_work/check_docs_123.py` | guard | Encoding guard for the CURRENT doc set (2026-10-03 cleanup; extended 2026-10-04 round 6). | 0 = 文档集干净;1 = 混入损坏标记或缺文件 | none (read-only) | yes | yes |
| `_dpsm_work/check_export_schema.py` | guard | DpsMeter export schema checker + coverage reporter  (1.5.2) | 0 = schema 合规;1 = schema 违规 | none (read-only) | yes | yes |
| `_dpsm_work/check_fact_signature.py` | guard | FACT signature replay -- offline, BEFORE the next battle. | 0 = 无异常;1 = 有事实签名异常 | --outdir 下的报告 | yes | yes |
| `_dpsm_work/check_given_fold_coupling.py` | guard | Guard (1.7.9): the General/GivenTalent switch must drive BOTH halves of the granted-talent rule. | 0 = 取消语义自洽;1 = 有耦合回归 | none (read-only) | yes | yes |
| `_dpsm_work/check_live_log.py` | guard | Live-log acceptance gate (1.7.9): the MACHINE's own log against the exports it wrote. | 0 = 无矛盾;1 = 面板/live 帧与导出不一致 | none (read-only) | yes | yes |
| `_dpsm_work/check_p2a_summary_and_lastbattle.py` | guard | P2-A offline guard: (6) the finished-battle summary carries the unattributed pool, | 0 = 摘要/上一场自洽;1 = 回归 | none (read-only) | no | yes |
| `_dpsm_work/check_tool_registry.py` | guard | RF7 (plan section 12): the tool registry guard. | 0 = 注册表与现实一致;1 = A–F 任一失败或自测红 | TOOL-REGISTRY.md(--write) | yes | yes |
| `_dpsm_work/comparison_eligibility.py` | census | ComparisonEligibility -- the N1 admission contract (CONTRIBUTION-NEXT-PHASE-ROADMAP.md section N1). | 0 = 计算完成;1 = 无可比数据 | --out 指定的报告 | yes | yes |
| `_dpsm_work/contrib/compare.py` | census | Cross-battle comparison 2.0 (roadmap N2). | 0 = 通过;1 = 有差异/自测失败 | --out 报告 | yes | yes |
| `_dpsm_work/contrib/crosscheck.py` | gate | Phase E acceptance: cross-implementation check (plugin contribution vs offline core). | 0 = 通过;1 = 有断言失败 | none (read-only) | yes | yes |
| `_dpsm_work/contrib/tests/test_gate.py` | gate | P0-A gate regression: prove the fixed gates now cave in where they used to exit 0. | 0 = 通过;1 = 有断言失败 | none (read-only) | yes | yes |
| `_dpsm_work/contribution_applicability.py` | census | P0-D: task applicability scanner for DpsMeter battle exports. | 0 = 生成;1 = 语料不足 | applicability.json(--out) | yes | yes |
| `_dpsm_work/contribution_gate.py` | cli | Contribution validation GATE: one status vocabulary and one exit-code contract. | 0 ok / 1 findings | none (read-only) | no | no |
| `_dpsm_work/decision_report.py` | report | N5 decision report: from the audited comparison layer to a decision (roadmap N5). | 0 = 生成;1 = 数据不足 | --out 报告(MD/JSON) | yes | yes |
| `_dpsm_work/identity_map.py` | cli | N3 / P1 stage-1 offline cross-battle identity mapping (read-only). | 0 ok / 1 findings | none (read-only) | yes | no |
| `_dpsm_work/n0_acceptance.py` | cli | N0 acceptance closure for the CONTRIBUTION-NEXT-PHASE-ROADMAP.md (section N0). | 0 ok / 1 findings | acceptance archive under --out (RESULTS.md, runs.json, corpus_manifest.json) | yes | no |
| `_dpsm_work/pairtrusted_impact.py` | report | P0-C: offline quantification of the PairTrusted / PairCorroborated effect on contribution. | 0 = 生成;1 = 数据不足 | pairtrusted_impact_report.json/.txt(已在 .gitignore 声明为派生) | yes | yes |
| `_dpsm_work/refactor_final_check.py` | guard | Integrity + consistency check of the refactored source tree -- with REAL blocking (N1). | 0 = 无孤儿/无违规;1 = 有 blocks | none (read-only) | yes | yes |
| `_dpsm_work/repo_manifest.py` | guard | RF0: the machine-readable half of the local Git baseline. | 0 = 无漂移(drift=0);1 = 有漂移 | baseline-manifest.json(--write) | no | yes |
| `_dpsm_work/tests/negative_control.py` | cli | RF1 negative control: the behaviour suite must be able to go RED. See REFACTOR-PLAN section 6. | 0 ok / 1 findings | stdout mutation report (no files written) | yes | no |
| `_dpsm_work/tool_census.py` | census | RF7 (plan section 12): the MECHANICAL half of the tool registry. | 0 = 扫描/合并完成 | tool_registry.json(--seed) | yes | no |
| `_dpsm_work/v150_validate.py` | guard | 1.5.0 runtime validation -- one command for the next battle. | 0 = 无问题;1 = 有需人工看的项 | --out 报告 | yes | yes |

## 尚未判定的脚本(只登记)

| 脚本 | 行数 | 首行说明 | 本地导入 |
|---|---|---|---|
| `_dpsm_work/atkadd_sensitivity.py` | 909 | atkadd sensitivity study (N4/P1), read-only and offline. |  |
| `_dpsm_work/compare_comps.py` | 54 | Compare the 1.5.4 real battles: totals, boss HP, madness uptime, per-character output. |  |
| `_dpsm_work/contrib/__init__.py` | 14 | Contribution analysis core (Stage B, 2026-10-03). |  |
| `_dpsm_work/contrib/aggregate.py` | 273 | Log-share split and aggregation. Implements dictionary sections 2 and 4. |  |
| `_dpsm_work/contrib/attribution.py` | 99 | fold -> rule owner. Implements the ladder of dictionary section 4. |  |
| `_dpsm_work/contrib/legacy_diff.py` | 110 | Regression evidence: does the new core reproduce the legacy Stage-0 table? |  |
| `_dpsm_work/contrib/loader.py` | 81 | Export JSON -> typed data. No attribution, no math beyond indexing. |  |
| `_dpsm_work/contrib/model.py` | 160 | Data model for the contribution core. Pure data, no I/O, no business logic. |  |
| `_dpsm_work/contrib/phase_e_dryrun.py` | 123 | Phase E dry run: would the acceptance gates accept a real contribution section? | check_export_schema.py |
| `_dpsm_work/contrib/report_json.py` | 181 | Contribution JSON draft (schemaVersion 0.1-draft). Not part of the plugin export. |  |
| `_dpsm_work/contrib/report_text.py` | 208 | Text reports: the full v1 report and the legacy (Stage 0) compatible table. |  |
| `_dpsm_work/contrib/rule115_census.py` | 155 | Phase G item 1 (offline, GO per REVIEW-phaseG-recon): the 1.15 census. |  |
| `_dpsm_work/contrib/run.py` | 133 | CLI: python -m contrib.run [export.json] [--team 1] [--legacy] | contribution_gate.py |
| `_dpsm_work/contrib/tests/__init__.py` | 1 |  |  |
| `_dpsm_work/contrib/tests/test_golden_155.py` | 145 | Golden test on the real 1.5.5 export: pins every number the project publishes. | contribution_gate.py |
| `_dpsm_work/contrib/tests/test_samples.py` | 195 | Hand-sample tests from CONTRIBUTION-DATA-DICTIONARY.md section 7 (S1-S6). |  |
| `_dpsm_work/contrib/validate.py` | 236 | Identity, accounting and coverage checks (dictionary section 9). | contribution_gate.py |
| `_dpsm_work/contrib_gap_dealt.py` | 35 | Pin down the totals.dealt vs per-event-sum gap (HANDOFF todo 4). |  |
| `_dpsm_work/contrib_recon_155.py` | 126 | Reconnaissance of a 1.5.5 export for the contribution core (Phase A/B/C). |  |
| `_dpsm_work/contrib_recon_155b.py` | 76 | Second recon: rule identity (kind/side/origin/label -> owner) + cancel shape. |  |
| `_dpsm_work/contrib_recon_155c.py` | 93 | Third recon: attribution correctness checks. ASCII stdout; CJK -> contrib_recon_155c.txt |  |
| `_dpsm_work/contrib_recon_155d.py` | 77 | Fourth recon: do holder-owned 'text' folds duplicate the global folds on the same hit? |  |
| `_dpsm_work/evidence_1152_probe_151.py` | 138 | Ad-hoc 1.5.1 first-run diagnosis. ASCII stdout, UTF-8 report. |  |
| `_dpsm_work/evidence_1152_residuals_150_vs_151.py` | 99 | Compare residual structure between the 1.5.0 battle and the 1.5.1 battle, and dump |  |
| `_dpsm_work/evidence_152live_compare.py` | 75 | 1.5.2 vs 1.5.1 export comparison: counting, hitValue, residual structure. |  |
| `_dpsm_work/evidence_152live_export_probe.py` | 42 | 1.5.2 real-machine export probe: P2 counting gap, hitValue presence, residual structure. |  |
| `_dpsm_work/evidence_152live_gap.py` | 30 | Where did the 1.5.1 battle's ~166 counted-but-missing damage events go? |  |
| `_dpsm_work/evidence_152live_hitvalue.py` | 51 | What IS the game's calc return? Compare hitValue against our reconstructed theory / power / amount. |  |
| `_dpsm_work/evidence_152live_mint.py` | 15 | Does the event stream start at t=0? Scan recent exports. |  |
| `_dpsm_work/evidence_152live_offsets.py` | 16 |  |  |
| `_dpsm_work/evidence_152live_p0.py` | 34 | P0: cross-tab residual vs maxAbsorbed (fold depth) on the 1.5.2 real-machine export. |  |
| `_dpsm_work/evidence_152live_p0b.py` | 21 | Same P0 cross-tab on the 1.5.1 export, plus attacker identification of the residual population. |  |
| `_dpsm_work/evidence_152live_p2_scan.py` | 72 | P2 invariant scan: matchExact+matchPair+matchNone == factCoverage.dmg, across export history. |  |
| `_dpsm_work/evidence_152live_roster.py` | 12 |  |  |
| `_dpsm_work/evidence_153_replay.py` | 45 | OFFLINE REPLAY of what 1.5.3 would report on an ALREADY-RECORDED battle, from the per-hit data alone. |  |
| `_dpsm_work/evidence_153live.py` | 236 | 1.5.3 LIVE verification: runs the falsification table of DpsMeter-1.5.3 section 6 on the first |  |
| `_dpsm_work/evidence_contrib_join.py` | 146 | Feasibility join for per-character contribution: how much of the fold-mass can CURRENT exports |  |
| `_dpsm_work/evidence_mad_2x2.py` | 38 | Is the missing x1.5 attacker-side (狂気 on the attacker) or victim-side (狂気 on the target)? |  |
| `_dpsm_work/evidence_mad_ctrl.py` | 54 | ---- victim madness, controlled by (attacker, skill, enemy-status-set) ---- |  |
| `_dpsm_work/evidence_p0_cancel.py` | 43 | Offline test of the P0 hypothesis: do the CANCELLED granted copies actually stack in the game? |  |
| `_dpsm_work/evidence_p0_crit.py` | 44 | --- corrected P0: modeled exponent vs residual exponent --- |  |
| `_dpsm_work/evidence_p0_data.py` | 33 | 1. what keys does a calc carry? |  |
| `_dpsm_work/evidence_p0_data2.py` | 27 |  |  |
| `_dpsm_work/evidence_p0_x1152.py` | 31 | aggregate: how often is a "vs poisoned/burned enemy" conditional rule present but NOT folded? |  |
| `_dpsm_work/evidence_roster_cmp.py` | 17 |  |  |
| `_dpsm_work/evidence_why_lower1.py` | 22 |  |  |
| `_dpsm_work/evidence_why_lower10.py` | 29 | Counterfactual: the removed rule [海魔の残滓] grants x1.15 per 毒/火傷 on the enemy. |  |
| `_dpsm_work/evidence_why_lower2.py` | 58 | Where does the damage go? Decompose per attacker, then per (attacker, skill) compare |  |
| `_dpsm_work/evidence_why_lower3.py` | 48 | residual distributions |  |
| `_dpsm_work/evidence_why_lower4.py` | 25 |  |  |
| `_dpsm_work/evidence_why_lower5.py` | 42 |  |  |
| `_dpsm_work/evidence_why_lower6.py` | 52 | Collect every distinct 与伤害补正 rule phrase seen in comp2/comp3 of damage events. |  |
| `_dpsm_work/evidence_why_lower7.py` | 34 | how often the two key statuses co-occur |  |
| `_dpsm_work/evidence_why_lower8.py` | 23 |  |  |
| `_dpsm_work/evidence_why_lower9.py` | 42 | madnessRatio field |  |
| `_dpsm_work/madness_owner_check.py` | 32 | Who is mad on OUR side, and are the guest-unreadable hook rows that same unit (self-application)? |  |
| `_dpsm_work/probe_contrib_struct.py` | 55 | Structural probe for the contribution-attribution question: what identity/source info does a |  |
| `_dpsm_work/probe_join_detail.py` | 66 | Detail probe: (a) all (talentType,param) pairs in actors, (b) ambiguous ability names -> holders, |  |
| `_dpsm_work/probe_recent.py` | 54 | Summary of recent battles + 1.5.4 channel verification. ASCII stdout; UTF-8 report file. |  |
| `_dpsm_work/review_contrib_core/audit_contrib_core.py` | 590 | Adversarial independent audit of _dpsm_work/contrib (contribution core). |  |
| `_dpsm_work/rules_census.py` | 72 | Per-rule fold-mass census for two battles + the differing units' ability lists. |  |
| `_dpsm_work/sample_intake.py` | 34 | Probe the two newest exports (round 6 intake). Read-only; prints ASCII facts. |  |
| `_dpsm_work/tests/il_equiv.py` | 87 | RF2 evidence: token-independent comparison of two IlDump transcripts. |  |
| `_dpsm_work/tests/rf2_split.py` | 148 | RF2: mechanical split of src/Aggregator.cs into partial files. |  |
| `_dpsm_work/umima_check.py` | 50 | What exactly is 海魔の残滓 in comp B, and how does 母なる変異の飛沫 compare? |  |
| `_dpsm_work/verify_155.py` | 45 | 1.5.5 acceptance: self-applied madness attribution + give-applier hook. |  |
| `_dpsm_work/victim_check.py` | 27 | Control: same enemy set / same front-line structure in the two battles? |  |
| `dpsmeter_analyze.py` | 201 | DpsMeter 离线精细分析器 |  |
| `dpsmeter_contrib.py` | 164 | DpsMeter per-character contribution analyzer (Stage 0, 2026-10-03). |  |
