namespace VitaTrack.Core.Features.ServiceConnections;

/// <summary>
/// The one rule for turning a saved <c>BaseUrl</c> into a request URI: drop any
/// trailing slash, then append the path. Both slash forms therefore collapse to the
/// same absolute URI, and the request never depends on an <c>HttpClient.BaseAddress</c>
/// — a pooled client carries whichever connection used it last, so a relative URL
/// would silently point a new connection at the previous host.
/// <para>
/// Shared rather than private: the catalog probe and the completion client build
/// their URIs the same way, and a second copy of this rule is how the two drift.
/// </para>
/// <para>
/// Throws <see cref="UriFormatException"/> on a base URL that is not absolute. The
/// callers wrap it — an unusable base URL is an unverified connection, not a crash.
/// </para>
/// </summary>
public static class ServiceEndpoint
{
    public static Uri Resolve(string baseUrl, string relativePath) =>
        new($"{baseUrl.TrimEnd('/')}/{relativePath.TrimStart('/')}", UriKind.Absolute);
}
