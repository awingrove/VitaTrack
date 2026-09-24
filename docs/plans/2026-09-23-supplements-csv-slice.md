# Supplements / CSV (MS) slice conversion — task briefing for the executing agent

> **Purpose:** this is the complete, self-contained brief for the agent session that
> converts the Supplements/CSV slice per the factory-v3 rollout backlog. The design
> decisions below are PRE-DECIDED and signed off by the developer (launching a session
> against this doc is the sign-off). The executing agent does mechanical work and keeps
> gates green; it does not invent architecture.
>
> **Target agent class:** cheap (≤ $1/1M tokens, e.g. MiMo-2.6-Flash). Record the ledger
> entry honestly; a stopped-and-asked session with truthful numbers is a better outcome
> than a green one with fabricated numbers. This is the **fourth** ledger entry — the
> ledger ratchet (a missing entry fails `verify-shard`) is already active.

## Step 0 — read these, in order, before touching anything

1. `AGENTS.md` (repo root) — house rules, FK delete order, commit conventions
2. `docs/adr/0006-vertical-slice-architecture.md` — the slice model + cross-slice invariant
3. `docs/factory/new-shard.md` — the recipe being followed (incl. hard invariants)
4. `docs/factory/verify-shard.md` — the acceptance gate to run
5. `VitaTrack.Core/Features/Dosing/`, `Features/Nutrients/`, `Features/Reporting/` —
   exemplar slices (copy this shape; Reporting is the most recent, note its typed-record
   and one-record-per-file style)
6. `shards.yaml` + `storymap.yaml` — the ownership index to edit
7. `docs/factory/technical-debt.md` — TD-002, which this slice pays down

Branch: create `feature/supplements-csv-slice` from `feature/factory-v3-vertical-slices`.
Never commit with `--no-verify`.

## Pre-decided design (do not relitigate; escalate if you believe it is wrong)

**Slice:** id `MS` (already exists), name stays "Supplements (CRUD, CSV import)". Files
move from `VitaTrack.Core/Data`, `VitaTrack.Core/Services`, `VitaTrack.Core/Models` into
`VitaTrack.Core/Features/Supplements/`; the controller splits per TD-002; CSV row
validation becomes an explicit Chain of Responsibility.

**File moves** (git mv, then namespace `VitaTrack.Core.Features.Supplements`):
- `VitaTrack.Core/Data/ISupplementRepository.cs` → `VitaTrack.Core/Features/Supplements/`
- `VitaTrack.Core/Data/SupplementRepository.cs`  → `VitaTrack.Core/Features/Supplements/`
- `VitaTrack.Core/Services/ICsvImportService.cs` → `VitaTrack.Core/Features/Supplements/`
- `VitaTrack.Core/Services/CsvImportService.cs`  → `VitaTrack.Core/Features/Supplements/`
- `VitaTrack.Core/Models/Supplement.cs`          → `VitaTrack.Core/Features/Supplements/`
- `VitaTrack.Core/Models/CsvSupplementRow.cs`    → `VitaTrack.Core/Features/Supplements/`
- `VitaTrack.Core/Models/CsvImportReport.cs`     → `VitaTrack.Core/Features/Supplements/`
  (contains `CsvImportReport`, `CsvImportSuccess`, `CsvImportFailure` — split into three
  files if the complete type would otherwise bundle unrelated records; three small
  records in one report file is acceptable, one file per concept preferred if trivial)

`using VitaTrack.Core.Models;` lines across the solution stay for the remaining Models
(`FamilyMember`, `LlmResult`); files that use `Supplement` etc. get the new `using`
added. `Views/_ViewImports.cshtml` keeps `@using VitaTrack.Core.Models` AND gains
`@using VitaTrack.Core.Features.Supplements` so Razor views compile unchanged where
possible — add it only if views reference the moved types by bare name (they do:
`@model Supplement`).

**Chain of Responsibility for CSV row validation** — replaces the if-chain in
`CsvImportService.ParseRow` (currently one 56-line method). Pre-decided shape:

- `VitaTrack.Core/Features/Supplements/ICsvRowRule.cs`:

      internal interface ICsvRowRule
      {
          // Applies one validation/parsing rule to the row context. Returns null to
          // continue to the next rule; a non-null CsvParseError stops the chain.
          CsvParseError? Apply(CsvRowContext context);
      }

- `VitaTrack.Core/Features/Supplements/CsvRowContext.cs`: mutable context holding
  `LineNumber`, the six raw trimmed fields (`Name`, `Brand`, `DailyDose`,
  `ManufacturerUrl`, `CostText`, `ServingsText`), the parsed results
  (`Cost`, `ServingsPerBottle` — `decimal?` each), and `Error` is NOT stored here
  (the return value carries it).
