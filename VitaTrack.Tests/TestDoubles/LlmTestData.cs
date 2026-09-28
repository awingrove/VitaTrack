using VitaTrack.Core.Features.LlmEnrichment;
using VitaTrack.Core.Features.ServiceConnections;

namespace VitaTrack.Tests.TestDoubles;

/// <summary>
/// The connection-and-settings pair every LLM seam test needs to make a call. Every
/// test class that drives the seam reaches for it, and each writing its own copy is
/// how two of them end up disagreeing about what a valid connection looks like — the
/// same reason <see cref="SequencedHttpClientFactory"/> exists. The four key values
/// these classes used to carry between them (<c>sk-connection</c>,
/// <c>test-real-api-key</c>, <c>test-api-key</c>, <c>sk-test</c>) are exactly the
/// drift the type exists to stop, so there is one key now and a test that cares about
/// a different one names it.
/// <para>
/// Every parameter has a default, so a test that cares about one value names it and a
/// test that cares about none writes nothing. <c>model</c> defaults to a non-blank
/// value because a blank one is the "go point at Settings" state, and a test that is
/// not about that state should not have to think about it.
/// </para>
/// </summary>
internal static class LlmTestData
{
    /// <summary>The key every seam test sends unless it names another. Asserted on
    /// directly by the tests that check the wire carried the connection's credential,
    /// so a change to it is a deliberate edit to a claim, not a quiet refactor.</summary>
    public const string ApiKey = "sk-test";

    public static ServiceConnection Connection(
        string baseUrl = "https://svc.example/v1",
        string apiKey = ApiKey,
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
