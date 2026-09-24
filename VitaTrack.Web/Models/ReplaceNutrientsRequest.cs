using VitaTrack.Core.Features.Nutrients;

namespace VitaTrack.Web.Models;

public class ReplaceNutrientsRequest
{
    public int SupplementId { get; set; }

    public List<SupplementNutrientDto>? Nutrients { get; set; }
}