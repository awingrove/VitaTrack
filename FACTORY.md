# VitaTrack — Agentic Software Factory

How features are built here, and what "done" means. This file is the process contract;
the code-level rules live in `AGENTS.md` (root) and the per-project `AGENTS.md` files.

## Principles

- **Upstream first.** Requirements (story map) and design (ADR / plan) precede code.
- **Slices, not layers.** Each feature is a vertical folder owning its logic end-to-end.
- **Guardrails enforce conformance; humans own design.** Automated checks catch rule
  violations; a developer confirms architecture decisions (ADR / slice boundary).
- **Cheap models are the benchmark, not the rule.** The productivity proof targets
  ≤ $1-per-1M-token models (GLM-5.3-Flash, Hy3 class); the factory runs anything.
- **Measure touch, not time.** Every shard records agent class, human interventions,
  guardrail failures, fix commits, and escaped defects (see `docs/factory/metrics.md`).

## Process

1. **Shard** — confirm the feature's slice (folder) and entry points; update `shards.yaml`
   and `storymap.yaml`. If it needs a new ADR or a new slice boundary, **STOP and get
   developer confirmation** before writing code (design-before-code gate).
2. **Exemplar copy** — start from the proven reference slice (`VitaTrack.Core/Features/Dosing/`)
   via the `new-shard` skill; change names, keep shape.
3. **Tracer bullet** — a green vertical stub (controller → handler → repo → view → e2e)
   before adding depth, so the path works from commit one.
4. **Build** — implement with value objects and typed contracts; no `Dictionary<string,string>`
   view data; handlers only where rules exist.
5. **Verify** — run `verify-shard`: architecture tests → slice unit → slice e2e → tracer
   green → Definition of Done.
6. **Record** — update metrics, log escaped defects, and (for the pilot) write a Lessons
   Learnt section that gates rollout.
7. **Ship** — agent pushes the branch, opens the PR, and reports it ready with green
   checks. **The human approves and merges.** An agent never approves, merges, closes,
   or force-drives a PR into `main` — that approval is the last human review before
   the trust boundary (see Human gates).

## Human gates

Points where the agent MUST stop and hand the decision to a human. Enumerated here
because every one of them has been skipped at least once:

- **Design-before-code gate** — new ADR, slice boundary, or plan-mandated review
  finding that conflicts with the plan's text. The agent surfaces options; the human
  decides (`docs/factory/design-review.md`).
- **PR approve + merge to `main`** — the human's final review. Agents prepare
  (branch, commits, PR, gate report); humans approve and merge.
- **Reviewer findings marked plan-mandated or Important** — adjudication is the
  human's unless the finding is unambiguous mechanical compliance with the plan
  (`docs/factory/verify-shard.md`, ledger gate).

**Gate-rejection rule:** a gate that rejects the work (branch protection, failing
required check, declined push) is the system working, not an obstacle. One rejection
→ fix the named cause. A second rejection of the same gate → **stop and report to
the human** — never hunt for another route around a gate.

## Skills

- `new-shard` — scaffold a shard by copying the exemplar; runs green immediately.
  Committed recipe: [docs/factory/new-shard.md](docs/factory/new-shard.md).
- `verify-shard` — scoped acceptance gate for one shard (Definition of Done).

## Definition of Done (per shard)

- Slice folder owns handlers + repository + models + result records.
- One concept per file; no complete type (including partials) approaches the 300-line split trigger.
- Typed contracts — no `Dictionary<string,string>` view data.
- Unit test + e2e spec green; story-map entry + shard-manifest entry present.
- UI reachable (no orphan page); cross-layer tracer green.
- Docs updated (`AGENTS.md`, ADR if architecture changed); metrics recorded.

## Capability ladder

- **Mechanical** steps (scaffold, rename, format, run gate) — checklist-only, any model.
- **Judgement** steps (design, abstraction, naming) — require human confirmation via the
  design-before-code gate. The factory does not invent architecture; it executes it.

## Quality management (Construx-aligned)

- Metrics: `docs/factory/metrics.md` + `docs/factory/shard-metrics.yaml` (ledger schema
  enforced by `ShardMetricsLedgerTests`). Warn-only until 3 shards have entries, then hard.
- Static analysis: Roslyn analyzers + warnings-as-errors in CI.
- Non-functional requirements: `docs/quality/nfr.md`.
- Technical debt register: `docs/factory/technical-debt.md` (each entry with an interest rate).
- Defect log: escaped defects with injection stage + root cause → post-mortem rule.
