using VitaTrack.Core.Features.ServiceConnections;

namespace VitaTrack.Core.Features.LlmEnrichment;

/// <summary>
/// The per-request knobs a connection carries, lifted out of the connection itself
/// so <see cref="ILlmClient"/> is asked for one specific model/variant pair rather
/// than reading whatever the ambient configuration happens to say. Nothing here is
/// defaulted: a value this record does not carry is one the request must not send.
/// </summary>
/// <remarks>
/// Its own file rather than a neighbour's, because it is a parameter of two
/// interfaces and both of them — <see cref="ILlmClient"/> and
/// <see cref="ISupplementLabelParser"/> — reach for it. It is also the one home
/// for the two messages below, which name the two reasons a call cannot be made.
/// </remarks>
public record LlmRequestSettings(string? Model, string? Variant, int MaxTokens, double Temperature)
{
    /// <summary>Single owner of the "no model was chosen" message. <see cref="LlmClient"/>
    /// reports it when a call arrives without a model, and <see cref="LlmService"/>
    /// reports it before ever building this record — the same fault, so it is worded
    /// once here rather than restated at each surface.</summary>
    internal const string ModelRequired = "No AI model is selected. Choose one in Settings, then try again.";

    /// <summary>Single owner of the "no such service" message, which is a *different*
    /// fault from the one above and must not read like it: a user who has already
    /// chosen a model and still cannot enrich has a connection naming a service this
    /// build does not ship, and sending them to the model picker would point them at
    /// something that is not the problem. <see cref="LlmClient"/> reports it rather
    /// than posting a credential to a service <see cref="ServiceDescriptorRegistry"/>
    /// cannot describe — the same id the catalog probe refuses — and
    /// <see cref="LlmService"/> reports it before a manufacturer page is fetched to
    /// find out.</summary>
    internal const string ServiceNotFound =
        "The saved AI service is not one this version of VitaTrack knows. Reconnect it in Settings, then try again.";
}
