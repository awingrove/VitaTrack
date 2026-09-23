namespace VitaTrack.Core.Features.Supplements;

internal sealed class CsvLengthLimitsRule : ICsvRowRule
{
    internal const int MaxNameLength = 200;
    internal const int MaxBrandLength = 200;
    internal const int MaxDailyDoseLength = 200;
    internal const int MaxManufacturerUrlLength = 500;

    public CsvParseError? Apply(CsvRowContext context)
    {
        if (context.Name.Length > MaxNameLength)
            return new CsvParseError(context.LineNumber, $"Name exceeds {MaxNameLength} characters");
        if (context.Brand.Length > MaxBrandLength)
            return new CsvParseError(context.LineNumber, $"Brand exceeds {MaxBrandLength} characters");
        if (context.DailyDose.Length > MaxDailyDoseLength)
            return new CsvParseError(context.LineNumber, $"DailyDose exceeds {MaxDailyDoseLength} characters");
        if (!string.IsNullOrWhiteSpace(context.ManufacturerUrl) && context.ManufacturerUrl.Length > MaxManufacturerUrlLength)
            return new CsvParseError(context.LineNumber, $"ManufacturerUrl exceeds {MaxManufacturerUrlLength} characters");
        return null;
    }
}
