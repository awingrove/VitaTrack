namespace VitaTrack.Infrastructure.Models
{
    public class LlmResult
    {
        public string NutritionJson { get; set; } = string.Empty;
        public string? SwapSuggestion { get; set; }
        public List<SupplementNutrientDto> Nutrients { get; set; } = new();
        public string? ExtractionError { get; set; }
    }

    public class SupplementNutrientDto
    {
        public string GenericName { get; set; } = string.Empty;
        public string SpecificForm { get; set; } = string.Empty;
        public string Dosage { get; set; } = string.Empty;
        public string? Unit { get; set; }
        public decimal? AmountPerServing { get; set; }
    }
}