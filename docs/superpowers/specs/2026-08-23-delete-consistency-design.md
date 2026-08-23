# Delete Consistency Pass — Design

Date: 2026-08-23

## Problem

Supplements and Nutrients already have single-row delete, checkbox multi-select,
and bulk delete. Blends are nutrients with `ParentNutrientId` children and delete
through the same nutrient paths. But:

1. **Broken confirm:** `Views/Supplement/Index.cshtml:54` uses an inline
   `onsubmit="return confirm(...)"`. CSP (`script-src 'self'`, no
   `'unsafe-inline'`) blocks inline event handlers in all non-Dev environments,
   so the supplement single-row confirm silently never fires in Test/Prod.
2. **No cascade warnings:** confirms never mention that deleting a supplement
   also deletes its nutrients and prescribed doses, or that deleting a blend
   parent deletes its child nutrients.
3. **Nutrient single-delete confirm page** (`SupplementNutrient/Delete.cshtml`)
   says nothing about blend-child cascade.

## Decisions (user-approved)

- Scope: full consistency pass across Supplements + Nutrients/Blends list pages.
  No new endpoints, no new dependencies.
- Blends stay per-supplement (no global blends page).
- Confirmation style: native `confirm()` driven by external JS (CSP-safe).
- Referenced items: cascade silently at the data layer, warn in the confirm text.
- The HTMX nutrient editor keeps its client-side Remove buttons (Save persists
  the row set, which covers deletion of saved rows); no server delete there.

## Changes

### JS
- New `wwwroot/js/delete-confirm.js`: binds a submit listener to every
  `form[data-confirm-message]`; shows `confirm(message)`; cancels submission on
  dismiss. CSP-safe replacement for inline `onsubmit`.
- Extend `wwwroot/js/delete-selected.js`: if the bulk form has
  `data-cascade-warning`, append it to the bulk confirm message.

### Views
- `Views/Supplement/Index.cshtml`: remove inline `onsubmit`; add
  `data-confirm-message` (+ cascade warning) to the row form and
  `data-cascade-warning` to the bulk form; include `delete-confirm.js`.
- `Views/SupplementNutrient/Index.cshtml`: add `data-cascade-warning` to the
  bulk form; include `delete-confirm.js` (single-row deletes keep the GET
  confirm page).
- `Views/SupplementNutrient/Delete.cshtml`: show "child nutrients will also be
  deleted" note when the nutrient has children.

### Controller
- `SupplementNutrientController.Delete` (GET): pass `HasChildren` in ViewData.

## Error handling / invariants
- Cascade order stays owned by repositories (unchanged).
- All deletes remain POST + antiforgery (unchanged).
- No orphaned pages: no links removed.

## Testing
- E2E (Playwright):
  - Supplement single delete: dialog appears, message mentions cascade;
    **dismissing** it leaves the row (regression guard for the CSP bug).
  - Nutrient bulk delete of a blend parent removes its children too.
  - Existing specs keep passing (dialog handlers already registered before clicks).
- Unit: none needed beyond existing controller/repo tests (no logic change).
- Storymap updated in the same change.
