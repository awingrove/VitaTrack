using System.Collections.Generic;
using System.Threading.Tasks;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Infrastructure.Data
{
    public interface IPrescribedDoseRepository
    {
        Task<IReadOnlyList<PrescribedDose>> GetAllAsync();
        Task<PrescribedDose?> GetByIdAsync(int id);
        Task<int> AddAsync(PrescribedDose prescribedDose);
        Task UpdateAsync(PrescribedDose prescribedDose);
        Task<int> DeleteAsync(int id);
    }
}