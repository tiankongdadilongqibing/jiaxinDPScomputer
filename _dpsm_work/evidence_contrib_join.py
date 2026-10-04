# -*- coding: utf-8 -*-
"""Feasibility join for per-character contribution: how much of the fold-mass can CURRENT exports
attribute to a concrete character? Offline, any 1.5.3 export. Report -> evidence_contrib_join.txt.
"""
import io, json, os, re, sys, math, collections

p = sys.argv[1]
d = json.load(io.open(p, encoding='utf-8'))
ev = d.get('events') or []
actors = d.get('actors') or []
L = []
def w(s=''): L.append(str(s))

# ---------- 1. roster & self output ----------
team1 = [a for a in actors if a.get('team') == 1]
w('=' * 100)
w(u'1. 队伍(team=1)与自身输出(actors 段现成聚合)')
w(u'   %-18s dealt=%-12s hits=%-6s maxHit=%-9s crit=%-5s heals=%s' % ('name', 'dealt', 'hit', 'maxHit', 'crit', 'healGiven'))
for a in sorted(team1, key=lambda x: -(x.get('dealt') or 0)):
    w(u'   %-18s %-12s %-6s %-9s %-5s %s' % (a.get('name'), a.get('dealt'), a.get('hit'), a.get('maxHit'), a.get('crit'), a.get('healingGiven')))

def dealt_of(nm):
    for a in team1:
        if a.get('name') == nm: return a.get('dealt') or 0
    return 0
dmg_ev = [e for e in ev if e.get('type') == 'dmg']
t1_hits = [e for e in dmg_ev if e.get('atkTeam') == 1]
by_atk = collections.Counter()
for e in t1_hits: by_atk[e.get('attacker')] += (e.get('amount') or 0)
w(u'   事件核对: dmg 事件=%d, 我方出手=%d;按 attacker 求和 vs actors.dealt 差=%s' % (
    len(dmg_ev), len(t1_hits), {k: by_atk.get(k, 0) - dealt_of(k) for k in list(by_atk)[:6]}))
own_ne = sum(1 for e in dmg_ev if e.get('owner') and e.get('attacker') and e.get('owner') != e.get('attacker'))
w(u'   owner != attacker 的事件数 = %d(DOT/召唤归属要看这个)' % own_ne)
dot_atk = collections.Counter()
dot_n = 0
for e in t1_hits:
    c2 = e.get('comp2') or ''
    if u'来源 DOT' in c2:
        dot_n += 1
        dot_atk[e.get('attacker')] += 1
w(u'   我方「来源 DOT」击 = %d, attacker 分布 = %s' % (dot_n, dict(dot_atk)))

# ---------- 2. join tables from actors ----------
ab_by_id = collections.defaultdict(set)
ab_by_name = collections.defaultdict(set)
tal_tp = collections.defaultdict(set)
for a in actors:
    nm = a.get('name')
    for ab in (a.get('abilities') or []):
        if ab.get('id') is not None: ab_by_id[ab.get('id')].add(nm)
        if ab.get('name'): ab_by_name[ab.get('name')].add(nm)
    for t in (a.get('talents') or []):
        if t.get('abilityId') is not None: ab_by_id[t.get('abilityId')].add(nm)
        if t.get('ability'): ab_by_name[t.get('ability')].add(nm)
        pp = t.get('p') or []
        tal_tp[(t.get('type'), pp[0] if pp else None)].add(nm)
w('')
w(u'2. 联结表(来自 actors 段): 能力id %d 个, 能力名 %d 个, (talentType,param) %d 个' % (len(ab_by_id), len(ab_by_name), len(tal_tp)))
amb_n = sum(1 for v in ab_by_name.values() if len(v) > 1)
w(u'   能力名歧义(同名多持有者)= %d / %d' % (amb_n, len(ab_by_name)))

# ---------- 3. text# join test: owner should equal attacker ----------
txt_ok = txt_bad = txt_unk = 0
for e in t1_hits[:3000]:
    c = e.get('calc') or {}
    for f in (c.get('fold') or []):
        if f.get('kind') != 'text': continue
        m = re.match(r'text#(\d+)/(\d+)/', str(f.get('origin', '')))
        if not m: txt_unk += 1; continue
        owners = ab_by_id.get(int(m.group(2)), set())
        if not owners: txt_unk += 1
        elif e.get('attacker') in owners: txt_ok += 1
        else: txt_bad += 1
