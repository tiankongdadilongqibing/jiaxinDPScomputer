# -*- coding: utf-8 -*-
import io, json, sys, collections
def load(p): return json.load(io.open(p, encoding='utf-8'))
def dmg(d): return [e for e in (d.get('events') or []) if e.get('calc')]
d = load(sys.argv[1]); ev = dmg(d)
print('events=%d' % len(ev))
# 1. what keys does a calc carry?
c0 = ev[0].get('calc') or {}
print('calc keys: %s' % sorted(c0.keys()))
print()
print('fold/foldCancels structure on a hit that HAS them:')
shown = 0
for e in ev:
    c = e.get('calc') or {}
    if c.get('fold') and c.get('foldCancels'):
        print('  --- attacker=%s residual=%s victimStatuses=%s foldMaxAbsorbed=%s' % (
            e.get('attacker'), c.get('residual'), json.dumps(c.get('victimStatuses'), ensure_ascii=False), c.get('foldMaxAbsorbed')))
        print('  fold:        %s' % json.dumps(c.get('fold'), ensure_ascii=False))
        print('  foldCancels: %s' % json.dumps(c.get('foldCancels'), ensure_ascii=False))
        shown += 1
        if shown >= 2: break
print()
# 2. how common are these arrays?
nf = sum(1 for e in ev if (e.get('calc') or {}).get('fold'))
nc = sum(1 for e in ev if (e.get('calc') or {}).get('foldCancels'))
print('hits with fold=%d/%d   foldCancels=%d/%d' % (nf, len(ev), nc, len(ev)))
cc = collections.Counter()
for e in ev:
    c = e.get('calc') or {}
    for s in (c.get('foldCancels') or []):
        cc[json.dumps(s, ensure_ascii=False)[:120]] += 1
print('top foldCancels entries:')
for k, v in cc.most_common(6): print('   %6d  %s' % (v, k))
