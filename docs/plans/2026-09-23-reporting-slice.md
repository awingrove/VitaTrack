# Reporting (RP) slice conversion — task briefing for the executing agent

> **Purpose:** this is the complete, self-contained brief for the agent session that
> converts the Reporting slice per the factory-v3 rollout backlog. The design decisions
> below are PRE-DECIDED and signed off by the developer (launching a session against
> this doc is the sign-off). The executing agent does mechanical work and keeps gates
> green; it does not invent architecture.
>
> **Target agent class:** cheap (≤ $1/1M tokens, e.g. MiMo-2.6-Flash). Third ledger
> entry — after this, a missing entry fails the `verify-shard` gate (ledger ratchet).
> Record the entry honestly; a stopped-and-asked session with truthful numbers is a
> better outcome than a green one with fabricated numbers.

## Step 0 — read these, in order, before touching anything

1. `AGENTS.md` (repo root) — house rules, FK delete order, commit conventions
2. `docs/adr/0006-vertical-slice-architecture.md` — the slice model + cross-slice invariant
3. `docs/factory/new-shard.md` — the recipe being followed (incl. hard invariants)
4. `docs/factory/verify-shard.md` — the acceptance gate to run
5. `VitaTrack.Core/Features/Dosing/` + `VitaTrack.Core/Features/Nutrients/` — exemplar
   slices (copy this shape)
6. `shards.yaml` + `storymap.yaml` — the ownership index to edit
7. `docs/factory/technical-debt.md` — TD-001 + TD-004, which this slice pays down

Branch: create `feature/reporting-slice` from `feature/factory-v3-vertical-slices`
(the factory branch; not yet merged to `main`). Never commit with `--no-verify`.

## Pre-decided design (do not relitigate; escalate if you believe it is wrong)

**Slice:** id `RP` (already exists in `shards.yaml`), name "Reports (nutrient + cost)".
The slice's files move from `VitaTrack.Core/Services` + `VitaTrack.Core/Models` into
`VitaTrack.Core/Features/Reporting/`, and the report contracts become typed records
passed straight to the views (no ViewData JSON round-trip).

**File moves** (git mv, then namespace `VitaTrack.Core.Features.Reporting`):
- `VitaTrack.Core/Services/IReportingService.cs` → `VitaTrack.Core/Features/Reporting/`
- `VitaTrack.Core/Services/ReportingService.cs`  → `VitaTrack.Core/Features/Reporting/`

**Result.cs dissolution (TD-001 paydown, one file per concept)** —
`VitaTrack.Core/Models/Result.cs` currently holds only report records. Move each to
its own file under `VitaTrack.Core/Features/Reporting/`, then DELETE `Result.cs`:
- `MemberCostRow.cs`
- `SupplementCostRow.cs`
- `NutrientContributionRow.cs`
- `NutrientReportData.cs`
- `CostReportData.cs`

**Typed contracts (TD-004 paydown) — the report records are reshaped:**

```csharp
public record NutrientUnitRow(string NutrientName, string Units);

public record NutrientTotalRow(string NutrientName, string Amount);

public record MemberNutrientTotals(string MemberName, IReadOnlyList<NutrientTotalRow> Totals);

public record NutrientContributionsCell(string NutrientName, IReadOnlyList<NutrientContributionRow> Contributions);

public record MemberNutrientContributions(string MemberName, IReadOnlyList<NutrientContributionsCell> ByNutrient);
```

`NutrientReportData` becomes:

```csharp
public record NutrientReportData(
    DateTime ReportDate,
    IReadOnlyList<NutrientUnitRow> Units,
    Money TotalCost,
    IReadOnlyList<MemberNutrientTotals> MemberTotals,
    IReadOnlyList<MemberNutrientContributions> MemberContributions,
    IReadOnlyList<Supplement> Supplements,
    IReadOnlyDictionary<int, Money> SupplementMonthlyCosts);
```

- The old `MemberNames: IReadOnlyList<string>` list is REPLACED by `MemberName` on the
  per-member records. Semantics preserved: today every member that has any active dose
  appears once in `MemberNames` and once per `MemberData` entry, in the same order
  (`memberTotals` iteration order) — the new shape keeps exactly that pairing. Members
  with doses but zero nutrient totals still appear (empty `Totals`), as today.
