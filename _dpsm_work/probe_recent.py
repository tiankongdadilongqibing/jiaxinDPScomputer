# -*- coding: utf-8 -*-
"""Summary of recent battles + 1.5.4 channel verification. ASCII stdout; UTF-8 report file."""
import io, json, os, sys, glob, collections
EX = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'BepInEx', 'plugins', 'DpsMeter', 'exports')
files = sorted(glob.glob(os.path.join(EX, 'battle_*.json')), key=os.path.getmtime)
new = [f for f in files if os.path.basename(f) >= 'battle_411001_20261003_2300' or '2310' in f or '2313' in f]
L = []
def w(s=''): L.append(str(s))
w('RECENT EXPORTS')
for f in files:
    b = os.path.basename(f)
    flag = 'NEW154' if (('230040' in b) or ('230311' in b) or ('230615' in b) or ('231044' in b) or ('231340' in b)) else ''
    w('  %-42s %8.1fMB %s' % (b, os.path.getsize(f)/1048576.0, flag))
w('')
for f in files:
    b = os.path.basename(f)
    try: d = json.load(io.open(f, encoding='utf-8'))
    except Exception as ex: w('%s: LOAD FAIL %s' % (b, ex)); continue
    if d.get('quest') != 411001: continue
    ev = d.get('events') or []
    a1 = [a for a in (d.get('actors') or []) if a.get('team') == 1 and (a.get('dealt') or 0) > 0]
    a1.sort(key=lambda x: -(x.get('dealt') or 0))
    tot = d.get('totals') or {}
    w('=' * 96)
    w('%s  ver=%s  dur=%.1fs  result=%s  dealt=%s' % (b, d.get('version'), d.get('duration', 0), d.get('result'), tot.get('dealt')))
    w('  team1(有输出): ' + ' | '.join('%s=%.1fM' % (a.get('name'), (a.get('dealt') or 0)/1e6) for a in a1))
    ma = d.get('madnessApplies') or {}
    if ma:
        w('  [1.5.4] madnessApplies: hook=%s recorded=%s nullOwn=%s nullGuest=%s rows=%s dropped=%s' % (ma.get('hookCalls'), ma.get('recorded'), ma.get('nullOwner'), ma.get('nullGuest'), len(ma.get('rows') or []), ma.get('rowsDropped')))
    ra = d.get('rosterAudit') or {}
    if ra.get('giverResolved') is not None:
        w('  [1.5.4] giver: resolved=%s null=%s errors=%s viaAction=%s viaTalent=%s' % (ra.get('giverResolved'), ra.get('giverNull'), ra.get('giverErrors'), ra.get('giverViaAction'), ra.get('giverViaTalent')))
    tl = d.get('timeline') or {}
    if tl: w('  timeline: statusChanges=%s resistChanges=%s' % (tl.get('statusChanges'), tl.get('resistChanges')))
    rec = d.get('reconcile') or {}
    w('  reconcile: exact=%s exactWithCrit=%s unexplained=%s' % (rec.get('exact'), rec.get('exactWithCrit'), rec.get('unexplained')))
    # byUnit coverage over folds
    cnt = collections.Counter()
    for e in ev:
        c = e.get('calc') or {}
        for fo in (c.get('fold') or []):
            k = fo.get('kind')
            if k in ('given', 'madness'):
                cnt[k + ('_with' if fo.get('byUnit') else '_without')] += 1
    if cnt: w('  byUnit: %s' % dict(cnt))
    # application rows sample
    rows = ma.get('rows') or []
    if rows:
        w('  applies sample: ' + ' ; '.join('%s->%s@%.1f' % (r.get('owner'), r.get('guest'), r.get('t') or 0) for r in rows[:6]))
    w('  team names: ' + ', '.join(a.get('name') for a in a1))
rep = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'probe_recent.txt')
io.open(rep, 'w', encoding='utf-8').write(u'\n'.join(L))
print('report -> ' + rep)
print('files=%d real-battles=%d' % (len(files), sum(1 for f in files for _ in [0] if True)))