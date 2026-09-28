using System.Net;
using System.Net.Http;

namespace VitaTrack.Tests.TestDoubles;

/// <summary>
/// Records the request it was given and answers with a fixed response. A stub rather
/// than a Moq-protected mock: the tests that use it assert on the request the code under
/// test actually built, so the request object has to be captured whole — headers,
/// method and URI together — which is the thing Moq's <c>Protected</c> setup hides.
/// </summary>
internal sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    private readonly HttpStatusCode _status = status;
    private readonly string _body = body;

    /// <summary>The last request that reached this handler, or null if none did. A test
    /// that asserts "no request was sent" checks this rather than the response.</summary>
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(new HttpResponseMessage
        {
            StatusCode = _status,
            Content = new StringContent(_body)
        });
    }
}
