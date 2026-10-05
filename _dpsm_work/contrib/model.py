# -*- coding: utf-8 -*-
"""Data model for the contribution core. Pure data, no I/O, no business logic.

Contract: _dpsm_work/CONTRIBUTION-DATA-DICTIONARY.md (schema v0.1-draft).
"""
from __future__ import annotations
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple

# Fixed reason codes (dictionary section 4). Every fold must land on exactly one.
REASON_CODES = (
    "byUnit", "byUnit_outside", "byUnit_ambiguous", "byUnit_unknown",
    "ability_holder_unique", "ability_holder_attacker", "ambiguous_multi_holder",
    "attacker_default", "global_name_unique", "global_ambiguous",
    # R54: the GRANTED channel ("阻挡增伤") got its own codes instead of being mixed into unknown_kind. They are
    # still UNATTRIBUTED -- the difference is what the reader is told: how close we are to naming the provider.
    "given_carrier_none", "given_carrier_one", "given_carrier_ambiguous",
    "unknown_kind", "zero_factor", "noop_factor",
)
RESOLVED_REASONS = ("byUnit", "ability_holder_unique", "ability_holder_attacker",
                    "attacker_default", "global_name_unique")
UNRESOLVED_REASONS = ("byUnit_outside", "byUnit_ambiguous", "byUnit_unknown",
                      "ambiguous_multi_holder", "global_ambiguous",
                      "given_carrier_none", "given_carrier_one", "given_carrier_ambiguous",
                      "unknown_kind")
# Reasons whose owner was decided through a character name (Phase C bookkeeping).
NAME_BASED_REASONS = ("byUnit", "ability_holder_unique", "ability_holder_attacker",
                      "attacker_default", "global_name_unique")


def game_residual(calc):
    """R62 (B): the hit's unexplained multiplier in the GAME's caliber.

    `calc["residual"]` is `applied / theory` (CalcBreakdown.cs:81): what reached 耐久 over what the
    chain predicted. An absorbed / nullified hit therefore drags it toward 0 and can hide a crit behind a
    1/30th "residual". The game's own figure is `applied + absorbed`, which is exactly what
    `calc["valueMatches"]` compares against `theory`. Measured 2026-10-05 over the 82 exports: 801
    damage hits carry `absorbed > 0` and 688 of them have `residual` < 1, so every residual ladder that
    read the exported field was classifying absorption as a missing multiplier.

    Returns None when the block cannot answer it (0 = "no chain" must never be published as a measured 0).
    """
    if not isinstance(calc, dict):
        return None
    applied = calc.get("applied")
    absorbed = calc.get("absorbed")
    theory = calc.get("theory")
    if (isinstance(applied, (int, float)) and isinstance(absorbed, (int, float))
            and isinstance(theory, (int, float)) and theory > 0):
        return (float(applied) + float(absorbed)) / float(theory)
    resid = calc.get("residual")
    return float(resid) if isinstance(resid, (int, float)) else None


@dataclass
class ActorRef:
    key: int
    name: str
    team: int = 0
    kind: str = ""
    summon: bool = False
    hp_pct_last: Optional[float] = None

    def __hash__(self):
        return hash(self.key)


@dataclass
class FoldEntry:
    """One multiplier from calc.fold (factor > 0 and != 1)."""
    kind: str
    side: str
    origin: str
    factor: float
    label: str = ""
    by_unit: str = ""


@dataclass
class HitLine:
    """One credit line of a single hit: amount -> owner (or unattributed)."""
    owner_key: Optional[int]
    amount: float
    reason: str
    rule_key: Tuple
    rule_name: str
    kind: str = ""
    side: str = ""
    factor: float = 1.0


