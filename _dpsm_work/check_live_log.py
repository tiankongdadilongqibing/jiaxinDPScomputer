# -*- coding: utf-8 -*-
"""Live-log acceptance gate (1.7.9): the MACHINE's own log against the exports it wrote.

WHY THIS EXISTS
    Until now the only way to accept a release was for the user to fight a battle and look at the
    overlay, which makes acceptance un-repeatable and un-auditable. BepInEx's LogOutput.log already
    carries the machine's own statements -- the boot banner, one `Battle end: ... unattributed=N(xH)`
    line per battle, one `Exported full battle data -> <path>` line per file, and a per-5s `[UI-DIAG]`
    heartbeat of the rendered overlay -- so the log and the JSON can be checked AGAINST EACH OTHER with
    no game running. That converts "the user says the panel looked right" into a reproducible fact, and
    it is the second independent source for numbers the plugin also exports (the first being the
    offline core, which re-derives them from the per-hit fold list).

WHAT IS CHECKED
    A. version: the boot banner version equals the version stamped in every export written after it.
    B. one pair per exported battle: result, quest, duration and -- the point of the exercise --
       totals.unattributedDamage/unattributedHits exactly equal the `Battle end` line the machine
       printed for that battle. For schema 1.1 exports the same number must also equal
       contribution.damageLedger.unknownAttackerDealt (the ledger moved it inside the section).
    C. overlay heartbeat: no contribution frame may print a placeholder header ("任务 0"), the history
       counter may never go backwards, and any frame that carries `unattrRow` (the row with the real
       unattributed figure, added by 1.7.9) must agree with an export from the same session. A log
       written by an older build cannot carry that field, which is reported as LEGACY_NOT_APPLICABLE
       WITH ITS REASON rather than passed.
    D. no DpsMeter exception/error line anywhere in the log.

STATUS/EXIT contract: contribution_gate.py (PASS 0 / WARNING 0 / LEGACY_NOT_APPLICABLE 0 with a reason /
DATA_MISSING 3 / ERROR 1).

Usage: python check_live_log.py [--log PATH] [--exports DIR] [--selftest]
ASCII-only output.
"""
from __future__ import print_function
import argparse, io, json, os, re, shutil, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import contribution_gate as gate  # noqa: E402  (one status vocabulary for every gate)

ROOT = os.path.abspath(os.path.join(HERE, '..'))
DEF_LOG = os.path.join(ROOT, 'BepInEx', 'LogOutput.log')
DEF_EXP = os.path.join(ROOT, 'BepInEx', 'plugins', 'DpsMeter', 'exports')

# CJK markers, kept as escapes so this file stays ASCII on disk
M_IN = '\u672a\u5f52\u56e0'    # 未归因  (in-domain unattributed, contribution section)
M_OUT = '\u672a\u5f52\u5c5e'   # 未归属  (session-level unknown-attacker damage)
M_NA = '\u4e0d\u53ef\u7528'     # 不可用
M_TASK = '\u4efb\u52a1'          # 任务

RE_BANNER = re.compile(r'Loading \[DpsMeter ([0-9][0-9.]*)\]')
RE_LOADED = re.compile(r'DpsMeter ([0-9][0-9.]*) loaded')
RE_END = re.compile(r'Battle end: result=(\S+) dur=([0-9.]+)s idle=[0-9.]+s quest=(\d+) '
                    r'actors=(\d+) unattributed=(\d+)\(x(\d+)\)')
RE_EXPORT = re.compile(r'Exported full battle data -> (.+?) \((\d+) bytes\)')
RE_DIAG = re.compile(r'\[UI-DIAG\] (.*)$')
RE_KV = re.compile(r'([A-Za-z_]+)=([^ ]*)')

def ascii_safe(s):
    """The Windows console is GBK: a row text quoted back into a message would be mojibake there."""
    try:
        return s.encode('ascii', 'replace').decode('ascii')
    except Exception:
        return '?'


def read_text(path):
    with io.open(path, 'r', encoding='utf-8', errors='replace') as fh:
        return fh.read()

