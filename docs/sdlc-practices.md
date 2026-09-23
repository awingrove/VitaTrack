# SDLC Practices in the VitaTrack Software Factory

How features are built, verified, and improved in this repository — the software factory
process, not the application itself. This document is the discussion brief for other teams
and agents; the contracts it describes live in `FACTORY.md`, `AGENTS.md`, `docs/factory/`,
and the architecture test suite, all machine-enforced.

## 1. Principles

Four principles define the lifecycle:

- **Upstream first.** Requirements (story map) and design (ADR / plan) precede code. An
  agent does not start typing until the slice is claimed and the design questions — if any
  — are answered.
- **Slices, not layers.** Each feature is a vertical folder owning its logic end-to-end:
  controller, handlers, repository, models, tests. No file in the repo *is* a feature by
  accident; ownership is declared.
- **Guardrails enforce conformance; humans own design.** Automated checks catch rule
  violations — orphan files, oversized types, unreachable pages. Cross-slice SQL is
  enforced by convention + routed deletes and audited at the design-review gate, not by
  an automated check (deferred as brittle; see `new-shard.md` and pilot lesson #5). A
  developer confirms architecture decisions. The factory executes architecture; it does
  not invent it.
- **Measure touch, not time.** Every shipped shard records agent class, human
  interventions, guardrail failures, fix commits, and escaped defects. Wall-clock hours
  are meaningless across models and sessions; touch and rework are the signal.

## 2. Upstream contracts: the YAML layer

Three YAML files form the machine-checked contract layer. They are not documentation —
architecture tests fail the build when they lie.

**`storymap.yaml`** is the requirements baseline: activities → tasks → stories, each task
carrying a unique id, a status, an `entry_point` describing how a user reaches it from
inside the running app, and explicit test references (`unit: TestClass.Method`,
`e2e: spec::title fragment`). `StoryMapConsistencyTests` resolves every reference against
real test sources — a story claiming coverage it doesn't have, or an E2E spec nobody
references, fails CI. Entry points pair with `UiReachabilityTests`, which scans views, JS,
and redirects for inbound links: no orphan pages, ever.

**`shards.yaml`** is the ownership manifest: each feature slice lists its controller, core
files, views, JS, unit tests, and E2E specs, plus a minimal allowlist for cross-cutting
code. `ShardOwnershipTests` enforces that every feature file belongs to exactly one slice
(or the allowlist), every declared path resolves, there are no double-claims, and slice
ids interlock with story-map task ids in both directions. The manifest is the map an agent
reads before touching code — and the thing that cannot drift without turning red.

**`docs/factory/shard-metrics.yaml`** is the intervention ledger, one entry per shipped
shard (schema enforced by `ShardMetricsLedgerTests`; see §6).

Together these make the upstream state *checkable*: a plan that contradicts the repo, or a
claim of coverage without a test, is a build failure rather than a stale document.

## 3. The shard process

Feature work runs as six stages, codified in `FACTORY.md` and `docs/factory/new-shard.md`:

1. **Shard** — confirm the feature's slice and entry points; update `shards.yaml` and
   `storymap.yaml`. If it needs a new ADR or a new slice boundary, **STOP and get
   developer confirmation** before writing code (the design-before-code gate).
2. **Exemplar copy** — start from the proven reference slice
   (`VitaTrack.Core/Features/Dosing/`) via the `new-shard` recipe; change names, keep
   shape. Scaffolding by imitation, not invention.
3. **Tracer bullet** — a green *vertical* stub before depth: controller → handler →
   repository → view → one E2E spec that clicks an in-app link. The whole pipe works from
   commit one; nothing is stubbed. A red tracer means the boundary or entry point is
   wrong — stop, don't patch forward (`docs/factory/tracer-bullet.md`).
4. **Build** — add depth: request DTOs (never entity binding from forms), one handler per
   business rule, value objects instead of bare primitives for domain amounts, result
   records instead of exceptions for control flow.
5. **Verify** — run the `verify-shard` gate: format → build → architecture tests → slice
   unit tests → slice e2E → tracer green → Definition of Done. A red gate means stop and
   fix the slice; widening the change to "fix forward" is forbidden.
6. **Record** — update the metrics ledger, log any escaped defect, and (for pilot slices)
   write Lessons Learnt that gates rollout of the recipe to further slices.

The per-shard **Definition of Done** is explicit: slice owns its artifacts, no complete
type (partials included) approaches the 300-line split trigger, typed contracts only (no
`Dictionary<string,string>` view data), unit + E2E green, story-map and shard-manifest
entries present, UI reachable, docs updated, metrics recorded.

## 4. Machine verification

Ten architecture test classes (`VitaTrack.ArchitectureTests`) enforce what csproj and code
review cannot express: layer dependencies (Web and Core free of `Dapper`/`System.Data`
where forbidden), no EF Core anywhere, repository naming, type size including partials,
controller hygiene (no `catch (Exception)`), shard ownership, story-map consistency, UI
reachability, metrics-ledger schema, and ecosystem guardrails.

CI (`.github/workflows/ci.yml`) runs the same chain every push and PR: design-system lint
(`DESIGN.md`) → format verification → Release build → unit + architecture tests → coverage
ratchet (gated at 65%, expected to climb) → Playwright E2E against the real app. The
pre-commit hook runs format plus architecture tests locally, so manifest surgery lands in
the same commit that moves the files. The gate a developer runs locally is the gate CI
runs — there is one definition of green.

## 5. The human/machine split

The factory distinguishes a **capability ladder**: mechanical steps (scaffold, rename,
format, run the gate) are checklist-only and cheap-model-safe; judgement steps (design,
abstraction, naming, boundaries) require human confirmation. Guardrails can tell you a
type is 313 lines; they cannot tell you whether a slice boundary is *right*.

That boundary is the design-review gate (`docs/factory/design-review.md`): when a shard
needs an ADR, an ambiguous slice boundary, or a schema migration carrying business
meaning, the agent stops and prompts the developer with options — never invents
architecture. The checklist requires a decision statement, at least two options with
tradeoffs, current-state facts verified against the manifests *at briefing time* (not
recalled), blast radius, migration/rollback, and a named reviewer's sign-off. Unsigned =
not merged. Humans own design; machines own construction — the rare, cheap, high-leverage
checkpoint that automated conformance can never replace.

## 6. Closed-loop quality

Measurement closes the loop. `docs/factory/shard-metrics.yaml` records, per shard: agent
class (`frontier` / `cheap` / `human`), human interventions (the headline number — zero
means fully autonomous), guardrail failures (red gate cycles), fix commits after the first
"done" claim (verification honesty), and escaped defects. The gate is ratcheted: warn-only
until three shards have entries, then a shipped slice without a ledger entry fails
`verify-shard`, and interventions above the target (starting at ≤ 1 per shard) fail review
— tightening as the recipe improves. Rising interventions mean slices are too large or
under-specified: fix the recipe, not the agent.

Escaped defects feed a defect log (`docs/factory/technical-debt.md`) recording injection
stage and root cause, and a **post-mortem rule**: when a bug reveals a systemic gap,
updating the relevant `AGENTS.md` / recipe / checklist is part of the fix, same commit.
Two worked examples show the loop turning:

- **DL-001** — `Money +` silently kept the left operand's currency. Unit tests and CI both
  passed: the tests were written by the same agent that wrote the defect, encoding the bug
  as expected behavior. Caught by human review. Systemic gap closed in `new-shard.md`:
  value objects must fail loudly on invalid combinations, and tests must cover error edges.
- **DL-002** — a slice briefing stated current-state facts from memory and misordered
  steps against the pre-commit gate. Caught by the executing cheap-model session, which
  logged deviations instead of silently complying. Systemic gap closed in
  `design-review.md`: verify claimed facts against the manifest and dry-run each step
  against the guardrail that gates it.

Technical debt lives in the same register with an explicit **interest rate** — what each
entry costs per change — so paydown is prioritized by cost, not vibes. The companion NFR
baseline is `docs/quality/nfr.md`.

## 7. What to steal

The transferable shape: declare requirements and ownership as YAML that tests can fail;
build vertical tracer bullets before depth; put one scannable gate between "done" and
merged; split mechanical from judgement work so agents run free where rules exist and stop
where design begins; and measure touch plus escaped defects, feeding every failure back
into the recipe. Documents describe the process; tests enforce it; the ledger proves it.
