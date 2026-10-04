# -*- coding: utf-8 -*-
"""Export JSON -> typed data. No attribution, no math beyond indexing."""
from __future__ import annotations
import io, json, os
from .model import ActorRef


class ExportData:
    """A loaded battle export."""

    def __init__(self, raw, path=""):
        self.raw = raw
        self.path = path
        self.version = raw.get("version")
        self.quest = raw.get("quest")
        self.duration = raw.get("duration")
        self.result = raw.get("result")
        self.app = raw.get("app")
        self.run = raw.get("run")
        self.started = raw.get("started")
        self.totals = raw.get("totals") or {}
        self.roster_audit = raw.get("rosterAudit") or {}
        self.events = raw.get("events") or []
        self.actors = []
        self.by_key = {}
        for a in (raw.get("actors") or []):
            hp = a.get("hpPct")
            ref = ActorRef(key=a.get("key"), name=a.get("name"), team=a.get("team") or 0,
                           kind=a.get("kind") or "", summon=bool(a.get("summon")),
                           hp_pct_last=(hp[-1] if isinstance(hp, list) and hp else None))
            self.actors.append(ref)
            if ref.key is not None:
                self.by_key[ref.key] = ref
        self.duplicate_names = self._dup_names()

    def _dup_names(self):
        seen = {}
        for a in self.actors:
            seen.setdefault(a.name, []).append(a.key)
        return {n: ks for n, ks in seen.items() if len(ks) > 1}

    @property
    def name(self):
        return os.path.basename(self.path) if self.path else ""

    def team_actors(self, team=1):
        return [a for a in self.actors if a.team == team]

    def team_name_index(self, team=1):
        """name -> ActorRef, only when that name is unique inside the team."""
        idx = {}
        seen = {}
        for a in self.actors:
            if a.team != team:
                continue
            seen.setdefault(a.name, []).append(a)
        for n, refs in seen.items():
            if len(refs) == 1:
                idx[n] = refs[0]
            else:
                idx[n] = None   # ambiguous name inside the team
        return idx

    def all_names(self):
        return set(a.name for a in self.actors if a.name)

    def source(self):
        return {"file": self.name, "pluginVersion": self.version, "quest": self.quest,
                "duration": self.duration, "result": self.result,
                "run": self.run, "started": self.started, "app": self.app}


def load(path):
    with io.open(path, "r", encoding="utf-8") as fh:
        raw = json.load(fh)
    return ExportData(raw, path)


def training_mode(export):
    """quest 9999 is the training ground (special x0.03 rules, dictionary section 8.4)."""
    return int(export.quest or 0) == 9999
