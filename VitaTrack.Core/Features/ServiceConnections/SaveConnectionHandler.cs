namespace VitaTrack.Core.Features.ServiceConnections;

/// <summary>
/// Saves the service connection: rejects a blank URL or key, keeps whatever the
/// form does not expose, and lets the repository enforce at-most-one-active. It
/// deliberately does not deactivate the previous connection — the repository does
/// that inside the same transaction as the write.
/// </summary>
public class SaveConnectionHandler(IServiceConnectionRepository connections)
{
    /// <summary>The one service that ships. The service registry and the selector
    /// that chooses among its entries replace this literal.</summary>
    public const string ServiceName = "opencode";

    /// <summary>SaveAsync's "no row was written" answer. Ids come from an AUTOINCREMENT
    /// column, so 0 is never a real id and must not be read as a successful save.</summary>
    private const int NoRowWritten = 0;

    private const string SaveFailed = "The connection could not be saved. Please try again.";

    private readonly IServiceConnectionRepository _connections = connections;

    public async Task<SaveConnectionResult> HandleAsync(ConnectServiceRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.BaseUrl))
            return SaveConnectionResult.Failed(ConnectServiceRequest.BaseUrlRequired);
        if (string.IsNullOrWhiteSpace(request.ApiKey))
            return SaveConnectionResult.Failed(ConnectServiceRequest.ApiKeyRequired);

        // Each save is a new row: the repository demotes the previous one in the same
        // transaction, so the connection in use is always the one just saved. Starting
        // from the row being replaced carries every field the form does not expose
        // forward instead of resetting it — Id is dropped, because this is an insert.
        var replaced = await _connections.GetActiveAsync(ct);
        var connection = (replaced ?? new ServiceConnection()) with
        {
            Id = 0,
            Service = ServiceName,
            BaseUrl = request.BaseUrl.Trim(),
            ApiKey = request.ApiKey.Trim(),
            Model = Trimmed(request.Model) ?? replaced?.Model,
            // Unverified and never-verified, so the pair can never disagree.
            Verification = ServiceConnection.Unverified,
            VerifiedAt = null,
            IsActive = true
            // CreatedAt/UpdatedAt stay default: the repository owns both stamps, so the
            // handler never has to know what "now" is.
        };

        var id = await _connections.SaveAsync(connection, ct);
        return id == NoRowWritten
            ? SaveConnectionResult.Failed(SaveFailed)
            : new SaveConnectionResult(id, null);
    }

    /// <summary>A free-text field the user may leave blank: blank keeps the saved
    /// model on a reconnect, and stores none on a first connect.</summary>
    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
