using VitaTrack.Core.Features.LlmEnrichment;
using VitaTrack.Core.Features.ServiceConnections;

namespace VitaTrack.Tests.TestDoubles;

/// <summary>
/// The connection-and-settings pair every LLM seam test needs to make a call. Five
/// test classes reach for it, and each writing its own copy is how two of them end up
/// disagreeing about what a valid connection looks like — the same reason
/// <see cref="SequencedHttpClientFactory"/> exists.
/// <para>
/// Every parameter has a default, so a test that cares about one value names it and a
/// test that cares about none writes nothing. <c>model</c> defaults to a non-blank
/// value because a blank one is the "go point at Settings" state, and a test that is
/// not about that state should not have to think about it.
/// </para>
/// </summary>
internal static class LlmTestData
{
    public static ServiceConnection Connection(
        string baseUrl = "https://svc.example/v1",
        string apiKey = "sk-test",
        string? model = "test-model",
        string? variant = null,
        int maxTokens = 4096,
        double temperature = 0.7,
        string service = ServiceDescriptorRegistry.OpenCodeServiceId) =>
        new()
        {
            Service = service,
            BaseUrl = baseUrl,
            ApiKey = apiKey,
            Model = model,
            Variant = variant,
            MaxTokens = maxTokens,
            Temperature = temperature,
            IsActive = true
        };

    public static LlmRequestSettings Settings(
        string? model = "test-model",
        string? variant = null,
        int maxTokens = 4096,
        double temperature = 0.7) =>
        new(model, variant, maxTokens, temperature);
}
