using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core.Features.LlmEnrichment;
using VitaTrack.Core.Features.ServiceConnections;
using VitaTrack.Tests.TestDoubles;

namespace VitaTrack.Tests;

/// <summary>
/// What a completion turns into: the content on a good answer, and the existing
/// error text on each way of not getting one. Which values the request carried is
/// <see cref="LlmClientRequestTests"/>' subject, not this class's — this one is about
/// the answer.
/// </summary>
[TestClass]
public class LlmClientTests
{
    private const string ReplyBody = @"{ ""choices"": [{ ""message"": { ""content"": ""hello"" } }] }";

    private static LlmClient Client(HttpMessageHandler handler) =>
        new(new SequencedHttpClientFactory(handler), new LlmSessionId(), new RecordingLogger<LlmClient>());

    private static Task<LlmCompletion> PostAsync(HttpMessageHandler handler) =>
        Client(handler).PostChatAsync(
            "system prompt",
            "user prompt",
            new ServiceConnection
            {
                Service = ServiceDescriptorRegistry.OpenCodeServiceId,
                BaseUrl = "https://svc.example/v1",
                ApiKey = "test-api-key"
            },
            new LlmRequestSettings("test-model", null, 512, 0.2));

    [TestMethod]
    public async Task PostChatAsync_ReturnsContent_OnSuccess()
    {
        var result = await PostAsync(new RecordingHandler(HttpStatusCode.OK, ReplyBody));

        Assert.AreEqual("hello", result.Content);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public async Task PostChatAsync_ReturnsFriendlyError_OnNonSuccessStatus()
    {
        var result = await PostAsync(
            new RecordingHandler(HttpStatusCode.InternalServerError, @"{ ""error"": { ""message"": ""upstream blew up"" } }"));

        Assert.IsNull(result.Content);
        Assert.IsNotNull(result.Error);
        Assert.AreEqual(
            "The AI service returned an error. Please try again or enter nutrients manually.",
            result.Error);
    }

    [TestMethod]
    public async Task PostChatAsync_ReturnsError_WhenChoicesEmpty()
    {
        var result = await PostAsync(new RecordingHandler(HttpStatusCode.OK, @"{ ""choices"": [] }"));

        Assert.IsNull(result.Content);
        Assert.AreEqual("No response from LLM", result.Error);
    }

    [TestMethod]
    public async Task PostChatAsync_ReturnsError_WhenContentEmpty()
    {
        var result = await PostAsync(
            new RecordingHandler(HttpStatusCode.OK, @"{ ""choices"": [{ ""message"": { ""content"": """" } }] }"));

        Assert.IsNull(result.Content);
        Assert.AreEqual("Empty response from LLM", result.Error);
    }

    [TestMethod]
    public async Task PostChatAsync_ReturnsError_WhenHandlerThrows()
    {
        var result = await PostAsync(new ThrowingHandler(new HttpRequestException("connection refused")));

        Assert.IsNull(result.Content);
        Assert.AreEqual("An error occurred while calling the AI service.", result.Error);
    }
}
