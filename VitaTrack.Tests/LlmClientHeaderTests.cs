using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core.Features.LlmEnrichment;
using VitaTrack.Core.Features.ServiceConnections;
using VitaTrack.Tests.TestDoubles;

namespace VitaTrack.Tests;

/// <summary>
/// The two headers the app has always put on every call, kept as their own class:
/// the user agent that identifies VitaTrack to the service, and the session header
/// that correlates its traffic. Which values they carry, and the one-home guarantee
/// behind the session header, are <see cref="LlmClientRequestTests"/>' subject, as are
/// the four error paths this class used to repeat in a weaker form.
/// </summary>
[TestClass]
public class LlmClientHeaderTests
{
    private const string ReplyBody = @"{ ""choices"": [{ ""message"": { ""content"": ""hello"" } }] }";

    private static Task<LlmCompletion> PostAsync(HttpMessageHandler handler) =>
        new LlmClient(new SequencedHttpClientFactory(handler), new LlmSessionId(), new RecordingLogger<LlmClient>())
            .PostChatAsync("system", "user", LlmTestData.Connection(), LlmTestData.Settings());

    [TestMethod]
    public async Task PostChatAsync_SendsTheApplicationUserAgent()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ReplyBody);

        await PostAsync(handler);

        Assert.IsTrue(
            handler.SentRequest().Headers.UserAgent.ToString().Contains("VitaTrack", StringComparison.Ordinal),
            "every request the app makes identifies the app, the completion included");
    }

    [TestMethod]
    public async Task PostChatAsync_SendsTheSessionHeader()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ReplyBody);

        await PostAsync(handler);

        Assert.IsTrue(
            handler.SentRequest().Headers.Contains(ServiceDescriptorRegistry.SessionHeaderName),
            "the completion carries the same session header the catalog probe does");
    }
}
