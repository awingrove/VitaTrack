namespace VitaTrack.Core.Features.Reporting;

public record NutrientContributionsCell(string NutrientName, IReadOnlyList<NutrientContributionRow> Contributions);
