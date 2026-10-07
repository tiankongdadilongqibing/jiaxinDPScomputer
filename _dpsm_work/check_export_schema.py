# -*- coding: utf-8 -*-
"""
DpsMeter export schema checker + coverage reporter  (1.5.2)

WHY THIS EXISTS
---------------
The export has no schema test. `Output/JsonCheck.cs` (1.5.0) verifies at export time that the document
is structurally valid JSON, but nothing verifies that the KEYS a reader depends on are actually there --
and a key that silently disappears is exactly how a script starts returning None instead of a number.
`_dpsm_work/v135_probe.py` was already reading `schemaVersion` / `pluginVersion`, two keys that have
never existed, and printing None without complaining.

So this script is the other half: given an export, it checks the keys (and their types) that the
analysis depends on, and it reports COVERAGE for the fields that are emitted only when they apply.
Running it is the first thing to do with a new export.

USAGE
    python check_export_schema.py [export.json ...]
    python check_export_schema.py --selftest        # prove the checker can reject

No argument: the newest export under BepInEx/plugins/DpsMeter/exports is used.
Writes a UTF-8 report to _dpsm_work/export_schema_report.txt (override with --report PATH) and
prints ASCII-only summary lines
(this console is GBK -- printing CJK to stdout mangles it).
"""

import glob
import io
import json
import os
import sys

ROOT = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(ROOT)
EXPORTS = os.path.join(REPO, 'BepInEx', 'plugins', 'DpsMeter', 'exports')
REPORT = os.path.join(ROOT, 'export_schema_report.txt')

# ---------------------------------------------------------------------------------------------
# What each version guarantees. "required" keys must exist for an export that DECLARES that version;
# "optional" keys may be absent (they are emitted only when the condition applies) but must have the
# declared type when present.
# ---------------------------------------------------------------------------------------------

# always present, since the version noted
ROOT_REQUIRED = [
    ('app', str, '0.9.0'), ('version', str, '0.9.0'), ('quest', int, '0.9.0'),
    ('duration', float, '0.9.0'), ('result', str, '0.9.0'), ('totals', dict, '0.9.0'),
    ('actors', list, '0.9.0'), ('events', list, '0.9.0'),
    ('statusAudit', dict, '0.9.0'), ('rosterAudit', dict, '0.9.0'),
    ('reconcile', dict, '1.3.0'), ('run', dict, '1.3.3'), ('forensics', dict, '1.4.0'),
    ('unattributedByVictim', dict, '0.9.0'),
    # 1.5.0
    ('hitDetail', dict, '1.5.0'), ('timeline', dict, '1.5.0'), ('facts', dict, '1.5.0'),
    ('factCoverage', dict, '1.5.0'), ('giveTypeList', list, '1.5.0'), ('resistMaster', dict, '1.5.0'),
    ('statusValues', list, '1.5.0'), ('layout', dict, '1.5.0'), ('liveRange', dict, '1.5.0'),
    ('subParams', dict, '1.5.0'), ('params', dict, '1.5.0'), ('statsRows', dict, '1.5.0'),
    # 1.7.2 (阶段 G): the OWNER of every applied parameter, i.e. the missing input for attributing a
    # flat/percent 攻击力 addition granted by a teammate (it is not a fold, so the model credits nobody).
    ('paramOwners', dict, '1.7.2'),
    # 1.7.4 (阶段 G): the counters of the per-hit attack-power attribution -- the audit of the REFUSALS.
    ('atkAdd', dict, '1.7.4'),
    # 1.7.12: the settings that change the MEANING of numbers already in this file (today: does
    # totals.dealt include same-team damage?). A file that does not state them cannot be read alone.
    ('config', dict, '1.7.12'),
    # 1.7.29 (R79): the incoming side of the same events, split per victim (受击来源拆分). The F3 page reads
    # it, and it is the only place the two amounts that belong to no attacker bucket are stated. A file that
    # stops writing it takes the whole analyse-incoming story with it, so it is REQUIRED from 1.7.29 on --
    # every export written before this round keeps validating (the section is optional when absent).
    ('takenBreakdown', dict, '1.7.29'),
]

# 1.7.12: the config block. Version-gated by ROOT_REQUIRED, so every older export stays valid.
CONFIG_KEYS = [
    ('filterFriendlyFire', bool, '1.7.12'),
]

ATKADD_KEYS = [
    ('hits', int), ('emitted', int), ('selfValues', int), ('skippedGuard', int),
    ('skippedCollision', int), ('skippedUnowned', int), ('skippedOwnerNull', int),
    ('skippedNegative', int), ('skippedType', int),
]

# 1.7.8 (P1-A): the self determination moved from name equality to the actor key, so these counters say
# WHY a value counted as self and whether the name fallback was needed. Required only for plugin >= 1.7.8,
# so every older export still validates. They also carry a real identity, asserted below.
ATKADD_KEYS_178 = [
    ('selfByKey', int), ('selfByNameFallback', int), ('nameCollision', int), ('ownerUnknown', int),
]

# reconcile keys, with the version that introduced them
RECON_REQUIRED = [
    ('dmgEvents', int, '1.3.0'), ('withCalc', int, '1.3.0'), ('exact', int, '1.3.0'),
    ('exactWithCrit', int, '1.3.9'), ('critInferred', int, '1.3.9'), ('approx', int, '1.3.0'),
    ('unexplained', int, '1.3.0'), ('theoryExceeds', int, '1.3.1'), ('absorbed', int, '1.2.x'),
    ('distinctResiduals', int, '1.3.0'), ('approxTolerance', (int, float), '1.3.0'),
    ('critInferredTolerance', (int, float), '1.3.10'), ('byPair', dict, '1.3.0'),
    ('byTenthExact', list, '1.3.0'), ('byTenthTotal', list, '1.3.0'), ('topResidual', list, '1.3.0'),
    # 1.5.0 (A3): the crit question decomposed
    ('critObserved', int, '1.5.0'), ('critObservedYes', int, '1.5.0'), ('critAgree', int, '1.5.0'),
    ('critInferredButDenied', int, '1.5.0'), ('critObservedButUnexplained', int, '1.5.0'),
]

HITDETAIL_KEYS = [
    ('produced', int), ('trimmed', int), ('errors', int),
    ('matchExact', int), ('matchPair', int), ('matchNone', int),
]

# R62 (1.7.14, A): the damage-detail channel's new self-report -- figures that WERE paired and then
# REJECTED because the composition that describes the hit read a different m_hitType (AttributionPolicy.
# RecordContradictsComposition). Required exactly from 1.7.14 on: every older export never had it, and
# "nothing was available" (matchNone) must stay distinguishable from "what was available was another
# hit's figure" (matchRejected).
HITDETAIL_KEYS_1714 = [('matchRejected', int)]

TIMELINE_KEYS = [
    ('observed', int), ('units', int), ('rowCount', int), ('rowsDropped', int),
    ('statusChanges', int), ('resistChanges', int), ('notCharacter', int),
    ('nullStatus', int), ('nullResistance', int), ('errors', int),
    ('resistSlots', list), ('statusFlags', list), ('rows', list),
]

FACTS_KEYS = [
    ('observed', int), ('classes', int), ('maxClasses', int), ('liveClasses', int),
    ('maxLiveClasses', int), ('classesOverflow', int), ('liveSkipped', int),
    ('stateUnreadable', int), ('errors', int), ('items', list),
]

# 1.7.2 (阶段 G): every entry of a unit's applied-parameter dictionary with its OWNER.
PARAMOWNERS_KEYS = [
    ('reads', int), ('errors', int), ('firstError', str), ('units', int),
    ('entriesSeen', int), ('entryReads', int), ('entriesDropped', int), ('budgetCapped', int),
    ('ownerNull', int), ('ownerSelf', int), ('ownerOther', int), ('keyOwnerNull', int),
    ('ownerErrors', int), ('crossChecked', int), ('mismatch', int), ('distinctOwners', int),
    ('maxEntriesPerRead', int), ('maxUnionRows', int), ('maxEntryReadsPerBattle', int),
    ('unionOverflow', int), ('rows', list),
]

# The row is (unit, target, type, value, ...) plus the OWNER, which is deliberately allowed to be null:
# "the game never populates Owner" is a RESULT this channel must be able to report, not an error. The
# two owner fields must still be PRESENT, because a missing field is indistinguishable from "not read".
# Added in the 1.7.2 rev2 walk and therefore present only from that build on: type-checked when
# present, never required -- requiring them would reject the first 1.7.2 export, which is a real file on
# disk that must stay readable. (Same lesson as the version-gated ROOT_REQUIRED list.)
PARAMOWNERS_OPTIONAL = [
    ('entriesTotal', int), ('ownerGetters', int),
]

PARAMOWNERS_ROW_KEYS = [
    ('unit', str), ('tgt', str), ('ty', str), ('val', int), ('vTgt', str), ('vTy', str),
    ('ref', str), ('firstT', (int, float)), ('lastT', (int, float)), ('seen', int),
]

# per-event keys emitted only when they apply
EVENT_OPTIONAL = [
    ('source', int), ('crit', bool), ('nominal', int), ('healCalc', bool), ('reversal', bool),
    ('atkTeam', int), ('vicTeam', int), ('friendly', bool),
    ('comp', str), ('comp2', str), ('comp3', str), ('comp4', str),
    ('statusDelta', str), ('inflicted', str), ('applier', str), ('triggers', list), ('calc', dict),
    # 1.5.0
    ('critObserved', int), ('hitMatch', int), ('calcHitType', int), ('calcEffectId', int),
    ('atkKey', int), ('vicKey', int), ('factId', int),
    # 1.5.2: the figure the matched damage-detail record carried. `hitMatch` is a verdict; without this
    # field the verdict could not be audited (5181/5220 best-effort on the 1.5.1 battle while the same
    # hits' independent `calc.theory` equalled `amount` exactly).
    ('hitValue', int),
]

CALC_OPTIONAL = [
    # 1.7.4 (阶段 G): the givers of this hit extra attack power, in the full fold vocabulary.
    ('atkAdd', list),
    ('effectId', int), ('hitType', int), ('attackPower', int), ('power', int), ('ratio', (int, float)),
    ('base', int), ('attrMult', (int, float)), ('dealtMult', (int, float)), ('takenMult', (int, float)),
    ('knownMult', (int, float)), ('theory', int), ('applied', int), ('residual', (int, float)),
    ('defense', int), ('defenseKind', str), ('penetration', int), ('effectiveDefense', int),
    ('minRule', bool), ('absorbed', int), ('critRate', int), ('critDamageRate', int),
    ('vicBlocking', int), ('vicUnitBlocking', int), ('vicBlockCount', int),
    ('vicExtra', int), ('vicExtraText', str), ('vicBuffs', int), ('vicGive', int),
    ('vicGiveApplied', int), ('vicGiveText', str), ('madnessRatio', int), ('madnessOn', bool),
    # 1.7.9: whether the General/GivenTalent switch folded the granted 被伤害 entries on THIS hit. Present
    # only from 1.7.9 on; checked for type whenever it IS present, like every other optional per-hit key.
    ('givenFoldOn', bool),
    ('pair', str), ('pairTrusted', bool), ('pairCorroborated', bool), ('valueMatches', bool),
    # 1.5.0 (A1/A5)
    ('fold', list), ('cancel', list), ('responsibility', int), ('maxAbsorbed', int), ('foldDropped', int),
]

FACT_ITEM_KEYS = [
    ('id', int), ('hits', int), ('atk', int), ('vic', int), ('eff', int), ('ht', int),
    ('pair', str), ('known', (int, float)), ('resid', (int, float)),
]

# 1.5.2: the components the SIGNATURE is built from. `atk`/`vic` above are the representative INSTANCE
# (the one the class's single live read came from) and stopped being the identity in 1.5.2 -- a
# per-instance key was 1232 of the 1485 signatures and served no rule. Kept in their own list so the
# requirement is gated: a 1.5.0/1.5.1 export legitimately has no such key, and a checker that demanded
# one would flag the whole archive.
FACT_ITEM_KEYS_152 = [
    ('atkName', str), ('vicName', str), ('atkTeam', int), ('vicTeam', int),
]

FOLD_STEP_KEYS = [
    ('side', str), ('kind', str), ('origin', str), ('factor', (int, float)), ('label', str),
]

CANCEL_STEP_KEYS = [('origin', str), ('value', (int, float)), ('by', str), ('label', str)]

# ---------------------------------------------------------------------------------------------
# Phase E contribution section (optional; present from the version that ships it, absent in 1.5.5).
# An OPTIONAL section is still checked whenever it IS present: a reader that silently reads None out
# of a malformed contribution block is exactly the failure this file exists to prevent. Field names
# follow PROJECT-DIRECTION-AND-IMPLEMENTATION-PLAN section 5-E.
# ---------------------------------------------------------------------------------------------
CONTRIBUTION_REQUIRED = [
    ('schemaVersion', str), ('method', str), ('damageBasis', str),
    ('totalDamage', (int, float)), ('attributedDamage', (int, float)),
    ('unattributedDamage', (int, float)), ('coverage', dict),
    ('actors', list), ('rules', list), ('links', list),
]

