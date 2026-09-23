namespace VitaTrack.Core.Features.Supplements;

/// <summary>
/// Mutable per-row state threaded through the CSV validation chain. Holds the
/// line number, the six raw trimmed fields, and the values the parsing rules
/// fill in. The chain carries errors via rule return values, not this context.
/// </summary>
internal sealed class CsvRowContext(
    int lineNumber,
    string name,
    string brand,
    string dailyDose,
    string manufacturerUrl,
    string costText,
    string servingsText)
{
    public int LineNumber { get; } = lineNumber;

    public string Name { get; set; } = name;
    public string Brand { get; set; } = brand;
    public string DailyDose { get; set; } = dailyDose;
    public string ManufacturerUrl { get; set; } = manufacturerUrl;
    public string CostText { get; set; } = costText;
    public string ServingsText { get; set; } = servingsText;

    public decimal? Cost { get; set; }
    public decimal? ServingsPerBottle { get; set; }
}
