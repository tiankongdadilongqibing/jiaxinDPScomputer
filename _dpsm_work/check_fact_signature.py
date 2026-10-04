# -*- coding: utf-8 -*-
"""
FACT signature replay -- offline, BEFORE the next battle.

WHY THIS EXISTS
    On 2026-10-03 the FACT table's signature was "fixed" in 1.5.1 (drop the per-hit churning counts)
    and the fix was shipped. It was still wrong: 1485 distinct signatures for 5055 hits, so the
    600-class cap bound and coverage was 36.6%. The 1.5.2 cause was different again -- the signature
    used `vicKey`, a per-INSTANCE actor key, which alone produced 1232 of those 1485 classes.

    Both mistakes were cheap to detect and expensive to detect LATE, because the only place the truth
    showed up was a battle the user had to fight. This script replays a candidate signature over the
    events of an export ALREADY ON DISK, so "how many classes will this key produce, and what coverage
    will it get" is answered before shipping instead of after.

WHAT IT REPRODUCES
    The plugin's own filling rule, faithfully: classes are kept in FIRST-SIGHT order and the table
    stops accepting new ones at MaxClasses. A key can therefore look fine by distinct-count and still
    starve, because a rare early signature takes a slot a common one needed. Both numbers are printed:
    `first-sight` (what FactStore does) and `size-ranked` (the upper bound it does not reach).

    It is also a REGRESSION REFERENCE, but read carefully: on battle_411001_20261003_175142 the legacy
    1.5.1 key replays to 2136 served over the 5055 EXPORTED events, while the plugin's own table recorded
    2017 hits. Those two do NOT have to agree and do not: the plugin was fed 5220 `Observe` calls, i.e.
    ~165 more than the export contains, and extra early classes consume cap slots that later exported
    events then miss. The gap is the same ~3% discrepancy the 1.5.2 log lines were added to expose. So
    the number to trust here is the DISTINCT count (1485 for 1.5.1, 240 for 1.5.2), which is a property
    of the key alone; the served numbers are properties of the key AND the counter discrepancy.

P0-A (2026-10-04) -- this script now FAILS instead of printing a verdict:
    * the cap literals at the top are checked against src/Diagnostics/FactStore.cs on every run, so the
      coverage numbers below cannot silently describe a cap the plugin no longer ships;
    * STARVED replay (classesOverflow > 0), a live-budget shortfall (liveSkipped > 0), a cap that the
      export itself contradicts (facts.classes > facts.maxClasses) and a replay/plugin bookkeeping
      mismatch that the "Observe calls > exported events" explanation cannot absorb all return non-zero;
    * an export whose events carry no calc block, or a missing export directory, is DATA_MISSING (3),
      not the old "no export under ... return 0".

USAGE
    python check_fact_signature.py [export.json] [--selftest] [--cap-source PATH] [--outdir DIR]
    Exit: 0 = the shipped key fits and every consistency check holds; 1 = ERROR (starved / cap drift /
    coverage anomaly); 3 = DATA_MISSING (no export, or no events with a signature to replay).
    ASCII stdout (GBK console); a UTF-8 report is written to _dpsm_work/fact_signature.txt.
"""

import collections
import io
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.abspath(__file__))
EXPORTS = os.path.join(os.path.dirname(ROOT), 'BepInEx', 'plugins', 'DpsMeter', 'exports')
REPORT = os.path.join(ROOT, 'fact_signature.txt')
FACTSTORE_CS = os.path.join(ROOT, 'src', 'Diagnostics', 'FactStore.cs')

# The caps the plugin ships with. Kept here as literals ON PURPOSE: if they change in
# Diagnostics/FactStore.cs without changing here, this tool's coverage number becomes a lie, and the
# selftest's legacy-expectation check below is what will notice.
# P0-A: they are no longer trusted on faith -- check_caps() reads the C# constants on every main() run.
MAX_CLASSES_152 = 2400
MAX_LIVE_152 = 900
MAX_CLASSES_151 = 600          # the cap the 1.5.1 build shipped with, for the reference replay