def parse_log(text):
    banner, loaded = None, None
    ends, exports, frames, errors = [], [], [], []
    for i, line in enumerate(text.splitlines()):
        if 'DpsMeter' not in line and not line.startswith('['):
            continue
        m = RE_BANNER.search(line)
        if m and banner is None:
            banner = m.group(1)
        m = RE_LOADED.search(line)
        if m and loaded is None:
            loaded = m.group(1)
        if 'DpsMeter' in line and '[Error' in line:
            errors.append(line)
        m = RE_END.search(line)
        if m:
            ends.append({'line': i, 'result': m.group(1), 'dur': float(m.group(2)),
                         'quest': int(m.group(3)), 'actors': int(m.group(4)),
                         'unattr': int(m.group(5)), 'unattr_hits': int(m.group(6))})
            continue
        m = RE_EXPORT.search(line)
        if m:
            exports.append({'line': i, 'path': m.group(1).strip(), 'bytes': int(m.group(2))})
            continue
        m = RE_DIAG.search(line)
        if m:
            body = m.group(1)
            kv = dict(RE_KV.findall(body))
            for key in ('firstRow', 'unattrRow'):
                mm = re.search(key + r"='([^']*)'", body)
                if mm:
                    kv[key] = mm.group(1)
            frames.append({'line': i, 'view': kv.get('view'), 'hist': kv.get('hist'),
                           'firstRow': kv.get('firstRow', ''), 'unattrRow': kv.get('unattrRow')})
    return {'banner': banner, 'loaded': loaded, 'ends': ends, 'exports': exports,
            'frames': frames, 'errors': errors}

def pair(parsed):
    """Attach to every export the nearest preceding Battle end line."""
    out = []
    for e in parsed['exports']:
        cand = [b for b in parsed['ends'] if b['line'] < e['line']]
        out.append((e, cand[-1] if cand else None))
    return out

def num_of(text):
    """First integer/decimal-looking run in a row text, as int when it has no dot."""
    if not text:
        return None
    m = re.search(r'([0-9][0-9,]*)(?:\.([0-9]+))?', text)
    if not m:
        return None
    digits = m.group(1).replace(',', '')
    try:
        return int(digits)
    except ValueError:
        return None

def num_after(text, marker):
    """The number that FOLLOWS the marker, not the first number in the row.

    1.7.11: found on a LIVE 1.7.10 log. The F6 detail summary row prints
    "  合计 194,697,612   未归因 0(0.00%)   命中 5,493   倍率池 168,197,251", and the first number in
    that row is 合计 -- so the gate compared the ATTRIBUTED total against
    contribution.unattributedDamage and reddened a panel that was printing the correct 0. A check that
    reads the wrong field is worse than no check: it burns trust in every real finding it makes.
    """
    if not text or not marker or marker not in text:
        return None
    return num_of(text.split(marker, 1)[1])

