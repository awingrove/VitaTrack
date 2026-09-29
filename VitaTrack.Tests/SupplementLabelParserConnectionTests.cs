using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VitaTrack.Core.Features.LlmEnrichment;
using VitaTrack.Core.Features.ServiceConnections;
using VitaTrack.Tests.TestDoubles;

namespace VitaTrack.Tests;

/// <summary>
/// The parser is the middle of the seam: it builds prompts and parses JSON, and
/// everything between it and the client is a pass-through. These tests exist because
/// a pass-through is exactly the kind of code that compiles, keeps compiling, and
/// quietly drops an argument — and a dropped connection is a completion posted to
/// nowhere, or to the wrong service.
/// </summary>
[TestClass]
public class SupplementLabelParserConnectionTests
{
    private const string AnyJson = @"{""nutrients"":[],""swapSuggestion"":null}";

    private static (Mock<ILlmClient> Client, SupplementLabelParser Parser) ParserReturning(string json)
    {
        var llmClient = new Mock<ILlmClient>();
        llmClient.Setup(c => c.PostChatAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ServiceConnection>(), It.IsAny<LlmRequestSettings>()))
            .ReturnsAsync(new LlmCompletion(json, null));
        return (llmClient, new SupplementLabelParser(llmClient.Object, new RecordingLogger<SupplementLabelParser>()));
    }

    [TestMethod]
    public async Task ExtractNutrientsAsync_PassesTheConnectionThroughUnchanged()
    {
        var (client, parser) = ParserReturning(AnyJson);
        var connection = LlmTestData.Connection(baseUrl: "https://chosen.example/v1", apiKey: "sk-chosen");

        await parser.ExtractNutrientsAsync(
            "Zinc", "NOW", "<html>label</html>", connection, LlmTestData.Settings());

        var sent = client.Invocations.Single().Arguments[2] as ServiceConnection;
        Assert.IsNotNull(sent, "the parser must hand the connection to the client, not null");
        Assert.AreSame(connection, sent, "the parser does not rebuild the connection; it forwards the one it was given");
    }

    [TestMethod]
    public async Task ExtractNutrientsAsync_PassesTheSettingsThroughUnchanged()
    {
        var (client, parser) = ParserReturning(AnyJson);
        var settings = LlmTestData.Settings(model: "model-x", variant: "high", maxTokens: 999, temperature: 0.25);

        await parser.ExtractNutrientsAsync(
            "Zinc", "NOW", "<html>label</html>", LlmTestData.Connection(), settings);

        var sent = client.Invocations.Single().Arguments[3] as LlmRequestSettings;
        Assert.IsNotNull(sent, "the parser must hand the settings to the client, not null");
        Assert.AreSame(settings, sent, "the parser does not re-derive the settings; it forwards the ones it was given");
    }

    /// <summary>One call, one completion. A parser that retried or double-posted would
    /// cost the user twice per supplement, and the count is the only thing that
    /// settles it.</summary>
    [TestMethod]
    public async Task ExtractNutrientsAsync_CallsTheClientOnce()
    {
        var (client, parser) = ParserReturning(AnyJson);

        await parser.ExtractNutrientsAsync(
            "Zinc", "NOW", "<html>label</html>", LlmTestData.Connection(), LlmTestData.Settings());

        client.Verify(
            c => c.PostChatAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ServiceConnection>(), It.IsAny<LlmRequestSettings>()),
            Times.Once);
    }
}
