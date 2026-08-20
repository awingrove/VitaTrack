using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VitaTrack.Infrastructure;
using VitaTrack.Infrastructure.Models;
using VitaTrack.Infrastructure.Services;

namespace VitaTrack.Tests;

[TestClass]
public class BlendEnrichmentTests
{
    private static IOptions<VitaTrackOptions> CreateOptions()
    {
        return Options.Create(new VitaTrackOptions
        {
            BaseUrl = "https://dummy.example.com/v1",
            ApiKey = "test-real-api-key",
            Model = "test-model",
            MaxTokens = 16384,
            Temperature = 0.1
        });
    }

    private static LlmService CreateServiceWithMockLlm(LlmCompletion completion)
    {
        var llmClientMock = new Mock<ILlmClient>();
        llmClientMock
            .Setup(c => c.PostChatAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(completion);
        var parser = new SupplementLabelParser(llmClientMock.Object, NullLogger<SupplementLabelParser>.Instance);
        var scraperMock = new Mock<IHtmlScraperService>();
        scraperMock
            .Setup(s => s.FetchCleanHtmlAsync(It.IsAny<string>()))
            .ReturnsAsync("<html><body>label</body></html>");
        var options = CreateOptions();
        return new LlmService(options, scraperMock.Object, parser, NullLogger<LlmService>.Instance);
    }

    [TestMethod]
    public async Task ExtractNutrientsAsync_ParsesNestedBlendChildren()
    {
        var blendJson = @"{
            ""nutrients"": [
                {
                    ""genericName"": ""Proprietary Herbal Blend"",
                    ""specificForm"": """",
                    ""dosage"": ""500mg"",
                    ""unit"": ""mg"",
                    ""amountPerServing"": 500,
                    ""children"": [
                        { ""genericName"": ""Ashwagandha"", ""specificForm"": ""KSM-66"", ""dosage"": ""300mg"" },
                        { ""genericName"": ""Rhodiola"", ""specificForm"": ""Root Extract"", ""dosage"": ""200mg"" }
                    ]
                }
            ],
            ""swapSuggestion"": null
        }";

        var llmClientMock = new Mock<ILlmClient>();
        llmClientMock
            .Setup(c => c.PostChatAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new LlmCompletion(blendJson, null));
        var parser = new SupplementLabelParser(llmClientMock.Object, NullLogger<SupplementLabelParser>.Instance);

        var result = await parser.ExtractNutrientsAsync("Calm Blend", "Brand", "<html></html>");

        Assert.IsNull(result.ExtractionError);
        Assert.AreEqual(1, result.Nutrients.Count);
        var blend = result.Nutrients[0];
        Assert.AreEqual("Proprietary Herbal Blend", blend.GenericName);
        Assert.IsNotNull(blend.Children);
        Assert.AreEqual(2, blend.Children.Count);
        Assert.AreEqual("Ashwagandha", blend.Children[0].GenericName);
        Assert.AreEqual("KSM-66", blend.Children[0].SpecificForm);
        Assert.AreEqual("300mg", blend.Children[0].Dosage);
        Assert.AreEqual("Rhodiola", blend.Children[1].GenericName);
        Assert.AreEqual("Root Extract", blend.Children[1].SpecificForm);
    }

    [TestMethod]
    public async Task ExtractNutrientsAsync_PromptContainsBlendInstructions()
    {
        string? capturedPrompt = null;
        var llmClientMock = new Mock<ILlmClient>();
        llmClientMock
            .Setup(c => c.PostChatAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((_, userPrompt) => capturedPrompt = userPrompt)
            .ReturnsAsync(new LlmCompletion("{\"nutrients\":[],\"swapSuggestion\":null}", null));
        var parser = new SupplementLabelParser(llmClientMock.Object, NullLogger<SupplementLabelParser>.Instance);

        await parser.ExtractNutrientsAsync("X", "Y", "<html></html>");

        Assert.IsNotNull(capturedPrompt);
        Assert.IsTrue(capturedPrompt.Contains("blend"), "extraction prompt should mention blends");
        Assert.IsTrue(capturedPrompt.Contains("children"), "schema should include a children array");
    }

    [TestMethod]
    public async Task EnrichSupplementAsync_NutritionJsonIncludesBlendChildren()
    {
        var blendJson = @"{
            ""nutrients"": [
                {
                    ""genericName"": ""Proprietary Herbal Blend"",
                    ""specificForm"": """",
                    ""dosage"": ""500mg"",
                    ""unit"": ""mg"",
                    ""amountPerServing"": 500,
                    ""children"": [
                        { ""genericName"": ""Ashwagandha"", ""specificForm"": ""KSM-66"", ""dosage"": ""300mg"" },
                        { ""genericName"": ""Rhodiola"", ""specificForm"": ""Root Extract"", ""dosage"": """" }
                    ]
                }
            ],
            ""swapSuggestion"": null
        }";

        var service = CreateServiceWithMockLlm(new LlmCompletion(blendJson, null));

        var supplement = new Supplement
        {
            Name = "Calm Blend",
            Brand = "Brand",
            DailyDose = "1 capsule",
            ManufacturerUrl = "https://example.com/product"
        };

        var result = await service.EnrichSupplementAsync(supplement);

        Assert.IsNull(result.ExtractionError);
        Assert.IsNotNull(result.NutritionJson);
        Assert.IsTrue(result.NutritionJson.Contains("Proprietary Herbal Blend"), "blend total should be present");
        Assert.IsTrue(result.NutritionJson.Contains("Proprietary Herbal Blend > Ashwagandha"), "child should be flattened with blend prefix");
        Assert.IsTrue(result.NutritionJson.Contains("Proprietary Herbal Blend > Rhodiola"), "child with empty dosage should still be flattened");
    }
}
