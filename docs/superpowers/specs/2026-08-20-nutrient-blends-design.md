# Nutrient Blends + Optional Dosage — Design Spec

- Date: 2026-08-20
- Branch: feature/nutrient-blends (proposed)
- Depends on: `feature/ui-refinement` (Save/Enrich split) — keep on a new branch off `main` after that merges, or stack on it.

## Goal

1. Let a nutrient be saved **without a dosage** — but only when it is a **sub-nutrient inside a blend** (top-level nutrients still require a dosage).
2. Support **blends**: a parent nutrient with a total dosage that groups several sub-nutrients (e.g. *G.I. Detox+* → "500mg Proprietary Herbal Blend" → 6 sub-nutrients).
3. Let the **LLM enrichment** return blends in its extracted results, so Enrich populates grouped nutrients automatically.

## Current State (baseline)

- `SupplementNutrient` (`VitaTrack.Infrastructure/Models/SupplementNutrient.cs`): `Dosage` is `[Required]`; DB column `Dosage TEXT NOT NULL` (empty string satisfies NOT NULL — no migration needed for empties).
- `SupplementNutrientDto` (`LlmResult.cs:11`): flat `GenericName/SpecificForm/Dosage/Unit/AmountPerServing`, no hierarchy.
- Persistence: `SupplementNutrientService.PersistAsync` inserts a **flat** list; `ReplaceAsync` deletes all then re-adds. No parent/child concept.
- LLM: `SupplementLabelParser.BuildUserPrompt`/`BuildExtractionPrompt` asks for a flat `nutrients[]` array; `ParseNutrients` reads flat. `LlmService` compiles `NutritionJson` as `name → amount` dict.
- UI: `_NutrientEditor.cshtml` renders a flat `<table id="nutrients-table">`; `nutrient-editor.js` adds/removes rows; form posts `nutrients[i].*`.
- Standalone pages: `SupplementNutrientController` (Index/Create/Edit/Delete) manage single nutrients.

## Design

### 1. Data model

`SupplementNutrient` gains:
```csharp
public int? ParentNutrientId { get; set; }
public SupplementNutrient? ParentNutrient { get; set; }
```
- `DbInit`: `ALTER TABLE SupplementNutrients ADD COLUMN ParentNutrientId INTEGER NULL` (guarded by `pragma_table_info`, matching the existing `FrequencyPerDay` pattern). Add self-FK optionally (SQLite allows; not required for behavior). No data loss.
- `Dosage`: remove `[Required]` in the model. Validation rule enforced in the service (see §3), not the model attribute, because it depends on whether the row is a blend child.
- `SpecificForm` stays required (out of scope; blends can put a placeholder or the form name). Flag as possible follow-up.

### 2. DTO + LLM result

`SupplementNutrientDto` gains:
```csharp
public int? ParentNutrientId { get; set; }
public List<SupplementNutrientDto>? Children { get; set; }
```
- **LLM path** uses `Children` (nested). **Editor submit path** uses `ParentNutrientId` (flat list with a client temp key). Server normalizes both into the hierarchy before persisting.

### 3. Service — hierarchy persistence + dosage rule

`SupplementNutrientService`:
- Add `PersistHierarchyAsync(int supplementId, IEnumerable<SupplementNutrientDto> roots)` (or extend `PersistAsync`) that:
  - Iterates roots (top-level, `ParentNutrientId == null`). Validates: `GenericName` required (existing), **`Dosage` required for top-level**.
  - Inserts parent → gets new `Id`.
  - Recursively inserts `Children`, setting `ParentNutrientId = parent.Id`. Children may omit `Dosage` (still need `GenericName`).
  - `ReplaceAsync` calls this instead of the flat insert (delete-all-then-insert order preserved; children inserted after parents so FK holds).
- Keep `AddAsync` for non-blend single adds (standalone Create) but route blend-aware saves through `PersistHierarchyAsync`.
- Return `ReplaceNutrientsResult` with `Saved`/`Failures`; a top-level nutrient missing dosage becomes a `NutrientFailure` (mirrors existing failure handling).

### 4. LLM enrichment — prompt, schema, parser

