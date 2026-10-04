# -*- coding: utf-8 -*-
"""Probe the two newest exports (round 6 intake). Read-only; prints ASCII facts."""
from __future__ import annotations
import io, json, os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path: sys.path.insert(0, HERE)
from contrib.compare import Sample, sample_credits

EXPORTS = os.path.join(os.path.dirname(HERE), 'BepInEx', 'plugins', 'DpsMeter', 'exports')
FILES = ['battle_9999_20261004_165952.json', 'battle_411001_20261004_170157.json']

def main():
    for f in FILES:
        p = os.path.join(EXPORTS, f)
        s = Sample(p)
        an = s.an
        print('==', f, os.path.getsize(p), 'bytes')
        print('   version=%s quest=%s duration=%.2f result=%s training=%s' % (s.version, s.quest, s.duration, s.result, s.training))
        print('   cap=%s' % (json.dumps(s.cap, sort_keys=True, ensure_ascii=True),))
        print('   roster=%d weak=%d nameFallback=%s' % (len(s.roster_key()), s.compWeak, s.nameFallback))
        for k in s.roster_key(): print('     ', k)
        print('   analyzable=%.2f pool_total=%.2f unattributed_credit=%.2f hits=%d identityError=%.6f'
              % (an.analyzable, an.pool_total, an.unattributed_credit, an.hits, an.identity_error()))
        print('   unattributed reasons: %s' % (json.dumps({k: v[0] for k, v in sorted(an.unattributed.items())}, sort_keys=True),))
        diag = an.diagnostics or {}
        keys = sorted(diag.keys())
        print('   diagnostics keys (%d): %s' % (len(keys), ', '.join(keys[:25])))
        creds, present = sample_credits(s)
        for ek, d in sorted(creds.items(), key=lambda kv: -kv[1]['total'])[:8]:
            print('     %-62s total=%13.1f hits=%5d strength=%s summon=%s' % (ek[:62], d['total'], d['hits'], d['strength'], d['summon']))
        print('')

if __name__ == '__main__':
    main()
