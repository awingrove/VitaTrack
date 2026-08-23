# Delete Consistency Pass — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every delete on the Supplements / Nutrients / Blends list pages is confirmed with a CSP-safe native dialog that warns about cascade effects.

**Architecture:** No new endpoints or repos. Replace a CSP-blocked inline `onsubmit` confirm with an external JS handler (`data-confirm-message` forms), extend bulk-delete JS with `data-cascade-warning`, and surface blend-child cascade on the nutrient confirm page.

**Tech Stack:** ASP.NET MVC, Razor, vanilla JS (external files), Playwright E2E.

## Global Constraints

- CSP `script-src 'self' https://cdn.jsdelivr.net`, no `'unsafe-inline'`: no inline `<script>`, no inline event handlers in `.cshtml`.
- Deletes stay POST + `[ValidateAntiForgeryToken]`.
- No file over 300 lines; methods under 30 lines.
- Story map updated in same change; every new E2E spec referenced from `storymap.yaml`.
- Run tests with `dotnet test`; E2E with `./test-e2e.sh`.

---

### Task 1: CSP-safe single-row confirm JS + Supplement Index fix

**Files:**
- Create: `VitaTrack.Web/wwwroot/js/delete-confirm.js`
- Modify: `VitaTrack.Web/Views/Supplement/Index.cshtml:54`

**Interfaces:**
- Produces: convention `form[data-confirm-message]` → submit-time `confirm(message)`. Later tasks add attributes to more forms.

- [ ] **Step 1: Write failing E2E test**

In `e2e-tests/playwright/tests/supplement-crud.spec.js`, extend `should delete a supplement` to assert the dialog actually fires with cascade wording, and add a cancel test:

```js
test('should show confirm dialog and not delete when dismissed', async ({ page, testInfo }) => {
  await page.goto('/Supplement');
  const unique = Date.now();
  // create supplement via UI
  await page.click('a:has-text("Add New Supplement")');
  await page.fill('input#Name', `CancelDelete${unique}`);
  await page.fill('input#Brand', 'CancelBrand');
  await page.fill('input#DailyDose', '1 cap');
  await page.click('button[type="submit"]:has-text("Create")');

  let dialogFired = false;
  page.on('dialog', async dialog => {
    dialogFired = true;
    expect(dialog.message()).toMatch(/nutrients and prescribed doses/i);
    await dialog.dismiss();
  });
  const row = page.locator('table tbody tr', { hasText: `CancelDelete${unique}` });
  await row.locator('form button:has-text("Delete")').click();
  await expect(row).toBeVisible(); // dismissal prevented deletion
  expect(dialogFired).toBe(true); // regression guard: CSP must not block the confirm
});
```

Also strengthen `should delete a supplement`: capture `dialog.message()` before accepting and assert it matches `/nutrients and prescribed doses/i`.

- [ ] **Step 2: Run test to verify it fails**

Run: `npx playwright test supplement-crud -g "not delete when dismissed"`
Expected: FAIL — no dialog fires (inline `onsubmit` blocked by CSP).

- [ ] **Step 3: Create `delete-confirm.js`**

```js
(function () {
    document.querySelectorAll('form[data-confirm-message]').forEach(function (form) {
        form.addEventListener('submit', function (e) {
            if (!confirm(form.dataset.confirmMessage)) {
                e.preventDefault();
            }
        });
    });
})();
```

- [ ] **Step 4: Wire Supplement Index**

Replace line 54's form tag:

```cshtml
<form asp-action="Delete" asp-route-id="@s.Id" method="post" style="display:inline"
      data-confirm-message="Delete this supplement? Its nutrients and prescribed doses will also be deleted.">
```

Add near line 64:

```html
<script src="~/js/delete-confirm.js"></script>
```

Remove nothing else. The existing `should delete a supplement` spec already registers `page.on('dialog', ...)` before clicking, so accepting works unchanged.

- [ ] **Step 5: Run tests to verify they pass**

Run: `npx playwright test supplement-crud`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add VitaTrack.Web/wwwroot/js/delete-confirm.js VitaTrack.Web/Views/Supplement/Index.cshtml e2e-tests/playwright/tests/supplement-crud.spec.js
git commit -m "fix: replace CSP-blocked inline confirm with external JS on supplement delete"
```

---

### Task 2: Bulk cascade warnings via `data-cascade-warning`

**Files:**
- Modify: `VitaTrack.Web/wwwroot/js/delete-selected.js`
- Modify: `VitaTrack.Web/Views/Supplement/Index.cshtml:15`
- Modify: `VitaTrack.Web/Views/SupplementNutrient/Index.cshtml:26`

**Interfaces:**
- Consumes: none.
- Produces: bulk forms may carry `data-cascade-warning`; appended to confirm text by `delete-selected.js`.

- [ ] **Step 1: Write failing E2E test**

In `e2e-tests/playwright/tests/supplement-nutrient.spec.js`, add:

```js
test('should warn about blend children when bulk deleting nutrients', async ({ page, testInfo }) => {
  await page.goto('/Supplement');
  await page.locator('table tbody tr').first().locator('a:has-text("Nutrients")').click();

  let dialogMessage = '';
  page.on('dialog', async dialog => {
    dialogMessage = dialog.message();
    await dialog.dismiss();
  });

  const boxes = page.locator('.row-checkbox');
  await boxes.first().check();
  if (await boxes.count() > 1) { await boxes.nth(1).check(); }
  await page.click('#delete-selected-btn');

  expect(dialogMessage).toMatch(/deleted as well|child nutrients/i);
});
```

And in `supplement-crud.spec.js` bulk-delete test, assert captured dialog message matches `/prescribed doses/i`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `npx playwright test supplement-nutrient -g "bulk deleting nutrients" && npx playwright test supplement-crud -g "multiple supplements"`
Expected: FAIL — messages lack warning text.

- [ ] **Step 3: Extend `delete-selected.js`**

Replace the submit listener body's confirm line (keep zero-check guard):

```js
        let message = 'Delete ' + checked.length + ' selected ' + entityName + '(s)?';
        const cascadeWarning = form.dataset.cascadeWarning;
        if (cascadeWarning) message += ' ' + cascadeWarning;
        if (!confirm(message)) {
            e.preventDefault();
        }
