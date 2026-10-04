# -*- coding: utf-8 -*-
"""Guard (1.7.9): the General/GivenTalent switch must drive BOTH halves of the granted-talent rule.

THE DEFECT THIS PINS (found 2026-10-04 by reading CompositionProbe.Chain.cs against real 1.7.8 data):
    CompositionProbe.Talents.cs registered a kind="given" fold step -- and incremented both "applied"
    counters -- without ever seeing Plugin.CfgGivenTalent, while CompositionProbe.Chain.cs consulted that
    switch only to decide `vicMod *= gv`. With General/GivenTalent=false an export would therefore carry
    prod(calc.fold) != dealtMult*takenMult (a fold whose factor the exported takenMult does not contain)
    and a vicGiveApplied naming entries the theory had discarded. MEASURED on the enabled path
    (battle_411001_20261004_134524, config GivenTalent=true): 2894/2894 hits carrying a kind="given" fold
    satisfy prod(fold) == dealtMult*takenMult within 3.78e-4 -- i.e. when the step IS registered the
    factor IS in the product, which is exactly why the unconditional registration is a contradiction and
    not a harmless no-op. The live config has GivenTalent=true, so the defect was latent, not silent.

WHAT IS CHECKED (source contract; no plugin build, ASCII-only output):
    1. GivenTalentDamage takes `applyToChain` and it defaults to true, so the flag-ON path is unchanged.
    2. Inside the callee, ALL FOUR side effects (mult *= f, applied++, the global GivenApplied++ and
       ctx.Add("vic","given",...)) sit inside `if (applyToChain)`, and that ctx.Add occurs exactly once
       in the whole src tree. The text/read path stays outside, which is what keeps switch-OFF a
       measurement (count + text still exported) rather than a deletion.
    3. The single production call site passes the SAME identifier that GivenFoldApplies() evaluates --
       the structural property that makes caller and callee agree by construction, not by coincidence.
    4. The pure decision helper exists (so recon_probe can exercise it offline).
    5. REGRESSION TWINS: the two sibling switches (狂気 outgoing Madness, 狂気 incoming MadnessVictim)
       already coupled registration and multiplication at one site. They must stay coupled: if a future
       refactor moves either fctx.Add out of its `if`, the same defect class is re-introduced there.
    6. The decision is exported per hit (GivenFoldOn -> "givenFoldOn" in BOTH per-hit writers), so a
       future export says which theory produced it instead of leaving it to inference.

Usage: python check_given_fold_coupling.py [--selftest]
Exit 0 = contract holds; 1 = violated. --selftest feeds mutated copies and asserts each check flips.
"""
from __future__ import print_function
import argparse, io, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, 'src')

F_TALENTS = os.path.join(SRC, 'Composition', 'CompositionProbe.Talents.cs')
F_CHAIN = os.path.join(SRC, 'Composition', 'CompositionProbe.Chain.cs')
F_BREAK = os.path.join(SRC, 'Model', 'CalcBreakdown.cs')
F_CALC = os.path.join(SRC, 'Output', 'CalcReconcile.cs')
F_FOREN = os.path.join(SRC, 'Diagnostics', 'Forensics.cs')
F_DIAG = os.path.join(SRC, 'Composition', 'CompositionProbe.Diagnostics.cs')

def read(path):
    with io.open(path, 'r', encoding='utf-8') as fh:
        return fh.read()

def matching_block(text, open_idx):
    depth = 0
    for k in range(open_idx, len(text)):
        c = text[k]
        if c == '{':
            depth += 1
        elif c == '}':
            depth -= 1
            if depth == 0:
                return open_idx, k
    return open_idx, len(text)

def span_of_if(text, if_text):
    """(start, end) of the {...} block controlled by the first occurrence of if_text, or None."""
    i = text.find(if_text)
    if i < 0:
        return None
    j = text.find('{', i + len(if_text))
    if j < 0:
        return None
    return matching_block(text, j)

def inside(span, idx):
    return span is not None and span[0] < idx < span[1]

