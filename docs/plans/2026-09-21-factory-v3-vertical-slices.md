# Factory v3 — Vertical Slices, Tracer Bullets, Value Objects

- **Status:** Draft (approved, not started)
- **Date:** 2026-09-21
- **Owner:** Agentic factory
- **Precedes:** rollout to all feature slices

## Context

VitaTrack's architecture was set by ADR-0001 (pragmatic MVC, 3 projects) when the
assumed force was "single human maintainer, no team to enforce layering metrics."
That force is now gone: we run an agent fleet plus a machine-enforced factory
(arch tests, story-map consistency, UI reachability). ADR-0001's *boundary* payoff
(3 projects, csproj-enforced direction, no boilerplate tax) still holds and is
kept. Its *organizing* decision ("business rules live in `Infrastructure/Services`",
"no use-case classes") now hinders clarity:

- Domain logic lives in a project named `Infrastructure` (a naming lie that ages badly).
- Logic bloats: `SupplementController.cs` 263 lines + partial `SupplementController.Editor.cs`;
  `ReportingService.cs` 218 lines.
- No file in the repo *is* a feature — the agent must reassemble a slice from 5 folders.
- `Infrastructure/Models/Result.cs` packs 6 unrelated DTOs in one file.
- `ReportingService.cs:206-208` hand-rolls the "is this dose active today" rule that
  belongs to Dosing; `PrescribedDoseController.cs:40,65` binds the entity directly
  (overposting risk, ArchitectureReview §2.6).

Goal: source code a developer admires — every feature one slice, obvious place to
change, real types not strings/dictionaries, patterns named where earned, docs that
are machine-checked, and a factory rigorous enough that cheap models can build deep
features.

## Locked Decisions

- **No MediatR.** Explicit handler classes injected into controllers. Zero deps, more
  readable, equally SOLID.
- **Pilot on Dosing**; record lessons learnt; gate rollout on them.
- **Value objects: yes.** `DoseMultiplier`, `DosePeriod` (pilot); `Dosage`, `Unit`,
  `Money` (rollout).
- **Rename `VitaTrack.Core` → `VitaTrack.Core`.** "Everything below the web
  surface" — feature slices + persistence + external clients. `Domain` would falsely
  imply no SQL; `Application` would falsely imply no persistence.

## Definition of Done (per shard)

- Slice folder owns handlers + repository + models + result records.
- One concept per file; no complete type (including partials) approaches the 300-line split trigger.
- Typed contracts — no `Dictionary<string,string>` view data.
- Unit test + e2e spec green; story-map entry + shard-manifest entry present.
- UI reachable (no orphan page); cross-layer tracer green.
- Docs updated (`AGENTS.md`, ADR if architecture changed).

## Cross-Slice Invariant (machine-enforced table map; interface-routing is the human gate)

A slice may **read** another slice's data only through that slice's published
repository/query interface. It must **never** issue SQL against another slice's tables.
The table clause is machine-checked; the interface clause is convention + routed
deletes (`AGENTS.md`) + the `design-review.md` human gate.

