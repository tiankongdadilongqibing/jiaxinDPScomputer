# -*- coding: utf-8 -*-
"""Structural probe for the contribution-attribution question: what identity/source info does a
1.5.3 export actually carry? Report -> probe_contrib_struct.txt (UTF-8); stdout ASCII.
"""
import io, json, re, collections, sys
p = sys.argv[1]
d = json.load(io.open(p, encoding='utf-8'))
L = []
def w(s=''): L.append(str(s))

w('TOP-LEVEL KEYS')
for k in d:
    v = d[k]
    if isinstance(v, list): w('  %-14s list len=%d' % (k, len(v)))
    elif isinstance(v, dict): w('  %-14s dict keys=%s' % (k, list(v.keys())[:16]))
    else: w('  %-14s %r' % (k, v))

ev = d.get('events') or []
w('')
w('EVENT KEYS: ' + ', '.join(sorted(ev[0].keys())))
w('EVENT[0]: ' + json.dumps(ev[0], ensure_ascii=False)[:2600])

pat = collections.Counter()
samp = {}
for e in ev:
    c = e.get('calc') or {}
    for f in (c.get('fold') or []):
        o = str(f.get('origin', ''))
        key = re.sub(r'[0-9]+', '#', o)
        pat[key] += 1
        samp.setdefault(key, f)
w('')
w('FOLD ORIGIN PATTERNS (all events; # = digits):')
for k, n in pat.most_common(40):
    w('  %-44s n=%-6d sample=%s' % (k, n, json.dumps(samp[k], ensure_ascii=False)[:160]))

for e in ev:
    cs = (e.get('calc') or {}).get('cancel') or []
    if cs:
        w('')
        w('SAMPLE CANCEL ENTRIES: ' + json.dumps(cs[:4], ensure_ascii=False)[:900])
        break

for sec in d:
    if sec == 'events': continue
    s = json.dumps(d[sec], ensure_ascii=False)
    w('')
    if len(s) < 2600: w('SECTION %s: %s' % (sec, s))
    else: w('SECTION %s <%d chars>: %s' % (sec, len(s), s[:900]))

import os
io.open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'probe_contrib_struct.txt'), 'w', encoding='utf-8').write(u'\n'.join(L))
print('report written; top keys: ' + ', '.join(d.keys()))
print('event keys: ' + ', '.join(sorted(ev[0].keys())))
print('distinct fold origin patterns: %d' % len(pat))