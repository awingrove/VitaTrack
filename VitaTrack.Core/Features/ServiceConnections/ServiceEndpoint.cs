namespace VitaTrack.Core.Features.ServiceConnections;

/// <summary>
/// The one rule for turning a saved <c>BaseUrl</c> into a request URI: drop any
/// trailing slash, strip a trailing <c>/v1</c> or <c>/openai</c> segment, then append
/// the path. The app always appends its own version segment (<c>v1/models</c>,
/// <c>v1/chat/completions</c>), so a base URL the user pasted with one would otherwise
/// double it — and gateways that expose an OpenAI-compatible surface under
/// <c>/openai/v1</c> collapse onto the same root. Both slash forms therefore resolve to
/// the same absolute URI, and the request never depends on an
/// <c>HttpClient.BaseAddress</c> — a pooled client carries whichever connection used it
/// last, so a relative URL would silently point a new connection at the previous host.
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
    public static Uri Resolve(string baseUrl, string relativePath)
    {
        var root = StripTrailingSuffix(baseUrl.TrimEnd('/'), "/v1");
        root = StripTrailingSuffix(root, "/openai");
        return new($"{root}/{relativePath.TrimStart('/')}", UriKind.Absolute);
    }

    private static string StripTrailingSuffix(string value, string suffix) =>
        value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? value[..^suffix.Length].TrimEnd('/')
            : value;
}
