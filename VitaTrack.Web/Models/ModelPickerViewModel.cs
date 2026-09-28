using VitaTrack.Core.Features.ServiceConnections;

namespace VitaTrack.Web.Models;

/// <summary>
/// The model picker's state: the models the last probe listed, what is chosen now, and
/// the fixed variant vocabulary. <para>
/// <see cref="Models"/> is the whole signal. Empty means the probe did not list anything
/// — a service with no <c>/v1/models</c>, a failed probe, or a plain page load, which
/// does not probe — and the view then renders a free-text field rather than a dropdown
/// with nothing in it. One rule, so the two branches cannot disagree about when a
/// picker is offered.
/// </para>
/// <para>
/// <see cref="JustSaved"/> is per-response, not stored: it is what lets the swap answer
/// a successful <c>SelectModel</c> with "saved" and a plain page load with nothing. A
/// silent re-render is indistinguishable from a no-op.
/// </para>
/// </summary>
public sealed record ModelPickerViewModel(
    IReadOnlyList<string> Models,
    string? SelectedModel,
    string? SelectedVariant,
    IReadOnlyList<string> Variants,
    bool JustSaved);
