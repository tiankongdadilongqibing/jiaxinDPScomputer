# -*- coding: utf-8 -*-
"""RF2 evidence: token-independent comparison of two IlDump transcripts.

Usage: python tests/il_equiv.py <pre.txt> <post.txt> [report.txt]

Raw metadata hashes CANNOT be compared across two builds: a signature blob and every IL operand that
names a member encode a CODED TOKEN (a metadata row index), and splitting one class changes the order in
which the compiler emits those rows. The comparison below therefore uses only token-independent facts:
  * each type's FIELD list in DECLARATION ORDER (the hazard a partial split can really introduce: static
    initialisation order);
  * each type's METHODS with their exact IL byte LENGTH;
  * generated names (lambda/display classes) are digit-normalised, because the compiler numbers them by the
    containing method's ordinal and the split legitimately changes that ordinal.
"""
import collections, io, re, sys


def load(p):
    raw = io.open(p, "rb").read()
    if raw[:2] in (b"\xff\xfe", b"\xfe\xff"):
        return raw.decode("utf-16").splitlines()
    return raw.decode("utf-8", "replace").splitlines()


def parse(lines):
    fields, methods = {}, {}
    cur = None
    for l in lines:
        if l.startswith("TYPE "):
            cur = l[5:]
        elif l.startswith("  FIELD "):
            m = re.match(r"  FIELD (\d+) (\S+)", l)
            if m:
                fields.setdefault(cur, []).append((int(m.group(1)), m.group(2)))
        elif l.startswith("  METHOD "):
            m = re.match(r"  METHOD (\S+) sig=\S+ il=\S+:len=(\d+)", l)
            if m:
                methods.setdefault(cur, []).append((m.group(1), int(m.group(2))))
    return fields, methods


def norm(n):
    return re.sub(r"\d+", "#", n)


def units(fields, methods):
    out = []
    for t in set(fields) | set(methods):
        out.append((norm(t),
                    tuple(sorted((i, norm(n)) for i, n in (fields.get(t) or []))),
                    tuple(sorted((norm(n), L) for n, L in (methods.get(t) or [])))))
    return sorted(out)


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    fa, ma = parse(load(sys.argv[1]))
    fb, mb = parse(load(sys.argv[2]))
    ua, ub = units(fa, ma), units(fb, mb)
    equal = ua == ub
    L = []
    L.append("pre  = %s" % sys.argv[1])
    L.append("post = %s" % sys.argv[2])
    L.append("types      : %d -> %d" % (len(ua), len(ub)))
    L.append("fields     : %d -> %d (declaration order compared per type)"
             % (sum(len(u[1]) for u in ua), sum(len(u[1]) for u in ub)))
    L.append("methods    : %d -> %d (name + exact IL byte length)"
             % (sum(len(u[2]) for u in ua), sum(len(u[2]) for u in ub)))
    L.append("EQUIVALENT : %s" % equal)
    if not equal:
        sa, sb = dict((u[0], u) for u in ua), dict((u[0], u) for u in ub)
        for k in sorted(set(sa) | set(sb)):
            if sa.get(k) != sb.get(k):
                L.append("  DIFF %s" % k)
                L.append("    pre  %s / %s" % ((sa.get(k) or (0, (), ()))[1][:6], (sa.get(k) or (0, (), ()))[2][:6]))
                L.append("    post %s / %s" % ((sb.get(k) or (0, (), ()))[1][:6], (sb.get(k) or (0, (), ()))[2][:6]))
    txt = "\n".join(L) + "\n"
    if len(sys.argv) > 3:
        io.open(sys.argv[3], "w", encoding="utf-8", newline="\n").write(txt)
    print(txt)
    return 0 if equal else 1


if __name__ == "__main__":
    sys.exit(main())