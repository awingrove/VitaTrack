# HTMX Supplement Enrichment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the full-page POST create flow with an HTMX single-page experience: Save → spinner → nutrient editor appears inline, auto-saved to DB.

**Architecture:** The Create form uses `hx-post` to submit to a new `Enrich` action. The server saves the supplement, runs LLM enrichment, saves nutrients, and returns a `_NutrientEditor` partial view. A `Save Changes` button in the partial HTMX-POSTs to `UpdateNutrients` for subsequent edits.

**Tech Stack:** ASP.NET MVC, HTMX 2.0.4 (CDN), Bootstrap 5, Dapper, SQLite

## Global Constraints

- HTMX loaded from CDN: `https://unpkg.com/htmx.org@2.0.4`
- Partial views use `@model` with the new `SupplementEditorViewModel`
- Anti-forgery tokens included automatically by HTMX when submitting forms
- Nutrient editor JS (add/remove/reindex) lives inline in `_NutrientEditor.cshtml` since it's loaded via HTMX partial (external JS files don't execute on `innerHTML` swap unless explicitly configured)
- Existing `Review.cshtml` and `review.js` kept for Edit flow (out of scope)

---

### Task 1: Add HTMX CDN to _Layout.cshtml

**Files:**
- Modify: `VitaTrack.Web/Views/Shared/_Layout.cshtml:37-40`

**Interfaces:**
- Produces: HTMX globally available on all pages

- [ ] **Step 1: Add HTMX script tag after Bootstrap bundle**

Add the HTMX CDN script immediately after the Bootstrap bundle script (line 40):

```html
    <!-- Bootstrap bundle (includes Popper) -->
    <script src="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js"
            integrity="sha384-YvpcrYf0tY3lHB60NNkmXc5s9fDVZLESaAA55NDzOxhy9GkcIdslK1eN7N6jIeHz"
            crossorigin="anonymous"></script>

    <!-- HTMX -->
    <script src="https://unpkg.com/htmx.org@2.0.4"></script>
```

- [ ] **Step 2: Verify build succeeds**

Run: `dotnet build VitaTrack.sln`
Expected: Build succeeds (no compilation changes, just HTML)

- [ ] **Step 3: Commit**

```bash
git add VitaTrack.Web/Views/Shared/_Layout.cshtml
git commit -m "feat: add HTMX CDN to shared layout"
```

---

### Task 2: Create SupplementEditorViewModel

**Files:**
- Create: `VitaTrack.Web/Models/SupplementEditorViewModel.cs`

**Interfaces:**
- Produces: `SupplementEditorViewModel` used by `_NutrientEditor.cshtml` and controller actions

- [ ] **Step 1: Create the Models directory and view model file**

```csharp
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Web.Models;

public class SupplementEditorViewModel
{
    public int SupplementId { get; set; }
    public string SupplementName { get; set; } = string.Empty;
    public List<SupplementNutrientDto> Nutrients { get; set; } = new();
    public string? SwapSuggestion { get; set; }
    public string? ExtractionError { get; set; }
    public bool SaveSuccess { get; set; }
}
```

- [ ] **Step 2: Verify build succeeds**

Run: `dotnet build VitaTrack.sln`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add VitaTrack.Web/Models/SupplementEditorViewModel.cs
git commit -m "feat: add SupplementEditorViewModel for HTMX nutrient editor"
```

---

### Task 3: Create _NutrientEditor.cshtml partial view

**Files:**
- Create: `VitaTrack.Web/Views/Supplement/_NutrientEditor.cshtml`

**Interfaces:**
- Consumes: `SupplementEditorViewModel` (from controller partial view results)
- Produces: HTML with nutrient table, Save Changes button (hx-post to UpdateNutrients), inline JS for add/remove/reindex

- [ ] **Step 1: Create the partial view**

```html
@using VitaTrack.Web.Models
@model SupplementEditorViewModel

@if (Model.SaveSuccess)
{
    <div class="alert alert-success alert-dismissible fade show" role="alert">
        Nutrients saved successfully.
        <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>
    </div>
}

@if (!string.IsNullOrWhiteSpace(Model.ExtractionError))
{
    <div class="alert alert-info">
        <strong>Note:</strong> @Model.ExtractionError
        <br /><small>You can manually add nutrients below.</small>
    </div>
}

<h4>Nutrients for @Model.SupplementName</h4>
<p class="text-muted">Edit the nutrient breakdown below. You can add or remove rows.</p>

