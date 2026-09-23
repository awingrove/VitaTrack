namespace VitaTrack.Core.Features.Dosing;

public interface IPrescribedDoseRepository
{
    Task<IReadOnlyList<PrescribedDose>> GetAllAsync();
    Task<IReadOnlyList<PrescribedDose>> GetByFamilyMemberIdAsync(int familyMemberId);
    Task<PrescribedDose?> GetByIdAsync(int id);
    Task<int> AddAsync(PrescribedDose prescribedDose);
    Task UpdateAsync(PrescribedDose prescribedDose);
    Task<int> DeleteAsync(int id);

    /// <summary>
    /// Deletes all prescribed doses belonging to the given supplements. The
    /// owning supplement slice calls this instead of issuing SQL against the
    /// PrescribedDoses table directly.
    /// </summary>
    Task DeleteBySupplementIdsAsync(IEnumerable<int> supplementIds);

    /// <summary>
    /// Deletes all prescribed doses belonging to the given family members. The
    /// owning family slice calls this instead of issuing SQL against the
    /// PrescribedDoses table directly.
    /// </summary>
    Task DeleteByFamilyMemberIdsAsync(IEnumerable<int> familyMemberIds);
}
