using System.ComponentModel.DataAnnotations;

namespace VitaTrack.Core.Features.ServiceConnections;

/// <summary>
/// What the connect form binds: where the service lives, the key to reach it with,
/// and an optional free-text model. There is no <c>Service</c> field — one service
/// ships, and <see cref="SaveConnectionHandler"/> names it. A service id and a
/// selector arrive with the model picker.
/// </summary>
public class ConnectServiceRequest
{
    /// <summary>Single owner of each message: the form's <c>[Required]</c> and the
    /// handler's own rejection of a blank value both reference it, so the two entry
    /// points cannot word the same fault differently.</summary>
    public const string BaseUrlRequired = "Base URL is required.";

    public const string ApiKeyRequired = "API key is required.";

    // The display name is the label the form renders and the wording every other
    // message on the screen uses. Left to the property name, asp-for would label the
    // fields "BaseUrl" and "ApiKey" while the saved-state table and the errors say
    // "Base URL" and "API key".
    [Display(Name = "Base URL")]
    [Required(ErrorMessage = BaseUrlRequired)]
    public string BaseUrl { get; set; } = string.Empty;

    [Display(Name = "API key")]
    [Required(ErrorMessage = ApiKeyRequired)]
    public string ApiKey { get; set; } = string.Empty;

    public string? Model { get; set; }
}
