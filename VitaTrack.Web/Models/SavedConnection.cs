namespace VitaTrack.Web.Models;

/// <summary>
/// The active connection as the screen may show it. The API key crosses into the
/// view only as <see cref="MaskedApiKey"/>, so the raw value has nowhere to leak
/// from: the projection is the only thing the view is handed.
/// </summary>
public sealed record SavedConnection(
    string Service,
    string BaseUrl,
    string? Model,
    string Verification,
    string MaskedApiKey);
