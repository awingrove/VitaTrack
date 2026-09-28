using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core.Features.LlmEnrichment;
using VitaTrack.Core.Features.ServiceConnections;
using VitaTrack.Tests.TestDoubles;

namespace VitaTrack.Tests;

/// <summary>
/// The request <see cref="LlmClient"/> builds: where it goes, what it authenticates
/// with, and what it says. Every value on it comes from the connection and the
/// settings handed to the call — nothing from configuration — and each test below
/// pins one of those by reading the request the client actually built.
/// <para>
/// Reading the body needs <see cref="RecordingHandler.LastRequestBody"/> rather than
/// the captured request: the client disposes the request it built, which takes the
/// content with it.
/// </para>
/// </summary>
[TestClass]
public class LlmClientRequestTests
{
    private const string ReplyBody = @"{ ""choices"": [{ ""message"": { ""content"": ""hello"" } }] }";

    private static ServiceConnection Connection(
        string baseUrl = "https://svc.example/v1",
        string apiKey = "sk-connection",
        string service = ServiceDescriptorRegistry.OpenCodeServiceId) =>
        new() { BaseUrl = baseUrl, ApiKey = apiKey, Service = service };

    private static LlmRequestSettings Settings(
        string? model = "some-model",
        string? variant = null,
        int maxTokens = 4096,
        double temperature = 0.7) =>
        new(model, variant, maxTokens, temperature);

    private static LlmClient Client(HttpMessageHandler handler) =>
        new(new SequencedHttpClientFactory(handler), new LlmSessionId(), new RecordingLogger<LlmClient>());

    private static JsonElement SentBody(RecordingHandler handler)
    {
        Assert.IsNotNull(handler.LastRequestBody, "no request body was sent");
        return JsonDocument.Parse(handler.LastRequestBody!).RootElement.Clone();
    }

    /// <summary>Review Focus #1. The invariant, not a path: however the two slash forms
    /// are combined, they must request the same URI. A form-sensitive combine would
    /// send a doubled or missing separator to one host and not the other, and the
    /// failure would depend on what the user typed into the connect form.</summary>
    [TestMethod]
    public async Task PostChatAsync_BuildsAbsoluteUri_NoBaseAddressOnClient()
    {
        var withSlash = new RecordingHandler(HttpStatusCode.OK, ReplyBody);
        var withoutSlash = new RecordingHandler(HttpStatusCode.OK, ReplyBody);

        await Client(withSlash).PostChatAsync("s", "u", Connection("https://svc.example/v1/"), Settings());
        await Client(withoutSlash).PostChatAsync("s", "u", Connection("https://svc.example/v1"), Settings());

        var slashed = withSlash.LastRequest!.RequestUri!;
        Assert.IsTrue(slashed.IsAbsoluteUri, "the request URI is absolute, so no BaseAddress took part in building it");
        Assert.AreEqual(
            withoutSlash.LastRequest!.RequestUri,
            slashed,
            "a trailing slash on the saved base URL must not change where the completion goes");
    }

