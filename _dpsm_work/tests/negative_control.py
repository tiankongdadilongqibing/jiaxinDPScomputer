# -*- coding: utf-8 -*-
"""RF1 negative control: the behaviour suite must be able to go RED. See REFACTOR-PLAN section 6.

A gate nobody has seen fail is not a gate. This driver mutates a TEMP COPY of `src` (never the real
sources), rebuilds the SAME BehaviorTests project against it via -p:SrcRoot, and requires the named
case to fail. Three rules make it a real check rather than a ritual:

  1. a mutation whose `find` text does not occur EXACTLY once is a driver failure -- a rename that
     silently stops matching must not look like a passing control;
  2. the expected case must appear as `FAIL <group>/<label>` in the output, not merely "some failure";
  3. `comment-only-control` changes PROSE only and must leave the suite GREEN, which is what proves the
     red results come from behaviour and not from "we rebuilt something".

Usage: python negative_control.py [--list] [--only NAME]... [--selftest] [--keep]
ASCII-only stdout (GBK console).
"""
from __future__ import print_function
import argparse, io, os, re, shutil, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SRC = os.path.join(HERE, "..", "src")
PROJ = os.path.join(HERE, "BehaviorTests", "BehaviorTests.csproj")
EXE = os.path.join(HERE, "BehaviorTests", "bin", "Release", "net6.0", "BehaviorTests.exe")
DOTNET = r"D:\dmmplayer\dotnet-sdk6\dotnet.exe"