@dataclass
class HitResult:
    index: int
    time: float
    attacker_key: int
    victim_key: Optional[int]
    damage: float
    multiplier: float
    base: float
    pool: float
    lines: List[HitLine] = field(default_factory=list)
    unattributed: float = 0.0
    folds_total: int = 0
    folds_resolved: int = 0
    folds_unresolved: int = 0
    folds_zero: int = 0
    folds_noop: int = 0
    crit: bool = False
    # What reached 耐久 over the chain's theory, exactly as the plugin exports it (`calc.residual`).
    residual_mult: Optional[float] = None
    # R62 (B): the same question asked in the GAME's caliber -- (applied + absorbed) / theory. Differs
    # from residual_mult only on absorbed hits; that difference is the point.
    residual_game: Optional[float] = None

    @property
    def credited(self) -> float:
        return sum(l.amount for l in self.lines if l.owner_key is not None)


@dataclass
class ActorCredit:
    key: int
    name: str
    team: int = 1
    kind: str = ""
    summon: bool = False
    direct: float = 0.0
    # 1.7.12: the same-team split of `direct`. `hostile` is the part aimed at the other team and
    # `friendly` the part aimed at the attacker OWN team (self-damage included). They come from the
    # event flag, exactly like the plugin does it, so the two implementations cannot disagree.
    friendly: float = 0.0
    friendly_hits: int = 0
    hostile: float = 0.0
    hits: int = 0
    base: float = 0.0
    self_rule: float = 0.0
    assist: float = 0.0
    received: float = 0.0
    rules: Dict[Tuple, float] = field(default_factory=dict)
    rule_hits: Dict[Tuple, int] = field(default_factory=dict)

    @property
    def total(self) -> float:
        return self.base + self.self_rule + self.assist


@dataclass
class RuleStat:
    key: Tuple
    name: str
    kind: str
    side: str
    owner_key: Optional[int]
    owner_name: str
    origin: str = ""
    hits: int = 0      # unique events carrying this rule (coverage numerator)
    folds: int = 0     # fold instances (a hit can stack the same rule several times)
    damage: float = 0.0
    resolved: bool = True
    beneficiaries: Dict[int, float] = field(default_factory=dict)   # attacker key -> amount
    beneficiary_hits: Dict[int, int] = field(default_factory=dict)  # unique events
    beneficiary_folds: Dict[int, int] = field(default_factory=dict)
    reasons: Dict[str, int] = field(default_factory=dict)


@dataclass
class LinkStat:
    from_key: int
    to_key: int
    amount: float = 0.0
    hits: int = 0      # unique events in which this giver buffed this attacker
    folds: int = 0
    rules: Dict[Tuple, float] = field(default_factory=dict)


@dataclass
class Analysis:
    source: Dict = field(default_factory=dict)
    actors: Dict[int, ActorCredit] = field(default_factory=dict)
    rules: Dict[Tuple, RuleStat] = field(default_factory=dict)
    links: Dict[Tuple, LinkStat] = field(default_factory=dict)
    unattributed: Dict[str, List[float]] = field(default_factory=dict)  # reason -> [amount, hits]
    unattributed_by_kind: Dict[str, float] = field(default_factory=dict)  # fold kind -> amount
    unattributed_by_attacker_kind: Dict[Tuple[int, str], float] = field(default_factory=dict)
    hits_detail: List[HitResult] = field(default_factory=list)   # only filled with keep_hits=True
    diagnostics: Dict = field(default_factory=dict)
    checks: List[str] = field(default_factory=list)
    analyzable: float = 0.0
    unattributed_credit: float = 0.0
    pool_total: float = 0.0
    hits: int = 0
    team: int = 1
    # R61 (schema 1.2): same-team hits (the enemy 回復反転 channel / self-damage) are OUT of the pool.
    # They are counted here so the exclusion stays visible; `same_team_in_pool` records which contract
    # this analysis was run under (True for <=1.1 files, False for 1.2+).
    self_team_hits: int = 0
    self_team_damage: float = 0.0
    same_team_in_pool: bool = True

    def actor_total_credit(self) -> float:
        return sum(a.total for a in self.actors.values())

    def identity_error(self) -> float:
        return self.actor_total_credit() + self.unattributed_credit - self.analyzable