- `Amount` in `NutrientTotalRow` stays a pre-formatted string (`"0.##"`) — same rule as
  today (`memberData` values). `NutrientContributionRow` (decimal Amount etc.) is
  unchanged.
- `Units` semantics preserved: one row per nutrient name, `Units` is the comma-joined
  ordered unit symbols (`"IU, µg"`), exactly as today. A nutrient whose rows have no
  defined unit gets NO row (today: absent from the dictionary).

**Controller + views — kill the ViewData JSON round-trip:**
- `ReportingController.NutrientReport`: `return View(data)` with
  `@model NutrientReportData`. Remove every `ViewData[...] = JsonSerializer.Serialize(...)`
  line and the `System.Text.Json` import.
- `ReportingController.CostReport`: `return View(data)` with `@model CostReportData`;
  remove the projected anonymous-object ViewData rows.
- `Views/Reporting/NutrientReport.cshtml`: rework to consume the typed model
  (`MemberTotals`, `MemberContributions`, `Units` as lists of records — iterate with
  `Where`/lookups instead of dictionary TryGetValue; keep building `allNutrients` as
  the sorted union). PRESERVE EXACTLY: the `bd-@(nutrientRow)-@(m)` collapse element
  ids and their ordering (nutrientRow increments per nutrient row, m = member index in
  report order), the expandable-button markup, the chevron SVGs, the contribution
  detail rows (editor links when `SupplementNutrientId` is set, `×(Multiplier)` suffix),
  and the "Supplements in Report" table (now rendered from
  `Model.Supplements` + `Model.SupplementMonthlyCosts` — the anonymous-object cast goes
  away). Keep `report-toggle.js` script tag as-is.
- `Views/Reporting/CostReport.cshtml`: consume `@model CostReportData` (rows and
  totals come from the model). Output markup unchanged.
- **Behavior invariant: the rendered HTML of both report pages must be identical**
  (except the source of truth moving from ViewData to the model). The three e2e specs
  `nutrient-report.spec.js`, `cost-report.spec.js`, `daily-loop.spec.js` are the proof —
  they must pass UNCHANGED. If you believe an e2e spec must be edited, that is a
  behavior change — escalate.

**Visitor over the blend tree (pre-decided in the rollout backlog):**
Today `GetNutrientReportDataAsync` iterates each supplement's nutrient rows flat. The
rule "every row with a defined dosage contributes under its own GenericName, blends
included" lives implicitly in that loop. Extract it into an explicit visitor so the
blend-tree traversal is named and one future place for blend aggregation policy:

- New file `VitaTrack.Core/Features/Reporting/NutrientAggregationVisitor.cs`:
  a small sealed class. The traversal visits each `SupplementNutrient` row exactly
  once — root rows (`ParentNutrientId is null`) then their children — driven by the
  existing flat list from `ISupplementNutrientRepository.GetBySupplementIdAsync`.
  The visitor carries the unit-collection and amount-accumulation logic currently
  inline in the loop (the `nutrientUnits`, `memberTotals`, `memberContributions`
  updates). Output must be IDENTICAL to today's flat loop — same rows, same order,
  same contribution buckets (test `NutrientReport_Contributions_SkipZeroDosageNutrients`
  is the blend-edge proof). No new interfaces, no pattern ceremony beyond this one
  sealed class.

**Unit-test contract updates (explicitly authorized — preserve semantics, not syntax):**
`ReportingServiceTests.cs` asserts on the old Dictionary contracts. Rewrite those
assertions against the new typed records with the SAME semantics, e.g.:
- `data.MemberData.Single()["Vitamin C"]` →
  `data.MemberTotals.Single().Totals.Single(t => t.NutrientName == "Vitamin C").Amount`
- `data.MemberContributions.Single()["Vitamin C"]` → the cell found by `NutrientName`
- `data.Units.TryGetValue(...)` → a `NutrientUnitRow` lookup
Do NOT change what is being proven (multipliers, hidden links, merged units, expired
doses, formatting). Test method names stay. If a test's meaning would change, escalate.

