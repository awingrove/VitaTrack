using VitaTrack.Core.Data;

namespace VitaTrack.Core.Features.Dosing;

public class PrescribeDoseHandler(IPrescribedDoseRepository doseRepo)
{
    public async Task<DoseCommandResult> HandleAsync(CreateDoseRequest request)
    {
        if (!DoseMultiplier.TryValidate(request.Multiplier, out var error))
            return new DoseCommandResult(null, error);

        var dose = new PrescribedDose
        {
            FamilyMemberId = request.FamilyMemberId,
            SupplementId = request.SupplementId,
            Multiplier = request.Multiplier,
            Instructions = request.Instructions ?? string.Empty,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
        };

        var id = await doseRepo.AddAsync(dose);
        return new DoseCommandResult(id, null);
    }
}
