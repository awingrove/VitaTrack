namespace VitaTrack.Core.Features.Dosing;

public record DoseCommandResult(int? Id, string? Error)
{
    public bool Succeeded => Error is null;
}
