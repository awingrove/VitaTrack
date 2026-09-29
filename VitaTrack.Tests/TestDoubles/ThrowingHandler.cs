using System.Net.Http;

namespace VitaTrack.Tests.TestDoubles;

/// <summary>
/// Fails every request with the exception it was given, standing in for the ways a
/// request dies before a response exists: a refused connection, a DNS failure, a
/// timeout. The exception is passed in rather than hard-coded so one test can name the
/// failure it is about — and so a test can prove the distinction between a timeout
/// (a real result) and a cancelled request (not one) survives in the code under test.
/// </summary>
internal sealed class ThrowingHandler(Exception failure) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw failure;
}
