namespace VitaTrack.Infrastructure.Models;

public record NutrientFailure(string GenericName, string Error);

public record ReplaceNutrientsResult(
    IReadOnlyList<SupplementNutrient> Saved,
    IReadOnlyList<NutrientFailure> Failures);

public record MemberCostRow(string Name, decimal MonthlyCost);
public record SupplementCostRow(string Name, string Brand, decimal UnitCost, decimal MonthlyCost);
public record NutrientContributionRow(
    int SupplementId,
    string SupplementName,
    string Brand,
    decimal Amount,
    decimal? Multiplier,
    // Id of the SupplementNutrient row behind the amount; null when the amount
    // aggregates several distinct nutrient rows, which cannot link to one editor.
    int? SupplementNutrientId);

public record NutrientReportData(
    DateTime ReportDate,
    IReadOnlyDictionary<string, string> Units,
    decimal TotalCost,
    IReadOnlyList<string> MemberNames,
    IReadOnlyList<Dictionary<string, string>> MemberData,
    IReadOnlyList<Dictionary<string, List<NutrientContributionRow>>> MemberContributions,
    IReadOnlyList<Supplement> Supplements,
    IReadOnlyDictionary<int, decimal> SupplementMonthlyCosts);

public record CostReportData(
    DateTime ReportDate,
    IReadOnlyList<SupplementCostRow> SupplementCosts,
    IReadOnlyList<MemberCostRow> MemberCosts,
    decimal GrandTotal);