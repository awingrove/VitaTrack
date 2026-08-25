# List Column Sorting — Design

Date: 2026-08-23

## Goal
Supplements and Nutrient (per-supplement) list pages get clickable column
sorting. Client-side, no server paging. Supplement list opens already sorted
by Name (server-side ORDER BY added for that initial order).

## Approach (approved)
- One reusable external JS `wwwroot/js/table-sort.js`, CSP-safe, bound to
  `table[data-sortable]`. Clickable `<th data-sort-key="..." data-sort-type="text|number">`
  sorts the table's `<tbody>` rows in place. Toggle asc/desc; show ▲/▼ glyph.
- Sort value taken from `td[data-sort-value]` when present, else cell text.
  Numeric columns use `data-sort-value` with a raw comparable value.
- Initial server order: `SupplementRepository.GetAllAsync` adds `ORDER BY Name`.
- No controller/repo change beyond that ORDER BY. No new deps. No orphan pages.

## Files
- Create: `VitaTrack.Web/wwwroot/js/table-sort.js`
- Modify: `VitaTrack.Infrastructure/Data/SupplementRepository.cs:16` (ORDER BY Name)
- Modify: `VitaTrack.Web/Views/Supplement/Index.cshtml` (sortable table + headers + data-sort-value on Cost cell)
- Modify: `VitaTrack.Web/Views/SupplementNutrient/Index.cshtml` (sortable table + headers)
- Test: `e2e-tests/playwright/tests/supplement-crud.spec.js`, `supplement-nutrient.spec.js`
- Doc: `storymap.yaml` MS-4 / MS-6 e2e refs

## Invariants
- Checkbox selection persists across sort (rows are reordered, not recreated).
- Cost shown as `£x.xx` but sorts numerically via `data-sort-value`.
- Glend children rows (indented) sort with same logic as plain rows.

## Testing
- E2E: click Name header → first row order changes; click again → reversed.
- Numeric column (Nutrient Count on supplements, Dosage on nutrients) sorts
  numerically not lexically (e.g. 9 before 10).
- Supplement list initially sorted by Name on load.
- Storymap updated same change.
