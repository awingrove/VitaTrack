# Plan: Prescribed Dose Filter + Report Polish

## Scope

Three surfaces: `PrescribedDose` Index/Create, `NutrientReport`.

## 1. PrescribedDose Index — family member filter

- `Index(int? familyMemberId)` query param; empty = all members.
- Repo: add `GetByFamilyMemberIdAsync(int)` (`WHERE pd.FamilyMemberId = @id`); controller picks repo method. No in-memory filtering.
- Filter UI: GET form above table — `form-select` listing all members ("All members" default) + `btn-outline-secondary` Apply. Boring full-page reload, no HTMX.
- "Add New Prescribed Dose" button carries current filter: `asp-route-familyMemberId` → Create preselects that member.

## 2. PrescribedDose Index — manufacturer column

- `PrescribedDose` model: add `public string? SupplementBrand` display property (alongside `SupplementName`).
- `PrescribedDoseRepository` `GetAllAsync`/`GetByIdAsync`: add `s.Brand as SupplementBrand` join column.
- Index: new "Manufacturer" column after Supplement. `@pd.SupplementBrand` (blank cell when null).

## 3. PrescribedDose/Create + Edit — supplement dropdown shows manufacturer

- Option text: `@s.Name (@s.Brand)` — omit bracket when Brand null/empty.
- Both Create.cshtml and Edit.cshtml (same option loop duplicated there).

## 4. Create — serving size not editable

- Already `readonly` input. Change to static display: plain `<p class="form-control-plaintext">` (Bootstrap static control). JS `prescribed-dose-serving.js` keeps populating it; no input semantics.

## 5. Multiplier stepper (Create + Edit)

- Bootstrap `input-group`: minus button | number input | plus button, buttons at each end.
- Input: `type="number" step="any" min="0.01"` — keeps any-numeric entry, browser/`[Range(0.01,1000)]` validation intact.
- Buttons: JS step ±0.25, clamped at 0.01; `btn-outline-secondary`. Larger touch targets (`btn` default size, not `btn-sm`).
- New `wwwroot/js/multiplier-stepper.js`, included by both Create and Edit. Server default 1 already via `Model.Multiplier = 1m`.

## 6. NutrientReport — trim insignificant decimals

- Totals: `ReportingService` emits `"0.##"` format instead of `"F2"` (25 → `25`, 1.5 → `1.5`).
- Contribution rows in view: `row.Amount.ToString("F2")` → `"0.##"`.
- Update `ReportingServiceTests` assertions expecting `"F2"` strings.

## 7. NutrientReport — supplement-count pill

- Per cell: `badge rounded-pill bg-secondary` after the amount, showing number of contributing supplements (= `contribs.Count`; matches DESIGN.md:207 "pills for nutrient counts" — no amendment needed).
- Only rendered when the cell is expandable (count > 0).
- E2E already covers expandable rows; extend `nutrient-report.spec.js` for pill + trimmed decimals; extend `prescribed-dose.spec.js` for filter, brand column, dropdown brand, stepper.

## 8. Bookkeeping (same change)

- [x] Unit: ReportingService totals format (`"0.##"` totals, integer → no decimals)
- [x] Unit: PrescribedDoseRepository GetByFamilyMemberIdAsync + SupplementBrand join (SqliteTestBase)
- [x] E2E: dose index filter narrows rows; create link preselects member
- [x] E2E: create page brand in dropdown; multiplier default 1, ± buttons step 0.25; free entry 1.3 accepted
- [x] E2E: report pill count + trimmed decimals
