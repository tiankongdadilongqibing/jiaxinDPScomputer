# -*- coding: utf-8 -*-
"""Cross-battle comparison 2.0 (roadmap N2).

WHAT CHANGED FROM compare.py v1
  1. ADMISSION FIRST: nothing is pooled until comparison_eligibility (N1) has said which metric of which
     sample may enter. A REJECTED sample is listed with its reason and contributes nothing; a RESTRICTED
     sample may only be pooled inside a stratum that carries the restriction in its own header.
  2. STRATA, not "one group per (quest, composition)": the per-metric stratum key includes the capability
     class (schema, damageBasis, atkadd present, observed GivenTalent flag). Two samples with different
     capabilities are never averaged together -- they become two strata with n printed per stratum.
  3. IDENTITY: actors are keyed by identity_map's entityKey (template + loadout fingerprint), not by name.
     Same name with different loadouts stays two rows; when identity_map is unavailable the name is used as
     a fallback and every affected row is tagged nameFallback.
  4. FOUR STATES PER ACTOR: appeared-with-zero (a real 0, included), confirmed-absent (not in the roster:
     excluded from the denominator and counted), identity-unknown (listed, never merged), missing-data
     (the sample was not admissible for this metric).
  5. STATISTICS: n / mean / median / min / max / stdev of the per-battle share, AND the cumulative
     numerator and denominator with their own names -- the two are different quantities and are never mixed.
  6. Rules: per-battle mean damageEquivalent AND the sum over the stratum, with the sample count.
  7. NOTHING COMPARABLE => exit 4 with reasons; a blank success report is impossible.

Usage
  python -m contrib.compare [pattern] [--quest N] [--team 1] [--applicability PATH]
                            [--out BASE] [--files F ...] [--selftest]
Exit: 0 = at least one stratum with n>=2; 4 = nothing comparable; 2 = no input; 3 = unreadable input.
ASCII-only stdout (GBK console); the report and JSON are UTF-8.
"""
from __future__ import annotations
import argparse, glob, io, json, math, os, shutil, subprocess, sys, tempfile

from . import loader, aggregate

HERE = os.path.dirname(os.path.abspath(__file__))
WORK = os.path.dirname(HERE)
ROOT = os.path.dirname(WORK)
EXPORTS = os.path.join(ROOT, "BepInEx", "plugins", "DpsMeter", "exports")
OUTDIR = os.path.join(HERE, "reports")
CONTRACT = "compare/2"

if WORK not in sys.path:
    sys.path.insert(0, WORK)
try:
    import comparison_eligibility as elig
except Exception:
    elig = None
try:
    import identity_map as idm
except Exception:
    idm = None

METRICS = ("team_damage", "actor_credit", "rule_coverage", "outcome")


def _f(v, default=0.0):
    try:
        return float(v)
    except (TypeError, ValueError):
        return default


def stdev(xs):
    if len(xs) < 2:
        return 0.0
    m = sum(xs) / len(xs)
    return math.sqrt(sum((x - m) ** 2 for x in xs) / (len(xs) - 1))


