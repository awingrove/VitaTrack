namespace VitaTrack.Core.Features.ServiceConnections;

/// <summary>
/// Saves the service connection: rejects a blank URL or key, keeps whatever the
/// form does not expose, and lets the repository enforce at-most-one-active. It
/// deliberately does not deactivate the previous connection — the repository does
/// that inside the same transaction as the write.
/// </summary>
public class SaveConnectionHandler(IServiceConnectionRepository connections)
{
    /// <summary>The one service that ships. A reference to the registry's id rather
    /// than a literal of its own, so the two names cannot drift; the selector that
    /// chooses among several services replaces this line entirely.</summary>
    public const string ServiceName = ServiceDescriptorRegistry.OpenCodeServiceId;

    private const string SaveFailed = "The connection could not be saved. Please try again.";

    private readonly IServiceConnectionRepository _connections = connections;

    public async Task<SaveConnectionResult> HandleAsync(ConnectServiceRequest request, CancellationToken ct = default)
    {
        // Not reachable from today's POST: MVC's [Required] fires first on a blank or
        // whitespace-only field, so ModelState is invalid before this runs. It is
        // depth, not decoration — the handler is a public seam, and the HTMX partial
        // and any future seed or import path call it without a ModelState of their
        // own. Deleting these two checks would let a keyless connection be written.
        if (string.IsNullOrWhiteSpace(request.BaseUrl))
            return SaveConnectionResult.Failed(ConnectServiceRequest.BaseUrlRequired);
        if (string.IsNullOrWhiteSpace(request.ApiKey))
            return SaveConnectionResult.Failed(ConnectServiceRequest.ApiKeyRequired);

        // Each save is a new row: the repository demotes the previous one in the same
        // transaction, so the connection in use is always the one just saved. Starting
        // from the row being replaced carries forward what the form does not expose —
        // Variant, MaxTokens and Temperature — instead of resetting it, and every
        // other property is assigned below so nothing else sneaks through the clone.
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
            IsActive = true,
            // Cleared on both paths, not inherited. A `with` copies every property, so
            // leaving these out would clone the replaced row's stamps — and SaveAsync
            // only supplies now when they are default, so a stale pair would be stored
            // verbatim. Reset explicitly: every new row is stamped when it is written,
            // and GetAllAsync's recency order has something to order on.
            CreatedAt = default,
            UpdatedAt = default
        };

        var id = await _connections.SaveAsync(connection, ct);
        return id == IServiceConnectionRepository.NoRowWritten
            ? SaveConnectionResult.Failed(SaveFailed)
            : new SaveConnectionResult(id, null);
    }

    /// <summary>A free-text field the user may leave blank: blank keeps the saved
    /// model on a reconnect, and stores none on a first connect.</summary>
    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
