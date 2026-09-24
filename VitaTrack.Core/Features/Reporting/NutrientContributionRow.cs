namespace VitaTrack.Core.Features.Reporting;

public record NutrientContributionRow(
    int SupplementId,
    string SupplementName,
    string Brand,
    decimal Amount,
    decimal? Multiplier,
    // Id of the SupplementNutrient row behind the amount; null when the amount
    // aggregates several distinct nutrient rows, which cannot link to one editor.
    int? SupplementNutrientId);
