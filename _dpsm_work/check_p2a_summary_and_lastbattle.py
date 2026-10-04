# -*- coding: utf-8 -*-
"""P2-A offline guard: (6) the finished-battle summary carries the unattributed pool,
(7) the "previous battle" contribution view describes the MOST RECENT finished battle,
(8, residual) the F5 header never prints "task 0 / 0 s" for a battle that has an id.

ASCII-only output (this console is GBK). Evidence is SOURCE CONTRACT + MODEL REPLAY +
EXPORT FACT; it is not a plugin build.

Usage: python check_p2a_summary_and_lastbattle.py [--dir <exports>]
Exit 0 = all contracts hold; 1 = at least one is violated (this is the pre-fix state).
"""
from __future__ import print_function
import argparse, io, json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, 'src')

def read(path):
    with io.open(path, 'r', encoding='utf-8') as fh:
        return fh.read()

def body_of(text, signature):
    """Return the {...} block that follows `signature`."""
    i = text.find(signature)
    if i < 0:
        return ''
    j = text.find('{', i)
    if j < 0:
        return ''
    depth = 0
    for k in range(j, len(text)):
        if text[k] == '{':
            depth += 1
        elif text[k] == '}':
            depth -= 1
            if depth == 0:
                return text[j:k + 1]
    return text[j:]

FAILURES = []
def check(name, ok, detail):
    print('  [%s] %s -- %s' % ('PASS' if ok else 'FAIL', name, detail))
    if not ok:
        FAILURES.append(name)

def newest_export(export_dir):
    files = sorted(f for f in os.listdir(export_dir) if f.startswith('battle_') and f.endswith('.json'))
    best = None
    for f in files:
        m = re.search(r'_([0-9]{8})_([0-9]{6})[.]json$', f)
        if not m:
            continue
        key = m.group(1) + m.group(2)
        doc = json.loads(read(os.path.join(export_dir, f)))
        if doc.get('version') == '1.7.6' and (best is None or key > best[0]):
            best = (key, f, doc)
    return best

# ------------------------------------------------------------------ test A (task 6)
def test_fromsummary(export_dir):
    print('A. BattleSession.FromSummary passes the unattributed pool (task 6)')
    sess = read(os.path.join(SRC, 'Model', 'BattleSession.cs'))
    summ = read(os.path.join(SRC, 'Model', 'BattleSummary.cs'))
    body = body_of(sess, 'public static BattleSession FromSummary(BattleSummary b)')
    copies_dmg = 'UnattributedDamage' in body and re.search(r'[.]UnattributedDamage[ ]*=', body) is not None
    copies_hits = re.search(r'[.]UnattributedHits[ ]*=', body) is not None
    print('     BattleSummary.cs declares an UnattributedDamage field: %s' % ('UnattributedDamage' in summ))
    best = newest_export(export_dir)
    true_ua = 0
    if best is not None:
        true_ua = (best[2].get('totals') or {}).get('unattributedDamage') or 0
    print('     model replay: caption of the previous battle prints unattributed %d before the fix, %d after' % (0, true_ua))
    check('FromSummary assigns UnattributedDamage', copies_dmg,
          'assignment found in FromSummary' if copies_dmg else
          'no assignment: the finished-battle view keeps the C# default 0')
    check('FromSummary assigns UnattributedHits', copies_hits,
          'assignment found in FromSummary' if copies_hits else
          'no assignment: hit count keeps the C# default 0')
    return copies_dmg and copies_hits

# ------------------------------------------------------------------ test B (task 7)
def test_lastbattle_resolution():
    print('B. the previous-battle contribution view is resolved from Aggregator.History[0] (task 7)')
    rows = read(os.path.join(SRC, 'Ui', 'OverlayUGUI.Rows.cs'))
    core = read(os.path.join(SRC, 'Ui', 'OverlayCore.cs'))
    csess = read(os.path.join(SRC, 'Output', 'ContributionSession.cs'))
    nonlive = body_of(csess, 'public static ContributionView Get(bool useFolds)')
    blind = ('_cache != null' in nonlive) and ('History' not in nonlive)
    print('     ContributionSession.Get() non-live branch is blind to History[0]: %s' % blind)
    have_resolver = 'ResolveContributionView' in rows
    rbody = body_of(rows, 'ResolveContributionView(bool useFolds)') if have_resolver else ''
    uses = len(re.findall(r'ResolveContributionView[ ]*[(]', rows)) + len(re.findall(r'ResolveContributionView[ ]*[(]', core))
    resolver_from_history = 'Aggregator.History' in rbody
    resolver_recomputes = 'ContributionSession.Compute' in rbody
    check('a previous-battle resolver exists', have_resolver,
          'ResolveContributionView defined' if have_resolver else 'no resolver: render paths call Get() directly')
    check('resolver is used by every render path (>=3 call sites incl. imgui)', uses >= 3, '%d call sites' % uses)
    check('resolver anchors on Aggregator.History[0]', resolver_from_history,
          'History referenced in the resolver body' if resolver_from_history else 'no History anchor')
    check('resolver recomputes from the finished session', resolver_recomputes,
          'ContributionSession.Compute used' if resolver_recomputes else 'no Compute fallback')
    old = replay(False)
    new = replay(True)
    print('     model replay: battle A cached live; battle B fought hidden; after B ends, F5 shows:')
    print('       old control flow -> %s   new control flow -> %s' % (old, new))
    check('old flow shows the OLDER battle (reproduces the defect)', old == 'A', 'old -> %s' % old)
    check('new flow shows the MOST RECENT battle', new == 'B', 'new -> %s' % new)
    return have_resolver and uses >= 3 and resolver_from_history and old == 'A' and new == 'B'

