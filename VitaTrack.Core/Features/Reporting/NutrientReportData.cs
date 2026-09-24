using VitaTrack.Core.Features.Supplements;
using VitaTrack.Core.Models;
using VitaTrack.Core.Primitives;

namespace VitaTrack.Core.Features.Reporting;

public record NutrientReportData(
    DateTime ReportDate,
    IReadOnlyList<NutrientUnitRow> Units,
    Money TotalCost,
    IReadOnlyList<MemberNutrientTotals> MemberTotals,
    IReadOnlyList<MemberNutrientContributions> MemberContributions,
    IReadOnlyList<Supplement> Supplements,
    IReadOnlyDictionary<int, Money> SupplementMonthlyCosts);