<form id="nutrient-editor-form">
    @Html.AntiForgeryToken()
    <input type="hidden" name="supplementId" value="@Model.SupplementId" />
    <table class="table" id="nutrients-table" data-nutrient-count="@(Model.Nutrients.Count)">
        <thead>
            <tr>
                <th>Generic Name</th>
                <th>Specific Form</th>
                <th>Dosage</th>
                <th></th>
            </tr>
        </thead>
        <tbody>
            @if (Model.Nutrients.Count > 0)
            {
                for (int i = 0; i < Model.Nutrients.Count; i++)
                {
                    <tr>
                        <td><input name="nutrients[@i].GenericName" value="@Model.Nutrients[i].GenericName" class="form-control" /></td>
                        <td><input name="nutrients[@i].SpecificForm" value="@Model.Nutrients[i].SpecificForm" class="form-control" /></td>
                        <td><input name="nutrients[@i].Dosage" value="@Model.Nutrients[i].Dosage" class="form-control" /></td>
                        <td><button type="button" class="btn btn-sm btn-danger remove-row">Remove</button></td>
                    </tr>
                }
            }
            else
            {
                <tr class="empty-row">
                    <td colspan="4" class="text-muted text-center">No nutrients. Click "Add Nutrient" to start.</td>
                </tr>
            }
        </tbody>
    </table>
    <button type="button" class="btn btn-sm btn-success" id="add-nutrient-row">Add Nutrient</button>

    <div class="form-group mt-3">
        <button type="button" class="btn btn-primary"
                hx-post="/Supplement/UpdateNutrients"
                hx-include="#nutrient-editor-form"
                hx-target="#nutrient-editor-container"
                hx-swap="innerHTML">
            Save Changes
        </button>
        <a href="/Supplement/Index" class="btn btn-secondary">Done</a>
    </div>
</form>

<script>
    (function () {
        const table = document.getElementById('nutrients-table');
        if (!table) return;

        let nutrientIndex = parseInt(table.dataset.nutrientCount || '0', 10);

        document.getElementById('add-nutrient-row').addEventListener('click', function () {
            const tbody = table.querySelector('tbody');
            const emptyRow = tbody.querySelector('.empty-row');
            if (emptyRow) emptyRow.remove();

            const tr = document.createElement('tr');
            tr.innerHTML =
                '<td><input name="nutrients[' + nutrientIndex + '].GenericName" class="form-control" /></td>' +
                '<td><input name="nutrients[' + nutrientIndex + '].SpecificForm" class="form-control" /></td>' +
                '<td><input name="nutrients[' + nutrientIndex + '].Dosage" class="form-control" /></td>' +
                '<td><button type="button" class="btn btn-sm btn-danger remove-row">Remove</button></td>';
            tbody.appendChild(tr);
            nutrientIndex++;

            tr.querySelector('.remove-row').addEventListener('click', function () {
                tr.remove();
                reindexNutrients();
            });
        });

        document.querySelectorAll('.remove-row').forEach(function (btn) {
            btn.addEventListener('click', function () {
                btn.closest('tr').remove();
                reindexNutrients();
            });
        });

        function reindexNutrients() {
            var rows = table.querySelectorAll('tbody tr:not(.empty-row)');
            rows.forEach(function (row, index) {
                row.querySelectorAll('input').forEach(function (input) {
                    var name = input.getAttribute('name');
                    if (name && name.startsWith('nutrients[')) {
                        var field = name.split('.')[1];
                        input.setAttribute('name', 'nutrients[' + index + '].' + field);
                    }
                });
            });

            table.dataset.nutrientCount = rows.length;

            if (rows.length === 0) {
                var tbody = table.querySelector('tbody');
                var emptyRow = document.createElement('tr');
                emptyRow.className = 'empty-row';
                emptyRow.innerHTML = '<td colspan="4" class="text-muted text-center">No nutrients. Click "Add Nutrient" to start.</td>';
                tbody.appendChild(emptyRow);
            }
        }
    })();
</script>
```

- [ ] **Step 2: Verify build succeeds**

Run: `dotnet build VitaTrack.sln`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add VitaTrack.Web/Views/Supplement/_NutrientEditor.cshtml
git commit -m "feat: add _NutrientEditor partial view with HTMX save"
```

---

### Task 4: Create _ValidationErrors.cshtml partial view

**Files:**
- Create: `VitaTrack.Web/Views/Supplement/_ValidationErrors.cshtml`

