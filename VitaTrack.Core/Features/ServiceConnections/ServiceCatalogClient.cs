using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VitaTrack.Core.Features.ServiceConnections;

/// <summary>
/// Asks a service which models it offers, by reading its OpenAI-compatible
/// <c>GET {BaseUrl}/v1/models</c>. The catalog is how the model picker gets
/// populated; the verification flag is how the settings page knows whether the
/// credential was ever accepted.
/// </summary>
public interface IServiceCatalogClient
{
    /// <summary>Probes the connection's service. Never throws: a network failure, a
    /// non-2xx status, an unparseable body, an unusable base URL and an unknown
    /// service id all answer <see cref="ModelCatalog.Unverified"/>, because a user
    /// whose provider does not implement model listing still has a usable connection.
    /// The one thing that does escape is the caller's own cancellation — a probe the
    /// caller abandoned is not a result about the connection, and a caller that
    /// persisted one would stamp a verification state for a request it never made.</summary>
    Task<ModelCatalog> ListModelsAsync(ServiceConnection connection, CancellationToken ct = default);
}

/// <inheritdoc cref="IServiceCatalogClient"/>
public class ServiceCatalogClient(IHttpClientFactory httpClientFactory, ILogger<ServiceCatalogClient> logger) : IServiceCatalogClient
{
    /// <summary>The pooled handler is shared with the completion client. Nothing is
    /// configured on the client itself: the absolute URI, the authorization header and
    /// the descriptor's headers all travel per request, because they belong to the
    /// connection being probed rather than to whichever connection used the client
    /// last — and <see cref="IHttpClientFactory"/> hands out a client per call, so
    /// nothing set here could leak into another.</summary>
    private const string ClientName = "llm";

    private const string CatalogPath = "v1/models";
    private const string AuthorizationHeader = "Authorization";
    private const string UserAgentHeader = "User-Agent";

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<ServiceCatalogClient> _logger = logger;

    public async Task<ModelCatalog> ListModelsAsync(ServiceConnection connection, CancellationToken ct = default)
    {
        try
        {
            // A row naming a service the registry does not know cannot be probed; the
            // connection is left as it is and reported unverified. Inside the try so the
            // "never throws" contract covers a caller that hands over nothing.
            var descriptor = ServiceDescriptorRegistry.Find(connection.Service);
            if (descriptor is null) return ModelCatalog.Unverified;

            using var request = BuildRequest(connection, descriptor);
            var client = _httpClientFactory.CreateClient(ClientName);
            using var response = await client.SendAsync(request, ct);

            return response.IsSuccessStatusCode
                ? ReadCatalog(await response.Content.ReadAsStringAsync(ct))
                : ModelCatalog.Unverified;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Every failure mode lands here — refused connection, timeout, a base URL
            // that is not a URI, a body that is not JSON — and all of them mean the
            // same thing to the caller. This is the "a failed probe is a state" rule,
            // not control flow dressed as an exception.
            //
            // The caller's own cancellation is deliberately not caught: the caller asked
            // for the work to stop, and a caller that reports "unverified" for work it
            // aborted would persist a verification stamp for a probe that never ran. A
            // timeout is the opposite case and stays swallowed — HttpClient cancels its
            // own internal token, not the caller's, so `ct.IsCancellationRequested` is
            // still false when a timeout arrives, and a timeout is a real probe result.
            LogFailure(ex);
            return ModelCatalog.Unverified;
        }
    }

    /// <summary>Records *that* a probe failed and *what kind* of failure it was, and
    /// nothing else. The request carries the connection's key and the base URL the user
    /// typed, and an exception's message can echo either back, so neither the exception
    /// object nor anything derived from the connection goes to the log. The type name is
    /// the whole payload: it is what separates a DNS failure from a refused connection
    /// in the log, which is the only question this line exists to answer.</summary>
    private void LogFailure(Exception failure) =>
        _logger.LogDebug("Service catalog probe failed ({FailureCategory}); the connection is unverified.",
            failure.GetType().Name);

    private static HttpRequestMessage BuildRequest(ServiceConnection connection, ServiceDescriptor descriptor)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, ServiceEndpoint.Resolve(connection.BaseUrl, CatalogPath));
        request.Headers.TryAddWithoutValidation(AuthorizationHeader, $"Bearer {connection.ApiKey}");
        request.Headers.TryAddWithoutValidation(UserAgentHeader, ServiceDescriptorRegistry.UserAgent);
        foreach (var header in descriptor.HeaderFactory())
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return request;
    }

    /// <summary>Reads the ids out of the standard <c>{ "data": [ { "id": … } ] }</c>
    /// shape. A body that parses but carries no catalog is not a verified connection:
    /// it would leave the user a picker with nothing in it. Entries that are not
    /// objects, or that carry no string id, are skipped rather than failing the whole
    /// listing — a service that pads its array should still yield the ids it did name.
    /// <para>
    /// An array that yields <em>no</em> ids lands on the same answer as no array at all,
    /// by the same argument: there is no catalog here, whatever the status code said. That
    /// matters because the one signal that decides the picker is <c>Models.Count</c> and
    /// the one that decides the badge is <c>Verified</c> — a 200 carrying an empty list
    /// would otherwise put a verified badge above a free-text field with no explanation,
    /// which is the disagreement the two signals must never have.
    /// </para></summary>
    private static ModelCatalog ReadCatalog(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return ModelCatalog.Unverified;

        var models = data.EnumerateArray()
            .Select(ModelId)
            .OfType<string>()
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        return models.Count == 0 ? ModelCatalog.Unverified : new ModelCatalog(models, true);
    }

    private static string? ModelId(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object
        && entry.TryGetProperty("id", out var id)
        && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;
}
