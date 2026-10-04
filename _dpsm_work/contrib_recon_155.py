# -*- coding: utf-8 -*-
"""Reconnaissance of a 1.5.5 export for the contribution core (Phase A/B/C).
ASCII-only stdout; CJK detail goes to _dpsm_work/contrib_recon_155.txt
Usage: python contrib_recon_155.py [export.json]"""
import io, json, os, sys, glob, collections

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
EXPORTS = os.path.join(ROOT, 'BepInEx', 'plugins', 'DpsMeter', 'exports')
if len(sys.argv) > 1:
    path = sys.argv[1]
else:
    cands = [p for p in sorted(glob.glob(os.path.join(EXPORTS, 'battle_*.json')), key=os.path.getmtime)
             if not os.path.basename(p).startswith('battle_9999')]
    path = cands[-1]
d = json.load(io.open(path, encoding='utf-8'))
out = {}
out['file'] = os.path.basename(path)
out['version'] = d.get('version')
out['quest'] = d.get('quest')
out['result'] = d.get('result')
out['duration'] = d.get('duration')
actors = d.get('actors') or []
ev = d.get('events') or []
out['actors'] = len(actors)
out['events'] = len(ev)
out['topKeys'] = list(d.keys())

keys = [a.get('key') for a in actors]
out['keyUnique'] = len(set(keys)) == len(keys)
out['keyNull'] = sum(1 for k in keys if k is None)
t1 = [a for a in actors if a.get('team') == 1]
t2 = [a for a in actors if a.get('team') == 2]
out['team1'] = len(t1); out['team2'] = len(t2)
names1 = [a.get('name') for a in t1]
dup1 = [n for n, c in collections.Counter(names1).items() if c > 1]
out['team1DupNames'] = dup1
names2 = set(a.get('name') for a in t2)
out['nameCollideTeam1Team2'] = sorted(set(names1) & names2)
byKey = {}
for a in actors: byKey[a.get('key')] = a
out['team1Roster'] = [{'key': a.get('key'), 'name': a.get('name'), 'kind': a.get('kind'),
                       'summon': a.get('summon'), 'dealt': a.get('dealt')} for a in t1]

# events
cnt = collections.Counter()
atkKeyMissing = 0; vicKeyMissing = 0; atkKeyUnknown = 0
mismatchName = collections.Counter()
tot1 = 0.0; totAll = 0.0
perAtkKey = collections.defaultdict(float)
foldKinds = collections.Counter(); foldSide = collections.Counter()
foldByUnit = collections.Counter(); foldOriginKind = collections.Counter()
factors = collections.Counter(); factorLt1 = 0; factorEq1 = 0
cancelKinds = collections.Counter(); cancelSide = collections.Counter()
calcMissing = 0; foldEmpty = 0; foldTotal = 0
foldCountDist = collections.Counter()
labels = collections.Counter(); byUnitVals = collections.Counter()
eventsNoCalc = []
for e in ev:
    t = e.get('type'); cnt[t] += 1
    if t != 'dmg': continue
    D = float(e.get('amount') or 0)
    totAll += D
    ak = e.get('atkKey'); vk = e.get('vicKey')
    if ak is None: atkKeyMissing += 1
    if vk is None: vicKeyMissing += 1
    a = byKey.get(ak)
    if ak is not None and a is None: atkKeyUnknown += 1
    if a is not None and a.get('name') != e.get('attacker'):
        mismatchName[(a.get('name'), e.get('attacker'))] += 1
    if e.get('atkTeam') == 1:
        tot1 += D
        perAtkKey[ak] += D
    c = e.get('calc')
    if not c:
        calcMissing += 1
        if len(eventsNoCalc) < 5: eventsNoCalc.append({k: e.get(k) for k in ('t','attacker','victim','amount','atkTeam','atkKey')})
        continue
    fs = c.get('fold') or []
    foldCountDist[len(fs)] += 1
    if not fs: foldEmpty += 1
    for f in fs:
        foldTotal += 1
        k = f.get('kind'); s = f.get('side'); o = str(f.get('origin', ''))
        foldKinds[k] += 1; foldSide[(k, s)] += 1
        bu = f.get('byUnit')
        foldByUnit[(k, bool(bu))] += 1
        if bu: byUnitVals[bu] += 1
        if k == 'text': foldOriginKind[o.split('/')[0] + '/' + (o.split('/')[1] if '/' in o else '')] += 1
        else: foldOriginKind[(k, o.split('/')[0] if '/' in o else o)] += 1
        try: fac = float(f.get('factor') or 0)
        except: fac = -1
        factors[repr(round(fac, 6))] += 1
        if 0 < fac < 1: factorLt1 += 1
        if abs(fac - 1.0) < 1e-12: factorEq1 += 1
        lab = f.get('label') or ''
        labels[lab] += 1
    for cs in (c.get('cancel') or []):
        cancelKinds[cs.get('kind')] += 1; cancelSide[(cs.get('kind'), cs.get('side'))] += 1
out['eventTypes'] = dict(cnt)
out['team1Dealt_eventSum'] = round(tot1, 3)
out['allDealt_eventSum'] = round(totAll, 3)
out['totals.dealt'] = (d.get('totals') or {}).get('dealt')
out['atkKeyMissing'] = atkKeyMissing; out['vicKeyMissing'] = vicKeyMissing; out['atkKeyUnknown'] = atkKeyUnknown
out['attackerNameMismatch'] = {str(k): v for k, v in mismatchName.most_common(10)}
out['calcMissing'] = calcMissing; out['eventsNoCalcSample'] = eventsNoCalc
out['foldEmpty'] = foldEmpty; out['foldTotal'] = foldTotal
out['foldCountDist'] = dict(sorted(foldCountDist.items()))
out['foldKinds'] = dict(foldKinds)
out['foldSide'] = {str(k): v for k, v in foldSide.items()}
out['foldByUnit'] = {str(k): v for k, v in foldByUnit.items()}
out['byUnitVals'] = dict(byUnitVals.most_common(15))
out['foldOriginKind'] = {str(k): v for k, v in sorted(foldOriginKind.items(), key=lambda x: -x[1])[:25]}
out['distinctFactors'] = len(factors)
out['factorTop'] = {str(k): v for k, v in factors.most_common(25)}
out['factorLt1'] = factorLt1; out['factorEq1'] = factorEq1
out['cancelKinds'] = {str(k): v for k, v in cancelKinds.items()}
out['cancelSide'] = {str(k): v for k, v in cancelSide.items()}
out['distinctLabels'] = len(labels)
with io.open(os.path.join(HERE, 'contrib_recon_155.txt'), 'w', encoding='utf-8') as fh:
    fh.write(u'FILE ' + os.path.basename(path) + u'\n')
    fh.write(u'== labels ==\n')
    for k, v in labels.most_common(60): fh.write(u'%8d  %s\n' % (v, k))
    fh.write(u'== byUnit values ==\n')
    for k, v in byUnitVals.most_common(30): fh.write(u'%8d  %s\n' % (v, k))
print(json.dumps(out, ensure_ascii=True, indent=1, sort_keys=True))
