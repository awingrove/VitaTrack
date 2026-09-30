# Supplement Comparison Page — Design

Date: 2026-09-29
Status: approved (design sections), pending spec review

## Purpose

User selects a handful of supplements from the supplement list and opens a
comparison page to **pick which supplement to buy/take**: each nutrient shown
across all selected products, with its specific form and amount per serving, so
overlaps and gaps are visible at a glance.

## Decisions (interview record)

| Question | Decision |
|---|---|
| Purpose | Decision A — pick which supplement to buy; overlaps + amounts matter most |
| Amount display | Raw stored `Dosage` normalized via `Dosage.Normalize` (canonical unit, e.g. `mcg` → `µg`); not converted across unit families |
| Missing nutrients | Full union of rows; blank cell (em dash) marks "not in this supplement" |
| Blend rows | Parent row + indented child rows beneath (EPA/DHA visible); orphan children treated as top-level |
| Selection mechanism | GET `?ids=1,2,3` built by JS from checked boxes; shareable, refresh/back-safe |
| Row matching | Trim + case-insensitive `GenericName`; label = first-seen spelling |
| Row order | Alphabetical by matched key (parents by own key, children keep relative order under parent) |
| Column order | Supplement list's default order (JS builds `ids` in table-row order) |
| Selection cap | 5 (client-side gating only; server accepts any ≥2) |
| Min selection | ≥2 resolved ids; otherwise redirect to list |
| Unknown/deleted ids | Silently skipped; redirect to list only when <2 remain |
| Column header | 3 lines: supplement name, manufacturer, `DailyDose` (shown as-is, TD-017 free text) |
| DESIGN.md | Amendment required (new button intent + grid pattern) — user confirmed |

## Approach

Chosen: **comparison builder in the Supplements slice** (approach A). Rejected:
new `Features/Comparison/` slice (YAGNI — heavy shards block for ~2 files);
controller composing repos directly (business logic in Web layer, untestable).

## Components

### Core — `VitaTrack.Core/Features/Supplements/`

- `SupplementComparisonBuilder.cs` — pure builder: takes fetched supplements
  with their nutrients, returns a `ComparisonGrid`. Responsibilities:
  1. Column order = input order.
  2. Row keys = `GenericName.Trim()` case-insensitive; label = first-seen spelling.
  3. Blend nesting: `ParentNutrientId` rows group under parent; orphan children top-level.
  4. Rows sorted alphabetically by key; children retain data order under parent.
  5. Cell = `SpecificForm` + dosage normalized through existing `Dosage.Normalize`
     (which calls `Unit.Canonicalize` — no second unit list, no reimplementation).
  6. Missing nutrient → empty cell marker (rendered as em dash).
- `ComparisonGrid.cs` — result-shaped records: `ComparisonGrid`,
  `ComparisonColumn` (name, manufacturer, dailyDose), `ComparisonRow`
  (label, isChild, cells), `ComparisonCell`.
- `BuildSupplementComparisonHandler.cs` — resolves ids via repos
  (`ISupplementRepository`, `ISupplementNutrientRepository`), drops unknowns,
  preserves requested order, requires ≥2, invokes the builder.

### Nutrients slice

- `ISupplementNutrientRepository` gains
  `GetBySupplementIdsAsync(IEnumerable<int> supplementIds)` — single `IN`
  query in `SupplementNutrientRepository` (sanctioned cross-slice route).

### Web — `VitaTrack.Web`

- `SupplementController.Compare(string ids)` — GET `/Supplement/Compare?ids=…`.
  Parse comma-separated ints, drop non-int/negative/unknown/duplicates,
  preserve order of surviving ids. <2 → `RedirectToAction(nameof(Index))`.
  ≥2 → handler → `View(grid)`.
- `Views/Supplement/Compare.cshtml` — `h2` "Compare Supplements", back-link to
  list, Bootstrap table: header row = 3-line column headers, first column =
  nutrient labels (children indented), em dash empty cells. Stock Bootstrap
  only, `asp-append-version` on any asset references.
- `Views/Supplement/Compare.cshtml` and list entry point:
  `Views/Supplement/Index.cshtml` gains a "Compare Selected" header button next
  to "Delete Selected": disabled until ≥2 checked, capped at 5 (6th check
  blocked with visible hint), `<a>` whose `href` JS rebuilds as
  `Compare?ids=` from checked boxes in row order.
- `wwwroot/js/compare-selected.js` — vanilla JS (no htmx), mirrors the
  `delete-selected.js` select-all/checkbox idiom.

## Error handling

- Malformed `ids` (`abc`, empty, negatives) → tokens dropped; <2 valid → redirect.
- Duplicate ids → deduped.
- Zero-nutrient supplement → column with all em dashes.
- Server never enforces the 5 cap (shareable URLs stay valid past it).

## Testing

- Unit (Core): `SupplementComparisonBuilderTests` — grouping/case-insensitive
  match, alphabetical order, blend nesting, orphan children, canonical
  normalization, empty cells, first-seen label spelling.
- Unit (Web): `SupplementControllerTests.Compare_*` — parsing (valid, malformed,
  duplicates, unknown), <2 redirect, ≥2 view + grid, list order preserved.
- Repository: `SupplementNutrientRepositoryTests.GetBySupplementIdsAsync`.
- E2E: `e2e-tests/playwright/tests/supplement-comparison.spec.js` — arrives by
  clicking (check boxes → Compare Selected → grid), asserts columns/rows/cells/
  blanks, disabled state at <2, cap behavior. No hardcoded ids; row-text
  lookups only.

## Docs / registry (same change)

- `shards.yaml`: Supplements slice claims new action/view/JS/unit tests;
  Nutrients slice claims new repo method + test; e2e spec claimed.
- `storymap.yaml`: new task under supplement activity,
  `entry_point: Supplements index -> Compare Selected`, test refs.
- `DESIGN.md`: amendment for the "Compare Selected" toolbar button intent and
  the comparison-grid pattern (3-line headers, indented blend children,
  em-dash empty cells).
- `AGENTS.md`/technical-debt register: no new debt anticipated; add entries
  only if implementation surfaces any.
