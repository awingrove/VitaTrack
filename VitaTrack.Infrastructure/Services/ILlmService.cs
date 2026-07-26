using VitaTrack.Infrastructure.Models;
using System.Threading.Tasks;

namespace VitaTrack.Infrastructure.Services
{
    public interface ILlmService
    {
        Task<LlmResult> EnrichSupplementAsync(Supplement supplement);
    }
}