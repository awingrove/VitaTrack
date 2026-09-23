namespace VitaTrack.Core.Features.Reporting;

public record MemberNutrientTotals(string MemberName, IReadOnlyList<NutrientTotalRow> Totals);
