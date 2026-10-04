# -*- coding: utf-8 -*-
"""Third recon: attribution correctness checks. ASCII stdout; CJK -> contrib_recon_155c.txt"""
import io, json, os, sys, glob, collections, re
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.dirname(HERE)
EXPORTS = os.path.join(ROOT, 'BepInEx', 'plugins', 'DpsMeter', 'exports')
p = sys.argv[1] if len(sys.argv) > 1 else sorted([q for q in glob.glob(os.path.join(EXPORTS,'battle_*.json')) if not os.path.basename(q).startswith('battle_9999')], key=os.path.getmtime)[-1]
d = json.load(io.open(p, encoding='utf-8'))
actors = d.get('actors') or []; byKey = {a.get('key'): a for a in actors}
hold_id = collections.defaultdict(set)
for a in actors:
    if a.get('team') != 1: continue
    for ab in (a.get('abilities') or []):
        if ab.get('id') is not None: hold_id[ab['id']].add(a.get('name'))
    for t in (a.get('talents') or []):
        if t.get('abilityId') is not None: hold_id[t['abilityId']].add(a.get('name'))
L = []
tot = d.get('totals') or {}
L.append(u'totals keys: ' + json.dumps(sorted(tot.keys())))
L.append(u'totals: ' + json.dumps(tot, ensure_ascii=False))
ev = d.get('events') or []
# (a) atkTeam null
null_sum = 0.0; null_rows = collections.Counter(); null_sample = []
for e in ev:
    if e.get('type') != 'dmg': continue
    if e.get('atkTeam') is None or e.get('atkKey') is None:
        amt = float(e.get('amount') or 0); null_sum += amt
        null_rows[(e.get('atkTeam'), e.get('attacker'), e.get('owner'))] += 1
        if len(null_sample) < 8: null_sample.append({k: e.get(k) for k in ('t','attacker','owner','victim','amount','atkTeam','vicTeam','source')})
L.append(u'(a) dmg with null atkTeam/atkKey: n=%d sum=%.1f' % (sum(null_rows.values()), null_sum))
L.append(u'    owners: ' + json.dumps({str(k): v for k, v in null_rows.most_common(10)}, ensure_ascii=False))
L.append(u'    sample: ' + json.dumps(null_sample, ensure_ascii=False))
# (b) atk-side text/talent folds: attacker in holders?
viol = collections.Counter(); ok = 0; noid = 0
t2names = set(a.get('name') for a in actors if a.get('team') != 1)
for e in ev:
    if e.get('type') != 'dmg' or e.get('atkTeam') != 1: continue
    c = e.get('calc') or {}
    for f in (c.get('fold') or []):
        if f.get('kind') not in ('text','talent') or f.get('side') != 'atk': continue
        org = str(f.get('origin',''))
        m = re.match(r'(?:text|talent)#(?:buff)?\d+/(\d+)/', org)
        if not m: noid += 1; continue
        hs = hold_id.get(int(m.group(1)), set())
        if e.get('attacker') in hs: ok += 1
        else: viol[(org, e.get('attacker'), tuple(sorted(hs))[:3])] += 1
L.append(u'(b) atk-side text/talent folds: attacker-in-holders=%d violations=%d noAbilityId=%d' % (ok, sum(viol.values()), noid))
for k, v in viol.most_common(10): L.append(u'    VIOL %s : %d' % (json.dumps(k, ensure_ascii=False), v))
# (d) attacker sets for selected rules
want = {'text#4/20042': None, 'text#4/20056': None, 'madness#250': None, 'global#2543376704832/0': None, 'global#2543377347904/0': None,
        'text#1/75/c1': None, 'text#8/2/c0': None, 'given#6/1006/-10': None, 'talent#0/30042/t1/1005': None}
att_by_rule = collections.defaultdict(collections.Counter)
for e in ev:
    if e.get('type') != 'dmg' or e.get('atkTeam') != 1: continue
    c = e.get('calc') or {}
    for f in (c.get('fold') or []):
        org = str(f.get('origin',''))
        if org in want: att_by_rule[org][e.get('attacker')] += 1
L.append(u'(d) attackers carrying selected rules:')
for org in want:
    L.append(u'    %s -> %s' % (org, json.dumps(dict(att_by_rule[org].most_common(8)), ensure_ascii=False)))
# (e) global folds: attackers
gl = collections.defaultdict(collections.Counter)
for e in ev:
    if e.get('type') != 'dmg' or e.get('atkTeam') != 1: continue
    for f in ((e.get('calc') or {}).get('fold') or []):
        if f.get('kind') == 'global':
            lab = (f.get('label') or '')
            nm = re.search(r'\[([^\]]+)\]', lab)
            gl[(nm.group(1) if nm else lab[:12], f.get('factor'))][e.get('attacker')] += 1
L.append(u'(e) global folds by (ruleName,factor) -> attackers:')
for k, v in sorted(gl.items(), key=lambda x: -sum(x[1].values())):
    L.append(u'    %s n=%d -> %s' % (json.dumps(k, ensure_ascii=False), sum(v.values()), json.dumps(dict(v.most_common(6)), ensure_ascii=False)))
# (c) cancels
cz = collections.Counter()
for e in ev:
    if e.get('type') != 'dmg': continue
    for cs in ((e.get('calc') or {}).get('cancel') or []):
        by = str(cs.get('by','')); org = str(cs.get('origin',''))
        cz[(by.split('/')[0], org.split('/')[0], str(cs.get('value')), cs.get('byUnit') or '')] += 1
L.append(u'(c) cancel (byPrefix, originPrefix, value, byUnit):')
for k, v in cz.most_common(20): L.append(u'    %s : %d' % (json.dumps(k, ensure_ascii=False), v))
# (f) given folds: attacker/side/victim relation + givers
gf = collections.Counter()
for e in ev:
    if e.get('type') != 'dmg' or e.get('atkTeam') != 1: continue
    for f in ((e.get('calc') or {}).get('fold') or []):
        if f.get('kind') == 'given':
            gf[(f.get('byUnit'), (f.get('label') or '')[:16], f.get('factor'))] += 1
L.append(u'(f) given folds (giver, label, factor):')
for k, v in gf.most_common(15): L.append(u'    %s : %d' % (json.dumps(k, ensure_ascii=False), v))
io.open(os.path.join(HERE,'contrib_recon_155c.txt'),'w',encoding='utf-8').write(u'\n'.join(L))
print('null_atk_sum=%.1f totals_dealt=%s diff=%.1f' % (null_sum, tot.get('dealt'), float(tot.get('dealt') or 0) - null_sum))
print('(b) ok=%d viol=%d noid=%d' % (ok, sum(viol.values()), noid))
