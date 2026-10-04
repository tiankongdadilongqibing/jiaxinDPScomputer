# -*- coding: utf-8 -*-
"""Contribution table LAYOUT guard (added with 1.7.7).

Why it exists: 1.7.6 shipped a table whose columns still drifted, because Fit() cut by .NET
character count while PadR/PadL padded by DISPLAY COLUMNS. 25 of 140 character rows and 78 of 340
rule rows overflowed their column. This script replicates the renderer's arithmetic exactly and
asserts that every rendered row of every export equals its table's nominal width.

It is a LAYOUT test only: it never re-checks the numbers (crosscheck/validate do that).
Usage: python check_contribution_layout.py [--dir <exports dir>] [--quiet]
"""
from __future__ import annotations
import argparse, io, json, os, re, shutil, sys, tempfile

def disp_char(c):
    o = ord(c)
    return 2 if ((0x1100 <= o <= 0x115F) or (0x2E80 <= o <= 0xA4CF) or (0xAC00 <= o <= 0xD7A3)
                 or (0xF900 <= o <= 0xFAFF) or (0xFE30 <= o <= 0xFE6F) or (0xFF00 <= o <= 0xFF60)
                 or (0xFFE0 <= o <= 0xFFE6)) else 1

def units(s):
    # UTF-16 code units, exactly like C#'s foreach over a string: an astral character is TWO
    # units of width 1 each (= 2 columns), which is what a full-width astral CJK glyph occupies.
    b = (s or '').encode('utf-16-le', 'surrogatepass')
    return [b[i] | (b[i + 1] << 8) for i in range(0, len(b), 2)]

def disp(s):
    return sum(disp_char(chr(u)) for u in units(s))

def pad_r(s, n):
    w = disp(s); return s if w >= n else s + ' ' * (n - w)

def pad_l(s, n):
    w = disp(s); return s if w >= n else ' ' * (n - w) + s

CUT = '..'
USE_OLD_FIT = False   # set by --selftest to PROVE this guard can fail

def fit(s, maxw):
    if not s: return ''
    if USE_OLD_FIT:
        # 1.7.6 semantics: cut by CHARACTER count and mark with U+2026
        return s if len(s) <= maxw else s[:maxw] + '\u2026'
    if disp(s) <= maxw: return s
    if maxw <= len(CUT):
        return CUT[:max(0, min(len(CUT), maxw))]
    budget = max(1, maxw - len(CUT))
    us = units(s)
    w = 0; i = 0
    while i < len(us):
        cw = disp_char(chr(us[i]))
        if w + cw > budget: break
        w += cw; i += 1
    if i > 0 and 0xD800 <= us[i - 1] <= 0xDBFF: i -= 1   # never cut a surrogate pair in half
    b = s.encode('utf-16-le', 'surrogatepass')
    return b[:i * 2].decode('utf-16-le', 'surrogatepass') + CUT

def cell(s):
    return (s or '').replace('\u00d7', 'x')

def fmt(v):
    try: return '{:,.0f}'.format(float(v))
    except Exception: return '0'

def pct(v):
    try: return '{:.2f}%'.format(float(v))
    except Exception: return '0.00%'

def num(v):
    try: return '{:,}'.format(int(v))
    except Exception: return '0'

def amt(v, width):
    # mirrors C# Amt(): full grouped form when it fits, else an M/G/T suffix that always fits
    try: v = float(v)
    except Exception: v = 0.0
    full = fmt(v)
    if disp(full) <= width: return pad_l(full, width)
    for suf, scale in (('M', 1e6), ('G', 1e9), ('T', 1e12)):
        c = '{:.1f}{}'.format(v / scale, suf)
        if disp(c) <= width: return pad_l(c, width)
    return pad_l('>' + '9' * max(0, width - 1), width)

T1_W, T2_W, T3_W = 85, 77, 53

