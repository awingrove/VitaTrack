using System.Net.Http;
using System.Text.Json;

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
    /// whose provider does not implement model listing still has a usable connection.</summary>
    Task<ModelCatalog> ListModelsAsync(ServiceConnection connection, CancellationToken ct = default);
}

/// <inheritdoc cref="IServiceCatalogClient"/>
public class ServiceCatalogClient(IHttpClientFactory httpClientFactory) : IServiceCatalogClient
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

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    public async Task<ModelCatalog> ListModelsAsync(ServiceConnection connection, CancellationToken ct = default)
    {
        // A row naming a service the registry does not know cannot be probed; the
        // connection is left as it is and reported unverified.
        var descriptor = ServiceDescriptorRegistry.Find(connection.Service);
        if (descriptor is null) return ModelCatalog.Unverified;

        try
        {
            using var request = BuildRequest(connection, descriptor);
            var client = _httpClientFactory.CreateClient(ClientName);
            using var response = await client.SendAsync(request, ct);

            return response.IsSuccessStatusCode
                ? ReadCatalog(await response.Content.ReadAsStringAsync(ct))
                : ModelCatalog.Unverified;
        }
        catch (Exception)
        {
            // Every failure mode lands here — refused connection, timeout, a base URL
            // that is not a URI, a body that is not JSON — and all of them mean the
            // same thing to the caller. This is the "a failed probe is a state" rule,
            // not control flow dressed as an exception.
            return ModelCatalog.Unverified;
        }
    }

    private static HttpRequestMessage BuildRequest(ServiceConnection connection, ServiceDescriptor descriptor)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, ServiceEndpoint.Resolve(connection.BaseUrl, CatalogPath));
        request.Headers.TryAddWithoutValidation(AuthorizationHeader, $"Bearer {connection.ApiKey}");
        request.Headers.TryAddWithoutValidation("User-Agent", ServiceDescriptorRegistry.UserAgent);
        foreach (var header in descriptor.HeaderFactory())
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return request;
    }

    /// <summary>Reads the ids out of the standard <c>{ "data": [ { "id": … } ] }</c>
    /// shape. A body that parses but carries no catalog is not a verified connection:
    /// it would leave the user a picker with nothing in it. Entries that are not
    /// objects, or that carry no string id, are skipped rather than failing the whole
    /// listing — a service that pads its array should still yield the ids it did name.</summary>
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

        return new ModelCatalog(models, true);
    }

    private static string? ModelId(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object
        && entry.TryGetProperty("id", out var id)
        && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;
}
