# Supplement CSV Import — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add CSV import to the Supplement list page — upload a fixed-format CSV, enrich each supplement via LLM, save all, display a text report in a modal.

**Architecture:** Synchronous POST with 20-row cap. Custom CSV parser (no library). HTMX multipart form submission swaps report partial into modal body. Each row processed independently — one failure doesn't block others.

**Tech Stack:** ASP.NET MVC, HTMX, Bootstrap 5 modal, Dapper, custom CSV parser.

## Global Constraints

- No new NuGet packages (custom CSV parser)
- Max 20 data rows per CSV upload (hard cap)
- Each row try/caught independently — failures don't block successes
- `[ValidateAntiForgeryToken]` on POST action
- No inline `<script>` — JS in external files under `wwwroot/js/`
- CSP: `script-src 'self' cdn.jsdelivr.net`
- Follow existing controller → repository/service pattern
- File-scoped namespaces, primary constructors
- No file > 300 lines

---

### Task 1: Models — CsvSupplementRow, CsvImportReport, CsvParseResult

**Files:**
- Create: `VitaTrack.Infrastructure/Models/CsvSupplementRow.cs`
- Create: `VitaTrack.Infrastructure/Models/CsvImportReport.cs`

**Interfaces:**
- Produces: `CsvSupplementRow`, `CsvImportReport`, `CsvImportSuccess`, `CsvImportFailure`, `CsvParseResult`, `CsvParseError` — used by Tasks 2, 3, 4

- [ ] **Step 1: Create CsvSupplementRow model**

```csharp
// VitaTrack.Infrastructure/Models/CsvSupplementRow.cs
namespace VitaTrack.Infrastructure.Models;

public record CsvSupplementRow(
    int RowNumber,
    string Name,
    string Brand,
    string DailyDose,
    string? ManufacturerUrl,
    decimal? Cost
);
```

- [ ] **Step 2: Create CsvImportReport and related models**

```csharp
// VitaTrack.Infrastructure/Models/CsvImportReport.cs
namespace VitaTrack.Infrastructure.Models;

public record CsvImportReport(
    int TotalRows,
    List<CsvImportSuccess> Successes,
    List<CsvImportFailure> Failures
);

public record CsvImportSuccess(string Name, string Brand, int NutrientCount);

public record CsvImportFailure(int RowNumber, string Name, string ErrorMessage);

public record CsvParseResult(
    List<CsvSupplementRow> Rows,
    List<CsvParseError> Errors
);

public record CsvParseError(int RowNumber, string Message);
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build VitaTrack.sln`
Expected: BUILD SUCCEEDED

- [ ] **Step 4: Commit**

```bash
git add VitaTrack.Infrastructure/Models/CsvSupplementRow.cs VitaTrack.Infrastructure/Models/CsvImportReport.cs
git commit -m "feat: add CSV import models"
```

---

### Task 2: CsvImportService — Parser Implementation

**Files:**
- Create: `VitaTrack.Infrastructure/Services/ICsvImportService.cs`
- Create: `VitaTrack.Infrastructure/Services/CsvImportService.cs`
- Modify: `VitaTrack.Infrastructure/ServiceCollectionExtensions.cs` (add DI registration)

**Interfaces:**
- Consumes: `CsvSupplementRow`, `CsvParseResult`, `CsvParseError` (from Task 1)
- Produces: `ICsvImportService.ParseAsync(Stream)` → `CsvParseResult` — used by Task 3

- [ ] **Step 1: Create ICsvImportService interface**

```csharp
// VitaTrack.Infrastructure/Services/ICsvImportService.cs
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Infrastructure.Services;

public interface ICsvImportService
{
    Task<CsvParseResult> ParseAsync(Stream csvStream);
}
```

- [ ] **Step 2: Create CsvImportService implementation**

