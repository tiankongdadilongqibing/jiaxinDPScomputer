# -*- coding: utf-8 -*-
import io, json, sys, collections
def load(p): return json.load(io.open(p, encoding='utf-8'))
def dmg(d): return [e for e in (d.get('events') or []) if e.get('calc')]

def walk(o, path, needle, out):
    if isinstance(o, dict):
        for k,v in o.items(): walk(v, path+'.'+str(k), needle, out)
    elif isinstance(o, list):
        for i,v in enumerate(o): walk(v, path+'['+str(i)+']', needle, out)
    elif isinstance(o, str) and needle in o:
        out.append((path, o[:120]))

for path in sys.argv[1:]:
    d = load(path); name = path.split('\\')[-1]
    ev = dmg(d)
    sa = sum(e.get('amount') or 0 for e in ev)
    st = sum((e.get('calc') or {}).get('theory') or 0 for e in ev)
    sb = sum((e.get('calc') or {}).get('base') or 0 for e in ev)
    print('='*100)
    print('%s  v%s' % (name, d.get('version')))
    print('  n=%d  SUM amount=%d  SUM theory=%d  SUM base=%d' % (len(ev), sa, st, sb))
    print('  SUM amount/theory = %.4f      SUM amount/base = %.4f' % (sa/st if st else 0, sa/sb if sb else 0))
    per = collections.defaultdict(lambda: [0,0,0])
    for e in ev:
        a = e.get('attacker'); c = e.get('calc') or {}
        per[a][0]+=1; per[a][1]+= e.get('amount') or 0; per[a][2]+= c.get('theory') or 0
    print('  %-22s %6s %12s %12s %8s' % ('attacker','n','SUMamount','SUMtheory','amt/th'))
    for a in sorted(per, key=lambda x:-per[x][1]):
        n,da,dt = per[a]
        print('  %-22s %6d %12d %12d %8.4f' % (a, n, da, dt, da/dt if dt else 0))
    for needle in ['海魔の残滓', '母なる変異の飛沫']:
        hits=[]
        walk(d, '', needle, hits)
        paths = collections.Counter(p.split('.actors')[0] if '.actors' in p else p.split('[')[0] for p,_ in hits)
        print('  needle %s : %d occurrences; top paths=%s' % (needle, len(hits), paths.most_common(4)))
        seen=set()
        for p,s in hits:
            if s not in seen:
                seen.add(s)
                print('      %s' % p.split('.events')[0][-90:])
            if len(seen)>=3: break
