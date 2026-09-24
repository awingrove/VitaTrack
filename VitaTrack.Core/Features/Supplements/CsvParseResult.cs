namespace VitaTrack.Core.Features.Supplements;

public record CsvParseResult(
    List<CsvSupplementRow> Rows,
    List<CsvParseError> Errors
);
