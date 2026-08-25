namespace VitaTrack.Infrastructure.Models;

public record CsvSupplementRow(
    int RowNumber,
    string Name,
    string Brand,
    string DailyDose,
    string? ManufacturerUrl,
    decimal? Cost,
    decimal? ServingsPerBottle
);
