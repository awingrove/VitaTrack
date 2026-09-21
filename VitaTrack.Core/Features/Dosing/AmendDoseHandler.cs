using VitaTrack.Core.Data;

namespace VitaTrack.Core.Features.Dosing;

public class AmendDoseHandler(IPrescribedDoseRepository doseRepo)
{
    public async Task<DoseCommandResult> HandleAsync(EditDoseRequest request)
    {
        var existing = await doseRepo.GetByIdAsync(request.Id);
        if (existing is null)
            return new DoseCommandResult(null, "Dose not found.");

        if (!DoseMultiplier.TryValidate(request.Multiplier, out var error))
            return new DoseCommandResult(null, error);

        existing.FamilyMemberId = request.FamilyMemberId;
        existing.SupplementId = request.SupplementId;
        existing.Multiplier = request.Multiplier;
        existing.Instructions = request.Instructions ?? string.Empty;
        existing.StartDate = request.StartDate;
        existing.EndDate = request.EndDate;

        await doseRepo.UpdateAsync(existing);
        return new DoseCommandResult(request.Id, null);
    }
}
