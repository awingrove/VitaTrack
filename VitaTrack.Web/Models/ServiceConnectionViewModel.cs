using VitaTrack.Core.Features.ServiceConnections;

namespace VitaTrack.Web.Models;

/// <summary>
/// Index's model: the saved connection when there is one, and the connect form the
/// user edits. The form is prefilled from the saved connection so a reconnect does
/// not make the user retype their URL and model.
/// </summary>
public sealed record ServiceConnectionViewModel(SavedConnection? Saved, ConnectServiceRequest Form);
