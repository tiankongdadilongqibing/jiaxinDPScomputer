# -*- coding: utf-8 -*-
"""Second recon: rule identity (kind/side/origin/label -> owner) + cancel shape.
ASCII stdout; CJK detail -> _dpsm_work/contrib_recon_155b.txt"""
import io, json, os, sys, glob, collections
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.dirname(HERE)
EXPORTS = os.path.join(ROOT, 'BepInEx', 'plugins', 'DpsMeter', 'exports')
p = sys.argv[1] if len(sys.argv) > 1 else sorted([q for q in glob.glob(os.path.join(EXPORTS,'battle_*.json')) if not os.path.basename(q).startswith('battle_9999')], key=os.path.getmtime)[-1]
d = json.load(io.open(p, encoding='utf-8'))
actors = d.get('actors') or []
byKey = {a.get('key'): a for a in actors}
# abilityId / name -> set of team1 holder names
hold_id = collections.defaultdict(set); hold_name = collections.defaultdict(set)
for a in actors:
    if a.get('team') != 1: continue
    nm = a.get('name')
    for ab in (a.get('abilities') or []):
        if ab.get('id') is not None: hold_id[ab['id']].add(nm)
        if ab.get('name'): hold_name[ab['name']].add(nm)
    for t in (a.get('talents') or []):
        if t.get('abilityId') is not None: hold_id[t['abilityId']].add(nm)
        if t.get('ability'): hold_name[t['ability']].add(nm)

rule = collections.Counter()       # (kind, side, origin, label) -> n
zero = collections.Counter()       # origin/label where factor <= 0
orig_side = collections.Counter()
L = []
ev = d.get('events') or []
cancel_sample = None
cancel_fields = collections.Counter()
for e in ev:
    if e.get('type') != 'dmg': continue
    c = e.get('calc')
    if not c: continue
    for cs in (c.get('cancel') or []):
        if cancel_sample is None: cancel_sample = cs
        cancel_fields[tuple(sorted(cs.keys()))] += 1
    for f in (c.get('fold') or []):
        k = f.get('kind'); s = f.get('side'); o = str(f.get('origin','')); lab = f.get('label') or ''
        try: fac = float(f.get('factor') or 0)
        except: fac = -1
        if fac <= 0: zero[(k, s, o, lab)] += 1
        rule[(k, s, o, fac, f.get('byUnit') or '', lab)] += 1
        orig_side[(k, s, o.split('/')[0])] += 1
res = {}
# group: for each (kind, side, origin), the labels and total
grp = collections.Counter()
for (k,s,o,fac,bu,lab), n in rule.items():
    grp[(k,s,o,fac,bu,lab)] = n
L.append(u'== rules (kind|side|origin|factor|byUnit|label : count) ==')
for (k,s,o,fac,bu,lab), n in sorted(rule.items(), key=lambda x: (-x[1])):
    L.append(u'%s|%s|%s|%s|%s|%s : %d' % (k,s,o,fac,bu,lab,n))
L.append(u'')
L.append(u'== zero/negative factors ==')
for (k,s,o,lab), n in zero.most_common(): L.append(u'%s|%s|%s|%s : %d' % (k,s,o,lab,n))
L.append(u'')
L.append(u'== cancel sample ==')
L.append(json.dumps(cancel_sample, ensure_ascii=False) if cancel_sample is not None else 'none')
L.append(u'cancel field-shapes: ' + json.dumps({str(k):v for k,v in cancel_fields.items()}, ensure_ascii=False))
L.append(u'')
L.append(u'== holders (abilityId) for text#4 origins ==')
for o in sorted(set(str(f.get('origin','')) for e in ev if e.get('type')=='dmg' and e.get('calc') for f in (e['calc'].get('fold') or []))):
    parts = o.split('/')
    if len(parts) > 1 and parts[1].isdigit():
        aid = int(parts[1])
        if parts[0].startswith('text') or parts[0] == 'global':
            L.append(u'%s -> holders_id=%s' % (o, sorted(hold_id.get(aid, set()))))
io.open(os.path.join(HERE,'contrib_recon_155b.txt'),'w',encoding='utf-8').write(u'\n'.join(L))
print('rules=%d cancelShapes=%d evNoCalc=%d' % (len(rule), len(cancel_fields), sum(1 for e in ev if e.get('type')=='dmg' and not e.get('calc'))))
print('origSide=' + json.dumps({str(k):v for k,v in sorted(orig_side.items())}, ensure_ascii=True))
# atkKey missing detail
miss = collections.Counter()
for e in ev:
    if e.get('type')=='dmg' and e.get('atkKey') is None:
        miss[(e.get('atkTeam'), bool(e.get('calc')))] += 1
print('atkKeyMissing by (team,hasCalc)=' + json.dumps({str(k):v for k,v in miss.items()}))
print('wrote contrib_recon_155b.txt lines=%d' % len(L))