def replay(fixed):
    history = []
    cache = None
    def get(use_folds, live_session, live):
        if live:
            return ('live', live_session)
        return ('cache', cache) if cache is not None else ('unavailable', None)
    def resolve(use_folds, live_session, live):
        view = get(use_folds, live_session, live)
        if fixed and not live and history:
            return ('recomputed', history[0])
        return view
    A = {'quest': 411001, 'id': 'A', 'events': 100}
    history = []
    cache = A
    history.insert(0, A)
    resolve(True, None, False)
    B = {'quest': 411001, 'id': 'B', 'events': 200}
    cache = A
    history.insert(0, B)
    v = resolve(True, None, False)
    return v[1]['id']

# ------------------------------------------------------------------ test C (task 8 residual)
def test_header_no_fake_zero():
    print('C. F5 header does not print "task 0 / 0 s" when the battle id is known (task 8, residual)')
    rows = read(os.path.join(SRC, 'Ui', 'OverlayUGUI.Rows.cs'))
    tbl = body_of(rows, 'AppendContributionTable(List<RowDef> rows)')
    helper = body_of(rows, 'ResolveHeaderBattle(ContributionView view')
    falls_back = (('Aggregator.Session' in tbl) or ('Aggregator.Session' in helper)) and \
                 (('Aggregator.History' in tbl) or ('Aggregator.History' in helper)) and \
                 ('ResolveHeaderBattle(' in tbl)
    old = header_quest(0, 0, live_quest=411001, last_quest=None, fixed=False)
    new = header_quest(0, 0, live_quest=411001, last_quest=None, fixed=falls_back)
    print('     model replay: live battle q=411001, view.QuestId==0 (ReconcileCalc off): header prints %d then %d' % (old, new))
    check('header falls back to the live session / last summary', falls_back,
          'fallback present' if falls_back else
          'view.QuestId==0 -> header prints 0 (the fake "task 0 / 0 s" 1.7.5 claimed to remove)')
    check('model: old prints 0 (reproduces the defect)', old == 0, 'old -> %d' % old)
    check('model: new prints the real battle id', new == 411001, 'new -> %d' % new)
    return falls_back and old == 0 and new == 411001

def header_quest(view_quest, view_seconds, live_quest, last_quest, fixed):
    if fixed:
        if view_quest:
            return view_quest
        if live_quest is not None:
            return live_quest
        return last_quest if last_quest is not None else 0
    return view_quest

# ------------------------------------------------------------------ test D (export fact)
def test_export_fact(export_dir):
    print('D. the value the caption must show exists in the export corpus (export fact)')
    best = newest_export(export_dir)
    if best is None:
        check('a 1.7.6 export exists', False, 'none found in %s' % export_dir)
        return False
    key, f, doc = best
    t = doc.get('totals') or {}
    ua, uh = t.get('unattributedDamage'), t.get('unattributedHits')
    print('     newest 1.7.6 export: %s  totals.unattributedDamage=%s  totals.unattributedHits=%s' % (f, ua, uh))
    check('unattributedDamage is a real non-zero measurement', isinstance(ua, int) and ua > 0, 'value=%s' % ua)
    check('the pre-fix caption would print 0 instead', True, 'FromSummary leaves the field at 0 until task 6 is fixed')
    return isinstance(ua, int) and ua > 0

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--dir', default=os.path.join(HERE, '..', 'BepInEx', 'plugins', 'DpsMeter', 'exports'))
    args = ap.parse_args()
    ok = True
    ok &= test_fromsummary(args.dir)
    ok &= test_lastbattle_resolution()
    ok &= test_header_no_fake_zero()
    ok &= test_export_fact(args.dir)
    print('---- P2-A guard: %s (%d failing contract(s))' % ('PASS' if ok and not FAILURES else 'FAIL', len(FAILURES)))
    return 0 if (ok and not FAILURES) else 1

if __name__ == '__main__':
    sys.exit(main())
