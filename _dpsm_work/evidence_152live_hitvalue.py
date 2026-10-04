# -*- coding: utf-8 -*-
# What IS the game's calc return? Compare hitValue against our reconstructed theory / power / amount.
import io, json, sys, collections

path = sys.argv[1]
d = json.load(io.open(path, encoding='utf-8'))
dmg = [e for e in (d.get('events') or []) if e.get('calc')]
print('damage events=%d' % len(dmg))

def r4(x):
    try: return round(float(x), 4)
    except Exception: return None

th_hv = collections.Counter(); pw_hv = collections.Counter(); am_hv = collections.Counter()
hv_le_amount = 0; hv_gt_amount = 0; hv_eq_power = 0; hv_eq_theory = 0
hv_zero = 0
for e in dmg:
    hv = e.get('hitValue')
    if hv is None or hv == 0:
        hv_zero += 1
        continue
    c = e.get('calc') or {}
    th = c.get('theory'); pw = c.get('power'); am = e.get('amount')
    if th is not None: th_hv[r4(float(th) / hv)] += 1
    if pw is not None: pw_hv[r4(float(pw) / hv)] += 1
    if am is not None: am_hv[r4(float(am) / hv)] += 1
    if am is not None:
        if hv <= am: hv_le_amount += 1
        else: hv_gt_amount += 1
    if pw == hv: hv_eq_power += 1
    if th == hv: hv_eq_theory += 1

print('hitValue absent/zero      : %d' % hv_zero)
print('hitValue <= amount        : %d   hitValue > amount: %d' % (hv_le_amount, hv_gt_amount))
print('hitValue == power         : %d' % hv_eq_power)
print('hitValue == theory        : %d' % hv_eq_theory)
print('')
print('theory / hitValue  top15 : %s' % th_hv.most_common(15))
print('power  / hitValue  top15 : %s' % pw_hv.most_common(15))
print('amount / hitValue  top15 : %s' % am_hv.most_common(15))

# for the events where theory == hitValue, what is the residual?
same = [e for e in dmg if (e.get('calc') or {}).get('theory') == e.get('hitValue')]
res = collections.Counter(r4((e.get('calc') or {}).get('residual')) for e in same)
print('')
print('when theory == hitValue (n=%d), residual dist: %s' % (len(same), res.most_common(10)))

# where theory/hitValue is NOT 1, is the residual 1?
notsame = [e for e in dmg if (e.get('calc') or {}).get('theory') not in (None, e.get('hitValue'))]
res2 = collections.Counter(r4((e.get('calc') or {}).get('residual')) for e in notsame)
print('when theory != hitValue (n=%d), residual dist: %s' % (len(notsame), res2.most_common(10)))
