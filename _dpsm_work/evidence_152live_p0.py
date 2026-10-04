# -*- coding: utf-8 -*-
# P0: cross-tab residual vs maxAbsorbed (fold depth) on the 1.5.2 real-machine export.
import io, json, sys, collections
path = sys.argv[1]
d = json.load(io.open(path, encoding='utf-8'))
dmg = [e for e in (d.get('events') or []) if e.get('calc')]
def r4(x):
    try: return round(float(x), 4)
    except Exception: return None

xt = collections.defaultdict(collections.Counter)
for e in dmg:
    c = e.get('calc') or {}
    xt[r4(c.get('residual'))][c.get('maxAbsorbed')] += 1
print('residual -> {maxAbsorbed: hits}  (top residuals by count)')
for res, cnt in sorted(xt.items(), key=lambda kv: -sum(kv[1].values()))[:10]:
    tot = sum(cnt.values())
    print('  r=%-8s n=%-5d  %s' % (res, tot, sorted((k, v) for k, v in cnt.items() if k is not None)))

print()
print('victims of r=1.3225 : %s' % collections.Counter(
    e.get('victim') for e in dmg if r4((e.get('calc') or {}).get('residual')) == 1.3225).most_common(6))
print('victims of r=1.323  : %s' % collections.Counter(
    e.get('victim') for e in dmg if r4((e.get('calc') or {}).get('residual')) == 1.323).most_common(6))
print('victims overall     : %s' % collections.Counter(e.get('victim') for e in dmg).most_common(6))
print()
print('maxAbsorbed -> residual mix (top rows)')
mb = collections.defaultdict(collections.Counter)
for e in dmg:
    c = e.get('calc') or {}
    mb[c.get('maxAbsorbed')][r4(c.get('residual'))] += 1
for k in sorted([k for k in mb if k is not None]):
    p = mb[k]
    print('  maxAbsorbed=%-3s n=%-5d top residuals: %s' % (k, sum(p.values()), p.most_common(6)))
