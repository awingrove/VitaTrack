namespace VitaTrack.Core.Features.Supplements;

public record CsvImportReport(
    int TotalRows,
    List<CsvImportSuccess> Successes,
    List<CsvImportFailure> Failures
);
