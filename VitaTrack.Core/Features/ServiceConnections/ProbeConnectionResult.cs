namespace VitaTrack.Core.Features.ServiceConnections;

/// <summary>
/// Result-shaped outcome of <see cref="ProbeConnectionHandler.ProbeAsync"/>: the catalog
/// the service answered with, and the error that stopped the stamp. <para>
/// <see cref="Succeeded"/> means the verification state was <em>written</em>, not that
/// the service answered — an unverified answer is a state the UI reports, so the
/// catalog comes back alongside the success and the caller reads
/// <see cref="ModelCatalog.Verified"/> to tell the two apart. Collapsing them into one
/// flag would make "the service does not list models" indistinguishable from "we could
/// not record what it told us", and the first is ordinary while the second is a bug.
/// </para>
/// </summary>
public record ProbeConnectionResult(ModelCatalog Catalog, string? Error)
{
    public bool Succeeded => Error is null;

    public static ProbeConnectionResult Failed(string error) => new(ModelCatalog.Unverified, error);
}