def t1_header():
    return ('  ' + pad_r('\u89d2\u8272', 16) + pad_l('\u603b\u8d21\u732e', 11) + pad_l('\u5360\u6bd4', 8)
            + pad_l('\u81ea\u8eab', 11) + pad_l('\u4ed6\u4eba\u56e0\u4f60', 11)
            + pad_l('\u88ab\u961f\u53cb\u5206\u8d70', 11) + pad_l('\u76f4\u63a5\u5360\u6bd4', 9) + pad_l('\u547d\u4e2d', 6))

def t1_row(a, total):
    share = 100.0 * a['totalCredit'] / total if total else 0.0
    dshare = 100.0 * a['directDamage'] / total if total else 0.0
    label = pad_r(fit(cell(a['name']) + ('*' if a.get('summon') else ''), 16), 16)
    return ('  ' + label + amt(a['totalCredit'], 11) + pad_l(pct(share), 8)
            + amt(a['baseCredit'] + a['selfRuleCredit'], 11) + amt(a['assistCredit'], 11)
            + amt(a['receivedAssist'], 11) + pad_l(pct(dshare), 9) + amt(a['hits'], 6))

def t1_total(cj):
    sn = sa = sr = 0.0
    for a in cj['actors']:
        sn += a['baseCredit'] + a['selfRuleCredit']; sa += a['assistCredit']; sr += a['receivedAssist']
    return ('  ' + pad_r('\u5408\u8ba1', 16) + amt(cj['attributedDamage'], 11) + pad_l('', 8)
            + amt(sn, 11) + amt(sa, 11) + amt(sr, 11) + pad_l('', 9) + pad_l('', 6))

def t2_header():
    return ('  ' + pad_r('\u89c4\u5219', 22) + pad_r('\u901a\u9053', 8) + pad_r('\u4fa7', 5) + pad_r('\u6301\u6709\u8005', 14)
            + pad_l('\u547d\u4e2d', 7) + pad_l('\u6298\u53e0', 7) + pad_l('\u5f53\u91cf', 12))

def t2_row(r):
    return ('  ' + pad_r(fit(cell(r['ruleName']), 22), 22)
            + pad_r(fit(cell(r['kind']), 8), 8) + pad_r(fit(cell(r['side']), 5), 5)
            + pad_r(fit(cell(r['ownerName']), 14), 14) + amt(r['hits'], 7) + amt(r['folds'], 7)
            + amt(r['damageEquivalent'], 12))

def t3_header():
    return ('  ' + pad_r('\u63d0\u4f9b\u8005', 14) + pad_r('\u2192', 4) + pad_r('\u53d7\u76ca\u8005', 14)
            + pad_l('\u547d\u4e2d', 7) + pad_l('\u5f53\u91cf', 12))

def t3_row(link, names):
    f = names.get(link['fromKey'], '#' + str(link['fromKey']))
    t = names.get(link['toKey'], '#' + str(link['toKey']))
    return ('  ' + pad_r(fit(cell(f), 14), 14) + pad_r('\u2192', 4) + pad_r(fit(cell(t), 14), 14)
            + amt(link['hits'], 7) + amt(link['amount'], 12))

def section_of(text):
    key = '"contribution":{'
    i = text.rfind(key)
    if i < 0: return None
    j = i + len(key) - 1; depth = 0; n = len(text); k = j; instr = False; esc = False
    while k < n:
        ch = text[k]
        if instr:
            if esc: esc = False
            elif ch == '\\': esc = True
            elif ch == '"': instr = False
        else:
            if ch == '"': instr = True
            elif ch == '{': depth += 1
            elif ch == '}':
                depth -= 1
                if depth == 0: return text[j:k+1]
        k += 1
    return None

