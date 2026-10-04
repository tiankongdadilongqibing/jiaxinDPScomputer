# -*- coding: utf-8 -*-
# 1.5.2 vs 1.5.1 export comparison: counting, hitValue, residual structure.
import io, json, sys, collections

out = []
def p(s): out.append(s)

def load(path):
    return json.load(io.open(path, encoding='utf-8'))

def r4(x):
    try: return round(float(x), 4)
    except Exception: return None

for path in sys.argv[1:]:
    d = load(path)
    ev = d.get('events') or []
    dmg = [e for e in ev if isinstance(e, dict) and e.get('calc')]
    hv = [e for e in dmg if e.get('hitValue') is not None]
    p('=' * 100)
    p('FILE %s' % path.split('\\')[-1])
    p('  version=%s  events=%d  damageEvents=%d  withCalc=%d' % (d.get('version'), len(ev), len(dmg), len(dmg)))
    p('  hitValue present: %d / %d' % (len(hv), len(dmg)))
    p('  factCoverage=%s' % json.dumps(d.get('factCoverage'), ensure_ascii=False))
    p('  hitDetail=%s' % json.dumps(d.get('hitDetail'), ensure_ascii=False))
    p('  reconcile=%s' % json.dumps(d.get('reconcile'), ensure_ascii=False)[:400])

    # residual histogram over damage events
    res = collections.Counter()
    for e in dmg:
        c = e.get('calc') or {}
        res[r4(c.get('residual'))] += 1
    p('  residual(top15 by count): %s' % res.most_common(15))
    p('  distinct residuals=%d' % len(res))

    # amount / hitValue
    if hv:
        ratio = collections.Counter()
        for e in hv:
            hv_ = e.get('hitValue') or 0
            if hv_:
                ratio[r4(float(e.get('amount', 0)) / float(hv_))] += 1
        p('  amount/hitValue(top15): %s' % ratio.most_common(15))
        p('  hitValue==amount count: %d ; hitValue==nominal count: %d'
          % (sum(1 for e in hv if e.get('hitValue') == e.get('amount')),
             sum(1 for e in hv if e.get('hitValue') == e.get('nominal'))))

    # unexplained = residual > 1.10 (game did more damage than our theory)
    big = collections.Counter()
    for e in dmg:
        c = e.get('calc') or {}
        rv = r4(c.get('residual'))
        if rv is not None and rv > 1.10:
            big[rv] += 1
    p('  residual>1.10 total=%d  top15=%s' % (sum(big.values()), big.most_common(15)))
    # victims behind the dominant 1.3225
    vic = collections.Counter()
    for e in dmg:
        c = e.get('calc') or {}
        if r4(c.get('residual')) == 1.3225:
            vic[e.get('victim')] += 1
    if vic:
        p('  victims of residual==1.3225: %s' % vic.most_common(8))
    # the maxAbsorbed distribution
    ma = collections.Counter()
    for e in dmg:
        c = e.get('calc') or {}
        if c.get('maxAbsorbed') is not None:
            ma[c.get('maxAbsorbed')] += 1
    if ma:
        p('  maxAbsorbed dist: %s' % sorted(ma.items()))

io.open(sys.argv[-1] if False else None or '/dev/null', 'w') if False else None
io.open('evidence_153_compare.out.txt', 'w', encoding='utf-8').write('\n'.join(out))
print('ok')