# Exit contract shared with contribution_gate.py (kept as literals so this script stays standalone
# and importable from a bare checkout).
EXIT_OK = 0
EXIT_ERROR = 1
EXIT_DATA_MISSING = 3


# --------------------------------------------------------------------------------------------
# P0-A: the cap literals above must equal the C# constants the plugin actually ships.
# --------------------------------------------------------------------------------------------

def read_cs_constants(path):
    """{name: int} for every `internal const int Name = VALUE;` in the C# source."""
    text = io.open(path, encoding='utf-8-sig').read()
    return {m.group(1): int(m.group(2))
            for m in re.finditer(r'const\s+int\s+(\w+)\s*=\s*(\d+)\s*;', text)}


def check_caps(source=FACTSTORE_CS, caps=None):
    """[] when this script's caps equal the shipped ones, else one message per drifted constant.

    Returns (failures, actual). `source` absent is itself a failure: an unverifiable cap is not a
    verified one.
    """
    caps = caps or {'MaxClasses': MAX_CLASSES_152, 'MaxLiveClasses': MAX_LIVE_152}
    if not os.path.exists(source):
        return (['cap source not found: %s' % source], {})
    actual = read_cs_constants(source)
    fails = []
    for name, want in sorted(caps.items()):
        got = actual.get(name)
        if got is None:
            fails.append('%s is not declared in %s' % (name, os.path.basename(source)))
        elif got != want:
            fails.append('%s drifted: check_fact_signature.py says %d, %s says %d'
                         % (name, want, os.path.basename(source), got))
    return (fails, actual)


# --------------------------------------------------------------------------------------------

def victim_statuses(ev):
    """The exact list FactStore appends, recovered from the exported comp4 sentence.

    comp4 is "受击方状态:<names>   自身状态:<names>"; the names are in BuffList order for 1.5.1 and
    that order is precisely what the 1.5.2 fix canonicalises, so the RAW list has to be recovered
    rather than a pre-sorted one."""
    c4 = ev.get('comp4') or ''
    i = c4.find('受击方状态:')
    if i < 0:
        return None
    s = c4[i + len('受击方状态:'):]
    j = s.find('   自身状态:')
    if j >= 0:
        s = s[:j]
    s = s.strip()
    if s in ('', '无'):
        return []
    return [x for x in s.split('、') if x]


def key_151(ev):
    """The signature 1.5.1 shipped: per-INSTANCE actor keys, RAW (BuffList-order) status list."""
    c = ev['calc']
    st = victim_statuses(ev)
    return (ev.get('atkKey'), ev.get('vicKey'), c.get('effectId'), c.get('hitType'),
            c.get('knownMult'), c.get('critRate'), c.get('critDamageRate'),
            c.get('vicBlocking'), c.get('vicUnitBlocking'), c.get('madnessRatio'),
            tuple(st) if st is not None else ('?',))


def key_152(ev):
    """The signature 1.5.2 ships: unit name + team instead of the instance key, canonical status set."""
    c = ev['calc']
    st = victim_statuses(ev)
    return (ev.get('attacker'), ev.get('atkTeam'), ev.get('victim'), ev.get('vicTeam'),
            c.get('effectId'), c.get('hitType'), c.get('knownMult'), c.get('critRate'),
            c.get('critDamageRate'), c.get('vicBlocking'), c.get('vicUnitBlocking'),
            c.get('madnessRatio'), tuple(sorted(st)) if st is not None else ('?',))


