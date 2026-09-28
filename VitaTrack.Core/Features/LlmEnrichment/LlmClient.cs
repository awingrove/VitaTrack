using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VitaTrack.Core.Features.ServiceConnections;

namespace VitaTrack.Core.Features.LlmEnrichment;

/// <summary>
/// Posts a chat completion to whichever connection the caller hands over. The pooled
/// handler is shared with the catalog probe and nothing about a destination or a
/// credential is ever set on the client: the absolute URI, the authorization header
/// and the descriptor's headers all travel per request, because they belong to the
/// connection being called rather than to whichever connection used the client last.
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
            var client = _httpClientFactory.CreateClient(ClientName);
            using var response = await client.SendAsync(request);
            var rawBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("LLM API error: {StatusCode} - {Content}", response.StatusCode, rawBody);
                return new LlmCompletion(null, "The AI service returned an error. Please try again or enter nutrients manually.");
            }

            return ReadCompletion(rawBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling LLM API");
            return new LlmCompletion(null, "An error occurred while calling the AI service.");
        }
    }

    private HttpRequestMessage BuildRequest(
        string systemPrompt,
        string userPrompt,
        ServiceConnection connection,
        LlmRequestSettings settings,
        string model)
    {
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
        var descriptor = ServiceDescriptorRegistry.Find(connection.Service);
        if (descriptor is not null)
        {
            foreach (var header in descriptor.HeaderFactory())
            {
                if (!request.Headers.Contains(header.Key))
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
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