def check_file(path, quiet=False):
    with io.open(path, 'r', encoding='utf-8') as fh:
        text = fh.read()
    raw = section_of(text)
    if raw is None:
        return ('ABSENT', 0, [])
    cj = json.loads(raw)
    viol = []
    names = {a['key']: a['name'] for a in cj['actors']}
    # GRID-CLEAN CENSUS (1.7.7): a padded column may only contain ASCII or full-width characters,
    # because DispWidth counts everything else as ONE column. U+00D7 is exempt: Cell() normalises it
    # to ASCII 'x' before padding. Any NEW character here means the next unknown-width glyph arrived.
    bad_chars = set()
    for t in (list(names.values()) + [r['ruleName'] for r in cj['rules']] + [r['ownerName'] for r in cj['rules']]
              + [r['kind'] for r in cj['rules']] + [r['side'] for r in cj['rules']]):
        for c in t or '':
            if ord(c) >= 128 and disp_char(c) == 1 and c != '\u00d7':
                bad_chars.add(c)
    if bad_chars:
        viol.append(('GRID-CLEAN', len(bad_chars), 0,
                     'unexpected one-column non-ASCII: ' + ','.join('U+%04X' % ord(c) for c in sorted(bad_chars))))
    total = cj['coverage']['analyzableDealt']
    rows = 0
    h1, h2, h3 = t1_header(), t2_header(), t3_header()
    for label, s, want in (('T1.header', h1, T1_W), ('T2.header', h2, T2_W), ('T3.header', h3, T3_W)):
        if disp(s) != want: viol.append((label, disp(s), want, s))
    for a in cj['actors']:
        if a['totalCredit'] <= 0 and a['directDamage'] <= 0: continue
        s = t1_row(a, total); rows += 1
        if disp(s) != T1_W: viol.append(('T1 ' + a['name'], disp(s), T1_W, s))
    s = t1_total(cj); rows += 1
    if disp(s) != T1_W: viol.append(('T1.total', disp(s), T1_W, s))
    shown = 0
    for r in cj['rules']:
        if r['damageEquivalent'] <= 0.0: continue
        if shown >= 12: break
        s = t2_row(r); rows += 1; shown += 1
        if disp(s) != T2_W: viol.append(('T2 ' + r['ruleName'], disp(s), T2_W, s))
    n = 0
    for l in cj.get('links') or []:
        if n >= 12: break
        s = t3_row(l, names); rows += 1; n += 1
        if disp(s) != T3_W: viol.append(('T3 %s->%s' % (l['fromKey'], l['toKey']), disp(s), T3_W, s))
    if viol and not quiet:
        # ASCII-escape everything: this console is GBK, and printing CJK here used to abort the run
        def _a(x): return str(x).encode('unicode_escape').decode('ascii')
        print('  VIOLATIONS (%d):' % len(viol))
        for label, got, want, s in viol[:6]:
            print('    %-32s width=%d want=%d  %s' % (_a(label), got, want, _a(s)))
    return ('OK' if not viol else 'BAD', rows, viol)

def _numeric_selftest():
    # a 1e9-class battle must not push any row: Amt() must fall back to M/G/T
    big = {'key': 1, 'name': '\u30c6\u30b9\u30c8', 'summon': False, 'totalCredit': 9.9e9,
           'directDamage': 9.9e9, 'baseCredit': 9.9e9, 'selfRuleCredit': 9.9e9,
           'assistCredit': 9.9e9, 'receivedAssist': 9.9e9, 'hits': 1234567}
    cj = {'actors': [big], 'attributedDamage': 9.9e9, 'coverage': {'analyzableDealt': 9.9e9}}
    r2 = {'ruleName': 'x', 'kind': 'madness', 'side': 'vic', 'ownerName': 'y',
          'hits': 1, 'folds': 1, 'damageEquivalent': 9.9e9}
    cases = ((t1_row(big, 9.9e9), T1_W), (t1_total(cj), T1_W), (t2_row(r2), T2_W),
             (t3_row({'fromKey': 1, 'toKey': 1, 'hits': 1, 'amount': 9.9e9}, {1: 'y'}), T3_W))
    bad = [(disp(x), w) for x, w in cases if disp(x) != w]
    bad_fit = []
    for m in range(0, 26):
        for probe in ('\u3042\u3044', 'abc', '\u30a8\u30f4\u30a1\u30e9\u30b9\u30fb\u30d5\u30e9\u30a6', '', '\uff8a\uff9d\uff76\uff78'):
            if disp(fit(probe, m)) > m: bad_fit.append((disp(fit(probe, m)), m))
    sample = cases[0][0][:16]
    return (not bad and not bad_fit,
            'widths=%s bad=%s bad_fit=%s compact=[%s]' % ([disp(x) for x, _ in cases], bad, bad_fit, sample.strip()))

