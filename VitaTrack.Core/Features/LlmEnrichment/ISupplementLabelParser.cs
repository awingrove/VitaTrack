using System.Threading.Tasks;

namespace VitaTrack.Core.Features.LlmEnrichment;

public interface ISupplementLabelParser
{
    Task<LlmResult> ExtractNutrientsAsync(string supplementName, string brand, string cleanedHtml);
}