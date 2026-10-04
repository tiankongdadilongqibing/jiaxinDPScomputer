# -*- coding: utf-8 -*-
"""Contribution validation GATE: one status vocabulary and one exit-code contract.

WHY THIS FILE EXISTS (P0-A, 2026-10-04)
    Before it, every gate invented its own vocabulary and its own exit convention, and several of
    them exited 0 exactly when they had nothing to check:

      * contrib/crosscheck.py printed "status=ABSENT ... note: plugin export has no contribution
        section yet (Phase E not deployed)" and returned 0 -- for a 1.7.6 export. A CURRENT-version
        file that lost its contribution section looked identical to a 1.5.5 file that legitimately
        predates the schema.
      * crosscheck.py never looked at the ERROR list contrib/validate.py returns; the schema/identity
        errors were computed and thrown away, so "the numbers agree" could be printed over a file
        whose per-hit identity was broken.
      * contrib/tests/test_golden_155.py returned 0 when its pinned export was absent ("SKIP golden").
      * check_fact_signature.py returned 0 on STARVED replay, on an empty file list, and never
        checked that its cap literals still match src/Diagnostics/FactStore.cs.

    This module is the single place that decides what a status MEANS and which exit code it maps to.
    It changes no contribution formula, no KPI definition and no plugin behaviour: it only classifies
    what the existing offline cores already compute.

STATUS / EXIT CONTRACT (frozen; every entry point above follows it)
    PASS                  -> exit 0    every applicable check ran and held
    WARNING               -> exit 0    ran, held, but printed a caveat (e.g. sub-unity folds)
    LEGACY_NOT_APPLICABLE -> exit 0    the check CANNOT apply to this input, and the reason is
                                       printed. Only for formats/versions that predate the feature.
    DATA_MISSING          -> exit 3    the input should have carried the data but does not
                                       (truncated file, wiped export, version >= 1.6.0 with no
                                       contribution section). Never folded into PASS.
    ERROR                 -> exit 1    ran and found a violation, or could not run at all
                                       (exception, unusable input, invalid analysis)
    Exit priority when several apply: ERROR(1) > DATA_MISSING(3) > else (0).
    An unknown status is a programming error: exit_code() raises rather than guessing.

WHY 1.5.x ABSENT IS LEGACY_NOT_APPLICABLE, NOT PASS OR ERROR
    Measured on the 26 exports in BepInEx/plugins/DpsMeter/exports (2026-10-04):
        version 1.5.3 (2), 1.5.4 (8), 1.5.5 (3)  -> 13 files, contribution section ABSENT
        version 1.6.0 (1) .. 1.7.6 (3)           -> 13 files, contribution section present
    The section first existed in 1.6.0 (HANDOFF section 7.2.86, "out["contribution"]" gated on
    CfgContribution from 1.6.0 on), so a 1.5.x file cannot be asked to carry it: the right answer is
    "this check does not apply", printed with the version that proves it. It is emphatically NOT a
    PASS for the current version: no 1.6.0+ export may ever report LEGACY_NOT_APPLICABLE for a
    missing section, and no legacy file may ever report PASS (see classify_section + test_gate).
"""
from __future__ import annotations

# ---- the status vocabulary -------------------------------------------------
PASS = "PASS"
WARNING = "WARNING"
LEGACY_NOT_APPLICABLE = "LEGACY_NOT_APPLICABLE"
DATA_MISSING = "DATA_MISSING"
ERROR = "ERROR"

STATUSES = (PASS, WARNING, LEGACY_NOT_APPLICABLE, DATA_MISSING, ERROR)

EXIT_PASS = 0
EXIT_WARNING = 0
EXIT_LEGACY_NOT_APPLICABLE = 0
EXIT_ERROR = 1
EXIT_DATA_MISSING = 3

EXIT = {
    PASS: EXIT_PASS,
    WARNING: EXIT_WARNING,
    LEGACY_NOT_APPLICABLE: EXIT_LEGACY_NOT_APPLICABLE,
    ERROR: EXIT_ERROR,
    DATA_MISSING: EXIT_DATA_MISSING,
}