**Interfaces:**
- Consumes: `ModelStateDictionary` (from controller validation)
- Produces: Bootstrap alert with validation error list, uses `hx-swap-oob="true"` to target `#validation-errors` div in Create.cshtml

- [ ] **Step 1: Create the partial view**

```html
@model Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary

<div id="validation-errors" hx-swap-oob="true">
    <div class="alert alert-danger">
        <strong>Please fix the following errors:</strong>
        <ul class="mb-0">
            @foreach (var entry in Model)
            {
                foreach (var error in entry.Value.Errors)
                {
                    <li>@error.ErrorMessage</li>
                }
            }
        </ul>
    </div>
</div>
```

- [ ] **Step 2: Verify build succeeds**

Run: `dotnet build VitaTrack.sln`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add VitaTrack.Web/Views/Supplement/_ValidationErrors.cshtml
git commit -m "feat: add _ValidationErrors partial view"
```

---

### Task 5: Update Create.cshtml with HTMX attributes

**Files:**
- Modify: `VitaTrack.Web/Views/Supplement/Create.cshtml`

**Interfaces:**
- Consumes: HTMX global (from Task 1)
- Produces: Form that HTMX-POSTs to `/Supplement/Enrich`, spinner, target container, validation area

- [ ] **Step 1: Replace Create.cshtml content**

Replace the entire file with:

```html
@{
    ViewData["Title"] = "Create";
    Layout = "_Layout";
}
@using VitaTrack.Infrastructure.Models
@model Supplement

<h2>Create Supplement</h2>
<p class="text-muted">Enter supplement details. If a manufacturer URL is provided, nutrients will be auto-extracted via AI.</p>

<div id="validation-errors"></div>

<form id="create-supplement-form"
      hx-post="/Supplement/Enrich"
      hx-indicator="#enrich-spinner"
      hx-target="#nutrient-editor-container"
      hx-swap="innerHTML">
    @Html.AntiForgeryToken()
    <div class="form-group">
        <label asp-for="Name" class="control-label"></label>
        <input asp-for="Name" class="form-control" />
        <span asp-validation-for="Name" class="text-danger"></span>
    </div>
    <div class="form-group">
        <label asp-for="Brand" class="control-label"></label>
        <input asp-for="Brand" class="form-control" />
        <span asp-validation-for="Brand" class="text-danger"></span>
    </div>
    <div class="form-group">
        <label asp-for="DailyDose" class="control-label"></label>
        <input asp-for="DailyDose" class="form-control" />
        <span asp-validation-for="DailyDose" class="text-danger"></span>
    </div>
    <div class="form-group">
        <label asp-for="ManufacturerUrl" class="control-label"></label>
        <input asp-for="ManufacturerUrl" class="form-control" />
        <span asp-validation-for="ManufacturerUrl" class="text-danger"></span>
    </div>
    <div class="form-group">
        <label asp-for="Cost" class="control-label"></label>
        <input asp-for="Cost" class="form-control" type="number" step="0.01" />
        <span asp-validation-for="Cost" class="text-danger"></span>
    </div>
    <div class="form-group mt-3">
        <button type="submit" class="btn btn-primary">Save</button>
        <a asp-action="Index" class="btn btn-secondary">Cancel</a>
    </div>
</form>

<div id="enrich-spinner" class="htmx-indicator text-center my-4">
    <div class="spinner-border text-primary" role="status">
        <span class="visually-hidden">Extracting nutrients...</span>
    </div>
    <p class="mt-2 text-muted">Extracting nutrients from manufacturer URL...</p>
</div>

<div id="nutrient-editor-container"></div>
```

- [ ] **Step 2: Verify build succeeds**

Run: `dotnet build VitaTrack.sln`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add VitaTrack.Web/Views/Supplement/Create.cshtml
git commit -m "feat: add HTMX attributes to supplement create form"
```

---

### Task 6: Add Enrich and UpdateNutrients controller actions

**Files:**
- Modify: `VitaTrack.Web/Controllers/SupplementController.cs`

**Interfaces:**
- Consumes: `ILlmService.EnrichSupplementAsync`, `ISupplementRepository.AddAsync`, `ISupplementNutrientRepository.AddAsync`, `ISupplementNutrientRepository.GetBySupplementIdAsync`, `ISupplementNutrientRepository.DeleteAsync`
- Produces: `PartialView("_NutrientEditor", SupplementEditorViewModel)`, `PartialView("_ValidationErrors", ModelStateDictionary)`

