namespace VitaTrack.Infrastructure.Models;

public record CsvImportReport(
    int TotalRows,
    List<CsvImportSuccess> Successes,
    List<CsvImportFailure> Failures
);

public record CsvImportSuccess(string Name, string Brand, int NutrientCount);

public record CsvImportFailure(int RowNumber, string Name, string ErrorMessage);

public record CsvParseResult(
    List<CsvSupplementRow> Rows,
    List<CsvParseError> Errors
);

public record CsvParseError(int RowNumber, string Message);