# ---- gate conditions whose exit code is fixed HERE, not at the call site ----
EXIT_ON_VALIDATE_ERROR = EXIT_ERROR       # crosscheck must consume validate ERROR
EXIT_ON_MISMATCH = EXIT_ERROR             # value disagreement between the two implementations
EXIT_ON_MISSING_MANDATORY = EXIT_ERROR    # a mandatory contribution field absent from the plugin
EXIT_ON_MISSING_SECTION = EXIT_DATA_MISSING   # version >= 1.6.0 and no contribution section
EXIT_ON_MISSING_GOLDEN = EXIT_DATA_MISSING    # pinned golden export absent
EXIT_ON_FACT_STARVED = EXIT_ERROR         # the shipped FACT signature starves
EXIT_ON_FACT_CAP_DRIFT = EXIT_ERROR       # check_fact_signature.py caps != FactStore.cs
EXIT_ON_FACT_COVERAGE = EXIT_ERROR        # plugin FACT bookkeeping disagrees with the export
EXIT_ON_TRAINING_SAME_VERDICT = EXIT_LEGACY_NOT_APPLICABLE   # quest 9999 has its own applicability

# ---- version gate ----------------------------------------------------------
# The first plugin version that must carry the contribution section (measured; see module docstring).
CONTRIBUTION_SINCE_VERSION = (1, 6, 0)


def version_tuple(v):
    """'1.7.6' -> (1, 7, 6). Unparsable / absent -> (0,), i.e. older than everything."""
    if isinstance(v, (list, tuple)):
        try:
            return tuple(int(x) for x in v)
        except (TypeError, ValueError):
            return (0,)
    if not isinstance(v, str):
        return (0,)
    head = v.strip().split("-")[0].split("+")[0]
    out = []
    for part in head.split("."):
        try:
            out.append(int(part))
        except ValueError:
            break
    return tuple(out) if out else (0,)


def version_at_least(v, floor=CONTRIBUTION_SINCE_VERSION):
    return version_tuple(v) >= tuple(floor)


def version_text(v):
    return ".".join(str(x) for x in version_tuple(v)) or "?"


# ---- reason types ----------------------------------------------------------
# "note" is deliberately OUTSIDE the four verdict severities: it records that a sub-check could not
# run on this input (e.g. the atkAdd identities on a pre-1.7.4 export) without affecting the status of
# the checks that DID apply. It is always printed.
REASON_NOTE = "note"
REASON_OK = "ok"
REASON_WARN = "warning"
REASON_LEGACY = "legacy"
REASON_MISSING = "missing"
REASON_ERROR = "error"
VERDICT_SEVERITIES = (REASON_OK, REASON_WARN, REASON_LEGACY, REASON_MISSING, REASON_ERROR)


def reason(severity, code, message, **fields):
    r = {"severity": severity, "code": code, "message": message}
    r.update(fields)
    return r


def status_from_reasons(reasons):
    """Fold a reason list into one status. ERROR beats DATA_MISSING beats LEGACY/WARNING/PASS."""
    sev = [r.get("severity") for r in reasons]
    if REASON_ERROR in sev:
        return ERROR
    if REASON_MISSING in sev:
        return DATA_MISSING
    if REASON_LEGACY in sev:
        return LEGACY_NOT_APPLICABLE
    if REASON_WARN in sev:
        return WARNING
    return PASS


def exit_code(status):
    if status not in EXIT:
        raise ValueError("unknown gate status %r (allowed: %s)" % (status, ", ".join(STATUSES)))
    return EXIT[status]


def combine(statuses):
    """Highest-priority status of several gates. ERROR > DATA_MISSING > LEGACY > WARNING > PASS."""
    worst = PASS
    rank = {PASS: 0, WARNING: 1, LEGACY_NOT_APPLICABLE: 2, DATA_MISSING: 3, ERROR: 4}
    for s in statuses:
        if s not in rank:
            raise ValueError("unknown gate status %r" % (s,))
        if rank[s] > rank[worst]:
            worst = s
    return worst


