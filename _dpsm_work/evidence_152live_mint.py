# -*- coding: utf-8 -*-
# Does the event stream start at t=0? Scan recent exports.
import io, json, os, sys, glob
exdir = sys.argv[1]
files = sorted(glob.glob(os.path.join(exdir, '*.json')), key=os.path.getmtime)[-14:]
print('%-44s %-7s %9s %9s %7s %7s' % ('file', 'ver', 'duration', 'min_t', 'max_t', 'events'))
for f in files:
    try:
        d = json.load(io.open(f, encoding='utf-8'))
        ev = d.get('events') or []
        ts = [e.get('t', 0) for e in ev]
        print('%-44s %-7s %9s %9.3f %7.3f %7d' % (os.path.basename(f)[:44], d.get('version'),
              d.get('duration'), min(ts) if ts else -1, max(ts) if ts else -1, len(ev)))
    except Exception as e:
        print('%-44s ERR %s' % (os.path.basename(f)[:44], str(e)[:60]))