# 1.7.11: receivedAssist is REQUIRED, not optional. The F5 table's new 被队友分走 column prints it, and
# the identity 直接打出 = 自身 + 被队友分走 (added below) needs it; a serializer that stopped writing it
# would otherwise make the column read as a measured 0. Every one of the 15 sections on disk has it.
# 1.7.12: the same-team split. Optional on disk (every section written before 1.7.12 lacks it) but
# validated whenever present -- see the identity check in _check_contribution.
CONTRIBUTION_ACTOR_OPTIONAL = [('friendly', (int, float)), ('friendlyHits', int), ('hostileDamage', (int, float))]

CONTRIBUTION_ACTOR_KEYS = [
    ('key', int), ('name', str),
    ('directDamage', (int, float)), ('baseCredit', (int, float)),
    ('selfRuleCredit', (int, float)), ('assistCredit', (int, float)),
    ('receivedAssist', (int, float)), ('totalCredit', (int, float)),
]

CONTRIBUTION_RULE_KEYS = [
    ('ruleName', str), ('kind', str), ('side', str),
    ('hits', int), ('damageEquivalent', (int, float)),
]

CONTRIBUTION_LINK_KEYS = [('fromKey', int), ('toKey', int), ('amount', (int, float)), ('hits', int)]

CONTRIBUTION_COVERAGE_KEYS = [('creditedShare', (int, float)), ('unattributedShare', (int, float))]

# 1.7.8 (P0-B): schemaVersion 1.1. totalDamage changed MEANING (it is analyzableDealt now, not
# analyzableDealt + unattributed), so the new keys below are required only for plugin versions that
# write 1.1 -- every 1.0 file already on disk keeps validating under the legacy identity.
# None is a legal VALUE for the three ratios: their denominator can legitimately be undefined, and
# "not applicable" must never be published as a measured 0.
CONTRIB_SCHEMA_110 = (1, 1)      # the SECTION's own schemaVersion, which is the contract identity
CONTRIB_PLUGIN_110 = (1, 7, 8)   # fallback only, for a section that does not declare a version
COVERAGE_KEYS_110 = [
    ('excludedDamage', (int, float)),
    ('analysisDamageCoverage', (int, float, type(None))),
    ('creditCoverageWithinAnalyzed', (int, float, type(None))),
    ('overallAttributedCoverage', (int, float, type(None))),
]
LEDGER_KEYS = [
    ('totalsDealt', (int, float, type(None))), ('events', int), ('analyzableHits', int),
    ('outsideTeamHits', int), ('outsideTeamDealt', (int, float)),
    ('unknownAttackerHits', int), ('unknownAttackerDealt', (int, float)),
    ('eventSumAll', (int, float)), ('reconciliationGap', (int, float, type(None))),
]

# 1.7.13 (R61, schema 1.2): same-team damage (the ENEMY's 回復反転 / self-damage) left the analysed
# pool, so the ledger carries a THIRD bucket and the two identities gain a term. Required exactly when
# the section declares 1.2; every 1.0/1.1 section keeps validating under the old pair.
LEDGER_KEYS_120 = [('selfTeamHits', int), ('selfTeamDealt', (int, float))]
CONTRIB_SCHEMA_120 = (1, 2)
CONTRIB_PLUGIN_120 = (1, 7, 13)

# ---------------------------------------------------------------------------------------------
# R79 (1.7.29) takenBreakdown: the INCOMING side of the same events, split per VICTIM. It publishes no
# amount the session had not already recorded -- every number in it is `events[].nominal` and
# `events[].amount` re-partitioned -- so the thing worth checking here is not a total but the PARTITION:
# five dimensions (source, hit type, effect, attacker, status) that must each sum to the victim's nominal,
# plus the two amounts that deliberately belong to NO attacker bucket (same-team damage and hits whose
# attacker could not be resolved). Validated whenever present, like contribution, so a pre-R79 export
# still validates and a R79 export cannot silently lose the block.
# ---------------------------------------------------------------------------------------------
TAKEN_REQUIRED = [
    ('schemaVersion', str), ('method', str), ('basis', str),
    ('hits', int), ('nominal', (int, float)), ('taken', (int, float)), ('residual', (int, float)),
    ('friendly', (int, float)), ('friendlyHits', int),
    ('unknown', (int, float)), ('unknownHits', int), ('actors', list),
]

TAKEN_ACTOR_KEYS = [
    ('key', int), ('name', str), ('team', int), ('ally', bool), ('position', int),
    ('hits', int), ('nominal', (int, float)), ('taken', (int, float)), ('residual', (int, float)),
    ('friendly', (int, float)), ('friendlyHits', int),
    ('unknown', (int, float)), ('unknownHits', int),
    ('bySource', list), ('byHitType', list), ('byAttacker', list), ('byEffect', list), ('byStatus', list),
]

TAKEN_BUCKET_KEYS = [
    ('key', int), ('name', str), ('amount', (int, float)), ('hits', int), ('quality', str),
]

TAKEN_STATUS_KEYS = [
    ('status', str), ('applier', str), ('amount', (int, float)), ('hits', int),
]

# The position snapshot's closed vocabulary: 0 = the native read failed (the object was already gone),
# 1 = 前衛, 2 = 後衛. Anything else means a reader is inventing a position.
TAKEN_POSITIONS = (0, 1, 2)

# R80 (1.7.30): the SAME section with no bucket cap -- a dimension lists every bucket instead of folding
# its tail at 64. The shape is unchanged, so this is a version claim, not a new vocabulary: 1.0 means "a
# dimension may end in a folded 其余 row", 1.1 means "it cannot". A 1.1 section inside an older export is
# tolerated (a file is judged by the contract it declares), but a 1.7.30 export still claiming 1.0 is a
# rejection -- an R80 reader that sees 1.0 must not assume the rows add up to the victim's nominal.
TAKEN_SCHEMA_110 = (1, 1)
TAKEN_PLUGIN_110 = (1, 7, 30)
TAKEN_SCHEMAS = ('1.0', '1.1')


def _isnum(x):
    return isinstance(x, (int, float)) and not isinstance(x, bool)


def _check_fold_sides(d, ver, problems):
    """R62 (C): the fold list carries the SIDE of every factor; 1.7.14 made the two summary doubles agree
    with it (battle-wide EnemyTakes rules used to be multiplied into dealtMult). Verified offline BEFORE
    shipping by replaying the corpus with the factor moved: dealtMult == prod(atk steps) and
    takenMult == prod(vic steps) on all 197,533 fold-carrying hits of the live 82 exports, so this is an
    identity and not a hope. Version-gated (<= 1.7.13 bucketed it the other way on purpose) and skipped
    when the per-hit fold cap dropped a step."""
    if ver < (1, 7, 14):
        return
    for i, e in enumerate(d.get('events') or []):
        if not isinstance(e, dict):
            continue
        c = e.get('calc')
        if not isinstance(c, dict):
            continue
        folds = c.get('fold')
        if not isinstance(folds, list) or not folds or c.get('foldDropped'):
            continue
        dm, tm = c.get('dealtMult'), c.get('takenMult')
        if not (_isnum(dm) and _isnum(tm)):
            continue
        pa = pv = 1.0
        for st in folds:
            if not isinstance(st, dict) or not _isnum(st.get('factor')):
                continue
            if st.get('side') == 'atk':
                pa *= float(st['factor'])
            elif st.get('side') == 'vic':
                pv *= float(st['factor'])
        tol = 6e-4 * (abs(pa) + abs(pv)) + 1e-3
        if abs(float(dm) - pa) > tol:
            problems.append('events[%d].calc.dealtMult(%.4f) != prod(atk folds)(%.4f) [since 1.7.14]'
                            % (i, dm, pa))
        if abs(float(tm) - pv) > tol:
            problems.append('events[%d].calc.takenMult(%.4f) != prod(vic folds)(%.4f) [since 1.7.14]'
                            % (i, tm, pv))

# Absolute floor for the SUM identities. The plugin may emit integer-rounded values, and the three
# numbers in "attributed + unattributed = total" are rounded independently, so the worst-case gap is
# 1.5 units; 2.0 accepts that while still catching a real accounting bug (the self-test pins both
# directions: a 1.0-unit rounding gap must pass, a 100-unit gap must fail).
CONTRIB_TOL_ABS = 2.0
CONTRIB_TOL_REL = 1e-6


def _close(a, b):
    if not isinstance(a, (int, float)) or not isinstance(b, (int, float)):
        return False
    return abs(a - b) <= max(CONTRIB_TOL_ABS, CONTRIB_TOL_REL * max(abs(a), abs(b)))


