# -*- coding: utf-8 -*-
import io, json, sys
files = sys.argv[1:]
for path in files:
    d = json.load(io.open(path, encoding='utf-8'))
    acts = d.get('actors') or []
    t1 = [a for a in acts if a.get('team') == 1]
    t1.sort(key=lambda a: -(a.get('dealt') or 0))
    tot = d.get('totals') or {}
    print('=' * 100)
    print('%s  v%s  quest=%s  dur=%ss  result=%s' % (path.split('\\')[-1], d.get('version'), d.get('quest'), d.get('duration'), d.get('result')))
    print('  totals: dealt=%s taken=%s healing=%s absorbed=%s dealtWithAbsorbed=%s unattributed=%s/%s'
          % (tot.get('dealt'), tot.get('taken'), tot.get('healing'), tot.get('absorbed'), tot.get('dealtWithAbsorbed'), tot.get('unattributedDamage'), tot.get('unattributedHits')))
    print('  player actors: %d   total dealt by t1 = %s' % (len(t1), sum((a.get('dealt') or 0) for a in t1)))
    print('  %-22s %7s %12s %8s %8s %8s' % ('name', 'hits', 'dealt', 'maxHit', 'crit', 'kind'))
    for a in t1:
        print('  %-22s %7s %12s %8s %8s %8s' % (a.get('name'), a.get('hit'), a.get('dealt'), a.get('maxHit'), a.get('crit'), a.get('kind')))
