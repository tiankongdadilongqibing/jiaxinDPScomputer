# -*- coding: utf-8 -*-
"""Fourth recon: do holder-owned 'text' folds duplicate the global folds on the same hit?
ASCII stdout; CJK detail -> contrib_recon_155d.txt"""
import io, json, os, sys, glob, collections, re
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.dirname(HERE)
EXPORTS = os.path.join(ROOT, 'BepInEx', 'plugins', 'DpsMeter', 'exports')
p = sys.argv[1] if len(sys.argv) > 1 else sorted([q for q in glob.glob(os.path.join(EXPORTS,'battle_*.json')) if not os.path.basename(q).startswith('battle_9999')], key=os.path.getmtime)[-1]
d = json.load(io.open(p, encoding='utf-8'))
ev = d.get('events') or []
L = []
def sig(f):
    return u'%s|%s|%s|%s' % (f.get('kind'), f.get('side'), f.get('origin'), f.get('factor'))
targets = [u'text#4/20042/c2', u'text#4/20056/c0', u'text#4/20079/c0', u'text#1/87/c1']
for tgt in targets:
    hit_rows = []
    both = 0
    for e in ev:
        if e.get('type') != 'dmg' or e.get('atkTeam') != 1: continue
        fs = (e.get('calc') or {}).get('fold') or []
        if any(str(f.get('origin')) == tgt for f in fs):
            globs = [f for f in fs if f.get('kind') == 'global']
            if globs: both += 1
            if len(hit_rows) < 3:
                hit_rows.append({'attacker': e.get('attacker'), 'amount': e.get('amount'),
                                 'knownMult': (e.get('calc') or {}).get('knownMult'),
                                 'folds': [sig(f) + '|' + (f.get('label') or '')[:22] for f in fs]})
    L.append(u'=== target %s : hitsWithTarget=%d ofWhichAlsoCarryGlobal=%d' % (tgt, sum(1 for e in ev if e.get('type')=='dmg' and e.get('atkTeam')==1 and any(str(f.get('origin'))==tgt for f in ((e.get('calc') or {}).get('fold') or []))), both))
    for r in hit_rows: L.append(u'   ' + json.dumps(r, ensure_ascii=False))
# counts of origin-prefix per hit for the holder
L.append(u'')
L.append(u'=== per-rule: hits where the rule appears, and whether a same-name global rule co-occurs ===')
pair = collections.Counter()
for e in ev:
    if e.get('type') != 'dmg' or e.get('atkTeam') != 1: continue
    fs = (e.get('calc') or {}).get('fold') or []
    ors = set(str(f.get('origin')) for f in fs)
    gl = set()
    for f in fs:
        if f.get('kind') == 'global':
            m = re.search(r'\[([^\]]+)\]', f.get('label') or '')
            if m: gl.add(m.group(1))
    for o in ors:
        if o.startswith('text#4/'):
            m = re.match(r'text#4/(\d+)/', o)
            nm = {u'20042': u'海魔の残滓', u'20056': u'母なる変異の飛沫', u'20079': u'幻惑胞子の過密爆縮'}.get(m.group(1) if m else '', '?')
            pair[(o, nm, nm in gl)] += 1
for k, v in pair.most_common(20): L.append(u'   %s : %d' % (json.dumps(k, ensure_ascii=False), v))
L.append(u'')
L.append(u'=== cancel co-occurrence: same rule family present in fold while cancel references it ===')
cc = collections.Counter()
for e in ev:
    if e.get('type') != 'dmg' or e.get('atkTeam') != 1: continue
    c = e.get('calc') or {}
    for cs in (c.get('cancel') or []):
        cc[(str(cs.get('by','')).split('/')[0], cs.get('value'))] += 1
L.append(u'   ' + json.dumps({str(k): v for k, v in cc.most_common(10)}, ensure_ascii=False))
# zero factor events: amounts
zf = []
for e in ev:
    if e.get('type') != 'dmg': continue
    for f in ((e.get('calc') or {}).get('fold') or []):
        try: fac = float(f.get('factor') or 0)
        except: fac = -1
        if fac <= 0:
            zf.append({'t': e.get('t'), 'attacker': e.get('attacker'), 'victim': e.get('victim'), 'amount': e.get('amount'),
                       'atkTeam': e.get('atkTeam'), 'origin': f.get('origin'), 'label': (f.get('label') or '')[:30], 'knownMult': (e.get('calc') or {}).get('knownMult')})
L.append(u'')
L.append(u'=== zero-factor hits (%d) ===' % len(zf))
for r in zf[:10]: L.append(u'   ' + json.dumps(r, ensure_ascii=False))
# summon ownership hints
L.append(u'')
L.append(u'=== team1 summons ===')
for a in (d.get('actors') or []):
    if a.get('team') == 1 and a.get('summon'):
        L.append(u'   ' + json.dumps({k: a.get(k) for k in ('key','name','kind','summon','dealt','abilities')}, ensure_ascii=False)[:600])
io.open(os.path.join(HERE,'contrib_recon_155d.txt'),'w',encoding='utf-8').write(u'\n'.join(L))
print('done lines=%d' % len(L))