def _check_contribution(d, problems, cov):
    """Optional-section validation + the two value identities of the contribution model."""
    sec = d.get('contribution')
    if sec is None:
        cov['contribution'] = 'absent (pre-Phase-E export)'
        return
    if not isinstance(sec, dict):
        problems.append('contribution is not an object')
        return
    if 'error' in sec:
        problems.append('contribution reported an internal error (the export itself is intact): %s'
                        % str(sec.get('error'))[:200])
    for name, typ in CONTRIBUTION_REQUIRED:
        if name not in sec:
            problems.append('contribution.%s missing' % name)
        elif not isinstance(sec[name], typ):
            problems.append('contribution.%s type=%s want=%s'
                            % (name, type(sec[name]).__name__, _tname(typ)))
    cov['contribution.schemaVersion'] = sec.get('schemaVersion')
    cov['contribution.actors'] = len(sec.get('actors') or []) if isinstance(sec.get('actors'), list) else '?'
    cov['contribution.rules'] = len(sec.get('rules') or []) if isinstance(sec.get('rules'), list) else '?'
    cov['contribution.links'] = len(sec.get('links') or []) if isinstance(sec.get('links'), list) else '?'
    # value identity. Schema 1.1 defines totalDamage = analyzableDealt and keeps the real conservation
    # law, attributed + unattributed == analyzableDealt. The 1.0 form (attributed+unattributed==total)
    # only held because unattributed was 0 in every export that carried the section; a 1.1 file with a
    # non-zero unattributed pool MUST NOT be judged by it.
    # Key the gate on the SECTION's schemaVersion, not on the plugin version: the section is what carries
    # the contract, and a mismatch between the two must not be able to switch the checks off silently.
    # (A negative control caught exactly that: a 1.1 section inside a 1.7.6 file skipped every check.)
    _sec_ver = sec.get('schemaVersion')
    is_110 = (_ver_tuple(_sec_ver) >= CONTRIB_SCHEMA_110) if _sec_ver else \
        (_ver_tuple(d.get('version')) >= CONTRIB_PLUGIN_110)
    # R61 (1.7.13): schema 1.2 takes the same-team bucket out of analyzableDealt. Keyed on the SECTION
    # version with the plugin version as fallback, exactly like 1.1 above.
    is_120 = (_ver_tuple(_sec_ver) >= CONTRIB_SCHEMA_120) if _sec_ver else \
        (_ver_tuple(d.get('version')) >= CONTRIB_PLUGIN_120)
    tot, att, un = sec.get('totalDamage'), sec.get('attributedDamage'), sec.get('unattributedDamage')
    _cv0 = sec.get('coverage') if isinstance(sec.get('coverage'), dict) else {}
    if is_110:
        _az0 = _cv0.get('analyzableDealt')
        if _isnum(tot) and _isnum(_az0) and not _close(tot, _az0):
            problems.append('contribution.totalDamage(%.0f) != analyzableDealt(%.0f) [schema 1.1]'
                            % (tot, _az0))
        if _isnum(att) and _isnum(un) and _isnum(_az0) and not _close(att + un, _az0):
            problems.append('contribution identity broken [schema 1.1]: attributed(%.0f)+unattributed(%.0f) '
                            '!= analyzableDealt(%.0f)' % (att, un, _az0))
    elif _isnum(tot) and _isnum(att) and _isnum(un) and not _close(att + un, tot):
        problems.append('contribution identity broken: attributed(%.0f)+unattributed(%.0f) != total(%.0f)'
                        % (att, un, tot))
    cov['contribution.totalDamage'] = tot
    cov['contribution.attributedDamage'] = att
    cov['contribution.unattributedDamage'] = un
    # share identity
    cv = sec.get('coverage')
    if isinstance(cv, dict):
        for k, t in CONTRIBUTION_COVERAGE_KEYS:
            if k not in cv:
                problems.append('contribution.coverage.%s missing' % k)
            elif not isinstance(cv[k], t):
                problems.append('contribution.coverage.%s type=%s' % (k, type(cv[k]).__name__))
        cs, us = cv.get('creditedShare'), cv.get('unattributedShare')
        # P0-B: with an EMPTY analysed pool there is no share to add up to 1 -- the plugin writes 0/0
        # there, and "not applicable" must not be reported as a broken identity (schema 1.1).
        _az_share = cv.get('analyzableDealt')
        _share_applies = (not is_110) or (_isnum(_az_share) and _az_share > 0)
        if _share_applies and _isnum(cs) and _isnum(us) and abs(cs + us - 1.0) > 1e-6:
            problems.append('contribution share identity broken: credited+unattributed = %.9f' % (cs + us))
        cov['contribution.creditedShare'] = cs
        if is_110:
            _check_coverage_110(d, sec, cv, att, problems, cov, is_120)
    # per-entry shapes + per-actor value identity (first offender per list only: one bug, one line)
    actor_spec = list(CONTRIBUTION_ACTOR_KEYS)
    if is_120:
        # 1.2: the excluded same-team amount is part of the contract, not an optional extra.
        actor_spec += [('friendly', (int, float)), ('friendlyHits', int)]
    for key, spec in (('actors', actor_spec), ('rules', CONTRIBUTION_RULE_KEYS),
                      ('links', CONTRIBUTION_LINK_KEYS)):
        rows = sec.get(key)
        if not isinstance(rows, list) or not rows:
            continue
        bad = set()
        for row in rows:
            if not isinstance(row, dict):
                problems.append('contribution.%s[] entry is not an object' % key)
                break
            for k, t in spec:
                if k not in row:
                    if k not in bad:
                        bad.add(k)
                        problems.append('contribution.%s[] entry missing %s' % (key, k))
                elif not isinstance(row[k], t):
                    if k not in bad:
                        bad.add(k)
                        problems.append('contribution.%s[].%s type=%s' % (key, k, type(row[k]).__name__))
        if key == 'actors':
            for row in rows:
                if not isinstance(row, dict):
                    break
                b, s_, a, tc = (row.get('baseCredit'), row.get('selfRuleCredit'),
                                row.get('assistCredit'), row.get('totalCredit'))
                if all(isinstance(x, (int, float)) for x in (b, s_, a, tc)) and not _close(b + s_ + a, tc):
                    problems.append('contribution.actors[key=%s] identity broken: base+self+assist(%.0f) != total(%.0f)'
                                    % (row.get('key'), b + s_ + a, tc))
                    break
                # 1.7.12/schema 1.2: 直接打出 = 自身(基础+自身规则) + 被队友分走. R62 GATED IT ON is_120:
                # MEASURED 2026-10-06 over the live corpus, 49 of the 271 actor rows in 1.7.11 sections
                # violate it (up to 4%), while all 337 rows of the 1.2 sections satisfy it exactly. The
                # 1.1 form cannot hold in general: `directDamage` is what the actor dealt, and the part of
                # it taken by a TEAMMATE's rule is not on this row at all (it lands in that teammate's
                # `assistCredit`), so the identity only closed on the 17 sections it was measured on.
                # The identity that IS true for every version is checked just above: base+self+assist == total.
                dv, rv = row.get('directDamage'), row.get('receivedAssist')
                if is_120 and all(isinstance(x, (int, float)) for x in (b, s_, dv, rv)) and not _close(b + s_ + rv, dv):
                    problems.append('contribution.actors[key=%s] identity broken: base+self+received(%.0f) != direct(%.0f)'
                                    % (row.get('key'), b + s_ + rv, dv))
                    break
                # 1.7.12: the same-team split of the actor's own hits. The three fields are OPTIONAL on
                # disk (the sections written before 1.7.12 do not carry them), so a missing field is not a
                # problem -- but a present one must be well typed and must CLOSE, or the new 自伤 column
                # and the JSON reader would be trusted to a number nothing checks.
                if not is_120:
                    for k, ty in CONTRIBUTION_ACTOR_OPTIONAL:
                        if k in row and not isinstance(row[k], ty):
                            problems.append('contribution.actors[].%s type=%s want=%s'
                                            % (k, type(row[k]).__name__, _tname(ty)))
                            break
                    else:
                        fr, ho = row.get('friendly'), row.get('hostileDamage')
                        if all(isinstance(x, (int, float)) for x in (fr, ho, dv)) and not _close(fr + ho, dv):
                            problems.append('contribution.actors[key=%s] identity broken: friendly+hostile(%.0f) != direct(%.0f)'
                                            % (row.get('key'), fr + ho, dv))
                            break
                # R62: NOT an identity under 1.2. `hits` is the actor's IN-POOL (enemy-facing) hits and
                # `friendlyHits` the same-team hits the contract EXCLUDES, i.e. two DISJOINT buckets, so
                # more same-team hits than in-pool hits is normal. MEASURED 2026-10-06 over the live
                # 103-export corpus: 26 real rows violate the old rule (1.7.13 x17, 1.7.15 x9; e.g. 24
                # same-team hits against 10 in-pool hits), all of them in the training ground. Kept for
                # <= 1.1 sections, where `hits` counted EVERY hit -- which is what it was measured on.
                fh, hh = row.get('friendlyHits'), row.get('hits')
                if (not is_120 and isinstance(fh, int) and not isinstance(fh, bool) and isinstance(hh, int)
                        and not isinstance(hh, bool) and fh > hh):
                    problems.append('contribution.actors[key=%s] friendlyHits(%d) > hits(%d)'
                                    % (row.get('key'), fh, hh))
                    break
            # R62: the two AGGREGATE identities that ARE true for 1.2 -- the per-actor rows PARTITION the
            # ledger's analysed pool. MEASURED 2026-10-06 on all 38 real 1.2 sections of the live corpus:
            # 0 mismatches for both. `friendlyHits` is deliberately NOT summed against
            # ledger.selfTeamHits: the ledger also counts same-team hits whose attacker is outside the
            # analysed pool (measured 61 vs 88 on one battle), so that sum is not an identity.
            if is_120:
                _led = sec.get('damageLedger') or {}
                _sh = 0
                _sd = 0.0
                _sh_ok = True
                for row in rows:
                    if not isinstance(row, dict):
                        _sh_ok = False
                        break
                    h = row.get('hits')
                    if isinstance(h, int) and not isinstance(h, bool):
                        _sh += h
                    else:
                        _sh_ok = False
                    dv_ = row.get('directDamage')
                    if isinstance(dv_, (int, float)) and not isinstance(dv_, bool):
                        _sd += dv_
                    else:
                        _sh_ok = False
                if _sh_ok and isinstance(_led.get('analyzableHits'), int) and _sh != _led['analyzableHits']:
                    problems.append('contribution actor hits sum(%d) != damageLedger.analyzableHits(%d) [schema 1.2]'
                                    % (_sh, _led['analyzableHits']))
                if _sh_ok and _isnum(_led.get('analyzableDealt')) and abs(_sd - float(_led['analyzableDealt'])) > CONTRIB_TOL_ABS:
                    problems.append('contribution actor directDamage sum(%.0f) != damageLedger.analyzableDealt(%.0f) [schema 1.2]'
                                    % (_sd, _led['analyzableDealt']))



def _check_coverage_110(d, sec, cv, attributed, problems, cov, is_120=False):
    """Schema 1.1 (P0-B): the coverage set, the damage ledger and the three ratios they publish.

    The ledger is deliberately THREE separate numbers (review NEXT-STEPS 0.3): excludedDamage is the
    model's own exclusion (an unresolvable attacker), outsideTeamDealt is out of scope by design, and
    reconciliationGap is what no bucket explains. The checker therefore asserts the two identities that
    make the split meaningful -- and it asserts them separately, so merging the buckets into one figure
    cannot pass by accident.
    """
    for k, t in COVERAGE_KEYS_110:
        if k not in cv:
            problems.append('contribution.coverage.%s missing [schema 1.1]' % k)
        elif not isinstance(cv[k], t):
            problems.append('contribution.coverage.%s type=%s' % (k, type(cv[k]).__name__))
    az = cv.get('analyzableDealt')
    led = sec.get('damageLedger')
    if not isinstance(led, dict):
        problems.append('contribution.damageLedger missing [schema 1.1]')
        return
    for k, t in LEDGER_KEYS:
        if k not in led:
            problems.append('contribution.damageLedger.%s missing' % k)
        elif not isinstance(led[k], t):
            problems.append('contribution.damageLedger.%s type=%s' % (k, type(led[k]).__name__))
    if is_120:
        # R61: the same-team bucket is what keeps the exclusion visible; it is required in 1.2.
        for k, t in LEDGER_KEYS_120:
            if k not in led:
                problems.append('contribution.damageLedger.%s missing [schema 1.2]' % k)
            elif not isinstance(led[k], t):
                problems.append('contribution.damageLedger.%s type=%s' % (k, type(led[k]).__name__))
    td = led.get('totalsDealt')
    out_d = led.get('outsideTeamDealt')
    unk_d = led.get('unknownAttackerDealt')
    exc = cv.get('excludedDamage')
    # excludedDamage must BE the unresolvable-attacker pool, not a merged bucket
    if _isnum(exc) and _isnum(unk_d) and not _close(exc, unk_d):
        problems.append('contribution.coverage.excludedDamage(%.0f) != damageLedger.unknownAttackerDealt(%.0f):'
                        ' excludedDamage must not be merged with out-of-scope damage' % (exc, unk_d))
    # totals.dealt is the game's own sum over actors: it covers both sides, but not the unroutable pool.
    # This compares TWO independent accumulators (per-event amounts here, per-actor DamageDealt in the
    # export), so the tolerance scales with the battle: measured on 1.7.6 battle_411001_20261004_115417
    # the difference is EXACTLY 0 (190,889,625 + 94,717 == 190,984,342), and 0.001% still catches a
    # dropped or double-counted bucket (which moves ~0.05% of a 200M battle, i.e. ~95,000 units).
    st_d = led.get('selfTeamDealt') if is_120 else 0.0
    if _isnum(az) and _isnum(out_d) and _isnum(td) and (not is_120 or _isnum(st_d)):
        _ledger_tol = max(CONTRIB_TOL_ABS, 1e-5 * abs(td))
        _sum = az + out_d + (st_d if _isnum(st_d) else 0.0)
        if abs(_sum - td) > _ledger_tol:
            problems.append('contribution ledger identity broken: analyzable(%.0f)+outsideTeam(%.0f)%s != totalsDealt(%.0f)'
                            % (az, out_d, ('+selfTeam(%.0f)' % st_d) if is_120 else '', td))
        cov['contribution.ledgerDelta'] = _sum - td
    root_un = (d.get('totals') or {}).get('unattributedDamage')
    if _isnum(unk_d) and _isnum(root_un) and not _close(unk_d, root_un):
        problems.append('contribution ledger: unknownAttackerDealt(%.0f) != totals.unattributedDamage(%.0f)'
                        % (unk_d, root_un))
    ev, ah, oh, uh = led.get('events'), led.get('analyzableHits'), led.get('outsideTeamHits'), led.get('unknownAttackerHits')
    sh = led.get('selfTeamHits') if is_120 else 0
    _hit_ok = all(isinstance(x, int) and not isinstance(x, bool) for x in (ev, ah, oh, uh)) and \
        (not is_120 or (isinstance(sh, int) and not isinstance(sh, bool)))
    if _hit_ok and ev != ah + oh + uh + (sh if is_120 else 0):
        problems.append('contribution ledger hit identity broken: events(%d) != analyzable(%d)+outsideTeam(%d)+unknown(%d)%s'
                        % (ev, ah, oh, uh, ('+selfTeam(%d)' % sh) if is_120 else ''))
    # 1.7.9: tie the ledger's whole-event census to a KPI the GAME computes itself. Every other ledger
    # identity here compares numbers this plugin derived from the same event list, so a ledger that
    # quietly stopped covering part of the battle could keep them all self-consistent; totals.taken comes
    # from the game's own accounting and closes that hole. MEASURED 2026-10-04: totals.taken ==
    # totals.dealt + totals.unattributedDamage in 29/29 corpus exports, and damageLedger.eventSumAll ==
    # totals.taken in all 3 exports that carry a ledger. Both exact, so the tolerance is the same
    # battle-scaled one used above rather than a new knob.
    root_totals = d.get('totals') or {}
    tk = root_totals.get('taken')
    rd, ru = root_totals.get('dealt'), root_totals.get('unattributedDamage')
    if _isnum(tk) and _isnum(rd) and _isnum(ru) and not _close(tk, rd + ru):
        problems.append('totals identity broken: taken(%.0f) != dealt(%.0f)+unattributedDamage(%.0f)'
                        % (tk, rd, ru))
    esum = led.get('eventSumAll')
    if _isnum(esum) and _isnum(tk):
        _esum_tol = max(CONTRIB_TOL_ABS, 1e-5 * abs(tk))
        if abs(esum - tk) > _esum_tol:
            problems.append('contribution ledger: eventSumAll(%.0f) != totals.taken(%.0f): the ledger must '
                            'account for exactly the damage the game itself reports as taken' % (esum, tk))
        cov['contribution.ledgerVsGameTaken'] = esum - tk
    for key, num_, den_, want_desc in (
            ('analysisDamageCoverage', az, td, 'analyzableDealt/totalsDealt'),
            ('creditCoverageWithinAnalyzed', attributed, az, 'attributedDamage/analyzableDealt'),
            ('overallAttributedCoverage', attributed, td, 'attributedDamage/totalsDealt')):
        got = cv.get(key)
        if _isnum(got) and _isnum(num_) and _isnum(den_) and den_:
            want = float(num_) / float(den_)
            if abs(got - want) > max(1e-4, 1e-4 * abs(want)):
                problems.append('contribution.coverage.%s = %.6f but %s = %.6f' % (key, got, want_desc, want))
        elif got is None and _isnum(den_) and den_:
            problems.append('contribution.coverage.%s is null but its denominator is known' % key)
    cov['contribution.analysisDamageCoverage'] = cv.get('analysisDamageCoverage')
    cov['contribution.damageLedger.gap'] = led.get('reconciliationGap')


