using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VitaTrack.Core.Features.LlmEnrichment;
using VitaTrack.Core.Features.ServiceConnections;
using VitaTrack.Core.Features.Supplements;
using VitaTrack.Tests.TestDoubles;

namespace VitaTrack.Tests;

/// <summary>
/// Which saved connection an enrichment is allowed to run against, and what it is
/// told when there is none. This is the gate that replaced the old "is the API key
/// configured?" check: there is no longer a configured answer to read, only a saved
/// connection that may or may not name a model.
/// </summary>
[TestClass]
public class LlmServiceConnectionTests
{
    private static Supplement WithUrl() => new()
    {
        Name = "Test",
        Brand = "Brand",
        DailyDose = "1 tablet",
        ManufacturerUrl = "https://example.com/product"
    };

    private static ServiceConnection Connection(string? model) => new()
    {
        Service = ServiceDescriptorRegistry.OpenCodeServiceId,
        BaseUrl = "https://svc.example/v1",
        ApiKey = "sk-connection",
        Model = model,
        MaxTokens = 16384,
        Temperature = 0.1
    };

    private static Mock<IServiceConnectionRepository> ConnectionsWith(ServiceConnection? connection)
    {
        var connections = new Mock<IServiceConnectionRepository>();
        connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        return connections;
    }

    /// <summary>A service whose scraper and client are both mocks, so "nothing was
    /// fetched" and "nothing was posted" are settled by the calls themselves rather
    /// than by counting HTTP clients — <see cref="HtmlScraperService"/> builds its
    /// client in its constructor, so a client count says nothing about whether a
    /// request was ever made.</summary>
    private static (LlmService Service, Mock<IHtmlScraperService> Scraper, Mock<ILlmClient> Client)
        UnreachablePath(ServiceConnection? connection)
    {
        var scraper = new Mock<IHtmlScraperService>();
        var client = new Mock<ILlmClient>();
        var service = new LlmService(
            ConnectionsWith(connection).Object,
            scraper.Object,
            new SupplementLabelParser(client.Object, new RecordingLogger<SupplementLabelParser>()),
            new RecordingLogger<LlmService>());
        return (service, scraper, client);
    }

    private static void AssertNothingWasReached(Mock<IHtmlScraperService> scraper, Mock<ILlmClient> client)
    {
        scraper.Verify(s => s.FetchCleanHtmlAsync(It.IsAny<string>()), Times.Never,
            "the page must not be fetched when there is nothing to call with it");
        client.Verify(
            c => c.PostChatAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ServiceConnection>(), It.IsAny<LlmRequestSettings>()),
            Times.Never,
            "no completion may be posted when no model was chosen");
    }

    /// <summary>No connection at all: nothing to send a key to, so the answer names
    /// Settings. A connection with no model is a different state that happens to land
    /// on the same message, so the two are asserted separately — they are two paths to
    /// one word, not one path.</summary>
    [TestMethod]
    public async Task EnrichSupplementAsync_WithNoConnection_PointsAtSettings_AndReachesNothing()
    {
        var (service, scraper, client) = UnreachablePath(null);

        var result = await service.EnrichSupplementAsync(WithUrl());

        Assert.IsNotNull(result.ExtractionError);
        StringAssert.Contains(result.ExtractionError, "Settings", "the error has to say where the model is chosen");
        AssertNothingWasReached(scraper, client);
    }

    [TestMethod]
    public async Task EnrichSupplementAsync_WithNoModelChosen_PointsAtSettings_AndReachesNothing()
    {
        var (service, scraper, client) = UnreachablePath(Connection(model: null));

        var result = await service.EnrichSupplementAsync(WithUrl());

        Assert.IsNotNull(result.ExtractionError);
        StringAssert.Contains(result.ExtractionError, "Settings", "the error has to say where the model is chosen");
        AssertNothingWasReached(scraper, client);
    }

    [TestMethod]
    public async Task EnrichSupplementAsync_WithAWhitespaceModel_PointsAtSettings_AndReachesNothing()
    {
        var (service, scraper, client) = UnreachablePath(Connection(model: "   "));

        var result = await service.EnrichSupplementAsync(WithUrl());

        StringAssert.Contains(result.ExtractionError, "Settings");
        AssertNothingWasReached(scraper, client);
    }

    /// <summary>The connection resolved from the repository is the one the request is
    /// built from: the repository is asked once, and its answer reaches the wire in the
    /// credential, the destination and the model. Without this, a service that resolved
    /// a connection and then let a different one be used would pass every other test
    /// here — they all stop before anything is sent.</summary>
    [TestMethod]
    public async Task EnrichSupplementAsync_PassesTheResolvedConnectionToTheCompletion()
    {
        var completion = new RecordingHandler(
            HttpStatusCode.OK, @"{ ""choices"": [{ ""message"": { ""content"": ""{}"" } }] }");
        var factory = new SequencedHttpClientFactory(
            new RecordingHandler(HttpStatusCode.OK, "<html><body><div>label</div></body></html>"),
            completion);
        var connections = ConnectionsWith(Connection(model: "chosen-model"));
        var service = new LlmService(
            connections.Object,
            new HtmlScraperService(factory, new RecordingLogger<HtmlScraperService>()),
            new SupplementLabelParser(
                new LlmClient(factory, new LlmSessionId(), new RecordingLogger<LlmClient>()),
                new RecordingLogger<SupplementLabelParser>()),
            new RecordingLogger<LlmService>());

        await service.EnrichSupplementAsync(WithUrl());

        Assert.IsNotNull(completion.LastRequest, "the completion was never attempted");
        Assert.AreEqual("Bearer sk-connection",
            completion.LastRequest!.Headers.GetValues("Authorization").Single(),
            "the key that went out is the one on the resolved connection");
        Assert.AreEqual("https://svc.example/v1/v1/chat/completions",
            completion.LastRequest!.RequestUri!.ToString(),
            "the host that was called is the resolved connection's, not a configured default");
        StringAssert.Contains(completion.LastRequestBody!, "chosen-model",
            "the model in the body is the one the connection carries");
        connections.Verify(c => c.GetActiveAsync(It.IsAny<CancellationToken>()), Times.Once,
            "the active connection is resolved once per enrichment, not once per call it fans out to");
    }

    /// <summary>Nothing is fetched when there is no URL to fetch, and that check comes
    /// first: a supplement with no manufacturer page has nothing to ask a model about,
    /// so it must not be reported as a missing connection.</summary>
    [TestMethod]
    public async Task EnrichSupplementAsync_WithNoUrl_IsNotReportedAsAMissingConnection()
    {
        var connections = ConnectionsWith(null);
        var service = new LlmService(
            connections.Object,
            Mock.Of<IHtmlScraperService>(),
            Mock.Of<ISupplementLabelParser>(),
            new RecordingLogger<LlmService>());

        var result = await service.EnrichSupplementAsync(new Supplement
        {
            Name = "Test",
            Brand = "Brand",
            ManufacturerUrl = null
        });

        Assert.IsNull(result.ExtractionError, "no URL is a skip, not a fault");
        connections.Verify(
            c => c.GetActiveAsync(It.IsAny<CancellationToken>()),
            Times.Never,
            "the connection is not needed when there is nothing to enrich");
    }
}
