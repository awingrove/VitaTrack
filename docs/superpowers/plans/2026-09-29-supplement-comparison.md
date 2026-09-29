# Supplement Comparison Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users select up to 5 supplements from the list and open a read-only comparison grid (supplements as columns, generic nutrients as rows, each cell = specific form + normalized amount per serving).

**Architecture:** Tracer-first (Pragmatic Programmer; FACTORY.md step 3): Task 1 builds a thin but complete vertical stub — repo → pure builder → handler → controller → view → button JS → e2e — so the whole pipe is green at commit one. Later tasks harden edges. Grid builder is a pure function in the Supplements slice; fetching goes through the published `ISupplementNutrientRepository` (cross-slice invariant); selection state lives entirely client-side in a GET URL.

**Tech Stack:** ASP.NET MVC + Razor, Dapper/SQLite, MSTest + Moq, Playwright, Bootstrap 5 (stock only).

**Spec:** `docs/superpowers/specs/2026-09-29-supplement-comparison-design.md` — all decisions (purpose, normalization, matching, ordering, cap, edge cases) live there; this plan argues from it.

## Global Constraints

- Row key = `GenericName.Trim()` compared case-insensitive; label = first-seen spelling (spec "Row matching").
- Amounts = raw stored `Dosage` passed through `Dosage.Normalize(...)` (`VitaTrack.Core/Primitives/Dosage.cs`) — never reimplement unit canonicalization, never convert across unit families.
- Column order = order of ids in the query string; rows alphabetical by matched key (`StringComparer.OrdinalIgnoreCase`); children keep first-seen data order under their parent.
- Min 2 resolved supplements or redirect to `Supplement/Index`; unknown/malformed/duplicate ids silently dropped; server never enforces the 5 cap (client-side only — shareable URLs stay valid past it).
- Column header = 3 lines: `Name`, `Brand`, `DailyDose` (free text, TD-017 — display as-is).
- Stock Bootstrap only, no custom CSS; no inline `<script>`/event handlers (CSP); every `wwwroot` asset reference uses `asp-append-version="true"`.
- Every new file claimed in `shards.yaml` in the same task that creates it (red build otherwise); the e2e spec file is referenced by a `storymap.yaml` story in the same change it is created.
- DESIGN.md amendment ships in the same task as the UI it describes (button intent + grid pattern + selection gating are all real at Task 1's commit).
- Comment claim vocabulary: any `CHECKS:`/`PINS:` comment in tests needs an `Assert.` in its enclosing method; prefer no claim comments.
- 300-line complete-type split trigger; keep every type well under it.
- Each task ends green: `dotnet test` passes and `./format-check.sh` passes before commit (pre-commit hook runs both).

## Review Focus

1. **Junk query strings** (`?ids=abc,-1,,2,,2`) — parse must drop non-ints/negatives/dupes without throwing, and <2 survivors must redirect. Pinned by `SupplementControllerCompareTests.Compare_MalformedAndDuplicateIds_RedirectsWhenFewerThanTwoResolve` and `Compare_JunkQuery_RendersWhenTwoValidRemain` (Task 2).
2. **Unknown/deleted ids mixed with valid ones** (`?ids=1,999999`) — silently skipped; renders when ≥2 remain, redirects when they don't. Pinned by `Compare_UnknownIdsSkipped_RendersWithSurvivors` / `Compare_UnknownIdsOnly_Redirects` (Task 2).
3. **Row merging** — `"Vitamin D3"` vs `"vitamin d3 "` must land on one row with first-seen label; blend children nest under parent, orphans top-level. Pinned by `Build_MergesNamesTrimmedAndCaseInsensitive` and `Build_NestsChildrenUnderParent_OrphansTopLevel` (Task 1 — merging is a tracer assertion, nesting gets its own review attention in the same task).
4. **Client-only cap/min gating** — server renders any ≥2 id list even past 5; list UI blocks the 6th checkbox and disables Compare below 2. Pinned by `Compare_BeyondCapUrl_StillRenders` (Task 2) and `should gate Compare Selected below two and cap at five` (Task 3).
5. **Select-all interplay** — `delete-selected.js` flips `cb.checked` programmatically (no `change` event fires), so Compare's state could go stale after select-all. Pinned by the `#select-all` path inside Task 3's gating e2e test.

---

### Task 1: Tracer bullet — complete vertical stub

One green vertical path: bulk fetch → records → pure builder → handler → `Compare` action → view → list-page control → e2e, plus the same-change claims (`shards.yaml`, `storymap.yaml`, `DESIGN.md`). Layers are bottom-up within the task; the task is done only when the e2e click-through passes.

**Files:**
- Modify: `VitaTrack.Core/Features/Nutrients/ISupplementNutrientRepository.cs`, `VitaTrack.Core/Features/Nutrients/SupplementNutrientRepository.cs`
- Create: `VitaTrack.Core/Features/Supplements/ComparisonCell.cs`, `ComparisonRow.cs`, `ComparisonColumn.cs`, `ComparisonGrid.cs`, `SupplementComparisonBuilder.cs`, `BuildSupplementComparisonHandler.cs`
- Modify: `VitaTrack.Core/ServiceCollectionExtensions.cs`
- Modify: `VitaTrack.Web/Controllers/SupplementController.cs` (ctor + `Compare` action)
- Create: `VitaTrack.Web/Views/Supplement/Compare.cshtml`
- Modify: `VitaTrack.Web/Views/Supplement/Index.cshtml`
- Create: `VitaTrack.Web/wwwroot/js/compare-selected.js`
- Modify: `DESIGN.md`, `shards.yaml`, `storymap.yaml`
- Test: `VitaTrack.Tests/SupplementNutrientRepositoryTests.cs` (extend), `VitaTrack.Tests/SupplementComparisonBuilderTests.cs` (new), `VitaTrack.Tests/BuildSupplementComparisonHandlerTests.cs` (new), `VitaTrack.Tests/SupplementControllerCompareTests.cs` (new), plus ctor call-site fixes in existing `SupplementController*Tests.cs`
- E2E: `e2e-tests/playwright/tests/supplement-comparison.spec.js` (new)

**Interfaces:**
- Produces (consumed by later tasks, exact shapes):

```csharp
// Nutrients repo (Task 1's handler consumes)
Task<IReadOnlyList<SupplementNutrient>> GetBySupplementIdsAsync(IEnumerable<int> supplementIds);

public sealed record ComparisonCell(string SpecificForm, string Dosage);            // Dosage may be "" (blend children)
public sealed record ComparisonRow(string Label, bool IsBlendChild, IReadOnlyList<ComparisonCell?> Cells); // null cell = supplement lacks it
public sealed record ComparisonColumn(int SupplementId, string Name, string Brand, string DailyDose);
public sealed record ComparisonGrid(IReadOnlyList<ComparisonColumn> Columns, IReadOnlyList<ComparisonRow> Rows);

public static class SupplementComparisonBuilder
{
    public static ComparisonGrid Build(
        IReadOnlyList<Supplement> columns,
        IReadOnlyDictionary<int, IReadOnlyList<SupplementNutrient>> nutrientsBySupplementId);
}

public sealed class BuildSupplementComparisonHandler(
    ISupplementRepository supplementRepo,
    ISupplementNutrientRepository nutrientRepo)
{
    // Returns null when fewer than two of orderedIds resolve to a supplement.
    public async Task<ComparisonGrid?> BuildAsync(IReadOnlyList<int> orderedIds);
}

// Controller: GET /Supplement/Compare?ids=1,2,3  (ctor gains BuildSupplementComparisonHandler comparisonHandler)
[HttpGet] public async Task<IActionResult> Compare(string? ids)
```

- [ ] **Step 1: Failing test — bulk fetch** in `SupplementNutrientRepositoryTests.cs` (existing `SqliteTestBase` style):

```csharp
GetBySupplementIdsAsync_ReturnsRowsForAllRequestedSupplements  // seed A(2 nutrients), B(1), C(0); ids [A,B] -> 3 rows, none from C
GetBySupplementIdsAsync_UnknownId_ReturnsNoRowsForIt          // ids [A, 999999] -> only A's rows
```

Run `dotnet test --filter "GetBySupplementIdsAsync"` — compile failure expected.

- [ ] **Step 2: Implement bulk fetch.** Interface addition next to `GetCountsBySupplementIdsAsync`; impl mirrors its SQL shape: `SELECT * FROM SupplementNutrients WHERE SupplementId IN @Ids`. Run the filter — PASS.

- [ ] **Step 3: Failing tests — builder** — new `VitaTrack.Tests/SupplementComparisonBuilderTests.cs`, helper builds `Supplement` + nutrient lists per id, calls `Build`:

```csharp
Build_PreservesColumnInputOrder                           // columns in input order with Name/Brand/DailyDose projected
Build_MergesNamesTrimmedAndCaseInsensitive                // "Vitamin D3" + "vitamin d3 " -> one row, Label == "Vitamin D3"
Build_SortsRowsAlphabetically_CaseInsensitive             // zinc before/after Vitamin D per OrdinalIgnoreCase order
Build_NestsChildrenUnderParent_OrphansTopLevel            // EPA child (ParentNutrientId set) -> IsBlendChild row directly after parent; child whose parent id is absent from all data -> top-level row
Build_ChildrenKeepDataOrder_UnderParent                   // two children appear in first-seen order under their parent
Build_NormalizesDosage_McgBecomesCanonicalMicroGram       // cell.Dosage == Dosage.Normalize("500 mcg") (canonical µg)
Build_MissingNutrient_YieldsNullCell                      // supplement without that nutrient -> Cells[i] is null
Build_EmptyDosageChild_YieldsCellWithEmptyDosage          // child with "" dosage -> cell present, Dosage == ""
```

Run `dotnet test --filter "SupplementComparisonBuilderTests"` — compile failure expected.

- [ ] **Step 4: Implement records + builder.**

Builder algorithm: iterate columns in order, then each supplement's nutrient list in data order; group top-level rows by normalized key (`Trim()` + `OrdinalIgnoreCase`), first sighting sets `Label`; child rows attach to their parent's key row (resolve `ParentNutrientId` against the union of all fetched nutrients by `Id`; unresolved = top-level); sort top-level rows by key with `StringComparer.OrdinalIgnoreCase`, children staying in first-seen order beneath their parent; cell per column = first nutrient in that column matching the row's key (children match within their parent group) → `new ComparisonCell(n.SpecificForm, Dosage.Normalize(n.Dosage))`, else `null`.

Run `dotnet test --filter "SupplementComparisonBuilderTests"` — PASS.

- [ ] **Step 5: Failing tests — handler** — new `VitaTrack.Tests/BuildSupplementComparisonHandlerTests.cs` (Moq both repos, per VitaTrack.Tests AGENTS — mocks isolate the unit):

```csharp
BuildAsync_FewerThanTwoResolve_ReturnsNull     // 0 or 1 resolved -> null (redirect path)
BuildAsync_PreservesRequestedOrder             // requested [b,a] -> Grid.Columns in that order
BuildAsync_DropsUnknownIds_RendersSurvivors    // [known1, 999999, known2] -> non-null, columns known1 then known2
BuildAsync_PassesNutrientsThrough              // mocked GetBySupplementIdsAsync result lands in grid rows
```

Run — compile failure expected.

- [ ] **Step 6: Implement handler + register.** `GetByIdAsync` per requested id (≤5, order-preserving, nulls dropped); <2 survivors → `null`; one `GetBySupplementIdsAsync(survivors)` grouped by `SupplementId`; call `SupplementComparisonBuilder.Build(...)`. Register `services.AddScoped<BuildSupplementComparisonHandler>();` next to the other handlers (concrete type, `ImportSupplementsHandler` precedent). Run handler tests — PASS.

- [ ] **Step 7: Failing tests — controller** — new `VitaTrack.Tests/SupplementControllerCompareTests.cs`, real handler over Moq'd repos, mirroring `SupplementControllerTests`'s `Url`/`ControllerContext` setup. Tracer set:

```csharp
Compare_ValidTwoIds_ReturnsViewWithGridInRequestOrder  // ViewResult; Model is ComparisonGrid; columns match ids order
Compare_FewerThanTwoValidIds_RedirectsToIndex          // "999999" / null / "" -> RedirectToActionResult to Index
```

Run — compile failure expected (no `Compare` action).

- [ ] **Step 8: Implement action + fix ctor call sites.** Ctor gains `BuildSupplementComparisonHandler comparisonHandler` (grep `new SupplementController(` in `VitaTrack.Tests`, update every site — known: `SupplementControllerTests`, `SupplementControllerEditTests`, `SupplementControllerImportCsvTests`, `SupplementControllerUpdateNutrientsTests`). Action: `ids?.Split(',')` → `int.TryParse` → keep `> 0` distinct preserving first occurrence → `BuildAsync(ordered)` → `null` → `RedirectToAction(nameof(Index))`, else `View(grid)`. Run `dotnet test --filter "SupplementController"` — PASS (new + pre-existing).

- [ ] **Step 9: Write `Views/Supplement/Compare.cshtml`.** `@model VitaTrack.Core.Features.Supplements.ComparisonGrid`; `ViewData["Title"] = "Compare Supplements"`; `<h2>` matches; back link `<a class="btn btn-outline-secondary" asp-action="Index">Back to Supplements</a>`; plain `table` (no `data-sortable`). Header row: first `<th>Nutrient</th>`, then per column a `<th>` with three Bootstrap-styled lines: `@c.Name`, `<span class="text-muted small">@c.Brand</span>`, `<span class="text-muted small">@c.DailyDose</span>`. Body: one `<tr>` per `ComparisonRow`; label cell gets `class="ps-4"` when `IsBlendChild`; per cell: `null` → `—`, else `@cell.SpecificForm` plus `<span class="text-muted"> @cell.Dosage</span>` only when `Dosage` non-empty. No inline styles/scripts.

- [ ] **Step 10: List-page control.** In `Index.cshtml` beside the Delete Selected button (same `mb-3` region):

```html
<a asp-action="Compare" id="compare-selected-btn" class="btn btn-outline-primary disabled" aria-disabled="true">Compare Selected</a>
<span id="compare-hint" class="text-muted ms-2" hidden>Compare up to 5 supplements.</span>
```

Script tag **after** `delete-selected.js` (registration order matters below): `<script src="~/js/compare-selected.js" asp-append-version="true"></script>`.

New `wwwroot/js/compare-selected.js` (vanilla IIFE, no inline handlers), pinned behavior:

```javascript
// reads: #select-all, .row-checkbox (DOM order), #compare-selected-btn, #compare-hint
// update():
//   checked = checked .row-checkbox in DOM order (ids from cb.value)
//   if checked.length > 5: uncheck every checkbox past the first 5 (DOM order), recompute
//   hint.hidden = checked.length < 5
//   checked.length >= 2 -> btn.href = '/Supplement/Compare?ids=' + checked.join(','),
//                           btn.classList.remove('disabled'), remove aria-disabled
//   else -> btn.removeAttribute('href'), btn.classList.add('disabled'), aria-disabled="true"
// listeners: 'change' on each .row-checkbox, 'change' on #select-all, update() once on load
```

Select-all note: `delete-selected.js` flips `cb.checked` without dispatching `change`, and its script tag precedes this one — by the time this file's select-all listener runs, checkbox states are already final. Do not add `dispatchEvent` workarounds. The literal `'/Supplement/Compare?ids='` is the inbound reference `UiReachabilityTests` scans for.

- [ ] **Step 11: Amend `DESIGN.md`** (all three are real at this commit): (1) button-intent table row `| Bulk compare (Compare Selected) | btn btn-outline-primary |`; (2) extend the `select-all`/`row-checkbox` pattern paragraph — same selection feeds Compare Selected, gated ≥2, capped 5 with `#compare-hint`; (3) component entry for the comparison grid — 3-line headers (name / brand / serving), blend children indented via Bootstrap `ps-*`, em dash for "not in this supplement".

- [ ] **Step 12: Claim everything.** `shards.yaml`: MS `core:` += 6 new Core files, MS `js:` += `compare-selected.js`, MS `unit_tests:` += 3 new test files, MS `e2e_specs:` += `supplement-comparison.spec.js`. `storymap.yaml`: new task `MS-6` under "Manage Supplements":

```yaml
- id: MS-6
  name: Compare supplements
  entry_point: Supplements index -> Compare Selected
  stories:
    - title: Select supplements and open the comparison grid
      status: done
      priority: medium
      tests:
        - e2e: supplement-comparison::should compare selected supplements via the Compare Selected button
        - unit: SupplementComparisonBuilderTests.Build_MergesNamesTrimmedAndCaseInsensitive
        - unit: SupplementControllerCompareTests.Compare_ValidTwoIds_ReturnsViewWithGridInRequestOrder
```

(Storymap grows in Task 3 as more tests land — refs must always resolve; add only refs for tests that exist.)

- [ ] **Step 13: Tracer e2e** — `e2e-tests/playwright/tests/supplement-comparison.spec.js`, following `supplement-crud.spec.js` idioms (screenshot helper, row-text locators, seed names read from `DbInit.EnsureCreated` — never hardcoded ids):

```javascript
test('should compare selected supplements via the Compare Selected button', async (page, testInfo) => {
  // goto /Supplement; check .row-checkbox in two named rows (tr:has-text("<name>"));
  // click #compare-selected-btn; expect h2 "Compare Supplements";
  // expect column headers contain both names + Brand + DailyDose lines;
  // expect a shared nutrient row with non-empty cells; expect an em dash where a supplement lacks a nutrient.
});
```

Arrives by clicking from the list (No Orphan Pages: ≥1 e2e per surface clicks in).

- [ ] **Step 14: Tracer gate + commit.**

```bash
./format-check.sh && dotnet test
cd e2e-tests/playwright && npx playwright test tests/supplement-comparison.spec.js
git add -A && git commit -m "feat: supplement comparison page (tracer bullet)"
```

Expected: all green — pipe works end to end at commit one.

---

### Task 2: Edge-case hardening — controller/handler

**Files:**
- Test: `VitaTrack.Tests/SupplementControllerCompareTests.cs`
- Modify: `VitaTrack.Web/Controllers/SupplementController.cs` only if a test exposes a gap

**Interfaces:**
- Consumes: `Compare(string? ids)` and `BuildAsync(IReadOnlyList<int>)` from Task 1.

- [ ] **Step 1: Write the failing edge tests:**

```csharp
Compare_JunkQuery_RendersWhenTwoValidRemain              // "abc,-1,,2,,2" -> ViewResult, 2 columns, no throw
Compare_MalformedAndDuplicateIds_RedirectsWhenFewerThanTwoResolve // "abc,2,2" -> redirect (dedupe leaves 1)
Compare_UnknownIdsSkipped_RendersWithSurvivors           // "known1,999999,known2" -> 2 columns, order known1, known2
Compare_UnknownIdsOnly_Redirects                         // "999999,1000000" -> redirect
Compare_BeyondCapUrl_StillRenders                        // 6 valid ids -> ViewResult, 6 columns (server ignores client cap)
```

- [ ] **Step 2: Run** `dotnet test --filter "SupplementControllerCompareTests"` — fail (test missing) or expose implementation gap.

- [ ] **Step 3: Fix implementation only if a test exposed a gap** — parser/handler already specified; the tests pin it.

- [ ] **Step 4: Run** full `dotnet test` + `./format-check.sh` — PASS.

- [ ] **Step 5: Commit**

```bash
git add VitaTrack.Tests/SupplementControllerCompareTests.cs
git commit -m "test: edge-case coverage for comparison id parsing"
```

---

### Task 3: E2E hardening — gating + redirect + storymap growth

**Files:**
- Modify: `e2e-tests/playwright/tests/supplement-comparison.spec.js`
- Modify: `storymap.yaml` (MS-6 gains the gating story + refs)

**Interfaces:**
- Consumes: Task 1's button/JS/hint markup and action; Test 1's exact titles are already referenced by storymap.

- [ ] **Step 1: Write the failing e2e tests** (exact titles — storymap refs them):

```javascript
test('should gate Compare Selected below two and cap at five', async (page, testInfo) => {
  // goto /Supplement;
  // 0 checked -> #compare-selected-btn has class 'disabled';
  // 1 checked (one named row) -> still disabled;
  // click #select-all (delete-selected.js flips all boxes) -> #compare-hint visible, exactly 5 .row-checkbox checked;
  // attempt a 6th -> still exactly 5 checked.
});

test('should redirect to the list when no valid ids resolve', async (page, testInfo) => {
  // goto /Supplement/Compare?ids=999999,abc -> lands on list (h2 "Supplements").
});
```

The select-all path is Review Focus #5 — do not replace it with individual checkbox clicks.

- [ ] **Step 2: Run** `cd e2e-tests/playwright && npx playwright test tests/supplement-comparison.spec.js` — fail first, then pass (fix JS only if a test exposes a gap).

- [ ] **Step 3: Grow `storymap.yaml` MS-6** — second story:

```yaml
    - title: Selection gating (min two, cap five) and stale-id redirects
      status: done
      priority: medium
      tests:
        - e2e: supplement-comparison::should gate Compare Selected below two and cap at five
        - e2e: supplement-comparison::should redirect to the list when no valid ids resolve
        - unit: SupplementControllerCompareTests.Compare_FewerThanTwoValidIds_RedirectsToIndex
```

- [ ] **Step 4: Run** `dotnet test VitaTrack.ArchitectureTests` (shards + storymap ref checks) — PASS.

- [ ] **Step 5: Commit**

```bash
git add e2e-tests/playwright/tests/supplement-comparison.spec.js storymap.yaml
git commit -m "test: e2e gating coverage + story map for comparison"
```

---

### Task 4: Full verify gate

- [ ] **Step 1: Run the verify-shard sequence** (FACTORY.md shape):

```bash
./format-check.sh && dotnet build -c Release && dotnet test
cd e2e-tests/playwright && npx playwright test
```

Expected: all green — format, build, arch + unit, full e2e.

- [ ] **Step 2: Fix anything red** (regressions only; no scope additions).

- [ ] **Step 3: No commit if nothing changed; otherwise commit the fix with a descriptive `fix:`/`test:` prefix.**
