# -*- coding: utf-8 -*-
# Is the missing x1.5 attacker-side (狂気 on the attacker) or victim-side (狂気 on the target)?
import io, json, sys, collections
def load(p): return json.load(io.open(p, encoding='utf-8'))
def dmg(d): return [e for e in (d.get('events') or []) if e.get('calc')]
def vmad(e):
    s=(e.get('comp4') or '')
    v = s.replace('受击方状态:','').split('自身状态')[0]
    return '狂気' in v
def amad(e):
    c=e.get('calc') or {}
    return bool(c.get('madnessOn'))

for path in sys.argv[1:]:
    d=load(path); ev=dmg(d); name=path.split('\\')[-1]
    G=collections.defaultdict(lambda:[0,0,0])
    for e in ev:
        c=e.get('calc') or {}
        G[(amad(e), vmad(e))][0]+=1
        G[(amad(e), vmad(e))][1]+= e.get('amount') or 0
        G[(amad(e), vmad(e))][2]+= c.get('theory') or 0
    print('=' * 88)
    print('%s' % name)
    print('  %-14s %-14s %7s %14s %14s %9s' % ('attacker mad','victim mad','n','SUMamount','SUMtheory','amt/th'))
    for k in [(False,False),(False,True),(True,False),(True,True)]:
        n,a,t=G[k]
        print('  %-14s %-14s %7d %14d %14d %9.4f' % (k[0],k[1],n,a,t, a/t if t else 0))
    # attacker-mad ratio split
    R=collections.defaultdict(lambda:[0,0,0])
    for e in ev:
        c=e.get('calc') or {}
        R[(amad(e), c.get('madnessRatio'))][0]+=1
        R[(amad(e), c.get('madnessRatio'))][1]+= e.get('amount') or 0
        R[(amad(e), c.get('madnessRatio'))][2]+= c.get('theory') or 0
    print('  by (attacker-mad, madnessRatio):')
    for k in sorted(R, key=lambda x:-R[x][0]):
        n,a,t=R[k]
        print('     %-14s ratio=%-5s n=%-6d amt/th=%.4f' % (k[0],k[1],n, a/t if t else 0))
