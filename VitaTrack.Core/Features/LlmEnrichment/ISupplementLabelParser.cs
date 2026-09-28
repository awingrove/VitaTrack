using System.Threading.Tasks;
using VitaTrack.Core.Features.ServiceConnections;

namespace VitaTrack.Core.Features.LlmEnrichment;

public interface ISupplementLabelParser
{
    /// <summary>Carries the connection and its settings through to
    /// <see cref="ILlmClient"/> rather than resolving them here: the parser builds
    /// prompts and parses JSON, and which service to call is
    /// <see cref="LlmService"/>'s decision to make once, not once per prompt.</summary>
    Task<LlmResult> ExtractNutrientsAsync(string supplementName, string brand, string cleanedHtml,
        ServiceConnection connection, LlmRequestSettings settings);
}
