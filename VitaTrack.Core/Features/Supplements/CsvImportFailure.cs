namespace VitaTrack.Core.Features.Supplements;

public record CsvImportFailure(int RowNumber, string Name, string ErrorMessage);
