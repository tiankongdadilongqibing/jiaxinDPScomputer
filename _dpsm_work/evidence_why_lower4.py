# -*- coding: utf-8 -*-
import io, json, sys
def load(p): return json.load(io.open(p, encoding='utf-8'))
def pick(d, attacker, eff):
    for e in (d.get('events') or []):
        c = e.get('calc') or {}
        if e.get('attacker') == attacker and c.get('effectId') == eff:
            return e
    return None
A=load(sys.argv[1]); B=load(sys.argv[2])
for (att, eff) in [('マッドシーカー',0), ('ネーフェ＝ジアー',0), ('メアリー',10002)]:
    print('#' * 100)
    print('### %s  eff=%s' % (att, eff))
    for tag, d in (('OLD 183213', A), ('NEW 191159', B)):
        e = pick(d, att, eff)
        if e is None:
            print('  %s: no such hit' % tag); continue
        c = e.get('calc') or {}
        print('  ---- %s  amount=%s  residual=%s' % (tag, e.get('amount'), c.get('residual')))
        print('     power=%s base=%s theory=%s | attr=%s dealt=%s taken=%s known=%s'
              % (c.get('power'), c.get('base'), c.get('theory'), c.get('attrMult'), c.get('dealtMult'), c.get('takenMult'), c.get('knownMult')))
        print('     comp : %s' % e.get('comp'))
        print('     comp2: %s' % e.get('comp2'))
        print('     comp3: %s' % e.get('comp3'))
        print('     comp4: %s' % e.get('comp4'))
