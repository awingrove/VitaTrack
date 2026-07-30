using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Moq.Protected;
using VitaTrack.Infrastructure.Models;
using VitaTrack.Infrastructure.Services;

namespace VitaTrack.Tests
{
    [TestClass]
    public class OpenRouterLlmServiceTests
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

        private static IHttpClientFactory CreateHttpClientFactory(
            HttpMessageHandler? scraperHandler = null,
            HttpMessageHandler? openRouterHandler = null)
        {
            var factoryMock = new Mock<IHttpClientFactory>();

            if (scraperHandler != null)
            {
                var client = new HttpClient(scraperHandler);
                factoryMock.Setup(f => f.CreateClient("scraper")).Returns(client);
            }

            if (openRouterHandler != null)
            {
                var client = new HttpClient(openRouterHandler);
                client.BaseAddress = new System.Uri("https://dummy.openrouter.ai/api/v1");
                factoryMock.Setup(f => f.CreateClient("openrouter")).Returns(client);
            }

            return factoryMock.Object;
        }

        private static IConfiguration CreateConfig(string? apiKey = "test-real-api-key")
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(
                [
                    new KeyValuePair<string,string?>("OpenRouter:BaseUrl", "https://dummy.openrouter.ai/api/v1"),
                    new KeyValuePair<string,string?>("OpenRouter:ApiKey", apiKey)
                ])
                .Build();
        }

        [TestMethod]
        public async Task EnrichSupplementAsync_ReturnsEmpty_WhenNoUrl()
        {
            var factory = CreateHttpClientFactory();
            var config = CreateConfig();
            var logger = new NullLogger<OpenRouterLlmService>();
            var service = new OpenRouterLlmService(factory, config, logger);

            var supplement = new Supplement
            {
                Name = "Test",
                Brand = "Brand",
                DailyDose = "1 tablet",
                ManufacturerUrl = null
            };

            var result = await service.EnrichSupplementAsync(supplement);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Nutrients.Count);
            Assert.IsNull(result.ExtractionError);
        }

        [TestMethod]
        public async Task EnrichSupplementAsync_ReturnsEmpty_WhenApiKeyMissing()
        {
            var factory = CreateHttpClientFactory();
            var config = CreateConfig(null);
            var logger = new NullLogger<OpenRouterLlmService>();
            var service = new OpenRouterLlmService(factory, config, logger);

            var supplement = new Supplement
            {
                Name = "Test",
                Brand = "Brand",
                DailyDose = "1 tablet",
                ManufacturerUrl = "https://example.com/product"
            };

            var result = await service.EnrichSupplementAsync(supplement);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Nutrients.Count);
            Assert.IsNotNull(result.ExtractionError);
            Assert.IsTrue(result.ExtractionError.Contains("API key not configured"));
        }

        [TestMethod]
        public async Task EnrichSupplementAsync_ReturnsError_WhenUrlFetchFails()
        {
            var scraperHandlerMock = CreateHandlerMock(HttpStatusCode.NotFound, "");
            var factory = CreateHttpClientFactory(scraperHandlerMock.Object);
            var config = CreateConfig();
            var logger = new NullLogger<OpenRouterLlmService>();
            var service = new OpenRouterLlmService(factory, config, logger);

            var supplement = new Supplement
            {
                Name = "Test",
                Brand = "Brand",
                DailyDose = "1 tablet",
                ManufacturerUrl = "https://example.com/notfound"
            };

            var result = await service.EnrichSupplementAsync(supplement);

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

            var scraperMock = CreateHandlerMock(HttpStatusCode.OK, htmlContent);
            var apiMock = CreateHandlerMock(HttpStatusCode.OK, apiResponse);
            var factory = CreateHttpClientFactory(scraperMock.Object, apiMock.Object);
            var config = CreateConfig();
            var logger = new NullLogger<OpenRouterLlmService>();
            var service = new OpenRouterLlmService(factory, config, logger);

            var supplement = new Supplement
            {
                Name = "Super Multivitamin",
                Brand = "TestBrand",
                DailyDose = "1 tablet",
                ManufacturerUrl = "https://example.com/product"
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

            var scraperMock = CreateHandlerMock(HttpStatusCode.OK, htmlContent);
            var apiMock = CreateHandlerMock(HttpStatusCode.OK, apiResponse);
            var factory = CreateHttpClientFactory(scraperMock.Object, apiMock.Object);
            var config = CreateConfig();
            var logger = new NullLogger<OpenRouterLlmService>();
            var service = new OpenRouterLlmService(factory, config, logger);

            var supplement = new Supplement
            {
                Name = "Test",
                Brand = "Brand",
                DailyDose = "1 tablet",
                ManufacturerUrl = "https://example.com/product"
            };

            // Act
            var result = await service.EnrichSupplementAsync(supplement);

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

            var scraperMock = CreateHandlerMock(HttpStatusCode.OK, htmlContent);
            var factory = CreateHttpClientFactory(scraperMock.Object);
            var config = CreateConfig();
            var logger = new NullLogger<OpenRouterLlmService>();
            var service = new OpenRouterLlmService(factory, config, logger);

            var supplement = new Supplement
            {
                Name = "Test",
                Brand = "Brand",
                DailyDose = "1 tablet",
                ManufacturerUrl = "https://example.com/empty-page"
            };

            // Act
            var result = await service.EnrichSupplementAsync(supplement);

            // Assert - API key is valid, but no content on page
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Nutrients.Count);
            Assert.IsNotNull(result.ExtractionError);
            Assert.IsTrue(result.ExtractionError.Contains("No content found"));
        }
    }
}