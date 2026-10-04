# -*- coding: utf-8 -*-
import io, json, sys, collections, re
def load(p): return json.load(io.open(p, encoding='utf-8'))

def rules(d):
    """Collect every distinct 与伤害补正 rule phrase seen in comp2/comp3 of damage events."""
    c = collections.Counter(); cond = collections.Counter()
    for e in (d.get('events') or []):
        for f in ('comp2','comp3'):
            s = e.get(f) or ''
            if '补正:' not in s: continue
            seg = s.split('补正:')[-1]
            seg = seg.split(' · ')[0]
            for part in seg.split('、'):
                part = part.strip()
                if not part or part.startswith('未检出'): continue
                c[part] += 1
                if '(条件性' in part or '未计入' in part: cond[part] += 1
    return c, cond

def norm(part):
    return re.sub(r'^\[[^\]]*\]\s*', '', part).strip()

A = load(sys.argv[1]); B = load(sys.argv[2])
ca, aca = rules(A); cb, acb = rules(B)
na = collections.Counter(); nb = collections.Counter()
for k,v in ca.items(): na[norm(k)] += v
for k,v in cb.items(): nb[norm(k)] += v
print('rule phrases ONLY in OLD (%s):' % sys.argv[1].split('\\')[-1])
for k,v in na.items():
    if k not in nb: print('   %8d  %s' % (v, k[:150]))
print()
print('rule phrases ONLY in NEW (%s):' % sys.argv[2].split('\\')[-1])
for k,v in nb.items():
    if k not in na: print('   %8d  %s' % (v, k[:150]))
print()
print('top 5 in each:')
print('  OLD:', [x[0][:70] for x in na.most_common(5)])
print('  NEW:', [x[0][:70] for x in nb.most_common(5)])

# owners of the two talents
for tag, d in (('OLD', A), ('NEW', B)):
    acts = d.get('actors') or []
    print()
    print('%s actors and whether they own the two talents:' % tag)
    for i, a in enumerate(acts):
        abils = a.get('abilities') or []
        names = [x.get('name') for x in abils if isinstance(x, dict)]
        flag = []
        for want in ('海魔の残滓', '母なる変異の飛沫'):
            if want in names: flag.append(want)
        print('   [%2d] %-22s team=%s summon=%s %s' % (i, a.get('name'), a.get('team'), a.get('summon'), ('<< ' + ','.join(flag)) if flag else ''))
