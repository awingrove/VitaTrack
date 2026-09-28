using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VitaTrack.Core.Features.ServiceConnections;

namespace VitaTrack.Core.Features.LlmEnrichment;

/// <summary>
/// Posts a chat completion to whichever connection the caller hands over. The pooled
/// handler is shared with the catalog probe, and this class sets nothing about a
/// destination or a credential on the client: the absolute URI, the authorization
/// header and the descriptor's headers all travel per request, because they belong
/// to the connection being called rather than to whichever connection used the
/// client last.
/// <para>
/// The named client is registered with no configure delegate, so there is no
/// configuration-derived <c>BaseAddress</c> and no default <c>Authorization</c> left
/// on it to lose to. That is a stronger statement than the code needs to make:
/// <c>LlmClientRequestTests</c> still hands the client a deliberately stale address
/// and authorization and shows both losing, so the per-request wins hold even against
/// a client configured by someone else.
/// </para>
/// </summary>
/// <inheritdoc cref="ILlmClient"/>
public class LlmClient(
    IHttpClientFactory httpClientFactory,
    LlmSessionId sessionId,
    ILogger<LlmClient> logger) : ILlmClient
{
    private const string ClientName = "llm";

    private const string CompletionsPath = "v1/chat/completions";
    private const string AuthorizationHeader = "Authorization";
    private const string UserAgentHeader = "User-Agent";

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly LlmSessionId _sessionId = sessionId;
    private readonly ILogger<LlmClient> _logger = logger;

    public async Task<LlmCompletion> PostChatAsync(
        string systemPrompt,
        string userPrompt,
        ServiceConnection connection,
        LlmRequestSettings settings)
    {
        // No fallback model. A config-driven default here would quietly answer with a
        // model the user never chose, on a bill they did not agree to.
        if (string.IsNullOrWhiteSpace(settings.Model))
            return new LlmCompletion(null, LlmRequestSettings.ModelRequired);

        // Captured so the non-null model is what the body is built from: the type
        // carries the fact that this call has one, so no `!` is needed further down
        // to say it.
        var model = settings.Model;

        try
        {
            // The URI is built inside the try because a saved base URL is only
            // checked for being non-blank at save time, so a user can store one that
            // is not a URI. This class's contract is an error, never a throw.
            using var request = BuildRequest(systemPrompt, userPrompt, connection, settings, model);
            if (request is null)
                return new LlmCompletion(null, LlmRequestSettings.ServiceNotFound);

            var client = _httpClientFactory.CreateClient(ClientName);
            using var response = await client.SendAsync(request);

            // The status is checked before the body is read, so an error body is never
            // buffered at all — there is nothing here that would want it, and a
            // provider's 401 is documented to quote the rejected key back.
            if (!response.IsSuccessStatusCode)
            {
                LogRefusal(response.StatusCode);
                return new LlmCompletion(null, "The AI service returned an error. Please try again or enter nutrients manually.");
            }

            return ReadCompletion(await response.Content.ReadAsStringAsync());
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            return new LlmCompletion(null, "An error occurred while calling the AI service.");
        }
    }

    /// <summary>Records that the service refused the completion and which status said
    /// so — which is the only part of the answer an operator can act on without
    /// writing down something identifying. <see cref="LogLevel.Warning"/> rather than
    /// the probe's <c>Debug</c>: an unverified probe is a state, while a refused
    /// completion is a real failure the user is told about. The status is logged as its
    /// number because that is the form both a provider's own documentation and a
    /// support thread use. The body is deliberately not an argument: most providers
    /// echo the rejected key back in a 401, and this request carried it.</summary>
    private void LogRefusal(HttpStatusCode statusCode) =>
        _logger.LogWarning("LLM API refused the completion: the service answered {StatusCode}.",
            (int)statusCode);

    /// <summary>Records *that* the call failed and *what kind* of failure it was, and
    /// nothing else — the rule <see cref="ServiceCatalogClient"/> documents for the
    /// probe. The request carried the connection's key and the base URL the user
    /// typed, and an <see cref="HttpRequestException"/> from a transport failure
    /// carries that base URL's host back in its message, so neither the exception
    /// object nor anything derived from the connection goes to the log. The type name
    /// is the whole payload: it is what separates a DNS failure from a refused
    /// connection, which is the only question this line exists to answer.</summary>
    private void LogFailure(Exception failure) =>
        _logger.LogError("Error calling the LLM API ({FailureCategory}).", failure.GetType().Name);

    private HttpRequestMessage? BuildRequest(
        string systemPrompt,
        string userPrompt,
        ServiceConnection connection,
        LlmRequestSettings settings,
        string model)
    {
        // A row naming a service the registry does not know gets no request, exactly
        // as the catalog probe refuses to probe that id. No saved row can say it
        // today — the connect form renders the registry's const — but ILlmClient is
        // public, and the hole opens the day ServiceDescriptorRegistry.All gains a
        // second entry. Refused here as well as in LlmService so a direct caller
        // cannot spend the user's key on a service the app cannot describe.
        var descriptor = ServiceDescriptorRegistry.Find(connection.Service);
        if (descriptor is null) return null;

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            ServiceEndpoint.Resolve(connection.BaseUrl, CompletionsPath))
        {
            Content = new StringContent(
                BuildBody(systemPrompt, userPrompt, settings, model), Encoding.UTF8, "application/json")
        };

        request.Headers.TryAddWithoutValidation(AuthorizationHeader, $"Bearer {connection.ApiKey}");
        request.Headers.TryAddWithoutValidation(UserAgentHeader, ServiceDescriptorRegistry.UserAgent);

        // From the injected singleton, not from the descriptor's factory: the
        // descriptor yields this same header for today's service, so applying both
        // would send the value twice.
        request.Headers.TryAddWithoutValidation(
            ServiceDescriptorRegistry.SessionHeaderName, _sessionId.Value);

        // Whatever else the service needs, applied only for headers the request does
        // not already carry — the same reason the session header is skipped above.
        foreach (var header in descriptor.HeaderFactory())
        {
            if (!request.Headers.Contains(header.Key))
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return request;
    }

    private static string BuildBody(
        string systemPrompt,
        string userPrompt,
        LlmRequestSettings settings,
        string model)
    {
        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            ["max_tokens"] = settings.MaxTokens,
            ["temperature"] = settings.Temperature
        };

        // Present only when chosen: an absent effort is what "not selected" means on
        // the wire, and sending a null-valued key is a different statement.
        if (settings.Variant is not null)
            body["reasoning_effort"] = settings.Variant;

        return JsonSerializer.Serialize(body);
    }

    private static LlmCompletion ReadCompletion(string rawBody)
    {
        var responseJson = JsonSerializer.Deserialize<JsonElement>(rawBody);
        var choices = responseJson.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
            return new LlmCompletion(null, "No response from LLM");

        var content = choices[0].GetProperty("message").GetProperty("content").GetString();
        return string.IsNullOrWhiteSpace(content)
            ? new LlmCompletion(null, "Empty response from LLM")
            : new LlmCompletion(content, null);
    }
}
