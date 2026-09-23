using VitaTrack.Core.Features.Supplements;
using VitaTrack.Core.Models;
using System.Threading.Tasks;

namespace VitaTrack.Core.Services;

public interface ILlmService
{
    Task<LlmResult> EnrichSupplementAsync(Supplement supplement);
}