using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using VitaTrack.Infrastructure.Models;
using VitaTrack.Infrastructure.Services;

namespace VitaTrack.Infrastructure.Services;

public class OpenRouterLlmService(
    IHttpClientFactory httpClientFactory,
    Microsoft.Extensions.Configuration.IConfiguration cfg,
    ILogger<OpenRouterLlmService> logger) : ILlmService
{
    private readonly HttpClient _http = httpClientFactory.CreateClient("openrouter");
    private readonly HttpClient _scraperHttp = httpClientFactory.CreateClient("scraper");
    private readonly Microsoft.Extensions.Configuration.IConfiguration _cfg = cfg;
    private readonly ILogger<OpenRouterLlmService> _logger = logger;

    public async Task<LlmResult> EnrichSupplementAsync(Supplement supplement)
    {
        var result = new LlmResult();

        if (string.IsNullOrWhiteSpace(supplement.ManufacturerUrl))
        {
            _logger.LogInformation("No manufacturer URL provided for supplement {SupplementName}, skipping LLM enrichment", supplement.Name);
            return result;
        }

        // Check API key early to avoid unnecessary URL fetch
        var apiKey = _cfg["OpenRouter:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("OpenRouter API key not configured, skipping LLM enrichment for {SupplementName}", supplement.Name);
            result.ExtractionError = "OpenRouter API key not configured";
            return result;
        }

        try
        {
            var html = await FetchPageHtmlAsync(supplement.ManufacturerUrl);
            if (string.IsNullOrWhiteSpace(html))
            {
                result.ExtractionError = "Failed to fetch manufacturer page";
                _logger.LogWarning("Failed to fetch manufacturer page for {Url}", supplement.ManufacturerUrl);
                return result;
            }

            var cleanedHtml = CleanHtml(html);
            if (string.IsNullOrWhiteSpace(cleanedHtml))
            {
                result.ExtractionError = "No content found on manufacturer page";
                return result;
            }

            var llmResult = await ExtractNutrientsWithLlmAsync(supplement.Name, supplement.Brand, cleanedHtml);
            result.Nutrients = llmResult.Nutrients;
            result.ExtractionError = llmResult.ExtractionError;
            result.SwapSuggestion = llmResult.SwapSuggestion;

            // Also populate legacy NutritionJson for backward compatibility
            if (llmResult.Nutrients.Count > 0)
            {
                var nutritionDict = llmResult.Nutrients.ToDictionary(
                    n => n.GenericName,
                    n => DosageParser.ParseAmount(n.Dosage));
                result.NutritionJson = JsonSerializer.Serialize(new { nutrition = nutritionDict });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enriching supplement {SupplementName}", supplement.Name);
            result.ExtractionError = "An error occurred while processing the supplement page.";
        }

        return result;
    }

    private async Task<string?> FetchPageHtmlAsync(string url)
    {
        if (!UrlSafetyValidator.IsUrlSafe(url))
        {
            _logger.LogWarning("Blocked unsafe URL: {Url}", url);
            return null;
        }

        try
        {
            var response = await _scraperHttp.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch {Url}: {StatusCode}", url, response.StatusCode);
                return null;
            }
            return await response.Content.ReadAsStringAsync();
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
            // Remove scripts, styles, and other non-content elements
            foreach (var element in document.QuerySelectorAll("script, style, nav, header, footer, aside, noscript, iframe"))
            {
                element.Remove();
            }

            // Try to find main content areas
            var mainContent = document.QuerySelector("main") 
                             ?? document.QuerySelector("[role='main']") 
                             ?? document.QuerySelector(".product-detail")
                             ?? document.QuerySelector(".product-info")
                             ?? document.QuerySelector("#product-details")
                             ?? document.Body;

            // Get text content, limiting length to avoid token limits
            var text = mainContent?.TextContent?.Trim() ?? string.Empty;
            
            // Truncate if too long (keep first ~8000 chars for token limits)
            if (text.Length > 8000)
            {
                text = text[..8000];
            }

            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning HTML");
            return string.Empty;
        }
    }

    private async Task<LlmResult> ExtractNutrientsWithLlmAsync(string supplementName, string brand, string cleanedHtml)
    {
        _ = new LlmResult();

        try
        {
            var model = _cfg["OpenRouter:Model"] ?? "openai/gpt-4o-mini";
            var prompt = BuildExtractionPrompt(supplementName, brand);

            var requestBody = new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = "You are a supplement label parser. Extract structured nutrient information from supplement product pages. Return ONLY valid JSON. Do NOT include markdown formatting, code blocks, or any text outside the JSON." },
                    new { role = "user", content = $@"{prompt}

Product Page Content:
{cleanedHtml}

Respond with ONLY this JSON structure (no markdown, no code fences):
{{
  ""nutrients"": [
    {{ ""genericName"": ""..."", ""specificForm"": ""..."", ""dosage"": ""..."", ""unit"": ""..."", ""amountPerServing"": 0 }}
  ],
  ""swapSuggestion"": ""...""
}}" }
                },
                max_tokens = 2000,
                temperature = 0.1
            };

            var response = await _http.PostAsJsonAsync("v1/chat/completions", requestBody);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("OpenRouter API error: {StatusCode} - {Content}", response.StatusCode, errorContent);
                return new LlmResult { ExtractionError = "The AI service returned an error. Please try again or enter nutrients manually." };
            }

            var responseJson = await response.Content.ReadFromJsonAsync<JsonElement>();
            var choices = responseJson.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
            {
                return new LlmResult { ExtractionError = "No response from LLM" };
            }

            var content = choices[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content))
            {
                return new LlmResult { ExtractionError = "Empty response from LLM" };
            }

            // Parse the structured JSON response
            // Strip markdown code blocks if present
            var cleanedContent = content.Trim();
            if (cleanedContent.StartsWith("```json"))
            {
                cleanedContent = cleanedContent[7..];
                var endIdx = cleanedContent.LastIndexOf("```");
                if (endIdx >= 0) cleanedContent = cleanedContent[..endIdx];
            }
            else if (cleanedContent.StartsWith("```"))
            {
                cleanedContent = cleanedContent[3..];
                var endIdx = cleanedContent.LastIndexOf("```");
                if (endIdx >= 0) cleanedContent = cleanedContent[..endIdx];
            }
            cleanedContent = cleanedContent.Trim();

            var parsed = JsonSerializer.Deserialize<JsonElement>(cleanedContent);
            
            var nutrients = new List<SupplementNutrientDto>();
            if (parsed.TryGetProperty("nutrients", out var nutrientsElement))
            {
                foreach (var nutrientElement in nutrientsElement.EnumerateArray())
                {
                    var dto = new SupplementNutrientDto
                    {
                        GenericName = nutrientElement.GetProperty("genericName").GetString() ?? string.Empty,
                        SpecificForm = nutrientElement.GetProperty("specificForm").GetString() ?? string.Empty,
                        Dosage = nutrientElement.GetProperty("dosage").GetString() ?? string.Empty
                    };

                    if (nutrientElement.TryGetProperty("unit", out var unitElement) && unitElement.ValueKind != JsonValueKind.Null)
                    {
                        dto.Unit = unitElement.GetString();
                    }

                    if (nutrientElement.TryGetProperty("amountPerServing", out var amountElement) && amountElement.ValueKind != JsonValueKind.Null)
                    {
                        dto.AmountPerServing = amountElement.GetDecimal();
                    }

                    nutrients.Add(dto);
                }
            }

            string? swapSuggestion = null;
            if (parsed.TryGetProperty("swapSuggestion", out var swapElement) && swapElement.ValueKind != JsonValueKind.Null)
            {
                swapSuggestion = swapElement.GetString();
            }

            return new LlmResult
            {
                Nutrients = nutrients,
                SwapSuggestion = swapSuggestion
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting nutrients via LLM for {SupplementName}", supplementName);
            return new LlmResult { ExtractionError = "An error occurred while extracting nutrients from the page." };
        }
    }

private string BuildExtractionPrompt(string supplementName, string brand)
    {
        return $@"Extract all nutrients from this supplement product page for '{supplementName}' by {brand}.

For each nutrient found on the label, extract:
1. genericName - the common nutrient name (e.g., 'Vitamin C', 'Zinc', 'Magnesium')
2. specificForm - the specific chemical form (e.g., 'Ascorbic Acid', 'Zinc Picolinate', 'Magnesium Glycinate')
3. dosage - the amount per serving as shown on label (e.g., '500mg', '15mg', '1000 IU')
4. unit (optional) - the unit if separable (e.g., 'mg', 'mcg', 'IU')
5. amountPerServing (optional) - numeric value only (e.g., 500, 15, 1000)

Also provide a swapSuggestion if you can recommend a better form or alternative product.

Return nutrients as an array. Only include nutrients explicitly listed on the label.";
    }
}