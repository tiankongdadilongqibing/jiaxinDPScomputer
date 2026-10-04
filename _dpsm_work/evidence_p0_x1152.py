# -*- coding: utf-8 -*-
import io, json, sys, collections
def load(p): return json.load(io.open(p, encoding='utf-8'))
def dmg(d): return [e for e in (d.get('events') or []) if e.get('calc')]
def vstat(e):
    v=(e.get('comp4') or '').replace('受击方状态:','').split('自身状态')[0]
    return [x.strip() for x in v.split('、') if x.strip()]
d=load(sys.argv[1]); ev=dmg(d)
n=0
print('OLD battle: hits whose residual is ~1.3225 (=1.15^2) -- what did the model fold?')
for e in ev:
    c=e.get('calc') or {}
    r=float(c.get('residual') or 0)
    if 1.31 < r < 1.34:
        print('-'*100)
        print('attacker=%s  residual=%s  statuses=%s  maxAbsorbed=%s responsibility=%s' % (e.get('attacker'), c.get('residual'), vstat(e), c.get('maxAbsorbed'), c.get('responsibility')))
        print('  FOLDED: %s' % [ (s.get('side'), s.get('factor'), (s.get('label') or '')[:46]) for s in (c.get('fold') or []) ])
        print('  comp2 : %s' % (e.get('comp2') or '')[:600])
        n+=1
        if n>=3: break
print()
# aggregate: how often is a "vs poisoned/burned enemy" conditional rule present but NOT folded?
cnt=collections.Counter()
for e in ev:
    s=(e.get('comp2') or '')
    for name in ['ド・マリニーの掛け時計','毒の短剣','母なる変異の飛沫','海魔の残滓']:
        if name in s:
            folded = any(name in (st.get('label') or '') for st in ((e.get('calc') or {}).get('fold') or []))
            cnt[(name, 'folded' if folded else 'parked')] += 1
print('rule name -> folded vs parked (how the model treated it in comp2):')
for k,v in sorted(cnt.items(), key=lambda x:-x[1]): print('   %-24s %-8s %6d' % (k[0], k[1], v))
