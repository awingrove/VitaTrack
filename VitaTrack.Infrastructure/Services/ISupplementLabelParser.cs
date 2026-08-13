using System.Threading.Tasks;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Infrastructure.Services;

public interface ISupplementLabelParser
{
    Task<LlmResult> ExtractNutrientsAsync(string supplementName, string brand, string cleanedHtml);
}