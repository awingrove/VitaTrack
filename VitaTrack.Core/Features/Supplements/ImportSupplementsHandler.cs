using VitaTrack.Core.Features.Nutrients;
using VitaTrack.Core.Models;
using VitaTrack.Core.Services;

namespace VitaTrack.Core.Features.Supplements;

/// <summary>
/// Imports parsed CSV supplement rows: enriches via LLM when a manufacturer
/// URL is present, persists the supplement and its nutrients, and builds the
/// import report.
/// </summary>
public class ImportSupplementsHandler(
    ISupplementRepository supplementRepo,
    ISupplementNutrientService nutrientService,
    ILlmService llmService)
{
    private readonly ISupplementRepository _supplementRepo = supplementRepo;
    private readonly ISupplementNutrientService _nutrientService = nutrientService;
    private readonly ILlmService _llmService = llmService;

    public async Task<CsvImportReport> ImportAsync(CsvParseResult parseResult)
    {
        var successes = new List<CsvImportSuccess>();
        var failures = new List<CsvImportFailure>();

        foreach (var row in parseResult.Rows)
        {
            var supplement = new Supplement
            {
                Name = row.Name,
                Brand = row.Brand,
                DailyDose = row.DailyDose,
                ManufacturerUrl = row.ManufacturerUrl,
                Cost = row.Cost,
                ServingsPerBottle = row.ServingsPerBottle
            };

            var nutrientCount = 0;
            if (!string.IsNullOrWhiteSpace(row.ManufacturerUrl))
            {
                var llmResult = await _llmService.EnrichSupplementAsync(supplement);
                ApplyEnrichment(supplement, llmResult);

                var newId = await _supplementRepo.AddAsync(supplement);
                if (llmResult.Nutrients.Count > 0)
                {
                    var persistResult = await _nutrientService.AddAsync(newId, llmResult.Nutrients);
                    nutrientCount = persistResult.Saved.Count;
                }
            }
            else
            {
                await _supplementRepo.AddAsync(supplement);
            }

            successes.Add(new CsvImportSuccess(row.Name, row.Brand, nutrientCount));
        }

        foreach (var error in parseResult.Errors)
        {
            failures.Add(new CsvImportFailure(error.RowNumber, "N/A", error.Message));
        }

        return new CsvImportReport(
            parseResult.Rows.Count + parseResult.Errors.Count, successes, failures);
    }

    private static void ApplyEnrichment(Supplement supplement, LlmResult llmResult)
    {
        supplement.NutritionJson = llmResult.NutritionJson;
        supplement.SwapSuggestion = llmResult.SwapSuggestion;
    }
}