- [ ] **Step 1: Add using directive for view model**

Add at the top of `SupplementController.cs`:

```csharp
using VitaTrack.Web.Models;
```

- [ ] **Step 2: Add the Enrich action**

Add after the `Create` GET action (after line 28):

```csharp
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Enrich(Supplement supplement)
{
    if (!ModelState.IsValid)
    {
        return PartialView("_ValidationErrors", ModelState);
    }

    var llmResult = new LlmResult();
    try
    {
        llmResult = await _llmService.EnrichSupplementAsync(supplement);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "LLM enrichment failed for supplement {Name}", supplement.Name);
        llmResult.ExtractionError = "Could not reach enrichment service. You can add nutrients manually.";
    }

    supplement.NutritionJson = llmResult.NutritionJson;
    supplement.SwapSuggestion = llmResult.SwapSuggestion;

    var newId = await _suppRepo.AddAsync(supplement);

    if (llmResult.Nutrients != null)
    {
        foreach (var n in llmResult.Nutrients.Where(n => !string.IsNullOrWhiteSpace(n.GenericName)))
        {
            try
            {
                await _nutrientRepo.AddAsync(new SupplementNutrient
                {
                    SupplementId = newId,
                    GenericName = n.GenericName,
                    SpecificForm = n.SpecificForm,
                    Dosage = n.Dosage
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add nutrient {GenericName} for supplement {SupplementId}", n.GenericName, newId);
            }
        }
    }

    var viewModel = new SupplementEditorViewModel
    {
        SupplementId = newId,
        SupplementName = supplement.Name,
        Nutrients = llmResult.Nutrients ?? new List<SupplementNutrientDto>(),
        SwapSuggestion = llmResult.SwapSuggestion,
        ExtractionError = llmResult.ExtractionError
    };

    return PartialView("_NutrientEditor", viewModel);
}
```

- [ ] **Step 3: Add the UpdateNutrients action**

Add after the `Enrich` action:

```csharp
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> UpdateNutrients(int supplementId, List<SupplementNutrientDto> nutrients)
{
    var supplement = await _suppRepo.GetByIdAsync(supplementId);
    if (supplement == null) return NotFound();

    var existingNutrients = await _nutrientRepo.GetBySupplementIdAsync(supplementId);
    foreach (var existing in existingNutrients)
    {
        await _nutrientRepo.DeleteAsync(existing.Id);
    }

    if (nutrients != null)
    {
        foreach (var n in nutrients.Where(n => !string.IsNullOrWhiteSpace(n.GenericName)))
        {
            try
            {
                await _nutrientRepo.AddAsync(new SupplementNutrient
                {
                    SupplementId = supplementId,
                    GenericName = n.GenericName,
                    SpecificForm = n.SpecificForm,
                    Dosage = n.Dosage
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add nutrient {GenericName} for supplement {SupplementId}", n.GenericName, supplementId);
            }
        }
    }

    var savedNutrients = await _nutrientRepo.GetBySupplementIdAsync(supplementId);
    var viewModel = new SupplementEditorViewModel
    {
        SupplementId = supplementId,
        SupplementName = supplement.Name,
        Nutrients = savedNutrients.Select(sn => new SupplementNutrientDto
        {
            GenericName = sn.GenericName,
            SpecificForm = sn.SpecificForm,
            Dosage = sn.Dosage
        }).ToList(),
        SaveSuccess = true
    };

    return PartialView("_NutrientEditor", viewModel);
}
```

- [ ] **Step 4: Verify build succeeds**

Run: `dotnet build VitaTrack.sln`
Expected: Build succeeds

- [ ] **Step 5: Commit**

```bash
git add VitaTrack.Web/Controllers/SupplementController.cs
git commit -m "feat: add Enrich and UpdateNutrients controller actions"
```

---

### Task 7: Remove old create/confirm actions from controller

**Files:**
- Modify: `VitaTrack.Web/Controllers/SupplementController.cs`

**Interfaces:**
- Removes: `Create` (POST, lines 30-46), `ConfirmCreate` (lines 48-82), `ConfirmEdit` (lines 144-186)
- Keeps: `Create` (GET), `Edit` (GET + POST), `Delete`, `DeleteSelected`, `Index`

- [ ] **Step 1: Remove the POST Create action**

Remove lines 30-46 (the `[HttpPost] Create(Supplement)` method).

- [ ] **Step 2: Remove the ConfirmCreate action**