# ---- contribution section presence ----------------------------------------


def classify_section(version, section_present):
    """Decide what a missing/present contribution section means for a given plugin version.

    Returns (status, reason). A section that IS present is always PASS here: its contents are the
    value checks' business (and a 1.6.0 file with a section still has to pass crosscheck).
    """
    if section_present:
        return PASS, reason(REASON_OK, "SECTION_PRESENT",
                            "contribution section present (plugin version %s)" % version_text(version))
    if not version_at_least(version):
        return LEGACY_NOT_APPLICABLE, reason(
            REASON_LEGACY, "SECTION_LEGACY",
            "plugin export version %s predates the contribution schema (first emitted by %s); "
            "the section cannot be expected here, so this gate does not apply -- it is NOT a pass "
            "for the current version" % (version_text(version), version_text(CONTRIBUTION_SINCE_VERSION)))
    return DATA_MISSING, reason(
        REASON_MISSING, "SECTION_MISSING",
        "plugin export version %s must carry the contribution section (emitted since %s) but the "
        "file has none: the export is truncated, the CfgContribution flag was off, or the section "
        "was stripped" % (version_text(version), version_text(CONTRIBUTION_SINCE_VERSION)))


def mandatory_fields(section):
    """(name, present) for every field a contribution section must carry to be checkable.

    NOTE: mandatory-field NAMES were read off the deployed 1.7.6/1.7.7 producers, never invented
    here. A producer that follows the plan's field list exactly is legitimate; a producer that omits
    one of these is not.
    """
    coverage = section.get("coverage") or {}
    pairs = (
        ("contribution.method", section.get("method")),
        ("contribution.damageBasis", section.get("damageBasis")),
        ("contribution.totalDamage", section.get("totalDamage")),
        ("contribution.attributedDamage", section.get("attributedDamage")),
        ("contribution.unattributedDamage", section.get("unattributedDamage")),
        ("contribution.coverage.analyzableDealt", coverage.get("analyzableDealt")),
        ("contribution.actors", section.get("actors")),
        ("contribution.rules", section.get("rules")),
        ("contribution.links", section.get("links")),
        ("contribution.schemaVersion", section.get("schemaVersion")),
    )
    return [(n, v is not None) for n, v in pairs]


# ---- validate.check consumption -------------------------------------------


def validate_result(an, export, issues=None, summary=None):
    """Run (or reuse) contrib.validate.check and reduce it to this gate's contract.

    WHY THIS FUNCTION EXISTS. Before P0-A, crosscheck.py called validate.check only to copy
    `summary` out of it and threw the ERROR list away, so a file whose own identity was broken could
    still print "OK". Returning the exit code from the same place as the status makes that omission
    impossible to repeat.

    Returns (exit_code, status, errors, warnings, summary). `errors` is the raw
    [(level, code, message)] list so callers can print the detail.
    """
    if issues is None:
        from contrib import validate as vmod
        issues, summary = vmod.check(an, export)
    errors = [i for i in issues if i[0] == "ERROR"]
    warnings = [i for i in issues if i[0] == "WARN"]
    if errors:
        status = ERROR
    elif warnings:
        status = WARNING
    else:
        status = PASS
    return exit_code(status), status, errors, warnings, summary


# ---- training-ground applicability (quest 9999) ----------------------------


def training_applicability(quest, training=None):
    """quest 9999 is the training ground (dictionary 8.4: x0.03 / sub-unity rules).

    Returns (applicability, reason). 'not_comparable' means the normal contribution model's verdict
    must not be carried over to a normal battle; the numbers are still computed and reported, but the
    gate hands back its own status (LEGACY_NOT_APPLICABLE = "this check does not apply here").
    """
    q = int(quest or 0)
    is_training = (q == 9999) if training is None else bool(training)
    if not is_training:
        return "full", reason(REASON_OK, "MODEL_APPLICABLE", "quest %s: normal contribution model" % q)
    return "not_comparable", reason(
        REASON_LEGACY, "TRAINING_NOT_COMPARABLE",
        "quest %s is the training ground (special x0.03 / sub-unity rules): the normal contribution "
        "model is recorded but NOT comparable with ordinary battles, and this gate returns its own "
        "applicability result instead of the normal PASS" % q)


