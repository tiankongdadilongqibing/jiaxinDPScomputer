# -*- coding: utf-8 -*-
"""DpsMeter per-character contribution analyzer (Stage 0, 2026-10-03).

Splits every team hit's damage into: base (attacker's own output incl. ATK-buffs/crit/residual),
and per-buff enabled shares using the log-share convention (shares sum to the buff pool exactly).
Attribution rules (all MEASURED on battle_411001_20261003_205449, see SESSION-STATE 7.2.76):
  text/talent folds (side=atk)  -> the attacker themselves (join verified 5875/5875, 0 exceptions)
  global folds                  -> ability-name join against actors[] (unique holder required)
  given folds                   -> byUnit (1.5.4+ exports name the giver); UNATTRIBUTED-excluded when absent
  madness folds                 -> byUnit (1.5.4+ hooks TalentActionAddMadness.ActExecute); excluded when absent
Usage: python dpsmeter_contrib.py [export.json]   (default: newest battle_*.json under
BepInEx/plugins/DpsMeter/exports).  Report -> _dpsm_work/contrib_report.txt ; stdout ASCII.
"""
import io, json, os, re, sys, math, glob, collections

HERE = os.path.dirname(os.path.abspath(__file__))
EXPORTS = os.path.join(HERE, 'BepInEx', 'plugins', 'DpsMeter', 'exports')
if len(sys.argv) > 1:
    path = sys.argv[1]
else:
    cands = sorted(glob.glob(os.path.join(EXPORTS, 'battle_*.json')), key=os.path.getmtime)
    if not cands:
        print('no export found under %s' % EXPORTS)
        sys.exit(0)
    path = cands[-1]
d = json.load(io.open(path, encoding='utf-8'))
ev = d.get('events') or []
actors = d.get('actors') or []
team1 = [a for a in actors if a.get('team') == 1]
t1names = set(a.get('name') for a in team1)

ab_by_id = collections.defaultdict(set)
ab_by_name = collections.defaultdict(set)
for a in actors:
    nm = a.get('name')
    for ab in (a.get('abilities') or []):
        if ab.get('id') is not None: ab_by_id[ab.get('id')].add(nm)
        if ab.get('name'): ab_by_name[ab.get('name')].add(nm)
    for t in (a.get('talents') or []):
        if t.get('abilityId') is not None: ab_by_id[t.get('abilityId')].add(nm)
        if t.get('ability'): ab_by_name[t.get('ability')].add(nm)

NAME = re.compile(r'\[([^\]]+)\]')
UNATTR = {u'狂気': u'未归因:狂気状态(无 byUnit,已排除)', u'赋予': u'未归因:赋予类(无 byUnit,已排除)',
          u'狂気场外': u'未归因:狂気施加者在队外(已排除)', u'赋予场外': u'未归因:赋予者在队外(已排除)',
          u'歧义': u'未归因:同名多持有者(已排除)'}

stats = {n: {'dealt': 0.0, 'base': 0.0, 'own': 0.0, 'team': 0.0, 'recv': 0.0, 'mad': 0.0, 'giv': 0.0, 'hits': 0, 'rules': collections.Counter()} for n in t1names}
unattr = collections.Counter()
skipped = collections.Counter()
cov = collections.Counter()
side_chk = collections.Counter()
tot_dealt = 0.0
tot_pool = 0.0
for e in ev:
    if e.get('type') != 'dmg' or e.get('atkTeam') != 1: continue
    atk = e.get('attacker')
    D = float(e.get('amount') or 0)
    if atk not in stats:
        skipped[atk or '(null)'] += D
        continue
    tot_dealt += D
    stats[atk]['dealt'] += D
    stats[atk]['hits'] += 1
    folds = (e.get('calc') or {}).get('fold') or []
    fs = []
    for f in folds:
        try: fac = float(f.get('factor') or 1.0)
        except: continue
        if fac <= 0: continue
        kind = f.get('kind'); side = f.get('side'); org = str(f.get('origin', '')); lab = f.get('label') or ''
        side_chk[(kind, side)] += 1
        owner = None
        if kind in ('text', 'talent') and side == 'atk':
            owner = atk
        elif kind in ('text', 'talent'):
            m = re.match(r'(?:text|talent)#(?:buff)?\d+/(\d+)/', org)
            holders = ab_by_id.get(int(m.group(1)), set()) if m else set()
            owner = next(iter(holders)) if len(holders) == 1 else UNATTR[u'歧义']
        elif kind == 'global':
            nm = NAME.search(lab)
            holders = ab_by_name.get(nm.group(1), set()) if nm else set()
            owner = next(iter(holders)) if len(holders) == 1 else UNATTR[u'歧义']
        elif kind == 'given':
            bu = f.get('byUnit')
            cov['given_with' if bu else 'given_without'] += 1
            if bu and bu in stats: owner = bu
            elif bu: owner = UNATTR[u'赋予场外']
            else: owner = UNATTR[u'赋予']
        elif kind == 'madness':
            bu = f.get('byUnit')
            cov['mad_with' if bu else 'mad_without'] += 1
            if bu and bu in stats: owner = bu
            elif bu: owner = UNATTR[u'狂気场外']
            else: owner = UNATTR[u'狂気']
        else:
            owner = UNATTR[u'歧义']
        fs.append((owner, fac, lab))
    M = 1.0
    for _, fac, _ in fs: M *= fac
    if not fs or abs(M - 1.0) < 1e-12:
        stats[atk]['base'] += D
        continue
    base = D / M
    pool = D - base
    tot_pool += pool
    stats[atk]['base'] += base
    lm = math.log(M)
    for owner, fac, lab in fs:
        share = pool * (math.log(fac) / lm) if lm > 0 else 0.0
        if owner in stats:
            if owner == atk: stats[atk]['own'] += share
            else:
                stats[owner]['team'] += share
                stats[atk]['recv'] += share
            nm2 = NAME.search(lab)
            stats[owner]['rules'][(nm2.group(1) if nm2 else lab[:18])] += share
        else:
            unattr[owner] += share
            if owner in (UNATTR[u'狂気'], UNATTR[u'狂気场外']): stats[atk]['mad'] += share
            elif owner in (UNATTR[u'赋予'], UNATTR[u'赋予场外']): stats[atk]['giv'] += share

