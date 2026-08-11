using Microsoft.AspNetCore.Mvc;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Models;
using VitaTrack.Infrastructure.Services;
using VitaTrack.Web.Models;

namespace VitaTrack.Web.Controllers;

public class SupplementController(
    ISupplementRepository suppRepo,
    ISupplementNutrientRepository nutrientRepo,
    ILlmService llmService,
    ILogger<SupplementController> logger) : Controller
{
    private readonly ISupplementRepository _suppRepo = suppRepo;
    private readonly ISupplementNutrientRepository _nutrientRepo = nutrientRepo;
    private readonly ILlmService _llmService = llmService;
    private readonly ILogger<SupplementController> _logger = logger;

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

        var llmResult = new LlmResult();
        try
        {
            llmResult = await _llmService.EnrichSupplementAsync(supplement);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM enrichment failed for supplement {Name}", supplement.Name);
            llmResult.ExtractionError = "Could not reach enrichment service. You can add nutrients manually.";
        }

        supplement.NutritionJson = llmResult.NutritionJson;
        supplement.SwapSuggestion = llmResult.SwapSuggestion;

        var newId = await _suppRepo.AddAsync(supplement);

        if (llmResult.Nutrients != null)
        {
            foreach (var n in llmResult.Nutrients.Where(n => !string.IsNullOrWhiteSpace(n.GenericName)))
            {
                try
                {
                    await _nutrientRepo.AddAsync(new SupplementNutrient
                    {
                        SupplementId = newId,
                        GenericName = n.GenericName,
                        SpecificForm = n.SpecificForm,
                        Dosage = n.Dosage
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to add nutrient {GenericName} for supplement {SupplementId}", n.GenericName, newId);
                }
            }
        }

        var viewModel = new SupplementEditorViewModel
        {
            SupplementId = newId,
            SupplementName = supplement.Name,
            Nutrients = llmResult.Nutrients ?? new List<SupplementNutrientDto>(),
            SwapSuggestion = llmResult.SwapSuggestion,
            ExtractionError = llmResult.ExtractionError
        };

        return PartialView("_NutrientEditor", viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateNutrients(int supplementId, List<SupplementNutrientDto> nutrients)
    {
        var supplement = await _suppRepo.GetByIdAsync(supplementId);
        if (supplement == null) return NotFound();

        var existingNutrients = await _nutrientRepo.GetBySupplementIdAsync(supplementId);
        foreach (var existing in existingNutrients)
        {
            await _nutrientRepo.DeleteAsync(existing.Id);
        }

        if (nutrients != null)
        {
            foreach (var n in nutrients.Where(n => !string.IsNullOrWhiteSpace(n.GenericName)))
            {
                try
                {
                    await _nutrientRepo.AddAsync(new SupplementNutrient
                    {
                        SupplementId = supplementId,
                        GenericName = n.GenericName,
                        SpecificForm = n.SpecificForm,
                        Dosage = n.Dosage
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to add nutrient {GenericName} for supplement {SupplementId}", n.GenericName, supplementId);
                }
            }
        }

        var savedNutrients = await _nutrientRepo.GetBySupplementIdAsync(supplementId);
        var viewModel = new SupplementEditorViewModel
        {
            SupplementId = supplementId,
            SupplementName = supplement.Name,
            Nutrients = savedNutrients.Select(sn => new SupplementNutrientDto
            {
                GenericName = sn.GenericName,
                SpecificForm = sn.SpecificForm,
                Dosage = sn.Dosage
            }).ToList(),
            SaveSuccess = true
        };

        return PartialView("_NutrientEditor", viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Supplement supplement)
    {
        if (ModelState.IsValid)
        {
            var llmResult = await _llmService.EnrichSupplementAsync(supplement);
            supplement.NutritionJson = llmResult.NutritionJson;
            supplement.SwapSuggestion = llmResult.SwapSuggestion;

            ViewData["ExtractedNutrients"] = llmResult.Nutrients;
            ViewData["ExtractionError"] = llmResult.ExtractionError;

            return View("Review", supplement);
        }
        return View(supplement);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmCreate(Supplement supplement, List<SupplementNutrientDto> nutrients)
    {
        if (!ModelState.IsValid)
        {
            ViewData["ExtractedNutrients"] = nutrients;
            return View("Review", supplement);
        }

        var newId = await _suppRepo.AddAsync(supplement);

        if (nutrients != null)
        {
            foreach (var n in nutrients.Where(n => !string.IsNullOrWhiteSpace(n.GenericName)))
            {
                try
                {
                    await _nutrientRepo.AddAsync(new SupplementNutrient
                    {
                        SupplementId = newId,
                        GenericName = n.GenericName,
                        SpecificForm = n.SpecificForm,
                        Dosage = n.Dosage
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to add nutrient {GenericName} for supplement {SupplementId}", n.GenericName, newId);
                }
            }
        }

        return RedirectToAction(nameof(Index));
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

        if (ModelState.IsValid)
        {
            var existingNutrients = await _nutrientRepo.GetBySupplementIdAsync(id);

            var llmResult = await _llmService.EnrichSupplementAsync(supplement);
            supplement.NutritionJson = llmResult.NutritionJson;
            supplement.SwapSuggestion = llmResult.SwapSuggestion;

            var mergedNutrients = new List<SupplementNutrientDto>();

            foreach (var existing in existingNutrients)
            {
                mergedNutrients.Add(new SupplementNutrientDto
                {
                    GenericName = existing.GenericName,
                    SpecificForm = existing.SpecificForm,
                    Dosage = existing.Dosage
                });
            }

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
            ViewData["ExtractionError"] = llmResult.ExtractionError;

            return View("Review", supplement);
        }

        return View(supplement);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmEdit(int id, Supplement supplement, List<SupplementNutrientDto> nutrients)
    {
        if (id != supplement.Id) return NotFound();

        if (!ModelState.IsValid)
        {
            ViewData["ExtractedNutrients"] = nutrients;
            return View("Review", supplement);
        }

        await _suppRepo.UpdateAsync(supplement);

        var existingNutrients = await _nutrientRepo.GetBySupplementIdAsync(id);
        foreach (var existing in existingNutrients)
        {
            await _nutrientRepo.DeleteAsync(existing.Id);
        }

        if (nutrients != null)
        {
            foreach (var n in nutrients.Where(n => !string.IsNullOrWhiteSpace(n.GenericName)))
            {
                try
                {
                    await _nutrientRepo.AddAsync(new SupplementNutrient
                    {
                        SupplementId = id,
                        GenericName = n.GenericName,
                        SpecificForm = n.SpecificForm,
                        Dosage = n.Dosage
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to add nutrient {GenericName} for supplement {SupplementId}", n.GenericName, id);
                }
            }
        }

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
}