def run(log_path):
    """NOTE ON INPUTS: there is deliberately no export-directory parameter. Every file this gate looks at
    is named by the log itself (the "Exported full battle data ->" line carries an absolute path), so a
    directory argument would be dead weight -- the first version of this file had one and never read it.
    """
    problems, notes, warnings = [], [], []
    if not os.path.isfile(log_path):
        return (gate.LEGACY_NOT_APPLICABLE, ['no log at %s: the game has not run since it was cleared, '
                                            'so this gate cannot apply (it is NOT a pass)' % log_path],
                problems, notes, warnings)
    parsed = parse_log(read_text(log_path))
    if parsed['banner'] is None and parsed['loaded'] is None:
        return (gate.DATA_MISSING, ['%s exists but carries no DpsMeter boot banner: the file cannot be '
                                    'attributed to a plugin version' % log_path],
                problems, notes, warnings)
    version = parsed['banner'] or parsed['loaded']
    alog = 'log banner: DpsMeter %s' % version
    if parsed['banner'] and parsed['loaded'] and parsed['banner'] != parsed['loaded']:
        problems.append('boot banner says %s but the load line says %s'
                        % (parsed['banner'], parsed['loaded']))
    # D. errors
    if parsed['errors']:
        problems.append('%d DpsMeter [Error] line(s) in the log; first: %s'
                        % (len(parsed['errors']), parsed['errors'][0][:160]))
    else:
        notes.append('no DpsMeter [Error] line in the log')
    # B. pairs
    pairs = pair(parsed)
    if not pairs:
        notes.append('the log lists no exported battle, so there is nothing to pair')
        if parsed['frames']:
            # An overlay was rendering during this session yet no battle was exported: the cross-check
            # this gate exists for cannot run. Saying PASS here would be the vacuous pass the whole gate
            # is meant to avoid, so it is a WARNING (exit 0, but named).
            warnings.append('%d overlay frame(s) but no exported battle: the machine-vs-export cross-check '
                            'could not run on this log' % len(parsed['frames']))
    session_unattr = {}
    checked = 0
    rotated_copies = 0
    for e, b in pairs:
        path = e['path']
        name = os.path.basename(path)
        # R57: the evidence bundle writes the same data to extract/<bundle>/battle.json, and ExtractKeep
        # deletes old bundles BY DESIGN. That is a COPY, not the durable corpus, so a missing one is a
        # note rather than "truncated corpus". (The writer now also logs copies on a different line; this
        # branch keeps the HISTORICAL logs -- already written before that change -- honest.)
        if os.sep + 'extract' + os.sep in path or name == 'battle.json':
            rotated_copies += 1
            continue
        if not os.path.isfile(path):
            problems.append('the log says %s was exported but the file is gone (truncated corpus?)' % name)
            continue
        try:
            with io.open(path, 'r', encoding='utf-8') as fh:
                doc = json.load(fh)
        except Exception as ex:
            problems.append('%s is not readable JSON: %s' % (name, ex))
            continue
        tot = doc.get('totals') or {}
        sec = doc.get('contribution') or {}
        led = sec.get('damageLedger') or {}
        checked += 1
        if doc.get('version') != version:
            problems.append('%s was stamped version %s but the boot banner says %s'
                            % (name, doc.get('version'), version))
        bl = doc.get('contribution') or {}
        if not b:
            problems.append('%s was exported with no preceding "Battle end" line: the machine statement '
                            'this gate compares against is missing' % name)
            continue
        if tot.get('unattributedDamage') != b['unattr']:
            problems.append('%s: machine said unattributed=%d but the export says totals.unattributedDamage=%s'
                            % (name, b['unattr'], tot.get('unattributedDamage')))
        else:
            session_unattr[b['unattr']] = b['unattr_hits']
        if tot.get('unattributedHits') != b['unattr_hits']:
            problems.append('%s: machine said unattributed hits x%d but the export says %s'
                            % (name, b['unattr_hits'], tot.get('unattributedHits')))
        if doc.get('quest') != b['quest']:
            problems.append('%s: machine said quest=%d, export says %s' % (name, b['quest'], doc.get('quest')))
        if doc.get('result') != b['result']:
            problems.append('%s: machine said result=%s, export says %s'
                            % (name, b['result'], doc.get('result')))
        dur = doc.get('duration')
        if isinstance(dur, (int, float)) and abs(float(dur) - b['dur']) > 0.15:
            problems.append('%s: machine said dur=%.2fs, export says %s' % (name, b['dur'], dur))
        if led:
            if led.get('unknownAttackerDealt') != b['unattr']:
                problems.append('%s: ledger.unknownAttackerDealt=%s disagrees with the machine line (%d)'
                                % (name, led.get('unknownAttackerDealt'), b['unattr']))
            if led.get('unknownAttackerHits') != b['unattr_hits']:
                problems.append('%s: ledger.unknownAttackerHits=%s disagrees with the machine line (x%d)'
                                % (name, led.get('unknownAttackerHits'), b['unattr_hits']))
        # the exporter logs the LENGTH OF THE JSON STRING (characters), not the file's byte size:
        # measured on the three 1.7.8 exports, len(text) == the logged number exactly, while the UTF-8
        # byte size is larger (CJK). Comparing the right quantity makes this an exact "not re-written"
        # check instead of a false alarm.
        try:
            with io.open(path, 'r', encoding='utf-8') as fh:
                chars = len(fh.read())
            if e['bytes'] and chars != e['bytes']:
                warnings.append('%s: the log recorded %d character(s) but the file now has %d '
                                '(re-written after export?)' % (name, e['bytes'], chars))
        except Exception as ex:
            warnings.append('%s: could not re-read for the length check: %s' % (name, ex))
    if checked:
        notes.append('%d exported battle(s) paired with the machine log; version %s' % (checked, version))
    if rotated_copies:
        notes.append('%d evidence-bundle copy path(s) skipped: ExtractKeep rotates those by design'
                     % rotated_copies)
    # C. overlay frames
    frames = parsed['frames']
    if not frames:
        notes.append('no [UI-DIAG] overlay frame in the log')
    for f in frames:
        if f['view'] == 'Contribution' and re.search(M_TASK + r'\s+0(?:\s|$)', f['firstRow'] or ''):
            problems.append('a Contribution frame printed a placeholder header: %r' % f['firstRow'])
    hists = []
    for f in frames:
        try:
            hists.append(int(f['hist']))
        except (TypeError, ValueError):
            pass
    if any(hists[i] < hists[i - 1] for i in range(1, len(hists))):
        problems.append('the history counter went backwards across [UI-DIAG] frames: %s' % hists)
    else:
        notes.append('history counter non-decreasing across %d frame(s) (max %s)'
                     % (len(frames), max(hists) if hists else 'n/a'))
    withrow = [f for f in frames if f.get('unattrRow') is not None]
    if not withrow:
        if problems:
            # a real violation found above outranks "the legend cannot be checked": never let the
            # legacy early-return swallow a problem the gate already proved.
            return (gate.ERROR, notes, problems, notes, warnings)
        return (gate.LEGACY_NOT_APPLICABLE,
                notes + ['the heartbeat in this log carries no unattrRow field: it was written by a build '
                         'older than 1.7.9, whose [UI-DIAG] line cannot show which unattributed figure the '
                         'panel printed. The caption text therefore cannot be checked from THIS log -- '
                         'which is not the same as it being correct.'],
                problems, notes, warnings)
    n_ok = 0
    live_partial = []
    live_in_domain = []
    for f in withrow:
        row = f['unattrRow'] or ''
        if M_NA in row:
            continue
        if M_OUT in row and '(x' not in row:
            # 1.7.11: several rows carry the 未归属 marker WITHOUT a hit count. The F6 detail line is
            # "敌方总伤害 X   未归属(无法归一敌方) N   (设置可关闭敌方栏)", and there N is the LIVE session
            # figure at that instant -- the live 1.7.10 log runs 0 ... 106,529 while that battle's own
            # export says 325,381. Comparing a live partial against a finished battle's total is a
            # category error, so this is "unverifiable", never "contradicted".
            live_partial.append(row)
            continue
        val = num_after(row, M_OUT) if M_OUT in row else (num_after(row, M_IN) if M_IN in row else num_of(row))
        if val is None:
            continue
        if M_OUT in row:
            if val in session_unattr:
                n_ok += 1
            elif not pairs:
                # No export line at all: the log cannot corroborate ANY figure, so this is "unverifiable",
                # not "contradicted". Calling it an error would redden a legitimately truncated log.
                warnings.append('overlay frame printed %r but the log names no exported battle, so nothing '
                                'can corroborate it' % row)
            else:
                problems.append('overlay frame printed %r but no export of this session carries that '
                                'unattributed figure (known: %s)' % (row, sorted(session_unattr)))
        elif M_IN in row:
            # in-domain unattributed credit; must match some exported contribution.unattributedDamage
            known = set()
            for e, _b in pairs:
                if os.path.isfile(e['path']):
                    try:
                        with io.open(e['path'], 'r', encoding='utf-8') as fh:
                            known.add((json.load(fh).get('contribution') or {}).get('unattributedDamage'))
                    except Exception:
                        pass
            if _matches_in_domain(val, known):
                n_ok += 1
            elif not pairs:
                warnings.append('overlay frame printed %r but the log names no exported battle, so nothing '
                                'can corroborate it' % row)
            elif not _battle_finished_by(f, parsed):
                # R62: the panel is showing a battle that has not ended yet, so this is a LIVE partial of
                # a growing figure. Sibling of the 未归属 carve-out above (same measured category error).
                live_in_domain.append(row)
            else:
                problems.append('overlay frame printed %r but no export carries that in-domain '
                                'unattributedDamage (known: %s)' % (row, sorted(x for x in known if x is not None)))
    if live_partial:
        warnings.append('%d overlay frame(s) printed a 未归属 row WITHOUT a hit count (a live partial '
                        'surface, e.g. %r): no export can corroborate those, and they are not claimed to be '
                        'correct' % (len(live_partial), live_partial[0]))
    if live_in_domain:
        warnings.append('%d overlay frame(s) printed the in-domain 未归因 row for a battle that had NOT '
                        'ended yet (a live partial of a growing figure, e.g. %r): a finished export cannot '
                        'corroborate it, and it is not claimed to be correct' % (len(live_in_domain), live_in_domain[0]))
    if n_ok:
        notes.append('%d overlay frame(s) carried an unattributed row agreeing with an export' % n_ok)
    else:
        notes.append('%d frame(s) carry unattrRow but none held a comparable figure' % len(withrow))
    status = gate.PASS if not problems else gate.ERROR
    if status == gate.PASS and warnings:
        status = gate.WARNING
    return (status, notes, problems, notes, warnings)

