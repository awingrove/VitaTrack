namespace VitaTrack.Core.Features.Nutrients;

public record NutrientFailure(string GenericName, string Error);

public record ReplaceNutrientsResult(
    IReadOnlyList<SupplementNutrient> Saved,
    IReadOnlyList<NutrientFailure> Failures);
