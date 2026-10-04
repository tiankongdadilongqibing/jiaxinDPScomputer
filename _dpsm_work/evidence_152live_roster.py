# -*- coding: utf-8 -*-
import io, json, sys
for path in sys.argv[1:]:
    d = json.load(io.open(path, encoding='utf-8'))
    acts = d.get('actors') or []
    t1 = [a for a in acts if a.get('team') == 1]
    t2 = [a for a in acts if a.get('team') == 2]
    print('=' * 80)
    print('FILE %s  version=%s  duration=%s  result=%s' % (path.split('\\')[-1], d.get('version'), d.get('duration'), d.get('result')))
    print('  team1 (player) n=%d: %s' % (len(t1), [(a.get('name'), a.get('kind'), a.get('hit'), a.get('dealt')) for a in t1]))
    print('  team2 (enemy)  n=%d (first 8): %s' % (len(t2), [(a.get('name'), a.get('kind'), a.get('hit')) for a in t2[:8]]))
    print('  giveTypeList=%s' % json.dumps(d.get('giveTypeList'), ensure_ascii=False)[:300])
