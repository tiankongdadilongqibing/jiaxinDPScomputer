# -*- coding: utf-8 -*-
import io, json, sys, collections
def load(p): return json.load(io.open(p, encoding='utf-8'))
def dmg(d): return [e for e in (d.get('events') or []) if e.get('calc')]
d = load(sys.argv[1]); ev = dmg(d)
keys = collections.Counter()
for e in ev:
    for k in (e.get('calc') or {}): keys[k] += 1
print('calc field coverage (top): %s' % keys.most_common(40))
ncan = sum(1 for e in ev if (e.get('calc') or {}).get('cancel'))
print()
print('hits with calc.cancel = %d / %d' % (ncan, len(ev)))
allk = collections.Counter()
for e in ev:
    for s in ((e.get('calc') or {}).get('cancel') or []): allk[json.dumps(s, ensure_ascii=False)[:110]] += 1
print('top cancel entries:')
for k, v in allk.most_common(8): print('   %7d  %s' % (v, k))
print()
print('sample fold step + cancel on an unexplained hit:')
for e in ev:
    c = e.get('calc') or {}
    if c.get('cancel') and float(c.get('residual') or 0) > 1.1:
        print('  attacker=%s residual=%s victim=%s' % (e.get('attacker'), c.get('residual'), json.dumps(c.get('victimStatuses'), ensure_ascii=False)))
        print('  fold  : %s' % json.dumps(c.get('fold'), ensure_ascii=False))
        print('  cancel: %s' % json.dumps(c.get('cancel'), ensure_ascii=False))
        print('  givenTalents=%s givenApplied=%s givenTalentText=%s' % (c.get('givenTalents'), c.get('givenApplied'), c.get('givenTalentText')))
        break
