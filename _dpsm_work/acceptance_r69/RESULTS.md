# N0 acceptance run (corpus manifest + guard runs)

- generated: 2026-10-06T15:08:17
- exports: 35 files in `D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\batch_inputs\rf0`
- source BuildInfo version: 1.7.20 ; deployed DLL sha256: 9E66D3EE9D89222896A33BED6AD6B98660C66B324C6218079F8CDC3EF28182AF
- config sha256: 8C6FA53D174428ECE18D21ABEAD59494A0C419CEB9614E08324A44CACDF14210 (contrib switches: Contribution=true, GivenGiverHook=true, GivenTalent=true, Madness=true, MadnessApplier=true, MadnessVictim=true, ShowContribution=true)

## expected vs observed (falsifiable)

Expectation = the verified 32-file snapshot + the verdict pinned per file added since; every pin is compared.

| kind | bucket | expected | observed | ok |
|---|---|---|---|---|
| crosscheck_batch | ERROR | 1 | 1 | yes |
| crosscheck_batch | LEGACY_NOT_APPLICABLE | 17 | 17 | yes |
| crosscheck_batch | PASS | 14 | 14 | yes |
| crosscheck_batch | WARNING | 3 | 3 | yes |
| crosscheck_embedded | ERROR | 1 | 1 | yes |
| crosscheck_embedded | LEGACY_NOT_APPLICABLE | 4 | 4 | yes |
| crosscheck_embedded | NOT_RUN | 13 | 13 | yes |
| crosscheck_embedded | PASS | 14 | 14 | yes |
| crosscheck_embedded | WARNING | 3 | 3 | yes |
| applicability | full | 16 | 16 | yes |
| applicability | not_comparable | 10 | 10 | yes |
| applicability | partial | 9 | 9 | yes |
| identity | batch.LEGACY == embedded.NOT_RUN + embedded.LEGACY | 17 | 17 | yes |
| known_bad | battle_411001_20261004_015919.json | 1 | 1 | yes |
| run_exit | applicability(all) | 0 | 0 | yes |
| run_exit | crosscheck(batch) | 1 | 1 | yes |
| run_exit | pairtrusted(all) | 0 | 0 | yes |
| run_exit | export_schema(all) | 0 | 0 | yes |
| run_exit | layout(all) | 0 | 0 | yes |
| run_exit | live_log | 0 | 0 | yes |
| run_exit | selftest/layout | 0 | 0 | yes |
| run_exit | selftest/schema | 0 | 0 | yes |
| run_exit | selftest/live_log | 0 | 0 | yes |
| run_exit | doc_convergence | 0 | 0 | yes |
| run_exit | selftest/doc_convergence | 0 | 0 | yes |
| run_exit | docs123 | 0 | 0 | yes |
| run_exit | selftest/docs123 | 0 | 0 | yes |
| run_exit | test_gate | 0 | 0 | yes |
| run_exit | given_coupling | 0 | 0 | yes |
| run_exit | selftest/given_coupling | 0 | 0 | yes |
| run_exit | p2a | 0 | 0 | yes |
| run_exit | factsig | 0 | 0 | yes |
| run_exit | selftest/applicability | 0 | 0 | yes |
| run_exit | selftest/pairtrusted | 0 | 0 | yes |
| run_exit | selftest/attribution_census | 0 | 0 | yes |
| run_exit | selftest/extract_verify | 0 | 0 | yes |
| run_exit | selftest/negctl_driver | 0 | 0 | yes |
| run_exit | extract_contract | 0 | 0 | yes |
| run_exit | selftest/extract_contract | 0 | 0 | yes |
| run_exit | battle_select | 0 | 0 | yes |
| run_exit | selftest/battle_select | 0 | 0 | yes |
| run_exit | refactor_final_check | 0 | 0 | yes |
| run_exit | selftest/refactor | 0 | 0 | yes |
| run_exit | tool_registry | 0 | 0 | yes |
| run_exit | selftest/tool_registry | 0 | 0 | yes |
| run_exit | archive_index | 0 | 0 | yes |
| run_exit | selftest/archive_index | 0 | 0 | yes |
| run_exit | v150_validate | 0 | 0 | yes |
| run_exit | selftest/v150 | 0 | 0 | yes |
| run_exit | selftest/eligibility | 0 | 0 | yes |
| run_exit | selftest/eligibility_e2e | 0 | 0 | yes |
| run_exit | selftest/compare | 0 | 0 | yes |
| run_exit | selftest/decision | 0 | 0 | yes |
| run_exit | selftest/budget | 0 | 0 | yes |
| run_exit | repo_manifest_verify | 0 | 0 | yes |
| run_exit | recon_probe | 0 | 0 | yes |
| run_exit | csharp_behavior | 0 | 0 | yes |
| run_exit | csharp_behavior_negctl | 0 | 0 | yes |
| allowlist_used | crosscheck(batch) | present | present | yes |
| per_file | battle_411001_20261004_144548.json.crosscheck | PASS | PASS | yes |
| per_file | battle_411001_20261004_144548.json.applicability | full | full | yes |
| per_file | battle_411001_20261004_170157.json.crosscheck | PASS | PASS | yes |
| per_file | battle_411001_20261004_170157.json.applicability | full | full | yes |
| per_file | battle_411001_20261004_180353.json.crosscheck | PASS | PASS | yes |
| per_file | battle_411001_20261004_180353.json.applicability | full | full | yes |
| per_file | battle_411001_20261004_180834.json.crosscheck | PASS | PASS | yes |
| per_file | battle_411001_20261004_180834.json.applicability | full | full | yes |
| per_file | battle_9999_20261004_165952.json.crosscheck | LEGACY_NOT_APPLICABLE | LEGACY_NOT_APPLICABLE | yes |
| per_file | battle_9999_20261004_165952.json.applicability | not_comparable | not_comparable | yes |
| per_file | battle_9999_20261004_175957.json.crosscheck | LEGACY_NOT_APPLICABLE | LEGACY_NOT_APPLICABLE | yes |
| per_file | battle_9999_20261004_175957.json.applicability | not_comparable | not_comparable | yes |
| input_freeze | file count | 35 | 35 | yes |
| input_freeze | file set | 0 missing / 0 extra | 0 missing / 0 extra | yes |
| output_isolation | changes outside --out | 0 undeclared | 0 undeclared | yes |
| current_output | _dpsm_work/export_schema_selftest.txt | declared | written | yes |
| current_output | _dpsm_work/v150_validate.txt | declared | written | yes |

