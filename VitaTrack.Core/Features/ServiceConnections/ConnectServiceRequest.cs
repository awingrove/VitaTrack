using System.ComponentModel.DataAnnotations;

namespace VitaTrack.Core.Features.ServiceConnections;

/// <summary>
/// What the connect form binds: which shipped service to point at, where it lives, the
/// key to reach it with, and an optional free-text model. <para>
/// <see cref="Service"/> is a descriptor id, not free text, and it is validated by
/// <see cref="SaveConnectionHandler"/> against <see cref="ServiceDescriptorRegistry"/>
/// rather than by a second list here. The default names a service the registry
/// actually ships, so a caller that builds a request by hand — a test, a seed, a
/// future import path — does not have to choose; the handler's lookup is what makes a
/// wrong choice a failure rather than a write.
/// </para>
/// </summary>
public class ConnectServiceRequest
{
    /// <summary>Single owner of each message: the form's <c>[Required]</c> and the
    /// handler's own rejection of a blank value both reference it, so the two entry
    /// points cannot word the same fault differently.</summary>
    public const string BaseUrlRequired = "Base URL is required.";

    public const string ApiKeyRequired = "API key is required.";

    /// <summary>Said once, by the one place that checks it. The service field is a
    /// <c>&lt;select&gt;</c> of shipped descriptors, so MVC's <c>[Required]</c> never
    /// fires for it: a post that names nothing else resolves to no descriptor, and this
    /// is the message the user gets.</summary>
    public const string UnknownService = "Choose a service from the list.";

    // The display names are the labels the form renders and the wording every other
    // message on the screen uses. Left to the property names, asp-for would label the
    // fields "BaseUrl" and "ApiKey" while the saved-state table and the errors say
    // "Base URL" and "API key".
    [Display(Name = "Service")]
    public string Service { get; set; } = ServiceDescriptorRegistry.OpenCodeServiceId;

    [Display(Name = "Base URL")]
    [Required(ErrorMessage = BaseUrlRequired)]
    public string BaseUrl { get; set; } = string.Empty;

    [Display(Name = "API key")]
    [Required(ErrorMessage = ApiKeyRequired)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Free text and optional, which is a different thing from the model
    /// picker's own field: this one keeps the model the user already chose when left
    /// blank, and it is a text box because no catalog has been read yet. Named here
    /// rather than in the view so the label has one owner and the two "Model" fields on
    /// this page can be told apart by a selector.</summary>
    [Display(Name = "Model (optional)")]
    public string? Model { get; set; }
}
