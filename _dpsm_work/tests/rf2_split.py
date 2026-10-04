# -*- coding: utf-8 -*-
"""RF2: mechanical split of src/Aggregator.cs into partial files.

Only METHODS move. Every field, const and nested struct STAYS in Aggregator.cs, because the static
constructor (field initialisation order) is the one thing a partial split can silently reorder and
partial-file order is not a language guarantee. The script refuses to write anything unless:

  * every method it finds is named in TARGETS (an unlisted method is a hard error, not a default file);
  * the MULTISET of emitted class-body lines equals the input body exactly (no line lost, none added).

It prints the per-file table so the commit diff can be reviewed against it.
"""
from __future__ import print_function
import collections, io, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
# Optional SRCDIR override so the parse + loss check can be DRY-RUN against a copy first.
# This script lives in tests/ (RF7 will give it a home); the tree it rewrites is the work dir above.
WORK = os.path.dirname(HERE) if os.path.basename(HERE) == "tests" else HERE
# Optional SRCDIR override so the parse + loss check can be DRY-RUN against a copy first.
SRCDIR = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(WORK, "src")
TARGET = os.path.join(SRCDIR, "Aggregator.cs")

MAIN = "Aggregator.cs"
SESSION = "Aggregator.Session.cs"
CLOCK = "Aggregator.Clock.cs"
ATTR = "Aggregator.Attribution.cs"
STATS = "Aggregator.Stats.cs"
FIN = "Aggregator.Finalize.cs"
FILEORDER = [MAIN, SESSION, CLOCK, ATTR, STATS, FIN]

TARGETS = {
    "InBattle": SESSION, "PendingCount": SESSION, "StartSession": SESSION,
    "EnsureSessionStarted": SESSION, "EnsureSessionStartedFor": SESSION,
    "TryResumeClosedSession": SESSION, "EndSession": SESSION, "ResetCurrent": SESSION,
    "GameUnitsPerSecond": CLOCK, "FrameDelta": CLOCK, "ClockSourceName": CLOCK, "Tick": CLOCK,
    "SampleHp": CLOCK, "BeginTimingIfNeeded": CLOCK,
    "NoteCalcActivity": ATTR, "TryGetCompForVictim": ATTR, "CalcValueMatches": ATTR,
    "TryResolveCalcSource": ATTR, "NoteHitDetail": ATTR, "RecordHitDetail": ATTR, "NoteActiveCalc": ATTR,
    "IsSameTeam": STATS, "TeamOf": STATS, "Accumulate": STATS, "RecordDamage": STATS, "RecordHeal": STATS,
    "FinalizeLocked": FIN,
    "PtrOf": MAIN, "Desc": MAIN, "NameOf": MAIN, "KindTag": MAIN,
}


def member_end(lines, start):
    depth = 0
    seen = False
    for i in range(start, len(lines)):
        for ch in lines[i]:
            if ch == "{":
                depth += 1
                seen = True
            elif ch == "}":
                depth -= 1
                if seen and depth == 0:
                    return i + 1
        if not seen and lines[i].rstrip().endswith(";"):
            return i + 1
    return len(lines)


def parse_units(body):
    units = []
    i = 0
    while i < len(body):
        j = i
        while j < len(body) and (body[j].strip() == "" or body[j].lstrip().startswith("//")):
            j += 1
        if j >= len(body):
            units.append(("keep", "(trailing blanks)", body[i:]))
            break
        end = member_end(body, j)
        unit = body[i:end]
        decl = body[j]
        has_brace = any("{" in l for l in body[j:end])
        if re.match(r"\s*(private|public|internal)?\s*(static\s+)?struct\b", decl) or not has_brace:
            units.append(("keep", (decl.strip().split()[0] if decl.strip() else "?"), unit))
        else:
            m = re.search(r"([A-Za-z_][A-Za-z0-9_]*)\s*\(", decl)
            name = m.group(1) if m else re.findall(r"[A-Za-z_][A-Za-z0-9_]*", decl)[-1]
            units.append(("method", name, unit))
        i = end
    return units


def main():
    text = io.open(TARGET, encoding="utf-8").read()
    lines = text.split("\n")
    ci = [i for i, l in enumerate(lines) if l.startswith("public static class Aggregator")]
    if len(ci) != 1:
        print("BLOCK: expected exactly one class declaration, found %d" % len(ci))
        return 1
    ci = ci[0]
    if lines[ci + 1].strip() != "{":
        print("BLOCK: class body does not open on the next line")
        return 1
    body_start = ci + 2
    close = max(i for i, l in enumerate(lines) if l.strip() == "}")
    body = lines[body_start:close]
    u_end = lines.index("")
    usings = lines[:u_end]

    units = parse_units(body)
    unknown = sorted(set(n for k, n, _ in units if k == "method" and n not in TARGETS))
    if unknown:
        print("BLOCK: %d method(s) are not in TARGETS: %s" % (len(unknown), ", ".join(unknown)))
        return 1

    out = dict((f, []) for f in FILEORDER)
    counts = collections.Counter()
    for kind, name, unit in units:
        if kind == "keep":
            out[MAIN] += unit
            counts["keep"] += 1
        else:
            f = TARGETS[name]
            out[f] += unit
            counts[f] += 1

    src_code = [l for l in body if l.strip() != ""]
    dst_code = []
    for f in FILEORDER:
        dst_code += [l for l in out[f] if l.strip() != ""]
    if collections.Counter(src_code) != collections.Counter(dst_code):
        print("BLOCK: LOSS CHECK FAILED (input %d code lines, output %d)" % (len(src_code), len(dst_code)))
        a = collections.Counter(src_code)
        b = collections.Counter(dst_code)
        for k in sorted(set(list(a) + list(b)))[:20]:
            if a.get(k) != b.get(k):
                print("   %s : %d -> %d" % (k.strip()[:70], a.get(k, 0), b.get(k, 0)))
        return 1

    for f in FILEORDER:
        print("  %-28s units=%-3d code lines=%d" % (f, counts[f], len([l for l in out[f] if l.strip()])))
    print("  %-28s kept members=%d  (fields/consts/struct stay in the facade)" % ("[kept]", counts["keep"]))
    print("  LOSS CHECK: %d code lines in, %d out, multiset equal" % (len(src_code), len(dst_code)))

    head = usings + ["", "namespace DpsMeter;", "", "public static partial class Aggregator", "{"]
    for f in FILEORDER:
        io.open(os.path.join(SRCDIR, f), "w", encoding="utf-8", newline="\n").write(
            "\n".join(head + out[f] + ["}", ""]))
    print("  wrote %d files" % len(FILEORDER))
    return 0


if __name__ == "__main__":
    sys.exit(main())