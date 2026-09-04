using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Moq.Protected;
using VitaTrack.Infrastructure.Services;

namespace VitaTrack.Tests;

[TestClass]
public class HtmlScraperServiceTests
{
    private static IHttpClientFactory CreateScraperFactory(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new System.Uri("https://example.com/") };
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("scraper")).Returns(client);
        return factoryMock.Object;
    }

    [TestMethod]
    public async Task FetchCleanHtmlAsync_BlocksUnsafeUrl_ReturnsNull()
    {
        var factory = CreateScraperFactory(new Mock<HttpMessageHandler>().Object);
        var svc = new HtmlScraperService(factory, NullLogger<HtmlScraperService>.Instance);

        var result = await svc.FetchCleanHtmlAsync("http://example.com/product");

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task FetchCleanHtmlAsync_ReturnsNull_WhenNotSuccess()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.NotFound });
        var svc = new HtmlScraperService(CreateScraperFactory(handler.Object), NullLogger<HtmlScraperService>.Instance);

        var result = await svc.FetchCleanHtmlAsync("https://example.com/missing");

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task FetchCleanHtmlAsync_ReturnsNull_WhenFetchThrows()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("network down"));
        var svc = new HtmlScraperService(CreateScraperFactory(handler.Object), NullLogger<HtmlScraperService>.Instance);

        var result = await svc.FetchCleanHtmlAsync("https://example.com/product");

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task FetchCleanHtmlAsync_TruncatesLongContent()
    {
        var longText = new string('x', 13000);
        var html = $"<html><body><main>{longText}</main></body></html>";
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(html)
            });
        var svc = new HtmlScraperService(CreateScraperFactory(handler.Object), NullLogger<HtmlScraperService>.Instance);

        var result = await svc.FetchCleanHtmlAsync("https://example.com/product");

        Assert.IsNotNull(result);
        Assert.IsTrue(result!.Length <= 12000, $"Expected truncation, got {result.Length}");
        Assert.IsFalse(result.Contains("script"));
    }
}
