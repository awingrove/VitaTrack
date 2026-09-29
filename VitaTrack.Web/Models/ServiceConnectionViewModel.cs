using VitaTrack.Core.Features.ServiceConnections;

namespace VitaTrack.Web.Models;

/// <summary>
/// The Settings page's whole state: the saved connection when there is one, the connect
/// form the user edits, and the model picker that only exists once something is saved.
/// The form is prefilled from the saved connection so a reconnect does not make the user
/// retype their URL and model.
/// <para>
/// <see cref="ModelPicker"/> is null exactly when <see cref="Saved"/> is null, and
/// <see cref="ServiceConnectionController"/> builds both in one place so the two cannot
/// disagree — a picker for a connection that is not there, or a saved connection with
/// nothing to point it at.
/// </para>
/// <para>
/// <see cref="ProbeNote"/> is per-response and never stored. It explains the free-text
/// model field in the words the user needs, and it is set only on the answer to a probe
/// that did not verify: a plain page load shows the picker with no note, because nobody
/// asked a question on it. The lasting truth about the connection is
/// <see cref="Saved.Verification"/>, which the badge renders.
/// </para>
/// </summary>
public sealed record ServiceConnectionViewModel(
    SavedConnection? Saved,
    ConnectServiceRequest Form,
    ModelPickerViewModel? ModelPicker,
    string? ProbeNote);
