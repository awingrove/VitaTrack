using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VitaTrack.Infrastructure;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Infrastructure.Services;

public class LlmService(
    IOptions<VitaTrackOptions> options,
    IHtmlScraperService scraper,
    ISupplementLabelParser parser,
    ILogger<LlmService> logger) : ILlmService
{
    private readonly VitaTrackOptions _options = options.Value;
    private readonly IHtmlScraperService _scraper = scraper;
    private readonly ISupplementLabelParser _parser = parser;
    private readonly ILogger<LlmService> _logger = logger;

    public async Task<LlmResult> EnrichSupplementAsync(Supplement supplement)
    {
        var result = new LlmResult();

        if (string.IsNullOrWhiteSpace(supplement.ManufacturerUrl))
        {
            _logger.LogInformation("No manufacturer URL provided for supplement {SupplementName}, skipping LLM enrichment", supplement.Name);
            return result;
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            _logger.LogWarning("LLM API key or base URL not configured, skipping LLM enrichment for {SupplementName}", supplement.Name);
            result.ExtractionError = "LLM API key not configured";
            return result;
        }

        try
        {
            var cleanedHtml = await _scraper.FetchCleanHtmlAsync(supplement.ManufacturerUrl);

            if (cleanedHtml == null)
            {
                result.ExtractionError = "Failed to fetch manufacturer page";
                _logger.LogWarning("Failed to fetch manufacturer page for {Url}", supplement.ManufacturerUrl);
                return result;
            }

            if (string.IsNullOrWhiteSpace(cleanedHtml))
            {
                result.ExtractionError = "No content found on manufacturer page";
                return result;
            }

            var parsed = await _parser.ExtractNutrientsAsync(supplement.Name, supplement.Brand, cleanedHtml);
            result.Nutrients = parsed.Nutrients;
            result.ExtractionError = parsed.ExtractionError;
            result.SwapSuggestion = parsed.SwapSuggestion;

            if (parsed.Nutrients.Count > 0)
            {
                var nutritionDict = new Dictionary<string, decimal>();
                foreach (var nutrient in parsed.Nutrients)
                {
                    nutritionDict[nutrient.GenericName] = DosageParser.ParseAmount(nutrient.Dosage);
                    if (nutrient.Children is { Count: > 0 })
                    {
                        foreach (var child in nutrient.Children)
                        {
                            nutritionDict[$"{nutrient.GenericName} > {child.GenericName}"] =
                                DosageParser.ParseAmount(child.Dosage);
                        }
                    }
                }

                result.NutritionJson = JsonSerializer.Serialize(
                    new { nutrition = nutritionDict },
                    new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enriching supplement {SupplementName}", supplement.Name);
            result.ExtractionError = "An error occurred while processing the supplement page.";
        }

        return result;
    }
}