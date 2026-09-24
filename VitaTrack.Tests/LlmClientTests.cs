using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Moq.Protected;
using VitaTrack.Core;
using VitaTrack.Core.Features.LlmEnrichment;

namespace VitaTrack.Tests;

[TestClass]
public class LlmClientTests
{
    private static Mock<HttpMessageHandler> CreateHandlerMock(HttpStatusCode status, string content)
    {
        var mock = new Mock<HttpMessageHandler>();
        mock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = status,
                Content = new StringContent(content)
            });
        return mock;
    }

    private static IHttpClientFactory CreateHttpClientFactory(HttpMessageHandler llmHandler)
    {
        var factoryMock = new Mock<IHttpClientFactory>();
        var client = new HttpClient(llmHandler)
        {
            BaseAddress = new System.Uri("https://8.8.8.8/v1")
        };
        factoryMock.Setup(f => f.CreateClient("llm")).Returns(client);
        return factoryMock.Object;
    }

    private static IOptions<VitaTrackOptions> CreateOptions(string? reasoningEffort = null) =>
        Options.Create(new VitaTrackOptions
        {
            BaseUrl = "https://8.8.8.8/v1",
            ApiKey = "test-api-key",
            Model = "test-model",
            MaxTokens = 512,
            Temperature = 0.2,
            ReasoningEffort = reasoningEffort
        });

    [TestMethod]
    public async Task PostChatAsync_ReturnsContent_OnSuccess()
    {
        var handlerMock = CreateHandlerMock(
            HttpStatusCode.OK,
            @"{ ""choices"": [{ ""message"": { ""content"": ""hello"" } }] }");

        var client = new LlmClient(
            CreateHttpClientFactory(handlerMock.Object),
            CreateOptions(),
            NullLogger<LlmClient>.Instance);

        var result = await client.PostChatAsync("system prompt", "user prompt");

        Assert.AreEqual("hello", result.Content);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public async Task PostChatAsync_SendsModelTemperatureAndMaxTokens_WithoutReasoningEffortByDefault()
    {
        string? capturedBody = null;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(@"{ ""choices"": [{ ""message"": { ""content"": ""hello"" } }] }")
            });

        var client = new LlmClient(
            CreateHttpClientFactory(handlerMock.Object),
            CreateOptions(),
            NullLogger<LlmClient>.Instance);

        await client.PostChatAsync("system prompt", "user prompt");

        Assert.IsNotNull(capturedBody, "Request body was not captured");
        using var document = JsonDocument.Parse(capturedBody!);
        var root = document.RootElement;

        Assert.AreEqual("test-model", root.GetProperty("model").GetString());
        Assert.AreEqual(512, root.GetProperty("max_tokens").GetInt32());
        Assert.AreEqual(0.2, root.GetProperty("temperature").GetDouble(), delta: 0.0001);
        Assert.IsFalse(
            root.TryGetProperty("reasoning_effort", out _),
            "reasoning_effort must be absent when not configured");
    }

    [TestMethod]
    public async Task PostChatAsync_SendsReasoningEffort_WhenConfigured()
    {
        string? capturedBody = null;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(@"{ ""choices"": [{ ""message"": { ""content"": ""hello"" } }] }")
            });

        var client = new LlmClient(
            CreateHttpClientFactory(handlerMock.Object),
            CreateOptions(reasoningEffort: "low"),
            NullLogger<LlmClient>.Instance);

        await client.PostChatAsync("system prompt", "user prompt");

        Assert.IsNotNull(capturedBody, "Request body was not captured");
        using var document = JsonDocument.Parse(capturedBody!);
        Assert.AreEqual("low", document.RootElement.GetProperty("reasoning_effort").GetString());
    }

    [TestMethod]
    public async Task PostChatAsync_ReturnsFriendlyError_OnNonSuccessStatus()
    {
        var handlerMock = CreateHandlerMock(
            HttpStatusCode.InternalServerError,
            @"{ ""error"": { ""message"": ""upstream blew up"" } }");

        var client = new LlmClient(
            CreateHttpClientFactory(handlerMock.Object),
            CreateOptions(),
            NullLogger<LlmClient>.Instance);

        var result = await client.PostChatAsync("system prompt", "user prompt");

        Assert.IsNull(result.Content);
        Assert.IsNotNull(result.Error);
        Assert.AreEqual(
            "The AI service returned an error. Please try again or enter nutrients manually.",
            result.Error);
    }

    [TestMethod]
    public async Task PostChatAsync_ReturnsError_WhenChoicesEmpty()
    {
        var handlerMock = CreateHandlerMock(HttpStatusCode.OK, @"{ ""choices"": [] }");

        var client = new LlmClient(
            CreateHttpClientFactory(handlerMock.Object),
            CreateOptions(),
            NullLogger<LlmClient>.Instance);

        var result = await client.PostChatAsync("system prompt", "user prompt");

        Assert.IsNull(result.Content);
        Assert.AreEqual("No response from LLM", result.Error);
    }

    [TestMethod]
    public async Task PostChatAsync_ReturnsError_WhenContentEmpty()
    {
        var handlerMock = CreateHandlerMock(
            HttpStatusCode.OK,
            @"{ ""choices"": [{ ""message"": { ""content"": """" } }] }");

        var client = new LlmClient(
            CreateHttpClientFactory(handlerMock.Object),
            CreateOptions(),
            NullLogger<LlmClient>.Instance);

        var result = await client.PostChatAsync("system prompt", "user prompt");

        Assert.IsNull(result.Content);
        Assert.AreEqual("Empty response from LLM", result.Error);
    }

    [TestMethod]
    public async Task PostChatAsync_ReturnsError_WhenHandlerThrows()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var client = new LlmClient(
            CreateHttpClientFactory(handlerMock.Object),
            CreateOptions(),
            NullLogger<LlmClient>.Instance);

        var result = await client.PostChatAsync("system prompt", "user prompt");

        Assert.IsNull(result.Content);
        Assert.AreEqual("An error occurred while calling the AI service.", result.Error);
    }
}
