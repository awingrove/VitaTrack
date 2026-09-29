using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core.Features.ServiceConnections;
using VitaTrack.Tests.TestDoubles;

namespace VitaTrack.Tests.Features.ServiceConnections;

/// <summary>
/// What the probe sends and what a readable answer turns into: the request's shape and
/// the catalog parsed out of a 2xx. Every failure path is in
/// <see cref="ServiceCatalogClientFailureTests"/>, which shares this class's contract —
/// an unverified result, never an exception — and its doubles.
/// <para>
/// The tests assert on the request the client actually built, so the handler captures
/// the request whole rather than mocking <c>SendAsync</c> and matching arguments.
/// <see cref="SequencedHttpClientFactory"/> hands out one client per handler in call
/// order, which is what lets a single factory drive two connections.
/// </para>
/// </summary>
[TestClass]
public class ServiceCatalogClientTests
{
    private const string CatalogBody = @"{ ""object"": ""list"", ""data"": [{ ""id"": ""m1"" }, { ""id"": ""m2"" }] }";

    private static ServiceConnection Connection(string baseUrl = "http://h", string service = "opencode") =>
        new() { BaseUrl = baseUrl, ApiKey = "sk-test", Service = service };

    private static ServiceCatalogClient Client(HttpMessageHandler handler) =>
        new(new SequencedHttpClientFactory(handler), new RecordingLogger<ServiceCatalogClient>());

    [TestMethod]
    public async Task ListModelsAsync_On200_ParsesIds_AndVerifies()
    {
        var client = Client(new RecordingHandler(HttpStatusCode.OK, CatalogBody));

        var catalog = await client.ListModelsAsync(Connection());

        Assert.IsTrue(catalog.Verified, "a 2xx catalog response is what verification means");
        CollectionAssert.AreEqual(new[] { "m1", "m2" }, catalog.Models.ToList());
    }

    [TestMethod]
    public async Task ListModelsAsync_On200_SkipsEntriesThatAreNotModelObjects()
    {
        // A service that pads its array must not cost the user the ids it did name.
        var body = @"{ ""data"": [ { ""id"": ""m1"" }, ""junk"", { ""name"": ""no-id"" }, { ""id"": ""m2"" } ] }";
        var client = Client(new RecordingHandler(HttpStatusCode.OK, body));

        var catalog = await client.ListModelsAsync(Connection());

        Assert.IsTrue(catalog.Verified, "a readable listing is a verified connection");
        CollectionAssert.AreEqual(new[] { "m1", "m2" }, catalog.Models.ToList());
    }

    [TestMethod]
    public async Task ListModelsAsync_SendsBearerAuthorizationHeader()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, CatalogBody);

        await Client(handler).ListModelsAsync(Connection());

        var request = handler.LastRequest;
        Assert.IsNotNull(request);
        var authorization = request!.Headers.GetValues("Authorization").Single();
        Assert.AreEqual("Bearer sk-test", authorization, "the key travels per request, from the connection being probed");
    }

    [TestMethod]
    public async Task ListModelsAsync_SendsDescriptorHeaders()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, CatalogBody);

        await Client(handler).ListModelsAsync(Connection());

        var request = handler.LastRequest;
        Assert.IsNotNull(request);
        Assert.IsTrue(request!.Headers.Contains("x-opencode-session"),
            "the probe carries the same descriptor headers a completion would");
    }

    [TestMethod]
    public async Task ListModelsAsync_SendsTheApplicationUserAgent()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, CatalogBody);

        await Client(handler).ListModelsAsync(Connection());

        var request = handler.LastRequest;
        Assert.IsNotNull(request);
        Assert.IsTrue(request!.Headers.UserAgent.ToString().Contains("VitaTrack", StringComparison.Ordinal),
            "every request the app makes identifies the app, the probe included");
    }

    /// <summary>Review Focus #1. The invariant, not a path: however the two slash forms
    /// are combined, they must request the same URI. A form-sensitive combine would send
    /// a doubled or missing separator to one host and not the other, and the failure
    /// would depend on what the user typed.</summary>
    [TestMethod]
    public async Task ListModelsAsync_BuildsAbsoluteUri_ForBaseUrlWithAndWithoutTrailingSlash()
    {
        var withSlash = new RecordingHandler(HttpStatusCode.OK, CatalogBody);
        var withoutSlash = new RecordingHandler(HttpStatusCode.OK, CatalogBody);

        await Client(withSlash).ListModelsAsync(Connection("http://h/v1/"));
        await Client(withoutSlash).ListModelsAsync(Connection("http://h/v1"));

        Assert.AreEqual(
            withoutSlash.LastRequest!.RequestUri,
            withSlash.LastRequest!.RequestUri,
            "a trailing slash on the saved base URL must not change where the probe goes");
    }

    [TestMethod]
    public async Task ListModelsAsync_BuildsAbsoluteUri_FromTheConnectionNotTheClient()
    {
        // The named client is pooled and may have been used for another connection
        // already; the URI comes from the connection, so a second host is reachable.
        // This is the test the shared SequencedHttpClientFactory exists for: a
        // single-client factory cannot tell the two requests apart.
        var first = new RecordingHandler(HttpStatusCode.OK, CatalogBody);
        var second = new RecordingHandler(HttpStatusCode.OK, CatalogBody);
        var client = new ServiceCatalogClient(
            new SequencedHttpClientFactory(first, second), new RecordingLogger<ServiceCatalogClient>());

        await client.ListModelsAsync(Connection("http://one.example"));
        await client.ListModelsAsync(Connection("http://two.example"));

        Assert.AreEqual("http://two.example/v1/models", second.LastRequest!.RequestUri!.ToString());
    }

    [TestMethod]
    public async Task ListModelsAsync_RequestsTheDocumentedCatalogPath()
    {
        // The documented probe is GET {BaseUrl}/v1/models. Asserted separately from the
        // slash-form invariant above so a wrong path is named as a path problem.
        var handler = new RecordingHandler(HttpStatusCode.OK, CatalogBody);

        await Client(handler).ListModelsAsync(Connection("http://h/v1"));

        var uri = handler.LastRequest!.RequestUri!;
        Assert.IsTrue(uri.IsAbsoluteUri, "the request URI is absolute: no HttpClient.BaseAddress is involved");
        Assert.AreEqual("http://h/v1/v1/models", uri.ToString());
    }

    [TestMethod]
    public async Task ListModelsAsync_UsesGet()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, CatalogBody);

        await Client(handler).ListModelsAsync(Connection());

        Assert.AreEqual(HttpMethod.Get, handler.LastRequest!.Method);
    }

    /// <summary>The request that never leaves the building: an unknown service id resolves
    /// no descriptor, so there is nothing to probe and no request is sent. Asserted on
    /// the factory rather than on the result, because "answered unverified" is also what
    /// a 404 produces and only the absent request tells the two apart.</summary>
    [TestMethod]
    public async Task ListModelsAsync_ForAnUnknownServiceId_SendsNoRequest()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, CatalogBody);
        var factory = new SequencedHttpClientFactory(handler);

        await new ServiceCatalogClient(factory, new RecordingLogger<ServiceCatalogClient>())
            .ListModelsAsync(Connection(service: "not-a-service"));

        Assert.AreEqual(0, factory.CreatedClients, "an unprobeable connection must not reach the network");
        Assert.IsNull(handler.LastRequest);
    }
}
