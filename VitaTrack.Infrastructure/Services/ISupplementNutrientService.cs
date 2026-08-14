using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Infrastructure.Services;

public interface ISupplementNutrientService
{
    Task<ReplaceNutrientsResult> ReplaceAsync(int supplementId, IEnumerable<SupplementNutrientDto> nutrients);
    Task<ReplaceNutrientsResult> AddAsync(int supplementId, IEnumerable<SupplementNutrientDto> nutrients);
}