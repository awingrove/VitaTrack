using System.Linq;
using System.Net;
using System.Net.Http;
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
/// The connection and settings come from <see cref="LlmTestData"/>, and the body is
/// read through <see cref="RecordingHandler.SentBody"/> rather than off the captured
/// request: the client disposes the request it built, which takes the content with it.
/// </para>
/// </summary>
[TestClass]
public class LlmClientRequestTests
{
    private const string ReplyBody = @"{ ""choices"": [{ ""message"": { ""content"": ""hello"" } }] }";

    private static LlmClient Client(HttpMessageHandler handler) =>
        new(new SequencedHttpClientFactory(handler), new LlmSessionId(), new RecordingLogger<LlmClient>());

    /// <summary>Review Focus #1. The invariant, not a path: however the two slash forms
    /// are combined, they must request the same URI. A form-sensitive combine would
    /// send a doubled or missing separator to one host and not the other, and the
    /// failure would depend on what the user typed into the connect form.</summary>
    [TestMethod]
    public async Task PostChatAsync_BuildsAbsoluteUri_NoBaseAddressOnClient()
    {
        var withSlash = new RecordingHandler(HttpStatusCode.OK, ReplyBody);
        var withoutSlash = new RecordingHandler(HttpStatusCode.OK, ReplyBody);

        await Client(withSlash).PostChatAsync("s", "u", LlmTestData.Connection("https://svc.example/v1/"), LlmTestData.Settings());
        await Client(withoutSlash).PostChatAsync("s", "u", LlmTestData.Connection(), LlmTestData.Settings());

        var slashed = withSlash.SentRequestUri();
        Assert.IsTrue(slashed.IsAbsoluteUri, "the request URI is absolute, so no BaseAddress took part in building it");
        Assert.AreEqual(
            withoutSlash.SentRequestUri(),
            slashed,
            "a trailing slash on the saved base URL must not change where the completion goes");
    }

