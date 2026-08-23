using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Models;
using VitaTrack.Web.Models;

namespace VitaTrack.Web.Controllers;

public partial class SupplementController
{
    private async Task<SupplementEditorViewModel> BuildEditorViewModelAsync(
        int supplementId,
        string supplementName,
        IEnumerable<SupplementNutrient> savedNutrients,
        string? extractionError,
        string? swapSuggestion = null) =>
        new()
        {
            SupplementId = supplementId,
            SupplementName = supplementName,
            Nutrients = await ToDtosAsync(savedNutrients.Where(s => !s.ParentNutrientId.HasValue)),
            SwapSuggestion = swapSuggestion,
            ExtractionError = extractionError
        };

    private static IEnumerable<SupplementNutrientDto> SafeNutrients(IEnumerable<SupplementNutrientDto>? nutrients)
        => (nutrients ?? []).Where(n => !string.IsNullOrWhiteSpace(n.GenericName));

    private async Task<List<SupplementNutrientDto>> ToDtosAsync(IEnumerable<SupplementNutrient> nutrients)
    {
        var dtos = new List<SupplementNutrientDto>();
        foreach (var sn in nutrients)
        {
            var dto = new SupplementNutrientDto { GenericName = sn.GenericName, SpecificForm = sn.SpecificForm, Dosage = sn.Dosage };
            var children = await _nutrientRepo.GetByParentIdAsync(sn.Id) ?? [];
            dto.Children = await ToDtosAsync(children);
            dtos.Add(dto);
        }

        return dtos;
    }

    private static string? BuildExtractionError(string? llmError, IReadOnlyList<NutrientFailure> failures)
    {
        if (string.IsNullOrWhiteSpace(llmError) && failures.Count == 0) return null;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(llmError)) parts.Add(llmError);
        if (failures.Count > 0)
            parts.Add($"{failures.Count} nutrient(s) failed to save: " + string.Join(", ", failures.Select(f => f.GenericName)));
        return string.Join(" | ", parts);
    }
}
