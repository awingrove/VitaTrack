using System.Collections.Generic;
using System.Threading.Tasks;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Infrastructure.Data
{
    public interface ISupplementNutrientRepository
    {
        Task<IReadOnlyList<SupplementNutrient>> GetBySupplementIdAsync(int supplementId);
        Task<SupplementNutrient?> GetByIdAsync(int id);
        Task<int> AddAsync(SupplementNutrient nutrient);
        Task UpdateAsync(SupplementNutrient nutrient);
        Task<int> DeleteAsync(int id);
    }
}