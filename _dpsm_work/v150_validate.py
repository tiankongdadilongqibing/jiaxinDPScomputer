# -*- coding: utf-8 -*-
"""
1.5.0 runtime validation -- one command for the next battle.

WHAT IT ANSWERS
    1. is the export structurally sound and schema-complete?      (the thing the compiler cannot check)
    2. did the three NEW channels actually produce data?          (HITDET / FACT / TIMELINE)
    3. how well does the crit INFERENCE agree with the GAME's own crit flag?
    4. did the KPI hold up against the same quest's history?      (the objective's "KPI 不下降")
    5. what does the new provenance say about the 1.15^n residual? (which rule absorbed how many copies)

WHY THE BASELINE IS NOW AN EXPLICIT LIST (N1, roadmap section N1)
    "KPI did not go down" is only meaningful against the same content. The baseline used to be selected
    IMPLICITLY ("same quest and version < 1.5.0"), and the current corpus cannot satisfy that at all --
    there is no pre-1.5.0 export left -- so the regression branch had become unreachable while still
    looking alive. The eligible samples are now an EXPLICIT list in kpi_baseline.json with a pinned
    SHA256 per sample:
      * it must be listed AND its file must hash to the pinned value (a drift is reported, not used);
      * same quest as the target, and STRICTLY older version than the target;
      * the metric must exist and its definition must match (exact since 1.3.0, exactWithCrit 1.3.9);
      * reconcile.withCalc > 0.
    kpi_baseline.json stores NO KPI value -- the numbers are always read from the hashed export, so there
    is one source of truth. Fewer than 2 usable samples => the report says 不可判 and claims nothing.
    The known damaged 1.6.0 sample is pinned in the file as EXCLUDED (roadmap 2.2 keeps it a negative).
    Rebuild with: python v150_validate.py --rebuild-baseline

USAGE
    python v150_validate.py [export.json]
    (no argument: newest export)
Writes a UTF-8 report to _dpsm_work/v150_validate.txt; stdout is ASCII-only (GBK console).
"""

import glob
import hashlib
import io
import json
import os
import shutil
import subprocess
import sys
import tempfile
import collections

ROOT = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(ROOT)
EXPORTS = os.path.join(REPO, 'BepInEx', 'plugins', 'DpsMeter', 'exports')
OUT = os.path.join(ROOT, 'v150_validate.txt')


def vt(v):
    try:
        p = [int(x) for x in str(v).split('.')[:3]]
        while len(p) < 3:
            p.append(0)
        return tuple(p)
    except Exception:
        return (0, 0, 0)


def load(paths):
    for p in paths:
        try:
            yield p, json.load(io.open(p, encoding='utf-8'))
        except Exception:
            continue


BASELINE_DEFAULT = os.path.join(ROOT, 'kpi_baseline.json')
KNOWN_DAMAGED = {'battle_411001_20261004_015919.json':
                 '1.6.0 known damaged sample (58 factor-truncation mismatches): roadmap 2.2 keeps it as a '
                 'NEGATIVE example, so it must never enter a KPI baseline'}
ELIGIBILITY = [
    'listed in this file AND the file hashes to the pinned SHA256',
    'quest == the target question',
    'version strictly older than the target version',
    'the metric exists and its definition matches (exact since 1.3.0, exactWithCrit since 1.3.9)',
    'reconcile.withCalc > 0']


def parse_args(argv):
    argv = list(argv[1:])
    def take(flag, default):
        if flag in argv:
            i = argv.index(flag)
            if i + 1 < len(argv):
                v = argv[i + 1]
                del argv[i:i + 2]
                return v
            del argv[i]
        return default
    baseline = take('--baseline', BASELINE_DEFAULT)
    exports_dir = take('--exports', EXPORTS)
    out_path = take('--out', OUT)
    rebuild = '--rebuild-baseline' in argv
    if rebuild:
        argv.remove('--rebuild-baseline')
    do_selftest = '--selftest' in argv
    if do_selftest:
        argv.remove('--selftest')
    return ([a for a in argv if not a.startswith('--')], baseline, rebuild, exports_dir, out_path,
            do_selftest)


def sha256_file(path, chunk=1 << 20):
    h = hashlib.sha256()
    with io.open(path, 'rb') as fh:
        while True:
            b = fh.read(chunk)
            if not b:
                break
            h.update(b)
    return h.hexdigest().upper()