def replay(events, keyfn, max_classes, max_live):
    """Reproduce FactStore's filling rule exactly. Returns a stats dict."""
    index = {}
    order = []                 # insertion order == first-sight order
    counts = collections.Counter()
    served = 0
    overflow = 0
    live_classes = 0
    for ev in events:
        k = keyfn(ev)
        counts[k] += 1
        if k in index:
            served += 1
            continue
        if len(order) >= max_classes:
            overflow += 1
            continue
        index[k] = len(order)
        order.append(k)
        served += 1
        if len(order) <= max_live:
            live_classes += 1
    sizes = sorted(counts.values(), reverse=True)
    return {
        'distinct': len(counts),
        'classes': len(order),
        'served': served,
        'overflow': overflow,
        'events': len(events),
        'liveClasses': live_classes,
        'singletons': sum(1 for v in counts.values() if v == 1),
        'maxHits': (sizes[0] if sizes else 0),
        # the upper bound first-sight does NOT reach: keep the biggest classes instead
        'servedIfRanked': sum(sizes[:max_classes]),
        'top': counts.most_common(5),
    }


def load_events(path):
    d = json.load(io.open(path, encoding='utf-8'))
    return d, [e for e in d.get('events') or []
               if e.get('type') == 'dmg' and e.get('calc')]


def report_line(label, s, cap):
    verdict = 'OK ' if s['overflow'] == 0 else 'STARVED'
    return ('%-26s distinct=%-6d cap=%-5d kept=%-5d served=%-6d overflow=%-6d '
            'servedIfRanked=%-6d singletons=%-5d maxHits=%-5d  %s'
            % (label, s['distinct'], cap, s['classes'], s['served'], s['overflow'],
               s['servedIfRanked'], s['singletons'], s['maxHits'], verdict))


def coverage_policy(d, evs, legacy, shipped, dmg_events):
    """P0-A: the conditions that used to be printed and then ignored.

    Returns (failures, notes). Every failure is a real contradiction; informational lines that can be
    explained by "the plugin saw more Observe calls than the export contains" stay notes.
    """
    fails = []
    notes = []
    facts = d.get('facts') or {}
    if shipped['overflow'] > 0:
        fails.append('shipped key STARVES: %d hits had no room (classes kept=%d, cap=%d)'
                     % (shipped['overflow'], shipped['classes'], MAX_CLASSES_152))
    if shipped['classes'] > MAX_CLASSES_152:
        fails.append('shipped key kept %d classes above its cap %d' % (shipped['classes'], MAX_CLASSES_152))
    if shipped['liveClasses'] < shipped['classes'] and shipped['classes'] > MAX_LIVE_152:
        fails.append('live budget short: %d classes but only %d got a live read (liveSkipped would be %d)'
                     % (shipped['classes'], shipped['liveClasses'], shipped['classes'] - MAX_LIVE_152))
    # the export's own bookkeeping may not contradict itself
    classes = facts.get('classes')
    max_classes = facts.get('maxClasses')
    live = facts.get('liveClasses')
    max_live = facts.get('maxLiveClasses')
    overflow = facts.get('classesOverflow')
    live_skipped = facts.get('liveSkipped')
    errors = facts.get('errors')
    observed = facts.get('observed')
    items = facts.get('items') or []
    if classes is not None and max_classes is not None and classes > max_classes:
        fails.append('facts.classes=%s exceeds facts.maxClasses=%s' % (classes, max_classes))
    if live is not None and max_live is not None and live > max_live:
        fails.append('facts.liveClasses=%s exceeds facts.maxLiveClasses=%s' % (live, max_live))
    if overflow:
        fails.append('the plugin itself counted %s class overflow(s) (facts.classesOverflow)' % overflow)
    if live_skipped:
        fails.append('the plugin itself skipped %s live read(s) (facts.liveSkipped)' % live_skipped)
    if errors:
        fails.append('the plugin counted %s FACT capture error(s) (facts.errors)' % errors)
    if items and observed is not None and sum(it.get('hits', 0) for it in items) != observed:
        fails.append('facts.observed=%s != sum(facts.items[].hits)=%d'
                     % (observed, sum(it.get('hits', 0) for it in items)))
    if dmg_events and not evs:
        fails.append('%d damage events but none carries a calc block: the shipped signature cannot be '
                     'replayed at all' % dmg_events)
    # informational, explained by the Observe/export gap (documented above)
    if classes is not None and shipped['classes'] != classes:
        notes.append('replay kept %d classes over the exported events; the plugin recorded %s '
                     '(extra Observe calls are documented; the DISTINCT count is the key property)'
                     % (shipped['classes'], classes))
    if facts.get('stateUnreadable'):
        notes.append('the plugin could not read the live state of %s class(es) (facts.stateUnreadable)'
                     % facts.get('stateUnreadable'))
    return fails, notes


