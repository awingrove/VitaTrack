using Microsoft.AspNetCore.Mvc;
using VitaTrack.Core.Data;
using VitaTrack.Core.Features.LlmEnrichment;
using VitaTrack.Web.Models;

using VitaTrack.Core.Features.Nutrients;
using VitaTrack.Core.Features.Supplements;

namespace VitaTrack.Web.Controllers;

public partial class SupplementController(
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
        var supplements = (await _suppRepo.GetAllAsync()).ToList();
        var counts = await _nutrientRepo.GetCountsBySupplementIdsAsync(supplements.Select(s => s.Id));
        foreach (var s in supplements) { counts.TryGetValue(s.Id, out var c); s.NutrientCount = c; }
        return View(supplements);
    }

    public IActionResult Create() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSave(CreateSupplementRequest request)
    {
        if (!ModelState.IsValid) return PartialView("_ValidationErrors", ModelState);

        var supplement = request.ToSupplement();
        await _suppRepo.AddAsync(supplement);
        Response.Headers["HX-Redirect"] = Url.Action("Index", "Supplement")!;
        return new EmptyResult();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Enrich(CreateSupplementRequest request)
    {
        if (!ModelState.IsValid) return PartialView("_ValidationErrors", ModelState);

        var supplement = request.ToSupplement();
        var llmResult = await _llmService.EnrichSupplementAsync(supplement);
        ApplyEnrichment(supplement, llmResult);

        var newId = await _suppRepo.AddAsync(supplement);
        var persistResult = await _nutrientService.PersistHierarchyAsync(newId, SafeNutrients(llmResult.Nutrients));

        var viewModel = await BuildEditorViewModelAsync(newId, supplement.Name, persistResult.Saved,
            BuildExtractionError(llmResult.ExtractionError, persistResult.Failures), llmResult.SwapSuggestion);

        return PartialView("_NutrientEditor", viewModel);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateNutrients(ReplaceNutrientsRequest request)
    {
        var supplement = await _suppRepo.GetByIdAsync(request.SupplementId);
        if (supplement == null) return NotFound();

        var replaceResult = await _nutrientService.ReplaceAsync(request.SupplementId, SafeNutrients(request.Nutrients));

        var viewModel = await BuildEditorViewModelAsync(request.SupplementId, supplement.Name, replaceResult.Saved,
            replaceResult.Failures.Count > 0
                ? $"{replaceResult.Failures.Count} nutrient(s) failed to save: " +
                  string.Join("; ", replaceResult.Failures.Select(f => $"{f.GenericName} ({f.Error})"))
                : null);
        viewModel.SaveSuccess = true;

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

    public async Task<IActionResult> EditNutrients(int id)
    {
        var supplement = await _suppRepo.GetByIdAsync(id);
        if (supplement == null) return NotFound();
        var nutrients = await _nutrientRepo.GetBySupplementIdAsync(id);
        var viewModel = await BuildEditorViewModelAsync(id, supplement.Name, nutrients, null);
        return View(viewModel);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EditSupplementRequest request)
    {
        if (id != request.Id) return NotFound();
        if (!ModelState.IsValid) return await ReturnEditViewOrNotFound(id);

        var supplement = request.ToSupplement();
        var existingNutrients = await _nutrientRepo.GetBySupplementIdAsync(id);
        var llmResult = await _llmService.EnrichSupplementAsync(supplement);
        ApplyEnrichment(supplement, llmResult);

        var mergedNutrients = await ToDtosAsync(existingNutrients);
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
                    existing.Children = llmNutrient.Children;
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

    private async Task<IActionResult> ReturnEditViewOrNotFound(int id)
    {
        var original = await _suppRepo.GetByIdAsync(id);
        if (original == null) return NotFound();
        return View("Edit", original);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditSave(int id, EditSupplementRequest request)
    {
        if (id != request.Id) return NotFound();
        if (!ModelState.IsValid) return await ReturnEditViewOrNotFound(id);

        var existing = await _suppRepo.GetByIdAsync(id);
        if (existing == null) return NotFound();

        var supplement = request.ToSupplement();
        supplement.NutritionJson = existing.NutritionJson;
        supplement.SwapSuggestion = existing.SwapSuggestion;
        await _suppRepo.UpdateAsync(supplement);
        return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmEdit(int id, ConfirmSupplementRequest request)
    {
        if (id != request.Id) return NotFound();
        if (!ModelState.IsValid) return View("Review", request.ToSupplement());
        var supplement = request.ToSupplement();
        await _suppRepo.UpdateAsync(supplement);
        await _nutrientService.PersistHierarchyAsync(id, SafeNutrients(request.Nutrients));

        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmCreate(ConfirmSupplementRequest request)
    {
        if (!ModelState.IsValid) return View("Review", request.ToSupplement());
        var supplement = request.ToSupplement();
        var newId = await _suppRepo.AddAsync(supplement);
        await _nutrientService.PersistHierarchyAsync(newId, SafeNutrients(request.Nutrients));

        return RedirectToAction(nameof(Index));
    }

    private static void ApplyEnrichment(Supplement supplement, LlmResult llmResult)
    {
        supplement.NutritionJson = llmResult.NutritionJson;
        supplement.SwapSuggestion = llmResult.SwapSuggestion;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        await _suppRepo.DeleteAsync(id);
        return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSelected(List<int> ids)
    {
        if (ids != null && ids.Count > 0) await _suppRepo.DeleteAsync(ids);
        return RedirectToAction(nameof(Index));
    }
}