    /// <summary>Review Focus #1, second half. A named <c>HttpClient</c> is pooled and
    /// the configure delegate still gives it a <c>BaseAddress</c> from configuration, so
    /// a relative URL would resolve against whatever that client happens to carry. The
    /// address handed to the client here is deliberately a *different* host: with the
    /// relative-URI mutation applied the recorded URI comes out as
    /// <c>https://stale.example/v1/chat/completions</c> — this host, not the
    /// connection's.</summary>
    [TestMethod]
    public async Task PostChatAsync_BuildsUriFromTheConnectionNotFromAClientBaseAddress()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ReplyBody);
        var factory = new SequencedHttpClientFactory(new Uri("https://stale.example/v1"), handler);
        var client = new LlmClient(factory, new LlmSessionId(), new RecordingLogger<LlmClient>());

        await client.PostChatAsync("s", "u", Connection("https://two.example/v1"), Settings());

        Assert.AreEqual(
            "https://two.example/v1/v1/chat/completions",
            handler.LastRequest!.RequestUri!.ToString(),
            "the connection's host wins over the pooled client's BaseAddress");
    }

    /// <summary>The credential belongs to the connection, and travels with the request
    /// rather than sitting on the client. The client here arrives already carrying a
    /// config-derived authorization as a <c>DefaultRequestHeader</c>, which is exactly
    /// the state the real <c>"llm"</c> named client is in until its configure delegate is
    /// deleted. .NET copies a pooled default onto a request only for headers the request
    /// does not already carry, so the per-request value is what arrives and the stale one
    /// never does: one value, and it is the connection's.</summary>
    [TestMethod]
    public async Task PostChatAsync_SendsBearerAuthorizationHeader_PerRequest()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ReplyBody);
        var factory = new SequencedHttpClientFactory(
            client => client.DefaultRequestHeaders.Add("Authorization", "Bearer stale-config-key"),
            handler);
        var client = new LlmClient(factory, new LlmSessionId(), new RecordingLogger<LlmClient>());

        await client.PostChatAsync("s", "u", Connection(apiKey: "sk-connection"), Settings());

        var authorization = handler.LastRequest!.Headers.GetValues("Authorization").ToList();
        Assert.AreEqual(1, authorization.Count, "one Authorization header, not the request's plus the client's");
        Assert.AreEqual("Bearer sk-connection", authorization[0], "the key comes from the connection being called");
    }

    [TestMethod]
    public async Task PostChatAsync_SendsModelFromSettings()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ReplyBody);

        await Client(handler).PostChatAsync("s", "u", Connection(), Settings(model: "vendor/model-x"));

        Assert.AreEqual("vendor/model-x", SentBody(handler).GetProperty("model").GetString());
    }

    [TestMethod]
    public async Task PostChatAsync_SendsVariantAsReasoningEffort_WhenChosen()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ReplyBody);

        await Client(handler).PostChatAsync("s", "u", Connection(), Settings(variant: "high"));

        Assert.AreEqual("high", SentBody(handler).GetProperty("reasoning_effort").GetString(),
            "the connection's variant is the request's reasoning_effort");
    }

    [TestMethod]
    public async Task PostChatAsync_OmitsReasoningEffort_WhenNoVariantChosen()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ReplyBody);

        await Client(handler).PostChatAsync("s", "u", Connection(), Settings(variant: null));

        Assert.IsFalse(SentBody(handler).TryGetProperty("reasoning_effort", out _),
            "an unchosen variant is an absent key, not a null-valued one");
    }

    /// <summary>Proves the two knobs come from the call rather than from configuration:
    /// the values here are neither the record defaults nor anything a config file
    /// could be holding, so a client still reading configuration cannot produce them.
    /// </summary>
    [TestMethod]
    public async Task PostChatAsync_UsesMaxTokensAndTemperatureFromSettings()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ReplyBody);

        await Client(handler).PostChatAsync("s", "u", Connection(), Settings(maxTokens: 321, temperature: 0.125));

        var body = SentBody(handler);
        Assert.AreEqual(321, body.GetProperty("max_tokens").GetInt32());
        Assert.AreEqual(0.125, body.GetProperty("temperature").GetDouble(), delta: 0.000001);
    }

    /// <summary>The one home for the session id. Stability alone would not catch a
    /// second id — a freshly minted one per construction is stable within a test — so
    /// the assertion that bites is the identity check against the registry, plus
    /// <c>Single</c>, which fails if the value is also sent a second time from the
    /// descriptor's header factory.</summary>
    [TestMethod]
    public async Task PostChatAsync_SendsXOpencodeSessionHeader_FromTheSessionSingleton()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ReplyBody);
        var client = Client(handler);

        await client.PostChatAsync("s", "u", Connection(), Settings());
        await client.PostChatAsync("s", "u", Connection(), Settings());

        var sent = handler.LastRequest!.Headers
            .GetValues(ServiceDescriptorRegistry.SessionHeaderName).ToList();
        Assert.AreEqual(1, sent.Count, "the session header is sent once, not once per source that knows the value");
        Assert.AreEqual(ServiceDescriptorRegistry.SessionId, sent[0],
            "the completion must carry the registry's session id, or the header correlates nothing with the probe");
    }

    /// <summary>No fallback model. The old client substituted <c>gpt-4o-mini</c> for an
    /// unset model, which silently spent the user's money on a model they never chose;
    /// this pins the replacement — an error that says where to fix it, and no request
    /// at all.</summary>
    [TestMethod]
    public async Task PostChatAsync_WithNullModel_ReturnsErrorPointingAtSettings()
    {
        var result = await Client(new RecordingHandler(HttpStatusCode.OK, ReplyBody))
            .PostChatAsync("s", "u", Connection(), Settings(model: null));

        Assert.IsNull(result.Content);
        Assert.IsNotNull(result.Error);
        StringAssert.Contains(result.Error, "Settings", "the error has to say where the model is chosen");
    }

    [TestMethod]
    public async Task PostChatAsync_WithNullModel_SendsNoRequest()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ReplyBody);
        var factory = new SequencedHttpClientFactory(handler);

        await new LlmClient(factory, new LlmSessionId(), new RecordingLogger<LlmClient>())
            .PostChatAsync("s", "u", Connection(), Settings(model: null));

        Assert.IsNull(handler.LastRequest, "a call with no model must not reach the network at all");
    }

    [TestMethod]
    public async Task PostChatAsync_OnNonSuccessStatus_KeepsTheFriendlyError()
    {
        var handler = new RecordingHandler(HttpStatusCode.InternalServerError, @"{ ""error"": ""boom"" }");

        var result = await Client(handler).PostChatAsync("s", "u", Connection(), Settings());

        Assert.IsNull(result.Content);
        Assert.AreEqual(
            "The AI service returned an error. Please try again or enter nutrients manually.",
            result.Error);
    }

    [TestMethod]
    public async Task PostChatAsync_WithEmptyChoices_KeepsTheNoResponseError()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, @"{ ""choices"": [] }");

        var result = await Client(handler).PostChatAsync("s", "u", Connection(), Settings());

        Assert.IsNull(result.Content);
        Assert.AreEqual("No response from LLM", result.Error);
    }

    [TestMethod]
    public async Task PostChatAsync_WithEmptyContent_KeepsTheEmptyResponseError()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, @"{ ""choices"": [{ ""message"": { ""content"": ""  "" } }] }");

        var result = await Client(handler).PostChatAsync("s", "u", Connection(), Settings());

        Assert.IsNull(result.Content);
        Assert.AreEqual("Empty response from LLM", result.Error);
    }

    [TestMethod]
    public async Task PostChatAsync_WhenTheRequestDies_KeepsTheCatchAllError()
    {
        var handler = new ThrowingHandler(new HttpRequestException("connection refused"));

        var result = await Client(handler).PostChatAsync("s", "u", Connection(), Settings());

        Assert.IsNull(result.Content);
        Assert.AreEqual("An error occurred while calling the AI service.", result.Error);
    }

    /// <summary>A base URL is only checked for being non-blank when the connection is
    /// saved, so one that is not a URI can reach here. What this pins is the shape of
    /// the answer — an <see cref="LlmCompletion"/>, not a throw — and nothing about
    /// which message the user sees.</summary>
    [TestMethod]
    public async Task PostChatAsync_WithAnUnusableBaseUrl_ReturnsAnErrorRatherThanThrowing()
    {
        var result = await Client(new RecordingHandler(HttpStatusCode.OK, ReplyBody))
            .PostChatAsync("s", "u", Connection(baseUrl: "not a url"), Settings());

        Assert.IsNull(result.Content);
        Assert.IsNotNull(result.Error);
    }
}