MUTATIONS = [
    dict(name="clock-paused-fills-combat", file="Model/BattleSession.cs",
         find="\t\tif (!paused) CombatSeconds += dt;",
         repl="\t\tCombatSeconds += dt;",
         expect="clock/advance/paused-delta-does-not-move-combat"),
    dict(name="window-constant-0.45", file="Model/BattleSession.cs",
         find="public const double HitMatchSeconds = 0.35;",
         repl="public const double HitMatchSeconds = 0.45;",
         expect="window/constants/hit-match-window-is-0.35s"),
    dict(name="window-no-exact-preference", file="Model/BattleSession.cs",
         find="\t\t\tif (hitRecord.Damage == damage || hitRecord.Damage == nominal)",
         repl="\t\t\tif (false && (hitRecord.Damage == damage || hitRecord.Damage == nominal))",
         expect="window/candidate-order/an-exact-match-beats-an-earlier-pair"),
    dict(name="window-expiry-uses-greater-equal", file="Model/BattleSession.cs",
         find="\t\t\tif (now - hitRecord.T > HitMatchSeconds) continue;",
         repl="\t\t\tif (now - hitRecord.T >= HitMatchSeconds) continue;",
         expect="window/expiry-boundary/exactly-at-the-window-still-matches (>)"),
    dict(name="reset-keys-restart-at-2", file="Model/BattleSession.cs",
         find="\t\t_nextActorKey = 1;\n\t\tPendingHits.Clear();",
         repl="\t\t_nextActorKey = 2;\n\t\tPendingHits.Clear();",
         expect="session/reset/reset-restarts-actor-keys-at-1"),
    dict(name="reset-clears-the-clock", file="Model/BattleSession.cs",
         find="\t\tTimingStarted = false;\n\t\tEvents.Clear();",
         repl="\t\tTimingStarted = false;\n\t\tActiveSeconds = 0.0;\n\t\tEvents.Clear();",
         expect="session/reset/reset-KEEPS-the-battle-clock"),
    dict(name="series-merge-window-0.4", file="Model/ActorStats.cs",
         find="if (t - last.T < 0.04)",
         repl="if (t - last.T < 0.4)",
         expect="series/damage-curve/a-sample-at-exactly-0.04s-is-appended"),
    # RF5a: the code moved to Policy/ContributionCachePolicy.cs, so the mutation follows it; the
    # expectation stays on the FACADE-level case, which is what proves the extraction kept the rule.
    dict(name="cache-ignores-event-count", file="Policy/ContributionCachePolicy.cs",
         find="\t\t\t|| cachedEvents != currentEvents",
         repl="\t\t\t|| false",
         expect="cache/live-recompute/a-new-event-recomputes-immediately (NOT a one-second throttle)"),
    dict(name="cache-refresh-10s", file="Policy/ContributionCachePolicy.cs",
         find="\tpublic const double RefreshSeconds = 1.0;",
         repl="\tpublic const double RefreshSeconds = 10.0;",
         expect="cache/time-clause/one-second-past-the-refresh-recomputes"),
    # ---- RF3: the extracted pure policies. Each mutation edits ONE threshold or ONE branch of a policy
    # and must redden the case that pins it; before RF3 these numbers were literals inside Aggregator, so
    # no mutation could reach them at all.
    dict(name="policy-clamp-1s", file="Policy/BattleClockPolicy.cs",
         find="public const double MaxFrameDelta = 0.25;",
         repl="public const double MaxFrameDelta = 1.0;",
         expect="policy/clock-clamp/a-stall-is-capped-at-the-bound"),
    dict(name="policy-units-40", file="Policy/BattleClockPolicy.cs",
         find="public const double DefaultUnitsPerGameSecond = 30.0;",
         repl="public const double DefaultUnitsPerGameSecond = 40.0;",
         expect="policy/clock-units/nothing-configured-falls-back"),
    dict(name="policy-source-fallback-real", file="Policy/BattleClockPolicy.cs",
         find="return legacyUsesGameTime ? \"engine\" : \"game\";",
         repl="return legacyUsesGameTime ? \"engine\" : \"real\";",
         expect="policy/clock-source/empty-config-defaults-to-game"),
    dict(name="policy-run-join-3s", file="Policy/SessionTransitionPolicy.cs",
         find="public const double RunJoinSeconds = 2.0;",
         repl="public const double RunJoinSeconds = 3.0;",
         expect="policy/run-grouping/run-join-window-is-2s"),
    dict(name="policy-resume-6s", file="Policy/SessionTransitionPolicy.cs",
         find="public const double ResumeWindowSeconds = 5.0;",
         repl="public const double ResumeWindowSeconds = 6.0;",
         expect="policy/resume-gate/resume-window-is-5s"),
    dict(name="policy-next-run-adds-two", file="Policy/SessionTransitionPolicy.cs",
         find="\t\tif (continues) runSeq++;",
         repl="        if (continues) runSeq += 2;",
         expect="policy/run-grouping/continuing-advances-the-sequence"),
    dict(name="policy-blind-0.09", file="Policy/AttributionPolicy.cs",
         find="public const double LiveBlindAge = 0.08;",
         repl="public const double LiveBlindAge = 0.09;",
         expect="policy/live-pair/live-blind-age"),
    dict(name="policy-fifo-0.70", file="Policy/AttributionPolicy.cs",
         find="public const double FifoWindow = 0.60;",
         repl="public const double FifoWindow = 0.70;",
         expect="policy/live-pair/fifo-window"),
    dict(name="policy-dmg-nominal-guard", file="Policy/AttributionPolicy.cs",
         find="return candidateDamage == damage || (nominal > 0 && candidateDamage == nominal);",
         repl="return candidateDamage == damage || (candidateDamage == nominal);",
         expect="policy/dmg-match/a-zero-nominal-never-matches"),
    dict(name="policy-pair-label-fifo", file="Policy/AttributionPolicy.cs",
         find="\t\t\tcase PairKind.Fifo: return \"fifo\";",
         repl="            case PairKind.Fifo: return \"FIFO\";",
         expect="policy/pair-label/fifo"),
    # ---- RF4: the cross-session state family, and RF3b's idle rule ----
    dict(name="continuity-clear-remembered-end", file="Runtime/SessionContinuity.cs",
         find="\t\tHasEnded = true;",
         repl="\t\tHasEnded = false;",
         expect="runtime/continuity-run-marker/an-idle-close-continues-the-run"),
    dict(name="continuity-keep-expired-session", file="Runtime/SessionContinuity.cs",
         find="\t\tif (gate == ResumeGate.WindowExpired) LastClosed = null;",
         repl="\t\tif (false) LastClosed = null;",
         expect="runtime/continuity-resume/an-expired-window-forgets-the-session"),
    dict(name="continuity-gap-always-reported", file="Runtime/SessionContinuity.cs",
         find="\t\t\tRunGap = continues ? runGap : -1.0,",
         repl="\t\t\tRunGap = runGap,",
         expect="runtime/continuity-run-marker/a-new-run-carries-no-gap"),
    dict(name="continuity-forget-on-gate", file="Runtime/SessionContinuity.cs",
         find="\t\tif (gate == ResumeGate.WindowExpired) LastClosed = null;\n\t\treturn gate;",
         repl="\t\tLastClosed = null;\n\t\treturn gate;",
         expect="runtime/continuity-resume/an-unknown-actor-leaves-the-session-remembered"),
    dict(name="idle-rule-ignores-pause", file="Policy/SessionTransitionPolicy.cs",
         find="\t\treturn !paused && eventCount > 0 && idleCombatSeconds > idleSeconds;",
         repl="\t\treturn eventCount > 0 && idleCombatSeconds > idleSeconds;",
         expect="policy/idle-rule/a-paused-frame-never-closes"),
    dict(name="idle-seconds-10", file="Policy/SessionTransitionPolicy.cs",
         find="\tpublic const double IdleSeconds = 8.0;",
         repl="\tpublic const double IdleSeconds = 10.0;",
         expect="policy/idle-rule/idle-seconds-is-8"),
    # ---- RF4 family 2: the battle-wide rule classifier ----
    dict(name="globalrule-drop-alle-gate", file="Policy/GlobalRuleClassifier.cs",
         find="\t\t\tif (clause.IndexOf(\"全て\", StringComparison.Ordinal) < 0\n\t\t\t\t&& clause.IndexOf(\"すべて\", StringComparison.Ordinal) < 0) return shape;",
         repl="\t\t\tif (false) return shape;",
         expect="globalrule/rejections/a-clause-without-alle-is-not-battle-wide"),
    dict(name="globalrule-drop-enemy-hp-gate", file="Policy/GlobalRuleClassifier.cs",
         find="\t\t\tif (clause.IndexOf(\"ブロック\", StringComparison.Ordinal) >= 0) return shape;\n\t\t\tif (clause.IndexOf(\"耐久\", StringComparison.Ordinal) >= 0\n\t\t\t\t|| clause.IndexOf(\"HP\", StringComparison.Ordinal) >= 0) return shape;",
         repl="\t\t\tif (clause.IndexOf(\"ブロック\", StringComparison.Ordinal) >= 0) return shape;",
         expect="globalrule/contracts/an-hp-gated-clause-never-parses-a-factor"),
    dict(name="globalrule-drop-ally-hp-gate", file="Policy/GlobalRuleClassifier.cs",
         find="\t\telse\n\t\t{\n\t\t\tif (clause.IndexOf(\"耐久\", StringComparison.Ordinal) >= 0\n\t\t\t\t|| clause.IndexOf(\"HP\", StringComparison.Ordinal) >= 0) return shape;\n\t\t}",
         repl="\t\telse\n\t\t{\n\t\t}",
         expect="globalrule/rejections/an-hp-conditional-ally-clause-stays-local"),
    dict(name="globalrule-always-taken-keyword", file="Policy/GlobalRuleClassifier.cs",
         find="\t\tdouble f = parseFactor(clause, enemyShape ? \"被ダメージ\" : \"与ダメージ\");",
         repl="\t\tdouble f = parseFactor(clause, \"被ダメージ\");",
         expect="globalrule/contracts/an-ally-clause-asks-for-the-dealt-keyword"),
    dict(name="globalrule-magic-precedence", file="Policy/GlobalRuleClassifier.cs",
         find="\t\tshape.MagicOnly = magic && !phys;",
         repl="\t\tshape.MagicOnly = magic;",
         expect="globalrule/classification/both-attributes-means-neither-restriction-magic"),
    dict(name="globalrule-per-status-any", file="Policy/GlobalRuleClassifier.cs",
         find="\t\tshape.PerStatus = clause.IndexOf(\"それぞれ\", StringComparison.Ordinal) >= 0;",
         repl="\t\tshape.PerStatus = clause.IndexOf(\"味方\", StringComparison.Ordinal) >= 0;",
         expect="globalrule/classification/the-ally-buff-is-not-per-status"),
    dict(name="globalrule-empty-memo-ignores-name", file="Policy/GlobalRuleClassifier.cs",
         find="\t\tbool sameEmptyMemo = f.EntryCount == 0 && f.HasNameMemo && f.NameMemoOwner == f.CurrentName;",
         repl="\t\tbool sameEmptyMemo = f.EntryCount == 0;",
         expect="globalrule/registry-facts/an-empty-entry-with-another-name-is-rescanned"),
    dict(name="globalrule-valve-500", file="Policy/GlobalRuleClassifier.cs",
         find="\tpublic const int ReclaimThreshold = 400;",
         repl="\tpublic const int ReclaimThreshold = 500;",
         expect="globalrule/registry-valve/reclaim-threshold-is-400"),
    # ---- RF4c: the battle-wide rule registry container ----
    dict(name="registry-reclaim-spares-dead", file="Runtime/GlobalRuleRegistry.cs",
         find="\t\t\t\t\tif (isOwnerAlive(list[i])) { alive = true; break; }",
         repl="\t\t\t\t\tif (true) { alive = true; break; }",
         expect="runtime/global-registry-reclaim/reclaim-drops-exactly-the-dead-owner"),
    dict(name="registry-empty-memo-survives", file="Runtime/GlobalRuleRegistry.cs",
         find="\t\t\tif (!alive) dead.Add(kv.Key);",
         repl="\t\t\tif (!alive && list != null && list.Count > 0) dead.Add(kv.Key);",
         expect="runtime/global-registry-reclaim/an-empty-memo-is-dropped-even-for-a-live-owner"),
    dict(name="registry-reclaim-keeps-name", file="Runtime/GlobalRuleRegistry.cs",
         find="\t\t\t_rules.Remove(dead[i]);\n\t\t\t_ownerName.Remove(dead[i]);",
         repl="\t\t\t_rules.Remove(dead[i]);",
         expect="runtime/global-registry-reclaim/the-dead-owner-name-is-forgotten"),
    dict(name="registry-clearall-keeps-names", file="Runtime/GlobalRuleRegistry.cs",
         find="\t\t_rules.Clear();\n\t\t_ownerName.Clear();",
         repl="\t\t_rules.Clear();",
         expect="runtime/global-registry-clear/clear-all-forgets-the-names-too"),
    dict(name="registry-set-stores-a-copy", file="Runtime/GlobalRuleRegistry.cs",
         find="\t\t_rules[key] = rules;",
         repl="\t\t_rules[key] = new List<TRule>();",
         expect="runtime/global-registry/try-get-returns-the-same-list"),
    # ---- RF5: the contribution cache policy ----
    dict(name="cache-refresh-2s", file="Policy/ContributionCachePolicy.cs",
         find="\tpublic const double RefreshSeconds = 1.0;",
         repl="\tpublic const double RefreshSeconds = 2.0;",
         expect="policy/cache/refresh-seconds-is-1"),
    dict(name="cache-stale-drops-session-clause", file="Policy/ContributionCachePolicy.cs",
         find="\t\treturn !sameSession",
         repl="\t\treturn false",
         expect="policy/cache/a-different-session-is-stale"),
    dict(name="cache-stale-drops-count-clause", file="Policy/ContributionCachePolicy.cs",
         find="\t\t\t|| cachedEvents != currentEvents",
         repl="\t\t\t|| false",
         expect="policy/cache/a-changed-event-count-is-stale"),
    dict(name="cache-stale-drops-folds-clause", file="Policy/ContributionCachePolicy.cs",
         find="\t\t\t|| cachedUsedFolds != useFolds",
         repl="\t\t\t|| false",
         expect="policy/cache/the-folds-clause-fires-if-it-is-ever-reached"),
    dict(name="cache-refresh-includes-boundary", file="Policy/ContributionCachePolicy.cs",
         find="\t\t\t|| now - cachedAt > refreshSeconds;",
         repl="\t\t\t|| now - cachedAt >= refreshSeconds;",
         expect="policy/cache/elapsed-exactly-at-the-refresh-is-not-stale"),
    dict(name="cache-select-always-none", file="Policy/ContributionCachePolicy.cs",
         find="\t\treturn hasCache ? ViewSource.History : ViewSource.None;",
         repl="\t\treturn ViewSource.None;",
         expect="policy/cache-source/no-battle-with-a-cache-is-history"),
    dict(name="cache-history-reason-text", file="Policy/ContributionCachePolicy.cs",
         find="\t\t\tcase CacheUnavailable.HistoryNone: return \"暂无战斗数据\";",
         repl="\t\t\tcase CacheUnavailable.HistoryNone: return \"暂无战斗数据。\";",
         expect="policy/cache-reason/history-with-folds-on-says-no-data"),
    # ---- RF4 apply side: copies and the per-status factor ----
    dict(name="apply-magic-hit-drops-both", file="Policy/GlobalRuleApplyPolicy.cs",
         find="\t\treturn hitType == HitTypeMagic || hitType == HitTypeBoth;",
         repl="\t\treturn hitType == HitTypeMagic;",
         expect="policy/globalrule-apply/a-both-hit-satisfies-both"),
    dict(name="apply-phys-hit-drops-both", file="Policy/GlobalRuleApplyPolicy.cs",
         find="\t\treturn hitType == HitTypePhys || hitType == HitTypeBoth;",
         repl="\t\treturn hitType == HitTypePhys;",
         expect="policy/globalrule-apply/a-both-hit-satisfies-both"),
    dict(name="apply-copies-ignores-the-no-status-case", file="Policy/GlobalRuleApplyPolicy.cs",
         find="\t\tif (tokenCount == 0) return 1;",
         repl="\t\tif (false) return 1;",
         expect="policy/globalrule-copies/a-clause-without-statuses-fires-once"),
    dict(name="apply-factor-ignores-per-status", file="Policy/GlobalRuleApplyPolicy.cs",
         find="\t\tif (copies > 1 && perStatus)",
         repl="\t\tif (copies > 1)",
         expect="policy/globalrule-factor/without-per-status-three-statuses-still-apply-once"),
    dict(name="apply-factor-uses-pow", file="Policy/GlobalRuleApplyPolicy.cs",
         find="\t\t	double acc = 1.0;\n\t\t\tfor (int h = 0; h < copies; h++) acc *= factor;\n\t\t\treturn acc;",
         repl="\t\t\treturn System.Math.Pow(factor, copies);",
         expect="policy/globalrule-factor/the-repeated-product-is-not-a-pow"),
    dict(name="apply-hit-type-phys-3", file="Policy/GlobalRuleApplyPolicy.cs",
         find="\tpublic const int HitTypePhys = 1;",
         repl="\tpublic const int HitTypePhys = 3;",
         expect="policy/globalrule-apply/hit-type-phys-is-1"),
    # ---- RF4 third family: the per-battle counters and their transitions ----
    dict(name="counters-start-keeps-event-count", file="Runtime/BattleRuntimeCounters.cs",
         find="\t\tEventCount = 0;\n\t\tAbsorbedTotal = 0L;",
         repl="\t\tAbsorbedTotal = 0L;",
         expect="runtime/counters/a-new-session-zeroes-the-event-count"),
    dict(name="counters-start-keeps-summary-throttle", file="Runtime/BattleRuntimeCounters.cs",
         find="\t\tLastSummaryLog = 0.0;\n\t\tLastTimeLog = 0.0;",
         repl="\t\tLastTimeLog = 0.0;",
         expect="runtime/counters/a-new-session-zeroes-the-summary-throttle"),
    dict(name="counters-start-clears-the-stamp", file="Runtime/BattleRuntimeCounters.cs",
         find="\t\tLastTimeLog = 0.0;\n\t}",
         repl="\t\tLastTimeLog = 0.0;\n\t\tGameTimeAtStart = 0;\n\t}",
         expect="runtime/counters/a-new-session-leaves-the-start-stamp-to-the-facade"),
    dict(name="counters-manual-reset-clears-nothing", file="Runtime/BattleRuntimeCounters.cs",
         find="\tpublic void OnManualReset()\n\t{\n\t\tEventCount = 0;\n\t}",
         repl="\tpublic void OnManualReset()\n\t{\n\t}",
         expect="runtime/counters/a-manual-reset-drops-the-event-count"),
    dict(name="counters-manual-reset-clears-more", file="Runtime/BattleRuntimeCounters.cs",
         find="\tpublic void OnManualReset()\n\t{\n\t\tEventCount = 0;\n\t}",
         repl="\tpublic void OnManualReset()\n\t{\n\t\tEventCount = 0;\n\t\tHitDetailProduced = 0;\n\t}",
         expect="runtime/counters/a-manual-reset-keeps-the-detail-produced"),
    # R62 (A) added HitMatchRejected, so the family is 13 fields and this mutation adds the 14th.
    dict(name="counters-fourteenth-field", file="Runtime/BattleRuntimeCounters.cs",
         find="\tpublic int GameTimeAtStart;",
         repl="\tpublic int GameTimeAtStart;\n\tpublic int ExtraCounter;",
         expect="runtime/counters-shape/the-family-has-thirteen-counters"),
    # ---- RF5b: the display formatter ----
    dict(name="format-dispwidth-all-wide", file="Ui/DisplayFormat.cs",
         find="\t\treturn wide ? 2 : 1;",
         repl="\t\treturn 2;",
         expect="display/width/ascii-is-one-column-per-character"),
    dict(name="format-cell-drops-normalisation", file="Ui/DisplayFormat.cs",
         find="\t\treturn string.IsNullOrEmpty(s) ? \"\" : s.Replace('\\u00D7', 'x');",
         repl="\t\treturn string.IsNullOrEmpty(s) ? \"\" : s;",
         expect="display/cell/the-multiplication-sign-becomes-an-ascii-x"),
    dict(name="format-fit-cuts-surrogates", file="Ui/DisplayFormat.cs",
         find="\t\tif (i > 0 && char.IsHighSurrogate(s[i - 1])) i--;   // never cut a surrogate pair in half\n",
         repl="",
         expect="display/fit/an-astral-string-is-not-cut-mid-pair"),
    dict(name="format-amt-never-falls-back", file="Ui/DisplayFormat.cs",
         find="\t\tif (DispWidth(full) <= width) return PadL(full, width);",
         repl="\t\tif (true) return PadL(full, width);",
         expect="display/amount/a-13-column-amount-falls-back-to-M"),
    dict(name="format-fmt-no-nan-guard", file="Ui/DisplayFormat.cs",
         find="\t\tif (double.IsNaN(v) || double.IsInfinity(v)) v = 0.0;\n\t\treturn v.ToString(\"N0\"",
         repl="\t\treturn v.ToString(\"N0\"",
         expect="display/numbers/fmt-of-nan-is-zero"),
    dict(name="format-pct-three-decimals", file="Ui/DisplayFormat.cs",
         find="\t\treturn v.ToString(\"F2\"",
         repl="\t\treturn v.ToString(\"F3\"",
         expect="display/numbers/pct-is-two-decimals"),
    # ---- RF5c: the column definition ----
    dict(name="columns-t1-total-12", file="Ui/ContributionColumns.cs",
         find="\tpublic const int T1Total = 11;",
         repl="\tpublic const int T1Total = 12;",
         expect="columns/spec/t1-widths"),
    dict(name="columns-t1-align-flip", file="Ui/ContributionColumns.cs",
         find="C(\"占比\", T1Share, true)",
         repl="C(\"占比\", T1Share, false)",
         expect="columns/spec/t1-alignment"),
    dict(name="columns-header-ignores-align", file="Ui/ContributionColumns.cs",
         find="\t\t\tsb.Append(cols[i].Right ? DisplayFormat.PadL(cols[i].Label, cols[i].Width)\n\t\t\t                         : DisplayFormat.PadR(cols[i].Label, cols[i].Width));",
         repl="\t\t\tsb.Append(DisplayFormat.PadR(cols[i].Label, cols[i].Width));",
         expect="columns/header/each-t1-slice-is-its-padded-label"),
    dict(name="columns-totals-fills-share", file="Ui/ContributionColumns.cs",
         find="\t\t     + DisplayFormat.PadL(\"\", T1Share)",
         repl="\t\t     + DisplayFormat.PadL(\"x\", T1Share)",
         expect="columns/totals/the-totals-row-verbatim"),
    dict(name="columns-line-width-84", file="Ui/ContributionColumns.cs",
         find="\tpublic const int T1LineWidth = 94;",
         repl="\tpublic const int T1LineWidth = 93;",
         expect="columns/spec/t1-line-width-is-94"),
    # ---- RF5d: the data-row builders ----
    dict(name="rows-t3-arrow-width", file="Ui/ContributionColumns.cs",
         find="\t\t     + DisplayFormat.PadR(\"→\", T3Arrow)",
         repl="\t\t     + DisplayFormat.PadR(\"→\", T3Hits)",
         expect="columns/rows/a-typical-t3-row-is-53-wide"),
    dict(name="rows-t1-share-width", file="Ui/ContributionColumns.cs",
         find="\t\t     + DisplayFormat.PadL(DisplayFormat.Pct(sharePct), T1Share)",
         repl="\t\t     + DisplayFormat.PadL(DisplayFormat.Pct(sharePct), T1Total)",
         expect="columns/rows/a-typical-t1-row-is-94-wide"),
    dict(name="rows-t1-marker-outside-fit", file="Ui/ContributionColumns.cs",
         find="DisplayFormat.PadR(DisplayFormat.Fit(DisplayFormat.Cell(name) + (summon ? \"*\" : \"\"), T1Name), T1Name)",
         repl="DisplayFormat.PadR(DisplayFormat.Fit(DisplayFormat.Cell(name), T1Name), T1Name) + (summon ? \"*\" : \"\")",
         expect="columns/rows/a-long-name-is-cut-to-the-column-with-the-mark"),
    dict(name="rows-t2-rule-no-fit", file="Ui/ContributionColumns.cs",
         find="DisplayFormat.PadR(DisplayFormat.Fit(DisplayFormat.Cell(rule), T2Rule), T2Rule)",
         repl="DisplayFormat.PadR(DisplayFormat.Cell(rule), T2Rule)",
         expect="columns/rows/a-long-rule-name-is-cut-to-22"),
    # ---- RF5e: the IMGUI fallback's contribution lines ----
    dict(name="fallback-actor-drops-the-summon-marker", file="Ui/FallbackText.cs",
         find="(summon ? \"[使魔]\" : \"\")",
         repl="\"\"",
         expect="fallback/contribution/the-summon-marker-is-a-suffix"),
    dict(name="fallback-actor-swaps-assist-and-received", file="Ui/FallbackText.cs",
         find="\t\t     + \"  他人因你 \" + DisplayFormat.Fmt(assist)\n\t\t     + \"  被队友分走 \" + DisplayFormat.Fmt(received)\n",
         repl="\t\t     + \"  他人因你 \" + DisplayFormat.Fmt(received)\n\t\t     + \"  被队友分走 \" + DisplayFormat.Fmt(assist)\n",
         expect="fallback/contribution/the-actor-line-is-verbatim"),
    dict(name="fallback-totals-drops-the-percent", file="Ui/FallbackText.cs",
         find="\t\t     + \"   未归因 \" + DisplayFormat.Fmt(unattributed) + \"(\" + DisplayFormat.Pct(unattrPct) + \")\"",
         repl="\t\t     + \"   未归因 \" + DisplayFormat.Fmt(unattributed) + \"(\" + DisplayFormat.Fmt(unattrPct) + \")\"",
         expect="fallback/contribution/the-totals-line-is-verbatim"),
    # ---- RF6a: the master-data official-name rule ----
    dict(name="masterdata-select-ignores-signature", file="Policy/MasterDataLabelPolicy.cs",
         find="\t\t\tif (c.Signature != liveSignature) continue;",
         repl="\t\t\tif (false) continue;",
         expect="policy/masterdata-select/the-base-engraving-resolves-by-its-signature"),
    dict(name="masterdata-signature-unsorted", file="Policy/MasterDataLabelPolicy.cs",
         find="\t\tids.Sort();",
         repl="",
         expect="policy/masterdata-signature/the-order-of-the-talents-does-not-matter"),
    dict(name="masterdata-empty-name-is-no-candidate", file="Policy/MasterDataLabelPolicy.cs",
         find="\t\tmiss = (distinct == 0) ? LabelMiss.NoCandidate : LabelMiss.Ambiguous;",
         repl="\t\tmiss = LabelMiss.NoCandidate;",
         expect="policy/masterdata-select/and-it-counts-as-ambiguous-not-as-no-candidate"),
    dict(name="masterdata-signature-joins-with-dash", file="Policy/MasterDataLabelPolicy.cs",
         find="\t\t\tif (i > 0) sb.Append(',');",
         repl="\t\t\tif (i > 0) sb.Append('-');",
         expect="policy/masterdata-signature/talents-are-sorted-and-joined"),
    # ---- RF3c: the composition chain's tolerances ----
    dict(name="comptol-tight-1pct", file="Policy/CompositionTolerancePolicy.cs",
         find="\tpublic const double TightRate = 0.005;",
         repl="\tpublic const double TightRate = 0.01;",
         expect="policy/composition-tolerance/tight-rate-is-5-permille"),
    dict(name="comptol-loose-below-tight", file="Policy/CompositionTolerancePolicy.cs",
         find="\tpublic const double LooseRate = 0.02;",
         repl="\tpublic const double LooseRate = 0.002;",
         expect="policy/composition-tolerance/the-truth-pass-is-tighter-than-the-display-pass"),
    dict(name="comptol-crit-max-not-shared", file="Policy/CompositionTolerancePolicy.cs",
         find="\tpublic static double CritDeltaMax { get { return BattleClockPolicy.MaxFrameDelta; } }",
         repl="\tpublic static double CritDeltaMax { get { return 0.30; } }",
         expect="policy/composition-tolerance/the-crit-upper-bound-is-the-clocks-frame-delta"),
    dict(name="comptol-crit-min-exclusive", file="Policy/CompositionTolerancePolicy.cs",
         find="\t\treturn !(dt < CritDeltaMin || dt > CritDeltaMax);",
         repl="\t\treturn !(dt <= CritDeltaMin || dt > CritDeltaMax);",
         expect="policy/composition-crit-window/exactly-at-the-minimum-is-usable"),
    # ---- RF4f: the per-battle activity ring ----
    dict(name="calclog-cap-128", file="Runtime/CalcActivityLog.cs",
         find="\tpublic const int Max = 64;",
         repl="\tpublic const int Max = 128;",
         expect="runtime/calc-activity-log/the-cap-is-64"),
    dict(name="calclog-drops-the-newest", file="Runtime/CalcActivityLog.cs",
         find="\t\twhile (_items.Count > Max) _items.RemoveAt(0);",
         repl="\t\twhile (_items.Count > Max) _items.RemoveAt(_items.Count - 1);",
         expect="runtime/calc-activity-log/and-drops-the-oldest"),
    dict(name="calclog-add-does-not-trim", file="Runtime/CalcActivityLog.cs",
         find="\t\t_items.Add(item);\n\t\twhile (_items.Count > Max) _items.RemoveAt(0);",
         repl="\t\t_items.Add(item);",
         expect="runtime/calc-activity-log/one-more-add-keeps-the-count-at-the-cap"),
    # ---- RF5f: the contribution row model ----
    dict(name="rowmodel-sums-only-the-shown", file="Ui/ContributionRowModel.cs",
         find="\t\t\tv.SumBase += a.Base;\n\t\t\tv.SumSelf += a.Self;\n\t\t\tv.SumAssist += a.Assist;\n\t\t\tv.SumReceived += a.Received;\n\t\t\tv.SumFriendly += a.Friendly;\n\t\t\tif (a.Total <= 0.0 && a.Direct <= 0.0) continue;",
         repl="\t\t\tif (a.Total <= 0.0 && a.Direct <= 0.0) continue;\n\t\t\tv.SumBase += a.Base;\n\t\t\tv.SumSelf += a.Self;\n\t\t\tv.SumAssist += a.Assist;\n\t\t\tv.SumReceived += a.Received;\n\t\t\tv.SumFriendly += a.Friendly;",
         expect="rowmodel/contribution/the-received-sum-covers-the-unshown-actor"),
    dict(name="rowmodel-share-from-direct", file="Ui/ContributionRowModel.cs",
         find="\t\t\t\tShare = total > 0.0 ? 100.0 * a.Total / total : 0.0,",
         repl="\t\t\t\tShare = total > 0.0 ? 100.0 * a.Direct / total : 0.0,",
         expect="rowmodel/contribution/the-share-is-a-percent-of-the-given-total"),
    dict(name="rowmodel-shows-everything", file="Ui/ContributionRowModel.cs",
         find="\t\t\tif (a.Total <= 0.0 && a.Direct <= 0.0) continue;",
         repl="",
         expect="rowmodel/contribution/only-contributing-actors-are-shown"),
    # ---- RF5g: the rule / link tables ----
    dict(name="rowmodel-rules-limit-13", file="Ui/ContributionRowModel.cs",
         find="\tpublic const int ShownLimit = 12;",
         repl="\tpublic const int ShownLimit = 13;",
         expect="rowmodel/rules/the-shown-limit-is-12"),
    dict(name="rowmodel-rules-keep-zero-damage", file="Ui/ContributionRowModel.cs",
         find="\t\t\tif (rr == null || rr.Damage <= 0.0) continue;",
         repl="\t\t\tif (rr == null) continue;",
         expect="rowmodel/rules/a-zero-equivalent-rule-is-not-shown"),
    dict(name="rowmodel-link-name-drops-the-hash", file="Ui/ContributionRowModel.cs",
         find="\t\treturn \"#\" + key;",
         repl="\t\treturn key.ToString();",
         expect="rowmodel/links/an-unknown-endpoint-becomes-a-hash-key"),
    # ---- RF4g: the attack snapshot ----
    dict(name="snapshot-valid-ignores-the-calc", file="Runtime/AttackSnapshot.cs",
         find="public bool Valid { get { return Calc != null && At >= 0.0; } }",
         repl="public bool Valid { get { return At >= 0.0; } }",
         expect="runtime/attack-snapshot/a-null-calc-with-a-stamp-is-not-valid"),
    dict(name="snapshot-clear-keeps-the-stamp", file="Runtime/AttackSnapshot.cs",
         find="\t\tCalc = null;\n\t\tAt = -1.0;",
         repl="\t\tCalc = null;",
         expect="runtime/attack-snapshot/clear-resets-the-stamp"),
    dict(name="snapshot-sentinel-zero", file="Runtime/AttackSnapshot.cs",
         find="public double At = -1.0;",
         repl="public double At = 0.0;",
         expect="runtime/attack-snapshot/and-its-stamp-is-the-minus-one-sentinel"),
    # ---- RF4h: the process-level history ring ----
    dict(name="history-inserts-at-the-back", file="Runtime/BattleHistoryRing.cs",
         find="\t\thistory.Insert(0, summary);",
         repl="\t\thistory.Add(summary);",
         expect="runtime/history-ring/the-newest-is-at-the-front"),
    dict(name="history-trims-the-front", file="Runtime/BattleHistoryRing.cs",
         find="\t\twhile (history.Count > max) history.RemoveAt(history.Count - 1);",
         repl="\t\twhile (history.Count > max) history.RemoveAt(0);",
         expect="runtime/history-ring/the-oldest-was-dropped"),
    dict(name="history-cap-21", file="Runtime/BattleHistoryRing.cs",
         find="\tpublic const int Max = 20;",
         repl="\tpublic const int Max = 21;",
         expect="runtime/history-ring/the-cap-is-20"),
    # ---- RF5h: the no-separator whole-number formatter ----
    dict(name="whole-adds-separators", file="Ui/DisplayFormat.cs",
         find="\t\treturn v.ToString(\"F0\", System.Globalization.CultureInfo.InvariantCulture);",
         repl="\t\treturn v.ToString(\"N0\", System.Globalization.CultureInfo.InvariantCulture);",
         expect="format/whole/and-no-thousands-separators"),
    # ---- round 41: the cache decisions ----
    dict(name="cache-ignores-the-generation", file="Policy/ContributionCachePolicy.cs",
         find="\t\t\t|| cachedGeneration != currentGeneration\n",
         repl="",
         expect="cache/generation/a-new-generation-is-stale-even-with-the-same-event-count"),
    dict(name="cache-throttle-not-strict", file="Policy/ContributionCachePolicy.cs",
         find="\t\t\t|| now - cachedAt > refreshSeconds;",
         repl="\t\t\t|| now - cachedAt >= refreshSeconds;",
         expect="cache/throttle/exactly-at-one-second-is-still-reused (strict >)"),
    # ---- R52: the evidence-extraction flow (trigger key, retention) and the unresolved-fold census ----
    # Each one edits ONE clause of the new code and must redden the case that pins it. The census ones are
    # the important half: they are what stops "the census is only a report" from being a comment.
    dict(name="extract-key-f13-enabled", file="Policy/ExtractPolicy.cs",
         find="&& n >= 1 && n <= 12)",
         repl="&& n >= 1 && n <= 13)",
         expect="extract/key/f13-is-not-a-key (off)"),
    dict(name="extract-retention-drops-the-newest", file="Policy/ExtractPolicy.cs",
         find="for (int i = 0; i < drop; i++) outl.Add(sorted[i]);",
         repl="for (int i = sorted.Count - drop; i < sorted.Count; i++) outl.Add(sorted[i]);",
         expect="extract/retention/the-OLDEST-is-dropped-first (never the newest)"),
    dict(name="extract-retention-keeps-nothing", file="Policy/ExtractPolicy.cs",
         find="keep < MinKeep ? MinKeep :",
         repl="keep < 0 ? 0 :",
         expect="extract/retention/keeping-zero-is-clamped-to-one (never delete everything)"),
    dict(name="extract-sanitise-drops-unknown-characters", file="Policy/ExtractPolicy.cs",
         find="if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');",
         repl="",
         expect="extract/bundle-name/separators-become-dashes"),
    dict(name="census-grant-key-cut-at-the-second-slash", file="Output/Contribution.cs",
         find="return origin.Substring(slash + 1);",
         repl="int slash2 = origin.IndexOf('/', slash + 1);\n\t\tif (slash2 < 0) return null;\n\t\treturn origin.Substring(slash + 1, slash2 - slash - 1);",
         expect="extract/census/a-unique-holding-actor-gives-a-unique-verdict"),
    dict(name="census-picks-the-first-of-many-carriers", file="Output/Contribution.cs",
         find="else if (cand.Count == 1)",
         repl="else if (cand.Count >= 1)",
         expect="extract/census/two-holding-actors-give-an-ambiguous-verdict"),
    dict(name="census-groups-copies-of-one-rule", file="Output/Contribution.cs",
         find="(f.Origin ?? \"\") + \"|\" + (f.Label ?? \"\")",
         repl="(f.Label ?? \"\")",
         expect="extract/census/two-copies-at-different-list-indexes-are-two-groups"),
    dict(name="census-victim-top-tie-reversed", file="Output/Contribution.cs",
         find="string.CompareOrdinal(x.Key, y.Key)",
         repl="string.CompareOrdinal(y.Key, x.Key)",
         expect="extract/census/a-victim-name-tie-is-broken-by-ordinal-order"),
    # R54: the granted-channel split and its on-screen labels.
    dict(name="given-branch-collapses-one-into-ambiguous", file="Output/Contribution.cs",
         find='if (holders.Count == 1) { reason = "given_carrier_one"; return null; }',
         repl='if (holders.Count == 1) { reason = "given_carrier_ambiguous"; return null; }',
         expect="extract/given-reasons/one-roster-holder-says-given_carrier_one"),
    dict(name="reason-label-swaps-the-one-candidate-text", file="Ui/FallbackText.cs",
         find='case "given_carrier_one":       return "阻挡增伤(唯一候选,未确认)";',
         repl='case "given_carrier_one":       return "阻挡增伤(多个候选,未确认)";',
         expect="extract/reason-labels/the-one-candidate-case-says-so"),
    dict(name="extract-source-prefers-the-stale-snapshot", file="Policy/ExtractPolicy.cs",
         find="\t\tif (liveAvailable) return BundleSource.Live;",
         repl="\t\tif (rememberedAvailable) return BundleSource.LastFinalised;",
         expect="extract/source/live-beats-the-snapshot (the live session is the more recent truth)"),
    dict(name="census-credits-the-unattributed-share-too", file="Output/Contribution.cs",
         find="\t\t\t\t\tst.Unattributed += share;",
         repl="\t\t\t\t\tst.Unattributed += share;\n\t\t\t\t\tac.Self += share;",
         expect="extract/census/nor-the-credited-amount"),
    # R55 (user request): the pending 「阻挡增伤」 table -- the granted pool drawn with the character
    # table's geometry. Each mutation edits ONE clause of the new model/caption and must redden the case
    # that pins it; the filter and the sort are the two that would silently mislead if they drifted.
    dict(name="pending-includes-every-unresolved-group", file="Ui/ContributionRowModel.cs",
         find="\t\t\tif (u == null || string.IsNullOrEmpty(u.CarrierVerdict)) continue;",
         repl="\t\t\tif (u == null) continue;",
         expect="extract/pending/but-only-the-granted-carriers-pool-reaches-the-pending-table"),
    dict(name="pending-splits-one-candidate-into-per-group-rows", file="Ui/ContributionRowModel.cs",
         find='\t\t\t\tid = "u|" + name;',
         repl='\t\t\t\tid = "u|" + name + "|" + u.Origin;',
         expect="extract/pending/two-copies-sharing-one-holder-are-ONE-row"),
    dict(name="pending-renames-the-ambiguous-caption", file="Ui/ContributionRowModel.cs",
         find='public const string AmbiguousNamePrefix = "候选";',
         repl='public const string AmbiguousNamePrefix = "候选者";',
         expect="extract/pending/and-it-carries-the-count-not-a-picked-name"),
    dict(name="pending-sorts-lightest-first", file="Ui/ContributionRowModel.cs",
         find="\t\t\t? (x.Amount < y.Amount ? 1 : -1)",
         repl="\t\t\t? (x.Amount < y.Amount ? -1 : 1)",
         expect="extract/pending/the-heavier-pool-is-printed-first-even-against-the-alphabet"),
    dict(name="pending-note-hides-the-ambiguous-candidates", file="Ui/FallbackText.cs",
         find="\t\tif (t == null || t.Ambiguous == null || t.Ambiguous.Count == 0) return note;",
         repl="\t\tif (t == null || t.Ambiguous == null || t.Ambiguous.Count >= 0) return note;",
         expect="extract/pending/the-note-lists-the-ambiguous-candidates"),
    dict(name="pending-relabels-the-fold-column-as-hits", file="Ui/ContributionColumns.cs",
         find='\t\tcols[cols.Length - 1].Label = "折叠";',
         repl='\t\tcols[cols.Length - 1].Label = "命中";',
         expect="extract/pending/the-last-column-is-relabelled-folds"),
    dict(name="pending-prints-a-zero-instead-of-a-dash", file="Ui/ContributionColumns.cs",
         find='\t\treturn DisplayFormat.PadL("-", width);',
         repl='\t\treturn DisplayFormat.PadL("0", width);',
         expect="extract/pending/the-five-inapplicable-credit-columns-print-a-dash (not a zero)"),
    # ---- R56: the battle reference (format, lifecycle, launch namespace, collision handling) ----
    # Each entry edits ONE clause of the contract and must redden the case that pins it. The export-side
    # clauses (ExportService/Overlay) are NOT reachable from this offline suite by construction, and that
    # limitation is stated in the round record rather than papered over with a mutation that cannot bite.
    dict(name="bref-sequence-pads-to-two", file="Policy/BattleRefPolicy.cs",
         find="\t\twhile (digits.Length < MinSequenceDigits) digits = \"0\" + digits;",
         repl="\t\twhile (digits.Length < 2) digits = \"0\" + digits;",
         expect="bref/format/the-short-tag-is-hash-padded-to-three"),
    dict(name="bref-end-without-a-result-is-final", file="Policy/BattleRefPolicy.cs",
         find="\t\treturn (end && hasResult) ? StateFinal : StateProvisional;",
         repl="\t\treturn end ? StateFinal : StateProvisional;",
         expect="bref/format/an-end-WITHOUT-a-result-is-only-provisional"),
    dict(name="bref-parse-accepts-sequence-zero", file="Policy/BattleRefPolicy.cs",
         find="\t\tif (value <= 0) return false;",
         repl="\t\tif (value < 0) return false;",
         expect="bref/format/sequence-zero-is-not-an-id"),
    dict(name="bref-file-name-drops-the-id", file="Policy/BattleRefPolicy.cs",
         find="\t\t\t+ (string.IsNullOrEmpty(id) ? \"\" : \"__\" + id) + \".json\";",
         repl="\t\t\t+ \".json\";",
         expect="bref/format/the-file-name-keeps-the-quest-in-its-historical-position"),
    dict(name="bref-copy-text-invents-a-hash", file="Policy/BattleRefPolicy.cs",
         find="\t\tsb.Append(\"sha256: \").Append(string.IsNullOrEmpty(sha256) ? \"(\u5c1a\u65e0\u53ef\u5206\u6790\u6587\u4ef6)\" : sha256.ToLowerInvariant()).Append('\\n');",
         repl="\t\tsb.Append(\"sha256: \").Append(sha256 == null ? \"\" : sha256.ToLowerInvariant()).Append('\\n');",
         expect="bref/format/and-it-does-NOT-invent-a-hash"),
    dict(name="bref-registry-never-advances", file="Runtime/BattleRefRegistry.cs",
         find="\tpublic int NextSequence() { return ++_seq; }",
         repl="\tpublic int NextSequence() { return _seq; }",
         expect="bref/registry/the-second-allocation-increments"),
    dict(name="bref-registry-ignores-a-collision", file="Runtime/BattleRefRegistry.cs",
         find="\t\t\tif (idExists == null || !idExists(id))",
         repl="\t\t\tif (true)",
         expect="bref/registry/a-colliding-candidate-is-probed-then-regenerated (the probe runs twice)"),
    dict(name="bref-resume-keeps-the-revision", file="Runtime/BattleRefRegistry.cs",
         find="\t\tr.CloseReason = \"\";\n\t\tr.Revision++;",
         repl="\t\tr.CloseReason = \"\";",
         expect="bref/registry/and-moves-the-revision (the provisional snapshot is stale)"),
    dict(name="bref-export-rebinds-on-every-copy", file="Runtime/BattleRefRegistry.cs",
         find="\t\tif (string.IsNullOrEmpty(r.ExportPath) || string.Equals(r.ExportPath, path, StringComparison.Ordinal))",
         repl="\t\tif (true)",
         expect="bref/registry/a-later-COPY-can-not-move-the-identity (the bundle is rotated away)"),
    dict(name="bref-view-reuses-the-live-identity", file="Model/BattleSession.cs",
         find="\t\tv.Ref = (b.Ref == null) ? null : b.Ref.Clone();",
         repl="\t\tv.Ref = b.Ref;",
         expect="bref/lifecycle/the-view-holds-a-COPY (a view must not be able to move the live identity)"),
    # ---- R60 (1.7.12): the same-team (self-damage) split of a character own hits ----
    # The split is DERIVED data, so these mutations are about it not silently becoming a second source of
    # truth: one moves the bucket, one double-counts the hits, one drops the new column from the
    # definition, one stops the footer sum, and one prints the wrong number in the fallback.
    dict(name="friendly-split-lands-in-hostile", file="Output/Contribution.cs",
         find="\t\t\t\tst.SelfTeamDamage += hit.Damage;",
         repl="\t\t\t\tst.SelfTeamDamage += 0.0;",
         expect="extract/friendly-split/the-session-ledger-counts-the-same-team-amount"),
    dict(name="friendly-hits-counted-twice", file="Output/Contribution.cs",
         find="fc.Friendly += hit.Damage;\n\t\t\t\tfc.FriendlyHits++;",
         repl="fc.Friendly += hit.Damage;\n\t\t\t\tfc.FriendlyHits += 2;",
         expect="extract/friendly-split/and-it-is-one-hit"),
    dict(name="same-team-reenters-the-pool", file="Output/Contribution.cs",
         find="\t\t\t\tcontinue;\n\t\t\t}\n\t\t\thitIndex++;",
         repl="\t\t\t}\n\t\t\thitIndex++;",
         expect="extract/friendly-split/direct-is-the-enemy-hit-only"),
    dict(name="friendly-column-width-off-by-one", file="Ui/ContributionColumns.cs",
         find="\tpublic const int T1Friendly = 9;",
         repl="\tpublic const int T1Friendly = 8;",
         expect="columns/spec/t1-widths"),
    dict(name="friendly-sum-drops-the-unshown-actor", file="Ui/ContributionRowModel.cs",
         find="\t\t\tv.SumFriendly += a.Friendly;",
         repl="\t\t\tv.SumFriendly += 0.0;",
         expect="rowmodel/contribution/the-friendly-sum-covers-the-unshown-actor"),
    dict(name="fallback-prints-received-as-self-damage", file="Ui/FallbackText.cs",
         find="+ \"  自伤 \" + DisplayFormat.Fmt(friendly);",
         repl="+ \"  自伤 \" + DisplayFormat.Fmt(received);",
         expect="fallback/contribution/the-actor-line-is-verbatim"),
    # ---- R62 (A): a damage-detail figure that belongs to ANOTHER hit must be rejected, not relabelled.
    # The predicate is the whole decision; each mutation removes one of its four guards and must redden the
    # case that pins it (measured defect it answers: D7F-009 factId 127 / D7F-010 factId 173).
    dict(name="pair-reject-accepts-every-type", file="Policy/AttributionPolicy.cs",
         find="		return recordHitType != compHitType;",
         repl="		return true;",
         expect="policy/record-contradiction/the-same-hit-type-does-not"),
    dict(name="pair-reject-ignores-composition-trust", file="Policy/AttributionPolicy.cs",
         find="		if (!compValid || !compTrusted) return false;",
         repl="		if (false) return false;",
         expect="policy/record-contradiction/an-untrusted-composition-never-rejects"),
    dict(name="pair-reject-treats-unreadable-as-a-value", file="Policy/AttributionPolicy.cs",
         find="		if (recordHitType < 0 || compHitType < 0) return false;",
         repl="		if (recordHitType < -99 || compHitType < -99) return false;",
         expect="policy/record-contradiction/an-unreadable-record-type-cannot-contradict"),
    dict(name="pair-reject-reason-code-is-2", file="Policy/AttributionPolicy.cs",
         find="	public const int HitMatchRejected = 3;",
         repl="	public const int HitMatchRejected = 2;",
         expect="policy/record-contradiction/the-rejected-reason-code-is-3"),
    dict(name="pair-reject-counter-survives-a-new-session", file="Runtime/BattleRuntimeCounters.cs",
         find="\t\tHitMatchRejected = 0;\n",
         repl="\t\tHitMatchRejected = 1;\n",
         expect="runtime/counters/a-new-session-zeroes-the-rejected"),
    # ---- R63's two `skill-cooldown-*` mutations were DELETED in R65, together with the policy they
    # mutated: `Policy/SkillCooldownPolicy.cs` published the auto-skill cooldown x30, and a live battle
    # falsified its premise (`Skill.CoolTimeFrame` equals the master value verbatim on 9 of 9 auto skills).
    # A mutation can only guard code that exists, and keeping a mutant for a deleted false rule would be a
    # gate on nothing.
    # ---- R64/R65: the auto-skill charge/cadence arithmetic. The probe can only be read in a live battle,
    # so the executable half is these conversions -- and each mutation removes one of the things the round
    # claims: that the charge must be DIVIDED by the measured rate before it can be compared with the
    # master's own numbers, that a counter which ran BACKWARDS is a reset and not a small positive rate,
    # and that the median must not be dragged onto IntervalSeconds' own "no interval" sentinel.
    dict(name="autoskill-charge-forgets-the-clock", file="Policy/AutoSkillCadencePolicy.cs",
         find="		double v = units / unitsPerSecond;",
         repl="		double v = units;",
         expect="policy/autoskill-cadence/the-measured-240-unit-charge-is-8-game-seconds"),
    dict(name="autoskill-rate-clamps-a-backwards-counter", file="Policy/AutoSkillCadencePolicy.cs",
         find="		if (double.IsNaN(v) || double.IsInfinity(v)) return 0.0;\n		return v;\n	}\n\n	/// <summary>\n	/// How many seconds a charge of `units`",
         repl="		if (double.IsNaN(v) || double.IsInfinity(v)) return 0.0;\n		return (v < 0.0) ? 0.0 : v;\n	}\n\n	/// <summary>\n	/// How many seconds a charge of `units`",
         expect="policy/autoskill-cadence/a-counter-that-ran-backwards-keeps-its-sign"),
    dict(name="autoskill-median-keeps-the-sentinel", file="Policy/AutoSkillCadencePolicy.cs",
         find="			if (double.IsNaN(v) || v <= 0.0) continue;",
         repl="			if (double.IsNaN(v)) continue;",
         expect="policy/autoskill-cadence/a-duplicate-record-does-not-drag-the-median"),
    dict(name="autoskill-median-sorts-the-callers-array", file="Policy/AutoSkillCadencePolicy.cs",
         find="		double[] copy = new double[values.Length];",
         repl="		double[] copy = values;",
         expect="policy/autoskill-cadence/the-median-does-not-reorder-the-callers-array"),
    # ---- R66: the 技能时间表. The probe and the overlay's row plumbing need the IL2CPP/Unity surface and
    # are NOT in the behaviour suite's compile list (see REFACTOR-BATCH-R66 section 5), so what these
    # mutations guard is the executable half: the merge window (two channels report one activation), the
    # battle-clock axis, the deterministic order, the median, and every marker that keeps the table from
    # being a silent truncation.
    dict(name="skilltimeline-window-eats-the-shortest-real-repeat", file="Policy/SkillTimelinePolicy.cs",
         find="\t\t\tif (n > 0 && (e.Active - g.Stamps[n - 1]) < MergeSeconds)",
         repl="\t\t\tif (n > 0 && (e.Active - g.Stamps[n - 1]) < MergeSeconds + 0.1)",
         expect="policy/skill-timeline/the-shortest-corroborated-repeat-survives"),
    dict(name="skilltimeline-a-double-report-is-listed-twice", file="Policy/SkillTimelinePolicy.cs",
         find="\tinternal const double MergeSeconds = 0.25;",
         repl="\tinternal const double MergeSeconds = 0.05;",
         expect="policy/skill-timeline/a-double-report-is-one-stamp"),
    dict(name="skilltimeline-rows-lose-their-order", file="Policy/SkillTimelinePolicy.cs",
         find="\t\t\tint c = string.CompareOrdinal(a.Unit, b.Unit);\n\t\t\tif (c != 0) return c;\n\t\t\tc = KindRank(a.Type).CompareTo(KindRank(b.Type));\n\t\t\tif (c != 0) return c;\n\t\t\treturn string.CompareOrdinal(a.Skill, b.Skill);",
         repl="\t\t\treturn 0;",
         expect="policy/skill-timeline/groups-are-ordered-by-unit-then-kind"),
    dict(name="skilltimeline-median-drops-the-last-gap", file="Policy/SkillTimelinePolicy.cs",
         find="\t\tfor (int i = 1; i < g.Stamps.Count; i++)",
         repl="\t\tfor (int i = 1; i < g.Stamps.Count - 1; i++)",
         expect="policy/skill-timeline/the-median-gap-of-9-10-11-is-10"),
    dict(name="skilltimeline-keeps-a-nan-clock-row", file="Policy/SkillTimelinePolicy.cs",
         find="\t\t\tif (double.IsNaN(e.Active) || double.IsInfinity(e.Active)) continue;",
         repl="\t\t\tif (false) continue;",
         expect="policy/skill-timeline/a-nan-clock-is-dropped"),
    dict(name="skilltimeline-keeps-a-row-without-a-unit", file="Policy/SkillTimelinePolicy.cs",
         find="\t\t\tif (string.IsNullOrEmpty(e.Unit) || string.IsNullOrEmpty(e.Skill)) continue;",
         repl="\t\t\tif (string.IsNullOrEmpty(e.Skill)) continue;",
         expect="policy/skill-timeline/a-row-without-a-unit-is-dropped"),
    dict(name="skilltimeline-over-skill-ranks-last", file="Policy/SkillTimelinePolicy.cs",
         find="\t\tif (type == 2) return 0;",
         repl="\t\tif (type == 2) return 9;",
         expect="policy/skill-timeline/the-over-skill-ranks-first"),
    dict(name="skilltimeline-channels-lose-the-first-seen-order", file="Policy/SkillTimelinePolicy.cs",
         find="\t\t\tif (!string.IsNullOrEmpty(e.Channel) && !g.Channels.Contains(e.Channel)) g.Channels.Add(e.Channel);",
         repl="\t\t\tif (!string.IsNullOrEmpty(e.Channel) && !g.Channels.Contains(e.Channel)) g.Channels.Insert(0, e.Channel);",
         expect="policy/skill-timeline/a-row-keeps-the-first-seen-channel-first"),
    dict(name="skilltimeline-overflow-prints-one-stamp-too-many", file="Ui/SkillTimelineText.cs",
         find="\t\tint shown = overflow ? SkillTimelinePolicy.MaxStamps - 1 : n;",
         repl="\t\tint shown = overflow ? SkillTimelinePolicy.MaxStamps : n;",
         expect="ui/skill-timeline-text/stamps-beyond-the-printed-ones-are-counted"),
    # R67: the burst mark is `并N条M格` now (R66's `x(N+1)` read as "N in one cell" and was false for 3 of
    # the 5 marked rows in the first live battle); the anchor moved with it.
    dict(name="skilltimeline-burst-marker-hides-its-count", file="Ui/SkillTimelineText.cs",
         find="\t\tif (g.Merged > 0)\n",
         repl="\t\tif (false)\n",
         expect="ui/skill-timeline-text/a-row-marks-a-folded-burst"),
    dict(name="skilltimeline-multiplicity-not-counted", file="Policy/SkillTimelinePolicy.cs",
         find="\t\t\t\tg.Multiplicity[n - 1]++;",
         repl="\t\t\t\t;",
         expect="policy/skill-timeline/each-cell-keeps-its-own-multiplicity"),
    dict(name="skilltimeline-burst-cells-count-rows-not-cells", file="Ui/SkillTimelineText.cs",
         find="\t\t\t\t.Append(g.BurstCells.ToString(CultureInfo.InvariantCulture)).Append(\"\u683c \");",
         repl="\t\t\t\t.Append((g.Merged + 1).ToString(CultureInfo.InvariantCulture)).Append(\"\u683c \");",
         expect="ui/skill-timeline-text/two-folds-in-two-cells-print-both-numbers"),
    dict(name="skilltimeline-stamp-cell-back-to-five-columns", file="Ui/SkillTimelineText.cs",
         find="\tinternal const int StampW = 6;",
         repl="\tinternal const int StampW = 5;",
         expect="ui/skill-timeline-text/three-digit-stamps-never-run-together"),
    dict(name="skilltimeline-skl-rows-counted-as-cmd", file="Ui/SkillTimelineText.cs",
         find="\t\t\t\telse if (e.Channel == SkillTimelineEvent.ChannelSkillCommand) skl++;",
         repl="\t\t\t\telse if (false) skl++;",
         expect="ui/skill-timeline-text/skl-rows-are-counted-in-their-own-channel"),
    dict(name="skilltimeline-truncation-hides-the-tail", file="Ui/SkillTimelineText.cs",
         find="\t\tif (groups.Count > shown)",
         repl="\t\tif (false)",
         expect="ui/skill-timeline-text/the-truncated-tail-is-reported"),
    dict(name="skilltimeline-record-warning-always-on", file="Ui/SkillTimelineText.cs",
         find="\t\tif (skl + rec == 0)",
         repl="\t\tif (skl + rec >= 0)",
         expect="ui/skill-timeline-text/a-record-channel-with-rows-does-not-warn"),
    dict(name="skilltimeline-warning-ignores-the-skl-channel", file="Ui/SkillTimelineText.cs",
         find="\t\tif (skl + rec == 0)",
         repl="\t\tif (rec == 0)",
         expect="ui/skill-timeline-text/a-skill-command-channel-with-rows-does-not-warn"),
    dict(name="skilltimeline-over-row-loses-its-highlight", file="Ui/SkillTimelineText.cs",
         find="\t\t\t: TimelineLineStyle.Header);",
         repl="\t\t\t: TimelineLineStyle.Row);",
         expect="ui/skill-timeline-text/an-over-skill-row-is-highlighted"),
    dict(name="skilltimeline-channel-line-drops-its-label", file="Ui/SkillTimelineText.cs",
         find="\t\t\t+ \" \u6761 / skl(\u5965\u4e49\u7279\u6b8a\u547d\u4ee4) \" + skl.ToString(CultureInfo.InvariantCulture)",
         repl="\t\t\t+ \" \u6761 / skl \" + skl.ToString(CultureInfo.InvariantCulture)",
         expect="ui/skill-timeline-text/the-channel-line-counts-all-three-channels"),
    dict(name="comment-only-control", file="Model/BattleSession.cs",
         find="/// <summary>Advance the clock by one frame's REAL seconds (already stall-clamped by the caller).",
         repl="/// <summary>Advance the clock by one frame's REAL seconds (already stall-clamped by the caller) [prose].",
         expect=None),
]