- One sealed rule class per file, in the same directory, in this chain order:
  1. `CsvRequiredFieldsRule.cs` — Name/Brand/DailyDose non-empty
     (`"Missing required field: X"`).
  2. `CsvLengthLimitsRule.cs` — Name/Brand/DailyDose ≤ 200 chars, ManufacturerUrl ≤ 500
     (`"X exceeds N characters"`). Move the four `Max*Length` constants here.
  3. `CsvCostRule.cs` — parses `CostText` with `NumberStyles.Float` +
     invariant culture; empty → null; non-positive → error; unparsable → error.
  4. `CsvServingsRule.cs` — same for `ServingsText` / `ServingsPerBottle`.
- Chain output: `CsvImportService` builds the row from the context after the chain
  passes (blank `ManufacturerUrl` → null, exactly as today).
- `CsvImportService` composes the chain once (an `ICsvRowRule[]` field, built in the
  constructor); `ParseRow` shrinks to: build context → run chain → first error wins →
  build row. Behavior must be IDENTICAL — same error messages, same order of checks,
  same `CsvParseError` row numbers. `CsvImportServiceTests` is the proof.
- `ParseCsvLine` stays `internal static` on `CsvImportService` (tests call it directly).
- No new interfaces beyond this one; no rule registration/config — the array is
  constructed inline. This is the pattern the code demands (per-row rules grow);
  nothing more.

**Handler extraction — `ImportSupplementsHandler`:** `ImportCsv` in
`SupplementController` currently carries the row→supplement→enrich→persist loop with
its rules (skip-enrich when no URL; nutrient count from persist result). That is a
rule-carrying operation: extract it into
`VitaTrack.Core/Features/Supplements/ImportSupplementsHandler.cs`:

      public class ImportSupplementsHandler(
          ISupplementRepository supplementRepo,
          ISupplementNutrientService nutrientService,
          ILlmService llmService)
      {
          public async Task<CsvImportReport> ImportAsync(CsvParseResult parseResult);
      }

- Move the loop verbatim: per row — if `ManufacturerUrl` non-blank → enrich via
  `ILlmService.EnrichSupplementAsync`, apply enrichment (NutritionJson/SwapSuggestion),
  add via repo, persist nutrients via `ISupplementNutrientService.AddAsync` and record
  the saved count; else add without enrichment. Successes/failures lists and the final
  `CsvImportReport` built exactly as today. Zero behavior change.
- The controller keeps: file-null/extension guards + calling `ICsvImportService.ParseAsync`
  + the parse-error short-circuit (`Errors.Count > 0 && Rows.Count == 0`) — those are
  HTTP-layer concerns — then calls the handler with the parse result and returns the
  report partial.
- Controller keeps its private `ApplyEnrichment` for its own enrich flows; the handler
  gets its own copy (small, local to the slice — deliberate duplication of a 3-line
  helper, not verbatim duplication of a logic block; note it in your report).

**Controller split (TD-002 paydown):**
- New `VitaTrack.Web/Controllers/SupplementImportController.cs`: single action
  `ImportCsv` (moved verbatim minus the loop). Constructor takes `ICsvImportService` +
  `ImportSupplementsHandler`. Route prefix becomes `/SupplementImport/ImportCsv`.
- `VitaTrack.Web/Views/Supplement/_ImportModal.cshtml`: `hx-post="/Supplement/ImportCsv"`
  → `hx-post="/SupplementImport/ImportCsv"`. No other view or JS references ImportCsv
  (verify with grep and fix any stragglers).
- `SupplementController` (main + `SupplementController.Editor.cs` partial) keeps
  everything else. After the split the complete type must be well under 300 lines.
- `VitaTrack.ArchitectureTests/FileSizeTests.cs`: remove
  `"VitaTrack.Web.Controllers.SupplementController"` from `KnownTypeDebt` and, if
  nothing else remains in that set, delete the set and its comment block — the debt is
  paid. Do NOT weaken any other rule.
- `SupplementControllerImportCsvTests.cs`: instantiate `SupplementImportController`
  (constructor shape changes accordingly). Test method names and assertions stay.
- `shards.yaml` MS `controller` entry gains `SupplementImportController.cs`; test
  reference `SupplementControllerImportCsvTests` stays resolving (storymap unaffected).
- A comment referencing the controller must show real working URLs (root `AGENTS.md`
  route rule) — update comments on the moved/changed actions.

**OUT OF SCOPE (explicitly deferred):** extracting handlers for Create/Enrich/Edit
flows (UI-heavy review orchestration — revisit only if the controller grows again);
changing CSV parsing/quote handling; changing enrichment semantics; splitting
`LlmService`; `Family`/`LLM` slices; any report changes.

