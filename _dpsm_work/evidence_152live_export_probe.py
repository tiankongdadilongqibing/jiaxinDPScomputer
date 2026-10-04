# -*- coding: utf-8 -*-
# 1.5.2 real-machine export probe: P2 counting gap, hitValue presence, residual structure.
import io, json, sys, collections

path = sys.argv[1]
d = json.load(io.open(path, encoding='utf-8'))
out = []
def p(s):
    out.append(s)

p('top-level keys: %s' % sorted(d.keys()))
ev = d.get('events') or []
p('events total           : %d' % len(ev))
dmgev = [e for e in ev if isinstance(e, dict) and e.get('calc')]
p('events with calc       : %d' % len(dmgev))
p('factCoverage           : %s' % json.dumps(d.get('factCoverage'), ensure_ascii=False))
hd = d.get('hitDetail') or {}
p('hitDetail              : %s' % json.dumps(hd, ensure_ascii=False))
p('facts.classes          : %s' % (d.get('facts') or {}).get('classes'))

# key inventory on damage events
keys = collections.Counter()
for e in dmgev:
    keys.update(e.keys())
p('')
p('event key -> count (damage events, n=%d):' % len(dmgev))
for k, c in sorted(keys.items()):
    p('   %-28s %d' % (k, c))

# hitValue presence per the HANDOFF 9.3 criterion
has_hv = sum(1 for e in dmgev if e.get('hitValue') is not None)
p('')
p('events with hitValue   : %d / %d' % (has_hv, len(dmgev)))

# sample event
if dmgev:
    p('')
    p('sample damage event (json):')
    p(json.dumps(dmgev[0], ensure_ascii=False, indent=1)[:2500])

io.open(sys.argv[2], 'w', encoding='utf-8').write('\n'.join(out))
print('ok')
