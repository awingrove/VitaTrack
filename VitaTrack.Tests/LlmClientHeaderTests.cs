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
/// behind the session header, are <see cref="LlmClientRequestTests"/>' subject.
/// </summary>
[TestClass]
public class LlmClientHeaderTests
{
    private const string ReplyBody = @"{ ""choices"": [{ ""message"": { ""content"": ""hello"" } }] }";

    private static LlmClient Client(HttpMessageHandler handler) =>
        new(new SequencedHttpClientFactory(handler), new LlmSessionId(), new RecordingLogger<LlmClient>());

    private static Task<LlmCompletion> PostAsync(HttpMessageHandler handler) =>
        Client(handler).PostChatAsync(
            "system",
            "user",
            new ServiceConnection
            {
                Service = ServiceDescriptorRegistry.OpenCodeServiceId,
                BaseUrl = "https://svc.example/v1",
                ApiKey = "test-real-api-key"
            },
            new LlmRequestSettings("test-model", null, 16384, 0.1));

    [TestMethod]
    public async Task PostChatAsync_SendsTheApplicationUserAgent()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ReplyBody);

        await PostAsync(handler);

        Assert.IsTrue(
            handler.LastRequest!.Headers.UserAgent.ToString().Contains("VitaTrack", StringComparison.Ordinal),
            "every request the app makes identifies the app, the completion included");
    }

    [TestMethod]
    public async Task PostChatAsync_SendsTheSessionHeader()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ReplyBody);

        await PostAsync(handler);

        Assert.IsTrue(
            handler.LastRequest!.Headers.Contains(ServiceDescriptorRegistry.SessionHeaderName),
            "the completion carries the same session header the catalog probe does");
    }

    [TestMethod]
    public async Task PostChatAsync_ReturnsError_WhenApiReturnsNonSuccess()
    {
        var result = await PostAsync(new RecordingHandler(HttpStatusCode.InternalServerError, "boom"));

        Assert.IsNull(result.Content);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Error));
    }

    [TestMethod]
    public async Task PostChatAsync_ReturnsError_WhenChoicesEmpty()
    {
        var result = await PostAsync(new RecordingHandler(HttpStatusCode.OK, @"{ ""choices"": [] }"));

        Assert.IsNull(result.Content);
        Assert.IsTrue(result.Error!.Contains("No response"));
    }

    [TestMethod]
    public async Task PostChatAsync_ReturnsError_WhenContentEmpty()
    {
        var result = await PostAsync(
            new RecordingHandler(HttpStatusCode.OK, @"{ ""choices"": [{ ""message"": { ""content"": ""   "" } }] }"));

        Assert.IsNull(result.Content);
        Assert.IsTrue(result.Error!.Contains("Empty response"));
    }
}
