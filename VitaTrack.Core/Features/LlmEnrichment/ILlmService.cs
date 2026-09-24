using VitaTrack.Core.Features.Supplements;
using System.Threading.Tasks;

namespace VitaTrack.Core.Features.LlmEnrichment;

public interface ILlmService
{
    Task<LlmResult> EnrichSupplementAsync(Supplement supplement);
}