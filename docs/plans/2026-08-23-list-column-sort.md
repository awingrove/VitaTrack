# List Column Sorting — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Clickable client-side column sorting on Supplements and Nutrient list pages, plus server-side Name ordering as the Supplement list's initial state.

**Architecture:** Reusable external `table-sort.js` (no deps) sorts table rows in place on `<th data-sort-key>` click. Initial Supplement order comes from `ORDER BY Name` in the repository. Markup carries `data-sort-type` and optional `data-sort-value` for formatted/numeric cells.

**Tech Stack:** ASP.NET MVC, Razor, vanilla JS (external), Playwright E2E.

## Global Constraints

- CSP `script-src 'self' https://cdn.jsdelivr.net`, no `'unsafe-inline'`: no inline JS or handlers in views.
- No new npm/NuGet deps.
- Delete checkboxes must stay selected after sorting (rows reorder, not recreate).
- Story map updated same change; new E2E specs referenced from `storymap.yaml`.
- Run `dotnet test`; E2E via `E2E_PORT=5010 npx playwright test` (dev server holds 5000).

---

### Task 1: Server-side initial Name order for Supplements

**Files:**
- Modify: `VitaTrack.Infrastructure/Data/SupplementRepository.cs:16`

**Interfaces:** none new.

- [ ] **Step 1: Write failing unit test** (characterization — order should hold)

In `VitaTrack.Tests/SupplementRepositoryTests.cs` add:

```csharp
[TestMethod]
public async Task GetAllAsync_ReturnsRowsOrderedByName()
{
    using var db = new SqliteTestBase();
    db.SeedSupplements(); // ensure at least 2 with distinct names
    var repo = new SupplementRepository(db.Connection);
    var rows = await repo.GetAllAsync();
    CollectionAssert.AreEqual(rows.Select(r => r.Name).OrderBy(n => n).ToList(), rows.Select(r => r.Name).ToList());
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter SupplementRepositoryTests.GetAllAsync_ReturnsRowsOrderedByName`
Expected: FAIL (current SQL has no ORDER BY).

- [ ] **Step 3: Implement**

Change line 16 SQL to include ` ORDER BY Name`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter SupplementRepositoryTests.GetAllAsync_ReturnsRowsOrderedByName`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add VitaTrack.Infrastructure/Data/SupplementRepository.cs VitaTrack.Tests/SupplementRepositoryTests.cs
git commit -m "feat(repo): order supplements by name in GetAllAsync"
```

---

### Task 2: Reusable client-side table sort JS

**Files:**
- Create: `VitaTrack.Web/wwwroot/js/table-sort.js`

**Interfaces:** Produces convention `table[data-sortable]` with `<th data-sort-key data-sort-type>` and `<td data-sort-value>`.

- [ ] **Step 1: Write failing E2E** (in supplement-crud.spec.js later task covers; here just create JS)

Create the JS:

```js
(function () {
    document.querySelectorAll('table[data-sortable]').forEach(function (table) {
        const tbody = table.querySelector('tbody');
        const headers = table.querySelectorAll('th[data-sort-key]');
        let currentKey = null;
        let asc = true;

        headers.forEach(function (th) {
            th.style.cursor = 'pointer';
            th.addEventListener('click', function () {
                const key = th.dataset.sortKey;
                const type = th.dataset.sortType || 'text';
                if (currentKey === key) { asc = !asc; } else { currentKey = key; asc = true; }
                sortRows(tbody, key, type, asc);
                headers.forEach(function (h) { h.textContent = h.textContent.replace(/ [▲▼]$/, ''); });
                th.textContent = th.textContent.trim() + (asc ? ' ▲' : ' ▼');
            });
        });

        function sortRows(tbody, key, type, asc) {
            const rows = Array.from(tbody.querySelectorAll('tr'));
            rows.sort(function (a, b) {
                const av = cellValue(a, key);
                const bv = cellValue(b, key);
                let cmp;
                if (type === 'number') { cmp = (parseFloat(av) || 0) - (parseFloat(bv) || 0); }
                else { cmp = String(av).localeCompare(String(bv), undefined, { numeric: true, sensitivity: 'base' }); }
                return asc ? cmp : -cmp;
            });
            rows.forEach(function (r) { tbody.appendChild(r); });
        }

        function cellValue(row, key) {
            const cell = row.querySelector('td[data-sort-key="' + key + '"]');
            if (!cell) return '';
            return cell.dataset.sortValue !== undefined ? cell.dataset.sortValue : cell.textContent.trim();
        }
    });
})();
```