# ---- atkAdd counter identities --------------------------------------------
#
# Measured 2026-10-04 on all 26 exports. The only EXACT relation that holds on every file is the
# field-name one, because the counters do not carry the root-level semantics their names suggest:
#   * 'emitted' is NOT a hit count. battle_9999_20261004_042458: atkAdd.emitted=509 while 472 events
#     carry a non-empty calc.atkAdd -- one hit can emit one row PER GIVER, so a hit with two givers
#     adds 2. Non-empty rows == 509 == emitted there, exactly.
#   * 'selfValues' is NOT a hit count either: battle_411001_20261004_115417 has hits=6191 but
#     selfValues=7003, and battle_700817_20261004_114821 has hits=129 but selfValues=179.
#   * the skip counters CANNOT be combined into one total. Measured: battle_411001_20261004_115417
#     skippedGuard=131 + skippedCollision=50 with hits=6191, so the un-skipped value list is twice the
#     emitted rows; there is no exported per-event input from which (number of skipped VALUE ENTRIES)
#     could be recomputed, because the export only carries the rows that were KEPT.
# The three relations below are therefore the whole verifiable set. skip counters get a RANGE check
# (non-negative and not larger than the number of captured items) plus the exact per-file measurement
# rule "sum(rules[].folds where kind==atkadd) == calc.atkAdd rows" above.
# The fields the 1.7.4-1.7.7 producers write. P1-A adds more (SelfByKey / SelfByNameFallback /
# NameCollision / OwnerUnknown); the check below does NOT hard-code the list it accepts -- it validates
# every numeric counter it FINDS and only requires these to exist, so an added counter is checked
# instead of rejected, and a counter that stops being written is noticed.
ATKADD_COUNTER_FIELDS = ("hits", "emitted", "selfValues", "skippedGuard", "skippedCollision",
                         "skippedUnowned", "skippedOwnerNull", "skippedNegative", "skippedType")
ATKADD_REQUIRED_FIELDS = ("hits", "emitted", "skippedGuard", "skippedCollision", "skippedUnowned",
                          "skippedOwnerNull", "skippedNegative", "skippedType")


def atkadd_counter_bounds(root_atkadd, section):
    """RANGE checks only: every counter is a non-negative integer, and no skip class can exceed the
    number of captured value occurrences ('hits' is the closest exported upper bound).

    A missing root block returns [] here: whether that is a NOTE (pre-1.7.4 export that carries no
    calc.atkAdd either) or an ERROR (hits carry calc.atkAdd but the counters are gone) is decided by
    atkadd_section_reasons, which can see the export body.
    """
    reasons = []
    if root_atkadd is None:
        return []
    for f in sorted(ATKADD_REQUIRED_FIELDS):
        if f not in root_atkadd:
            # a producer that stops writing a refusal counter makes "the section gives nobody credit"
            # and "the guards refused everything" indistinguishable -- the exact failure the counters
            # were added to prevent.
            reasons.append(reason(REASON_ERROR, "ATKADD_COUNTER_MISSING",
                                  "root atkAdd block has no required counter '%s'" % f))
    # every NUMERIC counter actually present is range-checked, including ones added after this file
    # was written (P1-A adds four); non-numeric fields are the schema guard's business.
    for f in sorted(root_atkadd):
        v = root_atkadd[f]
        if isinstance(v, bool):
            continue
        if isinstance(v, (int, float)) and (not isinstance(v, int) or v < 0):
            reasons.append(reason(REASON_ERROR, "ATKADD_COUNTER_RANGE",
                                  "atkAdd.%s=%r is not a non-negative integer" % (f, v)))
    hits = root_atkadd.get("hits")
    if isinstance(hits, int):
        for f in sorted(root_atkadd):
            if f == "hits":
                continue
            v = root_atkadd[f]
            if isinstance(v, int) and not isinstance(v, bool) and v > hits:
                reasons.append(reason(
                    REASON_WARN, "ATKADD_COUNTER_GT_HITS",
                    "atkAdd.%s=%d exceeds atkAdd.hits=%d: one hit may emit one row per giver or carry "
                    "several values, so this is legal but means the counter is per-value, not per-hit"
                    % (f, v, hits)))
    return reasons


