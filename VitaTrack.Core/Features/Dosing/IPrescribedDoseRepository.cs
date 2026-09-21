namespace VitaTrack.Core.Features.Dosing;

public interface IPrescribedDoseRepository
{
    Task<IReadOnlyList<PrescribedDose>> GetAllAsync();
    Task<IReadOnlyList<PrescribedDose>> GetByFamilyMemberIdAsync(int familyMemberId);
    Task<PrescribedDose?> GetByIdAsync(int id);
    Task<int> AddAsync(PrescribedDose prescribedDose);
    Task UpdateAsync(PrescribedDose prescribedDose);
    Task<int> DeleteAsync(int id);
}
