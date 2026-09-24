using System.Collections.Generic;
using System.Threading.Tasks;

namespace VitaTrack.Core.Features.Family;

public interface IFamilyRepository
{
    Task<IReadOnlyList<FamilyMember>> GetAllAsync();
    Task<FamilyMember?> GetByIdAsync(int id);
    Task<int> AddAsync(FamilyMember member);
    Task UpdateAsync(FamilyMember member);
    Task<int> DeleteAsync(int id);
}