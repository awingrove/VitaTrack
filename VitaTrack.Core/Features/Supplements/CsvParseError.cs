namespace VitaTrack.Core.Features.Supplements;

public record CsvParseError(int RowNumber, string Message);