def _battle_finished_by(frame, parsed):
    """Was the battle this frame DISPLAYS already over when the frame was printed?

    R62: the F5 contribution panel re-renders every second and prints the CURRENT, still-growing
    in-domain unattributed figure. MEASURED 2026-10-05 on the live log of a 17-battle training session:
    14 frames showed 2,189 / 101 / 285 / 508 / 2,747 / 288 / 462 / 3,302 / 3,598 at 3..43 s, while every
    export of that session settles at 0.0 -- those frames belong to a battle that had NOT ended yet (its
    own end line says dur=61 s). Comparing a live partial against a finished export is the same category
    error the 未归属 branch above already documents, so the same carve-out applies here: 'unverifiable',
    never 'contradicted'.

    Two signals keep the carve-out from swallowing a real disagreement of a FINISHED battle (the existing
    self-test case `in_domain_row_beyond_rounding_is_an_error` is the control):
      * the header's displayed seconds equal the duration of the most recent battle end for the same
        quest (the panel prints elapsed seconds as an integer), and
      * those seconds are NOT also the duration of the next battle end -- if they are, the same integer
        belongs to the battle in progress, so the frame is a live partial of it.
    """
    m = re.search(re.escape(M_TASK) + r"\s*(\d+)\s+([0-9]+)", frame.get('firstRow') or '')
    if not m:
        return False
    quest, secs = int(m.group(1)), int(m.group(2))
    line = frame.get('line', 10 ** 9)
    before = [b for b in parsed['ends'] if b['line'] < line and b['quest'] == quest]
    if not before:
        return False
    prev = before[-1]
    if secs not in (int(prev['dur']), int(round(prev['dur']))):
        return False
    after = [b for b in parsed['ends'] if b['line'] > line and b['quest'] == quest]
    if after and secs in (int(after[0]['dur']), int(round(after[0]['dur']))):
        return False
    return True


