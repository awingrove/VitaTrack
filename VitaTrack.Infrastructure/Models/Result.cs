namespace VitaTrack.Infrastructure.Models;

public record Result<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public string Error { get; init; } = string.Empty;

    public static Result<T> Success(T value) => new() { IsSuccess = true, Value = value };
    public static Result<T> Failure(string error) => new() { IsSuccess = false, Error = error };
}

public record NutrientFailure(string GenericName, string Error);

public record ReplaceNutrientsResult(
    IReadOnlyList<SupplementNutrient> Saved,
    IReadOnlyList<NutrientFailure> Failures);

public record SupplementCostRow(string Name, string Brand, decimal UnitCost, decimal MonthlyCost);
public record MemberCostRow(string Name, decimal MonthlyCost);

public record NutrientReportData(
    DateTime ReportDate,
    IReadOnlyDictionary<string, decimal> GrandTotals,
    decimal TotalCost,
    IReadOnlyList<string> MemberNames,
    IReadOnlyList<Dictionary<string, string>> MemberData,
    IReadOnlyList<Supplement> Supplements);

public record CostReportData(
    DateTime ReportDate,
    IReadOnlyList<SupplementCostRow> SupplementCosts,
    IReadOnlyList<MemberCostRow> MemberCosts,
    decimal GrandTotal);