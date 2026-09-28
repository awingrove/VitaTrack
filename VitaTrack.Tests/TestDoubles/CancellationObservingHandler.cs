using System.Net.Http;

namespace VitaTrack.Tests.TestDoubles;

/// <summary>
/// A handler that honours the cancellation token, the way <c>SocketsHttpHandler</c>
/// does: the request stays open until the token is cancelled, then the pending send
/// fails with a <see cref="TaskCanceledException"/>.
/// <para>
/// Exists because the synchronous stubs cannot express cancellation at all — a handler
/// that returns a completed task ignores the token, so a cancelled request looks
/// exactly like a fast one, and a test written against them would "prove" a
/// cancellation propagates when the code under test was never asked anything.
/// </para>
/// </summary>
internal sealed class CancellationObservingHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("the delay completed, so the token was never cancelled");
    }
}