def _taken_bucket_sum(rows):
    """Sum of a dimension's bucket amounts, or None if a row is malformed (the type check already said so)."""
    total = 0
    for row in rows:
        if not isinstance(row, dict) or not _isnum(row.get('amount')):
            return None
        total += row['amount']
    return total


def _check_taken(d, problems, cov):
    """Optional-section validation + the partition identities of the R79 incoming damage split."""
    sec = d.get('takenBreakdown')
    if sec is None:
        cov['takenBreakdown'] = 'absent (pre-R79 export)'
        return
    if not isinstance(sec, dict):
        problems.append('takenBreakdown is not an object')
        return
    if 'error' in sec:
        problems.append('takenBreakdown reported an internal error (the export itself is intact): %s'
                        % str(sec.get('error'))[:200])
    for name, typ in TAKEN_REQUIRED:
        if name not in sec:
            problems.append('takenBreakdown.%s missing' % name)
        elif not isinstance(sec[name], typ):
            problems.append('takenBreakdown.%s type=%s want=%s'
                            % (name, type(sec[name]).__name__, _tname(typ)))
    # The contract travels IN the section, so a reader never has to know which round wrote the file.
    if sec.get('method') != 'by-event/1':
        problems.append('takenBreakdown.method=%r (want by-event/1)' % (sec.get('method'),))
    if sec.get('basis') != 'nominal':
        problems.append('takenBreakdown.basis=%r (want nominal: the split runs over the game-invoked '
                        'nominal, not over the published taken)' % (sec.get('basis'),))
    cov['takenBreakdown.schemaVersion'] = sec.get('schemaVersion')
    # R80 (1.7.30): the section stops folding a dimension's tail at 64 buckets, which changes what the
    # rows MEAN -- before, a dimension could end in a 其余 row and a reader had to treat the list as
    # partial; after, every bucket is present. Keyed on the section's own version (the contract travels
    # with the data), with the plugin version as the fallback for a section that omits the claim.
    _tsec_ver = sec.get('schemaVersion')
    if isinstance(_tsec_ver, str) and _tsec_ver not in TAKEN_SCHEMAS:
        problems.append('takenBreakdown.schemaVersion=%r is not one of %s' % (_tsec_ver, list(TAKEN_SCHEMAS)))
    else:
        _t110 = (_ver_tuple(_tsec_ver) >= TAKEN_SCHEMA_110) if _tsec_ver else \
            (_ver_tuple(d.get('version', '0')) >= TAKEN_PLUGIN_110)
        if not _t110 and _ver_tuple(d.get('version', '0')) >= TAKEN_PLUGIN_110:
            problems.append('takenBreakdown.schemaVersion=%r but a %s export must declare 1.1: an R80 '
                            'reader must not assume a dimension is complete'
                            % (_tsec_ver, d.get('version')))
    cov['takenBreakdown.hits'] = sec.get('hits')
    cov['takenBreakdown.nominal'] = sec.get('nominal')

    nominal, taken, residual = sec.get('nominal'), sec.get('taken'), sec.get('residual')
    if _isnum(nominal) and _isnum(taken) and _isnum(residual) and not _close(nominal, taken + residual):
        problems.append('takenBreakdown identity broken: taken(%.0f)+residual(%.0f) != nominal(%.0f)'
                        % (taken, residual, nominal))

    actors = sec.get('actors')
    if not isinstance(actors, list):
        actors = []
    cov['takenBreakdown.actors'] = len(actors)
    sums = {'nominal': 0.0, 'taken': 0.0, 'residual': 0.0, 'friendly': 0.0, 'unknown': 0.0, 'hits': 0}
    for i, a in enumerate(actors):
        if not isinstance(a, dict):
            problems.append('takenBreakdown.actors[%d] is not an object' % i)
            continue
        for k, t in TAKEN_ACTOR_KEYS:
            if k not in a:
                problems.append('takenBreakdown.actors[%d] missing %s' % (i, k))
            elif not isinstance(a[k], t):
                problems.append('takenBreakdown.actors[%d].%s type=%s want=%s'
                                % (i, k, type(a[k]).__name__, _tname(t)))
        if a.get('position') not in TAKEN_POSITIONS:
            problems.append('takenBreakdown.actors[%d].position=%r is not one of %s'
                            % (i, a.get('position'), list(TAKEN_POSITIONS)))
        for k in ('nominal', 'taken', 'residual', 'friendly', 'unknown'):
            if _isnum(a.get(k)):
                sums[k] += a[k]
        if isinstance(a.get('hits'), int):
            sums['hits'] += a['hits']
        if isinstance(a.get('friendlyHits'), int) and isinstance(a.get('hits'), int) \
                and a['friendlyHits'] > a['hits']:
            problems.append('takenBreakdown.actors[%d] friendlyHits(%d) > hits(%d)'
                            % (i, a['friendlyHits'], a['hits']))
        if isinstance(a.get('unknownHits'), int) and isinstance(a.get('hits'), int) \
                and a['unknownHits'] > a['hits']:
            problems.append('takenBreakdown.actors[%d] unknownHits(%d) > hits(%d)'
                            % (i, a['unknownHits'], a['hits']))
        if _isnum(a.get('nominal')) and _isnum(a.get('taken')) and _isnum(a.get('residual')) \
                and not _close(a['nominal'], a['taken'] + a['residual']):
            problems.append('takenBreakdown.actors[%d] identity broken: taken(%.0f)+residual(%.0f) '
                            '!= nominal(%.0f)' % (i, a['taken'], a['residual'], a['nominal']))
        if _isnum(a.get('nominal')):
            for dim in ('bySource', 'byHitType', 'byEffect'):
                rows = a.get(dim)
                if not isinstance(rows, list):
                    continue
                sub = _taken_bucket_sum(rows)
                if sub is not None and not _close(a['nominal'], sub):
                    problems.append('takenBreakdown.actors[%d].%s sums to %.0f but the victim nominal is '
                                    '%.0f: the partition must cover the whole victim total'
                                    % (i, dim, sub, a['nominal']))
            rows = a.get('byAttacker')
            if isinstance(rows, list):
                sub = _taken_bucket_sum(rows)
                if sub is not None and _isnum(a.get('friendly')) and _isnum(a.get('unknown')) \
                        and not _close(a['nominal'], sub + a['friendly'] + a['unknown']):
                    problems.append('takenBreakdown.actors[%d]: byAttacker(%.0f)+friendly(%.0f)+unknown(%.0f)'
                                    ' != nominal(%.0f)'
                                    % (i, sub, a['friendly'], a['unknown'], a['nominal']))
        for dim, keys in (('bySource', TAKEN_BUCKET_KEYS), ('byHitType', TAKEN_BUCKET_KEYS),
                          ('byAttacker', TAKEN_BUCKET_KEYS), ('byEffect', TAKEN_BUCKET_KEYS),
                          ('byStatus', TAKEN_STATUS_KEYS)):
            rows = a.get(dim)
            if not isinstance(rows, list):
                continue
            for j, row in enumerate(rows):
                if not isinstance(row, dict):
                    problems.append('takenBreakdown.actors[%d].%s[%d] is not an object' % (i, dim, j))
                    continue
                for k, t in keys:
                    if k not in row:
                        problems.append('takenBreakdown.actors[%d].%s[%d] missing %s' % (i, dim, j, k))
                    elif not isinstance(row[k], t):
                        problems.append('takenBreakdown.actors[%d].%s[%d].%s type=%s'
                                        % (i, dim, j, k, type(row[k]).__name__))
                # The quality marker is a contract, not decoration: "" means every hit in this bucket was
                # resolved from the objects themselves; anything else is a best-effort fallback count.
                if dim != 'byStatus' and isinstance(row.get('quality'), str) and row['quality'] \
                        and not row['quality'].startswith('近似 '):
                    problems.append('takenBreakdown.actors[%d].%s[%d].quality=%r (want "" or 近似 a/h)'
                                    % (i, dim, j, row['quality']))
    for k in ('nominal', 'taken', 'residual', 'friendly', 'unknown'):
        if _isnum(sec.get(k)) and not _close(sec[k], sums[k]):
            problems.append('takenBreakdown.%s(%.0f) != sum over actors(%.0f)' % (k, sec[k], sums[k]))
    if isinstance(sec.get('hits'), int) and sec['hits'] != sums['hits']:
        problems.append('takenBreakdown.hits(%d) != sum over actors(%d)' % (sec['hits'], sums['hits']))
    if isinstance(sec.get('friendlyHits'), int) and isinstance(sec.get('unknownHits'), int):
        cov['takenBreakdown.friendlyHits'] = sec['friendlyHits']
        cov['takenBreakdown.unknownHits'] = sec['unknownHits']


def _ver_tuple(v):
    try:
        parts = str(v).split('.')
        while len(parts) < 3:
            parts.append('0')
        return tuple(int(''.join(ch for ch in p if ch.isdigit()) or 0) for p in parts[:3])
    except Exception:
        return (0, 0, 0)


def _tname(t):
    if isinstance(t, tuple):
        return '|'.join(x.__name__ for x in t)
    return t.__name__


def _check_keys(where, obj, spec, ver, problems, missing_required):
    for name, typ, since in spec:
        if _ver_tuple(since) > ver:
            continue                      # not guaranteed for this version
        if name not in obj:
            missing_required.append('%s.%s (since %s)' % (where, name, since))
            continue
        if not isinstance(obj[name], typ):
            problems.append('%s.%s type=%s want=%s' % (where, name, type(obj[name]).__name__, _tname(typ)))