    /// <summary>Review Focus #1, second half. A named <c>HttpClient</c> is pooled, so a
    /// client can carry a <c>BaseAddress</c> from a previous caller's connection and a
    /// relative URL would resolve against whatever that client happens to carry. The
    /// real <c>"llm"</c> client is registered with no configure delegate and so carries
    /// none, which is why this test supplies a hostile one itself: the invariant it
    /// pins is that the request URI is built from the connection and does not depend on
    /// the client's state at all, and that has to hold against a client someone else
    /// configured. The address handed to the client here is deliberately a *different*
    /// host: with the relative-URI mutation applied the recorded URI comes out as
    /// <c>https://stale.example/v1/chat/completions</c> — this host, not the
    /// connection's.</summary>
    [TestMethod]
    public async Task PostChatAsync_BuildsUriFromTheConnectionNotFromAClientBaseAddress()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ReplyBody);
        var factory = new SequencedHttpClientFactory(new Uri("https://stale.example/v1"), handler);
        var client = new LlmClient(factory, new LlmSessionId(), new RecordingLogger<LlmClient>());

        await client.PostChatAsync("s", "u", LlmTestData.Connection("https://two.example/v1"), LlmTestData.Settings());

        Assert.AreEqual(
            "https://two.example/v1/v1/chat/completions",
            handler.SentRequestUri().ToString(),
            "the connection's host wins over the pooled client's BaseAddress");
    }

    /// <summary>The credential belongs to the connection, and travels with the request
    /// rather than sitting on the client. The client here arrives already carrying an
    /// authorization as a <c>DefaultRequestHeader</c>: the state the real <c>"llm"</c>
    /// named client was in while it still had a configuration-derived delegate, and
    /// deliberately not its state now — nothing registers that default any more, so
    /// this is a hostile input rather than a description of the pool. What is being
    /// shown is that the per-request value wins regardless. .NET copies a pooled
    /// default onto a request only for headers the request does not already carry, so
    /// the per-request value is what arrives and the stale one never does: one value,
    /// and it is the connection's.</summary>
    [TestMethod]
    public async Task PostChatAsync_SendsBearerAuthorizationHeader_PerRequest()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ReplyBody);
        var factory = new SequencedHttpClientFactory(
            client => client.DefaultRequestHeaders.Add("Authorization", "Bearer stale-config-key"),
            handler);
        var client = new LlmClient(factory, new LlmSessionId(), new RecordingLogger<LlmClient>());

        await client.PostChatAsync("s", "u", LlmTestData.Connection(), LlmTestData.Settings());

        var authorization = handler.SentRequest().Headers.GetValues("Authorization").ToList();
        Assert.AreEqual(1, authorization.Count, "one Authorization header, not the request's plus the client's");
        Assert.AreEqual($"Bearer {LlmTestData.ApiKey}", authorization[0], "the key comes from the connection being called");
    }

    [TestMethod]
    public async Task PostChatAsync_SendsModelFromSettings()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ReplyBody);

        await Client(handler).PostChatAsync("s", "u", LlmTestData.Connection(), LlmTestData.Settings(model: "vendor/model-x"));

        Assert.AreEqual("vendor/model-x", handler.SentBody().GetProperty("model").GetString());
    }

    [TestMethod]
    public async Task PostChatAsync_SendsVariantAsReasoningEffort_WhenChosen()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ReplyBody);

        await Client(handler).PostChatAsync("s", "u", LlmTestData.Connection(), LlmTestData.Settings(variant: "high"));

        Assert.AreEqual("high", handler.SentBody().GetProperty("reasoning_effort").GetString(),
            "the connection's variant is the request's reasoning_effort");
    }

    [TestMethod]
    public async Task PostChatAsync_OmitsReasoningEffort_WhenNoVariantChosen()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ReplyBody);

        await Client(handler).PostChatAsync("s", "u", LlmTestData.Connection(), LlmTestData.Settings(variant: null));

        Assert.IsFalse(handler.SentBody().TryGetProperty("reasoning_effort", out _),
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

        await Client(handler).PostChatAsync(
            "s", "u", LlmTestData.Connection(), LlmTestData.Settings(maxTokens: 321, temperature: 0.125));

        var body = handler.SentBody();
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

        await client.PostChatAsync("s", "u", LlmTestData.Connection(), LlmTestData.Settings());
        await client.PostChatAsync("s", "u", LlmTestData.Connection(), LlmTestData.Settings());

        var sent = handler.SentRequest().Headers
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
            .PostChatAsync("s", "u", LlmTestData.Connection(), LlmTestData.Settings(model: null));

        Assert.IsNull(result.Content);
        StringAssert.Contains(result.Error, "Settings", "the error has to say where the model is chosen");
        Assert.AreEqual(LlmRequestSettings.ModelRequired, result.Error,
            "and it is the one message both surfaces must use for this fault, not a second wording");
    }

    [TestMethod]
    public async Task PostChatAsync_WithNullModel_SendsNoRequest()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ReplyBody);

        await Client(handler).PostChatAsync("s", "u", LlmTestData.Connection(), LlmTestData.Settings(model: null));

        Assert.IsNull(handler.LastRequest, "a call with no model must not reach the network at all");
    }

    /// <summary>A row naming a service the registry does not know gets no request. The
    /// catalog probe already refuses that id; this pins the completion client refusing
    /// it too, so a saved connection cannot spend the user's key against a service the
    /// app has no descriptor for and therefore no headers for. Today only the connect
    /// form can write a service id, and it writes the registry's const — so this is
    /// unreachable from the app and reachable from the public seam, which is the whole
    /// reason the check belongs here as well as in <c>LlmService</c>.
    /// <para>
    /// It failed before the fix: the descriptor headers were simply skipped and the
    /// request went out to the base URL anyway.
    /// </para></summary>
    [TestMethod]
    public async Task PostChatAsync_WithAnUnknownService_SendsNoRequest()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ReplyBody);

        var result = await Client(handler).PostChatAsync(
            "s", "u", LlmTestData.Connection(service: "not-a-service"), LlmTestData.Settings());

        Assert.IsNull(handler.LastRequest,
            "the credential must not be posted to a service the registry cannot describe");
        Assert.IsNull(result.Content);
        Assert.AreEqual(LlmRequestSettings.ServiceNotFound, result.Error);
        Assert.AreNotEqual(LlmRequestSettings.ModelRequired, result.Error,
            "'no such service' and 'no model chosen' are different faults and must not read identically "
            + "to a user who can act on neither");
    }

    [TestMethod]
    public async Task PostChatAsync_OnNonSuccessStatus_KeepsTheFriendlyError()
    {
        var handler = new RecordingHandler(HttpStatusCode.InternalServerError, @"{ ""error"": ""boom"" }");

        var result = await Client(handler).PostChatAsync("s", "u", LlmTestData.Connection(), LlmTestData.Settings());

        Assert.IsNull(result.Content);
        Assert.AreEqual(
            "The AI service returned an error. Please try again or enter nutrients manually.",
            result.Error);
    }

    [TestMethod]
    public async Task PostChatAsync_WithEmptyChoices_KeepsTheNoResponseError()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, @"{ ""choices"": [] }");

        var result = await Client(handler).PostChatAsync("s", "u", LlmTestData.Connection(), LlmTestData.Settings());

        Assert.IsNull(result.Content);
        Assert.AreEqual("No response from LLM", result.Error);
    }

    [TestMethod]
    public async Task PostChatAsync_WithEmptyContent_KeepsTheEmptyResponseError()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, @"{ ""choices"": [{ ""message"": { ""content"": ""  "" } }] }");

        var result = await Client(handler).PostChatAsync("s", "u", LlmTestData.Connection(), LlmTestData.Settings());

        Assert.IsNull(result.Content);
        Assert.AreEqual("Empty response from LLM", result.Error);
    }

    [TestMethod]
    public async Task PostChatAsync_WhenTheRequestDies_KeepsTheCatchAllError()
    {
        var handler = new ThrowingHandler(new HttpRequestException("connection refused"));

        var result = await Client(handler).PostChatAsync("s", "u", LlmTestData.Connection(), LlmTestData.Settings());

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
            .PostChatAsync("s", "u", LlmTestData.Connection(baseUrl: "not a url"), LlmTestData.Settings());

        Assert.IsNull(result.Content);
        Assert.IsNotNull(result.Error);
    }
}