# ------------------------------------------------------------------------------------- audit
def audit(talents, chain, breakdown, calc, forensic, diag=None):
    out = []
    def add(name, ok, detail):
        out.append((name, bool(ok), detail))

    # 1. parameter + default true
    m = re.search(r'GivenTalentDamage\(BattleObject bo,\s*int wantType,[^)]*\)', talents, re.S)
    sig = m.group(0) if m else ''
    add('1. GivenTalentDamage takes applyToChain with default true',
        'bool applyToChain = true' in sig,
        'signature: ' + ' '.join(sig.split())[-90:] if sig else 'signature not found')

    # 2. all four side effects inside if (applyToChain)
    gate = span_of_if(talents, 'if (applyToChain)')
    idx_mult = talents.find('mult *= f;')
    idx_app = talents.find('applied++;')
    idx_glob = talents.find('if (measure) GivenApplied++;')
    idx_add = talents.find('ctx.Add("vic", "given"')
    n_add = talents.count('ctx.Add("vic", "given"')
    add('2a. found the applyToChain gate and the ctx.Add side effect',
        gate is not None and idx_add > 0,
        'gate=%s ctxAdd=%d occurrence(s)' % ('yes' if gate else 'MISSING', n_add))
    add('2b. ctx.Add("vic","given",...) occurs exactly once in the callee',
        n_add == 1, 'occurrences=%d' % n_add)
    add('2c. mult *= f is inside the gate', inside(gate, idx_mult), 'idx=%d' % idx_mult)
    add('2d. applied++ is inside the gate', inside(gate, idx_app), 'idx=%d' % idx_app)
    add('2e. the global GivenApplied++ is inside the gate', inside(gate, idx_glob), 'idx=%d' % idx_glob)
    add('2f. the fold registration is inside the gate', inside(gate, idx_add), 'idx=%d' % idx_add)
    # the measurement must survive: text building stays outside the gate
    idx_sb = talents.find('sb.Append(v < 0')
    add('2g. the measurement/text path stays OUTSIDE the gate (switch-off is still a read)',
        idx_sb > 0 and not inside(gate, idx_sb), 'sb index=%d' % idx_sb)

    # 3. call site passes the same identifier the pure decision evaluates
    call = re.search(r'GivenTalentDamage\(blocker,\s*1006,[^;]*?\)\s*;', chain, re.S)
    call_txt = call.group(0) if call else ''
    passed = None
    if call_txt:
        p = re.search(r',\s*(true)\s*,\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)', call_txt)
        if p:
            passed = p.group(2)
    dec = re.search(r'GivenFoldApplies\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*,', chain)
    dec_id = dec.group(1) if dec else None
    add('3a. the production call site passes one identifier down',
        passed is not None, 'passed=%s' % passed)
    add('3b. the multiply branch evaluates the SAME identifier',
        passed is not None and dec_id == passed,
        'call passes %s, decision uses %s' % (passed, dec_id))
    n_calls = len(re.findall(r'GivenTalentDamage\(blocker,\s*1006', chain))
    add('3c. exactly one production call site', n_calls == 1, 'call sites=%d' % n_calls)

    # 4. the pure helper exists
    add('4. pure decision helper GivenFoldApplies exists',
        'internal static bool GivenFoldApplies(bool enabled, double gv)' in talents,
        'looked for the one-line predicate')

    # 5. sibling switches stay coupled
    g_out = span_of_if(chain, 'if (fold && mad != 1.0)')
    i_out = chain.find('fctx.Add("atk", "madness"')
    add('5a. Madness (outgoing) registration inside the same if as the multiply',
        inside(g_out, i_out), 'idx=%d' % i_out)
    g_in = span_of_if(chain, 'if (vicMad && (Plugin.CfgMadnessVictim')
    i_in = chain.find('fctx.Add("vic", "madness"')
    add('5b. MadnessVictim (incoming) registration inside the same if as the multiply',
        inside(g_in, i_in), 'idx=%d' % i_in)

    # 6. per-hit export of the decision
    add('6a. CalcBreakdown carries GivenFoldOn', 'public bool GivenFoldOn;' in breakdown, 'field')
    add('6b. CalcReconcile writes "givenFoldOn"', 'givenFoldOn' in calc, 'per-hit writer')
    add('6c. Forensics writes "givenFoldOn"', 'givenFoldOn' in forensic, 'per-hit writer')

    # 7. the [COMP] RE-DERIVATION must honour the same switches as the chain (1.7.10). Its own comment
    #    promises the log can never disagree with CalcBreakdown.KnownMult; before this it multiplied the
    #    given factor unconditionally (applyToChain left at its default) and ignored Plugin.CfgMadness,
    #    and it never modelled the VICTIM 狂気 fold at all.
    if diag is not None:
        add('7a. the diagnostics re-derivation passes the given switch to GivenTalentDamage',
            bool(re.search(r'GivenTalentDamage\(blocker,\s*1006,[^;]*?false\s*,\s*[A-Za-z_]', diag)),
            'looked for the call ending in ", false, <switch>)')
        add('7b. the diagnostics madness multiply is gated on Plugin.CfgMadness',
            bool(re.search(r'Plugin\.CfgMadness\s*==\s*null\s*\|\|\s*Plugin\.CfgMadness\.Value', diag)),
            'looked for the CfgMadness switch next to the madness multiply')
        add('7c. the diagnostics models the VICTIM madness fold under Plugin.CfgMadnessVictim',
            bool(re.search(r'Plugin\.CfgMadnessVictim\s*==\s*null\s*\|\|\s*Plugin\.CfgMadnessVictim\.Value', diag)),
            'looked for the CfgMadnessVictim gate on the incoming ×1.5 fold')
    return out

# ------------------------------------------------------------------------------------- selftest
def mutate(text, old, new):
    assert old in text, 'selftest mutation target missing: %r' % old[:60]
    return text.replace(old, new, 1)

