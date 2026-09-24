using Microsoft.AspNetCore.Mvc;
using VitaTrack.Core.Features.Supplements;

namespace VitaTrack.Web.Controllers;

/// <summary>
/// CSV import surface for supplements. Entry point: Supplements index -> Import
/// CSV modal -> POST /SupplementImport/ImportCsv.
/// </summary>
public class SupplementImportController(
    ICsvImportService csvImportService,
    ImportSupplementsHandler importHandler) : Controller
{
    private readonly ICsvImportService _csvImportService = csvImportService;
    private readonly ImportSupplementsHandler _importHandler = importHandler;

    // The report partial lives with the Supplement views (shards.yaml MS views);
    // PartialView's controller-folder lookup would miss it from this controller.
    private const string ImportReportView = "~/Views/Supplement/_ImportReport.cshtml";

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportCsv(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return PartialView(ImportReportView, new CsvImportReport(1, [],
                [new CsvImportFailure(0, "No file", "No file uploaded")]));

        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return PartialView(ImportReportView, new CsvImportReport(1, [],
                [new CsvImportFailure(0, "Invalid file", "File must be a .csv")]));

        CsvParseResult parseResult;
        await using var stream = file.OpenReadStream();
        parseResult = await _csvImportService.ParseAsync(stream);

        if (parseResult.Errors.Count > 0 && parseResult.Rows.Count == 0)
        {
            var failures = parseResult.Errors
                .Select(e => new CsvImportFailure(e.RowNumber, "N/A", e.Message)).ToList();
            return PartialView(ImportReportView, new CsvImportReport(parseResult.Errors.Count, [], failures));
        }

        var report = await _importHandler.ImportAsync(parseResult);
        return PartialView(ImportReportView, report);
    }
}
