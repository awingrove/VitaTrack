namespace VitaTrack.Infrastructure.Models;

public record NutrientFailure(string GenericName, string Error);

public record ReplaceNutrientsResult(
    IReadOnlyList<SupplementNutrient> Saved,
    IReadOnlyList<NutrientFailure> Failures);

public record SupplementCostRow(string Name, string Brand, decimal UnitCost, decimal MonthlyCost);
public record MemberCostRow(string Name, decimal MonthlyCost);

public record NutrientReportData(
    DateTime ReportDate,
    IReadOnlyDictionary<string, string> Units,
    decimal TotalCost,
    IReadOnlyList<string> MemberNames,
    IReadOnlyList<Dictionary<string, string>> MemberData,
    IReadOnlyList<Supplement> Supplements,
    IReadOnlyDictionary<int, decimal> SupplementMonthlyCosts);

public record CostReportData(
    DateTime ReportDate,
    IReadOnlyList<SupplementCostRow> SupplementCosts,
    IReadOnlyList<MemberCostRow> MemberCosts,
    decimal GrandTotal);