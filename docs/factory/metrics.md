# Shard Metrics & Intervention Ledger

The factory's claim to prove: an agent, given the shard recipe (`new-shard.md`) and the
guardrails, delivers a correct slice with **minimal human touch**. So we measure touch and
rework — not wall-clock hours, which are meaningless across models and sessions.

## Target model class

The productivity proof targets **≤ $1 per 1M token models** (GLM-5.3-Flash, Hy3 class).
This is a benchmark target, not a rule: the factory does not refuse to run other models.
A shard only counts toward the proof when its ledger entry records `agent` in that class.

## What we measure (per shard)

| Field | Meaning |
|---|---|
| `agent` | Model class that built it (`frontier` / `cheap` / `human`). Only `cheap` entries count toward the productivity proof. |
| `human_interventions` | Every stop that needed a human: design-review prompts, corrections, unblocks, review findings that forced changes. **The headline number.** `0` = fully autonomous. |
| `guardrail_failures` | Red gate cycles (arch/format/build/e2e failures before green). Loud failures are the design working; a high count means the recipe under-contextualizes the shard. |
| `fix_commits` | Commits after the first "done" claim. Measures verification honesty. |
| `defects_escaped` | Defects found after merge (from the defect log in `technical-debt.md`). |
| `cost_usd` (optional) | Token cost of the shard, when the session tooling reports it. Direct productivity-per-dollar number. |

## Ledger

Canonical ledger: `docs/factory/shard-metrics.yaml`. One block per shipped shard.
`ShardMetricsLedgerTests` enforces its integrity: ids resolve to real shards in
`shards.yaml`, ids are unique, and every entry carries the required fields.

## Gate

- **Warn-only until three shards** have entries. Warn = reviewer checks the entry exists
  and the numbers are plausible.
- **Hard afterward**: a shipped slice without a ledger entry fails `verify-shard`, and
  `human_interventions` above the ratchet target (start: **≤ 1 per shard**) fails review —
  the target ratchets down as the recipe improves.

## Reading the numbers

- Rising `human_interventions` → slices are too large or under-specified; fix the recipe,
  not the agent.
- High `guardrail_failures` with low `interventions` → guardrails are doing their job
  (self-correction), but consider enriching `new-shard.md` with the recurring failure mode.
- `defects_escaped > 0` → post-mortem per the defect-log rule; the systemic gap updates
  `AGENTS.md` / the recipe in the same change.
