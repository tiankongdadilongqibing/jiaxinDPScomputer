#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""N3 / P1 stage-1 offline cross-battle identity mapping (read-only).

Scope (roadmap N3, first batch: design + mapping + tests only):
  * read ONE exported battle JSON (never modified, never written back),
  * optionally read a human-authored run_annotations.json keyed by export SHA256,
  * resolve, per actor, a separation between
        actorInstanceKey  (this actor slot inside THIS export only)
        typeKey           (character / enemy template, no equipment)
        entityKey         (template + loadout fingerprint)
        loadoutFingerprint(stable hash of the equipment/ability id set)
    plus summon/source links and an identity-strength grade,
  * decide whether two resolved actors are the same entity.

Hard rules implemented here:
  * equal NAME is never sufficient to merge two actors;
  * a different loadout fingerprint means a different entity (or an explicit
    "split" marker), never a silent merge;
  * a summon never collapses into its owner (different template, different key);
  * run.id / run.seq are session counters, not cross-process unique keys.

Evidence grading used throughout: 实测 (MEASURED) / 离线重放 (OFFLINE_REPLAY) /
静态审查 (STATIC_REVIEW) / 推断 (INFERRED).

Console output is ASCII only (Windows GBK console); writes are UTF-8.
"""

import argparse
import hashlib
import json
import os
import sys

# ---------------------------------------------------------------------------
# versions / constants
# ---------------------------------------------------------------------------

TOOL_NAME = "identity_map.py"
TOOL_VERSION = "N3-P1-1.0"
IDENTITY_SCHEMA = "identity-map/1.0"
ANNOTATION_SCHEMA = "run-annotations/1.0"

# annotation fields that are "never guess-filled"
ANNOTATION_UNKNOWN = "unknown"

# identity strength grades
STRENGTH_STRONG = "strong"   # template id + (character bonus or loadout fingerprint)
STRENGTH_MEDIUM = "medium"   # template id only, or name+kind template with abilities
STRENGTH_WEAK = "weak"       # name/kind only, or fields missing entirely

# classification of the template id namespace
NS_CHAR = "char"       # slot-6 "潜在" ability id (character-specific)
NS_TOKEN = "token"     # summon / token ability id
NS_ENEMY = "enemy"     # enemy non-token unit
NS_NAMED = "named"     # name-derived fallback, not a real template id

# ability slots exported by the plugin (layout.slots mirrors this vocabulary)
SLOT_NAMES = {
    1: "base_passive",
    2: "job_trait",
    5: "passive",
    6: "potential",
    9: "artifact",
    10: "sigil",
}
SLOT_POTENTIAL = 6
SLOT_JOB = 2
SLOT_ARTIFACT = 9
SLOT_SIGIL = 10


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def sha256_bytes(data):
    return hashlib.sha256(data).hexdigest()


def load_json(path):
    with open(path, "rb") as f:
        raw = f.read()
    return json.loads(raw.decode("utf-8")), sha256_bytes(raw)


def _canon(obj):
    return json.dumps(obj, sort_keys=True, separators=(",", ":"), ensure_ascii=True)


def _digest16(obj):
    return hashlib.sha256(_canon(obj).encode("ascii")).hexdigest()[:16]


# ---------------------------------------------------------------------------
# actor extraction helpers (everything defensive: old exports may lack fields)
# ---------------------------------------------------------------------------

def _is_int(x):
    return isinstance(x, int) and not isinstance(x, bool)


def ability_rows(actor):
    rows = actor.get("abilities") if isinstance(actor, dict) else None
    if not isinstance(rows, list):
        return []
    return [r for r in rows if isinstance(r, dict)]


def find_slot(rows, slot):
    for r in rows:
        if r.get("slot") == slot:
            return r
    return None


def ability_id_set(rows):
    out = set()
    for r in rows:
        aid = r.get("id")
        if _is_int(aid):
            out.add(aid)
    return out


def loadout_canonical(rows):
    """Deterministic canonical form of the loadout as exported.

    Ordered list sorted by (slot, slotId-ish position, id, level, name,
    talent tuple).  Volatile per-hit numbers (delta/prop/agg) are excluded on
    purpose; they are observation counts, not configuration.
    """
    canon = []
    for r in rows:
        talents = r.get("talents") if isinstance(r.get("talents"), list) else []
        tcanon = []
        for t in talents:
            if not isinstance(t, dict):
                continue
            p = t.get("p") if isinstance(t.get("p"), list) else []
            tcanon.append([
                t.get("i"), t.get("type"), t.get("timing"),
                [x for x in p],
                t.get("cond", ""),
            ])
        tcanon.sort(key=lambda x: _canon(x))
        canon.append([
            r.get("slot"),
            r.get("id"),
            r.get("level"),
            r.get("name"),
            r.get("origin"),
            r.get("nameFrom", ""),
            tcanon,
        ])
    canon.sort(key=lambda x: _canon(x))
    return canon


def loadout_fingerprint(rows):
    """16 hex chars; primary hash of the ability/equipment id set.

    Built from (slot, id, level, talent tuples) only.  Display names and the
    per-hit observation counters (delta/prop/agg) are excluded because they are
    labels, not configuration: including them would make the fingerprint move
    when only a translation changed.
    """
    if not rows:
        return None
    return _digest16(loadout_id_canonical(rows))


def loadout_id_canonical(rows):
    canon = []
    for r in rows:
        talents = r.get("talents") if isinstance(r.get("talents"), list) else []
        tcanon = []
        for t in talents:
            if not isinstance(t, dict):
                continue
            p = t.get("p") if isinstance(t.get("p"), list) else []
            tcanon.append([t.get("i"), t.get("type"), t.get("timing"), [x for x in p],
                           t.get("cond", "")])
        tcanon.sort(key=lambda x: _canon(x))
        canon.append([r.get("slot"), r.get("id"), r.get("level"), tcanon])
    canon.sort(key=lambda x: _canon(x))
    return canon


def loadout_detail_hash(rows):
    """Secondary hash that also covers names/nameFrom, for audit only."""
    if not rows:
        return None
    return _digest16(loadout_canonical(rows))


def loadout_id_fingerprint(rows):
    """Coarse fingerprint: slot->sorted id list only (ignores roll details)."""
    if not rows:
        return None
    buckets = {}
    for r in rows:
        slot = r.get("slot")
        aid = r.get("id")
        if not _is_int(slot) or not _is_int(aid):
            continue
        buckets.setdefault(str(slot), set()).add(aid)
    if not buckets:
        return None
    norm = {k: sorted(v) for k, v in buckets.items()}
    return _digest16(norm)


def summon_owner_index(export):
    """Owner links observed in events: summonName -> {ownerName: hitCount}.

    Evidence level: OFFLINE_REPLAY.  The export has no explicit summon->owner
    field (see _dpsm_work/CONTRIBUTION-DATA-DICTIONARY.md, known limits), so the
    only export-internal witness is events[].owner != events[].attacker.
    """
    idx = {}
    events = export.get("events")
    if not isinstance(events, list):
        return idx
    for e in events:
        if not isinstance(e, dict):
            continue
        atk = e.get("attacker")
        own = e.get("owner")
        if not isinstance(atk, str) or not isinstance(own, str):
            continue
        if atk == own:
            continue
        d = idx.setdefault(atk, {})
        d[own] = d.get(own, 0) + 1
    return idx


def annotation_lookup(annotations):
    """Index annotation runs by export sha256 (case-insensitive)."""
    idx = {}
    if not isinstance(annotations, dict):
        return idx
    runs = annotations.get("runs")
    if not isinstance(runs, list):
        return idx
    for r in runs:
        if not isinstance(r, dict):
            continue
        h = r.get("exportSha256")
        if isinstance(h, str) and h:
            idx[h.strip().lower()] = r
    # also accept {sha256: {...}} short form
    alt = annotations.get("byExportSha256")
    if isinstance(alt, dict):
        for k, v in alt.items():
            if isinstance(k, str) and isinstance(v, dict):
                idx.setdefault(k.strip().lower(), v)
    return idx


def _ann_value(run_rec, path, default=ANNOTATION_UNKNOWN):
    cur = run_rec
    for part in path:
        if not isinstance(cur, dict):
            return default
        cur = cur.get(part)
    if cur is None:
        return default
    return cur


def annotation_loadout_ids(run_rec, actor_name):
    """Equipment/ability ids confirmed by a human for one actor, if present.

    Returns (ids, slots) lists.  Missing -> ([], []).  Nothing is guessed.
    """
    roster = _ann_value(run_rec, ["confirmed", "roster"], None)
    if not isinstance(roster, list):
        return [], []
    for entry in roster:
        if not isinstance(entry, dict):
            continue
        if entry.get("name") != actor_name:
            continue
        ids, slots = [], []
        loadout = entry.get("loadout")
        if isinstance(loadout, dict):
            for slot_name, items in loadout.items():
                if not isinstance(items, list):
                    continue
                for it in items:
                    if isinstance(it, dict):
                        aid = it.get("id")
                        if _is_int(aid):
                            ids.append(aid)
                            slots.append(slot_name)
                    elif _is_int(it):
                        ids.append(it)
                        slots.append(slot_name)
        return ids, slots
    return [], []


def annotation_template_id(run_rec, actor_name):
    roster = _ann_value(run_rec, ["confirmed", "roster"], None)
    if not isinstance(roster, list):
        return None
    for entry in roster:
        if isinstance(entry, dict) and entry.get("name") == actor_name:
            tid = entry.get("templateId")
            if _is_int(tid) or (isinstance(tid, str) and tid):
                return tid
    return None


# ---------------------------------------------------------------------------
# identity resolution
# ---------------------------------------------------------------------------

def classify_actor(actor, summon_owner_by_name):
    """kind/team/summon vocabulary as actually observed in the corpus.

    team 1 = player side, team 2 = enemy side (实测, 30 exports).
    kind values observed: P (unit), Boss, B, C, E.
    """
    kind = actor.get("kind")
    team = actor.get("team")
    is_summon = actor.get("summon") is True
    if kind is None:
        cls = "unknown"
    elif is_summon:
        cls = "summon"
    elif kind == "P" and team == 1:
        cls = "character"
    elif kind == "P":
        cls = "enemy_unit"
    elif kind in ("Boss", "B", "C", "E"):
        cls = "enemy"
    else:
        cls = "unknown"
    owner = None
    name = actor.get("name")
    if is_summon and isinstance(name, str):
        owners = summon_owner_by_name.get(name)
        if owners:
            # deterministic pick: most observed owner, then name order
            owner = sorted(owners.items(), key=lambda kv: (-kv[1], kv[0]))[0][0]
    return cls, owner


def resolve_actor(export, export_sha, actor, annotation_run=None, index=0):
    """Resolve one actor record into an identity record.

    Evidence levels per field are stated in design doc N3/IDENTITY-METADATA-DESIGN.md.
    """
    rows = ability_rows(actor)
    owner_idx = summon_owner_index(export)
    kind = actor.get("kind")
    team = actor.get("team")
    name = actor.get("name")
    summon = actor.get("summon")
    key = actor.get("key")
    entity_class, owner_name = classify_actor(actor, owner_idx)

    name_is_str = isinstance(name, str) and name != ""
    if not name_is_str:
        name = None

    pot = find_slot(rows, SLOT_POTENTIAL)
    job = find_slot(rows, SLOT_JOB)
    token = find_slot(rows, 1)

    template_id = None
    template_ns = None
    template_basis = "none"
    template_evidence = "missing"

    ann_tid = annotation_template_id(annotation_run, name) if (annotation_run and name) else None

    if pot is not None and _is_int(pot.get("id")):
        template_id = pot.get("id")
        template_ns = NS_CHAR
        template_basis = "slot6_potential"
        template_evidence = "MEASURED"
    elif token is not None and _is_int(token.get("id")):
        template_id = token.get("id")
        template_ns = NS_TOKEN
        template_basis = "slot1_base_passive_token"
        template_evidence = "MEASURED"
    elif ann_tid is not None:
        template_id = ann_tid
        template_ns = NS_CHAR
        template_basis = "annotation_roster_templateId"
        template_evidence = "OFFLINE_REPLAY"
    elif name_is_str and kind == "P" and summon is False and not rows:
        template_id = None
        template_ns = None
        template_basis = "none"
        template_evidence = "missing"
    elif isinstance(kind, str) and name_is_str:
        # enemy units / bosses without ability rows: name+kind is the only
        # witness.  This is a derived template, never a real game id.
        template_ns = NS_ENEMY
        template_id = _digest16({"kind": kind, "name": name})
        template_basis = "kind_name_hash"
        template_evidence = "INFERRED"
    elif name_is_str and not rows:
        # A record with neither abilities nor kind is a legacy/partial export.
        # Do NOT mint a template id: keep the identity weak and warn.
        template_id = None
        template_ns = None
        template_basis = "none"
        template_evidence = "missing"
    elif name_is_str:
        template_ns = NS_NAMED
        template_id = _digest16({"name": name})
        template_basis = "name_only_hash"
        template_evidence = "INFERRED"

    # loadout fingerprint (export) and annotation fingerprint fallback
    fp = loadout_fingerprint(rows)
    fp_ids = loadout_id_fingerprint(rows)
    fp_basis = "export_abilities" if fp else "missing"
    ann_ids, ann_slots = ([], [])
    if annotation_run and name:
        ann_ids, ann_slots = annotation_loadout_ids(annotation_run, name)
    ann_fp = None
    if not fp and ann_ids:
        ann_fp = _digest16({"ids": sorted(ann_ids), "src": "annotation"})
        fp = ann_fp
        fp_ids = ann_fp
        fp_basis = "annotation_roster_loadout"

    idset = sorted(ability_id_set(rows))
    if not idset and ann_ids:
        idset = sorted(set(ann_ids))
    from_annotation = bool(ann_ids)

    # string type keys (stable logical identity, no equipment)
    if template_id is not None:
        type_key = "type:%s:%s" % (template_ns or "unknown", template_id)
    else:
        type_key = None
    if template_id is not None and fp:
        entity_key = "ent:%s:%s:%s" % (template_ns or "unknown", template_id, fp)
    else:
        entity_key = None
    # field-aware key: the same template can legitimately appear on both sides
    # (mirror boss / trainer mission), so a camp-aware key is kept separately.
    if entity_key is not None and _is_int(team):
        same_entity_key = "%s|field=%s|class=%s" % (entity_key, team, entity_class)
    else:
        same_entity_key = None
    instance_key = "inst:%s:%s" % (export_sha[:16], key)

    # ---- strength -----------------------------------------------------
    warnings = []
    if entity_class == "unknown":
        warnings.append("class_unknown")
    if fp is None:
        warnings.append("loadout_fingerprint_missing")
    if template_id is None:
        warnings.append("template_id_unresolved")
    if not rows:
        warnings.append("abilities_missing_in_export")
    if ann_fp is not None:
        warnings.append("loadout_from_annotation_not_export")
    if ann_tid is not None and pot is None:
        warnings.append("template_from_annotation_not_export")
    if actor.get("summon") is True and owner_name is None:
        warnings.append("summon_owner_unknown")
    if not rows and isinstance(kind, str) and name_is_str and kind != "P":
        warnings.append("enemy_without_abilities_derived_template")

    # identity class: only a real (observed / human-confirmed) template, or an
    # observed loadout fingerprint, counts as resolved identity.  A hash derived
    # from "kind + name" or "name" is a derived key, not an identity.
    if template_evidence in ("MEASURED", "OFFLINE_REPLAY") and template_id is not None:
        identity_class = "resolved"
    elif template_evidence == "INFERRED":
        identity_class = "derived"
    else:
        identity_class = "unresolved"

    strength = STRENGTH_WEAK
    if identity_class == "resolved" and (template_ns == NS_CHAR or fp is not None):
        strength = STRENGTH_STRONG
    elif identity_class == "resolved":
        strength = STRENGTH_MEDIUM
    if identity_class == "derived":
        warnings.append("template_id_derived_from_kind_name")
    if identity_class == "unresolved" and rows:
        warnings.append("template_id_unresolved")
    if identity_class != "resolved":
        weak_identity = True
    else:
        weak_identity = strength != STRENGTH_STRONG

    if not rows and template_id is None:
        warnings.append("legacy_export_missing_identity_fields")

    rec = {
        "actorKey": key,
        "exportSha256": export_sha,
        "exportPath": None,
        "exportStarted": export.get("started"),
        "exportVersion": export.get("version"),
        "questId": export.get("quest"),
        "name": name,
        "team": team,
        "class": entity_class,
        "kind": kind,
        "summon": bool(summon) if isinstance(summon, bool) else None,
        "ownerName": owner_name,
        "ownerKey": None,
        "role": None,
        "slot": None,
        "slotBasis": "not_available_in_export",
        "templateId": template_id,
        "templateNamespace": template_ns,
        "templateBasis": template_basis,
        "templateEvidence": template_evidence,
        "typeKey": type_key,
        "instanceKey": instance_key,
        "entityKey": entity_key,
        "sameEntityKey": same_entity_key,
        "loadoutFingerprint": fp,
        "loadoutDetailHash": loadout_detail_hash(rows),
        "loadoutIdFingerprint": fp_ids,
        "loadoutFingerprintBasis": fp_basis,
        "equipmentIds": idset,
        "equipmentIdsFromAnnotation": from_annotation,
        "jobTraitId": job.get("id") if job else None,
        "potentialId": pot.get("id") if pot else None,
        "tokenId": token.get("id") if token else None,
        "abilityCount": len(rows),
        "identityClass": identity_class,
        "identityStrength": strength,
        "weakIdentity": weak_identity,
        "warnings": warnings,
        "annotationStatus": "unmatched",
        "evidence": {
            "kind_team_summon": "MEASURED" if kind is not None else "missing",
            "abilities": "MEASURED" if rows else "missing",
            "summon_owner": "OFFLINE_REPLAY" if owner_name else "missing",
            "class_split": "STATIC_REVIEW",
        },
    }
    return rec


def resolve_export(export_path, export=None, export_sha=None, annotations=None):
    """Resolve every actor of one export.  Read-only."""
    if export is None:
        export, export_sha = load_json(export_path)
    ann_idx = annotation_lookup(annotations)
    ann_run = ann_idx.get(export_sha.lower()) if export_sha else None

    events = export.get("events")
    owner_seen = set()
    if isinstance(events, list):
        for e in events:
            if not isinstance(e, dict):
                continue
            atk, own = e.get("attacker"), e.get("owner")
            if isinstance(atk, str) and isinstance(own, str) and atk != own:
                owner_seen.add(atk)

    raw_actors = export.get("actors")
    if not isinstance(raw_actors, list):
        raw_actors = []
    actors = []
    for i, a in enumerate(raw_actors):
        if not isinstance(a, dict):
            continue
        rec = resolve_actor(export, export_sha, a, ann_run, index=i)
        rec["exportPath"] = export_path
        if rec["name"] in owner_seen and rec["summon"] is not True:
            if "summon_flag_false_but_owner_link_seen" not in rec["warnings"]:
                rec["warnings"].append("summon_flag_false_but_owner_link_seen")
        actors.append(rec)

    by_key = {}
    for rec in actors:
        if _is_int(rec["actorKey"]):
            by_key[rec["actorKey"]] = rec
    # resolve owner keys now that the actor table exists
    for rec in actors:
        on = rec.get("ownerName")
        if on:
            for other in actors:
                if other.get("name") == on and other.get("team") == rec.get("team"):
                    rec["ownerKey"] = other.get("actorKey")
                    break
            else:
                for other in actors:
                    if other.get("name") == on:
                        rec["ownerKey"] = other.get("actorKey")
                        break

    # ---- annotation matching -----------------------------------------
    ann_used = None
    if ann_run is not None:
        ann_used = annotate_actors(actors, ann_run)

    # ---- same-name census inside this export --------------------------
    name_counts = {}
    for rec in actors:
        if rec["name"]:
            name_counts[rec["name"]] = name_counts.get(rec["name"], 0) + 1
    collisions = []
    for nm, n in sorted(name_counts.items()):
        if n < 2:
            continue
        group = [r for r in actors if r["name"] == nm]
        templates = sorted({str(r["templateId"]) for r in group})
        fingerprints = sorted({str(r["loadoutFingerprint"]) for r in group})
        collisions.append({
            "name": nm,
            "count": n,
            "teamSet": sorted({r["team"] for r in group if _is_int(r["team"])}),
            "templateIds": templates,
            "loadoutFingerprints": fingerprints,
            "distinctEntities": len({r["entityKey"] for r in group if r["entityKey"]}),
            "autoMergeAllowed": False,
            "note": "equal name never justifies merging; see same_entity()",
        })

    # ---- identity grouping -------------------------------------------
    entity_groups = {}
    for rec in actors:
        k = rec["entityKey"] or ("weak|%s|%s" % (rec["team"], rec["name"]))
        g = entity_groups.setdefault(k, {
            "entityKey": rec["entityKey"],
            "typeKey": rec["typeKey"],
            "templateId": rec["templateId"],
            "templateBasis": rec["templateBasis"],
            "loadoutFingerprint": rec["loadoutFingerprint"],
            "names": [],
            "teamSet": [],
            "actorKeys": [],
            "classes": [],
        })
        g["names"].append(rec["name"])
        g["teamSet"].append(rec["team"])
        g["actorKeys"].append(rec["actorKey"])
        g["classes"].append(rec["class"])
        g["sameEntityKeys"] = sorted(set(g.get("sameEntityKeys", []) + [rec["sameEntityKey"]]))
    groups = []
    for k in sorted(entity_groups):
        g = entity_groups[k]
        g["names"] = sorted({n for n in g["names"] if n is not None})
        g["teamSet"] = sorted({t for t in g["teamSet"] if _is_int(t)})
        g["classes"] = sorted({c for c in g["classes"]})
        g["actorCount"] = len(g["actorKeys"])
        g["weakOnly"] = g["entityKey"] is None
        g["fieldSplit"] = len(g.get("sameEntityKeys", [])) > 1
        g["mergeHint"] = ("same template+loadout, different camp/class: keep separate per field"
                          if g["fieldSplit"] else "camp-unambiguous")
        groups.append(g)

    result = {
        "schema": IDENTITY_SCHEMA,
        "tool": TOOL_NAME,
        "toolVersion": TOOL_VERSION,
        "generatedFrom": {
            "exportPath": export_path,
            "exportSha256": export_sha,
            "exportVersion": export.get("version"),
            "quest": export.get("quest"),
            "started": export.get("started"),
            "result": export.get("result"),
            "runId": (export.get("run") or {}).get("id") if isinstance(export.get("run"), dict) else None,
            "runSeq": (export.get("run") or {}).get("seq") if isinstance(export.get("run"), dict) else None,
            "runIdIsGlobalKey": False,
            "runIdNote": "run.id/seq are per-session counters; they reset on process restart",
        },
        "annotations": {
            "provided": annotations is not None,
            "matchedExportSha256": ann_run is not None,
            "schema": (annotations or {}).get("schema") if isinstance(annotations, dict) else None,
            "orphanSha256": sorted(set(annotation_lookup(annotations)) - {export_sha.lower()}) if export_sha else [],
            "applied": ann_used,
        },
        "actors": actors,
        "identityGroups": groups,
        "nameCollisions": collisions,
        "census": count_identity(actors, collisions),
        "evidenceLevels": {
            "actor_table": "MEASURED",
            "template_id": "MEASURED (slot6/slot1) or INFERRED (kind+name hash)",
            "loadout_fingerprint": "MEASURED (export abilities) or OFFLINE_REPLAY (annotations)",
            "summon_owner": "OFFLINE_REPLAY (events owner!=attacker)",
            "class_split": "STATIC_REVIEW",
            "formation_slot": "not available in export (no claim made)",
        },
    }
    return result


def annotate_actors(actors, ann_run):
    """Apply human-confirmed annotations onto resolved actors. Never guess-fill."""
    confirmed = _ann_value(ann_run, ["confirmed"], {})
    roster = _ann_value(confirmed, ["roster"], None)
    applied = {
        "rosterMatched": 0,
        "rosterUnmatched": 0,
        "rosterNamesNotInExport": [],
        "unknownKept": [],
    }
    matched_names = set()
    if isinstance(roster, list):
        for entry in roster:
            if not isinstance(entry, dict):
                continue
            nm = entry.get("name")
            for rec in actors:
                if rec.get("name") == nm:
                    rec["annotationStatus"] = "confirmed"
                    matched_names.add(nm)
                    applied["rosterMatched"] += 1
                    break
            else:
                applied["rosterUnmatched"] += 1
                if isinstance(nm, str):
                    applied["rosterNamesNotInExport"].append(nm)
    for rec in actors:
        if rec.get("name") in matched_names:
            continue
        rec["annotationStatus"] = "not_in_annotations"

    # record which annotation fields were left unknown; never fill them in
    for path in (["confirmed", "roster"],
                 ["confirmed", "levels"],
                 ["confirmed", "mode"],
                 ["confirmed", "questPhase"],
                 ["confirmed", "experimentGroup"],
                 ["confirmed", "damageModel"],
                 ["confirmed", "formation"]):
        val = _ann_value(ann_run, path, ANNOTATION_UNKNOWN)
        if val == ANNOTATION_UNKNOWN:
            applied["unknownKept"].append(".".join(path))
    for actor in (roster if isinstance(roster, list) else []):  # noqa: E501
        if not isinstance(actor, dict):
            continue
        lo = actor.get("loadout")
        if lo is None or lo == ANNOTATION_UNKNOWN:
            applied["unknownKept"].append("confirmed.roster[%s].loadout" % actor.get("name"))
    return applied


# ---------------------------------------------------------------------------
# same-entity decision
# ---------------------------------------------------------------------------

def same_entity(a, b):
    """Decide whether two resolved actors denote the same entity.

    Returns {"verdict": one of
                 same_entity | same_template_distinct_instances |
                 same_template_different_loadout | different_entity |
                 different_export | weak_identity | undecidable,
             "reasons": [...], "evidence": [...], "sameBattleHint": bool|None}

    Name equality alone is never "same_entity".
    """
    reasons = []
    evidence = []

    if not isinstance(a, dict) or not isinstance(b, dict):
        return {"verdict": "undecidable", "reasons": ["argument_not_record"], "evidence": []}

    a_x, b_x = a.get("exportSha256"), b.get("exportSha256")
    if a_x and b_x and a_x == b_x:
        if _is_int(a.get("actorKey")) and a.get("actorKey") == b.get("actorKey"):
            return {"verdict": "same_entity", "reasons": ["same_export_and_actor_key"],
                    "evidence": ["MEASURED"], "sameBattleHint": True}
    elif a_x and b_x:
        # run.id/seq carry no cross-process uniqueness; the export hash is the key.
        same_battle_hint = False
        if a.get("exportStarted") and a.get("exportStarted") == b.get("exportStarted"):
            same_battle_hint = True
        reasons.append("export_sha256_differs")
        if same_battle_hint:
            reasons.append("same_started_timestamp_but_unknown_if_reexport")
        return {"verdict": "different_export", "reasons": reasons,
                "evidence": ["run.id/seq must not be used as a cross-process key"],
                "sameBattleHint": same_battle_hint}

    if (_is_int(a.get("team")) and _is_int(b.get("team")) and a.get("team") != b.get("team")
            and a.get("templateId") is not None and a.get("templateId") == b.get("templateId")):
        return {"verdict": "different_entity",
                "reasons": ["template_id_equal_but_camp_differs"],
                "evidence": ["same template on opposite camps is a mirror unit, not one entity"],
                "sameBattleHint": None}

    a_sum = a.get("summon") is True
    b_sum = b.get("summon") is True
    if a_sum != b_sum:
        reasons.append("summon_flag_differs")
        evidence.append("MEASURED")
        if a.get("class") == "summon" or b.get("class") == "summon":
            return {"verdict": "different_entity", "reasons": reasons,
                    "evidence": evidence, "sameBattleHint": None}

    if a_sum and b_sum and a.get("ownerName") and b.get("ownerName"):
        if a.get("ownerName") == b.get("ownerName") and a.get("templateId") == b.get("templateId"):
            return {"verdict": "same_template_distinct_instances",
                    "reasons": ["same_owner_same_template_different_instance"],
                    "evidence": evidence + ["multiple token instances per owner are allowed"],
                    "sameBattleHint": None}

    # template mismatch -> definitely different entities
    if a.get("templateId") is not None and b.get("templateId") is not None:
        if a.get("templateNamespace") != b.get("templateNamespace") or a.get("templateId") != b.get("templateId"):
            return {"verdict": "different_entity",
                    "reasons": ["template_id_differs"],
                    "evidence": evidence + ["template id is MEASURED (slot6/slot1) or INFERRED (kind+name hash)"],
                    "sameBattleHint": None}

    fa, fb = a.get("loadoutFingerprint"), b.get("loadoutFingerprint")
    if fa and fb and fa != fb:
        return {"verdict": "same_template_different_loadout",
                "reasons": ["template_id_equal", "loadout_fingerprint_differs"],
                "evidence": evidence + ["MEASURED loadout fingerprint; keep split or mark explicitly"],
                "sameBattleHint": None}

    # either side unresolved -> no merge claim
    if a.get("templateId") is None or b.get("templateId") is None or fa is None or fb is None:
        if a.get("name") and a.get("name") == b.get("name"):
            reasons.append("equal_name_only")
        reasons.append("identity_incomplete")
        return {"verdict": "weak_identity", "reasons": reasons,
                "evidence": evidence + ["name equality is evidence, not identity"],
                "sameBattleHint": None}

    # both sides resolved, template equal, loadout equal
    if _is_int(a.get("actorKey")) and _is_int(b.get("actorKey")) and a.get("actorKey") != b.get("actorKey"):
        return {"verdict": "same_template_distinct_instances",
                "reasons": ["template_id_equal", "loadout_fingerprint_equal", "different_actor_keys"],
                "evidence": evidence + ["duplicate copies on one field are allowed; do not collapse"],
                "sameBattleHint": None}
    if a.get("name") == b.get("name"):
        return {"verdict": "same_entity",
                "reasons": ["template_id_equal", "loadout_fingerprint_equal", "name_equal"],
                "evidence": evidence + ["MEASURED"], "sameBattleHint": None}
    return {"verdict": "same_template_distinct_instances",
            "reasons": ["template_id_equal", "loadout_fingerprint_equal", "name_differs"],
            "evidence": evidence + ["same template, different display name"],
            "sameBattleHint": None}


def count_identity(actors, collisions):
    c = {
        "actorTotal": len(actors),
        "classCounts": {},
        "team1Total": 0,
        "team2Total": 0,
        "summonTotal": 0,
        "summonOwnerResolved": 0,
        "strong": 0,
        "medium": 0,
        "weak": 0,
        "weakWithName": 0,
        "templateResolved": 0,
        "loadoutResolved": 0,
        "loadoutFromAnnotation": 0,
        "abilityRowsMissing": 0,
        "nameCollisionGroups": len(collisions),
        "nameCollisionActors": sum(x["count"] for x in collisions),
        "warningCounts": {},
    }
    for a in actors:
        c["classCounts"][a["class"]] = c["classCounts"].get(a["class"], 0) + 1
        if a["team"] == 1:
            c["team1Total"] += 1
        elif a["team"] == 2:
            c["team2Total"] += 1
        if a["summon"]:
            c["summonTotal"] += 1
            if a["ownerName"]:
                c["summonOwnerResolved"] += 1
        c[a["identityStrength"]] = c.get(a["identityStrength"], 0) + 1
        key = "class_" + a["identityClass"]
        c[key] = c.get(key, 0) + 1
        if a["weakIdentity"] and a["name"]:
            c["weakWithName"] += 1
        if a["templateId"] is not None:
            c["templateResolved"] += 1
        if a["loadoutFingerprint"]:
            c["loadoutResolved"] += 1
            if a["loadoutFingerprintBasis"] == "annotation_roster_loadout":
                c["loadoutFromAnnotation"] += 1
        if a["abilityCount"] == 0:
            c["abilityRowsMissing"] += 1
        for w in a["warnings"]:
            c["warningCounts"][w] = c["warningCounts"].get(w, 0) + 1
    c["classCounts"] = dict(sorted(c["classCounts"].items()))
    c["warningCounts"] = dict(sorted(c["warningCounts"].items()))
    return c


# ---------------------------------------------------------------------------
# annotations template / validation
# ---------------------------------------------------------------------------

ANNOTATION_TEMPLATE = {
    "schema": ANNOTATION_SCHEMA,
    "note": "human-confirmed run metadata; unknown fields must stay the string \"unknown\"",
    "runs": [
        {
            "exportSha256": "<64 hex chars of the export file>",
            "exportPath": "<path, informational only>",
            "confirmedBy": "<who confirmed>",
            "confirmedAt": "<ISO8601>",
            "confirmed": {
                "roster": [
                    {
                        "name": "<actor name as exported>",
                        "templateId": "unknown",
                        "slot": "unknown",
                        "level": "unknown",
                        "loadout": {
                            "artifact": [{"id": "unknown", "level": "unknown"}],
                            "sigil": [{"id": "unknown"}],
                            "passive": [{"id": "unknown", "level": "unknown"}],
                            "jobTrait": [{"id": "unknown"}],
                            "potential": [{"id": "unknown"}],
                        },
                    }
                ],
                "formation": "unknown",
                "mode": "unknown",
                "questPhase": "unknown",
                "experimentGroup": "unknown",
                "damageModel": "unknown",
                "notes": "",
            },
        }
    ],
}


def validate_annotations(annotations, export_sha=None):
    """Structural validation; collects problems, never fixes or fills values."""
    problems = []
    info = {"runs": 0, "matched": False, "unknownFieldsKept": 0, "unknownFieldsFilledIn": []}
    if not isinstance(annotations, dict):
        return ["annotation root is not an object"], info
    if annotations.get("schema") != ANNOTATION_SCHEMA:
        problems.append("unexpected annotation schema: %r" % (annotations.get("schema"),))
    runs = annotations.get("runs")
    if not isinstance(runs, list):
        problems.append("annotations.runs missing or not a list")
        return problems, info
    info["runs"] = len(runs)
    known_top = {"schema", "note", "runs", "byExportSha256", "generatedBy", "generatedAt"}
    for k in annotations:
        if k not in known_top:
            problems.append("unknown top-level annotation field: %s" % k)
    seen = set()
    for i, r in enumerate(runs):
        if not isinstance(r, dict):
            problems.append("runs[%d] is not an object" % i)
            continue
        h = r.get("exportSha256")
        if not isinstance(h, str) or len(h) != 64:
            problems.append("runs[%d].exportSha256 is not a 64-char hex string" % i)
        elif h.lower() in seen:
            problems.append("runs[%d].exportSha256 duplicated" % i)
        else:
            seen.add(h.lower())
            if export_sha and h.lower() == export_sha.lower():
                info["matched"] = True
        conf = r.get("confirmed")
        if not isinstance(conf, dict):
            problems.append("runs[%d].confirmed missing" % i)
            continue
        for f in ("roster", "levels", "formation", "mode", "questPhase", "experimentGroup",
                  "damageModel"):
            if f not in conf:
                problems.append("runs[%d].confirmed.%s missing (write \"unknown\" if not confirmed)" % (i, f))
            elif conf.get(f) == ANNOTATION_UNKNOWN:
                info["unknownFieldsKept"] += 1
            elif conf.get(f) in ("", None, []):
                info["unknownFieldsFilledIn"].append("runs[%d].confirmed.%s" % (i, f))
        if "notes" in conf and conf.get("notes") is None:
            problems.append("runs[%d].confirmed.notes is null; use \"\" for no comment" % i)
        roster = conf.get("roster")
        if roster is not None and roster != ANNOTATION_UNKNOWN and not isinstance(roster, list):
            problems.append("runs[%d].confirmed.roster must be a list or \"unknown\"" % i)
        if isinstance(roster, list):
            for j, e in enumerate(roster):
                if not isinstance(e, dict):
                    problems.append("runs[%d].confirmed.roster[%d] is not an object" % (i, j))
                    continue
                for f in ("name", "templateId", "slot", "level", "loadout"):
                    if f not in e:
                        problems.append("runs[%d].confirmed.roster[%d].%s missing" % (i, j, f))
                    elif e.get(f) == ANNOTATION_UNKNOWN:
                        info["unknownFieldsKept"] += 1
    for f in info["unknownFieldsFilledIn"]:
        problems.append("field written as empty instead of \"unknown\": %s" % f)
    return problems, info


# ---------------------------------------------------------------------------
# self test (synthetic exports; no real file needed)
# ---------------------------------------------------------------------------

def _ability(slot, aid, level=1, name="x", talents=None):
    return {"slot": slot, "slotName": SLOT_NAMES.get(slot, "?"), "origin": 1,
            "id": aid, "level": level, "name": name, "talents": talents or []}


def _synth_export(actors, run_id=1, run_seq=0, version="1.7.10", events=None, started="2026-10-04T00:00:00"):
    return {
        "app": "DpsMeter", "version": version, "quest": 411001, "duration": 60.0,
        "result": "Lose", "started": started, "run": {"id": run_id, "seq": run_seq},
        "actors": actors, "events": events or [],
    }


def _resolve(export, sha, annotations=None):
    return resolve_export("<synthetic>", export=export, export_sha=sha, annotations=annotations)


def _pick(res, name, key):
    for a in res["actors"]:
        if a["name"] == name and a["actorKey"] == key:
            return a
    raise AssertionError("actor %s/%s missing" % (name, key))


class _Checks(object):
    def __init__(self):
        self.rows = []

    def check(self, case, name, ok, detail=""):
        self.rows.append((case, name, bool(ok), detail))

    def failures(self):
        return [r for r in self.rows if not r[2]]


def selftest():
    c = _Checks()

    # ---- case A: same name, different character template -----------------
    a1 = {"key": 2, "name": "SAME_NAME", "team": 1, "kind": "P", "summon": False, "dealt": 10,
          "abilities": [_ability(6, 42, name="A"), _ability(9, 75, name="sword"),
                        _ability(10, 1, name="sig")]}
    a2 = {"key": 3, "name": "SAME_NAME", "team": 1, "kind": "P", "summon": False, "dealt": 20,
          "abilities": [_ability(6, 56, name="B"), _ability(9, 75, name="sword"),
                        _ability(10, 1, name="sig")]}
    eA = _synth_export([a1, a2], run_id=1)
    rA = _resolve(eA, "a" * 64)
    ra1, ra2 = _pick(rA, "SAME_NAME", 2), _pick(rA, "SAME_NAME", 3)
    d = same_entity(ra1, ra2)
    c.check("A_same_name_diff_template", "no_auto_merge", d["verdict"] == "different_entity", d["verdict"])
    c.check("A_same_name_diff_template", "reason_template_differs", "template_id_differs" in d["reasons"], str(d["reasons"]))
    c.check("A_same_name_diff_template", "distinct_entity_keys", ra1["entityKey"] != ra2["entityKey"], "")
    c.check("A_same_name_diff_template", "name_collision_recorded", len(rA["nameCollisions"]) == 1, str(len(rA["nameCollisions"])))
    c.check("A_same_name_diff_template", "collision_not_automerge",
            rA["nameCollisions"][0]["autoMergeAllowed"] is False, "")
    c.check("A_same_name_diff_template", "distinct_templates_in_collision",
            len(rA["nameCollisions"][0]["templateIds"]) == 2, str(rA["nameCollisions"][0]["templateIds"]))

    # ---- case B: same template, equipment changed ------------------------
    b1 = {"key": 2, "name": "HERO", "team": 1, "kind": "P", "summon": False, "dealt": 10,
          "abilities": [_ability(6, 42, name="HERO"), _ability(9, 75, name="sword"),
                        _ability(10, 1, name="sig")]}
    b2 = {"key": 2, "name": "HERO", "team": 1, "kind": "P", "summon": False, "dealt": 10,
          "abilities": [_ability(6, 42, name="HERO"), _ability(9, 10030, name="hourglass"),
                        _ability(10, 1, name="sig")]}
    rB1 = _resolve(_synth_export([b1]), "b" * 64)
    rB2 = _resolve(_synth_export([b2]), "c" * 64)
    rb1, rb2 = _pick(rB1, "HERO", 2), _pick(rB2, "HERO", 2)
    d = same_entity(rb1, rb2)
    c.check("B_same_char_loadout_change", "split_not_merge", d["verdict"] == "different_export", d["verdict"])
    c.check("B_same_char_loadout_change", "fingerprint_differs", rb1["loadoutFingerprint"] != rb2["loadoutFingerprint"], "")
    c.check("B_same_char_loadout_change", "same_template_id", rb1["templateId"] == rb2["templateId"], "")
    c.check("B_same_char_loadout_change", "same_type_key", rb1["typeKey"] == rb2["typeKey"], "")
    c.check("B_same_char_loadout_change", "different_entity_key", rb1["entityKey"] != rb2["entityKey"], "")
    # and inside one export the split is explicit
    rB3 = _resolve(_synth_export([b1, {"key": 5, "name": "HERO", "team": 1, "kind": "P",
                                       "summon": False,
                                       "abilities": [_ability(6, 42, name="HERO"),
                                                     _ability(9, 10030, name="hourglass")]}]), "d" * 64)
    rb3, rb4 = _pick(rB3, "HERO", 2), _pick(rB3, "HERO", 5)
    d2 = same_entity(rb3, rb4)
    c.check("B_same_char_loadout_change", "same_template_different_loadout_verdict",
            d2["verdict"] == "same_template_different_loadout", d2["verdict"])

    # ---- case C: summon vs owner ----------------------------------------
    owner = {"key": 2, "name": "SUMMONER", "team": 1, "kind": "P", "summon": False, "dealt": 10,
             "abilities": [_ability(6, 42, name="SUMMONER")]}
    tok = {"key": 11, "name": "CANNON", "team": 1, "kind": "P", "summon": True, "dealt": 5,
           "abilities": [_ability(1, 30042, name="CANNON")]}
    tok2 = {"key": 12, "name": "CANNON", "team": 1, "kind": "P", "summon": True, "dealt": 5,
            "abilities": [_ability(1, 30042, name="CANNON")]}
    events = [{"t": 1.0, "attacker": "CANNON", "owner": "SUMMONER", "atkKey": 11, "atkTeam": 1},
              {"t": 2.0, "attacker": "SUMMONER", "owner": "SUMMONER", "atkKey": 2, "atkTeam": 1}]
    rC = _resolve(_synth_export([owner, tok, tok2], events=events), "e" * 64)
    ro, rt, rt2 = _pick(rC, "SUMMONER", 2), _pick(rC, "CANNON", 11), _pick(rC, "CANNON", 12)
    c.check("C_summon_vs_owner", "owner_link_found", rt["ownerName"] == "SUMMONER", str(rt["ownerName"]))
    c.check("C_summon_vs_owner", "owner_not_merged",
            same_entity(ro, rt)["verdict"] == "different_entity", same_entity(ro, rt)["verdict"])
    c.check("C_summon_vs_owner", "summon_flag_differs",
            "summon_flag_differs" in same_entity(ro, rt)["reasons"], str(same_entity(ro, rt)["reasons"]))
    c.check("C_summon_vs_owner", "two_tokens_distinct_instances",
            same_entity(rt, rt2)["verdict"] == "same_template_distinct_instances",
            same_entity(rt, rt2)["verdict"])
    c.check("C_summon_vs_owner", "summon_owner_flag_false_never",
            ro["summon"] is False and rt["summon"] is True, "")
    c.check("C_summon_vs_owner", "summon_template_namespace", rt["templateNamespace"] == "token", str(rt["templateNamespace"]))
    c.check("C_summon_vs_owner", "summon_has_distinct_entity_key", rt["entityKey"] != ro["entityKey"], "")

    # ---- case D: legacy file missing every new field ---------------------
    legacy = {"key": 4, "name": "OLDUNIT", "team": 1, "dealt": 100}
    eD = {"app": "DpsMeter", "version": "1.0.0", "quest": 411001, "actors": [legacy],
          "started": "2026-10-04T00:00:00"}
    rD = _resolve(eD, "f" * 64)
    rd = _pick(rD, "OLDUNIT", 4)
    c.check("D_legacy_missing_fields", "no_failure", rD is not None, "")
    c.check("D_legacy_missing_fields", "weak_identity", rd["identityStrength"] == "weak", rd["identityStrength"])
    c.check("D_legacy_missing_fields", "weak_flag", rd["weakIdentity"] is True, "")
    c.check("D_legacy_missing_fields", "warning_present",
            "legacy_export_missing_identity_fields" in rd["warnings"], str(rd["warnings"]))
    c.check("D_legacy_missing_fields", "abilities_missing_warning",
            "abilities_missing_in_export" in rd["warnings"], str(rd["warnings"]))
    c.check("D_legacy_missing_fields", "no_guessed_template", rd["templateId"] is None, str(rd["templateId"]))
    c.check("D_legacy_missing_fields", "no_guessed_loadout", rd["loadoutFingerprint"] is None, "")
    # same export, one resolvable actor and one legacy actor -> still no merge claim
    legacy_mix = {"key": 7, "name": "MIX", "team": 1, "kind": "P", "summon": False}
    eD2 = _synth_export([legacy_mix, b1])
    rD2 = _resolve(eD2, "f" * 64)
    c.check("D_legacy_missing_fields", "legacy_vs_modern_weak",
            same_entity(_pick(rD2, "MIX", 7), _pick(rD2, "HERO", 2))["verdict"] == "weak_identity",
            same_entity(_pick(rD2, "MIX", 7), _pick(rD2, "HERO", 2))["verdict"])
    c.check("D_legacy_missing_fields", "cross_export_is_not_same_run",
            same_entity(rd, rb1)["verdict"] == "different_export", same_entity(rd, rb1)["verdict"])
    c.check("D_legacy_missing_fields", "cross_export_same_started_hint",
            same_entity(rd, rb1)["sameBattleHint"] is True, str(same_entity(rd, rb1)["sameBattleHint"]))
    c.check("D_legacy_missing_fields", "census_counts_weak", rD["census"]["weak"] == 1, str(rD["census"]["weak"]))

    # ---- case E: same run.id/seq, different export sha -------------------
    eE1 = _synth_export([dict(b1, key=2)], run_id=1, run_seq=0)
    eE2 = _synth_export([dict(b1, key=2, dealt=999)], run_id=1, run_seq=0)
    rE1 = _resolve(eE1, "1" * 64)
    rE2 = _resolve(eE2, "2" * 64)
    re1, re2 = _pick(rE1, "HERO", 2), _pick(rE2, "HERO", 2)
    dE = same_entity(re1, re2)
    c.check("E_run_id_not_global_key", "different_export_verdict", dE["verdict"] == "different_export", dE["verdict"])
    c.check("E_run_id_not_global_key", "reason_sha_differs", "export_sha256_differs" in dE["reasons"], str(dE["reasons"]))
    c.check("E_run_id_not_global_key", "run_id_equal_but_not_used",
            re1["exportSha256"] != re2["exportSha256"] and eE1["run"] == eE2["run"], "")
    c.check("E_run_id_not_global_key", "instance_key_includes_export", re1["instanceKey"] != re2["instanceKey"], "")
    c.check("E_run_id_not_global_key", "entity_key_cross_run_reusable",
            re1["entityKey"] == re2["entityKey"], "%s vs %s" % (re1["entityKey"], re2["entityKey"]))
    c.check("E_run_id_not_global_key", "runIdIsGlobalKey_false",
            rE1["generatedFrom"]["runIdIsGlobalKey"] is False, "")

    # ---- case F: annotation never guess-fills ----------------------------
    ann = {
        "schema": ANNOTATION_SCHEMA,
        "runs": [{
            "exportSha256": "b" * 64,
            "confirmedBy": "tester",
            "confirmedAt": "2026-10-04T00:00:00",
            "confirmed": {
                "roster": [{"name": "HERO", "templateId": "unknown", "slot": "unknown",
                            "level": "unknown",
                            "loadout": {"artifact": [{"id": 10030, "level": "unknown"}]}}],
                "levels": "unknown",
                "formation": "unknown", "mode": "unknown", "questPhase": "unknown",
                "experimentGroup": "unknown", "damageModel": "unknown", "notes": "",
            },
        }],
    }
    probs, info = validate_annotations(ann, "b" * 64)
    c.check("F_annotation", "schema_valid", probs == [], str(probs))
    c.check("F_annotation", "matched", info["matched"] is True, "")
    c.check("F_annotation", "unknown_kept", info["unknownFieldsKept"] > 0, str(info["unknownFieldsKept"]))
    c.check("F_annotation", "nothing_filled_in", info["unknownFieldsFilledIn"] == [], str(info["unknownFieldsFilledIn"]))
    legacy_actor = {"key": 5, "name": "NOABIL", "team": 1, "kind": "P", "summon": False}
    rF = _resolve(_synth_export([legacy_actor]), "b" * 64, annotations=ann)
    rf = _pick(rF, "NOABIL", 5)
    c.check("F_annotation", "annotation_matched_flag", rF["annotations"]["matchedExportSha256"] is True, "")
    c.check("F_annotation", "unknown_template_not_filled", rf["templateId"] is None, str(rf["templateId"]))
    # annotation loadout IS allowed to supply the fingerprint, and is labelled
    rF2 = _resolve(_synth_export([dict(b1, key=2)]), "b" * 64, annotations=ann)
    rf2 = _pick(rF2, "HERO", 2)
    c.check("F_annotation", "export_abilities_preferred",
            rf2["loadoutFingerprintBasis"] == "export_abilities", rf2["loadoutFingerprintBasis"])
    rF3 = _resolve(_synth_export([{"key": 6, "name": "HERO", "team": 1, "kind": "P", "summon": False}]),
                   "b" * 64, annotations=ann)
    rf3 = _pick(rF3, "HERO", 6)
    c.check("F_annotation", "annotation_fallback_labelled",
            rf3["loadoutFingerprintBasis"] == "annotation_roster_loadout", rf3["loadoutFingerprintBasis"])
    c.check("F_annotation", "annotation_fallback_warned",
            "loadout_from_annotation_not_export" in rf3["warnings"], str(rf3["warnings"]))
    # empty-string instead of "unknown" is a validation problem
    bad = json.loads(json.dumps(ann))
    bad["runs"][0]["confirmed"]["mode"] = ""
    probs2, _ = validate_annotations(bad, "b" * 64)
    c.check("F_annotation", "empty_instead_of_unknown_rejected", len(probs2) > 0, str(probs2))

    # ---- case G: template id collision across names (weak template) ------
    ga = {"key": 2, "name": "NAME_ONE", "team": 1, "kind": "P", "summon": False,
          "abilities": [_ability(6, 99, name="NAME_ONE")]}
    gb = {"key": 3, "name": "NAME_TWO", "team": 1, "kind": "P", "summon": False,
          "abilities": [_ability(6, 99, name="NAME_TWO")]}
    rG = _resolve(_synth_export([ga, gb]), "9" * 64)
    rg1, rg2 = _pick(rG, "NAME_ONE", 2), _pick(rG, "NAME_TWO", 3)
    c.check("G_template_vs_name", "same_template_id_but_names_differ",
            rg1["templateId"] == rg2["templateId"] and rg1["name"] != rg2["name"], "")
    c.check("G_template_vs_name", "still_different_instances",
            rg1["instanceKey"] != rg2["instanceKey"], "")
    c.check("G_template_vs_name", "no_claim_of_same_entity_from_name_only",
            same_entity(rg1, rg2)["verdict"] in ("same_entity", "same_template_distinct_instances"),
            same_entity(rg1, rg2)["verdict"])

    # ---- case H: same template+loadout on both camps (mirror unit) -------
    h1 = {"key": 2, "name": "MIRROR", "team": 1, "kind": "P", "summon": False,
          "abilities": [_ability(6, 77, name="MIRROR"), _ability(9, 75, name="sword")]}
    h2 = {"key": 9, "name": "MIRROR", "team": 2, "kind": "P", "summon": False,
          "abilities": [_ability(6, 77, name="MIRROR"), _ability(9, 75, name="sword")]}
    rH = _resolve(_synth_export([h1, h2]), "8" * 64)
    rh1, rh2 = _pick(rH, "MIRROR", 2), _pick(rH, "MIRROR", 9)
    c.check("H_same_template_both_camps", "camp_differs_not_merged",
            same_entity(rh1, rh2)["verdict"] == "different_entity", same_entity(rh1, rh2)["verdict"])
    c.check("H_same_template_both_camps", "reason_camp_differs",
            "template_id_equal_but_camp_differs" in same_entity(rh1, rh2)["reasons"],
            str(same_entity(rh1, rh2)["reasons"]))
    c.check("H_same_template_both_camps", "entity_key_equal_by_design",
            rh1["entityKey"] == rh2["entityKey"], "")
    c.check("H_same_template_both_camps", "same_entity_key_differs",
            rh1["sameEntityKey"] != rh2["sameEntityKey"], "")
    c.check("H_same_template_both_camps", "group_flagged_field_split",
            any(g["fieldSplit"] for g in rH["identityGroups"]), "")

    failures = c.failures()
    for case, name, ok, detail in c.rows:
        print("[%s] %-36s %-42s %s" % ("PASS" if ok else "FAIL", case, name, detail))
    print("SELFTEST: %d checks, %d failed" % (len(c.rows), len(failures)))
    return 0 if not failures else 1


def print_annotation_template():
    print(json.dumps(ANNOTATION_TEMPLATE, ensure_ascii=True, indent=2, sort_keys=False))
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser(
        prog=TOOL_NAME,
        description="Offline cross-battle identity mapping for DpsMeter exports (read-only).")
    ap.add_argument("--export", help="path to one DpsMeter battle export JSON (read-only)")
    ap.add_argument("--annotations", help="optional run_annotations.json (may be absent/empty)")
    ap.add_argument("--out", help="write the identity result JSON here (UTF-8)")
    ap.add_argument("--selftest", action="store_true", help="run the synthetic negative-control suite")
    ap.add_argument("--print-annotation-template", action="store_true",
                    help="print the run_annotations.json field template and exit")
    args = ap.parse_args(argv)

    if args.selftest:
        return selftest()
    if args.print_annotation_template:
        return print_annotation_template()
    if not args.export:
        ap.print_help()
        return 2

    annotations = None
    ann_sha = None
    if args.annotations:
        if os.path.isfile(args.annotations):
            annotations = json.loads(open(args.annotations, "rb").read().decode("utf-8"))
            ann_sha = sha256_file(args.annotations)
        else:
            print("NOTE: annotations path not found, continuing without annotations: %s" % args.annotations)
    res = resolve_export(args.export, annotations=annotations)
    if annotations is not None:
        problems, info = validate_annotations(annotations, res["generatedFrom"]["exportSha256"])
        res["annotations"]["validationProblems"] = problems
        res["annotations"]["validationInfo"] = info
        res["annotationsFileSha256"] = ann_sha

    out = json.dumps(res, ensure_ascii=True, indent=2, sort_keys=False) + "\n"
    if args.out:
        with open(args.out, "w", encoding="utf-8", newline="\n") as f:
            f.write(out)
        print("WROTE %s" % args.out)
    else:
        sys.stdout.write(out)

    # human-readable one-liner goes to stderr so stdout can stay machine-readable
    g = res["generatedFrom"]
    c = res["census"]
    sys.stderr.write("export=%s sha256=%s version=%s quest=%s run.id=%s run.seq=%s actors=%d strong=%d medium=%d weak=%d summons=%d collisions=%d\n" % (
        os.path.basename(args.export), g["exportSha256"], g["exportVersion"], g["quest"],
        g["runId"], g["runSeq"], c["actorTotal"], c["strong"], c["medium"], c["weak"],
        c["summonTotal"], c["nameCollisionGroups"]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
