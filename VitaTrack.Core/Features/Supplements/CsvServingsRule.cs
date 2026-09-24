using System.Globalization;

namespace VitaTrack.Core.Features.Supplements;

internal sealed class CsvServingsRule : ICsvRowRule
{
    public CsvParseError? Apply(CsvRowContext context)
    {
        if (string.IsNullOrWhiteSpace(context.ServingsText))
            return null;

        if (decimal.TryParse(context.ServingsText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            if (parsed <= 0)
                return new CsvParseError(context.LineNumber, "ServingsPerBottle must be positive");
            context.ServingsPerBottle = parsed;
            return null;
        }

        return new CsvParseError(context.LineNumber, $"Invalid ServingsPerBottle value: '{context.ServingsText}'");
    }
}
