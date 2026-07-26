using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Models;
using VitaTrack.Infrastructure.Services;

namespace VitaTrack.Web.Controllers
{
    public class SupplementController : Controller
    {
        private readonly ISupplementRepository _suppRepo;
        private readonly ISupplementNutrientRepository _nutrientRepo;
        private readonly ILlmService _llmService;

        public SupplementController(
            ISupplementRepository suppRepo,
            ISupplementNutrientRepository nutrientRepo,
            ILlmService llmService)
        {
            _suppRepo = suppRepo;
            _nutrientRepo = nutrientRepo;
            _llmService = llmService;
        }

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
        public async Task<IActionResult> ConfirmCreate(Supplement supplement)
        {
            if (!ModelState.IsValid)
                return View("Review", supplement);

            var newId = await _suppRepo.AddAsync(supplement);

            var nutrientsJson = Request.Form["nutrientsJson"].ToString();
            if (!string.IsNullOrWhiteSpace(nutrientsJson) && nutrientsJson != "[]")
            {
                var nutrients = JsonSerializer.Deserialize<List<SupplementNutrientDto>>(
                    nutrientsJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

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
                        catch { }
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
        public async Task<IActionResult> ConfirmEdit(int id, Supplement supplement)
        {
            if (id != supplement.Id) return NotFound();

            if (!ModelState.IsValid)
                return View("Review", supplement);

            await _suppRepo.UpdateAsync(supplement);

            // Replace all existing nutrients
            var existingNutrients = await _nutrientRepo.GetBySupplementIdAsync(id);
            foreach (var existing in existingNutrients)
            {
                await _nutrientRepo.DeleteAsync(existing.Id);
            }

            var nutrientsJson = Request.Form["nutrientsJson"].ToString();
            if (!string.IsNullOrWhiteSpace(nutrientsJson) && nutrientsJson != "[]")
            {
                var nutrients = JsonSerializer.Deserialize<List<SupplementNutrientDto>>(
                    nutrientsJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

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
                        catch { /* skip failed saves */ }
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
    }
}