def build_baseline(paths):
    """Regenerate the explicit list from the corpus. No KPI value is stored."""
    approved, excluded = [], []
    for p in paths:
        try:
            d = json.load(io.open(p, encoding='utf-8'))
        except Exception:
            continue
        name = os.path.basename(p)
        if name in KNOWN_DAMAGED:
            excluded.append({'file': name, 'sha256': sha256_file(p), 'why': KNOWN_DAMAGED[name]})
            continue
        rec = d.get('reconcile') or {}
        if not rec.get('withCalc'):
            continue
        if vt(d.get('version')) < (1, 3, 9):
            continue
        approved.append({'file': name, 'sha256': sha256_file(p), 'quest': d.get('quest'),
                         'version': str(d.get('version'))})
    approved.sort(key=lambda x: (x['quest'] or 0, vt(x['version']), x['file']))
    return {'contract': 'kpi-baseline/1',
            'why': 'The selector used to be implicit (same quest, version < 1.5.0) and became unreachable; '
                   'this list makes the eligible samples explicit and hash-pinned.',
            'eligibility': ELIGIBILITY,
            'definitionVersions': {'exact': '1.3.0', 'exactWithCrit': '1.3.9'},
            'regressionCriterion': 'current exactWithCrit below the historical minimum by more than 3.0 percentage points',
            'valuesSource': 'read from the hashed export at run time; this file stores no KPI value',
            'approved': approved, 'excluded': excluded}


def select_baseline(target_path, target_doc, base, exports_dir):
    """(used, rejected). Every rejection carries its own reason -- a silently dropped sample is a bug."""
    used, rejected = [], []
    tq = target_doc.get('quest')
    tv = vt(target_doc.get('version'))
    for e in (base or {}).get('approved') or []:
        name = e.get('file') or '?'
        p = os.path.join(exports_dir, name)
        if not os.path.isfile(p):
            rejected.append((name, 'file missing'))
            continue
        h = sha256_file(p)
        if h != (e.get('sha256') or '').upper():
            rejected.append((name, 'sha256 drift: pinned %s, found %s' % ((e.get('sha256') or '')[:12], h[:12])))
            continue
        if e.get('quest') != tq:
            rejected.append((name, 'quest %s != target %s' % (e.get('quest'), tq)))
            continue
        try:
            d = json.load(io.open(p, encoding='utf-8'))
        except Exception as ex:
            rejected.append((name, 'unreadable (%r)' % (ex,)))
            continue
        fv = str(d.get('version'))
        if e.get('version') and str(e.get('version')) != fv:
            rejected.append((name, 'entry says version %s but the file says %s (pinned metadata must match the'
                                   ' hashed file, exactly like the sha256)' % (e.get('version'), fv)))
            continue
        if vt(fv) >= tv:
            rejected.append((name, 'version %s is not older than the target %s'
                             % (fv, target_doc.get('version'))))
            continue
        r = d.get('reconcile') or {}
        if not r.get('withCalc'):
            rejected.append((name, 'reconcile.withCalc = 0'))
            continue
        used.append((name, str(d.get('version')), r))
    return used, rejected


