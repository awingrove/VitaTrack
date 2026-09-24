namespace VitaTrack.Core.Features.Supplements;

public interface ICsvImportService
{
    Task<CsvParseResult> ParseAsync(Stream csvStream);
}
