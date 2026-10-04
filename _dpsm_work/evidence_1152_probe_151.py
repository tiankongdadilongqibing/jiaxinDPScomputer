# -*- coding: utf-8 -*-
"""Ad-hoc 1.5.1 first-run diagnosis. ASCII stdout, UTF-8 report."""
import json, os, sys, collections

EXP = sys.argv[1] if len(sys.argv) > 1 else \
    r'D:\dmmplayer\rlyehshoujotaix_cl\BepInEx\plugins\DpsMeter\exports\battle_411001_20261003_175142.json'
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '_t1151_probe.txt')
L = []
def w(s=''):
    L.append(str(s))

d = json.load(open(EXP, encoding='utf-8'))
w('root keys: %d' % len(d))
w(sorted(d.keys()))
w()
ev = d.get('events') or []
dmg = [e for e in ev if e.get('type') == 'dmg']
w('events=%d dmg=%d' % (len(ev), len(dmg)))
w()

# ---------- 1. FACT classes ----------
f = d.get('facts') or {}
items = f.get('items') or []
w('=== 1. FACT ===')
w('observed=%s classes=%s/%s liveClasses=%s/%s overflow=%s liveSkip=%s err=%s' % (
    f.get('observed'), f.get('classes'), f.get('maxClasses'),
    f.get('liveClasses'), f.get('maxLiveClasses'),
    f.get('classesOverflow'), f.get('liveSkipped'), f.get('errors')))
hist = collections.Counter(it.get('hits') for it in items)
w('class hit-count histogram (hits->classes):')
for k in sorted(hist):
    w('   hits=%-6s classes=%d' % (k, hist[k]))
covered = sum(it.get('hits', 0) for it in items)
w('sum(hits over classes) = %d   (observed=%s)' % (covered, f.get('observed')))
# which dimensions explode? group classes by (eff, ht) and by atk/vic
dim_eff = collections.Counter(it.get('eff') for it in items)
dim_ht = collections.Counter(it.get('ht') for it in items)
dim_pair = collections.Counter((it.get('atk'), it.get('vic')) for it in items)
dim_known = collections.Counter(it.get('known') for it in items)
w('distinct eff=%d  ht=%d  (atk,vic)=%d  known=%d' % (
    len(dim_eff), len(dim_ht), len(dim_pair), len(dim_known)))
w('eff distribution: %s' % (dim_eff.most_common(10),))
w('ht  distribution: %s' % (dim_ht.most_common(),))
w('known distribution top10: %s' % (dim_known.most_common(10),))
w('(atk,vic) top10: %s' % (dim_pair.most_common(10),))
w()

# ---------- 2. HITDET ----------
w('=== 2. HITDET ===')
w('hitDetail=%s' % (d.get('hitDetail'),))
esc = [e for e in dmg if e.get('calc')]
w('dmg with calc=%d' % len(esc))
mh = collections.Counter(e.get('hitMatch', 0) for e in esc)
w('hitMatch distribution (over dmg-with-calc): %s' % (sorted(mh.items()),))
def eq(a, b):
    return a is not None and b is not None and a == b
# for weak matches, which of the four candidate equalities holds?
tally = collections.Counter()
ex = []
for e in esc:
    if e.get('hitMatch', 0) != 2:
        continue
    c = e['calc']
    amt = e.get('amount'); nom = e.get('nominal')
    th = c.get('theory'); ap = c.get('applied'); ab = c.get('absorbed', 0)
    gv = (ap + ab) if ap is not None else None
    tag = []
    if eq(th, amt): tag.append('theory==amount')
    if eq(th, nom): tag.append('theory==nominal')
    if eq(ap, amt): tag.append('applied==amount')
    if eq(ap, nom): tag.append('applied==nominal')
    if eq(gv, amt): tag.append('applied+abs==amount')
    if eq(gv, nom): tag.append('applied+abs==nominal')
    tally[tuple(tag) or ('NONE',)] += 1
    if len(ex) < 12:
        ex.append((amt, nom, th, ap, ab, c.get('knownMult'), c.get('takenMult'),
                   c.get('dealtMult'), c.get('effectId'), c.get('hitType')))
for k, v in tally.most_common():
    w('  weak: %-40s %d' % (','.join(k), v))
w('weak samples (amount, nominal, theory, applied, absorbed, knownMult, takenMult, dealtMult, eff, ht):')
for t in ex:
    w('   %s' % (t,))
w()

# ---------- 3. Unexplained residuals ----------
w('=== 3. residuals ===')
res = collections.Counter()
res_by_atk = collections.defaultdict(collections.Counter)
for e in esc:
    c = e['calc']
    r = c.get('residual')
    if r is None:
        continue
    res[r] += 1
    res_by_atk[e.get('attacker')][r] += 1
w('top residual values (theory/gamevalue):')
for k, v in res.most_common(20):
    w('   r=%-10s n=%d' % (k, v))
w()
w('per-attacker: n, exact-ish(r=1.000), top3 non-1 residuals')
for atk, cnt in sorted(res_by_atk.items(), key=lambda kv: -sum(kv[1].values())):
    tot = sum(cnt.values())
    one = cnt.get(1.0, 0) + cnt.get(1, 0)
    nz = [(k, v) for k, v in cnt.most_common() if k not in (1.0, 1)]
    w('   %-24s n=%-5d r=1:%d' % (atk, tot, one))
    for k, v in nz[:3]:
        w('        r=%s n=%d' % (k, v))
w()

# ---------- 4. the 1.323 signature ----------
w('=== 4. the 1.323 multiplier ===')
sub = [e for e in esc if e['calc'].get('residual') == 1.323]
w('n=%d' % len(sub))
byatk = collections.Counter(e.get('attacker') for e in sub)
w('by attacker: %s' % (byatk.most_common(10),))
w('sample fold/cancel origin labels for those hits:')
fold = collections.Counter()
for e in sub[:400]:
    for s in (e['calc'].get('fold') or []):
        fold[(s.get('side'), s.get('kind'), s.get('origin'), s.get('factor'))] += 1
for k, v in fold.most_common(12):
    w('   %s  n=%d' % (k, v))
w()
w('=== 5. fold channel totals vs log ===')
ch = collections.Counter()
for e in esc:
    for s in (e['calc'].get('fold') or []):
        ch[s.get('kind') or s.get('channel')] += 1
w('fold by kind: %s' % (ch.most_common(),))
w('sample fold step: %s' % ((esc[0]['calc'].get('fold') or [{}])[0],))
cancel = collections.Counter()
for e in esc:
    for s in (e['calc'].get('cancel') or []):
        cancel[(s.get('origin'), s.get('by'))] += 1
w('cancel by (origin,by) top8: %s' % (cancel.most_common(8),))

open(OUT, 'w', encoding='utf-8').write('\n'.join(L) + '\n')
print('written %s  lines=%d' % (OUT, len(L)))
