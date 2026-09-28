using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VitaTrack.Core.Features.ServiceConnections;
using VitaTrack.Core.Features.Supplements;
using VitaTrack.Core.Primitives;

namespace VitaTrack.Core.Features.LlmEnrichment;

public class LlmService(
    IServiceConnectionRepository connections,
    IHtmlScraperService scraper,
    ISupplementLabelParser parser,
    ILogger<LlmService> logger) : ILlmService
{
    /// <summary>Single owner of the "there is no connection" message, which is a
    /// different fault from <see cref="LlmRequestSettings.ModelRequired"/> and must not
    /// read like it: it is worded here rather than on the shared record because
    /// <see cref="LlmClient"/> can never see this state — it is handed a connection —
    /// so no second surface could report it and there is nothing to share. "Choose a
    /// model" is also the wrong advice for it, because the model picker is gated
    /// behind having a connection at all.</summary>
    private const string ConnectionRequired =
        "No AI service connection is set up. Add one in Settings, then try again.";

    private readonly IServiceConnectionRepository _connections = connections;
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

        // Resolved here so the controller stays a caller of one method, and so the
        // three checks happen before the page is fetched: a supplement with no usable
        // connection should not cost a network round trip to the manufacturer. Three
        // gates rather than one, because they are three faults the user can act on
        // differently — see the three messages.
        var connection = await _connections.GetActiveAsync();
        if (connection is null)
            return Refused(result, supplement, ConnectionRequired);

        // A row naming a service the registry does not know: nothing can describe the
        // headers that service needs, and LlmClient refuses to post a credential to
        // it, so the fetch below would be spent finding that out here instead.
        if (ServiceDescriptorRegistry.Find(connection.Service) is null)
            return Refused(result, supplement, LlmRequestSettings.ServiceNotFound);

        if (string.IsNullOrWhiteSpace(connection.Model))
            return Refused(result, supplement, LlmRequestSettings.ModelRequired);

        var settings = new LlmRequestSettings(
            connection.Model,
            connection.Variant,
            connection.MaxTokens,
            connection.Temperature);

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

            var parsed = await _parser.ExtractNutrientsAsync(
                supplement.Name, supplement.Brand, cleanedHtml, connection, settings);
            result.Nutrients = parsed.Nutrients;
            result.ExtractionError = parsed.ExtractionError;
            result.SwapSuggestion = parsed.SwapSuggestion;

            if (parsed.Nutrients.Count > 0)
            {
                var nutritionDict = new Dictionary<string, decimal>();
                foreach (var nutrient in parsed.Nutrients)
                {
                    // Best-effort: a malformed legacy dosage contributes 0 rather than throwing.
                    nutritionDict[nutrient.GenericName] =
                        Dosage.TryParse(nutrient.Dosage, out var dose) ? dose.Amount : 0m;
                    if (nutrient.Children is { Count: > 0 })
                    {
                        foreach (var child in nutrient.Children)
                        {
                            nutritionDict[$"{nutrient.GenericName} > {child.GenericName}"] =
                                Dosage.TryParse(child.Dosage, out var childDose) ? childDose.Amount : 0m;
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

    /// <summary>Answers one failed gate and hands the result back, so each check in
    /// <see cref="EnrichSupplementAsync"/> reads as a condition rather than a
    /// repetition of the same four lines. <paramref name="message"/> is the const for
    /// that specific fault — a user who has no connection and a user whose model was
    /// never chosen can act on different things, and one message for both would send
    /// the first to a picker they cannot reach yet. The log line names the reason
    /// because the reason is one of these constants, never anything the user typed:
    /// this call is about to be made with a credential.</summary>
    private LlmResult Refused(LlmResult result, Supplement supplement, string message)
    {
        _logger.LogWarning(
            "Skipping LLM enrichment for {SupplementName}: {Reason}", supplement.Name, message);
        result.ExtractionError = message;
        return result;
    }
}