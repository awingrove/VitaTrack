using VitaTrack.Core.Models;
using VitaTrack.Core.Primitives;

namespace VitaTrack.Core.Features.Reporting;

public record NutrientReportData(
    DateTime ReportDate,
    IReadOnlyDictionary<string, string> Units,
    Money TotalCost,
    IReadOnlyList<string> MemberNames,
    IReadOnlyList<Dictionary<string, string>> MemberData,
    IReadOnlyList<Dictionary<string, List<NutrientContributionRow>>> MemberContributions,
    IReadOnlyList<Supplement> Supplements,
    IReadOnlyDictionary<int, Money> SupplementMonthlyCosts);