- [ ] **Step 2: No unit test needed** (logic in view). Verified by E2E in Task 3/4.

- [ ] **Step 3: Commit**

```bash
git add VitaTrack.Web/wwwroot/js/table-sort.js
git commit -m "feat: reusable client-side table-sort.js"
```

---

### Task 3: Supplement list sorting + initial order E2E

**Files:**
- Modify: `VitaTrack.Web/Views/Supplement/Index.cshtml`
- Test: `e2e-tests/playwright/tests/supplement-crud.spec.js`

**Interfaces:** Consumes `data-sortable` JS from Task 2.

- [ ] **Step 1: Write failing E2E**

In `supplement-crud.spec.js` add:

```js
test('should sort supplements by a column when header clicked', async ({ page }, testInfo) => {
  await page.goto('/Supplement');
  // Initially sorted by Name ascending
  const nameHeader = page.locator('th[data-sort-key="Name"]');
  await expect(nameHeader).toContainText('▲');

  // Click Nutrient Count header -> numeric sort
  await page.locator('th[data-sort-key="NutrientCount"]').click();
  const firstCount = await page.locator('table tbody tr:first-child td[data-sort-key="NutrientCount"]').getAttribute('data-sort-value');
  const secondCount = await page.locator('table tbody tr:nth-child(2) td[data-sort-key="NutrientCount"]').getAttribute('data-sort-value');
  expect(Number(firstCount)).toBeLessThanOrEqual(Number(secondCount));

  // Click again -> descending
  await page.locator('th[data-sort-key="NutrientCount"]').click();
  const firstCountDesc = await page.locator('table tbody tr:first-child td[data-sort-key="NutrientCount"]').getAttribute('data-sort-value');
  const secondCountDesc = await page.locator('table tbody tr:nth-child(2) td[data-sort-key="NutrientCount"]').getAttribute('data-sort-value');
  expect(Number(firstCountDesc)).toBeGreaterThanOrEqual(Number(secondCountDesc));
  await screenshot(page, testInfo, 'supplement-sorted');
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `E2E_PORT=5010 npx playwright test supplement-crud -g "sort supplements"`
Expected: FAIL (no sortable markup / no header).

- [ ] **Step 3: Markup changes**

Edit `Views/Supplement/Index.cshtml`:
- `<table class="table">` → `<table class="table" data-sortable>`
- Header cells: wrap sortable ones with `data-sort-key` / `data-sort-type`:
  - Name → `th data-sort-key="Name" data-sort-type="text"`
  - Brand → `th data-sort-key="Brand" data-sort-type="text"`
  - Serving (DailyDose) → `th data-sort-key="DailyDose" data-sort-type="text"`
  - Cost → `th data-sort-key="Cost" data-sort-type="number"`
  - Nutrient Count → `th data-sort-key="NutrientCount" data-sort-type="number"`
- Add `data-sort-key`/`data-sort-value` to the matching `<td>` cells:
  - Name cell: `<td data-sort-key="Name">@s.Name</td>` (optional; text fallback works, but add for stable matching)
  - Cost cell: `<td data-sort-key="Cost" data-sort-value="@(s.Cost?.ToString("F2") ?? "0")">@(s.Cost.HasValue ? $"£{s.Cost.Value:F2}" : "N/A")</td>`
  - NutrientCount cell: `<td data-sort-key="NutrientCount" data-sort-value="@s.NutrientCount">@s.NutrientCount</td>`
  - Brand / DailyDose: add `data-sort-key` accordingly.
- Add `<script src="~/js/table-sort.js"></script>` with the other scripts.

- [ ] **Step 4: Run to verify it passes**

Run: `E2E_PORT=5010 npx playwright test supplement-crud`
Expected: PASS (including new sort test).

- [ ] **Step 5: Commit**

```bash
git add VitaTrack.Web/Views/Supplement/Index.cshtml e2e-tests/playwright/tests/supplement-crud.spec.js
git commit -m "feat: client-side column sorting on supplement list"
```

---

### Task 4: Nutrient list sorting

**Files:**
- Modify: `VitaTrack.Web/Views/SupplementNutrient/Index.cshtml`
- Test: `e2e-tests/playwright/tests/supplement-nutrient.spec.js`

**Interfaces:** Consumes `table-sortable` JS.

- [ ] **Step 1: Write failing E2E**

In `supplement-nutrient.spec.js`:

```js
test('should sort nutrients by a column when header clicked', async ({ page }, testInfo) => {
  await page.goto('/Supplement');
  await page.click('tr:has-text("Multivitamin") >> text=Nutrients');
  await expect(page.locator('h2')).toHaveText(/Multivitamin/);

  await page.locator('th[data-sort-key="GenericName"]').click();
  const firstName = (await page.locator('table tbody tr:first-child td[data-sort-key="GenericName"]').textContent()).trim();
  const secondName = (await page.locator('table tbody tr:nth-child(2) td[data-sort-key="GenericName"]').textContent()).trim();
  expect(String(firstName).localeCompare(String(secondName), undefined, { numeric: true }) <= 0).toBeTruthy();

  await screenshot(page, testInfo, 'nutrient-sorted');
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `E2E_PORT=5010 npx playwright test supplement-nutrient -g "sort nutrients"`
Expected: FAIL.

- [ ] **Step 3: Markup changes**

Edit `Views/SupplementNutrient/Index.cshtml`:
- `<table class="table">` → `<table class="table" data-sortable>`
- Header `<th>Generic Name</th>` → `th data-sort-key="GenericName" data-sort-type="text"`
- `<th>Specific Form</th>` → `th data-sort-key="SpecificForm" data-sort-type="text"`
- `<th>Dosage</th>` → `th data-sort-key="Dosage" data-sort-type="text"`
- In row cells, add `data-sort-key`:
  - `<td data-sort-key="GenericName">@nutrient.GenericName</td>` (for root rows; blend child rows use a span — guard cellValue to read text; mark the wrapper cell too) and `data-sort-key="SpecificForm"`/`"Dosage"` on the other cells.
- Add `<script src="~/js/table-sort.js"></script>`.

- [ ] **Step 4: Run to verify it passes**

Run: `E2E_PORT=5010 npx playwright test supplement-nutrient`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add VitaTrack.Web/Views/SupplementNutrient/Index.cshtml e2e-tests/playwright/tests/supplement-nutrient.spec.js
git commit -m "feat: client-side column sorting on nutrient list"
```

---

### Task 5: Story map + full verification

**Files:**
- Modify: `storymap.yaml`
- Run: format-check, dotnet test, E2E.

- [ ] **Step 1: Add e2e refs**

Under MS-4 "Remove a supplement with confirmation dialog" — no, add a new story or attach to list story. Add story under MS-4:

```yaml
          - title: Sort supplements by column
            status: done
            priority: medium
            tests:
              - e2e: supplement-crud::should sort supplements by a column when header clicked
```

Under MS-6 add:

```yaml
          - title: Sort nutrients by column
            status: done
            priority: medium
            tests:
              - e2e: supplement-nutrient::should sort nutrients by a column when header clicked
```

- [ ] **Step 2: Verify consistency tests**

Run: `dotnet test --filter StoryMapConsistencyTests`
Expected: PASS

- [ ] **Step 3: Full verification**

```bash
./format-check.sh
dotnet build VitaTrack.sln && dotnet test
E2E_PORT=5010 CI=true VitaTrack__ApiKey="" ./test-e2e.sh
```