Remove lines 48-82 (the `[HttpPost] ConfirmCreate` method).

- [ ] **Step 3: Remove the ConfirmEdit action**

Remove lines 144-186 (the `[HttpPost] ConfirmEdit` method).

- [ ] **Step 4: Verify build succeeds**

Run: `dotnet build VitaTrack.sln`
Expected: Build succeeds (no remaining references to removed actions in non-Edit views)

- [ ] **Step 5: Commit**

```bash
git add VitaTrack.Web/Controllers/SupplementController.cs
git commit -m "refactor: remove old Create/ConfirmCreate/ConfirmEdit actions"
```

---

### Task 8: Update controller tests

**Files:**
- Modify: `VitaTrack.Tests/SupplementControllerTests.cs`

**Interfaces:**
- Consumes: `SupplementController` with new `Enrich` and `UpdateNutrients` actions
- Tests: Enrich (with URL, without URL, invalid model, LLM exception), UpdateNutrients (success, supplement not found)

- [ ] **Step 1: Replace Create_Post test with Enrich tests**

Remove the `Create_Post_EnrichesAndRedirectsToReview` test method (lines 63-86). Replace with:

```csharp
[TestMethod]
public async Task Enrich_WithUrl_SavesAndReturnsNutrientEditor()
{
    var supplement = new Supplement { Name = "TestSupp", Brand = "Brand", DailyDose = "1 pill", ManufacturerUrl = "https://example.com" };
    var llmResult = new LlmResult
    {
        NutritionJson = "{}",
        SwapSuggestion = "Try X",
        Nutrients =
        [
            new() { GenericName = "Vitamin C", SpecificForm = "Ascorbic Acid", Dosage = "500mg" }
        ]
    };
    _llmService.Setup(s => s.EnrichSupplementAsync(It.IsAny<Supplement>())).ReturnsAsync(llmResult);
    _suppRepo.Setup(r => r.AddAsync(It.IsAny<Supplement>())).ReturnsAsync(42);
    _nutrientRepo.Setup(r => r.AddAsync(It.IsAny<SupplementNutrient>())).ReturnsAsync(1);

    var result = await _controller.Enrich(supplement);

    var partialResult = result as PartialViewResult;
    Assert.IsNotNull(partialResult);
    Assert.AreEqual("_NutrientEditor", partialResult.ViewName);
    var model = partialResult.Model as SupplementEditorViewModel;
    Assert.IsNotNull(model);
    Assert.AreEqual(42, model.SupplementId);
    Assert.AreEqual("TestSupp", model.SupplementName);
    Assert.AreEqual(1, model.Nutrients.Count);
    Assert.AreEqual("Vitamin C", model.Nutrients[0].GenericName);
    _suppRepo.Verify(r => r.AddAsync(It.IsAny<Supplement>()), Times.Once);
}

[TestMethod]
public async Task Enrich_WithoutUrl_SavesAndReturnsEmptyEditor()
{
    var supplement = new Supplement { Name = "TestSupp", Brand = "Brand", DailyDose = "1 pill" };
    _suppRepo.Setup(r => r.AddAsync(It.IsAny<Supplement>())).ReturnsAsync(43);

    var result = await _controller.Enrich(supplement);

    var partialResult = result as PartialViewResult;
    Assert.IsNotNull(partialResult);
    Assert.AreEqual("_NutrientEditor", partialResult.ViewName);
    var model = partialResult.Model as SupplementEditorViewModel;
    Assert.IsNotNull(model);
    Assert.AreEqual(43, model.SupplementId);
    Assert.AreEqual(0, model.Nutrients.Count);
    _llmService.Verify(s => s.EnrichSupplementAsync(It.IsAny<Supplement>()), Times.Never);
}

[TestMethod]
public async Task Enrich_LlmException_SavesAndReturnsEditorWithError()
{
    var supplement = new Supplement { Name = "TestSupp", Brand = "Brand", DailyDose = "1 pill", ManufacturerUrl = "https://example.com" };
    _llmService.Setup(s => s.EnrichSupplementAsync(It.IsAny<Supplement>())).ThrowsAsync(new Exception("LLM down"));
    _suppRepo.Setup(r => r.AddAsync(It.IsAny<Supplement>())).ReturnsAsync(44);

    var result = await _controller.Enrich(supplement);

    var partialResult = result as PartialViewResult;
    Assert.IsNotNull(partialResult);
    var model = partialResult.Model as SupplementEditorViewModel;
    Assert.IsNotNull(model);
    Assert.IsNotNull(model.ExtractionError);
    Assert.IsTrue(model.ExtractionError.Contains("manually"));
    _suppRepo.Verify(r => r.AddAsync(It.IsAny<Supplement>()), Times.Once);
}

[TestMethod]
public async Task Enrich_InvalidModel_ReturnsValidationErrors()
{
    _controller.ModelState.AddModelError("Name", "Name is required");
    var supplement = new Supplement { Brand = "Brand", DailyDose = "1 pill" };

    var result = await _controller.Enrich(supplement);

    var partialResult = result as PartialViewResult;
    Assert.IsNotNull(partialResult);
    Assert.AreEqual("_ValidationErrors", partialResult.ViewName);
}
```

