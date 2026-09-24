using System.Threading.Tasks;

namespace VitaTrack.Core.Features.LlmEnrichment;

public interface IHtmlScraperService
{
    Task<string?> FetchCleanHtmlAsync(string url);
}