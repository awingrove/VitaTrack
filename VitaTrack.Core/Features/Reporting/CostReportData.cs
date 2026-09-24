using VitaTrack.Core.Primitives;

namespace VitaTrack.Core.Features.Reporting;

public record CostReportData(
    DateTime ReportDate,
    IReadOnlyList<SupplementCostRow> SupplementCosts,
    IReadOnlyList<MemberCostRow> MemberCosts,
    Money GrandTotal);
