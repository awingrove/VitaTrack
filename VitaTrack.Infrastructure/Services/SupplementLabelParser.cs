using System.Text.Json;
using Microsoft.Extensions.Logging;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Infrastructure.Services;

public class SupplementLabelParser(
    ILlmClient llmClient,
    ILogger<SupplementLabelParser> logger) : ISupplementLabelParser
{
    private const string SystemPrompt = "You are a supplement label parser. Extract structured nutrient information from supplement product pages. Return ONLY valid JSON. Do NOT include markdown formatting, code blocks, or any text outside the JSON.";

    private readonly ILlmClient _llmClient = llmClient;
    private readonly ILogger<SupplementLabelParser> _logger = logger;

    public async Task<LlmResult> ExtractNutrientsAsync(string supplementName, string brand, string cleanedHtml)
    {
        try
        {
            var userPrompt = BuildUserPrompt(supplementName, brand, cleanedHtml);

            var completion = await _llmClient.PostChatAsync(SystemPrompt, userPrompt);
            if (completion.Error != null || completion.Content == null)
            {
                return new LlmResult { ExtractionError = completion.Error ?? "Empty response from LLM" };
            }

            var cleanedContent = StripCodeBlocks(completion.Content);
            var parsed = JsonSerializer.Deserialize<JsonElement>(cleanedContent);

            var nutrients = ParseNutrients(parsed);
            var swapSuggestion = ParseSwapSuggestion(parsed);

            return new LlmResult
            {
                Nutrients = nutrients,
                SwapSuggestion = swapSuggestion
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing LLM response for {SupplementName}", supplementName);
            return new LlmResult { ExtractionError = "An error occurred while extracting nutrients from the page." };
        }
    }

    private static string BuildUserPrompt(string supplementName, string brand, string cleanedHtml)
    {
        var prompt = BuildExtractionPrompt(supplementName, brand);
        return $@"{prompt}

Product Page Content:
{cleanedHtml}

Respond with ONLY this JSON structure (no markdown, no code fences):
{{
  ""nutrients"": [
    {{
      ""genericName"": ""..."",
      ""specificForm"": ""..."",
      ""dosage"": ""..."",
      ""unit"": ""..."",
      ""amountPerServing"": 0,
      ""children"": [
        {{ ""genericName"": ""..."", ""specificForm"": ""..."", ""dosage"": ""..."" }}
      ]
    }}
  ],
  ""swapSuggestion"": ""...""
}}";
    }

    private static string BuildExtractionPrompt(string supplementName, string brand)
    {
        return $@"Extract all nutrients from this supplement product page for '{supplementName}' by {brand}.

For each nutrient found on the label, extract:
1. genericName - the common nutrient name (e.g., 'Vitamin C', 'Zinc', 'Magnesium')
2. specificForm - the specific chemical form (e.g., 'Ascorbic Acid', 'Zinc Picolinate', 'Magnesium Glycinate')
3. dosage - the amount per serving as shown on label (e.g., '500mg', '15mg', '1000 IU')
4. unit (optional) - the unit if separable (e.g., 'mg', 'mcg', 'IU')
5. amountPerServing (optional) - numeric value only (e.g., 500, 15, 1000)

A nutrient MAY be a blend - give it a genericName (e.g. 'Proprietary Herbal Blend'), a total dosage (e.g. '500mg'), and a children array of sub-nutrients (each with genericName, specificForm, and optional dosage).

Also provide a swapSuggestion if you can recommend a better form or alternative product.

Return nutrients as an array. Only include nutrients explicitly listed on the label.";
    }

    private static string StripCodeBlocks(string content)
    {
        var cleaned = content.Trim();
        if (cleaned.StartsWith("```json"))
        {
            cleaned = cleaned[7..];
            var endIdx = cleaned.LastIndexOf("```");
            if (endIdx >= 0) cleaned = cleaned[..endIdx];
        }
        else if (cleaned.StartsWith("```"))
        {
            cleaned = cleaned[3..];
            var endIdx = cleaned.LastIndexOf("```");
            if (endIdx >= 0) cleaned = cleaned[..endIdx];
        }
        return cleaned.Trim();
    }

    private static List<SupplementNutrientDto> ParseNutrients(JsonElement parsed)
    {
        var nutrients = new List<SupplementNutrientDto>();
        if (!parsed.TryGetProperty("nutrients", out var nutrientsElement)) return nutrients;

        foreach (var nutrientElement in nutrientsElement.EnumerateArray())
        {
            var dto = ParseNutrient(nutrientElement);
            nutrients.Add(dto);
        }

        return nutrients;
    }

    private static SupplementNutrientDto ParseNutrient(JsonElement nutrientElement)
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

        if (nutrientElement.TryGetProperty("children", out var childrenElement) && childrenElement.ValueKind == JsonValueKind.Array)
        {
            var children = new List<SupplementNutrientDto>();
            foreach (var childElement in childrenElement.EnumerateArray())
            {
                children.Add(ParseNutrient(childElement));
            }

            dto.Children = children;
        }

        return dto;
    }

    private static string? ParseSwapSuggestion(JsonElement parsed)
    {
        if (parsed.TryGetProperty("swapSuggestion", out var swapElement) && swapElement.ValueKind != JsonValueKind.Null)
        {
            return swapElement.GetString();
        }
        return null;
    }
}