def check_export(d, name='<mem>'):
    """Returns (problems, missing_required, coverage). All lists/dicts of plain Python types."""
    problems, missing, cov = [], [], {}
    if not isinstance(d, dict):
        return ['root is not an object'], ['root'], cov

    ver = _ver_tuple(d.get('version', '0'))
    cov['version'] = d.get('version', '?')
    _check_keys('root', d, ROOT_REQUIRED, ver, problems, missing)

    if 'config' in d and isinstance(d['config'], dict):
        _check_keys('config', d['config'], CONFIG_KEYS, ver, problems, missing)
    if 'reconcile' in d and isinstance(d['reconcile'], dict):
        _check_keys('reconcile', d['reconcile'], RECON_REQUIRED, ver, problems, missing)
    if ver >= (1, 5, 0):
        for key, spec in (('hitDetail', HITDETAIL_KEYS), ('timeline', TIMELINE_KEYS), ('facts', FACTS_KEYS)):
            if key in d and isinstance(d[key], dict):
                for k, t in spec:
                    if k not in d[key]:
                        missing.append('%s.%s' % (key, k))
                    elif not isinstance(d[key][k], t):
                        problems.append('%s.%s type=%s want=%s' % (key, k, type(d[key][k]).__name__, _tname(t)))
        # R62 (1.7.14): version-gated, so the whole pre-R62 archive keeps validating unchanged.
        if ver >= (1, 7, 14) and 'hitDetail' in d and isinstance(d['hitDetail'], dict):
            for k, t in HITDETAIL_KEYS_1714:
                if k not in d['hitDetail']:
                    missing.append('hitDetail.%s (since 1.7.14)' % k)
                elif not isinstance(d['hitDetail'][k], t):
                    problems.append('hitDetail.%s type=%s want=%s'
                                    % (k, type(d['hitDetail'][k]).__name__, _tname(t)))

    # 1.7.2 (阶段 G) paramOwners: validated whenever it is present, not only when the version requires
    # it, so an export that carries the section is checked even if its version stamp is older.
    if ver >= (1, 7, 4) and 'atkAdd' in d and isinstance(d['atkAdd'], dict):
        for k, ty in ATKADD_KEYS:
            if k not in d['atkAdd']:
                missing.append('atkAdd.%s' % k)
            elif not isinstance(d['atkAdd'][k], ty):
                problems.append('atkAdd.%s type=%s want=%s'
                                % (k, type(d['atkAdd'][k]).__name__, _tname(ty)))
        # 1.7.8 (P1-A): the four self-attribution counters, plus the identity they exist to carry. The
        # identity is what makes them usable: selfByKey + selfByNameFallback must PARTITION selfValues,
        # otherwise a value counted as self through neither path would be invisible.
        if ver >= (1, 7, 8):
            for k, ty in ATKADD_KEYS_178:
                if k not in d['atkAdd']:
                    missing.append('atkAdd.%s (since 1.7.8)' % k)
                elif not isinstance(d['atkAdd'][k], ty):
                    problems.append('atkAdd.%s type=%s want=%s'
                                    % (k, type(d['atkAdd'][k]).__name__, _tname(ty)))
            sv, sk, sn = (d['atkAdd'].get('selfValues'), d['atkAdd'].get('selfByKey'),
                          d['atkAdd'].get('selfByNameFallback'))
            if all(isinstance(x, int) and not isinstance(x, bool) for x in (sv, sk, sn)) \
                    and sv != sk + sn:
                problems.append('atkAdd self identity broken: selfValues(%d) != selfByKey(%d) + '
                                'selfByNameFallback(%d)' % (sv, sk, sn))
    if 'paramOwners' in d and isinstance(d['paramOwners'], dict):
        # 2-tuple spec on purpose: the section is validated whenever present, so an export that
        # carries it is checked even when its version stamp predates the requirement.
        for k, ty in PARAMOWNERS_KEYS:
            if k not in d['paramOwners']:
                missing.append('paramOwners.%s' % k)
            elif not isinstance(d['paramOwners'][k], ty):
                problems.append('paramOwners.%s type=%s want=%s'
                                % (k, type(d['paramOwners'][k]).__name__, _tname(ty)))
        for k, ty in PARAMOWNERS_OPTIONAL:
            if k in d['paramOwners'] and not isinstance(d['paramOwners'][k], ty):
                problems.append('paramOwners.%s type=%s want=%s'
                                % (k, type(d['paramOwners'][k]).__name__, _tname(ty)))
        rows = d['paramOwners'].get('rows')
        if isinstance(rows, list):
            for i, row in enumerate(rows):
                if i >= 8:                       # bounded: the shape is uniform, 8 rows prove it
                    break
                if not isinstance(row, dict):
                    problems.append('paramOwners.rows[%d] is not an object' % i)
                    continue
                for k, t in PARAMOWNERS_ROW_KEYS:
                    if k not in row:
                        missing.append('paramOwners.rows[%d].%s' % (i, k))
                    elif not isinstance(row[k], t):
                        problems.append('paramOwners.rows[%d].%s type=%s want=%s'
                                        % (i, k, type(row[k]).__name__, _tname(t)))
                for k in ('owner', 'keyOwner'):
                    if k not in row:
                        missing.append('paramOwners.rows[%d].%s' % (i, k))
                    elif row[k] is not None and not isinstance(row[k], str):
                        problems.append('paramOwners.rows[%d].%s type=%s want=str|None'
                                        % (i, k, type(row[k]).__name__))

    ev = d.get('events') or []
    cov['events'] = len(ev)
    if ev:
        # key coverage over the whole event array (a key present in zero events is a dead channel)
        cnt = {}
        for e in ev:
            for k in e:
                cnt[k] = cnt.get(k, 0) + 1
        for k in ('factId', 'critObserved', 'hitMatch', 'atkKey', 'vicKey', 'calc'):
            cov['ev.' + k] = cnt.get(k, 0)
        # type check on the first event that has each optional key
        seen = set()
        for e in ev:
            for k, t in EVENT_OPTIONAL:
                if k in e and k not in seen:
                    seen.add(k)
                    if not isinstance(e[k], t):
                        problems.append('event.%s type=%s want=%s' % (k, type(e[k]).__name__, _tname(t)))
        # calc object: key COVERAGE over every calc-bearing event (a key present in only a few hits is a
        # different fact from one present in all of them), plus type checks. Only the FIRST violation per
        # key is reported -- a wrong type in 5,485 events is one bug, not 5,485 lines.
        calc_seen = {}
        calc_bad = set()
        shape_done = set()
        for e in ev:
            c = e.get('calc')
            if not isinstance(c, dict):
                continue
            for k in c:
                calc_seen[k] = calc_seen.get(k, 0) + 1
            for k, t in CALC_OPTIONAL:
                if k in c and k not in calc_bad and not isinstance(c[k], t):
                    calc_bad.add(k)
                    problems.append('calc.%s type=%s want=%s (first hit t=%s)'
                                    % (k, type(c[k]).__name__, _tname(t), e.get('t')))
            # entry SHAPE of the 1.5.0 provenance lists: pinned on the first non-empty one
            for listKey, spec in (('fold', FOLD_STEP_KEYS), ('cancel', CANCEL_STEP_KEYS), ('atkAdd', FOLD_STEP_KEYS)):
                if listKey in shape_done:
                    continue
                lst = c.get(listKey)
                if not isinstance(lst, list) or not lst:
                    continue
                shape_done.add(listKey)
                for st in lst:
                    for k, t in spec:
                        if k not in st:
                            problems.append('calc.%s[] entry missing %s' % (listKey, k))
                        elif not isinstance(st[k], t):
                            problems.append('calc.%s[].%s type=%s' % (listKey, k, type(st[k]).__name__))
                    # 1.7.4: unlike fold/cancel, byUnit is NOT optional on an atkAdd entry -- the
                    # whole point of the channel is WHO gets the credit, and a missing giver would silently
                    # route the share back to the attacker (the bug this channel exists to fix).
                    if listKey == 'atkAdd' and 'byUnit' not in st:
                        problems.append('calc.atkAdd[] entry missing byUnit (no giver)')
            # 1.5.4: byUnit is conditional (only given/madness folds carry it, and only when the
            # giver/applier resolved); type-check it wherever present, on EVERY list (the shape pin
            # above deliberately samples only the first non-empty one).
            for lk2 in ('fold', 'cancel'):
                lst2 = c.get(lk2)
                if isinstance(lst2, list):
                    for st2 in lst2:
                        if isinstance(st2, dict) and 'byUnit' in st2 and not isinstance(st2['byUnit'], str):
                            problems.append('calc.%s[].byUnit type=%s' % (lk2, type(st2['byUnit']).__name__))
        for k in sorted(calc_seen):
            cov['calc.' + k] = calc_seen[k]

    # facts items (1.5.0)
    facts = d.get('facts') or {}
    items = facts.get('items') if isinstance(facts, dict) else None
    cov['facts.classes'] = len(items or [])
    if items:
        spec = list(FACT_ITEM_KEYS)
        if ver >= (1, 5, 2):
            spec += FACT_ITEM_KEYS_152
        for k, t in spec:
            if k not in items[0]:
                problems.append('facts.items[0] missing %s' % k)
            elif not isinstance(items[0][k], t):
                problems.append('facts.items[0].%s type=%s' % (k, type(items[0][k]).__name__))
        live = sum(1 for it in items if it.get('state'))
        cov['facts.liveClasses'] = live
        cov['facts.hitsSum'] = sum(it.get('hits', 0) for it in items)
    fc = d.get('factCoverage') or {}
    if isinstance(fc, dict):
        cov['factCoverage.dmg'] = fc.get('dmg')
        cov['factCoverage.withFact'] = fc.get('withFact')
        cov['factCoverage.withLiveState'] = fc.get('withLiveState')

    tm = d.get('timeline') or {}
    if isinstance(tm, dict):
        cov['timeline.rowCount'] = tm.get('rowCount')
        cov['timeline.resistChanges'] = tm.get('resistChanges')
        cov['timeline.statusChanges'] = tm.get('statusChanges')

    hd = d.get('hitDetail') or {}
    if isinstance(hd, dict):
        for k in ('produced', 'matchExact', 'matchPair', 'matchNone', 'matchRejected'):
            cov['hitDetail.' + k] = hd.get(k)

    # R62 (C): the fold sides vs the two summary doubles, for exports that claim 1.7.14 or later.
    _check_fold_sides(d, ver, problems)

    # Phase E: optional contribution section (validated whenever present)
    _check_contribution(d, problems, cov)

    # R79: optional incoming-side section (validated whenever present, like contribution)
    _check_taken(d, problems, cov)


    return problems, missing, cov


def report(paths):
    out = []
    total_bad = 0
    for p in paths:
        try:
            d = json.load(io.open(p, encoding='utf-8'))
        except Exception as ex:
            out.append('FILE %s UNREADABLE: %s' % (os.path.basename(p), ex))
            total_bad += 1
            continue
        probs, missing, cov = check_export(d, os.path.basename(p))
        out.append('=' * 100)
        out.append('FILE %s   version=%s' % (os.path.basename(p), cov.get('version')))
        out.append('  missing required : %s' % ('none' if not missing else '; '.join(missing)))
        out.append('  type problems    : %s' % ('none' if not probs else '; '.join(probs)))
        out.append('  coverage         :')
        for k in sorted(cov):
            out.append('      %-28s %s' % (k, cov[k]))
        bad = len(missing) + len(probs)
        total_bad += bad
        out.append('  VERDICT          : %s' % ('OK' if bad == 0 else '%d PROBLEM(S)' % bad))
    out.append('=' * 100)
    out.append('TOTAL PROBLEMS: %d' % total_bad)
    io.open(REPORT, 'w', encoding='utf-8').write('\n'.join(out) + '\n')
    return total_bad


# ---------------------------------------------------------------------------------------------
# self-test: a checker that has never been shown to REJECT anything is not a checker. This builds a
# minimal 1.5.0-shaped export, then breaks it in four specific ways and requires each to be caught.
# ---------------------------------------------------------------------------------------------

def _fixture(version='1.5.2'):
    ev = {
        't': 1.0, 'type': 'dmg', 'victim': 'v', 'attacker': 'a', 'amount': 100, 'nominal': 100,
        'source': 1, 'crit': False, 'atkTeam': 1, 'vicTeam': 2,
        'atkKey': 1, 'vicKey': 2, 'factId': 1, 'critObserved': 1, 'hitMatch': 1, 'hitValue': 100,
        'calc': {
            'effectId': 1, 'hitType': 1, 'theory': 100, 'applied': 100, 'residual': 1.0,
            'pair': 'live-same', 'pairTrusted': True, 'valueMatches': True,
            'fold': [{'side': 'atk', 'kind': 'text', 'origin': 'text#1/2/c1', 'factor': 1.15, 'label': 'x'}],
            'cancel': [{'origin': 'given#0/1006/-15', 'value': 1.15, 'by': 'global#1/0', 'label': 'y'}],
            'responsibility': 1, 'maxAbsorbed': 5,
        },
    }
    d = {
        'app': 'DpsMeter', 'version': version, 'quest': 1, 'duration': 1.0, 'result': 'Win',
        'totals': {}, 'actors': [], 'events': [ev],
        'statusAudit': {}, 'rosterAudit': {}, 'unattributedByVictim': {},
        'reconcile': {
            'dmgEvents': 1, 'withCalc': 1, 'exact': 1, 'exactWithCrit': 1, 'critInferred': 0,
            'critObserved': 1, 'critObservedYes': 0, 'critAgree': 0, 'critInferredButDenied': 0,
            'critObservedButUnexplained': 0,
            'approx': 0, 'unexplained': 0, 'theoryExceeds': 0, 'absorbed': 0, 'distinctResiduals': 1,
            'approxTolerance': 0.05, 'critInferredTolerance': 0.002, 'byPair': {},
            'byTenthExact': [0] * 10, 'byTenthTotal': [1] + [0] * 9, 'topResidual': [],
        },
        'run': {}, 'forensics': {},
        'hitDetail': {'produced': 1, 'trimmed': 0, 'errors': 0, 'matchExact': 1, 'matchPair': 0, 'matchNone': 0},
        'timeline': {'observed': 1, 'units': 1, 'rowCount': 0, 'rowsDropped': 0, 'statusChanges': 0,
                     'resistChanges': 0, 'notCharacter': 0, 'nullStatus': 0, 'nullResistance': 0,
                     'errors': 0, 'resistSlots': ['毒'], 'statusFlags': ['毒'], 'rows': []},
        'facts': {'observed': 1, 'classes': 1, 'maxClasses': 2400, 'liveClasses': 1, 'maxLiveClasses': 900,
                  'classesOverflow': 0, 'liveSkipped': 0, 'stateUnreadable': 0, 'errors': 0,
                  'items': [{'id': 1, 'hits': 1, 'atk': 1, 'vic': 2, 'eff': 1, 'ht': 1,
                             'pair': 'live-same', 'known': 1.0, 'resid': 1.0, 'state': 's',
                             'atkName': 'a', 'vicName': 'v', 'atkTeam': 1, 'vicTeam': 2}]},
        'factCoverage': {'dmg': 1, 'withFact': 1, 'withLiveState': 1},
        'giveTypeList': [], 'resistMaster': {}, 'statusValues': [], 'layout': {},
        'liveRange': {}, 'subParams': {}, 'params': {}, 'statsRows': {},
    }
    return d


def _contrib_fixture():
    """A minimal but faithful contribution section (shape + both value identities)."""
    return {
        'schemaVersion': '0.1-draft', 'method': 'log-share/1', 'damageBasis': 'dealt',
        'totalDamage': 3000, 'attributedDamage': 3000, 'unattributedDamage': 0,
        'coverage': {'creditedShare': 1.0, 'unattributedShare': 0.0, 'analyzableDealt': 3000,
                     'hits': 1, 'byUnitGiven': [1, 1], 'byUnitMadness': [0, 0]},
        'actors': [{'key': 1, 'name': 'Alpha', 'directDamage': 3000, 'baseCredit': 2000,
                    'selfRuleCredit': 0, 'assistCredit': 0, 'receivedAssist': 1000, 'totalCredit': 2000},
                   {'key': 2, 'name': 'Beta', 'directDamage': 0, 'baseCredit': 0,
                    'selfRuleCredit': 0, 'assistCredit': 1000, 'receivedAssist': 0, 'totalCredit': 1000}],
        'rules': [{'ruleName': 'x', 'kind': 'given', 'side': 'vic', 'hits': 1, 'damageEquivalent': 1000}],
        'links': [{'fromKey': 2, 'toKey': 1, 'amount': 1000, 'hits': 1}],
    }


