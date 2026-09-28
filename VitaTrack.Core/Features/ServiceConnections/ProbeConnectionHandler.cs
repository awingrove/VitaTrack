namespace VitaTrack.Core.Features.ServiceConnections;

/// <summary>
/// Asks the active connection's service which models it offers and records what it
/// learned on the row. The write is the whole point: a verification flag that lives only
/// in a response is gone on the next page load, so the unverified state would be the
/// only one that ever survived.
/// <para>
/// The target is the <em>active</em> connection rather than a named id, and that is not
/// a shortcut. A connect saves and probes in one action, so the row just written is the
/// active one, and a second connect re-tests the same way. Reading by id would need a
/// repository seam that exists for no other caller.
/// </para>
/// <para>
/// A failed probe is a state, not a failure: the connection stays saved and usable and
/// comes back <see cref="ServiceConnection.Unverified"/>. A 404 is not a bad key —
/// several OpenAI-compatible endpoints do not implement model listing at all.
/// </para>
/// </summary>
public class ProbeConnectionHandler(
    IServiceConnectionRepository connections,
    IServiceCatalogClient catalog)
{
    /// <summary>Said when there is nothing to probe. The connect form cannot reach it —
    /// it has just written a row — but the model picker's save can: a page left open
    /// while the connection was removed in another tab posts a model to nothing. The
    /// web layer reports this same message rather than one of its own, so a single
    /// absence has a single wording.</summary>
    public const string NoConnection = "There is no saved connection to test. Connect a service first.";

    private const string StampFailed = "The connection was saved, but its test result could not be recorded.";

    private readonly IServiceConnectionRepository _connections = connections;
    private readonly IServiceCatalogClient _catalog = catalog;

    public async Task<ProbeConnectionResult> ProbeAsync(CancellationToken ct = default)
    {
        var connection = await _connections.GetActiveAsync(ct);
        if (connection is null)
            return ProbeConnectionResult.Failed(NoConnection);

        // The caller's token, deliberately. ServiceCatalogClient is the component that
        // decided a caller's cancellation is not a result about the connection; this
        // handler is what would persist that result, so it must let the same decision
        // reach it rather than converting the abort into a stamp. The client's own
        // timeout still arrives here as an unverified answer, which is right: a timeout
        // is something the service did.
        var catalog = await _catalog.ListModelsAsync(connection, ct);

        var stamped = connection with
        {
            Verification = catalog.Verified ? ServiceConnection.Verified : ServiceConnection.Unverified,
            // Cleared on the unverified path rather than left in place: the pair is read
            // together everywhere, and a stale VerifiedAt beside an unverified flag is a
            // contradiction the UI would have to interpret.
            VerifiedAt = catalog.Verified ? DateTimeOffset.UtcNow : null,
        };

        var id = await _connections.SaveAsync(stamped, ct);
        return id == IServiceConnectionRepository.NoRowWritten
            ? ProbeConnectionResult.Failed(StampFailed)
            : new ProbeConnectionResult(catalog, null);
    }
}