- [x] **Cross-slice SQL arch test:** shipped 2026-09-23 as `CrossSliceSqlTests` —
      explicit table→slice map (`tables:` per slice in `shards.yaml`), then assert every
      SQL statement found in a slice's core files references only declared tables.
      Only SQL-shaped string literals are scanned (no full inline-SQL parsing — pilot
      lesson #5's brittleness concern). Design note: the original sketch said "only its
      own slice's tables"; display joins in `PrescribedDoseRepository` cannot be routed
      through injected repos without a DI constructor cycle
      (`SupplementRepository → IPrescribedDoseRepository → ISupplementRepository`), so
      cross-slice read dependencies are *declared* in `tables` instead — a visible
      manifest diff for design review. Writes stay routed per `AGENTS.md`. Negative-proof:
      injecting a raw `DELETE FROM PrescribedDoses` into `FamilyRepository` fails the test.

---

## Phase 0 — Rename `Infrastructure` → `Core`

- [ ] Rename project folder + csproj + assembly: `VitaTrack.Core` → `VitaTrack.Core`.
- [ ] Namespace `VitaTrack.Core.*` → `VitaTrack.Core.*` (146 occurrences / 108 files, excluding obj/bin).
- [ ] Update 3 `ProjectReference`s + `VitaTrack.sln:10`.
- [ ] `AddCore` → `AddCore` (7 refs incl. `ServiceCollectionExtensionsTests.cs:39,45,66,87,132,165`).
- [ ] Arch tests with old namespaces: `ServicesLayeringTests.cs:22`, `RepositoryNamingTests`, `EcosystemGuardrailTests`, `FileSizeTests`.
- [ ] Move `VitaTrack.Core/AGENTS.md` → `VitaTrack.Core/AGENTS.md`; rewrite per new structure.
- [ ] Update root `AGENTS.md`, `docs/adr/*`, `docs/ArchitectureReview.md` (docs must not lie, §2.2).
- [ ] Gate: full `dotnet build` + `dotnet test` green. Separate commit.

## Phase 1 — ADR-0006 + FACTORY.md

- [ ] `docs/adr/0006-vertical-slice-architecture.md`: marks ADR-0001 **Superseded for organization only**;
      keeps its 3-project boundary. Defines: slice shape; explicit handlers over MediatR
      (rationale: 7 controllers, zero deps); cross-slice read-only-via-interface invariant;
      handler-per-operation threshold = *extract when the operation carries a rule*, not for CRUD passthrough.
- [ ] `FACTORY.md` at root: the process (shard → exemplar copy → tracer bullet → verify → ship)
      + Definition of Done per shard + capability ladder (mechanical steps cheap-model-safe).
- [ ] Update `docs/adr/README.md` table.
- [ ] Add factory ADRs for: VSA, explicit-handlers-vs-MediatR, metadata shards, tracing.

## Phase 2 — Shard index (metadata, zero runtime risk)

- [ ] `shards.yaml` at root: keyed by story-map task id → controller / slice folder / views / js / unit tests / e2e specs.
- [ ] `VitaTrack.ArchitectureTests/ShardOwnershipTests.cs`:
      - no orphan feature files (allowlist: `HomeController`, `_Layout.cshtml`, `DbInit`, `DosageParser`/`Dosage`, `Program.cs`, `ServiceCollectionExtensions`, `VitaTrackOptions`);
      - every artifact path resolves;
      - no double-claim;
      - shard ↔ story-map id integrity both directions.
- [ ] Populate `shards.yaml` for all current features (MF, MS, PD, RP, LLM, SHELL).
- [ ] Keep `StoryMapConsistencyTests` green (add light cross-reference check only).

## Phase 3 — Dosing pilot (proves the shape)

**Move → `VitaTrack.Core/Features/Dosing/`:**
- [ ] `Models/PrescribedDose.cs` → `Features/Dosing/PrescribedDose.cs`
- [ ] `Data/IPrescribedDoseRepository.cs` → `Features/Dosing/IPrescribedDoseRepository.cs`
- [ ] `Data/PrescribedDoseRepository.cs` → `Features/Dosing/PrescribedDoseRepository.cs`
- [ ] `VitaTrack.Tests/PrescribedDoseRepositoryTests.cs` → `VitaTrack.Tests/Features/Dosing/`

**New in slice:**
- [ ] `DoseMultiplier.cs` — value object: default `1`, range `0.01–1000`, step `0.25`. Server is authority;
      `multiplier-stepper.js` remains client UX only (today `[Range]` + JS step duplicate the rule).
- [ ] `DosePeriod.cs` — `Start`/`End` + `IsActiveOn(DateTime)`. Replaces the inline rule at `ReportingService.cs:206-208`.
- [ ] `CreateDoseRequest.cs` / `EditDoseRequest.cs` — request DTOs; end entity binding at `PrescribedDoseController.cs:40,65`.
- [ ] `PrescribeDoseHandler.cs`, `AmendDoseHandler.cs` — operations that carry rules (defaults + validation).
      Index/Delete stay thin controller→repo. **Whether CRUD-passthrough handlers pay off is a lessons-learnt item.**
- [ ] Controller → thin map request → handler → result → view. Target < 80 lines (from 97).

**Wire up:**
- [ ] `ServiceCollectionExtensions` registration (repo + handlers).
- [ ] `shards.yaml` Dosing entry; story-map `PD-*` artifacts updated; `ReportingService` now uses `dose.IsActiveOn(today)`.
- [ ] Gate: full suite + `prescribed-dose.spec.js` green.

## Phase 4 — Cross-layer tracing (reference = Dosing slice)

- [ ] `VitaTrack.Core/Diagnostics/FeatureActivitySource.cs` — single `ActivitySource("VitaTrack")`, `ActivityListener` enabled Dev/Test only (off in Prod → zero cost).
- [ ] `VitaTrack.Web/Filters/FeatureActivityFilter.cs` (`IAsyncActionFilter`): wraps each action in `Activity` named `VitaTrack.{Controller}.{Action}`, tags `feature` + `shard`, logs start/end + elapsed.
- [ ] Services/repos emit boundary spans (method, elapsed, row count) tagged with `feature`.
- [ ] `X-Trace-Id` response header (root Activity id) for e2e correlation.
- [ ] `e2e-tests/playwright/helpers/screenshot.js`: on failure, capture `X-Trace-Id` + layer-boundary trace excerpt into `error-context.md`.
- [ ] `HomeController.Error` logs feature id + Activity trace (already required by AGENTS.md).
- [ ] Instrument Dosing first as the reference; register filter Dev/Test only in `Program.cs`.

## Phase 5 — `new-shard` skill (built FROM the pilot, not before)

- [ ] `.claude/skills/new-shard/SKILL.md`: copies the proven `Features/Dosing/` exemplar — handler + repo +
      value object + result record + unit test + e2e stub + shard entry + story-map entry — then runs green.
- [ ] Checklist: no-orphan-page, entry point wired, `asp-append-version`, CSP-clean JS, exemplar shape match.

## Phase 6 — `verify-shard` gate + lessons learnt

- [ ] `.claude/skills/verify-shard/SKILL.md`: arch tests → slice unit (`--filter`) → slice e2e (`--grep`) → tracer green → DoD check.
- [ ] `docs/plans/2026-09-20-dosing-slice-pilot.md` with **mandatory Lessons Learnt** section:
      did handler-per-rule hold? did value objects pay? rename fallout? what is the exemplar? → gates rollout.

## Phase 7 — Instrumentation, review & quality management

Closes the gaps a Construx review would raise: the factory is currently a construction
machine with no instruments, and guardrails check conformance but not design correctness.

- [x] **Metrics starter set** — `docs/factory/metrics.md`: one entry per shard recording
      agent class, human interventions, guardrail failures, fix commits, and escaped
      defects. Purpose: prove the factory works, not assert it. (Hours were dropped —
      meaningless across models; touch and rework are the signal.)
- [x] **Ledger gate** — `ShardMetricsLedgerTests` validates `docs/factory/shard-metrics.yaml`
      (entries resolve to real shards, unique ids, required fields). **Warn-only until 3
      shards have entries**; then a missing entry fails `verify-shard` and interventions
      above the ratchet target fail review. The pilot is the first entry (frontier-executed,
      so not a productivity-proof data point).
- [ ] **Design-before-code gate + human review** — when a shard needs an ADR or a new slice
      boundary decision, the agent **STOPS and prompts the developer for confirmation** before
      generating code. Guardrails cannot judge whether a boundary or abstraction is correct;
      this human checkpoint is the stage that does. Rare, cheap, high leverage —
      humans own architecture, machines own construction.
- [ ] **Static analysis** — enable Roslyn analyzers + `TreatWarningsAsErrors` in CI
      (ArchitectureReview §2.4, still open). Cheap defect-removal stage; count the warnings.
- [ ] **Non-functional requirements** — `docs/quality/nfr.md`: performance, accessibility,
      security, operability. Reference the existing CSP/security headers; name the gaps explicitly.
- [ ] **Technical debt register** — `docs/technical-debt.md`: each entry with an "interest rate"
      (what it costs per change). Seed with: `Result.cs` split, `SupplementController` split,
      `DosageParser` → `Dosage`, `Dictionary<string,string>` report contracts.
- [ ] **Human design review checklist** — `docs/factory/design-review.md`: short checklist for
      ADRs and new slice boundaries, **requiring a named architecture reviewer** as owner. A
      checklist with no owner silently never happens.
- [ ] **Defect log** — escaped defects recorded with injection stage + root cause, feeding the
      existing post-mortem rule (systemic gaps update AGENTS.md/ADR in the same change).

---

## Rollout backlog (each slice gated by `verify-shard`)

- [ ] **Nutrients** — `Dosage` + `Unit` value objects; `DosageParser` → `Dosage.Parse`; **Composite** for blend
      hierarchy (`SupplementNutrient.ParentNutrientId`); **Strategy** for unit normalization.
- [ ] **Reporting** — **Visitor** over the blend tree; kill `IReadOnlyList<Dictionary<string,string>>`
      (`Result.cs:23`); `Money` value object.
- [ ] **Supplements / CSV** — **Chain of Responsibility** for row validation; split `SupplementController`
      (263 + 50 partial).
- [ ] **Family**, **LLM** — remaining slices.
- [ ] Split `Result.cs` (6 concepts: `NutrientFailure`, `ReplaceNutrientsResult`, `MemberCostRow`,
      `SupplementCostRow`, `NutrientContributionRow`, report data records) — one file per concept.

## Progress log

- **Money `+` defect fixed** (found in review): mixed-currency addition now throws
  instead of silently keeping the left currency; `default` adopts the other operand's
  currency so accumulators simplify back to `+=`. ReportingService guards collapsed.
- **Metrics redesigned** away from hours: agent class + human interventions + guardrail
  failures + fix commits + escaped defects, with `ShardMetricsLedgerTests` enforcing the
  ledger schema. Productivity proof targets ≤$1/1M-token models (GLM-5.3-Flash / Hy3
  class) — a benchmark, not a rule; only `agent: cheap` entries count toward it.
- **Unit value object shipped** (`VitaTrack.Core/Primitives/Unit.cs`): canonical, alias-safe
  measurement unit (mcg/ug/μg→µg, iu→IU). Adopted in `ReportingService` nutrient-unit
  grouping (replaces `HashSet<string>`). `shards.yaml` allowlists the shared `Primitives/**`
  kernel. Verified: 12 arch + 184 unit + 12 report e2e green.
- **Money + Dosage value objects shipped.** `VitaTrack.Core.Primitives/Money.cs` (amount +
  currency; GBP/USD/EUR symbols, defaults to GBP to match the historical `£` UI) and
  `Dosage.cs` (amount + `Unit`, parsed from `"500 mg"`). `ReportingService` now aggregates
  costs as `Money` and parses nutrients via `Dosage`; report records (`CostReportData`,
  `NutrientReportData`, cost rows) carry `Money`. A `Currency` column (default `'GBP'`) was
  added to `Supplements` via a backward-compatible `DbInit` migration. Views drop the
  hardcoded `£` and render `Money.ToString()`. Verified: 12 arch + 193 unit + 12 report e2e.
- **Design decision (human-review gate exercised):** currency defaulted to **GBP** because the
  existing views hardcoded `£`. Flip to USD/EUR by changing the column default + seed; the
  value object is currency-agnostic. Recorded here rather than blocking, per "go all the way".

## Risks / Guardrails

- Rename is broad but mechanical; one commit + full green gate contains it.
- Shared SQLite tables: cross-slice invariant (read-only-via-interface, no cross-slice SQL) —
  the table clause is machine-checked by `CrossSliceSqlTests` (declared `tables:` map);
  interface routing beyond the map remains convention + design review.
- Value objects touch Dapper column mapping; pilot proves the mapping pattern before rollout.
- No speculative scaffolding: handler-per-rule threshold (Phase 1) prevents ceremony; patterns
  (Composite/Visitor/Strategy/Chain) are applied only where the code already demands them.
