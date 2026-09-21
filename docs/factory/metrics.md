# Shard Metrics & Estimation Gate

Every shard records **estimate**, **actuals**, and **defects** so the factory can measure
whether cheap models, with shard-context, complete deep features without escalation. This
is the Construx gap: a software factory without measurement is undefended against drift.

## Ledger

The canonical ledger is `docs/factory/shard-metrics.yaml`. One block per shipped shard:

```yaml
- id: PD
  name: Prescribed Doses
  estimate_hours: 4
  actual_hours: 3.5
  defects_escaped: 0
  notes: pilot; value objects paid off; handler-per-rule held
```

## Estimation gate

- **Warn-only** until **three** shards have recorded `actual_hours`. The gate is the
  `estimate_hours` field being present and within an order of magnitude of the prior
  median.
- **Hard** afterward: a shard without `estimate_hours`, or whose estimate deviates > 3x
  from the running median for its slice class, fails the verify-shard gate (local + CI).
- The gate is intentionally lightweight (a yaml lint + range check), not a time-tracking
  system — the goal is trend visibility, not surveillance.

## What we measure

- `estimate_hours` — developer's pre-code estimate.
- `actual_hours` — wall-clock from first shard commit to green CI.
- `defects_escaped` — defects found after the shard merged (from the defect log in
  `technical-debt.md`).
- `escalations` — count of times the agent had to stop for human design review
  (`design-review.md`); a rising rate signals slices that are too large or under-specified.

## Current state

As of the Dosing pilot, only one shard has actuals, so the gate remains **warn-only**.
