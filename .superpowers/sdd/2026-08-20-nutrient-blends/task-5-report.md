# Task 5 Report — Editor UI for Blends (Add Blend + sub-nutrient rows)

## Status
DONE.

## Commits
- `808f82d3cfaafd3a74dfeff14c6767e9962424fe` — feat: editor UI for blends (Add Blend + sub-nutrient rows)

## Build result
Build `VitaTrack.Web` + `VitaTrack.Tests`: 0 errors (2 pre-existing nullability warnings in tests, unrelated). `dotnet format VitaTrack.sln --verify-no-changes`: clean. Pre-commit hook (format + ArchitectureTests) passed.

## Changes
1. `_NutrientEditor.cshtml`
   - Renders roots; indents child rows with `class="blend-child"` and `style="padding-left:2rem"` on the Generic Name cell.
   - Child `Dosage` input has NO `required`; parent `Dosage` keeps `required`.
   - Each child row carries a hidden `nutrients[seq].ParentNutrientId` = the parent's sequential index.
   - Added `#add-blend-row` ("Add Blend") button and per-blend `.add-sub-nutrient` button (`data-parent-key`).
   - Server-rendered rows carry `data-row-key` (root-r / child-r-c) and `data-parent-key` so JS can manage them. Sequential indices (`seq`) computed across roots+children in DOM order.
   - CSP-safe: no inline `<script>`/handlers; only external `/js/nutrient-editor.js`.

2. `nutrient-editor.js`
   - `add-nutrient-row`: plain top-level nutrient (required dosage).
   - `add-blend-row`: appends a top-level blend row (required dosage) with a unique `data-row-key` and an embedded `.add-sub-nutrient` button.
   - `.add-sub-nutrient`: appends an indented `blend-child` row whose `data-parent-key` = the blend's key; child Dosage optional.
   - `reindexNutrients()`: renumbers all `nutrients[index].Field` inputs by DOM order; builds a `rowKey -> sequentialIndex` map; sets each child's hidden `nutrients[index].ParentNutrientId` to the resolved parent index; removes any `ParentNutrientId` hidden input from root rows. Re-adds the empty-row when no rows remain.
   - Remove: delegated handler removes the row AND any descendant rows (by `data-parent-key`), then reindexes.
   - Save (`hx-post`) has a capture-phase `click` listener that calls `reindexNutrients()` before htmx serializes the form, guaranteeing correct sequential indices / parent linkage.

## Verification of POST contract (by reading rendered logic)
- Roots render first (sequential index = their position), children immediately follow their parent, so a parent's sequential index equals its render position. The flat list posted is `nutrients[0..n]` with each child carrying `nutrients[i].ParentNutrientId` = parent sequential index, matching the agreed editor contract.

## Concerns
- Server-side reconstruction: `SupplementController.SafeNutrients` currently only filters by `GenericName` and does NOT rebuild `Children` from the flat `ParentNutrientId` list, and `PersistHierarchyAsync` ignores `ParentNutrientId` (expects `Children` populated). So a manual Add-Blend → Save → reload will NOT yet persist the grouping on the server. This is expected per the brief (editor submits the flat contract; server reconstruction belongs to a later task). Editor UI is functionally correct for producing the contract; full round-trip persistence requires that downstream wiring.
- Only one nesting level is supported (blend → sub-nutrients); grandchildren are not handled, matching current data model.
- The stale LSP diagnostics in `VitaTrack.Tests` (`ParentNutrientId`/`GetByParentIdAsync`) do not reflect actual compilation — both projects build 0 errors; the members exist in the merged infrastructure layer.

## Fix — Round-trip persistence gap (Important finding, now resolved)
- The editor POSTs a FLAT `nutrients[i]` list where child rows carry `ParentNutrientId` = the parent's sequential index. `PersistHierarchyAsync` only read `root.Children`, so every posted row was persisted as a ROOT and blends were never grouped.
- Added `SupplementNutrientService.NormalizeFlatToHierarchy(IEnumerable<SupplementNutrientDto>)` which:
  - Indexes the flat list 0-based by position.
  - For each DTO with `ParentNutrientId.HasValue`, attaches it to the parent DTO at that index's `Children` (creating `Children` if null); a DTO that also carries its own `Children` has them merged onto the parent.
  - Keeps DTOs without `ParentNutrientId` (or whose parent index is out of range) as roots, preserving any pre-set `Children`.
  - This handles both the editor path (`ParentNutrientId` set, no `Children`) and the LLM path (`Children` set, no `ParentNutrientId`) gracefully.
- `ReplaceAsync` now calls `PersistHierarchyAsync(supplementId, NormalizeFlatToHierarchy(nutrients))` instead of passing the flat list directly, so the blend grouping is reconstructed before persistence.
- Added service test `ReplaceAsync_FlatListWithParentNutrientId_GroupsIntoHierarchy`: posts 3 flat DTOs (index 0 root, 1 & 2 `ParentNutrientId = 0`); asserts 3 saved rows, root has no parent, both children reference the root's new Id.
- Verification: `dotnet test --filter FullyQualifiedName~Replace` (2 passed); full `dotnet test` (69 + 7 passed, 1 skipped); `dotnet format VitaTrack.sln --verify-no-changes` clean.