def selftest():
    t0, c0, b0, k0, f0 = (read(F_TALENTS), read(F_CHAIN), read(F_BREAK), read(F_CALC), read(F_FOREN))
    d0 = read(F_DIAG)
    base = audit(t0, c0, b0, k0, f0, d0)
    bad = [n for n, ok, _ in base if not ok]
    print('baseline: %d checks, %d failing' % (len(base), len(bad)))
    for n in bad:
        print('   FAILING BASELINE: %s' % n)
    cases = []
    # M1 drop the default -> the switch could silently become hard-wired ON
    cases.append(('M1 no applyToChain parameter',
                  mutate(t0, ', bool applyToChain = true', ''),
                  c0, b0, k0, f0, '1. GivenTalentDamage takes applyToChain with default true'))
    # M2 the original defect: register outside the gate
    cases.append(('M2 registration moved outside the gate',
                  mutate(t0, 'if (applyToChain)', 'if (true)'),
                  c0, b0, k0, f0, '2f. the fold registration is inside the gate'))
    # M3 call site hard-wires true
    cases.append(('M3 caller passes literal true',
                  t0, mutate(c0, ', fctx, true, givenFold);', ', fctx, true, true);'),
                  b0, k0, f0, '3b. the multiply branch evaluates the SAME identifier'))
    # M4 multiply branch stops using the helper
    cases.append(('M4 multiply branch no longer uses GivenFoldApplies',
                  t0, mutate(c0, 'if (GivenFoldApplies(givenFold, gv))', 'if (givenFold && gv != 1.0)'),
                  b0, k0, f0, '3b. the multiply branch evaluates the SAME identifier'))
    # M5 the madness twin regresses
    cases.append(('M5 Madness (outgoing) registration escapes its if',
                  t0, mutate(c0, 'if (fold && mad != 1.0)', 'if (true)'),
                  b0, k0, f0, None))
    # M6 the decision stops being exported
    cases.append(('M6 CalcReconcile stops writing givenFoldOn',
                  t0, c0, b0, mutate(k0, 'givenFoldOn', 'foldswitch_dropped'), f0,
                  '6b. CalcReconcile writes "givenFoldOn"'))
    # M9-M11 the [COMP] re-derivation stops honouring the switches (1.7.10)
    cases.append(('M9 diagnostics drops the given switch again',
                  t0, c0, b0, k0, f0, mutate(d0, ', false, givenFoldOn);', ', false);'),
                  '7a. the diagnostics re-derivation passes the given switch to GivenTalentDamage'))
    cases.append(('M10 diagnostics madness multiply loses its switch',
                  t0, c0, b0, k0, f0,
                  mutate(d0, '(Plugin.CfgMadness == null || Plugin.CfgMadness.Value) && mad != 1.0',
                         'mad != 1.0'),
                  '7b. the diagnostics madness multiply is gated on Plugin.CfgMadness'))
    cases.append(('M11 diagnostics loses the victim madness channel',
                  t0, c0, b0, k0, f0,
                  mutate(d0, '(Plugin.CfgMadnessVictim == null || Plugin.CfgMadnessVictim.Value)',
                         'true'),
                  '7c. the diagnostics models the VICTIM madness fold under Plugin.CfgMadnessVictim'))

    failures = 0
    for case in cases:
        if len(case) == 7:
            label, tt, cc, bb, kk, ff, expect = case
            dd = d0
        else:
            label, tt, cc, bb, kk, ff, dd, expect = case
        res = audit(tt, cc, bb, kk, ff, dd)
        fails = [n for n, ok, _ in res if not ok]
        if expect is None:
            # a mutation that must be caught by SOME check but whose exact one we do not pin
            print('  [%s] %s -- %d check(s) failing' % ('PASS' if fails else 'FAIL', label, len(fails)))
            if not fails:
                failures += 1
        else:
            hit = expect in fails
            print('  [%s] %s -- expected "%s" to fail; failing=%s' % ('PASS' if hit else 'FAIL', label, expect, fails))
            if not hit:
                failures += 1
    print('---- selftest: %d/%d mutations caught' % (len(cases) - failures, len(cases)))
    return 0 if (failures == 0 and not bad) else 1

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--selftest', action='store_true')
    a = ap.parse_args()
    if a.selftest:
        return selftest()
    res = audit(read(F_TALENTS), read(F_CHAIN), read(F_BREAK), read(F_CALC), read(F_FOREN), read(F_DIAG))
    bad = 0
    for name, ok, detail in res:
        print('  [%s] %s -- %s' % ('PASS' if ok else 'FAIL', name, detail))
        if not ok:
            bad += 1
    print('---- given-talent fold coupling guard: %s (%d/%d failing)' % ('PASS' if bad == 0 else 'FAIL', bad, len(res)))
    return 0 if bad == 0 else 1

if __name__ == '__main__':
    sys.exit(main())
