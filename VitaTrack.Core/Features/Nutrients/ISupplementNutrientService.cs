using VitaTrack.Core.Models;

namespace VitaTrack.Core.Features.Nutrients;

public interface ISupplementNutrientService
{
    Task<ReplaceNutrientsResult> ReplaceAsync(int supplementId, IEnumerable<SupplementNutrientDto> nutrients);
    Task<ReplaceNutrientsResult> AddAsync(int supplementId, IEnumerable<SupplementNutrientDto> nutrients);
    Task<ReplaceNutrientsResult> PersistHierarchyAsync(int supplementId, IEnumerable<SupplementNutrientDto> roots);
}