```

- [ ] **Step 4: Add attributes**

`Views/Supplement/Index.cshtml` line 15:

```cshtml
<form asp-action="DeleteSelected" method="post" id="delete-selected-form" data-entity-name="supplement"
      data-cascade-warning="Their nutrients and prescribed doses will be deleted as well.">
```

`Views/SupplementNutrient/Index.cshtml` line 26:

```cshtml
<form asp-action="DeleteSelected" asp-route-supplementId="@supplement?.Id" method="post" id="delete-selected-form" data-entity-name="nutrient"
      data-cascade-warning="Child nutrients of selected blends will be deleted as well.">
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `npx playwright test supplement-crud supplement-nutrient`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add VitaTrack.Web/wwwroot/js/delete-selected.js VitaTrack.Web/Views/Supplement/Index.cshtml VitaTrack.Web/Views/SupplementNutrient/Index.cshtml e2e-tests/playwright/tests/supplement-crud.spec.js e2e-tests/playwright/tests/supplement-nutrient.spec.js
git commit -m "feat: cascade warnings in bulk delete confirms"
```

---

### Task 3: Blend-child cascade note on nutrient confirm page

**Files:**
- Modify: `VitaTrack.Web/Controllers/SupplementNutrientController.cs:102-110`
- Modify: `VitaTrack.Web/Views/SupplementNutrient/Delete.cshtml`

**Interfaces:**
- Consumes: `ISupplementNutrientRepository.GetBySupplementIdAsync(nutrient.SupplementId)` (exists).
- Produces: `ViewData["HasChildren"]` (bool).

- [ ] **Step 1: Write failing E2E test**

In `supplement-nutrient.spec.js` `should delete a nutrient` flow, create a blend parent with one child first, navigate to the parent's Delete page, assert the page shows text matching `/child nutrients will also be deleted/i`, then delete and assert both rows are gone. Sketch:

```js
// after creating parent "BlendParent<unique>" and child rows via Create form
// (child created with ParentNutrientId select pointing at the parent)
const parentRow = page.locator('table tbody tr', { hasText: `BlendParent${unique}` });
await parentRow.locator('a:has-text("Delete")').click();
await expect(page.locator('text=/child nutrients will also be deleted/i')).toBeVisible();
await page.click('input[type="submit"][value="Delete"]');
await expect(parentRow).toHaveCount(0);
await expect(page.locator('table tbody tr', { hasText: `BlendChild${unique}` })).toHaveCount(0);
```

(Reuse the spec's existing create helpers/patterns for the two rows rather than new plumbing.)

- [ ] **Step 2: Run test to verify it fails**

Run: `npx playwright test supplement-nutrient -g "delete a nutrient"`
Expected: FAIL — no cascade copy on the page.

- [ ] **Step 3: Controller change**

In GET `Delete(int id)` after fetching nutrient:

```csharp
var all = await _nutrientRepo.GetBySupplementIdAsync(nutrient.SupplementId);
ViewData["HasChildren"] = all.Any(n => n.ParentNutrientId == id);
```

- [ ] **Step 4: View change**

In `Views/SupplementNutrient/Delete.cshtml`, inside the confirmation block:

```cshtml
@if ((bool)(ViewData["HasChildren"] ?? false))
{
    <p class="text-danger">This blend's child nutrients will also be deleted.</p>
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `npx playwright test supplement-nutrient`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add VitaTrack.Web/Controllers/SupplementNutrientController.cs VitaTrack.Web/Views/SupplementNutrient/Delete.cshtml e2e-tests/playwright/tests/supplement-nutrient.spec.js
git commit -m "feat: show blend-child cascade warning on nutrient delete page"
```

---

### Task 4: Story map update

**Files:**
- Modify: `storymap.yaml` (MS-4 and MS-6 story test lists)

- [ ] **Step 1: Add test refs**

Under MS-4 "Remove a supplement with confirmation dialog":

```yaml
              - e2e: supplement-crud::should show confirm dialog and not delete when dismissed
```

Under MS-4 "Delete multiple supplements...": keep existing refs (message assertion folded into existing spec).

Under MS-6 "Delete a nutrient":

```yaml
              - e2e: supplement-nutrient::should warn about blend children when bulk deleting nutrients
```

(plus whatever spec name Task 3 lands on, referenced likewise)

- [ ] **Step 2: Verify consistency tests pass**

Run: `dotnet test --filter StoryMapConsistencyTests`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add storymap.yaml
git commit -m "docs: reference new delete-consistency e2e specs in story map"
```

---

### Task 5: Full verification

- [ ] **Step 1:** `./format-check.sh` → clean (or run `dotnet format VitaTrack.sln` first)
- [ ] **Step 2:** `dotnet build VitaTrack.sln && dotnet test` → green
- [ ] **Step 3:** `./test-e2e.sh` → green
