# -*- coding: utf-8 -*-
"""Control: same enemy set / same front-line structure in the two battles?"""
import io, json, os, collections
HERE = os.path.dirname(os.path.abspath(__file__))
EX = os.path.join(HERE, '..', 'BepInEx', 'plugins', 'DpsMeter', 'exports')
L = []
def w(s=''): L.append(str(s))
for nm in ('battle_411001_20261003_231044.json', 'battle_411001_20261003_231340.json'):
    d = json.load(io.open(os.path.join(EX, nm), encoding='utf-8'))
    w('=' * 96)
    w(nm)
    b = [a for a in (d.get('actors') or []) if a.get('team') == 2]
    b.sort(key=lambda x: -(x.get('taken') or 0))
    tot_taken = sum(a.get('taken') or 0 for a in b)
    w('  team2 actors=%d  total taken=%.1fM' % (len(b), tot_taken/1e6))
    for a in b[:8]:
        if (a.get('taken') or 0) <= 0: continue
        w('    %-22s taken=%-11.1fM hits=%-6s dealt=%-9s hpPct=%s' % (a.get('name'), (a.get('taken') or 0)/1e6, a.get('hit'), a.get('dealt'), (a.get('hpPct') or [])[-1:]))
    # team1 hp / front-line clue
    t1 = [a for a in (d.get('actors') or []) if a.get('team') == 1 and (a.get('dealt') or 0) > 0]
    w('  team1 hpPct last: ' + ' '.join('%s=%s' % (a.get('name'), (a.get('hpPct') or [None])[-1]) for a in t1))
    # player-side incoming damage (survivability)
    w('  team1 taken: ' + ' '.join('%s=%.2fM' % (a.get('name'), (a.get('taken') or 0)/1e6) for a in sorted(t1, key=lambda x: -(x.get('taken') or 0))[:6]))
    w('  healing: ' + ' '.join('%s=%.1fk' % (a.get('name'), (a.get('healingGiven') or 0)/1e3) for a in t1 if (a.get('healingGiven') or 0) > 0))
rep = os.path.join(HERE, 'victim_check.txt')
io.open(rep, 'w', encoding='utf-8').write(u'\n'.join(L))
print('report -> ' + rep)