using System.Threading.Tasks;

namespace VitaTrack.Core.Services;

public record LlmCompletion(string? Content, string? Error);

public interface ILlmClient
{
    Task<LlmCompletion> PostChatAsync(string systemPrompt, string userPrompt);
}