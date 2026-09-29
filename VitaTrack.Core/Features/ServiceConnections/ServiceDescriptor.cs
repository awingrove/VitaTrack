namespace VitaTrack.Core.Features.ServiceConnections;

/// <summary>
/// What one supported service contributes to an otherwise identical code path.
/// Everything in the app that talks to an OpenAI-compatible endpoint reads its
/// identity, label, default URL and headers from here; the request shape itself is
/// generic, so a second service is a new record in a list rather than a new class.
/// </summary>
/// <param name="ServiceId">The value stored in <see cref="ServiceConnection.Service"/> —
/// a descriptor id, never free text, so <c>ServiceDescriptorRegistry</c> can validate it.</param>
/// <param name="DisplayName">The label shown to the user.</param>
/// <param name="DefaultBaseUrl">Prefill for the connect form. Empty when the URL is the
/// user's to supply, which is the case for every service shipped today.</param>
/// <param name="HeaderFactory">The service-specific headers for one request. A delegate,
/// not a dictionary, because the session header carries a per-process value.</param>
public sealed record ServiceDescriptor(
    string ServiceId,
    string DisplayName,
    string DefaultBaseUrl,
    Func<IReadOnlyDictionary<string, string>> HeaderFactory);