**storymap.yaml:** references to `ReportingServiceTests.<method>` must keep resolving
(StoryMapConsistencyTests checks). Method names stay, so no storymap surgery expected.

**shards.yaml:** update the RP `core` entries to the new `Features/Reporting/` paths
(keep `ReportingServiceTests.cs` under `unit_tests`; add the new record files). The
`ReportingController` / views / js / e2e entries stay put. Run `ShardOwnershipTests`
as proof.

**OUT OF SCOPE (explicitly deferred):** anything in `SupplementCostRow`'s consumers
beyond the view swap; changing report formatting rules; touching
`Supplement`/`SupplementNutrient` models; LLM or CSV slices; `Dictionary` contracts in
other slices.

## Execution order (commit per green step; tick as you go)

- [ ] 1. Branch + read docs. Commit nothing yet.
- [ ] 2. git mv the two service files + dissolve `Result.cs` into five record files;
        namespace + using updates across the solution; build + full `dotnet test` green.
        Commit (`refactor:`).
- [ ] 3. Reshape the report records (typed contracts) + `ReportingService` +
        `ReportingServiceTests` updates. Gates green. Commit (`refactor:`).
- [ ] 4. `NutrientAggregationVisitor` extraction inside `ReportingService` — behavior
        identical. Gates green. Commit (`refactor:`).
- [ ] 5. Controller + both views consume the model directly (no ViewData JSON). Full
        unit suite + the three report e2e specs green. Commit (`feat:`).
- [ ] 6. `shards.yaml` RP path surgery. Arch tests green (ShardOwnership is the proof).
        Commit (`docs:`).
- [ ] 7. Ledger entry + full final gate (incl. FULL e2e suite once) + push.

## The gate (run after every step; all must be green)

    dotnet format VitaTrack.sln --verify-no-changes
    dotnet build VitaTrack.sln -c Release
    dotnet test VitaTrack.sln -c Release          # 13 arch + ~211 unit
    cd e2e-tests/playwright && npx playwright test \
        tests/nutrient-report.spec.js tests/cost-report.spec.js tests/daily-loop.spec.js

## Hard rules

- NEVER weaken a guardrail, test, or threshold to make it pass. If
  ShardOwnershipTests/FileSizeTests/StoryMapConsistencyTests fail, fix the slice or
  the manifest — not the test. Editing a test's *assertions* is forbidden except for
  the `ReportingServiceTests` contract updates this briefing explicitly authorizes
  (semantics preserved).
- NEVER `git commit --no-verify`. NEVER push to `main`. No PR — push the branch only.
- No hardcoded DB ids in e2e; dynamic assertions only (parallel workers share one DB).
- Keep every complete type under 300 lines (partials count).
- No new abstractions, interfaces, or config beyond the pre-decided list.
- Do NOT run the full e2e suite more than the final once (workers share one server;
  the three report specs are your per-step proof).

## Escalation (STOP and ask the human; record it later as an intervention)

- A guardrail test seems wrong, not just failing.
- A behavior change beyond "identical output, new home" appears necessary (including
  any e2e spec edit).
- Rendered HTML of either report page changes (beyond trivial whitespace).
- Any decision that would need a new ADR or a different slice boundary.
- You are stuck after two failed attempts at the same fix.

When you stop: state what you were doing, the two attempts, and the exact question.

## Done = all of these

- Gates green (format, build, 13 arch incl. ShardOwnership + StoryMapConsistency +
  CrossSliceSqlTests, full unit suite, the three RP e2e specs — plus the FULL e2e
  suite once at the end).
- `shards.yaml` updated; no orphan, no double-claim. `Result.cs` gone.
- Ledger entry in `docs/factory/shard-metrics.yaml` (third entry):

      - id: RP
        name: Reports (nutrient + cost)
        agent: cheap
        human_interventions: <actual count of stops>
        guardrail_failures: <count of red gate runs before final green>
        fix_commits: <commits after your first "done" claim>
        defects_escaped: 0

  Derive the counts from your actual session history, do not estimate them. Check
  `docs/factory/shard-metrics.yaml` for the exact field names/format of existing
  entries and match it.
- Branch pushed. Report: commits, final gate output, interventions (with reasons),
  and anything you noticed but did not touch (goes to the debt register).