```csharp
// VitaTrack.Infrastructure/Services/CsvImportService.cs
using System.Text;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Infrastructure.Services;

public class CsvImportService : ICsvImportService
{
    private const int MaxRows = 20;
    private static readonly string[] ExpectedHeaders = ["Name", "Brand", "DailyDose", "ManufacturerUrl", "Cost"];

    public async Task<CsvParseResult> ParseAsync(Stream csvStream)
    {
        var rows = new List<CsvSupplementRow>();
        var errors = new List<CsvParseError>();

        using var reader = new StreamReader(csvStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lineNumber = 0;
        string? headerLine = null;

        while (await reader.ReadLineAsync() is { } line)
        {
            lineNumber++;
            line = line.Trim();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (headerLine == null)
            {
                headerLine = line;
                var headerError = ValidateHeader(line);
                if (headerError != null)
                {
                    errors.Add(new CsvParseError(0, headerError));
                    return new CsvParseResult(rows, errors);
                }
                continue;
            }

            if (rows.Count >= MaxRows)
            {
                errors.Add(new CsvParseError(0, $"CSV exceeds maximum of {MaxRows} rows"));
                return new CsvParseResult(new List<CsvSupplementRow>(), errors);
            }

            var fields = ParseCsvLine(line);
            var rowResult = ParseRow(lineNumber, fields);
            if (rowResult.Error != null)
                errors.Add(rowResult.Error);
            else if (rowResult.Row != null)
                rows.Add(rowResult.Row);
        }

        if (headerLine == null)
            errors.Add(new CsvParseError(0, "CSV file is empty"));

        return new CsvParseResult(rows, errors);
    }

    private static string? ValidateHeader(string headerLine)
    {
        var headers = ParseCsvLine(headerLine);
        if (headers.Length != ExpectedHeaders.Length)
            return $"Expected {ExpectedHeaders.Length} columns, found {headers.Length}";

        for (var i = 0; i < ExpectedHeaders.Length; i++)
        {
            if (!string.Equals(headers[i].Trim(), ExpectedHeaders[i], StringComparison.OrdinalIgnoreCase))
                return $"Column {i + 1}: expected '{ExpectedHeaders[i]}', found '{headers[i].Trim()}'";
        }

        return null;
    }

    private static (CsvSupplementRow? Row, CsvParseError? Error) ParseRow(int lineNumber, string[] fields)
    {
        var name = fields.Length > 0 ? fields[0].Trim() : string.Empty;
        var brand = fields.Length > 1 ? fields[1].Trim() : string.Empty;
        var dailyDose = fields.Length > 2 ? fields[2].Trim() : string.Empty;
        var manufacturerUrl = fields.Length > 3 ? fields[3].Trim() : null;
        var costStr = fields.Length > 4 ? fields[4].Trim() : null;

        if (string.IsNullOrWhiteSpace(name))
            return (null, new CsvParseError(lineNumber, "Missing required field: Name"));
        if (string.IsNullOrWhiteSpace(brand))
            return (null, new CsvParseError(lineNumber, "Missing required field: Brand"));
        if (string.IsNullOrWhiteSpace(dailyDose))
            return (null, new CsvParseError(lineNumber, "Missing required field: DailyDose"));

        decimal? cost = null;
        if (!string.IsNullOrWhiteSpace(costStr))
        {
            if (decimal.TryParse(costStr, out var parsed))
                cost = parsed;
            else
                return (null, new CsvParseError(lineNumber, $"Invalid Cost value: '{costStr}'"));
        }

        if (string.IsNullOrWhiteSpace(manufacturerUrl))
            manufacturerUrl = null;

        var row = new CsvSupplementRow(lineNumber, name, brand, dailyDose, manufacturerUrl, cost);
        return (row, null);
    }

    internal static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
```

- [ ] **Step 3: Register in DI**

Add to `VitaTrack.Infrastructure/ServiceCollectionExtensions.cs` after line 53 (`services.AddScoped<ISupplementLabelParser, SupplementLabelParser>();`):

```csharp
services.AddScoped<ICsvImportService, CsvImportService>();
```

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build VitaTrack.sln`
Expected: BUILD SUCCEEDED

- [ ] **Step 5: Commit**

```bash
git add VitaTrack.Infrastructure/Services/ICsvImportService.cs VitaTrack.Infrastructure/Services/CsvImportService.cs VitaTrack.Infrastructure/ServiceCollectionExtensions.cs
git commit -m "feat: add CsvImportService with custom CSV parser"
```

---

### Task 3: Unit Tests — CsvImportService

**Files:**
- Create: `VitaTrack.Tests/CsvImportServiceTests.cs`

**Interfaces:**
- Consumes: `ICsvImportService.ParseAsync(Stream)` (from Task 2)
- Consumes: `CsvSupplementRow`, `CsvParseResult`, `CsvParseError` (from Task 1)

- [ ] **Step 1: Create CsvImportServiceTests**

```csharp
// VitaTrack.Tests/CsvImportServiceTests.cs
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Infrastructure.Services;

