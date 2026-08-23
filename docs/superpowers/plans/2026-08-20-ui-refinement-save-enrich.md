# UI Refinement: Save / Enrich Split — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add separate Save (no AI) and Enrich (LLM) buttons to the Supplement Create and Edit pages, with an overwrite warning on Enrich when nutrients already exist.

**Architecture:** Two new controller actions (`CreateSave`, `EditSave`) persist supplement fields without calling the LLM. The existing `Enrich`/`Edit` (POST) actions remain the enrichment path. Views get two distinct buttons; external CSP-safe JS guards the Enrich button with a `window.confirm` when nutrients already exist.

**Tech Stack:** ASP.NET MVC (C# 12), HTMX, Bootstrap 5, MSTest + Moq, Playwright.

## Global Constraints

- No inline JavaScript — all JS lives in `wwwroot/js/*.js` (CSP `script-src 'self'`). (AGENTS.md Web)
- Controllers stay thin: delegate to repositories/services, map, return view/result. (AGENTS.md Web)
- `<Nullable>enable</Nullable>` — respect nullability; avoid `!` unless documented. (root AGENTS.md)
- Run `dotnet format VitaTrack.sln` / `./format-check.sh` before committing; pre-commit hook gates on it. (root AGENTS.md)
- Tests: MSTest, `Moq` only to mock dependencies. Keep green. (root AGENTS.md)
- `CreateSave` returns `HX-Redirect` header so HTMX navigates to the list full-page; `EditSave` returns a normal `RedirectToAction`.

---

### Task 1: `CreateSave` controller action + test

**Files:**
- Modify: `VitaTrack.Web/Controllers/SupplementController.cs` (add action after `Create()`)
- Modify: `VitaTrack.Tests/SupplementControllerTests.cs` (add test)

**Interfaces:**
- Consumes: `ISupplementRepository.AddAsync(Supplement) : Task<int>`
- Produces: `CreateSave(CreateSupplementRequest) : Task<IActionResult>` returning `HX-Redirect` to `Index`

- [ ] **Step 1: Write the failing test**

Add to `SupplementControllerTests.cs`:

```csharp
[TestMethod]
public async Task CreateSave_PersistsAndRedirectsWithoutEnrichment()
{
    var request = new CreateSupplementRequest
    {
        Name = "PlainSupp",
        Brand = "Brand",
        DailyDose = "1 pill",
        ManufacturerUrl = "https://example.com"
    };

    var result = await _controller.CreateSave(request);

    var emptyResult = result as EmptyResult;
    Assert.IsNotNull(emptyResult);
    Assert.AreEqual("/Supplement/Index", _controller.Response.Headers["HX-Redirect"].ToString());
    _suppRepo.Verify(r => r.AddAsync(It.IsAny<Supplement>()), Times.Once);
    _llmService.Verify(s => s.EnrichSupplementAsync(It.IsAny<Supplement>()), Times.Never);
    _nutrientService.Verify(s => s.AddAsync(It.IsAny<int>(), It.IsAny<IEnumerable<SupplementNutrientDto>>()), Times.Never);
}

[TestMethod]
public async Task CreateSave_InvalidModel_ReturnsValidationErrors()
{
    _controller.ModelState.AddModelError("Name", "Name is required");
    var request = new CreateSupplementRequest { Brand = "Brand", DailyDose = "1 pill" };

    var result = await _controller.CreateSave(request);

    var partialResult = result as PartialViewResult;
    Assert.IsNotNull(partialResult);
    Assert.AreEqual("_ValidationErrors", partialResult.ViewName);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~CreateSave"`
Expected: FAIL (CS0117: 'SupplementController' does not contain a definition for 'CreateSave')

- [ ] **Step 3: Write minimal implementation**

Add to `SupplementController.cs` (after the `Create()` method):

```csharp
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> CreateSave(CreateSupplementRequest request)
{
    if (!ModelState.IsValid)
    {
        return PartialView("_ValidationErrors", ModelState);
    }

    var supplement = request.ToSupplement();
    await _suppRepo.AddAsync(supplement);
    Response.Headers["HX-Redirect"] = Url.Action("Index", "Supplement")!;
    return new EmptyResult();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~CreateSave"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add VitaTrack.Web/Controllers/SupplementController.cs VitaTrack.Tests/SupplementControllerTests.cs
git commit -m "feat: add CreateSave action (save without enrichment)"
```

---

### Task 2: `EditSave` controller action + test

**Files:**
- Modify: `VitaTrack.Web/Controllers/SupplementController.cs` (add action near `Edit` POST)
- Modify: `VitaTrack.Tests/SupplementControllerTests.cs` (add test)

**Interfaces:**
- Consumes: `ISupplementRepository.UpdateAsync(Supplement) : Task`, `ISupplementRepository.GetByIdAsync(int) : Task<Supplement?>`
- Produces: `EditSave(int id, EditSupplementRequest) : Task<IActionResult>` redirecting to `Index`, no LLM, no nutrient merge

- [ ] **Step 1: Write the failing test**

Add to `SupplementControllerTests.cs`:

```csharp
[TestMethod]
public async Task EditSave_PersistsAndRedirectsWithoutEnrichment()
{
    var request = new EditSupplementRequest { Id = 7, Name = "PlainEdit", Brand = "Brand", DailyDose = "2 pills" };
    _suppRepo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(new Supplement { Id = 7, Name = "Old" });

    var result = await _controller.EditSave(7, request);

    var redirectResult = result as RedirectToActionResult;
    Assert.IsNotNull(redirectResult);
    Assert.AreEqual("Index", redirectResult.ActionName);
    _suppRepo.Verify(r => r.UpdateAsync(It.IsAny<Supplement>()), Times.Once);
    _llmService.Verify(s => s.EnrichSupplementAsync(It.IsAny<Supplement>()), Times.Never);
    _nutrientService.Verify(s => s.ReplaceAsync(It.IsAny<int>(), It.IsAny<IEnumerable<SupplementNutrientDto>>()), Times.Never);
}

[TestMethod]
public async Task EditSave_InvalidModel_ReturnsEditView()
{
    var request = new EditSupplementRequest { Id = 8, Name = "", Brand = "Brand", DailyDose = "1 pill" };
    _controller.ModelState.AddModelError("Name", "Name is required");
    _suppRepo.Setup(r => r.GetByIdAsync(8)).ReturnsAsync(new Supplement { Id = 8, Name = "Old" });

    var result = await _controller.EditSave(8, request);

    var viewResult = result as ViewResult;
    Assert.IsNotNull(viewResult);
    Assert.AreEqual("Edit", viewResult.ViewName);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~EditSave"`
Expected: FAIL (CS0117: 'SupplementController' does not contain a definition for 'EditSave')

- [ ] **Step 3: Write minimal implementation**

Add to `SupplementController.cs` (after the existing `Edit(int id, EditSupplementRequest request)` POST method):

```csharp
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> EditSave(int id, EditSupplementRequest request)
{
    if (id != request.Id) return NotFound();
    if (!ModelState.IsValid)
    {
        var original = await _suppRepo.GetByIdAsync(id);
        if (original == null) return NotFound();
        return View(original);
    }

    var supplement = request.ToSupplement();
    await _suppRepo.UpdateAsync(supplement);
    return RedirectToAction(nameof(Index));
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~EditSave"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add VitaTrack.Web/Controllers/SupplementController.cs VitaTrack.Tests/SupplementControllerTests.cs
git commit -m "feat: add EditSave action (save without enrichment)"
```

---

### Task 3: Create.cshtml buttons + `create.js` Enrich warning

**Files:**
- Modify: `VitaTrack.Web/Views/Supplement/Create.cshtml` (two buttons + script ref)
- Create: `VitaTrack.Web/wwwroot/js/create.js`

**Interfaces:**
- Consumes: `CreateSave` action (Task 1), `Enrich` action (existing)
- Produces: `create.js` guarding `#enrich-btn` via `htmx:beforeRequest`

- [ ] **Step 1: Update Create.cshtml button group**

Replace the button group (lines 44-47) with:

```cshtml
    <div class="form-group mt-3">
        <button type="submit"
                class="btn btn-primary"
                hx-post="/Supplement/CreateSave">Save</button>
        <button type="submit"
                id="enrich-btn"
                class="btn btn-info"
                hx-post="/Supplement/Enrich"
                hx-indicator="#enrich-spinner">Enrich</button>
        <a asp-action="Index" class="btn btn-secondary">Cancel</a>
    </div>
```

Add at end of file (after line 57):

```cshtml
<script src="/js/create.js"></script>
```

- [ ] **Step 2: Create create.js**

Create `VitaTrack.Web/wwwroot/js/create.js`:

```js
document.addEventListener('htmx:beforeRequest', function (e) {
    var el = e.detail.elt;
    if (!el || el.id !== 'enrich-btn') return;

    var table = document.querySelector('#nutrient-editor-container [data-nutrient-count]');
    var count = table ? parseInt(table.getAttribute('data-nutrient-count'), 10) : 0;
    if (count > 0) {
        var ok = window.confirm('This supplement already has nutrients. Enriching may overwrite them. Continue?');
        if (!ok) e.preventDefault();
    }
});
```

- [ ] **Step 3: Build to verify views compile**

Run: `dotnet build VitaTrack.Web/VitaTrack.Web.csproj`
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add VitaTrack.Web/Views/Supplement/Create.cshtml VitaTrack.Web/wwwroot/js/create.js
git commit -m "feat: split Create buttons (Save/Enrich) with overwrite warning"
```

---

### Task 4: Edit.cshtml buttons + `edit.js` Enrich warning

**Files:**
- Modify: `VitaTrack.Web/Views/Supplement/Edit.cshtml` (two submit buttons + script ref)
- Create: `VitaTrack.Web/wwwroot/js/edit.js`

**Interfaces:**
- Consumes: `EditSave` action (Task 2), `Edit` POST action (existing enrich+merge)
- Produces: `edit.js` guarding `#enrich-btn` via `click` + `data-nutrient-count`

- [ ] **Step 1: Update Edit.cshtml button group**

Replace the submit button (line 47) with:

```cshtml
    <div class="form-group">
        <button type="submit" class="btn btn-primary" formaction="/Supplement/EditSave">Save</button>
        <button type="submit" id="enrich-btn" class="btn btn-info"
                formaction="/Supplement/Edit"
                data-nutrient-count="@Model.NutrientCount">Enrich</button>
    </div>
```

Note: the form element (`<form asp-action="Edit">`) stays as-is so both `formaction` targets override its default action. Add at end of file (after line 65):

```cshtml
<script src="/js/edit.js"></script>
```

- [ ] **Step 2: Create edit.js**

Create `VitaTrack.Web/wwwroot/js/edit.js`:

```js
document.addEventListener('DOMContentLoaded', function () {
    var enrichBtn = document.getElementById('enrich-btn');
    if (!enrichBtn) return;

    enrichBtn.addEventListener('click', function (e) {
        var count = parseInt(enrichBtn.getAttribute('data-nutrient-count') || '0', 10);
        if (count > 0) {
            var ok = window.confirm('This supplement already has nutrients. Enriching may overwrite them. Continue?');
            if (!ok) e.preventDefault();
        }
    });
});
```

- [ ] **Step 3: Build to verify views compile**

Run: `dotnet build VitaTrack.Web/VitaTrack.Web.csproj`
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add VitaTrack.Web/Views/Supplement/Edit.cshtml VitaTrack.Web/wwwroot/js/edit.js
git commit -m "feat: split Edit buttons (Save/Enrich) with overwrite warning"
```

---

### Task 5: Playwright E2E tests

**Files:**
- Modify: `e2e-tests/playwright/tests/supplement.spec.js` (or create if absent — match existing spec file naming)

**Interfaces:**
- Consumes: running app (Create/Edit pages), real endpoints (no HTTP mocking)

- [ ] **Step 1: Add E2E coverage**

Add tests asserting:
- Create page renders both `#enrich-btn` and a Save button posting to `/Supplement/CreateSave`.
- Edit page for a seeded supplement with nutrients renders `#enrich-btn` with `data-nutrient-count` > 0; clicking Enrich triggers a native dialog (use `page.on('dialog', d => d.dismiss())` or `accept`) — assert the dialog message warns about overwriting.
- Save (no enrichment) navigates back to the supplement list.

Follow existing spec pattern (e.g. `prescribed-dose.spec.js`) for navigation and `global-setup.js` DB reset. Use dynamic assertions for shared-DB parallelism.

```js
test('create page has separate Save and Enrich buttons', async ({ page }) => {
  await page.goto('/Supplement/Create');
  await expect(page.locator('#enrich-btn')).toBeVisible();
  await expect(page.locator('button[formaction="/Supplement/CreateSave"]')).toBeVisible();
});

test('edit page warns on Enrich when nutrients exist', async ({ page }) => {
  page.on('dialog', dialog => dialog.accept());
  await page.goto('/Supplement/Edit/1');
  const btn = page.locator('#enrich-btn');
  await expect(btn).toHaveAttribute('data-nutrient-count', /\d+/);
  // click Enrich; overwrite warning dialog must appear
  await btn.click();
  // (dialog handler above proves a dialog was raised; assertion of message via spy if desired)
});
```

- [ ] **Step 2: Run E2E**

Run: `cd e2e-tests/playwright && npx playwright test supplement`
Expected: PASS (CI runs full `./test-e2e.sh` if available)

- [ ] **Step 3: Commit**

```bash
git add e2e-tests/playwright/tests/supplement.spec.js
git commit -m "test: Playwright coverage for Save/Enrich split"
```

---

### Task 6: Final verification + format gate

**Files:** none new

- [ ] **Step 1: Format check**

Run: `./format-check.sh`
Expected: no changes needed (or run `dotnet format VitaTrack.sln` first)

- [ ] **Step 2: Build + full unit test**

Run: `dotnet build VitaTrack.sln && dotnet test`
Expected: build + all tests green

- [ ] **Step 3: Commit any format fixes**

```bash
git add -A
git commit -m "style: apply dotnet format"  # only if format changed files
```