- [ ] **Step 2: Replace ConfirmCreate test with UpdateNutrients tests**

Remove the `ConfirmCreate_AddsSupplementAndNutrients` test method (lines 89-101). Replace with:

```csharp
[TestMethod]
public async Task UpdateNutrients_DeletesOldAndInsertsNew()
{
    var supplement = new Supplement { Id = 42, Name = "TestSupp", Brand = "Brand", DailyDose = "1 pill" };
    var existingNutrients = new List<SupplementNutrient>
    {
        new() { Id = 10, SupplementId = 42, GenericName = "Old", SpecificForm = "Form", Dosage = "10mg" }
    };
    _suppRepo.Setup(r => r.GetByIdAsync(42)).ReturnsAsync(supplement);
    _nutrientRepo.Setup(r => r.GetBySupplementIdAsync(42)).ReturnsAsync(existingNutrients);
    _nutrientRepo.Setup(r => r.DeleteAsync(10)).ReturnsAsync(1);
    _nutrientRepo.Setup(r => r.AddAsync(It.IsAny<SupplementNutrient>())).ReturnsAsync(1);

    var nutrients = new List<SupplementNutrientDto>
    {
        new() { GenericName = "Zinc", SpecificForm = "Citrate", Dosage = "15mg" }
    };

    var result = await _controller.UpdateNutrients(42, nutrients);

    var partialResult = result as PartialViewResult;
    Assert.IsNotNull(partialResult);
    Assert.AreEqual("_NutrientEditor", partialResult.ViewName);
    var model = partialResult.Model as SupplementEditorViewModel;
    Assert.IsNotNull(model);
    Assert.IsTrue(model.SaveSuccess);
    _nutrientRepo.Verify(r => r.DeleteAsync(10), Times.Once);
    _nutrientRepo.Verify(r => r.AddAsync(It.IsAny<SupplementNutrient>()), Times.Once);
}

[TestMethod]
public async Task UpdateNutrients_SupplementNotFound_ReturnsNotFound()
{
    _suppRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((Supplement?)null);

    var result = await _controller.UpdateNutrients(99, new List<SupplementNutrientDto>());

    Assert.IsInstanceOfType(result, typeof(NotFoundResult));
}
```

- [ ] **Step 3: Remove ConfirmEdit tests**

Remove the `ConfirmEdit_UpdatesSupplementAndReplacesNutrients` test (lines 165-183) and `ConfirmEdit_ReturnsNotFoundWhenIdMismatch` test (lines 185-193).

- [ ] **Step 4: Add using directive for view model**

Add at the top of the test file:

```csharp
using VitaTrack.Web.Models;
```

- [ ] **Step 5: Run tests to verify**

Run: `dotnet test`
Expected: All tests pass

- [ ] **Step 6: Commit**

```bash
git add VitaTrack.Tests/SupplementControllerTests.cs
git commit -m "test: update controller tests for HTMX Enrich/UpdateNutrients actions"
```

---

### Task 9: Verify full build and test suite

**Files:**
- None (verification only)

- [ ] **Step 1: Build the solution**

Run: `dotnet build VitaTrack.sln`
Expected: Build succeeds with no errors

- [ ] **Step 2: Run all tests**

Run: `dotnet test`
Expected: All tests pass

- [ ] **Step 3: Run Playwright E2E tests (if applicable)**

Run: `cd e2e-tests/playwright && npx playwright test`
Expected: Existing E2E tests still pass (supplement create flow may need E2E updates — flag if failures occur)

- [ ] **Step 4: Commit any E2E test fixes if needed**

If E2E tests fail due to the changed create flow, update them and commit:

```bash
git add e2e-tests/playwright/
git commit -m "test: update E2E tests for HTMX supplement create flow"
```