def _matches_in_domain(val, known):
    """The panel prints this figure ROUNDED to an integer ("9,326,541") while the export carries a double
    ("9326541.024"), so an exact membership test rejects a value the panel printed correctly. MEASURED
    2026-10-04: 102 frames of one live battle were reported as ERROR for exactly that reason, while the
    export value was present in the known set all along. The tolerance is the rounding interval, +/-0.5,
    and nothing wider -- a real disagreement is orders of magnitude larger."""
    for c in known:
        if c is None:
            continue
        try:
            if abs(float(val) - float(c)) <= 0.5:
                return True
        except (TypeError, ValueError):
            continue
    return False


def selftest():
    tmp = tempfile.mkdtemp(prefix='livelog_')
    fails = 0
    def case(label, log_text, export_doc, want, tamper=None):
        nonlocal fails
        d = os.path.join(tmp, label)
        os.makedirs(d, exist_ok=True)
        exp = os.path.join(d, 'battle_411001_x.json')
        doc = json.loads(json.dumps(export_doc))
        if tamper:
            tamper(doc)
        with io.open(exp, 'w', encoding='utf-8') as fh:
            fh.write(json.dumps(doc))
        lp = os.path.join(d, 'log.txt')
        text = log_text.replace('__EXP__', exp).replace('__BYTES__', str(len(json.dumps(doc))))
        with io.open(lp, 'w', encoding='utf-8') as fh:
            fh.write(text)
        st, reasons, probs, _n, _w = run(lp)
        ok = (st == want)
        print('  [%s] %s -- status=%s want=%s%s' % ('PASS' if ok else 'FAIL', label, st, want,
                                                    ('; ' + ascii_safe(probs[0])[:110]) if probs else ''))
        if not ok:
            fails += 1
    base_doc = {'version': '1.7.9', 'quest': 411001, 'result': 'Lose', 'duration': 119.07,
                'totals': {'unattributedDamage': 257056, 'unattributedHits': 73},
                'contribution': {'damageLedger': {'unknownAttackerDealt': 257056, 'unknownAttackerHits': 73},
                                 'unattributedDamage': 0}}
    frame19 = ("[Info   :  DpsMeter] [DpsMeter][UI-DIAG] canvas=True visible=True "
               "view=Contribution panel=780x797 rowsTotal=450 rowsActive=53 hist=1 "
               "fontNull=False contentH=797 viewH=789 scroll=0 firstRow='%s 411001 119' "
               "unattrRow='! %s 257056(x73)'\n" % (M_TASK, M_OUT))
    base_log = ('[Info   :   BepInEx] Loading [DpsMeter 1.7.9]\n'
                '[Info   :  DpsMeter] [DpsMeter] DpsMeter 1.7.9 loaded. F8 show/hide\n'
                '[Info   :  DpsMeter] [DpsMeter] Battle end: result=Lose dur=119.1s idle=0.0s quest=411001 '
                'actors=210 unattributed=257056(x73)\n'
                '[Info   :  DpsMeter] [DpsMeter] Exported full battle data -> __EXP__ (__BYTES__ bytes)\n'
                + frame19)
    case('agree', base_log, base_doc, gate.PASS)
    case('machine_vs_export_mismatch', base_log, base_doc, gate.ERROR,
         tamper=lambda d: d['totals'].__setitem__('unattributedDamage', 1))
    case('ledger_disagrees', base_log, base_doc, gate.ERROR,
         tamper=lambda d: d['contribution']['damageLedger'].__setitem__('unknownAttackerDealt', 5))
    case('wrong_version', base_log, base_doc, gate.ERROR,
         tamper=lambda d: d.__setitem__('version', '1.7.8'))
    # legacy heartbeat (pre-1.7.9) -> LEGACY_NOT_APPLICABLE, and it must SAY why
    legacy = base_log.replace(" unattrRow='! %s 257056(x73)'" % M_OUT, "")
    case('legacy_heartbeat', legacy, base_doc, gate.LEGACY_NOT_APPLICABLE)
    # the vacuity the adversarial review found: frames but no exported battle must NOT be a clean PASS
    no_export = ('[Info   :   BepInEx] Loading [DpsMeter 1.7.9]\n'
                 '[Info   :  DpsMeter] [DpsMeter] DpsMeter 1.7.9 loaded. F8 show/hide\n'
                 + frame19)
    case('frames_but_no_export_is_not_a_vacuous_pass', no_export, base_doc, gate.WARNING)
    # control: the same frame WITH its export line is a PASS
    case('frames_with_export_is_a_pass', base_log, base_doc, gate.PASS)
    case('unattrrow_agrees', base_log, base_doc, gate.PASS)
    badf = base_log.replace('257056(x73)', '999999(x73)')
    case('unattrrow_mismatch', badf, base_doc, gate.ERROR)
    # 2026-10-04: the panel prints the in-domain figure rounded, the export carries a double. An exact
    # membership test turned 102 live frames into false ERRORs, so the control is now paired:
    in_ok = base_log.replace("unattrRow='! %s 257056(x73)'" % M_OUT,
                             "unattrRow='\u5408\u8ba1 194,697,612   \u672a\u5f52\u56e0 9,326,541(4.75%)'")
    case('in_domain_row_rounds_to_the_export', in_ok, base_doc, gate.PASS,
         tamper=lambda d: d['contribution'].__setitem__('unattributedDamage', 9326541.024))
    in_bad = base_log.replace("unattrRow='! %s 257056(x73)'" % M_OUT,
                              "unattrRow='\u5408\u8ba1 194,697,612   \u672a\u5f52\u56e0 9,326,600(4.75%)'")
    case('in_domain_row_beyond_rounding_is_an_error', in_bad, base_doc, gate.ERROR,
         tamper=lambda d: d['contribution'].__setitem__('unattributedDamage', 9326541.024))
    # 1.7.11: the REAL F6 summary row -- a bigger number (合计) precedes the 未归因 marker. Parsing the
    # first number in the row made this a false ERROR on a live log; the export below carries 0.
    f6row = ('\u5408\u8ba1 194,697,612   \u672a\u5f52\u56e0 0(0.00%)   \u547d\u4e2d 5,493   '
             '\u500d\u7387\u6c60 168,197,251')
    f6log = base_log.replace("unattrRow='! %s 257056(x73)'" % M_OUT, "unattrRow='%s'" % f6row)
    case('real_f6_summary_row_agrees', f6log, base_doc, gate.PASS)
    case('real_f6_summary_row_mismatch', f6log, base_doc, gate.ERROR,
         tamper=lambda d: d['contribution'].__setitem__('unattributedDamage', 7))
    # 1.7.11: the F6 enemy line prints the LIVE 未归属 partial (0 .. 106,529 in the live log) while the
    # battle's export says 325,381 -- it must be unverifiable, not "wrong".
    enemy_row = ('\u654c\u65b9\u603b\u4f24\u5bb3 390   \u672a\u5f52\u5c5e(\u65e0\u6cd5\u5f52\u4e00\u654c\u65b9) 0   '
                 '(\u8bbe\u7f6e\u53ef\u5173\u95ed\u654c\u65b9\u680f)')
    enemy_log = base_log.replace("unattrRow='! %s 257056(x73)'" % M_OUT, "unattrRow='%s'" % enemy_row)
    case('live_enemy_line_is_unverifiable_not_wrong', enemy_log, base_doc, gate.WARNING)
    # R62: the F5 panel re-renders every second, so a frame printed BEFORE its battle's end line shows a
    # LIVE partial of a still-growing in-domain figure. Measured 2026-10-05: 14 such frames (2,189 / 101 /
    # 3,598 ... at 3..43 s) were ERRORs against exports of the same session that settle at 0.0.
    live_in_log = ('[Info   :   BepInEx] Loading [DpsMeter 1.7.9]\n'
                   '[Info   :  DpsMeter] [DpsMeter] DpsMeter 1.7.9 loaded. F8 show/hide\n'
                   "[Info   :  DpsMeter] [DpsMeter][UI-DIAG] canvas=True visible=True "
                   "view=Contribution panel=780x797 rowsTotal=450 rowsActive=53 hist=1 "
                   "fontNull=False contentH=797 viewH=789 scroll=0 firstRow='%s 411001 3' "
                   "unattrRow='\u5408\u8ba1 100   \u672a\u5f52\u56e0 2198(2.00%%)'\n" % M_TASK
                   + '[Info   :  DpsMeter] [DpsMeter] Battle end: result=Lose dur=119.1s idle=0.0s quest=411001 '
                   'actors=210 unattributed=257056(x73)\n'
                   '[Info   :  DpsMeter] [DpsMeter] Exported full battle data -> __EXP__ (__BYTES__ bytes)\n')
    case('live_in_domain_partial_is_unverifiable_not_wrong', live_in_log, base_doc, gate.WARNING)
    placeholder = base_log.replace('%s 411001 119' % M_TASK, '%s 0   0' % M_TASK)
    case('placeholder_header', placeholder, base_doc, gate.ERROR)
    shutil.rmtree(tmp, ignore_errors=True)
    print('---- selftest: %s' % ('PASS' if fails == 0 else 'FAIL (%d)' % fails))
    return 0 if fails == 0 else 1

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--log', default=DEF_LOG)
    ap.add_argument('--selftest', action='store_true')
    a = ap.parse_args()
    if a.selftest:
        return selftest()
    status, reasons, problems, notes, warnings = run(a.log)
    print('live-log gate: %s' % status)
    for n in notes:
        print('  note: %s' % ascii_safe(n))
    for w in warnings:
        print('  warn: %s' % ascii_safe(w))
    for p in problems:
        print('  ERROR: %s' % ascii_safe(p))
    if status in (gate.LEGACY_NOT_APPLICABLE, gate.DATA_MISSING):
        for r in reasons:
            print('  reason: %s' % ascii_safe(r))
    print('---- exit %d' % gate.EXIT[status])
    return gate.EXIT[status]

if __name__ == '__main__':
    sys.exit(main())
