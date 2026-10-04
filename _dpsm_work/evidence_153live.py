# -*- coding: utf-8 -*-
"""1.5.3 LIVE verification: runs the falsification table of DpsMeter-1.5.3 section 6 on the first
real battle after the two fixes. UTF-8 report -> _dpsm_work/evidence_153live.txt; stdout is ASCII.
Note: comp2 renders fold factors with 2 decimals (x1.32 = 1.3225), so the DM check compares against
round(1.15**k, 2).
"""
import io, json, os, re, sys, collections

TOL = 0.002
path = sys.argv[1]
d = json.load(io.open(path, encoding='utf-8'))
L = []
def w(s=''): L.append(s)

ev = d.get('events') or []
hits = [e for e in ev if (e.get('calc') or {}).get('residual')]

def C(e): return e.get('calc') or {}
def R(e):
    try: return float(C(e).get('residual') or 0)
    except: return 0.0
def VS(e):
    v = C(e).get('victimStatuses')
    if isinstance(v, list): return [str(x).strip() for x in v]
    c4 = e.get('comp4') or ''
    i = c4.find(u'受击方状态:')
    if i < 0: i = c4.find(u'受击状态:')
    seg = c4[i:] if i >= 0 else ''
    j = seg.find(u'自身状态')
    if j >= 0: seg = seg[:j]
    seg = seg.replace(u'受击方状态:', '').replace(u'受击状态:', '')
    return [x.strip() for x in seg.split(u'、') if x.strip()]
def CR(e):
    c = C(e)
    try: return float(c.get('critRate') or 0), float(c.get('critDamageRate') or 0)
    except: return 0.0, 0.0
def critX(e, r):
    cr, cd = CR(e)
    return cr > 0 and cd > 100 and abs(r - cd / 100.0) <= TOL
def clauses(e): return [s for s in (e.get('comp2') or '').split(u'、') if s.strip()]
def parked(e):
    out = []
    for seg in clauses(e):
        if u'条件性,未计入' in seg:
            m = re.search(r'\[([^\]]+)\]', seg)
            out.append(m.group(1) if m else seg[:14])
    return out

w('=' * 100)
w(u'1.5.3 实机验证 —— ' + os.path.basename(path))
w(u'  version=%s quest=%s result=%s duration=%.1fs events=%d hits=%d' % (d.get('version'), d.get('quest'), d.get('result'), d.get('duration', 0), len(ev), len(hits)))

ne = nc = nu = 0
for e in hits:
    r = R(e)
    if r <= 0: continue
    if abs(r - 1.0) <= TOL: ne += 1
    elif critX(e, r): nc += 1
    else: nu += 1
tot = ne + nc + nu
w('')
w(u'A. KPI(|residual-1| <= %.3f;同队 1.5.2 实机 = 33.1%%;离线重放预测 = 82.7%%)' % TOL)
w(u'   exact          %6d (%.1f%%)' % (ne, 100.0 * ne / tot))
w(u'   exactWithCrit  %6d (%.1f%%)' % (ne + nc, 100.0 * (ne + nc) / tot))
w(u'   unexplained    %6d (%.1f%%)' % (nu, 100.0 * nu / tot))

hist = collections.Counter()
for e in hits:
    r = R(e)
    if r > 0: hist[round(r, 3)] += 1
w('')
w(u'B. 残差直方图(top 12;1.5.2 时代这里是 1.5/2.85/1.984/1.323/1.322/1.15)')
for k, n in hist.most_common(12): w(u'   %-8s %5d' % (k, n))

DM = re.compile(u'\[ド・マリニーの掛け時計\][^、]*')
dm_n = dm_parked = 0
dm_val = collections.Counter()
dm_ok = dm_bad = 0
for e in hits:
    m = DM.search(e.get('comp2') or '')
    if not m: continue
    dm_n += 1
    seg = m.group(0)
    if u'条件性,未计入' in seg:
        dm_parked += 1; dm_val['PARKED'] += 1; continue
    mm = re.search(u'→×([0-9.]+)', seg)
    v = float(mm.group(1)) if mm else -1.0
    dm_val[v] += 1
    k = sum(1 for s in VS(e) if s in (u'毒', u'火傷'))
    exp = 1.15 ** k if k >= 1 else 0.0
    if k >= 1 and abs(v - round(exp, 2)) < 1e-9: dm_ok += 1
    else: dm_bad += 1
