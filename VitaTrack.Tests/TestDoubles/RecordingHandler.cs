using System.Net;
using System.Net.Http;

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
    /// carried none. Survives the caller disposing the request — see the type summary.
    /// </summary>
    public string? LastRequestBody { get; private set; }

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
