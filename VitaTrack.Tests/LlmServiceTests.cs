using System.Net;
using System.Net.Http;
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
/// Enrichment end to end over the real scraper, parser and client: a manufacturer
/// page in, nutrients out. The connection a call runs against comes from
/// <see cref="IServiceConnectionRepository"/>; which answers count as usable is
/// <see cref="LlmServiceConnectionTests"/>' subject.
/// </summary>
[TestClass]
public class LlmServiceTests
{
    private static Mock<IServiceConnectionRepository> ConnectionsWith(ServiceConnection? connection)
    {
        var connections = new Mock<IServiceConnectionRepository>();
        connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        return connections;
    }

    private static LlmService CreateService(IHttpClientFactory factory, ServiceConnection? connection = null)
    {
        var scraper = new HtmlScraperService(factory, new RecordingLogger<HtmlScraperService>());
        var llmClient = new LlmClient(factory, new LlmSessionId(), new RecordingLogger<LlmClient>());
        var parser = new SupplementLabelParser(llmClient, new RecordingLogger<SupplementLabelParser>());
        return new LlmService(
            ConnectionsWith(connection ?? LlmTestData.Connection()).Object,
            scraper,
            parser,
            new RecordingLogger<LlmService>());
    }

    /// <summary>One factory serving both named clients, in the order the enrichment
    /// path asks for them: the page first, the completion second.</summary>
    private static IHttpClientFactory TwoWayFactory(HttpMessageHandler scraper, HttpMessageHandler completion) =>
        new SequencedHttpClientFactory(scraper, completion);

    private static Supplement SupplementWithUrl(string? url = "https://8.8.8.8/product") => new()
    {
        Name = "Test",
        Brand = "Brand",
        DailyDose = "1 tablet",
        ManufacturerUrl = url
    };

