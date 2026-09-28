namespace VitaTrack.Core.Features.ServiceConnections;

/// <summary>
/// What a catalog probe learned about a connection: the model ids the service
/// listed, and whether it answered at all. An empty catalog with
/// <see cref="Verified"/> false is the ordinary outcome for a provider that does
/// not implement <c>/v1/models</c> — a state the UI reports, never an error.
/// </summary>
/// <param name="Models">The ids the service listed, in the order it listed them.</param>
/// <param name="Verified">True only when the service returned a catalog this app could read.</param>
public sealed record ModelCatalog(IReadOnlyList<string> Models, bool Verified)
{
    /// <summary>The one "the probe did not verify" answer. A shared empty list is safe
    /// to hand out: there is nothing in it to mutate, and one name for the state keeps
    /// callers from writing a second, subtly different unverified result.</summary>
    public static ModelCatalog Unverified { get; } = new(Array.Empty<string>(), false);
}
