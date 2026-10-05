# -*- coding: utf-8 -*-
"""fold -> rule owner. Implements the ladder of dictionary section 4.

Every resolution returns (ActorRef | None, reason). None means "goes to the
unattributed pool" -- a fold is never silently charged to the attacker.
"""
from __future__ import annotations
import re
from .model import RESOLVED_REASONS, NAME_BASED_REASONS

NAME_RE = re.compile(r"\[([^\]]+)\]")
ABILITY_ID_RE = re.compile(r"^(?:text|talent)#(?:buff)?\d+/(\d+)(?:/|$)")


def ability_id_of(origin):
    m = ABILITY_ID_RE.match(str(origin or ""))
    return int(m.group(1)) if m else None


def grant_key_of(origin):
    """R54: "given#4/1006/-10" -> "1006/-10" (the granted modifier's identity). None for any other channel,
    so only the granted channel is probed."""
    s = str(origin or "")
    if not s.startswith("given#"):
        return None
    i = s.find("/")
    return s[i + 1:] if 0 <= i < len(s) - 1 else None


class OwnerIndex:
    """Stable-key-first owner index for one team.

    Lookup order: actors[].key for the attacker, then byUnit name -> key,
    then ability id -> holder keys, then ability name -> holder keys.
    """

    def __init__(self, export, team=1):
        self.team = team
        self.by_key = {a.key: a for a in export.team_actors(team)}
        self.name_index = export.team_name_index(team)     # name -> ActorRef or None (ambiguous)
        self.ambiguous_team_names = set(n for n, r in self.name_index.items() if r is None)
        self.all_names = export.all_names()
        self.by_ability_id = {}    # ability id -> [ActorRef]
        self.by_ability_name = {}  # ability name -> [ActorRef]
        # R54: "<type>/<param>" -> [ActorRef] for the team actors that HOLD a rule granting it. This is the
        # same evidence Contribution.Index.ByGrant carries in C#, so the two cores decide alike.
        self.by_grant = {}
        # actor rows are plain dicts in the export; keep raw rows for ability data
        self.raw_by_key = {}
        for row in (export.raw.get("actors") or []):
            if row.get("team") != team:
                continue
            k = row.get("key")
            self.raw_by_key[k] = row
            for ab in (row.get("abilities") or []):
                if ab.get("id") is not None:
                    self.by_ability_id.setdefault(ab["id"], []).append(self.by_key[k])
                if ab.get("name"):
                    self.by_ability_name.setdefault(ab["name"], []).append(self.by_key[k])
            for t in (row.get("talents") or []):
                if t.get("abilityId") is not None:
                    self.by_ability_id.setdefault(t["abilityId"], []).append(self.by_key[k])
                if t.get("ability"):
                    self.by_ability_name.setdefault(t["ability"], []).append(self.by_key[k])
            for ab in (row.get("abilities") or []):
                for t in (ab.get("talents") or []):
                    if "GiveTalent" not in str(t.get("cond") or ""):
                        continue
                    p = t.get("p") or []
                    if not p:
                        continue
                    self.by_grant.setdefault("%s/%s" % (t.get("type"), p[0]), []).append(self.by_key[k])
        # de-duplicate holder lists (an actor can expose the same ability twice)
        for d in (self.by_ability_id, self.by_ability_name, self.by_grant):
            for k, refs in d.items():
                uniq = {}
                for r in refs:
                    uniq[r.key] = r
                d[k] = list(uniq.values())

    def resolve_by_unit(self, by_unit):
        """byUnit is a character NAME in the 1.5.5 contract, so it is mapped to a key here.

        A name that is ambiguous inside our team must NOT be merged (Phase C): it becomes
        byUnit_ambiguous instead of silently picking one of the same-named actors.
        """
        ref = self.name_index.get(by_unit)
        if ref is not None:
            return ref, "byUnit"
        if by_unit in self.ambiguous_team_names:
            return None, "byUnit_ambiguous"
        if by_unit in self.all_names:
            return None, "byUnit_outside"
        return None, "byUnit_unknown"

    def resolve(self, fold, attacker):
        """fold: FoldEntry, attacker: ActorRef -> (ActorRef | None, reason)."""
        if fold.by_unit:
            return self.resolve_by_unit(fold.by_unit)
        kind = fold.kind
        if kind in ("text", "talent"):
            aid = ability_id_of(fold.origin)
            holders = self.by_ability_id.get(aid, []) if aid is not None else []
            if len(holders) == 1:
                return holders[0], "ability_holder_unique"
            if len(holders) > 1:
                if any(h.key == attacker.key for h in holders):
                    return attacker, "ability_holder_attacker"
                return None, "ambiguous_multi_holder"
            return attacker, "attacker_default"
        if kind == "global":
            m = NAME_RE.search(fold.label or "")
            holders = self.by_ability_name.get(m.group(1), []) if m else []
            if len(holders) == 1:
                return holders[0], "global_name_unique"
            return None, "global_ambiguous"
        if kind == "given":
            # R54: the granted 「阻挡增伤」 family. Still UNATTRIBUTED; the code only says how close the
            # roster-side evidence is to naming the provider (none / one / several).
            gk = grant_key_of(fold.origin)
            holders = self.by_grant.get(gk, []) if gk else []
            if not holders:
                return None, "given_carrier_none"
            if len(holders) == 1:
                return None, "given_carrier_one"
            return None, "given_carrier_ambiguous"
        return None, "unknown_kind"

    def reason_is_name_based(self, reason):
        return reason in NAME_BASED_REASONS
