using System.Threading.Tasks;

namespace VitaTrack.Core.Services;

public interface IHtmlScraperService
{
    Task<string?> FetchCleanHtmlAsync(string url);
}