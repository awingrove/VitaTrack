using VitaTrack.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VitaTrack.Core.Data;

public interface ISupplementRepository
{
    Task<IReadOnlyList<Supplement>> GetAllAsync();
    Task<Supplement?> GetByIdAsync(int id);
    Task<int> AddAsync(Supplement supplement);
    Task UpdateAsync(Supplement supplement);
    Task<int> DeleteAsync(int id);
    Task<int> DeleteAsync(IEnumerable<int> ids);
}