using System.Threading.Tasks;
using VitaTrack.Core.Features.ServiceConnections;

namespace VitaTrack.Core.Features.LlmEnrichment;

public record LlmCompletion(string? Content, string? Error);

/// <summary>
/// The per-request knobs a connection carries, lifted out of the connection itself
/// so <see cref="ILlmClient"/> is asked for one specific model/variant pair rather
/// than reading whatever the ambient configuration happens to say. Nothing here is
/// defaulted: a value this record does not carry is one the request must not send.
/// </summary>
public record LlmRequestSettings(string? Model, string? Variant, int MaxTokens, double Temperature)
{
    /// <summary>Single owner of the "no model was chosen" message. <see cref="LlmClient"/>
    /// reports it when a call arrives without a model, and <see cref="LlmService"/>
    /// reports it before ever building this record — the same fault, so it is worded
    /// once here rather than restated at each surface.</summary>
    internal const string ModelRequired = "No AI model is selected. Choose one in Settings, then try again.";
}

public interface ILlmClient
{
    /// <summary>Posts one chat completion to the connection's own service. The
    /// connection travels per call rather than being read from configuration: the
    /// pooled named client carries whichever connection used it last, so nothing
    /// about the destination or the credential can live on it.</summary>
    Task<LlmCompletion> PostChatAsync(string systemPrompt, string userPrompt,
        ServiceConnection connection, LlmRequestSettings settings);
}
