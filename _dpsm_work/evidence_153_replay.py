# -*- coding: utf-8 -*-
# OFFLINE REPLAY of what 1.5.3 would report on an ALREADY-RECORDED battle, from the per-hit data alone.
# No game, no new battle: the recorded residual is divided by exactly the factors 1.5.3 now folds.
import io, json, sys, re, collections
TOL = 0.002                      # CalcReconcile.CritInferredTolerance, the "arithmetic reproduced" band
DM = re.compile(u'\[ド.マリニーの掛け時計\][^、]*\(条件性,未计入\)')
def load(p): return json.load(io.open(p, encoding='utf-8'))
def vstat(e):
    v = (e.get('comp4') or '').replace('受击状态:', '').replace('受击方状态:', '').split('自身状态')[0]
    return [x.strip() for x in v.split('、') if x.strip()]
def near1(x): return abs(x - 1.0) <= TOL
for path in sys.argv[1:]:
    d = load(path); ev = [e for e in d['events'] if e.get('calc')]
    before = collections.Counter(); after = collections.Counter()
    n0 = n1 = 0; touch_mad = touch_dm = touch_both = 0
    single = 0; single_ok = 0
    for e in ev:
        c = e.get('calc') or {}
        try: r = float(c.get('residual') or 0)
        except: continue
        if r <= 0: continue
        vs = vstat(e)
        mad = 1.5 if '狂気' in vs else 1.0
        dm = 1.0
        if DM.search(e.get('comp2') or ''):
            k = sum(1 for s in vs if s in ('毒', '火傷'))
            if k >= 1: dm = 1.15 ** k
            if k == 1:
                single += 1
                if near1(r / (mad * dm)): single_ok += 1
        if mad != 1.0: touch_mad += 1
        if dm != 1.0: touch_dm += 1
        if mad != 1.0 and dm != 1.0: touch_both += 1
        r2 = r / (mad * dm)
        if near1(r): n0 += 1
        if near1(r2): n1 += 1
        before[round(r, 3)] += 1; after[round(r2, 3)] += 1
    print('=' * 96)
    print('%s   version=%s   hits=%d' % (path.split('\\')[-1], d.get('version'), len(ev)))
    print('  arithmetic reproduced (|residual-1| <= %.3f):  BEFORE %d (%.1f%%)  ->  AFTER %d (%.1f%%)   +%d hits'
          % (TOL, n0, 100.0*n0/len(ev), n1, 100.0*n1/len(ev), n1-n0))
    print('  hits the fix touches: victim狂気=%d  ド.マリニー=%d  both=%d' % (touch_mad, touch_dm, touch_both))
    print('  ド.マリニー single-status subset: n=%d, of which the fix closes: %d' % (single, single_ok))
    print('  residual top5 BEFORE: %s' % before.most_common(5))
    print('  residual top5 AFTER : %s' % after.most_common(5))