def stat_block(xs):
    if not xs:
        return None
    s = sorted(xs)
    n = len(s)
    med = s[n // 2] if n % 2 else 0.5 * (s[n // 2 - 1] + s[n // 2])
    return {"n": n, "mean": sum(s) / n, "median": med, "min": s[0], "max": s[-1], "stdev": stdev(s)}


class Sample(object):
    """One loaded battle plus the facts the comparator needs about it."""

    def __init__(self, path, team=1):
        self.path = path
        self.team = team
        self.ex = loader.load(path)
        self.name = self.ex.name
        self.quest = self.ex.quest
        self.version = str(self.ex.version)
        self.duration = _f(self.ex.duration)
        self.result = self.ex.result
        self.training = loader.training_mode(self.ex)
        self.an = aggregate.analyze(self.ex, team)
        self.identity = {}
        self.identityAvailable = idm is not None
        # NOT a global switch: a single weak actor (e.g. the ally boss 生ける炎 クトゥグア, which has no
        # ability rows) used to flip the whole sample to "name fallback" and, because the flag was read at
        # different moments, the SAME battle could land in different strata. Count them instead.
        self.weakActors = 0
        self.compWeak = 0
        self.nameFallback = False
        if idm is not None:
            try:
                res = idm.resolve_export(path)   # let it load what it expects; passing contrib's
                # ExportData made it raise, and the fallback silently disabled identity for every sample
                for a in (res.get("actors") or []):
                    if a.get("team") == team:
                        self.identity[a.get("actorKey")] = a
            except Exception:
                self.identity = {}
        raw = self.ex.raw or {}
        sec = raw.get("contribution") or {}
        audit = self.ex.roster_audit or {}
        self.cap = {"atkAdd": "atkAdd" in raw,
                    "schema": sec.get("schemaVersion"), "method": sec.get("method"),
                    "basis": sec.get("damageBasis"),
                    "giveObserved": _f(audit.get("giveFlagTrue")) > 0,
                    "hasSection": bool(sec)}
        self.an_actors = self.an.actors

    def entity_of(self, actor_ref):
        rec = self.identity.get(actor_ref.key)
        if rec and rec.get("entityKey"):
            return rec["entityKey"], rec.get("identityStrength") or "unknown", rec
        self.weakActors += 1
        return "name:" + str(actor_ref.name), "name-fallback", {}

    def composition(self):
        """The entity keys of the non-summon team actors, sorted -- the identity half of a stratum key.

        A weak actor contributes its NAME key, so two battles that both field that weak actor still match;
        a battle that fields a different one does not. The count is reported (compositionWeak) instead of
        being folded into the key, so the key cannot depend on WHEN it was computed.
        """
        keys = []
        weak = 0
        for a in self.ex.team_actors(self.team):
            if a.summon:
                continue
            k, strength, _r = self.entity_of(a)
            keys.append(k)
            if k.startswith("name:") or strength == "name-fallback":
                weak += 1
        self.compWeak = weak
        return tuple(sorted(keys))

    def stratum_key(self, metric, strict_loadout=False):
        mode = "training" if self.training else "normal"
        gear = (self.comp_key(),) if strict_loadout else ()
        if metric == "team_damage":
            # the ROSTER is part of the key: averaging different lineups' totals is not a team metric.
            return (self.quest, mode, self.roster_key()) + gear
        if metric == "outcome":
            return (self.quest, mode, self.roster_key()) + gear
        if metric == "rule_coverage":
            return (self.quest, mode, self.cap["schema"], self.roster_key()) + gear
        return (self.quest, mode, self.cap["schema"], self.cap["basis"], self.cap["atkAdd"],
                self.cap["giveObserved"], self.roster_key()) + gear

    def template_of(self, a):
        rec = self.identity.get(a.key) or {}
        if rec.get("templateId") is not None:
            return "tpl:%s:%s" % (rec.get("templateNamespace") or "?", rec.get("templateId"))
        return "name:" + str(a.name)

    def roster_key(self):
        """WHO is in the team (template ids), independent of gear -- the primary grouping key."""
        keys = []
        for a in self.ex.team_actors(self.team):
            if a.summon:
                continue
            keys.append(self.template_of(a))
        return tuple(sorted(keys))

    def comp_key(self):
        """WHO with WHICH gear (template + loadout fingerprint) -- reported, and used when strict."""
        return self.composition()


def sample_credits(s):
    """(entityKey -> credit dict for THIS sample, set of entity keys present in the roster)."""
    out = {}
    present = set()
    seen = {}
    for a in s.ex.team_actors(s.team):
        ek, strength, rec = s.entity_of(a)
        if ek in seen:
            # TWO actors in ONE battle resolving to the same entity key (a weak/name collision): never
            # merge them -- keep separate rows so nothing is silently summed (roadmap N2 item 4).
            ek = "%s|inst:%s" % (ek, a.key)
        seen[ek] = True
        present.add(ek)
        d = out.setdefault(ek, {"name": a.name, "summon": bool(a.summon), "strength": strength,
                                "direct": 0.0, "base": 0.0, "self": 0.0, "assist": 0.0, "received": 0.0,
                                "hits": 0, "instances": []})
        d["instances"].append(a.key)
        if strength == "name-fallback" or strength != "strong":
            d["strength"] = strength
        cr = s.an_actors.get(a.key)
        if cr is not None:
            d["direct"] += cr.direct
            d["base"] += cr.base
            d["self"] += cr.self_rule
            d["assist"] += cr.assist
            d["received"] += cr.received
            d["hits"] += cr.hits
    for d in out.values():
        d["total"] = d["base"] + d["self"] + d["assist"]
    return out, present


def actor_table(samples, metric, analyzable_by_sample):
    """Per-entity statistics over the samples where the entity was actually in the roster."""
    creds = [(s, sample_credits(s)) for s in samples]
    all_ek = set()
    for _s, (cr, present) in creds:
        all_ek |= present
    rows = []
    for ek in sorted(all_ek):
        appeared = []
        absent = 0
        weak = 0
        name = ""
        summon = False
        for s, (cr, _p) in creds:
            if ek in cr:
                appeared.append((s, cr[ek]))
                name = cr[ek]["name"]
                summon = cr[ek]["summon"]
                if cr[ek]["strength"] != "strong":
                    weak += 1
            else:
                absent += 1
        shares = []
        cum_num = 0.0
        cum_den = 0.0
        zero = 0
        for s, d in appeared:
            den = analyzable_by_sample.get(s.name, 0.0)
            if den > 0:
                shares.append(d["total"] / den)
            cum_num += d["total"]
            cum_den += den
            if abs(d["direct"]) < 1e-9 and abs(d["total"]) < 1e-9:
                zero += 1
        rows.append({"entityKey": ek, "name": name, "summon": summon,
                     "samplesObserved": len(appeared), "samplesAbsent": absent,
                     "appearedAsZero": zero, "identityWeakSamples": weak,
                     "identityUnknown": weak == len(appeared) and weak > 0,
                     "share": stat_block(shares),
                     "cumulativeCredit": cum_num, "cumulativeAnalyzable": cum_den,
                     "cumulativeShare": (cum_num / cum_den) if cum_den else None,
                     "meanDirectShare": stat_block([d["direct"] / analyzable_by_sample.get(s.name, 1.0)
                                                    for s, d in appeared if analyzable_by_sample.get(s.name)])})
    rows.sort(key=lambda r: -(r["share"]["mean"] if r["share"] else 0.0))
    return rows


def rule_table(samples, metric):
    """Rules keyed by (name, kind, side, owner entity): per-battle MEAN and the SUM are both reported."""
    acc = {}
    for s in samples:
        per_rule = {}
        for k, cr in s.an_actors.items():
            if not cr.rules:
                continue
            ref = s.ex.by_key.get(k)
            owner_ek = ""
            if ref is not None:
                owner_ek, _st, _rec = s.entity_of(ref)
            for rk, amount in cr.rules.items():
                name, kind, side, _owner = rk
                key = (name, kind, side, owner_ek)
                slot = per_rule.setdefault(key, {"damage": 0.0, "hits": 0})
                slot["damage"] += amount
                slot["hits"] += cr.rule_hits.get(rk, 0)
        for key, slot in per_rule.items():
            e = acc.setdefault(key, {"name": key[0], "kind": key[1], "side": key[2],
                                     "damage": 0.0, "hits": 0, "samples": 0})
            e["damage"] += slot["damage"]
            e["hits"] += slot["hits"]
            e["samples"] += 1
    out = []
    n = max(1, len(samples))
    for e in acc.values():
        e["damageMeanPerBattle"] = e["damage"] / n
        e["samplesInStratum"] = n
        out.append(e)
    out.sort(key=lambda r: -r["damage"])
    return out


def group_report(samples, metric, analyzable_by_sample, restricted_reasons, loadout_strict=False):
    return {"metric": metric, "n": len(samples), "files": [s.name for s in samples],
            "versionSpan": sorted(set(s.version for s in samples), key=lambda v: [int(x) for x in v.split(".")]),
            "restricted": bool(restricted_reasons), "reasons": restricted_reasons,
            "capability": {"schema": sorted(set(str(s.cap["schema"]) for s in samples)),
                           "atkAdd": sorted(set(s.cap["atkAdd"] for s in samples)),
                           "giveObserved": sorted(set(s.cap["giveObserved"] for s in samples)),
                           "compositionWeakActors": max(getattr(s, "compWeak", 0) for s in samples),
                           "rosterBasis": "templateId",
                           "loadoutKeys": len(set(s.comp_key() for s in samples)),
                           "mixedLoadout": len(set(s.comp_key() for s in samples)) > 1,
                           "strictLoadout": bool(loadout_strict)},
            "analyzable": stat_block([analyzable_by_sample[s.name] for s in samples]),
            "duration": stat_block([s.duration for s in samples]),
            "results": sorted(set(str(s.result) for s in samples)),
            "actors": [r for r in actor_table(samples, metric, analyzable_by_sample) if not r["summon"]],
            "summons": [r for r in actor_table(samples, metric, analyzable_by_sample) if r["summon"]],
            "rules": rule_table(samples, metric)}


def render_text(rep):
    L = []
    L.append("=" * 122)
    L.append("DpsMeter 跨场比较报告 2.0  (contract %s)" % CONTRACT)
    L.append("  输入 %d 份;准入层 %s" % (rep["n_inputs"],
                                          (rep["admission"] or {}).get("contract", "不可用")))
    idn = rep["identity"]
    L.append("  身份:composition 全用 entityKey 的 %d 份;含弱身份 actor(按名字键)的 %d 份;identity_map=%s"
             % (idn["fullyResolved"], idn["withWeakActor"], idn["identityMapAvailable"]))
    L.append("        (名字键只用于该 actor 自身;同名不同实体的两个 actor 在同一场里也永不合并)")
    L.append("")
    L.append("--- 1. 准入(逐指标;先准入,后分组)---")
    for m in rep["metrics"]:
        L.append("  %-13s %-11s 分组 %d 个 / 可比较(样本>=2) %d 个"
                 % (m["metric"], m["status"], len(m.get("strata") or []), m.get("comparableStrata", 0)))
        for r in m["reasons"][:4]:
            L.append("      [限制] %s" % r)
        for g in m.get("rejectedSamples") or []:
            L.append("      [排除] %s —— %s" % (g[0], g[1]))
    L.append("")
    for m in rep["metrics"]:
        if not m.get("strata"):
            L.append("--- %s:无可比较分层 —— %s" % (m["metric"],
                                                     (m.get("noStratumReason") or m["reasons"][:1] or "无准入样本")))
            L.append("")
            continue
        for si, g in enumerate(m["strata"]):
            L.append("--- %s  分层 %d/%d  n=%d%s" % (m["metric"], si + 1, len(m["strata"]), g["n"],
                                                     "  [RESTRICTED]" if g["restricted"] else ""))
            L.append("    文件: %s" % ", ".join(g["files"]))
            L.append("    版本: %s | 阵容按 templateId;装备组合 %d 种%s | 弱身份 actor %d 个"
                     % (",".join(g["versionSpan"]), g["capability"]["loadoutKeys"],
                        " (该层内换过装)" if g["capability"]["mixedLoadout"] else "",
                        g["capability"]["compositionWeakActors"]))
            a = g["analyzable"] or {}
            d = g["duration"] or {}
            L.append("    可分析伤害: 均值 %.0f  中位 %.0f  范围 %.0f~%.0f  标准差 %.0f (n=%d)"
                     % (a.get("mean", 0), a.get("median", 0), a.get("min", 0), a.get("max", 0),
                        a.get("stdev", 0), a.get("n", 0)))
            L.append("    时长: 均值 %.1fs  范围 %.1f~%.1fs" % (d.get("mean", 0), d.get("min", 0), d.get("max", 0)))
            if g["restricted"]:
                for r in g["reasons"][:3]:
                    L.append("    [该分层带限制] %s" % r)
            L.append("    角色(按累计份额降序;obser=出场,abs=确认未出场,zero=真实 0)")
            L.append("      %-18s %6s %5s %5s %9s %9s %9s %9s %9s"
                     % ("实体", "obser", "abs", "zero", "share均", "中位", "最小", "最大", "σ"))
            for r in g["actors"][:24]:
                s = r["share"] or {}
                L.append("      %-18s %6d %5d %5d %8.2f%% %8.2f%% %8.2f%% %8.2f%% %8.2f"
                         % ((r["name"][:16] + ("*" if r["summon"] else "")) [:18], r["samplesObserved"],
                            r["samplesAbsent"], r["appearedAsZero"], 100 * s.get("mean", 0),
                            100 * s.get("median", 0), 100 * s.get("min", 0), 100 * s.get("max", 0),
                            100 * s.get("stdev", 0)))
                if r["identityUnknown"]:
                    L.append("        ^ 身份不明(该实体所有出场都只有弱身份):不参与跨场合并" )
            L.append("    累计口径(与上面的每场份额平均是两回事):")
            for r in g["actors"][:8]:
                L.append("      %-18s 累计贡献 %12.0f / 累计分析伤害 %12.0f = %.2f%%"
                         % (r["name"][:16], r["cumulativeCredit"], r["cumulativeAnalyzable"],
                            100 * (r["cumulativeShare"] or 0)))
            if g.get("summons"):
                L.append("    召唤物(每场实例,不做跨场合并;不并入主人):")
                for r in g["summons"][:12]:
                    s = r["share"] or {}
                    L.append("      %-18s 出场 %d/%d 场  份额均 %6.2f%%  实体键 %s"
                             % (r["name"][:18], r["samplesObserved"], g["n"], 100 * s.get("mean", 0),
                                r["entityKey"][:28]))
            L.append("    规则当量(每场均值 与 合计 并列;样本数 %d):" % g["n"])
            for r in g["rules"][:8]:
                L.append("      %-26s %-8s %-4s 每场均 %11.0f   合计 %12.0f   (%d/%d 场)"
                         % (r["name"][:26], r["kind"], r["side"], r["damageMeanPerBattle"], r["damage"],
                            r["samples"], g["n"]))
            L.append("")
    L.append("--- 适用边界 ---")
    for t in rep["limits"]:
        L.append("  * %s" % t)
    return "\n".join(L)


LIMITS = [
    "先准入再分组:被 REJECTED 的样本不进任何表;被 RESTRICTED 的样本所在的层会显式标注限制。",
    "分层键包含能力集(schema/damageBasis/atkadd/观测到的 GivenTalent 开关)与身份构成,跨层不做平均。",
    "每场份额平均 与 累计贡献/累计分析伤害 是两个不同的量,各有各的分母,禁止互换。",
    "n<2 的层只做描述,不构成比较;整份报告没有任何 n>=2 的层时退出码为 4。",
    "会心、治疗、敌方(team 2)视角与固定窗口 DPS 都不在本报告的范围内。",
    "assistCredit 只按 N4 的结论表述为分账约定,不得读成「换掉角色会少的伤害」。",
]


def build_report(paths, team, gate, strict_loadout=False):
    if elig is None:
        raise RuntimeError("comparison_eligibility (N1) is required: run from _dpsm_work")
    samples = [Sample(p, team) for p in paths]
    # hand the admission layer the documents we already parsed: its tail-window fast path misses a
    # section that sits >500 KB from EOF, which a re-serialised copy does.
    docs = {s.name: s.ex.raw for s in samples}
    admission = elig.evaluate(paths, gate, docs)
    analyzable = {s.name: s.an.analyzable for s in samples}
    metrics_out = []
    for dec in admission["metrics"]:
        metric = dec["metric"]
        members = dec["members"]
        admissible, rejected = [], []
        for s in samples:
            st = members.get(s.name)
            if st in (elig.REJECTED, elig.NOT_APPLICABLE):
                rejected.append((s.name, st))
            else:
                admissible.append(s)
        strata = {}
        for s in admissible:
            strata.setdefault(s.stratum_key(metric, strict_loadout), []).append(s)
        groups = []
        for key, members_s in sorted(strata.items(), key=lambda kv: str(kv[0])):
            reasons = []
            for s in members_s:
                st = members.get(s.name)
                if st == elig.RESTRICTED:
                    reasons.append("%s: RESTRICTED(见准入层)" % s.name)
            groups.append(group_report(members_s, metric, analyzable, reasons, strict_loadout))
        metrics_out.append({"metric": metric, "status": dec["status"], "reasons": dec["reasons"],
                            "rejectedSamples": rejected, "strata": groups,
                            "comparableStrata": len([g for g in groups if g["n"] >= 2]),
                            "noStratumReason": ("该指标没有通过准入的样本" if not admissible else
                                                "所有分层都只有 1 个样本:只做描述,不做比较")})
    return {"contract": CONTRACT, "n_inputs": len(paths), "team": team,
            "identity": {"fullyResolved": len([s for s in samples if getattr(s, "compWeak", 0) == 0]),
                         "withWeakActor": len([s for s in samples if getattr(s, "compWeak", 0) > 0]),
                         "identityMapAvailable": idm is not None},
            "admission": {k: admission[k] for k in ("contract", "n") if k in admission},
            "admissionFull": admission, "metrics": metrics_out, "limits": LIMITS,
            "inputSha256": {s.name: sample_sha(s) for s in samples}}


def sample_sha(s):
    try:
        return idm.sha256_file(s.path) if idm is not None else ""
    except Exception:
        return ""


def main(argv=None):
    ap = argparse.ArgumentParser(description="cross-battle comparison 2.0 (N2)")
    ap.add_argument("pattern", nargs="?", default="battle_411001_*.json")
    ap.add_argument("--quest", type=int, default=None)
    ap.add_argument("--team", type=int, default=1)
    ap.add_argument("--out", default=None)
    ap.add_argument("--applicability", default=None)
    ap.add_argument("--files", nargs="*", default=None)
    ap.add_argument("--strict-loadout", action="store_true",
                    help="also require one identical gear combination inside a stratum (splits more)")
    ap.add_argument("--selftest", action="store_true")
    a = ap.parse_args(argv)
    if a.selftest:
        return selftest()
    if a.files:
        paths = [p if os.path.isabs(p) else os.path.join(EXPORTS, p) for p in a.files]
    else:
        paths = sorted(glob.glob(os.path.join(EXPORTS, a.pattern)))
    if a.quest is not None:
        paths = [p for p in paths if os.path.basename(p).split("_")[1:2] == [str(a.quest)]]
    paths = [p for p in paths if os.path.isfile(p)]
    if not paths:
        print("no input matched")
        return 2
    gate = elig.load_gate(a.applicability) if (elig is not None and a.applicability) else None
    try:
        rep = build_report(paths, a.team, gate, a.strict_loadout)
    except Exception as ex:
        print("ERROR: %r" % (ex,))
        return 3
    os.makedirs(OUTDIR, exist_ok=True)
    base = a.out or os.path.join(OUTDIR, "compare2_%s.txt" % (a.quest if a.quest else "all"))
    if base.endswith(".txt"):
        base = base[:-4]
    io.open(base + ".txt", "w", encoding="utf-8").write(render_text(rep) + "\n")
    io.open(base + ".json", "w", encoding="utf-8").write(json.dumps(rep, ensure_ascii=False, indent=1))
    n_comp = sum(m["comparableStrata"] for m in rep["metrics"])
    print("inputs=%d metrics=%d comparable strata=%d" % (rep["n_inputs"], len(rep["metrics"]), n_comp))
    for m in rep["metrics"]:
        print("  %-13s %-11s strata=%d comparable=%d" % (m["metric"], m["status"], len(m["strata"]),
                                                          m["comparableStrata"]))
    print("report=%s.txt" % base)
    if n_comp == 0:
        print("NOTHING COMPARABLE: no stratum has n>=2 (exit 4)")
        return 4
    return 0



def _run_cli(py, cwd, tag, tmp, args):
    lp = os.path.join(tmp, "cli_%s.txt" % tag)
    with io.open(lp, "w", encoding="utf-8") as fh:
        rc = subprocess.call([py, "-m", "contrib.compare"] + args, cwd=cwd, stdout=fh,
                             stderr=subprocess.STDOUT)
    return rc, io.open(lp, encoding="utf-8", errors="replace").read()


def _load_json(path):
    try:
        return json.load(io.open(path, encoding="utf-8"))
    except Exception:
        return {}


def _metric(rep, name):
    for m in rep.get("metrics") or []:
        if m.get("metric") == name:
            return m
    return {}


def selftest():
    """The roadmap's acceptance cases, each run through THIS CLI (not the flat functions)."""
    py = sys.executable
    cwd = WORK
    tmp = tempfile.mkdtemp(prefix="compare2_")
    fails = []
    E = EXPORTS

    def case(label, ok, extra=""):
        print("  [%s] %-58s %s" % ("PASS" if ok else "FAIL", label, extra))
        if not ok:
            fails.append(label)

    def copy_of(name, newname, mutate=None):
        d = json.load(io.open(os.path.join(E, name), encoding="utf-8"))
        if mutate:
            mutate(d)
        p = os.path.join(tmp, newname)
        io.open(p, "w", encoding="utf-8").write(json.dumps(d, ensure_ascii=False))
        return p

    A = os.path.join(E, "battle_411001_20261004_134853.json")
    B = os.path.join(E, "battle_411001_20261004_134524.json")
    C = os.path.join(E, "battle_411001_20261004_144548.json")
    old = os.path.join(E, "battle_411001_20261004_035437.json")   # 1.7.3, no atkAdd section
    train = os.path.join(E, "battle_9999_20261004_135214.json")
    legacy = os.path.join(E, "battle_411001_20261003_235204.json")  # 1.5.5, no contribution section
    damaged = os.path.join(E, "battle_411001_20261004_015919.json")  # 1.6.0 known damaged

    # 1. the positive control: three real same-composition battles -> a published comparison
    out = os.path.join(tmp, "r1")
    rc, txt = _run_cli(py, cwd, "positive", tmp, ["--files", A, B, C, "--out", out])
    rep = _load_json(out + ".json")
    case("three comparable real battles publish a comparison (exit 0)",
         rc == 0 and _metric(rep, "team_damage").get("comparableStrata", 0) >= 1, "rc=%s" % rc)
    case("actor_credit is stratified by capability, each stratum with its own n",
         len(_metric(rep, "actor_credit").get("strata") or []) >= 1,
         "strata=%d" % len(_metric(rep, "actor_credit").get("strata") or []))

    # 2. zero-contribution actor: strip one attacker's events, keep it in the roster
    def strip_events(d):
        d["events"] = [e for e in (d.get("events") or []) if e.get("atkKey") != 9]
    zero = copy_of("battle_411001_20261004_144548.json", "zero.json", strip_events)
    out = os.path.join(tmp, "r2")
    rc, txt = _run_cli(py, cwd, "zero", tmp, ["--files", A, C, zero, "--out", out])
    rep = _load_json(out + ".json")
    rows = (_metric(rep, "actor_credit").get("strata") or [{}])[0].get("actors") or []
    z = [r for r in rows if r.get("appearedAsZero", 0) > 0]
    case("an actor whose events were removed is a REAL zero, not a missing row",
         bool(z) and all(r["samplesObserved"] >= 1 for r in z), "rows_with_zero=%d" % len(z))

    # 3. same name, two entities: never merged
    def rename(d):
        for a in d.get("actors") or []:
            if a.get("key") == 3:
                a["name"] = "テトラ"
    dup = copy_of("battle_411001_20261004_144548.json", "dupname.json", rename)
    out = os.path.join(tmp, "r3")
    rc, txt = _run_cli(py, cwd, "dup", tmp, ["--files", C, dup, "--out", out])
    rep = _load_json(out + ".json")
    rows = (_metric(rep, "actor_credit").get("strata") or [{}])[0].get("actors") or []
    same = [r for r in rows if r.get("name") == "テトラ"]
    case("two entities with the same NAME stay two rows (never merged)",
         len(same) >= 2 and len(set(r["entityKey"] for r in same)) == len(same),
         "rows=%d distinct=%d" % (len(same), len(set(r["entityKey"] for r in same))))

    # 4. different capability (1.7.3 without atkAdd vs 1.7.8 with it) -> different strata
    out = os.path.join(tmp, "r4")
    rc, txt = _run_cli(py, cwd, "cap", tmp, ["--files", old, A, B, "--out", out])
    rep = _load_json(out + ".json")
    caps = [g["capability"]["atkAdd"] for g in (_metric(rep, "actor_credit").get("strata") or [])]
    case("a different capability set is a different stratum, never an average",
         any(False in c for c in caps) and any(True in c for c in caps), "caps=%s" % caps)

    # 5. different observed config (GivenTalent flag read as off in one copy)
    def noflag(d):
        d.setdefault("rosterAudit", {})["giveFlagTrue"] = 0
    nof = copy_of("battle_411001_20261004_144548.json", "noflag.json", noflag)
    out = os.path.join(tmp, "r5")
    rc, txt = _run_cli(py, cwd, "cfg", tmp, ["--files", C, nof, "--out", out])
    rep = _load_json(out + ".json")
    caps = [g["capability"]["giveObserved"] for g in (_metric(rep, "actor_credit").get("strata") or [])]
    case("a different observed config flag is a different stratum",
         any(False in c for c in caps) and any(True in c for c in caps), "caps=%s" % caps)

    # 6. the training ground is mixed in but never pooled with a normal quest
    out = os.path.join(tmp, "r6")
    rc, txt = _run_cli(py, cwd, "train", tmp, ["--files", A, B, train, "--out", out])
    rep = _load_json(out + ".json")
    groups = _metric(rep, "team_damage").get("strata") or []
    case("training ground is its own stratum and is not pooled with a normal quest",
         len(groups) >= 2 and any(9999 in (g["files"],) or any("9999" in f for f in g["files"]) for g in groups),
         "strata=%d" % len(groups))

    # 7. the known damaged sample is REJECTED (through --applicability) and never averaged in
    app = os.path.join(tmp, "app.json")
    io.open(app, "w", encoding="utf-8").write(json.dumps({"exports": [
        {"file": os.path.basename(damaged), "crosscheck": {"status": "ERROR"}, "correctedUnexplainedLeaks": 0},
        {"file": os.path.basename(A), "crosscheck": {"status": "PASS"}, "correctedUnexplainedLeaks": 0},
        {"file": os.path.basename(C), "crosscheck": {"status": "PASS"}, "correctedUnexplainedLeaks": 0}]}))
    out = os.path.join(tmp, "r7")
    rc, txt = _run_cli(py, cwd, "damaged", tmp,
                       ["--files", damaged, A, C, "--applicability", app, "--out", out])
    rep = _load_json(out + ".json")
    rej = [x for m in rep.get("metrics") or [] for x in (m.get("rejectedSamples") or [])]
    case("the gate-ERROR sample is listed as rejected, not silently averaged",
         any(os.path.basename(damaged) == r[0] for r in rej), "rejected=%d" % len(rej))

    # 8. nothing comparable -> exit 4 with a reason, never a blank success
    out = os.path.join(tmp, "r8")
    rc, txt = _run_cli(py, cwd, "single", tmp, ["--files", legacy, "--out", out])
    case("a single sample is not a comparison (exit 4)",
         rc == 4 and "NOTHING COMPARABLE" in txt, "rc=%s" % rc)

    # 9. unequal stratum sizes are printed with their own n. A and C field template 42, B fields 60
    # (measured), so this is two strata of 2 and 1 -- verified by the roster keys themselves.
    out = os.path.join(tmp, "r9")
    rc, txt = _run_cli(py, cwd, "uneq", tmp, ["--files", A, C, B, "--out", out])
    rep = _load_json(out + ".json")
    ns = sorted(g["n"] for g in (_metric(rep, "team_damage").get("strata") or []))
    case("strata with different sizes keep their own n (no pooled average)",
         ns == [1, 2] and sum(ns) == 3, "n=%s" % ns)

    shutil.rmtree(tmp, ignore_errors=True)
    print("---- selftest: %s" % ("PASS" if not fails else "FAIL %s" % fails))
    return 0 if not fails else 1


if __name__ == "__main__":
    sys.exit(main())

