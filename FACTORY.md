# VitaTrack — Agentic Software Factory

How features are built here, and what "done" means. This file is the process contract;
the code-level rules live in `AGENTS.md` (root) and the per-project `AGENTS.md` files.

## Principles

- **Upstream first.** Requirements (story map) and design (ADR / plan) precede code.
- **Slices, not layers.** Each feature is a vertical folder owning its logic end-to-end.
- **Guardrails enforce conformance; humans own design.** Automated checks catch rule
  violations; a developer confirms architecture decisions (ADR / slice boundary).
- **Cheap models suffice** because work is sliced small, contextualized by the shard
  manifest, and self-correcting via loud guardrail failures.
- **Measure.** Every shard records estimate, actuals, and defects (see `docs/factory/metrics.md`).

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

## Skills

- `new-shard` — scaffold a shard by copying the exemplar; runs green immediately.
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

- Metrics: `docs/factory/metrics.md`. Estimation warn-only until 3 shards have actuals,
  then a hard gate.
- Static analysis: Roslyn analyzers + warnings-as-errors in CI.
- Non-functional requirements: `docs/quality/nfr.md`.
- Technical debt register: `docs/technical-debt.md` (each entry with an interest rate).
- Defect log: escaped defects with injection stage + root cause → post-mortem rule.