| tool | bucket | expected | observed |
|---|---|---|---|
| batch | ERROR | 1 | 1 |
| batch | LEGACY_NOT_APPLICABLE | 17 | 17 |
| batch | PASS | 14 | 14 |
| batch | WARNING | 3 | 3 |
| embedded | ERROR | 1 | 1 |
| embedded | LEGACY_NOT_APPLICABLE | 4 | 4 |
| embedded | NOT_RUN | 13 | 13 |
| embedded | PASS | 14 | 14 |
| embedded | WARNING | 3 | 3 |

**Labeling divergence (documented, tracked by N1):** a file with no contribution section is LEGACY_NOT_APPLICABLE to `crosscheck --batch` and NOT_RUN to the applicability report: same fact, two words. Tracked by N1 (an absent section must not pass as legacy-ok).

**Batch inputs and side effects.** The input list is frozen by name (35 files); the watch
roots below are re-stat-ed around the whole run, so the historical archives must show no change at all:

- _dpsm_work (files only)
-  (files only)
- _dpsm_work/acceptance_1.7.11 (recursive)
- _dpsm_work/contrib/reports (recursive)

Files the applicability report labels NOT_RUN (13): battle_411001_20261003_205449.json, battle_411001_20261003_230040.json, battle_411001_20261003_230311.json, battle_411001_20261003_230615.json, battle_411001_20261003_231044.json, battle_411001_20261003_231340.json, battle_411001_20261003_235204.json, battle_411001_20261004_012328.json, battle_9999_20261003_205129.json, battle_9999_20261003_225530.json, battle_9999_20261003_225647.json, battle_9999_20261003_225905.json, battle_9999_20261004_012138.json

## guard runs

