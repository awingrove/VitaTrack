# Nutrients (NT) slice conversion — task briefing for the executing agent

> **Purpose:** this is the complete, self-contained brief for the agent session that
> carves the Nutrients slice out of Supplements. The design decisions below are
> PRE-DECIDED and signed off by the developer (launching a session against this doc is
> the sign-off). The executing agent does mechanical work and keeps gates green; it does
> not invent architecture.
>
> **Target agent class:** cheap (≤ $1/1M tokens, e.g. GLM-5.3-Flash). This is the
> factory's first productivity-proof data point — see `docs/factory/metrics.md`.
> Record the ledger entry honestly; a stopped-and-asked session with truthful numbers is
> a better outcome than a green one with fabricated numbers.

## Step 0 — read these, in order, before touching anything

1. `AGENTS.md` (repo root) — house rules, FK delete order, commit conventions
2. `docs/adr/0006-vertical-slice-architecture.md` — the slice model + cross-slice invariant
3. `docs/factory/new-shard.md` — the recipe being followed (incl. hard invariants)
4. `docs/factory/verify-shard.md` — the acceptance gate to run
5. `VitaTrack.Core/Features/Dosing/` — the exemplar slice (copy this shape)
6. `shards.yaml` + `storymap.yaml` — the ownership index to edit

Branch: create `feature/nutrients-slice` from `feature/factory-v3-vertical-slices`
(the factory branch; not yet merged). Never commit with `--no-verify`.

## Pre-decided design (do not relitigate; escalate if you believe it is wrong)

**Slice:** id `NT`, name "Nutrients & Blends". Carved out of MS. MS keeps supplements,
CSV import, and their views/JS; NT owns the nutrient/blend domain.

**File moves** (git mv, then change namespace to `VitaTrack.Core.Features.Nutrients`):
- `VitaTrack.Core/Models/SupplementNutrient.cs`        → `VitaTrack.Core/Features/Nutrients/`
- `VitaTrack.Core/Data/ISupplementNutrientRepository.cs` → `VitaTrack.Core/Features/Nutrients/`
- `VitaTrack.Core/Data/SupplementNutrientRepository.cs`  → `VitaTrack.Core/Features/Nutrients/`
- `VitaTrack.Core/Services/ISupplementNutrientService.cs` → `VitaTrack.Core/Features/Nutrients/`
- `VitaTrack.Core/Services/SupplementNutrientService.cs`  → `VitaTrack.Core/Features/Nutrients/`
- From `VitaTrack.Core/Models/Result.cs`, move `ReplaceNutrientsResult` and
  `NutrientFailure` into new files under `Features/Nutrients/` (partial TD-001 paydown).
  The report records stay in `Result.cs`.

**Ownership moves** (files stay put, claims move in `shards.yaml` from MS to NT):
- controller: `VitaTrack.Web/Controllers/SupplementNutrientController.cs`
- views: `VitaTrack.Web/Views/SupplementNutrient/**`
- js: `wwwroot/js/nutrient-editor.js`, `wwwroot/js/nutrient-form.js`
- unit_tests: `SupplementNutrientControllerTests`, `SupplementNutrientServiceTests`,
  `SupplementNutrientServiceHierarchyTests`, `SupplementNutrientValidationTests`,
  `SupplementNutrientDosageNormalizationTests`, `SupplementNutrientRepositoryTests`,
  `MockSupplementNutrientRepo` (test helper)
- e2e_specs: `supplement-nutrient.spec.js`, `supplement-blends.spec.js`,
  `supplement-delete-cascade.spec.js`, `blend-cascade-delete.spec.js`,
  `nutrient-validation-surfaces.spec.js`
- STAYS in MS: `enrich-without-url-roundtrip.spec.js` (enrichment flow),
  `CsvImportServiceTests`, `SupplementController*Tests`. STAYS in RP:
  `ReportingServiceTests`. STAYS in LLM: `BlendEnrichmentTests`. STAYS in SHELL:
  `daily-loop.spec.js`.

**storymap.yaml:** add NT-prefixed task(s) for nutrients/blends with real `entry_point`s;
keep every existing test reference resolving (StoryMapConsistencyTests will check).

