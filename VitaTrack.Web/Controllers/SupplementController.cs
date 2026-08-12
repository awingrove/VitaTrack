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
    ILlmService llmService) : Controller
{
    private readonly ISupplementRepository _suppRepo = suppRepo;
    private readonly ISupplementNutrientRepository _nutrientRepo = nutrientRepo;
    private readonly ISupplementNutrientService _nutrientService = nutrientService;
    private readonly ILlmService _llmService = llmService;

    public async Task<IActionResult> Index()
    {
        var supplements = await _suppRepo.GetAllAsync();
        return View(supplements);
    }

    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enrich(Supplement supplement)
    {
        if (!ModelState.IsValid)
        {
            return PartialView("_ValidationErrors", ModelState);
        }

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
    public async Task<IActionResult> UpdateNutrients(int supplementId, List<SupplementNutrientDto> nutrients)
    {
        var supplement = await _suppRepo.GetByIdAsync(supplementId);
        if (supplement == null) return NotFound();

        var replaceResult = await _nutrientService.ReplaceAsync(supplementId, SafeNutrients(nutrients));

        var viewModel = new SupplementEditorViewModel
        {
            SupplementId = supplementId,
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
        return View(supplement);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Supplement supplement)
    {
        if (id != supplement.Id) return NotFound();
        if (!ModelState.IsValid) return View(supplement);

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
    public async Task<IActionResult> ConfirmEdit(int id, Supplement supplement, List<SupplementNutrientDto>? nutrients)
    {
        if (id != supplement.Id) return NotFound();
        if (!ModelState.IsValid) return View("Review", supplement);

        await _suppRepo.UpdateAsync(supplement);
        await _nutrientService.ReplaceAsync(id, SafeNutrients(nutrients));

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmCreate(Supplement supplement, List<SupplementNutrientDto>? nutrients)
    {
        if (!ModelState.IsValid) return View("Review", supplement);

        var newId = await _suppRepo.AddAsync(supplement);
        await _nutrientService.AddAsync(newId, SafeNutrients(nutrients));

        return RedirectToAction(nameof(Index));
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