# Nutrient Blends + Optional Dosage — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow blend nutrients (parent with total dosage + grouped sub-nutrients) and let sub-nutrients omit dosage; make LLM enrichment emit blends.

**Architecture:** Self-referential `ParentNutrientId` on `SupplementNutrient` (no new tables). DTO gains `Children`/`ParentNutrientId`. Service persists hierarchy (parents first). LLM prompt/schema gains nested `children`; parser recurses. Editor gets "Add Blend" + indented sub-nutrient rows; client RowKey resolves parents on submit.

**Tech Stack:** ASP.NET MVC (C# 12), Dapper/SQLite, HTMX + Bootstrap, System.Text.Json, MSTest + Moq, Playwright.

## Global Constraints

- No inline JavaScript — all JS in `wwwroot/js/*.js` (CSP `script-src 'self'`). (AGENTS.md Web)
- Controllers thin: delegate to services/repos. (AGENTS.md Web)
- `<Nullable>enable</Nullable>` — respect nullability. (root AGENTS.md)
- `dotnet format` clean; pre-commit hook gates. (root AGENTS.md)
- No EF Core; Dapper only. (root AGENTS.md)
- 300-line hard limit per .cs file. (root AGENTS.md)
- Tests MSTest + Moq; Playwright hits real app. (AGENTS.md)
- **Dosage rule:** top-level nutrient REQUIRES dosage; blend CHILD may omit. Enforced in service, not model attribute.

---

### Task 1: Data model — `ParentNutrientId` + migration + optional dosage

**Files:**
- Modify: `VitaTrack.Infrastructure/Models/SupplementNutrient.cs` (add `ParentNutrientId`/`ParentNutrient`; drop `Dosage [Required]`)
- Modify: `VitaTrack.Infrastructure/Data/DbInit.cs` (add column via guarded ALTER)
- Modify: `VitaTrack.Infrastructure/Data/SupplementNutrientRepository.cs` (read/write `ParentNutrientId`; add `GetByParentIdAsync`)
- Test: `VitaTrack.Tests/SupplementNutrientRepositoryTests.cs`

**Interfaces:**
- Consumes: existing `SupplementNutrient` schema
- Produces: `ParentNutrientId` column; `GetByParentIdAsync(int parentId)` for child queries

- [ ] **Step 1: Write failing repo test**

```csharp
[TestMethod]
public async Task Add_WithParentId_PersistsAndReadsBack()
{
    await using var conn = await CreateConnectionAsync();
    var repo = new SupplementNutrientRepository(conn);
    var parent = new SupplementNutrient { SupplementId = 1, GenericName = "Proprietary Blend", SpecificForm = "Blend", Dosage = "500mg" };
    var parentId = await repo.AddAsync(parent);
    var child = new SupplementNutrient { SupplementId = 1, GenericName = "Zinc", SpecificForm = "Picolinate", Dosage = "", ParentNutrientId = parentId };
    await repo.AddAsync(child);

    var children = await repo.GetByParentIdAsync(parentId);
    Assert.AreEqual(1, children.Count);
    Assert.AreEqual("Zinc", children[0].GenericName);
}
```

- [ ] **Step 2: Run test — fail** (`dotnet test --filter "FullyQualifiedName~Add_WithParentId"`)

- [ ] **Step 3: Implement**
  - `SupplementNutrient.cs`: remove `[Required]` above `Dosage`; add `public int? ParentNutrientId { get; set; }` and `public SupplementNutrient? ParentNutrient { get; set; }`.
  - `DbInit.cs`: after the `FrequencyPerDay` ALTER block, add:
    ```csharp
    var parentCol = db.QuerySingle<int>("SELECT COUNT(*) FROM pragma_table_info('SupplementNutrients') WHERE name = 'ParentNutrientId';");
    if (parentCol == 0)
        db.Execute("ALTER TABLE SupplementNutrients ADD COLUMN ParentNutrientId INTEGER NULL;");
    ```
  - `SupplementNutrientRepository`: include `ParentNutrientId` in SELECT/INSERT/UPDATE; add:
    ```csharp
    public async Task<IReadOnlyList<SupplementNutrient>> GetByParentIdAsync(int parentId)
    {
        const string sql = "SELECT Id, SupplementId, GenericName, SpecificForm, Dosage, ParentNutrientId FROM SupplementNutrients WHERE ParentNutrientId = @ParentId";
        return (await _db.QueryAsync<SupplementNutrient>(sql, new { ParentId = parentId })).ToList();
    }
    ```

- [ ] **Step 4: Run test — pass** (`dotnet test --filter "FullyQualifiedName~Add_WithParentId"`)

- [ ] **Step 5: Commit**
```bash
git add VitaTrack.Infrastructure
git commit -m "feat: add ParentNutrientId + optional dosage to SupplementNutrient"
```

---

### Task 2: DTO + service hierarchy persistence + dosage rule

**Files:**
- Modify: `VitaTrack.Infrastructure/Models/LlmResult.cs` (`SupplementNutrientDto` add `ParentNutrientId`, `Children`)
- Modify: `VitaTrack.Infrastructure/Services/SupplementNutrientService.cs` (`PersistHierarchyAsync`; dosage rule)
- Test: `VitaTrack.Tests/SupplementNutrientServiceTests.cs`

**Interfaces:**
- Consumes: `ISupplementNutrientRepository` (incl. `GetByParentIdAsync`)
- Produces: `PersistHierarchyAsync(int, IEnumerable<SupplementNutrientDto>)`; `ReplaceAsync` routes through it

- [ ] **Step 1: Write failing service tests**

```csharp
[TestMethod]
public async Task PersistHierarchy_StoresChildrenWithParentId()
{
    var repo = new MockRepo();
    var svc = new SupplementNutrientService(repo, NullLogger);
    var blend = new SupplementNutrientDto { GenericName = "Proprietary Blend", SpecificForm = "Blend", Dosage = "500mg",
        Children = [ new SupplementNutrientDto { GenericName = "Zinc", SpecificForm = "Picolinate" } ] };

    var result = await svc.PersistHierarchyAsync(1, [ blend ]);

    Assert.AreEqual(2, result.Saved.Count);
    var child = repo.Added.Single(n => n.GenericName == "Zinc");
    Assert.IsTrue(child.ParentNutrientId > 0);
}

[TestMethod]
public async Task PersistHierarchy_TopLevelMissingDosage_Fails()
{
    var repo = new MockRepo();
    var svc = new SupplementNutrientService(repo, NullLogger);
    var result = await svc.PersistHierarchyAsync(1, [ new SupplementNutrientDto { GenericName = "Vit C", SpecificForm = "Ascorbic" } ]);
    Assert.IsTrue(result.Failures.Any(f => f.GenericName == "Vit C"));
}
```

- [ ] **Step 2: Run — fail**

- [ ] **Step 3: Implement**
  - `SupplementNutrientDto`: add `public int? ParentNutrientId { get; set; }` and `public List<SupplementNutrientDto>? Children { get; set; }`.
  - `SupplementNutrientService`: add recursive helper:
    ```csharp
    public async Task<ReplaceNutrientsResult> PersistHierarchyAsync(int supplementId, IEnumerable<SupplementNutrientDto> roots)
    {
        var saved = new List<SupplementNutrient>();
        var failures = new List<NutrientFailure>();
        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r.GenericName)))
        {
            if (string.IsNullOrWhiteSpace(root.Dosage))
            { failures.Add(new NutrientFailure(root.GenericName, "Top-level nutrient requires a dosage")); continue; }
            var parentId = await _nutrientRepo.AddAsync(new SupplementNutrient {
                SupplementId = supplementId, GenericName = root.GenericName, SpecificForm = root.SpecificForm, Dosage = root.Dosage });
            saved.Add(await _nutrientRepo.GetByIdAsync(parentId));
            if (root.Children != null)
                foreach (var c in root.Children.Where(x => !string.IsNullOrWhiteSpace(x.GenericName)))
                {
                    var child = new SupplementNutrient { SupplementId = supplementId, GenericName = c.GenericName,
                        SpecificForm = c.SpecificForm, Dosage = c.Dosage ?? string.Empty, ParentNutrientId = parentId };
                    await _nutrientRepo.AddAsync(child);
                    saved.Add(child);
                }
        }
        return new ReplaceNutrientsResult(saved, failures);
    }
    ```
  - `ReplaceAsync` calls `PersistHierarchyAsync` (after delete-all). Keep `AddAsync` for single adds.

- [ ] **Step 4: Run — pass**

- [ ] **Step 5: Commit**
```bash
git add VitaTrack.Infrastructure
git commit -m "feat: persist nutrient hierarchy + enforce top-level dosage rule"
```

---

### Task 3: LLM prompt / schema / parser blends + NutritionJson

**Files:**
- Modify: `VitaTrack.Infrastructure/Services/SupplementLabelParser.cs` (prompt + `ParseNutrients` recurse + `NutritionJson` build)
- Modify: `VitaTrack.Infrastructure/Services/LlmService.cs` (`NutritionJson` includes children)
- Test: `VitaTrack.Tests/LlmServiceTests.cs` (parser nested; prompt contains blend instructions)

**Interfaces:**
- Consumes: `ILlmClient` (mocked)
- Produces: `LlmResult.Nutrients` with nested `Children`; blend-aware `NutritionJson`

- [ ] **Step 1: Write failing tests**
  - Parser: given JSON with a blend + 2 children, assert `result.Nutrients[0].Children.Count == 2` and child names correct.
  - Prompt: assert `BuildExtractionPrompt` output contains "blend" and "children".
  - `LlmService`: mock `ILlmClient` to return blend JSON; assert `NutritionJson` contains child names.

- [ ] **Step 2: Run — fail**

- [ ] **Step 3: Implement**
  - `BuildExtractionPrompt`: add a paragraph: "A nutrient may be a **blend** — give it a `genericName` (e.g. 'Proprietary Herbal Blend'), a total `dosage` (e.g. '500mg'), and a `children` array of sub-nutrients (each with `genericName`, `specificForm`, and optional `dosage`)."
  - `BuildUserPrompt` JSON schema: add `"children": [ { "genericName": "...", "specificForm": "...", "dosage": "..." } ]` inside each nutrient.
  - `ParseNutrients`: recurse on `children` → set `dto.Children`.
  - `LlmService`: when building `NutritionJson`, for blends add `"<blend> > <child>"` entries (flattened) alongside the blend total.

- [ ] **Step 4: Run — pass**

- [ ] **Step 5: Commit**
```bash
git add VitaTrack.Infrastructure
git commit -m "feat: LLM enrichment returns nutrient blends (prompt/schema/parser)"
```

---

### Task 4: Controller mapping (ToDtos / Enrich / Edit) hierarchy

**Files:**
- Modify: `VitaTrack.Web/Controllers/SupplementController.cs` (`ToDtos` recursive; `Enrich`/`Edit`/`Confirm*` persist via hierarchy)
- Modify: `VitaTrack.Tests/SupplementControllerTests.cs` (or `SupplementControllerEditTests.cs`)

**Interfaces:**
- Consumes: `ISupplementNutrientService.PersistHierarchyAsync`
- Produces: editor model with `Children`; enrichment merges blends

- [ ] **Step 1: Write failing tests**
  - `ToDtos` maps a parent with children → DTO `Children` populated.
  - `Enrich` returns editor whose `Nutrients` contains a blend with `Children`.

- [ ] **Step 2: Run — fail**

- [ ] **Step 3: Implement**
  - `ToDtos`: query `GetByParentIdAsync(parent.Id)`, map each to DTO with `Children` recursively.
  - `Enrich`: replace `AddAsync(newId, SafeNutrients(...))` with `PersistHierarchyAsync(newId, SafeNutrients(llmResult.Nutrients))` (flatten roots to roots only — `SafeNutrients` returns roots; children ride inside).
  - `Edit` POST merge: when merging LLM nutrients, carry `Children` under the matched/added blend.
  - `ConfirmCreate`/`ConfirmEdit`: persist via `PersistHierarchyAsync`.

- [ ] **Step 4: Run — pass**

- [ ] **Step 5: Commit**
```bash
git add VitaTrack.Web VitaTrack.Tests
git commit -m "feat: map + persist nutrient hierarchy in controller"
```

---

### Task 5: Editor UI — Add Blend + sub-nutrient rows

**Files:**
- Modify: `VitaTrack.Web/Views/Supplement/_NutrientEditor.cshtml` (blend row + indented child; "Add Blend"/"Add sub-nutrient" buttons)
- Modify: `VitaTrack.Web/wwwroot/js/nutrient-editor.js` (RowKey, parent linkage, submit rewrite)
- Test: `e2e-tests/playwright/tests/supplement-crud.spec.js` (later, Task 8)

**Interfaces:**
- Consumes: `SupplementEditorViewModel.Nutrients` (now with `Children`)
- Produces: grouped rows; flat POST list with `ParentNutrientId` resolved from client RowKey

- [ ] **Step 1: Update `_NutrientEditor.cshtml`**
  - Render roots; for each root with `Children`, render indented child rows (CSS class `blend-child`).
  - Add `<button id="add-blend-row">Add Blend</button>`; each blend row gets `<button class="add-sub-nutrient" data-parent-key="...">Add sub-nutrient</button>`.
  - Mark child dosage input without `required`; parent dosage `required`.

- [ ] **Step 2: Update `nutrient-editor.js`**
  - Assign `data-row-key` to each row; "Add Blend" appends a blend row; "Add sub-nutrient" appends a child row with `data-parent-key` = blend's key and CSS indent.
  - On submit (the existing `UpdateNutrients` hx button), rewrite inputs to `nutrients[i].GenericName/SpecificForm/Dosage/ParentNutrientId` where `ParentNutrientId` = resolved parent key (or empty for roots). Preserve existing `add/remove-row` behavior; keep CSP-safe (no inline handlers).

- [ ] **Step 3: Build** (`dotnet build VitaTrack.Web`)
Expected: 0 errors.

- [ ] **Step 4: Commit**
```bash
git add VitaTrack.Web
git commit -m "feat: editor UI for blends (Add Blend + sub-nutrient rows)"
```

---

### Task 6: Standalone nutrient pages — parent dropdown

**Files:**
- Modify: `VitaTrack.Web/Views/SupplementNutrient/Create.cshtml` + `Edit.cshtml`
- Modify: `VitaTrack.Web/Controllers/SupplementNutrientController.cs` (pass top-level nutrients for dropdown)

**Interfaces:**
- Consumes: `ISupplementNutrientRepository` top-level list
- Produces: optional `ParentNutrientId` select

- [ ] **Step 1: Add dropdown**
  - Controller `Create`/`Edit` (GET): load top-level nutrients (`ParentNutrientId IS NULL`) for the supplement; pass via `ViewData["ParentOptions"]` (anonymous objects per AGENTS.md — no ValueTuples).
  - Views: `<select asp-for="ParentNutrientId">` with options; dosage optional when a parent chosen (client: if parent selected, remove `required` from dosage).

- [ ] **Step 2: Build + run** (`dotnet build VitaTrack.Web`)
Expected: 0 errors.

- [ ] **Step 3: Commit**
```bash
git add VitaTrack.Web
git commit -m "feat: optional parent blend dropdown on nutrient pages"
```

---

### Task 7: Seed blend (demo/E2E)

**Files:**
- Modify: `VitaTrack.Infrastructure/Data/DbInit.cs` (seed one blend + children on a seeded supplement)

- [ ] **Step 1: Add seed rows**
  - After existing SupplementNutrients seed, insert a blend for supplement 3 (Multivitamin): e.g. `(3, 'Proprietary Blend', 'Blend', '500mg')` then 2 children with `ParentNutrientId = <that id>` (use `last_insert_rowid()` or fixed id if safe). Keep FK order.

- [ ] **Step 2: Build + unit test** (repo reads child under blend)

- [ ] **Step 3: Commit**
```bash
git add VitaTrack.Infrastructure
git commit -m "test: seed a sample nutrient blend"
```

---

### Task 8: Playwright E2E

**Files:**
- Modify: `e2e-tests/playwright/tests/supplement-crud.spec.js`

- [ ] **Step 1: Add tests**
  - Edit a seeded supplement (id 3 has a blend) → editor shows grouped blend + child rows.
  - Create/Edit flow: add a blend with 2 sub-nutrients (one without dosage), Save Changes, reload, verify grouped rows present.
  - Enrich a supplement with a URL whose label has a blend → editor shows grouped nutrients.

- [ ] **Step 2: Run** (`cd e2e-tests/playwright && npx playwright test supplement-crud`)
Expected: GREEN.

- [ ] **Step 3: Commit**
```bash
git add e2e-tests
git commit -m "test: Playwright coverage for blends + optional dosage"
```

---

### Task 9: Final verification + format gate

**Files:** none new

- [ ] **Step 1: Format check** (`./format-check.sh`) — clean
- [ ] **Step 2: Build + test** (`dotnet build VitaTrack.sln && dotnet test`) — green
- [ ] **Step 3: Commit any format fixes**
```bash
git add -A && git commit -m "style: apply dotnet format"  # only if needed
```
