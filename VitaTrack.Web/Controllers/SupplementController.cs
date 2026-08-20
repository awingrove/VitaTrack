using Microsoft.AspNetCore.Mvc;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Models;
using VitaTrack.Infrastructure.Services;
using VitaTrack.Web.Models;

namespace VitaTrack.Web.Controllers;

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

    public async Task<IActionResult> Index()
    {
        var supplements = (await _suppRepo.GetAllAsync()).ToList();
        var counts = await _nutrientRepo.GetCountsBySupplementIdsAsync(supplements.Select(s => s.Id));
        foreach (var supplement in supplements)
        {
            counts.TryGetValue(supplement.Id, out var count);
            supplement.NutrientCount = count;
        }
        return View(supplements);
    }

    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enrich(CreateSupplementRequest request)
    {
        if (!ModelState.IsValid)
        {
            return PartialView("_ValidationErrors", ModelState);
        }

        var supplement = request.ToSupplement();
        var llmResult = await _llmService.EnrichSupplementAsync(supplement);
        supplement.NutritionJson = llmResult.NutritionJson;
        supplement.SwapSuggestion = llmResult.SwapSuggestion;

        var newId = await _suppRepo.AddAsync(supplement);
        var persistResult = await _nutrientService.AddAsync(newId, SafeNutrients(llmResult.Nutrients));

        var viewModel = new SupplementEditorViewModel
        {
            SupplementId = newId,
            SupplementName = supplement.Name,
            Nutrients = ToDtos(persistResult.Saved),
            SwapSuggestion = llmResult.SwapSuggestion,
            ExtractionError = BuildExtractionError(llmResult.ExtractionError, persistResult.Failures)
        };

        return PartialView("_NutrientEditor", viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateNutrients(ReplaceNutrientsRequest request)
    {
        var supplement = await _suppRepo.GetByIdAsync(request.SupplementId);
        if (supplement == null) return NotFound();

        var replaceResult = await _nutrientService.ReplaceAsync(request.SupplementId, SafeNutrients(request.Nutrients));

        var viewModel = new SupplementEditorViewModel
        {
            SupplementId = request.SupplementId,
            SupplementName = supplement.Name,
            Nutrients = ToDtos(replaceResult.Saved),
            SaveSuccess = true,
            ExtractionError = replaceResult.Failures.Count > 0
                ? $"{replaceResult.Failures.Count} nutrient(s) failed to save: " +
                  string.Join(", ", replaceResult.Failures.Select(f => f.GenericName))
                : null
        };

        return PartialView("_NutrientEditor", viewModel);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var supplement = await _suppRepo.GetByIdAsync(id);
        if (supplement == null) return NotFound();
        var nutrients = await _nutrientRepo.GetBySupplementIdAsync(id);
        supplement.NutrientCount = nutrients.Count;
        return View(supplement);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EditSupplementRequest request)
    {
        if (id != request.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            var original = await _suppRepo.GetByIdAsync(id);
            if (original == null) return NotFound();
            return View(original);
        }

        var supplement = request.ToSupplement();
        var existingNutrients = await _nutrientRepo.GetBySupplementIdAsync(id);
        var llmResult = await _llmService.EnrichSupplementAsync(supplement);
        supplement.NutritionJson = llmResult.NutritionJson;
        supplement.SwapSuggestion = llmResult.SwapSuggestion;

        var mergedNutrients = ToDtos(existingNutrients);
        if (llmResult.Nutrients != null)
        {
            foreach (var llmNutrient in llmResult.Nutrients)
            {
                var existing = mergedNutrients.FirstOrDefault(n =>
                    n.GenericName.Equals(llmNutrient.GenericName, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.SpecificForm = llmNutrient.SpecificForm;
                    existing.Dosage = llmNutrient.Dosage;
                }
                else
                {
                    mergedNutrients.Add(llmNutrient);
                }
            }
        }

        ViewData["ExtractedNutrients"] = mergedNutrients;
        ViewData["ExtractionError"] = BuildExtractionError(llmResult.ExtractionError, []);
        return View("Review", supplement);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmEdit(int id, ConfirmSupplementRequest request)
    {
        if (id != request.Id) return NotFound();
        if (!ModelState.IsValid) return View("Review", request.ToSupplement());

        var supplement = request.ToSupplement();
        await _suppRepo.UpdateAsync(supplement);
        await _nutrientService.ReplaceAsync(id, SafeNutrients(request.Nutrients));

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmCreate(ConfirmSupplementRequest request)
    {
        if (!ModelState.IsValid) return View("Review", request.ToSupplement());

        var supplement = request.ToSupplement();
        var newId = await _suppRepo.AddAsync(supplement);
        await _nutrientService.AddAsync(newId, SafeNutrients(request.Nutrients));

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportCsv(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return PartialView("_ImportReport", new CsvImportReport(1, [],
                [new CsvImportFailure(0, "No file", "No file uploaded")]));

        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return PartialView("_ImportReport", new CsvImportReport(1, [],
                [new CsvImportFailure(0, "Invalid file", "File must be a .csv")]));

        CsvParseResult parseResult;
        await using var stream = file.OpenReadStream();
        parseResult = await _csvImportService.ParseAsync(stream);

        if (parseResult.Errors.Count > 0 && parseResult.Rows.Count == 0)
        {
            var failures = parseResult.Errors
                .Select(e => new CsvImportFailure(e.RowNumber, "N/A", e.Message)).ToList();
            return PartialView("_ImportReport", new CsvImportReport(parseResult.Errors.Count, [], failures));
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

        var report = new CsvImportReport(
            parseResult.Rows.Count + parseResult.Errors.Count, successes, failuresList);
        return PartialView("_ImportReport", report);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        await _suppRepo.DeleteAsync(id);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSelected(List<int> ids)
    {
        if (ids != null && ids.Count > 0)
        {
            await _suppRepo.DeleteAsync(ids);
        }
        return RedirectToAction(nameof(Index));
    }

    private static IEnumerable<SupplementNutrientDto> SafeNutrients(IEnumerable<SupplementNutrientDto>? nutrients)
        => (nutrients ?? []).Where(n => !string.IsNullOrWhiteSpace(n.GenericName));

    private static List<SupplementNutrientDto> ToDtos(IEnumerable<SupplementNutrient> nutrients)
        => nutrients.Select(sn => new SupplementNutrientDto
        {
            GenericName = sn.GenericName,
            SpecificForm = sn.SpecificForm,
            Dosage = sn.Dosage
        }).ToList();

    private static string? BuildExtractionError(string? llmError, IReadOnlyList<NutrientFailure> failures)
    {
        if (string.IsNullOrWhiteSpace(llmError) && failures.Count == 0) return null;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(llmError)) parts.Add(llmError);
        if (failures.Count > 0)
            parts.Add($"{failures.Count} nutrient(s) failed to save: " + string.Join(", ", failures.Select(f => f.GenericName)));
        return string.Join(" | ", parts);
    }
}