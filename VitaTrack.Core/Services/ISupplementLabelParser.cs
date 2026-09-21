using System.Threading.Tasks;
using VitaTrack.Core.Models;

namespace VitaTrack.Core.Services;

public interface ISupplementLabelParser
{
    Task<LlmResult> ExtractNutrientsAsync(string supplementName, string brand, string cleanedHtml);
}