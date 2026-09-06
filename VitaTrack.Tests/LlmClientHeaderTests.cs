using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Moq.Protected;
using VitaTrack.Infrastructure;
using VitaTrack.Infrastructure.Models;
using VitaTrack.Infrastructure.Services;

namespace VitaTrack.Tests;

[TestClass]
public class LlmClientHeaderTests
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
            BaseAddress = new System.Uri("https://dummy.example.com/v1")
        };
        factoryMock.Setup(f => f.CreateClient("llm")).Returns(client);
        return factoryMock.Object;
    }

    private static IOptions<VitaTrackOptions> CreateOptions() =>
        Options.Create(new VitaTrackOptions
        {
            BaseUrl = "https://dummy.example.com/v1",
            ApiKey = "test-real-api-key",
            Model = "test-model",
            MaxTokens = 16384,
            Temperature = 0.1
        });

    [TestMethod]
    public async Task PostChatAsync_SendsOpenCodeSessionAndUserAgentHeaders()
    {
        HttpRequestMessage? capturedRequest = null;
        var apiMock = new Mock<HttpMessageHandler>();
        apiMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(@"{ ""choices"": [{ ""message"": { ""content"": ""hello"" } }] }")
            });

        var factory = CreateHttpClientFactory(apiMock.Object);
        var client = new LlmClient(factory, CreateOptions(), NullLogger<LlmClient>.Instance);

        await client.PostChatAsync("system", "user");

        Assert.IsNotNull(capturedRequest);
        Assert.IsTrue(capturedRequest!.Headers.Contains("x-opencode-session"), "Missing x-opencode-session header");
        Assert.IsFalse(string.IsNullOrWhiteSpace(capturedRequest.Headers.GetValues("x-opencode-session").First()));
        Assert.IsTrue(capturedRequest.Headers.UserAgent.ToString().Contains("VitaTrack"), "Missing User-Agent header");
    }
}
