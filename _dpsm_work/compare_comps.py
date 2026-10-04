# -*- coding: utf-8 -*-
"""Compare the 1.5.4 real battles: totals, boss HP, madness uptime, per-character output."""
import io, json, os, sys, glob, collections
HERE = os.path.dirname(os.path.abspath(__file__))
EX = os.path.join(HERE, '..', 'BepInEx', 'plugins', 'DpsMeter', 'exports')
NEW = ['230040', '230311', '230615', '231044', '231340']
L = []
def w(s=''): L.append(str(s))
files = [f for f in sorted(glob.glob(os.path.join(EX, 'battle_*.json')), key=os.path.getmtime) if any(k in os.path.basename(f) for k in NEW)]
rows = []
for f in files:
    b = os.path.basename(f)
    d = json.load(io.open(f, encoding='utf-8'))
    ev = [e for e in (d.get('events') or []) if e.get('type') == 'dmg' and e.get('atkTeam') == 1]
    actors = d.get('actors') or []
    t1 = [a for a in actors if a.get('team') == 1 and (a.get('dealt') or 0) > 0]
    bosses = [a for a in actors if a.get('team') == 2 and (a.get('taken') or 0) > (a.get('dealt') or 0) * 5]
    boss = max(bosses, key=lambda a: a.get('taken') or 0) if bosses else None
    vmad = sum(1 for e in ev if (e.get('calc') or {}).get('victimMadnessOn'))
    amad = sum(1 for e in ev if (e.get('calc') or {}).get('madnessOn'))
    crits = sum(1 for e in ev if e.get('crit'))
    hp = (boss.get('hpPct') or [None])[-1] if boss else None
    tot = d.get('totals') or {}
    rec = d.get('reconcile') or {}
    dic = {'file': b, 'ver': d.get('version'), 'hits': len(ev), 'dealt': tot.get('dealt'), 'boss': boss.get('name') if boss else None,
           'bossTaken': boss.get('taken') if boss else None, 'hpEnd': hp, 'vmad': vmad, 'amad': amad, 'crits': crits,
           'exact': rec.get('exact'), 'withCalc': rec.get('withCalc'), 'unexp': rec.get('unexplained'),
           'chars': [(a.get('name'), a.get('dealt'), a.get('hit'), a.get('maxHit')) for a in sorted(t1, key=lambda x: -(x.get('dealt') or 0))],
           'skills': collections.Counter(), 'per10s': collections.Counter()}
    for e in ev:
        dic['per10s'][int(e.get('t') or 0)//10] += e.get('amount') or 0
    for a in t1:
        for k, v in (a.get('skills') or {}).items(): dic['skills'][k] += v
    rows.append(dic)
w('%-1s %-40s %-7s %-9s %-10s %-8s %-7s %-7s %-7s %s' % ('', 'file', 'hits', 'dealt(M)', 'bossTaken', 'hpEnd', 'vmad%', 'amad%', 'crit', 'exact%'))
for r in rows:
    w('%-41s %-7d %-9.1f %-10s %-8s %-6.1f%% %-6.1f%% %-7d %.1f%%' % (
        r['file'], r['hits'], (r['dealt'] or 0)/1e6, r['bossTaken'], r['hpEnd'],
        100.0*r['vmad']/max(1,r['hits']), 100.0*r['amad']/max(1,r['hits']), r['crits'],
        100.0*(r['exact'] or 0)/max(1, r['withCalc'] or 1)))
w('')
for r in rows:
    w('=' * 100)
    w('%s  ver=%s hits=%d dealt=%.1fM boss=%s bossTaken=%s hpEnd=%s vmad=%d(%.0f%%) amad=%d(%.0f%%) crit=%d' % (
        r['file'], r['ver'], r['hits'], (r['dealt'] or 0)/1e6, r['boss'], r['bossTaken'], r['hpEnd'],
        r['vmad'], 100.0*r['vmad']/max(1,r['hits']), r['amad'], 100.0*r['amad']/max(1,r['hits']), r['crits']))
    for nm, dl, h, mx in r['chars']:
        w('   %-18s dealt=%-10.1fM hits=%-6d avg=%-9.0f max=%s' % (nm, (dl or 0)/1e6, h or 0, (dl or 0)/max(1, h or 1), mx))
    w('   per10s(M): ' + ' '.join('%d:%d' % (k, v//1000000) for k, v in sorted(r['per10s'].items())))
    w('   top skills: ' + ' '.join('%s=%.1fM' % (k, v/1e6) for k, v in r['skills'].most_common(6)))
rep = os.path.join(HERE, 'compare_comps.txt')
io.open(rep, 'w', encoding='utf-8').write(u'\n'.join(L))
print('report -> ' + rep)
for r in rows: print('%-41s hits=%-6d dealt=%.1fM hpEnd=%-7s vmad=%.0f%% amad=%.0f%% crit=%d' % (r['file'][7:], r['hits'], (r['dealt'] or 0)/1e6, r['hpEnd'], 100.0*r['vmad']/max(1,r['hits']), 100.0*r['amad']/max(1,r['hits']), r['crits']))