def mainline(argv):
    paths, baseline_path, rebuild, exports_dir, out_path, do_selftest = parse_args(argv)
    if do_selftest:
        return selftest()
    allf = sorted(glob.glob(os.path.join(exports_dir, '*.json')), key=os.path.getmtime)
    if rebuild:
        base = build_baseline(allf)
        io.open(baseline_path, 'w', encoding='utf-8').write(
            json.dumps(base, ensure_ascii=False, indent=1).encode('utf-8') if sys.version_info[0] == 2
            else json.dumps(base, ensure_ascii=False, indent=1))
        print('baseline -> %s (approved=%d excluded=%d)'
              % (baseline_path, len(base['approved']), len(base['excluded'])))
        return 0
    target = paths[0] if paths else (allf[-1] if allf else None)
    if not target:
        print('no export yet (cleared 2026-10-03; re-run after the next battle)')
        return 0
    baseline_doc, baseline_error = {}, None
    if os.path.isfile(baseline_path):
        try:
            baseline_doc = json.load(io.open(baseline_path, encoding='utf-8'))
        except Exception as ex:
            baseline_error = 'unreadable: %r' % (ex,)
    else:
        baseline_error = 'missing: %s' % baseline_path
    d = json.load(io.open(target, encoding='utf-8'))
    ver = str(d.get('version'))
    quest = d.get('quest')
    rec = d.get('reconcile') or {}
    ev = d.get('events') or []
    out = []
    bad = 0

    out.append('=' * 96)
    out.append('1.5.0 验证报告')
    out.append('  文件     %s' % os.path.basename(target))
    out.append('  版本     %s     任务 %s     时长 %ss     结果 %s' % (ver, quest, d.get('duration'), d.get('result')))

    # ---- 1. schema / structure -------------------------------------------------------------
    out.append('')
    out.append('--- 1. 结构与 schema ---')
    if vt(ver) < (1, 5, 0):
        out.append('  !! 版本 < 1.5.0 —— 这不是一份 1.5.0 导出,下面的新通道全部为 N/A。')
        out.append('     1.5.0 是否真的被加载? 看 BepInEx 日志里的版本行。')
        bad += 1
    need = ['hitDetail', 'timeline', 'facts', 'factCoverage', 'giveTypeList', 'resistMaster',
            'statusValues', 'layout', 'liveRange', 'subParams']
    missing = [k for k in need if k not in d]
    out.append('  1.5.0 根键缺失: %s' % ('无' if not missing else ', '.join(missing)))
    if missing:
        bad += 1
    out.append('  (结构合法性由插件自己的 JsonCheck 在导出时判过 —— 看日志里 [DpsMeter][JSON] 结构=OK)')

    # ---- 2. new channels -------------------------------------------------------------------
    out.append('')
    out.append('--- 2. 三条新通道是否真的在工作 ---')
    hd = d.get('hitDetail') or {}
    if hd:
        prod = hd.get('produced', 0)
        me = hd.get('matchExact', 0)
        mp = hd.get('matchPair', 0)
        mn = hd.get('matchNone', 0)
        cov = 100.0 * (me + mp) / prod if prod else 0.0
        out.append('  [HITDET] 产出=%d 精确=%d 弱=%d 未匹配=%d 丢弃=%d 错=%d  => 匹配率 %.1f%%'
                   % (prod, me, mp, mn, hd.get('trimmed', 0), hd.get('errors', 0), cov))
        out.append('           期望: 产出≈伤害事件数, 匹配率接近 100%, 「弱」占比应很小')
        if prod == 0:
            bad += 1
            out.append('           !! 产出为 0 —— 通道没接线,source/crit 仍是常量')
        if cov < 90 and prod:
            out.append('           !! 匹配率偏低,值得查(弱匹配会给出「尽力猜」的 source/crit)')
    fc = d.get('factCoverage') or {}
    f = d.get('facts') or {}
    evd = len([e for e in ev if e.get('type') == 'dmg'])
    new_ver = vt(ver) >= (1, 5, 0)
    if not new_ver:
        out.append('  (N/A —— 版本 < 1.5.0,这三个通道不存在于这份导出里)')
    if fc:
        dm, wf, wl = fc.get('dmg', 0), fc.get('withFact', 0), fc.get('withLiveState', 0)
        out.append('  [FACT]   伤害事件=%d 有事实=%d 含活体=%d  类=%d 含活体类=%d 类溢出=%d 活体略过=%d'
                   % (dm, wf, wl, f.get('classes', 0), f.get('liveClasses', 0),
                      f.get('classesOverflow', 0), f.get('liveSkipped', 0)))
        if dm:
            out.append('           覆盖率 %.1f%%  (目标≈100%%;1.4.0 的取证只覆盖 160/%d)' % (100.0 * wf / dm, dm))
            if wf < dm:
                out.append('           !! 有 %d 击没有事实 —— 类表溢出或通道被关' % (dm - wf))
        if f.get('classesOverflow'):
            out.append('           !! 类溢出 %d —— MaxClasses(%d) 偏小,应提高' % (f['classesOverflow'], f.get('maxClasses')))
    tm = d.get('timeline') or {}
    if tm:
        out.append('  [TIMELINE] 读取=%d 单位=%d 行=%d 状态变=%d 抗性变=%d 丢=%d 非角色=%d 错=%d'
                   % (tm.get('observed', 0), tm.get('units', 0), tm.get('rowCount', 0),
                      tm.get('statusChanges', 0), tm.get('resistChanges', 0), tm.get('rowsDropped', 0),
                      tm.get('notCharacter', 0), tm.get('errors', 0)))
        out.append('           期望: 读取≈伤害事件数;抗性变>0 才说明「抗性会动」被实测捕捉(1.4.1 只有 64 点采样)')
        if tm.get('observed', 0) == 0:
            bad += 1
            out.append('           !! 没有读取 —— 时间线通道没工作')

    # ---- 3. crit inference vs the game's own flag ------------------------------------------
    out.append('')
    out.append('--- 3. 会心:推断 vs 实测 ---')
    co = rec.get('critObserved', 0)
    if co:
        out.append('  实测=%d (其中是会心=%d)  推断一致=%d  实测否决=%d  实测是会心但算不平=%d'
                   % (co, rec.get('critObservedYes', 0), rec.get('critAgree', 0),
                      rec.get('critInferredButDenied', 0), rec.get('critObservedButUnexplained', 0)))
        out.append('  凭会心新增=%d  => 其中被实测证实的比例 %.1f%%'
                   % (rec.get('critInferred', 0),
                      100.0 * rec.get('critAgree', 0) / rec.get('critInferred', 1) if rec.get('critInferred') else 0.0))
        if rec.get('critInferredButDenied'):
            out.append('  !! 「实测否决」非零 —— 说明有命中被算成了会心而游戏说不是,口径需要改')
    else:
        out.append('  实测=0 —— FlyText 钩子这场一次都没匹配上(不是致命,但会心口径仍只能靠推断)')

    # ---- 4. KPI vs the APPROVED baseline list (N1) ------------------------------------------
    out.append('')
    out.append('--- 4. KPI 与批准基线列表的对照 ---')
    if baseline_error:
        base, rejected = [], []
        out.append('  基线文件不可用(%s)—— 不判定"是否下降",只报本场数字' % baseline_error)
    else:
        base, rejected = select_baseline(target, d, baseline_doc, exports_dir)
        out.append('  基线契约 %s | 批准 %d 份 | 本场可用 %d 份 | 未采用 %d 份'
                   % (baseline_doc.get('contract', '?'), len(baseline_doc.get('approved') or []),
                      len(base), len(rejected)))
        for f, why in rejected[:8]:
            out.append('    [未采用] %s —— %s' % (f, why))
        if len(rejected) > 8:
            out.append('    [未采用] ... 另 %d 份(完整清单见报告末尾)' % (len(rejected) - 8))
    if base:
        def pct(r, k):
            w = r.get('withCalc', 0)
            return 100.0 * r.get(k, 0) / w if w else 0.0
        ex = [pct(r, 'exact') for _, v, r in base if vt(v) >= (1, 3, 0) and 'exact' in r]
        ew = [pct(r, 'exactWithCrit') for _, v, r in base if vt(v) >= (1, 3, 9) and 'exactWithCrit' in r]
        out.append('  基线(同任务 %s,批准清单中可用 %d 份;按"该指标存在且口径一致"的版本过滤):' % (quest, len(base)))
        # 口径必须匹配: `exact` 的定法(含被吸收)自 1.3.0 才固定,`exactWithCrit` 自 1.3.9 才有。
        # 把没有该键的旧版本算进来会让中位数和最低值都变成 0,于是"没有下降"变成一句空话 ——
        # 这正是本项目一贯反对的"两个不可比的口径放在一起比"。
        if len(ex) >= 2:
            out.append('      exact          n=%d  中位 %.1f%%  范围 %.1f–%.1f%%  (口径自 1.3.0)'
                       % (len(ex), sorted(ex)[len(ex) // 2], min(ex), max(ex)))
        else:
            out.append('      exact          n=%d —— 样本不足,不做判定' % len(ex))
        if len(ew) >= 2:
            out.append('      exactWithCrit  n=%d  中位 %.1f%%  范围 %.1f–%.1f%%  (口径自 1.3.9)'
                       % (len(ew), sorted(ew)[len(ew) // 2], min(ew), max(ew)))
        else:
            out.append('      exactWithCrit  n=%d —— 样本不足,不做判定' % len(ew))
        w = rec.get('withCalc', 0)
        if w:
            ne, nw = pct(rec, 'exact'), pct(rec, 'exactWithCrit')
            out.append('  本场:  withCalc=%d  exact=%.1f%%  exactWithCrit=%.1f%%  approx=%d  unexplained=%d  theoryExceeds=%d'
                       % (w, ne, nw, rec.get('approx', 0), rec.get('unexplained', 0), rec.get('theoryExceeds', 0)))
            if len(ew) >= 2:
                if nw + 1e-9 < min(ew) - 3.0:
                    bad += 1
                    out.append('  !! exactWithCrit 低于历史最低值 3 个百分点以上 —— 视为回归,先查是不是新开关改了算术')
                else:
                    out.append('  => exactWithCrit 未低于历史范围(判据:不低于历史最低值 3pp)')
            else:
                out.append('  => 历史样本不足,只报本场数字,不判定"是否下降"')
    else:
        out.append('  => 可用基线不足 2 份 —— 不可判:既不声称"没有下降",也不声称"下降"')
        out.append('  exact=%d exactWithCrit=%d withCalc=%d' % (rec.get('exact', 0), rec.get('exactWithCrit', 0), rec.get('withCalc', 0)))

    # ---- 5. the new provenance vs the 1.15^n question ---------------------------------------
    out.append('')
    out.append('--- 5. 新溯源能回答什么(1.15^n 的成因) ---')
    fold_kinds = collections.Counter()
    origins = collections.Counter()
    maxabs = collections.Counter()
    cancels = collections.Counter()
    steps_total = 0
    for e in ev:
        c = e.get('calc')
        if not isinstance(c, dict):
            continue
        for st in (c.get('fold') or []):
            steps_total += 1
            fold_kinds[st.get('kind')] += 1
            origins[(st.get('kind'), round(float(st.get('factor', 1.0)), 4))] += 1
        if c.get('maxAbsorbed'):
            maxabs[c['maxAbsorbed']] += 1
        for cs in (c.get('cancel') or []):
            cancels[cs.get('by')] += 1
    out.append('  折叠步数合计=%d  按通道: %s' % (steps_total, dict(fold_kinds)))
    out.append('  被取消的授予按「吸收者」Top5: %s' % (cancels.most_common(5),))
    out.append('  maxAbsorbed 分布(一条责任规则吸收了几份授予): %s' % (dict(sorted(maxabs.items())),))
    if maxabs and max(maxabs) > 1:
        out.append('  ==> 存在「一条规则吸收多份授予」(%d 击) —— 这正是「1.15^n 是不是取消过头」的直接证据。'
                   % sum(n for k, n in maxabs.items() if k > 1))
    else:
        out.append('  本场没有「一条规则吸收多份」的形状。')
    out.append('  按通道+因子值统计的 Top8: %s'
               % sorted(origins.items(), key=lambda kv: -kv[1])[:8])

    if baseline_error is None and rejected:
        out.append('')
        out.append('--- 附:基线未采用清单(%d)---' % len(rejected))
        for f, why in rejected:
            out.append('    %s —— %s' % (f, why))

    out.append('')
    out.append('=' * 96)
    out.append('结论: %s' % ('全部检查通过' if bad == 0 else '%d 项需要看' % bad))
    io.open(out_path, 'w', encoding='utf-8').write('\n'.join(out) + '\n')
    print('report -> %s' % out_path)
    print('version=%s quest=%s problems=%d' % (ver, quest, bad))
    return 1 if bad else 0


def selftest():
    """Negative controls for the baseline contract, run through THIS CLI (N1: a check must be seen to say no)."""
    me = os.path.abspath(__file__)
    py = sys.executable
    tmp = tempfile.mkdtemp(prefix="kpi_")
    ex_dir = os.path.join(tmp, "exports")
    os.makedirs(ex_dir)
    basep = os.path.join(tmp, "baseline.json")
    fails = []
    TARGET = "battle_411001_20261004_144548.json"
    ENT = ["battle_411001_20261004_134853.json", "battle_411001_20261004_134524.json"]

    def cp(name, version=None, ew=None, newname=None):
        d = json.load(io.open(os.path.join(EXPORTS, name), encoding="utf-8"))
        if version:
            d["version"] = version
        if ew is not None:
            r = d.setdefault("reconcile", {})
            w = r.get("withCalc") or 0
            if w:
                r["exactWithCrit"] = int(round(ew * w / 100.0))
                r["exact"] = int(round(ew * w / 100.0))
        nm = newname or name
        p = os.path.join(ex_dir, nm)
        io.open(p, "w", encoding="utf-8").write(json.dumps(d, ensure_ascii=False))
        return nm, sha256_file(p), str(d.get("version"))

    def write_base(entries):
        io.open(basep, "w", encoding="utf-8").write(
            json.dumps({"contract": "kpi-baseline/1", "approved": entries, "excluded": []}, ensure_ascii=False))

    def run(tag, target_name):
        outp = os.path.join(tmp, "r_%s.txt" % tag)
        rc = subprocess.call([py, me, "--exports", ex_dir, "--baseline", basep, "--out", outp,
                              os.path.join(ex_dir, target_name)])
        return rc, io.open(outp, encoding="utf-8", errors="replace").read()

    def case(label, ok, extra=""):
        print("  [%s] %-56s %s" % ("PASS" if ok else "FAIL", label, extra))
        if not ok:
            fails.append(label)

    t_bad, _, _ = cp(TARGET, newname="target_low.json", ew=90.0)
    t_ok, _, _ = cp(TARGET, newname="target_ok.json")
    ents = []
    for n in ENT:
        nm, h, v = cp(n, version="1.7.8", newname="base_" + n)
        ents.append({"file": nm, "sha256": h, "quest": 411001, "version": v})

    write_base(ents)
    rc, txt = run("low", t_bad)
    case("a KPI below the baseline minimum - 3pp is called a regression",
         rc == 1 and "回归" in txt, "rc=%s" % rc)
    rc, txt = run("ok", t_ok)
    case("a KPI inside the baseline range is not a regression",
         rc == 0 and "未低于历史范围" in txt, "rc=%s" % rc)

    write_base(ents[:1])
    rc, txt = run("one", t_ok)
    case("one usable sample -> no verdict (样本不足)",
         rc == 0 and "不做判定" in txt and "回归" not in txt, "rc=%s" % rc)

    write_base([])
    rc, txt = run("none", t_ok)
    case("an empty baseline -> 不可判, no claim",
         rc == 0 and "不可判" in txt, "rc=%s" % rc)

    drifted = [dict(ents[0], sha256="DEADBEEF" * 4), ents[1]]
    write_base(drifted)
    rc, txt = run("drift", t_ok)
    case("a hash-drifted entry is reported and NOT used",
         rc == 0 and "sha256 drift" in txt, "rc=%s" % rc)

    liar = [dict(ents[0], version="1.7.10"), ents[1]]
    write_base(liar)
    rc, txt = run("liar", t_ok)
    case("an entry whose declared version disagrees with its file is not used",
         rc == 0 and "entry says version" in txt, "rc=%s" % rc)

    nm_new, h_new, v_new = cp(TARGET, version="1.7.10", newname="base_new.json")
    same = [{"file": nm_new, "sha256": h_new, "quest": 411001, "version": v_new}, ents[1]]
    write_base(same)
    rc, txt = run("samever", t_ok)
    case("a baseline entry that is not OLDER than the target is not used",
         rc == 0 and "is not older" in txt, "rc=%s" % rc)

    if os.path.isfile(BASELINE_DEFAULT):
        b = json.load(io.open(BASELINE_DEFAULT, encoding="utf-8"))
        ex = [e.get("file") for e in (b.get("excluded") or [])]
        ap = [e.get("file") for e in (b.get("approved") or [])]
        case("the real baseline EXCLUDES the 1.6.0 damaged sample",
             "battle_411001_20261004_015919.json" in ex and "battle_411001_20261004_015919.json" not in ap,
             "excluded=%d approved=%d" % (len(ex), len(ap)))
    else:
        print("  [SKIP] real baseline not found at %s" % BASELINE_DEFAULT)

    shutil.rmtree(tmp, ignore_errors=True)
    print("---- selftest: %s" % ("PASS" if not fails else "FAIL %s" % fails))
    return 0 if not fails else 1


if __name__ == "__main__":
    sys.exit(mainline(sys.argv))
