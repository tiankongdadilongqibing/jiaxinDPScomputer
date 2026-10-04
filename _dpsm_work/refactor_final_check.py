# -*- coding: utf-8 -*-
"""Integrity + consistency check of the refactored source tree -- with REAL blocking (N1).

2026-10-03: the scan used to walk only `src/`, and that gap cost a file: `recon_probe/Program.cs` was
rewritten once by a PowerShell whole-file read/write and came back as ANSI -- no longer valid UTF-8,
one line short. Nothing failed here because the guard never looked at it.

2026-10-04 / N1 (roadmap section N1): the guard used to PRINT every category and exit 0 unless the
BuildInfo/csproj version pair disagreed. A check that cannot say no is not a check, so the categories
are now split, and every BLOCK below has a tamper-a-copy negative control in --selftest that runs THIS
CLI (not a flat function):

  BLOCK (exit 1): a .cs that is not valid UTF-8 | a U+FFFD in a .cs | a REQUIRED source file missing |
                  BuildInfo.Version != csproj <Version> | a partial CLASS GROUP whose member does not
                  declare `partial`, or that is empty | a dead-symbol REFERENCE IN CODE

The scanned roots include tests/ (RF1): the behaviour suite compiles production sources, so a corrupted
test file is a broken test, not a harmless one.
  WARN  (exit 0): mojibake suspects -- the heuristic matches legitimate Latin-1-ish text, so it is a
                  review item, never a verdict | a dead-symbol name that appears ONLY in comments or
                  string literals (prose, not a reference)

Usage: python refactor_final_check.py [--root DIR] [--selftest]
ASCII-only stdout (GBK console).
"""
import argparse, glob, io, os, re, shutil, subprocess, sys, tempfile

Q = chr(34)
PAT = re.compile('[\u4e00-\u9fff]')
MOJI = re.compile('[\u00c3\u00c2\u00e5\u00e6\u00e7\u00e8\u00e9\u00ea\u00eb\u00ec\u00ed\u00ee\u00ef\u00f0\u00f1\u00f2\u00f3\u00f4\u00f5\u00f6\u00f8\u00f9\u00fa\u00fb\u00fc\u00fd\u00fe\u00ff]|\ufffd|\u20ac')
DEAD = re.compile("StatsGivenDamageHook|StatsTakenDamageHook|StatsGivenHealHook|StatsTakenHealHook"
                  "|StatsAttackHook|DamageTakeOverHook|ChangeLifeHook|ObscuredToInt")
REQUIRED = ["src/BuildInfo.cs", "src/DpsMeter.csproj", "src/Composition/CharacterNames.cs", "src/Plugin.cs"]
PARTIAL_GROUPS = (("src/Composition/CompositionProbe*.cs", "CompositionProbe"),
                  ("src/Ui/OverlayUGUI*.cs", "OverlayUGUI"),
                  # RF2: Aggregator was split into a facade plus five responsibility partials. The group is
                  # checked by the SAME rule as the other two, so a new part that forgets `partial`, or a
                  # part that ends up empty, blocks instead of compiling only by accident.
                  ("src/Aggregator*.cs", "Aggregator"))


def code_only(t):
    """Drop // and /* */ comments and string/char literals, so a dead name in PROSE is not a block."""
    out = []
    i = 0
    n = len(t)
    while i < n:
        c = t[i]
        if c == "/" and i + 1 < n and t[i + 1] == "/":
            j = t.find(chr(10), i)
            i = n if j < 0 else j
        elif c == "/" and i + 1 < n and t[i + 1] == "*":
            j = t.find("*/", i + 2)
            i = n if j < 0 else j + 2
        elif c == Q:
            i += 1
            while i < n:
                if t[i] == chr(92):
                    i += 2
                    continue
                if t[i] == Q:
                    i += 1
                    break
                i += 1
        elif c == chr(39):
            i += 1
            while i < n:
                if t[i] == chr(92):
                    i += 2
                    continue
                if t[i] == chr(39):
                    i += 1
                    break
                i += 1
        else:
            out.append(c)
            i += 1
    return "".join(out)


