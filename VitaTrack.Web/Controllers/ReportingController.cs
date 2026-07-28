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
        private readonly IFamilyRepository _familyRepo;
        private readonly ISupplementNutrientRepository _nutrientRepo;

        public ReportingController(
            ISupplementRepository supplementRepo,
            IPrescribedDoseRepository prescribedDoseRepo,
            IFamilyRepository familyRepo,
            ISupplementNutrientRepository nutrientRepo)
        {
            _supplementRepo = supplementRepo;
            _prescribedDoseRepo = prescribedDoseRepo;
            _familyRepo = familyRepo;
            _nutrientRepo = nutrientRepo;
        }

        // GET: /Reporting/NutrientReport
        public async Task<IActionResult> NutrientReport()
        {
            var allDoses = await _prescribedDoseRepo.GetAllAsync();
            var today = System.DateTime.Today;
            var activeDoses = allDoses.Where(pd =>
                (!pd.StartDate.HasValue || pd.StartDate <= today) &&
                (!pd.EndDate.HasValue || pd.EndDate >= today)).ToList();

            var supplementCache = new Dictionary<int, Supplement>();
            var familyCache = new Dictionary<int, FamilyMember>();
            var nutrientCache = new Dictionary<int, List<SupplementNutrient>>();

            var memberTotals = new Dictionary<int, Dictionary<string, decimal>>();
            var grandTotals = new Dictionary<string, decimal>();
            decimal totalCost = 0;

            foreach (var pd in activeDoses)
            {
                if (!supplementCache.TryGetValue(pd.SupplementId, out var supplement))
                {
                    supplement = await _supplementRepo.GetByIdAsync(pd.SupplementId);
                    if (supplement != null)
                        supplementCache[pd.SupplementId] = supplement;
                }
                if (supplement == null) continue;

                if (!nutrientCache.TryGetValue(pd.SupplementId, out var nutrients))
                {
                    var list = await _nutrientRepo.GetBySupplementIdAsync(pd.SupplementId);
                    nutrientCache[pd.SupplementId] = list.ToList();
                }

                decimal dosageAmount = 0;
                if (!string.IsNullOrWhiteSpace(pd.Dosage))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(pd.Dosage, @"[\d]+\.?\d*");
                    decimal.TryParse(match.Value, out dosageAmount);
                }

                var dailyFrequency = pd.FrequencyPerDay > 0 ? pd.FrequencyPerDay : 1;
                if (!memberTotals.ContainsKey(pd.FamilyMemberId))
                    memberTotals[pd.FamilyMemberId] = new Dictionary<string, decimal>();

                foreach (var n in nutrientCache[pd.SupplementId])
                {
                    var nutrientValue = ParseDosageValue(n.Dosage);
                    var dailyAmount = nutrientValue * dosageAmount * pd.FrequencyPerDay;

                    if (memberTotals[pd.FamilyMemberId].ContainsKey(n.GenericName))
                    {
                        memberTotals[pd.FamilyMemberId][n.GenericName] += dailyAmount;
                    }
                    else
                    {
                        memberTotals[pd.FamilyMemberId][n.GenericName] = dailyAmount;
                    }

                    if (grandTotals.ContainsKey(n.GenericName))
                        grandTotals[n.GenericName] += dailyAmount;
                    else
                        grandTotals[n.GenericName] = dailyAmount;
                }

                if (supplement.Cost.HasValue)
                {
                    totalCost += supplement.Cost.Value * dailyFrequency;
                }
            }

            // Build per-member data
            var memberData = new List<Dictionary<string, string>>();
            var memberNames = new List<string>();
            foreach (var kvp in memberTotals)
            {
                if (!familyCache.TryGetValue(kvp.Key, out var member))
                {
                    member = await _familyRepo.GetByIdAsync(kvp.Key);
                    if (member != null)
                        familyCache[kvp.Key] = member;
                }
                var name = member?.DisplayName ?? $"Member #{kvp.Key}";
                memberNames.Add(name);
                memberData.Add(kvp.Value.ToDictionary(n => n.Key, n => n.Value.ToString("F2")));
            }

            ViewData["GrandTotals"] = JsonSerializer.Serialize(grandTotals);
            ViewData["TotalCost"] = totalCost.ToString("F2");
            ViewData["ReportDate"] = today.ToString("yyyy-MM-dd");
            ViewData["MemberNames"] = JsonSerializer.Serialize(memberNames);
            ViewData["MemberData"] = JsonSerializer.Serialize(memberData);

            var supplementIds = activeDoses.Select(pd => pd.SupplementId).Distinct();
            var supplements = new List<Supplement>();
            foreach (var id in supplementIds)
            {
                if (supplementCache.TryGetValue(id, out var supp) && supp != null)
                    supplements.Add(supp);
            }
            return View(supplements);
        }

        // GET: /Reporting/CostReport
        public async Task<IActionResult> CostReport()
        {
            var allDoses = await _prescribedDoseRepo.GetAllAsync();
            var today = System.DateTime.Today;
            var activeDoses = allDoses.Where(pd =>
                (!pd.StartDate.HasValue || pd.StartDate <= today) &&
                (!pd.EndDate.HasValue || pd.EndDate >= today)).ToList();

            var supplementCache = new Dictionary<int, Supplement>();
            var familyCache = new Dictionary<int, FamilyMember>();

            var supplementCosts = new Dictionary<int, (string Name, string Brand, decimal UnitCost, decimal MonthlyCost)>();
            var memberCosts = new Dictionary<int, (string Name, decimal MonthlyCost)>();
            decimal grandTotal = 0;

            foreach (var pd in activeDoses)
            {
                if (!supplementCache.TryGetValue(pd.SupplementId, out var supplement))
                {
                    supplement = await _supplementRepo.GetByIdAsync(pd.SupplementId);
                    supplementCache[pd.SupplementId] = supplement!;
                }
                if (supplement == null || !supplement.Cost.HasValue) continue;

                var dailyFrequency = pd.FrequencyPerDay > 0 ? pd.FrequencyPerDay : 1;
                var monthlyCost = supplement.Cost.Value * dailyFrequency;

                if (supplementCosts.TryGetValue(pd.SupplementId, out var existing))
                {
                    supplementCosts[pd.SupplementId] = (existing.Name, existing.Brand, existing.UnitCost, existing.MonthlyCost + monthlyCost);
                }
                else
                {
                    supplementCosts[pd.SupplementId] = (supplement.Name, supplement.Brand, supplement.Cost.Value, monthlyCost);
                }

                if (!familyCache.TryGetValue(pd.FamilyMemberId, out var member))
                {
                    member = await _familyRepo.GetByIdAsync(pd.FamilyMemberId);
                    familyCache[pd.FamilyMemberId] = member!;
                }
                var memberName = member?.DisplayName ?? $"Member #{pd.FamilyMemberId}";

                if (memberCosts.TryGetValue(pd.FamilyMemberId, out var memberExisting))
                {
                    memberCosts[pd.FamilyMemberId] = (memberExisting.Name, memberExisting.MonthlyCost + monthlyCost);
                }
                else
                {
                    memberCosts[pd.FamilyMemberId] = (memberName, monthlyCost);
                }

                grandTotal += monthlyCost;
            }

            ViewData["SupplementCosts"] = supplementCosts.Values.Select(s => new { s.Name, s.Brand, s.UnitCost, s.MonthlyCost }).ToList();
            ViewData["MemberCosts"] = memberCosts.Values.Select(m => new { m.Name, m.MonthlyCost }).ToList();
            ViewData["GrandTotal"] = grandTotal.ToString("F2");
            ViewData["ReportDate"] = today.ToString("yyyy-MM-dd");

            return View();
        }

        private static decimal ParseDosageValue(string dosage)
        {
            if (string.IsNullOrWhiteSpace(dosage)) return 0;
            var match = System.Text.RegularExpressions.Regex.Match(dosage, @"[\d]+\.?\d*");
            if (decimal.TryParse(match.Value, out var val)) return val;
            return 0;
        }
    }
}