namespace VitaTrack.Tests;

[TestClass]
public class CsvImportServiceTests
{
    private readonly CsvImportService _service = new();

    private static Stream ToStream(string csv) => new MemoryStream(Encoding.UTF8.GetBytes(csv));

    [TestMethod]
    public async Task ParseAsync_ValidCsvThreeRows_ReturnsThreeRows()
    {
        var csv = """
            Name,Brand,DailyDose,ManufacturerUrl,Cost
            Vitamin D3,NatureWise,2 capsules,https://example.com,15.99
            Magnesium,Doctor's Best,2 tablets,https://example.com,12.49
            Zinc Complex,NOW,1 capsule,,
            """;

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(3, result.Rows.Count);
        Assert.AreEqual(0, result.Errors.Count);
        Assert.AreEqual("Vitamin D3", result.Rows[0].Name);
        Assert.AreEqual("NatureWise", result.Rows[0].Brand);
        Assert.AreEqual(15.99m, result.Rows[0].Cost);
        Assert.IsNull(result.Rows[2].Cost);
        Assert.IsNull(result.Rows[2].ManufacturerUrl);
    }

    [TestMethod]
    public async Task ParseAsync_MissingName_ReturnsErrorForRow()
    {
        var csv = """
            Name,Brand,DailyDose,ManufacturerUrl,Cost
            ,BrandX,1 tablet,,
            """;

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(0, result.Rows.Count);
        Assert.AreEqual(1, result.Errors.Count);
        Assert.AreEqual(2, result.Errors[0].RowNumber);
        Assert.IsTrue(result.Errors[0].Message.Contains("Name"));
    }

    [TestMethod]
    public async Task ParseAsync_MissingBrand_ReturnsErrorForRow()
    {
        var csv = """
            Name,Brand,DailyDose,ManufacturerUrl,Cost
            Vitamin C,,500mg,,
            """;

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(0, result.Rows.Count);
        Assert.AreEqual(1, result.Errors.Count);
        Assert.IsTrue(result.Errors[0].Message.Contains("Brand"));
    }

    [TestMethod]
    public async Task ParseAsync_MissingDailyDose_ReturnsErrorForRow()
    {
        var csv = """
            Name,Brand,DailyDose,ManufacturerUrl,Cost
            Vitamin C,NatureWise,,,
            """;

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(0, result.Rows.Count);
        Assert.AreEqual(1, result.Errors.Count);
        Assert.IsTrue(result.Errors[0].Message.Contains("DailyDose"));
    }

    [TestMethod]
    public async Task ParseAsync_Exceeds20Rows_RejectsEntireFile()
    {
        var sb = new StringBuilder("Name,Brand,DailyDose,ManufacturerUrl,Cost\n");
        for (var i = 1; i <= 21; i++)
            sb.AppendLine($"Product{i},Brand{i},1 tablet,,");

        var result = await _service.ParseAsync(ToStream(sb.ToString()));

        Assert.AreEqual(0, result.Rows.Count);
        Assert.IsTrue(result.Errors.Any(e => e.Message.Contains("20")));
    }

    [TestMethod]
    public async Task ParseAsync_QuotedFieldWithComma_ParsedCorrectly()
    {
        var csv = """
            Name,Brand,DailyDose,ManufacturerUrl,Cost
            "Vitamin D, 5000 IU",NatureWise,1 capsule,,
            """;

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(1, result.Rows.Count);
        Assert.AreEqual("Vitamin D, 5000 IU", result.Rows[0].Name);
    }

    [TestMethod]
    public async Task ParseAsync_EmptyLines_Skipped()
    {
        var csv = """

            Name,Brand,DailyDose,ManufacturerUrl,Cost

            Vitamin D3,NatureWise,2 capsules,,

            Zinc,NOW,1 capsule,,

            """;

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(2, result.Rows.Count);
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public async Task ParseAsync_InvalidCost_ReturnsError()
    {
        var csv = """
            Name,Brand,DailyDose,ManufacturerUrl,Cost
            Vitamin D3,NatureWise,2 capsules,,abc
            """;

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(0, result.Rows.Count);
        Assert.AreEqual(1, result.Errors.Count);
        Assert.IsTrue(result.Errors[0].Message.Contains("Cost"));
    }

    [TestMethod]
    public async Task ParseAsync_BomCharacter_Handled()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF };
        var csvBytes = Encoding.UTF8.GetBytes("Name,Brand,DailyDose,ManufacturerUrl,Cost\nVitamin D3,NatureWise,2 capsules,,");
        var stream = new MemoryStream(bytes.Concat(csvBytes).ToArray());