def atkadd_section_reasons(root_atkadd, section, nonempty_hits, rows, rows_indomain=None):
    """Cross-check the counters against the export body.

    nonempty_hits = events of type 'dmg' whose calc.atkAdd list is non-empty (one per hit)
    rows          = total number of calc.atkAdd entries in those events (one per giver per hit)
    rows_indomain = the same entries counted only on hits the contribution model analyses, i.e. the
                    attacker key belongs to the analysed team (the Python twin of
                    Contribution.cs Compute's     if (hit.AttackerKey == 0 ||
                    !ix.Team.TryGetValue(hit.AttackerKey, out attacker)) continue;
                    which aggregate.analyze mirrors as "row.team != team -> outside_team_events").
                    None when the caller cannot delimit the domain; the folds check then falls back to
                    the all-in-domain case only, never to an ERROR over a mixed body.
    """
    reasons = []
    if root_atkadd is None:
        if nonempty_hits > 0:
            # the body proves the counters were produced, so their absence is a real loss
            reasons.append(reason(REASON_ERROR, "ATKADD_SECTION_MISSING",
                                  "%d hits carry calc.atkAdd but the export has no root atkAdd "
                                  "counter block" % nonempty_hits))
        elif section is not None:
            # a pre-1.7.4 export whose contribution section is perfectly checkable: the atkAdd
            # identities did not exist yet, so this sub-check is skipped and SAID to be skipped.
            reasons.append(reason(REASON_NOTE, "ATKADD_ABSENT",
                                  "no root atkAdd counters and no calc.atkAdd rows (pre-1.7.4 "
                                  "export): the atkadd counter identities could not be checked"))
        return reasons
    emitted = root_atkadd.get("emitted")
    if emitted is None:
        return reasons + [reason(REASON_ERROR, "ATKADD_EMITTED_MISSING",
                                 "root atkAdd block has no 'emitted' field")]
    # (1) exact, measured on every 1.7.4+ export in the corpus
    if emitted != rows:
        reasons.append(reason(
            REASON_ERROR, "ATKADD_EMITTED_VS_BODY",
            "atkAdd.emitted=%s != non-empty calc.atkAdd entries in the export=%d" % (emitted, rows)))
    if section is None:
        return reasons
    # (2) EXACT, but only over the analysed DOMAIN. This check used to compare the folds against ALL
    #     non-empty calc.atkAdd rows and produced a false ERROR on the training ground
    #     (battle_9999_20261004_042458: folds=34 vs 509 rows). The integer relation holds on the domain
    #     Contribution.Compute actually walks: a hit reaches the fold loop only when
    #     AttackerKey != 0 and the attacker row is in the analysed team (Contribution.cs Compute:
    #     `if (hit.AttackerKey == 0 || !ix.Team.TryGetValue(hit.AttackerKey, out attacker)) continue;`).
    #     Its Python twin is aggregate.analyze's `row.team != team -> outside_team_events; continue`.
    #     MEASURED on all 8 exports that carry calc.atkAdd (2026-10-04):
    #       411001 1.7.4-1.7.6 (6 files): in-domain == total == folds (5296..6006)
    #       700817 1.7.6:                 45 == 45 == 45
    #       9999   1.7.5:                 34 == 34 in-domain folds, out of 509 total rows
    #     The 475 out-of-domain rows belong to team-2 / non-team-1 attackers and never enter Compute.
    atkadd_folds = sum(int(r.get("folds") or 0) for r in (section.get("rules") or [])
                       if r.get("kind") == "atkadd")
    out_of_domain = max(0, rows - (rows if rows_indomain is None else rows_indomain))
    if atkadd_folds == 0:
        reasons.append(reason(REASON_NOTE, "ATKADD_FOLDS_ABSENT",
                              "the section carries no kind=atkadd rule while the body has %d "
                              "calc.atkAdd entrie(s) (offline producer or fold capture off): the fold "
                              "restatement cannot be checked, but the root counters above still were"
                              % rows))
    elif rows_indomain is None:
        # the caller could not delimit the domain -- the only safe claim left is the all-in-domain case
        if atkadd_folds != rows:
            reasons.append(reason(REASON_NOTE, "ATKADD_FOLDS_DOMAIN_UNKNOWN",
                                  "the section authors %d kind=atkadd fold(s) and the body has %d "
                                  "non-empty calc.atkAdd entries, but this caller could not delimit "
                                  "the analysed domain, so the relation cannot be asserted here"
                                  % (atkadd_folds, rows)))
    elif atkadd_folds == rows_indomain:
        if out_of_domain:
            reasons.append(reason(
                REASON_NOTE, "ATKADD_FOLDS_OUT_OF_DOMAIN",
                "the section restates exactly the %d in-domain kind=atkadd fold(s); %d of the %d "
                "non-empty calc.atkAdd entrie(s) belong to attackers outside the analysed team and "
                "never reach the model (expected, not a shortfall)" % (atkadd_folds, out_of_domain, rows)))
    else:
        reasons.append(reason(REASON_ERROR, "ATKADD_FOLDS_VS_BODY",
                              "contribution rules kind=atkadd folds=%d != non-empty calc.atkAdd "
                              "entries on in-domain hits=%d%s: the section authors atkadd rows but "
                              "does not restate all of them"
                              % (atkadd_folds, rows_indomain,
                                 (" (all %d body rows are in-domain)" % rows) if not out_of_domain
                                 else (" (%d of %d body rows are outside the analysed team)"
                                       % (out_of_domain, rows)))))
    # (3) strict identity, measured on all 8 exports that carry calc.atkAdd
    if emitted > rows:
        reasons.append(reason(
            REASON_ERROR, "ATKADD_SKIPPED_CANNOT_EXCEED",
            "atkAdd.emitted=%s > non-empty calc.atkAdd entries=%d: a field name says more rows were "
            "written than the export carries" % (emitted, rows)))
    return reasons


