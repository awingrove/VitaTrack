using VitaTrack.Core.Features.Nutrients;

namespace VitaTrack.Core.Features.LlmEnrichment;

public class LlmResult
{
    public string NutritionJson { get; set; } = string.Empty;
    public string? SwapSuggestion { get; set; }
    public List<SupplementNutrientDto> Nutrients { get; set; } = [];
    public string? ExtractionError { get; set; }
}