    [TestMethod]
    public async Task EnrichSupplementAsync_ReturnsEmpty_WhenNoUrl()
    {
        var service = CreateService(new SequencedHttpClientFactory(new RecordingHandler(HttpStatusCode.OK, "")));

        var result = await service.EnrichSupplementAsync(SupplementWithUrl(url: null));

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Nutrients.Count);
        Assert.IsNull(result.ExtractionError);
    }

    [TestMethod]
    public async Task EnrichSupplementAsync_ReturnsError_WhenUrlFetchFails()
    {
        var service = CreateService(
            new SequencedHttpClientFactory(new RecordingHandler(HttpStatusCode.NotFound, "")));

        var result = await service.EnrichSupplementAsync(SupplementWithUrl(url: "https://8.8.8.8/notfound"));

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Nutrients.Count);
        Assert.IsNotNull(result.ExtractionError);
    }

    [TestMethod]
    public async Task EnrichSupplementAsync_ExtractsNutrients_WhenApiReturnsValidResponse()
    {
        // Arrange: scraper returns HTML with product info
        var htmlContent = @"<html><body><div class=""product-info"">
                <h1>Super Multivitamin</h1>
                <table>
                    <tr><td>Vitamin C</td><td>Ascorbic Acid</td><td>500mg</td></tr>
                    <tr><td>Zinc</td><td>Zinc Picolinate</td><td>15mg</td></tr>
                    <tr><td>Vitamin D</td><td>Cholecalciferol</td><td>1000IU</td></tr>
                </table>
            </div></body></html>";

        var apiResponse = @"{
                ""choices"": [{
                    ""message"": {
                        ""content"": ""{\""nutrients\"": [{\""genericName\"": \""Vitamin C\"", \""specificForm\"": \""Ascorbic Acid\"", \""dosage\"": \""500mg\"", \""unit\"": \""mg\"", \""amountPerServing\"": 500}, {\""genericName\"": \""Zinc\"", \""specificForm\"": \""Zinc Picolinate\"", \""dosage\"": \""15mg\"", \""unit\"": \""mg\"", \""amountPerServing\"": 15}, {\""genericName\"": \""Vitamin D\"", \""specificForm\"": \""Cholecalciferol\"", \""dosage\"": \""1000IU\"", \""unit\"": \""IU\"", \""amountPerServing\"": 1000}], \""swapSuggestion\"": \""Try sublingual Vitamin D for better absorption\""}""
                    }
                }]
            }";

        var service = CreateService(TwoWayFactory(
            new RecordingHandler(HttpStatusCode.OK, htmlContent),
            new RecordingHandler(HttpStatusCode.OK, apiResponse)));

        var supplement = new Supplement
        {
            Name = "Super Multivitamin",
            Brand = "TestBrand",
            DailyDose = "1 tablet",
            ManufacturerUrl = "https://8.8.8.8/product"
        };

        // Act
        var result = await service.EnrichSupplementAsync(supplement);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNull(result.ExtractionError, $"Unexpected error: {result.ExtractionError}");
        Assert.AreEqual(3, result.Nutrients.Count, "Should extract 3 nutrients");

        Assert.AreEqual("Vitamin C", result.Nutrients[0].GenericName);
        Assert.AreEqual("Ascorbic Acid", result.Nutrients[0].SpecificForm);
        Assert.AreEqual("500mg", result.Nutrients[0].Dosage);

        Assert.AreEqual("Zinc", result.Nutrients[1].GenericName);
        Assert.AreEqual("Zinc Picolinate", result.Nutrients[1].SpecificForm);
        Assert.AreEqual("15mg", result.Nutrients[1].Dosage);

        Assert.AreEqual("Vitamin D", result.Nutrients[2].GenericName);
        Assert.AreEqual("Cholecalciferol", result.Nutrients[2].SpecificForm);
        Assert.AreEqual("1000IU", result.Nutrients[2].Dosage);

        // Legacy NutritionJson should also be populated
        Assert.IsNotNull(result.NutritionJson);
        Assert.IsTrue(result.NutritionJson.Contains("vitamin_c") || result.NutritionJson.Contains("Vitamin C"));

        // Swap suggestion should be set
        Assert.IsNotNull(result.SwapSuggestion);
        Assert.IsTrue(result.SwapSuggestion.Contains("sublingual"));
    }

    [TestMethod]
    public async Task EnrichSupplementAsync_ReturnsError_WhenApiResponseHasMalformedJson()
    {
        // Arrange: scraper returns HTML, API returns invalid JSON in content field
        var htmlContent = @"<html><body><div class=""product-info"">Vitamin C 500mg</div></body></html>";

        // The API response is valid JSON, but the content field contains plain text, not JSON
        var apiResponse = @"{
                ""choices"": [{
                    ""message"": {
                        ""content"": ""This is not JSON at all""
                    }
                }]
            }";

        var service = CreateService(TwoWayFactory(
            new RecordingHandler(HttpStatusCode.OK, htmlContent),
            new RecordingHandler(HttpStatusCode.OK, apiResponse)));

        // Act
        var result = await service.EnrichSupplementAsync(SupplementWithUrl());

        // Assert - should still have error from JSON parsing
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Nutrients.Count);
        Assert.IsNotNull(result.ExtractionError);
    }

    [TestMethod]
    public async Task EnrichSupplementAsync_ReturnsError_WhenHtmlHasNoContent()
    {
        // Arrange: scraper returns HTML that gets cleaned to nothing
        var htmlContent = @"<html><head><title>Loading...</title></head><body><script>redirect();</script><style>.hidden{display:none}</style></body></html>";

        var service = CreateService(new SequencedHttpClientFactory(new RecordingHandler(HttpStatusCode.OK, htmlContent)));

        // Act
        var result = await service.EnrichSupplementAsync(SupplementWithUrl(url: "https://8.8.8.8/empty-page"));

        // Assert - the connection is usable, but there is no content on the page
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Nutrients.Count);
        Assert.IsNotNull(result.ExtractionError);
        Assert.IsTrue(result.ExtractionError.Contains("No content found"));
    }

    [TestMethod]
    public async Task EnrichSupplementAsync_ReturnsError_WhenScraperThrows()
    {
        var scraper = new Mock<IHtmlScraperService>();
        scraper.Setup(s => s.FetchCleanHtmlAsync(It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("network down"));
        var service = new LlmService(
            ConnectionsWith(LlmTestData.Connection()).Object,
            scraper.Object,
            Mock.Of<ISupplementLabelParser>(),
            new RecordingLogger<LlmService>());

        var result = await service.EnrichSupplementAsync(new Supplement
        {
            Name = "Zinc",
            Brand = "NOW",
            ManufacturerUrl = "https://example.com/product"
        });

        Assert.AreEqual("An error occurred while processing the supplement page.", result.ExtractionError);
        Assert.AreEqual(0, result.Nutrients.Count);
        Assert.IsTrue(string.IsNullOrEmpty(result.NutritionJson));
    }
}
