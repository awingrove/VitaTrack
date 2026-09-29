# Supplement Comparison Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users select up to 5 supplements from the list and open a read-only comparison grid (supplements as columns, generic nutrients as rows, each cell = specific form + normalized amount per serving).

**Architecture:** Pure grid builder + fetch handler in the Supplements slice of `VitaTrack.Core`, one new bulk read method on the Nutrients repository, a `Compare` GET action on the existing `SupplementController`, and a list-page "Compare Selected" anchor wired by external JS that builds `?ids=` from checked boxes in table-row order.

**Tech Stack:** ASP.NET MVC + Razor, Dapper/SQLite, MSTest + Moq, Playwright, Bootstrap 5 (stock only).

**Spec:** `docs/superpowers/specs/2026-09-29-supplement-comparison-design.md` — all decisions (purpose, normalization, matching, ordering, cap, edge cases) live there; this plan argues from it.

## Global Constraints

- Row key = `GenericName.Trim()` compared case-insensitive; label = first-seen spelling (spec "Row matching").
- Amounts = raw stored `Dosage` passed through `Dosage.Normalize(...)` (`VitaTrack.Core/Primitives/Dosage.cs`) — never reimplement unit canonicalization, never convert across unit families.
- Column order = order of ids in the query string; rows alphabetical by matched key; children keep data order under their parent.
- Min 2 resolved supplements or redirect to `Supplement/Index`; unknown/malformed/duplicate ids silently dropped; server never enforces the 5 cap (client-side only).
- Column header = 3 lines: `Name`, `Brand`, `DailyDose` (free text, TD-017 — display as-is).
- Stock Bootstrap only, no custom CSS; no inline `<script>`/event handlers (CSP); every `wwwroot` asset reference uses `asp-append-version="true"`.
- Every new file claimed in `shards.yaml` in the same task that creates it (red build otherwise); new e2e spec referenced by a `storymap.yaml` task in the same change.
- Comment claim vocabulary: any `CHECKS:`/`PINS:` comment in tests needs an `Assert.` in its enclosing method; prefer no claim comments.
- 300-line complete-type split trigger; keep every type well under it.
- Each task ends green: `dotnet test` passes and `./format-check.sh` passes before commit (pre-commit hook runs both).

## Review Focus

1. **Junk query strings** (`?ids=abc,-1,,2,,2`) — parse must drop non-ints/negatives/dupes without throwing, and <2 survivors must redirect. Pinned by `SupplementControllerCompareTests.Compare_MalformedAndDuplicateIds_RedirectsWhenFewerThanTwoResolve` (Task 5) and `Compare_JunkQuery_RendersWhenTwoValidRemain`.
2. **Unknown/deleted ids mixed with valid ones** (`?ids=1,999999`) — silently skipped; still renders when ≥2 remain; redirects when they don't. Pinned by `Compare_UnknownIdsSkipped_RendersWithSurvivors` / `Compare_UnknownIdsOnly_Redirects` (Task 5).
3. **Row merging** — `"Vitamin D3"` vs `"vitamin d3 "` must land on one row with first-seen label, and blend children must nest under their parent (orphans top-level). Pinned by `Build_MergesNamesTrimmedAndCaseInsensitive` and `Build_NestsChildrenUnderParent_OrphansTopLevel` (Task 2).
4. **Client-only cap/min gating** — server must render any ≥2 id list even past 5 (shareable URLs), while the list UI blocks the 6th checkbox and disables Compare below 2. Pinned by `Compare_BeyondCapUrl_StillRenders` (Task 5) and the cap/disabled e2e tests (Task 6).
5. **Select-all interplay** — `delete-selected.js` sets `cb.checked` programmatically (no `change` event fires), so Compare's state could go stale after select-all. Pinned by e2e `should gate Compare Selected…` using the `#select-all` header checkbox (Task 6).

---

### Task 1: Bulk nutrient fetch `GetBySupplementIdsAsync`

**Files:**
- Modify: `VitaTrack.Core/Features/Nutrients/ISupplementNutrientRepository.cs`
- Modify: `VitaTrack.Core/Features/Nutrients/SupplementNutrientRepository.cs`
- Test: `VitaTrack.Tests/SupplementNutrientRepositoryTests.cs` (existing, already claimed in shards)

