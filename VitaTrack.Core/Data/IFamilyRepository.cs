using VitaTrack.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VitaTrack.Core.Data;

public interface IFamilyRepository
{
    Task<IReadOnlyList<FamilyMember>> GetAllAsync();
    Task<FamilyMember?> GetByIdAsync(int id);
    Task<int> AddAsync(FamilyMember member);
    Task UpdateAsync(FamilyMember member);
    Task<int> DeleteAsync(int id);
}