def _contrib_fixture_178():
    """The P0-B ACCEPTANCE SAMPLE, verbatim from CONTRIBUTION-REVIEW-NEXT-STEPS.md Agent P0-B:

        totals.dealt=1000, analyzableDealt=600, unattributedCredit=100, excludedDamage=400,
        attributedDamage=500  ->  totalDamage=600, analysisDamageCoverage=60%,
        creditCoverageWithinAnalyzed=83.333...%, overallAttributedCoverage=50%.

    The ledger is filled so that the two identities hold exactly: analyzable+outsideTeam == totalsDealt
    and unknownAttackerDealt == totals.unattributedDamage. If either identity were merged or dropped,
    the matching REJECTS case below fails.
    """
    return {
        'schemaVersion': '1.1', 'producer': 'plugin', 'method': 'log-share/1', 'damageBasis': 'dealt',
        'totalDamage': 600, 'attributedDamage': 500, 'unattributedDamage': 100,
        'coverage': {
            'creditedShare': 500.0 / 600.0, 'unattributedShare': 100.0 / 600.0,
            'analyzableDealt': 600, 'excludedDamage': 400,
            'analysisDamageCoverage': 0.6, 'creditCoverageWithinAnalyzed': 500.0 / 600.0,
            'overallAttributedCoverage': 0.5, 'hits': 5,
        },
        'damageLedger': {
            'totalsDealt': 1000, 'events': 10, 'analyzableHits': 5, 'outsideTeamHits': 3,
            'outsideTeamDealt': 400, 'unknownAttackerHits': 2, 'unknownAttackerDealt': 400,
            'eventSumAll': 1400, 'reconciliationGap': 0,
        },
        'actors': [{'key': 1, 'name': 'Alpha', 'directDamage': 600, 'baseCredit': 500,
                    'selfRuleCredit': 0, 'assistCredit': 0, 'receivedAssist': 100, 'totalCredit': 500}],
        'rules': [{'ruleName': 'x', 'kind': 'given', 'side': 'vic', 'hits': 1, 'damageEquivalent': 500}],
        'links': [{'fromKey': 1, 'toKey': 1, 'amount': 500, 'hits': 1}],
    }


def _contrib_fixture_120():
    """R61: the same acceptance sample one contract step later. Same-team damage (the enemy 回復反転
    channel, which schema 1.1 still counted inside analyzableDealt) is now OUT of the pool and carried by
    the ledger's third bucket:
        totalsDealt 1000 = analyzable 400 + outsideTeam 400 + selfTeam 200
        events 10 = analyzableHits 4 + outsideTeamHits 3 + unknownAttackerHits 2 + selfTeamHits 1
        eventSumAll 1400 = 400 + 400 + 200 + 400
        attributed 300 + unattributed 100 == analyzable 400
    """
    return {
        'schemaVersion': '1.2', 'producer': 'plugin', 'method': 'log-share/1', 'damageBasis': 'dealt',
        'totalDamage': 400, 'attributedDamage': 300, 'unattributedDamage': 100,
        'coverage': {
            'creditedShare': 0.75, 'unattributedShare': 0.25, 'analyzableDealt': 400,
            'excludedDamage': 400, 'analysisDamageCoverage': 0.4,
            'creditCoverageWithinAnalyzed': 0.75, 'overallAttributedCoverage': 0.3, 'hits': 4,
        },
        'damageLedger': {
            'totalsDealt': 1000, 'events': 10, 'analyzableHits': 4, 'outsideTeamHits': 3,
            'outsideTeamDealt': 400, 'unknownAttackerHits': 2, 'unknownAttackerDealt': 400,
            'selfTeamHits': 1, 'selfTeamDealt': 200, 'eventSumAll': 1400, 'reconciliationGap': 0,
        },
        # R62: `hits` must PARTITION the ledger (sum(actor.hits) == analyzableHits == 4) -- the fixture
        # claimed 5 while the ledger it ships said 4, which the new aggregate identity caught.
        'actors': [{'key': 1, 'name': 'Alpha', 'directDamage': 400, 'baseCredit': 300,
                    'selfRuleCredit': 0, 'assistCredit': 0, 'receivedAssist': 100, 'totalCredit': 300,
                    'friendly': 200, 'friendlyHits': 1, 'hits': 4}],
        'rules': [{'ruleName': 'x', 'kind': 'given', 'side': 'vic', 'hits': 1, 'damageEquivalent': 300}],
        'links': [{'fromKey': 1, 'toKey': 1, 'amount': 300, 'hits': 1}],
    }


def _fixture_113():
    """A 1.7.13 export: every version-gated block, including the 1.2 contract and the config echo."""
    d = _fixture_178()
    d['version'] = '1.7.13'
    d['config'] = {'filterFriendlyFire': False}
    d['contribution'] = _contrib_fixture_120()
    return d


def _fixture_114():
    """A 1.7.14 export: 1.7.13 plus the damage-detail channel's REJECTED-figure counter (R62 A)."""
    d = _fixture_113()
    d['version'] = '1.7.14'
    d['hitDetail']['matchRejected'] = 0
    return d


def _taken_base():
    """A 1.7.30 export: everything the round gates require (paramOwners 1.7.2, atkAdd 1.7.4, config
    1.7.12, hitDetail.matchRejected 1.7.14) so a case can only fail on the thing it is about."""
    d = _fixture_114()
    d['version'] = '1.7.30'
    return d


def _taken_fixture(version='1.1'):
    """A faithful R80 takenBreakdown section: two victims and every partition identity satisfied.

    Victim 1 (ally, position 1, key 1) took 300 nominal in two hits -- 200 from a resolvable attacker
    and 100 of same-team damage -- of which 240 landed. Victim 2 (enemy, position unreadable, key 2)
    has no resolvable attacker at all, which is the case that must NOT be folded into a unit bucket.
    Section totals: hits 3, nominal 350, taken 240, residual 110, friendly 100, unknown 50.

    `version` defaults to 1.1 (R80): no dimension is capped, so the bucket lists are complete.
    """
    return {
        'schemaVersion': version, 'method': 'by-event/1', 'basis': 'nominal',
        'hits': 3, 'nominal': 350, 'taken': 240, 'residual': 110,
        'friendly': 100, 'friendlyHits': 1, 'unknown': 50, 'unknownHits': 1,
        'actors': [
            {'key': 1, 'name': 'Alpha', 'team': 1, 'ally': True, 'position': 1, 'hits': 2,
             'nominal': 300, 'taken': 240, 'residual': 60, 'friendly': 100, 'friendlyHits': 1,
             'unknown': 0, 'unknownHits': 0,
             'bySource': [{'key': 2, 'name': 'DOT', 'amount': 100, 'hits': 1, 'quality': ''},
                          {'key': 1, 'name': 'DirectAttack', 'amount': 200, 'hits': 1, 'quality': ''}],
             'byHitType': [{'key': 1, 'name': 'Physical', 'amount': 300, 'hits': 2,
                            'quality': '近似 1/2'}],
             'byAttacker': [{'key': 9, 'name': 'Beta', 'amount': 200, 'hits': 1, 'quality': ''}],
             'byEffect': [{'key': 77, 'name': 'effect77', 'amount': 300, 'hits': 2, 'quality': ''}],
             'byStatus': [{'status': 'poison', 'applier': 'Gamma', 'amount': 100, 'hits': 1}]},
            {'key': 2, 'name': 'Bravo', 'team': 2, 'ally': False, 'position': 0, 'hits': 1,
             'nominal': 50, 'taken': 0, 'residual': 50, 'friendly': 0, 'friendlyHits': 0,
             'unknown': 50, 'unknownHits': 1,
             'bySource': [{'key': 11, 'name': 'Drain', 'amount': 50, 'hits': 1, 'quality': ''}],
             'byHitType': [{'key': -1, 'name': 'unidentified', 'amount': 50, 'hits': 1, 'quality': ''}],
             'byAttacker': [],
             'byEffect': [{'key': 0, 'name': 'unidentified', 'amount': 50, 'hits': 1, 'quality': ''}],
             'byStatus': []}],
    }


def _fixture_178():
    """A 1.7.8 export carries every version-gated block (paramOwners 1.7.2, atkAdd 1.7.4, ledger 1.7.8)."""
    d = _fixture('1.7.8')
    d['paramOwners'] = _paramowners_fixture()
    d['atkAdd'] = _atkadd_fixture()
    d['totals']['dealt'] = 1000
    d['totals']['unattributedDamage'] = 400
    # 1.7.9: the game's own taken accumulator, consistent with dealt+unattributed and with the fixture
    # ledger's eventSumAll (1400). The checker ties all three together.
    d['totals']['taken'] = 1400
    d['contribution'] = _contrib_fixture_178()
    return d


def _fixture_174():
    """A 1.7.4 fixture must satisfy BOTH version-gated requirements: paramOwners (1.7.2) and, unless the
    case deliberately omits it, atkAdd (1.7.4)."""
    d = _fixture('1.7.4')
    d['paramOwners'] = _paramowners_fixture()
    return d


def _atkadd_fixture():
    """The attribution counters: nine refusal counters, the emitted total, and (1.7.8/P1-A) the four
    self-attribution counters with their partition identity selfValues == selfByKey + selfByNameFallback.
    The extra keys are harmless for the 1.7.4 cases, which only require the older set."""
    return {'hits': 5475, 'emitted': 5391, 'selfValues': 7, 'skippedGuard': 131,
            'skippedCollision': 807, 'skippedUnowned': 0, 'skippedOwnerNull': 0,
            'skippedNegative': 0, 'skippedType': 0,
            'selfByKey': 5, 'selfByNameFallback': 2, 'nameCollision': 1, 'ownerUnknown': 2}


def _paramowners_fixture():
    """A minimal 阶段 G section: one applied 攻击力 entry whose giver IS named, plus the second owner
    side left null, because recording both sides is the point of the channel."""
    return {
        'reads': 24, 'errors': 0, 'firstError': '', 'units': 12,
        'entriesSeen': 96, 'entryReads': 96, 'entriesDropped': 0, 'budgetCapped': 0,
        'ownerNull': 0, 'ownerSelf': 40, 'ownerOther': 56, 'keyOwnerNull': 96,
        'ownerErrors': 0, 'crossChecked': 56, 'mismatch': 0, 'distinctOwners': 8,
        'maxEntriesPerRead': 64, 'maxUnionRows': 1024, 'maxEntryReadsPerBattle': 20000,
        'unionOverflow': 0,
        'rows': [{'unit': '-1|Alpha', 'owner': 'Lunaris', 'keyOwner': None, 'tgt': 'ATK',
                  'ty': 'Actual', 'val': 300, 'vTgt': 'ATK', 'vTy': 'Actual',
                  'ref': '/refExistenceTime3000', 'firstT': 1.0, 'lastT': 60.0, 'seen': 24}],
    }


