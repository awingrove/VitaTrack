namespace VitaTrack.Core.Features.Nutrients;

// Nutrient-editing contract shared across slices: parsed by LLM enrichment,
// persisted by the Nutrients service, bound by the Web editor flows.
public class SupplementNutrientDto
{
    public string GenericName { get; set; } = string.Empty;
    public string SpecificForm { get; set; } = string.Empty;
    public string Dosage { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public decimal? AmountPerServing { get; set; }
    public int? ParentNutrientId { get; set; }
    public List<SupplementNutrientDto>? Children { get; set; }
}
