# -*- coding: utf-8 -*-
# Where did the 1.5.1 battle's ~166 counted-but-missing damage events go?
# Look for time gaps / type mix / duplicate ranges in the surviving event stream.
import io, json, sys, collections

for path in sys.argv[1:]:
    d = json.load(io.open(path, encoding='utf-8'))
    ev = d.get('events') or []
    dmg = [e for e in ev if e.get('calc')]
    ts = sorted(e.get('t', 0) for e in ev)
    print('=' * 90)
    print('FILE %s' % path.split('\\')[-1])
    print('  version=%s duration=%s started=%s result=%s run=%s'
          % (d.get('version'), d.get('duration'), d.get('started'), d.get('result'), json.dumps(d.get('run'))))
    print('  events=%d dmg=%d' % (len(ev), len(dmg)))
    print('  type mix: %s' % collections.Counter(e.get('type') for e in ev).most_common())
    print('  t range: min=%.3f max=%.3f' % (ts[0], ts[-1]))
    # biggest time gaps between consecutive events
    gaps = []
    for i in range(1, len(ts)):
        gaps.append((ts[i] - ts[i-1], ts[i-1], ts[i]))
    gaps.sort(reverse=True)
    print('  top5 event-time gaps: %s' % [('%.3f' % g[0], '%.2f->%.2f' % (g[1], g[2])) for g in gaps[:5]])
    # per-10s bucket counts
    b = collections.Counter(int(t // 10) for t in ts)
    print('  events per 10s bucket: %s' % sorted(b.items()))
    # duplicate (t, victim, amount) keys -> would reveal double counting
    dup = collections.Counter((round(e.get('t',0),3), e.get('victim'), e.get('amount')) for e in ev)
    nd = sum(1 for k, c in dup.items() if c > 1)
    print('  duplicate (t,victim,amount) keys: %d  (max multiplicity %d)' % (nd, max(dup.values()) if dup else 0))
