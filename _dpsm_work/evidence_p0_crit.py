# -*- coding: utf-8 -*-
import io, json, sys, collections
def load(p): return json.load(io.open(p, encoding='utf-8'))
def dmg(d): return [e for e in (d.get('events') or []) if e.get('calc')]
def vstat(e):
    v=(e.get('comp4') or '').replace('受击方状态:','').split('自身状态')[0]
    return [x.strip() for x in v.split('、') if x.strip()]
d=load(sys.argv[1]); ev=dmg(d)
print('residual vs critDamageRate (is the 1.9 group just CRITS?)')
G=collections.defaultdict(lambda: collections.Counter())
for e in ev:
    c=e.get('calc') or {}
    G[c.get('critDamageRate')][round(float(c.get('residual') or 0),3)] += 1
for k in sorted(G, key=lambda x: -sum(G[x].values())):
    print('  critDamageRate=%-6s n=%-6d top: %s' % (k, sum(G[k].values()), G[k].most_common(4)))
print()
print('restricting to hits whose model already matches (residual<=1.05): crit rate seen?')
cc=collections.Counter(); ccr=collections.Counter()
for e in ev:
    c=e.get('calc') or {}
    r=float(c.get('residual') or 0)
    if r<=1.05: cc[c.get('critDamageRate')]+=1
print('  critDamageRate among explained hits: %s' % cc.most_common(8))
print()
# --- corrected P0: modeled exponent vs residual exponent ---
def exp15(x):
    import math
    if x<=0: return None
    v=math.log(x)/math.log(1.15)
    return round(v,2)
H=collections.defaultdict(lambda: collections.Counter())
for e in ev:
    c=e.get('calc') or {}
    r=float(c.get('residual') or 0)
    vs=vstat(e)
    mod=0.0
    for s in (c.get('fold') or []):
        if s.get('side')=='vic': mod += math.log(s.get('factor') or 1)/math.log(1.15)
    if abs(mod-round(mod))<0.02 and r>1.0:
        d_=exp15(r)
        H[(int(round(mod)), len(c.get('cancel') or []), c.get('maxAbsorbed'))][d_]+=1
print('modeled 1.15-exponent / nCancel / maxAbsorbed -> residual exponent (1.15^d)')
for k in sorted(H, key=lambda x:-sum(H[x].values()))[:12]:
    print('  mod15=%-3s nCancel=%-3s maxAbs=%-3s -> %s' % (k[0],k[1],k[2], H[k].most_common(3)))
