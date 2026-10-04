# -*- coding: utf-8 -*-
"""Per-rule fold-mass census for two battles + the differing units' ability lists."""
import io, json, os, sys, re, math, collections
HERE = os.path.dirname(os.path.abspath(__file__))
EX = os.path.join(HERE, '..', 'BepInEx', 'plugins', 'DpsMeter', 'exports')
NAME = re.compile(r'\[([^\]]+)\]')
def load(name):
    return json.load(io.open(os.path.join(EX, name), encoding='utf-8'))
def census(d):
    ev = [e for e in (d.get('events') or []) if e.get('type') == 'dmg' and e.get('atkTeam') == 1]
    ab_by_name = collections.defaultdict(set)
    for a in (d.get('actors') or []):
        for ab in (a.get('abilities') or []):
            if ab.get('name'): ab_by_name[ab['name']].add(a.get('name'))
        for t in (a.get('talents') or []):
            if t.get('ability'): ab_by_name[t['ability']].add(a.get('name'))
    mass = collections.Counter(); cnt = collections.Counter(); prov = collections.Counter()
    msum = 0.0
    for e in ev:
        c = e.get('calc') or {}
        M = 1.0
        for f in (c.get('fold') or []):
            try: fac = float(f.get('factor') or 1.0)
            except: continue
            if fac <= 0: continue
            M *= fac
            lm = abs(math.log(fac))
            kind = f.get('kind'); lab = f.get('label') or ''; m = NAME.search(lab)
            rn = m.group(1) if m else lab[:26]
            key = '%s|%s|%s' % (kind, rn, f.get('factor'))
            mass[key] += lm; cnt[key] += 1
            if kind == 'text' or kind == 'talent': prov[e.get('attacker')] += lm
            elif kind == 'global':
                hs = ab_by_name.get(rn, set())
                prov[next(iter(hs)) if len(hs) == 1 else '(global?)'] += lm
            else: prov[f.get('byUnit') or '(' + kind + ':no-byUnit)'] += lm
        if M > 0: msum += math.log(M)
    return mass, cnt, prov, len(ev), msum
L = []
def w(s=''): L.append(str(s))
dA = load('battle_411001_20261003_231044.json'); dB = load('battle_411001_20261003_231340.json')
mA, cA, pA, hA, sA = census(dA); mB, cB, pB, hB, sB = census(dB)
w('hits A=%d B=%d ; sum log(M) A=%.1f B=%.1f' % (hA, hB, sA, sB))
w('')
w('%-58s %10s %8s %10s %8s' % ('rule|kind|factor', 'A mass', 'A n', 'B mass', 'B n'))
allkeys = set(mA) | set(mB)
for k in sorted(allkeys, key=lambda x: -(mA.get(x, 0) + mB.get(x, 0)))[:26]:
    w('%-58s %10.0f %8d %10.0f %8d' % (k[:58], mA.get(k, 0), cA.get(k, 0), mB.get(k, 0), cB.get(k, 0)))
w('')
w('provider mass (log-share):')
for nm in sorted(set(pA) | set(pB), key=lambda x: -(pA.get(x, 0) + pB.get(x, 0)))[:18]:
    w('   %-28s A=%-12.0f B=%-12.0f diff=%+.0f' % (str(nm)[:28], pA.get(nm, 0), pB.get(nm, 0), pB.get(nm, 0) - pA.get(nm, 0)))
w('')
w('units only in B (abilities/talents):')
for a in (dB.get('actors') or []):
    if a.get('name') in ('メルティエル', '火砲'):
        w('  [%s] team=%s dealt=%s hits=%s' % (a.get('name'), a.get('team'), a.get('dealt'), a.get('hit')))
        for ab in (a.get('abilities') or []):
            w('     slot=%-10s id=%-7s %s' % (ab.get('slotName'), ab.get('id'), ab.get('name')))
        for t in (a.get('talents') or [])[:14]:
            w('     talent %-22s type=%-5s p=%s cond=%s' % (str(t.get('ability'))[:22], t.get('type'), t.get('p'), str(t.get('cond'))[:40]))
w('')
w('units only in A:')
for a in (dA.get('actors') or []):
    if a.get('name') in ('ルゥ=ルルサ', 'メルティエル', '火砲'):
        w('  [%s] team=%s dealt=%s hits=%s' % (a.get('name'), a.get('team'), a.get('dealt'), a.get('hit')))
        for ab in (a.get('abilities') or []):
            w('     slot=%-10s id=%-7s %s' % (ab.get('slotName'), ab.get('id'), ab.get('name')))
rep = os.path.join(HERE, 'rules_census.txt')
io.open(rep, 'w', encoding='utf-8').write(u'\n'.join(L))
print('report -> ' + rep)
print('hits A=%d B=%d sumlogM A=%.1f B=%.1f' % (hA, hB, sA, sB))