        var result = await _service.ParseAsync(stream);

        Assert.AreEqual(1, result.Rows.Count);
        Assert.AreEqual("Vitamin D3", result.Rows[0].Name);
    }

    [TestMethod]
    public async Task ParseAsync_WrongHeader_Rejected()
    {
        var csv = """
            Foo,Bar,Baz,Qux,Quux
            Vitamin D3,NatureWise,2 capsules,,
            """;

        var result = await _service.ParseAsync(ToStream(csv));

        Assert.AreEqual(0, result.Rows.Count);
        Assert.IsTrue(result.Errors.Count > 0);
    }

    [TestMethod]
    public async Task ParseAsync_EmptyFile_ReturnsError()
    {
        var result = await _service.ParseAsync(ToStream(""));

        Assert.AreEqual(0, result.Rows.Count);
        Assert.IsTrue(result.Errors.Any(e => e.Message.Contains("empty")));
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test VitaTrack.Tests --filter CsvImportServiceTests`
Expected: All tests PASS

- [ ] **Step 3: Commit**

```bash
git add VitaTrack.Tests/CsvImportServiceTests.cs
git commit -m "test: add CsvImportService unit tests"
```

---

### Task 4: Controller — ImportCsv Action

**Files:**
- Modify: `VitaTrack.Web/Controllers/SupplementController.cs` — add `ImportCsv` action + inject `ICsvImportService`

**Interfaces:**
- Consumes: `ICsvImportService.ParseAsync(Stream)` (from Task 2)
- Consumes: `CsvSupplementRow`, `CsvImportReport`, `CsvImportSuccess`, `CsvImportFailure` (from Task 1)
- Consumes: `ILlmService.EnrichSupplementAsync(Supplement)` (existing)
- Consumes: `ISupplementRepository.AddAsync(Supplement)` (existing)
- Consumes: `ISupplementNutrientService.AddAsync(int, IEnumerable<SupplementNutrientDto>)` (existing)
- Produces: `ImportCsv(IFormFile)` → returns `_ImportReport` partial — used by Task 5

- [ ] **Step 1: Add ICsvImportService to constructor**

Update the `SupplementController` constructor to add the new dependency:

```csharp
public class SupplementController(
    ISupplementRepository suppRepo,
    ISupplementNutrientRepository nutrientRepo,
    ISupplementNutrientService nutrientService,
    ILlmService llmService,
    ICsvImportService csvImportService) : Controller
{
    private readonly ISupplementRepository _suppRepo = suppRepo;
    private readonly ISupplementNutrientRepository _nutrientRepo = nutrientRepo;
    private readonly ISupplementNutrientService _nutrientService = nutrientService;
    private readonly ILlmService _llmService = llmService;
    private readonly ICsvImportService _csvImportService = csvImportService;
```

- [ ] **Step 2: Add ImportCsv action**

Add before the `Delete` action (before line 160):

```csharp
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> ImportCsv(IFormFile file)
{
    if (file == null || file.Length == 0)
        return PartialView("_ImportReport", new CsvImportReport(0, [], [new CsvParseError(0, "No file uploaded").ToFailure("No file")]));

    if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        return PartialView("_ImportReport", new CsvImportReport(0, [], [new CsvParseError(0, "File must be a .csv").ToFailure("Invalid file")]));

    CsvParseResult parseResult;
    await using var stream = file.OpenReadStream();
    parseResult = await _csvImportService.ParseAsync(stream);

    if (parseResult.Errors.Count > 0 && parseResult.Rows.Count == 0)
    {
        var failures = parseResult.Errors.Select(e => new CsvImportFailure(e.RowNumber, "N/A", e.Message)).ToList();
        return PartialView("_ImportReport", new CsvImportReport(0, [], failures));
    }

    var successes = new List<CsvImportSuccess>();
    var failuresList = new List<CsvImportFailure>();

    foreach (var row in parseResult.Rows)
    {
        try
        {
            var supplement = new Supplement
            {
                Name = row.Name,
                Brand = row.Brand,
                DailyDose = row.DailyDose,
                ManufacturerUrl = row.ManufacturerUrl,
                Cost = row.Cost
            };

            var nutrientCount = 0;
            if (!string.IsNullOrWhiteSpace(row.ManufacturerUrl))
            {
                var llmResult = await _llmService.EnrichSupplementAsync(supplement);
                supplement.NutritionJson = llmResult.NutritionJson;
                supplement.SwapSuggestion = llmResult.SwapSuggestion;

                var newId = await _suppRepo.AddAsync(supplement);
                if (llmResult.Nutrients.Count > 0)
                {
                    var persistResult = await _nutrientService.AddAsync(newId, llmResult.Nutrients);
                    nutrientCount = persistResult.Saved.Count;
                }
            }
            else
            {
                await _suppRepo.AddAsync(supplement);
            }

            successes.Add(new CsvImportSuccess(row.Name, row.Brand, nutrientCount));
        }
        catch (Exception ex)
        {
            failuresList.Add(new CsvImportFailure(row.RowNumber, row.Name, ex.Message));
        }
    }

    foreach (var error in parseResult.Errors)
    {
        failuresList.Add(new CsvImportFailure(error.RowNumber, "N/A", error.Message));
    }

    var report = new CsvImportReport(parseResult.Rows.Count + parseResult.Errors.Count, successes, failuresList);
    return PartialView("_ImportReport", report);
}
```

- [ ] **Step 3: Add helper extension for CsvParseError → CsvImportFailure**

Add a private static helper at the bottom of the controller class (inside the class, before the closing brace):

```csharp
private static CsvImportFailure ToFailure(this CsvParseError error, string name)
    => new(error.RowNumber, name, error.Message);
```

Wait — extension methods can't be in a non-static class easily. Instead, inline the conversion in the action. Let me adjust: remove the `ToFailure` call and inline it.

Actually, the `ToFailure` helper can be a private static method in the controller. Update the action to use inline conversion instead of an extension method. Replace `new CsvParseError(0, "No file uploaded").ToFailure("No file")` with `new CsvImportFailure(0, "No file", "No file uploaded")` and similar.

- [ ] **Step 4: Add using for ICsvImportService**

The controller already has `using VitaTrack.Infrastructure.Services;` which covers it. No change needed.

- [ ] **Step 5: Build to verify compilation**

Run: `dotnet build VitaTrack.sln`
Expected: BUILD SUCCEEDED

- [ ] **Step 6: Commit**

```bash
git add VitaTrack.Web/Controllers/SupplementController.cs
git commit -m "feat: add ImportCsv action to SupplementController"
```

---

### Task 5: Unit Tests — SupplementController.ImportCsv

**Files:**
- Modify: `VitaTrack.Tests/SupplementControllerTests.cs` — add tests for ImportCsv

**Interfaces:**
- Consumes: `SupplementController.ImportCsv(IFormFile)` (from Task 4)

- [ ] **Step 1: Add ICsvImportService mock to test setup**

Add to the test class fields:

```csharp
private Mock<ICsvImportService> _csvImportService = null!;
```

Update `Setup` method to initialize and pass to controller:

```csharp
_csvImportService = new Mock<ICsvImportService>();
_controller = new SupplementController(
    _suppRepo.Object,
    _nutrientRepo.Object,
    _nutrientService.Object,
    _llmService.Object,
    _csvImportService.Object);
```

- [ ] **Step 2: Add ImportCsv test — null file returns error report**

```csharp
[TestMethod]
public async Task ImportCsv_NullFile_ReturnsErrorReport()
{
    var result = await _controller.ImportCsv(null!);

    var partialResult = result as PartialViewResult;
    Assert.IsNotNull(partialResult);
    var report = partialResult.Model as CsvImportReport;
    Assert.IsNotNull(report);
    Assert.AreEqual(0, report.Successes.Count);
    Assert.IsTrue(report.Failures.Count > 0);
}
```

- [ ] **Step 3: Add ImportCsv test — valid CSV processes rows**

```csharp
[TestMethod]
public async Task ImportCsv_ValidCsv_ReturnsSuccessReport()
{
    var csvRows = new List<CsvSupplementRow>
    {
        new(2, "Vitamin D3", "NatureWise", "2 capsules", null, null)
    };
    var parseResult = new CsvParseResult(csvRows, []);
    _csvImportService.Setup(s => s.ParseAsync(It.IsAny<Stream>())).ReturnsAsync(parseResult);
    _suppRepo.Setup(r => r.AddAsync(It.IsAny<Supplement>())).ReturnsAsync(1);

    var file = CreateFormFile("Name,Brand,DailyDose\nVitamin D3,NatureWise,2 capsules");
    var result = await _controller.ImportCsv(file);

    var partialResult = result as PartialViewResult;
    Assert.IsNotNull(partialResult);
    var report = partialResult.Model as CsvImportReport;
    Assert.IsNotNull(report);
    Assert.AreEqual(1, report.Successes.Count);
    Assert.AreEqual(0, report.Failures.Count);
    _suppRepo.Verify(r => r.AddAsync(It.IsAny<Supplement>()), Times.Once);
}
```

- [ ] **Step 4: Add helper method for creating IFormFile**

```csharp
private static IFormFile CreateFormFile(string content)
{
    var bytes = System.Text.Encoding.UTF8.GetBytes(content);
    var stream = new MemoryStream(bytes);
    return new FormFile(stream, 0, bytes.Length, "file", "test.csv");
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet test VitaTrack.Tests --filter SupplementControllerTests`
Expected: All tests PASS

- [ ] **Step 6: Commit**

```bash
git add VitaTrack.Tests/SupplementControllerTests.cs
git commit -m "test: add ImportCsv controller unit tests"
```

---

### Task 6: Views — _ImportModal and _ImportReport Partials

**Files:**
- Create: `VitaTrack.Web/Views/Supplement/_ImportModal.cshtml`
- Create: `VitaTrack.Web/Views/Supplement/_ImportReport.cshtml`
- Create: `wwwroot/templates/supplements-sample.csv`

**Interfaces:**
- Consumes: `CsvImportReport` model (from Task 1) — used by `_ImportReport.cshtml`

- [ ] **Step 1: Create sample CSV file**

```csv
Name,Brand,DailyDose,ManufacturerUrl,Cost
Vitamin D3,NatureWise,2 capsules,https://www.naturewise.com/products/vitamin-d3,15.99
Magnesium Glycinate,Doctor's Best,2 tablets,,12.49
Zinc Complex,NOW,1 capsule,,
```

Save to: `wwwroot/templates/supplements-sample.csv`

- [ ] **Step 2: Create _ImportModal.cshtml**

```html
<!-- VitaTrack.Web/Views/Supplement/_ImportModal.cshtml -->
<div class="modal fade" id="importCsvModal" tabindex="-1" aria-labelledby="importCsvModalLabel" aria-hidden="true">
    <div class="modal-dialog modal-lg">
        <div class="modal-content">
            <div class="modal-header">
                <h5 class="modal-title" id="importCsvModalLabel">Import Supplements from CSV</h5>
                <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
            </div>
            <div class="modal-body">
                <p>Upload a CSV file with columns: <code>Name, Brand, DailyDose, ManufacturerUrl, Cost</code></p>
                <p><a href="/templates/supplements-sample.csv" download>Download sample CSV</a></p>
                <p class="text-muted small">Maximum 20 rows per upload.</p>

                <form hx-post="/Supplement/ImportCsv"
                      hx-encoding="multipart/form-data"
                      hx-target="#import-report-container"
                      hx-indicator="#import-spinner"
                      hx-swap="innerHTML">
                    @Html.AntiForgeryToken()
                    <div class="mb-3">
                        <input type="file" name="file" accept=".csv" class="form-control" required />
                    </div>
                    <button type="submit" class="btn btn-primary">Upload &amp; Import</button>
                </form>

                <div id="import-spinner" class="ht-indicator text-center mt-3" style="display:none;">
                    <div class="spinner-border text-primary" role="status">
                        <span class="visually-hidden">Processing...</span>
                    </div>
                    <p class="mt-2">Importing supplements and enriching via LLM...</p>
                </div>

                <div id="import-report-container" class="mt-3"></div>
            </div>
        </div>
    </div>
</div>
```

- [ ] **Step 3: Create _ImportReport.cshtml**

```html
<!-- VitaTrack.Web/Views/Supplement/_ImportReport.cshtml -->
@using VitaTrack.Infrastructure.Models
@model CsvImportReport

<div class="card">
    <div class="card-header">
        <strong>CSV Import Report</strong>
    </div>
    <div class="card-body">
        <p>
            <strong>Total rows:</strong> @Model.TotalRows |
            <span class="text-success"><strong>Imported:</strong> @Model.Successes.Count</span> |
            <span class="text-danger"><strong>Failed:</strong> @Model.Failures.Count</span>
        </p>

        @if (Model.Successes.Count > 0)
        {
            <h6 class="text-success mt-3">Imported</h6>
            <ul class="list-group mb-3">
                @foreach (var s in Model.Successes)
                {
                    <li class="list-group-item list-group-item-success">
                        &#10003; <strong>@s.Name</strong> by @s.Brand
                        @if (s.NutrientCount > 0)
                        {
                            <span>(enriched with @s.NutrientCount nutrient(s))</span>
                        }
                    </li>
                }
            </ul>
        }

        @if (Model.Failures.Count > 0)
        {
            <h6 class="text-danger mt-3">Failed</h6>
            <ul class="list-group mb-3">
                @foreach (var f in Model.Failures)
                {
                    <li class="list-group-item list-group-item-danger">
                        &#10007; Row @f.RowNumber: <strong>@f.Name</strong> — @f.ErrorMessage
                    </li>
                }
            </ul>
        }

        <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Close</button>
    </div>
</div>

<script src="~/js/import-csv.js"></script>
```

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build VitaTrack.sln`
Expected: BUILD SUCCEEDED

- [ ] **Step 5: Commit**

```bash
git add VitaTrack.Web/Views/Supplement/_ImportModal.cshtml VitaTrack.Web/Views/Supplement/_ImportReport.cshtml wwwroot/templates/supplements-sample.csv
git commit -m "feat: add CSV import modal and report partials"
```

---

### Task 7: Wire Up Index.cshtml + JS

**Files:**
- Modify: `VitaTrack.Web/Views/Supplement/Index.cshtml`
- Create: `wwwroot/js/import-csv.js`

**Interfaces:**
- Consumes: `_ImportModal` partial (from Task 6)

- [ ] **Step 1: Add Import CSV button to Index.cshtml**

Replace line 10 (`<p><a class="btn btn-success" asp-action="Create">Add New Supplement</a></p>`) with:

```html
<p>
    <a class="btn btn-success" asp-action="Create">Add New Supplement</a>
    <button type="button" class="btn btn-outline-primary" data-bs-toggle="modal" data-bs-target="#importCsvModal">Import CSV</button>
</p>
```

- [ ] **Step 2: Include _ImportModal partial at bottom of Index.cshtml**

Add before the closing `</table>` tag or after the `<script>` tag at the bottom:

```html
<partial name="_ImportModal" />
```

Add after the existing `<script src="~/js/delete-selected.js"></script>` line.

- [ ] **Step 3: Create import-csv.js**

```javascript
// wwwroot/js/import-csv.js
(function () {
    document.body.addEventListener('htmx:afterSwap', function (evt) {
        if (evt.detail.target.id === 'import-report-container') {
            var spinner = document.getElementById('import-spinner');
            if (spinner) spinner.style.display = 'none';
        }
    });
})();
```

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build VitaTrack.sln`
Expected: BUILD SUCCEEDED

- [ ] **Step 5: Commit**

```bash
git add VitaTrack.Web/Views/Supplement/Index.cshtml wwwroot/js/import-csv.js
git commit -m "feat: wire up CSV import modal on supplement list page"
```

---

### Task 8: Update SupplementControllerTests for new constructor

**Files:**
- Modify: `VitaTrack.Tests/SupplementControllerTests.cs` — update all test setup to pass ICsvImportService mock

- [ ] **Step 1: Update Setup method**

The controller constructor now requires `ICsvImportService`. Update the `Setup` method:

```csharp
[TestInitialize]
public void Setup()
{
    _suppRepo = new Mock<ISupplementRepository>();
    _nutrientRepo = new Mock<ISupplementNutrientRepository>();
    _nutrientService = new Mock<ISupplementNutrientService>();
    _llmService = new Mock<ILlmService>();
    _csvImportService = new Mock<ICsvImportService>();
    _controller = new SupplementController(
        _suppRepo.Object,
        _nutrientRepo.Object,
        _nutrientService.Object,
        _llmService.Object,
        _csvImportService.Object);
}
```

- [ ] **Step 2: Run all tests**

Run: `dotnet test VitaTrack.Tests`
Expected: All tests PASS

- [ ] **Step 3: Commit**

```bash
git add VitaTrack.Tests/SupplementControllerTests.cs
git commit -m "fix: update SupplementControllerTests for new constructor"
```

---

### Task 9: E2E Test — CSV Import

**Files:**
- Create: `e2e-tests/playwright/tests/csv-import.spec.js`
- Create: `e2e-tests/playwright/test-data/sample-import.csv`

**Interfaces:**
- Consumes: Import CSV modal UI (from Task 7)
- Consumes: `_ImportReport` partial (from Task 6)

- [ ] **Step 1: Create test CSV file**

```csv
Name,Brand,DailyDose,ManufacturerUrl,Cost
TestCSV Product Alpha,TestBrand Alpha,1 tablet,,
TestCSV Product Beta,TestBrand Beta,2 capsules,,
TestCSV Product Gamma,TestBrand Gamma,1 tablet,,
```

Save to: `e2e-tests/playwright/test-data/sample-import.csv`

- [ ] **Step 2: Create csv-import.spec.js**

```javascript
const { test, expect } = require('@playwright/test');
const path = require('path');

test.describe('CSV Import', () => {

  test('should open import modal and download sample CSV', async ({ page }) => {
    await page.goto('/Supplement');
    await page.click('button:has-text("Import CSV")');
    await expect(page.locator('#importCsvModal')).toBeVisible();
    await expect(page.locator('#importCsvModal')).toContainText('Import Supplements from CSV');
    await expect(page.locator('a:has-text("Download sample CSV")')).toBeVisible();
  });

  test('should import a valid CSV and show report', async ({ page }) => {
    await page.goto('/Supplement');
    await page.click('button:has-text("Import CSV")');
    await expect(page.locator('#importCsvModal')).toBeVisible();

    const csvPath = path.join(__dirname, '..', 'test-data', 'sample-import.csv');
    await page.setInputFiles('input[type="file"]', csvPath);
    await page.click('button:has-text("Upload & Import")');

    // Wait for report to appear
    await expect(page.locator('#import-report-container .card')).toBeVisible({ timeout: 60000 });
    await expect(page.locator('#import-report-container')).toContainText('Imported');
    await expect(page.locator('#import-report-container')).toContainText('TestCSV Product Alpha');
    await expect(page.locator('#import-report-container')).toContainText('TestCSV Product Beta');
    await expect(page.locator('#import-report-container')).toContainText('TestCSV Product Gamma');
  });

  test('should show error for empty file input', async ({ page }) => {
    await page.goto('/Supplement');
    await page.click('button:has-text("Import CSV")');
    await expect(page.locator('#importCsvModal')).toBeVisible();

    // Try to submit without selecting a file — browser validation should prevent it
    await expect(page.locator('input[type="file"]')).toHaveAttribute('required', '');
  });

});
```

- [ ] **Step 3: Run E2E tests**

Run: `cd e2e-tests/playwright && npx playwright test csv-import --project=chromium`
Expected: All tests PASS

- [ ] **Step 4: Commit**

```bash
git add e2e-tests/playwright/tests/csv-import.spec.js e2e-tests/playwright/test-data/sample-import.csv
git commit -m "test: add CSV import E2E tests"
```

---

### Task 10: Final Verification

- [ ] **Step 1: Run full test suite**

Run: `dotnet test VitaTrack.sln`
Expected: All tests PASS

- [ ] **Step 2: Run format check**

Run: `./format-check.sh`
Expected: No formatting issues

- [ ] **Step 3: Run E2E tests**

Run: `cd e2e-tests/playwright && npx playwright test`
Expected: All tests PASS

- [ ] **Step 4: Manual smoke test**

1. `dotnet run --project VitaTrack.Web`
2. Navigate to Supplements page
3. Click "Import CSV" — modal opens
4. Download sample CSV — file downloads
5. Upload the sample CSV — report shows 3 imported, 0 failed
6. Verify supplements appear in the list
7. Close modal

- [ ] **Step 5: Final commit if needed**

Any cleanup commits as needed.
