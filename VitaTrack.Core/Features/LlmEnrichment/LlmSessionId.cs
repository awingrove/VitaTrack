using VitaTrack.Core.Features.ServiceConnections;

namespace VitaTrack.Core.Features.LlmEnrichment;

/// <summary>
/// The session id the completion client stamps on every request it builds.
/// <para>
/// A DI wrapper, not an id. The value is
/// <see cref="ServiceDescriptorRegistry.SessionId"/> — the same one the catalog
/// probe sends — so the <c>x-opencode-session</c> header correlates the probe and
/// the completion in the service's own logs. It is registered rather than reached
/// for statically so the client depends on a singleton it was given, and it
/// deliberately owns and generates nothing: a second id minted here would leave
/// the header correlating nothing across the two paths, which is the failure that
/// came from minting one per request in the first place.
/// </para>
/// </summary>
public sealed class LlmSessionId
{
    /// <summary>Returns the process-wide session id. There is deliberately no
    /// backing field: see the type summary for why this must not become a second
    /// source of one.</summary>
    public string Value => ServiceDescriptorRegistry.SessionId;
}
