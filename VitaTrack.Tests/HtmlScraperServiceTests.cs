using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Moq.Protected;
using VitaTrack.Core.Features.LlmEnrichment;

namespace VitaTrack.Tests;

[TestClass]
public class HtmlScraperServiceTests
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

    private static HtmlScraperService CreateService(HttpMessageHandler scraperHandler) =>
        new(CreateHttpClientFactory(scraperHandler), NullLogger<HtmlScraperService>.Instance);

    private static IHttpClientFactory CreateHttpClientFactory(HttpMessageHandler scraperHandler)
    {
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("scraper"))
            .Returns(new HttpClient(scraperHandler));
        return factoryMock.Object;
    }

    [TestMethod]
    public async Task FetchCleanHtmlAsync_ReturnsNull_AndNeverSendsRequest_WhenUrlUnsafe()
    {
        var handlerMock = new Mock<HttpMessageHandler>();

        var service = CreateService(handlerMock.Object);

        var result = await service.FetchCleanHtmlAsync("http://127.0.0.1/x");

        Assert.IsNull(result);
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [TestMethod]
    public async Task FetchCleanHtmlAsync_ReturnsNull_WhenStatusIsNotFound()
    {
        var handlerMock = CreateHandlerMock(HttpStatusCode.NotFound, string.Empty);
        var service = CreateService(handlerMock.Object);

        var result = await service.FetchCleanHtmlAsync("https://8.8.8.8/missing");

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task FetchCleanHtmlAsync_ReturnsNull_WhenHandlerThrows()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));
        var service = CreateService(handlerMock.Object);

        var result = await service.FetchCleanHtmlAsync("https://8.8.8.8/page");

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task FetchCleanHtmlAsync_ReturnsMainContent_AndStripsScriptStyleAndNonMainText()
    {
        var html = @"<html><head><title>Shop</title><style>body { color: red; }</style></head>
<body>
<nav>Site navigation</nav>
<script>var tracking = 1;</script>
<div class=""sidebar"">Sidebar promotions</div>
<main>
<h1>Product Facts</h1>
<p>Vitamin C 500 mg per serving</p>
</main>
</body></html>";
        var handlerMock = CreateHandlerMock(HttpStatusCode.OK, html);
        var service = CreateService(handlerMock.Object);

        var result = await service.FetchCleanHtmlAsync("https://8.8.8.8/page");

        Assert.IsNotNull(result);
        StringAssert.Contains(result, "Product Facts");
        StringAssert.Contains(result, "Vitamin C 500 mg per serving");
        Assert.IsFalse(result.Contains("var tracking"), "script content must be stripped");
        Assert.IsFalse(result.Contains("color: red"), "style content must be stripped");
        Assert.IsFalse(result.Contains("Sidebar promotions"), "content outside <main> must be excluded when <main> exists");
        Assert.IsFalse(result.Contains("Site navigation"), "nav content must be stripped");
    }

    [TestMethod]
    public async Task FetchCleanHtmlAsync_FallsBackToArticle_WhenNoMain()
    {
        var html = @"<html><body>
<article><p>Article body: Zinc 15 mg</p></article>
<div>Unrelated body text</div>
</body></html>";
        var handlerMock = CreateHandlerMock(HttpStatusCode.OK, html);
        var service = CreateService(handlerMock.Object);

        var result = await service.FetchCleanHtmlAsync("https://8.8.8.8/article");

        Assert.IsNotNull(result);
        StringAssert.Contains(result, "Article body: Zinc 15 mg");
        Assert.IsFalse(result.Contains("Unrelated body text"), "article fallback must not include other body text");
    }

    [TestMethod]
    public async Task FetchCleanHtmlAsync_TruncatesLongMainText()
    {
        var html = $"<html><body><main>{new string('a', 15000)}</main></body></html>";
        var handlerMock = CreateHandlerMock(HttpStatusCode.OK, html);
        var service = CreateService(handlerMock.Object);

        var result = await service.FetchCleanHtmlAsync("https://8.8.8.8/long");

        Assert.IsNotNull(result);
        Assert.IsTrue(result!.Length > 0, "truncated result should not be empty");
        Assert.IsTrue(result.Length <= 12000, "result must be truncated to 12000 chars");
    }
}
