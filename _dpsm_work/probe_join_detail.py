# -*- coding: utf-8 -*-
"""Detail probe: (a) all (talentType,param) pairs in actors, (b) ambiguous ability names -> holders,
(c) global# ownerPointer distinctness per rule name, (d) given entry indices seen. ASCII stdout.
"""
import io, json, os, re, sys, collections
d = json.load(io.open(sys.argv[1], encoding='utf-8'))
actors = d.get('actors') or []
ev = d.get('events') or []
L = []
def w(s=''): L.append(str(s))

tal_tp = collections.Counter()
tal_who = collections.defaultdict(set)
name_who = collections.defaultdict(set)
for a in actors:
    nm = a.get('name')
    for t in (a.get('talents') or []):
        pp = t.get('p') or []
        key = (t.get('type'), pp[0] if pp else None)
        tal_tp[key] += 1
        tal_who[key].add(nm)
    for ab in (a.get('abilities') or []):
        if ab.get('name'): name_who[ab.get('name')].add(nm)
    for t in (a.get('talents') or []):
        if t.get('ability'): name_who[t.get('ability')].add(nm)

w(u'(a) actors 里全部 (talentType, p0) 对(计数, 持有者):')
for k, n in sorted(tal_tp.items(), key=lambda x: -x[1]):
    w(u'   %-16s n=%-4d who=%s' % (str(k), n, sorted(tal_who[k])[:6]))

w(u'')
w(u'(b) 同名多持有者的能力名:')
for nm, who in sorted(name_who.items()):
    if len(who) > 1: w(u'   %-24s -> %s' % (nm, sorted(who)))

ptr_name = collections.defaultdict(set)
name_ptr = collections.defaultdict(set)
given_idx = collections.Counter()
NAME = re.compile(r'\[([^\]]+)\]')
for e in ev:
    if e.get('atkTeam') != 1: continue
    c = e.get('calc') or {}
    for f in (c.get('fold') or []):
        org = str(f.get('origin', ''))
        if f.get('kind') == 'global':
            m = re.match(r'global#(\d+)/(\d+)', org)
            nmm = NAME.search(f.get('label') or '')
            if m and nmm:
                ptr_name[m.group(1)].add(nmm.group(1))
                name_ptr[nmm.group(1)].add(m.group(1))
        elif f.get('kind') == 'given':
            m = re.match(r'given#(\d+)/(-?\d+)/(-?\d+)', org)
            if m: given_idx[(m.group(2), m.group(3))] += 1
w(u'')
w(u'(c) global# ownerPointer <-> 规则名:')
for nm, ptrs in sorted(name_ptr.items()):
    w(u'   %-24s 指针数=%d %s' % (nm, len(ptrs), sorted(p[-6:] for p in ptrs)[:6]))
for pt, nms in sorted(ptr_name.items()):
    if len(nms) > 1: w(u'   !! 指针 %s 对应多个规则名: %s' % (pt[-6:], sorted(nms)))
w(u'')
w(u'(d) given 折叠的 (type,param) 分布: %s' % dict(given_idx))

rep = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'probe_join_detail.txt')
io.open(rep, 'w', encoding='utf-8').write(u'\n'.join(L))
print('report -> ' + rep)
print('talent pairs=%d, ambiguous names=%d, global names=%d' % (len(tal_tp), sum(1 for v in name_who.values() if len(v) > 1), len(name_ptr)))