L = []
def w(s=''): L.append(str(s))
w('=' * 110)
w(u'DpsMeter 队伍贡献分析(Stage 0) —— ' + os.path.basename(path))
w(u'  version=%s quest=%s duration=%.1fs result=%s' % (d.get('version'), d.get('quest'), d.get('duration', 0), d.get('result')))
w(u'  口径:对数份额(log-share)。每击 D 拆成 基础 D/M 与倍率池 D×(M-1)/M,池内按 ln(f)/ln(M) 分。')
w(u'        M=该击全部折叠倍率之积。会心与攻击力类增益不在折叠里,留在基础项(改造 D 才会拆)。残差 2.3% 同样留在基础项。')
w(u'        1.5.4 起 given/madness 折叠带 byUnit(来源角色):有 byUnit → 归因;无/队外 → 从角色贡献中排除,单列。')
w(u'  校验:入表我方 dmg 合计 = %.0f;表外攻击者 = %.0f;插件 totals.dealt = %s' % (tot_dealt, sum(skipped.values()), (d.get('totals') or {}).get('dealt')))
w('')
w(u'每行恒等式:直接伤害 = 基础 + 自身规则 + 受队友赋能 + 狂気未归因 + 赋予未归因(±取整)。')
w(u'「为团队赋能」是别人直接伤害里由我开的倍率份额,与「直接伤害」列重叠,不要相加。')
w(u'%-14s %13s %6s %12s %12s %12s %12s %12s %13s %5s' % (u'角色', u'直接伤害', u'占比', u'基础', u'自身规则', u'受队友赋能', u'狂気(未归因)', u'赋予(未归因)', u'为团队赋能', u'出手'))
for nm, s in sorted(stats.items(), key=lambda x: -x[1]['dealt']):
    if s['dealt'] <= 0 and s['team'] <= 0: continue
    w(u'%-14s %13.0f %5.1f%% %12.0f %12.0f %12.0f %12.0f %12.0f %13.0f %5d' % (nm, s['dealt'], 100.0 * s['dealt'] / tot_dealt if tot_dealt else 0, s['base'], s['own'], s['recv'], s['mad'], s['giv'], s['team'], s['hits']))
if skipped:
    w(u'')
    w(u'未入队名册的我方攻击者(计入 totals.dealt 但不进上表): %s' % dict(skipped))
w('')
w(u'倍率池总量 = %.0f(占总伤害 %.1f%%);其中未能归因:' % (tot_pool, 100.0 * tot_pool / tot_dealt if tot_dealt else 0))
for k, v in unattr.most_common():
    w(u'   %-30s %14.0f  (占池 %.1f%%)' % (k, v, 100.0 * v / tot_pool if tot_pool else 0))
w('')
w(u'各角色 top3 赋能规则(伤害当量):')
for nm, s in sorted(stats.items(), key=lambda x: -(x[1]['own'] + x[1]['team'])):
    top = s['rules'].most_common(3)
    if top: w(u'   %-16s %s' % (nm, u' · '.join(u'%s=%.0f' % (k, v) for k, v in top)))
w('')
w(u'通道×side 普查(核对 text/talent 是否全为 atk 侧): %s' % dict(side_chk))
w(u'')
w(u'byUnit 覆盖率(1.5.4+ 导出才有;1.5.3 导出应全为 without): given %d/%d, madness %d/%d' % (cov['given_with'], cov['given_with'] + cov['given_without'], cov['mad_with'], cov['mad_with'] + cov['mad_without']))

rep = os.path.join(HERE, '_dpsm_work', 'contrib_report.txt')
io.open(rep, 'w', encoding='utf-8').write(u'\n'.join(L))
print('report -> ' + rep)
print('team dealt=%.0f  pool=%.0f (%.1f%% of dealt)' % (tot_dealt, tot_pool, 100.0 * tot_pool / tot_dealt if tot_dealt else 0))
ua = sum(unattr.values())
print('byUnit coverage: given %d/%d madness %d/%d' % (cov['given_with'], cov['given_with'] + cov['given_without'], cov['mad_with'], cov['mad_with'] + cov['mad_without']))
print('unattributed pool=%.0f (%.1f%% of pool)' % (ua, 100.0 * ua / tot_pool if tot_pool else 0))
attrib = tot_pool - ua
print('attributed pool=%.0f (%.1f%% of pool)' % (attrib, 100.0 * attrib / tot_pool if tot_pool else 0))