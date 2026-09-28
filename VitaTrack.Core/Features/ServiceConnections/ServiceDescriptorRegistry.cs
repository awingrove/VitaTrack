namespace VitaTrack.Core.Features.ServiceConnections;

/// <summary>
/// The shipped services, the values a connection's <c>Variant</c> may take, and the
/// lookups that turn a submitted value back into the registry's own spelling. This is
/// the single authority for what a valid service id and a valid variant are:
/// <see cref="SaveConnectionHandler"/> resolves the service through
/// <see cref="Find"/> rather than naming one, and <see cref="SelectModelRequest"/>
/// validates the variant through <see cref="FindVariant"/>, so no second place carries
/// either vocabulary.
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

    /// <summary>The registry's own spelling of a submitted variant, or null when it is
    /// not one of the six. Both halves of the round trip live here: the form renders
    /// <see cref="Variants"/> and the controller stores what this returns, so the value
    /// in the column is always a value from the list rather than a differently-cased
    /// copy of one. Case-insensitive for the same reason <see cref="Find"/> is.</summary>
    public static string? FindVariant(string? variant) =>
        Variants.FirstOrDefault(v => string.Equals(v, variant, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyDictionary<string, string> OpenCodeHeaders() =>
        new Dictionary<string, string> { [SessionHeaderName] = SessionId };
}