# ---------------------------------------------------------------------------------------------
# 1.7.11: THE REPLICA MUST STILL BE A REPLICA.
# Everything above re-implements the renderer by hand, and until now nothing checked that the two still
# agree: editing OverlayUGUI.Rows.cs' row format without touching this file kept the guard GREEN while
# the panel moved. So the renderer's own column list is now parsed out of the C# source and compared
# with the Python replica, label by label and width by width. A drift in either direction fails.
# ---------------------------------------------------------------------------------------------
SRC_ROWS = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'src', 'Ui', 'OverlayUGUI.Rows.cs')
RE_CS_CELL = re.compile(r'Pad[RL]\("([^"]*)",\s*(\d+)\)')
RE_CS_NUM = re.compile(r'(?:Amt|PadL)\([^,]+,\s*(\d+)\)')
# RF5c: the column DEFINITION moved from the renderer into src/Ui/ContributionColumns.cs. The guard reads
# the definition (labels + widths), the renderer's remaining inline row widths, and requires both to agree
# with this file's replica -- so the panel cannot move without the guard moving with it.
SRC_SPEC = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'src', 'Ui', 'ContributionColumns.cs')
RE_SPEC_CONST = re.compile(r'public const int (\w+) = (\d+);')
RE_SPEC_ENTRY = re.compile(r'C\("([^"]*)",\s*(\w+),\s*(?:true|false)\)')
# Only the OUTER cell call carries a column width: matching Amt/PadL/PadR keeps Fit's inner width out.
RE_WIDTH_LITERAL = re.compile(r'(?:Amt|PadL|PadR)\([^,]+,\s*(\d+)\)')
# Every T1/T2/T3 identifier; the builders' own names are filtered out below, so the column constants
# never have to be listed here (a list would be one more thing to keep in sync).
RE_BUILDER_CONST = re.compile(r'\b(T[123][A-Z][A-Za-z]*)\b')
BUILDER_NAMES = ('T1Row', 'T2Row', 'T3Row')
RE_BARE_WIDTH = re.compile(r',\s*\d+\)')
T1_CELLS = [
    ('\u89d2\u8272', 16), ('\u603b\u8d21\u732e', 11), ('\u5360\u6bd4', 8),
    ('\u81ea\u8eab', 11), ('\u4ed6\u4eba\u56e0\u4f60', 11),
    ('\u88ab\u961f\u53cb\u5206\u8d70', 11), ('\u76f4\u63a5\u5360\u6bd4', 9), ('\u547d\u4e2d', 6),
]
T2_CELLS = [
    ('\u89c4\u5219', 22), ('\u901a\u9053', 8), ('\u4fa7', 5), ('\u6301\u6709\u8005', 14),
    ('\u547d\u4e2d', 7), ('\u6298\u53e0', 7), ('\u5f53\u91cf', 12),
]
T3_CELLS = [
    ('\u63d0\u4f9b\u8005', 14), ('\u2192', 4), ('\u53d7\u76ca\u8005', 14),
    ('\u547d\u4e2d', 7), ('\u5f53\u91cf', 12),
]

def _csharp_block(text, start, end):
    i = text.find(start)
    if i < 0:
        return None
    j = text.find(end, i)
    if j < 0:
        return None
    return text[i:j]

