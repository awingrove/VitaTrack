using System;
using System.Net.Http;

namespace VitaTrack.Tests.TestDoubles;

/// <summary>
/// An <see cref="IHttpClientFactory"/> that hands out one client per handler, in call
/// order, so a test that drives two requests through one factory can tell which request
/// went to which handler. The four earlier Moq factories each return a single client,
/// which cannot express "the second call reached a different host"; this one is the
/// generalisation, extracted so a second test needing it does not write a sixth copy.
/// <para>
/// The client name is accepted and ignored: a test that cares about which named client a
/// caller asked for is testing the caller's constant, not its behaviour. Once the
/// handlers run out, the last one is reused, so a single-handler test does not have to
/// count its calls.
/// </para>
/// </summary>
internal sealed class SequencedHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler[] _handlers;
    private readonly Uri? _baseAddress;
    private readonly Action<HttpClient>? _configure;
    private int _next;

    public SequencedHttpClientFactory(params HttpMessageHandler[] handlers)
        : this(null, null, handlers)
    {
    }

    /// <param name="baseAddress">Optional. Set it only to reproduce a client that
    /// already carries one — the catalog probe and the completion client both build
    /// absolute URIs and must not depend on a <c>BaseAddress</c> at all, so a test that
    /// wants to prove they don't sets a *hostile* one and shows it losing.</param>
    /// <param name="configure">Optional, applied to every client handed out. Reproduces
    /// the defaults a named client's configure delegate leaves behind, which is how a
    /// test shows a per-request header beating a stale pooled one.</param>
    public SequencedHttpClientFactory(Uri? baseAddress, params HttpMessageHandler[] handlers)
        : this(baseAddress, null, handlers)
    {
    }

    public SequencedHttpClientFactory(Action<HttpClient> configure, params HttpMessageHandler[] handlers)
        : this(null, configure, handlers)
    {
    }

    private SequencedHttpClientFactory(
        Uri? baseAddress,
        Action<HttpClient>? configure,
        HttpMessageHandler[] handlers)
    {
        if (handlers.Length == 0)
            throw new ArgumentException("at least one handler is required", nameof(handlers));

        _handlers = handlers;
        _baseAddress = baseAddress;
        _configure = configure;
    }

    /// <summary>The number of clients handed out — i.e. how many calls the code under
    /// test made. A test that expects no request at all asserts zero here rather than
    /// asserting an absent side effect. Note that a client may be built in a
    /// constructor rather than per call (<see cref="Features.LlmEnrichment.HtmlScraperService"/>
    /// does), so this counts client creation, not requests sent.</summary>
    public int CreatedClients => _next;

    public HttpClient CreateClient(string name)
    {
        var handler = _handlers[Math.Min(_next, _handlers.Length - 1)];
        _next++;
        var client = new HttpClient(handler, disposeHandler: false);
        if (_baseAddress is not null)
            client.BaseAddress = _baseAddress;
        _configure?.Invoke(client);
        return client;
    }
}
