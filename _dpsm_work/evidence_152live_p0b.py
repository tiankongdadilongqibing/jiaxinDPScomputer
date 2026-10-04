# -*- coding: utf-8 -*-
# Same P0 cross-tab on the 1.5.1 export, plus attacker identification of the residual population.
import io, json, sys, collections
path = sys.argv[1]
d = json.load(io.open(path, encoding='utf-8'))
dmg = [e for e in (d.get('events') or []) if e.get('calc')]
def r4(x):
    try: return round(float(x), 4)
    except Exception: return None
print('FILE %s  version=%s' % (path.split('\\')[-1], d.get('version')))
xt = collections.defaultdict(collections.Counter)
for e in dmg:
    c = e.get('calc') or {}
    xt[r4(c.get('residual'))][c.get('maxAbsorbed')] += 1
for res, cnt in sorted(xt.items(), key=lambda kv: -sum(kv[1].values()))[:8]:
    print('  r=%-8s n=%-5d  %s' % (res, sum(cnt.values()), sorted((k, v) for k, v in cnt.items() if k is not None)))
print('  attackers of r in (1.323,1.322): %s' % collections.Counter(
    (e.get('attacker'), e.get('victim')) for e in dmg
    if r4((e.get('calc') or {}).get('residual')) in (1.323, 1.322)).most_common(6))
print('  maxAbsorbed dist overall: %s' % sorted(
    collections.Counter((e.get('calc') or {}).get('maxAbsorbed') for e in dmg).items()))