w('')
w(u'3. text# 联结测试(前 3000 击): 规则所属能力 id → actors 表')
w(u'   owner==attacker: %d, owner!=attacker: %d, id 查不到: %d' % (txt_ok, txt_bad, txt_unk))

# ---------- 4. fold-mass attribution over ALL team-1 hits ----------
mass = collections.Counter()
who_mass = collections.Counter()
who_hits = collections.Counter()
amb_examples = collections.Counter()
unk_examples = collections.Counter()
NAME = re.compile(r'\[([^\]]+)\]')
for e in t1_hits:
    c = e.get('calc') or {}
    fs = c.get('fold') or []
    if not fs: continue
    touched = set()
    for f in fs:
        fac = f.get('factor') or 1.0
        try: lm = abs(math.log(float(fac)))
        except: lm = 0.0
        if lm <= 0: continue
        kind = f.get('kind'); org = str(f.get('origin', '')); lab = f.get('label') or ''
        owner = None; cls = None
        if kind in ('text', 'talent'):
            m = re.match(r'(?:text|talent)#(?:buff)?(\d+)/(\d+)/', org)
            aid = int(m.group(2)) if m else None
            owners = ab_by_id.get(aid, set()) if aid is not None else set()
            if len(owners) == 1: owner = next(iter(owners)); cls = 'unique'
            elif len(owners) > 1: cls = 'ambiguous'; amb_examples[tuple(sorted(owners))] += 1
            else: cls = 'unknown-id'; unk_examples[org[:24]] += 1
        elif kind == 'global':
            nm = NAME.search(lab)
            owners = ab_by_name.get(nm.group(1), set()) if nm else set()
            if len(owners) == 1: owner = next(iter(owners)); cls = 'unique'
            elif len(owners) > 1: cls = 'ambiguous'; amb_examples[tuple(sorted(owners))] += 1
            else: cls = 'unknown-name'; unk_examples[(nm.group(1) if nm else lab[:20])[:24]] += 1
        elif kind == 'given':
            m = re.match(r'given#\d+/(-?\d+)/(-?\d+)', org)
            owners = tal_tp.get((int(m.group(1)), int(m.group(2))), set()) if m else set()
            if len(owners) == 1: owner = next(iter(owners)); cls = 'unique'
            elif len(owners) > 1: cls = 'ambiguous'; amb_examples[tuple(sorted(owners))] += 1
            else: cls = 'unknown-given'; unk_examples[org[:24]] += 1
        elif kind == 'madness':
            cls = 'mechanic-status'; owner = u'(狂気状态:施加者未记录)'
        else:
            cls = 'unknown-kind'; unk_examples[kind or '?'] += 1
        mass[cls] += lm
        if owner and cls == 'unique':
            who_mass[owner] += lm
            touched.add(owner)
    for o in touched: who_hits[o] += 1

tm = sum(mass.values())
w('')
w(u'4. 全场我方击的倍率质量归属(log 份额;总量=%.1f)' % tm)
for k, v in mass.most_common():
    w(u'   %-18s %10.1f  (%.1f%%)' % (k, v, 100.0 * v / tm if tm else 0))
w(u'   歧义样例 top5: %s' % [(list(k), n) for k, n in amb_examples.most_common(5)])
w(u'   未知样例 top8: %s' % unk_examples.most_common(8))

w('')
w(u'5. 按角色的影响面预览(unique 归属部分)')
w(u'   %-18s %-12s %-10s %s' % ('name', 'dealt', 'fold质量', '触及击数'))
for nm, v in who_mass.most_common():
    dealt = dealt_of(nm)
    w(u'   %-18s %-12s %-10.1f %d' % (nm, dealt, v, who_hits[nm]))

rep = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'evidence_contrib_join.txt')
io.open(rep, 'w', encoding='utf-8').write(u'\n'.join(L))
print('report -> ' + rep)
print('team1=%d hits=%d dot=%d owner!=atk=%d' % (len(team1), len(t1_hits), dot_n, own_ne))
print('text join: ok=%d bad=%d unk=%d' % (txt_ok, txt_bad, txt_unk))
print('mass total=%.1f: %s' % (tm, dict(mass)))