def selftest():
    cases = []
    good = _fixture()
    cases.append(('accepts a well-formed 1.5.2 export', good, 0))

    # 1.5.2 gating: a 1.5.1 export has no facts identity keys and MUST still be accepted. Without this
    # case the new requirement would flag the entire archive while the self-test stayed green.
    old = _fixture('1.5.1')
    for k in ('atkName', 'vicName', 'atkTeam', 'vicTeam'):
        old['facts']['items'][0].pop(k, None)
    old['events'][0].pop('hitValue', None)
    cases.append(('accepts a 1.5.1 export without the 1.5.2 keys', old, 0))

    legacy = _fixture('1.4.0')
    for k in ('hitDetail', 'timeline', 'facts', 'factCoverage', 'giveTypeList', 'resistMaster',
              'statusValues', 'layout', 'liveRange', 'subParams', 'params', 'statsRows', 'paramOwners'):
        legacy.pop(k, None)
    for k in ('critObserved', 'critObservedYes', 'critAgree', 'critInferredButDenied',
              'critObservedButUnexplained'):
        legacy['reconcile'].pop(k, None)
    cases.append(('accepts a 1.4.0 export without the 1.5.0 keys', legacy, 0))

    miss = _fixture()
    miss.pop('facts')
    cases.append(('REJECTS a 1.5.0 export missing a root key', miss, 1))

    wrong = _fixture()
    wrong['events'][0]['factId'] = 'one'
    cases.append(('REJECTS a wrong event field type', wrong, 1))

    fold = _fixture()
    fold['events'][0]['calc']['fold'] = [{'side': 'atk', 'origin': 'o'}]
    cases.append(('REJECTS a fold entry missing its factor', fold, 1))

    ver = _fixture()
    ver['version'] = '1.5.0'
    ver['reconcile']['critInferredTolerance'] = 'x'
    cases.append(('REJECTS a wrong reconcile field type', ver, 1))

    # 1.5.2: the two new requirements must be shown to REJECT, not just to accept -- a checker that has
    # never been seen to say no is not a checker (the same lesson the 1.5.0 self-test was written for).
    noname = _fixture()
    noname['facts']['items'][0].pop('atkName')
    cases.append(('REJECTS a fact item without its 1.5.2 identity', noname, 1))

    badhit = _fixture()
    badhit['events'][0]['hitValue'] = 'x'
    cases.append(('REJECTS a non-integer hitValue', badhit, 1))

    # Phase E contribution section: present -> validated; absent -> accepted (1.5.5 archive stays green)
    c_ok = _fixture()
    c_ok['contribution'] = _contrib_fixture()
    cases.append(('accepts a well-formed contribution section', c_ok, 0))

    c_nokey = _fixture()
    c_nokey['contribution'] = _contrib_fixture()
    c_nokey['contribution'].pop('method')
    cases.append(('REJECTS a contribution section missing method', c_nokey, 1))

    c_badtype = _fixture()
    c_badtype['contribution'] = _contrib_fixture()
    c_badtype['contribution']['actors'][0]['totalCredit'] = 'many'
    cases.append(('REJECTS a non-numeric actor credit', c_badtype, 1))

    c_tot = _fixture()
    c_tot['contribution'] = _contrib_fixture()
    c_tot['contribution']['attributedDamage'] = 2900.0
    cases.append(('REJECTS a broken attributed+unattributed=total identity', c_tot, 1))

    # ... and the tolerance must not be so tight that independent integer rounding trips it
    c_round = _fixture()
    c_round['contribution'] = _contrib_fixture()
    c_round['contribution']['attributedDamage'] = 2999.0
    c_round['contribution']['unattributedDamage'] = 1.0
    cases.append(('accepts a 1-unit rounding gap in the totals identity', c_round, 0))

    c_actor = _fixture()
    c_actor['contribution'] = _contrib_fixture()
    c_actor['contribution']['actors'][1]['assistCredit'] = 1.0
    cases.append(('REJECTS a broken per-actor base+self+assist=total identity', c_actor, 1))

    # 1.7.12/1.2: the regrouped F5 row prints 自身 and 被队友分走; the guard must reject a row whose
    # own-hit ledger does not close, or the column would be decoration. R62 moved this control onto the
    # 1.2 fixture: the identity is only asserted for is_120 (49 of the 271 REAL 1.7.11 rows break it).
    c_own = _fixture_113()
    c_own['contribution']['actors'][0]['receivedAssist'] = 1500.0
    cases.append(('REJECTS a broken per-actor base+self+received=direct identity (1.2)', c_own, 1))

    c_norecv = _fixture()
    c_norecv['contribution'] = _contrib_fixture()
    c_norecv['contribution']['actors'][0].pop('receivedAssist')
    cases.append(('REJECTS an actor without receivedAssist', c_norecv, 1))

    c_share = _fixture()
    c_share['contribution'] = _contrib_fixture()
    c_share['contribution']['coverage']['unattributedShare'] = 0.2
    cases.append(('REJECTS a broken credited+unattributed share identity', c_share, 1))

    # the degradation stub (an internal error) must be LOUD, not silently accepted as zeroes
    c_err = _fixture()
    c_err['contribution'] = {'schemaVersion': '1.0', 'method': 'log-share/1', 'damageBasis': 'dealt',
                             'error': 'NullReferenceException: boom'}
    cases.append(('REJECTS a degraded contribution error stub', c_err, 1))

    # 1.7.9's per-hit givenFoldOn is OPTIONAL (older exports cannot carry it), so the accept case must
    # not be a bare fixture: a 1.7.2+ version demands paramOwners and a 1.7.4+ version demands atkAdd, and
    # those unrelated misses would make the case fail for the wrong reason. _fixture_178() is the
    # complete 1.7.8 document, which is the right base for "the field is accepted when present".
    pb_taken = _fixture_178()
    pb_taken['contribution']['damageLedger']['eventSumAll'] = 9999
    cases.append(('REJECTS a ledger that disagrees with the game\'s totals.taken', pb_taken, 1))

    pb_taken2 = _fixture_178()
    pb_taken2['totals']['taken'] = 12345
    cases.append(('REJECTS totals.taken != dealt+unattributedDamage', pb_taken2, 1))

    c_gfo_ok = _fixture_178()
    c_gfo_ok['events'][0]['calc']['givenFoldOn'] = True
    cases.append(('accepts a per-hit givenFoldOn', c_gfo_ok, 0))

    c_gfo_bad = _fixture_178()
    c_gfo_bad['events'][0]['calc']['givenFoldOn'] = 'yes'
    cases.append(('REJECTS a non-boolean per-hit givenFoldOn', c_gfo_bad, 1))

    c_old = _fixture('1.5.1')
    for k in ('atkName', 'vicName', 'atkTeam', 'vicTeam'):
        c_old['facts']['items'][0].pop(k, None)
    c_old['events'][0].pop('hitValue', None)
    cases.append(('accepts a 1.5.1 export with no contribution section', c_old, 0))

    # ---- R79 (1.7.29) takenBreakdown, extended by R80 (1.7.30). The section is REQUIRED from 1.7.29 on
    # and merely optional before it, and every identity it states is a claim the page reads aloud: the
    # victim total must survive being split five ways, and the attacker dimension plus the two
    # unbucketed amounts must reconstruct it. A file that breaks one of those must be caught here,
    # because on the page it would look like a plausible number. R80 added the schema claim itself: 1.1
    # means "no dimension is capped", so an R80 export still calling itself 1.0 must be rejected.
    t_ok = _taken_base()
    t_ok['takenBreakdown'] = _taken_fixture()
    cases.append(('accepts a well-formed 1.7.30 takenBreakdown section (schema 1.1)', t_ok, 0))

    t_old = _fixture_114()
    t_old['takenBreakdown'] = _taken_fixture()
    cases.append(('accepts a 1.7.28 export carrying takenBreakdown (pre-R79 shape check only)',
                  t_old, 0))

    t_10 = _taken_base()
    t_10['version'] = '1.7.29'
    t_10['takenBreakdown'] = _taken_fixture('1.0')
    cases.append(('accepts a 1.7.29 export whose section still declares the capped 1.0 shape', t_10, 0))

    t_abs = _taken_base()
    cases.append(('REJECTS a 1.7.30 export without takenBreakdown', t_abs, 1))

    t_cap = _taken_base()
    t_cap['takenBreakdown'] = _taken_fixture('1.0')
    cases.append(('REJECTS a 1.7.30 export that still claims the capped 1.0 shape', t_cap, 1))

    t_sv = _taken_base()
    t_sv['takenBreakdown'] = _taken_fixture('2.0')
    cases.append(('REJECTS a takenBreakdown schemaVersion outside the vocabulary', t_sv, 1))

    t_src = _taken_base()
    t_src['takenBreakdown'] = _taken_fixture()
    t_src['takenBreakdown']['actors'][0]['bySource'][0]['amount'] = 1
    cases.append(('REJECTS a bySource that does not sum to the victim nominal', t_src, 1))

    t_atk = _taken_base()
    t_atk['takenBreakdown'] = _taken_fixture()
    t_atk['takenBreakdown']['actors'][0]['byAttacker'][0]['amount'] = 100
    cases.append(('REJECTS a victim whose byAttacker+friendly+unknown != nominal', t_atk, 1))

    t_id = _taken_base()
    t_id['takenBreakdown'] = _taken_fixture()
    t_id['takenBreakdown']['taken'] = 100
    cases.append(('REJECTS a section whose taken+residual != nominal', t_id, 1))

    t_pos = _taken_base()
    t_pos['takenBreakdown'] = _taken_fixture()
    t_pos['takenBreakdown']['actors'][1]['position'] = 3
    cases.append(('REJECTS a position outside 0/1/2 (the game has two rows, plus unreadable)',
                  t_pos, 1))

    t_q = _taken_base()
    t_q['takenBreakdown'] = _taken_fixture()
    t_q['takenBreakdown']['actors'][0]['bySource'][0]['quality'] = '0/1'
    cases.append(('REJECTS a quality marker that is neither "" nor 近似 a/h', t_q, 1))

    t_m = _taken_base()
    t_m['takenBreakdown'] = _taken_fixture()
    t_m['takenBreakdown']['method'] = 'log-share/1'
    cases.append(('REJECTS a takenBreakdown built by the wrong method', t_m, 1))

    t_b = _taken_base()
    t_b['takenBreakdown'] = _taken_fixture()
    t_b['takenBreakdown']['basis'] = 'dealt'
    cases.append(('REJECTS a takenBreakdown over the wrong basis', t_b, 1))

    t_err = _taken_base()
    t_err['takenBreakdown'] = {'schemaVersion': '1.1', 'method': 'by-event/1', 'basis': 'nominal',
                               'error': 'NullReferenceException: boom'}
    cases.append(('REJECTS a degraded takenBreakdown error stub', t_err, 1))

    # 1.7.2 (阶段 G) paramOwners. The requirement is version-gated, and the owner is allowed to be NULL,
    # because "the game never fills Owner" is a RESULT this channel must be able to report -- while a
    # wrong type, a missing counter or a row without its owner FIELD must all be rejected.
    p_ok = _fixture('1.7.2')
    p_ok['paramOwners'] = _paramowners_fixture()
    cases.append(('accepts a well-formed 1.7.2 paramOwners section', p_ok, 0))

    p_abs = _fixture('1.7.2')
    cases.append(('REJECTS a 1.7.2 export without paramOwners', p_abs, 1))

    p_old = _fixture('1.7.1')
    p_old['paramOwners'] = _paramowners_fixture()
    cases.append(('accepts a 1.7.1 export carrying paramOwners', p_old, 0))

    p_type = _fixture('1.7.2')
    p_type['paramOwners'] = _paramowners_fixture()
    p_type['paramOwners']['entryReads'] = 'x'
    cases.append(('REJECTS a wrong paramOwners counter type', p_type, 1))

    p_row = _fixture('1.7.2')
    p_row['paramOwners'] = _paramowners_fixture()
    p_row['paramOwners']['rows'][0].pop('val')
    cases.append(('REJECTS a paramOwners row without its value', p_row, 1))

    p_field = _fixture('1.7.2')
    p_field['paramOwners'] = _paramowners_fixture()
    p_field['paramOwners']['rows'][0].pop('owner')
    cases.append(('REJECTS a paramOwners row without an owner field', p_field, 1))

    p_v2 = _fixture('1.7.2')
    p_v2['paramOwners'] = _paramowners_fixture()
    p_v2['paramOwners']['entriesTotal'] = 24447
    p_v2['paramOwners']['ownerGetters'] = 15539
    cases.append(('accepts a rev2 paramOwners section with the scan counters', p_v2, 0))

    p_v2bad = _fixture('1.7.2')
    p_v2bad['paramOwners'] = _paramowners_fixture()
    p_v2bad['paramOwners']['entriesTotal'] = 'many'
    cases.append(('REJECTS a wrong optional paramOwners counter type', p_v2bad, 1))

    a_ok = _fixture_174()
    a_ok['atkAdd'] = _atkadd_fixture()
    cases.append(('accepts a well-formed 1.7.4 atkAdd counter block', a_ok, 0))

    a_abs = _fixture_174()
    cases.append(('REJECTS a 1.7.4 export without the atkAdd counters', a_abs, 1))

    a_type = _fixture_174()
    a_type['atkAdd'] = _atkadd_fixture()
    a_type['atkAdd']['emitted'] = 'x'
    cases.append(('REJECTS a wrong atkAdd counter type', a_type, 1))

    a_nogiver = _fixture_174()
    a_nogiver['atkAdd'] = _atkadd_fixture()
    a_nogiver['events'][0]['calc']['atkAdd'] = [{'side': 'atk', 'kind': 'atkadd', 'origin': 'atkadd#x',
                                                'label': 'l', 'factor': 1.2}]
    cases.append(('REJECTS an atkAdd fold without a giver', a_nogiver, 1))

    # ---- P1-A (1.7.8): the self counters and their partition identity
    s_ok = _fixture('1.7.8')
    s_ok['paramOwners'] = _paramowners_fixture()
    s_ok['atkAdd'] = _atkadd_fixture()
    cases.append(('accepts the 1.7.8 self-attribution counters', s_ok, 0))

    s_missing = _fixture('1.7.8')
    s_missing['paramOwners'] = _paramowners_fixture()
    s_missing['atkAdd'] = _atkadd_fixture()
    s_missing['atkAdd'].pop('selfByKey')
    cases.append(('REJECTS a 1.7.8 export without selfByKey', s_missing, 1))

    s_identity = _fixture('1.7.8')
    s_identity['paramOwners'] = _paramowners_fixture()
    s_identity['atkAdd'] = _atkadd_fixture()
    s_identity['atkAdd']['selfValues'] = 9   # 5 + 2 != 9
    cases.append(('REJECTS a broken selfValues == selfByKey + selfByNameFallback', s_identity, 1))

    a_fold = _fixture_174()
    a_fold['atkAdd'] = _atkadd_fixture()
    a_fold['events'][0]['calc']['atkAdd'] = [{'side': 'atk', 'kind': 'atkadd', 'origin': 'atkadd#x',
                                              'label': 'l', 'factor': 1.2, 'byUnit': 'x'}]
    cases.append(('accepts a well-formed atkAdd fold', a_fold, 0))

    p_null = _fixture('1.7.2')
    p_null['paramOwners'] = _paramowners_fixture()
    p_null['paramOwners']['rows'][0]['owner'] = None
    p_null['paramOwners']['ownerNull'] = 1
    cases.append(('accepts a row whose owner is unreadable (a reportable result)', p_null, 0))

    # ---- P0-B (1.7.8) schema 1.1: the acceptance sample of the review, and one REJECT case per way the
    # ledger can be mis-built. Every one of these failed before the fix in the sense that the OLD checker
    # accepted the old (double-counting) totalDamage convention -- the 1.1 cases below pin the new one.
    cases.append(('accepts the 1.7.8 schema 1.1 acceptance sample (P0-B)', _fixture_178(), 0))

    pb_old = _fixture_178()
    pb_old['contribution']['totalDamage'] = 700   # = analyzable + unattributed, the pre-1.7.8 convention
    cases.append(('REJECTS the pre-1.7.8 totalDamage convention (analyzable+unattributed)', pb_old, 1))

    pb_conserve = _fixture_178()
    pb_conserve['contribution']['unattributedDamage'] = 200  # 500+200 != 600
    cases.append(('REJECTS a 1.1 file where attributed+unattributed != analyzableDealt', pb_conserve, 1))

    pb_noled = _fixture_178()
    pb_noled['contribution'].pop('damageLedger')
    cases.append(('REJECTS a 1.1 file with no damageLedger', pb_noled, 1))

    pb_merge = _fixture_178()
    pb_merge['contribution']['coverage']['excludedDamage'] = 800  # merged with the out-of-scope bucket
    cases.append(('REJECTS excludedDamage that was merged with out-of-scope damage', pb_merge, 1))

    pb_ratio = _fixture_178()
    pb_ratio['contribution']['coverage']['analysisDamageCoverage'] = 0.5
    cases.append(('REJECTS a wrong analysisDamageCoverage', pb_ratio, 1))

    pb_gap = _fixture_178()
    pb_gap['contribution']['damageLedger']['outsideTeamDealt'] = 100  # 600+100 != 1000
    cases.append(('REJECTS a broken ledger identity (analyzable+outsideTeam != totalsDealt)', pb_gap, 1))

    pb_unroutable = _fixture_178()
    pb_unroutable['totals']['unattributedDamage'] = 999
    cases.append(('REJECTS unknownAttackerDealt that disagrees with totals.unattributedDamage', pb_unroutable, 1))

    pb_hits = _fixture_178()
    pb_hits['contribution']['damageLedger']['events'] = 11
    cases.append(('REJECTS a broken ledger hit identity', pb_hits, 1))

    pb_null = _fixture_178()
    pb_null['contribution']['coverage']['analysisDamageCoverage'] = None  # denominator IS known
    cases.append(('REJECTS a null ratio whose denominator is known', pb_null, 1))

    pb_zero = _fixture_178()
    pb_zero['totals']['dealt'] = 0
    pb_zero['totals']['unattributedDamage'] = 0
    pb_zero['totals']['taken'] = 0   # the game-side accumulator of an empty battle, same as dealt/unattributed
    c0 = pb_zero['contribution']
    c0['totalDamage'] = 0; c0['attributedDamage'] = 0; c0['unattributedDamage'] = 0
    c0['coverage'].update({'creditedShare': 0.0, 'unattributedShare': 0.0, 'analyzableDealt': 0,
                           'excludedDamage': 0, 'analysisDamageCoverage': None,
                           'creditCoverageWithinAnalyzed': None, 'overallAttributedCoverage': None,
                           'hits': 0})
    c0['damageLedger'].update({'totalsDealt': 0, 'events': 0, 'analyzableHits': 0, 'outsideTeamHits': 0,
                               'outsideTeamDealt': 0, 'unknownAttackerHits': 0, 'unknownAttackerDealt': 0,
                               'eventSumAll': 0, 'reconciliationGap': 0})
    c0['actors'] = []
    cases.append(('accepts an empty battle (ratios null, zero denominators)', pb_zero, 0))

    # the ledger identity compares two independent accumulators, so a 1-unit drift on a 500M battle must
    # be ACCEPTED (a tighter check would reject real files); a dropped bucket must still be caught.
    pb_drift = _fixture_178()
    _c = pb_drift['contribution']
    _c['coverage'].update({'analyzableDealt': 300000000, 'analysisDamageCoverage': 0.6,
                           'overallAttributedCoverage': 0.5})
    _c['totalDamage'] = 300000000
    _c['attributedDamage'] = 250000000
    _c['unattributedDamage'] = 50000000
    _c['coverage']['creditCoverageWithinAnalyzed'] = 250000000.0 / 300000000.0
    _c['coverage']['creditedShare'] = 250000000.0 / 300000000.0
    _c['coverage']['unattributedShare'] = 50000000.0 / 300000000.0
    _c['damageLedger'].update({'totalsDealt': 500000000, 'outsideTeamDealt': 199999999})
    cases.append(('accepts a 1-unit ledger drift on a 500M battle', pb_drift, 0))

    pb_drop = _fixture_178()
    pb_drop['contribution']['damageLedger']['outsideTeamDealt'] = 0   # the whole bucket dropped
    cases.append(('REJECTS a dropped outside-team bucket on a 1e4 battle', pb_drop, 1))

    # 1.7.12: the same-team split. Present -> type-checked and its identity asserted; absent -> accepted
    # (the whole pre-1.7.12 archive must stay green).
    f_ok = _fixture_178()
    f_ok['contribution']['actors'][0].update({'friendly': 150.0, 'friendlyHits': 2,
                                              'hostileDamage': 450.0, 'hits': 5})
    cases.append(('accepts an actor with a closing friendly+hostile split', f_ok, 0))

    f_bad = _fixture_178()
    f_bad['contribution']['actors'][0].update({'friendly': 150.0, 'friendlyHits': 2,
                                               'hostileDamage': 1000.0, 'hits': 5})
    cases.append(('REJECTS an actor whose friendly+hostile != direct', f_bad, 1))

    f_hits = _fixture_178()
    f_hits['contribution']['actors'][0].update({'friendly': 150.0, 'friendlyHits': 9,
                                                'hostileDamage': 450.0, 'hits': 5})
    cases.append(('REJECTS friendlyHits above the actor hits', f_hits, 1))

    f_type = _fixture_178()
    f_type['contribution']['actors'][0]['friendly'] = 'many'
    cases.append(('REJECTS a non-numeric actor friendly', f_type, 1))

    # 1.7.12 root config: the settings that change the meaning of existing numbers must be stated by the
    # file itself, and the requirement is version-gated so the archive stays valid.
    cfg_ok = _fixture_178()
    cfg_ok['version'] = '1.7.12'
    cfg_ok['config'] = {'filterFriendlyFire': True}
    cases.append(('accepts a 1.7.12 export stating filterFriendlyFire', cfg_ok, 0))

    cfg_abs = _fixture_178()
    cfg_abs['version'] = '1.7.12'
    cases.append(('REJECTS a 1.7.12 export without its config block', cfg_abs, 1))

    cfg_type = _fixture_178()
    cfg_type['version'] = '1.7.12'
    cfg_type['config'] = {'filterFriendlyFire': 'yes'}
    cases.append(('REJECTS a non-boolean filterFriendlyFire', cfg_type, 1))

    # ---- R61 (schema 1.2): same-team damage left the analysed pool ----
    c12 = _fixture_113()
    cases.append(('accepts a well-formed 1.2 section (selfTeam bucket in the ledger)', c12, 0))

    c12_noledger = _fixture_113()
    c12_noledger['contribution']['damageLedger'].pop('selfTeamDealt')
    cases.append(('REJECTS a 1.2 ledger without the selfTeam bucket', c12_noledger, 1))

    c12_ident = _fixture_113()
    c12_ident['contribution']['damageLedger']['selfTeamDealt'] = 10
    cases.append(('REJECTS a 1.2 ledger whose buckets do not reach totalsDealt', c12_ident, 1))

    c12_hits = _fixture_113()
    c12_hits['contribution']['damageLedger']['selfTeamHits'] = 7
    cases.append(('REJECTS a 1.2 ledger whose hit buckets do not reach events', c12_hits, 1))

    c12_actor = _fixture_113()
    c12_actor['contribution']['actors'][0].pop('friendly')
    cases.append(('REJECTS a 1.2 actor without the excluded same-team amount', c12_actor, 1))

    # ---- R62 (1.7.14, A): the damage-detail channel's rejected-figure counter ----
    # The requirement is version-gated, so the whole pre-R62 archive keeps validating; a 1.7.14 file that
    # omits the counter is REJECTED rather than read as "nothing was ever rejected".
    c14 = _fixture_114()
    cases.append(('accepts a 1.7.14 export carrying hitDetail.matchRejected', c14, 0))

    c14_abs = _fixture_114()
    del c14_abs['hitDetail']['matchRejected']
    cases.append(('REJECTS a 1.7.14 export without hitDetail.matchRejected', c14_abs, 1))

    c14_type = _fixture_114()
    c14_type['hitDetail']['matchRejected'] = 'two'
    cases.append(('REJECTS a non-integer hitDetail.matchRejected', c14_type, 1))

    # ---- R62 (C): the fold list's SIDES and the two summary multipliers must agree ----
    c14_fold_ok = _fixture_114()
    c14_fold_ok['events'][0]['calc']['dealtMult'] = 1.15
    c14_fold_ok['events'][0]['calc']['takenMult'] = 1.0
    cases.append(('accepts a 1.7.14 calc whose fold sides match dealtMult/takenMult', c14_fold_ok, 0))

    c14_fold_bad = _fixture_114()
    c14_fold_bad['events'][0]['calc']['dealtMult'] = 1.0
    c14_fold_bad['events'][0]['calc']['takenMult'] = 1.15
    cases.append(('REJECTS a 1.7.14 calc whose fold sides do not match the multipliers', c14_fold_bad, 1))

    # A <= 1.7.13 export keeps validating: the old bucketing is not a defect of those files.
    old_fold = _fixture_113()
    old_fold['events'][0]['calc']['dealtMult'] = 1.15
    old_fold['events'][0]['calc']['takenMult'] = 1.0
    cases.append(('accepts a 1.7.13 calc with the pre-R62 bucketing', old_fold, 0))

    # ---- R62: two actor rules that REAL data falsified, and the identities that replace them ----
    # (1) friendlyHits vs hits: under 1.2 they are DISJOINT buckets (in-pool vs excluded same-team), so
    # more same-team hits than in-pool hits is normal -- 26 real rows in the live corpus (1.7.13/1.7.15).
    f_ok12 = _fixture_113()
    f_ok12['contribution']['actors'][0].update({'friendly': 900.0, 'friendlyHits': 9})
    cases.append(('accepts a 1.2 actor with more same-team hits than in-pool hits', f_ok12, 0))
    # ... while the <= 1.1 contract still forbids it (that is where `hits` counted every hit).
    f_bad11 = _fixture_178()
    f_bad11['contribution']['actors'][0].update({'friendly': 900.0, 'friendlyHits': 9, 'hits': 5})
    cases.append(('REJECTS a 1.1 actor whose friendlyHits exceed its hits', f_bad11, 1))
    # (2) the per-actor rows must PARTITION the analysed pool: sum(hits) == ledger.analyzableHits
    # (measured 0 mismatches on all 38 real 1.2 sections) -- the fixture itself used to break it.
    sum_bad = _fixture_113()
    sum_bad['contribution']['actors'][0]['hits'] = 9
    cases.append(('REJECTS a 1.2 section whose actor hits do not sum to the ledger', sum_bad, 1))
    sum_bad2 = _fixture_113()
    sum_bad2['contribution']['actors'][0]['directDamage'] = 999
    cases.append(('REJECTS a 1.2 section whose directDamage does not sum to analyzableDealt', sum_bad2, 1))
    # (3) base+self+received == direct is a 1.2 identity only; 49 of the 271 real 1.7.11 rows break it.
    old11 = _fixture_178()
    old11['contribution']['actors'][0]['directDamage'] = 900
    cases.append(('accepts a 1.1 actor whose directDamage the old formula cannot close', old11, 0))

    out = []
    fails = 0
    for label, doc, want_bad in cases:
        probs, missing, cov = check_export(doc)
        got = len(probs) + len(missing)
        ok = (got > 0) if want_bad else (got == 0)
        if not ok:
            fails += 1
        out.append('%-4s %-52s caught=%d%s' % ('PASS' if ok else 'FAIL', label, got,
                                               ('  [' + '; '.join((probs + missing)[:3]) + ']') if got else ''))
    io.open(os.path.join(ROOT, 'export_schema_selftest.txt'), 'w', encoding='utf-8').write('\n'.join(out) + '\n')
    print('selftest cases=%d failed=%d' % (len(cases), fails))
    for line in out:
        print(line.encode('ascii', 'replace').decode('ascii'))
    return fails


def main(argv):
    global REPORT
    if '--selftest' in argv:
        return 1 if selftest() else 0
    argv = list(argv)
    # RF0 section 5.4 (output isolation): the report used to be written to a FIXED path inside
    # _dpsm_work, so an acceptance batch rewrote a file outside its own directory. --report lets the
    # caller send it into the batch archive; the default is unchanged for standalone use.
    if '--report' in argv:
        i = argv.index('--report')
        if i + 1 >= len(argv):
            print('--report needs a path')
            return 2
        REPORT = os.path.abspath(argv[i + 1])
        del argv[i:i + 2]
    paths = [a for a in argv[1:] if not a.startswith('--')]
    if not paths:
        allf = sorted(glob.glob(os.path.join(EXPORTS, '*.json')), key=os.path.getmtime)
        if not allf:
            print('no exports at %s (cleared 2026-10-03; re-run after the next battle)' % EXPORTS)
            return 0
        paths = [allf[-1]]
    bad = report(paths)
    print('checked %d file(s), problems=%d, report=%s' % (len(paths), bad, REPORT))
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
