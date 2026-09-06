using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VitaTrack.Infrastructure;

namespace VitaTrack.Infrastructure.Services;

public class LlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly VitaTrackOptions _options;
    private readonly ILogger<LlmClient> _logger;
    private readonly string _sessionId = Guid.NewGuid().ToString();

    public LlmClient(
        IHttpClientFactory httpClientFactory,
        IOptions<VitaTrackOptions> options,
        ILogger<LlmClient> logger)
    {
        _http = httpClientFactory.CreateClient("llm")!; // IHttpClientFactory.CreateClient returns a configured client for a registered named client
        _options = options.Value;
        _logger = logger;
        if (_http is not null)
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "VitaTrack/1.0 (+https://github.com/awingrove/VitaTrack)");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("x-opencode-session", _sessionId);
        }
    }

    public async Task<LlmCompletion> PostChatAsync(string systemPrompt, string userPrompt)
    {
        var model = string.IsNullOrWhiteSpace(_options.Model) ? "gpt-4o-mini" : _options.Model;
        var maxTokens = _options.MaxTokens;

        var requestBody = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            ["max_tokens"] = maxTokens,
            ["temperature"] = _options.Temperature
        };

        if (!string.IsNullOrWhiteSpace(_options.ReasoningEffort))
        {
            requestBody["reasoning_effort"] = _options.ReasoningEffort;
        }

        try
        {
            var response = await _http.PostAsJsonAsync("v1/chat/completions", requestBody);
            var rawBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("LLM API error: {StatusCode} - {Content}", response.StatusCode, rawBody);
                return new LlmCompletion(null, "The AI service returned an error. Please try again or enter nutrients manually.");
            }

            var responseJson = JsonSerializer.Deserialize<JsonElement>(rawBody);
            var choices = responseJson.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
            {
                return new LlmCompletion(null, "No response from LLM");
            }

            var content = choices[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content))
            {
                return new LlmCompletion(null, "Empty response from LLM");
            }

            return new LlmCompletion(content, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling LLM API");
            return new LlmCompletion(null, "An error occurred while calling the AI service.");
        }
    }
}