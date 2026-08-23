using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Infrastructure.Services;

public class ReportingService(
    ISupplementRepository supplementRepo,
    IPrescribedDoseRepository prescribedDoseRepo,
    IFamilyRepository familyRepo,
    ISupplementNutrientRepository nutrientRepo) : IReportingService
{
    private readonly ISupplementRepository _supplementRepo = supplementRepo;
    private readonly IPrescribedDoseRepository _prescribedDoseRepo = prescribedDoseRepo;
    private readonly IFamilyRepository _familyRepo = familyRepo;
    private readonly ISupplementNutrientRepository _nutrientRepo = nutrientRepo;

    public async Task<NutrientReportData> GetNutrientReportDataAsync()
    {
        var (activeDoses, today) = await GetActiveDosesAsync();
        var supplementCache = new Dictionary<int, Supplement?>();
        var familyCache = new Dictionary<int, FamilyMember?>();
        var nutrientCache = new Dictionary<int, List<SupplementNutrient>>();
        var memberTotals = new Dictionary<int, Dictionary<string, decimal>>();
        var grandTotals = new Dictionary<string, decimal>();
        var supplementMonthlyCosts = new Dictionary<int, decimal>();
        decimal totalCost = 0;

        foreach (var pd in activeDoses)
        {
            var supplement = await GetCachedAsync(supplementCache, pd.SupplementId, _supplementRepo.GetByIdAsync);
            if (supplement == null) continue;

            if (!nutrientCache.TryGetValue(pd.SupplementId, out _))
            {
                var list = await _nutrientRepo.GetBySupplementIdAsync(pd.SupplementId);
                nutrientCache[pd.SupplementId] = [.. list];
            }

            var dailyFrequency = GetDailyFrequency(pd);
            memberTotals.TryAdd(pd.FamilyMemberId, []);

            foreach (var n in nutrientCache[pd.SupplementId])
            {
                var nutrientValue = DosageParser.ParseAmount(n.Dosage);
                var dailyAmount = nutrientValue * dailyFrequency;

                memberTotals[pd.FamilyMemberId][n.GenericName] =
                    memberTotals[pd.FamilyMemberId].GetValueOrDefault(n.GenericName) + dailyAmount;
                grandTotals[n.GenericName] = grandTotals.GetValueOrDefault(n.GenericName) + dailyAmount;
            }

            var monthlyCost = GetMonthlyCost(supplement, dailyFrequency);
            if (monthlyCost > 0m)
            {
                totalCost += monthlyCost;
                supplementMonthlyCosts[pd.SupplementId] =
                    supplementMonthlyCosts.GetValueOrDefault(pd.SupplementId) + monthlyCost;
            }
        }

        var memberNames = new List<string>();
        var memberData = new List<Dictionary<string, string>>();
        foreach (var kvp in memberTotals)
        {
            var member = await GetCachedAsync(familyCache, kvp.Key, _familyRepo.GetByIdAsync);
            memberNames.Add(member?.DisplayName ?? $"Member #{kvp.Key}");
            memberData.Add(kvp.Value.ToDictionary(n => n.Key, n => n.Value.ToString("F2")));
        }

        var supplements = new List<Supplement>();
        foreach (var id in activeDoses.Select(pd => pd.SupplementId).Distinct())
        {
            if (supplementCache.TryGetValue(id, out var supp) && supp != null)
                supplements.Add(supp);
        }

        return new NutrientReportData(
            ReportDate: today,
            GrandTotals: grandTotals,
            TotalCost: totalCost,
            MemberNames: memberNames,
            MemberData: memberData,
            Supplements: supplements,
            SupplementMonthlyCosts: supplementMonthlyCosts);
    }

    public async Task<CostReportData> GetCostReportDataAsync()
    {
        var (activeDoses, today) = await GetActiveDosesAsync();
        var supplementCache = new Dictionary<int, Supplement?>();
        var familyCache = new Dictionary<int, FamilyMember?>();

        var supplementCosts = new Dictionary<int, SupplementCostRow>();
        var memberCosts = new Dictionary<int, MemberCostRow>();
        decimal grandTotal = 0;

        foreach (var pd in activeDoses)
        {
            var supplement = await GetCachedAsync(supplementCache, pd.SupplementId, _supplementRepo.GetByIdAsync);
            if (supplement == null || !supplement.Cost.HasValue || supplement.ServingsPerBottle is not > 0) continue;

            var dailyFrequency = GetDailyFrequency(pd);
            var monthlyCost = GetMonthlyCost(supplement, dailyFrequency);

            supplementCosts[pd.SupplementId] = supplementCosts.TryGetValue(pd.SupplementId, out var existing)
                ? existing with { MonthlyCost = existing.MonthlyCost + monthlyCost }
                : new SupplementCostRow(supplement.Name, supplement.Brand, GetUnitCost(supplement), monthlyCost);

            var member = await GetCachedAsync(familyCache, pd.FamilyMemberId, _familyRepo.GetByIdAsync);
            var memberName = member?.DisplayName ?? $"Member #{pd.FamilyMemberId}";

            memberCosts[pd.FamilyMemberId] = memberCosts.TryGetValue(pd.FamilyMemberId, out var memberExisting)
                ? memberExisting with { MonthlyCost = memberExisting.MonthlyCost + monthlyCost }
                : new MemberCostRow(memberName, monthlyCost);

            grandTotal += monthlyCost;
        }

        return new CostReportData(
            ReportDate: today,
            SupplementCosts: supplementCosts.Values.ToList(),
            MemberCosts: memberCosts.Values.ToList(),
            GrandTotal: grandTotal);
    }

    private static decimal GetDailyFrequency(PrescribedDose pd) => pd.FrequencyPerDay > 0 ? pd.FrequencyPerDay : 1;

    private static bool HasServings(Supplement supplement) => supplement.ServingsPerBottle is > 0;

    private static decimal GetMonthlyCost(Supplement supplement, decimal dailyFrequency)
        => HasServings(supplement) && supplement.Cost.HasValue
            // `!` safe: HasServings proves ServingsPerBottle non-null; Cost guarded by HasValue
            ? supplement.Cost.Value / supplement.ServingsPerBottle!.Value * dailyFrequency * 30m
            : 0m;

    // Caller guarantees Cost.HasValue; HasServings guards the servings divisor.
    private static decimal GetUnitCost(Supplement supplement)
        => HasServings(supplement)
            ? supplement.Cost!.Value / supplement.ServingsPerBottle!.Value
            : supplement.Cost!.Value;

    private async Task<(List<PrescribedDose> ActiveDoses, DateTime Today)> GetActiveDosesAsync()
    {
        var allDoses = await _prescribedDoseRepo.GetAllAsync();
        var today = DateTime.Today;
        var activeDoses = allDoses.Where(pd =>
            (!pd.StartDate.HasValue || pd.StartDate <= today) &&
            (!pd.EndDate.HasValue || pd.EndDate >= today)).ToList();
        return (activeDoses, today);
    }

    private static async Task<T?> GetCachedAsync<T>(Dictionary<int, T?> cache, int id, Func<int, Task<T?>> fetch) where T : class
    {
        if (cache.TryGetValue(id, out var cached)) return cached;
        var value = await fetch(id);
        cache[id] = value;
        return value;
    }
}