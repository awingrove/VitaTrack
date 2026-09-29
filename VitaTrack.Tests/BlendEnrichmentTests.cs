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
/// Blend-shaped JSON through the parser: nested children, a child missing its dosage,
/// and the prompt that asks for them. Which connection and settings reach the client
/// is <see cref="SupplementLabelParserConnectionTests"/>' subject.
/// </summary>
[TestClass]
public class BlendEnrichmentTests
{
    private const string BlendJson = @"{
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

    private static Mock<ILlmClient> LlmReturning(string content)
    {
        var llmClient = new Mock<ILlmClient>();
        llmClient.Setup(c => c.PostChatAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ServiceConnection>(), It.IsAny<LlmRequestSettings>()))
            .ReturnsAsync(new LlmCompletion(content, null));
        return llmClient;
    }

    private static SupplementLabelParser Parser(ILlmClient llmClient) =>
        new(llmClient, new RecordingLogger<SupplementLabelParser>());

    private static Task<LlmResult> ParseAsync(ILlmClient llmClient) =>
        Parser(llmClient).ExtractNutrientsAsync(
            "Calm Blend", "Brand", "<html></html>", LlmTestData.Connection(), LlmTestData.Settings());

    [TestMethod]
    public async Task ExtractNutrientsAsync_ParsesNestedBlendChildren()
    {
        var result = await ParseAsync(LlmReturning(BlendJson).Object);

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
    public async Task ExtractNutrientsAsync_ChildWithoutDosageKey_DoesNotThrow()
    {
        var json = @"{
            ""nutrients"": [
                {
                    ""genericName"": ""Proprietary Herbal Blend"",
                    ""specificForm"": """",
                    ""dosage"": ""500mg"",
                    ""children"": [
                        { ""genericName"": ""Ashwagandha"", ""specificForm"": ""KSM-66"" },
                        { ""genericName"": ""Rhodiola"", ""specificForm"": ""Root Extract"", ""dosage"": ""200mg"" }
                    ]
                }
            ],
            ""swapSuggestion"": null
        }";

        var result = await ParseAsync(LlmReturning(json).Object);

        Assert.IsNull(result.ExtractionError);
        Assert.AreEqual(1, result.Nutrients.Count);
        var blend = result.Nutrients[0];
        Assert.IsNotNull(blend.Children);
        Assert.AreEqual(2, blend.Children.Count);
        Assert.AreEqual("Ashwagandha", blend.Children[0].GenericName);
        Assert.AreEqual(string.Empty, blend.Children[0].Dosage);
        Assert.AreEqual("200mg", blend.Children[1].Dosage);
    }

    [TestMethod]
    public async Task ExtractNutrientsAsync_PromptContainsBlendInstructions()
    {
        // Seeded with a value the assertions cannot match, so a client that never calls
        // back fails the same assertion a prompt missing "blend" would — there is no
        // separate "was the callback reached at all" check to keep in step, and so no
        // null-forgiving operator to justify.
        var capturedPrompt = "callback never fired";
        var llmClient = new Mock<ILlmClient>();
        llmClient.Setup(c => c.PostChatAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ServiceConnection>(), It.IsAny<LlmRequestSettings>()))
            .Callback<string, string, ServiceConnection, LlmRequestSettings>(
                (_, userPrompt, _, _) => capturedPrompt = userPrompt)
            .ReturnsAsync(new LlmCompletion("{\"nutrients\":[],\"swapSuggestion\":null}", null));

        await ParseAsync(llmClient.Object);

        Assert.IsTrue(capturedPrompt.Contains("blend"), "extraction prompt should mention blends");
        Assert.IsTrue(capturedPrompt.Contains("children"), "schema should include a children array");
    }

    [TestMethod]
    public async Task EnrichSupplementAsync_NutritionJsonIncludesBlendChildren()
    {
        var json = @"{
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

        var scraper = new Mock<IHtmlScraperService>();
        scraper.Setup(s => s.FetchCleanHtmlAsync(It.IsAny<string>()))
            .ReturnsAsync("<html><body>label</body></html>");

        var service = new LlmService(
            ConnectionsReturning(LlmTestData.Connection()).Object,
            scraper.Object,
            Parser(LlmReturning(json).Object),
            new RecordingLogger<LlmService>());

        var result = await service.EnrichSupplementAsync(new Supplement
        {
            Name = "Calm Blend",
            Brand = "Brand",
            DailyDose = "1 capsule",
            ManufacturerUrl = "https://example.com/product"
        });

        Assert.IsNull(result.ExtractionError);
        Assert.IsNotNull(result.NutritionJson);
        Assert.IsTrue(result.NutritionJson.Contains("Proprietary Herbal Blend"), "blend total should be present");
        Assert.IsTrue(result.NutritionJson.Contains("Proprietary Herbal Blend > Ashwagandha"), "child should be flattened with blend prefix");
        Assert.IsTrue(result.NutritionJson.Contains("Proprietary Herbal Blend > Rhodiola"), "child with empty dosage should still be flattened");
    }

    private static Mock<IServiceConnectionRepository> ConnectionsReturning(ServiceConnection? connection)
    {
        var connections = new Mock<IServiceConnectionRepository>();
        connections.Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        return connections;
    }
}