def build_and_run(srcroot):
    """Returns (build_ok, exit_code, stdout). Child output goes to a FILE, never through a pipe."""
    logdir = tempfile.mkdtemp(prefix="negctl_")
    bl = os.path.join(logdir, "build.txt")
    rl = os.path.join(logdir, "run.txt")
    with io.open(bl, "w", encoding="utf-8") as fh:
        rc = subprocess.call([DOTNET, "build", PROJ, "-c", "Release", "-t:Rebuild", "-v", "quiet",
                              "-p:SrcRoot=" + srcroot], cwd=HERE, stdout=fh, stderr=subprocess.STDOUT)
    if rc != 0:
        return False, -1, io.open(bl, encoding="utf-8", errors="replace").read()
    with io.open(rl, "w", encoding="utf-8") as fh:
        code = subprocess.call([EXE, "--quiet"], cwd=HERE, stdout=fh, stderr=subprocess.STDOUT)
    return True, code, io.open(rl, encoding="utf-8", errors="replace").read()


def apply_mutation(tmp, mut):
    """Copy src/ once per run, replace EXACTLY one occurrence. Returns (srcroot, applied, detail)."""
    if os.path.isdir(tmp):
        shutil.rmtree(tmp)
    shutil.copytree(SRC, tmp, ignore=shutil.ignore_patterns("bin", "obj", "__pycache__"))
    p = os.path.join(tmp, mut["file"].replace("/", os.sep))
    if not os.path.isfile(p):
        return tmp, False, "mutation target missing: " + mut["file"]
    t = io.open(p, encoding="utf-8").read()
    n = t.count(mut["find"])
    if n != 1:
        return tmp, False, "find text occurs %d times (need exactly 1)" % n
    io.open(p, "w", encoding="utf-8").write(t.replace(mut["find"], mut["repl"]))
    return tmp, True, "replaced 1 occurrence in " + mut["file"]


