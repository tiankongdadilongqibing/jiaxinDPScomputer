# -*- coding: utf-8 -*-
# Offline test of the P0 hypothesis: do the CANCELLED granted copies actually stack in the game?
# Uses only data already present in the 1.5.2 export (calc.fold / calc.cancel / calc.maxAbsorbed).
import io, json, sys, collections
def load(p): return json.load(io.open(p, encoding='utf-8'))
def dmg(d): return [e for e in (d.get('events') or []) if e.get('calc')]
def vstat(e):
    v=(e.get('comp4') or '').replace('受击方状态:','').split('自身状态')[0]
    return [x for x in v.split('、') if x]

for path in sys.argv[1:]:
    d=load(path); ev=dmg(d); name=path.split('\\')[-1]
    print('=' * 100)
    print('%s' % name)
    G=collections.defaultdict(lambda: collections.Counter())
    ex = {}
    for e in ev:
        c=e.get('calc') or {}
        vs=vstat(e)
        k_po = sum(1 for s in vs if s in ('毒','火傷'))
        glob = tuple(sorted((s.get('factor') for s in (c.get('fold') or []) if s.get('side')=='vic'), reverse=True))
        can = (c.get('cancel') or [])
        n15 = sum(1 for s in can if '/1006/-15' in (s.get('origin') or ''))
        key=(k_po, n15, len(can), c.get('maxAbsorbed'), glob)
        G[key][round(float(c.get('residual') or 0),4)] += 1
        ex.setdefault(key, e)
    print('  k(毒/火傷) n15 nCancel maxAbsorbed  vicGlobalFactors        -> residual distribution (top3)')
    for k in sorted(G, key=lambda x: -sum(G[x].values()))[:14]:
        dist = G[k].most_common(3)
        print('  %-9s %-4s %-7s %-10s  %-22s -> %s' % (k[0], k[1], k[2], k[3], str(k[4]), dist))
    print()
    print('  --- 3 concrete hits with residual ~1.3225 ---')
    n=0
    for e in ev:
        c=e.get('calc') or {}
        r=round(float(c.get('residual') or 0),4)
        if 1.30 < r < 1.34:
            print('   attacker=%s residual=%s statuses=%s maxAbsorbed=%s responsibility=%s'
                  % (e.get('attacker'), c.get('residual'), vstat(e), c.get('maxAbsorbed'), c.get('responsibility')))
            print('      vic globals applied: %s' % [(s.get('factor'), s.get('label','')[:34]) for s in (c.get('fold') or []) if s.get('side')=='vic'])
            print('      cancels: %s' % [(s.get('origin'), s.get('value'), s.get('by')) for s in (c.get('cancel') or [])])
            n+=1
            if n>=3: break