`SupplementLabelParser`:
- `BuildExtractionPrompt`: instruct that a nutrient **may be a blend** — a `genericName` with a total `dosage` and a `children` array of sub-nutrients (each with `genericName`, `specificForm`, `dosage` optional, `unit`, `amountPerServing`). Example blend block added to the prompt.
- `BuildUserPrompt` JSON schema:
  ```json
  {
    "nutrients": [
      { "genericName": "...", "specificForm": "...", "dosage": "...",
        "unit": "...", "amountPerServing": 0,
        "children": [ { "genericName": "...", "specificForm": "...", "dosage": "..." } ] }
    ],
    "swapSuggestion": "..."
  }
  ```
- `ParseNutrients`: recurse — for each nutrient, parse `children` (if present) into the DTO's `Children` list. Blend dosage required; child dosage optional.
- `LlmService.EnrichSupplementAsync`: when building `NutritionJson`, include blend parents and (flattened, prefixed by `"<Blend> > <Child>"`) their children so downstream reports keep working.

### 5. Controller mapping

- `SupplementController.ToDtos(SupplementNutrient)`: map `ParentNutrientId` and recursively map `Children` (query children by `ParentNutrientId`) so `_NutrientEditor` shows grouped nutrients after Enrich.
- `Enrich` action: `SafeNutrients` + persist via `PersistHierarchyAsync` (not flat `AddAsync`); same for `Edit` POST merge — merge LLM `Children` under their blend, then persist hierarchy.
- `EditSave` / `CreateSave`: unchanged (no enrichment). `ConfirmCreate`/`ConfirmEdit`: persist via hierarchy-aware path.

### 6. UI — `_NutrientEditor.cshtml` + `nutrient-editor.js`

- Keep the flat `<table>`. Add **"Add Blend"** button → inserts a blend parent row (GenericName + **required** Dosage).
- Each blend row gets an **"Add sub-nutrient"** button → inserts an indented child row with `ParentNutrientId` = the parent's client `RowKey`; child Dosage input marked optional (no `required`).
- `nutrient-editor.js`: assign each row a `data-row-key`; child rows carry `data-parent-key`. On submit, rewrite field names to `nutrients[i].GenericName` etc. and set `nutrients[i].ParentNutrientId` from the resolved parent key → POST flat list. Children render indented (CSS) under parent.
- `hx-post="/Supplement/UpdateNutrients"` already swaps this partial; grouping survives the round-trip because the server returns `ToDtos` with `Children`.

### 7. Standalone pages (`SupplementNutrientController`)

- `Create`/`Edit` views: add an optional **"Parent blend"** dropdown (lists existing top-level nutrients for that supplement). Submit sets `ParentNutrientId`. Dosage optional when a parent is chosen.

### 8. Reports / counts

- `GetCountsBySupplementIdsAsync` counts every row (blends + children) — unchanged behavior; reports still work.

### 9. Seed data (optional, for demo/E2E)

- In `DbInit` seed, add one blend to a seeded supplement (e.g. supplement 3 "Multivitamin" → "Proprietary Blend" 500mg with 2 child nutrients) so blends are visible without manual entry.

## Testing

- **Unit/service** (`SupplementNutrientServiceTests` / `SupplementNutrientRepositoryTests`):
  - Persist a blend: parent + children stored; child `ParentNutrientId` = parent Id.
  - Top-level nutrient without dosage → `NutrientFailure`; child without dosage → success.
  - `ReplaceAsync` with blends preserves hierarchy.
- **LLM** (`LlmServiceTests` / `SupplementLabelParser` tests with `Moq`ed `ILlmClient`):
  - Parser extracts nested `children` from a blend JSON; builds `NutritionJson` including blend + children.
  - Prompt contains blend instructions (assert prompt string includes "blend"/"children").
- **Controller** (`SupplementControllerTests`): `Enrich` returns editor whose model contains a blend with children; `ToDtos` maps hierarchy.
- **Playwright** (`e2e-tests/playwright`):
  - Create/Edit a supplement, add a blend with 2 sub-nutrients (one without dosage), save, reload, verify grouped rows present.
  - Enrich a supplement with a URL whose label has a blend → editor shows grouped nutrients.

## Out of scope

- Editing blend membership reordering, drag-and-drop, multi-level nesting beyond one level (blend → children only).
- Making `SpecificForm` optional (flagged; blends can use a placeholder).
- Changing report calculations (counts only).