def one(mut, keep):
    tmp = os.path.join(tempfile.gettempdir(), "dpsm_negctl_" + re.sub(r"[^A-Za-z0-9_.-]", "_", mut["name"]))
    srcroot, applied, detail = apply_mutation(tmp, mut)
    if not applied:
        print("  [FAIL] %-32s %s" % (mut["name"], detail))
        return 1
    ok, code, out = build_and_run(srcroot)
    expect = mut.get("expect")
    # R52 FIX. Every branch used to print and then fall through to "return 0", so the summary line
    # ("N failure(s) of M") only ever counted a mutation that failed to APPLY, and the pipeline's exit
    # code said "clean" while the transcript said "did not bite". A gate whose counter cannot see its own
    # failures is exactly the defect this project keeps hunting, so each branch now returns 1 and
    # --selftest proves it with a mutation that applies, compiles and changes nothing.
    verdict = 0
    if not ok:
        print("  [FAIL] %-32s build FAILED against the mutated copy (a mutant must still compile)" % mut["name"])
        print("         " + " | ".join(out.strip().splitlines()[-3:])[:200])
        verdict = 1
    elif expect is None:
        if code == 0:
            print("  [PASS] %-32s prose-only mutation stays GREEN (%s)" % (mut["name"], detail))
        else:
            print("  [FAIL] %-32s prose-only mutation turned the suite RED (the suite is not reading behaviour)" % mut["name"])
            print("         " + " | ".join(l for l in out.splitlines() if l.startswith("FAIL"))[:300])
            verdict = 1
    else:
        reds = [l for l in out.splitlines() if l.startswith("FAIL ")]
        hit = any(("FAIL " + expect) in l for l in reds)
        if code != 0 and hit:
            print("  [PASS] %-32s -> %s went red" % (mut["name"], expect))
        elif code == 0:
            print("  [FAIL] %-32s suite stayed GREEN; %s did not bite" % (mut["name"], expect))
            verdict = 1
        else:
            print("  [FAIL] %-32s went red on the WRONG case (wanted %s)" % (mut["name"], expect))
            print("         reds: " + " | ".join(reds)[:300])
            verdict = 1
    m = re.search(r"behavior tests: cases=(\d+) failed=(\d+) pinned=(\d+)", out)
    if m:
        print("         cases=%s failed=%s pinned=%s" % (m.group(1), m.group(2), m.group(3)))
    if not keep:
        shutil.rmtree(tmp, ignore_errors=True)
    return verdict


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--list", action="store_true")
    ap.add_argument("--only", action="append", default=[])
    ap.add_argument("--keep", action="store_true")
    ap.add_argument("--selftest", action="store_true", help="prove the driver itself can say no")
    a = ap.parse_args()
    if a.list:
        for m in MUTATIONS:
            print("%-32s %-40s %s" % (m["name"], m["file"], m.get("expect") or "(must stay green)"))
        return 0
    if not os.path.isfile(DOTNET):
        print("BLOCK: dotnet not found at " + DOTNET)
        return 1
    if not os.path.isfile(EXE):
        print("BLOCK: build the behavior suite first: dotnet build " + PROJ)
        return 1
    sel = [m for m in MUTATIONS if m["name"] == "comment-only-control"]
    bogus = dict(name="selftest-bogus-find", file="Model/BattleSession.cs",
                 find="this text does not exist anywhere", repl="x", expect=None)
    # R52: a mutation that APPLIES and COMPILES but changes no behaviour. Before the counter fix it was
    # indistinguishable from a passing control; now it must be counted as a failure, which is what proves
    # the summary line can see "did not bite".
    noop = dict(name="selftest-noop-mutation", file="Model/BattleSession.cs",
                find="public const double HitMatchSeconds = 0.35;",
                repl="public const double HitMatchSeconds = 0.35 ;",
                expect="window/constants/hit-match-window-is-0.35s")
    if a.selftest:
        print("negative-control driver selftest (3 case(s)):")
        cases = [
            ("a mutation whose find text does not match is a DRIVER failure",
             one(bogus, a.keep) == 1),
            ("a prose-only mutation stays GREEN", one(sel[0], a.keep) == 0),
            ("a mutation that applies but does not bite is COUNTED as a failure",
             one(noop, a.keep) == 1),
        ]
        bad = 0
        for label, ok in cases:
            print("  [%s] %s" % ("PASS" if ok else "FAIL", label))
            if not ok:
                bad += 1
        print("driver selftest: %d case(s), %d failed" % (len(cases), bad))
        return 1 if bad else 0
    picks = [m for m in MUTATIONS if not a.only or m["name"] in a.only]
    if not picks:
        print("BLOCK: --only matched no mutation")
        return 1
    print("negative control: %d mutation(s), temp copies of src (the real sources are never modified)" % len(picks))
    bad = 0
    for m in picks:
        bad += one(m, a.keep)
    print("negative control: %d failure(s) of %d" % (bad, len(picks)))
    # Leave the tree consistent: the mutant builds overwrite bin/, so rebuild once against the REAL src.
    with io.open(os.path.join(tempfile.gettempdir(), "negctl_final_build.txt"), "w", encoding="utf-8") as fh:
        subprocess.call([DOTNET, "build", PROJ, "-c", "Release", "-t:Rebuild", "-v", "quiet"],
                        cwd=HERE, stdout=fh, stderr=subprocess.STDOUT)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
