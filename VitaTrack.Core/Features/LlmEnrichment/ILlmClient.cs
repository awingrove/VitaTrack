using System.Threading.Tasks;

namespace VitaTrack.Core.Features.LlmEnrichment;

public record LlmCompletion(string? Content, string? Error);

public interface ILlmClient
{
    Task<LlmCompletion> PostChatAsync(string systemPrompt, string userPrompt);
}