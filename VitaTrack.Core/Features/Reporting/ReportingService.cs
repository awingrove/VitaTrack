using VitaTrack.Core.Data;
using VitaTrack.Core.Features.Dosing;
using VitaTrack.Core.Models;
using VitaTrack.Core.Primitives;

using VitaTrack.Core.Features.Nutrients;
using VitaTrack.Core.Features.Supplements;

namespace VitaTrack.Core.Features.Reporting;

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
        var memberContributions = new Dictionary<int, Dictionary<string, Dictionary<int, ContributionBucket>>>();
        var nutrientUnits = new Dictionary<string, HashSet<Unit>>();
        var supplementMonthlyCosts = new Dictionary<int, Money>();
        var visitor = new NutrientAggregationVisitor(nutrientUnits, memberTotals, memberContributions);
        Money totalCost = default;

        foreach (var pd in activeDoses)
        {
            var supplement = await GetCachedAsync(supplementCache, pd.SupplementId, _supplementRepo.GetByIdAsync);
            if (supplement == null) continue;

            if (!nutrientCache.TryGetValue(pd.SupplementId, out _))
            {
                var list = await _nutrientRepo.GetBySupplementIdAsync(pd.SupplementId);
                nutrientCache[pd.SupplementId] = [.. list];
            }

            var dailyFrequency = GetMultiplier(pd);
            visitor.Visit(pd.FamilyMemberId, pd.SupplementId, dailyFrequency, nutrientCache[pd.SupplementId]);

            var monthlyCost = GetMonthlyCost(supplement, dailyFrequency);
            if (monthlyCost.Amount > 0m)
            {
                totalCost += monthlyCost;
                supplementMonthlyCosts[pd.SupplementId] =
                    supplementMonthlyCosts.TryGetValue(pd.SupplementId, out var existing)
                        ? existing + monthlyCost
                        : monthlyCost;
            }
        }

        var memberTotalsRows = new List<MemberNutrientTotals>();
        var memberContributionRows = new List<MemberNutrientContributions>();
        foreach (var kvp in memberTotals)
        {
            var member = await GetCachedAsync(familyCache, kvp.Key, _familyRepo.GetByIdAsync);
            var memberName = member?.DisplayName ?? $"Member #{kvp.Key}";
            memberTotalsRows.Add(new MemberNutrientTotals(
                memberName,
                kvp.Value.Select(n => new NutrientTotalRow(n.Key, n.Value.ToString("0.##"))).ToList()));

            var cells = new List<NutrientContributionsCell>();
            if (memberContributions.TryGetValue(kvp.Key, out var byNutrient))
            {
                foreach (var (nutrient, bySupplement) in byNutrient)
                {
                    var rows = bySupplement
                        .Select(b =>
                        {
                            // Contributions are only recorded after the null-supplement guard,
                            // so the cache entry exists and is non-null here.
                            var supp = supplementCache[b.Key]!;
                            var bucket = b.Value;
                            var nutrientId = bucket.NutrientIds.Count == 1 ? (int?)bucket.NutrientIds.First() : null;
                            return new NutrientContributionRow(
                                b.Key, supp.Name, supp.Brand, bucket.Amount, bucket.Multiplier, nutrientId);
                        })
                        .OrderBy(r => r.SupplementName, StringComparer.Ordinal)
                        .ToList();
                    cells.Add(new NutrientContributionsCell(nutrient, rows));
                }
            }
            memberContributionRows.Add(new MemberNutrientContributions(memberName, cells));
        }

        var supplements = new List<Supplement>();
        foreach (var id in activeDoses.Select(pd => pd.SupplementId).Distinct())
        {
            if (supplementCache.TryGetValue(id, out var supp) && supp != null)
                supplements.Add(supp);
        }

        var reportUnits = nutrientUnits
            .Select(k => new NutrientUnitRow(k.Key, string.Join(", ", k.Value.OrderBy(u => u.Symbol))))
            .ToList();

        return new NutrientReportData(
            ReportDate: today,
            Units: reportUnits,
            TotalCost: totalCost,
            MemberTotals: memberTotalsRows,
            MemberContributions: memberContributionRows,
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
        Money grandTotal = default;

        foreach (var pd in activeDoses)
        {
            var supplement = await GetCachedAsync(supplementCache, pd.SupplementId, _supplementRepo.GetByIdAsync);
            if (supplement == null || !supplement.Cost.HasValue || supplement.ServingsPerBottle is not > 0) continue;

            var dailyFrequency = GetMultiplier(pd);
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

    private static decimal GetMultiplier(PrescribedDose pd) => pd.Multiplier > 0 ? pd.Multiplier : 1;

    private static bool HasServings(Supplement supplement) => supplement.ServingsPerBottle is > 0;

    private static Money GetMonthlyCost(Supplement supplement, decimal dailyFrequency)
        => HasServings(supplement) && supplement.Cost.HasValue
            // `!` safe: HasServings proves ServingsPerBottle non-null; Cost guarded by HasValue
            ? new Money(supplement.Cost.Value / supplement.ServingsPerBottle!.Value * dailyFrequency * 30m, supplement.Currency)
            : default;

    // Caller guarantees Cost.HasValue; HasServings guards the servings divisor.
    private static Money GetUnitCost(Supplement supplement)
        => HasServings(supplement)
            ? new Money(supplement.Cost!.Value / supplement.ServingsPerBottle!.Value, supplement.Currency)
            : new Money(supplement.Cost!.Value, supplement.Currency);

    private async Task<(List<PrescribedDose> ActiveDoses, DateTime Today)> GetActiveDosesAsync()
    {
        var allDoses = await _prescribedDoseRepo.GetAllAsync();
        var today = DateTime.Today;
        var activeDoses = allDoses.Where(pd => pd.IsActiveOn(today)).ToList();
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