def check(root):
    blocks, warns, info = [], [], []
    files = 0
    cjk = 0
    unreadable, fffd, moji, dead_code, dead_prose = [], [], [], [], []
    # RF1 added tests/BehaviorTests: new production-executing sources must be under the same
    # encoding/dead-symbol guard as src, or the next PowerShell rewrite of a test file is invisible here.
    for top in ("src", "recon_probe", "test", "tests"):
        d = os.path.join(root, top)
        if not os.path.isdir(d):
            continue
        for r, dirs, fns in os.walk(d):
            dirs[:] = [x for x in dirs if x not in ("bin", "obj")]
            for fn in sorted(fns):
                if not fn.endswith(".cs"):
                    continue
                p = os.path.join(r, fn)
                rel = os.path.relpath(p, root).replace(os.sep, "/")
                files += 1
                try:
                    t = io.open(p, encoding="utf-8").read()
                except UnicodeDecodeError as ex:
                    unreadable.append("%s (%s)" % (rel, ex.reason))
                    continue
                except Exception as ex:
                    unreadable.append("%s (%r)" % (rel, ex))
                    continue
                cjk += len(PAT.findall(t))
                if "\ufffd" in t:
                    fffd.append(rel)
                if MOJI.search(t):
                    moji.append(rel)
                code = code_only(t)
                m = DEAD.search(code)
                if m:
                    dead_code.append("%s:%s" % (rel, m.group(0)))
                elif DEAD.search(t):
                    dead_prose.append("%s:%s" % (rel, DEAD.search(t).group(0)))
    info.append("cs files            : %d" % files)
    info.append("CJK characters      : %d" % cjk)
    if unreadable:
        blocks.append("not valid UTF-8 (%d): %s" % (len(unreadable), "; ".join(unreadable[:6])))
    if fffd:
        blocks.append("U+FFFD in source (%d): %s" % (len(fffd), ", ".join(fffd[:6])))
    for rel in REQUIRED:
        if not os.path.isfile(os.path.join(root, rel)):
            blocks.append("required source file missing: %s" % rel)
    if moji:
        warns.append("mojibake suspects (%d): %s  -- review rule: a one-column Latin-1 letter next to a"
                     " multi-byte CJK run is a re-encode; a deliberate accented word is not" % (len(moji), ", ".join(moji[:6])))
    if dead_code:
        blocks.append("dead symbol reference in CODE (%d): %s" % (len(dead_code), ", ".join(dead_code[:6])))
    if dead_prose:
        warns.append("dead symbol name in a comment/string only (%d): %s" % (len(dead_prose), ", ".join(dead_prose[:6])))
    if not dead_code and not dead_prose:
        info.append("dead symbol refs    : none")
    try:
        bi_t = io.open(os.path.join(root, "src/BuildInfo.cs"), encoding="utf-8").read()
        cs_t = io.open(os.path.join(root, "src/DpsMeter.csproj"), encoding="utf-8").read()
        bi = re.search("Version = " + Q + "([^" + Q + "]+)" + Q, bi_t)
        cs = re.search("<Version>([^<]+)</Version>", cs_t)
        if not (bi and cs):
            blocks.append("version truth: could not parse BuildInfo.Version or csproj <Version>")
        elif bi.group(1) != cs.group(1):
            blocks.append("version mismatch: BuildInfo=%s csproj=%s" % (bi.group(1), cs.group(1)))
        else:
            info.append("version truth       : BuildInfo=%s csproj=%s agree=True" % (bi.group(1), cs.group(1)))
    except Exception as ex:
        blocks.append("version truth unreadable: %r" % (ex,))
    for grp, cls in PARTIAL_GROUPS:
        parts = sorted(glob.glob(os.path.join(root, grp)))
        if not parts:
            blocks.append("partial group is empty: %s (expected the %s parts)" % (grp, cls))
            continue
        wrong = []
        for p in parts:
            try:
                t = io.open(p, encoding="utf-8").read()
            except Exception as ex:
                wrong.append("%s (unreadable: %r)" % (os.path.relpath(p, root), ex))
                continue
            if ("public static partial class %s" % cls) not in t:
                wrong.append(os.path.relpath(p, root).replace(os.sep, "/"))
        if wrong:
            blocks.append("partial declaration missing in %d of %d %s parts: %s"
                          % (len(wrong), len(parts), cls, ", ".join(wrong[:6])))
        else:
            info.append("%-24s %d partial files, all declare partial: True" % (cls, len(parts)))
    cn = os.path.join(root, "src/Composition/CharacterNames.cs")
    if os.path.isfile(cn):
        try:
            info.append("CharacterNames rows : %d"
                        % len(re.findall(Q + "[^" + Q + "]+" + Q, io.open(cn, encoding="utf-8").read())))
        except Exception:
            pass
    return blocks, warns, info


def main(argv=None):
    ap = argparse.ArgumentParser(description="refactored-tree integrity check (N1: blocking)")
    ap.add_argument("--root", default=os.getcwd())
    ap.add_argument("--selftest", action="store_true")
    a = ap.parse_args(argv)
    if a.selftest:
        return selftest()
    blocks, warns, info = check(os.path.abspath(a.root))
    for s in info:
        print(s)
    for s in warns:
        print("WARN: " + s)
    for s in blocks:
        print("BLOCK: " + s)
    print("refactor check: blocks=%d warns=%d" % (len(blocks), len(warns)))
    return 1 if blocks else 0