**Interfaces:**
- Produces: `Task<IReadOnlyList<SupplementNutrient>> GetBySupplementIdsAsync(IEnumerable<int> supplementIds)` — rows for all given supplements in one query. Task 3's handler consumes it.

- [ ] **Step 1: Write the failing test** in `SupplementNutrientRepositoryTests.cs` (follow the file's existing `SqliteTestBase` arrange style):

```csharp
[TestMethod]
public async Task GetBySupplementIdsAsync_ReturnsRowsForAllRequestedSupplements()
{
    // seed supplements A, B, C; 2 nutrients on A, 1 on B
    var rows = await _repo.GetBySupplementIdsAsync(new[] { aId, bId });
    Assert.AreEqual(3, rows.Count);
    Assert.IsTrue(rows.All(r => r.SupplementId == aId || r.SupplementId == bId));
}

[TestMethod]
public async Task GetBySupplementIdsAsync_UnknownId_ReturnsNoRowsForIt()
{
    var rows = await _repo.GetBySupplementIdsAsync(new[] { aId, 999999 });
    // only A's rows come back
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "GetBySupplementIdsAsync"` — Expected: compile failure (method does not exist).

- [ ] **Step 3: Implement the method**

Interface addition next to `GetCountsBySupplementIdsAsync`; impl mirrors the existing `GetCountsBySupplementIdsAsync` SQL shape: `SELECT * FROM SupplementNutrients WHERE SupplementId IN @Ids`. Dapper expands `IN @Ids`. Keep same nullability (`IReadOnlyList<SupplementNutrient>`).

- [ ] **Step 4: Run tests**

Run: `dotnet test --filter "GetBySupplementIdsAsync"` — Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add VitaTrack.Core/Features/Nutrients/ VitaTrack.Tests/SupplementNutrientRepositoryTests.cs
git commit -m "feat: bulk nutrient fetch by supplement ids"
```

---

### Task 2: Comparison records + pure builder

**Files:**
- Create: `VitaTrack.Core/Features/Supplements/ComparisonCell.cs`
- Create: `VitaTrack.Core/Features/Supplements/ComparisonRow.cs`
- Create: `VitaTrack.Core/Features/Supplements/ComparisonColumn.cs`
- Create: `VitaTrack.Core/Features/Supplements/ComparisonGrid.cs`
- Create: `VitaTrack.Core/Features/Supplements/SupplementComparisonBuilder.cs`
- Test: `VitaTrack.Tests/SupplementComparisonBuilderTests.cs` (new)
- Modify: `shards.yaml` (MS `core:` += the five files; MS `unit_tests:` += the test file)

**Interfaces:**
- Consumes: `Supplement` (Id, Name, Brand, DailyDose), `SupplementNutrient` (Id, SupplementId, GenericName, SpecificForm, Dosage, ParentNutrientId), `Dosage.Normalize(string?) -> string`.
- Produces (Task 3 relies on exact shapes):

```csharp
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
```

- [ ] **Step 1: Write the failing tests** — new `VitaTrack.Tests/SupplementComparisonBuilderTests.cs`, helper that builds a `Supplement` + nutrient list per id and calls `Build`. Test methods and their assertions:

```csharp
Build_PreservesColumnInputOrder_ByNameMatchIrrelevant      // columns come back in input order with Name/Brand/DailyDose projected
Build_MergesNamesTrimmedAndCaseInsensitive                 // "Vitamin D3" + "vitamin d3 " -> one row, Label == "Vitamin D3"
Build_SortsRowsAlphabetically_CaseInsensitive              // zinc row before/after Vitamin D per OrdinalIgnoreCase order
Build_NestsChildrenUnderParent_OrphansTopLevel             // EPA child (ParentNutrientId set) row IsBlendChild, immediately after parent; child whose parent id absent -> top-level row
Build_ChildrenKeepDataOrder_UnderParent                    // two children appear in first-seen order under parent
Build_NormalizesDosage_McgBecomesMicroGram                 // cell.Dosage == Dosage.Normalize("500 mcg") i.e. canonical µg
Build_MissingNutrient_YieldsNullCell                       // supplement without that nutrient -> Cells[i] is null
Build_EmptyDosageChild_YieldsCellWithEmptyDosage           // child with "" dosage -> cell present, Dosage == ""
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "SupplementComparisonBuilderTests"` — Expected: compile failure.

- [ ] **Step 3: Implement records + builder**

Builder algorithm (signature + tests leave the data structure open, pin this one): iterate columns in order, then each supplement's nutrient list in data order; group top-level rows by normalized key (`Trim()` + `OrdinalIgnoreCase`), first sighting sets `Label`; child rows attach to their parent's key row (resolve parent by `ParentNutrientId` against the union of all fetched nutrients by `Id`; unresolved = top-level); after collection, sort top-level rows by key with `StringComparer.OrdinalIgnoreCase`, children staying in first-seen order beneath their parent; cell per column = first nutrient in that column matching the row's key (children match within their parent group) → `new ComparisonCell(n.SpecificForm, Dosage.Normalize(n.Dosage))`, else `null`.

- [ ] **Step 4: Run tests**

Run: `dotnet test --filter "SupplementComparisonBuilderTests"` — Expected: PASS.

- [ ] **Step 5: Claim files in `shards.yaml`**

Add the five Core files to MS `core:` and the test file to MS `unit_tests:` (alphabetically consistent with neighbors).

- [ ] **Step 6: Verify shard claims + run full suite**

Run: `dotnet test VitaTrack.ArchitectureTests` then `dotnet test` — Expected: PASS (no orphan files).

- [ ] **Step 7: Commit**

```bash
git add VitaTrack.Core/Features/Supplements/ VitaTrack.Tests/SupplementComparisonBuilderTests.cs shards.yaml
git commit -m "feat: supplement comparison grid builder"
```

---

### Task 3: `BuildSupplementComparisonHandler` + DI registration

**Files:**
- Create: `VitaTrack.Core/Features/Supplements/BuildSupplementComparisonHandler.cs`
- Modify: `VitaTrack.Core/ServiceCollectionExtensions.cs`
- Test: `VitaTrack.Tests/BuildSupplementComparisonHandlerTests.cs` (new)
- Modify: `shards.yaml` (MS `core:` += handler; MS `unit_tests:` += test file)

**Interfaces:**
- Consumes: `ISupplementRepository.GetByIdAsync(int)`, `ISupplementNutrientRepository.GetBySupplementIdsAsync` (Task 1), `SupplementComparisonBuilder.Build` (Task 2).
- Produces (Task 5 consumes):

```csharp
public sealed class BuildSupplementComparisonHandler(
    ISupplementRepository supplementRepo,
    ISupplementNutrientRepository nutrientRepo)
{
    // Returns null when fewer than two of orderedIds resolve to a supplement.
    public async Task<ComparisonGrid?> BuildAsync(IReadOnlyList<int> orderedIds);
}
```

- [ ] **Step 1: Write the failing tests** — new `VitaTrack.Tests/BuildSupplementComparisonHandlerTests.cs` with Moq mocks of both repositories (per VitaTrack.Tests AGENTS, mocks isolate the unit):

```csharp
BuildAsync_FewerThanTwoResolve_ReturnsNull        // 0 or 1 GetByIdAsync hit returns a supplement -> null (redirect path)
BuildAsync_PreservesRequestedOrder                // requested [b,a] -> Grid.Columns in that order
BuildAsync_DropsUnknownIds_RendersSurvivors       // [known, 999999] -> non-null, one column, nutrient fetch asked only for resolved ids
BuildAsync_PassesNutrientsThrough                 // mocked GetBySupplementIdsAsync result lands in grid rows
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "BuildSupplementComparisonHandlerTests"` — Expected: compile failure.

- [ ] **Step 3: Implement the handler**

Approach: `GetByIdAsync` per requested id (≤5, order-preserving, nulls dropped); if survivors < 2 return `null`; one `GetBySupplementIdsAsync(survivorIds)` call; group result by `SupplementId`; call `SupplementComparisonBuilder.Build(survivors, groups)`.

- [ ] **Step 4: Register in `ServiceCollectionExtensions.AddCore`**

Next to the other handler registrations (`services.AddScoped<BuildSupplementComparisonHandler>();` — concrete type, matching `ImportSupplementsHandler` precedent).

- [ ] **Step 5: Run tests**

Run: `dotnet test --filter "BuildSupplementComparisonHandlerTests"` — Expected: PASS.

- [ ] **Step 6: Claim files in `shards.yaml`** (handler → MS `core:`, test → MS `unit_tests:`), then run `dotnet test VitaTrack.ArchitectureTests` — Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add VitaTrack.Core/Features/Supplements/BuildSupplementComparisonHandler.cs VitaTrack.Core/ServiceCollectionExtensions.cs VitaTrack.Tests/BuildSupplementComparisonHandlerTests.cs shards.yaml
git commit -m "feat: comparison build handler with id resolution"
```

---

### Task 4: List-page "Compare Selected" UI + DESIGN.md amendment

**Files:**
- Modify: `VitaTrack.Web/Views/Supplement/Index.cshtml` (toolbar line ~14-18, script tags ~65-68)
- Create: `VitaTrack.Web/wwwroot/js/compare-selected.js`
- Modify: `DESIGN.md`
- Modify: `shards.yaml` (MS `js:` += compare-selected.js)

**Interfaces:**
- Consumes: existing `.row-checkbox` / `#select-all` markup (do not rename — `delete-selected.js` and existing e2e depend on it).
- Produces (Task 5 relies on it): a working inbound reference to the `Compare` action — literal `/Supplement/Compare` inside the JS (this is what `UiReachabilityTests` scans for), ids in table-row order, `#compare-hint` element.

- [ ] **Step 1: Add markup to `Index.cshtml`**

Inside the existing delete-selected form block area (keep `mb-3` grouping), beside the Delete Selected button:

```html
<a asp-action="Compare" id="compare-selected-btn" class="btn btn-outline-primary disabled"
   aria-disabled="true">Compare Selected</a>
<span id="compare-hint" class="text-muted ms-2" hidden>Compare up to 5 supplements.</span>
```

Server-rendered disabled (no href until JS resolves selection — anchor gets `href` from the tag helper initially; JS normalizes on load, see Step 2). Script tag after `delete-selected.js` (registration order matters — see Step 2 note):

```html
<script src="~/js/compare-selected.js" asp-append-version="true"></script>
```

- [ ] **Step 2: Write `wwwroot/js/compare-selected.js`**

Pin these behaviors (vanilla, IIFE, no inline handlers):

```javascript
// reads: #select-all, .row-checkbox (DOM order), #compare-selected-btn, #compare-hint
// update():
//   checked = checkboxes filtered to checked, in DOM order (ids from cb.value)
//   if checked.length > 5: uncheck every checkbox past the first 5 (in DOM order), recompute
//   hint.hidden = checked.length < 5
//   if checked.length >= 2: btn.href = '/Supplement/Compare?ids=' + checked.join(','),
//                            btn.classList.remove('disabled'), remove aria-disabled
//   else: btn.removeAttribute('href'), btn.classList.add('disabled'), set aria-disabled="true"
// listeners: 'change' on each .row-checkbox, 'change' on #select-all, and update() once on load
```

Note for the select-all listener: `delete-selected.js` flips `cb.checked` without dispatching `change`, and it is registered first (its script tag precedes this one), so by the time this file's select-all listener runs, checkbox states are already final — that is why the script tag placement in Step 1 is not arbitrary. Do not add `dispatchEvent` workarounds.

- [ ] **Step 3: Amend `DESIGN.md`** (required — new button intent + new grid pattern):

1. Add a row to the button-intent table (near "Bulk destructive"): `| Bulk compare (Compare Selected) | btn btn-outline-primary |`
2. Extend the checkbox-selection pattern paragraph (the `select-all` + `row-checkbox` sentence) to note the same selection also feeds Compare Selected, gated at ≥2 and capped at 5 with `#compare-hint`.
3. Add a short component entry for the comparison grid: 3-line column headers (name / brand / serving), blend children indented under their parent row (Bootstrap `ps-*`, no custom CSS), em dash for "nutrient not in this supplement".

- [ ] **Step 4: Claim the JS in `shards.yaml`** (MS `js:` list).

- [ ] **Step 5: Verify**

Run: `dotnet test VitaTrack.ArchitectureTests && ./format-check.sh` — Expected: PASS. (UI-reachability for `Compare` still red until Task 5 adds the action — architecture tests only flag *existing* orphan actions, none exists yet.)

- [ ] **Step 6: Commit**

```bash
git add VitaTrack.Web/Views/Supplement/Index.cshtml VitaTrack.Web/wwwroot/js/compare-selected.js DESIGN.md shards.yaml
git commit -m "feat: Compare Selected toolbar control on supplement list"
```

---

### Task 5: `Compare` action + `Compare.cshtml` view

**Files:**
- Modify: `VitaTrack.Web/Controllers/SupplementController.cs` (add `Compare` + ctor param)
- Create: `VitaTrack.Web/Views/Supplement/Compare.cshtml`
- Test: `VitaTrack.Tests/SupplementControllerCompareTests.cs` (new)
- Modify: every `new SupplementController(` construction site in `VitaTrack.Tests` (grep first — `SupplementControllerTests.cs`, `SupplementControllerEditTests.cs`, `SupplementControllerImportCsvTests.cs`, `SupplementControllerUpdateNutrientsTests.cs` are the known ones; add the new handler argument, built from mocked repos)

**Interfaces:**
- Consumes: `BuildSupplementComparisonHandler` (Task 3), Task 4's `/Supplement/Compare` JS reference.
- Produces: `GET /Supplement/Compare?ids=1,2,3` rendering `@model VitaTrack.Core.Features.Supplements.ComparisonGrid`.

- [ ] **Step 1: Write the failing tests** — new `VitaTrack.Tests/SupplementControllerCompareTests.cs`. Construct the controller with a **real** handler built over Moq'd repositories (handler is concrete; repos are the seam), mirroring `SupplementControllerTests`'s `Url`/`ControllerContext` setup:

```csharp
Compare_ValidTwoIds_ReturnsViewWithGridInRequestOrder   // ViewResult, Model is ComparisonGrid, columns match ids order
Compare_JunkQuery_RendersWhenTwoValidRemain             // "abc,-1,,2,,2" -> two-column grid, no throw
Compare_MalformedAndDuplicateIds_RedirectsWhenFewerThanTwoResolve // "abc,2,2" -> RedirectToActionResult to Index
Compare_UnknownIdsSkipped_RendersWithSurvivors          // "known1,999999,known2" -> 2 columns, order known1, known2
Compare_UnknownIdsOnly_Redirects                        // "999999,1000000" -> redirect to Index
Compare_BeyondCapUrl_StillRenders                       // 6 valid ids -> ViewResult with 6 columns (server ignores cap)
Compare_NullOrEmptyIds_Redirects                        // null / "" -> redirect to Index
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "SupplementControllerCompareTests"` — Expected: compile failure (no `Compare` action).

- [ ] **Step 3: Implement the action**

```csharp
// GET: /Supplement/Compare?ids=1,2,3
[HttpGet]
public async Task<IActionResult> Compare(string? ids)
```

Parse: `ids?.Split(',')` → `int.TryParse` each → keep `> 0`, distinct preserving first occurrence → pass to `_comparisonHandler.BuildAsync(ordered)` → `null` → `RedirectToAction(nameof(Index))`, else `return View(grid)`. Ctor gains `BuildSupplementComparisonHandler comparisonHandler` (update all construction sites found by grep).

- [ ] **Step 4: Run controller tests**

Run: `dotnet test --filter "SupplementControllerCompareTests"` — Expected: PASS. Then `dotnet test --filter "SupplementController"` — all pre-existing controller tests green with the new ctor argument.

- [ ] **Step 5: Write `Views/Supplement/Compare.cshtml`**

`@model VitaTrack.Core.Features.Supplements.ComparisonGrid`; `ViewData["Title"] = "Compare Supplements"`; `<h2>` matches; back link `<a class="btn btn-outline-secondary" asp-action="Index">Back to Supplements</a>`; plain `table` (no `data-sortable`). Header row: first `<th>Nutrient</th>`, then one `<th>` per column with three Bootstrap-styled lines: `@c.Name`, `<span class="text-muted small">@c.Brand</span>`, `<span class="text-muted small">@c.DailyDose</span>`. Body: one `<tr>` per `ComparisonRow`; label cell gets `class="ps-4"` when `IsBlendChild` (else default), label text as stored; per cell: `null` → `—`, else `@cell.SpecificForm` plus `<span class="text-muted"> @cell.Dosage</span>` only when `Dosage` non-empty. No inline styles/scripts.

- [ ] **Step 6: Full verification**

Run: `dotnet test && ./format-check.sh` — Expected: PASS, including `UiReachabilityTests` (Task 4's JS is the inbound reference) and `ShardOwnershipTests`.

- [ ] **Step 7: Commit**

```bash
git add VitaTrack.Web/Controllers/SupplementController.cs VitaTrack.Web/Views/Supplement/Compare.cshtml VitaTrack.Tests/
git commit -m "feat: supplement comparison page"
```

---

### Task 6: E2E spec + story map

**Files:**
- Create: `e2e-tests/playwright/tests/supplement-comparison.spec.js`
- Modify: `storymap.yaml` (new task `MS-6` under "Manage Supplements")
- Modify: `shards.yaml` (MS `e2e_specs:` += the spec)

**Interfaces:**
- Consumes: everything from Tasks 1-5; seed data from `DbInit.EnsureCreated` (read it for exact seeded supplement/nutrient names — never hardcode ids).

- [ ] **Step 1: Write the spec** — `supplement-comparison.spec.js`, following `supplement-crud.spec.js` idioms (`screenshot` helper, row-text locators). Exact test titles (storymap refs these fragments):

```javascript
test('should compare selected supplements via the Compare Selected button', ...)
//   goto /Supplement; check .row-checkbox in two named rows (tr:has-text("<name>"));
//   click #compare-selected-btn; expect h2 "Compare Supplements";
//   expect column headers contain both names + Brand + DailyDose lines;
//   expect a nutrient row from the seed data with non-empty cells, and an em dash where a supplement lacks a nutrient.

test('should gate Compare Selected below two and cap at five', ...)
//   0 checked -> #compare-selected-btn has class 'disabled';
//   1 checked -> still disabled;
//   select #select-all (delete-selected.js flips all boxes) -> hint #compare-hint visible, exactly 5 .row-checkbox checked;
//   check a 6th -> still exactly 5 checked.

test('should redirect to the list when no valid ids resolve', ...)
//   goto /Supplement/Compare?ids=999999,abc -> lands on /Supplement list (h2 "Supplements").
```

Titles must be the exact e2e fragments referenced in Step 2. The first test arrives by clicking from the list (No Orphan Pages: ≥1 e2e per surface clicks in).

- [ ] **Step 2: Claim in `shards.yaml` + reference in `storymap.yaml`**

New task under the supplement activity, id `MS-6`:

```yaml
- id: MS-6
  name: Compare supplements
  entry_point: Supplements index -> Compare Selected
  stories:
    - title: Select up to five supplements and open the comparison grid
      status: done
      priority: medium
      tests:
        - e2e: supplement-comparison::should compare selected supplements via the Compare Selected button
        - unit: SupplementComparisonBuilderTests.Build_MergesNamesTrimmedAndCaseInsensitive
        - unit: SupplementControllerCompareTests.Compare_ValidTwoIds_ReturnsViewWithGridInRequestOrder
    - title: Selection gating (min two, cap five) and stale-id redirects
      status: done
      priority: medium
      tests:
        - e2e: supplement-comparison::should gate Compare Selected below two and cap at five
        - unit: SupplementControllerCompareTests.Compare_UnknownIdsOnly_Redirects
```

Every `unit:`/`e2e:` fragment must resolve (`StoryMapConsistencyTests`); adjust fragment only to match the real title, never weaken the test.

- [ ] **Step 3: Run verification**

Run: `dotnet test VitaTrack.ArchitectureTests` (shards + storymap checks) — Expected: PASS.

- [ ] **Step 4: Run e2e**

Run: `cd e2e-tests/playwright && npx playwright test tests/supplement-comparison.spec.js` — Expected: PASS (server starts via config's webServer with `--environment Test`).

- [ ] **Step 5: Full gate**

Run: `./format-check.sh && dotnet test && npx playwright test` (from `e2e-tests/playwright`) — Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add e2e-tests/playwright/tests/supplement-comparison.spec.js storymap.yaml shards.yaml
git commit -m "test: e2e coverage + story map for supplement comparison"
```
