using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core.Features.ServiceConnections;
using VitaTrack.Tests.TestDoubles;

namespace VitaTrack.Tests.Features.ServiceConnections;

/// <summary>
/// Every way a catalog probe can fail, in one class, because they share one contract:
/// a failure is a <see cref="ModelCatalog.Unverified"/> answer and never an escaping
/// exception. The tests assert the unverified result rather than wrapping the call in a
/// <c>try</c>/<c>Assert.Fail</c> — an exception that escaped would surface as a test
/// <em>error</em> with its stack trace, and one that was swallowed would return null and
/// fail the <c>Verified</c> assertion, so the assertion is the proof either way.
/// <para>
/// The one exception to "never escapes" is the caller's own cancellation, and the timeout
/// beside it: a probe the caller abandoned is not a result about the connection, while a
/// timeout is. Same exception type, opposite answers, decided by the token — which is
/// why both are pinned here.
/// </para>
/// <para>
/// The shared doubles live in <c>VitaTrack.Tests.TestDoubles</c>.
/// </para>
/// </summary>
[TestClass]
public class ServiceCatalogClientFailureTests
{
    private const string CatalogBody = @"{ ""object"": ""list"", ""data"": [{ ""id"": ""m1"" }] }";

    private static ServiceConnection Connection(string baseUrl = "http://h", string service = "opencode") =>
        new() { BaseUrl = baseUrl, ApiKey = "sk-test", Service = service };

    private static ServiceCatalogClient Client(HttpMessageHandler handler, ILogger<ServiceCatalogClient>? logger = null) =>
        new(new SequencedHttpClientFactory(handler), logger ?? new RecordingLogger<ServiceCatalogClient>());

    private static async Task AssertUnverified(ModelCatalog catalog, string because)
    {
        Assert.IsFalse(catalog.Verified, because);
        Assert.AreEqual(0, catalog.Models.Count, "nothing was listed, so nothing is offered");
    }

    [TestMethod]
    public async Task OnARejectedKey_ReturnsUnverified()
    {
        var client = Client(new RecordingHandler(HttpStatusCode.Unauthorized, @"{ ""error"": ""bad key"" }"));

        await AssertUnverified(await client.ListModelsAsync(Connection()), "a rejected key is not a verified connection");
    }

    [TestMethod]
    public async Task OnAnEndpointWithoutModelListing_ReturnsUnverified()
    {
        // 404 is not a bad key: several OpenAI-compatible endpoints do not implement
        // model listing at all, and the connection is still usable for completions.
        var client = Client(new RecordingHandler(HttpStatusCode.NotFound, "not found"));

        await AssertUnverified(await client.ListModelsAsync(Connection()),
            "an endpoint without /v1/models is unverified, not broken");
    }

    [TestMethod]
    public async Task OnAServerError_ReturnsUnverified()
    {
        var client = Client(new RecordingHandler(HttpStatusCode.InternalServerError, "boom"));

        await AssertUnverified(await client.ListModelsAsync(Connection()), "a server error is not verification");
    }

    [TestMethod]
    public async Task OnAMalformedBody_ReturnsUnverified()
    {
        var client = Client(new RecordingHandler(HttpStatusCode.OK, "{ not json"));

        await AssertUnverified(await client.ListModelsAsync(Connection()),
            "a body that cannot be parsed proves nothing about the connection");
    }

    [TestMethod]
    public async Task OnA200WithNoDataArray_ReturnsUnverified()
    {
        // Parsed, but there is no catalog in it. Reporting this as a verified connection
        // with an empty dropdown would leave the user with a picker and nothing to pick.
        var client = Client(new RecordingHandler(HttpStatusCode.OK, @"{ ""object"": ""list"" }"));

        await AssertUnverified(await client.ListModelsAsync(Connection()),
            "a listing with no catalog in it is not a verified connection");
    }

    /// <summary>The same verdict for the case the status code alone would get wrong. A
    /// 200 that carries an empty array is a service that answered and offered nothing,
    /// and the two halves of the answer are read by different parts of the UI:
    /// <c>Models.Count</c> picks the free-text field, <c>Verified</c> picks the badge and
    /// whether the unverified note appears. Reporting this as verified would put a
    /// "verified" pill above a model field with nothing in it and no explanation — the
    /// one-signal rule and the badge disagreeing, which is what this pins shut.</summary>
    [TestMethod]
    public async Task OnA200WithAnEmptyCatalog_ReturnsUnverified()
    {
        var client = Client(new RecordingHandler(HttpStatusCode.OK, @"{ ""object"": ""list"", ""data"": [] }"));

        await AssertUnverified(await client.ListModelsAsync(Connection()),
            "a catalog that names no model is not a catalog, whatever the status code said");
    }

    /// <summary>And the same verdict when the array is present but every entry is
    /// unusable — which is the other way to arrive at no ids, and the reason the check is
    /// on the parsed list rather than on the shape of the body.</summary>
    [TestMethod]
    public async Task OnA200WhoseEntriesAreAllUnusable_ReturnsUnverified()
    {
        var client = Client(new RecordingHandler(HttpStatusCode.OK, @"{ ""data"": [ ""junk"", { ""name"": ""no-id"" } ] }"));

        await AssertUnverified(await client.ListModelsAsync(Connection()),
            "no entry yielded an id, so there is still nothing to offer");
    }