def _cs_cells(text, start, end):
    blk = _csharp_block(text, start, end)
    if blk is None:
        return None
    return [(lbl, int(w)) for lbl, w in RE_CS_CELL.findall(blk)]

def _cs_nums(text, start, end):
    blk = _csharp_block(text, start, end)
    if blk is None:
        return None
    return [int(w) for w in RE_CS_NUM.findall(blk)]

def spec_columns(path=None):
    """The C# column definition, resolved to [(label, width), ...] per table, or None when unparseable."""
    p = path or SRC_SPEC
    if not os.path.isfile(p):
        return None
    with io.open(p, 'r', encoding='utf-8') as fh:
        text = fh.read()
    consts = dict((n, int(v)) for n, v in RE_SPEC_CONST.findall(text))
    out = []
    for arr in ('T1', 'T2', 'T3'):
        blk = _csharp_block(text, arr + ' =', '};')
        if blk is None:
            return None
        cols = []
        for lbl, name in RE_SPEC_ENTRY.findall(blk):
            if name not in consts:
                return None
            cols.append((lbl, consts[name]))
        out.append(cols)
    return out


def _widths_in_order(text, start, end):
    """The column widths of a row block, one per cell, in the order the row writes them."""
    blk = _csharp_block(text, start, end)
    if blk is None:
        return None
    return [int(w) for w in RE_WIDTH_LITERAL.findall(blk)]


def spec_names(path=None):
    """The definition's constant NAMES per table, in column order."""
    p = path or SRC_SPEC
    if not os.path.isfile(p):
        return None
    with io.open(p, 'r', encoding='utf-8') as fh:
        text = fh.read()
    out = []
    for arr in ('T1', 'T2', 'T3'):
        blk = _csharp_block(text, arr + ' =', '};')
        if blk is None:
            return None
        out.append([name for _lbl, name in RE_SPEC_ENTRY.findall(blk)])
    return out


def check_source_replica():
    """Return a list of drift problems between the C# definition/renderer and this file's replica."""
    if not os.path.isfile(SRC_SPEC):
        return ['column definition not found at %s' % SRC_SPEC]
    spec = spec_columns()
    if spec is None:
        return ['the column definition could not be parsed (%s)' % os.path.basename(SRC_SPEC)]
    problems = []
    want_tables = [T1_CELLS, T2_CELLS, T3_CELLS]
    for i, name in enumerate(('T1', 'T2', 'T3')):
        if spec[i] != want_tables[i]:
            problems.append('%s drifted: definition=%s replica=%s' % (name, spec[i], want_tables[i]))
    # RF5d: the data rows are built by T1Row/T2Row/T3Row in the SAME definition file, so there is nothing
    # left to cross-check in the renderer. What IS checked: each builder references the definition's
    # constants in column order, and carries no bare width literal (a copy that could drift).
    names = spec_names()
    if names is None:
        problems.append('the column definition could not be parsed for builder names')
        return problems
    with io.open(SRC_SPEC, 'r', encoding='utf-8') as fh:
        spec_text = fh.read()
    for builder, want in (('T1Row', names[0]), ('T2Row', names[1]), ('T3Row', names[2])):
        blk = _csharp_block(spec_text, 'public static string ' + builder + '(', '\n\t}')
        if blk is None:
            problems.append('%s not found in %s' % (builder, os.path.basename(SRC_SPEC)))
            continue
        used = []
        for c in RE_BUILDER_CONST.findall(blk):
            if c in BUILDER_NAMES:
                continue
            if c not in used:
                used.append(c)
        if used != want:
            problems.append('%s uses %s, definition order is %s' % (builder, used, want))
        bare = RE_BARE_WIDTH.findall(blk)
        if bare:
            problems.append('%s carries bare width literal(s) %s -- the width belongs to the definition'
                            % (builder, bare))
    return problems
    with io.open(SRC_ROWS, 'r', encoding='utf-8') as fh:
        rows = fh.read()
    # The block starts at the label line so the name column is included; each cell contributes exactly one
    # width because only the outer Amt/PadL/PadR call matches.
    row_w = _widths_in_order(rows, 'string label = DisplayFormat.PadR', 'Color = AllyColor')
    if row_w is None:
        problems.append('T1 data row not found in %s' % os.path.basename(SRC_ROWS))
    elif row_w != [w for _lbl, w in T1_CELLS]:
        problems.append('T1 data row widths = %s, definition says %s'
                        % (row_w, [w for _lbl, w in T1_CELLS]))
    return problems


