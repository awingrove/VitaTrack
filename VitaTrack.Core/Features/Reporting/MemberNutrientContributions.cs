namespace VitaTrack.Core.Features.Reporting;

public record MemberNutrientContributions(string MemberName, IReadOnlyList<NutrientContributionsCell> ByNutrient);
