# -*- coding: utf-8 -*-
"""Who is mad on OUR side, and are the guest-unreadable hook rows that same unit (self-application)?"""
import io, json, os, glob, collections
HERE = os.path.dirname(os.path.abspath(__file__))
EX = os.path.join(HERE, '..', 'BepInEx', 'plugins', 'DpsMeter', 'exports')
L = []
def w(s=''): L.append(str(s))
for f in sorted(glob.glob(os.path.join(EX, 'battle_411001_20261003_23*.json')), key=os.path.getmtime):
    d = json.load(io.open(f, encoding='utf-8'))
    if d.get('version') != '1.5.4': continue
    ev = [e for e in (d.get('events') or []) if e.get('type') == 'dmg' and e.get('atkTeam') == 1]
    mad_by_atk = collections.Counter(); hits_by_atk = collections.Counter()
    vmad_by_vic = collections.Counter()
    for e in ev:
        a = e.get('attacker'); c = e.get('calc') or {}
        hits_by_atk[a] += 1
        if c.get('madnessOn'): mad_by_atk[a] += 1
        if c.get('victimMadnessOn'): vmad_by_vic[e.get('victim')] += 1
    ma = d.get('madnessApplies') or {}
    rows = ma.get('rows') or []
    pair = collections.Counter((r.get('owner'), r.get('guest')) for r in rows)
    nullg = collections.Counter(r.get('owner') for r in rows if r.get('guest') is None)
    w('=' * 96)
    w('%s  hook=%s recorded=%s nullGuest=%s' % (os.path.basename(f)[7:], ma.get('hookCalls'), ma.get('recorded'), ma.get('nullGuest')))
    w('  attacker-mad hits by attacker: %s' % dict(mad_by_atk))
    w('  hits by attacker:             %s' % dict(hits_by_atk))
    w('  victim-mad hits by victim:    %s' % dict(vmad_by_vic))
    w('  applies (owner,guest) top10:  %s' % pair.most_common(10))
    w('  rows with guest=null, by owner: %s' % dict(nullg))
rep = os.path.join(HERE, 'madness_owner_check.txt')
io.open(rep, 'w', encoding='utf-8').write(u'\n'.join(L))
print('report -> ' + rep)