# ---- granted-talent counters vs the body (1.7.9) -------------------------------------------
GIVE_APPLIED_FIXED_VERSION = (1, 7, 9)


def give_section_reasons(roster, version, body_steps, body_hits_applied, dropped_steps=0):
    """Cross-check the granted-talent self-report counters against the export body.

    WHERE THE COUNTERS LIVE (this is why the first version of this check was a no-op): giveApplied and
    giveFoldHits are written INSIDE the root rosterAudit block (ExportService.cs appends
    `,"rosterAudit":{...` there) and NOT at the root. The first version was handed the root dict, so every
    lookup returned None and the whole check silently collapsed into a single GIVE_COUNTERS_ABSENT note --
    a gate that never ran while reporting that it had nothing to check. It now takes that block itself.

    MEASURED (2026-10-04, then re-measured independently): giveApplied == body_steps + body_hits held
    EXACTLY in 28/28 files with a non-zero count (battle_700817_20261004_114821 is 0/0/0 and satisfies
    both readings). The cause was one static incremented under two different units: once per granted ENTRY
    inside GivenTalentDamage, and once per HIT in its caller.

    body_steps        = kind="given" fold steps in the exported events (== giveApplied from 1.7.9 on).
    body_hits_applied = hits whose given-factor PRODUCT is not 1.0. It is deliberately NOT "hits carrying
    a step": exactly-cancelling entries (0.5 x 2.0) register a step and count as entries while the folded
    product stays 1.0, so the caller passes the product-aware count.
    dropped_steps     = exported hits that dropped fold steps at FoldContext.MaxSteps. A dropped step is
    invisible in the body, so when any exists the identities can only be asserted as a lower bound.

    A 1.7.9+ file must satisfy BOTH identities, and a missing counter on such a file is DATA_MISSING --
    never a silent skip. An older file that still shows the mixed sum is a note, not an error: it cannot
    be re-exported, and reddening the archive would not make it true.
    """
    reasons = []
    ga = None if roster is None else roster.get("giveApplied")
    fh = None if roster is None else roster.get("giveFoldHits")
    legacy = not version_at_least(version, GIVE_APPLIED_FIXED_VERSION)
    # (1) the entry-level counter. No branch returns early: the hit-level check below must still run when
    #     THIS is the field that is missing.
    if ga is None:
        if legacy:
            reasons.append(reason(REASON_NOTE, "GIVE_COUNTERS_ABSENT",
                                  "no granted-talent counters (pre-1.3.5 export): giveApplied/giveFoldHits "
                                  "could not be checked while the body carries %d fold step(s) of "
                                  "kind=\"given\"" % body_steps))
        else:
            reasons.append(reason(REASON_MISSING, "GIVE_APPLIED_MISSING",
                                  "version %s must export the granted-talent counters inside rosterAudit, "
                                  "but that block carries no giveApplied (the body has %d kind=\"given\" "
                                  "fold step(s))" % (version_text(version), body_steps)))
    elif ga == body_steps:
        pass
    elif legacy and ga == body_steps + body_hits_applied:
        reasons.append(reason(REASON_NOTE, "GIVE_APPLIED_LEGACY_MIXED",
                              "giveApplied=%d == entries(%d) + applied hits(%d): this version added one "
                              "increment per granted entry and one per hit into the same counter, so it "
                              "measures neither. 1.7.9 split the hit-level increment into giveFoldHits. "
                              "Not an error here because a legacy export cannot be re-written."
                              % (ga, body_steps, body_hits_applied)))
    else:
        reasons.append(reason(REASON_ERROR, "GIVE_APPLIED_VS_BODY",
                              "giveApplied=%d != kind=\"given\" fold steps in the body=%d (version %s)%s"
                              % (ga, body_steps, version_text(version),
                                 "; the legacy entries+hits reading would have been %d"
                                 % (body_steps + body_hits_applied) if legacy else "")))
    # (2) the hit-level counter
    if fh is None:
        if not legacy:
            reasons.append(reason(REASON_MISSING, "GIVE_FOLDHITS_MISSING",
                                  "version %s must export giveFoldHits (the hit-level counter introduced "
                                  "in 1.7.9) inside rosterAudit, but the block does not carry it"
                                  % version_text(version)))
    elif dropped_steps:
        if fh > body_hits_applied:
            reasons.append(reason(REASON_ERROR, "GIVE_FOLDHITS_VS_BODY",
                                  "giveFoldHits=%d exceeds the %d hit(s) whose given factors do not "
                                  "cancel, even allowing for %d hit(s) with dropped fold steps"
                                  % (fh, body_hits_applied, dropped_steps)))
        else:
            reasons.append(reason(REASON_NOTE, "GIVE_FOLDHITS_UNDERCOUNTED",
                                  "giveFoldHits=%d <= %d applied hit(s); %d hit(s) dropped fold steps at "
                                  "FoldContext.MaxSteps, so the body cannot confirm the exact count"
                                  % (fh, body_hits_applied, dropped_steps)))
    elif fh != body_hits_applied:
        reasons.append(reason(REASON_ERROR, "GIVE_FOLDHITS_VS_BODY",
                              "giveFoldHits=%d != hits whose given factors do not cancel in the body=%d"
                              % (fh, body_hits_applied)))
    return reasons
