# -*- coding: utf-8 -*-
import io, json, sys, collections
def load(p): return json.load(io.open(p, encoding='utf-8'))
def dmg(d): return [e for e in (d.get('events') or []) if e.get('calc')]
def vstat(e):
    s = (e.get('comp4') or '').replace('受击方状态:', '')
    s = s.split('自身状态')[0].strip()
    return s if s else '(none)'

for path in sys.argv[1:]:
    d = load(path); name = path.split('\\')[-1]
    ev = dmg(d)
    g = collections.defaultdict(lambda: [0, 0, 0])
    for e in ev:
        c = e.get('calc') or {}
        k = vstat(e)
        g[k][0] += 1; g[k][1] += e.get('amount') or 0; g[k][2] += c.get('theory') or 0
    print('=' * 96)
    print('%s   n=%d   SUM amt/theory=%.4f' % (name, len(ev), sum(x[1] for x in g.values()) / sum(x[2] for x in g.values())))
    print('  %-24s %6s %14s %14s %9s' % ('victim status set', 'n', 'SUMamount', 'SUMtheory', 'amt/th'))
    for k in sorted(g, key=lambda x: -g[x][1]):
        n, a, t = g[k]
        print('  %-24s %6d %14d %14d %9.4f' % (k, n, a, t, a / t if t else 0))
