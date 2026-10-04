# -*- coding: utf-8 -*-
"""What exactly is 海魔の残滓 in comp B, and how does 母なる変異の飛沫 compare?"""
import io, json, os, collections
HERE = os.path.dirname(os.path.abspath(__file__))
EX = os.path.join(HERE, '..', 'BepInEx', 'plugins', 'DpsMeter', 'exports')
d = json.load(io.open(os.path.join(EX, 'battle_411001_20261003_231340.json'), encoding='utf-8'))
ev = [e for e in (d.get('events') or []) if e.get('type') == 'dmg' and e.get('atkTeam') == 1]
L = []
def w(s=''): L.append(str(s))
samp = {}; fac = collections.Counter(); perhit = collections.Counter(); side = collections.Counter()
hits_with = 0
for e in ev:
    n = 0
    for f in ((e.get('calc') or {}).get('fold') or []):
        if '海魔の残滓' in (f.get('label') or '') or '海魔の残滓' in str(f.get('origin')):
            n += 1
            fac[f.get('factor')] += 1
            side[f.get('side')] += 1
            samp.setdefault(f.get('kind'), f)
    if n: hits_with += 1
    perhit[n] += 1
w('海魔の残滓 in battle 231340 (comp B):')
w('  hits carrying it: %d / %d (%.1f%%)' % (hits_with, len(ev), 100.0*hits_with/max(1,len(ev))))
w('  factors: %s' % dict(fac))
w('  sides: %s' % dict(side))
w('  folds-per-hit distribution: %s' % dict(sorted(perhit.items())))
for k, v in samp.items(): w('  sample[%s]: %s' % (k, json.dumps(v, ensure_ascii=False)[:220]))
w('')
w('victim statuses on hits where 海魔の残滓 folds (is it status-gated?):')
vs = collections.Counter(); vs_all = collections.Counter()
for e in ev:
    v = (e.get('calc') or {}).get('victimStatuses') or []
    key = ','.join(sorted(v))
    vs_all[key] += 1
    for f in ((e.get('calc') or {}).get('fold') or []):
        if '海魔の残滓' in (f.get('label') or ''):
            vs[key] += 1; break
w('  with 海魔の残滓: %s' % vs.most_common(6))
w('  all team hits : %s' % vs_all.most_common(6))
w('')
w('母なる変異の飛沫 (both comps) 與 holdership:')
for nm2 in ('battle_411001_20261003_231044.json', 'battle_411001_20261003_231340.json'):
    dd = json.load(io.open(os.path.join(EX, nm2), encoding='utf-8'))
    for a in (dd.get('actors') or []):
        for ab in (a.get('abilities') or []):
            if ab.get('name') in ('海魔の残滓', '母なる変異の飛沫'):
                w('  %s: %s holds %s (slot=%s id=%s)' % (nm2[-11:-5], a.get('name'), ab.get('name'), ab.get('slotName'), ab.get('id')))
rep = os.path.join(HERE, 'umima_check.txt')
io.open(rep, 'w', encoding='utf-8').write(u'\n'.join(L))
print('report -> ' + rep)