using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VitaTrack.Infrastructure.Models;
using VitaTrack.Infrastructure.Services;

namespace VitaTrack.Tests;

[TestClass]
public class SupplementLabelParserTests
{
    private static ILlmClient MockClient(LlmCompletion completion)
    {
        var mock = new Mock<ILlmClient>();
        mock.Setup(c => c.PostChatAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(completion);
        return mock.Object;
    }

    [TestMethod]
    public async Task ExtractNutrientsAsync_ReturnsError_WhenLlmReportsError()
    {
        var parser = new SupplementLabelParser(MockClient(new LlmCompletion(null, "boom")), NullLogger<SupplementLabelParser>.Instance);

        var result = await parser.ExtractNutrientsAsync("Vit C", "Brand", "<html/>");

        Assert.IsNotNull(result.Nutrients);
        Assert.AreEqual(0, result.Nutrients.Count);
        Assert.AreEqual("boom", result.ExtractionError);
    }

    [TestMethod]
    public async Task ExtractNutrientsAsync_ReturnsError_WhenContentIsNull()
    {
        var parser = new SupplementLabelParser(MockClient(new LlmCompletion(null, null)), NullLogger<SupplementLabelParser>.Instance);

        var result = await parser.ExtractNutrientsAsync("Vit C", "Brand", "<html/>");

        Assert.IsNotNull(result.ExtractionError);
        Assert.IsTrue(result.ExtractionError!.Contains("Empty response"));
    }

    [TestMethod]
    public async Task ExtractNutrientsAsync_ReturnsEmpty_WhenNoNutrientsProperty()
    {
        var parser = new SupplementLabelParser(
            MockClient(new LlmCompletion(@"{ ""swapSuggestion"": ""try X"" }", null)),
            NullLogger<SupplementLabelParser>.Instance);

        var result = await parser.ExtractNutrientsAsync("Vit C", "Brand", "<html/>");

        Assert.IsNotNull(result.Nutrients);
        Assert.AreEqual(0, result.Nutrients.Count);
        Assert.AreEqual("try X", result.SwapSuggestion);
    }

    [TestMethod]
    public async Task ExtractNutrientsAsync_StripsCodeFences()
    {
        var content = "```json\n" + @"{ ""nutrients"": [{ ""genericName"": ""Zinc"", ""specificForm"": ""Picolinate"", ""dosage"": ""15mg"" }] }" + "\n```";
        var parser = new SupplementLabelParser(
            MockClient(new LlmCompletion(content, null)),
            NullLogger<SupplementLabelParser>.Instance);

        var result = await parser.ExtractNutrientsAsync("Vit C", "Brand", "<html/>");

        Assert.IsNull(result.ExtractionError, result.ExtractionError);
        Assert.AreEqual(1, result.Nutrients.Count);
        Assert.AreEqual("Zinc", result.Nutrients[0].GenericName);
    }

    [TestMethod]
    public async Task ExtractNutrientsAsync_ReturnsError_WhenContentIsInvalidJson()
    {
        var parser = new SupplementLabelParser(
            MockClient(new LlmCompletion("this is not json", null)),
            NullLogger<SupplementLabelParser>.Instance);

        var result = await parser.ExtractNutrientsAsync("Vit C", "Brand", "<html/>");

        Assert.IsNotNull(result.ExtractionError);
    }
}
