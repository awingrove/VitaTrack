# UI Refinement: Save / Enrich Split (Supplement page)

- Date: 2026-08-20
- Branch: feature/ui-refinement

## Goal

Give the Supplement Create and Edit pages two distinct, clearly separated buttons:

- **Save** — persists the supplement fields with no AI enrichment and no nutrient extraction, then returns to the supplement list.
- **Enrich** — runs the LLM enrichment (extracts nutrients from the manufacturer URL), persists the supplement, and loads/replaces the nutrient breakdown. If the supplement already has nutrients, the user is warned on click that enriching may overwrite them.

## Current Behavior (baseline)

- `Create.cshtml`: single form with one "Save" button that `hx-post`s to `/Supplement/Enrich`. That action always calls the LLM, persists the supplement, persists nutrients, and swaps in the `_NutrientEditor` partial. There is no plain Save.
- `Edit.cshtml`: form posts to the `Edit` POST action, which always calls the LLM and merges extracted nutrients with existing ones, then shows the `Review` view. There is no plain Save and no Enrich-only button.

## New Behavior

### Controller — `SupplementController.cs`

- **`CreateSave(CreateSupplementRequest request)`** (new `HttpPost`, `[ValidateAntiForgeryToken]`)
  - Validate `ModelState`. On failure return `PartialView("_ValidationErrors", ModelState)`.
  - Map to `Supplement` (no `EnrichSupplementAsync` call). `await _suppRepo.AddAsync(supplement)`.
  - Set `Response.Headers["HX-Redirect"] = Url.Action("Index", "Supplement")` and return `EmptyResult()` so HTMX performs a full-page navigation back to the list.
- **`Enrich(...)`** — unchanged. Still calls the LLM, persists supplement + nutrients, returns `_NutrientEditor`.
- **`EditSave(int id, EditSupplementRequest request)`** (new `HttpPost`, `[ValidateAntiForgeryToken]`)
  - Validate (`id == request.Id`, `ModelState`). On failure re-render `Edit` with the original.
  - Map to `Supplement`, `await _suppRepo.UpdateAsync(supplement)`. Do NOT call the LLM; do NOT merge/replace nutrients.
  - `return RedirectToAction(nameof(Index))` (full page navigation).
- **`Edit(...)` POST** — unchanged; remains the Enrich action (enrich + merge → `Review`).

### Views

#### `Create.cshtml`

- Form keeps `hx-target="#nutrient-editor-container"` / `hx-swap="innerHTML"` for the Enrich partial.
- Two buttons:
  - `Save` → `hx-post="/Supplement/CreateSave"`, class `btn btn-primary`.
  - `Enrich` → `hx-post="/Supplement/Enrich"`, id `enrich-btn`, class `btn btn-info`, keeps `hx-indicator="#enrich-spinner"`.
- Order: Save primary, Enrich secondary, then Cancel link.

#### `Edit.cshtml`

- Single form `action="/Supplement/Edit"`, `method="post"`.
- Two submit buttons using `formaction`:
  - `Save` → `formaction="/Supplement/EditSave"`, class `btn btn-primary`.
  - `Enrich` → `formaction="/Supplement/Edit"`, id `enrich-btn`, class `btn btn-info`.
- Button `data-nutrient-count="@Model.NutrientCount"` (or a hidden field) so JS knows whether nutrients exist.

### Enrich warning (overwrite guard)

- **Create**: `wwwroot/js/create.js` listens for `htmx:beforeRequest` on the document. If the triggering element is `#enrich-btn` and the nutrient editor container currently has `data-nutrient-count > 0`, call `window.confirm("This supplement already has nutrients. Enriching may overwrite them. Continue?")`. If the user cancels, `event.preventDefault()` to stop the request.
- **Edit**: `wwwroot/js/edit.js` attaches a `click` listener to `#enrich-btn`. If `Model.NutrientCount > 0` and the user cancels `window.confirm(...)`, `event.preventDefault()` to stop form submission.
- Both files are external (no inline JS) to satisfy CSP (`script-src 'self'`).

## Testing

- `VitaTrack.Tests`:
  - `CreateSave` persists a supplement, does not call the LLM, and sets `HX-Redirect` to the Index URL (mock repo, verify `AddAsync` invoked once, `EnrichSupplementAsync` never invoked).
  - `EditSave` updates the supplement, does not merge nutrients, and redirects to Index (mock repo + `NutrientService`, verify `UpdateAsync` invoked, `ReplaceAsync`/`EnrichSupplementAsync` never invoked).
- Playwright (`e2e-tests/playwright`):
  - Create page shows both Save and Enrich buttons.
  - Edit page shows both Save and Enrich; when a supplement has nutrients, clicking Enrich surfaces the confirm dialog (assert via `window.confirm` stub or dialog handler).
  - Save (no enrichment) returns to the list without calling the LLM (unit-level cover; E2E asserts navigation).

## Out of scope

- CSV import enrichment, Review/ConfirmEdit flow, SupplementNutrient management pages.
