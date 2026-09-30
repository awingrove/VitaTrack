using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core.Features.LlmEnrichment;
using VitaTrack.Tests.TestDoubles;

namespace VitaTrack.Tests;

/// <summary>
/// The two wire statements the picker's reasoning-effort changes make. <see cref="LlmClientRequestTests"/>
/// owns the rest of what <see cref="LlmClient"/> sends; these are the tests added with the
/// "default" variant, which share one claim: whatever the picker shows, the body carries a
/// literal <c>reasoning_effort</c> only when a concrete effort was chosen, absent otherwise.
/// <para>
/// The connection and settings come from <see cref="LlmTestData"/>, and the body is read
/// through <see cref="RecordingHandler.SentBody"/> rather than off the captured request,
/// same as the parent class.
/// </para>
/// </summary>
[TestClass]
public class LlmClientReasoningEffortTests
{
    private const string ReplyBody = @"{ ""choices"": [{ ""message"": { ""content"": ""hello"" } }] }";

    private static LlmClient Client(HttpMessageHandler handler) =>
        new(new SequencedHttpClientFactory(handler), new LlmSessionId(), new RecordingLogger<LlmClient>());

    /// <summary>"default" is the picker's "let the model use its own effort" choice: on
    /// the wire it is the same statement as no variant at all, an absent key, not a
    /// literal "default" — which providers that accept no default effort reject, the
    /// same way they reject the literal "none".</summary>
    [TestMethod]
    public async Task PostChatAsync_OmitsReasoningEffort_WhenVariantIsDefault()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ReplyBody);

        await Client(handler).PostChatAsync("s", "u", LlmTestData.Connection(), LlmTestData.Settings(variant: "default"));

        Assert.IsFalse(handler.SentBody().TryGetProperty("reasoning_effort", out _),
            "the 'default' variant is the model's own effort, an absent key on the wire");
    }
}
