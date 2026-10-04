# -*- coding: utf-8 -*-
"""Compare residual structure between the 1.5.0 battle and the 1.5.1 battle, and dump
full calc specimens so the remaining unexplained classes can be named. ASCII stdout."""
import json, os, sys, collections

E = r'D:\dmmplayer\rlyehshoujotaix_cl\BepInEx\plugins\DpsMeter\exports'
A = os.path.join(E, 'battle_411001_20261003_173710.json')   # 1.5.0
B = os.path.join(E, 'battle_411001_20261003_175142.json')   # 1.5.1
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '_t1151_cmp.txt')
L = []
def w(s=''):
    L.append(str(s))

def load(p):
    return json.load(open(p, encoding='utf-8'))

def resid(d):
    """per attacker: n, r==1 count, top non-1 residuals; plus global histogram"""
    hist = collections.Counter()
    by = collections.defaultdict(collections.Counter)
    for e in d['events']:
        if e.get('type') != 'dmg':
            continue
        c = e.get('calc')
        if not c:
            continue
        r = c.get('residual')
        if r is None:
            continue
        hist[r] += 1
        by[e.get('attacker')][r] += 1
    return hist, by

da, db = load(A), load(B)
for tag, d in (('1.5.0 battle 173710', da), ('1.5.1 battle 175142', db)):
    rc = d.get('reconcile') or {}
    w('===== %s  version=%s =====' % (tag, d.get('version')))
    w('  withCalc=%s exact=%s(%.1f%%) exactWithCrit=%s(%.1f%%) critInferred=%s approx=%s unexplained=%s' % (
        rc.get('withCalc'), rc.get('exact'),
        100.0 * rc.get('exact', 0) / max(1, rc.get('withCalc', 1)),
        rc.get('exactWithCrit'),
        100.0 * rc.get('exactWithCrit', 0) / max(1, rc.get('withCalc', 1)),
        rc.get('critInferred'), rc.get('approx'), rc.get('unexplained')))
    h, by = resid(d)
    w('  residual histogram top12 (r = gamevalue/theory; r>1 => we UNDER-counted):')
    for k, v in h.most_common(12):
        w('     r=%-9s n=%d' % (k, v))
    w('  per-attacker (n with residual, r==1, top3 non-1):')
    for atk, cnt in sorted(by.items(), key=lambda kv: -sum(kv[1].values())):
        tot = sum(cnt.values())
        one = cnt.get(1.0, 0) + cnt.get(1, 0)
        nz = [(k, v) for k, v in cnt.most_common() if k not in (1.0, 1)]
        w('     %-22s tot=%-5d r=1:%-5d  %s' % (
            atk, tot, one, ', '.join('r=%s n=%d' % (k, v) for k, v in nz[:3])))
    w()

# ---- specimen dump from the 1.5.1 battle: one hit per dominant unexplained residual ----
w('===== specimens (1.5.1) =====')
db_ev = [e for e in db['events'] if e.get('type') == 'dmg' and e.get('calc')]
want = [(1.9, 'マッドシーカー'), (1.323, 'メアリー'), (1.65, 'ネーフェ＝ジアー'), (1.15, None)]
for r, atk in want:
    got = None
    for e in db_ev:
        c = e['calc']
        if c.get('residual') != r:
            continue
        if atk and e.get('attacker') != atk:
            continue
        got = e
        break
    if got is None:
        w('  -- no specimen for r=%s atk=%s' % (r, atk))
        continue
    c = got['calc']
    w('  ---- r=%s atk=%s vic=%s eff=%s ht=%s' % (
        r, got.get('attacker'), got.get('victim'), c.get('effectId'), c.get('hitType')))
    w('       amount=%s nominal=%s theory=%s applied=%s absorbed=%s known=%s' % (
        got.get('amount'), got.get('nominal'), c.get('theory'), c.get('applied'),
        c.get('absorbed'), c.get('knownMult')))
    w('       attrMult=%s dealtMult=%s takenMult=%s base=%s ratio=%s' % (
        c.get('attrMult'), c.get('dealtMult'), c.get('takenMult'), c.get('base'), c.get('ratio')))
    w('       critRate=%s critDamageRate=%s madnessRatio=%s responsibility=%s maxAbsorbed=%s' % (
        c.get('critRate'), c.get('critDamageRate'), c.get('madnessRatio'),
        c.get('responsibility'), c.get('maxAbsorbed')))
    w('       vicGive=%s vicGiveApplied=%s vicBuffs=%s vicExtra=%s' % (
        c.get('vicGive'), c.get('vicGiveApplied'), c.get('vicBuffs'), c.get('vicExtra')))
    w('       pair=%s pairTrusted=%s valueMatches=%s' % (
        c.get('pair'), c.get('pairTrusted'), c.get('valueMatches')))
    w('       vicGiveText=%s' % (c.get('vicGiveText'),))
    for s in (c.get('fold') or []):
        w('       fold  %-4s %-8s %-28s x%-8s %s' % (
            s.get('side'), s.get('kind'), s.get('origin'), s.get('factor'), s.get('label')))
    for s in (c.get('cancel') or []):
        w('       CXL  %-28s v=%-8s by=%-28s %s' % (
            s.get('origin'), s.get('value'), s.get('by'), s.get('label')))
    w()

open(OUT, 'w', encoding='utf-8').write('\n'.join(L) + '\n')
print('written %s lines=%d' % (OUT, len(L)))
