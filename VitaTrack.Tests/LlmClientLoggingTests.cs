using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core.Features.LlmEnrichment;
using VitaTrack.Core.Features.ServiceConnections;
using VitaTrack.Tests.TestDoubles;

namespace VitaTrack.Tests;

/// <summary>
/// What the completion client is allowed to write down. The request it builds carries
/// the user's API key and goes to the base URL they typed, and a provider's answer to
/// a rejected key routinely quotes that key back — so every line below is a place a
/// secret could leave the process, and each test asserts both halves: the thing the
/// line <em>is</em> for is in it, and the three things that must never be in it are
/// not.
/// <para>
/// The rule is <see cref="ServiceCatalogClient"/>'s, verbatim: log the failure's
/// <em>category</em>, never the payload, and never pass the exception object — the
/// logger renders its message, and an <see cref="HttpRequestException"/> from a
/// transport failure carries the base URL's host. The direct analogue is
/// <c>ServiceCatalogClientFailureTests.WhenTheProbeFails_LogsTheCategoryAndNothing
/// Identifying</c>.
/// </para>
/// </summary>
[TestClass]
public class LlmClientLoggingTests
{
    private const string ProviderKey = "sk-connection-under-test";
    private const string ProviderHost = "gateway.internal.example";

    /// <summary>A 401 whose body quotes the rejected credential back, which is what
    /// OpenAI-compatible providers do and what the old <c>{Content}</c> argument
    /// wrote to the log verbatim. JSON, so the failure is not distinguishable from a
    /// real answer by shape alone.</summary>
    private const string RejectionBody =
        @"{ ""error"": { ""message"": ""Incorrect API key provided: sk-connection-under-test"", ""type"": ""invalid_request_error"" } }";

    private static (LlmClient Client, RecordingLogger<LlmClient> Logger) ClientFor(HttpMessageHandler handler)
    {
        var logger = new RecordingLogger<LlmClient>();
        return (new LlmClient(new SequencedHttpClientFactory(handler), new LlmSessionId(), logger), logger);
    }

    private static ServiceConnection RejectedConnection() =>
        LlmTestData.Connection(baseUrl: $"https://{ProviderHost}/v1", apiKey: ProviderKey);

    /// <summary>Everything a line would expose about the connection, in one place, so
    /// neither test below can forget one of the three. The exception check is what
    /// settles the second case: <see cref="RecordingLogger"/> formats the message
    /// template and stores the exception separately, exactly as a sink would receive
    /// it — so a line that passed the object is caught here rather than by a substring
    /// match the double never renders.</summary>
    private static void AssertNothingIdentifyingReached(RecordingLogger<LlmClient> logger, LogLevel level)
    {
        var text = logger.TextAt(level);
        Assert.IsFalse(text.Contains(ProviderKey, StringComparison.OrdinalIgnoreCase),
            $"the API key must never reach the log: {text}");
        Assert.IsFalse(text.Contains(ProviderHost, StringComparison.OrdinalIgnoreCase),
            $"the base URL must never reach the log: {text}");
        Assert.IsFalse(text.Contains("Incorrect API key provided", StringComparison.OrdinalIgnoreCase),
            $"the provider's error message must never reach the log: {text}");
        Assert.AreEqual(0, logger.Entries.Count(e => e.Exception is not null),
            "passing the exception to the logger would render its message; only its type name is safe");
    }

    /// <summary>A non-2xx is a real failure the user is told about, so the line stays
    /// at <see cref="LogLevel.Warning"/> — the catalog probe's <c>Debug</c> is for an
    /// unverified probe, which is a state rather than a failure. What the line may
    /// carry is the status and nothing else: the body is not an argument, and the read
    /// that used to buffer it now sits below the status check, so the provider's
    /// wording never enters the process at all. The status is the part worth keeping —
    /// it is what tells a rejected key from a broken endpoint.
    /// <para>
    /// This failed before the fix: the old line rendered the body, so the text
    /// contained the key the provider had just echoed back.
    /// </para></summary>
    [TestMethod]
    public async Task OnANonSuccessStatus_LogsTheStatusAndNotTheBody()
    {
        var (client, logger) = ClientFor(new RecordingHandler(HttpStatusCode.Unauthorized, RejectionBody));

        await client.PostChatAsync("s", "u", RejectedConnection(), LlmTestData.Settings());

        var text = logger.TextAt(LogLevel.Warning);
        StringAssert.Contains(text, "401",
            "the status is the whole payload: it is what tells a rejected key from a broken endpoint");
        AssertNothingIdentifyingReached(logger, LogLevel.Warning);
    }

    /// <summary>The catch path, which is the transport failure the refusal line never
    /// sees: DNS, refused connection, timeout. <c>LogError(ex, …)</c> would render
    /// <see cref="HttpRequestException"/>'s message, and that message is where .NET
    /// puts the host it failed to reach — the base URL the user typed. The level stays
    /// <see cref="LogLevel.Error"/>; the defect was the payload, not the severity.
    /// </summary>
    [TestMethod]
    public async Task WhenTheRequestFails_LogsTheCategoryAndNothingIdentifying()
    {
        var (client, logger) = ClientFor(
            new ThrowingHandler(new HttpRequestException($"no such host is known: {ProviderHost}")));

        await client.PostChatAsync("s", "u", RejectedConnection(), LlmTestData.Settings());

        StringAssert.Contains(logger.TextAt(LogLevel.Error), nameof(HttpRequestException),
            "the category is the whole payload: it is what tells a DNS failure from a refused connection");
        AssertNothingIdentifyingReached(logger, LogLevel.Error);
    }
}
