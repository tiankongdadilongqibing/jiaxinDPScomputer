# -*- coding: utf-8 -*-
import io, os, sys
f = sys.argv[1]
sz = os.path.getsize(f)
data = None
with io.open(f, 'rb') as fh:
    head = fh.read(4000)
print('size', sz)
print('head 600 bytes repr:')
print(repr(head[:600]))
for key in [b'"hitDetail"', b'"factCoverage"', b'"version"', b'"events"', b'"reconcile"']:
    with io.open(f, 'rb') as fh:
        blob = fh.read()
    i = blob.find(key)
    print('%-18s first offset=%s  last offset=%s' % (key.decode(), i, blob.rfind(key)))
    del blob