w('')
w(u'C. ド・マリニーの掛け時計(1.5.2 实测:folded 0 / parked 960)')
w(u'   出现 %d 击: parked %d, 折叠值分布 %s(comp2 显示 2 位小数,×1.32 即 ×1.3225)' % (dm_n, dm_parked, dict(dm_val)))
w(u'   折叠值与 round(1.15^k,2)(受击方 毒/火傷 个数)一致: %d 击 OK, %d 击不符' % (dm_ok, dm_bad))

vm_n = vm_mism = vm_fold = 0
vm_res = collections.Counter()
mad_orig = collections.Counter()
for e in hits:
    c = C(e)
    vm = bool(c.get('victimMadnessOn'))
    has = any(u'狂気' in s for s in VS(e))
    if vm != has: vm_mism += 1
    for f in (c.get('fold') or []):
        if str(f.get('kind', '')) == 'madness':
            mad_orig[str(f.get('origin', ''))] += 1
    if vm:
        vm_n += 1
        r = R(e)
        if abs(r - 1.0) <= TOL: vm_res['exact'] += 1
        elif critX(e, r): vm_res['crit'] += 1
        else: vm_res[round(r, 3)] += 1
        if any('vicmadness' in str(f.get('origin', '')) for f in (c.get('fold') or [])): vm_fold += 1
w('')
w(u'D. 受击方狂気(1.5.2 实测:3224 击残差 1.5,完全不折)')
w(u'   victimMadnessOn 击数 = %d, 其中有 vicmadness#150 折叠 = %d' % (vm_n, vm_fold))
w(u'   victimMadnessOn 与 victimStatuses 不一致 = %d(必须为 0)' % vm_mism)
w(u'   这些击的残差: %s' % dict(vm_res))
w(u'   madness 通道全部 origin: %s' % dict(mad_orig))

am_n = 0
am_res = collections.Counter()
for e in hits:
    c = C(e)
    if c.get('madnessOn') and not c.get('victimMadnessOn'):
        am_n += 1
        r = R(e)
        if abs(r - 1.0) <= TOL: am_res['exact'] += 1
        elif critX(e, r): am_res['crit'] += 1
        else: am_res[round(r, 3)] += 1
w('')
w(u'E. 攻方狂気(旧队 1.5.2 实测:受方非狂気时残差 1.3022 —— 当时未解)')
w(u'   攻方狂気且受方非狂気 = %d 击, 残差: %s' % (am_n, dict(am_res)))

un = collections.Counter()
un_vs = collections.Counter()
un_pk = collections.Counter()
samples = {}
for i, e in enumerate(hits):
    r = R(e)
    if r <= 0 or abs(r - 1.0) <= TOL or critX(e, r): continue
    k = round(r, 3)
    un[k] += 1
    un_vs[u','.join(sorted(set(VS(e))))] += 1
    for name in parked(e): un_pk[name] += 1
    if k not in samples: samples[k] = i
w('')
w(u'F. 未解释击(%d)分带' % nu)
for k, n in un.most_common(10):
    w(u'   residual=%-8s n=%-4d 事件#%d' % (k, n, samples.get(k, -1)))
w(u'   未解释击的受击方状态 top5: %s' % un_vs.most_common(5))
w(u'   未解释击的被搁置规则 top5: %s' % un_pk.most_common(5))
for k, n in un.most_common(3):
    i = samples.get(k)
    if i is not None:
        e = hits[i]
        w(u'   -- residual=%s 样本(事件#%d) comp2: %s' % (k, i, (e.get('comp2') or '')[:200]))
        w(u'      comp4: %s' % ((e.get('comp4') or '')[:120]))
        c = C(e)
        w(u'      critRate=%s critDamageRate=%s madnessOn=%s victimMadnessOn=%s' % (c.get('critRate'), c.get('critDamageRate'), c.get('madnessOn'), c.get('victimMadnessOn')))

