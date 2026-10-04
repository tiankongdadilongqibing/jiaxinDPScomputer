# -*- coding: utf-8 -*-
import io, json, sys, collections
files = sys.argv[1:]
for path in files:
    d = json.load(io.open(path, encoding='utf-8'))
    name = path.split('\\')[-1]
    print('=' * 100)
    print('%s  v%s  dealt=%s' % (name, d.get('version'), (d.get('totals') or {}).get('dealt')))
    print('  giveTypeList : %s' % json.dumps(d.get('giveTypeList'), ensure_ascii=False))
    acts = d.get('actors') or []
    t1 = [a for a in acts if a.get('team') == 1]
    print('  team1 actors (name / kind / summon / hits / dealt):')
    for a in sorted(t1, key=lambda x: -(x.get('dealt') or 0)):
        print('     %-22s kind=%-4s summon=%-5s hits=%-6s dealt=%s' % (a.get('name'), a.get('kind'), a.get('summon'), a.get('hit'), a.get('dealt')))
    ra = d.get('rosterAudit')
    print('  rosterAudit type=%s' % type(ra).__name__)
    if isinstance(ra, dict):
        print('     keys=%s' % list(ra.keys())[:20])
        print('     %s' % json.dumps(ra, ensure_ascii=False)[:900])
    elif isinstance(ra, list):
        print('     n=%d  first=%s' % (len(ra), json.dumps(ra[0], ensure_ascii=False)[:500] if ra else ''))
    print('  statusAudit=%s' % json.dumps(d.get('statusAudit'), ensure_ascii=False)[:300])