## Execution order (commit per green step; tick as you go)

- [ ] 1. Branch + read docs. Commit nothing yet.
- [ ] 2. git mv the seven files; namespace + using updates across the solution
        (including `_ViewImports.cshtml`); build + full `dotnet test` green.
        Commit (`refactor:`).
- [ ] 3. CoR row validation: interface, context, four rules, service rewiring.
        `CsvImportServiceTests` green UNCHANGED (it is the behavior proof — do not edit
        its assertions). If `CsvImportServiceTests` would exceed the 300-line type-size
        trigger, split NEW tests into a new file, never edit existing assertions.
        Commit (`refactor:`).
- [ ] 4. `ImportSupplementsHandler` extraction + controller swap. Gates green.
        Commit (`refactor:`).
- [ ] 5. Controller split: `SupplementImportController`, `_ImportModal.cshtml` hx-post
        update, `KnownTypeDebt` removal, import test constructor updates. Full unit
        suite + the four MS e2e specs (`csv-import.spec.js`, `supplement-crud.spec.js`,
        `supplement-llm-flow.spec.js`, `enrich-without-url-roundtrip.spec.js`) green.
        Commit (`feat:`).
- [ ] 6. `shards.yaml` MS path surgery (core, controller, unit_tests unchanged names).
        Arch tests green (ShardOwnership is the proof). Commit (`docs:`).
- [ ] 7. Ledger entry + full final gate (incl. FULL e2e suite once) + push.

## The gate (run after every step; all must be green)

    dotnet format VitaTrack.sln --verify-no-changes
    dotnet build VitaTrack.sln -c Release
    dotnet test VitaTrack.sln -c Release          # 15 arch + ~210+ unit
    cd e2e-tests/playwright && npx playwright test \
        tests/csv-import.spec.js tests/supplement-crud.spec.js \
        tests/supplement-llm-flow.spec.js tests/enrich-without-url-roundtrip.spec.js

## Hard rules

- NEVER weaken a guardrail, test, or threshold to make it pass. If
  ShardOwnershipTests/FileSizeTests/StoryMapConsistencyTests fail, fix the slice or
  the manifest — not the test. Editing a test's *assertions* is forbidden except:
  `SupplementControllerImportCsvTests` constructor updates are authorized (same
  assertions, new controller type); `CsvImportServiceTests` must NOT be touched at all.
- NEVER `git commit --no-verify`. NEVER push to `main`. No PR — push the branch only.
- No hardcoded DB ids in e2e; dynamic assertions only (parallel workers share one DB).
- Keep every complete type under 300 lines (partials count). Watch
  `CsvImportServiceTests` (exactly 300 today) — do not grow it.
- No new abstractions, interfaces, or config beyond the pre-decided list.
- Do NOT run the full e2e suite more than the final once.

## Escalation (STOP and ask the human; record it later as an intervention)

- A guardrail test seems wrong, not just failing.
- A behavior change beyond "identical output, new home" appears necessary (including
  any e2e spec edit or URL beyond the pre-decided ImportCsv route change).
- `Supplement.cs` namespace move turns out to break something the design didn't foresee
  (e.g. a serializer, reflection, or CSV header contract relying on the old namespace).
- Any decision that would need a new ADR or a different slice boundary.
- You are stuck after two failed attempts at the same fix.

When you stop: state what you were doing, the two attempts, and the exact question.

## Done = all of these

- Gates green (format, build, 15 arch incl. ShardOwnership + StoryMapConsistency +
  CrossSliceSqlTests, full unit suite, the four MS e2e specs — plus the FULL e2e suite
  once at the end).
- `shards.yaml` updated; no orphan, no double-claim; `KnownTypeDebt` gone.
- Ledger entry in `docs/factory/shard-metrics.yaml` (fourth entry):

      - id: MS
        name: Supplements (CRUD, CSV import)
        agent: cheap
        human_interventions: <actual count of stops>
        guardrail_failures: <count of red gate runs before final green>
        fix_commits: <commits after your first "done" claim>
        defects_escaped: 0

  Plus the usage fields documented in `metrics.md` (`agent_model`, `tokens_input`,
  `tokens_output`, `tokens_reasoning`, `tokens_cache_read`, `cost_usd`) extracted from
  your own session — `opencode session list` → `opencode export <sessionID>`, use the
  session-level `info` block; record honest numbers, `0` on free tier is real.
  Derive the counts from your actual session history, do not estimate them.
- Branch pushed. Report: commits, final gate output, interventions (with reasons),
  and anything you noticed but did not touch (goes to the debt register).