| run | exit | seconds | log |
|---|---|---|---|
| applicability(all) | 0 | 34.05 | `_dpsm_work/acceptance_r69/runs/applicability_all_.txt` |
| crosscheck(batch) | 1 | 16.91 | `_dpsm_work/acceptance_r69/runs/crosscheck_batch_.txt` |
| pairtrusted(all) | 0 | 43.0 | `_dpsm_work/acceptance_r69/runs/pairtrusted_all_.txt` |
| export_schema(all) | 0 | 11.91 | `_dpsm_work/acceptance_r69/runs/export_schema_all_.txt` |
| layout(all) | 0 | 2.49 | `_dpsm_work/acceptance_r69/runs/layout_all_.txt` |
| live_log | 0 | 0.59 | `_dpsm_work/acceptance_r69/runs/live_log.txt` |
| selftest/layout | 0 | 8.5 | `_dpsm_work/acceptance_r69/runs/selftest_layout.txt` |
| selftest/schema | 0 | 0.13 | `_dpsm_work/acceptance_r69/runs/selftest_schema.txt` |
| selftest/live_log | 0 | 0.16 | `_dpsm_work/acceptance_r69/runs/selftest_live_log.txt` |
| doc_convergence | 0 | 0.29 | `_dpsm_work/acceptance_r69/runs/doc_convergence.txt` |
| selftest/doc_convergence | 0 | 1.93 | `_dpsm_work/acceptance_r69/runs/selftest_doc_convergence.txt` |
| docs123 | 0 | 0.18 | `_dpsm_work/acceptance_r69/runs/docs123.txt` |
| selftest/docs123 | 0 | 0.17 | `_dpsm_work/acceptance_r69/runs/selftest_docs123.txt` |
| test_gate | 0 | 7.53 | `_dpsm_work/acceptance_r69/runs/test_gate.txt` |
| given_coupling | 0 | 0.1 | `_dpsm_work/acceptance_r69/runs/given_coupling.txt` |
| selftest/given_coupling | 0 | 0.09 | `_dpsm_work/acceptance_r69/runs/selftest_given_coupling.txt` |
| p2a | 0 | 24.37 | `_dpsm_work/acceptance_r69/runs/p2a.txt` |
| factsig | 0 | 0.6 | `_dpsm_work/acceptance_r69/runs/factsig.txt` |
| selftest/applicability | 0 | 0.14 | `_dpsm_work/acceptance_r69/runs/selftest_applicability.txt` |
| selftest/pairtrusted | 0 | 21.62 | `_dpsm_work/acceptance_r69/runs/selftest_pairtrusted.txt` |
| selftest/attribution_census | 0 | 0.14 | `_dpsm_work/acceptance_r69/runs/selftest_attribution_census.txt` |
| selftest/extract_verify | 0 | 0.15 | `_dpsm_work/acceptance_r69/runs/selftest_extract_verify.txt` |
| selftest/negctl_driver | 0 | 6.52 | `_dpsm_work/acceptance_r69/runs/selftest_negctl_driver.txt` |
| extract_contract | 0 | 0.1 | `_dpsm_work/acceptance_r69/runs/extract_contract.txt` |
| selftest/extract_contract | 0 | 0.13 | `_dpsm_work/acceptance_r69/runs/selftest_extract_contract.txt` |
| battle_select | 0 | 0.14 | `_dpsm_work/acceptance_r69/runs/battle_select.txt` |
| selftest/battle_select | 0 | 0.31 | `_dpsm_work/acceptance_r69/runs/selftest_battle_select.txt` |
| refactor_final_check | 0 | 0.3 | `_dpsm_work/acceptance_r69/runs/refactor_final_check.txt` |
| selftest/refactor | 0 | 5.52 | `_dpsm_work/acceptance_r69/runs/selftest_refactor.txt` |
| tool_registry | 0 | 0.22 | `_dpsm_work/acceptance_r69/runs/tool_registry.txt` |
| selftest/tool_registry | 0 | 0.26 | `_dpsm_work/acceptance_r69/runs/selftest_tool_registry.txt` |
| archive_index | 0 | 0.09 | `_dpsm_work/acceptance_r69/runs/archive_index.txt` |
| selftest/archive_index | 0 | 0.09 | `_dpsm_work/acceptance_r69/runs/selftest_archive_index.txt` |
| v150_validate | 0 | 9.99 | `_dpsm_work/acceptance_r69/runs/v150_validate.txt` |
| selftest/v150 | 0 | 14.06 | `_dpsm_work/acceptance_r69/runs/selftest_v150.txt` |
| selftest/eligibility | 0 | 0.99 | `_dpsm_work/acceptance_r69/runs/selftest_eligibility.txt` |
| selftest/eligibility_e2e | 0 | 2.41 | `_dpsm_work/acceptance_r69/runs/selftest_eligibility_e2e.txt` |
| selftest/compare | 0 | 42.95 | `_dpsm_work/acceptance_r69/runs/selftest_compare.txt` |
| selftest/decision | 0 | 79.29 | `_dpsm_work/acceptance_r69/runs/selftest_decision.txt` |
| selftest/budget | 0 | 0.54 | `_dpsm_work/acceptance_r69/runs/selftest_budget.txt` |
| repo_manifest_verify | 0 | 7.25 | `_dpsm_work/acceptance_r69/runs/repo_manifest_verify.txt` |
| recon_probe | 0 | 1.96 | `_dpsm_work/acceptance_r69/runs/recon_probe.txt` |
| csharp_behavior | 0 | 2.73 | `_dpsm_work/acceptance_r69/runs/csharp_behavior.txt` |
| csharp_behavior_negctl | 0 | 16.58 | `_dpsm_work/acceptance_r69/runs/csharp_behavior_negctl.txt` |