def _source_selftest():
    """Prove the drift check has teeth now that the geometry lives in the definition file."""
    global SRC_SPEC
    tmp = tempfile.mkdtemp(prefix='layout_src_')
    try:
        with io.open(SRC_SPEC, 'r', encoding='utf-8') as fh:
            text = fh.read()
        bad = text.replace('public const int T1Total = 11;', 'public const int T1Total = 12;')
        if bad == text:
            return (False, 'tamper target T1Total = 11 not found -- the check may be reading nothing')
        tp = os.path.join(tmp, 'ContributionColumns.cs')
        with io.open(tp, 'w', encoding='utf-8') as fh:
            fh.write(bad)
        keep = SRC_SPEC
        SRC_SPEC = tp
        probs = check_source_replica()
        SRC_SPEC = keep
        return (bool(probs), 'tampered definition -> %d problem(s)' % len(probs))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--dir', default=os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                                  '..', 'BepInEx', 'plugins', 'DpsMeter', 'exports'))
    ap.add_argument('--quiet', action='store_true')
    ap.add_argument('--selftest', action='store_true',
                    help='prove the guard has teeth: replay the 1.7.6 Fit semantics and require violations')
    args = ap.parse_args()
    drift = check_source_replica()
    for d in drift:
        print('SOURCE-DRIFT: ' + d)
    if args.selftest:
        if drift:
            print('selftest: FAIL (renderer/replica drift)')
            return 1
        num_ok, num_msg = _numeric_selftest()
        print('selftest(numeric slack): ' + num_msg)
        src_ok, src_msg = _source_selftest()
        print('selftest(source drift check): ' + src_msg)
        global USE_OLD_FIT
        USE_OLD_FIT = True
        old = _sweep(args)
        USE_OLD_FIT = False
        new = _sweep(args)
        print('selftest: old(1.7.6 Fit) bad=%d rows=%d violations=%d | new(1.7.7 Fit) bad=%d rows=%d violations=%d' % (
            old[0], old[1], old[2], new[0], new[1], new[2]))
        ok = (old[2] > 0) and (new[2] == 0) and num_ok and src_ok
        print('selftest: ' + ('PASS' if ok else 'FAIL'))
        return 0 if ok else 1
    bad, rows, _v = _sweep(args)
    return 1 if (bad or drift) else 0

def _sweep(args):
    files = sorted(f for f in os.listdir(args.dir) if f.startswith('battle_') and f.endswith('.json'))
    total = absent = bad = 0; rows = 0; badfiles = []; viol_total = 0
    for f in files:
        status, n, viol = check_file(os.path.join(args.dir, f), args.quiet)
        total += 1
        if status == 'ABSENT': absent += 1; continue
        rows += n
        if status == 'BAD':
            bad += 1; badfiles.append(f); viol_total += len(viol)
            if not args.quiet:
                print('BAD  %s  rows=%d violations=%d' % (f, n, len(viol)))
        elif not args.quiet: print('OK   %s  rows=%d' % (f, n))
    if not args.quiet:
        print('---- layout: files=%d with-section=%d absent=%d bad=%d rows-checked=%d' % (
            total, total - absent, absent, bad, rows))
        if badfiles: print('bad files: ' + ', '.join(badfiles))
    return (bad, rows, viol_total)

if __name__ == '__main__':
    sys.exit(main())
