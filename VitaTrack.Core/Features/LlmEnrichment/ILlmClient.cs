using System.Threading.Tasks;
using VitaTrack.Core.Features.ServiceConnections;

namespace VitaTrack.Core.Features.LlmEnrichment;

public record LlmCompletion(string? Content, string? Error);

public interface ILlmClient
{
    /// <summary>Posts one chat completion to the connection's own service. The
    /// connection travels per call rather than being read from configuration: the
    /// pooled named client carries whichever connection used it last, so nothing
    /// about the destination or the credential can live on it.</summary>
    Task<LlmCompletion> PostChatAsync(string systemPrompt, string userPrompt,
        ServiceConnection connection, LlmRequestSettings settings);
}
