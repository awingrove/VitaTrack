using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core.Features.LlmEnrichment;
using VitaTrack.Tests.TestDoubles;

namespace VitaTrack.Tests;

/// <summary>
/// What a good answer turns into: the content, and no error. Every way of not getting
/// one — a refused status, empty choices, empty content, a request that dies — is
/// asserted in <see cref="LlmClientRequestTests"/> against its exact error string,
/// which is where those cases live now; this class is what is left of the original
/// pair, and the header assertions are <see cref="LlmClientHeaderTests"/>' subject.
/// </summary>
[TestClass]
public class LlmClientTests
{
    private const string ReplyBody = @"{ ""choices"": [{ ""message"": { ""content"": ""hello"" } }] }";

    [TestMethod]
    public async Task PostChatAsync_ReturnsContent_OnSuccess()
    {
        var client = new LlmClient(
            new SequencedHttpClientFactory(new RecordingHandler(HttpStatusCode.OK, ReplyBody)),
            new LlmSessionId(),
            new RecordingLogger<LlmClient>());

        var result = await client.PostChatAsync(
            "system prompt", "user prompt", LlmTestData.Connection(), LlmTestData.Settings());

        Assert.AreEqual("hello", result.Content);
        Assert.IsNull(result.Error);
    }
}
