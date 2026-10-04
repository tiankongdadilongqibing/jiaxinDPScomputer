# -*- coding: utf-8 -*-
import io, json, sys, collections
def load(p): return json.load(io.open(p, encoding='utf-8'))
def dmg(d): return [e for e in (d.get('events') or []) if e.get('calc')]
def vstat(e):
    s=(e.get('comp4') or '').replace('受击方状态:','').split('自身状态')[0].strip()
    return s
d = load(sys.argv[1])
ev = dmg(d)
print('NEW battle: the 狂気-vs-not contrast, same status set')
G = collections.defaultdict(lambda:[0,0,0])
for e in ev:
    vs = vstat(e); mad = '狂気' in vs
    key = (tuple(sorted(x for x in vs.split('、') if x and x!='狂気')), mad)
    c = e.get('calc') or {}
    G[key][0]+=1; G[key][1]+= e.get('amount') or 0; G[key][2]+= c.get('theory') or 0
for k in sorted(G, key=lambda x:-G[x][1])[:10]:
    n,a,t = G[k]
    print('  %-22s mad=%-5s n=%-5d amt/th=%.4f' % ('+'.join(k[0]) or '(none)', k[1], n, a/t if t else 0))
print()
# madnessRatio field
mr = collections.Counter()
for e in ev:
    c = e.get('calc') or {}
    vs = vstat(e)
    mr[(('狂気' in vs), c.get('madnessRatio'))] += 1
print('madnessRatio x victim-mad:', dict(list(mr.items())[:12]))
print()
# where does 狂気 come from? state timeline rows naming 狂気
st = d.get('stateTimeline') or d.get('timeline') or {}
rows = st.get('rows') if isinstance(st, dict) else None
print('timeline rows=%s' % (len(rows) if rows else 0))
cnt = collections.Counter()
if rows:
    for r in rows:
        s = json.dumps(r, ensure_ascii=False)
        if '狂気' in s: cnt[str(r.get('unit')) + ' ' + str(r.get('status') or r.get('kind'))] += 1
print('timeline rows mentioning 狂気:', cnt.most_common(6))
# who applied 狂気: look at abilities of the added unit
for a in (d.get('actors') or []):
    if a.get('name') == 'ルゥ=ルルサ':
        print('ルゥ=ルルサ abilities:', [x.get('name') for x in (a.get('abilities') or []) if isinstance(x, dict)][:12])
