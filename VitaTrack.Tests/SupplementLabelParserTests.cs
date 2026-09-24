using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VitaTrack.Core.Features.LlmEnrichment;

namespace VitaTrack.Tests;

[TestClass]
public class SupplementLabelParserTests
{
    private const string NutrientJson =
        """{"nutrients":[{"genericName":"Vitamin C","specificForm":"Ascorbic Acid","dosage":"500mg","unit":"mg","amountPerServing":500}],"swapSuggestion":"Consider a buffered form"}""";

    private const string JsonFenced = "```json\n" + NutrientJson + "\n```";
    private const string BareFenced = "```\n" + NutrientJson + "\n```";

    private static SupplementLabelParser CreateParser(string? content, string? error = null)
    {
        var llmClient = new Mock<ILlmClient>();
        llmClient.Setup(c => c.PostChatAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new LlmCompletion(content, error));
        return new SupplementLabelParser(llmClient.Object, NullLogger<SupplementLabelParser>.Instance);
    }

    private static void AssertSingleNutrientParsed(LlmResult result)
    {
        Assert.IsNull(result.ExtractionError);
        Assert.AreEqual(1, result.Nutrients.Count);
        Assert.AreEqual("Vitamin C", result.Nutrients[0].GenericName);
        Assert.AreEqual("Ascorbic Acid", result.Nutrients[0].SpecificForm);
        Assert.AreEqual("500mg", result.Nutrients[0].Dosage);
        Assert.AreEqual("Consider a buffered form", result.SwapSuggestion);
    }

    [TestMethod]
    public async Task ExtractNutrientsAsync_PlainJson_ParsesNutrientsAndSwapSuggestion()
    {
        var parser = CreateParser(NutrientJson);

        var result = await parser.ExtractNutrientsAsync("Vitamin C", "NatureWise", "<html>label</html>");

        AssertSingleNutrientParsed(result);
        Assert.AreEqual("mg", result.Nutrients[0].Unit);
        Assert.AreEqual(500m, result.Nutrients[0].AmountPerServing);
    }

    [TestMethod]
    public async Task ExtractNutrientsAsync_JsonCodeFences_ParsesNutrients()
    {
        var parser = CreateParser(JsonFenced);

        var result = await parser.ExtractNutrientsAsync("Vitamin C", "NatureWise", "<html>label</html>");

        AssertSingleNutrientParsed(result);
    }

    [TestMethod]
    public async Task ExtractNutrientsAsync_BareCodeFences_ParsesNutrients()
    {
        var parser = CreateParser(BareFenced);

        var result = await parser.ExtractNutrientsAsync("Vitamin C", "NatureWise", "<html>label</html>");

        AssertSingleNutrientParsed(result);
    }

    [TestMethod]
    public async Task ExtractNutrientsAsync_LlmError_ReturnsExtractionErrorWithoutNutrients()
    {
        var parser = CreateParser("unused", "rate limit exceeded");

        var result = await parser.ExtractNutrientsAsync("Zinc", "NOW", "<html>label</html>");

        Assert.AreEqual("rate limit exceeded", result.ExtractionError);
        Assert.AreEqual(0, result.Nutrients.Count);
        Assert.IsNull(result.SwapSuggestion);
    }

    [TestMethod]
    public async Task ExtractNutrientsAsync_NullContent_ReturnsEmptyResponseError()
    {
        var parser = CreateParser(null);

        var result = await parser.ExtractNutrientsAsync("Zinc", "NOW", "<html>label</html>");

        Assert.AreEqual("Empty response from LLM", result.ExtractionError);
        Assert.AreEqual(0, result.Nutrients.Count);
    }

    [TestMethod]
    public async Task ExtractNutrientsAsync_MalformedJson_ReturnsExtractionError()
    {
        var parser = CreateParser("this is not json {{{");

        var result = await parser.ExtractNutrientsAsync("Zinc", "NOW", "<html>label</html>");

        Assert.AreEqual("An error occurred while extracting nutrients from the page.", result.ExtractionError);
        Assert.AreEqual(0, result.Nutrients.Count);
        Assert.IsNull(result.SwapSuggestion);
    }

    [TestMethod]
    public async Task ExtractNutrientsAsync_NoNutrientsProperty_ReturnsEmptyListWithoutError()
    {
        var parser = CreateParser("""{"swapSuggestion":"no nutrients listed"}""");

        var result = await parser.ExtractNutrientsAsync("Zinc", "NOW", "<html>label</html>");

        Assert.IsNull(result.ExtractionError);
        Assert.AreEqual(0, result.Nutrients.Count);
        Assert.AreEqual("no nutrients listed", result.SwapSuggestion);
    }

    [TestMethod]
    public async Task ExtractNutrientsAsync_ChildrenArray_ParsesChildDosageFormsAndOptionals()
    {
        var parser = CreateParser("""
            {"nutrients":[{"genericName":"Proprietary Herbal Blend","specificForm":"Blend","dosage":"500mg","children":[
            {"genericName":"Ashwagandha","specificForm":"KSM-66","dosage":"250mg","unit":"mg","amountPerServing":250},
            {"genericName":"Rhodiola","specificForm":"Standardized 3%","dosage":null}
            ]}]}
            """);

        var result = await parser.ExtractNutrientsAsync("Adaptogen Blend", "Gaia", "<html>label</html>");

        Assert.IsNull(result.ExtractionError);
        var children = result.Nutrients.Single().Children;
        Assert.IsNotNull(children);
        Assert.AreEqual(2, children!.Count);
        Assert.AreEqual("Ashwagandha", children[0].GenericName);
        Assert.AreEqual("KSM-66", children[0].SpecificForm);
        Assert.AreEqual("250mg", children[0].Dosage);
        Assert.AreEqual("mg", children[0].Unit);
        Assert.AreEqual(250m, children[0].AmountPerServing);
        Assert.AreEqual("Rhodiola", children[1].GenericName);
        Assert.AreEqual("Standardized 3%", children[1].SpecificForm);
        Assert.AreEqual(string.Empty, children[1].Dosage);
        Assert.IsNull(children[1].Unit);
        Assert.IsNull(children[1].AmountPerServing);
    }
}