**Cross-slice cascade fix (the invariant's first enforcement):**
`SupplementRepository.DeleteAsync` (both overloads) currently issues raw
`DELETE FROM SupplementNutrients` and `DELETE FROM PrescribedDoses` — cross-slice SQL
against NT and PD. Fix in this change:
- Add `DeleteBySupplementIdsAsync(IEnumerable<int>)` to `ISupplementNutrientRepository`;
  it must delete blend children BEFORE parents (ParentNutrientId app-enforced cascade,
  same order as `DeleteAsync(int)`).
- Add `DeleteBySupplementIdsAsync(IEnumerable<int>)` to `IPrescribedDoseRepository`.
- `SupplementRepository` takes both repos via constructor injection and routes deletes
  through them. Repo-to-repo injection is the accepted pragmatic pattern here.
- Update the cascade-delete unit tests to prove behavior is unchanged. The e2e specs
  `supplement-delete-cascade.spec.js` and `blend-cascade-delete.spec.js` cover this
  behavior end-to-end — they are your proof nothing regressed; run them in the gate.
- Update the FK-cascade bullets in root + `VitaTrack.Core/AGENTS.md` to say deletes are
  routed via the owning slice's repository.

**Value-object adoption (small, real):** add a computed `ParsedDosage` property
(`Dosage` value object from `VitaTrack.Core.Primitives`) on `SupplementNutrient`:
`public Dosage ParsedDosage => Dosage.Parse(Dosage);` — and switch the nutrient loop in
`ReportingService.GetNutrientReportDataAsync` to use it instead of calling
`Dosage.Parse(n.Dosage)`. The `Dosage` string column and Dapper mapping do not change.

**OUT OF SCOPE (explicitly deferred to a later NT task):** Composite pattern for the
blend hierarchy, Strategy for unit normalization, splitting `SupplementController`,
any change to report row types.

## Execution order (commit per green step; tick as you go)

- [ ] 1. Branch + read docs. Commit nothing yet.
- [ ] 2. git mv the five files + the two Result.cs records; namespace + using updates
       across the solution; build + full `dotnet test` green. Commit (`refactor:`).
- [ ] 3. Cascade fix + tests + AGENTS.md FK text. Gates green. Commit (`fix:` or
       `refactor:`).
- [ ] 4. `ParsedDosage` + ReportingService switch. Gates green. Commit (`feat:`).
- [ ] 5. `shards.yaml` + `storymap.yaml` ownership/NT task surgery: add NT-prefixed
       tasks with real `entry_point`s, and re-point the e2e refs that live on MS tasks
       for the specs moving to NT (`supplement-nutrient`, `supplement-blends`,
       `supplement-delete-cascade`, `blend-cascade-delete`,
       `nutrient-validation-surfaces`). Arch tests green (ShardOwnership +
       StoryMapConsistency are the proof). Commit (`docs:` or `test:`).
- [ ] 6. Ledger entry + final gate + push.

## The gate (run after every step; all must be green)

    dotnet format VitaTrack.sln --verify-no-changes
    dotnet build VitaTrack.sln -c Release
    dotnet test VitaTrack.sln -c Release          # 13 arch + ~207 unit
    cd e2e-tests/playwright && npx playwright test \
        tests/supplement-nutrient.spec.js tests/supplement-blends.spec.js \
        tests/supplement-delete-cascade.spec.js tests/blend-cascade-delete.spec.js \
        tests/nutrient-validation-surfaces.spec.js

## Hard rules

- NEVER weaken a guardrail, test, or threshold to make it pass. If
  ShardOwnershipTests/FileSizeTests/StoryMapConsistencyTests fail, fix the slice or
  the manifest — not the test. Editing a test's *assertions* is forbidden; editing
  `using` lines and namespaces after file moves is expected.
- NEVER `git commit --no-verify`. NEVER push to `main`. No PR — push the branch only.
- No hardcoded DB ids in e2e; dynamic assertions only (parallel workers share one DB).
- Keep every complete type under 300 lines (partials count). If a moved type is near
  the limit, extract an existing concept — do not dodge with partials.
- No new abstractions, interfaces, or config beyond the pre-decided list.

## Escalation (STOP and ask the human; record it later as an intervention)

- A guardrail test seems wrong, not just failing.
- A behavior change beyond "identical output, new home" appears necessary.
- storymap surgery needs deleting/rewriting existing stories, not adding NT tasks.
- Any decision that would need a new ADR or a different slice boundary.
- You are stuck after two failed attempts at the same fix.

When you stop: state what you were doing, the two attempts, and the exact question.

## Done = all of these

- Gates green (format, build, 13 arch incl. ShardOwnership + StoryMapConsistency,
  full unit suite, the five NT e2e specs — plus the FULL e2e suite once at the end).
- `shards.yaml`/`storymap.yaml` updated; no orphan, no double-claim.
- Ledger entry in `docs/factory/shard-metrics.yaml`:

      - id: NT
        name: Nutrients & Blends
        agent: cheap
        human_interventions: <actual count of stops>
        guardrail_failures: <count of red gate runs before final green>
        fix_commits: <commits after your first "done" claim>
        defects_escaped: 0

  Derive the counts from your actual session history, do not estimate them.
- Branch pushed. Report: commits, final gate output, interventions (with reasons),
  and anything you noticed but did not touch (goes to the debt register).
