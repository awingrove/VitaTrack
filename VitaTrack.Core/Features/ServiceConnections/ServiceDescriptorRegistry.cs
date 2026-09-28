namespace VitaTrack.Core.Features.ServiceConnections;

/// <summary>
/// The shipped services, the values a connection's <c>Variant</c> may take, and the
/// lookup that turns a stored service id back into a descriptor. This is the single
/// authority for what a valid service id is: <c>SaveConnectionHandler.ServiceName</c>
/// is a const reference to <see cref="OpenCodeServiceId"/> rather than a literal of its
/// own, and the connect form renders that const, so no second place carries the id.
/// A selector over <see cref="All"/> replaces that when a second service ships.
/// </summary>
public static class ServiceDescriptorRegistry
{
    /// <summary>The id of the only service that ships.</summary>
    public const string OpenCodeServiceId = "opencode";

    /// <summary>Correlates every request in one run against the gateway's logs. Its
    /// value changes per process, which is why the descriptor hands it out through a
    /// factory rather than as a constant.</summary>
    public const string SessionHeaderName = "x-opencode-session";

    /// <summary>Identifies the app to the service. Not service-specific, so it is not
    /// the descriptor's business — it is set on the request instead.</summary>
    public const string UserAgent = "VitaTrack/1.0 (+https://github.com/awingrove/VitaTrack)";

    /// <summary>The session id for this process: every request the app makes in a run
    /// carries it, and a restart begins a new one. Public because the singleton the
    /// completion client takes must be this same value — a second id generated
    /// elsewhere would leave the header correlating nothing, which is the failure that
    /// came from minting one per request.</summary>
    public static string SessionId { get; } = Guid.NewGuid().ToString();

    public static IReadOnlyList<ServiceDescriptor> All { get; } =
    [
        new ServiceDescriptor(OpenCodeServiceId, "OpenCode", string.Empty, OpenCodeHeaders)
    ];

    /// <summary>The variant vocabulary, exactly: what <c>reasoning_effort</c> accepts
    /// across the OpenAI-compatible providers in scope. Fixed rather than discovered —
    /// <c>/v1/models</c> does not carry variants, so there is nothing to discover
    /// them from.</summary>
    public static IReadOnlyList<string> Variants { get; } = ["none", "low", "medium", "high", "xhigh", "max"];

    /// <summary>The descriptor for a stored service id, or null when the id names no
    /// shipped service. Case-insensitive: it comes from a form and a text column.</summary>
    public static ServiceDescriptor? Find(string serviceId) =>
        All.FirstOrDefault(d => string.Equals(d.ServiceId, serviceId, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyDictionary<string, string> OpenCodeHeaders() =>
        new Dictionary<string, string> { [SessionHeaderName] = SessionId };
}