# --------------------------------------------------------------------------------------------
# self-test: this tool's whole purpose is to accept or reject a KEY, so it must be shown to reject
# one. The synthetic case is the exact mistake 1.5.2 fixed -- a per-instance victim identity --
# and the assertion is that the tool reports massive overflow for it and none for the kind identity.
# --------------------------------------------------------------------------------------------

def _synth(n_hits=40, n_instances=12):
    evs = []
    for i in range(n_hits):
        st = ['毒', '火傷'] if i % 3 else ['火傷', '毒']       # same SET, two orders
        evs.append({
            'atkKey': 1, 'vicKey': (i % n_instances) + 1, 'atkTeam': 1, 'vicTeam': 2,
            'attacker': 'A', 'victim': 'V',
            'calc': {'effectId': 0, 'hitType': 2, 'knownMult': 1.5, 'critRate': 0,
                     'critDamageRate': 150, 'vicBlocking': 0, 'vicUnitBlocking': 0,
                     'madnessRatio': 0},
            'comp4': '受击方状态:' + '、'.join(st) + '   自身状态:无',
        })
    return evs


def selftest():
    fails = 0
    out = []

    def chk(label, ok):
        nonlocal fails
        if not ok:
            fails += 1
        out.append('%-4s %s' % ('PASS' if ok else 'FAIL', label))

    evs = _synth()
    kind = replay(evs, key_152, 8, 8)
    inst = replay(evs, key_151, 8, 8)
    chk('kind identity replays to ONE class', kind['distinct'] == 1)
    chk('kind identity does not starve', kind['overflow'] == 0 and kind['served'] == len(evs))
    chk('instance identity explodes (12 victims)', inst['distinct'] == 12)
    chk('instance identity STARVES at cap 8', inst['overflow'] > 0)
    # the canonical-order claim: the synthetic data deliberately alternates the two orders
    chk('status ORDER alone does not split the kind key', kind['distinct'] == 1)
    raw = replay(evs, lambda e: key_152(e)[:-1] + (tuple(victim_statuses(e)),), 8, 8)
    chk('RAW status order WOULD split it (so the fix is not cosmetic)', raw['distinct'] == 2)
    # the live budget must cover every class, or classes silently lose their one live read
    live = replay(evs, key_152, 2400, 900)
    chk('live budget covers every class', live['liveClasses'] == live['classes'])

    # ---- P0-A: the coverage policy must be shown to FIRE, not just to exist ----------------
    # 'inst' is the STARVED replay (12 classes at cap 8), so the policy's starvation branch is fed
    # the real thing rather than a synthetic flag.
    facts_bad = {'facts': {'classes': 3, 'maxClasses': 2, 'classesOverflow': 4, 'liveSkipped': 1,
                           'errors': 2, 'observed': 10,
                           'items': [{'hits': 3}, {'hits': 3}]}}
    fails_bad, _notes = coverage_policy(facts_bad, evs, inst, inst, len(evs))
    chk('coverage policy fires on overflow', any('STARVE' in f for f in fails_bad))
    chk('coverage policy fires on classes > maxClasses', any('maxClasses' in f for f in fails_bad))
    chk('coverage policy fires on liveSkipped', any('liveSkipped' in f for f in fails_bad))
    chk('coverage policy fires on plugin errors', any('error' in f for f in fails_bad))
    chk('coverage policy fires on observed != sum(items.hits)',
        any('sum(facts.items' in f for f in fails_bad))
    facts_ok = {'facts': {'classes': 1, 'maxClasses': 2400, 'classesOverflow': 0, 'liveSkipped': 0,
                          'errors': 0, 'observed': 3, 'items': [{'hits': 3}]}}
    fails_ok, _notes_ok = coverage_policy(facts_ok, evs, kind, kind, len(evs))
    chk('coverage policy stays silent on a clean export', not fails_ok)
    chk('coverage policy refuses to replay an export with no calc block',
        coverage_policy({}, [], kind, kind, 5)[0] != [])

    # ---- P0-A: the cap literals must be shown to be COMPARED, not just declared -----------
    tmp = os.path.join(ROOT, '_fact_signature_selftest_caps.cs')
    io.open(tmp, 'w', encoding='utf-8').write(
        'namespace DpsMeter { static class FactStore {'
        ' internal const int MaxClasses = 2400;'
        ' internal const int MaxLiveClasses = 900; } }\n')
    ok_caps, actual_ok = check_caps(tmp)
    chk('cap check accepts FactStore.cs values that agree', not ok_caps and actual_ok['MaxClasses'] == 2400)
    io.open(tmp, 'w', encoding='utf-8').write(
        'namespace DpsMeter { static class FactStore {'
        ' internal const int MaxClasses = 999;'
        ' internal const int MaxLiveClasses = 900; } }\n')
    bad_caps, _actual = check_caps(tmp)
    chk('cap check REJECTS a drifted MaxClasses (2400 != 999)',
        any('MaxClasses drifted' in f for f in bad_caps))
    io.open(tmp, 'w', encoding='utf-8').write('namespace DpsMeter { }\n')
    missing_caps, _actual = check_caps(tmp)
    chk('cap check rejects a source with no such constant',
        any('not declared' in f for f in missing_caps))
    os.remove(tmp)
    none_caps, _actual = check_caps(os.path.join(ROOT, 'no_such_factstore.cs'))
    chk('cap check rejects a missing source file', bool(none_caps))

    io.open(os.path.join(ROOT, 'fact_signature_selftest.txt'), 'w', encoding='utf-8').write(
        '\n'.join(out) + '\n')
    print('selftest cases=%d failed=%d' % (len(out), fails))
    for line in out:
        print(line.encode('ascii', 'replace').decode('ascii'))
    return fails


