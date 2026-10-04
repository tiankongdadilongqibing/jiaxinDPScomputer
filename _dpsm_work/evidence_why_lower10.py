# -*- coding: utf-8 -*-
# Counterfactual: the removed rule [海魔の残滓] grants x1.15 per 毒/火傷 on the enemy.
# Strip it from the OLD battle to see what that party would do WITHOUT it,
# and add it to the NEW battle to see what the new party WOULD do with it.
import io, json, sys, collections
def load(p): return json.load(io.open(p, encoding='utf-8'))
def dmg(d): return [e for e in (d.get('events') or []) if e.get('calc')]
def vstat(e):
    return (e.get('comp4') or '').replace('受击方状态:','').split('自身状态')[0]

for path in sys.argv[1:]:
    d = load(path); ev = dmg(d); name = path.split('\\')[-1]
    tot = sum(e.get('amount') or 0 for e in ev)
    has_rule = sum(1 for e in ev if '海魔の残滓' in (e.get('comp2') or ''))
    ks = collections.Counter()
    stripped = 0.0
    for e in ev:
        vs = vstat(e)
        k = (1 if '毒' in vs else 0) + (1 if '火傷' in vs else 0)
        ks[k] += 1
        stripped += (e.get('amount') or 0) / (1.15 ** k)
    print('=' * 92)
    print('%s' % name)
    print('  measured total dealt            = %d' % tot)
    print('  events whose comp2 names 海魔の残滓 = %d / %d' % (has_rule, len(ev)))
    print('  enemy 毒/火傷 count per event     = %s' % dict(sorted(ks.items())))
    print('  total / 1.15^k  (rule stripped)  = %d   (%.1f%% of measured)' % (stripped, 100*stripped/tot))
    print('  total * 1.15^k  (rule added)     = %d   (%.1f%% of measured)' % (
        sum((e.get('amount') or 0) * 1.15 ** ((1 if '毒' in vstat(e) else 0) + (1 if '火傷' in vstat(e) else 0)) for e in ev), 0))
