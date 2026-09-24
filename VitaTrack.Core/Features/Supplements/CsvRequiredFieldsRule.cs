namespace VitaTrack.Core.Features.Supplements;

internal sealed class CsvRequiredFieldsRule : ICsvRowRule
{
    public CsvParseError? Apply(CsvRowContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Name))
            return new CsvParseError(context.LineNumber, "Missing required field: Name");
        if (string.IsNullOrWhiteSpace(context.Brand))
            return new CsvParseError(context.LineNumber, "Missing required field: Brand");
        if (string.IsNullOrWhiteSpace(context.DailyDose))
            return new CsvParseError(context.LineNumber, "Missing required field: DailyDose");
        return null;
    }
}
