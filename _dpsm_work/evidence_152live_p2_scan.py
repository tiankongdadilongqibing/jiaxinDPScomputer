# -*- coding: utf-8 -*-
# P2 invariant scan: matchExact+matchPair+matchNone == factCoverage.dmg, across export history.
import io, os, re, sys, glob, collections

exdir = sys.argv[1]
files = sorted(glob.glob(os.path.join(exdir, '*.json')), key=os.path.getmtime)
TAIL = 2 * 1024 * 1024

re_ver = re.compile(r'"version"\s*:\s*"([^"]+)"')
re_hd  = re.compile(r'"hitDetail"\s*:\s*\{([^}]*)\}')
re_fc  = re.compile(r'"factCoverage"\s*:\s*\{([^}]*)\}')
re_dmg = re.compile(r'"dmg"\s*:\s*(\d+)')

def num(body, k):
    m = re.search(r'"%s"\s*:\s*(-?\d+)' % k, body)
    return int(m.group(1)) if m else None

rows = []
for f in files:
    try:
        sz = os.path.getsize(f)
        with io.open(f, 'rb') as fh:
            head = fh.read(256).decode('utf-8', 'replace')
            if sz > TAIL:
                fh.seek(sz - TAIL)
            tail = fh.read().decode('utf-8', 'replace')
        blob = head + '\n' + tail
        mh = re_hd.search(blob)
        if not mh:
            rows.append(dict(f=os.path.basename(f), ver='nohd', ok=None))
            continue
        body = mh.group(1)
        prod, trim, err = num(body, 'produced'), num(body, 'trimmed'), num(body, 'errors')
        me, mp, mn = num(body, 'matchExact'), num(body, 'matchPair'), num(body, 'matchNone')
        fc = re_fc.search(blob)
        dmg = None
        if fc:
            dmg = num(fc.group(1), 'dmg')
        ver = re_ver.search(blob)
        matched = None if None in (me, mp, mn) else me + mp + mn
        rows.append(dict(f=os.path.basename(f), ver=ver.group(1) if ver else '?',
                         produced=prod, exact=me, pair=mp, none=mn, matched=matched, dmg=dmg))
    except Exception as e:
        rows.append(dict(f=os.path.basename(f), ver='ERR', ok=None, e=str(e)[:80]))

out = []
p = out.append
have = [r for r in rows if r.get('matched') is not None and r.get('dmg') is not None]
p('exports scanned       : %d' % len(rows))
p('exports with hitDetail: %d' % len(have))
p('exports without       : %d' % (len(rows) - len(have)))
p('')
p('%-44s %-7s %7s %6s %6s %5s %8s %7s %-12s' % ('file', 'ver', 'prod', 'exact', 'pair', 'none', 'matched', 'dmg', 'delta'))
bad = []
for r in have:
    d = r['matched'] - r['dmg']
    if d: bad.append(r)
    p('%-44s %-7s %7s %6s %6s %5s %8s %7s %-12s' % (r['f'][:44], r['ver'], r['produced'], r['exact'], r['pair'], r['none'], r['matched'], r['dmg'], ('OK' if d == 0 else '%+d' % d)))
p('')
p('MISMATCH count: %d' % len(bad))
p('by version (ver: OK / BAD):')
c = collections.Counter()
for r in have:
    c[(r['ver'], 'OK' if r['matched'] == r['dmg'] else 'BAD')] += 1
for k in sorted(c):
    p('   %-10s %-4s %d' % (k[0], k[1], c[k]))
p('')
p('per-export delta vs produced: produced - matched =')
for r in have:
    p('   %-44s prod=%6s matched=%6s dmg=%6s  produced-matched=%+5d' % (r['f'][:44], r['produced'], r['matched'], r['dmg'], (r['produced'] or 0) - r['matched']))
io.open('evidence_153_p2_scan.out.txt', 'w', encoding='utf-8').write('\n'.join(out))
print('ok have=%d bad=%d' % (len(have), len(bad)))
