namespace VitaTrack.Core.Features.ServiceConnections;

/// <summary>
/// What a catalog probe learned about a connection: the model ids the service
/// listed, and whether it answered with one. An empty catalog with
/// <see cref="Verified"/> false is the ordinary outcome for a provider that does
/// not implement <c>/v1/models</c> — a state the UI reports, never an error — and
/// it is also what a 2xx carrying an empty list answers, because there is no
/// catalog to offer either way.
/// </summary>
/// <param name="Models">The ids the service listed, in the order it listed them.</param>
/// <param name="Verified">True only when the service returned a catalog this app could
/// read <em>and</em> that named at least one model. The two fields therefore agree by
/// construction: <c>Verified</c> is never true beside an empty <c>Models</c>, because
/// <c>Models.Count</c> is the signal the model picker branches on and a verified badge
/// above a free-text field with nothing to put in it would read as a contradiction.</param>
public sealed record ModelCatalog(IReadOnlyList<string> Models, bool Verified)
{
    /// <summary>The one "the probe did not verify" answer. A shared empty list is safe
    /// to hand out: there is nothing in it to mutate, and one name for the state keeps
    /// callers from writing a second, subtly different unverified result.</summary>
    public static ModelCatalog Unverified { get; } = new(Array.Empty<string>(), false);
}