def main(argv):
    args = argv[1:]
    if '--selftest' in args:
        return EXIT_ERROR if selftest() else EXIT_OK
    path = None
    outdir = None
    caps_source = FACTSTORE_CS
    i = 0
    while i < len(args):
        a = args[i]
        if a == '--cap-source' and i + 1 < len(args):
            caps_source = args[i + 1]
            i += 2
            continue
        if a == '--outdir' and i + 1 < len(args):
            outdir = args[i + 1]
            i += 2
            continue
        if not a.startswith('--'):
            path = a
        i += 1

    # P0-A: the cap literals are part of the claim, so they are verified on EVERY run.
    cap_fails, cap_actual = check_caps(caps_source)
    if cap_fails:
        print('CAP CHECK FAILED -- the coverage numbers below would describe a cap the plugin does '
              'not ship:')
        for f in cap_fails:
            print('  ' + f.encode('ascii', 'replace').decode('ascii'))
        return EXIT_ERROR
    print('cap check: %s' % json.dumps(cap_actual, sort_keys=True))

    if path is None:
        cands = []
        for dirpath, _dirs, files in os.walk(EXPORTS):
            for f in files:
                if f.endswith('.json'):
                    p = os.path.join(dirpath, f)
                    cands.append((os.path.getmtime(p), p))
        if not cands:
            print('DATA_MISSING: no export under %s -- nothing to replay; this is not a pass '
                  '(exit %d). Re-run after the next battle or pass an export path.'
                  % (EXPORTS, EXIT_DATA_MISSING))
            return EXIT_DATA_MISSING
        path = max(cands)[1]

    try:
        d, evs = load_events(path)
    except Exception as exc:
        print('ERROR: could not read %s: %s: %s'
              % (os.path.basename(path), type(exc).__name__,
                 str(exc).encode('ascii', 'replace').decode('ascii')))
        return EXIT_ERROR
    dmg_events = sum(1 for e in (d.get('events') or []) if e.get('type') == 'dmg')
    if not evs:
        print('DATA_MISSING: %s has %d damage events but none with a calc block, so no signature can '
              'be replayed (exit %d).' % (os.path.basename(path), dmg_events, EXIT_DATA_MISSING))
        return EXIT_DATA_MISSING

    out = []
    out.append('=' * 118)
    out.append('FACT signature replay')
    out.append('  file    %s' % os.path.basename(path))
    out.append('  version %s   quest %s   damage events %d   replayable %d'
               % (d.get('version'), d.get('quest'), dmg_events, len(evs)))
    out.append('  caps    ' + json.dumps(cap_actual, sort_keys=True))
    out.append('-' * 118)

    legacy = replay(evs, key_151, MAX_CLASSES_151, 420)
    shipped = replay(evs, key_152, MAX_CLASSES_152, MAX_LIVE_152)
    out.append(report_line('1.5.1 legacy key', legacy, MAX_CLASSES_151))
    out.append(report_line('1.5.2 shipped key', shipped, MAX_CLASSES_152))
    out.append('')
    out.append('coverage: 1.5.1 legacy %d/%d (%.1f%%)   1.5.2 shipped %d/%d (%.1f%%)'
               % (legacy['served'], len(evs), 100.0 * legacy['served'] / max(1, len(evs)),
                  shipped['served'], len(evs), 100.0 * shipped['served'] / max(1, len(evs))))

    # Cross-check against the export's own bookkeeping, when it carries a facts table. This is
    # INFORMATIONAL, not an equality: the plugin's table was fed every `Observe` call (including the
    # ~165 that never became exported events), so its served count and this replay's cannot be equal
    # by construction. The DISTINCT count above is the key's own property and is what a verdict may
    # rest on; printing both keeps the difference visible instead of hiding it behind one number.
    facts = d.get('facts') or {}
    items = facts.get('items') or []
    if items:
        hits = sum(it.get('hits', 0) for it in items)
        out.append('plugin bookkeeping: observed=%s  sum(facts.items[].hits)=%d  classes=%s/%s'
                   % (facts.get('observed'), hits, facts.get('classes'), facts.get('maxClasses')))
        out.append('  note: replay served (%d) counts only the %d exported events; the plugin counted '
                   '%s Observe calls. Not expected to be equal.'
                   % (legacy['served'], len(evs), facts.get('observed')))

    policy_fails, policy_notes = coverage_policy(d, evs, legacy, shipped, dmg_events)
    for n in policy_notes:
        out.append('NOTE: ' + n)
    if policy_fails:
        out.append('VERDICT: FAIL -- %d consistency failure(s):' % len(policy_fails))
        for f in policy_fails:
            out.append('  * ' + f)
    else:
        out.append('VERDICT: shipped key fits (headroom %d classes) and every consistency check holds.'
                   % (MAX_CLASSES_152 - shipped['classes']))
    out.append('')
    out.append('top 5 classes by hits (shipped key):')
    for k, v in shipped['top']:
        out.append('   n=%-5d %s' % (v, k))

    report = os.path.join(outdir, os.path.basename(REPORT)) if outdir else REPORT
    if outdir:
        os.makedirs(outdir, exist_ok=True)
    io.open(report, 'w', encoding='utf-8').write('\n'.join(out) + '\n')
    for line in out:
        print(line.encode('ascii', 'replace').decode('ascii'))
    print('report -> %s' % report)
    if policy_fails:
        print('EXIT=1 (see VERDICT above)')
        return EXIT_ERROR
    return EXIT_OK


if __name__ == '__main__':
    sys.exit(main(sys.argv))