## per-file verdicts

| file | version | quest | schema | crosscheck | applicability |
|---|---|---|---|---|---|
| battle_411001_20261003_205449.json | 1.5.3 | 411001 | - | NOT_RUN | partial |
| battle_411001_20261003_230040.json | 1.5.4 | 411001 | - | NOT_RUN | partial |
| battle_411001_20261003_230311.json | 1.5.4 | 411001 | - | NOT_RUN | partial |
| battle_411001_20261003_230615.json | 1.5.4 | 411001 | - | NOT_RUN | partial |
| battle_411001_20261003_231044.json | 1.5.4 | 411001 | - | NOT_RUN | partial |
| battle_411001_20261003_231340.json | 1.5.4 | 411001 | - | NOT_RUN | partial |
| battle_411001_20261003_235204.json | 1.5.5 | 411001 | - | NOT_RUN | partial |
| battle_411001_20261004_012328.json | 1.5.5 | 411001 | - | NOT_RUN | partial |
| battle_411001_20261004_015919.json | 1.6.0 | 411001 | 1.0 | ERROR | not_comparable |
| battle_411001_20261004_021914.json | 1.6.1 | 411001 | 1.0 | PASS | full |
| battle_411001_20261004_023627.json | 1.7.0 | 411001 | 1.0 | PASS | full |
| battle_411001_20261004_033231.json | 1.7.2 | 411001 | 1.0 | PASS | full |
| battle_411001_20261004_035437.json | 1.7.3 | 411001 | 1.0 | PASS | full |
| battle_411001_20261004_041309.json | 1.7.4 | 411001 | 1.0 | PASS | full |
| battle_411001_20261004_042637.json | 1.7.5 | 411001 | 1.0 | PASS | full |
| battle_411001_20261004_042844.json | 1.7.5 | 411001 | 1.0 | PASS | full |
| battle_411001_20261004_113553.json | 1.7.5 | 411001 | 1.0 | PASS | full |
| battle_411001_20261004_115024.json | 1.7.6 | 411001 | 1.0 | PASS | full |
| battle_411001_20261004_115417.json | 1.7.6 | 411001 | 1.0 | WARNING | full |
| battle_411001_20261004_134524.json | 1.7.8 | 411001 | 1.1 | WARNING | full |
| battle_411001_20261004_134853.json | 1.7.8 | 411001 | 1.1 | PASS | full |
| battle_411001_20261004_144548.json | 1.7.10 | 411001 | 1.1 | PASS | full |
| battle_411001_20261004_170157.json | 1.7.11 | 411001 | 1.1 | PASS | full |
| battle_411001_20261004_180353.json | 1.7.11 | 411001 | 1.1 | PASS | full |
| battle_411001_20261004_180834.json | 1.7.11 | 411001 | 1.1 | PASS | full |
| battle_700817_20261004_114821.json | 1.7.6 | 700817 | 1.0 | WARNING | partial |
| battle_9999_20261003_205129.json | 1.5.3 | 9999 | - | NOT_RUN | not_comparable |
| battle_9999_20261003_225530.json | 1.5.4 | 9999 | - | NOT_RUN | not_comparable |
| battle_9999_20261003_225647.json | 1.5.4 | 9999 | - | NOT_RUN | not_comparable |
| battle_9999_20261003_225905.json | 1.5.4 | 9999 | - | NOT_RUN | not_comparable |
| battle_9999_20261004_012138.json | 1.5.5 | 9999 | - | NOT_RUN | not_comparable |
| battle_9999_20261004_042458.json | 1.7.5 | 9999 | 1.0 | LEGACY_NOT_APPLICABLE | not_comparable |
| battle_9999_20261004_135214.json | 1.7.8 | 9999 | 1.1 | LEGACY_NOT_APPLICABLE | not_comparable |
| battle_9999_20261004_165952.json | 1.7.11 | 9999 | 1.1 | LEGACY_NOT_APPLICABLE | not_comparable |
| battle_9999_20261004_175957.json | 1.7.11 | 9999 | 1.1 | LEGACY_NOT_APPLICABLE | not_comparable |

