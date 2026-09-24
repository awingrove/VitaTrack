using System.Collections.Generic;
using System.Threading.Tasks;

namespace VitaTrack.Core.Features.Nutrients;

public interface ISupplementNutrientRepository
{
    Task<IReadOnlyList<SupplementNutrient>> GetBySupplementIdAsync(int supplementId);
    Task<IReadOnlyList<SupplementNutrient>> GetByParentIdAsync(int parentId);
    Task<IDictionary<int, int>> GetCountsBySupplementIdsAsync(IEnumerable<int> supplementIds);
    Task<SupplementNutrient?> GetByIdAsync(int id);
    Task<int> AddAsync(SupplementNutrient nutrient);
    Task UpdateAsync(SupplementNutrient nutrient);
    Task<int> DeleteAsync(int id);
    Task<int> DeleteAsync(IEnumerable<int> ids);

    /// <summary>
    /// Deletes all nutrients (blend children before parents) belonging to the
    /// given supplements. The owning supplement slice calls this instead of
    /// issuing SQL against the SupplementNutrients table directly.
    /// </summary>
    Task DeleteBySupplementIdsAsync(IEnumerable<int> supplementIds);
}