    [TestMethod]
    public async Task WhenTheRequestFails_ReturnsUnverified()
    {
        var client = Client(new ThrowingHandler(new HttpRequestException("connection refused")));

        await AssertUnverified(await client.ListModelsAsync(Connection()),
            "a network failure is a state the user can act on, not an exception");
    }

    [TestMethod]
    public async Task OnAnUnusableBaseUrl_ReturnsUnverified()
    {
        // ServiceEndpoint.Resolve throws UriFormatException on a base URL that is not a
        // URI, and the client absorbs it: the URL is the user's to type.
        var client = Client(new RecordingHandler(HttpStatusCode.OK, CatalogBody));

        await AssertUnverified(await client.ListModelsAsync(Connection(baseUrl: "not a url")),
            "an unusable base URL is an unverified connection, not a crash");
    }

    [TestMethod]
    public async Task ForAnUnknownServiceId_ReturnsUnverified()
    {
        // The Service column is a descriptor id, not free text; a row naming a service
        // the registry does not know must not throw its way out of the probe.
        var client = Client(new RecordingHandler(HttpStatusCode.OK, CatalogBody));

        await AssertUnverified(await client.ListModelsAsync(Connection(service: "not-a-service")),
            "a service the registry does not know cannot be probed");
    }

    [TestMethod]
    public async Task ForANullConnection_ReturnsUnverified()
    {
        // The registry lookup sits inside the never-throws guard, so the documented
        // contract holds for a caller that hands over nothing at all. Not reachable from
        // today's form; it is a public seam and the doc claims more than the code did.
        var client = Client(new RecordingHandler(HttpStatusCode.OK, CatalogBody));

        await AssertUnverified(await client.ListModelsAsync(null!), "there is no connection to verify");
    }

    /// <summary>The caller's own cancellation is not a result about the connection. The
    /// handler that persists <c>Verification</c> and <c>VerifiedAt</c> would stamp a
    /// probe that never ran, so the cancellation has to escape rather than answer
    /// "unverified". The token is cancelled while the request is in flight: a token
    /// already cancelled at the call site never reaches the handler, and a synchronous
    /// stub would not notice it at all.</summary>
    [TestMethod]
    public async Task WhenTheCallerCancels_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var client = Client(new CancellationObservingHandler());
        var probe = client.ListModelsAsync(Connection(), cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () => await probe);
    }

    /// <summary>The other half of the same distinction, and the reason the guard is on
    /// the token rather than on the exception type. An <see cref="HttpClient"/> timeout
    /// arrives as a <see cref="TaskCanceledException"/> while the caller's token is
    /// untouched, so a blanket rethrow for that type would turn a timeout into a crash.
    /// A timeout is a real probe result and stays an unverified one.</summary>
    [TestMethod]
    public async Task WhenTheRequestTimesOut_StaysUnverified()
    {
        var client = Client(new ThrowingHandler(new TaskCanceledException("timeout")));

        var catalog = await client.ListModelsAsync(Connection(), CancellationToken.None);

        await AssertUnverified(catalog, "a timeout is a probe result: the service did not answer in time");
    }

    /// <summary>A DNS failure and a refused connection have to be distinguishable to
    /// whoever reads the log, which is the only reason the catch path logs at all. The
    /// credential is the constraint: the request carries the key and an exception's
    /// message can echo the base URL back, so the line states the failure's category and
    /// nothing that identifies the connection. The exception object is not passed either
    /// — <c>LogDebug(ex, …)</c> would render its message, which is exactly what must not
    /// be written down.</summary>
    [TestMethod]
    public async Task WhenTheProbeFails_LogsTheCategoryAndNothingIdentifying()
    {
        var logger = new RecordingLogger<ServiceCatalogClient>();
        var failure = new HttpRequestException("no such host is known: gateway.internal.example");
        var client = Client(new ThrowingHandler(failure), logger);

        await client.ListModelsAsync(Connection("https://gateway.internal.example"));

        var text = logger.TextAt(LogLevel.Debug);
        StringAssert.Contains(text, nameof(HttpRequestException),
            "the category is the whole payload: it is what tells a DNS failure from a refused connection");
        Assert.IsFalse(text.Contains("sk-test", StringComparison.OrdinalIgnoreCase),
            $"the API key must never reach the log: {text}");
        Assert.IsFalse(text.Contains("gateway.internal.example", StringComparison.OrdinalIgnoreCase),
            $"the base URL must never reach the log: {text}");
        Assert.IsFalse(text.Contains("no such host is known", StringComparison.OrdinalIgnoreCase),
            $"the exception message can echo the request, so it is not logged: {text}");
        Assert.AreEqual(0, logger.Entries.Count(e => e.Exception is not null),
            "passing the exception to the logger would render its message; only its type name is safe");
    }
}
