# -*- coding: utf-8 -*-
"""Pin down the totals.dealt vs per-event-sum gap (HANDOFF todo 4)."""
import io, json, os, sys, glob, collections
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.dirname(HERE)
EXPORTS = os.path.join(ROOT, 'BepInEx', 'plugins', 'DpsMeter', 'exports')
p = sys.argv[1] if len(sys.argv) > 1 else sorted([q for q in glob.glob(os.path.join(EXPORTS,'battle_*.json')) if not os.path.basename(q).startswith('battle_9999')], key=os.path.getmtime)[-1]
d = json.load(io.open(p, encoding='utf-8'))
tot = d.get('totals') or {}
by = collections.Counter(); n = collections.Counter()
rows = []
for e in d.get('events') or []:
    if e.get('type') != 'dmg': continue
    at = e.get('atkTeam'); vt = e.get('vicTeam')
    by[(at, vt)] += float(e.get('amount') or 0)
    n[(at, vt)] += 1
    if at not in (1, None):
        rows.append({'t': e.get('t'), 'attacker': e.get('attacker'), 'victim': e.get('victim'),
                     'atkTeam': at, 'vicTeam': vt, 'amount': e.get('amount')})
L = []
L.append('file %s' % os.path.basename(p))
L.append('totals %s' % json.dumps(tot, ensure_ascii=False))
L.append('(atkTeam,vicTeam) -> n, sum')
for k in sorted(by, key=lambda x: -by[x]):
    L.append('  %s n=%d sum=%.0f' % (k, n[k], by[k]))
L.append('')
L.append('non-team1-attacker events (%d):' % len(rows))
for r in rows[:30]: L.append('  ' + json.dumps(r, ensure_ascii=False))
s_dealt = by[(1, 2)]
L.append('')
L.append('sum(atk=1)=%.0f  totals.dealt=%s  diff=%.0f' % (sum(by[k] for k in by if k[0] == 1), tot.get('dealt'), float(tot.get('dealt') or 0) - sum(by[k] for k in by if k[0] == 1)))
L.append('sum(atk=1,vic=2)=%.0f' % s_dealt)
L.append('sum(null)=%.0f  totals.unattributedDamage=%s' % (by[(None, 2)], tot.get('unattributedDamage')))
io.open(os.path.join(HERE, 'contrib_gap_dealt.txt'), 'w', encoding='utf-8').write(u'\n'.join(L))
print('sums:', json.dumps({str(k): [n[k], round(by[k])] for k in sorted(by, key=lambda x: -by[x])}))
print('dealt=%s sum_atk1=%.0f sum_null=%.0f unattr=%s' % (tot.get('dealt'), sum(by[k] for k in by if k[0] == 1), by[(None, 2)], tot.get('unattributedDamage')))
