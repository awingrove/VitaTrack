using VitaTrack.Core.Models;

namespace VitaTrack.Core.Services;

public interface ICsvImportService
{
    Task<CsvParseResult> ParseAsync(Stream csvStream);
}
