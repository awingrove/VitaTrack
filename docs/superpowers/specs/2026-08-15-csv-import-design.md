# Supplement CSV Import — Design Spec

**Date:** 2026-08-15  
**Branch:** `feature/csv-import`

## Overview

Add a CSV import feature to the Supplement list page. Users upload a fixed-format CSV containing supplement entries (including ManufacturerUrl). The server parses, validates, enriches each supplement via LLM, and saves. A text report is displayed in a modal showing successes and failures.

## CSV Format

Fixed 5-column format. First row is header.

```
Name,Brand,DailyDose,ManufacturerUrl,Cost
Vitamin D3,NatureWise,2 capsules,https://naturewise.com/vitamin-d3,15.99
Magnesium Glycinate,Doctor's Best,2 tablets,https://drsbest.com/magnesium,12.49
Zinc Complex,NOW,1 capsule,,
```

| Column          | Required | Notes                        |
|-----------------|----------|------------------------------|
| Name            | yes      | max 200 chars                |
| Brand           | yes      | max 200 chars                |
| DailyDose       | yes      | e.g. "2 tablets", max 200    |
| ManufacturerUrl | no       | max 500 chars, for LLM       |
| Cost            | no       | decimal, positive            |

## Architecture

### Approach: Synchronous POST with row cap

- Upload → single POST to `/Supplement/ImportCsv`
- Sequential processing: parse → validate → enrich (LLM) → save per row
- Hard cap: 20 rows max per upload
- Returns report partial swapped into modal body via HTMX

No background jobs, no SignalR, no polling. Matches existing enrichment pattern.

### New Files

| File | Layer | Purpose |
|------|-------|---------|
| `Infrastructure/Services/CsvImportService.cs` | Service | CSV parsing and validation |
| `Infrastructure/Services/ICsvImportService.cs` | Service | Interface |
| `Infrastructure/Models/CsvSupplementRow.cs` | Model | Parsed row DTO |
| `Infrastructure/Models/CsvImportReport.cs` | Model | Report result model |
| `VitaTrack.Web/Views/Supplement/_ImportModal.cshtml` | View | Bootstrap modal for upload |
| `VitaTrack.Web/Views/Supplement/_ImportReport.cshtml` | View | Report content partial |
| `wwwroot/templates/supplements-sample.csv` | Static | Downloadable sample CSV |

### Modified Files

| File | Change |
|------|--------|
| `SupplementController.cs` | Add `ImportCsv` action |
| `Views/Supplement/Index.cshtml` | Add "Import CSV" button + include modal partial |
| `ServiceCollectionExtensions.cs` | Register `ICsvImportService` |

## UI Design

### Index.cshtml Changes

- Add "Import CSV" button in action bar next to "Add New Supplement" (same row)
- Button has `data-bs-toggle="modal" data-bs-target="#importCsvModal"`
- Include `_ImportModal` partial at page bottom

### _ImportModal.cshtml

Bootstrap modal containing:
- Header: "Import Supplements from CSV"
- Body:
  - "Download sample CSV" link → `/templates/supplements-sample.csv`
  - File input (`<input type="file" accept=".csv">`)
  - "Upload & Import" button
  - Spinner (hidden, shown on submit)
- HTMX form: `hx-post="/Supplement/ImportCsv"` `hx-encoding="multipart/form-data"` `hx-target="#import-report-container"` `hx-indicator="#import-spinner"`
- Report container div: `#import-report-container`

### _ImportReport.cshtml

Swapped into modal body after import:
- Summary: "Total: X | Imported: Y | Failed: Z"
- Success list: checkmark icon, supplement name + brand + nutrient count
- Failure list: X icon, row number + name + error message
- "Close" button to dismiss modal

## CSV Parser — CsvImportService

### ParseAsync(Stream csvStream) → CsvParseResult

1. Strip BOM if present
2. Read all lines
3. Validate header row matches `Name,Brand,DailyDose,ManufacturerUrl,Cost`
4. Skip empty lines
5. If >20 data rows → reject entire file, return error
6. For each line:
   - Parse fields (handle quoted commas)
   - Trim whitespace
   - Validate required fields (Name, Brand, DailyDose) non-empty
   - Parse Cost as `decimal?` (null if empty, error if non-numeric)
   - Create `CsvSupplementRow` with RowNumber
7. Return `CsvParseResult` with `Rows` and `Errors`

### Edge Cases

- Quoted fields: `"Vitamin D, 5000 IU"` → single field
- Empty lines: skip
- BOM character: strip
- Extra/missing columns: row error
- Invalid Cost: row error with message

## Controller — SupplementController.ImportCsv

```
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> ImportCsv(IFormFile file)
```

1. Validate file not null/empty, has `.csv` extension
2. Call `CsvImportService.ParseAsync(file.OpenReadStream())`
3. If parse has errors and no valid rows → return `_ImportReport` with all failures
4. For each valid row:
   a. Create `Supplement` from row (map fields)
   b. If ManufacturerUrl present → `LlmService.EnrichSupplementAsync(supplement)`
   c. `SupplementRepository.AddAsync(supplement)` → get Id
   d. If enrichment returned nutrients → `SupplementNutrientService.AddAsync(id, nutrients)`
   e. Add to successes (with nutrient count) or failures (with error)
5. Build `CsvImportReport`
6. Return `_ImportReport` partial

### Error Handling

- Each row is try/caught independently — one failure doesn't block others
- LLM timeout/error → row failure with message
- DB error → row failure with message

## Models

### CsvSupplementRow
```csharp
public record CsvSupplementRow(
    int RowNumber,
    string Name,
    string Brand,
    string DailyDose,
    string? ManufacturerUrl,
    decimal? Cost
);
```

### CsvImportReport
```csharp
public record CsvImportReport(
    int TotalRows,
    List<CsvImportSuccess> Successes,
    List<CsvImportFailure> Failures
);

public record CsvImportSuccess(string Name, string Brand, int NutrientCount);
public record CsvImportFailure(int RowNumber, string Name, string ErrorMessage);
```

### CsvParseResult
```csharp
public record CsvParseResult(
    List<CsvSupplementRow> Rows,
    List<CsvParseError> Errors
);

public record CsvParseError(int RowNumber, string Message);
```

## Testing

### Unit Tests — CsvImportServiceTests

- Valid CSV 3 rows → 3 rows, 0 errors
- Missing Name → error with row number
- Missing Brand → error with row number
- Missing DailyDose → error with row number
- >20 rows → entire file rejected
- Quoted field with comma → parsed correctly
- Empty lines → skipped
- Invalid Cost → error
- BOM character → handled
- Wrong header → rejected

### E2E Tests — csv-import.spec.js

- Upload valid CSV → report shows correct counts, supplements appear in list
- Upload CSV with errors → report shows failures with row numbers
- Download sample CSV link works
- Modal opens and closes

## Dependencies

- No new NuGet packages (custom CSV parser)
- Reuses existing: `ILlmService`, `ISupplementRepository`, `ISupplementNutrientService`
- Bootstrap 5 modal (already available)
- HTMX for form submission and partial swap
