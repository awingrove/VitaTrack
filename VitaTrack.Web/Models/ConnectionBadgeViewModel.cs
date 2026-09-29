using VitaTrack.Core.Features.ServiceConnections;

namespace VitaTrack.Web.Models;

/// <summary>
/// The connection's verification state, reduced to the one word a badge shows. It is a
/// projection like <see cref="SavedConnection"/> rather than the
/// <see cref="ServiceConnection"/> itself, so the view cannot reach a property this
/// projection does not carry — the row holds the API key in plaintext, and the badge is
/// rendered on pages that have no other business seeing it.
/// <para>
/// Two states, not three: with no connection at all there is nothing to badge, and the
/// enrichment flow already says so in words (<c>LlmService</c> refuses with "No AI
/// service connection is set up"). A badge saying "disconnected" there would be a
/// vaguer second statement of a message the user can act on.
/// </para>
/// </summary>
public sealed record ConnectionBadgeViewModel(string Verification)
{
    public bool Verified => Verification == ServiceConnection.Verified;
}
