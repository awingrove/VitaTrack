namespace VitaTrack.Core.Features.ServiceConnections;

/// <summary>
/// Result-shaped outcome of <see cref="SaveConnectionHandler.HandleAsync"/>: the
/// saved row's id, or the error that stopped the save. No exception for a rejected
/// request or an unwritten row — those are ordinary outcomes here.
/// </summary>
public record SaveConnectionResult(int? Id, string? Error)
{
    public bool Succeeded => Error is null;

    public static SaveConnectionResult Failed(string error) => new(null, error);
}