pk = collections.Counter()
fd = collections.Counter()
for e in hits:
    for seg in clauses(e):
        m = re.search(r'\[([^\]]+)\]', seg)
        if not m: continue
        if u'条件性,未计入' in seg: pk[m.group(1)] += 1
        elif u'→×' in seg: fd[m.group(1)] += 1
w('')
w(u'G. 规则停车普查:被搁置(条件性,未计入) / 被折叠')
w(u'   parked: %s' % pk.most_common(8))
w(u'   folded: %s' % fd.most_common(8))

nco = sum(1 for e in ev if e.get('critObserved'))
w('')
w(u'H. CritObserved = %d / %d(P3:会心观测通道,预期仍为 0)' % (nco, len(ev)))
rec = d.get('reconcile') or {}
w(u'   reconcile: %s' % json.dumps(rec, ensure_ascii=False)[:400])

oc_n = oc_dot = 0
oc_rules = collections.Counter()
dot_tot = dot_oc = dot_ex = 0
for e in hits:
    r = R(e)
    if r <= 0: continue
    c2 = e.get('comp2') or ''
    isdot = (u'来源 DOT' in c2)
    if isdot: dot_tot += 1
    over = (r < 1.0 - TOL) and not critX(e, r)
    if isdot and over: dot_oc += 1
    if isdot and abs(r - 1.0) <= TOL: dot_ex += 1
    if over:
        oc_n += 1
        if isdot: oc_dot += 1
        for seg in clauses(e):
            mm = re.search(u'→×([0-9.]+)', seg)
            if mm:
                nm = re.search(r'\[([^\]]+)\]', seg)
                oc_rules[(nm.group(1) if nm else '?', mm.group(1))] += 1
w('')
w(u'I. 多算击(residual<1,非会心)与「来源 DOT」的关系 —— 0.87≈1/1.15, 0.826≈1/1.1^2')
w(u'   多算击总数 %d, 其中 comp2 带「来源 DOT」: %d' % (oc_n, oc_dot))
w(u'   全场「来源 DOT」击: %d, 其中多算 %d, exact %d' % (dot_tot, dot_oc, dot_ex))
w(u'   多算击上仍被折叠的规则 top8: %s' % oc_rules.most_common(8))

dev = collections.Counter()
for e in hits:
    cr, cd = CR(e)
    r = R(e)
    if cr <= 0 or cd <= 100 or r <= 0: continue
    if abs(r - 1.0) <= TOL: continue
    dev[round(abs(r - cd / 100.0), 4)] += 1
w('')
w(u'J. 会心击的残差偏离 |r - cd/100| 分布(判据容差=0.002)')
for k, n in sorted(dev.items())[:14]:
    w(u'   dev=%-8s n=%d %s' % (k, n, 'PASS' if k <= TOL else 'MISS'))

if len(sys.argv) > 2:
    d2 = json.load(io.open(sys.argv[2], encoding='utf-8'))
    ev2 = d2.get('events') or []
    w('')
    w(u'K. 另一份导出: %s  version=%s quest=%s result=%s duration=%.1fs events=%d' % (os.path.basename(sys.argv[2]), d2.get('version'), d2.get('quest'), d2.get('result'), d2.get('duration', 0), len(ev2)))

rep = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'evidence_153live.txt')
io.open(rep, 'w', encoding='utf-8').write(u'\n'.join(L))
print('report -> ' + rep)
print('KPI: exact=%d (%.1f%%) crit=%d unexplained=%d' % (ne, 100.0 * ne / tot, nc, nu))
print('DM: n=%d parked=%d ok=%d bad=%d' % (dm_n, dm_parked, dm_ok, dm_bad))
print('VMAD: n=%d fold=%d mismatch=%d' % (vm_n, vm_fold, vm_mism))
print('AMAD: n=%d exact=%d' % (am_n, am_res.get('exact', 0)))
print('OVERCOUNT: n=%d of which DOT=%d | DOT hits total=%d oc=%d exact=%d' % (oc_n, oc_dot, dot_tot, dot_oc, dot_ex))
print('CRITDEV (|r-cd/100| -> n): %s' % sorted(dev.items())[:10])
print('CRITOBS=%d' % nco)