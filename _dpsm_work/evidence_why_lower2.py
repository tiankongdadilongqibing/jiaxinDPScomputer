# -*- coding: utf-8 -*-
# Where does the damage go? Decompose per attacker, then per (attacker, skill) compare
# attack power vs the multiplier product, to separate attacker-side buffs from victim-side debuffs.
import io, json, sys, collections, statistics as st

def load(p):
    return json.load(io.open(p, encoding='utf-8'))

def dmg_events(d):
    return [e for e in (d.get('events') or []) if e.get('calc')]

def med(xs):
    xs = [x for x in xs if x is not None]
    return round(st.median(xs), 3) if xs else None

A = load(sys.argv[1]); B = load(sys.argv[2])
print('A = %s  dealt=%s' % (sys.argv[1].split('\\')[-1], (A.get('totals') or {}).get('dealt')))
print('B = %s  dealt=%s' % (sys.argv[2].split('\\')[-1], (B.get('totals') or {}).get('dealt')))

da, db = dmg_events(A), dmg_events(B)
by = lambda ev, k: collections.Counter(e.get(k) for e in ev)

# ---- per actor ----
ha = collections.Counter(e.get('attacker') for e in da)
hb = collections.Counter(e.get('attacker') for e in db)
vda = collections.Counter()
vdb = collections.Counter()
for e in da: vda[e.get('attacker')] += e.get('amount') or 0
for e in db: vdb[e.get('attacker')] += e.get('amount') or 0
print()
print('%-22s %6s %6s %12s %12s %10s %10s' % ('attacker', 'nA', 'nB', 'sumA', 'sumB', 'avgA', 'avgB'))
for name in sorted(set(ha) | set(hb), key=lambda n: -(vda[n] + vdb[n])):
    nA, nB = ha[name], hb[name]
    aA = round(vda[name] / nA) if nA else 0
    aB = round(vdb[name] / nB) if nB else 0
    print('%-22s %6d %6d %12d %12d %10d %10d' % (name, nA, nB, vda[name], vdb[name], aA, aB))

# ---- per (attacker, skill) ----
def keyed(ev):
    m = collections.defaultdict(list)
    for e in ev:
        c = e.get('calc') or {}
        m[(e.get('attacker'), c.get('effectId'), c.get('hitType'))].append(e)
    return m
ka, kb = keyed(da), keyed(db)
common = [k for k in ka if k in kb and len(ka[k]) >= 15 and len(kb[k]) >= 15]
common.sort(key=lambda k: -(len(ka[k]) + len(kb[k])))
print()
print('same (attacker, effectId, hitType) present in both, n>=15 each:')
print('%-20s %7s %6s %6s %9s %9s %9s %9s %9s' % ('attacker', 'eff', 'nA', 'nB', 'powerA', 'powerB', 'th/pw A', 'th/pw B', 'amtA/amtB'))
for k in common[:18]:
    ea, eb = ka[k], kb[k]
    pa = med([(e.get('calc') or {}).get('power') for e in ea])
    pb = med([(e.get('calc') or {}).get('power') for e in eb])
    ta = med([((e.get('calc') or {}).get('theory') or 0) / ((e.get('calc') or {}).get('power') or 1) for e in ea])
    tb = med([((e.get('calc') or {}).get('theory') or 0) / ((e.get('calc') or {}).get('power') or 1) for e in eb])
    aa = med([e.get('amount') for e in ea]); ab = med([e.get('amount') for e in eb])
    print('%-20s %7s %6d %6d %9s %9s %9s %9s %9s' % (k[0], k[1], len(ea), len(eb), pa, pb, ta, tb, ('%s/%s' % (aa, ab))))
