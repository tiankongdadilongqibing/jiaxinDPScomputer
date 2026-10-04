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
    dict(name="counters-thirteenth-field", file="Runtime/BattleRuntimeCounters.cs",
         find="\tpublic int GameTimeAtStart;",
         repl="\tpublic int GameTimeAtStart;\n\tpublic int ExtraCounter;",
         expect="runtime/counters-shape/the-family-has-twelve-counters"),
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
         find="\tpublic const int T1LineWidth = 85;",
         repl="\tpublic const int T1LineWidth = 84;",
         expect="columns/spec/t1-line-width-is-85"),
    # ---- RF5d: the data-row builders ----
    dict(name="rows-t3-arrow-width", file="Ui/ContributionColumns.cs",
         find="\t\t     + DisplayFormat.PadR(\"→\", T3Arrow)",
         repl="\t\t     + DisplayFormat.PadR(\"→\", T3Hits)",
         expect="columns/rows/a-typical-t3-row-is-53-wide"),
    dict(name="rows-t1-share-width", file="Ui/ContributionColumns.cs",
         find="\t\t     + DisplayFormat.PadL(DisplayFormat.Pct(sharePct), T1Share)",
         repl="\t\t     + DisplayFormat.PadL(DisplayFormat.Pct(sharePct), T1Total)",
         expect="columns/rows/a-typical-t1-row-is-85-wide"),
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
         find="\t\t     + \"  他人因你 \" + DisplayFormat.Fmt(assist)\n\t\t     + \"  被队友分走 \" + DisplayFormat.Fmt(received);",
         repl="\t\t     + \"  他人因你 \" + DisplayFormat.Fmt(received)\n\t\t     + \"  被队友分走 \" + DisplayFormat.Fmt(assist);",
         expect="fallback/contribution/the-actor-line-is-verbatim"),
    dict(name="fallback-totals-drops-the-percent", file="Ui/FallbackText.cs",
         find="\t\t     + \"   未归因 \" + DisplayFormat.Fmt(unattributed) + \"(\" + DisplayFormat.Pct(unattrPct) + \")\"",
         repl="\t\t     + \"   未归因 \" + DisplayFormat.Fmt(unattributed) + \"(\" + DisplayFormat.Fmt(unattrPct) + \")\"",
         expect="fallback/contribution/the-totals-line-is-verbatim"),
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
    if not ok:
        print("  [FAIL] %-32s build FAILED against the mutated copy (a mutant must still compile)" % mut["name"])
        print("         " + " | ".join(out.strip().splitlines()[-3:])[:200])
    elif expect is None:
        if code == 0:
            print("  [PASS] %-32s prose-only mutation stays GREEN (%s)" % (mut["name"], detail))
        else:
            print("  [FAIL] %-32s prose-only mutation turned the suite RED (the suite is not reading behaviour)" % mut["name"])
            print("         " + " | ".join(l for l in out.splitlines() if l.startswith("FAIL"))[:300])
    else:
        reds = [l for l in out.splitlines() if l.startswith("FAIL ")]
        hit = any(("FAIL " + expect) in l for l in reds)
        if code != 0 and hit:
            print("  [PASS] %-32s -> %s went red" % (mut["name"], expect))
        elif code == 0:
            print("  [FAIL] %-32s suite stayed GREEN; %s did not bite" % (mut["name"], expect))
        else:
            print("  [FAIL] %-32s went red on the WRONG case (wanted %s)" % (mut["name"], expect))
            print("         reds: " + " | ".join(reds)[:300])
    m = re.search(r"behavior tests: cases=(\d+) failed=(\d+) pinned=(\d+)", out)
    if m:
        print("         cases=%s failed=%s pinned=%s" % (m.group(1), m.group(2), m.group(3)))
    if not keep:
        shutil.rmtree(tmp, ignore_errors=True)
    return 0


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
    if a.selftest:
        print("negative-control driver selftest (2 case(s)):")
        bad = 0
        bad += one(bogus, a.keep)      # a mutation that does not apply must be a DRIVER failure
        bad += one(sel[0], a.keep)     # prose-only must stay green
        print("driver selftest: %d failure(s)" % bad)
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
