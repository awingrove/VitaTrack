using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Infrastructure.Services;

public interface ICsvImportService
{
    Task<CsvParseResult> ParseAsync(Stream csvStream);
}
