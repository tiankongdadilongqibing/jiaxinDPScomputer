# -*- coding: utf-8 -*-
import io, json, sys, collections
def load(p): return json.load(io.open(p, encoding='utf-8'))
def dmg(d): return [e for e in (d.get('events') or []) if e.get('calc')]

for path in sys.argv[1:]:
    d = load(path); name = path.split('\\')[-1]
    ev = dmg(d)
    print('=' * 90)
    print('%s  n=%d' % (name, len(ev)))
    st = collections.Counter()
    for e in ev:
        s = (e.get('comp4') or '')
        s = s.replace('受击方状态:', '').strip()
        if s in ('无', ''): st['(none)'] += 1; continue
        for x in s.split('、'):
            st[x.strip()] += 1
    print('  victim statuses per hit (comp4): %s' % dict(st.most_common(8)))
    # how often the two key statuses co-occur
    both = sum(1 for e in ev if '毒' in (e.get('comp4') or '') and '火傷' in (e.get('comp4') or ''))
    poison = sum(1 for e in ev if '毒' in (e.get('comp4') or ''))
    burn = sum(1 for e in ev if '火傷' in (e.get('comp4') or ''))
    frozen = sum(1 for e in ev if '凍結' in (e.get('comp4') or ''))
    print('  hits with 毒=%d  火傷=%d  凍結=%d  毒&火傷=%d   (of %d)' % (poison, burn, frozen, both, len(ev)))
    # 海魔の残滓 marker counts
    hk = sum(1 for e in ev if '海魔の残滓' in (e.get('comp2') or ''))
    hm = sum(1 for e in ev if '母なる変異の飛沫' in (e.get('comp2') or ''))
    print('  comp2 mentions 海魔の残滓=%d  母なる変異の飛沫=%d' % (hk, hm))
    # summon links
    acts = d.get('actors') or []
    for i, a in enumerate(acts):
        if a.get('summon'):
            print('  summon [%2d] %-14s keys=%s' % (i, a.get('name'), [k for k in a.keys() if k not in ('abilities','talents','triggers')]))
            break
