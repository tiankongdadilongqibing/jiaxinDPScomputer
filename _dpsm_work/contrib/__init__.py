# -*- coding: utf-8 -*-
"""Contribution analysis core (Stage B, 2026-10-03).

Modules:
  model.py        data classes (pure)
  loader.py       export JSON -> typed data
  attribution.py  fold -> rule owner (ladder + reason codes)
  aggregate.py    log-share split + actor/rule/link aggregation
  validate.py     identity and coverage checks
  report_text.py  legacy Stage-0 compatible text + full report
  report_json.py  contribution JSON draft (v0.1-draft)
  run.py          CLI entry point
"""
__all__ = ["model", "loader", "attribution", "aggregate", "validate", "report_text", "report_json"]
