using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Web.Controllers
{
    public class ReportingController : Controller
    {
        private readonly ISupplementRepository _supplementRepo;
        private readonly IPrescribedDoseRepository _prescribedDoseRepo;

        public ReportingController(ISupplementRepository supplementRepo, IPrescribedDoseRepository prescribedDoseRepo)
        {
            _supplementRepo = supplementRepo;
            _prescribedDoseRepo = prescribedDoseRepo;
        }

        // GET: /Reporting/NutrientReport
        public async Task<IActionResult> NutrientReport()
        {
            // Get all active prescribed doses (where today is within the date range)
            var prescribedDoses = await _prescribedDoseRepo.GetAllAsync();
            var today = System.DateTime.Today;
            var activeDoses = prescribedDoses.Where(pd =>
                pd.StartDate <= today &&
                (!pd.EndDate.HasValue || pd.EndDate >= today)).ToList();

            // Cache for supplements to avoid repeated DB calls
            var supplementCache = new Dictionary<int, Supplement>();
            var nutrientTotals = new System.Collections.Generic.Dictionary<string, decimal>();

            foreach (var pd in activeDoses)
            {
                // Get the supplement for this prescribed dose
                if (!supplementCache.TryGetValue(pd.SupplementId, out var supplement))
                {
                    supplement = await _supplementRepo.GetByIdAsync(pd.SupplementId);
                    supplementCache[pd.SupplementId] = supplement;
                }

                if (supplement == null) continue;

                // Parse the dosage amount from the Dosage string (e.g., "500 mg" -> 500)
                decimal dosageAmount = 0;
                if (!string.IsNullOrWhiteSpace(pd.Dosage))
                {
                    // Extract the first number from the string
                    var match = System.Text.RegularExpressions.Regex.Match(pd.Dosage, @"[\d]+\.?\d*");
                    if (decimal.TryParse(match.Value, out var amount))
                    {
                        dosageAmount = amount;
                    }
                }

                // Get the nutrition per unit from the supplement
                if (!string.IsNullOrEmpty(supplement.NutritionJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(supplement.NutritionJson);
                        if (doc.RootElement.TryGetProperty("nutrition", out var nutritionElement))
                        {
                            foreach (var nutrient in nutritionElement.EnumerateObject())
                            {
                                if (nutrient.Value.ValueKind == JsonValueKind.Number)
                                {
                                    var amountPerUnit = nutrient.Value.GetDecimal();
                                    // Calculate the daily amount for this nutrient from this prescribed dose
                                    var dailyAmount = amountPerUnit * dosageAmount * pd.FrequencyPerDay;
                                    if (nutrientTotals.ContainsKey(nutrient.Name))
                                    {
                                        nutrientTotals[nutrient.Name] += dailyAmount;
                                    }
                                    else
                                    {
                                        nutrientTotals[nutrient.Name] = dailyAmount;
                                    }
                                }
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Ignore malformed JSON
                    }
                }
            }

            // Get the list of unique supplements that contributed to the report
            var supplementIds = activeDoses.Select(pd => pd.SupplementId).Distinct();
            var supplements = new List<Supplement>();
            foreach (var id in supplementIds)
            {
                if (supplementCache.TryGetValue(id, out var supp) && supp != null)
                {
                    supplements.Add(supp);
                }
            }

            ViewData["NutrientTotals"] = nutrientTotals;
            return View(supplements);
        }
    }
}
