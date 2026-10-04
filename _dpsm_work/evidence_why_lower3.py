# -*- coding: utf-8 -*-
import io, json, sys, collections, statistics as st

def load(p): return json.load(io.open(p, encoding='utf-8'))
def dmg(d): return [e for e in (d.get('events') or []) if e.get('calc')]
def med(xs):
    xs=[x for x in xs if x is not None]
    return round(st.median(xs),4) if xs else None

A=load(sys.argv[1]); B=load(sys.argv[2])
da,db=dmg(A),dmg(B)

def keyed(ev):
    m=collections.defaultdict(list)
    for e in ev:
        c=e.get('calc') or {}
        m[(e.get('attacker'),c.get('effectId'))].append(e)
    return m
ka,kb=keyed(da),keyed(db)
common=[k for k in ka if k in kb and len(ka[k])>=40 and len(kb[k])>=40]
common.sort(key=lambda k:-(len(ka[k])+len(kb[k])))

fields=['power','base','knownMult','attrMult','dealtMult','takenMult','vicGive','vicGiveApplied','vicBuffs','madnessRatio','maxAbsorbed','residual']
print('medians per (attacker,effectId); only the fields that MOVE matter')
print('%-18s %6s %5s %5s | %s' % ('attacker','eff','nA','nB','  '.join('%-11s' % f for f in fields)))
for k in common[:10]:
    ea,eb=ka[k],kb[k]
    vals=[]
    for f in fields:
        va=med([(e.get('calc') or {}).get(f) for e in ea])
        vb=med([(e.get('calc') or {}).get(f) for e in eb])
        vals.append('%s->%s' % (va,vb) if va!=vb else '%-11s' % ('%s=' % va))
    print('%-18s %6s %5d %5d | %s' % (k[0],k[1],len(ea),len(eb),'  '.join(vals[:6])))
    print('%-18s %6s %5s %5s | %s' % ('','','','','  '.join(vals[6:])))

# residual distributions
def resid(ev):
    c=collections.Counter()
    for e in ev:
        r=(e.get('calc') or {}).get('residual')
        try: c[round(float(r),4)]+=1
        except Exception: pass
    return c
print()
print('residual top10 A: %s' % resid(da).most_common(10))
print('residual top10 B: %s' % resid(db).most_common(10))
print('unexplained (residual>1.10) A=%d B=%d' % (sum(v for k,v in resid(da).items() if k and k>1.10),
                                                sum(v for k,v in resid(db).items() if k and k>1.10)))
