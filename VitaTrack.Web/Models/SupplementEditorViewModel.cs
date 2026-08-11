using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Web.Models;

public class SupplementEditorViewModel
{
    public int SupplementId { get; set; }
    public string SupplementName { get; set; } = string.Empty;
    public List<SupplementNutrientDto> Nutrients { get; set; } = new();
    public string? SwapSuggestion { get; set; }
    public string? ExtractionError { get; set; }
    public bool SaveSuccess { get; set; }
}
