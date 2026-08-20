# HTMX Supplement Enrichment — Single-Page Flow

## Problem

The current supplement creation flow uses a full-page POST to a separate Review page for LLM enrichment results. This causes a jarring page reload and a two-step create-then-confirm process. We want a smooth, single-page experience: click Save, see a spinner, get results inline.

## Current Flow

1. `GET /Supplement/Create` — empty form
2. `POST /Supplement/Create` — full-page POST, calls `LlmService.EnrichSupplementAsync`, renders `Review.cshtml`
3. `POST /Supplement/ConfirmCreate` — saves to DB, redirects to Index

## Target Flow

1. `GET /Supplement/Create` — form with HTMX attributes
2. User fills in fields, clicks **Save**
3. HTMX POST to `POST /Supplement/Enrich`
   - **With ManufacturerUrl:** spinner shown → server fetches URL + LLM enrichment → auto-saves supplement + nutrients to DB → returns `_NutrientEditor` partial
   - **Without ManufacturerUrl:** server saves supplement to DB → returns empty `_NutrientEditor` partial
4. Nutrient editor appears inline (pre-filled if enrichment succeeded, empty if not)
5. User edits nutrients → clicks **Save Changes** → HTMX POST to `POST /Supplement/UpdateNutrients` → DB updated, editor refreshed

## Design

### 1. HTMX Setup

Add HTMX CDN to `Views/Shared/_Layout.cshtml` after Bootstrap JS. The layout already has a comment placeholder: `@* Shared layout -- Bootstrap 5 + HTMX *@`.

```html
<script src="https://unpkg.com/htmx.org@2.0.4"></script>
```

### 2. Create.cshtml — Form Changes

The form gains HTMX attributes:

- `hx-post="/Supplement/Enrich"` — async POST via HTMX
- `hx-indicator="#enrich-spinner"` — loading indicator tied to this request
- `hx-target="#nutrient-editor-container"` — response HTML injected here
- `hx-swap="innerHTML"` — replace container contents

Below the form:
- A spinner div with `id="enrich-spinner"` and `class="htmx-indicator"` (auto-hidden/shown by HTMX)
- An empty `div#nutrient-editor-container` — target for partial responses

The Save button triggers the HTMX request (no `type="submit"` — use `type="button"` or `hx-trigger` to avoid full form submit).

### 3. Controller Actions

**`POST Enrich(Supplement supplement)`** (new)

1. Validate ModelState → if invalid, return `PartialView("_ValidationErrors", modelState)` (errors swap into a validation area above the form)
2. Call `_llmService.EnrichSupplementAsync(supplement)`
3. Save supplement to DB via `_suppRepo.AddAsync(supplement)` (receives new Id)
4. Save extracted nutrients via `_nutrientRepo.AddAsync` for each nutrient with non-empty GenericName
5. Return `PartialView("_NutrientEditor", editorModel)` with:
   - `SupplementId` (the new DB Id)
   - `Nutrients` (extracted list, or empty)
   - `SwapSuggestion` (if present)
   - `ExtractionError` (if any — displayed as info message, not blocking)

**`POST UpdateNutrients(int supplementId, List<SupplementNutrientDto> nutrients)`** (new)

1. Delete existing nutrients for `supplementId` via `_nutrientRepo.DeleteBySupplementIdAsync`
2. Insert updated list via `_nutrientRepo.AddAsync` for each
3. Return `PartialView("_NutrientEditor", refreshedModel)` with success indicator

**Removed actions:** `Create` (POST), `ConfirmCreate`, `ConfirmEdit` — replaced by `Enrich` and `UpdateNutrients`. The `Edit` (POST) action is unchanged and still renders `Review.cshtml` (out of scope for this change).

### 4. Partial Views

**`_NutrientEditor.cshtml`** — Model: editor view model with `SupplementId`, `List<SupplementNutrientDto>`, `SwapSuggestion`, `ExtractionError`, `SaveSuccess`

- Read-only supplement name header
- Editable nutrient table (GenericName, SpecificForm, Dosage columns)
- "Remove" button per row
- "Add Nutrient" button (vanilla JS, same pattern as current `review.js`)
- Swap Suggestion display (if present)
- Extraction error/info message (if present)
- "Save Changes" button:
  - `hx-post="/Supplement/UpdateNutrients"`
  - `hx-include="closest form"` or explicit nutrient field serialization
  - `hx-target="#nutrient-editor-container"` (replaces self)
  - `hx-swap="innerHTML"`
- Success banner (shown after Save Changes completes)

**`_ValidationErrors.cshtml`** — Model: `ModelStateDictionary`

- Renders validation error summary as Bootstrap alert
- Swapped into `#validation-area` div above the form

### 5. Error Handling

| Scenario | Behavior |
|----------|----------|
| No ManufacturerUrl | Skip LLM, save supplement, return empty editor |
| URL fetch fails | Save supplement, return editor with info message |
| LLM returns no nutrients | Save supplement, return editor with info message |
| LLM call throws exception | Save supplement, return editor with info message (catch in controller) |
| Invalid ModelState on Enrich | Return validation errors partial, form stays intact |
| Invalid ModelState on UpdateNutrients | Return validation errors within editor |

The supplement is always saved regardless of enrichment outcome. The user can always add nutrients manually.

### 6. Files Changed / Created

| File | Action |
|------|--------|
| `Views/Shared/_Layout.cshtml` | Add HTMX CDN script tag |
| `Views/Supplement/Create.cshtml` | Add HTMX attributes, spinner, target container, validation area |
| `Views/Supplement/_NutrientEditor.cshtml` | **New** — partial view for nutrient editor |
| `Views/Supplement/_ValidationErrors.cshtml` | **New** — partial view for validation errors |
| `Controllers/SupplementController.cs` | Add `Enrich`, `UpdateNutrients`; remove `ConfirmCreate`, `ConfirmEdit`, `Create` (POST) |
| `Views/Supplement/Review.cshtml` | **Keep** — still used by Edit flow (POST Edit renders it); delete in follow-up when Edit is converted |
| `wwwroot/js/review.js` | **Keep** — still used by Review.cshtml for Edit flow |

### 7. Editor View Model

```csharp
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

Located in `VitaTrack.Web/Models/SupplementEditorViewModel.cs` (new file).

## Out of Scope

- Edit flow (`POST /Supplement/Edit`, `POST /Supplement/ConfirmEdit`) — still uses `Review.cshtml`; convert to HTMX in a follow-up
- Auto-save on nutrient edits (explicit Save Changes button only)
- Index page changes — no changes to the supplement listing
