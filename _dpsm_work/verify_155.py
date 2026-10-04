# -*- coding: utf-8 -*-
"""1.5.5 acceptance: self-applied madness attribution + give-applier hook."""
import io, json, os, collections
HERE = os.path.dirname(os.path.abspath(__file__))
EX = os.path.join(HERE, '..', 'BepInEx', 'plugins', 'DpsMeter', 'exports')
f = os.path.join(EX, 'battle_411001_20261003_235204.json')
d = json.load(io.open(f, encoding='utf-8'))
L = []
def w(s=''): L.append(str(s))
ev = [e for e in (d.get('events') or []) if e.get('type') == 'dmg' and e.get('atkTeam') == 1]
w('battle 235204  ver=%s dur=%.1fs result=%s hits=%d dealt=%.1fM' % (d.get('version'), d.get('duration', 0), d.get('result'), len(ev), ((d.get('totals') or {}).get('dealt') or 0)/1e6))
t1 = [a for a in (d.get('actors') or []) if a.get('team') == 1 and (a.get('dealt') or 0) > 0]
w('team1: ' + ' | '.join('%s=%.1fM' % (a.get('name'), (a.get('dealt') or 0)/1e6) for a in sorted(t1, key=lambda x: -(x.get('dealt') or 0))))
w('')
ma = d.get('madnessApplies') or {}
w('[MADAPP] hook=%s recorded=%s selfApplied=%s nullOwner=%s nullGuest=%s errors=%s rows=%d dropped=%s' % (
    ma.get('hookCalls'), ma.get('recorded'), ma.get('selfApplied'), ma.get('nullOwner'), ma.get('nullGuest'), ma.get('errors'), len(ma.get('rows') or []), ma.get('rowsDropped')))
rows = ma.get('rows') or []
pair = collections.Counter((r.get('owner'), r.get('guest'), bool(r.get('self'))) for r in rows)
for k, n in pair.most_common(8): w('   row %s x%d' % (str(k), n))
ga = d.get('givenApplies') or {}
w('')
w('[GIVAPP] hookCalls=%s recorded=%s nullOwner=%s nullGuest=%s targets=%s multiGiver=%s lookupHits=%s lookupMisses=%s errors=%s rows=%d dropped=%s' % (
    ga.get('hookCalls'), ga.get('recorded'), ga.get('nullOwner'), ga.get('nullGuest'), ga.get('targets'), ga.get('multiGiver'), ga.get('lookupHits'), ga.get('lookupMisses'), ga.get('errors'), len(ga.get('rows') or []), ga.get('rowsDropped')))
grows = ga.get('rows') or []
gpair = collections.Counter((r.get('kind'), r.get('owner'), r.get('guest')) for r in grows)
for k, n in gpair.most_common(8): w('   row %s x%d' % (str(k), n))
w('')
cnt = collections.Counter(); mad_by_atk = collections.Counter()
for e in ev:
    c = e.get('calc') or {}
    if c.get('madnessOn'): mad_by_atk[e.get('attacker')] += 1
    for fo in (c.get('fold') or []):
        k = fo.get('kind')
        if k in ('given', 'madness'):
            cnt[k + ('_with' if fo.get('byUnit') else '_without')] += 1
w('byUnit coverage: %s' % dict(cnt))
w('attacker-mad hits by attacker: %s' % dict(mad_by_atk))
ra = d.get('rosterAudit') or {}
w('rosterAudit giver: resolved=%s null=%s errors=%s' % (ra.get('giverResolved'), ra.get('giverNull'), ra.get('giverErrors')))
rec = d.get('reconcile') or {}
w('reconcile: exact=%s exactWithCrit=%s unexplained=%s' % (rec.get('exact'), rec.get('exactWithCrit'), rec.get('unexplained')))
rep = os.path.join(HERE, 'verify_155.txt')
io.open(rep, 'w', encoding='utf-8').write(u'\n'.join(L))
print('report -> ' + rep)