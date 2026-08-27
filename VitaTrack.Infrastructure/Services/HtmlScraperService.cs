using System.Net.Http;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;

namespace VitaTrack.Infrastructure.Services;

public class HtmlScraperService(
    IHttpClientFactory httpClientFactory,
    ILogger<HtmlScraperService> logger) : IHtmlScraperService
{
    private readonly HttpClient _http = httpClientFactory.CreateClient("scraper");
    private readonly ILogger<HtmlScraperService> _logger = logger;

    public async Task<string?> FetchCleanHtmlAsync(string url)
    {
        if (!UrlSafetyValidator.IsUrlSafe(url))
        {
            _logger.LogWarning("Blocked unsafe URL: {Url}", url);
            return null;
        }

        try
        {
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch {Url}: {StatusCode}", url, response.StatusCode);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync();
            return CleanHtml(html);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching {Url}", url);
            return null;
        }
    }

    private string CleanHtml(string html)
    {
        try
        {
            var parser = new HtmlParser();
            var document = parser.ParseDocument(html);
            foreach (var element in document.QuerySelectorAll("script, style, nav, header, footer, aside, noscript, iframe, svg, picture"))
            {
                element.Remove();
            }

            var mainContent = document.QuerySelector("main")
                             ?? document.QuerySelector("[role='main']")
                             ?? document.QuerySelector(".product-detail")
                             ?? document.QuerySelector(".product-info")
                             ?? document.QuerySelector("#product-details")
                             ?? document.QuerySelector("[data-section*='product']")
                             ?? document.QuerySelector("[data-section*='nutritional']")
                             ?? document.QuerySelector("article")
                             ?? document.Body;

            var text = mainContent?.TextContent?.Trim() ?? string.Empty;

            if (text.Length > 12000)
            {
                text = text[..12000];
            }

            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning HTML");
            return string.Empty;
        }
    }
}