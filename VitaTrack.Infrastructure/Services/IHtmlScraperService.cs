using System.Threading.Tasks;

namespace VitaTrack.Infrastructure.Services;

public interface IHtmlScraperService
{
    Task<string?> FetchCleanHtmlAsync(string url);
}