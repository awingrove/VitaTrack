using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VitaTrack.Tests.TestDoubles;

/// <summary>
/// Records the request it was given and answers with a fixed response. A stub rather
/// than a Moq-protected mock: the tests that use it assert on the request the code under
/// test actually built, so the request object has to be captured whole — headers,
/// method and URI together — which is the thing Moq's <c>Protected</c> setup hides.
/// <para>
/// The body is read eagerly, at send time, rather than left on the captured request.
/// Both HTTP clients build their request in a <c>using</c>, so by the time a test
/// looks at <see cref="LastRequest"/> that request has been disposed and its content
/// throws <see cref="ObjectDisposedException"/>; the URI and headers survive disposal
/// but the body does not. Copying the string out while the handler still holds it is
/// the only way a test can assert on what was posted.
/// </para>
/// </summary>
internal sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    private readonly HttpStatusCode _status = status;
    private readonly string _body = body;

    /// <summary>The last request that reached this handler, or null if none did. A test
    /// that asserts "no request was sent" checks this rather than the response.</summary>
    public HttpRequestMessage? LastRequest { get; private set; }

    /// <summary>The body of the last request as it was sent, or null if that request
    /// carried none. Survives the caller disposing the request — see the type summary.</summary>
    public string? LastRequestBody { get; private set; }

    /// <summary>The request that reached this handler, as a test failure when none did.
    /// <para>
    /// The accessor exists so the tests below it never write <c>LastRequest!</c>:
    /// a null-forgiving operator there is the compiler being told to trust a claim the
    /// test has not made, and there is no inline justification to give it. Throwing
    /// <see cref="AssertFailedException"/> from here is a normal test failure with a
    /// readable message, which is strictly better than a
    /// <see cref="NullReferenceException"/> three frames later.
    /// </para></summary>
    public HttpRequestMessage SentRequest() =>
        LastRequest ?? throw new AssertFailedException("no request reached this handler");

    /// <summary>The URI the captured request went to, as a test failure when there was
    /// no request. Non-null for the same reason: this handler only runs for a request
    /// <c>HttpClient</c> accepted, and that throws rather than send one with no URI.
    /// Reading it here is what keeps the call sites from writing <c>RequestUri!</c>.</summary>
    public Uri SentRequestUri() =>
        SentRequest().RequestUri ?? throw new AssertFailedException("the captured request carried no URI");

    /// <summary>The body that was sent, parsed as JSON. Reading the <c>model</c>
    /// property out of this is tighter than asserting a substring against the raw text,
    /// which passes on a <c>model</c> that appears anywhere — an echoed prompt, say —
    /// and fails on a body that merely orders its keys differently. Cloned, because the
    /// backing document is disposed with this expression and a live
    /// <see cref="JsonElement"/> over it would throw on the next read.</summary>
    public JsonElement SentBody() =>
        JsonDocument.Parse(LastRequestBody ?? throw new AssertFailedException("no request body was sent"))
            .RootElement.Clone();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastRequestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage
        {
            StatusCode = _status,
            Content = new StringContent(_body)
        };
    }
}