def selftest():
    me = os.path.abspath(__file__)
    py = sys.executable
    src = os.path.join(os.path.dirname(me), "src")
    tmp = tempfile.mkdtemp(prefix="refactor_")
    fails = []

    def fresh(name):
        d = os.path.join(tmp, name)
        if os.path.isdir(d):
            shutil.rmtree(d)
        os.makedirs(d)
        shutil.copytree(src, os.path.join(d, "src"), ignore=shutil.ignore_patterns("bin", "obj"))
        return d

    def run(d, tag):
        lp = os.path.join(tmp, "out_%s.txt" % tag)
        with io.open(lp, "w", encoding="utf-8") as fh:
            rc = subprocess.call([py, me, "--root", d], stdout=fh, stderr=subprocess.STDOUT)
        return rc, io.open(lp, encoding="utf-8", errors="replace").read()

    def case(label, ok, extra=""):
        print("  [%s] %-56s %s" % ("PASS" if ok else "FAIL", label, extra))
        if not ok:
            fails.append(label)

    d = fresh("clean")
    rc, txt = run(d, "clean")
    case("clean copy is green", rc == 0 and "blocks=0" in txt, "rc=%s" % rc)

    d = fresh("badutf")
    io.open(os.path.join(d, "src", "Plugin.cs"), "wb").write(b"// \xff\xfe not utf-8\n")
    rc, txt = run(d, "badutf")
    case("invalid UTF-8 blocks", rc == 1 and "not valid UTF-8" in txt, "rc=%s" % rc)

    d = fresh("fffd")
    p = os.path.join(d, "src", "Plugin.cs")
    io.open(p, "a", encoding="utf-8").write(chr(10) + "// broken " + chr(0xFFFD) + chr(10))
    rc, txt = run(d, "fffd")
    case("U+FFFD in a source file blocks", rc == 1 and "U+FFFD" in txt, "rc=%s" % rc)

    d = fresh("missing")
    os.remove(os.path.join(d, "src", "BuildInfo.cs"))
    rc, txt = run(d, "missing")
    case("required source file missing blocks", rc == 1 and "required source file missing" in txt, "rc=%s" % rc)

    d = fresh("ver")
    p = os.path.join(d, "src", "DpsMeter.csproj")
    t = io.open(p, encoding="utf-8").read()
    i = t.find("<Version>")
    j = t.find("</Version>", i)
    # replace the VALUE, not the tag: the first version of this tamper produced <Version>9.9.9<Version>1.7.11
    # and the parser correctly still read 1.7.11 -- the guard was right and the TEST was wrong.
    io.open(p, "w", encoding="utf-8").write(t[:i] + "<Version>9.9.9" + t[j:])
    rc, txt = run(d, "ver")
    case("BuildInfo/csproj version mismatch blocks", rc == 1 and "version mismatch" in txt, "rc=%s" % rc)

    d = fresh("partial")
    p = os.path.join(d, "src", "Ui", "OverlayUGUI.Chart.cs")
    t = io.open(p, encoding="utf-8").read().replace("public static partial class OverlayUGUI",
                                                   "public static class OverlayUGUI", 1)
    io.open(p, "w", encoding="utf-8").write(t)
    rc, txt = run(d, "partial")
    case("a partial part that forgot `partial` blocks", rc == 1 and "partial declaration missing" in txt, "rc=%s" % rc)

    d = fresh("aggpartial")
    p = os.path.join(d, "src", "Aggregator.Clock.cs")
    if os.path.isfile(p):
        t = io.open(p, encoding="utf-8").read().replace("public static partial class Aggregator",
                                                       "public static class Aggregator", 1)
        io.open(p, "w", encoding="utf-8").write(t)
        rc, txt = run(d, "aggpartial")
        case("an Aggregator partial that forgot `partial` blocks",
             rc == 1 and "partial declaration missing" in txt, "rc=%s" % rc)
    else:
        case("an Aggregator partial that forgot `partial` blocks", False,
             "src/Aggregator.Clock.cs is missing -- the RF2 group cannot be tampered")

    d = fresh("deadcode")
    p = os.path.join(d, "src", "Plugin.cs")
    io.open(p, "a", encoding="utf-8").write(chr(10) + "internal class Z { static void M() { DamageTakeOverHook(); } }" + chr(10))
    rc, txt = run(d, "deadcode")
    case("dead symbol in CODE blocks", rc == 1 and "dead symbol reference in CODE" in txt, "rc=%s" % rc)

    d = fresh("deadprose")
    p = os.path.join(d, "src", "Plugin.cs")
    io.open(p, "a", encoding="utf-8").write(chr(10) + "// DamageTakeOverHook was removed in 1.0.29" + chr(10))
    rc, txt = run(d, "deadprose")
    case("dead symbol in a COMMENT only warns (exit 0)", rc == 0 and "WARN" in txt, "rc=%s" % rc)

    d = fresh("moji")
    p = os.path.join(d, "src", "Plugin.cs")
    io.open(p, "a", encoding="utf-8").write(chr(10) + "// caf" + chr(0x00C3) + chr(0x00A9) + " legacy note" + chr(10))
    rc, txt = run(d, "moji")
    case("mojibake suspect warns but does not block", rc == 0 and "WARN" in txt and "blocks=0" in txt, "rc=%s" % rc)

    d = os.path.join(tmp, "empty")
    os.makedirs(d)
    rc, txt = run(d, "empty")
    case("an empty root blocks on the required inputs", rc == 1 and "required source file missing" in txt, "rc=%s" % rc)

    shutil.rmtree(tmp, ignore_errors=True)
    print("---- selftest: %s" % ("PASS" if not fails else "FAIL %s" % fails))
    return 0 if not fails else 1


if __name__ == "__main__":
    sys.exit(main())
