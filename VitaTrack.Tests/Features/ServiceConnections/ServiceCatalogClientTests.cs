using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core.Features.ServiceConnections;

namespace VitaTrack.Tests.Features.ServiceConnections;

/// <summary>
/// A failed probe is a state, not an exception: every test here that expects
/// <c>Verified == false</c> would surface an escaping exception as a test error
/// rather than a failure, so "does not throw" is asserted by the tests running to
/// completion with an unverified result, not by a decorative catch assertion.
/// </summary>
[TestClass]
public class ServiceCatalogClientTests
{
    private const string CatalogBody = @"{ ""object"": ""list"", ""data"": [{ ""id"": ""m1"" }, { ""id"": ""m2"" }] }";

    private static ServiceConnection Connection(string baseUrl = "http://h", string service = "opencode") =>
        new() { BaseUrl = baseUrl, ApiKey = "sk-test", Service = service };

    private static ServiceCatalogClient Client(HttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler));

    [TestMethod]
    public async Task ListModelsAsync_On200_ParsesIds_AndVerifies()
    {
        var client = Client(new StubHandler(HttpStatusCode.OK, CatalogBody));

        var catalog = await client.ListModelsAsync(Connection());

        Assert.IsTrue(catalog.Verified, "a 2xx catalog response is what verification means");
        CollectionAssert.AreEqual(new[] { "m1", "m2" }, catalog.Models.ToList());
    }

    [TestMethod]
    public async Task ListModelsAsync_On401_ReturnsUnverified_AndEmptyCatalog()
    {
        var client = Client(new StubHandler(HttpStatusCode.Unauthorized, @"{ ""error"": ""bad key"" }"));

        var catalog = await client.ListModelsAsync(Connection());

        Assert.IsFalse(catalog.Verified, "a rejected key is not a verified connection");
        Assert.AreEqual(0, catalog.Models.Count, "nothing was listed, so nothing is offered");
    }

    [TestMethod]
    public async Task ListModelsAsync_On404_ReturnsUnverified()
    {
        // 404 is not a bad key: several OpenAI-compatible endpoints do not implement
        // model listing at all, and the connection is still usable for completions.
        var client = Client(new StubHandler(HttpStatusCode.NotFound, "not found"));

        var catalog = await client.ListModelsAsync(Connection());

        Assert.IsFalse(catalog.Verified, "an endpoint without /v1/models is unverified, not broken");
        Assert.AreEqual(0, catalog.Models.Count);
    }

    [TestMethod]
    public async Task ListModelsAsync_On500_ReturnsUnverified()
    {
        var client = Client(new StubHandler(HttpStatusCode.InternalServerError, "boom"));

        var catalog = await client.ListModelsAsync(Connection());

        Assert.IsFalse(catalog.Verified, "a server error is not verification");
        Assert.AreEqual(0, catalog.Models.Count);
    }

    [TestMethod]
    public async Task ListModelsAsync_OnMalformedJson_ReturnsUnverified()
    {
        var client = Client(new StubHandler(HttpStatusCode.OK, "{ not json"));

        var catalog = await client.ListModelsAsync(Connection());

        Assert.IsFalse(catalog.Verified, "a body that cannot be parsed proves nothing about the connection");
        Assert.AreEqual(0, catalog.Models.Count);
    }

    [TestMethod]
    public async Task ListModelsAsync_On200_WithoutADataArray_ReturnsUnverified()
    {
        // Parsed, but there is no catalog in it. Reporting this as a verified
        // connection with an empty dropdown would leave the user with a picker and
        // nothing to pick.
        var client = Client(new StubHandler(HttpStatusCode.OK, @"{ ""object"": ""list"" }"));

        var catalog = await client.ListModelsAsync(Connection());

        Assert.IsFalse(catalog.Verified);
        Assert.AreEqual(0, catalog.Models.Count);
    }

    [TestMethod]
    public async Task ListModelsAsync_On200_SkipsEntriesThatAreNotModelObjects()
    {
        // A service that pads its array must not cost the user the ids it did name.
        var body = @"{ ""data"": [ { ""id"": ""m1"" }, ""junk"", { ""name"": ""no-id"" }, { ""id"": ""m2"" } ] }";
        var client = Client(new StubHandler(HttpStatusCode.OK, body));

        var catalog = await client.ListModelsAsync(Connection());

        Assert.IsTrue(catalog.Verified, "a readable listing is a verified connection");
        CollectionAssert.AreEqual(new[] { "m1", "m2" }, catalog.Models.ToList());
    }

    [TestMethod]
    public async Task ListModelsAsync_WhenTheRequestFails_ReturnsUnverified()
    {
        var client = Client(new ThrowingHandler(new HttpRequestException("connection refused")));

        var catalog = await client.ListModelsAsync(Connection());

        Assert.IsFalse(catalog.Verified, "a network failure is a state the user can act on, not an exception");
        Assert.AreEqual(0, catalog.Models.Count);
    }

    [TestMethod]
    public async Task ListModelsAsync_OnAMalformedBaseUrl_ReturnsUnverified()
    {
        var client = Client(new StubHandler(HttpStatusCode.OK, CatalogBody));

        var catalog = await client.ListModelsAsync(Connection(baseUrl: "not a url"));

        Assert.IsFalse(catalog.Verified, "an unusable base URL is an unverified connection, not a crash");
    }

    [TestMethod]
    public async Task ListModelsAsync_ForAnUnknownServiceId_ReturnsUnverified()
    {
        // The Service column is a descriptor id, not free text; a row naming a service
        // the registry does not know must not throw its way out of the probe.
        var client = Client(new StubHandler(HttpStatusCode.OK, CatalogBody));

        var catalog = await client.ListModelsAsync(Connection(service: "not-a-service"));

        Assert.IsFalse(catalog.Verified);
        Assert.AreEqual(0, catalog.Models.Count);
    }

    [TestMethod]
    public async Task ListModelsAsync_SendsBearerAuthorizationHeader()
    {
        var handler = new StubHandler(HttpStatusCode.OK, CatalogBody);

        await Client(handler).ListModelsAsync(Connection());

        var request = handler.LastRequest;
        Assert.IsNotNull(request);
        var authorization = request!.Headers.GetValues("Authorization").Single();
        Assert.AreEqual("Bearer sk-test", authorization, "the key travels per request, from the connection being probed");
    }

    [TestMethod]
    public async Task ListModelsAsync_SendsDescriptorHeaders()
    {
        var handler = new StubHandler(HttpStatusCode.OK, CatalogBody);

        await Client(handler).ListModelsAsync(Connection());

        var request = handler.LastRequest;
        Assert.IsNotNull(request);
        Assert.IsTrue(request!.Headers.Contains("x-opencode-session"),
            "the probe carries the same descriptor headers a completion would");
    }

    [TestMethod]
    public async Task ListModelsAsync_SendsTheApplicationUserAgent()
    {
        var handler = new StubHandler(HttpStatusCode.OK, CatalogBody);

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
        var withSlash = new StubHandler(HttpStatusCode.OK, CatalogBody);
        var withoutSlash = new StubHandler(HttpStatusCode.OK, CatalogBody);

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
        var first = new StubHandler(HttpStatusCode.OK, CatalogBody);
        var second = new StubHandler(HttpStatusCode.OK, CatalogBody);
        var factory = new StubHttpClientFactory(first, second);
        var client = new ServiceCatalogClient(factory);

        await client.ListModelsAsync(Connection("http://one.example"));
        await client.ListModelsAsync(Connection("http://two.example"));

        Assert.AreEqual("http://two.example/v1/models", second.LastRequest!.RequestUri!.ToString());
    }

    [TestMethod]
    public async Task ListModelsAsync_RequestsTheDocumentedCatalogPath()
    {
        // The documented probe is GET {BaseUrl}/v1/models. Asserted separately from the
        // slash-form invariant above so a wrong path is named as a path problem.
        var handler = new StubHandler(HttpStatusCode.OK, CatalogBody);

        await Client(handler).ListModelsAsync(Connection("http://h/v1"));

        var uri = handler.LastRequest!.RequestUri!;
        Assert.IsTrue(uri.IsAbsoluteUri, "the request URI is absolute: no HttpClient.BaseAddress is involved");
        Assert.AreEqual("http://h/v1/v1/models", uri.ToString());
    }

    [TestMethod]
    public async Task ListModelsAsync_UsesGet()
    {
        var handler = new StubHandler(HttpStatusCode.OK, CatalogBody);

        await Client(handler).ListModelsAsync(Connection());

        Assert.AreEqual(HttpMethod.Get, handler.LastRequest!.Method);
    }

    /// <summary>Records the request and answers with a fixed response. A stub rather
    /// than a Moq-protected mock: the tests assert on the request the client actually
    /// built, so the request object has to be captured whole.</summary>
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = status,
                Content = new StringContent(body)
            });
        }
    }

    private sealed class ThrowingHandler(Exception failure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw failure;
    }

    /// <summary>Hands out one client per handler, in call order, so a test that probes
    /// two connections can tell which request went where.</summary>
    private sealed class StubHttpClientFactory(params HttpMessageHandler[] handlers) : IHttpClientFactory
    {
        private int _next;

        public HttpClient CreateClient(string name)
        {
            var handler = handlers[Math.Min(_next, handlers.Length - 1)];
            _next++;
            return new HttpClient(handler, disposeHandler: false);
        }
    }
}
