# -*- coding: utf-8 -*-
import io, json, sys, collections
def load(p): return json.load(io.open(p, encoding='utf-8'))
def dmg(d): return [e for e in (d.get('events') or []) if e.get('calc')]
def vset(e):
    v=(e.get('comp4') or '').replace('受击方状态:','').split('自身状态')[0]
    return tuple(sorted(x for x in v.split('、') if x and x != '狂気'))
def amad(e): return bool((e.get('calc') or {}).get('madnessOn'))

for path in sys.argv[1:]:
    d=load(path); ev=dmg(d); name=path.split('\\')[-1]
    print('=' * 92)
    print('%s' % name)
    # ---- victim madness, controlled by (attacker, skill, enemy-status-set) ----
    G=collections.defaultdict(lambda:[0,0,0,0,0,0])  # n,amt,th for (notmad,mad)
    for e in ev:
        c=e.get('calc') or {}
        k=(e.get('attacker'), c.get('effectId'), vset(e))
        i = 3 if ('狂気' in (e.get('comp4') or '')) else 0
        G[k][i]+=1; G[k][i+1]+= e.get('amount') or 0; G[k][i+2]+= c.get('theory') or 0
    print('  VICTIM-mad factor, controlled on (attacker, effectId, enemy status w/o 狂気):')
    tot=[0,0,0,0]
    rows=[]
    for k,v in G.items():
        if v[0]>=20 and v[3]>=20:
            r_no=v[1]/v[2] if v[2] else 0
            r_ma=v[4]/v[5] if v[5] else 0
            rows.append((v[0]+v[3], k, v[0], r_no, v[3], r_ma, r_ma/r_no if r_no else 0))
            tot[0]+=v[1]; tot[1]+=v[2]; tot[2]+=v[4]; tot[3]+=v[5]
    for _,k,n0,r0,n1,r1,f in sorted(rows, key=lambda x:-x[0])[:10]:
        print('     %-16s eff=%-6s %-16s nNo=%-5d %.4f  nMad=%-5d %.4f  x%.4f'
              % (k[0], k[1], '+'.join(k[2]) or '(none)', n0, r0, n1, r1, f))
    if tot[1] and tot[3]:
        print('     POOLED: no-mad %.4f (%d)  mad %.4f (%d)  x%.4f'
              % (tot[0]/tot[1], tot[0], tot[2]/tot[3], tot[2], (tot[2]/tot[3])/(tot[0]/tot[1])))
    # ---- attacker madness, controlled by (attacker, effectId, enemy status set incl 狂気) ----
    H=collections.defaultdict(lambda:[0,0,0,0,0,0])
    for e in ev:
        c=e.get('calc') or {}
        k=(e.get('attacker'), c.get('effectId'), (e.get('comp4') or '').replace('受击方状态:','').split('自身状态')[0])
        i = 3 if amad(e) else 0
        H[k][i]+=1; H[k][i+1]+= e.get('amount') or 0; H[k][i+2]+= c.get('theory') or 0
    print('  ATTACKER-mad residual ratio (theory ALREADY folds x2.5), controlled on (attacker, effectId, enemy status):')
    T=[0,0,0,0]; rows=[]
    for k,v in H.items():
        if v[0]>=20 and v[3]>=20:
            r0=v[1]/v[2] if v[2] else 0
            r1=v[4]/v[5] if v[5] else 0
            rows.append((v[0]+v[3],k,v[0],r0,v[3],r1))
            T[0]+=v[1]; T[1]+=v[2]; T[2]+=v[4]; T[3]+=v[5]
    for _,k,n0,r0,n1,r1 in sorted(rows,key=lambda x:-x[0])[:8]:
        print('     %-16s eff=%-6s %-18s nNo=%-5d %.4f  nMad=%-5d %.4f' % (k[0],k[1],k[2][:18],n0,r0,n1,r1))
    if T[1] and T[3]:
        print('     POOLED: no-mad %.4f  mad %.4f   (if the x2.5 fold were exact these would be equal)' % (T[0]/T[1], T[2]/T[3]))
